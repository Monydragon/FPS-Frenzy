using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;

namespace FpsFrenzy.Kni.Animation;

public sealed record Keyframe(int Bone, TimeSpan Time, Matrix Transform);

public sealed record AnimationClip(TimeSpan Duration, IReadOnlyList<Keyframe> Keyframes);

public sealed class SkinningData(
    IReadOnlyDictionary<string, AnimationClip> animationClips,
    IReadOnlyList<Matrix> bindPose,
    IReadOnlyList<Matrix> inverseBindPose,
    IReadOnlyList<int> skeletonHierarchy)
{
    public IReadOnlyDictionary<string, AnimationClip> AnimationClips { get; } = animationClips;
    public IReadOnlyList<Matrix> BindPose { get; } = bindPose;
    public IReadOnlyList<Matrix> InverseBindPose { get; } = inverseBindPose;
    public IReadOnlyList<int> SkeletonHierarchy { get; } = skeletonHierarchy;
}

public sealed class SkinningDataReader : ContentTypeReader<SkinningData>
{
    protected override SkinningData Read(ContentReader input, SkinningData? existingInstance)
    {
        int clipCount = input.ReadInt32();
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

        return new SkinningData(clips, ReadMatrices(input), ReadMatrices(input), ReadIntegers(input));
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
}
