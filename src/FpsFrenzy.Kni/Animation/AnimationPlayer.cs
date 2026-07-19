using Microsoft.Xna.Framework;

namespace FpsFrenzy.Kni.Animation;

public enum AnimationLoopMode
{
    Loop,
    Clamp,
}

public readonly record struct AnimationPlaybackOptions(
    AnimationLoopMode LoopMode,
    TimeSpan TransitionDuration,
    float PlaybackRate = 1f,
    float StartNormalized = 0f,
    float EndNormalized = 1f)
{
    public static AnimationPlaybackOptions Looping { get; } =
        new(AnimationLoopMode.Loop, TimeSpan.FromSeconds(0.12), 1f);

    public static AnimationPlaybackOptions OneShot { get; } =
        new(AnimationLoopMode.Clamp, TimeSpan.FromSeconds(0.12), 1f);
}

public sealed class AnimationPlayer
{
    private readonly SkinningData _skinningData;
    private readonly BoneTransform[] _bindPose;
    private readonly BoneTransform[] _sampledPose;
    private readonly BoneTransform[] _transitionSourcePose;
    private readonly Matrix[] _boneTransforms;
    private readonly Matrix[] _worldTransforms;
    private readonly Matrix[] _skinTransforms;
    private AnimationClip? _currentClip;
    private TimeSpan _currentTime;
    private AnimationLoopMode _loopMode;
    private TimeSpan _transitionDuration;
    private TimeSpan _transitionElapsed;
    private TimeSpan _segmentStart;
    private TimeSpan _segmentEnd;
    private float _playbackRate = 1f;

    public AnimationPlayer(SkinningData skinningData)
    {
        ArgumentNullException.ThrowIfNull(skinningData);
        if (skinningData.BindPose.Count == 0 ||
            skinningData.BindPose.Count != skinningData.InverseBindPose.Count ||
            skinningData.BindPose.Count != skinningData.SkeletonHierarchy.Count)
        {
            throw new ArgumentException("Skinning data must contain equally sized, non-empty skeleton arrays.", nameof(skinningData));
        }

        if (skinningData.BindPose.Count > SkinningData.MaximumReachBones)
        {
            throw new ArgumentException(
                $"Skeleton has {skinningData.BindPose.Count} bones; the Reach/mobile limit is {SkinningData.MaximumReachBones}.",
                nameof(skinningData));
        }

        _skinningData = skinningData;
        _bindPose = new BoneTransform[skinningData.BindPose.Count];
        _sampledPose = new BoneTransform[skinningData.BindPose.Count];
        _transitionSourcePose = new BoneTransform[skinningData.BindPose.Count];
        _boneTransforms = new Matrix[skinningData.BindPose.Count];
        _worldTransforms = new Matrix[skinningData.BindPose.Count];
        _skinTransforms = new Matrix[skinningData.BindPose.Count];
        for (int index = 0; index < _bindPose.Length; index++)
        {
            _bindPose[index] = BoneTransform.FromMatrix(skinningData.BindPose[index]);
        }

        CopyBindPose();
        UpdateSkinTransforms(Matrix.Identity);
    }

    public ReadOnlySpan<Matrix> SkinTransforms => _skinTransforms;
    public ReadOnlySpan<Matrix> BoneTransforms => _boneTransforms;
    public AnimationClip? CurrentClip => _currentClip;
    public TimeSpan CurrentTime => _currentTime;
    public float CurrentNormalizedTime => _currentClip is null
        ? 0f
        : (float)(_currentTime.TotalSeconds / _currentClip.Duration.TotalSeconds);
    public float SegmentStartNormalized => _currentClip is null
        ? 0f
        : (float)(_segmentStart.TotalSeconds / _currentClip.Duration.TotalSeconds);
    public float SegmentEndNormalized => _currentClip is null
        ? 1f
        : (float)(_segmentEnd.TotalSeconds / _currentClip.Duration.TotalSeconds);
    public AnimationLoopMode LoopMode => _loopMode;
    public float PlaybackRate => _playbackRate;
    public bool IsPaused { get; set; }
    public bool IsTransitioning => _transitionElapsed < _transitionDuration;
    public bool IsComplete =>
        _currentClip is not null &&
        _loopMode == AnimationLoopMode.Clamp &&
        _currentTime >= _segmentEnd &&
        !IsTransitioning;

    public Matrix[] GetSkinTransforms() => _skinTransforms;

    public void StartClip(AnimationClip clip) =>
        StartClip(clip, new AnimationPlaybackOptions(AnimationLoopMode.Loop, TimeSpan.Zero));

    public void StartClip(AnimationClip clip, AnimationPlaybackOptions options)
    {
        ValidatePlayback(clip, options);
        _currentClip = clip;
        SetSegment(clip, options);
        _currentTime = _segmentStart;
        _loopMode = options.LoopMode;
        _playbackRate = options.PlaybackRate;
        _transitionDuration = TimeSpan.Zero;
        _transitionElapsed = TimeSpan.Zero;
        SampleCurrentPose();
        WriteSampledPose();
        UpdateSkinTransforms(Matrix.Identity);
    }

