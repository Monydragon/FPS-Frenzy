using FpsFrenzy.Kni.Animation;
using Microsoft.Xna.Framework;

namespace FpsFrenzy.Content.Tests;

public sealed class AnimationPlayerTests
{
    [Fact]
    public void UpdateInterpolatesTranslationAndRotationBetweenTrackSamples()
    {
        AnimationClip clip = CreateClip(
            Transform(0f, Quaternion.Identity),
            Transform(10f, Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2)));
        AnimationPlayer player = CreatePlayer(clip);

        player.Update(TimeSpan.FromSeconds(0.5), Matrix.Identity);

        BoneTransform pose = BoneTransform.FromMatrix(player.BoneTransforms[0]);
        Assert.Equal(5f, pose.Translation.X, precision: 3);
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver4);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, pose.Rotation)) > 0.999f);
    }

    [Fact]
    public void LoopingClipWrapsAndRemainsFrameRateIndependent()
    {
        AnimationClip clip = CreateClip(Transform(0f), Transform(10f));
        AnimationPlayer singleStep = CreatePlayer(clip);
        AnimationPlayer splitSteps = CreatePlayer(clip);

        singleStep.Update(TimeSpan.FromSeconds(1.25), Matrix.Identity);
        for (int index = 0; index < 5; index++)
        {
            splitSteps.Update(TimeSpan.FromSeconds(0.25), Matrix.Identity);
        }

        Assert.Equal(0.25, singleStep.CurrentTime.TotalSeconds, precision: 5);
        AssertMatrixNear(singleStep.BoneTransforms[0], splitSteps.BoneTransforms[0]);
        Assert.Equal(2.5f, singleStep.BoneTransforms[0].Translation.X, precision: 3);
    }

    [Fact]
    public void PlaybackRateCanChangeWithoutRestartingTheCurrentClip()
    {
        AnimationPlayer player = CreatePlayer(CreateClip(Transform(0f), Transform(10f)));
        player.Update(TimeSpan.FromSeconds(0.2), Matrix.Identity);

        player.SetPlaybackRate(2f);
        player.Update(TimeSpan.FromSeconds(0.2), Matrix.Identity);

        Assert.Equal(0.6, player.CurrentTime.TotalSeconds, precision: 5);
        Assert.Equal(6f, player.BoneTransforms[0].Translation.X, precision: 3);
    }

    [Fact]
    public void ClampedOneShotHoldsItsFinalPose()
    {
        AnimationClip clip = CreateClip(Transform(0f), Transform(10f));
        AnimationPlayer player = CreatePlayer(
            clip,
            new AnimationPlaybackOptions(AnimationLoopMode.Clamp, TimeSpan.Zero));

        player.Update(TimeSpan.FromSeconds(1.25), Matrix.Identity);
        player.Update(TimeSpan.FromSeconds(2), Matrix.Identity);

        Assert.True(player.IsComplete);
        Assert.Equal(1, player.CurrentTime.TotalSeconds, precision: 5);
        Assert.Equal(10f, player.BoneTransforms[0].Translation.X, precision: 3);
    }

    [Fact]
    public void ClampedSegmentStartsAtAuthoredWindowAndHoldsItsEndPose()
    {
        AnimationClip clip = CreateClip(Transform(0f), Transform(10f));
        AnimationPlayer player = CreatePlayer(
            clip,
            new AnimationPlaybackOptions(
                AnimationLoopMode.Clamp,
                TimeSpan.Zero,
                StartNormalized: 0.25f,
                EndNormalized: 0.5f));

        Assert.Equal(0.25, player.CurrentTime.TotalSeconds, precision: 5);
        Assert.Equal(2.5f, player.BoneTransforms[0].Translation.X, precision: 3);

        player.Update(TimeSpan.FromSeconds(1), Matrix.Identity);

        Assert.True(player.IsComplete);
        Assert.Equal(0.5, player.CurrentTime.TotalSeconds, precision: 5);
        Assert.Equal(5f, player.BoneTransforms[0].Translation.X, precision: 3);
    }

    [Fact]
    public void TransitionBetweenSameClipSegmentsCrossfadesAtAuthoredBoundary()
    {
        AnimationClip clip = CreateClip(Transform(0f), Transform(10f));
        AnimationPlayer player = CreatePlayer(
            clip,
            new AnimationPlaybackOptions(
                AnimationLoopMode.Clamp,
                TimeSpan.Zero,
                StartNormalized: 0f,
                EndNormalized: 0.3f));
        player.Update(TimeSpan.FromSeconds(0.3), Matrix.Identity);
        Assert.Equal(3f, player.BoneTransforms[0].Translation.X, precision: 3);

        player.TransitionTo(
            clip,
            new AnimationPlaybackOptions(
                AnimationLoopMode.Clamp,
                TimeSpan.FromSeconds(0.2),
                StartNormalized: 0.6f,
                EndNormalized: 0.9f));

        Assert.Equal(0.6, player.CurrentTime.TotalSeconds, precision: 5);
        Assert.Equal(3f, player.BoneTransforms[0].Translation.X, precision: 3);
        player.Update(TimeSpan.FromSeconds(0.1), Matrix.Identity);
        Assert.Equal(5f, player.BoneTransforms[0].Translation.X, precision: 3);
        player.Update(TimeSpan.FromSeconds(0.1), Matrix.Identity);
        Assert.Equal(8f, player.BoneTransforms[0].Translation.X, precision: 3);
    }

    [Fact]
    public void PausedPlayerDoesNotAdvanceClipOrTransition()
    {
        AnimationClip from = CreateConstantClip(0f);
        AnimationClip to = CreateConstantClip(10f);
        AnimationPlayer player = CreatePlayer(from);
        player.TransitionTo(
            to,
            new AnimationPlaybackOptions(AnimationLoopMode.Loop, TimeSpan.FromSeconds(0.2)));
        player.IsPaused = true;

        player.Update(TimeSpan.FromSeconds(0.1), Matrix.Identity);

        Assert.Equal(TimeSpan.Zero, player.CurrentTime);
        Assert.True(player.IsTransitioning);
        Assert.Equal(0f, player.BoneTransforms[0].Translation.X, precision: 3);
    }

    [Fact]
    public void TransitionCrossfadesFromCapturedPose()
    {
        AnimationPlayer player = CreatePlayer(CreateConstantClip(0f));
        player.TransitionTo(
            CreateConstantClip(10f),
            new AnimationPlaybackOptions(AnimationLoopMode.Loop, TimeSpan.FromSeconds(0.2)));

        player.Update(TimeSpan.FromSeconds(0.1), Matrix.Identity);

        Assert.True(player.IsTransitioning);
        Assert.Equal(5f, player.BoneTransforms[0].Translation.X, precision: 3);
        player.Update(TimeSpan.FromSeconds(0.1), Matrix.Identity);
        Assert.False(player.IsTransitioning);
        Assert.Equal(10f, player.BoneTransforms[0].Translation.X, precision: 3);
    }

    [Fact]
    public void HigherPriorityTransitionContinuesFromCurrentlyBlendedPose()
    {
        AnimationPlayer player = CreatePlayer(CreateConstantClip(0f));
        AnimationPlaybackOptions transition = new(
            AnimationLoopMode.Loop,
            TimeSpan.FromSeconds(0.2));
        player.TransitionTo(CreateConstantClip(10f), transition);
        player.Update(TimeSpan.FromSeconds(0.1), Matrix.Identity);
        Assert.Equal(5f, player.BoneTransforms[0].Translation.X, precision: 3);

        player.TransitionTo(CreateConstantClip(20f), transition);
        player.Update(TimeSpan.FromSeconds(0.1), Matrix.Identity);

        Assert.Equal(12.5f, player.BoneTransforms[0].Translation.X, precision: 3);
    }

    [Fact]
    public void ReachBoneLimitIsEnforcedAtRuntime()
    {
        const int boneCount = SkinningData.MaximumReachBones + 1;
        Matrix[] bindPose = Enumerable.Repeat(Matrix.Identity, boneCount).ToArray();
        int[] hierarchy = Enumerable.Repeat(-1, boneCount).ToArray();
        AnimationClip clip = CreateConstantClip(0f);
        SkinningData data = new(
            new Dictionary<string, AnimationClip> { ["test"] = clip },
            bindPose,
            bindPose,
            hierarchy);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new AnimationPlayer(data));
        Assert.Contains("Reach/mobile limit", exception.Message, StringComparison.Ordinal);
    }

    private static AnimationPlayer CreatePlayer(
        AnimationClip clip,
        AnimationPlaybackOptions? options = null)
    {
        SkinningData data = new(
            new Dictionary<string, AnimationClip> { ["test"] = clip },
            [Matrix.Identity],
            [Matrix.Identity],
            [-1]);
        AnimationPlayer player = new(data);
        player.StartClip(
            clip,
            options ?? new AnimationPlaybackOptions(AnimationLoopMode.Loop, TimeSpan.Zero));
        return player;
    }

    private static AnimationClip CreateClip(BoneTransform start, BoneTransform end) =>
        new(
            TimeSpan.FromSeconds(1),
            [
                new BoneAnimationTrack(
                    0,
                    [
                        new TransformKeyframe(TimeSpan.Zero, start),
                        new TransformKeyframe(TimeSpan.FromSeconds(1), end),
                    ]),
            ]);

    private static AnimationClip CreateConstantClip(float translationX) =>
        CreateClip(Transform(translationX), Transform(translationX));

    private static BoneTransform Transform(float translationX, Quaternion? rotation = null) =>
        new(Vector3.One, rotation ?? Quaternion.Identity, new Vector3(translationX, 0f, 0f));

    private static void AssertMatrixNear(Matrix expected, Matrix actual)
    {
        Assert.Equal(expected.M11, actual.M11, precision: 4);
        Assert.Equal(expected.M22, actual.M22, precision: 4);
        Assert.Equal(expected.M33, actual.M33, precision: 4);
        Assert.Equal(expected.Translation.X, actual.Translation.X, precision: 4);
        Assert.Equal(expected.Translation.Y, actual.Translation.Y, precision: 4);
        Assert.Equal(expected.Translation.Z, actual.Translation.Z, precision: 4);
    }
}
