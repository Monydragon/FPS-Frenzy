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
    public void AdventureCheckpointBoonsRebuildRuntimeModifiers()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        AdventureDefinition adventure = catalog.Adventures["null-signal"];
        AdventureCheckpoint checkpoint = new()
        {
            AdventureId = adventure.Id,
            Seed = 424242,
            GeneratorVersion = adventure.GeneratorVersion,
            NextStageIndex = 1,
            FloorsCompleted = 1,
            BoonIds = ["reinforced-shell"],
            RunExperienceEarned = 450,
            RunLevelsGained = 1,
            RunProficiencyExperience = new Dictionary<WeaponFamily, int> { [WeaponFamily.Pulse] = 90 },
            RunCollectedItemIds = ["cache-item"],
            RunRarityTotals = new Dictionary<ItemRarity, int> { [ItemRarity.Rare] = 1 },
            RunHighestItemPower = 12,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Mode = GameMode.Adventure,
            AdventureId = adventure.Id,
            Seed = checkpoint.Seed,
            AdventureCheckpoint = checkpoint,
            StartingWeaponId = "pulse-sidearm",
        });

        Assert.Contains("reinforced-shell", simulation.OwnedUpgradeIds);
        Assert.Equal(120f, simulation.Player.MaximumHealth);
        Assert.Equal(20f, simulation.RunModifiers.MaximumHealthBonus);
        Assert.Equal(450, simulation.RunExperienceEarned);
        Assert.Equal(90, simulation.RunProficiencyExperience[WeaponFamily.Pulse]);
        Assert.Equal(1, simulation.RunEquipmentCollected);
        Assert.Equal(12, simulation.RunHighestItemPower);
        Assert.Contains("reinforced-shell", simulation.CreateAdventureCheckpoint()!.BoonIds);
    }

    [Fact]
    public void AdventureStageEntryFacesTheFirstConnectedRoute()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Mode = GameMode.Adventure,
            AdventureId = "null-signal",
            Seed = 424242,
            StartingWeaponId = "pulse-sidearm",
        });
        GeneratedDungeonFloor floor = Assert.IsType<GeneratedDungeonFloor>(simulation.GeneratedDungeonFloor);
        GeneratedDungeonRoom start = floor.Rooms[0];
        Vector3 route = Vector3.Normalize(floor.Rooms[start.Connections[0]].Center - start.Center);
        Vector3 view = simulation.GetViewDirection();
        view = Vector3.Normalize(new Vector3(view.X, 0f, view.Z));

        Assert.True(Vector3.Dot(route, view) > 0.99f);
    }

    [Fact]
    public void CoreWardenIsPresentButLockedUntilBothShieldControlsAreDisabled()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        AdventureDefinition adventure = catalog.Adventures["null-signal"];
        AdventureCheckpoint checkpoint = new()
        {
            AdventureId = adventure.Id,
            Seed = 424242,
            GeneratorVersion = adventure.GeneratorVersion,
            NextStageIndex = adventure.Floors.Count,
            FloorsCompleted = adventure.Floors.Count,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Mode = GameMode.Adventure,
            AdventureId = adventure.Id,
            Seed = checkpoint.Seed,
            AdventureCheckpoint = checkpoint,
            StartingWeaponId = "pulse-sidearm",
        });

        for (int tick = 0; tick < 180; tick++)
        {
            simulation.Step([]);
        }

        EnemyState boss = Assert.IsType<EnemyState>(simulation.ActiveBoss);
        Assert.Equal("core-warden", boss.Definition.Id);
        Assert.True(simulation.AdventureSnapshot!.BossInvulnerable);
        Assert.Equal(EnemyActionState.Idle, boss.ActionState);
        Assert.DoesNotContain(simulation.Projectiles, projectile => projectile.IsHostile);
        Assert.Equal(simulation.Player.MaximumHealth, simulation.Player.Health);
    }

    [Fact]
    public void BothCoreShieldControlsRemainContextActionsUntilTheBossUnlocks()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        AdventureDefinition adventure = catalog.Adventures["null-signal"];
        AdventureCheckpoint checkpoint = new()
        {
            AdventureId = adventure.Id,
            Seed = 424242,
            GeneratorVersion = adventure.GeneratorVersion,
            NextStageIndex = adventure.Floors.Count,
            FloorsCompleted = adventure.Floors.Count,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Mode = GameMode.Adventure,
            AdventureId = adventure.Id,
            Seed = checkpoint.Seed,
            AdventureCheckpoint = checkpoint,
            StartingWeaponId = "pulse-sidearm",
            GodModeEnabled = true,
        });

        simulation.Step([]);
        simulation.TeleportPlayerForTesting(new Vector3(-10f, 1.65f, 0f));
        Assert.Equal("core-shield-west", simulation.NearbyAdventureBossControlId);
        Assert.True(simulation.HasContextAction);
        Interact(simulation);

        Assert.Contains("core-shield-west", simulation.AdventureSnapshot!.CompletedInteractables);
        Assert.Null(simulation.NearbyAdventureBossControlId);

        simulation.TeleportPlayerForTesting(new Vector3(10f, 1.65f, 0f));
        Assert.Equal("core-shield-east", simulation.NearbyAdventureBossControlId);
        Assert.True(simulation.HasContextAction);
        Interact(simulation);

        Assert.Contains("core-shield-east", simulation.AdventureSnapshot!.CompletedInteractables);
        Assert.Equal(AdventureRunPhase.BossActive, simulation.AdventureSnapshot.Phase);
        while (simulation.AdventureStoryBeat is not null)
        {
            Interact(simulation);
        }
        Assert.False(simulation.HasContextAction);
    }

    [Fact]
    public void DifficultyTradesSupplyFrequencyAndAmountForRarityLuck()
    {
        DifficultyDefinition casual = DifficultyCatalog.Get(DifficultyMode.Casual);
        DifficultyDefinition normal = DifficultyCatalog.Get(DifficultyMode.Normal);
        DifficultyDefinition extreme = DifficultyCatalog.Get(DifficultyMode.Extreme);

        float casualHealthChance = DifficultyCatalog.ScaleHealthDropChance(casual.Mode, 0.20f);
        float normalHealthChance = DifficultyCatalog.ScaleHealthDropChance(normal.Mode, 0.20f);
        float extremeHealthChance = DifficultyCatalog.ScaleHealthDropChance(extreme.Mode, 0.20f);
        Assert.True(casualHealthChance > normalHealthChance && normalHealthChance > extremeHealthChance);

        float casualAmmoChance = DifficultyCatalog.ScaleAmmoDropChance(casual.Mode, casualHealthChance, 0.25f);
        float normalAmmoChance = DifficultyCatalog.ScaleAmmoDropChance(normal.Mode, normalHealthChance, 0.25f);
        float extremeAmmoChance = DifficultyCatalog.ScaleAmmoDropChance(extreme.Mode, extremeHealthChance, 0.25f);
        Assert.True(casualAmmoChance > normalAmmoChance && normalAmmoChance > extremeAmmoChance);
        Assert.True(DifficultyCatalog.ScaleSupplyAmount(casual.Mode, 20) >
            DifficultyCatalog.ScaleSupplyAmount(normal.Mode, 20));
        Assert.True(DifficultyCatalog.ScaleSupplyAmount(normal.Mode, 20) >
            DifficultyCatalog.ScaleSupplyAmount(extreme.Mode, 20));
        Assert.True(casual.RarityLuckBonus < normal.RarityLuckBonus &&
            normal.RarityLuckBonus < extreme.RarityLuckBonus);

        ContentCatalog catalog = LoadShippedCatalog();
        int casualRareDrops = Enumerable.Range(0, 2_000).Count(serial =>
            LootGenerator.Generate(424242, 100, 7, serial, ThreatTier.TierI, catalog,
                rarityLuck: casual.RarityLuckBonus).Rarity >= ItemRarity.Rare);
        int extremeRareDrops = Enumerable.Range(0, 2_000).Count(serial =>
            LootGenerator.Generate(424242, 100, 7, serial, ThreatTier.TierI, catalog,
                rarityLuck: extreme.RarityLuckBonus).Rarity >= ItemRarity.Rare);
        Assert.True(extremeRareDrops > casualRareDrops,
            $"Expected Extreme rarity luck to beat Casual ({extremeRareDrops} vs {casualRareDrops}).");
    }

    [Fact]
    public void CoreChamberFootprintStaysAuthoredWhileWallDressingVariesBySeed()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        AdventureDefinition adventure = catalog.Adventures["null-signal"];
        AdventureCheckpoint firstCheckpoint = new()
        {
            AdventureId = adventure.Id,
            Seed = 424242,
            GeneratorVersion = adventure.GeneratorVersion,
            NextStageIndex = adventure.Floors.Count,
            FloorsCompleted = adventure.Floors.Count,
        };
        AdventureCheckpoint secondCheckpoint = firstCheckpoint with { Seed = firstCheckpoint.Seed + 1 };
        using GameSimulation first = new(catalog, new RunConfiguration
        {
            Mode = GameMode.Adventure,
            AdventureId = adventure.Id,
            Seed = firstCheckpoint.Seed,
            AdventureCheckpoint = firstCheckpoint,
            StartingWeaponId = "pulse-sidearm",
        });
        using GameSimulation second = new(catalog, new RunConfiguration
        {
            Mode = GameMode.Adventure,
            AdventureId = adventure.Id,
            Seed = secondCheckpoint.Seed,
            AdventureCheckpoint = secondCheckpoint,
            StartingWeaponId = "pulse-sidearm",
        });

        Assert.Equal(first.Arena.BoundsMin, second.Arena.BoundsMin);
        Assert.Equal(first.Arena.BoundsMax, second.Arena.BoundsMax);
        Assert.NotEqual(
            first.Arena.Primitives.Where(item => item.Id.StartsWith("seed-panel", StringComparison.Ordinal))
                .Select(item => item.Position).ToArray(),
            second.Arena.Primitives.Where(item => item.Id.StartsWith("seed-panel", StringComparison.Ordinal))
                .Select(item => item.Position).ToArray());
        Assert.All(first.Arena.Primitives.Where(item => item.Id.StartsWith("seed-panel", StringComparison.Ordinal)),
            item =>
            {
                Assert.False(item.HasCollision);
                Assert.True(MathF.Abs(item.Position.Z) > 15.6f);
            });
    }

    [Fact]
    public void CompletingTheFloorObjectiveDisablesGateQueriesPhysicsAndNavigationTogether()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Mode = GameMode.Adventure,
            AdventureId = "null-signal",
            Seed = 424242,
            StartingWeaponId = "pulse-sidearm",
            GodModeEnabled = true,
        });
        GeneratedDungeonFloor floor = Assert.IsType<GeneratedDungeonFloor>(simulation.GeneratedDungeonFloor);
        GeneratedDungeonGate gate = Assert.Single(floor.Gates);
        Assert.Equal((true, true, true), simulation.GetAdventureGateState(gate.Id));

        while (simulation.AdventureStoryBeat is not null)
        {
            Interact();
        }

        DungeonFloorRecipe recipe = catalog.Adventures["null-signal"].Floors[0];
        foreach (AdventureObjectiveDefinition objective in recipe.Objectives)
        {
            foreach (GeneratedDungeonInteractable interactable in floor.Interactables
                .Where(item => item.ObjectiveId == objective.Id)
                .Take(objective.RequiredCount))
            {
                simulation.TeleportPlayerForTesting(interactable.Position);
                Interact();
            }

            if (objective.Id == gate.UnlockObjectiveId)
            {
                break;
            }
        }

        Assert.Equal((false, false, false), simulation.GetAdventureGateState(gate.Id));
        return;

        void Interact()
        {
            simulation.Step([new PlayerCommand(
                simulation.Tick + 1,
                simulation.Player.Id,
                Vector2.Zero,
                Vector2.Zero,
                PlayerButtons.Interact,
                -1)]);
            simulation.Step([]);
        }
    }

    [Fact]
    public void AdventureLiftAndBoonProgressThroughEveryFloorIntoTheCoreChamber()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        AdventureDefinition adventure = catalog.Adventures["null-signal"];
        AdventureCheckpoint? checkpoint = null;
        string[] boonIds = catalog.Upgrades.Keys.Order(StringComparer.Ordinal).Take(3).ToArray();

        for (int floorIndex = 0; floorIndex < adventure.Floors.Count; floorIndex++)
        {
            using GameSimulation simulation = new(catalog, new RunConfiguration
            {
                Mode = GameMode.Adventure,
                AdventureId = adventure.Id,
                Seed = 424242,
                AdventureCheckpoint = checkpoint,
                StartingWeaponId = "pulse-sidearm",
                GodModeEnabled = true,
            });
            GeneratedDungeonFloor floor = Assert.IsType<GeneratedDungeonFloor>(simulation.GeneratedDungeonFloor);
            DungeonFloorRecipe recipe = adventure.Floors[floorIndex];
            foreach (AdventureObjectiveDefinition objective in recipe.Objectives)
            {
                foreach (GeneratedDungeonInteractable interactable in floor.Interactables
                    .Where(item => item.ObjectiveId == objective.Id)
                    .Take(objective.RequiredCount))
                {
                    simulation.TeleportPlayerForTesting(interactable.Position);
                    Interact(simulation);
                }
            }

            Assert.All(simulation.AdventureSnapshot!.Objectives, objective => Assert.True(objective.Complete));
            GeneratedDungeonInteractable lift = Assert.Single(floor.Interactables,
                item => item.Kind == AdventureInteractableKind.Lift);
            simulation.TeleportPlayerForTesting(lift.Position);
            Assert.True(simulation.CanInteractWithAdventure(lift.Id));
            Interact(simulation);
            Assert.Equal(AdventureRunPhase.FloorReward, simulation.AdventureSnapshot!.Phase);

            checkpoint = simulation.ChooseAdventureBoon(boonIds[floorIndex]);
            Assert.Equal(floorIndex + 1, checkpoint.NextStageIndex);
            Assert.Equal(floorIndex + 1, checkpoint.FloorsCompleted);
        }

        using GameSimulation bossStage = new(catalog, new RunConfiguration
        {
            Mode = GameMode.Adventure,
            AdventureId = adventure.Id,
            Seed = 424242,
            AdventureCheckpoint = checkpoint,
            StartingWeaponId = "pulse-sidearm",
            GodModeEnabled = true,
        });
        Assert.Null(bossStage.GeneratedDungeonFloor);
        Assert.Equal(AdventureStageKind.Boss, bossStage.AdventureSnapshot!.StageKind);
        Assert.Equal(AdventureRunPhase.BossLocked, bossStage.AdventureSnapshot.Phase);
    }

    [Fact]
    public void AdventureChestsGuaranteeWeaponExperimentsForEmptyQuickbarSlots()
    {
        ContentCatalog catalog = LoadShippedCatalog();
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Mode = GameMode.Adventure,
            AdventureId = "null-signal",
            Seed = 424242,
            StartingWeaponId = "pulse-sidearm",
            GodModeEnabled = true,
        });
        GeneratedDungeonFloor floor = Assert.IsType<GeneratedDungeonFloor>(simulation.GeneratedDungeonFloor);
        GeneratedDungeonInteractable chest = floor.Interactables.First(item =>
            item.Kind == AdventureInteractableKind.EquipmentCache);
        int populatedBefore = simulation.PopulatedWeaponSlots.Count(populated => populated);

        simulation.TeleportPlayerForTesting(chest.Position);
        Assert.True(simulation.HasContextAction);
        Interact(simulation);

        Assert.Contains(chest.Id, simulation.AdventureSnapshot!.CompletedInteractables);
        Assert.True(simulation.PopulatedWeaponSlots.Count(populated => populated) >= populatedBefore + 2);
        Assert.True(simulation.PendingProgression.Equipment.Count(item => item.IsWeapon) >= 2);
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

    private static void Interact(GameSimulation simulation)
    {
        simulation.Step([new PlayerCommand(
            simulation.Tick + 1,
            simulation.Player.Id,
            Vector2.Zero,
            Vector2.Zero,
            PlayerButtons.Interact,
            -1)]);
        simulation.Step([]);
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
