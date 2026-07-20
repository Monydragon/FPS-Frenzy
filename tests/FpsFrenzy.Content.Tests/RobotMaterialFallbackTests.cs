using FpsFrenzy.Kni.Rendering;
using Microsoft.Xna.Framework;

namespace FpsFrenzy.Content.Tests;

public sealed class RobotMaterialFallbackTests
{
    [Fact]
    public void EmissiveMaskOwnsAccentWhileHitFlashRemainsOnBasePass()
    {
        Vector3 accent = new(0.2f, 0.7f, 1f);

        Vector3 withoutMask = RobotMaterialFallback.CalculateBaseEmissive(accent, 0.5f, false);
        Vector3 withMask = RobotMaterialFallback.CalculateBaseEmissive(accent, 0.5f, true);
        Vector3 maskColor = RobotMaterialFallback.CalculateEmissiveMaskColor(
            new Vector3(-1f, 0.7f, 4f));

        AssertVectorNear((accent * 0.12f) + (Vector3.One * 0.35f), withoutMask);
        AssertVectorNear(Vector3.One * 0.35f, withMask);
        AssertVectorNear(new Vector3(0f, 0.7f, 1f), maskColor);
    }

    [Fact]
    public void RimColorClampsAuthoredInputsAndBrightensForHitFlash()
    {
        Vector3 baseRim = RobotMaterialFallback.CalculateRimColor(
            new Vector3(-4f, 0.5f, 5f),
            0f);
        Vector3 hitRim = RobotMaterialFallback.CalculateRimColor(
            new Vector3(-4f, 0.5f, 5f),
            1f);
        Vector3 invalidRim = RobotMaterialFallback.CalculateRimColor(
            new Vector3(float.NaN, 1f, 1f),
            float.PositiveInfinity);

        Assert.InRange(baseRim.X, 0f, 1f);
        Assert.InRange(baseRim.Y, 0f, 1f);
        Assert.InRange(baseRim.Z, 0f, 1f);
        Assert.True(hitRim.X > baseRim.X);
        Assert.True(hitRim.Y > baseRim.Y);
        Assert.True(hitRim.Z >= baseRim.Z);
        AssertVectorNear(new Vector3(0.08f, 0.2f, 0.32f), invalidRim);
    }

    [Fact]
    public void ExpandedRimShellKeepsAuthoredGroundAnchorFixed()
    {
        EnemyVisualCalibration calibration = new()
        {
            SourceHeight = 2f,
            SourceGroundAnchor = new Vector3(3f, -1f, 4f),
            GroundOffset = 0.15f,
            ForwardYaw = 0.35f,
        };
        Matrix world = calibration.CreateWorld(
            new Vector3(8f, 2f, -6f),
            targetHeight: 4f,
            facingYaw: -0.2f);
        Matrix rimWorld = RobotMaterialFallback.CreateRimWorld(
            world,
            calibration.SourceGroundAnchor);

        Vector3 baseAnchor = Vector3.Transform(calibration.SourceGroundAnchor, world);
        Vector3 rimAnchor = Vector3.Transform(calibration.SourceGroundAnchor, rimWorld);
        Vector3 sourcePoint = calibration.SourceGroundAnchor + Vector3.UnitY;
        float baseRadius = Vector3.Distance(baseAnchor, Vector3.Transform(sourcePoint, world));
        float rimRadius = Vector3.Distance(rimAnchor, Vector3.Transform(sourcePoint, rimWorld));

        AssertVectorNear(baseAnchor, rimAnchor);
        Assert.Equal(
            baseRadius * RobotMaterialFallback.RimShellScale,
            rimRadius,
            precision: 4);
    }

    [Fact]
    public void StaticPresenterMultipliesTintWithoutErasingAuthoredMaterialColor()
    {
        Vector3 authored = new(0.8f, 0.35f, 0.1f);

        Vector3 untinted = StaticModelPresenter.ComposeMaterialColor(authored, null);
        Vector3 tinted = StaticModelPresenter.ComposeMaterialColor(authored, new Vector3(0.5f, 1f, 2f));

        AssertVectorNear(authored, untinted);
        AssertVectorNear(new Vector3(0.4f, 0.35f, 0.2f), tinted);
    }

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 4);
        Assert.Equal(expected.Y, actual.Y, precision: 4);
        Assert.Equal(expected.Z, actual.Z, precision: 4);
    }
}
