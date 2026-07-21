using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Core.Simulation;

public enum RunPhase
{
    LegacyWaves,
    EncounterActive,
    RecoveryLoot,
    RewardSelection,
    BossActive,
    Victory,
    Defeat,
}

public sealed record RunConfiguration
{
    public GameMode Mode { get; init; } = GameMode.Arena;
    public string ArenaId { get; init; } = "training-ring";
    public string AdventureId { get; init; } = "null-signal";
    public int Seed { get; init; } = 1337;
    public DifficultyMode Difficulty { get; init; } = DifficultyMode.Normal;
    public string StartingWeaponId { get; init; } = "pulse-sidearm";
    public ThreatTier ThreatTier { get; init; } = ThreatTier.TierI;
    public EquipmentLoadout? StartingEquipment { get; init; }
    public WeaponQuickbarLoadout? StartingWeaponQuickbar { get; init; }
    public WeaponSetLoadout? StartingWeaponSetA { get; init; }
    public WeaponSetLoadout? StartingWeaponSetB { get; init; }
    public IReadOnlyList<EquipmentInstance>? StartingStash { get; init; }
    public PlayerProgressionState? Progression { get; init; }
    public bool GodModeEnabled { get; init; }
    public bool IsFirstRun { get; init; }
    public IReadOnlyCollection<string>? UnlockedUpgradeIds { get; init; }
    public RunCheckpoint? Checkpoint { get; init; }
    public AdventureCheckpoint? AdventureCheckpoint { get; init; }
}

public sealed record WeaponCheckpointState
{
    public required string WeaponId { get; init; }
    public int Magazine { get; init; }
    public int Reserve { get; init; }
    public float Energy { get; init; }
    public float Heat { get; init; }
    public float FireCooldownSeconds { get; init; }
    public float ReloadRemainingSeconds { get; init; }
    public float ReloadDurationSeconds { get; init; }
    public int BurstShotsRemaining { get; init; }
    public bool IsOverheated { get; init; }
    public float MagazineConsumptionAccumulator { get; init; }
}

