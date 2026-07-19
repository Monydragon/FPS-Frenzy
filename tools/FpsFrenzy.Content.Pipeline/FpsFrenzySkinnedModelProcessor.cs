using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using Microsoft.Xna.Framework.Content.Pipeline.Processors;
using Microsoft.Xna.Framework.Graphics;

namespace FpsFrenzy.Content.Pipeline;

// Adapted from Microsoft's MIT-licensed XNA Skinned Model sample distributed by KNI.
[ContentProcessor(DisplayName = "FPS Frenzy Skinned Model Processor")]
public sealed class FpsFrenzySkinnedModelProcessor : ModelProcessor
{
    private const int MaximumBones = 72;
    private const string DefaultRequiredAnimationClips =
        "CharacterArmature|Idle,CharacterArmature|Walk,CharacterArmature|Bite_Front," +
        "CharacterArmature|HitRecieve,CharacterArmature|Death";
    private static readonly char[] RequiredAnimationClipSeparators = [',', ';'];

    [DefaultValue(DefaultRequiredAnimationClips)]
    public string RequiredAnimationClips { get; set; } = DefaultRequiredAnimationClips;

    public override ModelContent Process(NodeContent input, ContentProcessorContext context)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        ValidateMesh(input, context, null);
        BoneContent skeleton = MeshHelper.FindSkeleton(input) ??
            throw new InvalidContentException("The skinned model does not contain a skeleton.");
        FlattenTransforms(input, skeleton);
        IList<BoneContent> bones = MeshHelper.FlattenSkeleton(skeleton);
        if (bones.Count > MaximumBones)
        {
            throw new InvalidContentException(
                $"Skeleton has {bones.Count} bones; the Reach/mobile limit is {MaximumBones}.");
        }

        Dictionary<string, AnimationClipContent> clips = ProcessAnimations(skeleton.Animations, bones);
        foreach (string clipName in RequiredAnimationClips.Split(
            RequiredAnimationClipSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string requiredClip = clipName.Trim();
            if (!clips.ContainsKey(requiredClip))
            {
                throw new InvalidContentException($"Required animation clip '{requiredClip}' was not found.");
            }
        }

        List<Matrix> bindPose = [];
        List<Matrix> inverseBindPose = [];
        List<int> hierarchy = [];
        foreach (BoneContent bone in bones)
        {
            bindPose.Add(bone.Transform);
            inverseBindPose.Add(Matrix.Invert(bone.AbsoluteTransform));
            hierarchy.Add(bone.Parent is BoneContent parent ? bones.IndexOf(parent) : -1);
        }

        ModelContent model = base.Process(input, context);
        model.Tag = new SkinningDataContent(clips, bindPose, inverseBindPose, hierarchy);
        return model;
    }

    [DefaultValue(MaterialProcessorDefaultEffect.SkinnedEffect)]
    public override MaterialProcessorDefaultEffect DefaultEffect
    {
        get => MaterialProcessorDefaultEffect.SkinnedEffect;
        set { }
    }

    private static Dictionary<string, AnimationClipContent> ProcessAnimations(
        AnimationContentDictionary animations,
        IList<BoneContent> bones)
    {
        Dictionary<string, int> boneMap = new(StringComparer.Ordinal);
        for (int index = 0; index < bones.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(bones[index].Name))
            {
                boneMap.Add(bones[index].Name, index);
            }
        }

