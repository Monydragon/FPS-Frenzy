using System.Numerics;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Input;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Core.Tests;

public sealed class RogueliteTests
{
    private static readonly int[] ExpectedPurgePressureWaves = [8, 12, 14];
    private static readonly int[] ExpectedElitePressureWaves = [2, 3, 4];

    [Fact]
    public void StandardCatalogContainsEighteenNonStackingUpgradesAndTwelveStartingOptions()
    {
        Assert.Equal(18, StandardUpgradeCatalog.All.Count);
        Assert.Equal(18, StandardUpgradeCatalog.All.Select(definition => definition.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(12, StandardUpgradeCatalog.InitiallyUnlockedIds.Count);
        Assert.All(StandardUpgradeCatalog.All, definition => Assert.NotEmpty(definition.Effects));
    }

    [Fact]
    public void RunDirectorBuildsTheSameThreeSectorNineEncounterRunFromTheSameSeed()
    {
        ArenaSectorDefinition[] sectors = CreateSectors(4);
        RunDirector first = new(2026, sectors, StandardUpgradeCatalog.All);
        RunDirector second = new(2026, sectors, StandardUpgradeCatalog.All);

        Assert.Equal(3, first.SelectedSectors.Count);
        Assert.Equal(9, first.Encounters.Count);
        Assert.Equal(
            first.SelectedSectors.Select(sector => sector.Id),
            second.SelectedSectors.Select(sector => sector.Id));
        Assert.Equal(
            first.Encounters.Select(encounter => encounter.ObjectiveType),
            second.Encounters.Select(encounter => encounter.ObjectiveType));
        Assert.Equal(
            ExpectedPurgePressureWaves,
            first.Encounters
                .Where(encounter => encounter.ObjectiveType == EncounterObjectiveType.Purge)
                .OrderBy(encounter => encounter.SectorNumber)
                .Select(encounter => encounter.PressureWaveCount));
        Assert.Equal(
            ExpectedElitePressureWaves,
            first.Encounters
                .Where(encounter => encounter.ObjectiveType == EncounterObjectiveType.EliteHunt)
                .OrderBy(encounter => encounter.SectorNumber)
                .Select(encounter => encounter.PressureWaveCount));
        Assert.All(first.SelectedSectors, sector =>
        {
            EncounterObjectiveType[] objectives = first.Encounters
                .Where(encounter => encounter.SectorId == sector.Id)
                .Select(encounter => encounter.ObjectiveType)
                .Order()
                .ToArray();
            Assert.Equal(
                new[]
                {
                    EncounterObjectiveType.Purge,
                    EncounterObjectiveType.RelayDefense,
                    EncounterObjectiveType.EliteHunt,
                }.Order(),
                objectives);
        });
    }

    [Fact]
    public void FirstRunUsesOnboardingOrderAndNineUniqueThreeChoiceRewards()
    {
        RunDirector director = new(
            17,
            CreateSectors(4),
            StandardUpgradeCatalog.All,
            isFirstRun: true);

        Assert.Equal(EncounterObjectiveType.Purge, director.CurrentEncounter?.ObjectiveType);
        for (int encounter = 0; encounter < RunDirector.EncounterCount; encounter++)
        {
            UpgradeOffer offer = director.CompleteEncounter();
            Assert.Equal(3, offer.Choices.Count);
            Assert.Equal(3, offer.Choices.Select(choice => choice.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain(offer.Choices, choice => director.Modifiers.OwnedUpgradeIds.Contains(choice.Id));
            director.CompleteRecovery();
            director.ChooseUpgrade(offer.Choices[0].Id);
        }

        Assert.Equal(RunPhase.BossActive, director.Phase);
        Assert.Equal(9, director.Modifiers.OwnedUpgradeIds.Count);
        director.CompleteBoss();
        Assert.Equal(RunPhase.Victory, director.Phase);
    }

    [Fact]
    public void TypedUpgradeEffectsProduceSpecifiedWeaponAndRecoveryModifiers()
    {
        RunModifiers modifiers = new(StandardUpgradeCatalog.All);

        Assert.True(modifiers.Apply("calibrated-cells"));
        Assert.True(modifiers.Apply("field-loader"));
        Assert.True(modifiers.Apply("tight-choke"));

        Assert.InRange(modifiers.DamageMultiplier("pulse-sidearm", 10f), 1.119f, 1.121f);
        Assert.InRange(modifiers.ReloadTimeMultiplier, 0.799f, 0.801f);
        Assert.InRange(modifiers.RecoveryMultiplier, 1.199f, 1.201f);
        Assert.InRange(modifiers.SpreadMultiplier("scatter-blaster"), 0.699f, 0.701f);
        Assert.InRange(modifiers.FalloffStartMultiplier("scatter-blaster"), 1.249f, 1.251f);
        Assert.False(modifiers.Apply("field-loader"));

        RunModifiers pulseModifiers = new(StandardUpgradeCatalog.All);
        pulseModifiers.Apply("pulse-capacitor");
        WeaponState sidearm = new(new WeaponDefinition
        {
            Id = "pulse-sidearm",
            DisplayName = "Pulse",
            ModelAsset = "pulse",
            AmmoMode = AmmoMode.RegeneratingEnergy,
            ShotMode = ShotMode.Hitscan,
            Damage = 20f,
            FireIntervalSeconds = 0.01f,
            EnergyCapacity = 100f,
            EnergyPerShot = 10f,
            EnergyRegenerationPerSecond = 0f,
        }, pulseModifiers);
        for (int shot = 0; shot < 5; shot++)
        {
            Assert.True(sidearm.TryFire());
            sidearm.Tick(0.02f);
        }

        Assert.InRange(sidearm.Energy, 57.49f, 57.51f);
    }

    [Fact]
    public void CheckpointRestoresSeedEncounterAndOwnedUpgrades()
    {
        RunDirector original = new(81, CreateSectors(4), StandardUpgradeCatalog.All);
        UpgradeOffer offer = original.CompleteEncounter();
        original.CompleteRecovery();
        original.ChooseUpgrade(offer.Choices[0].Id);
        RunCheckpoint checkpoint = original.CreateCheckpoint("robot-arena", "pulse-sidearm");

        RunDirector restored = new(
            checkpoint.Seed,
            CreateSectors(4),
            StandardUpgradeCatalog.All,
            checkpoint: checkpoint);

        Assert.Equal(1, restored.CurrentEncounterIndex);
        Assert.Single(restored.Modifiers.OwnedUpgradeIds);
        Assert.Contains(offer.Choices[0].Id, restored.Modifiers.OwnedUpgradeIds);
    }

    [Fact]
    public void FirstRunCheckpointPreservesOnboardingSectorAndObjectiveOrder()
    {
        ArenaSectorDefinition[] sectors = CreateSectors(4);
        RunDirector original = new(
            81,
            sectors,
            StandardUpgradeCatalog.All,
            isFirstRun: true);
        UpgradeOffer offer = original.CompleteEncounter();
        original.CompleteRecovery();
        original.ChooseUpgrade(offer.Choices[0].Id);
        RunCheckpoint checkpoint = original.CreateCheckpoint("robot-arena", "pulse-sidearm");

        RunDirector restored = new(
            checkpoint.Seed,
            sectors,
            StandardUpgradeCatalog.All,
            checkpoint: checkpoint,
            isFirstRun: false);

        Assert.True(checkpoint.IsFirstRun);
        Assert.True(restored.IsFirstRun);
        Assert.Equal(
            original.SelectedSectors.Select(sector => sector.Id),
            restored.SelectedSectors.Select(sector => sector.Id));
        Assert.Equal(
            original.Encounters.Select(encounter => (encounter.SectorId, encounter.ObjectiveType)),
            restored.Encounters.Select(encounter => (encounter.SectorId, encounter.ObjectiveType)));
        Assert.Equal(original.CurrentEncounterIndex, restored.CurrentEncounterIndex);
    }

    [Fact]
    public void EnemyAttackPublishesWindupBeforeItsImpact()
    {
        using GameSimulation simulation = new(CreateSimulationCatalog(withSectors: false), new RunConfiguration
        {
            ArenaId = "robot-arena",
        });
        bool attackStarted = false;
        bool attackImpacted = false;
        float healthAtStart = 0f;
        for (int tick = 0; tick < 240 && !attackImpacted; tick++)
        {
            simulation.Step([]);
            if (simulation.CombatEvents.Any(combatEvent =>
                combatEvent.Type == CombatEventType.EnemyAttackStarted))
            {
                attackStarted = true;
                healthAtStart = simulation.Player.Health;
            }

            if (simulation.CombatEvents.Any(combatEvent =>
                combatEvent.Type == CombatEventType.EnemyAttackImpact))
            {
                attackImpacted = true;
            }

            if (attackStarted && !attackImpacted)
            {
                Assert.Equal(healthAtStart, simulation.Player.Health);
            }
        }

        Assert.True(attackStarted);
        Assert.True(attackImpacted);
        Assert.True(simulation.Player.Health < healthAtStart);
    }

    [Fact]
    public void LocomotionFacesMovementInsteadOfSidewaysTowardTarget()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        EnemyDefinition striker = catalog.Enemies["striker"];
        catalog.Enemies["striker"] = striker with
        {
            Behavior = EnemyBehavior.Skirmisher,
            MoveSpeed = 3f,
            StrafeSpeed = 3f,
            PreferredRange = 15f,
            RangedAttackRange = 14f,
            ProjectileSpeed = 12f,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "robot-arena",
            Seed = 42,
            IsFirstRun = true,
        });

        EnemyState? enemy = null;
        bool observedLocomotion = false;
        for (int tick = 0; tick < 80 && !observedLocomotion; tick++)
        {
            simulation.Step([]);
            enemy = simulation.Enemies.FirstOrDefault(candidate => !candidate.IsDead);
            observedLocomotion = enemy is not null && enemy.ActionState == EnemyActionState.Locomotion &&
                Vector3.DistanceSquared(enemy.Position, enemy.PreviousPosition) > 0.000001f;
        }

        enemy = Assert.IsType<EnemyState>(enemy);
        Assert.True(observedLocomotion);
        Vector3 movement = Vector3.Normalize(enemy.Position - enemy.PreviousPosition);
        Vector3 facing = new(MathF.Sin(enemy.FacingYaw), 0f, MathF.Cos(enemy.FacingYaw));
        float alignment = Vector3.Dot(movement, facing);
        Assert.True(alignment > 0.98f,
            $"Position {enemy.Position}, previous {enemy.PreviousPosition}, state {enemy.ActionState}; " +
            $"movement {movement} and facing {facing} (yaw {enemy.FacingYaw}) aligned by only {alignment}.");
    }

    [Fact]
    public void SectorSpawnUsesAVisibleTelegraphAtAStandardDistanceBeforeCreatingEnemy()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        catalog.Arenas["robot-arena"].Sectors[0] = catalog.Arenas["robot-arena"].Sectors[0] with
        {
            EntryPoint = new Vector3(3f, 0f, 0f),
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "robot-arena",
            Seed = 42,
            IsFirstRun = true,
        });

        simulation.Step([]);

        PendingEnemySpawn pending = Assert.Single(simulation.PendingEnemySpawns);
        Assert.Empty(simulation.Enemies);
        Assert.InRange(simulation.Player.Position.X, 2.999f, 3.001f);
        Assert.InRange(Vector3.Distance(pending.Position, simulation.Player.Position), 14f, 28f);
        Assert.InRange(pending.RemainingSeconds, 0.72f, 0.75f);
        Assert.Contains(simulation.CombatEvents, combatEvent =>
            combatEvent.Type == CombatEventType.EnemySpawnTelegraph);
        for (int tick = 0; tick < 50 && simulation.Enemies.Count == 0; tick++)
        {
            simulation.Step([]);
        }

        Assert.Single(simulation.Enemies);
    }