public sealed record RunCheckpoint
{
    public const int CurrentSchemaVersion = 6;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public int Seed { get; init; }
    public required string ArenaId { get; init; }
    public int NextEncounterIndex { get; init; }
    public required string StartingWeaponId { get; init; }
    public ThreatTier ThreatTier { get; init; } = ThreatTier.TierI;
    public DifficultyMode Difficulty { get; init; } = DifficultyMode.Normal;
    public bool IsFirstRun { get; init; }
    public bool GodModeUsed { get; init; }
    public List<string> OwnedUpgradeIds { get; init; } = [];
    public List<string> CollectedWeaponIds { get; init; } = [];
    public List<WeaponCheckpointState> WeaponStates { get; init; } = [];
    public string? SelectedWeaponId { get; init; }
    public List<string> ActiveArmoryWeaponIds { get; init; } = [];
    public List<string> NewlyUnlockedIds { get; init; } = [];
    public List<string> ProfileUnlockBaselineIds { get; init; } = [];
    public float? PlayerHealth { get; init; }
    public float? PlayerMaximumHealth { get; init; }
    public float? PlayerArmor { get; init; }
    public float? PlayerMaximumArmor { get; init; }
    public float? PlayerSecondsSinceDamage { get; init; }
    public uint SimulationTick { get; init; }
    public uint RandomState { get; init; }
    public float ElapsedRunSeconds { get; init; }
    public int Score { get; init; }
    public int Kills { get; init; }
    public float DamageTaken { get; init; }
    public int CloseRangeKills { get; init; }
    public int LongRangeKills { get; init; }
    public int SectorsCompleted { get; init; }
    public int CompletedPurgeEncounters { get; init; }
    public int CompletedRelayEncounters { get; init; }
    public int CompletedEliteEncounters { get; init; }
    public EncounterObjectiveType? LastCompletedEncounterObjective { get; init; }
    public float LastCompletedEncounterMetric { get; init; }
    public EquipmentLoadout EquipmentLoadout { get; init; } = new();
    public List<EquipmentInstance> EquipmentItems { get; init; } = [];
    public Dictionary<EquipmentSlot, WeaponCheckpointState> HandWeaponStates { get; init; } = [];
    public WeaponSetLoadout WeaponSetA { get; init; } = new();
    public WeaponSetLoadout WeaponSetB { get; init; } = new();
    public int ActiveWeaponSetIndex { get; init; }
    public WeaponQuickbarLoadout WeaponQuickbar { get; init; } = new();
    public int ActiveWeaponSlotIndex { get; init; }
    public Dictionary<int, WeaponSetCheckpointState> WeaponSetStates { get; init; } = [];
    public List<EquipmentInstance> IssuedItemInstances { get; init; } = [];
    public PendingRunProgression PendingProgression { get; init; } = new();
    public RecoveryCache RecoveryCache { get; init; } = new();
    public int LootDropSerial { get; init; }
    public Dictionary<string, float> AbilityCooldowns { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int RunExperienceEarned { get; init; }
    public int RunLevelsGained { get; init; }
    public Dictionary<WeaponFamily, int> RunProficiencyExperience { get; init; } = [];
    public List<string> RunAbilitiesMastered { get; init; } = [];
    public List<string> RunCollectedItemIds { get; init; } = [];
    public Dictionary<ItemRarity, int> RunRarityTotals { get; init; } = [];
    public int RunHighestItemPower { get; init; }
}

public sealed record WeaponSetCheckpointState
{
    public WeaponCheckpointState? RightHand { get; init; }
    public WeaponCheckpointState? LeftHand { get; init; }
}

public sealed record UpgradeOffer(int EncounterNumber, IReadOnlyList<UpgradeDefinition> Choices);

public sealed record RunSnapshot(
    int Seed,
    RunPhase Phase,
    int EncounterIndex,
    int TotalEncounters,
    int SectorNumber,
    string? SectorId,
    EncounterObjectiveType? ObjectiveType,
    IReadOnlyList<string> OwnedUpgradeIds,
    bool GodModeUsed)
{
    public ThreatTier ThreatTier { get; init; } = ThreatTier.TierI;
    public DifficultyMode Difficulty { get; init; } = DifficultyMode.Normal;
    public int ActiveWeaponSetIndex { get; init; }
    public int ActiveWeaponSlotIndex { get; init; }
    public int PlayerLevel { get; init; } = 1;
    public int PlayerExperience { get; init; }
    public int PlayerLevelsGained { get; init; }
    public int ExperienceGained { get; init; }
    public Dictionary<WeaponFamily, int> ProficiencyRanks { get; init; } = [];
    public Dictionary<WeaponFamily, int> ProficiencyExperienceGained { get; init; } = [];
    public List<string> AbilitiesMastered { get; init; } = [];
    public Dictionary<ItemRarity, int> RarityTotals { get; init; } = [];
    public int EquipmentCollected { get; init; }
    public int HighestItemPower { get; init; }
}

public sealed class RunModifiers
{
    private readonly Dictionary<string, UpgradeDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, WeaponDefinition> _weapons;
    private readonly HashSet<string> _ownedUpgradeIds = new(StringComparer.OrdinalIgnoreCase);

