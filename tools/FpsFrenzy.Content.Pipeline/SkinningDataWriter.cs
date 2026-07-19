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
    private const int TrsTrackWithCalibrationFormatMarker = -3;

    protected override void Write(ContentWriter output, SkinningDataContent value)
    {
        output.Write(TrsTrackWithCalibrationFormatMarker);
        output.Write(value.AnimationClips.Count);
        foreach (KeyValuePair<string, AnimationClipContent> pair in
                 value.AnimationClips.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.Write(pair.Key);
            output.Write(pair.Value.Duration.Ticks);
            output.Write(pair.Value.SampleRate);
            output.Write(pair.Value.Tracks.Count);
            foreach (BoneTrackContent track in pair.Value.Tracks)
            {
                output.Write(track.Bone);
                output.Write(track.Keyframes.Count);
                foreach (TransformKeyframeContent keyframe in track.Keyframes)
                {
                    output.Write(keyframe.Time.Ticks);
                    output.Write(keyframe.Scale);
                    output.Write(keyframe.Rotation);
                    output.Write(keyframe.Translation);
                }
            }
        }

        WriteMatrices(output, value.BindPose);
        WriteMatrices(output, value.InverseBindPose);
        output.Write(value.SkeletonHierarchy.Count);
        foreach (int parent in value.SkeletonHierarchy)
        {
            output.Write(parent);
        }

        output.Write(value.SourceBoundsMinimum);
        output.Write(value.SourceBoundsMaximum);
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
