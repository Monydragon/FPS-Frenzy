using System.Numerics;
using System.Text;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Input;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Core.Tests;

public sealed class SimulationTests
{
    [Fact]
    public void FixedStepAdvancesExactlySixtyTicksPerSecond()
    {
        using GameSimulation simulation = CreateSimulation(LoadShippedCatalog());
        for (int tick = 0; tick < 60; tick++)
        {
            simulation.Step([]);
        }

        Assert.Equal(60u, simulation.Tick);
        Assert.InRange(simulation.ElapsedRunSeconds, 0.999f, 1.001f);
        Assert.InRange(simulation.Player.Position.Y, 1.62f, 1.72f);
    }

    [Fact]
    public void CommandCarriesStableIdentityAndTickedInput()
    {
        PlayerCommand command = new(
            42,
            new EntityId(7),
            new Vector2(0.5f, -0.25f),
            new Vector2(0.1f, -0.2f),
            PlayerButtons.Fire | PlayerButtons.AimDownSights,
            3);

        Assert.Equal(42u, command.Tick);
        Assert.Equal(7, command.PlayerId.Value);
        Assert.True(command.Has(PlayerButtons.Fire));
        Assert.True(command.Has(PlayerButtons.AimDownSights));
        Assert.Equal(3, command.WeaponSlot);
    }

    [Fact]
    public void PauseUsesAnEdgeAndStopsSimulationTicks()
    {
        using GameSimulation simulation = CreateSimulation(LoadShippedCatalog());
        PlayerCommand pause = new(1, simulation.Player.Id, Vector2.Zero, Vector2.Zero, PlayerButtons.Pause, -1);

        simulation.Step([pause]);
        simulation.Step([pause]);

        Assert.Equal(GamePhase.Paused, simulation.Phase);
        Assert.Equal(0u, simulation.Tick);
    }

    [Theory]
    [InlineData(ShotMode.Hitscan)]
    [InlineData(ShotMode.Projectile)]
    public void DamageKillWaveCompletionAndCleanupRunEndToEnd(ShotMode shotMode)
    {
        using GameSimulation simulation = CreateSimulation(
            CreateCombatCatalog(shotMode, includeWeaponPickup: false));
        for (int tick = 0; tick < 100; tick++)
        {
            simulation.Step([]);
        }

        Assert.Single(simulation.Enemies);
        PlayerCommand fire = new(
            simulation.Tick + 1,
            simulation.Player.Id,
            Vector2.Zero,
            new Vector2(0f, -0.09f),
            PlayerButtons.Fire,
            -1);
        simulation.Step([fire]);
        Assert.True(simulation.LastShotSeconds < 0.01f,
            $"Weapon did not fire; phase={simulation.Phase}, ammo={simulation.Player.CurrentWeapon.Magazine}.");
        if (shotMode == ShotMode.Hitscan)
        {
            EnemyState target = simulation.Enemies[0];
            Assert.True(target.IsDead,
                $"Hitscan missed; player={simulation.Player.Position}, target={target.Position}, pitch={simulation.Player.Pitch}.");
        }
        else
        {
            Assert.NotEmpty(simulation.Projectiles);
        }

        for (int tick = 0; tick < 120 && simulation.Phase == GamePhase.Playing; tick++)
        {
            simulation.Step([]);
        }

        Assert.Equal(1, simulation.Kills);
        Assert.Equal(GamePhase.Victory, simulation.Phase);
        Assert.True(simulation.Score > 100);

        for (int tick = 0; tick < 70; tick++)
        {
            simulation.Step([]);
        }

        Assert.Empty(simulation.Enemies);
    }

    [Fact]
    public void WeaponPickupAddsToRunInventoryAndPersistsAcrossTicks()
    {
        using GameSimulation simulation = CreateSimulation(
            CreateCombatCatalog(ShotMode.Hitscan, includeWeaponPickup: true));

        simulation.Step([]);
        simulation.Step([]);

        Assert.Equal(2, simulation.Player.Weapons.Count);
        Assert.Equal("burst-carbine", simulation.Player.CurrentWeapon.Definition.Id);
    }

    [Fact]
    public void DuplicateWeaponPickupUsesItsAuthoredRefillAmount()
    {
        using GameSimulation simulation = CreateSimulation(
            CreateCombatCatalog(ShotMode.Hitscan, includeWeaponPickup: true),
            startingWeaponId: "burst-carbine");
        WeaponState weapon = simulation.Player.CurrentWeapon;
        for (int shot = 0; shot < 10; shot++)
        {
            Assert.True(weapon.TryFire());
            weapon.Tick(0.11f);
        }

        weapon.BeginReload();
        weapon.Tick(1.1f);
        Assert.Equal(170, weapon.Reserve);

        simulation.Step([]);

        Assert.Equal(177, weapon.Reserve);
        Assert.False(Assert.Single(simulation.Pickups).IsAvailable);
    }

