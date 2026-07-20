using System.Numerics;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Core.Tests;

public sealed class WeaponPresentationMathTests
{
    [Fact]
    public void RecoilIsTrackedIndependentlyForEachHand()
    {
        WeaponState right = new(CreateWeapon());
        WeaponState left = new(CreateWeapon() with { Id = "left-pistol" });

        Assert.True(right.TryFire());
        right.Tick(0.035f);
        left.Tick(0.035f);

        WeaponPresentationPose rightPose = Pose(right, isLeftHand: false, isDualWielding: true);
        WeaponPresentationPose leftPose = Pose(left, isLeftHand: true, isDualWielding: true);

        Assert.True(rightPose.FireKick > 0.9f);
        Assert.Equal(0f, leftPose.FireKick);
        Assert.True(rightPose.Position.Z > leftPose.Position.Z);
    }

    [Fact]
    public void ReloadArcUsesTheActualModifiedReloadDuration()
    {
        WeaponState weapon = new(CreateWeapon());
        weapon.SetPersistentModifiers(1f, 1f, 0.8f, 1f, refill: false);
        Assert.True(weapon.TryFire());
        weapon.Tick(0.2f);
        weapon.BeginReload(durationMultiplier: 1.15f);

        float expectedDuration = 1.4f * 0.8f * 1.15f;
        Assert.Equal(expectedDuration, weapon.ReloadDurationSeconds, precision: 4);
        weapon.Tick(expectedDuration * 0.5f);

        WeaponPresentationPose pose = Pose(weapon, isLeftHand: false, isDualWielding: false);
        Assert.InRange(weapon.ReloadProgress, 0.499f, 0.501f);
        Assert.True(pose.ReloadArc > 0.99f);
    }

    [Fact]
    public void PresentationIsFrameRateIndependent()
    {
        WeaponState atThirty = new(CreateWeapon());
        WeaponState atSixty = new(CreateWeapon());
        Assert.True(atThirty.TryFire());
        Assert.True(atSixty.TryFire());

        for (int frame = 0; frame < 3; frame++)
        {
            atThirty.Tick(1f / 30f);
        }

        for (int frame = 0; frame < 6; frame++)
        {
            atSixty.Tick(1f / 60f);
        }

        WeaponPresentationPose thirtyPose = Pose(atThirty, false, false);
        WeaponPresentationPose sixtyPose = Pose(atSixty, false, false);
        Assert.InRange(Vector3.Distance(thirtyPose.Position, sixtyPose.Position), 0f, 0.00001f);
        Assert.InRange(MathF.Abs(thirtyPose.Pitch - sixtyPose.Pitch), 0f, 0.00001f);
    }

    [Fact]
    public void DualWieldAimKeepsPistolsOnOppositeSides()
    {
        WeaponState right = new(CreateWeapon());
        WeaponState left = new(CreateWeapon() with { Id = "left-pistol" });

        WeaponPresentationPose rightPose = WeaponPresentationMath.CalculatePose(
            right, false, true, 1f, 1f, 1f, 0f, 1f);
        WeaponPresentationPose leftPose = WeaponPresentationMath.CalculatePose(
            left, true, true, 1f, 1f, 1f, 0f, 1f);

        Assert.True(rightPose.Position.X >= 0.105f);
        Assert.True(leftPose.Position.X <= -0.105f);
    }

    [Fact]
    public void AuthoredMuzzleAxisPointsAwayFromTheCamera()
    {
        WeaponState weapon = new(CreateWeapon());
        WeaponPresentationPose pose = Pose(weapon, false, false);

        Vector3 muzzleDirection = Vector3.TransformNormal(
            Vector3.UnitX,
            Matrix4x4.CreateRotationY(pose.Yaw));

        Assert.True(muzzleDirection.Z < -0.9f,
            $"Expected the +X muzzle axis to point down camera -Z, but got {muzzleDirection}.");
    }

    [Fact]
    public void AllReleaseWeaponsAlignMuzzleAndSightWithTheFocusRay()
    {
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
        Assert.Equal(50, catalog.WeaponVisualCalibrations.Count);
        foreach (WeaponDefinition definition in catalog.Weapons.Values)
        {
            WeaponState weapon = new(definition);
            WeaponPresentationPose pose = WeaponPresentationMath.CalculatePose(
                weapon, isLeftHand: false, isDualWielding: false, adsBlend: 1f,
                equipSeconds: 2f, elapsedSimulationSeconds: 0f, movementBlend: 0f, cameraBobScale: 0f);
            Vector3 sight = WeaponPresentationMath.CalculateCalibrationAnchorPosition(
                definition, pose, definition.Visual.SightAnchor);
            Vector3 barrel = WeaponPresentationMath.CalculateCalibrationAnchorPosition(
                definition, pose, definition.Visual.BarrelTip);
            Vector3 rear = WeaponPresentationMath.CalculateCalibrationAnchorPosition(
                definition, pose, definition.Visual.RearAnchor);
            Vector3 muzzleDirection = WeaponPresentationMath.CalculateMuzzleDirection(definition, pose);

            Assert.InRange(MathF.Abs(sight.X), 0f, 0.0001f);
            Assert.InRange(MathF.Abs(sight.Y), 0f, 0.0001f);
            Assert.True(Vector3.Dot(muzzleDirection, -Vector3.UnitZ) >= MathF.Cos(MathF.PI / 180f),
                $"{definition.Id} muzzle direction was {muzzleDirection}.");
            Assert.True(barrel.Z < rear.Z - 0.025f,
                $"{definition.Id} barrel {barrel} was not ahead of rear anchor {rear}.");
            Assert.True(rear.Z < -0.05f, $"{definition.Id} intersects the camera near plane.");
        }
    }

    private static WeaponPresentationPose Pose(
        WeaponState weapon,
        bool isLeftHand,
        bool isDualWielding) =>
        WeaponPresentationMath.CalculatePose(
            weapon,
            isLeftHand,
            isDualWielding,
            adsBlend: 0f,
            equipSeconds: 1f,
            elapsedSimulationSeconds: 1f,
            movementBlend: 0f,
            cameraBobScale: 1f);

    private static WeaponDefinition CreateWeapon() => new()
    {
        Id = "test-pistol",
        DisplayName = "Test Pistol",
        ModelAsset = "Models/Weapons/test",
        Family = WeaponFamily.Pulse,
        Handedness = Handedness.OneHanded,
        AmmoMode = AmmoMode.MagazineReserve,
        ShotMode = ShotMode.Hitscan,
        TriggerMode = TriggerMode.SemiAutomatic,
        Damage = 10f,
        FireIntervalSeconds = 0.2f,
        MagazineSize = 12,
        ReserveCapacity = 48,
        ReloadSeconds = 1.4f,
        RecoilKick = 1f,
        ViewModelHipOffset = new Vector3(0.3f, -0.3f, -0.62f),
        ViewModelAdsOffset = new Vector3(0.05f, -0.21f, -0.54f),
        ViewModelTargetSpan = 0.46f,
        ViewModelYawDegrees = 108f,
    };
}
