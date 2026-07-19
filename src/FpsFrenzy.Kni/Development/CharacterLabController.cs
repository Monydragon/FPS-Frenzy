namespace FpsFrenzy.Kni.Development;

public enum CharacterLabCaptureMode
{
    Stills,
    Reel,
}

public enum CharacterLabPose
{
    Idle,
    Locomotion,
    Attack,
    Hit,
    Death,
}

public enum CharacterLabDistance
{
    Near,
    Medium,
    Far,
}

public sealed record CharacterLabOptions
{
    public const int ReelDurationSeconds = 10;
    public const string EnableVariable = "FPS_FRENZY_CHARACTER_LAB";
    public const string EnemyVariable = "FPS_FRENZY_LAB_ENEMY";
    public const string ModeVariable = "FPS_FRENZY_LAB_MODE";
    public const string FrameRateVariable = "FPS_FRENZY_LAB_FPS";

    public static IReadOnlyList<string> RobotRoster { get; } =
    [
        "robot-striker",
        "robot-interceptor",
        "robot-juggernaut",
        "robot-wasp",
        "robot-warden",
        "breach-walker",
    ];

    public required string EnemyId { get; init; }
    public CharacterLabCaptureMode Mode { get; init; } = CharacterLabCaptureMode.Stills;
    public int FramesPerSecond { get; init; } = 60;

    public string ReelName => $"character-lab-{EnemyId}-reel-{FramesPerSecond}fps";

    public static CharacterLabOptions? FromEnvironment() => FromValues(
        Environment.GetEnvironmentVariable);

    public static CharacterLabOptions? FromValues(Func<string, string?> getValue)
    {
        ArgumentNullException.ThrowIfNull(getValue);
        if (!string.Equals(getValue(EnableVariable), "1", StringComparison.Ordinal))
        {
            return null;
        }

        string enemyId = getValue(EnemyVariable)?.Trim() ?? "robot-striker";
        if (!RobotRoster.Contains(enemyId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Character Lab enemy '{enemyId}' is not in the schema-v2 robot roster.");
        }

        string modeValue = getValue(ModeVariable)?.Trim() ?? "stills";
        CharacterLabCaptureMode mode = modeValue.ToLowerInvariant() switch
        {
            "stills" => CharacterLabCaptureMode.Stills,
            "reel" => CharacterLabCaptureMode.Reel,
            _ => throw new InvalidOperationException(
                $"Character Lab mode '{modeValue}' must be 'stills' or 'reel'."),
        };

        int framesPerSecond = int.TryParse(getValue(FrameRateVariable), out int configuredFrameRate)
            ? configuredFrameRate
            : 60;
        if (framesPerSecond is not (30 or 60))
        {
            throw new InvalidOperationException("Character Lab recordings support only 30 or 60 FPS.");
        }

        return new CharacterLabOptions
        {
            EnemyId = RobotRoster.First(id => string.Equals(id, enemyId, StringComparison.OrdinalIgnoreCase)),
            Mode = mode,
            FramesPerSecond = framesPerSecond,
        };
    }
}

/// <summary>
/// Pure deterministic schedule for Character Lab stills and state reels. Rendering code advances
/// this controller only after a frame has actually been written, keeping slow PNG encoding from
/// changing which animation state appears in a numbered recording frame.
/// </summary>
public sealed class CharacterLabController
{
    private static readonly CharacterLabPose[] Poses = Enum.GetValues<CharacterLabPose>();
    private static readonly CharacterLabDistance[] Distances = Enum.GetValues<CharacterLabDistance>();
    private readonly CharacterLabOptions _options;
    private int _poseIndex;
    private int _distanceIndex;
    private int _warmupFrame;
    private int _capturedReelFrames;
    private bool _captureQueued;
    private bool _complete;