        Dictionary<string, AnimationClipContent> clips = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, AnimationContent> pair in animations)
        {
            List<KeyframeContent> keyframes = [];
            foreach (KeyValuePair<string, AnimationChannel> channel in pair.Value.Channels)
            {
                if (!boneMap.TryGetValue(channel.Key, out int boneIndex))
                {
                    throw new InvalidContentException(
                        $"Animation channel '{channel.Key}' does not belong to the model skeleton.");
                }

                keyframes.AddRange(channel.Value.Select(keyframe =>
                    new KeyframeContent(
                        boneIndex,
                        keyframe.Time,
                        RestoreBindPoseUnitConversion(keyframe.Transform, bones[boneIndex].Transform))));
            }

            keyframes.Sort((left, right) => left.Time.CompareTo(right.Time));
            if (keyframes.Count == 0 || pair.Value.Duration <= TimeSpan.Zero)
            {
                throw new InvalidContentException($"Animation '{pair.Key}' has no usable keyframes.");
            }

            clips.Add(pair.Key, new AnimationClipContent(pair.Value.Duration, keyframes));
        }

        if (clips.Count == 0)
        {
            throw new InvalidContentException("The skinned model contains no animation clips.");
        }

        return clips;
    }

    private static Matrix RestoreBindPoseUnitConversion(Matrix animatedTransform, Matrix bindTransform)
    {
        if (!animatedTransform.Decompose(out Vector3 animatedScale, out Quaternion animatedRotation, out Vector3 animatedTranslation) ||
            !bindTransform.Decompose(out Vector3 bindScale, out _, out _))
        {
            return animatedTransform;
        }

        // The Quaternius FBX stores armature unit conversion in the local bind scales. Its animation
        // channels omit reciprocal child scales and expand those child translations by the same ratio.
        // Reconstruct both parts of the local unit conversion to avoid displaced bones and exploding triangles.
        Vector3 correctedTranslation = new(
            animatedTranslation.X * ScaleRatio(bindScale.X, animatedScale.X),
            animatedTranslation.Y * ScaleRatio(bindScale.Y, animatedScale.Y),
            animatedTranslation.Z * ScaleRatio(bindScale.Z, animatedScale.Z));
        return Matrix.CreateScale(bindScale) *
            Matrix.CreateFromQuaternion(animatedRotation) *
            Matrix.CreateTranslation(correctedTranslation);
    }

    private static float ScaleRatio(float bindScale, float animatedScale) =>
        Math.Abs(animatedScale) <= 0.000001f ? 1f : bindScale / animatedScale;

    private static void ValidateMesh(NodeContent node, ContentProcessorContext context, string? parentBoneName)
    {
        if (node is MeshContent mesh)
        {
            if (parentBoneName is not null)
            {
                context.Logger.LogWarning(null, node.Identity,
                    "Mesh '{0}' is a child of bone '{1}'; transforms may not import as expected.",
                    mesh.Name, parentBoneName);
            }

            bool hasSkinning = mesh.Geometry.All(geometry =>
                geometry.Vertices.Channels.Contains(VertexChannelNames.Weights()));
            if (!hasSkinning)
            {
                context.Logger.LogWarning(null, node.Identity,
                    "Mesh '{0}' has no skinning weights and was removed.", mesh.Name);
                node.Parent?.Children.Remove(node);
                return;
            }
        }
        else if (node is BoneContent)
        {
            parentBoneName = node.Name;
        }

        foreach (NodeContent child in node.Children.ToArray())
        {
            ValidateMesh(child, context, parentBoneName);
        }
    }

    private static void FlattenTransforms(NodeContent node, BoneContent skeleton)
    {
        foreach (NodeContent child in node.Children)
        {
            if (ReferenceEquals(child, skeleton))
            {
                continue;
            }

            MeshHelper.TransformScene(child, child.Transform);
            child.Transform = Matrix.Identity;
            FlattenTransforms(child, skeleton);
        }
    }
}

internal sealed class SkinningDataContent(
    Dictionary<string, AnimationClipContent> animationClips,
    List<Matrix> bindPose,
    List<Matrix> inverseBindPose,
    List<int> skeletonHierarchy)
{
    public Dictionary<string, AnimationClipContent> AnimationClips { get; } = animationClips;
    public List<Matrix> BindPose { get; } = bindPose;
    public List<Matrix> InverseBindPose { get; } = inverseBindPose;
    public List<int> SkeletonHierarchy { get; } = skeletonHierarchy;
}

internal sealed class AnimationClipContent(TimeSpan duration, List<KeyframeContent> keyframes)
{
    public TimeSpan Duration { get; } = duration;
    public List<KeyframeContent> Keyframes { get; } = keyframes;
}

internal sealed class KeyframeContent(int bone, TimeSpan time, Matrix transform)
{
    public int Bone { get; } = bone;
    public TimeSpan Time { get; } = time;
    public Matrix Transform { get; } = transform;
}