    public void TransitionTo(AnimationClip clip, AnimationPlaybackOptions options)
    {
        ValidatePlayback(clip, options);
        if (_currentClip is null || options.TransitionDuration == TimeSpan.Zero)
        {
            StartClip(clip, options with { TransitionDuration = TimeSpan.Zero });
            return;
        }

        for (int bone = 0; bone < _boneTransforms.Length; bone++)
        {
            _transitionSourcePose[bone] = BoneTransform.FromMatrix(_boneTransforms[bone]);
        }

        _currentClip = clip;
        SetSegment(clip, options);
        _currentTime = _segmentStart;
        _loopMode = options.LoopMode;
        _playbackRate = options.PlaybackRate;
        _transitionDuration = options.TransitionDuration;
        _transitionElapsed = TimeSpan.Zero;
        SampleCurrentPose();
        WriteTransitionedPose(0f);
    }

    public void SetPlaybackRate(float playbackRate)
    {
        if (!float.IsFinite(playbackRate) || playbackRate <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playbackRate),
                "Playback rate must be finite and greater than zero.");
        }

        _playbackRate = playbackRate;
    }

    public void Update(TimeSpan elapsed, Matrix rootTransform)
    {
        if (_currentClip is null)
        {
            throw new InvalidOperationException("StartClip must be called before Update.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        if (!IsPaused)
        {
            AdvanceTime(elapsed);
            if (IsTransitioning)
            {
                _transitionElapsed = Min(_transitionDuration, _transitionElapsed + elapsed);
            }
        }

        SampleCurrentPose();
        if (IsTransitioning)
        {
            float amount = _transitionDuration <= TimeSpan.Zero
                ? 1f
                : (float)(_transitionElapsed.TotalSeconds / _transitionDuration.TotalSeconds);
            WriteTransitionedPose(amount);
        }
        else
        {
            WriteSampledPose();
        }

        UpdateSkinTransforms(rootTransform);
    }

    private static void ValidatePlayback(AnimationClip clip, AnimationPlaybackOptions options)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (options.TransitionDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Transition duration cannot be negative.");
        }

        if (float.IsNaN(options.PlaybackRate) || float.IsInfinity(options.PlaybackRate) || options.PlaybackRate <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Playback rate must be finite and greater than zero.");
        }

        if (!float.IsFinite(options.StartNormalized) || !float.IsFinite(options.EndNormalized) ||
            options.StartNormalized < 0f || options.EndNormalized > 1f ||
            options.StartNormalized >= options.EndNormalized)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The normalized clip window must satisfy 0 <= StartNormalized < EndNormalized <= 1.");
        }
    }

    private void SetSegment(AnimationClip clip, AnimationPlaybackOptions options)
    {
        _segmentStart = TimeSpan.FromTicks((long)Math.Round(clip.Duration.Ticks * options.StartNormalized));
        _segmentEnd = TimeSpan.FromTicks((long)Math.Round(clip.Duration.Ticks * options.EndNormalized));
        if (_segmentEnd <= _segmentStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The normalized clip window is too narrow for this clip's time resolution.");
        }
    }

    private void AdvanceTime(TimeSpan elapsed)
    {
        long elapsedTicks = (long)Math.Round(elapsed.Ticks * (double)_playbackRate);
        long segmentStartTicks = _segmentStart.Ticks;
        long segmentEndTicks = _segmentEnd.Ticks;
        long nextTicks = _currentTime.Ticks + Math.Max(0L, elapsedTicks);
        if (_loopMode == AnimationLoopMode.Loop)
        {
            long segmentTicks = segmentEndTicks - segmentStartTicks;
            nextTicks = segmentStartTicks + ((nextTicks - segmentStartTicks) % segmentTicks);
        }
        else
        {
            nextTicks = Math.Min(nextTicks, segmentEndTicks);
        }

        _currentTime = TimeSpan.FromTicks(nextTicks);
    }

    private void SampleCurrentPose()
    {
        Array.Copy(_bindPose, _sampledPose, _bindPose.Length);
        foreach (BoneAnimationTrack track in _currentClip!.Tracks)
        {
            if ((uint)track.Bone >= (uint)_sampledPose.Length)
            {
                throw new InvalidDataException(
                    $"Animation track targets bone {track.Bone}, but the skeleton has {_sampledPose.Length} bones.");
            }

            _sampledPose[track.Bone] = track.Sample(_currentTime);
        }
    }

    private void WriteSampledPose()
    {
        for (int bone = 0; bone < _boneTransforms.Length; bone++)
        {
            _boneTransforms[bone] = _sampledPose[bone].ToMatrix();
        }
    }

    private void WriteTransitionedPose(float amount)
    {
        for (int bone = 0; bone < _boneTransforms.Length; bone++)
        {
            _boneTransforms[bone] = BoneTransform.Interpolate(
                _transitionSourcePose[bone], _sampledPose[bone], amount).ToMatrix();
        }
    }

    private void UpdateSkinTransforms(Matrix rootTransform)
    {
        for (int bone = 0; bone < _worldTransforms.Length; bone++)
        {
            int parent = _skinningData.SkeletonHierarchy[bone];
            if (parent < 0)
            {
                _worldTransforms[bone] = _boneTransforms[bone] * rootTransform;
                continue;
            }

            if (parent >= bone)
            {
                throw new InvalidDataException(
                    $"Skeleton parent {parent} for bone {bone} must precede its child in the flattened hierarchy.");
            }

            _worldTransforms[bone] = _boneTransforms[bone] * _worldTransforms[parent];
        }

        for (int bone = 0; bone < _skinTransforms.Length; bone++)
        {
            _skinTransforms[bone] = _skinningData.InverseBindPose[bone] * _worldTransforms[bone];
        }
    }

    private void CopyBindPose()
    {
        for (int index = 0; index < _boneTransforms.Length; index++)
        {
            _boneTransforms[index] = _skinningData.BindPose[index];
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
}
