using System.Numerics;
using FpsFrenzy.Kni.Rendering;

namespace FpsFrenzy.Content.Tests;

public sealed class HudMathTests
{
    [Fact]
    public void CompassBearingWrapsAcrossPositiveAndNegativePi()
    {
        CompassProjection projection = HudMath.ProjectEnemy(
            MathF.PI - 0.05f,
            Vector3.Zero,
            new Vector3(-0.1f, 0f, 1f));

        Assert.InRange(projection.RelativeBearing, -0.2f, 0.2f);
        Assert.False(projection.IsBehind);
    }

    [Fact]
    public void CompassMarksBehindAndVerticalEnemies()
    {
        CompassProjection projection = HudMath.ProjectEnemy(
            0f,
            Vector3.Zero,
            new Vector3(1f, 3f, 4f));

        Assert.True(projection.IsBehind);
        Assert.Equal(1, projection.VerticalDirection);
        Assert.Equal(1f, projection.NormalizedPosition);
    }

    [Fact]
    public void AdsInterpolationConvergesAndChangesReticleMode()
    {
        float blend = 0f;
        for (int frame = 0; frame < 60; frame++)
        {
            blend = HudMath.InterpolateAds(blend, aiming: true, 1f / 60f);
        }

        Assert.InRange(blend, 0.99f, 1f);
        Assert.Equal(ReticleMode.AimDot, HudMath.GetReticleMode(aiming: true));
        Assert.Equal(ReticleMode.Hip, HudMath.GetReticleMode(aiming: false));
    }

    [Fact]
    public void SafeAreaHonorsAllDisplayCutoutInsets()
    {
        HudSafeArea safe = HudMath.CreateSafeArea(2400, 1080, 80, 24, 120, 36);

        Assert.Equal(80, safe.Left);
        Assert.Equal(24, safe.Top);
        Assert.Equal(2280, safe.Right);
        Assert.Equal(1044, safe.Bottom);
        Assert.InRange(safe.CenterX, safe.Left, safe.Right);
    }
}
