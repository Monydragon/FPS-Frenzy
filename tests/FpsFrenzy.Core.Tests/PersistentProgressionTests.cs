using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Input;
using FpsFrenzy.Core.Simulation;
using System.Numerics;

namespace FpsFrenzy.Core.Tests;

public sealed class PersistentProgressionTests
{
    [Fact]
    public void ProgressionMathMatchesThreatAndLevelSpecifications()
    {
        Assert.Equal(330, RpgProgressionMath.ExperienceToNextLevel(1));
        Assert.Equal(56_680, RpgProgressionMath.ExperienceToNextLevel(99));
        Assert.Equal(1f, RpgProgressionMath.ItemPowerScale(1));
        Assert.Equal(2.2375f, RpgProgressionMath.ItemPowerScale(100), 4);
        Assert.Equal(2.62f, RpgProgressionMath.EnemyHealthMultiplier(ThreatTier.TierX), 3);
        Assert.Equal(1.63f, RpgProgressionMath.EnemyDamageMultiplier(ThreatTier.TierX), 3);
        Assert.Equal(20, RpgProgressionMath.PassiveAbilityCapacity(100));
    }

    [Fact]
    public void RarityOddsInterpolateBetweenTierEndpoints()
    {
        Assert.Equal([0.65f, 0.25f, 0.09f, 0.01f, 0f],
            RpgProgressionMath.RarityProbabilities(ThreatTier.TierI));
        Assert.Equal([0.05f, 0.20f, 0.35f, 0.30f, 0.10f],
            RpgProgressionMath.RarityProbabilities(ThreatTier.TierX));
        Assert.Equal(1f, RpgProgressionMath.RarityProbabilities(ThreatTier.TierV).Sum(), 4);
    }

    [Fact]
    public void ProductionCatalogProvidesFiftyDataDrivenSidegradeWeaponBasesAndAbilityLibrary()
    {
        ContentCatalog catalog = LoadCatalog();

        Assert.Equal(50, catalog.Weapons.Values.Count(weapon => weapon.Family != WeaponFamily.None));
        Assert.Equal(10, catalog.WeaponArchetypes.Count);
        Assert.Equal(50, catalog.WeaponBases.Count);
        Assert.All(Enum.GetValues<WeaponFamily>().Where(family => family != WeaponFamily.None), family =>
            Assert.Equal([1, 2, 3, 4, 5], catalog.Weapons.Values
                .Where(weapon => weapon.Family == family)
                .Select(weapon => weapon.BaseTier)
                .Order()
                .ToArray()));
        Assert.Equal(12, catalog.Abilities.Values.Count(ability => ability.Kind == AbilityKind.Active));
        Assert.True(catalog.Abilities.Values.Count(ability => ability.Kind == AbilityKind.Passive) >= 24);
        WeaponBehavior authoredBehaviors = catalog.Weapons.Values.Aggregate(
            WeaponBehavior.None, (combined, weapon) => combined | weapon.BehaviorFlags);
        Assert.Equal(
            WeaponBehavior.Charge | WeaponBehavior.Pierce | WeaponBehavior.Ricochet |
            WeaponBehavior.Knockback | WeaponBehavior.RampDamage | WeaponBehavior.SplitShot |
            WeaponBehavior.Cluster | WeaponBehavior.Pull | WeaponBehavior.DamageOverTime |
            WeaponBehavior.ChainField | WeaponBehavior.Stun | WeaponBehavior.Homing |
            WeaponBehavior.Returning | WeaponBehavior.WeakPointBonus,
            authoredBehaviors);
    }

