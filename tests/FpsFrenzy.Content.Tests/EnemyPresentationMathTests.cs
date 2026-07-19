using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Rendering;
using System.Numerics;

namespace FpsFrenzy.Content.Tests;

public sealed class EnemyPresentationMathTests
{
    [Fact]
    public void LocomotionUsesMeasuredHorizontalSpeedAndIgnoresVerticalOffsets()
    {
        float speed = EnemyPresentationMath.MeasureHorizontalSpeed(
            new Vector3(1f, 8f, 2f),
            new Vector3(1.3f, -4f, 2.4f),
            0.1f);

        Assert.Equal(5f, speed, precision: 4);
    }

    [Fact]
    public void LocomotionLatchStopsRunInPlaceAndUsesHysteresis()
    {
        Assert.False(EnemyPresentationMath.UpdateLocomotionLatch(
            false,
            EnemyActionState.Locomotion,
            0f));
        Assert.True(EnemyPresentationMath.UpdateLocomotionLatch(
            false,
            EnemyActionState.Locomotion,
            EnemyPresentationMath.LocomotionEnterSpeed));
        Assert.True(EnemyPresentationMath.UpdateLocomotionLatch(
            true,
            EnemyActionState.Locomotion,
            0.1f));
        Assert.False(EnemyPresentationMath.UpdateLocomotionLatch(
            true,
            EnemyActionState.Locomotion,
            0.02f));
        Assert.False(EnemyPresentationMath.UpdateLocomotionLatch(
            true,
            EnemyActionState.Windup,
            5f));
    }

    [Theory]
    [InlineData(4f, 4f, 1f)]
    [InlineData(8f, 4f, 1.45f)]
    [InlineData(0.2f, 4f, 0.65f)]
    public void LocomotionPlaybackScaleTracksMovementWithinSafeLimits(
        float measuredSpeed,
        float authoredSpeed,
        float expected)
    {
        Assert.Equal(
            expected,
            EnemyPresentationMath.GetLocomotionPlaybackScale(measuredSpeed, authoredSpeed),
            precision: 4);
    }

    [Fact]
    public void ChargingPlaybackUsesChargeSpeedInsteadOfWalkSpeed()
    {
        float authoredSpeed = EnemyPresentationMath.GetAuthoredLocomotionSpeed(
            EnemyActionState.Charging,
            moveSpeed: 2.6f,
            chargeSpeed: 11.5f);

        Assert.Equal(11.5f, authoredSpeed);
        Assert.Equal(
            1f,
            EnemyPresentationMath.GetLocomotionPlaybackScale(11.5f, authoredSpeed),
            precision: 4);
    }

    [Fact]
    public void StandardLocomotionPlaybackUsesMoveSpeed()
    {
        Assert.Equal(
            2.6f,
            EnemyPresentationMath.GetAuthoredLocomotionSpeed(
                EnemyActionState.Locomotion,
                moveSpeed: 2.6f,
                chargeSpeed: 11.5f));
    }
}
