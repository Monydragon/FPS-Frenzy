using Microsoft.Xna.Framework;

namespace FpsFrenzy.Kni.Animation;

public sealed class AnimationPlayer
{
    private readonly SkinningData _skinningData;
    private readonly Matrix[] _boneTransforms;
    private readonly Matrix[] _worldTransforms;
    private readonly Matrix[] _skinTransforms;
    private AnimationClip? _currentClip;
    private TimeSpan _currentTime;
    private int _currentKeyframe;

    public AnimationPlayer(SkinningData skinningData)
    {
        ArgumentNullException.ThrowIfNull(skinningData);
        _skinningData = skinningData;
        _boneTransforms = new Matrix[skinningData.BindPose.Count];
        _worldTransforms = new Matrix[skinningData.BindPose.Count];
        _skinTransforms = new Matrix[skinningData.BindPose.Count];
    }

    public ReadOnlySpan<Matrix> SkinTransforms => _skinTransforms;
    public AnimationClip? CurrentClip => _currentClip;
    public TimeSpan CurrentTime => _currentTime;

    public Matrix[] GetSkinTransforms() => _skinTransforms;

    public void StartClip(AnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        _currentClip = clip;
        _currentTime = TimeSpan.Zero;
        _currentKeyframe = 0;
        CopyBindPose();
    }

    public void Update(TimeSpan elapsed, Matrix rootTransform)
    {
        AnimationClip clip = _currentClip ??
            throw new InvalidOperationException("StartClip must be called before Update.");
        TimeSpan nextTime = _currentTime + elapsed;
        while (nextTime >= clip.Duration)
        {
            nextTime -= clip.Duration;
        }

        if (nextTime < _currentTime)
        {
            _currentKeyframe = 0;
            CopyBindPose();
        }

        _currentTime = nextTime;
        while (_currentKeyframe < clip.Keyframes.Count &&
               clip.Keyframes[_currentKeyframe].Time <= _currentTime)
        {
            Keyframe keyframe = clip.Keyframes[_currentKeyframe++];
            _boneTransforms[keyframe.Bone] = keyframe.Transform;
        }

        UpdateSkinTransforms(rootTransform);
    }

    private void UpdateSkinTransforms(Matrix rootTransform)
    {
        _worldTransforms[0] = _boneTransforms[0] * rootTransform;
        for (int bone = 1; bone < _worldTransforms.Length; bone++)
        {
            int parent = _skinningData.SkeletonHierarchy[bone];
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
}