    [Fact]
    public void LootRollIsStableAndStaysInsideThreatItemPowerBand()
    {
        ContentCatalog catalog = LoadCatalog();
        EquipmentInstance first = LootGenerator.Generate(44, 900, 12, 3, ThreatTier.TierVII, catalog);
        EquipmentInstance second = LootGenerator.Generate(44, 900, 12, 3, ThreatTier.TierVII, catalog);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.WeaponBaseId, second.WeaponBaseId);
        Assert.Equal(first.EquipmentBaseId, second.EquipmentBaseId);
        Assert.Equal(first.Rarity, second.Rarity);
        Assert.Equal(first.ItemPower, second.ItemPower);
        Assert.Equal(first.Affixes, second.Affixes);
        Assert.InRange(first.ItemPower, 61, 70);
        Assert.Equal(RpgProgressionMath.AffixCount(first.Rarity), first.Affixes.Count);
    }

    [Fact]
    public void TwoHandedWeaponReservesBothHandsAndOneHandedItemsRemainIndependent()
    {
        ContentCatalog catalog = LoadCatalog();
        EquipmentInstance pulseA = WeaponItem("pulse-a", "pulse-sidearm");
        EquipmentInstance pulseB = WeaponItem("pulse-b", "ion-repeater");
        EquipmentInstance burst = WeaponItem("burst", "burst-carbine");
        Dictionary<string, EquipmentInstance> stash = new(StringComparer.OrdinalIgnoreCase)
        {
            [pulseA.Id] = pulseA,
            [pulseB.Id] = pulseB,
            [burst.Id] = burst,
        };
        EquipmentLoadout loadout = new();

        Assert.True(loadout.TryEquip(pulseA, EquipmentSlot.RightHand, stash, catalog, out _));
        Assert.True(loadout.TryEquip(pulseB, EquipmentSlot.LeftHand, stash, catalog, out _));
        Assert.NotEqual(loadout[EquipmentSlot.RightHand], loadout[EquipmentSlot.LeftHand]);
        Assert.True(loadout.TryEquip(burst, EquipmentSlot.RightHand, stash, catalog, out _));
        Assert.Equal("burst", loadout[EquipmentSlot.RightHand]);
        Assert.Equal("burst", loadout[EquipmentSlot.LeftHand]);
    }

    [Fact]
    public void TwoIssuedWeaponSetsSwapInPointThreeFiveSecondsAndCheckpointIndependently()
    {
        ContentCatalog catalog = LoadCatalog();
        RunConfiguration configuration = new()
        {
            ArenaId = "orbital-depot",
            StartingWeaponSetA = new WeaponSetLoadout
            {
                RightHand = StarterWeaponReference.Issue("pulse-sidearm"),
                LeftHand = StarterWeaponReference.Issue("ion-sprayer"),
            },
            StartingWeaponSetB = new WeaponSetLoadout
            {
                RightHand = StarterWeaponReference.Issue("longshot-rifle"),
            },
            GodModeEnabled = true,
        };
        using GameSimulation simulation = new(catalog, configuration);

        Assert.Equal("pulse-sidearm", simulation.Player.EffectiveRightHandWeapon.Definition.Id);
        Assert.Equal("ion-sprayer", simulation.Player.LeftHandWeapon?.Definition.Id);
        simulation.Step([Command(simulation, PlayerButtons.SwapWeaponSet)]);
        Assert.Equal(0, simulation.ActiveWeaponSetIndex);
        for (int tick = 0; tick < 21; tick++)
        {
            simulation.Step([]);
        }

        Assert.Equal(1, simulation.ActiveWeaponSetIndex);
        Assert.Equal("longshot-rifle", simulation.Player.EffectiveRightHandWeapon.Definition.Id);
        Assert.Same(simulation.Player.EffectiveRightHandWeapon, simulation.Player.LeftHandWeapon);
        RunCheckpoint checkpoint = Assert.IsType<RunCheckpoint>(simulation.CreateRunCheckpoint());
        Assert.Equal(RunCheckpoint.CurrentSchemaVersion, checkpoint.SchemaVersion);
        Assert.Equal(1, checkpoint.ActiveWeaponSetIndex);
        Assert.Equal(1, checkpoint.ActiveWeaponSlotIndex);
        Assert.Equal(WeaponQuickbarLoadout.SlotCount, checkpoint.WeaponQuickbar.Slots.Count);
        Assert.Equal(3, checkpoint.IssuedItemInstances.Count);

        using GameSimulation restored = new(catalog, configuration with { Checkpoint = checkpoint });
        Assert.Equal(1, restored.ActiveWeaponSetIndex);
        Assert.Equal("longshot-rifle", restored.Player.EffectiveRightHandWeapon.Definition.Id);
    }

    [Fact]
    public void PersistentStashItemCannotOccupyBothWeaponSets()
    {
        ContentCatalog catalog = LoadCatalog();
        EquipmentInstance pulse = WeaponItem("owned-pulse", "pulse-sidearm");
        StarterWeaponReference reference = new()
        {
            WeaponBaseId = "pulse-sidearm",
            ItemInstanceId = pulse.Id,
        };

        Assert.Throws<ArgumentException>(() => new GameSimulation(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            StartingStash = [pulse],
            StartingWeaponSetA = new WeaponSetLoadout { RightHand = reference },
            StartingWeaponSetB = new WeaponSetLoadout { RightHand = reference },
        }));
    }

    [Fact]
    public void TenSlotQuickbarSelectsZeroAsSlotTenAndCyclesPastEmptySlots()
    {
        ContentCatalog catalog = LoadCatalog();
        WeaponQuickbarLoadout quickbar = new();
        quickbar.Slots[0] = new WeaponPresetSlot
        {
            RightHand = StarterWeaponReference.Issue("pulse-sidearm"),
        };
        quickbar.Slots[9] = new WeaponPresetSlot
        {
            RightHand = StarterWeaponReference.Issue("longshot-rifle"),
        };
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            StartingWeaponQuickbar = quickbar,
            GodModeEnabled = true,
        });

        simulation.Step([new PlayerCommand(
            simulation.Tick + 1, simulation.Player.Id, Vector2.Zero, Vector2.Zero,
            PlayerButtons.None, WeaponQuickbarLoadout.SlotCount - 1)]);
        for (int tick = 0; tick < 21; tick++)
        {
            simulation.Step([]);
        }

        Assert.Equal(9, simulation.ActiveWeaponSlotIndex);
        Assert.Equal("longshot-rifle", simulation.Player.EffectiveRightHandWeapon.Definition.Id);
        simulation.Step([Command(simulation, PlayerButtons.SwapWeaponSet)]);
        for (int tick = 0; tick < 21; tick++)
        {
            simulation.Step([]);
        }
        Assert.Equal(0, simulation.ActiveWeaponSlotIndex);
    }

    [Theory]
    [InlineData(ItemRarity.Common, 1, 2, 0, 0)]
    [InlineData(ItemRarity.Uncommon, 11, 6, 1, 0)]
    [InlineData(ItemRarity.Rare, 21, 12, 2, 0)]
    [InlineData(ItemRarity.Epic, 31, 20, 3, 1)]
    [InlineData(ItemRarity.Legendary, 91, 60, 4, 2)]
    public void DismantleYieldMatchesRarityAndItemPowerBand(
        ItemRarity rarity, int itemPower, int scrap, int components, int cores)
    {
        EquipmentInstance item = WeaponItem("drop", "pulse-sidearm") with
        {
            Rarity = rarity,
            ItemPower = itemPower,
        };

        Assert.Equal(new CraftingMaterialBundle(scrap, components, cores),
            EquipmentCrafting.GetDismantleYield(item));
    }

    [Fact]
    public void InfusionConsumesExactBaseDuplicateAndPreservesTargetIdentityAndAffixes()
    {
        PlayerProgressionState progression = new()
        {
            HighestUnlockedThreatTier = ThreatTier.TierIII,
            Materials = new CraftingWallet { Scrap = 99, Components = 99, Cores = 99 },
        };
        EquipmentInstance target = WeaponItem("target", "pulse-sidearm") with
        {
            Rarity = ItemRarity.Epic,
            ItemPower = 12,
            Affixes = [new RolledAffix { AffixId = "damage", Value = 0.1f }],
        };
        EquipmentInstance donor = WeaponItem("donor", "pulse-sidearm") with { ItemPower = 29 };
        progression.Stash.AddRange([target, donor]);

        Assert.True(EquipmentCrafting.TryInfuse(
            progression, target.Id, donor.Id, new HashSet<string>(), out ItemUpgradeQuote? quote, out _));

        EquipmentInstance upgraded = Assert.Single(progression.Stash);
        Assert.Equal(target.Id, upgraded.Id);
        Assert.Equal(29, upgraded.ItemPower);
        Assert.Equal(target.Affixes, upgraded.Affixes);
        Assert.Equal(new CraftingMaterialBundle(15, 6, 1), quote?.Cost);
        Assert.Equal(84, progression.Materials.Scrap);
        Assert.Equal(93, progression.Materials.Components);
        Assert.Equal(98, progression.Materials.Cores);
    }

    [Fact]
    public void ContextualSecondaryInputFocusesSinglesAndFiresLeftWhileDualWielding()
    {
        ContextualSecondaryAction single = ContextualWeaponInput.Resolve(
            secondaryTriggerPressed: true, dedicatedFocusPressed: false, dualWielding: false);
        ContextualSecondaryAction dual = ContextualWeaponInput.Resolve(
            secondaryTriggerPressed: true, dedicatedFocusPressed: false, dualWielding: true);
        ContextualSecondaryAction dualFocus = ContextualWeaponInput.Resolve(
            secondaryTriggerPressed: false, dedicatedFocusPressed: true, dualWielding: true);

        Assert.True(single.Focus);
        Assert.False(single.FireLeft);
        Assert.False(dual.Focus);
        Assert.True(dual.FireLeft);
        Assert.True(dualFocus.Focus);
        Assert.False(dualFocus.FireLeft);
    }

    [Fact]
    public void PausedCharacterManagementPreservesWeaponResourceAndStartsNewAbilityOnCooldown()
    {
        ContentCatalog catalog = LoadCatalog();
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            GodModeEnabled = true,
        });
        WeaponState weapon = simulation.Player.EffectiveRightHandWeapon;
        Assert.True(weapon.TryFire());
        float energyAfterShot = weapon.Energy;
        PlayerProgressionState managed = new();
        managed.AbilityMastery.Abilities["barrier-pulse"] = new AbilityProgress
        {
            AbilityPoints = catalog.Abilities["barrier-pulse"].RequiredAbilityPoints,
            IsMastered = true,
        };
        managed.AbilityMastery.EquippedActiveAbilityIds.Add("barrier-pulse");
        simulation.SetPaused(true);

        Assert.True(simulation.ApplyCharacterManagement(
            managed, simulation.WeaponQuickbar.Clone(), out string? error), error);

        Assert.Equal(energyAfterShot, simulation.Player.EffectiveRightHandWeapon.Energy);
        Assert.Equal(catalog.Abilities["barrier-pulse"].CooldownSeconds,
            simulation.AbilityCooldowns["barrier-pulse"]);
        Assert.False(simulation.Player.IsAiming);
    }

    [Fact]
    public void PendingProgressionCommitsExactlyOnce()
    {
        ContentCatalog catalog = LoadCatalog();
        PlayerProgressionState progression = new();
        PendingRunProgression pending = new()
        {
            CommitId = "seed-44-encounter-1",
            Experience = 330,
            Equipment = [WeaponItem("reward-item", "pulse-sidearm")],
        };
        pending.ProficiencyExperience[WeaponFamily.Pulse] = 100;

        Assert.True(pending.Commit(progression, catalog));
        Assert.False(pending.Commit(progression, catalog));
        Assert.Equal(2, progression.Level);
        Assert.Single(progression.Stash);
        Assert.Equal(100, progression.Proficiencies.Get(WeaponFamily.Pulse).Experience);
    }

    [Fact]
    public void DualWieldHandsFireAndReloadIndependently()
    {
        ContentCatalog catalog = LoadCatalog();
        PlayerProgressionState progression = new();
        EquipmentInstance right = WeaponItem("right-pulse", "pulse-sidearm");
        EquipmentInstance left = WeaponItem("left-pulse", "pulse-sidearm");
        progression.Stash.AddRange([right, left]);
        progression.Loadout.EquippedItemIds[EquipmentSlot.RightHand] = right.Id;
        progression.Loadout.EquippedItemIds[EquipmentSlot.LeftHand] = left.Id;
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Progression = progression,
            StartingEquipment = progression.Loadout,
            StartingStash = progression.Stash,
            GodModeEnabled = true,
        });

        WeaponState rightHand = simulation.Player.EffectiveRightHandWeapon;
        WeaponState leftHand = Assert.IsType<WeaponState>(simulation.Player.LeftHandWeapon);
        float rightEnergy = rightHand.Energy;
        float leftEnergy = leftHand.Energy;
        simulation.Step([Command(simulation, PlayerButtons.FireLeft)]);

        Assert.Equal(rightEnergy, rightHand.Energy);
        Assert.True(leftHand.Energy < leftEnergy);
        Assert.NotSame(rightHand, leftHand);
    }

    [Fact]
    public void RecoveryCachePrecedesBoonAndCommitsTakenItemsOnce()
    {
        ContentCatalog catalog = LoadCatalog();
        PlayerProgressionState progression = new();
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Progression = progression,
            GodModeEnabled = true,
            IsFirstRun = true,
        });

        Assert.True(simulation.DebugCompleteCurrentStage());
        Assert.Equal(RunPhase.RecoveryLoot, simulation.RunPhase);
        Assert.Equal(2, simulation.RecoveryCache.Items.Count);
        simulation.CompleteRecovery();
        UpgradeDefinition choice = simulation.PendingUpgradeOffers[0];
        simulation.ChooseUpgrade(choice.Id);

        Assert.Equal(RunPhase.EncounterActive, simulation.RunPhase);
        Assert.Equal(2, progression.Stash.Count);
        Assert.Single(progression.CommittedRewardIds);
    }

    [Fact]
    public void ThreatTierScalesHealthWithoutChangingEncounterConcurrency()
    {
        ContentCatalog catalog = LoadCatalog();
        using GameSimulation tierOne = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Seed = 99,
            GodModeEnabled = true,
        });
        PlayerProgressionState veteran = new() { HighestUnlockedThreatTier = ThreatTier.TierX };
        using GameSimulation tierTen = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Seed = 99,
            ThreatTier = ThreatTier.TierX,
            Progression = veteran,
            GodModeEnabled = true,
        });

        EnemyState first = RunUntilEnemy(tierOne);
        EnemyState tenth = RunUntilEnemy(tierTen);
        Assert.Equal(first.Definition.Id, tenth.Definition.Id);
        Assert.Equal(first.MaximumHealth * 2.62f, tenth.MaximumHealth, 3);
        Assert.Equal(tierOne.CurrentEncounter?.MaximumConcurrentEnemies,
            tierTen.CurrentEncounter?.MaximumConcurrentEnemies);
    }

    [Fact]
    public void PlayerCapsAtLevelOneHundredAndMainMenuRespecRefundsRanks()
    {
        ContentCatalog catalog = LoadCatalog();
        PlayerProgressionState progression = new();
        int totalExperience = Enumerable.Range(1, 99).Sum(RpgProgressionMath.ExperienceToNextLevel);
        Assert.Equal(99, progression.AddExperience(totalExperience));
        Assert.Equal(100, progression.Level);
        Assert.Equal(99, progression.UnspentTalentPoints);

        Assert.True(progression.TrySpendTalent("arsenal-1", catalog, out _));
        Assert.Equal(98, progression.UnspentTalentPoints);
        progression.RespecFromMainMenu();
        Assert.Equal(99, progression.UnspentTalentPoints);
        Assert.Empty(progression.TalentRanks);
    }

    [Fact]
    public void DebugSandboxCanGrantRpgProgressAndSpawnEveryRarity()
    {
        ContentCatalog catalog = LoadCatalog();
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            GodModeEnabled = true,
        });

        simulation.DebugGrantRpgProgression();
        simulation.DebugSpawnLootShowcase();

        Assert.True(simulation.Progression.Level > 1);
        Assert.All(catalog.Abilities.Keys, abilityId =>
            Assert.True(simulation.Progression.AbilityMastery.IsMastered(abilityId)));
        ItemRarity[] dropped = simulation.Pickups
            .Where(pickup => pickup.Type == PickupType.Equipment)
            .Select(pickup => Assert.IsType<EquipmentInstance>(pickup.Equipment).Rarity)
            .Order()
            .ToArray();
        Assert.Equal(Enum.GetValues<ItemRarity>(), dropped);
    }

    [Fact]
    public void DebugArenaShowcaseSpawnsEveryReleaseEnemyAndRarity()
    {
        ContentCatalog catalog = LoadCatalog();
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            GodModeEnabled = true,
        });

        IReadOnlyList<EnemyState> spawned = simulation.DebugPopulateArenaShowcase();
        simulation.SetDebugAiFrozen(true);
        for (int tick = 0; tick < 120; tick++)
        {
            simulation.Step([]);
        }

        string[] expectedEnemies = catalog.Enemies.Values
            .Where(enemy => enemy.SchemaVersion >= 2)
            .Select(enemy => enemy.Id)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(expectedEnemies, spawned.Select(enemy => enemy.Definition.Id)
            .Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains(spawned, enemy => enemy.Definition.IsBoss);
        Assert.Equal(spawned.Count, simulation.Enemies.Count);
        Assert.Equal(Enum.GetValues<ItemRarity>(), simulation.Pickups
            .Where(pickup => pickup.Type == PickupType.Equipment)
            .Select(pickup => Assert.IsType<EquipmentInstance>(pickup.Equipment).Rarity)
            .Order());
    }

    [Fact]
    public void RunResultDeltasSurviveARewardCheckpointRestore()
    {
        ContentCatalog catalog = LoadCatalog();
        PlayerProgressionState progression = new();
        using GameSimulation first = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Progression = progression,
            GodModeEnabled = true,
        });
        Assert.True(first.DebugCompleteCurrentStage());
        first.CompleteRecovery();
        first.ChooseUpgrade(first.PendingUpgradeOffers[0].Id);
        RunCheckpoint checkpoint = Assert.IsType<RunCheckpoint>(first.CreateRunCheckpoint());

        using GameSimulation restored = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Progression = progression,
            StartingStash = progression.Stash,
            StartingEquipment = progression.Loadout,
            Checkpoint = checkpoint,
            GodModeEnabled = true,
        });

        Assert.Equal(2, restored.RunSnapshot?.EquipmentCollected);
        Assert.Equal(checkpoint.RunRarityTotals, restored.RunSnapshot?.RarityTotals);
        Assert.Equal(checkpoint.RunHighestItemPower, restored.RunSnapshot?.HighestItemPower);
    }

    private static PlayerCommand Command(GameSimulation simulation, PlayerButtons buttons) => new(
        simulation.Tick + 1,
        simulation.Player.Id,
        Vector2.Zero,
        Vector2.Zero,
        buttons);

    private static EnemyState RunUntilEnemy(GameSimulation simulation)
    {
        for (int tick = 0; tick < 240; tick++)
        {
            simulation.Step([]);
            EnemyState? enemy = simulation.Enemies.FirstOrDefault(candidate => !candidate.IsDead);
            if (enemy is not null)
            {
                return enemy;
            }
        }

        throw new Xunit.Sdk.XunitException("No enemy spawned during the test window.");
    }

    private static EquipmentInstance WeaponItem(string id, string weaponId) => new()
    {
        Id = id,
        WeaponBaseId = weaponId,
        DisplayName = weaponId,
        PrimarySlot = EquipmentSlot.RightHand,
        ItemPower = 1,
    };

    private static ContentCatalog LoadCatalog() => ContentCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
}
