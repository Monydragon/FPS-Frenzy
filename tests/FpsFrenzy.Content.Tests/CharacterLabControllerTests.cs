using FpsFrenzy.Kni.Development;

namespace FpsFrenzy.Content.Tests;

public sealed class CharacterLabControllerTests
{
    [Fact]
    public void EnvironmentOptionsAreDisabledUnlessExplicitlyEnabled()
    {
        CharacterLabOptions? options = CharacterLabOptions.FromValues(_ => null);

        Assert.Null(options);
    }

    [Fact]
    public void EnvironmentOptionsNormalizeTheRobotAndValidateCaptureRate()
    {
        Dictionary<string, string?> values = new(StringComparer.Ordinal)
        {
            [CharacterLabOptions.EnableVariable] = "1",
            [CharacterLabOptions.EnemyVariable] = "ROBOT-WASP",
            [CharacterLabOptions.ModeVariable] = "reel",
            [CharacterLabOptions.FrameRateVariable] = "30",
        };

        CharacterLabOptions options = Assert.IsType<CharacterLabOptions>(
            CharacterLabOptions.FromValues(name => values.GetValueOrDefault(name)));

        Assert.Equal("robot-wasp", options.EnemyId);
        Assert.Equal(CharacterLabCaptureMode.Reel, options.Mode);
        Assert.Equal(30, options.FramesPerSecond);
        Assert.Equal("character-lab-robot-wasp-reel-30fps", options.ReelName);
    }

    [Fact]
    public void StillMatrixIsDeterministicAndContainsEveryPoseAtEveryDistance()
    {
        CharacterLabController controller = new(new CharacterLabOptions
        {
            EnemyId = "robot-striker",
            Mode = CharacterLabCaptureMode.Stills,
            FramesPerSecond = 60,
        });
        List<string> captureNames = [];
        int safety = 10_000;

        while (!controller.IsComplete && safety-- > 0)
        {
            controller.AdvanceStillAnimationFrame();
            if (!controller.TryBeginStillCapture(out string name))
            {
                continue;
            }

            captureNames.Add(name);
            controller.NotifyStillCaptured();
        }

        Assert.True(safety > 0);
        Assert.Equal(15, captureNames.Count);
        Assert.Equal("character-lab-robot-striker-idle-near", captureNames[0]);
        Assert.Equal("character-lab-robot-striker-idle-medium", captureNames[1]);
        Assert.Equal("character-lab-robot-striker-idle-far", captureNames[2]);
        Assert.Equal("character-lab-robot-striker-death-far", captureNames[^1]);
        Assert.Equal(15, captureNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public void ReelUsesExactFrameCountAndTwoSecondsPerPose(int framesPerSecond)
    {
        CharacterLabController controller = new(new CharacterLabOptions
        {
            EnemyId = "breach-walker",
            Mode = CharacterLabCaptureMode.Reel,
            FramesPerSecond = framesPerSecond,
        });
        int framesPerPose = 2 * framesPerSecond;

        Assert.Equal(CharacterLabPose.Idle, controller.CurrentPose);
        for (int index = 0; index < framesPerPose; index++)
        {
            controller.NotifyRecordingFrameCaptured();
        }

        Assert.Equal(CharacterLabPose.Locomotion, controller.CurrentPose);
        while (!controller.IsComplete)
        {
            controller.NotifyRecordingFrameCaptured();
        }

        Assert.Equal(10 * framesPerSecond, controller.CapturedReelFrames);
        Assert.Equal(10 * framesPerSecond, controller.ExpectedReelFrames);
        Assert.Equal(CharacterLabPose.Death, controller.CurrentPose);
    }

    [Fact]
    public void ReelAlwaysUsesMediumDistanceAndStillsUseAuthoredDistanceOrder()
    {
        CharacterLabController reel = new(new CharacterLabOptions
        {
            EnemyId = "robot-warden",
            Mode = CharacterLabCaptureMode.Reel,
        });
        CharacterLabController stills = new(new CharacterLabOptions
        {
            EnemyId = "robot-warden",
            Mode = CharacterLabCaptureMode.Stills,
        });

        Assert.Equal(CharacterLabDistance.Medium, reel.CurrentDistance);
        Assert.Equal(12f, reel.CameraDistanceMeters);
        Assert.Equal(CharacterLabDistance.Near, stills.CurrentDistance);
        Assert.Equal(5f, stills.CameraDistanceMeters);
    }
}
