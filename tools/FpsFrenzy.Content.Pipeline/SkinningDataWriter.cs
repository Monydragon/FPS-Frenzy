using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler;

namespace FpsFrenzy.Content.Pipeline;

[ContentTypeWriter]
internal sealed class SkinningDataWriter : ContentTypeWriter<SkinningDataContent>
{
    protected override void Write(ContentWriter output, SkinningDataContent value)
    {
        output.Write(value.AnimationClips.Count);
        foreach (KeyValuePair<string, AnimationClipContent> pair in
                 value.AnimationClips.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.Write(pair.Key);
            output.Write(pair.Value.Duration.Ticks);
            output.Write(pair.Value.Keyframes.Count);
            foreach (KeyframeContent keyframe in pair.Value.Keyframes)
            {
                output.Write(keyframe.Bone);
                output.Write(keyframe.Time.Ticks);
                output.Write(keyframe.Transform);
            }
        }

        WriteMatrices(output, value.BindPose);
        WriteMatrices(output, value.InverseBindPose);
        output.Write(value.SkeletonHierarchy.Count);
        foreach (int parent in value.SkeletonHierarchy)
        {
            output.Write(parent);
        }
    }

    public override string GetRuntimeReader(TargetPlatform targetPlatform) =>
        "FpsFrenzy.Kni.Animation.SkinningDataReader, FpsFrenzy.Kni";

    public override string GetRuntimeType(TargetPlatform targetPlatform) =>
        "FpsFrenzy.Kni.Animation.SkinningData, FpsFrenzy.Kni";

    private static void WriteMatrices(ContentWriter output, List<Matrix> matrices)
    {
        output.Write(matrices.Count);
        foreach (Matrix matrix in matrices)
        {
            output.Write(matrix);
        }
    }
}