    public RunModifiers(
        IEnumerable<UpgradeDefinition> definitions,
        IReadOnlyDictionary<string, WeaponDefinition>? weapons = null)
    {
        _definitions = definitions.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        _weapons = weapons ?? new Dictionary<string, WeaponDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> OwnedUpgradeIds => _ownedUpgradeIds;
    public float MaximumHealthBonus => Sum(UpgradeEffectType.MaximumHealth);
    public float EncounterHealing => Sum(UpgradeEffectType.EncounterHealing);
    public bool HasEmergencyBarrier => Has(UpgradeEffectType.EmergencyBarrier);
    public float PickupRadiusMultiplier => Product(UpgradeEffectType.PickupRadius);
    public float PickupAmountMultiplier => Product(UpgradeEffectType.PickupAmount);
    public float IncomingDamageMultiplier => Product(UpgradeEffectType.IncomingDamage);
    public float KillMovementSpeedMultiplier => Product(UpgradeEffectType.KillMovementSpeed);

    public bool Apply(string upgradeId)
    {
        if (!_definitions.ContainsKey(upgradeId))
        {
            throw new ArgumentException($"Unknown upgrade '{upgradeId}'.", nameof(upgradeId));
        }

        return _ownedUpgradeIds.Add(upgradeId);
    }

    public float DamageMultiplier(string weaponId, float distance)
    {
        float multiplier = Product(UpgradeEffectType.GlobalDamage);
        multiplier *= ProductForWeapon(UpgradeEffectType.WeaponDamage, weaponId);
        if (distance <= 8f)
        {
            multiplier *= Product(UpgradeEffectType.CloseRangeDamage);
        }

        if (distance >= 18f)
        {
            multiplier *= Product(UpgradeEffectType.LongRangeDamage);
        }

        return multiplier;
    }

    public float AmmoCostMultiplier(string weaponId) =>
        ProductForWeapon(UpgradeEffectType.WeaponAmmoCost, weaponId);

    public int BurstCountBonus(string weaponId) =>
        (int)MathF.Round(SumForWeapon(UpgradeEffectType.WeaponBurstRounds, weaponId));

    public float SpreadMultiplier(string weaponId) => ProductForWeapon(UpgradeEffectType.WeaponSpread, weaponId);
    public float FalloffStartMultiplier(string weaponId) => ProductForWeapon(UpgradeEffectType.WeaponFalloffStart, weaponId);
    public float HeatGenerationMultiplier(string weaponId) => ProductForWeapon(UpgradeEffectType.WeaponHeatGeneration, weaponId);
    public float CoolingMultiplier(string weaponId) =>
        ProductForWeapon(UpgradeEffectType.WeaponCooling, weaponId) * Product(UpgradeEffectType.ReloadAndRecovery);
    public float SplashRadiusMultiplier(string weaponId) => ProductForWeapon(UpgradeEffectType.WeaponSplashRadius, weaponId);
    public float ProjectileSpeedMultiplier(string weaponId) => ProductForWeapon(UpgradeEffectType.WeaponProjectileSpeed, weaponId);
    public int ChainTargetBonus(string weaponId) =>
        (int)MathF.Round(SumForWeapon(UpgradeEffectType.WeaponChainTargets, weaponId));
    public float ChainRadiusMultiplier(string weaponId) => ProductForWeapon(UpgradeEffectType.WeaponChainRadius, weaponId);
    public float FireIntervalMultiplier => Product(UpgradeEffectType.FireInterval);
    public float CapacityMultiplier => Product(UpgradeEffectType.WeaponCapacity);
    public float ReloadTimeMultiplier => Effects(UpgradeEffectType.ReloadAndRecovery)
        .Aggregate(1f, (value, effect) => value * MathF.Max(0.1f, 2f - effect.Value));
    public float RecoveryMultiplier => Product(UpgradeEffectType.ReloadAndRecovery);

    private bool Has(UpgradeEffectType type) => Effects(type).Any();
    private float Sum(UpgradeEffectType type) => Effects(type).Sum(effect => effect.Value);
    private float Product(UpgradeEffectType type) => Effects(type).Aggregate(1f, (value, effect) => value * effect.Value);
    private float SumForWeapon(UpgradeEffectType type, string weaponId) => Effects(type)
        .Where(effect => MatchesWeapon(effect, weaponId))
        .Sum(effect => effect.Value);
    private float ProductForWeapon(UpgradeEffectType type, string weaponId) => Effects(type)
        .Where(effect => MatchesWeapon(effect, weaponId))
        .Aggregate(1f, (value, effect) => value * effect.Value);

    private IEnumerable<UpgradeEffectDefinition> Effects(UpgradeEffectType type) => _ownedUpgradeIds
        .Select(id => _definitions[id])
        .SelectMany(definition => definition.Effects)
        .Where(effect => effect.Type == type);

    private bool MatchesWeapon(UpgradeEffectDefinition effect, string weaponId) =>
        string.Equals(effect.WeaponId, weaponId, StringComparison.OrdinalIgnoreCase) ||
        (effect.WeaponFamily != WeaponFamily.None && _weapons.TryGetValue(weaponId, out WeaponDefinition? weapon) &&
            weapon.Family == effect.WeaponFamily);
}

public sealed class RunDirector
{
    public const int EncounterCount = 9;

    private readonly IReadOnlyDictionary<string, UpgradeDefinition> _upgrades;
    private readonly HashSet<string> _unlockedUpgradeIds;
    private readonly List<EncounterDefinition> _encounters;