    [Fact]
    public void PendingPortalRevalidatesDistanceBeforeMaterializingEnemy()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "robot-arena",
            Seed = 42,
            IsFirstRun = true,
        });

        simulation.Step([]);
        PendingEnemySpawn pending = Assert.Single(simulation.PendingEnemySpawns);
        for (int tick = 0; tick < 50 && simulation.Enemies.Count == 0; tick++)
        {
            simulation.Step(
            [
                new PlayerCommand(
                    simulation.Tick + 1,
                    simulation.Player.Id,
                    -Vector2.UnitX,
                    Vector2.Zero,
                    PlayerButtons.None,
                    -1),
            ]);
        }

        Assert.Empty(simulation.Enemies);
        Assert.True(Vector3.Distance(simulation.Player.Position, pending.Position) < 12f);

        for (int tick = 0; tick < 120 && simulation.Enemies.Count == 0; tick++)
        {
            simulation.Step(
            [
                new PlayerCommand(
                    simulation.Tick + 1,
                    simulation.Player.Id,
                    Vector2.UnitX,
                    Vector2.Zero,
                    PlayerButtons.None,
                    -1),
            ]);
        }

        EnemyState enemy = Assert.Single(simulation.Enemies);
        Assert.InRange(Vector3.Distance(enemy.Position, simulation.Player.Position), 12f, 32f);
    }

    [Fact]
    public void RelayDefenseReplenishesPressureInsteadOfGoingIdle()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        catalog.Weapons["pulse-sidearm"] = catalog.Weapons["pulse-sidearm"] with
        {
            Damage = 1_000f,
            FireIntervalSeconds = 0.01f,
            MagazineSize = 1_000,
            ReserveCapacity = 0,
            SpreadDegrees = 0f,
            Range = 100f,
        };
        EnemyDefinition striker = catalog.Enemies["striker"];
        catalog.Enemies["striker"] = striker with { MaxHealth = 1f, MoveSpeed = 0.01f };
        RunCheckpoint checkpoint = new()
        {
            Seed = 42,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = 1,
            IsFirstRun = true,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Checkpoint = checkpoint,
            GodModeEnabled = true,
        });

        int spawned = 0;
        for (int tick = 0; tick < 2_400 && simulation.Phase == GamePhase.Playing; tick++)
        {
            EnemyState? target = simulation.Enemies.FirstOrDefault(enemy => !enemy.IsDead);
            PlayerButtons buttons = PlayerButtons.None;
            Vector2 look = Vector2.Zero;
            if (target is not null && (tick & 1) == 0)
            {
                Vector3 direction = target.Position - simulation.Player.Position;
                float targetYaw = MathF.Atan2(direction.X, -direction.Z);
                look = new Vector2(targetYaw - simulation.Player.Yaw, 0f);
                buttons = PlayerButtons.Fire;
            }

            simulation.Step(
            [
                new PlayerCommand(
                    simulation.Tick + 1,
                    simulation.Player.Id,
                    Vector2.Zero,
                    look,
                    buttons,
                    -1),
            ]);
            spawned += simulation.CombatEvents.Count(combatEvent =>
                combatEvent.Type == CombatEventType.EnemySpawned);
        }

        Assert.Equal(EncounterObjectiveType.RelayDefense, simulation.LastCompletedEncounterObjective ??
            simulation.RunSnapshot?.ObjectiveType);
        Assert.True(spawned > 10, $"Expected a reinforcement wave after the 10-threat opening batch; saw {spawned}.");
        Assert.True(simulation.RelayObjective?.RemainingSeconds > 0f);
    }

    [Fact]
    public void PurgeCompletesOnlyAfterEveryAuthoredPressureWaveIsCleared()
    {
        ContentCatalog catalog = CreateObjectiveTestCatalog();
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "robot-arena",
            Seed = 42,
            IsFirstRun = true,
            GodModeEnabled = true,
        });

        CombatEvent completion = RunUntilObjectiveResult(simulation, CombatEventType.EncounterCompleted);

        Assert.Equal(RunPhase.RecoveryLoot, simulation.RunPhase);
        Assert.Equal(GamePhase.Playing, simulation.Phase);
        Assert.Equal(EncounterObjectiveType.Purge, simulation.LastCompletedEncounterObjective);
        Assert.Equal(1f, simulation.LastCompletedEncounterMetric);
        Assert.Equal(1, simulation.CompletedPurgeEncounters);
        Assert.Equal(ExpectedPurgePressureWaves[0], simulation.CurrentPressureWaveNumber);
        Assert.Equal(simulation.CurrentEncounter?.Id, completion.CueId);
        Assert.Equal(1f, completion.Value);
        Assert.NotNull(simulation.CurrentUpgradeOffer);
        Assert.Contains(simulation.CombatEvents, combatEvent =>
            combatEvent.Type == CombatEventType.UpgradeOffered);
    }

    [Fact]
    public void EncounterCompletionKeepsTheLethalImpactRangeOnTheKillEvent()
    {
        ContentCatalog catalog = CreateObjectiveTestCatalog();
        catalog.Arenas["robot-arena"].Sectors[0] =
            catalog.Arenas["robot-arena"].Sectors[0] with
            {
                EntryPoint = new Vector3(10f, 0f, 0f),
            };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "robot-arena",
            Seed = 42,
            IsFirstRun = true,
            GodModeEnabled = true,
        });

        RunUntilObjectiveResult(simulation, CombatEventType.EncounterCompleted);

        CombatEvent finalKill = simulation.CombatEvents.Last(combatEvent =>
            combatEvent.Type == CombatEventType.EnemyKilled);
        float postCompletionDistance = Vector3.Distance(
            simulation.Player.Position,
            finalKill.SecondaryPosition);
        Assert.True(finalKill.RangeMeters >= 18f,
            $"Expected a long-range final hit, got {finalKill.RangeMeters:0.00}m.");
        Assert.True(postCompletionDistance < 18f,
            $"The test must expose the hub-teleport race; post-step distance was {postCompletionDistance:0.00}m.");
    }

    [Fact]
    public void EliteHuntCompletesWhenTheMarkedFinalWaveEliteIsDefeated()
    {
        ContentCatalog catalog = CreateObjectiveTestCatalog();
        RunCheckpoint checkpoint = new()
        {
            Seed = 42,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = 2,
            IsFirstRun = true,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Checkpoint = checkpoint,
            GodModeEnabled = true,
        });

        bool observedMarkedElite = false;
        CombatEvent completion = RunUntilObjectiveResult(
            simulation,
            CombatEventType.EncounterCompleted,
            () => observedMarkedElite |= simulation.Enemies.Any(enemy => enemy.IsElite));

        Assert.True(observedMarkedElite);
        Assert.Equal(RunPhase.RecoveryLoot, simulation.RunPhase);
        Assert.Equal(EncounterObjectiveType.EliteHunt, simulation.LastCompletedEncounterObjective);
        Assert.Equal(1, simulation.CompletedEliteEncounters);
        Assert.Equal(ExpectedElitePressureWaves[0], simulation.CurrentPressureWaveNumber);
        Assert.Equal(simulation.CurrentEncounter?.Id, completion.CueId);
        Assert.True(completion.Value > 0f);
        Assert.NotNull(simulation.CurrentUpgradeOffer);
    }

    [Fact]
    public void DestroyedRelayFailsTheEncounterAndPublishesObjectiveFailure()
    {
        ContentCatalog catalog = CreateObjectiveTestCatalog();
        RunCheckpoint checkpoint = new()
        {
            Seed = 42,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = 1,
            IsFirstRun = true,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Checkpoint = checkpoint,
            GodModeEnabled = true,
        });

        simulation.Step([]);
        RelayObjectiveState relay = Assert.IsType<RelayObjectiveState>(simulation.RelayObjective);
        simulation.ApplyRelayDamage(relay.MaximumHealth * 2f);
        Assert.Contains(simulation.CombatEvents, combatEvent =>
            combatEvent.Type == CombatEventType.RelayDamaged);

        simulation.Step([]);

        Assert.Equal(0f, relay.Health);
        Assert.Equal(GamePhase.Defeat, simulation.Phase);
        Assert.Equal(RunPhase.Defeat, simulation.RunPhase);
        Assert.Null(simulation.LastCompletedEncounterObjective);
        Assert.Null(simulation.CurrentUpgradeOffer);
        CombatEvent failure = Assert.Single(simulation.CombatEvents, combatEvent =>
            combatEvent.Type == CombatEventType.EncounterFailed);
        Assert.Equal(simulation.CurrentEncounter?.Id, failure.CueId);
    }

    [Fact]
    public void LethalEnemyAttackFailsTheActiveEncounterAfterPublishingPlayerDamage()
    {
        ContentCatalog catalog = CreateObjectiveTestCatalog();
        EnemyDefinition striker = catalog.Enemies["striker"];
        catalog.Enemies["striker"] = striker with
        {
            AttackRange = 30f,
            AttackDamage = 100f,
            AttackWindupSeconds = 0.01f,
            AttackCooldownSeconds = 0.01f,
        };
        RunCheckpoint checkpoint = new()
        {
            Seed = 42,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            IsFirstRun = true,
            PlayerHealth = 20f,
            PlayerMaximumHealth = 100f,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration { Checkpoint = checkpoint });

        CombatEvent playerDamage = RunUntilObjectiveResult(
            simulation,
            CombatEventType.PlayerDamaged,
            engageEnemies: false);

        Assert.True(playerDamage.Value >= 20f);
        Assert.Equal(0f, simulation.Player.Health);
        Assert.Equal(GamePhase.Defeat, simulation.Phase);
        Assert.Equal(RunPhase.Defeat, simulation.RunPhase);
        Assert.Null(simulation.LastCompletedEncounterObjective);
        Assert.Null(simulation.CurrentUpgradeOffer);
        CombatEvent failure = Assert.Single(simulation.CombatEvents, combatEvent =>
            combatEvent.Type == CombatEventType.EncounterFailed);
        Assert.Equal(simulation.CurrentEncounter?.Id, failure.CueId);
    }

    [Fact]
    public void CheckpointRestoresCollectedSelectedAndActiveArmoryWeapons()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        AddTestWeapon(catalog, "beam-rifle");
        AddTestWeapon(catalog, "burst-carbine");
        RunCheckpoint checkpoint = new()
        {
            Seed = 91,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            CollectedWeaponIds = ["pulse-sidearm", "beam-rifle"],
            SelectedWeaponId = "beam-rifle",
            ActiveArmoryWeaponIds = ["burst-carbine"],
        };

        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "robot-arena",
            Checkpoint = checkpoint,
        });

        Assert.Equal(2, simulation.Player.Weapons.Count);
        Assert.Equal("beam-rifle", simulation.SelectedWeaponId);
        Assert.Contains("burst-carbine", simulation.Pickups.Select(pickup => pickup.WeaponId));
        RunCheckpoint saved = Assert.IsType<RunCheckpoint>(simulation.CreateRunCheckpoint());
        Assert.Contains("beam-rifle", saved.CollectedWeaponIds);
        Assert.Equal("beam-rifle", saved.SelectedWeaponId);
        Assert.Contains("burst-carbine", saved.ActiveArmoryWeaponIds);
    }

    [Fact]
    public void RecoveryHubWaitsForPlayerToCollectArmoryWeapon()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        AddTestWeapon(catalog, "burst-carbine");
        RunCheckpoint checkpoint = new()
        {
            Seed = 91,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = 1,
            ActiveArmoryWeaponIds = ["burst-carbine"],
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration { Checkpoint = checkpoint });

        Assert.True(simulation.AwaitingArmoryCollection);
        simulation.Step([]);
        Assert.True(simulation.AwaitingArmoryCollection);
        Assert.DoesNotContain(simulation.Player.Weapons,
            weapon => weapon.Definition.Id == "burst-carbine");
        Assert.Empty(simulation.Enemies);

        for (int tick = 0; tick < 180 && simulation.AwaitingArmoryCollection; tick++)
        {
            simulation.Step(
            [
                new PlayerCommand(
                    simulation.Tick + 1,
                    simulation.Player.Id,
                    Vector2.UnitY,
                    Vector2.Zero,
                    PlayerButtons.None,
                    -1),
            ]);
        }

        Assert.False(simulation.AwaitingArmoryCollection);
        Assert.Contains(simulation.Player.Weapons,
            weapon => weapon.Definition.Id == "burst-carbine");
    }

    [Fact]
    public void CheckpointRoundTripPreservesCumulativeRunAndPlayerState()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        RunCheckpoint checkpoint = new()
        {
            Seed = 5150,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = 4,
            GodModeUsed = true,
            OwnedUpgradeIds = ["reinforced-shell"],
            CollectedWeaponIds = ["pulse-sidearm"],
            WeaponStates =
            [
                new WeaponCheckpointState
                {
                    WeaponId = "pulse-sidearm",
                    Magazine = 3,
                    Reserve = 17,
                    FireCooldownSeconds = 0.14f,
                    ReloadRemainingSeconds = 0.6f,
                    MagazineConsumptionAccumulator = 0.35f,
                },
            ],
            SelectedWeaponId = "pulse-sidearm",
            PlayerHealth = 47.5f,
            PlayerMaximumHealth = 120f,
            SimulationTick = 12_345,
            RandomState = 0x1234_5678u,
            ElapsedRunSeconds = 234.5f,
            Score = 4_321,
            Kills = 37,
            DamageTaken = 88.25f,
            CloseRangeKills = 11,
            LongRangeKills = 7,
            SectorsCompleted = 1,
            CompletedPurgeEncounters = 2,
            CompletedRelayEncounters = 1,
            CompletedEliteEncounters = 1,
            LastCompletedEncounterObjective = EncounterObjectiveType.RelayDefense,
            LastCompletedEncounterMetric = 0.76f,
        };

        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Checkpoint = checkpoint,
        });

        Assert.Equal(47.5f, simulation.Player.Health);
        Assert.Equal(120f, simulation.Player.MaximumHealth);
        Assert.Equal(12_345u, simulation.Tick);
        Assert.Equal(3, simulation.Player.CurrentWeapon.Magazine);
        Assert.Equal(17, simulation.Player.CurrentWeapon.Reserve);
        Assert.Equal(0.14f, simulation.Player.CurrentWeapon.FireCooldownSeconds);
        Assert.Equal(0.6f, simulation.Player.CurrentWeapon.ReloadRemainingSeconds);
        Assert.Equal(234.5f, simulation.ElapsedRunSeconds);
        Assert.Equal(4_321, simulation.Score);
        Assert.Equal(37, simulation.Kills);
        Assert.Equal(88.25f, simulation.DamageTaken);
        Assert.Equal(11, simulation.CloseRangeKills);
        Assert.Equal(7, simulation.LongRangeKills);
        Assert.Equal(1, simulation.SectorsCompleted);
        Assert.Equal(2, simulation.CompletedPurgeEncounters);
        Assert.Equal(1, simulation.CompletedRelayEncounters);
        Assert.Equal(1, simulation.CompletedEliteEncounters);
        Assert.Equal(EncounterObjectiveType.RelayDefense, simulation.LastCompletedEncounterObjective);
        Assert.Equal(0.76f, simulation.LastCompletedEncounterMetric);
        Assert.True(simulation.RunSnapshot?.GodModeUsed);

        RunCheckpoint saved = Assert.IsType<RunCheckpoint>(simulation.CreateRunCheckpoint());
        Assert.Equal(checkpoint.PlayerHealth, saved.PlayerHealth);
        Assert.Equal(checkpoint.PlayerMaximumHealth, saved.PlayerMaximumHealth);
        Assert.Equal(checkpoint.SimulationTick, saved.SimulationTick);
        Assert.Equal(checkpoint.RandomState, saved.RandomState);
        WeaponCheckpointState savedWeapon = Assert.Single(saved.WeaponStates);
        Assert.Equal(3, savedWeapon.Magazine);
        Assert.Equal(17, savedWeapon.Reserve);
        Assert.Equal(0.14f, savedWeapon.FireCooldownSeconds);
        Assert.Equal(0.6f, savedWeapon.ReloadRemainingSeconds);
        Assert.Equal(0.35f, savedWeapon.MagazineConsumptionAccumulator);
        Assert.Equal(checkpoint.ElapsedRunSeconds, saved.ElapsedRunSeconds);
        Assert.Equal(checkpoint.Score, saved.Score);
        Assert.Equal(checkpoint.Kills, saved.Kills);
        Assert.Equal(checkpoint.DamageTaken, saved.DamageTaken);
        Assert.Equal(checkpoint.CloseRangeKills, saved.CloseRangeKills);
        Assert.Equal(checkpoint.LongRangeKills, saved.LongRangeKills);
        Assert.Equal(checkpoint.SectorsCompleted, saved.SectorsCompleted);
        Assert.Equal(checkpoint.CompletedPurgeEncounters, saved.CompletedPurgeEncounters);
        Assert.Equal(checkpoint.CompletedRelayEncounters, saved.CompletedRelayEncounters);
        Assert.Equal(checkpoint.CompletedEliteEncounters, saved.CompletedEliteEncounters);
        Assert.Equal(checkpoint.LastCompletedEncounterObjective, saved.LastCompletedEncounterObjective);
        Assert.Equal(checkpoint.LastCompletedEncounterMetric, saved.LastCompletedEncounterMetric);
    }

    [Fact]
    public void SemanticallyInvalidCheckpointIdsFallBackWithoutCrashingContinue()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        RunCheckpoint checkpoint = new()
        {
            Seed = 99,
            ArenaId = "missing-arena",
            StartingWeaponId = "missing-weapon",
            NextEncounterIndex = 4,
            OwnedUpgradeIds = ["missing-upgrade", "calibrated-cells"],
            CollectedWeaponIds = ["missing-weapon"],
            SelectedWeaponId = "missing-weapon",
            ActiveArmoryWeaponIds = ["missing-weapon"],
        };

        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            Checkpoint = checkpoint,
        });

        Assert.Equal("robot-arena", simulation.Arena.Id);
        Assert.Equal("pulse-sidearm", simulation.SelectedWeaponId);
        Assert.Contains("calibrated-cells", simulation.OwnedUpgradeIds);
        Assert.DoesNotContain("missing-upgrade", simulation.OwnedUpgradeIds);
        Assert.DoesNotContain("missing-weapon", simulation.CollectedWeaponIds);
        Assert.Equal(5, simulation.EncounterNumber);
    }

    [Fact]
    public void ExcessCheckpointUpgradesCannotExhaustTheNextRewardOffer()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        RunCheckpoint checkpoint = new()
        {
            Seed = 99,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = 0,
            OwnedUpgradeIds = StandardUpgradeCatalog.All.Select(upgrade => upgrade.Id).ToList(),
        };

        RunDirector director = new(
            checkpoint.Seed,
            catalog.Arenas["robot-arena"].Sectors,
            StandardUpgradeCatalog.All,
            StandardUpgradeCatalog.InitiallyUnlockedIds,
            checkpoint);

        Assert.Empty(director.Modifiers.OwnedUpgradeIds);
        Assert.Equal(3, director.CompleteEncounter().Choices.Count);
    }

    [Fact]
    public void ShippedReleaseArenaStartsInsideSectorWithAReachableRobotPortal()
    {
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Seed = 1337,
            IsFirstRun = true,
        });

        simulation.Step([]);

        Assert.Equal(RunPhase.EncounterActive, simulation.RunPhase);
        Assert.NotNull(simulation.ActiveSector);
        PendingEnemySpawn pending = Assert.Single(simulation.PendingEnemySpawns);
        Assert.StartsWith("robot-", pending.EnemyId, StringComparison.Ordinal);
        Assert.InRange(Vector3.Distance(simulation.Player.Position, pending.Position), 12f, 32f);
    }

    [Fact]
    public void BossCheckpointSelectsBreachWalkerAndReturnsPlayerToCentralSpawn()
    {
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
        RunCheckpoint checkpoint = new()
        {
            Seed = 440,
            ArenaId = "orbital-depot",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = RunDirector.EncounterCount,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            Checkpoint = checkpoint,
        });

        for (int tick = 0; tick < 120 && simulation.ActiveBoss is null; tick++)
        {
            simulation.Step(
            [
                new PlayerCommand(
                    simulation.Tick + 1,
                    simulation.Player.Id,
                    Vector2.UnitY,
                    Vector2.Zero,
                    PlayerButtons.None,
                    -1),
            ]);
        }

        EnemyState boss = Assert.IsType<EnemyState>(simulation.ActiveBoss);
        Assert.Equal("breach-walker", boss.Definition.Id);
        Assert.True(Vector3.Distance(simulation.Player.Position, simulation.Arena.BossArenaAnchor) >= 12f);
        Assert.InRange(
            simulation.Player.Position.X,
            simulation.Arena.BossArenaAnchor.X - simulation.Arena.BossArenaHalfExtents.X,
            simulation.Arena.BossArenaAnchor.X + simulation.Arena.BossArenaHalfExtents.X);
        Assert.InRange(
            simulation.Player.Position.Z,
            simulation.Arena.BossArenaAnchor.Z - simulation.Arena.BossArenaHalfExtents.Z,
            simulation.Arena.BossArenaAnchor.Z + simulation.Arena.BossArenaHalfExtents.Z);

        for (int tick = 0; tick < 60 && simulation.Phase == GamePhase.Playing; tick++)
        {
            simulation.Step(
            [
                new PlayerCommand(
                    simulation.Tick + 1,
                    simulation.Player.Id,
                    -Vector2.UnitY,
                    Vector2.Zero,
                    PlayerButtons.None,
                    -1),
            ]);
        }

        Assert.InRange(
            simulation.Player.Position.Z,
            simulation.Arena.BossArenaAnchor.Z - simulation.Arena.BossArenaHalfExtents.Z + 0.49f,
            simulation.Arena.BossArenaAnchor.Z + simulation.Arena.BossArenaHalfExtents.Z - 0.49f);
        Assert.All(simulation.Enemies.Where(enemy => !enemy.IsDead), enemy =>
        {
            Assert.InRange(
                enemy.Position.X,
                simulation.Arena.BossArenaAnchor.X - simulation.Arena.BossArenaHalfExtents.X,
                simulation.Arena.BossArenaAnchor.X + simulation.Arena.BossArenaHalfExtents.X);
            Assert.InRange(
                enemy.Position.Z,
                simulation.Arena.BossArenaAnchor.Z - simulation.Arena.BossArenaHalfExtents.Z,
                simulation.Arena.BossArenaAnchor.Z + simulation.Arena.BossArenaHalfExtents.Z);
        });
    }

    [Fact]
    public void OpenArenaAllowsPlayerToCrossActiveSectorBoundary()
    {
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Seed = 1337,
            IsFirstRun = true,
        });

        simulation.Step([]);
        ArenaSectorDefinition sector = Assert.IsType<ArenaSectorDefinition>(simulation.ActiveSector);
        float direction = sector.EntryPoint.X < 0f ? 1f : -1f;
        for (int tick = 0; tick < 100; tick++)
        {
            simulation.Step(
            [
                new PlayerCommand(
                    simulation.Tick + 1,
                    simulation.Player.Id,
                    new Vector2(direction, 0f),
                    Vector2.Zero,
                    PlayerButtons.None,
                    -1),
            ]);
        }

        Assert.True(
            simulation.Player.Position.X > sector.BoundsMax.X + 0.5f ||
            simulation.Player.Position.X < sector.BoundsMin.X - 0.5f,
            "The active sector bounds must remain presentation-only in the release arena.");
    }

    [Fact]
    public void KillingGeneratedBossCommitsVictoryBeforeFurtherDamageCanResolve()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        catalog.Weapons["pulse-sidearm"] = catalog.Weapons["pulse-sidearm"] with
        {
            Damage = 2_000f,
            Range = 100f,
            SpreadDegrees = 0f,
        };
        RunCheckpoint checkpoint = new()
        {
            Seed = 700,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = RunDirector.EncounterCount,
            PlayerHealth = 1f,
            PlayerMaximumHealth = 100f,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration { Checkpoint = checkpoint });
        for (int tick = 0; tick < 80 && simulation.ActiveBoss is null; tick++)
        {
            simulation.Step([]);
        }

        EntityId bossId = Assert.IsType<EnemyState>(simulation.ActiveBoss).Id;
        simulation.Step(
        [
            new PlayerCommand(
                simulation.Tick + 1,
                simulation.Player.Id,
                Vector2.Zero,
                Vector2.Zero,
                PlayerButtons.Fire,
                -1),
        ]);

        Assert.Equal(GamePhase.Victory, simulation.Phase);
        Assert.Equal(RunPhase.Victory, simulation.RunPhase);
        Assert.Equal(1f, simulation.Player.Health);
        Assert.Contains(simulation.CombatEvents, combatEvent =>
            combatEvent.Type == CombatEventType.EnemyKilled && combatEvent.TargetId == bossId);

        for (int tick = 0; tick < 120; tick++)
        {
            simulation.Step([]);
        }

        Assert.Equal(GamePhase.Victory, simulation.Phase);
        Assert.Equal(1f, simulation.Player.Health);
    }

    [Fact]
    public void BossPhaseChangeCancelsOldAttackAndTelegraphsCappedCentralSummons()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        catalog.Weapons["pulse-sidearm"] = catalog.Weapons["pulse-sidearm"] with { Damage = 1_000f };
        EnemyDefinition bossDefinition = catalog.Enemies["breach-walker"];
        catalog.Enemies["breach-walker"] = bossDefinition with
        {
            AttackWindupSeconds = 0.8f,
            BossPhases =
            [
                bossDefinition.BossPhases[0],
                bossDefinition.BossPhases[1] with { SummonEnemyId = "striker", SummonCount = 2 },
                bossDefinition.BossPhases[2],
            ],
        };
        RunCheckpoint checkpoint = new()
        {
            Seed = 77,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = RunDirector.EncounterCount,
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration { Checkpoint = checkpoint });
        EnemyState? boss = null;
        for (int tick = 0; tick < 80; tick++)
        {
            simulation.Step([]);
            boss = simulation.ActiveBoss;
            if (boss?.PendingAttackKind == EnemyAttackKind.BossVolley &&
                boss.ActionState == EnemyActionState.Windup)
            {
                break;
            }
        }

        boss = Assert.IsType<EnemyState>(boss);
        Assert.Equal(EnemyAttackKind.BossVolley, boss.PendingAttackKind);
        PlayerCommand fire = new(
            simulation.Tick + 1,
            simulation.Player.Id,
            Vector2.Zero,
            Vector2.Zero,
            PlayerButtons.Fire,
            -1);
        simulation.Step([fire]);

        Assert.Equal(1, boss.CurrentBossPhaseIndex);
        Assert.False(boss.PendingAttackKind != EnemyAttackKind.None &&
            boss.ActionState == EnemyActionState.Navigating);
        Assert.Equal(2, simulation.PendingEnemySpawns.Count);
        Assert.All(simulation.PendingEnemySpawns, pending =>
        {
            Assert.Equal("striker", pending.EnemyId);
            Assert.StartsWith("boss-summon-", pending.PortalId, StringComparison.Ordinal);
            Assert.InRange(Vector3.Distance(pending.Position, simulation.Player.Position), 12f, 32f);
        });
        Assert.Contains(simulation.CombatEvents, combatEvent =>
            combatEvent.Type == CombatEventType.EnemySpawnTelegraph);

        bool attackResolved = false;
        for (int tick = 0; tick < 120 && simulation.Phase == GamePhase.Playing; tick++)
        {
            simulation.Step([]);
            attackResolved |= simulation.CombatEvents.Any(combatEvent =>
                combatEvent.Type == CombatEventType.EnemyAttackImpact && combatEvent.SourceId == boss.Id);
            Assert.True(simulation.Enemies.Count(enemy => !enemy.IsDead) +
                simulation.PendingEnemySpawns.Count <= 8);
        }

        Assert.True(attackResolved);
    }

    [Fact]
    public void DebugHarnessUsesNormalRewardTransitionAndCanResolveTheBossStage()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        using (GameSimulation encounter = new(catalog, new RunConfiguration
               {
                   ArenaId = "robot-arena",
                   Seed = 42,
                   IsFirstRun = true,
               }))
        {
            Assert.True(encounter.DebugCompleteCurrentStage());
            Assert.Equal(RunPhase.RecoveryLoot, encounter.RunPhase);
            encounter.CompleteRecovery();
            Assert.Equal(RunPhase.RewardSelection, encounter.RunPhase);
            UpgradeDefinition upgrade = encounter.PendingUpgradeOffers[0];
            encounter.ChooseUpgrade(upgrade.Id);
            Assert.Equal(RunPhase.EncounterActive, encounter.RunPhase);
            Assert.Contains(upgrade.Id, encounter.OwnedUpgradeIds);
        }

        RunCheckpoint checkpoint = new()
        {
            Seed = 42,
            ArenaId = "robot-arena",
            StartingWeaponId = "pulse-sidearm",
            NextEncounterIndex = RunDirector.EncounterCount,
        };
        using GameSimulation boss = new(catalog, new RunConfiguration { Checkpoint = checkpoint });

        Assert.True(boss.DebugCompleteCurrentStage());
        Assert.Equal(GamePhase.Victory, boss.Phase);
        Assert.Equal(RunPhase.Victory, boss.RunPhase);
    }

    private static ArenaSectorDefinition[] CreateSectors(int count) => Enumerable.Range(1, count)
        .Select(index => new ArenaSectorDefinition
        {
            Id = $"sector-{index}",
            DisplayName = $"Sector {index}",
            BoundsMin = new Vector3(-20f, 0f, -20f),
            BoundsMax = new Vector3(20f, 5f, 20f),
            EntryPoint = Vector3.Zero,
            ObjectiveAnchor = new Vector3(0f, 0f, -5f),
            SpawnPortals =
            [
                new SpawnPortalDefinition
                {
                    Id = $"portal-{index}",
                    Position = new Vector3(-15f, 0.7f, 0f),
                },
            ],
        })
        .ToArray();

    private static ContentCatalog CreateSimulationCatalog(bool withSectors)
    {
        ContentCatalog catalog = new();
        catalog.Weapons.Add("pulse-sidearm", new WeaponDefinition
        {
            Id = "pulse-sidearm",
            DisplayName = "Pulse Sidearm",
            ModelAsset = "Models/Weapons/pulse",
            AmmoMode = AmmoMode.MagazineReserve,
            ShotMode = ShotMode.Hitscan,
            TriggerMode = TriggerMode.SemiAutomatic,
            Damage = 25f,
            FireIntervalSeconds = 0.2f,
            MagazineSize = 24,
            ReserveCapacity = 120,
            ReloadSeconds = 1f,
        });
        catalog.Enemies.Add("striker", new EnemyDefinition
        {
            SchemaVersion = 2,
            Id = "striker",
            DisplayName = "Striker",
            ModelAsset = "Models/Enemies/striker",
            MaxHealth = 50f,
            MoveSpeed = 0.01f,
            ColliderRadius = 0.55f,
            ColliderHeight = 1.5f,
            AttackRange = 2f,
            AttackDamage = 10f,
            AttackCooldownSeconds = 1f,
            AttackWindupSeconds = 0.45f,
            ScoreValue = 100,
        });
        catalog.Enemies.Add("breach-walker", new EnemyDefinition
        {
            SchemaVersion = 2,
            Id = "breach-walker",
            DisplayName = "Breach Walker",
            ModelAsset = "Models/Enemies/breach-walker",
            Behavior = EnemyBehavior.Boss,
            IsBoss = true,
            MaxHealth = 1500f,
            MoveSpeed = 1f,
            ColliderRadius = 1f,
            ColliderHeight = 3f,
            AttackRange = 2f,
            AttackDamage = 15f,
            AttackCooldownSeconds = 1f,
            RangedAttackRange = 25f,
            ProjectileSpeed = 12f,
            BossPhases =
            [
                new BossPhaseDefinition { Id = "phase-1", DisplayName = "Phase 1", HealthThreshold = 1f },
                new BossPhaseDefinition { Id = "phase-2", DisplayName = "Phase 2", HealthThreshold = 0.65f },
                new BossPhaseDefinition { Id = "phase-3", DisplayName = "Phase 3", HealthThreshold = 0.3f },
            ],
        });
        catalog.Arenas.Add("robot-arena", new ArenaDefinition
        {
            Id = "robot-arena",
            DisplayName = "Robot Arena",
            WaveSetId = "test-waves",
            BoundsMin = new Vector3(-25f, 0f, -25f),
            BoundsMax = new Vector3(25f, 5f, 25f),
            PlayerSpawn = new Vector3(0f, 1.65f, 0f),
            BossArenaAnchor = new Vector3(0f, 0.7f, -10f),
            EnemySpawns = [new Vector3(0f, 0.7f, -1f)],
            Primitives =
            [
                new ArenaPrimitiveDefinition
                {
                    Id = "floor",
                    Position = new Vector3(0f, -0.5f, 0f),
                    Size = new Vector3(50f, 1f, 50f),
                },
            ],
            Sectors = withSectors ? [.. CreateSectors(3)] : [],
        });
        catalog.WaveSets.Add("test-waves", new WaveSetDefinition
        {
            Id = "test-waves",
            InterWaveDelaySeconds = 0f,
            Waves =
            [
                new WaveDefinition
                {
                    Id = "wave-1",
                    MaximumConcurrentEnemies = 1,
                    SpawnIntervalSeconds = 0.01f,
                    SpawnGroups = [new SpawnGroupDefinition { EnemyId = "striker", Count = 1 }],
                },
            ],
        });
        return catalog;
    }

    private static ContentCatalog CreateObjectiveTestCatalog()
    {
        ContentCatalog catalog = CreateSimulationCatalog(withSectors: true);
        WeaponDefinition weapon = catalog.Weapons["pulse-sidearm"];
        catalog.Weapons["pulse-sidearm"] = weapon with
        {
            TriggerMode = TriggerMode.Automatic,
            Damage = 1_000f,
            FireIntervalSeconds = 0.01f,
            MagazineSize = 1_000,
            ReserveCapacity = 0,
            SpreadDegrees = 0f,
            Range = 100f,
        };
        EnemyDefinition striker = catalog.Enemies["striker"];
        catalog.Enemies["striker"] = striker with
        {
            MaxHealth = 1f,
            MoveSpeed = 0.01f,
            ThreatWeight = 10f,
        };
        return catalog;
    }

    private static CombatEvent RunUntilObjectiveResult(
        GameSimulation simulation,
        CombatEventType resultType,
        Action? observe = null,
        bool engageEnemies = true)
    {
        for (int tick = 0; tick < 4_000; tick++)
        {
            observe?.Invoke();
            EnemyState? target = simulation.Enemies.FirstOrDefault(enemy => !enemy.IsDead);
            Vector2 look = Vector2.Zero;
            PlayerButtons buttons = PlayerButtons.None;
            if (engageEnemies && target is not null)
            {
                Vector3 direction = target.Position - simulation.Player.Position;
                float targetYaw = MathF.Atan2(direction.X, -direction.Z);
                look = new Vector2(targetYaw - simulation.Player.Yaw, 0f);
                buttons = PlayerButtons.Fire;
            }

            simulation.Step(
            [
                new PlayerCommand(
                    simulation.Tick + 1,
                    simulation.Player.Id,
                    Vector2.Zero,
                    look,
                    buttons,
                    -1),
            ]);
            observe?.Invoke();
            foreach (CombatEvent combatEvent in simulation.CombatEvents)
            {
                if (combatEvent.Type == resultType)
                {
                    return combatEvent;
                }
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Simulation did not publish {resultType} within the deterministic tick budget. " +
            $"Phase={simulation.Phase}, run phase={simulation.RunPhase}, " +
            $"wave={simulation.CurrentPressureWaveNumber}/{simulation.CurrentPressureWaveTotal}, " +
            $"remaining={simulation.RemainingEnemies}.");
    }

    private static void AddTestWeapon(ContentCatalog catalog, string id) => catalog.Weapons.Add(id, new WeaponDefinition
    {
        Id = id,
        DisplayName = id,
        ModelAsset = $"Models/Weapons/{id}",
        AmmoMode = AmmoMode.MagazineReserve,
        ShotMode = ShotMode.Hitscan,
        Damage = 20f,
        FireIntervalSeconds = 0.2f,
        MagazineSize = 24,
        ReserveCapacity = 120,
        ReloadSeconds = 1f,
    });
}