    public CharacterLabController(CharacterLabOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.FramesPerSecond is not (30 or 60))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "Character Lab recordings support only 30 or 60 FPS.");
        }

        _options = options;
    }

    public CharacterLabCaptureMode Mode => _options.Mode;
    public CharacterLabPose CurrentPose => Mode == CharacterLabCaptureMode.Reel
        ? ReelPoseForFrame(_capturedReelFrames, _options.FramesPerSecond)
        : Poses[Math.Min(_poseIndex, Poses.Length - 1)];
    public CharacterLabDistance CurrentDistance => Mode == CharacterLabCaptureMode.Reel
        ? CharacterLabDistance.Medium
        : Distances[Math.Min(_distanceIndex, Distances.Length - 1)];
    public int FramesPerSecond => _options.FramesPerSecond;
    public int CapturedReelFrames => _capturedReelFrames;
    public int ExpectedReelFrames => CharacterLabOptions.ReelDurationSeconds * FramesPerSecond;
    public bool IsComplete => _complete;
    public bool IsStillPoseReady => Mode == CharacterLabCaptureMode.Stills &&
        _warmupFrame >= WarmupFrames(CurrentPose, FramesPerSecond);
    public bool ShouldPauseAnimation => Mode == CharacterLabCaptureMode.Stills && IsStillPoseReady;

    public float CameraDistanceMeters => CurrentDistance switch
    {
        CharacterLabDistance.Near => 5f,
        CharacterLabDistance.Medium => 12f,
        CharacterLabDistance.Far => 24f,
        _ => throw new InvalidOperationException("Unsupported Character Lab camera distance."),
    };

    public void AdvanceStillAnimationFrame()
    {
        if (Mode != CharacterLabCaptureMode.Stills || _complete || _captureQueued || IsStillPoseReady)
        {
            return;
        }

        _warmupFrame++;
    }

    public bool TryBeginStillCapture(out string captureName)
    {
        captureName = string.Empty;
        if (Mode != CharacterLabCaptureMode.Stills || _complete || _captureQueued || !IsStillPoseReady)
        {
            return false;
        }

        captureName = $"character-lab-{_options.EnemyId}-{Slug(CurrentPose)}-{Slug(CurrentDistance)}";
        _captureQueued = true;
        return true;
    }

    public void NotifyStillCaptured()
    {
        if (Mode != CharacterLabCaptureMode.Stills || !_captureQueued || _complete)
        {
            return;
        }

        _captureQueued = false;
        _distanceIndex++;
        if (_distanceIndex < Distances.Length)
        {
            return;
        }

        _distanceIndex = 0;
        _poseIndex++;
        _warmupFrame = 0;
        if (_poseIndex >= Poses.Length)
        {
            _poseIndex = Poses.Length - 1;
            _complete = true;
        }
    }

    public void NotifyRecordingFrameCaptured()
    {
        if (Mode != CharacterLabCaptureMode.Reel || _complete)
        {
            return;
        }

        _capturedReelFrames++;
        if (_capturedReelFrames >= ExpectedReelFrames)
        {
            _capturedReelFrames = ExpectedReelFrames;
            _complete = true;
        }
    }

    public static CharacterLabPose ReelPoseForFrame(int frame, int framesPerSecond)
    {
        if (framesPerSecond is not (30 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        }

        int clampedFrame = Math.Clamp(
            frame,
            0,
            (CharacterLabOptions.ReelDurationSeconds * framesPerSecond) - 1);
        int framesPerPose = (CharacterLabOptions.ReelDurationSeconds * framesPerSecond) / Poses.Length;
        return Poses[Math.Min(clampedFrame / framesPerPose, Poses.Length - 1)];
    }

    private static int WarmupFrames(CharacterLabPose pose, int framesPerSecond)
    {
        float seconds = pose switch
        {
            CharacterLabPose.Idle => 0.5f,
            CharacterLabPose.Locomotion => 0.5f,
            CharacterLabPose.Attack => 0.32f,
            CharacterLabPose.Hit => 0.18f,
            CharacterLabPose.Death => 1.15f,
            _ => 0.5f,
        };
        return Math.Max(1, (int)MathF.Round(seconds * framesPerSecond));
    }

    private static string Slug<T>(T value) where T : struct, Enum =>
        value.ToString().ToLowerInvariant();
}
