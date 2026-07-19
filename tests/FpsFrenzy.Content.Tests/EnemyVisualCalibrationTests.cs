using FpsFrenzy.Kni.Rendering;
using Microsoft.Xna.Framework;

namespace FpsFrenzy.Content.Tests;

public sealed class EnemyVisualCalibrationTests
{
    [Fact]
    public void CreateWorldPlacesAuthoredGroundAnchorAtGroundOffset()
    {
        EnemyVisualCalibration calibration = new()
        {
            SourceHeight = 2f,
            SourceGroundAnchor = new Vector3(3f, -1f, 4f),
            GroundOffset = 0.15f,
            ForwardYaw = MathHelper.PiOver2,
            HealthBarAnchor = new Vector3(3f, 1.5f, 4f),
        };
        Vector3 groundPosition = new(8f, 2f, -6f);

        Matrix world = calibration.CreateWorld(groundPosition, targetHeight: 4f, facingYaw: 0f);
        Vector3 transformedGroundAnchor = Vector3.Transform(calibration.SourceGroundAnchor, world);
        Vector3 healthBarPosition = calibration.GetHealthBarWorldPosition(
            groundPosition,
            targetHeight: 4f,
            facingYaw: 0f);

        AssertVectorNear(groundPosition + new Vector3(0f, 0.15f, 0f), transformedGroundAnchor);
        Assert.Equal(5f, healthBarPosition.Y - transformedGroundAnchor.Y, precision: 3);
    }

    [Fact]
    public void CreateWorldRejectsInvalidAuthoredScale()
    {
        EnemyVisualCalibration calibration = new() { SourceHeight = 0f };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calibration.CreateWorld(Vector3.Zero, targetHeight: 1f, facingYaw: 0f));
    }

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 4);
        Assert.Equal(expected.Y, actual.Y, precision: 4);
        Assert.Equal(expected.Z, actual.Z, precision: 4);
    }
}
