using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;

namespace FpsFrenzy.Kni.Animation;

public readonly record struct BoneTransform(Vector3 Scale, Quaternion Rotation, Vector3 Translation)
{
    public static BoneTransform FromMatrix(Matrix transform)
    {
        if (!transform.Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 translation))
        {
            throw new InvalidDataException("An animation transform could not be decomposed into scale, rotation, and translation.");
        }

        rotation.Normalize();
        return new BoneTransform(scale, rotation, translation);
    }

    public static BoneTransform Interpolate(BoneTransform from, BoneTransform to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        Quaternion rotation = Quaternion.Slerp(from.Rotation, to.Rotation, amount);
        rotation.Normalize();
        return new BoneTransform(
            Vector3.Lerp(from.Scale, to.Scale, amount),
            rotation,
            Vector3.Lerp(from.Translation, to.Translation, amount));
    }

    public Matrix ToMatrix() =>
        Matrix.CreateScale(Scale) *
        Matrix.CreateFromQuaternion(Rotation) *
        Matrix.CreateTranslation(Translation);
}

public sealed record TransformKeyframe(TimeSpan Time, BoneTransform Transform);

public sealed record BoneAnimationTrack(int Bone, IReadOnlyList<TransformKeyframe> Keyframes)
{
    public BoneTransform Sample(TimeSpan time)
    {
        if (Keyframes.Count == 0)
        {
            throw new InvalidDataException($"Animation track for bone {Bone} contains no keyframes.");
        }

        if (Keyframes.Count == 1 || time <= Keyframes[0].Time)
        {
            return Keyframes[0].Transform;
        }

        int low = 0;
        int high = Keyframes.Count - 1;
        while (low + 1 < high)
        {
            int middle = low + ((high - low) / 2);
            if (Keyframes[middle].Time <= time)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        TransformKeyframe left = Keyframes[low];
        TransformKeyframe right = Keyframes[high];
        if (time >= right.Time)
        {
            return right.Transform;
        }

        double intervalTicks = right.Time.Ticks - left.Time.Ticks;
        float amount = intervalTicks <= 0d
            ? 0f
            : (float)((time.Ticks - left.Time.Ticks) / intervalTicks);
        return BoneTransform.Interpolate(left.Transform, right.Transform, amount);
    }
}

// Retained so older code and already-built XNB assets can be upgraded at load time.
public sealed record Keyframe(int Bone, TimeSpan Time, Matrix Transform);

public sealed class AnimationClip
{
    public AnimationClip(
        TimeSpan duration,
        IReadOnlyList<BoneAnimationTrack> tracks,
        int sampleRate = 30)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Animation duration must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0)
        {
            throw new ArgumentException("An animation clip must contain at least one bone track.", nameof(tracks));
        }

        if (sampleRate < 0 || sampleRate > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        Duration = duration;
        Tracks = tracks;
        SampleRate = sampleRate;
    }

    public AnimationClip(TimeSpan duration, IReadOnlyList<Keyframe> keyframes)
        : this(duration, ConvertLegacyKeyframes(keyframes), sampleRate: 0)
    {
    }

    public TimeSpan Duration { get; }
    public IReadOnlyList<BoneAnimationTrack> Tracks { get; }
    public int SampleRate { get; }

    private static BoneAnimationTrack[] ConvertLegacyKeyframes(IReadOnlyList<Keyframe> keyframes)
    {
        ArgumentNullException.ThrowIfNull(keyframes);
        return keyframes
            .GroupBy(keyframe => keyframe.Bone)
            .OrderBy(group => group.Key)
            .Select(group => new BoneAnimationTrack(
                group.Key,
                group.OrderBy(keyframe => keyframe.Time)
                    .Select(keyframe => new TransformKeyframe(
                        keyframe.Time,
                        BoneTransform.FromMatrix(keyframe.Transform)))
                    .ToArray()))
            .ToArray();
    }
}

public sealed class SkinningData(
    IReadOnlyDictionary<string, AnimationClip> animationClips,
    IReadOnlyList<Matrix> bindPose,
    IReadOnlyList<Matrix> inverseBindPose,
    IReadOnlyList<int> skeletonHierarchy,
    Vector3? sourceBoundsMinimum = null,
    Vector3? sourceBoundsMaximum = null)
{
    public const int MaximumReachBones = 72;

    public IReadOnlyDictionary<string, AnimationClip> AnimationClips { get; } = animationClips;
    public IReadOnlyList<Matrix> BindPose { get; } = bindPose;
    public IReadOnlyList<Matrix> InverseBindPose { get; } = inverseBindPose;
    public IReadOnlyList<int> SkeletonHierarchy { get; } = skeletonHierarchy;
    public bool HasSourceGeometryBounds { get; } = sourceBoundsMinimum.HasValue && sourceBoundsMaximum.HasValue;
    public Vector3 SourceBoundsMinimum { get; } = sourceBoundsMinimum ?? Vector3.Zero;
    public Vector3 SourceBoundsMaximum { get; } = sourceBoundsMaximum ?? Vector3.Zero;
}

