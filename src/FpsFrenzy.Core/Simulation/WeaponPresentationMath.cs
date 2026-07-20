using System.Numerics;
using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Core.Simulation;

/// <summary>
/// Builds a camera-local first-person pose. The simulation owns the clocks so
/// weapon motion freezes while paused and is identical at 30 and 60 FPS.
/// </summary>
public static class WeaponPresentationMath
{
    public const float EquipDurationSeconds = 0.28f;
    public const float FireKickDurationSeconds = 0.18f;

    public static WeaponPresentationPose CalculatePose(
        WeaponState weapon,
        bool isLeftHand,
        bool isDualWielding,
        float adsBlend,
        float equipSeconds,
        float elapsedSimulationSeconds,
        float movementBlend,
        float cameraBobScale)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        WeaponDefinition definition = weapon.Definition;
        WeaponAnimationDefinition animation = definition.Visual.Animation;
        adsBlend = SanitizeUnit(adsBlend);
        movementBlend = SanitizeUnit(movementBlend);
        cameraBobScale = Math.Clamp(FiniteOr(cameraBobScale, 1f), 0f, 2f);
        float handSign = isLeftHand ? -1f : 1f;
        float targetSpan = Math.Clamp(FiniteOr(definition.ViewModelTargetSpan, 0.65f), 0.1f, 1.5f) *
            float.Lerp(1f, definition.Visual.AdsTargetSpanScale, adsBlend);

        Vector3 adsOffset = definition.ViewModelAdsOffset;
        Vector3 calibratedSight = TransformCalibrationAnchor(
            definition,
            definition.Visual.SightAnchor,
            targetSpan,
            DegreesToRadians(definition.ViewModelYawDegrees),
            DegreesToRadians(definition.ViewModelPitchDegrees),
            DegreesToRadians(definition.ViewModelRollDegrees));
        adsOffset.X = -calibratedSight.X;
        adsOffset.Y = -calibratedSight.Y;
        Vector3 offset = Vector3.Lerp(definition.ViewModelHipOffset, adsOffset, adsBlend);
        if (isLeftHand)
        {
            offset.X = -MathF.Abs(offset.X);
        }

        float elapsed = MathF.Max(0f, FiniteOr(elapsedSimulationSeconds, 0f));
        float stridePhase = elapsed * 10.5f + (isLeftHand ? MathF.PI : 0f);
        float bobWeight = movementBlend * cameraBobScale * float.Lerp(1f, 0.35f, adsBlend);
        offset.X += MathF.Sin(stridePhase) * 0.0065f * bobWeight * animation.BobScale;
        offset.Y -= MathF.Abs(MathF.Cos(stridePhase)) * 0.0075f * bobWeight * animation.BobScale;
        offset.X += MathF.Sin((elapsed * 1.8f) + (isLeftHand ? 1.2f : 0f)) *
            0.0025f * cameraBobScale * animation.SwayScale * (1f - (adsBlend * 0.7f));
        if (isDualWielding)
        {
            // Keep two pistols readable while aiming, after sway has been
            // applied, so neither hand can drift across the center line.
            float minimumSeparation = float.Lerp(0.27f, 0.105f, adsBlend);
            offset.X = handSign * MathF.Max(MathF.Abs(offset.X), minimumSeparation);
        }

        float yaw = DegreesToRadians(definition.ViewModelYawDegrees);
        float pitch = DegreesToRadians(definition.ViewModelPitchDegrees);
        float roll = DegreesToRadians(definition.ViewModelRollDegrees);

        float equipDuration = Math.Clamp(FiniteOr(animation.EquipSeconds, EquipDurationSeconds), 0.05f, 2f);
        float equip = SmoothStep(Math.Clamp(FiniteOr(equipSeconds, 0f) / equipDuration, 0f, 1f));
        float unequipped = 1f - equip;
        offset.Y -= unequipped * 0.25f;
        offset.Z += unequipped * 0.10f;
        yaw += handSign * DegreesToRadians(5f) * unequipped;
        roll += handSign * DegreesToRadians(24f) * unequipped;

        float fireKick = CalculateFireKick(weapon.SecondsSinceLastShot, animation.FireKickSeconds) *
            Math.Clamp(definition.RecoilKick, 0f, 2.5f);
        offset.Y += fireKick * 0.018f;
        offset.Z += fireKick * animation.RecoilDistance;
        pitch += fireKick * DegreesToRadians(animation.RecoilPitchDegrees);
        yaw += handSign * fireKick * DegreesToRadians(0.9f);
        roll += handSign * fireKick * DegreesToRadians(1.4f);

