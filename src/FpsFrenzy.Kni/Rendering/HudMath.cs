using System.Numerics;

namespace FpsFrenzy.Kni.Rendering;

public enum ReticleMode
{
    Hip,
    AimDot,
}

public readonly record struct CompassProjection(
    float RelativeBearing,
    float NormalizedPosition,
    bool IsBehind,
    int VerticalDirection);

public readonly record struct HudSafeArea(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public int CenterX => Left + (Width / 2);
    public int CenterY => Top + (Height / 2);
}

public static class HudMath
{
    public static CompassProjection ProjectEnemy(
        float playerYaw,
        Vector3 playerPosition,
        Vector3 enemyPosition)
    {
        float bearing = MathF.Atan2(
            enemyPosition.X - playerPosition.X,
            -(enemyPosition.Z - playerPosition.Z));
        float relative = WrapAngle(bearing - playerYaw);
        bool behind = MathF.Abs(relative) > MathF.PI / 2f;
        float normalized = behind ? (relative < 0f ? 0f : 1f) : (relative / MathF.PI) + 0.5f;
        float verticalDelta = enemyPosition.Y - playerPosition.Y;
        int vertical = MathF.Abs(verticalDelta) < 1.5f ? 0 : Math.Sign(verticalDelta);
        return new CompassProjection(relative, normalized, behind, vertical);
    }

    public static float InterpolateAds(float current, bool aiming, float deltaSeconds, float speed = 12f)
    {
        float target = aiming ? 1f : 0f;
        return float.Lerp(current, target, 1f - MathF.Exp(-speed * deltaSeconds));
    }

    public static ReticleMode GetReticleMode(bool aiming) => aiming ? ReticleMode.AimDot : ReticleMode.Hip;

    public static HudSafeArea CreateSafeArea(
        int viewportWidth,
        int viewportHeight,
        int insetLeft,
        int insetTop,
        int insetRight,
        int insetBottom)
    {
        int left = Math.Clamp(insetLeft, 0, viewportWidth);
        int top = Math.Clamp(insetTop, 0, viewportHeight);
        int right = Math.Clamp(viewportWidth - insetRight, left, viewportWidth);
        int bottom = Math.Clamp(viewportHeight - insetBottom, top, viewportHeight);
        return new HudSafeArea(left, top, right - left, bottom - top);
    }

    public static float WrapAngle(float angle) => MathF.Atan2(MathF.Sin(angle), MathF.Cos(angle));
}
