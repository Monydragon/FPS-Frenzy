using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
    private const int DefaultAnimationSampleRate = 30;
    private const string DefaultRequiredAnimationClips =
        "CharacterArmature|Idle,CharacterArmature|Walk,CharacterArmature|Bite_Front," +
        "CharacterArmature|HitRecieve,CharacterArmature|Death";
    private static readonly char[] RequiredAnimationClipSeparators = [',', ';'];

    [DefaultValue(DefaultRequiredAnimationClips)]
    public string RequiredAnimationClips { get; set; } = DefaultRequiredAnimationClips;

    [DefaultValue(DefaultAnimationSampleRate)]
    public int AnimationSampleRate { get; set; } = DefaultAnimationSampleRate;

    [DefaultValue(1f)]
    public float SourceScale { get; set; } = 1f;

    [DefaultValue(true)]
    public bool RequireAlbedoTexture { get; set; } = true;

    [DefaultValue(true)]
    public bool StripRootTranslation { get; set; } = true;

    [DefaultValue("")]
    public string RootMotionBone { get; set; } = string.Empty;

    [DefaultValue("")]
    public string AlbedoTexture { get; set; } = string.Empty;

    [DefaultValue(true)]
    public bool NormalizeImportedBoneBasis { get; set; } = true;

    [DefaultValue(4f)]
    public float MaximumSkinExcursionMultiplier { get; set; } = 4f;

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

        if (AnimationSampleRate < 1 || AnimationSampleRate > 120)
        {
            throw new InvalidContentException("AnimationSampleRate must be between 1 and 120 Hz.");
        }

        if (!IsFinite(SourceScale) || SourceScale <= 0f)
        {
            throw new InvalidContentException("SourceScale must be a finite value greater than zero.");
        }

        if (!IsFinite(MaximumSkinExcursionMultiplier) || MaximumSkinExcursionMultiplier < 1f)
        {
            throw new InvalidContentException(
                "MaximumSkinExcursionMultiplier must be finite and at least one.");
        }

        // SourceScale is the authored scene-root conversion. Some Blender FBX files also contain an
        // internal 100 x 0.01 armature basis that is absent from their animation channels; that
        // separate, importer-specific mismatch is normalized once per bone below.
        if (Math.Abs(SourceScale - 1f) > 0.000001f)
        {
            input.Transform *= Matrix.CreateScale(SourceScale);
        }

        if (!string.IsNullOrWhiteSpace(AlbedoTexture))
        {
            BindAlbedoTexture(input, AlbedoTexture);
        }

        ValidateMesh(input, context, null, RequireAlbedoTexture);
        BoneContent skeleton = MeshHelper.FindSkeleton(input) ??
            throw new InvalidContentException("The skinned model does not contain a skeleton.");
        FlattenTransforms(input, skeleton);
        IList<BoneContent> bones = MeshHelper.FlattenSkeleton(skeleton);
        if (string.Equals(
                Environment.GetEnvironmentVariable("FPS_FRENZY_SKIN_DIAGNOSTICS"),
                "1",
                StringComparison.Ordinal))
        {
            LogSkinDiagnostics(context, skeleton.Animations, bones);
        }

        if (bones.Count > MaximumBones)
        {
            throw new InvalidContentException(
                $"Skeleton has {bones.Count} bones; the Reach/mobile limit is {MaximumBones}.");
        }

        int rootMotionBoneIndex = ResolveRootMotionBone(bones, RootMotionBone);
        string[] requiredClipNames = RequiredAnimationClips.Split(
                RequiredAnimationClipSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToArray();
        string basisReferenceClip = requiredClipNames.Length > 0
            ? requiredClipNames[0]
            : skeleton.Animations.Keys.OrderBy(name => name, StringComparer.Ordinal).First();
        Dictionary<string, AnimationClipContent> clips = ProcessAnimations(
            skeleton.Animations,
            bones,
            AnimationSampleRate,
            StripRootTranslation ? rootMotionBoneIndex : -1,
            NormalizeImportedBoneBasis,
            basisReferenceClip);
        foreach (string requiredClip in requiredClipNames)
        {
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
            ValidateTransform(bone.Transform, $"Bind pose for bone '{bone.Name}'");
            ValidateInvertible(bone.AbsoluteTransform, $"Absolute bind pose for bone '{bone.Name}'");
            bindPose.Add(bone.Transform);
            inverseBindPose.Add(Matrix.Invert(bone.AbsoluteTransform));
            hierarchy.Add(bone.Parent is BoneContent parent ? bones.IndexOf(parent) : -1);
        }

        AnimationClipContent calibrationClip = requiredClipNames.Length > 0
            ? clips[requiredClipNames[0]]
            : clips.OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
        GeometryBounds presentationBounds = MeasureSkinnedGeometryBounds(
            input,
            bones,
            inverseBindPose,
            hierarchy,
            calibrationClip,
            sampleIndex: 0);
        ValidateSkinExcursions(
            context,
            input,
            bones,
            inverseBindPose,
            hierarchy,
            clips,
            presentationBounds,
            MaximumSkinExcursionMultiplier);

        ModelContent model = base.Process(input, context);
        model.Tag = new SkinningDataContent(
            clips,
            bindPose,
            inverseBindPose,
            hierarchy,
            presentationBounds.Minimum,
            presentationBounds.Maximum);
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
        IList<BoneContent> bones,
        int sampleRate,
        int rootMotionBoneIndex,
        bool normalizeImportedBoneBasis,
        string basisReferenceClip)
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
        if (!animations.TryGetValue(basisReferenceClip, out AnimationContent? basisReferenceAnimation))
        {
            throw new InvalidContentException(
                $"Animation-basis reference clip '{basisReferenceClip}' was not found.");
        }

        AnimationUnitCorrection[] unitCorrections = new AnimationUnitCorrection[bones.Count];
        for (int boneIndex = 0; boneIndex < bones.Count; boneIndex++)
        {
            unitCorrections[boneIndex] = normalizeImportedBoneBasis
                ? DetermineAnimationUnitCorrection(
                    animations,
                    basisReferenceAnimation,
                    bones[boneIndex].Name,
                    bones[boneIndex].Transform)
                : AnimationUnitCorrection.Identity;
        }

        foreach (KeyValuePair<string, AnimationContent> pair in animations)
        {
            foreach (KeyValuePair<string, AnimationChannel> channel in pair.Value.Channels)
            {
                if (!boneMap.ContainsKey(channel.Key))
                {
                    throw new InvalidContentException(
                        $"Animation channel '{channel.Key}' does not belong to the model skeleton.");
                }
            }

            if (pair.Value.Channels.Count == 0 || pair.Value.Duration <= TimeSpan.Zero)
            {
                throw new InvalidContentException($"Animation '{pair.Key}' has no usable keyframes.");
            }

            List<BoneTrackContent> tracks = new(bones.Count);
            for (int boneIndex = 0; boneIndex < bones.Count; boneIndex++)
            {
                string boneName = bones[boneIndex].Name;
                AnimationChannel? channel = pair.Value.Channels.TryGetValue(boneName, out AnimationChannel found)
                    ? found
                    : null;
                tracks.Add(ResampleTrack(
                    boneIndex,
                    channel,
                    bones[boneIndex].Transform,
                    pair.Value.Duration,
                    sampleRate,
                    stripTranslation: boneIndex == rootMotionBoneIndex,
                    unitCorrections[boneIndex]));
            }

            clips.Add(pair.Key, new AnimationClipContent(pair.Value.Duration, sampleRate, tracks));
        }

        if (clips.Count == 0)
        {
            throw new InvalidContentException("The skinned model contains no animation clips.");
        }

        return clips;
    }

    private static BoneTrackContent ResampleTrack(
        int boneIndex,
        AnimationChannel? channel,
        Matrix bindTransform,
        TimeSpan duration,
        int sampleRate,
        bool stripTranslation,
        AnimationUnitCorrection unitCorrection)
    {
        List<SourceKeyframe> source = channel is null
            ? [new SourceKeyframe(TimeSpan.Zero, bindTransform)]
            : channel.Select(keyframe => new SourceKeyframe(keyframe.Time, keyframe.Transform))
                .OrderBy(keyframe => keyframe.Time)
                .ToList();
        if (source.Count == 0)
        {
            source.Add(new SourceKeyframe(TimeSpan.Zero, bindTransform));
        }

        foreach (SourceKeyframe keyframe in source)
        {
            ValidateTransform(keyframe.Transform, $"Animation transform for bone {boneIndex}");
        }

        int sampleCount = Math.Max(2, (int)Math.Ceiling(duration.TotalSeconds * sampleRate) + 1);
        List<TransformKeyframeContent> samples = new(sampleCount);
        Vector3 lockedRootTranslation = Decompose(bindTransform).Translation;
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            TimeSpan time = sampleIndex == sampleCount - 1
                ? duration
                : TimeSpan.FromTicks(Math.Min(
                    duration.Ticks,
                    (long)Math.Round(sampleIndex * (double)TimeSpan.TicksPerSecond / sampleRate)));
            TransformContent transform = Sample(source, time);
            if (channel is not null && unitCorrection.RequiresCorrection)
            {
                transform = Decompose(transform.ToMatrix() * unitCorrection.BasisTransform);
            }

            transform = new TransformContent(
                transform.Scale,
                transform.Rotation,
                ResolveRootTranslation(transform.Translation, lockedRootTranslation, stripTranslation));

            samples.Add(new TransformKeyframeContent(
                time, transform.Scale, transform.Rotation, transform.Translation));
        }

        return new BoneTrackContent(boneIndex, samples);
    }

    internal static Vector3 ResolveRootTranslation(
        Vector3 sampledTranslation,
        Vector3 bindTranslation,
        bool stripRootMotion) => stripRootMotion ? bindTranslation : sampledTranslation;

    private static AnimationUnitCorrection DetermineAnimationUnitCorrection(
        AnimationContentDictionary animations,
        AnimationContent basisReferenceAnimation,
        string boneName,
        Matrix bindTransform)
    {
        TransformContent bind = Decompose(bindTransform);
        List<Vector3> animatedScales = new();
        foreach (AnimationContent animation in animations.Values)
        {
            if (!animation.Channels.TryGetValue(boneName, out AnimationChannel? channel) || channel.Count == 0)
            {
                continue;
            }

            foreach (AnimationKeyframe keyframe in channel)
            {
                animatedScales.Add(Decompose(keyframe.Transform).Scale);
            }
        }

        if (animatedScales.Count == 0)
        {
            return AnimationUnitCorrection.Identity;
        }

        AnimationUnitCorrection? resolved = null;
        foreach (Vector3 animatedScale in animatedScales)
        {
            if (!TryDivide(bind.Scale, animatedScale, out Vector3 correction))
            {
                throw new InvalidContentException(
                    $"Animation scale for bone '{boneName}' is non-invertible, so its FBX animation basis cannot be validated.");
            }

            AnimationUnitCorrection candidate = CalculateAnimationUnitCorrection(correction);
            if (resolved is null)
            {
                resolved = candidate;
                continue;
            }

            if (candidate.RequiresCorrection != resolved.Value.RequiresCorrection ||
                (candidate.RequiresCorrection &&
                 Math.Abs(candidate.TranslationScale / resolved.Value.TranslationScale - 1f) > 0.02f))
            {
                throw new InvalidContentException(
                    $"Animation scales for bone '{boneName}' imply inconsistent FBX animation bases. " +
                    "Author an explicit scene-root conversion instead of relying on inferred per-keyframe repair.");
            }
        }

        AnimationUnitCorrection unitCorrection = resolved ?? AnimationUnitCorrection.Identity;
        if (!unitCorrection.RequiresCorrection)
        {
            return unitCorrection;
        }

        AnimationChannel? referenceChannel = basisReferenceAnimation.Channels.TryGetValue(
            boneName,
            out AnimationChannel basisChannel)
                ? basisChannel
                : animations.Values
                    .Select(animation => animation.Channels.TryGetValue(boneName, out AnimationChannel channel)
                        ? channel
                        : null)
                    .FirstOrDefault(channel => channel is { Count: > 0 });
        if (referenceChannel is null || referenceChannel.Count == 0)
        {
            return AnimationUnitCorrection.Identity;
        }

        Matrix referenceTransform = referenceChannel[0].Transform;
        ValidateInvertible(referenceTransform, $"Animation-basis reference for bone '{boneName}'");
        Matrix basisTransform = CalculateAnimationBasisTransform(referenceTransform, bindTransform);
        ValidateInvertible(basisTransform, $"Imported animation-basis correction for bone '{boneName}'");
        TransformContent basis = Decompose(basisTransform);
        if (!IsApproximatelyUniform(basis.Scale) ||
            Math.Abs(basis.Scale.X / unitCorrection.TranslationScale - 1f) > 0.02f)
        {
            throw new InvalidContentException(
                $"The full imported animation-basis correction for bone '{boneName}' does not match its " +
                "validated uniform unit conversion.");
        }

        return new AnimationUnitCorrection(basis.Scale, unitCorrection.TranslationScale, basisTransform);
    }

    // Retained for legacy FBX fixtures. Production mechs use their canonical glTF transforms and disable
    // this compatibility normalization instead of relying on inferred animation-basis repair.
    internal static Matrix CalculateAnimationBasisTransform(Matrix referenceTransform, Matrix bindTransform) =>
        Matrix.Invert(referenceTransform) * bindTransform;

    // This is a single imported-bone basis normalization, applied unchanged to every sample. It is
    // intentionally not the removed keyframe-by-keyframe scale-ratio heuristic, which erased authored
    // scale motion and could make a different guess at every frame.
    internal static AnimationUnitCorrection CalculateAnimationUnitCorrection(Vector3 correction)
    {
        if (!IsFinite(correction))
        {
            throw new InvalidContentException("The inferred FBX animation-basis correction is non-finite.");
        }

        bool hasExtremeMismatch =
            Math.Abs(correction.X) < 0.25f || Math.Abs(correction.X) > 4f ||
            Math.Abs(correction.Y) < 0.25f || Math.Abs(correction.Y) > 4f ||
            Math.Abs(correction.Z) < 0.25f || Math.Abs(correction.Z) > 4f;
        if (!hasExtremeMismatch)
        {
            return AnimationUnitCorrection.Identity;
        }

        if (correction.X <= 0f || correction.Y <= 0f || correction.Z <= 0f ||
            !IsApproximatelyUniform(correction))
        {
            throw new InvalidContentException(
                $"The inferred FBX animation-basis correction {correction} is nonuniform or reflected. " +
                "Only an unambiguous uniform unit conversion can be normalized automatically.");
        }

        float uniformCorrection = (correction.X + correction.Y + correction.Z) / 3f;
        return new AnimationUnitCorrection(
            correction,
            uniformCorrection,
            Matrix.CreateScale(uniformCorrection));
    }

    private static bool TryDivide(Vector3 numerator, Vector3 denominator, out Vector3 result)
    {
        if (Math.Abs(denominator.X) <= 0.000001f ||
            Math.Abs(denominator.Y) <= 0.000001f ||
            Math.Abs(denominator.Z) <= 0.000001f)
        {
            result = Vector3.One;
            return false;
        }

        result = new Vector3(
            numerator.X / denominator.X,
            numerator.Y / denominator.Y,
            numerator.Z / denominator.Z);
        return IsFinite(result);
    }

    private static bool IsApproximatelyUniform(Vector3 value)
    {
        float minimum = Math.Min(Math.Abs(value.X), Math.Min(Math.Abs(value.Y), Math.Abs(value.Z)));
        float maximum = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
        return minimum > 0.000001f && maximum / minimum <= 1.02f;
    }

    private static TransformContent Sample(List<SourceKeyframe> source, TimeSpan time)
    {
        if (source.Count == 1 || time <= source[0].Time)
        {
            return Decompose(source[0].Transform);
        }

        for (int index = 1; index < source.Count; index++)
        {
            if (time > source[index].Time)
            {
                continue;
            }

            SourceKeyframe leftKeyframe = source[index - 1];
            SourceKeyframe rightKeyframe = source[index];
            TransformContent left = Decompose(leftKeyframe.Transform);
            TransformContent right = Decompose(rightKeyframe.Transform);
            double intervalTicks = rightKeyframe.Time.Ticks - leftKeyframe.Time.Ticks;
            float amount = intervalTicks <= 0d
                ? 0f
                : (float)((time.Ticks - leftKeyframe.Time.Ticks) / intervalTicks);
            return Interpolate(left, right, MathHelper.Clamp(amount, 0f, 1f));
        }

        return Decompose(source[source.Count - 1].Transform);
    }

    private static TransformContent Interpolate(TransformContent left, TransformContent right, float amount) =>
        new(
            Vector3.Lerp(left.Scale, right.Scale, amount),
            Quaternion.Slerp(left.Rotation, right.Rotation, amount),
            Vector3.Lerp(left.Translation, right.Translation, amount));

    private static TransformContent Decompose(Matrix transform)
    {
        if (!transform.Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 translation))
        {
            throw new InvalidContentException("Animation transform could not be decomposed into scale, rotation, and translation.");
        }

        rotation.Normalize();
        return new TransformContent(scale, rotation, translation);
    }

    private static void ValidateTransform(Matrix transform, string label)
    {
        if (!IsFinite(transform) || !transform.Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 translation) ||
            !IsFinite(scale) || !IsFinite(rotation) || !IsFinite(translation))
        {
            throw new InvalidContentException($"{label} is non-finite or cannot be decomposed.");
        }
    }

    private static void ValidateInvertible(Matrix transform, string label)
    {
        ValidateTransform(transform, label);
        float determinant = transform.Determinant();
        if (!IsFinite(determinant) || Math.Abs(determinant) <= 0.000000000001f)
        {
            throw new InvalidContentException($"{label} is not invertible.");
        }
    }

    private static bool IsFinite(Matrix value) =>
        IsFinite(value.M11) && IsFinite(value.M12) && IsFinite(value.M13) && IsFinite(value.M14) &&
        IsFinite(value.M21) && IsFinite(value.M22) && IsFinite(value.M23) && IsFinite(value.M24) &&
        IsFinite(value.M31) && IsFinite(value.M32) && IsFinite(value.M33) && IsFinite(value.M34) &&
        IsFinite(value.M41) && IsFinite(value.M42) && IsFinite(value.M43) && IsFinite(value.M44);

    private static bool IsFinite(Vector3 value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static int ResolveRootMotionBone(IList<BoneContent> bones, string rootMotionBone)
    {
        if (string.IsNullOrWhiteSpace(rootMotionBone))
        {
            return 0;
        }

        for (int index = 0; index < bones.Count; index++)
        {
            if (string.Equals(bones[index].Name, rootMotionBone.Trim(), StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidContentException($"Root-motion bone '{rootMotionBone}' was not found in the skeleton.");
    }

    private static void BindAlbedoTexture(NodeContent input, string albedoTexture)
    {
        string sourceFilename = input.Identity?.SourceFilename ?? string.Empty;
        string sourceDirectory = Path.GetDirectoryName(sourceFilename) ?? string.Empty;
        string texturePath = Path.GetFullPath(Path.Combine(
            sourceDirectory,
            albedoTexture.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(texturePath))
        {
            throw new InvalidContentException(
                $"Authored albedo texture '{albedoTexture}' resolved to '{texturePath}', but the file does not exist.");
        }

        BindAlbedoTextureRecursive(input, texturePath);
    }

    private static void BindAlbedoTextureRecursive(NodeContent node, string texturePath)
    {
        if (node is MeshContent mesh)
        {
            foreach (GeometryContent geometry in mesh.Geometry)
            {
                if (!geometry.Vertices.Channels.Contains(VertexChannelNames.Weights()))
                {
                    continue;
                }

                geometry.Material.Textures["Texture"] = new ExternalReference<TextureContent>(texturePath);
            }
        }

        foreach (NodeContent child in node.Children)
        {
            BindAlbedoTextureRecursive(child, texturePath);
        }
    }

    private static void LogSkinDiagnostics(
        ContentProcessorContext context,
        AnimationContentDictionary animations,
        IList<BoneContent> bones)
    {
        foreach (BoneContent bone in bones)
        {
            TransformContent bind = Decompose(bone.Transform);
            TransformContent absolute = Decompose(bone.AbsoluteTransform);
            float minimumScale = float.MaxValue;
            float maximumScale = 0f;
            float maximumTranslation = 0f;
            int keyedClipCount = 0;
            foreach (AnimationContent animation in animations.Values)
            {
                if (!animation.Channels.TryGetValue(bone.Name, out AnimationChannel? channel))
                {
                    continue;
                }

                keyedClipCount++;
                foreach (var keyframe in channel)
                {
                    TransformContent keyed = Decompose(keyframe.Transform);
                    minimumScale = Math.Min(minimumScale, MinimumComponent(keyed.Scale));
                    maximumScale = Math.Max(maximumScale, MaximumComponent(keyed.Scale));
                    maximumTranslation = Math.Max(maximumTranslation, keyed.Translation.Length());
                }
            }

            context.Logger.LogWarning(
                null,
                bone.Identity,
                "SKIN_DIAG bone={0} bindScale={1} bindTranslation={2} absoluteScale={3} keyedClips={4} keyedScaleRange={5}..{6} keyedTranslationMax={7}",
                bone.Name,
                bind.Scale,
                bind.Translation,
                absolute.Scale,
                keyedClipCount,
                keyedClipCount == 0 ? 0f : minimumScale,
                maximumScale,
                maximumTranslation);
        }
    }

    private static float MinimumComponent(Vector3 value) => Math.Min(value.X, Math.Min(value.Y, value.Z));

    private static float MaximumComponent(Vector3 value) => Math.Max(value.X, Math.Max(value.Y, value.Z));

    private static void ValidateMesh(
        NodeContent node,
        ContentProcessorContext context,
        string? parentBoneName,
        bool requireAlbedoTexture)
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

            if (requireAlbedoTexture && mesh.Geometry.Any(geometry => geometry.Material.Textures.Count == 0))
            {
                throw new InvalidContentException(
                    $"Skinned mesh '{mesh.Name}' has a material without an albedo texture.");
            }
        }
        else if (node is BoneContent)
        {
            parentBoneName = node.Name;
        }

        foreach (NodeContent child in node.Children.ToArray())
        {
            ValidateMesh(child, context, parentBoneName, requireAlbedoTexture);
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

    private static GeometryBounds MeasureSkinnedGeometryBounds(
        NodeContent input,
        IList<BoneContent> bones,
        List<Matrix> inverseBindPose,
        List<int> hierarchy,
        AnimationClipContent clip,
        int sampleIndex)
    {
        Dictionary<string, int> boneMap = new(StringComparer.Ordinal);
        for (int index = 0; index < bones.Count; index++)
        {
            boneMap.Add(bones[index].Name, index);
        }

        Matrix[] worldTransforms = new Matrix[bones.Count];
        Matrix[] skinTransforms = new Matrix[bones.Count];
        foreach (BoneTrackContent track in clip.Tracks)
        {
            if (track.Keyframes.Count == 0)
            {
                throw new InvalidContentException(
                    $"Animation track for bone {track.Bone} contains no samples.");
            }

            TransformKeyframeContent sample = track.Keyframes[Math.Min(sampleIndex, track.Keyframes.Count - 1)];
            Matrix local = Matrix.CreateScale(sample.Scale) *
                Matrix.CreateFromQuaternion(sample.Rotation) *
                Matrix.CreateTranslation(sample.Translation);
            int parent = hierarchy[track.Bone];
            worldTransforms[track.Bone] = parent < 0 ? local : local * worldTransforms[parent];
            skinTransforms[track.Bone] = inverseBindPose[track.Bone] * worldTransforms[track.Bone];
        }

        Vector3 minimum = new(float.MaxValue);
        Vector3 maximum = new(float.MinValue);
        int measuredVertexCount = 0;
        AccumulateSkinnedGeometryBounds(
            input,
            boneMap,
            skinTransforms,
            ref minimum,
            ref maximum,
            ref measuredVertexCount);
        if (measuredVertexCount == 0 || !IsFinite(minimum) || !IsFinite(maximum))
        {
            throw new InvalidContentException(
                "The skinned model has no finite weighted vertices for visual calibration.");
        }

        Vector3 span = maximum - minimum;
        if (span.X <= 0.0001f || span.Y <= 0.0001f || span.Z <= 0.0001f)
        {
            throw new InvalidContentException(
                $"The skinned model's calibrated geometry bounds are degenerate: {minimum} to {maximum}.");
        }

        return new GeometryBounds(minimum, maximum);
    }

    private static void ValidateSkinExcursions(
        ContentProcessorContext context,
        NodeContent input,
        IList<BoneContent> bones,
        List<Matrix> inverseBindPose,
        List<int> hierarchy,
        IReadOnlyDictionary<string, AnimationClipContent> clips,
        GeometryBounds calibratedBounds,
        float maximumMultiplier)
    {
        foreach (KeyValuePair<string, AnimationClipContent> pair in clips)
        {
            int sampleCount = pair.Value.Tracks.Max(track => track.Keyframes.Count);
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                GeometryBounds poseBounds = MeasureSkinnedGeometryBounds(
                    input,
                    bones,
                    inverseBindPose,
                    hierarchy,
                    pair.Value,
                    sampleIndex);
                if (string.Equals(
                        Environment.GetEnvironmentVariable("FPS_FRENZY_POSE_BOUNDS_DIAGNOSTICS"),
                        "1",
                        StringComparison.Ordinal))
                {
                    Vector3 span = poseBounds.Maximum - poseBounds.Minimum;
                    Vector3 center = (poseBounds.Minimum + poseBounds.Maximum) * 0.5f;
                    context.Logger.LogWarning(
                        null,
                        input.Identity,
                        $"POSE-BOUNDS clip='{pair.Key}' sample={sampleIndex}/{sampleCount - 1} " +
                        $"span={span} center={center} min={poseBounds.Minimum} max={poseBounds.Maximum}");
                }

                ValidateSkinExcursionBounds(
                    calibratedBounds,
                    poseBounds,
                    maximumMultiplier,
                    $"Animation '{pair.Key}' sample {sampleIndex}");
            }
        }
    }

    internal static void ValidateSkinExcursionBounds(
        GeometryBounds calibratedBounds,
        GeometryBounds poseBounds,
        float maximumMultiplier,
        string label)
    {
        Vector3 calibratedSpan = calibratedBounds.Maximum - calibratedBounds.Minimum;
        Vector3 poseSpan = poseBounds.Maximum - poseBounds.Minimum;
        float calibratedMaximumSpan = MaximumComponent(calibratedSpan);
        float allowedSpan = calibratedMaximumSpan * maximumMultiplier;
        float padding = calibratedMaximumSpan * (maximumMultiplier - 1f);
        Vector3 allowedMinimum = calibratedBounds.Minimum - new Vector3(padding);
        Vector3 allowedMaximum = calibratedBounds.Maximum + new Vector3(padding);
        bool insideExpandedBounds =
            poseBounds.Minimum.X >= allowedMinimum.X && poseBounds.Minimum.Y >= allowedMinimum.Y &&
            poseBounds.Minimum.Z >= allowedMinimum.Z && poseBounds.Maximum.X <= allowedMaximum.X &&
            poseBounds.Maximum.Y <= allowedMaximum.Y && poseBounds.Maximum.Z <= allowedMaximum.Z;
        if (!IsFinite(calibratedMaximumSpan) || calibratedMaximumSpan <= 0.0001f ||
            !IsFinite(maximumMultiplier) || maximumMultiplier < 1f ||
            !IsFinite(poseBounds.Minimum) || !IsFinite(poseBounds.Maximum) ||
            MaximumComponent(poseSpan) > allowedSpan || !insideExpandedBounds)
        {
            throw new InvalidContentException(
                $"{label} drives skinned geometry outside the calibrated presentation bounds " +
                $"({poseBounds.Minimum} to {poseBounds.Maximum}; calibrated " +
                $"{calibratedBounds.Minimum} to {calibratedBounds.Maximum}, maximum {maximumMultiplier}x)." );
        }
    }

    private static void AccumulateSkinnedGeometryBounds(
        NodeContent node,
        IReadOnlyDictionary<string, int> boneMap,
        IReadOnlyList<Matrix> skinTransforms,
        ref Vector3 minimum,
        ref Vector3 maximum,
        ref int measuredVertexCount)
    {
        if (node is MeshContent mesh)
        {
            foreach (GeometryContent geometry in mesh.Geometry)
            {
                string weightsName = VertexChannelNames.Weights();
                if (!geometry.Vertices.Channels.Contains(weightsName))
                {
                    continue;
                }

                VertexChannel<BoneWeightCollection> weights =
                    geometry.Vertices.Channels.Get<BoneWeightCollection>(weightsName);
                for (int vertexIndex = 0; vertexIndex < geometry.Vertices.VertexCount; vertexIndex++)
                {
                    Vector3 sourcePosition = geometry.Vertices.Positions[vertexIndex];
                    Vector3 skinnedPosition = Vector3.Zero;
                    float totalWeight = 0f;
                    foreach (BoneWeight weight in weights[vertexIndex])
                    {
                        if (!boneMap.TryGetValue(weight.BoneName, out int boneIndex))
                        {
                            throw new InvalidContentException(
                                $"Vertex references unknown skinning bone '{weight.BoneName}'.");
                        }

                        skinnedPosition += Vector3.Transform(sourcePosition, skinTransforms[boneIndex]) * weight.Weight;
                        totalWeight += weight.Weight;
                    }

                    if (!IsFinite(totalWeight) || totalWeight <= 0.000001f)
                    {
                        throw new InvalidContentException("A skinned vertex has no positive finite bone weight.");
                    }

                    skinnedPosition /= totalWeight;
                    if (!IsFinite(skinnedPosition))
                    {
                        throw new InvalidContentException("A calibrated skinned vertex position is non-finite.");
                    }

                    minimum = Vector3.Min(minimum, skinnedPosition);
                    maximum = Vector3.Max(maximum, skinnedPosition);
                    measuredVertexCount++;
                }
            }
        }

        foreach (NodeContent child in node.Children)
        {
            AccumulateSkinnedGeometryBounds(
                child,
                boneMap,
                skinTransforms,
                ref minimum,
                ref maximum,
                ref measuredVertexCount);
        }
    }
}

internal sealed class SkinningDataContent(
    Dictionary<string, AnimationClipContent> animationClips,
    List<Matrix> bindPose,
    List<Matrix> inverseBindPose,
    List<int> skeletonHierarchy,
    Vector3 sourceBoundsMinimum,
    Vector3 sourceBoundsMaximum)
{
    public Dictionary<string, AnimationClipContent> AnimationClips { get; } = animationClips;
    public List<Matrix> BindPose { get; } = bindPose;
    public List<Matrix> InverseBindPose { get; } = inverseBindPose;
    public List<int> SkeletonHierarchy { get; } = skeletonHierarchy;
    public Vector3 SourceBoundsMinimum { get; } = sourceBoundsMinimum;
    public Vector3 SourceBoundsMaximum { get; } = sourceBoundsMaximum;
}

internal sealed class AnimationClipContent(
    TimeSpan duration,
    int sampleRate,
    List<BoneTrackContent> tracks)
{
    public TimeSpan Duration { get; } = duration;
    public int SampleRate { get; } = sampleRate;
    public List<BoneTrackContent> Tracks { get; } = tracks;
}

internal sealed class BoneTrackContent(int bone, List<TransformKeyframeContent> keyframes)
{
    public int Bone { get; } = bone;
    public List<TransformKeyframeContent> Keyframes { get; } = keyframes;
}

internal sealed class TransformKeyframeContent(
    TimeSpan time,
    Vector3 scale,
    Quaternion rotation,
    Vector3 translation)
{
    public TimeSpan Time { get; } = time;
    public Vector3 Scale { get; } = scale;
    public Quaternion Rotation { get; } = rotation;
    public Vector3 Translation { get; } = translation;
}

internal sealed class SourceKeyframe(TimeSpan time, Matrix transform)
{
    public TimeSpan Time { get; } = time;
    public Matrix Transform { get; } = transform;
}

internal sealed class TransformContent(Vector3 scale, Quaternion rotation, Vector3 translation)
{
    public Vector3 Scale { get; } = scale;
    public Quaternion Rotation { get; } = rotation;
    public Vector3 Translation { get; } = translation;

    public Matrix ToMatrix() =>
        Matrix.CreateScale(Scale) *
        Matrix.CreateFromQuaternion(Rotation) *
        Matrix.CreateTranslation(Translation);
}

internal readonly struct GeometryBounds(Vector3 minimum, Vector3 maximum)
{
    public Vector3 Minimum { get; } = minimum;
    public Vector3 Maximum { get; } = maximum;
}

internal readonly struct AnimationUnitCorrection(
    Vector3 scale,
    float translationScale,
    Matrix basisTransform)
{
    public static AnimationUnitCorrection Identity { get; } = new(Vector3.One, 1f, Matrix.Identity);

    public Vector3 Scale { get; } = scale;
    public float TranslationScale { get; } = translationScale;
    public Matrix BasisTransform { get; } = basisTransform;
    public bool RequiresCorrection => Math.Abs(TranslationScale - 1f) > 0.01f;
}