    public RunDirector(
        int seed,
        IReadOnlyList<ArenaSectorDefinition> availableSectors,
        IEnumerable<UpgradeDefinition> upgrades,
        IEnumerable<string>? unlockedUpgradeIds = null,
        RunCheckpoint? checkpoint = null,
        bool isFirstRun = false,
        IReadOnlyDictionary<string, WeaponDefinition>? weapons = null)
    {
        if (availableSectors.Count < 3)
        {
            throw new ArgumentException("A roguelite run requires at least three authored sectors.", nameof(availableSectors));
        }

        Seed = seed;
        _upgrades = upgrades.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        _unlockedUpgradeIds = new HashSet<string>(
            unlockedUpgradeIds ?? StandardUpgradeCatalog.InitiallyUnlockedIds,
            StringComparer.OrdinalIgnoreCase);
        Modifiers = new RunModifiers(_upgrades.Values, weapons);
        int restoredEncounterIndex = Math.Clamp(
            checkpoint?.NextEncounterIndex ?? 0,
            0,
            EncounterCount);
        if (checkpoint is not null)
        {
            // A valid autosave owns exactly one reward per completed encounter. Cap a
            // directly supplied/semantically corrupt checkpoint as a final safety net so it
            // cannot exhaust the offer pool and crash the next completion.
            foreach (string upgradeId in checkpoint.OwnedUpgradeIds
                .Where(_upgrades.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(restoredEncounterIndex))
            {
                Modifiers.Apply(upgradeId);
            }
        }

        IsFirstRun = checkpoint?.IsFirstRun ?? isFirstRun;
        SelectedSectors = SelectSectors(seed, availableSectors, IsFirstRun);
        _encounters = BuildEncounters(seed, SelectedSectors, IsFirstRun);
        CurrentEncounterIndex = Math.Clamp(restoredEncounterIndex, 0, _encounters.Count);

        Phase = CurrentEncounterIndex == _encounters.Count ? RunPhase.BossActive : RunPhase.EncounterActive;
        GodModeUsed = checkpoint?.GodModeUsed ?? false;
    }

    public int Seed { get; }
    public bool IsFirstRun { get; }
    public RunPhase Phase { get; private set; }
    public int CurrentEncounterIndex { get; private set; }
    public IReadOnlyList<ArenaSectorDefinition> SelectedSectors { get; }
    public IReadOnlyList<EncounterDefinition> Encounters => _encounters;
    public EncounterDefinition? CurrentEncounter => CurrentEncounterIndex < _encounters.Count
        ? _encounters[CurrentEncounterIndex]
        : null;
    public UpgradeOffer? CurrentOffer { get; private set; }
    public RunModifiers Modifiers { get; }
    public bool GodModeUsed { get; private set; }

    public UpgradeOffer CompleteEncounter()
    {
        if (Phase != RunPhase.EncounterActive || CurrentEncounter is null)
        {
            throw new InvalidOperationException("Only an active encounter can be completed.");
        }

        List<UpgradeDefinition> choices = _unlockedUpgradeIds
            .Where(id => _upgrades.ContainsKey(id) && !Modifiers.OwnedUpgradeIds.Contains(id))
            .Select(id => _upgrades[id])
            .OrderBy(definition => StableHash(Seed, CurrentEncounterIndex, definition.Id))
            .ThenBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        if (choices.Count != 3)
        {
            throw new InvalidOperationException("A reward requires three unique unlocked, unowned upgrades.");
        }

        CurrentOffer = new UpgradeOffer(CurrentEncounterIndex + 1, choices);
        Phase = RunPhase.RecoveryLoot;
        return CurrentOffer;
    }

    public UpgradeOffer CompleteRecovery()
    {
        if (Phase != RunPhase.RecoveryLoot || CurrentOffer is null)
        {
            throw new InvalidOperationException("There is no active recovery stage.");
        }

        Phase = RunPhase.RewardSelection;
        return CurrentOffer;
    }

    public UpgradeDefinition ChooseUpgrade(string upgradeId)
    {
        if (Phase != RunPhase.RewardSelection || CurrentOffer is null)
        {
            throw new InvalidOperationException("There is no active upgrade offer.");
        }

        UpgradeDefinition selected = CurrentOffer.Choices.FirstOrDefault(definition =>
            string.Equals(definition.Id, upgradeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Upgrade '{upgradeId}' is not in the current offer.", nameof(upgradeId));
        if (!Modifiers.Apply(selected.Id))
        {
            throw new InvalidOperationException($"Upgrade '{upgradeId}' has already been selected.");
        }

        CurrentOffer = null;
        CurrentEncounterIndex++;
        Phase = CurrentEncounterIndex >= _encounters.Count ? RunPhase.BossActive : RunPhase.EncounterActive;
        return selected;
    }

    public void CompleteBoss()
    {
        if (Phase != RunPhase.BossActive)
        {
            throw new InvalidOperationException("The boss is not active.");
        }

        Phase = RunPhase.Victory;
    }

    public void Fail() => Phase = RunPhase.Defeat;
    public void MarkGodModeUsed() => GodModeUsed = true;

    public RunSnapshot CreateSnapshot() => new(
        Seed,
        Phase,
        CurrentEncounterIndex,
        _encounters.Count,
        Math.Min(3, (CurrentEncounterIndex / 3) + 1),
        CurrentEncounter?.SectorId,
        CurrentEncounter?.ObjectiveType,
        Modifiers.OwnedUpgradeIds.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        GodModeUsed);

    public RunCheckpoint CreateCheckpoint(string arenaId, string startingWeaponId) => new()
    {
        Seed = Seed,
        ArenaId = arenaId,
        StartingWeaponId = startingWeaponId,
        IsFirstRun = IsFirstRun,
        NextEncounterIndex = CurrentEncounterIndex,
        GodModeUsed = GodModeUsed,
        OwnedUpgradeIds = Modifiers.OwnedUpgradeIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
    };

    private static ArenaSectorDefinition[] SelectSectors(
        int seed,
        IReadOnlyList<ArenaSectorDefinition> sectors,
        bool isFirstRun)
    {
        if (isFirstRun)
        {
            return sectors.Take(3).ToArray();
        }

        return sectors
            .OrderBy(sector => StableHash(seed, -1, sector.Id))
            .ThenBy(sector => sector.Id, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    private static List<EncounterDefinition> BuildEncounters(
        int seed,
        IReadOnlyList<ArenaSectorDefinition> sectors,
        bool isFirstRun)
    {
        List<EncounterDefinition> encounters = [];
        for (int sectorIndex = 0; sectorIndex < sectors.Count; sectorIndex++)
        {
            ArenaSectorDefinition sector = sectors[sectorIndex];
            EncounterObjectiveType[] objectiveTypes =
            [
                EncounterObjectiveType.Purge,
                EncounterObjectiveType.RelayDefense,
                EncounterObjectiveType.EliteHunt,
            ];
            if (!(isFirstRun && sectorIndex == 0))
            {
                objectiveTypes = objectiveTypes
                    .OrderBy(type => StableHash(seed, sectorIndex, type.ToString()))
                    .ToArray();
            }

            int sectorNumber = sectorIndex + 1;
            float threatBudget = sectorNumber switch
            {
                1 => 10f,
                2 => 16f,
                _ => 24f,
            };
            int maximumConcurrent = sectorNumber switch
            {
                1 => 4,
                2 => 6,
                _ => 8,
            };
            float relaySeconds = sectorNumber switch
            {
                1 => 60f,
                2 => 75f,
                _ => 90f,
            };

            for (int objectiveIndex = 0; objectiveIndex < objectiveTypes.Length; objectiveIndex++)
            {
                EncounterObjectiveType type = objectiveTypes[objectiveIndex];
                encounters.Add(new EncounterDefinition
                {
                    Id = $"{sector.Id}-{objectiveIndex + 1}-{type.ToString().ToLowerInvariant()}",
                    SectorId = sector.Id,
                    ObjectiveType = type,
                    SectorNumber = sectorNumber,
                    ThreatBudget = threatBudget,
                    MaximumConcurrentEnemies = maximumConcurrent,
                    RelayDurationSeconds = relaySeconds,
                    PressureWaveCount = type switch
                    {
                        EncounterObjectiveType.Purge => sectorNumber switch
                        {
                            1 => 8,
                            2 => 12,
                            _ => 14,
                        },
                        EncounterObjectiveType.EliteHunt => sectorNumber + 1,
                        _ => 1,
                    },
                    PressureWaveDelaySeconds = 2f,
                });
            }
        }

        return encounters;
    }

    private static uint StableHash(int seed, int salt, string value)
    {
        uint hash = 2166136261;
        hash = (hash ^ (uint)seed) * 16777619;
        hash = (hash ^ (uint)salt) * 16777619;
        foreach (char character in value)
        {
            hash = (hash ^ character) * 16777619;
        }

        return hash;
    }
}