    [Fact]
    public void NavigationFindsAPathAroundGeneratedCover()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        NavigationGrid navigation = new(catalog.Arenas["training-ring"]);

        List<Vector3> path = navigation.FindPath(new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f));

        Assert.NotEmpty(path);
        Assert.True(path.Count > 20);
    }

    [Fact]
    public void SemiAutomaticSidearmFiresOnceUntilTheTriggerIsReleased()
    {
        using GameSimulation simulation = CreateSimulation(LoadShippedCatalog());
        PlayerCommand held = new(1, simulation.Player.Id, Vector2.Zero, Vector2.Zero, PlayerButtons.Fire, -1);
        int fired = 0;
        for (int tick = 0; tick < 30; tick++)
        {
            simulation.Step([held with { Tick = simulation.Tick + 1 }]);
            fired += simulation.CombatEvents.Count(combatEvent => combatEvent.Type == CombatEventType.WeaponFired);
        }

        Assert.Equal(1, fired);
        simulation.Step([]);
        simulation.Step([held with { Tick = simulation.Tick + 1 }]);
        Assert.Contains(simulation.CombatEvents, combatEvent => combatEvent.Type == CombatEventType.WeaponFired);
    }

    [Fact]
    public void WeaponMuzzleFollowsThePlayersLookDirection()
    {
        using GameSimulation simulation = CreateSimulation(LoadShippedCatalog());
        Vector3 initialMuzzle = simulation.GetWeaponMuzzlePosition();

        PlayerCommand look = new(
            simulation.Tick + 1,
            simulation.Player.Id,
            Vector2.Zero,
            new Vector2(MathF.PI * 0.5f, 0.32f),
            PlayerButtons.None,
            -1);
        simulation.Step([look]);

        Vector3 muzzleOffset = simulation.GetWeaponMuzzlePosition() - simulation.Player.Position;
        Assert.True(Vector3.Distance(initialMuzzle, simulation.GetWeaponMuzzlePosition()) > 0.35f);
        Assert.InRange(muzzleOffset.Length(), 0.4f, 1.1f);
        Assert.True(Vector3.Dot(Vector3.Normalize(muzzleOffset), simulation.GetViewDirection()) > 0.7f);
    }

    [Fact]
    public void ProjectileStartsAtTheMuzzleAndConvergesOnTheReticleRay()
    {
        using GameSimulation simulation = CreateSimulation(
            LoadShippedCatalog(),
            startingWeaponId: "plasma-launcher");
        PlayerCommand fire = new(
            simulation.Tick + 1,
            simulation.Player.Id,
            Vector2.Zero,
            Vector2.Zero,
            PlayerButtons.Fire,
            -1);
        simulation.Step([fire]);

        ProjectileState projectile = Assert.Single(simulation.Projectiles);
        CombatEvent fired = Assert.Single(simulation.CombatEvents, combatEvent =>
            combatEvent.Type == CombatEventType.WeaponFired);
        Vector3 expectedDirection = Vector3.Normalize(
            simulation.Player.Position +
            (simulation.GetViewDirection() * simulation.Player.CurrentWeapon.Definition.Range) -
            fired.Position);
        Assert.True(Vector3.Distance(projectile.PreviousPosition, fired.Position) < 0.001f);
        Assert.True(Vector3.Dot(Vector3.Normalize(projectile.Velocity), expectedDirection) > 0.999f);
        Assert.True(Vector3.Distance(fired.Position, simulation.GetWeaponMuzzlePosition()) < 0.001f);
    }

    [Fact]
    public void HitscanKeepsReticleAccuracyWhileVisualFeedbackStartsAtTheMuzzle()
    {
        using GameSimulation simulation = CreateSimulation(
            CreateCombatCatalog(ShotMode.Hitscan, includeWeaponPickup: false));
        for (int tick = 0; tick < 100; tick++)
        {
            simulation.Step([]);
        }

        PlayerCommand look = new(
            simulation.Tick + 1,
            simulation.Player.Id,
            Vector2.Zero,
            new Vector2(0f, -0.09f),
            PlayerButtons.None,
            -1);
        simulation.Step([look]);
        Vector3 muzzle = simulation.GetWeaponMuzzlePosition();
        PlayerCommand fire = look with
        {
            Tick = simulation.Tick + 1,
            LookDelta = Vector2.Zero,
            Buttons = PlayerButtons.Fire,
        };
        simulation.Step([fire]);

        Assert.True(simulation.Enemies[0].IsDead);
        Assert.Contains(simulation.CombatEvents, combatEvent =>
            combatEvent.Type == CombatEventType.EnemyHit &&
            Vector3.Distance(combatEvent.SecondaryPosition, muzzle) < 0.001f);
    }

    [Fact]
    public void OrbitalDepotMovementStaysFiniteAndInsideArenaBoundsUnderCombatLoad()
    {
        using GameSimulation simulation = CreateSimulation(LoadShippedCatalog(), "orbital-depot");
        for (int tick = 0; tick < 900 && simulation.Phase == GamePhase.Playing; tick++)
        {
            float strafe = ((tick / 120) & 1) == 0 ? 0.75f : -0.75f;
            PlayerCommand command = new(
                simulation.Tick + 1,
                simulation.Player.Id,
                new Vector2(strafe, 0.45f),
                new Vector2(0.002f, 0f),
                PlayerButtons.None,
                -1);
            simulation.Step([command]);

            Assert.True(float.IsFinite(simulation.Player.Position.X));
            Assert.InRange(simulation.Player.Position.X, simulation.Arena.BoundsMin.X, simulation.Arena.BoundsMax.X);
            Assert.InRange(simulation.Player.Position.Z, simulation.Arena.BoundsMin.Z, simulation.Arena.BoundsMax.Z);
            Assert.All(simulation.Enemies, enemy =>
            {
                Assert.True(float.IsFinite(enemy.Position.X) && float.IsFinite(enemy.Position.Z));
                Assert.InRange(enemy.Position.X, simulation.Arena.BoundsMin.X, simulation.Arena.BoundsMax.X);
                Assert.InRange(enemy.Position.Z, simulation.Arena.BoundsMin.Z, simulation.Arena.BoundsMax.Z);
            });
        }
    }

    [Fact]
    public void GodModePreventsIncomingDamageWithoutChangingCombatDifficulty()
    {
        ContentCatalog catalog = CreateCombatCatalog(
            ShotMode.Hitscan,
            includeWeaponPickup: false,
            enemyAttackRange: 10f,
            enemyAttackDamage: 60f);
        using GameSimulation simulation = CreateSimulation(catalog);

        for (int tick = 0; tick < 180 && simulation.Player.Health == simulation.Player.MaximumHealth; tick++)
        {
            simulation.Step([]);
        }

        float damagedHealth = simulation.Player.Health;
        Assert.True(damagedHealth < simulation.Player.MaximumHealth);
        simulation.SetPlayerInvulnerable(true);
        Assert.Equal(damagedHealth, simulation.Player.Health);
        for (int tick = 0; tick < 120; tick++)
        {
            simulation.Step([]);
        }

        Assert.Equal(damagedHealth, simulation.Player.Health);
        simulation.SetPlayerInvulnerable(false);
        for (int tick = 0; tick < 120 && simulation.Player.Health == damagedHealth; tick++)
        {
            simulation.Step([]);
        }

        Assert.True(simulation.Player.Health < damagedHealth);
        Assert.Equal(DifficultyMode.Normal, simulation.Difficulty);
    }

    [Fact]
    public void OrbitalDepotStandardProfileKeepsWeaponsAndEnemiesInAuthoredBands()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        WaveSetDefinition waves = catalog.WaveSets[catalog.Arenas["orbital-depot"].WaveSetId];

        Assert.Equal(DifficultyMode.Normal, waves.Difficulty);
        Assert.All(catalog.Weapons.Values, weapon =>
        {
            float effectiveDamagePerSecond = EffectiveDamagePerSecond(weapon);
            Assert.InRange(effectiveDamagePerSecond, 70f, 120f);
            Assert.InRange(weapon.Range, 30f, 120f);
        });
        Assert.All(catalog.Enemies.Values.Where(enemy => !enemy.IsBoss), enemy =>
        {
            Assert.InRange(enemy.MaxHealth, 40f, enemy.SchemaVersion >= 2 ? 260f : 200f);
            Assert.InRange(enemy.AttackDamage, 5f, 30f);
            Assert.InRange(enemy.AttackCooldownSeconds, 0.75f, 3f);
            Assert.InRange(enemy.AttackDamage / enemy.AttackCooldownSeconds, 3f, 12f);
        });
        Assert.True(waves.Waves.Count >= 10);
        Assert.NotNull(waves.BossWave);
    }

    private static float EffectiveDamagePerSecond(WeaponDefinition weapon)
    {
        float damage = weapon.Damage * weapon.PelletCount;
        if (weapon.TriggerMode != TriggerMode.Burst)
        {
            return damage / weapon.FireIntervalSeconds;
        }

        float cycleSeconds = ((weapon.BurstCount - 1) * weapon.FireIntervalSeconds) + weapon.BurstRecoverySeconds;
        return damage * weapon.BurstCount / cycleSeconds;
    }

    private static GameSimulation CreateSimulation(
        ContentCatalog catalog,
        string arenaId = "training-ring",
        string startingWeaponId = "pulse-sidearm") => new(catalog, new RunConfiguration
        {
            ArenaId = arenaId,
            StartingWeaponId = startingWeaponId,
        });

    private static ContentCatalog LoadShippedCatalog() =>
        ContentCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "Data"));

    private static ContentCatalog CreateCombatCatalog(
        ShotMode shotMode,
        bool includeWeaponPickup,
        float enemyAttackRange = 0.5f,
        float enemyAttackDamage = 1f)
    {
        string pulse = $$$"""
            {
              "id":"pulse-sidearm","displayName":"Pulse","modelAsset":"Models/Weapons/blaster-a",
              "ammoMode":"MagazineReserve","shotMode":"{{{shotMode}}}","damage":100,
              "fireIntervalSeconds":0.05,"magazineSize":24,"reserveCapacity":120,"reloadSeconds":1,
              "projectileSpeed":30,"projectileRadius":0.2,"range":80,
              "hipFieldOfViewDegrees":80,"adsFieldOfViewDegrees":55
            }
            """;
        const string burst = """
            {
              "id":"burst-carbine","displayName":"Burst","modelAsset":"Models/Weapons/blaster-c",
              "ammoMode":"MagazineReserve","shotMode":"Hitscan","damage":20,
              "fireIntervalSeconds":0.1,"magazineSize":30,"reserveCapacity":180,"reloadSeconds":1,
              "hipFieldOfViewDegrees":80,"adsFieldOfViewDegrees":55
            }
            """;
        string enemy = $$$"""
            {
              "id":"alien-grunt","displayName":"Alien","modelAsset":"Models/Enemies/Alien",
              "animationClips":{"idle":"Idle","walk":"Walk","attack":"Attack","hit":"Hit","death":"Death"},
              "maxHealth":100,"moveSpeed":0.01,"colliderRadius":0.55,"colliderHeight":1.4,
              "attackRange":{{{enemyAttackRange}}},"attackDamage":{{{enemyAttackDamage}}},"attackCooldownSeconds":1,"scoreValue":100
            }
            """;
        string pickups = includeWeaponPickup
            ? "[{\"type\":\"Weapon\",\"position\":{\"x\":0,\"y\":1.65,\"z\":0},\"amount\":7,\"weaponId\":\"burst-carbine\",\"respawnSeconds\":30}]"
            : "[]";
        string arena = $$$"""
            {
              "id":"training-ring","displayName":"Test","waveSetId":"training-waves",
              "boundsMin":{"x":-10,"y":0,"z":-10},"boundsMax":{"x":10,"y":5,"z":10},
              "playerSpawn":{"x":0,"y":1.65,"z":0},"navigationCellSize":0.5,
              "enemySpawns":[{"x":0,"y":0.7,"z":-5}],
              "primitives":[{"id":"floor","position":{"x":0,"y":-0.5,"z":0},"size":{"x":20,"y":1,"z":20}}],
              "pickupSpawns":{{{pickups}}}
            }
            """;
        const string waves = """
            {
              "id":"training-waves","interWaveDelaySeconds":0.01,
              "waves":[{"id":"wave-1","maximumConcurrentEnemies":1,"spawnIntervalSeconds":0.01,
              "spawnGroups":[{"enemyId":"alien-grunt","count":1}]}]
            }
            """;

        using Stream pulseStream = Json(pulse);
        using Stream burstStream = Json(burst);
        using Stream enemyStream = Json(enemy);
        using Stream arenaStream = Json(arena);
        using Stream waveStream = Json(waves);
        return ContentCatalog.Load([pulseStream, burstStream], [enemyStream], [arenaStream], [waveStream]);
    }

    private static MemoryStream Json(string value) => new(Encoding.UTF8.GetBytes(value));
}
