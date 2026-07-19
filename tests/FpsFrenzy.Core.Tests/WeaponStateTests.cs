using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Core.Tests;

public sealed class WeaponStateTests
{
    [Fact]
    public void MagazineWeaponConsumesAndReloadsAFullMagazine()
    {
        WeaponState weapon = new(CreateWeapon(AmmoMode.MagazineReserve));

        for (int shot = 0; shot < 24; shot++)
        {
            Assert.True(weapon.TryFire());
            weapon.Tick(0.1f);
        }

        Assert.Equal(0, weapon.Magazine);
        Assert.False(weapon.TryFire());
        Assert.True(weapon.IsReloading);
        weapon.Tick(1.5f);
        Assert.Equal(24, weapon.Magazine);
        Assert.Equal(96, weapon.Reserve);
    }

    [Fact]
    public void HeatWeaponBlocksAtMaximumAndCools()
    {
        WeaponState weapon = new(CreateWeapon(AmmoMode.Heat) with
        {
            HeatPerShot = 0.6f,
            HeatDissipationPerSecond = 0.4f,
        });

        Assert.True(weapon.TryFire());
        weapon.Tick(0.1f);
        Assert.False(weapon.TryFire());
        weapon.Tick(1f);
        Assert.True(weapon.TryFire());
    }

    [Fact]
    public void EnergyWeaponRegeneratesAndDuplicateAmmoRefillsIt()
    {
        WeaponState weapon = new(CreateWeapon(AmmoMode.RegeneratingEnergy) with
        {
            EnergyCapacity = 10f,
            EnergyPerShot = 6f,
            EnergyRegenerationPerSecond = 2f,
        });

        Assert.True(weapon.TryFire());
        weapon.Tick(0.1f);
        Assert.False(weapon.TryFire());
        weapon.AddAmmo(10);
        Assert.Equal(10f, weapon.Energy);
        Assert.True(weapon.TryFire());
    }

    [Fact]
    public void BurstWeaponQueuesThreeShotsAndAppliesRecoveryAfterTheLastRound()
    {
        WeaponState weapon = new(CreateWeapon(AmmoMode.MagazineReserve) with
        {
            TriggerMode = TriggerMode.Burst,
            BurstCount = 3,
            BurstRecoverySeconds = 0.3f,
        });

        weapon.StartBurst();
        for (int shot = 0; shot < 3; shot++)
        {
            Assert.True(weapon.TryFire());
            weapon.CompleteBurstShot();
            if (shot < 2)
            {
                weapon.Tick(0.05f);
            }
        }

        Assert.Equal(0, weapon.BurstShotsRemaining);
        Assert.InRange(weapon.FireCooldownSeconds, 0.299f, 0.301f);
        Assert.Equal(21, weapon.Magazine);
    }

    [Fact]
    public void HeatLockoutRequiresCoolingBelowRecoveryThreshold()
    {
        WeaponState weapon = new(CreateWeapon(AmmoMode.Heat) with
        {
            HeatPerShot = 0.7f,
            HeatDissipationPerSecond = 0.2f,
        });

        Assert.True(weapon.TryFire());
        weapon.Tick(0.05f);
        Assert.False(weapon.TryFire());
        Assert.True(weapon.IsOverheated);
        weapon.Tick(1f);
        Assert.False(weapon.TryFire());
        weapon.Tick(1f);
        Assert.True(weapon.TryFire());
    }

    private static WeaponDefinition CreateWeapon(AmmoMode ammoMode) => new()
    {
        Id = "test-weapon",
        DisplayName = "Test Weapon",
        ModelAsset = "Models/Weapons/test",
        AmmoMode = ammoMode,
        ShotMode = ShotMode.Hitscan,
        Damage = 10f,
        FireIntervalSeconds = 0.05f,
        MagazineSize = 24,
        ReserveCapacity = 120,
        ReloadSeconds = 1.4f,
    };
}