        float reloadArc = weapon.IsReloading
            ? MathF.Pow(MathF.Sin(weapon.ReloadProgress * MathF.PI), 0.8f)
            : 0f;
        offset.X -= handSign * reloadArc * 0.035f;
        offset.Y -= reloadArc * animation.ReloadDropDistance;
        offset.Z += reloadArc * 0.055f;
        float stylePitch = animation.ReloadStyle switch
        {
            WeaponReloadStyle.PistolTilt => 0.75f,
            WeaponReloadStyle.LauncherRoll => 1.25f,
            WeaponReloadStyle.EnergyVent => -0.45f,
            WeaponReloadStyle.HeavyBrace => 0.55f,
            _ => 1f,
        };
        pitch -= reloadArc * DegreesToRadians(animation.ReloadPitchDegrees) * stylePitch;
        roll += handSign * reloadArc * DegreesToRadians(animation.ReloadRollDegrees);

        float overheat = weapon.IsOverheated
            ? 0.5f + (MathF.Sin(elapsed * 31f) * 0.5f)
            : 0f;
        offset.X += handSign * overheat * 0.004f * animation.OverheatShakeScale;
        offset.Y += overheat * 0.012f * animation.OverheatShakeScale;
        roll -= handSign * overheat * DegreesToRadians(2.5f);

        return new WeaponPresentationPose(
            offset,
            targetSpan,
            yaw,
            pitch,
            roll,
            fireKick,
            reloadArc);
    }

    public static Vector3 CalculateCalibrationAnchorPosition(
        WeaponDefinition definition,
        WeaponPresentationPose pose,
        Vector3 anchor)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return pose.Position + TransformCalibrationAnchor(
            definition, anchor, pose.TargetSpan, pose.Yaw, pose.Pitch, pose.Roll);
    }

    public static Vector3 CalculateMuzzleDirection(
        WeaponDefinition definition,
        WeaponPresentationPose pose)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Matrix4x4 rotation = Matrix4x4.CreateRotationX(pose.Pitch) *
            Matrix4x4.CreateRotationY(pose.Yaw) *
            Matrix4x4.CreateRotationZ(pose.Roll);
        return Vector3.Normalize(Vector3.TransformNormal(definition.Visual.ForwardAxis, rotation));
    }

    internal static float CalculateFireKick(float secondsSinceLastShot)
        => CalculateFireKick(secondsSinceLastShot, FireKickDurationSeconds);

    internal static float CalculateFireKick(float secondsSinceLastShot, float durationSeconds)
    {
        float duration = Math.Clamp(FiniteOr(durationSeconds, FireKickDurationSeconds), 0.05f, 1f);
        float time = MathF.Max(0f, FiniteOr(secondsSinceLastShot, duration));
        if (time >= duration)
        {
            return 0f;
        }

        float riseSeconds = MathF.Min(0.035f, duration * 0.35f);
        return time <= riseSeconds
            ? SmoothStep(time / riseSeconds)
            : 1f - SmoothStep((time - riseSeconds) / (duration - riseSeconds));
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - (2f * value));
    }

    private static float SanitizeUnit(float value) => Math.Clamp(FiniteOr(value, 0f), 0f, 1f);

    private static float FiniteOr(float value, float fallback) => float.IsFinite(value) ? value : fallback;

    private static Vector3 TransformCalibrationAnchor(
        WeaponDefinition definition,
        Vector3 anchor,
        float targetSpan,
        float yaw,
        float pitch,
        float roll)
    {
        Vector3 normalized = (anchor - definition.Visual.PivotOffset) * targetSpan *
            definition.Visual.SourceSpanScale;
        Matrix4x4 rotation = Matrix4x4.CreateRotationX(pitch) *
            Matrix4x4.CreateRotationY(yaw) *
            Matrix4x4.CreateRotationZ(roll);
        return Vector3.Transform(normalized, rotation);
    }

    private static float DegreesToRadians(float value) => FiniteOr(value, 0f) * (MathF.PI / 180f);
}

public readonly record struct WeaponPresentationPose(
    Vector3 Position,
    float TargetSpan,
    float Yaw,
    float Pitch,
    float Roll,
    float FireKick,
    float ReloadArc);