public sealed class SkinningDataReader : ContentTypeReader<SkinningData>
{
    private const int TrsTrackFormatMarker = -2;
    private const int TrsTrackWithCalibrationFormatMarker = -3;

    protected override SkinningData Read(ContentReader input, SkinningData? existingInstance)
    {
        int markerOrClipCount = input.ReadInt32();
        bool hasCalibrationBounds = markerOrClipCount == TrsTrackWithCalibrationFormatMarker;
        Dictionary<string, AnimationClip> clips = markerOrClipCount is TrsTrackFormatMarker or TrsTrackWithCalibrationFormatMarker
            ? ReadTrsClips(input, input.ReadInt32())
            : ReadLegacyClips(input, markerOrClipCount);

        Matrix[] bindPose = ReadMatrices(input);
        Matrix[] inverseBindPose = ReadMatrices(input);
        int[] hierarchy = ReadIntegers(input);
        if (bindPose.Length > SkinningData.MaximumReachBones)
        {
            throw new InvalidDataException(
                $"Skeleton has {bindPose.Length} bones; the Reach/mobile limit is {SkinningData.MaximumReachBones}.");
        }

        if (bindPose.Length != inverseBindPose.Length || bindPose.Length != hierarchy.Length)
        {
            throw new InvalidDataException("Skinning data contains inconsistent skeleton array lengths.");
        }

        Vector3? sourceBoundsMinimum = hasCalibrationBounds ? input.ReadVector3() : null;
        Vector3? sourceBoundsMaximum = hasCalibrationBounds ? input.ReadVector3() : null;
        if (hasCalibrationBounds &&
            (!IsFinite(sourceBoundsMinimum!.Value) || !IsFinite(sourceBoundsMaximum!.Value) ||
             sourceBoundsMaximum.Value.X <= sourceBoundsMinimum.Value.X ||
             sourceBoundsMaximum.Value.Y <= sourceBoundsMinimum.Value.Y ||
             sourceBoundsMaximum.Value.Z <= sourceBoundsMinimum.Value.Z))
        {
            throw new InvalidDataException("Skinning data contains invalid source-geometry calibration bounds.");
        }

        return new SkinningData(
            clips,
            bindPose,
            inverseBindPose,
            hierarchy,
            sourceBoundsMinimum,
            sourceBoundsMaximum);
    }

    private static Dictionary<string, AnimationClip> ReadTrsClips(ContentReader input, int clipCount)
    {
        Dictionary<string, AnimationClip> clips = new(clipCount, StringComparer.Ordinal);
        for (int clipIndex = 0; clipIndex < clipCount; clipIndex++)
        {
            string name = input.ReadString();
            TimeSpan duration = TimeSpan.FromTicks(input.ReadInt64());
            int sampleRate = input.ReadInt32();
            int trackCount = input.ReadInt32();
            BoneAnimationTrack[] tracks = new BoneAnimationTrack[trackCount];
            for (int trackIndex = 0; trackIndex < trackCount; trackIndex++)
            {
                int bone = input.ReadInt32();
                int keyframeCount = input.ReadInt32();
                TransformKeyframe[] keyframes = new TransformKeyframe[keyframeCount];
                for (int keyframeIndex = 0; keyframeIndex < keyframeCount; keyframeIndex++)
                {
                    keyframes[keyframeIndex] = new TransformKeyframe(
                        TimeSpan.FromTicks(input.ReadInt64()),
                        new BoneTransform(
                            input.ReadVector3(),
                            input.ReadQuaternion(),
                            input.ReadVector3()));
                }

                tracks[trackIndex] = new BoneAnimationTrack(bone, keyframes);
            }

            clips.Add(name, new AnimationClip(duration, tracks, sampleRate));
        }

        return clips;
    }

    private static Dictionary<string, AnimationClip> ReadLegacyClips(ContentReader input, int clipCount)
    {
        if (clipCount < 0)
        {
            throw new InvalidDataException($"Unsupported skinning-data format marker {clipCount}.");
        }

        Dictionary<string, AnimationClip> clips = new(clipCount, StringComparer.Ordinal);
        for (int clipIndex = 0; clipIndex < clipCount; clipIndex++)
        {
            string name = input.ReadString();
            TimeSpan duration = TimeSpan.FromTicks(input.ReadInt64());
            int keyframeCount = input.ReadInt32();
            Keyframe[] keyframes = new Keyframe[keyframeCount];
            for (int keyframeIndex = 0; keyframeIndex < keyframeCount; keyframeIndex++)
            {
                keyframes[keyframeIndex] = new Keyframe(
                    input.ReadInt32(),
                    TimeSpan.FromTicks(input.ReadInt64()),
                    input.ReadMatrix());
            }

            clips.Add(name, new AnimationClip(duration, keyframes));
        }

        return clips;
    }

    private static Matrix[] ReadMatrices(ContentReader input)
    {
        Matrix[] matrices = new Matrix[input.ReadInt32()];
        for (int index = 0; index < matrices.Length; index++)
        {
            matrices[index] = input.ReadMatrix();
        }

        return matrices;
    }

    private static int[] ReadIntegers(ContentReader input)
    {
        int[] values = new int[input.ReadInt32()];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = input.ReadInt32();
        }

        return values;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
