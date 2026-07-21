using System.Numerics;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Input;

namespace FpsFrenzy.Core.Simulation;

public sealed class GameSimulation : IDisposable
{
    public const float FixedDeltaSeconds = 1f / 60f;
    public const float PlayerMoveSpeed = 7.5f;
    public const float PlayerEyeHeight = 1.65f;

    private static readonly IReadOnlySet<string> NoUpgradeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private sealed record QuickbarCandidate(
        StarterWeaponReference Reference,
        WeaponDefinition Weapon,
        int SourceSlot,
        bool IsRightHand);

    private readonly ContentCatalog _catalog;
    private readonly RunConfiguration _configuration;
    private readonly WaveSetDefinition _waveSet;
    private readonly List<WaveDefinition> _runWaves;
    private readonly RunDirector? _runDirector;
    private readonly NavigationGrid _navigation;
    private uint _randomState;
    private readonly BepuPlayerController _playerController;
    private readonly List<CombatEvent> _combatEvents = [];
    private readonly List<string> _queuedSummons = [];
    private readonly List<EntityId> _queuedRecycles = [];
    private readonly Queue<GeneratedEnemySpawn> _generatedSpawnQueue = [];
    private readonly Queue<WeaponFamily> _guaranteedWeaponFamilies = [];
    private int _nextEntityValue = 2;
    private int _pendingEnemies;
    private float _spawnRemainingSeconds;
    private float _interWaveRemainingSeconds = 1.5f;
    private int _spawnCursor;
    private int _spawnGroupCursor;
    private int _pendingInSpawnGroup;
    private bool _waveActive;
    private bool _generatedEncounterStarted;
    private bool _generatedBossStarted;
    private bool _awaitingArmoryCollection;
    private float _generatedSpawnRemainingSeconds;
    private int _generatedEncounterEnemyTotal;
    private int _generatedPressureWaveIndex;
    private int _generatedFiniteWavesRemaining;
    private float _generatedWaveBreakSeconds;
    private bool _generatedWaveBreakActive;
    private float _encounterElapsedSeconds;
    private int _completedPurgeEncounters;
    private int _completedRelayEncounters;
    private int _completedEliteEncounters;
    private int _bossSummonPortalCursor;
    private EntityId _eliteTargetId;
    private bool _emergencyBarrierAvailable;
    private float _emergencyBarrierSeconds;
    private float _damageAppliedThisTick;
    private PlayerButtons _previousButtons;
    private int _lootDropSerial;
    private PendingRunProgression _pendingProgression;
    private RecoveryCache _recoveryCache;
    private readonly Dictionary<string, EquipmentInstance> _equipmentItems;
    private readonly Dictionary<string, float> _abilityCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<WeaponState, float> _chargeSeconds = [];
    private readonly Dictionary<WeaponState, int> _rampShots = [];
    private readonly RuntimeWeaponSet[] _weaponSets = Enumerable.Range(0, WeaponQuickbarLoadout.SlotCount)
        .Select(_ => new RuntimeWeaponSet()).ToArray();
    private readonly Dictionary<string, WeaponState> _weaponStatesByItemId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<EntityId> _dismissedWeaponPickups = [];
    private PendingWeaponPickupDecision? _pendingWeaponPickupDecision;
    private int _runExperienceEarned;
    private int _runLevelsGained;
    private readonly Dictionary<WeaponFamily, int> _runProficiencyExperience = [];
    private readonly HashSet<string> _runAbilitiesMastered = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _runCollectedItemIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ItemRarity, int> _runRarityTotals = [];
    private int _runHighestItemPower;

    public GameSimulation(ContentCatalog catalog, RunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        _catalog = catalog;
        Difficulty = DifficultyCatalog.Normalize(configuration.Checkpoint?.Difficulty ?? configuration.Difficulty);
        ThreatTier = configuration.Checkpoint?.ThreatTier ?? configuration.ThreatTier;
        if (ThreatTier is < ThreatTier.TierI or > ThreatTier.TierX)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration), "Threat tier must be Tier I-X.");
        }

        Progression = configuration.Progression ?? new PlayerProgressionState();
        if (ThreatTier > Progression.HighestUnlockedThreatTier && configuration.Progression is not null)
        {
            throw new ArgumentException("The selected threat tier has not been unlocked.", nameof(configuration));
        }

        _pendingProgression = configuration.Checkpoint?.PendingProgression ?? CreatePendingProgression(0);
        _recoveryCache = configuration.Checkpoint?.RecoveryCache ?? new RecoveryCache();
        _lootDropSerial = Math.Max(0, configuration.Checkpoint?.LootDropSerial ?? 0);
        if (configuration.Checkpoint is RunCheckpoint progressionCheckpoint)
        {
            _runExperienceEarned = Math.Max(0, progressionCheckpoint.RunExperienceEarned);
            _runLevelsGained = Math.Max(0, progressionCheckpoint.RunLevelsGained);
            foreach ((WeaponFamily family, int amount) in progressionCheckpoint.RunProficiencyExperience)
            {
                _runProficiencyExperience[family] = Math.Max(0, amount);
            }
            _runAbilitiesMastered.UnionWith(progressionCheckpoint.RunAbilitiesMastered);
            _runCollectedItemIds.UnionWith(progressionCheckpoint.RunCollectedItemIds);
            foreach ((ItemRarity rarity, int count) in progressionCheckpoint.RunRarityTotals)
            {
                _runRarityTotals[rarity] = Math.Max(0, count);
            }
            _runHighestItemPower = Math.Max(0, progressionCheckpoint.RunHighestItemPower);
        }
        IEnumerable<EquipmentInstance> equipmentItems = configuration.Checkpoint?.EquipmentItems.Count > 0
            ? configuration.Checkpoint.EquipmentItems
            : configuration.StartingStash ?? Progression.Stash;
        equipmentItems = equipmentItems.Concat(configuration.Checkpoint?.IssuedItemInstances ?? []);
        _equipmentItems = equipmentItems
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        EquipmentLoadout = configuration.Checkpoint?.EquipmentLoadout.EquippedItemIds.Count > 0
            ? configuration.Checkpoint.EquipmentLoadout.Clone()
            : configuration.StartingEquipment?.Clone() ?? Progression.Loadout.Clone();
        if (configuration.Checkpoint is not null)
        {
            foreach ((string abilityId, float cooldown) in configuration.Checkpoint.AbilityCooldowns)
            {
                _abilityCooldowns[abilityId] = NonNegativeFinite(cooldown);
            }
        }
        string arenaId = configuration.Checkpoint?.ArenaId is string checkpointArenaId &&
            catalog.Arenas.ContainsKey(checkpointArenaId)
                ? checkpointArenaId
                : configuration.ArenaId;
        Arena = catalog.Arenas[arenaId];
        _waveSet = catalog.WaveSets[Arena.WaveSetId];
        _runWaves = _waveSet.BossWave is null ? _waveSet.Waves : [.. _waveSet.Waves, _waveSet.BossWave];
        string startingWeaponId = configuration.Checkpoint?.StartingWeaponId is string checkpointWeaponId &&
            catalog.Weapons.ContainsKey(checkpointWeaponId)
                ? checkpointWeaponId
                : configuration.StartingWeaponId;
        if (!catalog.Weapons.TryGetValue(startingWeaponId, out WeaponDefinition? startingWeapon))
        {
            throw new ArgumentException($"Unknown starting weapon '{startingWeaponId}'.", nameof(configuration));
        }

        WeaponSetLoadout legacySet = CreateLegacyWeaponSet(EquipmentLoadout, startingWeaponId);
        WeaponQuickbarLoadout requestedQuickbar = configuration.Checkpoint?.WeaponQuickbar is
                { Slots.Count: WeaponQuickbarLoadout.SlotCount } checkpointQuickbar &&
                checkpointQuickbar.Slots.Any(slot => !slot.IsEmpty)
            ? checkpointQuickbar
            : configuration.StartingWeaponQuickbar ?? WeaponQuickbarLoadout.FromLegacy(
                configuration.Checkpoint?.WeaponSetA is { IsEmpty: false } checkpointSetA
                    ? checkpointSetA
                    : configuration.StartingWeaponSetA ?? legacySet,
                configuration.Checkpoint?.WeaponSetB is { IsEmpty: false } checkpointSetB
                    ? checkpointSetB
                    : configuration.StartingWeaponSetB ?? new WeaponSetLoadout());
        WeaponQuickbar = NormalizeWeaponQuickbar(requestedQuickbar);
        WeaponSetA = WeaponQuickbar[0].ToWeaponSet();
        WeaponSetB = WeaponQuickbar[1].ToWeaponSet();

        CurrentWaveIndex = 0;
        _navigation = new NavigationGrid(Arena);
        int randomSeed = configuration.Checkpoint?.Seed ?? configuration.Seed;
        _randomState = configuration.Checkpoint?.RandomState is > 0u and uint savedRandomState
            ? savedRandomState
            : CreateInitialRandomState(randomSeed);
        _playerController = new BepuPlayerController(Arena);
        bool hasReleaseRoster = catalog.Enemies.Values.Any(enemy => enemy.SchemaVersion >= 2 && !enemy.IsBoss) &&
            catalog.Enemies.Values.Any(enemy => enemy.SchemaVersion >= 2 && enemy.IsBoss);
        if (Arena.Sectors.Count >= 3 && hasReleaseRoster)
        {
            _runDirector = new RunDirector(
                randomSeed,
                Arena.Sectors,
                catalog.Upgrades.Values,
                configuration.UnlockedUpgradeIds,
                configuration.Checkpoint,
                configuration.IsFirstRun,
                catalog.Weapons);
        }

        SetPlayerInvulnerable(configuration.GodModeEnabled);

        Player = new PlayerState
        {
            Id = new EntityId(1),
            Position = Arena.PlayerSpawn,
            PreviousPosition = Arena.PlayerSpawn,
            MaximumHealth = 100f + (_runDirector?.Modifiers.MaximumHealthBonus ?? 0f),
            Health = 100f + (_runDirector?.Modifiers.MaximumHealthBonus ?? 0f),
        };
        InitializeWeaponSets(configuration, startingWeapon);
        Player.MaximumHealth += CalculatePermanentMaximumHealthBonus();
        Player.Health = Player.MaximumHealth;
        if (configuration.Checkpoint is not null)
        {
            foreach (string weaponId in configuration.Checkpoint.CollectedWeaponIds)
            {
                if (Player.Weapons.Any(weapon => weapon.Definition.Id.Equals(
                        weaponId, StringComparison.OrdinalIgnoreCase)) ||
                    !catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? collectedWeapon))
                {
                    continue;
                }

                Player.Weapons.Add(new WeaponState(collectedWeapon, _runDirector?.Modifiers));
            }

            int selectedIndex = Player.Weapons.FindIndex(weapon => weapon.Definition.Id.Equals(
                configuration.Checkpoint.SelectedWeaponId,
                StringComparison.OrdinalIgnoreCase));
            if (selectedIndex >= 0)
            {
                Player.SelectedWeaponIndex = selectedIndex;
            }

            foreach (WeaponCheckpointState weaponState in configuration.Checkpoint.WeaponStates)
            {
                WeaponState? weapon = Player.Weapons.FirstOrDefault(candidate =>
                    candidate.Definition.Id.Equals(weaponState.WeaponId, StringComparison.OrdinalIgnoreCase));
                weapon?.RestoreCheckpointState(weaponState);
            }

            RestoreCheckpointState(configuration.Checkpoint);
        }

        _emergencyBarrierAvailable = _runDirector?.Modifiers.HasEmergencyBarrier ?? false;

        foreach (PickupSpawnDefinition spawn in Arena.PickupSpawns)
        {
            Pickups.Add(new PickupState
            {
                Id = NextEntity(),
                Type = spawn.Type,
                Position = spawn.Position,
                Amount = spawn.Amount,
                WeaponId = spawn.WeaponId,
                RespawnSeconds = spawn.RespawnSeconds,
            });
        }

        if (configuration.Checkpoint is not null)
        {
            foreach (string weaponId in configuration.Checkpoint.ActiveArmoryWeaponIds
                .Where(weaponId => catalog.Weapons.ContainsKey(weaponId) &&
                    Player.Weapons.All(weapon => !weapon.Definition.Id.Equals(
                        weaponId, StringComparison.OrdinalIgnoreCase))))
            {
                AddArmoryPickup(weaponId);
            }

            _awaitingArmoryCollection = Pickups.Any(IsActiveArmoryPickup);
            if (_awaitingArmoryCollection)
            {
                TeleportPlayerToRecoveryHub();
            }
        }
    }

    public ArenaDefinition Arena { get; }
    public PlayerState Player { get; }
    public List<EnemyState> Enemies { get; } = [];
    public List<PickupState> Pickups { get; } = [];
    public List<ProjectileState> Projectiles { get; } = [];
    public List<PendingEnemySpawn> PendingEnemySpawns { get; } = [];
    public GamePhase Phase { get; private set; } = GamePhase.Playing;
    public DifficultyMode Difficulty { get; }
    public ThreatTier ThreatTier { get; }
    public PlayerProgressionState Progression { get; }
    public PendingRunProgression PendingProgression => _pendingProgression;
    public PendingWeaponPickupDecision? PendingWeaponPickupDecision => _pendingWeaponPickupDecision;
    public RecoveryCache RecoveryCache => _recoveryCache;
    public EquipmentLoadout EquipmentLoadout { get; }
    public WeaponSetLoadout WeaponSetA { get; private set; } = new();
    public WeaponSetLoadout WeaponSetB { get; private set; } = new();
    public WeaponQuickbarLoadout WeaponQuickbar { get; private set; } = new();
    public int ActiveWeaponSlotIndex => Player.ActiveWeaponSetIndex;
    public int ActiveWeaponSetIndex => Player.ActiveWeaponSetIndex;
    public IReadOnlyList<bool> PopulatedWeaponSlots => WeaponQuickbar.Slots
        .Select(slot => !slot.IsEmpty).ToArray();
    public RuntimeWeaponSet GetWeaponSlotState(int slotIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slotIndex, WeaponQuickbarLoadout.SlotCount);
        return _weaponSets[slotIndex];
    }
    public EquipmentInstance? GetWeaponSlotEquipment(int slotIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slotIndex, WeaponQuickbarLoadout.SlotCount);
        return GetWeaponSlotItem(slotIndex);
    }
    public IReadOnlyDictionary<string, EquipmentInstance> EquipmentItems => _equipmentItems;
    public IReadOnlyDictionary<string, float> AbilityCooldowns => _abilityCooldowns;
    public global::FpsFrenzy.Core.Simulation.RunPhase RunPhase =>
        _runDirector?.Phase ?? global::FpsFrenzy.Core.Simulation.RunPhase.LegacyWaves;
    public RunSnapshot? RunSnapshot
    {
        get
        {
            if (_runDirector is null)
            {
                return null;
            }

            return _runDirector.CreateSnapshot() with
            {
                ThreatTier = ThreatTier,
                PlayerLevel = Progression.Level,
                PlayerExperience = Progression.Experience,
                PlayerLevelsGained = _runLevelsGained,
                ExperienceGained = _runExperienceEarned,
                ProficiencyRanks = Progression.Proficiencies.Families.ToDictionary(
                    pair => pair.Key, pair => pair.Value.Rank),
                ProficiencyExperienceGained = new Dictionary<WeaponFamily, int>(_runProficiencyExperience),
                AbilitiesMastered = _runAbilitiesMastered.Order(StringComparer.OrdinalIgnoreCase).ToList(),
                RarityTotals = new Dictionary<ItemRarity, int>(_runRarityTotals),
                EquipmentCollected = _runCollectedItemIds.Count,
                HighestItemPower = _runHighestItemPower,
                Difficulty = Difficulty,
                ActiveWeaponSetIndex = ActiveWeaponSetIndex,
                ActiveWeaponSlotIndex = ActiveWeaponSlotIndex,
            };
        }
    }
    public EncounterDefinition? CurrentEncounter => _runDirector?.CurrentEncounter;
    public ArenaSectorDefinition? ActiveSector => _runDirector?.CurrentEncounter is { } encounter
        ? _runDirector.SelectedSectors[encounter.SectorNumber - 1]
        : null;
    public UpgradeOffer? CurrentUpgradeOffer => _runDirector?.CurrentOffer;
    public RunModifiers? RunModifiers => _runDirector?.Modifiers;
    public RelayObjectiveState? RelayObjective { get; private set; }
    public int RunSeed => _runDirector?.Seed ?? _configuration.Seed;
    public int EncounterNumber => _runDirector is null ? CurrentWaveIndex + 1 :
        Math.Min(RunDirector.EncounterCount + 1, _runDirector.CurrentEncounterIndex + 1);
    public int CurrentSectorNumber => _runDirector?.CurrentEncounter?.SectorNumber ??
        (_runDirector?.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.BossActive ? 4 : 0);
    public int SectorsCompleted => _runDirector is null ? 0 :
        (_runDirector.CurrentEncounterIndex +
            (_runDirector.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.RewardSelection ? 1 : 0)) / 3;
    public IReadOnlySet<string> OwnedUpgradeIds => _runDirector?.Modifiers.OwnedUpgradeIds ??
        NoUpgradeIds;
    public IReadOnlyList<UpgradeDefinition> PendingUpgradeOffers =>
        _runDirector?.CurrentOffer?.Choices ?? [];
    public IReadOnlyList<string> CollectedWeaponIds => Player.Weapons
        .Select(weapon => weapon.Definition.Id)
        .ToArray();
    public string SelectedWeaponId => Player.CurrentWeapon.Definition.Id;
    public bool AwaitingArmoryCollection => _awaitingArmoryCollection;
    public bool DebugAiFrozen { get; private set; }
    public bool DebugShowcaseActive { get; private set; }
    public float ObjectiveProgress => CalculateObjectiveProgress();
    public int CurrentPressureWaveNumber => _generatedPressureWaveIndex;

    public bool ApplyCharacterManagement(
        PlayerProgressionState managedProgression,
        WeaponQuickbarLoadout managedQuickbar,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(managedProgression);
        ArgumentNullException.ThrowIfNull(managedQuickbar);
        if (Phase != GamePhase.Paused)
        {
            error = "Character management can only be applied while gameplay is paused.";
            return false;
        }

        try
        {
            HashSet<string> previousActives = Progression.AbilityMastery.EquippedActiveAbilityIds
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Progression.Level = managedProgression.Level;
            Progression.Experience = managedProgression.Experience;
            Progression.UnspentTalentPoints = managedProgression.UnspentTalentPoints;
            ReplaceDictionary(Progression.TalentRanks, managedProgression.TalentRanks);
            ReplaceDictionary(Progression.Proficiencies.Families, managedProgression.Proficiencies.Families);
            ReplaceDictionary(Progression.AbilityMastery.Abilities, managedProgression.AbilityMastery.Abilities);
            Progression.AbilityMastery.EquippedActiveAbilityIds.Clear();
            Progression.AbilityMastery.EquippedActiveAbilityIds.AddRange(
                managedProgression.AbilityMastery.EquippedActiveAbilityIds);
            Progression.AbilityMastery.EquippedPassiveAbilityIds.Clear();
            Progression.AbilityMastery.EquippedPassiveAbilityIds.AddRange(
                managedProgression.AbilityMastery.EquippedPassiveAbilityIds);
            Progression.HighestUnlockedThreatTier = managedProgression.HighestUnlockedThreatTier;
            Progression.Materials.Scrap = managedProgression.Materials.Scrap;
            Progression.Materials.Components = managedProgression.Materials.Components;
            Progression.Materials.Cores = managedProgression.Materials.Cores;
            Progression.Stash.Clear();
            Progression.Stash.AddRange(managedProgression.Stash);
            ReplaceDictionary(Progression.Loadout.EquippedItemIds,
                managedProgression.Loadout.EquippedItemIds);
            Progression.CommittedRewardIds.Clear();
            Progression.CommittedRewardIds.UnionWith(managedProgression.CommittedRewardIds);

            foreach (string abilityId in Progression.AbilityMastery.EquippedActiveAbilityIds
                .Where(id => !previousActives.Contains(id)))
            {
                if (_catalog.Abilities.TryGetValue(abilityId, out EquipmentAbilityDefinition? ability))
                {
                    _abilityCooldowns[abilityId] = MathF.Max(
                        _abilityCooldowns.GetValueOrDefault(abilityId), ability.CooldownSeconds);
                }
            }

            foreach (string itemId in _equipmentItems.Values.Where(item => !item.IsRunBound)
                .Select(item => item.Id).ToArray())
            {
                _equipmentItems.Remove(itemId);
            }
            foreach (EquipmentInstance item in managedProgression.Stash)
            {
                _equipmentItems[item.Id] = item;
            }
            ReplaceDictionary(EquipmentLoadout.EquippedItemIds,
                managedProgression.Loadout.EquippedItemIds);

            WeaponQuickbar = NormalizeWeaponQuickbar(managedQuickbar);
            WeaponSetA = WeaponQuickbar[0].ToWeaponSet();
            WeaponSetB = WeaponQuickbar[1].ToWeaponSet();
            foreach (RuntimeWeaponSet set in _weaponSets)
            {
                set.RightHand = null;
                set.LeftHand = null;
                set.RightHandItemId = null;
                set.LeftHandItemId = null;
            }
            HashSet<string> occupiedPersistentItems = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < WeaponQuickbarLoadout.SlotCount; index++)
            {
                InitializeWeaponSet(index, WeaponQuickbar[index].ToWeaponSet(), checkpoint: null,
                    occupiedPersistentItems);
            }
            foreach ((string itemId, WeaponState state) in _weaponStatesByItemId)
            {
                if (_equipmentItems.TryGetValue(itemId, out EquipmentInstance? item))
                {
                    ApplyPersistentWeaponStats(state, item, refill: false);
                }
            }
            int active = Player.ActiveWeaponSetIndex is >= 0 and < WeaponQuickbarLoadout.SlotCount &&
                _weaponSets[Player.ActiveWeaponSetIndex].RightHand is not null
                    ? Player.ActiveWeaponSetIndex
                    : FindNextPopulatedWeaponSlot(Player.ActiveWeaponSetIndex, 1);
            ActivateWeaponSet(active, emitEvent: false);
            Player.IsAiming = false;
            float previousHealth = Player.Health;
            Player.MaximumHealth = 100f + CalculatePermanentMaximumHealthBonus() +
                (_runDirector?.Modifiers.MaximumHealthBonus ?? 0f);
            Player.Health = Math.Clamp(previousHealth, 0f, Player.MaximumHealth);
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void ReplaceDictionary<TKey, TValue>(
        IDictionary<TKey, TValue> target,
        IEnumerable<KeyValuePair<TKey, TValue>> source)
        where TKey : notnull
    {
        target.Clear();
        foreach ((TKey key, TValue value) in source)
        {
            target[key] = value;
        }
    }
    public int CurrentPressureWaveTotal => _runDirector?.CurrentEncounter is EncounterDefinition encounter &&
        encounter.ObjectiveType is EncounterObjectiveType.Purge or EncounterObjectiveType.EliteHunt
            ? encounter.PressureWaveCount
            : 0;
    public float PressureWaveBreakRemainingSeconds => _generatedWaveBreakActive
        ? _generatedWaveBreakSeconds
        : 0f;
    public bool GodModeEnabled { get; private set; }
    public uint Tick { get; private set; }
    public int CurrentWaveIndex { get; private set; }
    public int TotalWaves => _runDirector is null ? _runWaves.Count : RunDirector.EncounterCount + 1;
    public int Score { get; private set; }
    public int Kills { get; private set; }
    public float DamageTaken { get; private set; }
    public int CloseRangeKills { get; private set; }
    public int LongRangeKills { get; private set; }
    public int CompletedPurgeEncounters => _completedPurgeEncounters;
    public int CompletedRelayEncounters => _completedRelayEncounters;
    public int CompletedEliteEncounters => _completedEliteEncounters;
    public EncounterObjectiveType? LastCompletedEncounterObjective { get; private set; }
    public float LastCompletedEncounterMetric { get; private set; }
    public float ElapsedRunSeconds { get; private set; }
    public float InterWaveRemainingSeconds => _interWaveRemainingSeconds;
    public int RemainingEnemies => Enemies.Count(enemy => !enemy.IsDead) + _pendingEnemies +
        _generatedSpawnQueue.Count + PendingEnemySpawns.Count;
    public float LastShotSeconds { get; private set; } = 99f;
    public float LastHitSeconds { get; private set; } = 99f;
    public float LastKillSeconds { get; private set; } = 99f;
    public float PlayerDamageFlashSeconds { get; private set; }
    public IReadOnlyList<CombatEvent> CombatEvents => _combatEvents;
    public bool IsBossWave => _runDirector is null
        ? CurrentWaveIndex == _runWaves.Count - 1 && _waveSet.BossWave is not null
        : _runDirector.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.BossActive;
    public EnemyState? ActiveBoss => Enemies.FirstOrDefault(enemy => enemy.Definition.IsBoss && !enemy.IsDead);

    public void Step(ReadOnlySpan<PlayerCommand> commands, float fixedDeltaSeconds = FixedDeltaSeconds)
    {
        if (fixedDeltaSeconds <= 0f || fixedDeltaSeconds > 0.1f)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
        }

        _combatEvents.Clear();
        _damageAppliedThisTick = 0f;
        PlayerCommand command = commands.Length > 0
            ? commands[0]
            : new PlayerCommand(Tick + 1, Player.Id, Vector2.Zero, Vector2.Zero, PlayerButtons.None, -1);
        bool pausePressed = command.Has(PlayerButtons.Pause) && !_previousButtons.HasFlag(PlayerButtons.Pause);
        if (pausePressed && Phase is GamePhase.Playing or GamePhase.Paused)
        {
            Phase = Phase == GamePhase.Paused ? GamePhase.Playing : GamePhase.Paused;
        }

        if (Phase == GamePhase.Paused)
        {
            _previousButtons = command.Buttons;
            return;
        }

        if (Phase is GamePhase.Victory or GamePhase.Defeat)
        {
            foreach (EnemyState enemy in Enemies)
            {
                if (enemy.IsDead)
                {
                    enemy.DeathSeconds += fixedDeltaSeconds;
                }
            }

            CleanupEnemies();
            _previousButtons = command.Buttons;
            return;
        }

        if (_runDirector?.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.RewardSelection)
        {
            _previousButtons = command.Buttons;
            return;
        }

        Tick++;
        ElapsedRunSeconds += fixedDeltaSeconds;
        LastShotSeconds += fixedDeltaSeconds;
        LastHitSeconds += fixedDeltaSeconds;
        LastKillSeconds += fixedDeltaSeconds;
        PlayerDamageFlashSeconds = MathF.Max(0f, PlayerDamageFlashSeconds - fixedDeltaSeconds);
        _emergencyBarrierSeconds = MathF.Max(0f, _emergencyBarrierSeconds - fixedDeltaSeconds);
        Player.AdrenalSeconds = MathF.Max(0f, Player.AdrenalSeconds - fixedDeltaSeconds);
        Player.PreviousPosition = Player.Position;
        UpdatePlayer(command, fixedDeltaSeconds);
        UpdateAbilities(command, fixedDeltaSeconds);
        if (_runDirector?.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.RecoveryLoot &&
            command.Has(PlayerButtons.Interact) && !_previousButtons.HasFlag(PlayerButtons.Interact))
        {
            CompleteRecovery();
            _previousButtons = command.Buttons;
            return;
        }
        UpdateWeapons(command, fixedDeltaSeconds);
        if (DebugShowcaseActive)
        {
            // The arena lab owns its roster and loot; campaign portals/objectives stay dormant.
        }
        else if (_runDirector is null)
        {
            UpdateWaveDirector(fixedDeltaSeconds);
        }
        else
        {
            UpdateRunDirector(fixedDeltaSeconds);
        }
        if (Phase == GamePhase.Playing)
        {
            if (!DebugAiFrozen)
            {
                UpdateEnemies(fixedDeltaSeconds);
            }
            UpdateProjectiles(fixedDeltaSeconds);
            UpdatePickups(command, fixedDeltaSeconds);
            TryCompleteGeneratedBoss();
        }
        CleanupEnemies();

        if (Phase == GamePhase.Playing && Player.Health <= 0f)
        {
            Player.Health = 0f;
            if (_runDirector is not null)
            {
                Vector3 objectivePosition = RelayObjective?.Position ??
                    ActiveSector?.ObjectiveAnchor ?? Arena.BossArenaAnchor;
                string objectiveId = _runDirector.CurrentEncounter?.Id ?? "breach-walker";
                AddEvent(CombatEventType.EncounterFailed, objectivePosition, Player.Position,
                    EntityId.None, Player.Id, objectiveId);
            }

            Phase = GamePhase.Defeat;
            _runDirector?.Fail();
            CommitFinalProgression(includeWorldEquipment: false);
        }

        _previousButtons = command.Buttons;
    }

    public void SetPaused(bool paused)
    {
        if (Phase is GamePhase.Victory or GamePhase.Defeat)
        {
            return;
        }

        Phase = paused ? GamePhase.Paused : GamePhase.Playing;
        if (paused)
        {
            Player.IsAiming = false;
        }
        _previousButtons &= ~PlayerButtons.Pause;
    }

    public void SetPlayerInvulnerable(bool invulnerable)
    {
        GodModeEnabled = invulnerable;
        if (invulnerable)
        {
            _runDirector?.MarkGodModeUsed();
        }
    }

    /// <summary>
    /// Grants a deterministic block of RPG progress inside the non-persistent debug sandbox.
    /// </summary>
    public void DebugGrantRpgProgression()
    {
        Progression.AddExperience(2_500);
        foreach (WeaponFamily family in Enum.GetValues<WeaponFamily>().Where(family => family != WeaponFamily.None))
        {
            Progression.Proficiencies.AddExperience(family, 1_000);
        }
        foreach (EquipmentAbilityDefinition ability in _catalog.Abilities.Values)
        {
            Progression.AbilityMastery.AddAbilityPoints(ability.Id, ability.RequiredAbilityPoints, _catalog);
        }
        AddEvent(CombatEventType.ProgressionCommitted, Player.Position, Player.Position,
            Player.Id, Player.Id, "debug-rpg-grant", 2_500f);
    }

    /// <summary>
    /// Equips a controller-testable pair of active abilities in the non-persistent debug sandbox.
    /// The pair is mastered and its cooldowns are reset so each selection can be tested immediately.
    /// </summary>
    public bool DebugEquipActiveAbilities(string ability1Id, string ability2Id)
    {
        string[] abilityIds = [ability1Id, ability2Id];
        if (abilityIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != abilityIds.Length ||
            abilityIds.Any(id => !_catalog.Abilities.TryGetValue(id, out EquipmentAbilityDefinition? ability) ||
                ability.Kind != AbilityKind.Active))
        {
            return false;
        }

        foreach (string abilityId in abilityIds)
        {
            EquipmentAbilityDefinition ability = _catalog.Abilities[abilityId];
            Progression.AbilityMastery.AddAbilityPoints(
                abilityId,
                ability.RequiredAbilityPoints,
                _catalog);
        }

        bool equipped = Progression.AbilityMastery.TrySetLoadout(
            abilityIds,
            [],
            Progression.Level,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            _catalog,
            out _);
        if (equipped)
        {
            _abilityCooldowns.Clear();
        }
        return equipped;
    }

    /// <summary>
    /// Replaces the canonical family slot with a run-bound debug weapon without rebuilding the sandbox.
    /// </summary>
    public bool DebugEquipWeapon(string weaponId)
    {
        if (!_catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? weapon) ||
            weapon.Family == WeaponFamily.None)
        {
            return false;
        }

        int slotIndex = WeaponQuickbarLoadout.SlotForFamily(weapon.Family);
        EquipmentInstance item = new()
        {
            Id = $"debug-{slotIndex}-{weapon.Id}",
            WeaponBaseId = weapon.Id,
            DisplayName = weapon.DisplayName,
            PrimarySlot = EquipmentSlot.RightHand,
            Rarity = ItemRarity.Common,
            ItemPower = RpgProgressionMath.MinimumItemPower(ThreatTier),
            IsLocked = true,
            IsRunBound = true,
        };
        return EquipWeaponItem(item, activateIfCurrent: true, forceActivate: true);
    }

    public bool ResolveWeaponPickup(WeaponPickupDecisionAction action)
    {
        PendingWeaponPickupDecision? decision = _pendingWeaponPickupDecision;
        if (decision is null)
        {
            return false;
        }

        PickupState? pickup = Pickups.FirstOrDefault(candidate => candidate.Id == decision.PickupId);
        if (pickup is null || !pickup.IsAvailable)
        {
            _pendingWeaponPickupDecision = null;
            SetPaused(false);
            return false;
        }

        bool resolved = action switch
        {
            WeaponPickupDecisionAction.Replace => CollectAndEquipWeaponPickup(pickup, decision.OfferedItem),
            WeaponPickupDecisionAction.Dismantle => DismantleWeaponPickup(pickup, decision.OfferedItem),
            WeaponPickupDecisionAction.Leave => DismissWeaponPickup(pickup),
            _ => false,
        };
        if (resolved)
        {
            _pendingWeaponPickupDecision = null;
            SetPaused(false);
        }
        return resolved;
    }

    private bool CollectAndEquipWeaponPickup(PickupState pickup, EquipmentInstance item)
    {
        int slotIndex = WeaponSlotForItem(item);
        bool wasActive = slotIndex == Player.ActiveWeaponSetIndex;
        if (!RegisterCollectedWeapon(item) ||
            !EquipWeaponItem(item, activateIfCurrent: wasActive, forceActivate: false))
        {
            return false;
        }
        ConsumeWeaponPickup(pickup, item);
        return true;
    }

    private bool DismantleWeaponPickup(PickupState pickup, EquipmentInstance item)
    {
        CraftingMaterialBundle value = EquipmentCrafting.GetDismantleYield(item);
        CraftingMaterialBundle current = _pendingProgression.DismantledMaterials;
        _pendingProgression.DismantledMaterials = new CraftingMaterialBundle(
            current.Scrap + value.Scrap,
            current.Components + value.Components,
            current.Cores + value.Cores);
        Pickups.Remove(pickup);
        _dismissedWeaponPickups.Remove(pickup.Id);
        if (pickup.Type == PickupType.Weapon)
        {
            _awaitingArmoryCollection = Pickups.Any(IsActiveArmoryPickup);
        }
        AddEvent(CombatEventType.WeaponDismantled, pickup.Position, Player.Position,
            pickup.Id, Player.Id, item.Id, item.ItemPower);
        return true;
    }

    private bool DismissWeaponPickup(PickupState pickup)
    {
        _dismissedWeaponPickups.Add(pickup.Id);
        return true;
    }

    private bool EquipWeaponItem(EquipmentInstance item, bool activateIfCurrent, bool forceActivate)
    {
        if (item.WeaponBaseId is not string weaponId ||
            !_catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? weapon) ||
            weapon.Family == WeaponFamily.None)
        {
            return false;
        }

        int slotIndex = WeaponQuickbarLoadout.SlotForFamily(weapon.Family);
        _equipmentItems[item.Id] = item;
        StarterWeaponReference reference = new()
        {
            WeaponBaseId = weapon.Id,
            ItemInstanceId = item.Id,
        };
        WeaponPresetSlot replacement = new()
        {
            RightHand = reference,
            LeftHand = weapon.Handedness == Handedness.TwoHanded ? reference : null,
        };
        WeaponSetLoadout normalized = NormalizeWeaponSet(replacement.ToWeaponSet(), slotIndex);
        WeaponQuickbar.Slots[slotIndex] = WeaponPresetSlot.FromWeaponSet(normalized);
        WeaponSetA = WeaponQuickbar[0].ToWeaponSet();
        WeaponSetB = WeaponQuickbar[1].ToWeaponSet();

        HashSet<string> occupiedPersistentItems = _weaponSets
            .Where((_, index) => index != slotIndex)
            .SelectMany(set => new[] { set.RightHandItemId, set.LeftHandItemId })
            .OfType<string>()
            .Where(id => _equipmentItems.TryGetValue(id, out EquipmentInstance? existing) && !existing.IsRunBound)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        RuntimeWeaponSet runtime = _weaponSets[slotIndex];
        runtime.RightHand = null;
        runtime.LeftHand = null;
        runtime.RightHandItemId = null;
        runtime.LeftHandItemId = null;
        InitializeWeaponSet(slotIndex, normalized, checkpoint: null, occupiedPersistentItems);
        if (forceActivate || activateIfCurrent)
        {
            ActivateWeaponSet(slotIndex, emitEvent: true);
        }
        return true;
    }

    private bool RegisterCollectedWeapon(EquipmentInstance item)
    {
        if (item.IsRunBound)
        {
            _equipmentItems[item.Id] = item;
            return true;
        }
        if (_pendingProgression.Equipment.Any(existing => existing.Id.Equals(
                item.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        _pendingProgression.Equipment.Add(item);
        _equipmentItems[item.Id] = item;
        return true;
    }

    private int WeaponSlotForItem(EquipmentInstance item) =>
        item.WeaponBaseId is string weaponId && _catalog.Weapons.TryGetValue(
            weaponId, out WeaponDefinition? weapon) && weapon.Family != WeaponFamily.None
                ? WeaponQuickbarLoadout.SlotForFamily(weapon.Family)
                : -1;

    private EquipmentInstance? GetWeaponSlotItem(int slotIndex)
    {
        string? itemId = slotIndex is >= 0 and < WeaponQuickbarLoadout.SlotCount
            ? _weaponSets[slotIndex].RightHandItemId
            : null;
        return itemId is not null && _equipmentItems.TryGetValue(itemId, out EquipmentInstance? item)
            ? item
            : null;
    }

    private void ConsumeWeaponPickup(PickupState pickup, EquipmentInstance item)
    {
        Pickups.Remove(pickup);
        _dismissedWeaponPickups.Remove(pickup.Id);
        AddEvent(CombatEventType.PickupCollected, pickup.Position, Player.Position,
            pickup.Id, Player.Id, pickup.Type.ToString(), Math.Max(1, pickup.Amount));
        AddEvent(CombatEventType.EquipmentCollected, pickup.Position, Player.Position,
            pickup.Id, Player.Id, item.Id, item.ItemPower);
        AddEvent(CombatEventType.WeaponCollected, pickup.Position, Player.Position,
            pickup.Id, Player.Id, item.WeaponBaseId, item.ItemPower);
        if (pickup.Type == PickupType.Weapon)
        {
            _awaitingArmoryCollection = Pickups.Any(IsActiveArmoryPickup);
        }
    }

    /// <summary>
    /// Drops one item of each rarity around the player for beam, interaction, comparison,
    /// recovery-cache, and distance-culling tests.
    /// </summary>
    public void DebugSpawnLootShowcase()
    {
        ItemRarity[] rarities = Enum.GetValues<ItemRarity>();
        for (int index = 0; index < rarities.Length; index++)
        {
            ItemRarity rarity = rarities[index];
            EquipmentInstance generated = GenerateLoot(-9_000 - index, rarity);
            EquipmentInstance item = generated with
            {
                Rarity = rarity,
                UniqueEffectId = rarity == ItemRarity.Legendary
                    ? generated.UniqueEffectId ?? "legendary-debug-showcase"
                    : null,
            };
            float angle = index * (MathF.Tau / rarities.Length);
            Vector3 position = Player.Position + new Vector3(MathF.Sin(angle) * 3f, 0f, MathF.Cos(angle) * 3f);
            AddEquipmentDrop(item, position);
        }
    }

    public PickupState DebugSpawnWeaponDrop(
        string weaponId,
        ItemRarity rarity = ItemRarity.Rare,
        int itemPower = 25)
    {
        if (!_catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? weapon) ||
            weapon.Family == WeaponFamily.None)
        {
            throw new ArgumentException($"Unknown playable debug weapon '{weaponId}'.", nameof(weaponId));
        }

        EquipmentInstance item = new()
        {
            Id = $"debug-drop-{_lootDropSerial++}-{weapon.Id}",
            WeaponBaseId = weapon.Id,
            DisplayName = weapon.DisplayName,
            PrimarySlot = EquipmentSlot.RightHand,
            Rarity = rarity,
            ItemPower = Math.Clamp(itemPower, 1, 100),
        };
        Vector3 openPosition = FindOpenDropPosition(Player.Position + new Vector3(0f, 0f, 0.25f));
        PickupState pickup = new()
        {
            Id = NextEntity(),
            Type = PickupType.Equipment,
            Position = new Vector3(openPosition.X, 0.2f, openPosition.Z),
            Equipment = item,
            IsDropped = true,
        };
        Pickups.Add(pickup);
        AddEvent(CombatEventType.EquipmentDropped, openPosition, Player.Position,
            EntityId.None, Player.Id, item.Id, item.ItemPower);
        return pickup;
    }

    public void SetDebugAiFrozen(bool frozen) => DebugAiFrozen = frozen;

    public EnemyState DebugSpawnEnemy(string enemyId)
    {
        if (!_catalog.Enemies.TryGetValue(enemyId, out EnemyDefinition? definition) || definition.IsBoss)
        {
            throw new ArgumentException($"Unknown non-boss debug enemy '{enemyId}'.", nameof(enemyId));
        }

        Vector3 forward = GetViewDirection();
        forward.Y = 0f;
        forward = forward.LengthSquared() < 0.001f ? -Vector3.UnitZ : Vector3.Normalize(forward);
        Vector3 spawn = Player.Position + (forward * 12f);
        spawn.X = Math.Clamp(spawn.X, Arena.BoundsMin.X + 1f, Arena.BoundsMax.X - 1f);
        spawn.Y = 0f;
        spawn.Z = Math.Clamp(spawn.Z, Arena.BoundsMin.Z + 1f, Arena.BoundsMax.Z - 1f);
        return SpawnEnemyAt(enemyId, spawn, isElite: false);
    }

    /// <summary>
    /// Populates the non-persistent arena lab with one of every release enemy and
    /// one equipment drop of every rarity. Call this on a fresh debug simulation.
    /// </summary>
    public IReadOnlyList<EnemyState> DebugPopulateArenaShowcase()
    {
        DebugShowcaseActive = true;
        EnemyDefinition[] definitions = _catalog.Enemies.Values
            .Where(definition => definition.SchemaVersion >= 2)
            .OrderBy(definition => definition.IsBoss)
            .ThenBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        List<EnemyState> spawned = new(definitions.Length);
        for (int index = 0; index < definitions.Length; index++)
        {
            float angle = index * (MathF.Tau / Math.Max(1, definitions.Length));
            float radius = definitions[index].IsBoss ? 18f : 13f + ((index & 1) * 2f);
            Vector3 position = Player.Position + new Vector3(
                MathF.Sin(angle) * radius,
                0f,
                MathF.Cos(angle) * radius);
            position.X = Math.Clamp(position.X, Arena.BoundsMin.X + 2f, Arena.BoundsMax.X - 2f);
            position.Y = 0f;
            position.Z = Math.Clamp(position.Z, Arena.BoundsMin.Z + 2f, Arena.BoundsMax.Z - 2f);
            spawned.Add(SpawnEnemyAt(definitions[index].Id, position, isElite: false));
        }

        DebugSpawnLootShowcase();
        return spawned;
    }

    public void DebugTeleportToSector(int sectorIndex)
    {
        if (Arena.Sectors.Count == 0)
        {
            return;
        }

        ArenaSectorDefinition sector = Arena.Sectors[Math.Abs(sectorIndex) % Arena.Sectors.Count];
        Vector3 position = sector.ObjectiveAnchor + new Vector3(0f, PlayerEyeHeight, 0f);
        _playerController.Teleport(position);
        Player.Position = position;
        Player.PreviousPosition = position;
        Player.VerticalVelocity = 0f;
        Player.IsGrounded = true;
    }

    /// <summary>
    /// Advances the active campaign stage for the local development harness.
    /// It deliberately uses the normal encounter completion path so reward and boss
    /// transitions can be exercised without waiting through an entire combat budget.
    /// </summary>
    public bool DebugCompleteCurrentStage()
    {
        if (_runDirector is null || Phase != GamePhase.Playing)
        {
            return false;
        }

        if (_runDirector.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.EncounterActive &&
            _runDirector.CurrentEncounter is EncounterDefinition encounter)
        {
            if (!_generatedEncounterStarted)
            {
                BeginGeneratedEncounter();
            }

            CompleteGeneratedEncounter(encounter);
            return true;
        }

        if (_runDirector.Phase != global::FpsFrenzy.Core.Simulation.RunPhase.BossActive)
        {
            return false;
        }

        foreach (EnemyState enemy in Enemies.Where(enemy => !enemy.IsDead))
        {
            enemy.Health = 0f;
            enemy.ActionState = EnemyActionState.Death;
        }

        PendingEnemySpawns.Clear();
        _generatedSpawnQueue.Clear();
        _queuedSummons.Clear();
        Projectiles.RemoveAll(projectile => projectile.IsHostile);
        _runDirector.CompleteBoss();
        UnlockNextThreatTier();
        CommitFinalProgression(includeWorldEquipment: true);
        Phase = GamePhase.Victory;
        AddEvent(CombatEventType.EncounterCompleted, Arena.BossArenaAnchor, Player.Position,
            EntityId.None, Player.Id, "breach-walker-debug");
        return true;
    }

    public UpgradeOffer CompleteRecovery(bool takeAll = true)
    {
        if (_runDirector is null || _runDirector.Phase !=
            global::FpsFrenzy.Core.Simulation.RunPhase.RecoveryLoot)
        {
            throw new InvalidOperationException("The run is not in recovery.");
        }

        if (takeAll)
        {
            _recoveryCache.TakeAll(_pendingProgression);
        }

        return _runDirector.CompleteRecovery();
    }

    public UpgradeDefinition ChooseUpgrade(string upgradeId)
    {
        if (_runDirector is null)
        {
            throw new InvalidOperationException("Upgrade choices are only available in a sector run.");
        }

        float previousMaximumHealth = Player.MaximumHealth;
        UpgradeDefinition selected = _runDirector.ChooseUpgrade(upgradeId);
        Player.MaximumHealth = 100f + CalculatePermanentMaximumHealthBonus() +
            _runDirector.Modifiers.MaximumHealthBonus;
        if (Player.MaximumHealth > previousMaximumHealth)
        {
            Player.Health = MathF.Min(Player.MaximumHealth,
                Player.Health + (Player.MaximumHealth - previousMaximumHealth));
        }

        _emergencyBarrierAvailable = _runDirector.Modifiers.HasEmergencyBarrier;
        if (CommitPendingProgression())
        {
            AddEvent(CombatEventType.ProgressionCommitted, Player.Position, Player.Position,
                Player.Id, Player.Id, _pendingProgression.CommitId, _pendingProgression.Experience);
        }

        _pendingProgression = CreatePendingProgression(_runDirector.CurrentEncounterIndex);
        _recoveryCache = new RecoveryCache();
        _awaitingArmoryCollection = Pickups.Any(IsActiveArmoryPickup);
        _generatedEncounterStarted = false;
        if (_runDirector.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.BossActive)
        {
            _generatedBossStarted = false;
        }

        AddEvent(CombatEventType.UpgradeApplied, Player.Position, Player.Position,
            Player.Id, Player.Id, selected.Id);
        return selected;
    }

    private PendingRunProgression CreatePendingProgression(int encounterIndex) => new()
    {
        CommitId = $"run-{_configuration.Seed}-tier-{(int)ThreatTier}-encounter-{encounterIndex + 1}",
    };

    private WeaponSetLoadout CreateLegacyWeaponSet(EquipmentLoadout loadout, string startingWeaponId)
    {
        StarterWeaponReference? ReferenceFor(EquipmentSlot slot)
        {
            string? itemId = loadout[slot];
            return itemId is not null && _equipmentItems.TryGetValue(itemId, out EquipmentInstance? item) &&
                item.WeaponBaseId is string weaponId
                    ? new StarterWeaponReference { WeaponBaseId = weaponId, ItemInstanceId = itemId }
                    : null;
        }

        StarterWeaponReference? right = ReferenceFor(EquipmentSlot.RightHand);
        StarterWeaponReference? left = ReferenceFor(EquipmentSlot.LeftHand);
        return new WeaponSetLoadout
        {
            RightHand = right ?? StarterWeaponReference.Issue(startingWeaponId),
            LeftHand = left,
        };
    }

    private WeaponSetLoadout NormalizeWeaponSet(WeaponSetLoadout requested, int setIndex)
    {
        ArgumentNullException.ThrowIfNull(requested);
        StarterWeaponReference? right = MaterializeReference(requested.RightHand, setIndex, "right");
        bool sharesUnmaterializedTwoHandedIssue = requested.RightHand is not null && requested.LeftHand is not null &&
            requested.RightHand.ItemInstanceId is null && requested.LeftHand.ItemInstanceId is null &&
            requested.RightHand.WeaponBaseId.Equals(requested.LeftHand.WeaponBaseId,
                StringComparison.OrdinalIgnoreCase) &&
            _catalog.Weapons.TryGetValue(requested.RightHand.WeaponBaseId, out WeaponDefinition? requestedWeapon) &&
            requestedWeapon.Handedness == Handedness.TwoHanded;
        StarterWeaponReference? left = sharesUnmaterializedTwoHandedIssue
            ? right
            : MaterializeReference(requested.LeftHand, setIndex, "left");
        WeaponDefinition? rightWeapon = ResolveWeapon(right);
        WeaponDefinition? leftWeapon = ResolveWeapon(left);

        if (rightWeapon?.Handedness == Handedness.TwoHanded)
        {
            if (leftWeapon is not null && !string.Equals(right?.ItemInstanceId, left?.ItemInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Weapon set {setIndex + 1} cannot pair a two-handed weapon.");
            }
            left = right;
        }
        else if (leftWeapon?.Handedness == Handedness.TwoHanded)
        {
            if (rightWeapon is not null && !string.Equals(right?.ItemInstanceId, left?.ItemInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Weapon set {setIndex + 1} cannot pair a two-handed weapon.");
            }
            right = left;
        }
        else if (right is not null && left is not null && string.Equals(
            right.ItemInstanceId, left.ItemInstanceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A persistent one-handed item instance cannot occupy multiple positions.");
        }

        return new WeaponSetLoadout { RightHand = right, LeftHand = left };
    }

    private WeaponQuickbarLoadout NormalizeWeaponQuickbar(WeaponQuickbarLoadout requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        List<QuickbarCandidate> candidates = [];
        for (int sourceSlot = 0; sourceSlot < WeaponQuickbarLoadout.SlotCount; sourceSlot++)
        {
            WeaponPresetSlot source = requested[sourceSlot];
            AddQuickbarCandidate(candidates, source.RightHand, sourceSlot, isRightHand: true);
            bool duplicateTwoHandedReference = source.RightHand is not null && source.LeftHand is not null &&
                string.Equals(source.RightHand.WeaponBaseId, source.LeftHand.WeaponBaseId,
                    StringComparison.OrdinalIgnoreCase) &&
                _catalog.Weapons.TryGetValue(source.RightHand.WeaponBaseId, out WeaponDefinition? shared) &&
                shared.Handedness == Handedness.TwoHanded &&
                (string.Equals(source.RightHand.ItemInstanceId, source.LeftHand.ItemInstanceId,
                    StringComparison.OrdinalIgnoreCase) || source.RightHand.ItemInstanceId is null &&
                    source.LeftHand.ItemInstanceId is null);
            if (!duplicateTwoHandedReference)
            {
                AddQuickbarCandidate(candidates, source.LeftHand, sourceSlot, isRightHand: false);
            }
        }

        List<WeaponPresetSlot> slots = Enumerable.Range(0, WeaponQuickbarLoadout.SlotCount)
            .Select(_ => new WeaponPresetSlot()).ToList();
        foreach (WeaponFamily family in WeaponQuickbarLoadout.FamilyOrder)
        {
            QuickbarCandidate[] familyCandidates = candidates
                .Where(candidate => candidate.Weapon.Family == family)
                .OrderByDescending(candidate => IsPersistentCandidate(candidate.Reference))
                .ThenByDescending(candidate => CandidateRarity(candidate.Reference))
                .ThenByDescending(candidate => CandidateItemPower(candidate.Reference))
                .ThenByDescending(candidate => candidate.IsRightHand)
                .ThenBy(candidate => candidate.SourceSlot)
                .ToArray();
            if (familyCandidates.Length == 0)
            {
                continue;
            }

            QuickbarCandidate primary = familyCandidates[0];
            StarterWeaponReference? left = null;
            if (primary.Weapon.Handedness == Handedness.TwoHanded)
            {
                left = primary.Reference;
            }
            else
            {
                left = familyCandidates.Skip(1)
                    .FirstOrDefault(candidate => candidate.Weapon.Handedness == Handedness.OneHanded &&
                        !SameItemReference(primary.Reference, candidate.Reference))?.Reference;
            }

            int targetSlot = WeaponQuickbarLoadout.SlotForFamily(family);
            WeaponSetLoadout normalized = NormalizeWeaponSet(new WeaponSetLoadout
            {
                RightHand = primary.Reference,
                LeftHand = left,
            }, targetSlot);
            slots[targetSlot] = WeaponPresetSlot.FromWeaponSet(normalized);
        }
        return new WeaponQuickbarLoadout { Slots = slots };
    }

    private void AddQuickbarCandidate(
        ICollection<QuickbarCandidate> candidates,
        StarterWeaponReference? reference,
        int sourceSlot,
        bool isRightHand)
    {
        if (reference is not null && _catalog.Weapons.TryGetValue(
                reference.WeaponBaseId, out WeaponDefinition? weapon) && weapon.Family != WeaponFamily.None)
        {
            candidates.Add(new QuickbarCandidate(reference, weapon, sourceSlot, isRightHand));
        }
    }

    private bool IsPersistentCandidate(StarterWeaponReference reference) =>
        reference.ItemInstanceId is string itemId &&
        _equipmentItems.TryGetValue(itemId, out EquipmentInstance? item) && !item.IsRunBound;

    private ItemRarity CandidateRarity(StarterWeaponReference reference) =>
        reference.ItemInstanceId is string itemId && _equipmentItems.TryGetValue(itemId, out EquipmentInstance? item)
            ? item.Rarity
            : ItemRarity.Common;

    private int CandidateItemPower(StarterWeaponReference reference) =>
        reference.ItemInstanceId is string itemId && _equipmentItems.TryGetValue(itemId, out EquipmentInstance? item)
            ? item.ItemPower
            : RpgProgressionMath.MinimumItemPower(ThreatTier);

    private static bool SameItemReference(StarterWeaponReference left, StarterWeaponReference right) =>
        left.ItemInstanceId is not null && right.ItemInstanceId is not null &&
        left.ItemInstanceId.Equals(right.ItemInstanceId, StringComparison.OrdinalIgnoreCase);

    private StarterWeaponReference? MaterializeReference(
        StarterWeaponReference? reference,
        int setIndex,
        string hand)
    {
        if (reference is null)
        {
            return null;
        }
        if (!_catalog.Weapons.TryGetValue(reference.WeaponBaseId, out WeaponDefinition? weapon))
        {
            throw new ArgumentException($"Unknown weapon base '{reference.WeaponBaseId}'.");
        }

        string itemId = reference.ItemInstanceId ??
            $"issued-{_configuration.Seed}-set-{setIndex + 1}-{hand}-{weapon.Id}";
        if (_equipmentItems.TryGetValue(itemId, out EquipmentInstance? existing))
        {
            if (!string.Equals(existing.WeaponBaseId, weapon.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Item '{itemId}' does not contain '{weapon.Id}'.");
            }
        }
        else
        {
            _equipmentItems[itemId] = new EquipmentInstance
            {
                Id = itemId,
                WeaponBaseId = weapon.Id,
                DisplayName = $"{weapon.DisplayName} (Armory Issue)",
                PrimarySlot = hand == "left" ? EquipmentSlot.LeftHand : EquipmentSlot.RightHand,
                Rarity = ItemRarity.Common,
                ItemPower = RpgProgressionMath.MinimumItemPower(ThreatTier),
                IsLocked = true,
                IsRunBound = true,
            };
        }

        return reference with { ItemInstanceId = itemId };
    }

    private WeaponDefinition? ResolveWeapon(StarterWeaponReference? reference) =>
        reference is not null && _catalog.Weapons.TryGetValue(reference.WeaponBaseId, out WeaponDefinition? weapon)
            ? weapon
            : null;

    private void InitializeWeaponSets(RunConfiguration configuration, WeaponDefinition startingWeapon)
    {
        HashSet<string> occupiedPersistentItems = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < WeaponQuickbarLoadout.SlotCount; index++)
        {
            InitializeWeaponSet(index, WeaponQuickbar[index].ToWeaponSet(), configuration.Checkpoint,
                occupiedPersistentItems);
        }
        if (_weaponSets.All(set => set.RightHand is null))
        {
            WeaponState fallback = new(startingWeapon, _runDirector?.Modifiers);
            Player.Weapons.Add(fallback);
            int fallbackSlot = startingWeapon.Family == WeaponFamily.None
                ? 0
                : WeaponQuickbarLoadout.SlotForFamily(startingWeapon.Family);
            _weaponSets[fallbackSlot].RightHand = fallback;
            _weaponSets[fallbackSlot].LeftHand = startingWeapon.Handedness == Handedness.TwoHanded ? fallback : null;
        }

        int activeSet = Math.Clamp(
            configuration.Checkpoint?.ActiveWeaponSlotIndex ?? configuration.Checkpoint?.ActiveWeaponSetIndex ?? 0,
            0,
            WeaponQuickbarLoadout.SlotCount - 1);
        if (_weaponSets[activeSet].RightHand is null)
        {
            activeSet = FindNextPopulatedWeaponSlot(activeSet, 1);
        }
        ActivateWeaponSet(activeSet, emitEvent: false);

        if (configuration.Checkpoint is not null && configuration.Checkpoint.WeaponSetStates.Count == 0)
        {
            foreach ((EquipmentSlot slot, WeaponCheckpointState savedState) in
                configuration.Checkpoint.HandWeaponStates)
            {
                WeaponState? handState = slot == EquipmentSlot.RightHand
                    ? Player.RightHandWeapon
                    : slot == EquipmentSlot.LeftHand ? Player.LeftHandWeapon : null;
                if (handState is not null && handState.Definition.Id.Equals(savedState.WeaponId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    handState.RestoreCheckpointState(savedState);
                }
            }
        }
    }

    private int FindNextPopulatedWeaponSlot(int current, int direction)
    {
        direction = direction < 0 ? -1 : 1;
        for (int offset = 1; offset <= _weaponSets.Length; offset++)
        {
            int candidate = (current + (offset * direction) + _weaponSets.Length) % _weaponSets.Length;
            if (_weaponSets[candidate].RightHand is not null)
            {
                return candidate;
            }
        }
        return Math.Clamp(current, 0, _weaponSets.Length - 1);
    }

    private void InitializeWeaponSet(
        int setIndex,
        WeaponSetLoadout loadout,
        RunCheckpoint? checkpoint,
        HashSet<string> occupiedPersistentItems)
    {
        RuntimeWeaponSet runtime = _weaponSets[setIndex];
        runtime.RightHandItemId = loadout.RightHand?.ItemInstanceId;
        runtime.LeftHandItemId = loadout.LeftHand?.ItemInstanceId;
        runtime.RightHand = CreateWeaponState(runtime.RightHandItemId, occupiedPersistentItems);
        runtime.LeftHand = string.Equals(runtime.RightHandItemId, runtime.LeftHandItemId,
                StringComparison.OrdinalIgnoreCase)
            ? runtime.RightHand
            : CreateWeaponState(runtime.LeftHandItemId, occupiedPersistentItems);

        if (checkpoint?.WeaponSetStates.TryGetValue(setIndex, out WeaponSetCheckpointState? saved) == true)
        {
            RestoreWeaponSetHand(runtime.RightHand, saved.RightHand);
            if (!ReferenceEquals(runtime.LeftHand, runtime.RightHand))
            {
                RestoreWeaponSetHand(runtime.LeftHand, saved.LeftHand);
            }
        }
    }

    private WeaponState? CreateWeaponState(string? itemId, HashSet<string> occupiedPersistentItems)
    {
        if (itemId is null || !_equipmentItems.TryGetValue(itemId, out EquipmentInstance? item) ||
            item.WeaponBaseId is null || !_catalog.Weapons.TryGetValue(item.WeaponBaseId, out WeaponDefinition? weapon))
        {
            return null;
        }
        if (!item.IsRunBound && !occupiedPersistentItems.Add(itemId))
        {
            throw new ArgumentException($"Persistent item '{itemId}' occupies multiple weapon-set positions.");
        }
        if (_weaponStatesByItemId.TryGetValue(itemId, out WeaponState? existing))
        {
            return existing;
        }

        WeaponState state = new(weapon, _runDirector?.Modifiers);
        ApplyPersistentWeaponStats(state, item, refill: true);
        _weaponStatesByItemId[itemId] = state;
        Player.Weapons.Add(state);
        return state;
    }

    private static void RestoreWeaponSetHand(WeaponState? weapon, WeaponCheckpointState? saved)
    {
        if (weapon is not null && saved is not null && weapon.Definition.Id.Equals(
            saved.WeaponId, StringComparison.OrdinalIgnoreCase))
        {
            weapon.RestoreCheckpointState(saved);
        }
    }

    private void RestoreCheckpointState(RunCheckpoint checkpoint)
    {
        float derivedMaximumHealth = Player.MaximumHealth;
        Player.MaximumHealth = checkpoint.PlayerMaximumHealth is float savedMaximumHealth &&
            float.IsFinite(savedMaximumHealth) && savedMaximumHealth > 0f
                ? savedMaximumHealth
                : derivedMaximumHealth;
        Player.Health = checkpoint.PlayerHealth is float savedHealth && float.IsFinite(savedHealth)
            ? Math.Clamp(savedHealth, 0f, Player.MaximumHealth)
            : Player.MaximumHealth;
        Tick = checkpoint.SimulationTick;
        ElapsedRunSeconds = NonNegativeFinite(checkpoint.ElapsedRunSeconds);
        Score = Math.Max(0, checkpoint.Score);
        Kills = Math.Max(0, checkpoint.Kills);
        DamageTaken = NonNegativeFinite(checkpoint.DamageTaken);
        CloseRangeKills = Math.Max(0, checkpoint.CloseRangeKills);
        LongRangeKills = Math.Max(0, checkpoint.LongRangeKills);
        LastCompletedEncounterObjective = checkpoint.LastCompletedEncounterObjective;
        LastCompletedEncounterMetric = NonNegativeFinite(checkpoint.LastCompletedEncounterMetric);
        CurrentWaveIndex = Math.Clamp(checkpoint.NextEncounterIndex, 0, RunDirector.EncounterCount);

        _completedPurgeEncounters = Math.Max(0, checkpoint.CompletedPurgeEncounters);
        _completedRelayEncounters = Math.Max(0, checkpoint.CompletedRelayEncounters);
        _completedEliteEncounters = Math.Max(0, checkpoint.CompletedEliteEncounters);
        if (_completedPurgeEncounters + _completedRelayEncounters + _completedEliteEncounters == 0 &&
            checkpoint.NextEncounterIndex > 0 && _runDirector is not null)
        {
            IEnumerable<EncounterDefinition> completed = _runDirector.Encounters.Take(checkpoint.NextEncounterIndex);
            _completedPurgeEncounters = completed.Count(encounter =>
                encounter.ObjectiveType == EncounterObjectiveType.Purge);
            _completedRelayEncounters = completed.Count(encounter =>
                encounter.ObjectiveType == EncounterObjectiveType.RelayDefense);
            _completedEliteEncounters = completed.Count(encounter =>
                encounter.ObjectiveType == EncounterObjectiveType.EliteHunt);
        }
    }

    private static float NonNegativeFinite(float value) =>
        float.IsFinite(value) ? MathF.Max(0f, value) : 0f;

    public RunCheckpoint? CreateRunCheckpoint()
    {
        if (_runDirector is null)
        {
            return null;
        }

        RunCheckpoint checkpoint = _runDirector.CreateCheckpoint(Arena.Id, Player.Weapons[0].Definition.Id);
        return checkpoint with
        {
            Difficulty = Difficulty,
            ThreatTier = ThreatTier,
            CollectedWeaponIds = Player.Weapons.Select(weapon => weapon.Definition.Id).ToList(),
            WeaponStates = Player.Weapons.Select(weapon => weapon.CreateCheckpointState()).ToList(),
            SelectedWeaponId = Player.CurrentWeapon.Definition.Id,
            ActiveArmoryWeaponIds = Pickups
                .Where(pickup => pickup.Type == PickupType.Weapon && pickup.IsDropped &&
                    !string.IsNullOrWhiteSpace(pickup.WeaponId))
                .Select(pickup => pickup.WeaponId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PlayerHealth = Player.Health,
            PlayerMaximumHealth = Player.MaximumHealth,
            SimulationTick = Tick,
            RandomState = _randomState,
            ElapsedRunSeconds = ElapsedRunSeconds,
            Score = Score,
            Kills = Kills,
            DamageTaken = DamageTaken,
            CloseRangeKills = CloseRangeKills,
            LongRangeKills = LongRangeKills,
            SectorsCompleted = SectorsCompleted,
            CompletedPurgeEncounters = _completedPurgeEncounters,
            CompletedRelayEncounters = _completedRelayEncounters,
            CompletedEliteEncounters = _completedEliteEncounters,
            LastCompletedEncounterObjective = LastCompletedEncounterObjective,
            LastCompletedEncounterMetric = LastCompletedEncounterMetric,
            EquipmentLoadout = EquipmentLoadout.Clone(),
            EquipmentItems = _equipmentItems.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToList(),
            HandWeaponStates = CreateHandWeaponCheckpointStates(),
            WeaponSetA = WeaponSetA.Clone(),
            WeaponSetB = WeaponSetB.Clone(),
            ActiveWeaponSetIndex = ActiveWeaponSetIndex,
            WeaponQuickbar = WeaponQuickbar.Clone(),
            ActiveWeaponSlotIndex = ActiveWeaponSlotIndex,
            WeaponSetStates = CreateWeaponSetCheckpointStates(),
            IssuedItemInstances = _equipmentItems.Values
                .Where(item => item.IsRunBound)
                .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PendingProgression = _pendingProgression,
            RecoveryCache = _recoveryCache,
            LootDropSerial = _lootDropSerial,
            AbilityCooldowns = new Dictionary<string, float>(_abilityCooldowns, StringComparer.OrdinalIgnoreCase),
            RunExperienceEarned = _runExperienceEarned,
            RunLevelsGained = _runLevelsGained,
            RunProficiencyExperience = new Dictionary<WeaponFamily, int>(_runProficiencyExperience),
            RunAbilitiesMastered = _runAbilitiesMastered.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            RunCollectedItemIds = _runCollectedItemIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            RunRarityTotals = new Dictionary<ItemRarity, int>(_runRarityTotals),
            RunHighestItemPower = _runHighestItemPower,
        };
    }

    private Dictionary<EquipmentSlot, WeaponCheckpointState> CreateHandWeaponCheckpointStates()
    {
        Dictionary<EquipmentSlot, WeaponCheckpointState> states = [];
        if (Player.RightHandWeapon is not null)
        {
            states[EquipmentSlot.RightHand] = Player.RightHandWeapon.CreateCheckpointState();
        }

        if (Player.LeftHandWeapon is not null)
        {
            states[EquipmentSlot.LeftHand] = Player.LeftHandWeapon.CreateCheckpointState();
        }

        return states;
    }

    private Dictionary<int, WeaponSetCheckpointState> CreateWeaponSetCheckpointStates()
    {
        Dictionary<int, WeaponSetCheckpointState> states = [];
        for (int index = 0; index < _weaponSets.Length; index++)
        {
            RuntimeWeaponSet set = _weaponSets[index];
            states[index] = new WeaponSetCheckpointState
            {
                RightHand = set.RightHand?.CreateCheckpointState(),
                LeftHand = ReferenceEquals(set.LeftHand, set.RightHand)
                    ? set.RightHand?.CreateCheckpointState()
                    : set.LeftHand?.CreateCheckpointState(),
            };
        }
        return states;
    }

    public Vector3 GetViewDirection()
    {
        float cosinePitch = MathF.Cos(Player.Pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Sin(Player.Yaw) * cosinePitch,
            MathF.Sin(Player.Pitch),
            -MathF.Cos(Player.Yaw) * cosinePitch));
    }

    public Vector3 GetWeaponMuzzlePosition() =>
        GetWeaponMuzzlePosition(Player.EffectiveRightHandWeapon.Definition, isLeftHand: false);

    public Vector3 GetWeaponMuzzlePosition(WeaponDefinition weapon, bool isLeftHand)
    {
        Vector3 forward = GetViewDirection();
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
        Vector3 offset = Player.IsAiming ? weapon.ViewModelAdsOffset : weapon.ViewModelHipOffset;
        if (isLeftHand)
        {
            offset.X = -MathF.Abs(offset.X);
        }
        float forwardDistance = Math.Clamp(-offset.Z + 0.15f, 0.5f, 0.9f);
        Vector3 desired = Player.Position +
            (right * offset.X * 0.95f) +
            (up * offset.Y) +
            (forward * forwardDistance);

        float nearestFraction = 1f;
        foreach (ArenaPrimitiveDefinition primitive in Arena.Primitives)
        {
            if (primitive.HasCollision &&
                SegmentIntersectsBox(Player.Position, desired, primitive, out float fraction))
            {
                nearestFraction = MathF.Min(nearestFraction, fraction);
            }
        }

        return nearestFraction < 1f
            ? Vector3.Lerp(Player.Position, desired, MathF.Max(0f, nearestFraction - 0.05f))
            : desired;
    }

    private void UpdatePlayer(PlayerCommand command, float deltaSeconds)
    {
        Player.Yaw = WrapAngle(Player.Yaw + command.LookDelta.X);
        Player.Pitch = Math.Clamp(Player.Pitch + command.LookDelta.Y, -1.45f, 1.45f);
        Player.IsAiming = command.Has(PlayerButtons.AimDownSights);

        if (Arena.TraversalMode != ArenaTraversalMode.OpenArena &&
            command.WeaponSlot >= 0 && command.WeaponSlot < Player.Weapons.Count)
        {
            Player.SelectedWeaponIndex = command.WeaponSlot;
        }

        Vector2 moveInput = command.Movement;
        if (moveInput.LengthSquared() > 1f)
        {
            moveInput = Vector2.Normalize(moveInput);
        }

        Vector3 forward = new(MathF.Sin(Player.Yaw), 0f, -MathF.Cos(Player.Yaw));
        Vector3 right = new(MathF.Cos(Player.Yaw), 0f, MathF.Sin(Player.Yaw));
        float movementMultiplier = Player.AdrenalSeconds > 0f
            ? _runDirector?.Modifiers.KillMovementSpeedMultiplier ?? 1f
            : 1f;
        Vector3 desiredVelocity = ((right * moveInput.X) + (forward * moveInput.Y)) *
            PlayerMoveSpeed * CalculatePermanentMovementMultiplier() * movementMultiplier;
        desiredVelocity += Player.ExternalVelocity;
        Player.ExternalVelocity *= MathF.Exp(-6f * deltaSeconds);
        if (Player.ExternalVelocity.LengthSquared() < 0.0001f)
        {
            Player.ExternalVelocity = Vector3.Zero;
        }

        bool jumpPressed = command.Has(PlayerButtons.Jump) && !_previousButtons.HasFlag(PlayerButtons.Jump);
        PlayerPhysicsResult result = _playerController.Step(
            desiredVelocity, jumpPressed, Player.IsGrounded, deltaSeconds);
        Player.Position = result.Position;
        Player.VerticalVelocity = result.VerticalVelocity;
        Player.IsGrounded = result.IsGrounded;
    }

    public void Dispose() => _playerController.Dispose();

    private void UpdateAbilities(PlayerCommand command, float deltaSeconds)
    {
        foreach (string id in _abilityCooldowns.Keys.ToArray())
        {
            _abilityCooldowns[id] = MathF.Max(0f, _abilityCooldowns[id] - deltaSeconds);
        }

        if (command.Has(PlayerButtons.Ability1) && !_previousButtons.HasFlag(PlayerButtons.Ability1))
        {
            TryActivateAbility(0);
        }

        if (command.Has(PlayerButtons.Ability2) && !_previousButtons.HasFlag(PlayerButtons.Ability2))
        {
            TryActivateAbility(1);
        }
    }

    public bool TryActivateAbility(int slotIndex)
    {
        if (slotIndex is < 0 or > 1 ||
            slotIndex >= Progression.AbilityMastery.EquippedActiveAbilityIds.Count)
        {
            return false;
        }

        string abilityId = Progression.AbilityMastery.EquippedActiveAbilityIds[slotIndex];
        if (!_catalog.Abilities.TryGetValue(abilityId, out EquipmentAbilityDefinition? ability) ||
            ability.Kind != AbilityKind.Active || _abilityCooldowns.GetValueOrDefault(abilityId) > 0f ||
            (!Progression.AbilityMastery.IsMastered(abilityId) && !GetTaughtAbilityIds().Contains(abilityId)))
        {
            return false;
        }

        float power = CalculateAbilityPowerMultiplier();
        switch (abilityId)
        {
            case "barrier-pulse":
            case "phase-guard":
                _emergencyBarrierSeconds = MathF.Max(_emergencyBarrierSeconds, 2f * power);
                break;
            case "repair-drone":
                Player.Health = MathF.Min(Player.MaximumHealth, Player.Health + (25f * power));
                break;
            case "ammo-synthesizer":
                Player.EffectiveRightHandWeapon.AddAmmo((int)MathF.Round(24f * power));
                Player.LeftHandWeapon?.AddAmmo((int)MathF.Round(24f * power));
                break;
            case "coolant-vent":
                Player.EffectiveRightHandWeapon.AddAmmo((int)MathF.Round(40f * power));
                Player.LeftHandWeapon?.AddAmmo((int)MathF.Round(40f * power));
                break;
            case "overclock":
                Player.AdrenalSeconds = MathF.Max(Player.AdrenalSeconds, 3f * power);
                break;
            default:
                DamageEnemiesInAbilityRadius(abilityId, power);
                break;
        }

        _abilityCooldowns[abilityId] = ability.CooldownSeconds * CalculateAbilityCooldownMultiplier();
        AddEvent(CombatEventType.AbilityActivated, Player.Position, Player.Position,
            Player.Id, EntityId.None, abilityId, _abilityCooldowns[abilityId]);
        return true;
    }

    private void DamageEnemiesInAbilityRadius(string abilityId, float power)
    {
        float radius = abilityId is "gravity-well" or "arc-nova" ? 10f : 7f;
        float damage = abilityId == "target-mark" ? 65f : 42f;
        foreach (EnemyState enemy in Enemies.Where(enemy => !enemy.IsDead &&
            Vector3.DistanceSquared(enemy.Position, Player.Position) <= radius * radius).ToArray())
        {
            DamageEnemy(enemy, damage * power, enemy.Position, Player.Id, $"ability:{abilityId}");
        }
    }

    private HashSet<string> GetTaughtAbilityIds()
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (string itemId in EquipmentLoadout.EquippedItemIds.Values)
        {
            if (!_equipmentItems.TryGetValue(itemId, out EquipmentInstance? item))
            {
                continue;
            }

            string? abilityId = item.WeaponBaseId is string weaponId &&
                _catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? weapon)
                    ? weapon.TaughtAbilityId
                    : item.EquipmentBaseId is string equipmentId &&
                        _catalog.EquipmentBases.TryGetValue(equipmentId, out EquipmentBaseDefinition? equipment)
                            ? equipment.TaughtAbilityId
                            : null;
            if (!string.IsNullOrWhiteSpace(abilityId))
            {
                ids.Add(abilityId);
            }
        }

        return ids;
    }

    private float CalculateAbilityPowerMultiplier()
    {
        float multiplier = EquippedAffixProduct(AffixEffectType.AbilityPower) *
            PassiveEffectProduct(AffixEffectType.AbilityPower) *
            (1f + TalentBonus(AffixEffectType.AbilityPower));
        return Math.Clamp(multiplier, 1f, 2f);
    }

    private float CalculateAbilityCooldownMultiplier()
    {
        float reduction = TalentBonus(AffixEffectType.CooldownRecovery) +
            MathF.Max(0f, (EquippedAffixProduct(AffixEffectType.CooldownRecovery) *
                PassiveEffectProduct(AffixEffectType.CooldownRecovery)) - 1f);
        return 1f - Math.Clamp(reduction, 0f, 0.30f);
    }

    private void ApplyPersistentWeaponStats(WeaponState state, EquipmentInstance item, bool refill)
    {
        float capacity = AffixProduct(item, AffixEffectType.Capacity) *
            PassiveEffectProduct(AffixEffectType.Capacity) *
            (1f + TalentBonus(AffixEffectType.Capacity));
        float fireInterval = AffixProduct(item, AffixEffectType.FireInterval) *
            PassiveEffectProduct(AffixEffectType.FireInterval) *
            (1f - Math.Clamp(TalentBonus(AffixEffectType.FireInterval), 0f, 0.5f));
        float reload = (1f / AffixProduct(item, AffixEffectType.ReloadAndRecovery)) *
            (1f / PassiveEffectProduct(AffixEffectType.ReloadAndRecovery)) *
            (1f - Math.Clamp(TalentBonus(AffixEffectType.ReloadAndRecovery), 0f, 0.5f));
        float recovery = AffixProduct(item, AffixEffectType.ReloadAndRecovery) *
            PassiveEffectProduct(AffixEffectType.ReloadAndRecovery) *
            (1f + TalentBonus(AffixEffectType.ReloadAndRecovery));
        state.SetPersistentModifiers(capacity, fireInterval, reload, recovery, refill);
    }

    private float CalculatePersistentStabilityMultiplier(bool isLeftHand)
    {
        EquipmentInstance? item = GetHandItem(isLeftHand);
        float affix = item is null ? 1f : AffixProduct(item, AffixEffectType.Stability);
        return affix * PassiveEffectProduct(AffixEffectType.Stability) *
            (1f - Math.Clamp(TalentBonus(AffixEffectType.Stability), 0f, 0.5f));
    }

    private float CalculateDualWieldPenaltyScale() => 1f - Math.Clamp(
        TalentBonus(AffixEffectType.Stability) / 0.30f,
        0f,
        0.5f);

    private float CalculatePermanentMovementMultiplier()
    {
        float multiplier = EquippedAffixProduct(AffixEffectType.MovementSpeed) *
            PassiveEffectProduct(AffixEffectType.MovementSpeed) *
            (1f + TalentBonus(AffixEffectType.MovementSpeed));
        return Math.Clamp(multiplier, 1f, 1.15f);
    }

    private float CalculatePermanentMaximumHealthBonus()
    {
        float bonus = TalentBonus(AffixEffectType.MaximumHealth) +
            PassiveEffectAdditive(AffixEffectType.MaximumHealth);
        foreach (EquipmentInstance item in EquippedItems())
        {
            float scale = RpgProgressionMath.ItemPowerScale(item.ItemPower);
            if (item.EquipmentBaseId is string baseId &&
                _catalog.EquipmentBases.TryGetValue(baseId, out EquipmentBaseDefinition? definition))
            {
                bonus += definition.BaseMaximumHealth * scale;
            }
            foreach (RolledAffix rolled in item.Affixes)
            {
                if (_catalog.Affixes.TryGetValue(rolled.AffixId, out AffixDefinition? affix) &&
                    affix.EffectType == AffixEffectType.MaximumHealth)
                {
                    bonus += rolled.Value;
                }
            }
        }
        return MathF.Max(0f, bonus);
    }

    private float CalculateRewardMultiplier(AffixEffectType type, float maximumBonus = 2f)
    {
        float multiplier = EquippedAffixProduct(type) * (1f + TalentBonus(type));
        multiplier *= PassiveEffectProduct(type);
        return Math.Clamp(multiplier, 1f, 1f + maximumBonus);
    }

    private float EquippedAffixProduct(AffixEffectType type)
    {
        float product = 1f;
        foreach (EquipmentInstance item in EquippedItems())
        {
            product *= AffixProduct(item, type);
        }
        return product;
    }

    private float AffixProduct(EquipmentInstance item, AffixEffectType type)
    {
        float product = 1f;
        foreach (RolledAffix rolled in item.Affixes)
        {
            if (_catalog.Affixes.TryGetValue(rolled.AffixId, out AffixDefinition? affix) && affix.EffectType == type)
            {
                product *= rolled.Value;
            }
        }
        return product;
    }

    private float TalentBonus(AffixEffectType type) => _catalog.Talents.Values
        .Where(talent => talent.EffectType == type)
        .Sum(talent => talent.ValuePerRank * Progression.TalentRanks.GetValueOrDefault(talent.Id));

    private float PassiveEffectProduct(AffixEffectType type, WeaponFamily family = WeaponFamily.None)
    {
        float product = 1f;
        HashSet<string> taught = GetTaughtAbilityIds();
        foreach (string abilityId in Progression.AbilityMastery.EquippedPassiveAbilityIds)
        {
            if ((!Progression.AbilityMastery.IsMastered(abilityId) && !taught.Contains(abilityId)) ||
                !_catalog.Abilities.TryGetValue(abilityId, out EquipmentAbilityDefinition? ability) ||
                ability.Kind != AbilityKind.Passive)
            {
                continue;
            }
            foreach (AffixEffectDefinition effect in ability.Effects.Where(effect => effect.Type == type &&
                (effect.WeaponFamily == WeaponFamily.None || effect.WeaponFamily == family)))
            {
                product *= effect.Value;
            }
        }
        return product;
    }

    private float PassiveEffectAdditive(AffixEffectType type)
    {
        float sum = 0f;
        HashSet<string> taught = GetTaughtAbilityIds();
        foreach (string abilityId in Progression.AbilityMastery.EquippedPassiveAbilityIds)
        {
            if ((!Progression.AbilityMastery.IsMastered(abilityId) && !taught.Contains(abilityId)) ||
                !_catalog.Abilities.TryGetValue(abilityId, out EquipmentAbilityDefinition? ability) ||
                ability.Kind != AbilityKind.Passive)
            {
                continue;
            }
            sum += ability.Effects.Where(effect => effect.Type == type).Sum(effect => effect.Value);
        }
        return sum;
    }

    private IEnumerable<EquipmentInstance> EquippedItems()
    {
        foreach (string itemId in EquipmentLoadout.EquippedItemIds.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_equipmentItems.TryGetValue(itemId, out EquipmentInstance? item))
            {
                yield return item;
            }
        }
    }

    private EquipmentInstance? GetHandItem(bool isLeftHand)
    {
        string? itemId = EquipmentLoadout[isLeftHand ? EquipmentSlot.LeftHand : EquipmentSlot.RightHand];
        return itemId is not null && _equipmentItems.TryGetValue(itemId, out EquipmentInstance? item) ? item : null;
    }

    private float CalculatePersistentWeaponDamageMultiplier(bool isLeftHand)
    {
        EquipmentInstance? item = GetHandItem(isLeftHand);
        if (item is null)
        {
            return 1f;
        }

        float itemPower = RpgProgressionMath.ItemPowerScale(item.ItemPower);
        WeaponFamily family = item.WeaponBaseId is string itemWeaponId &&
            _catalog.Weapons.TryGetValue(itemWeaponId, out WeaponDefinition? itemWeapon)
                ? itemWeapon.Family
                : WeaponFamily.None;
        float permanentMultiplier = EquippedAffixProduct(AffixEffectType.WeaponDamage) *
            PassiveEffectProduct(AffixEffectType.WeaponDamage, family);

        float talentBonus = _catalog.Talents.Values
            .Where(talent => talent.EffectType == AffixEffectType.WeaponDamage)
            .Sum(talent => talent.ValuePerRank * Progression.TalentRanks.GetValueOrDefault(talent.Id));
        permanentMultiplier *= 1f + talentBonus;
        if (item.WeaponBaseId is string weaponId && _catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? weapon) &&
            Progression.Proficiencies.Get(weapon.Family).MasteryUnlocked)
        {
            permanentMultiplier *= 1.05f;
        }

        return itemPower * MathF.Min(1.25f, permanentMultiplier);
    }

    private void ApplyWeaponControl(
        EnemyState enemy,
        WeaponBehavior behavior,
        Vector3 impact,
        string weaponId,
        float baseDamage)
    {
        if (behavior == WeaponBehavior.None || enemy.IsDead)
        {
            return;
        }

        float controlScale = enemy.Definition.IsBoss ? 0f : enemy.IsElite ? 0.3f : 1f;
        if ((behavior & WeaponBehavior.DamageOverTime) != 0)
        {
            enemy.DamageOverTimeRemaining = MathF.Max(enemy.DamageOverTimeRemaining, 3f);
            enemy.DamageOverTimePerSecond = MathF.Max(enemy.DamageOverTimePerSecond, baseDamage * 0.18f);
            enemy.DamageOverTimeSourceId = weaponId;
        }
        if ((behavior & WeaponBehavior.ChainField) != 0)
        {
            enemy.DamageOverTimeRemaining = MathF.Max(enemy.DamageOverTimeRemaining, 2.5f);
            enemy.DamageOverTimePerSecond = MathF.Max(enemy.DamageOverTimePerSecond, baseDamage * 0.10f);
            enemy.DamageOverTimeSourceId = weaponId;
        }

        if (controlScale <= 0f)
        {
            return;
        }

        if ((behavior & WeaponBehavior.Stun) != 0)
        {
            enemy.ControlImpairmentSeconds = MathF.Max(enemy.ControlImpairmentSeconds, 0.8f * controlScale);
            enemy.ActionState = EnemyActionState.HitReaction;
        }

        Vector3 direction = enemy.Position - impact;
        direction.Y = 0f;
        if (direction.LengthSquared() <= 0.0001f)
        {
            direction = enemy.Position - Player.Position;
            direction.Y = 0f;
        }

        if (direction.LengthSquared() > 0.0001f)
        {
            direction = Vector3.Normalize(direction);
            if ((behavior & WeaponBehavior.Knockback) != 0)
            {
                TrySetEnemyPosition(enemy, enemy.Position + (direction * 1.8f * controlScale));
            }
            else if ((behavior & WeaponBehavior.Pull) != 0)
            {
                TrySetEnemyPosition(enemy, enemy.Position - (direction * 1.5f * controlScale));
            }
        }
    }

    private void UpdateWeapons(PlayerCommand command, float deltaSeconds)
    {
        int requestedSet = Arena.TraversalMode == ArenaTraversalMode.OpenArena &&
            command.SelectedQuickbarSlot is >= 0 and < WeaponQuickbarLoadout.SlotCount
            ? command.SelectedQuickbarSlot
            : -1;
        bool swapPressed = command.Has(PlayerButtons.SwapWeaponSet) &&
            !_previousButtons.HasFlag(PlayerButtons.SwapWeaponSet);
        if (requestedSet >= 0 || swapPressed)
        {
            BeginWeaponSetSwap(requestedSet >= 0
                ? requestedSet
                : FindNextPopulatedWeaponSlot(Player.ActiveWeaponSetIndex, 1));
        }

        WeaponState activeRight = Player.EffectiveRightHandWeapon;
        WeaponState? activeLeft = ReferenceEquals(Player.LeftHandWeapon, activeRight)
            ? null
            : Player.LeftHandWeapon;
        foreach (WeaponState weapon in Player.Weapons)
        {
            bool wasReloading = weapon.IsReloading;
            bool isActive = ReferenceEquals(weapon, activeRight) || ReferenceEquals(weapon, activeLeft);
            weapon.Tick(deltaSeconds, isActive ? 1f : 0.5f);
            if (wasReloading && !weapon.IsReloading)
            {
                AddEvent(CombatEventType.ReloadCompleted, Player.Position, Player.Position,
                    Player.Id, EntityId.None, weapon.Definition.Id);
            }
        }

        WeaponState right = Player.EffectiveRightHandWeapon;
        WeaponState? left = ReferenceEquals(Player.LeftHandWeapon, right) ? null : Player.LeftHandWeapon;
        bool dualWielding = left is not null;
        float dualPenaltyScale = CalculateDualWieldPenaltyScale();
        if (command.Has(PlayerButtons.Reload))
        {
            BeginHandReload(right, dualWielding ? 1f + (0.15f * dualPenaltyScale) : 1f);
            if (left is not null)
            {
                BeginHandReload(left, 1f + (0.15f * dualPenaltyScale));
            }
        }

        TryFireHand(right, PlayerButtons.FireRight, command.Buttons, deltaSeconds,
            isLeftHand: false, dualWielding: dualWielding);
        if (left is not null)
        {
            TryFireHand(left, PlayerButtons.FireLeft, command.Buttons, deltaSeconds,
                isLeftHand: true, dualWielding: true);
        }
    }

    public bool BeginWeaponSetSwap(int targetSetIndex)
    {
        if (targetSetIndex is < 0 or >= WeaponQuickbarLoadout.SlotCount ||
            targetSetIndex == Player.ActiveWeaponSetIndex ||
            _weaponSets[targetSetIndex].RightHand is null)
        {
            return false;
        }

        CancelWeaponSetAttackState(_weaponSets[Player.ActiveWeaponSetIndex]);
        Player.IsAiming = false;
        Player.WeaponSwapRemainingSeconds = 0f;
        ActivateWeaponSet(targetSetIndex, emitEvent: true);
        return true;
    }

    private void CancelWeaponSetAttackState(RuntimeWeaponSet set)
    {
        foreach (WeaponState weapon in new[] { set.RightHand, set.LeftHand }.OfType<WeaponState>().Distinct())
        {
            weapon.CancelAttackState();
            _chargeSeconds.Remove(weapon);
            _rampShots.Remove(weapon);
        }
    }

    private void ActivateWeaponSet(int setIndex, bool emitEvent)
    {
        RuntimeWeaponSet set = _weaponSets[setIndex];
        if (set.RightHand is null)
        {
            return;
        }

        Player.ActiveWeaponSetIndex = setIndex;
        Player.RightHandWeapon = set.RightHand;
        Player.LeftHandWeapon = set.LeftHand;
        Player.SelectedWeaponIndex = Math.Max(0, Player.Weapons.IndexOf(set.RightHand));
        EquipmentLoadout.EquippedItemIds.Remove(EquipmentSlot.RightHand);
        EquipmentLoadout.EquippedItemIds.Remove(EquipmentSlot.LeftHand);
        if (set.RightHandItemId is not null)
        {
            EquipmentLoadout.EquippedItemIds[EquipmentSlot.RightHand] = set.RightHandItemId;
        }
        if (set.LeftHandItemId is not null)
        {
            EquipmentLoadout.EquippedItemIds[EquipmentSlot.LeftHand] = set.LeftHandItemId;
        }

        if (emitEvent)
        {
            AddEvent(CombatEventType.WeaponSetSwapped, Player.Position, Player.Position,
                Player.Id, Player.Id, $"weapon-set-{setIndex + 1}", setIndex);
        }
    }

    private void BeginHandReload(WeaponState weapon, float durationMultiplier)
    {
        bool wasReloading = weapon.IsReloading;
        weapon.BeginReload(durationMultiplier);
        if (!wasReloading && weapon.IsReloading)
        {
            AddEvent(CombatEventType.ReloadStarted, Player.Position, Player.Position,
                Player.Id, EntityId.None, weapon.Definition.Id);
        }
    }

    private void TryFireHand(
        WeaponState weapon,
        PlayerButtons fireButton,
        PlayerButtons buttons,
        float deltaSeconds,
        bool isLeftHand,
        bool dualWielding)
    {
        bool fireHeld = (buttons & fireButton) != 0;
        bool firePressed = fireHeld && (_previousButtons & fireButton) == 0;
        float chargeMultiplier = 1f;
        if ((weapon.Definition.BehaviorFlags & WeaponBehavior.Charge) != 0)
        {
            if (fireHeld)
            {
                _chargeSeconds[weapon] = MathF.Min(1.2f,
                    _chargeSeconds.GetValueOrDefault(weapon) + deltaSeconds);
                return;
            }

            if ((_previousButtons & fireButton) == 0 || !_chargeSeconds.Remove(weapon, out float chargedSeconds))
            {
                return;
            }

            firePressed = true;
            chargeMultiplier = float.Lerp(1f, 2f, Math.Clamp(chargedSeconds / 1.2f, 0f, 1f));
        }
        if (firePressed && weapon.Definition.TriggerMode == TriggerMode.Burst)
        {
            weapon.StartBurst();
        }

        bool wantsShot = weapon.Definition.TriggerMode switch
        {
            TriggerMode.SemiAutomatic => firePressed,
            TriggerMode.Automatic => fireHeld,
            TriggerMode.Burst => weapon.BurstShotsRemaining > 0,
            _ => false,
        };
        if (!wantsShot)
        {
            return;
        }

        if (!weapon.TryFire())
        {
            if (firePressed)
            {
                AddEvent(CombatEventType.DryFire, Player.Position, Player.Position,
                    Player.Id, EntityId.None, weapon.Definition.Id);
            }
            return;
        }

        weapon.CompleteBurstShot();
        LastShotSeconds = 0f;
        Vector3 direction = GetViewDirection();
        Vector3 muzzle = GetWeaponMuzzlePosition(weapon.Definition, isLeftHand);
        float persistentDamageMultiplier = CalculatePersistentWeaponDamageMultiplier(isLeftHand);
        persistentDamageMultiplier *= chargeMultiplier;
        if ((weapon.Definition.BehaviorFlags & WeaponBehavior.RampDamage) != 0)
        {
            int rampShots = Math.Min(6, _rampShots.GetValueOrDefault(weapon) + 1);
            _rampShots[weapon] = rampShots;
            persistentDamageMultiplier *= 1f + (rampShots * 0.04f);
        }
        else
        {
            _rampShots.Remove(weapon);
        }
        AddEvent(CombatEventType.WeaponFired, muzzle, Player.Position + direction,
            Player.Id, EntityId.None, weapon.Definition.Id, weapon.Definition.ScreenShake);
        if (weapon.Definition.ShotMode == ShotMode.Hitscan)
        {
            bool splitShot = (weapon.Definition.BehaviorFlags & WeaponBehavior.SplitShot) != 0;
            int pelletCount = weapon.Definition.PelletCount + (splitShot ? 2 : 0);
            float pelletDamageScale = splitShot ? 0.45f : 1f;
            for (int pellet = 0; pellet < pelletCount; pellet++)
            {
                float spreadMultiplier = _runDirector?.Modifiers.SpreadMultiplier(weapon.Definition.Id) ?? 1f;
                spreadMultiplier *= dualWielding ? 1f + (0.2f * CalculateDualWieldPenaltyScale()) : 1f;
                spreadMultiplier *= CalculatePersistentStabilityMultiplier(isLeftHand);
                Vector3 pelletDirection = ApplySpread(direction,
                    weapon.Definition.SpreadDegrees * spreadMultiplier);
                FireHitscan(Player.Position, muzzle, pelletDirection, weapon.Definition,
                    persistentDamageMultiplier * pelletDamageScale);
            }
        }
        else
        {
            Vector3 aimPoint = Player.Position + (direction * weapon.Definition.Range);
            Vector3 projectileDirection = Vector3.Normalize(aimPoint - muzzle);
            float projectileSpeed = weapon.Definition.ProjectileSpeed *
                (_runDirector?.Modifiers.ProjectileSpeedMultiplier(weapon.Definition.Id) ?? 1f);
            float lifetimeSeconds = weapon.Definition.Range / MathF.Max(1f, projectileSpeed);
            Projectiles.Add(new ProjectileState
            {
                Id = NextEntity(),
                OwnerId = Player.Id,
                Position = muzzle,
                PreviousPosition = muzzle,
                Origin = muzzle,
                Velocity = projectileDirection * projectileSpeed,
                Radius = weapon.Definition.ProjectileRadius,
                Damage = weapon.Definition.Damage * persistentDamageMultiplier,
                WeaponId = weapon.Definition.Id,
                BehaviorFlags = weapon.Definition.BehaviorFlags,
                Motion = weapon.Definition.ProjectileMotion,
                WeakPointMultiplier = weapon.Definition.WeakPointMultiplier,
                SplashRadius = weapon.Definition.SplashRadius *
                    (_runDirector?.Modifiers.SplashRadiusMultiplier(weapon.Definition.Id) ?? 1f),
                ChainRadius = weapon.Definition.ChainRadius *
                    (_runDirector?.Modifiers.ChainRadiusMultiplier(weapon.Definition.Id) ?? 1f),
                ChainTargets = weapon.Definition.ChainTargets +
                    (_runDirector?.Modifiers.ChainTargetBonus(weapon.Definition.Id) ?? 0),
                Color = weapon.Definition.ProjectileColor,
                InitialLifetimeSeconds = lifetimeSeconds,
                RemainingSeconds = lifetimeSeconds,
            });
        }
    }

    private void FireHitscan(
        Vector3 origin,
        Vector3 visualOrigin,
        Vector3 direction,
        WeaponDefinition weapon,
        float persistentDamageMultiplier)
    {
        float nearestArenaDistance = weapon.Range;
        foreach (ArenaPrimitiveDefinition primitive in Arena.Primitives)
        {
            if (!primitive.HasCollision)
            {
                continue;
            }

            if (RayIntersectsBox(origin, direction, primitive, out float distance) && distance > 0.05f)
            {
                nearestArenaDistance = MathF.Min(nearestArenaDistance, distance);
            }
        }

        EnemyState? hit = null;
        bool hitWeakPoint = false;
        float nearestEnemyDistance = nearestArenaDistance;
        foreach (EnemyState enemy in Enemies)
        {
            if (enemy.IsDead)
            {
                continue;
            }

            if (TryRayEnemyHit(origin, direction, enemy, out float distance, out bool weakPoint) &&
                distance < nearestEnemyDistance)
            {
                nearestEnemyDistance = distance;
                hit = enemy;
                hitWeakPoint = weakPoint;
            }
        }

        if (hit is not null)
        {
            float multiplier = CalculateDamageFalloff(weapon, nearestEnemyDistance);
            multiplier *= _runDirector?.Modifiers.DamageMultiplier(weapon.Id, nearestEnemyDistance) ?? 1f;
            Vector3 impact = origin + (direction * nearestEnemyDistance);
            float damage = weapon.Damage * persistentDamageMultiplier * multiplier;
            if (hitWeakPoint)
            {
                damage *= weapon.WeakPointMultiplier;
            }
            DamageEnemy(hit, damage, impact, Player.Id, weapon.Id);
            ApplyWeaponControl(hit, weapon.BehaviorFlags, impact, weapon.Id,
                weapon.Damage * persistentDamageMultiplier);
            AddEvent(CombatEventType.EnemyHit, impact, visualOrigin, Player.Id, hit.Id, weapon.Id,
                damage);
            if ((weapon.BehaviorFlags & WeaponBehavior.Pierce) != 0)
            {
                foreach ((EnemyState enemy, float distance) in Enemies
                    .Where(enemy => !enemy.IsDead && enemy != hit)
                    .Select(enemy => (Enemy: enemy, Hit: RayIntersectsSphere(
                        origin,
                        direction,
                        enemy.Position + new Vector3(0f, enemy.Definition.ColliderHeight * 0.35f, 0f),
                        enemy.Definition.ColliderRadius,
                        out float enemyDistance), Distance: enemyDistance))
                    .Where(result => result.Hit && result.Distance > nearestEnemyDistance &&
                        result.Distance < nearestArenaDistance)
                    .OrderBy(result => result.Distance)
                    .Take(2)
                    .Select(result => (result.Enemy, result.Distance)))
                {
                    float pierceDamage = weapon.Damage * persistentDamageMultiplier * 0.65f *
                        CalculateDamageFalloff(weapon, distance);
                    Vector3 pierceImpact = origin + (direction * distance);
                    DamageEnemy(enemy, pierceDamage, pierceImpact, Player.Id, weapon.Id);
                    ApplyWeaponControl(enemy, weapon.BehaviorFlags, pierceImpact, weapon.Id,
                        weapon.Damage * persistentDamageMultiplier);
                    AddEvent(CombatEventType.EnemyHit, pierceImpact, visualOrigin,
                        Player.Id, enemy.Id, weapon.Id, pierceDamage);
                }
            }
            if ((weapon.BehaviorFlags & WeaponBehavior.Ricochet) != 0)
            {
                EnemyState? ricochet = Enemies
                    .Where(enemy => !enemy.IsDead && enemy != hit &&
                        Vector3.DistanceSquared(enemy.Position, hit.Position) <= 64f)
                    .OrderBy(enemy => Vector3.DistanceSquared(enemy.Position, hit.Position))
                    .ThenBy(enemy => enemy.Id.Value)
                    .FirstOrDefault();
                if (ricochet is not null)
                {
                    float ricochetDamage = weapon.Damage * persistentDamageMultiplier * 0.55f;
                    Vector3 ricochetImpact = ricochet.Position +
                        new Vector3(0f, ricochet.Definition.ColliderHeight * 0.35f, 0f);
                    DamageEnemy(ricochet, ricochetDamage, ricochetImpact, Player.Id, weapon.Id);
                    ApplyWeaponControl(ricochet, weapon.BehaviorFlags, ricochetImpact, weapon.Id,
                        weapon.Damage * persistentDamageMultiplier);
                    AddEvent(CombatEventType.EnemyHit, ricochetImpact, impact,
                        Player.Id, ricochet.Id, weapon.Id, ricochetDamage);
                }
            }
        }
        else
        {
            Vector3 impact = origin + (direction * nearestArenaDistance);
            AddEvent(CombatEventType.WorldImpact, impact, visualOrigin, Player.Id, EntityId.None, weapon.Id);
        }
    }

    private static bool TryRayEnemyHit(
        Vector3 origin,
        Vector3 direction,
        EnemyState enemy,
        out float distance,
        out bool weakPoint)
    {
        float nearestWeakPoint = float.PositiveInfinity;
        foreach (EnemyWeakPointDefinition definition in enemy.Definition.WeakPoints)
        {
            if (RayIntersectsSphere(origin, direction, enemy.Position + definition.Offset,
                    definition.Radius, out float weakPointDistance))
            {
                nearestWeakPoint = MathF.Min(nearestWeakPoint, weakPointDistance);
            }
        }

        if (float.IsFinite(nearestWeakPoint))
        {
            distance = nearestWeakPoint;
            weakPoint = true;
            return true;
        }

        Vector3 center = enemy.Position + new Vector3(0f, enemy.Definition.ColliderHeight * 0.35f, 0f);
        weakPoint = false;
        return RayIntersectsSphere(origin, direction, center, enemy.Definition.ColliderRadius, out distance);
    }

    private void UpdateRunDirector(float deltaSeconds)
    {
        if (_runDirector is null)
        {
            return;
        }

        switch (_runDirector.Phase)
        {
            case global::FpsFrenzy.Core.Simulation.RunPhase.EncounterActive:
                if (_awaitingArmoryCollection)
                {
                    break;
                }

                _encounterElapsedSeconds += deltaSeconds;
                if (!_generatedEncounterStarted)
                {
                    BeginGeneratedEncounter();
                }

                UpdatePendingEnemySpawns(deltaSeconds);
                UpdateGeneratedSpawnQueue(deltaSeconds);
                UpdateRelayObjective(deltaSeconds);
                EvaluateGeneratedEncounter();
                break;
            case global::FpsFrenzy.Core.Simulation.RunPhase.BossActive:
                if (!_generatedBossStarted)
                {
                    BeginGeneratedBoss();
                }

                UpdatePendingEnemySpawns(deltaSeconds);
                TryCompleteGeneratedBoss();

                break;
        }
    }

    private void TryCompleteGeneratedBoss()
    {
        if (_runDirector?.Phase != global::FpsFrenzy.Core.Simulation.RunPhase.BossActive ||
            !_generatedBossStarted || ActiveBoss is not null ||
            PendingEnemySpawns.Any(pending =>
                pending.PortalId.Equals("boss-core", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _runDirector.CompleteBoss();
        UnlockNextThreatTier();
        CommitFinalProgression(includeWorldEquipment: true);
        Phase = GamePhase.Victory;
        Score += (int)(Player.Health * 10f);
    }

    private void CommitFinalProgression(bool includeWorldEquipment)
    {
        if (includeWorldEquipment)
        {
            EquipmentInstance[] worldItems = Pickups
                .Where(pickup => pickup.Type == PickupType.Equipment && pickup.Equipment is not null)
                .Select(pickup => pickup.Equipment!)
                .ToArray();
            _recoveryCache.Gather(worldItems);
            _recoveryCache.TakeAll(_pendingProgression);
            Pickups.RemoveAll(pickup => pickup.Type == PickupType.Equipment && pickup.IsDropped);
        }

        CommitPendingProgression();
    }

    private bool CommitPendingProgression()
    {
        int previousLevel = Progression.Level;
        HashSet<string> previouslyMastered = Progression.AbilityMastery.Abilities
            .Where(pair => pair.Value.IsMastered)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!_pendingProgression.Commit(Progression, _catalog))
        {
            return false;
        }

        _runExperienceEarned += Math.Max(0, _pendingProgression.Experience);
        _runLevelsGained += Math.Max(0, Progression.Level - previousLevel);
        foreach ((WeaponFamily family, int amount) in _pendingProgression.ProficiencyExperience)
        {
            _runProficiencyExperience[family] = _runProficiencyExperience.GetValueOrDefault(family) +
                Math.Max(0, amount);
        }
        foreach (string abilityId in Progression.AbilityMastery.Abilities
            .Where(pair => pair.Value.IsMastered && !previouslyMastered.Contains(pair.Key))
            .Select(pair => pair.Key))
        {
            _runAbilitiesMastered.Add(abilityId);
        }
        foreach (EquipmentInstance item in _pendingProgression.Equipment)
        {
            if (!_runCollectedItemIds.Add(item.Id))
            {
                continue;
            }
            _runRarityTotals[item.Rarity] = _runRarityTotals.GetValueOrDefault(item.Rarity) + 1;
            _runHighestItemPower = Math.Max(_runHighestItemPower, item.ItemPower);
        }
        return true;
    }

    private void UnlockNextThreatTier()
    {
        if (ThreatTier < ThreatTier.TierX && Progression.HighestUnlockedThreatTier <= ThreatTier)
        {
            Progression.HighestUnlockedThreatTier = (ThreatTier)((int)ThreatTier + 1);
        }
    }

    private void BeginGeneratedEncounter()
    {
        if (_runDirector?.CurrentEncounter is not EncounterDefinition encounter)
        {
            return;
        }

        CurrentWaveIndex = _runDirector.CurrentEncounterIndex;
        _generatedEncounterStarted = true;
        _generatedSpawnRemainingSeconds = 0f;
        _encounterElapsedSeconds = 0f;
        _generatedSpawnQueue.Clear();
        _generatedPressureWaveIndex = 0;
        _generatedFiniteWavesRemaining = encounter.ObjectiveType is
            EncounterObjectiveType.Purge or EncounterObjectiveType.EliteHunt
            ? Math.Max(1, encounter.PressureWaveCount)
            : 0;
        _generatedWaveBreakSeconds = 0f;
        _generatedWaveBreakActive = false;
        PendingEnemySpawns.Clear();
        _eliteTargetId = EntityId.None;
        _emergencyBarrierAvailable = _runDirector.Modifiers.HasEmergencyBarrier;
        ArenaSectorDefinition sector = _runDirector.SelectedSectors[encounter.SectorNumber - 1];
        Vector3 entryPoint = new(
            sector.EntryPoint.X,
            MathF.Max(PlayerEyeHeight, sector.EntryPoint.Y),
            sector.EntryPoint.Z);
        _playerController.Teleport(entryPoint);
        Player.Position = entryPoint;
        Player.PreviousPosition = entryPoint;
        Player.VerticalVelocity = 0f;
        Player.IsGrounded = true;
        Projectiles.Clear();
        _generatedEncounterEnemyTotal = 0;
        ConfigureGuaranteedWeaponDrops();
        QueueGeneratedPressureWave(encounter);
        RelayObjective = encounter.ObjectiveType == EncounterObjectiveType.RelayDefense
            ? new RelayObjectiveState
            {
                Position = sector.ObjectiveAnchor,
                MaximumHealth = encounter.RelayMaximumHealth,
                Health = encounter.RelayMaximumHealth,
                RemainingSeconds = encounter.RelayDurationSeconds,
            }
            : null;
        AddEvent(CombatEventType.SectorActivated, sector.EntryPoint, sector.ObjectiveAnchor,
            EntityId.None, Player.Id, sector.Id, encounter.SectorNumber);
        AddEvent(CombatEventType.EncounterStarted, sector.ObjectiveAnchor, Player.Position,
            EntityId.None, Player.Id, encounter.ObjectiveType.ToString(), _runDirector.CurrentEncounterIndex + 1);
    }

    private void ConfigureGuaranteedWeaponDrops()
    {
        _guaranteedWeaponFamilies.Clear();
        if (_runDirector is null)
        {
            return;
        }

        int encounterIndex = _runDirector.CurrentEncounterIndex;
        WeaponFamily[] missing = WeaponQuickbarLoadout.FamilyOrder
            .Where(family => _weaponSets[WeaponQuickbarLoadout.SlotForFamily(family)].RightHand is null)
            .OrderBy(family => StableArmoryOrder(family.ToString(), encounterIndex))
            .ToArray();
        int quota;
        IEnumerable<WeaponFamily> selected;
        if (encounterIndex < 3 && missing.Length > 0)
        {
            int remainingFirstSectorEncounters = 3 - encounterIndex;
            quota = (int)Math.Ceiling(missing.Length / (double)remainingFirstSectorEncounters);
            selected = missing.Take(quota);
        }
        else
        {
            quota = 1;
            WeaponFamily[] pool = missing.Length > 0
                ? missing
                : WeaponQuickbarLoadout.FamilyOrder
                    .OrderBy(family => StableArmoryOrder(family.ToString(), encounterIndex))
                    .ToArray();
            selected = pool.Take(quota);
        }

        foreach (WeaponFamily family in selected)
        {
            _guaranteedWeaponFamilies.Enqueue(family);
        }
    }

    private void BuildGeneratedSpawnQueue(EncounterDefinition encounter)
    {
        List<EnemyDefinition> roster = _catalog.Enemies.Values
            .Where(definition => definition.SchemaVersion >= 2 && !definition.IsBoss &&
                IsRoleAvailable(definition.Behavior, encounter.SectorNumber))
            .OrderBy(definition => definition.Behavior)
            .ThenBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roster.Count == 0)
        {
            throw new InvalidOperationException("The sector campaign needs at least one non-boss enemy.");
        }

        Random encounterRandom = new(
            _runDirector!.Seed ^
            ((_runDirector.CurrentEncounterIndex + 1) * 7919) ^
            ((_generatedPressureWaveIndex + 1) * 104729));
        List<GeneratedEnemySpawn> selected = [];
        float remaining = encounter.ThreatBudget;
        if (encounter.SectorNumber == 3)
        {
            foreach (EnemyBehavior role in new[]
            {
                EnemyBehavior.Chaser,
                EnemyBehavior.Skirmisher,
                EnemyBehavior.Spitter,
                EnemyBehavior.Charger,
                EnemyBehavior.Warden,
            })
            {
                EnemyDefinition? required = roster.FirstOrDefault(definition => definition.Behavior == role);
                if (required is null || ThreatWeight(required) > remaining)
                {
                    continue;
                }

                selected.Add(new GeneratedEnemySpawn(required.Id, false));
                remaining -= ThreatWeight(required);
            }
        }

        while (remaining >= roster.Min(ThreatWeight) && selected.Count < 40)
        {
            List<EnemyDefinition> affordable = roster
                .Where(definition => ThreatWeight(definition) <= remaining + 0.001f)
                .ToList();
            if (affordable.Count == 0)
            {
                break;
            }

            EnemyDefinition selectedDefinition = affordable[encounterRandom.Next(affordable.Count)];
            selected.Add(new GeneratedEnemySpawn(selectedDefinition.Id, false));
            remaining -= ThreatWeight(selectedDefinition);
        }

        bool isFinalEliteWave = encounter.ObjectiveType == EncounterObjectiveType.EliteHunt &&
            _generatedPressureWaveIndex >= Math.Max(1, encounter.PressureWaveCount) - 1;
        if (isFinalEliteWave && selected.Count > 0)
        {
            int eliteIndex = selected
                .Select((spawn, index) => (Spawn: spawn, Index: index))
                .OrderByDescending(item => ThreatWeight(_catalog.Enemies[item.Spawn.EnemyId]))
                .ThenBy(item => item.Index)
                .First().Index;
            selected[eliteIndex] = selected[eliteIndex] with { IsElite = true };
        }

        IEnumerable<GeneratedEnemySpawn> ordered = encounter.ObjectiveType == EncounterObjectiveType.EliteHunt
            ? selected.Where(spawn => !spawn.IsElite)
                .OrderBy(_ => encounterRandom.Next())
                .Concat(selected.Where(spawn => spawn.IsElite))
            : selected.OrderBy(_ => encounterRandom.Next());
        foreach (GeneratedEnemySpawn spawn in ordered)
        {
            _generatedSpawnQueue.Enqueue(spawn);
        }
    }

    private void QueueGeneratedPressureWave(EncounterDefinition encounter)
    {
        int countBefore = _generatedSpawnQueue.Count;
        BuildGeneratedSpawnQueue(encounter);
        int added = _generatedSpawnQueue.Count - countBefore;
        _generatedEncounterEnemyTotal += Math.Max(0, added);
        _generatedPressureWaveIndex++;
        if (encounter.ObjectiveType is EncounterObjectiveType.Purge or EncounterObjectiveType.EliteHunt)
        {
            _generatedFiniteWavesRemaining = Math.Max(0, _generatedFiniteWavesRemaining - 1);
        }
    }

    private void UpdateGeneratedSpawnQueue(float deltaSeconds)
    {
        if (_runDirector?.CurrentEncounter is not EncounterDefinition encounter)
        {
            return;
        }

        UpdateGeneratedPressureWaves(encounter, deltaSeconds);
        _generatedSpawnRemainingSeconds = MathF.Max(0f, _generatedSpawnRemainingSeconds - deltaSeconds);
        int activeAndPending = Enemies.Count(enemy => !enemy.IsDead) + PendingEnemySpawns.Count;
        if (_generatedSpawnQueue.Count == 0 || activeAndPending >= encounter.MaximumConcurrentEnemies ||
            _generatedSpawnRemainingSeconds > 0f)
        {
            return;
        }

        ArenaSectorDefinition sector = _runDirector.SelectedSectors[encounter.SectorNumber - 1];
        SpawnPortalDefinition? portal = SelectSpawnPortal(sector);
        if (portal is null)
        {
            _generatedSpawnRemainingSeconds = 0.25f;
            return;
        }

        GeneratedEnemySpawn spawn = _generatedSpawnQueue.Dequeue();
        PendingEnemySpawns.Add(new PendingEnemySpawn
        {
            EnemyId = spawn.EnemyId,
            PortalId = portal.Id,
            Position = portal.Position,
            RemainingSeconds = MathF.Max(0.75f, portal.TelegraphSeconds),
            IsElite = spawn.IsElite,
        });
        AddEvent(CombatEventType.EnemySpawnTelegraph, portal.Position, Player.Position,
            EntityId.None, Player.Id, portal.Id, MathF.Max(0.75f, portal.TelegraphSeconds));
        _generatedSpawnRemainingSeconds = 0.35f;
    }

    private void UpdateGeneratedPressureWaves(EncounterDefinition encounter, float deltaSeconds)
    {
        bool waveClear = _generatedSpawnQueue.Count == 0 && PendingEnemySpawns.Count == 0 &&
            Enemies.All(enemy => enemy.IsDead);
        bool shouldReinforce = encounter.ObjectiveType switch
        {
            EncounterObjectiveType.Purge or EncounterObjectiveType.EliteHunt =>
                _generatedFiniteWavesRemaining > 0,
            EncounterObjectiveType.RelayDefense => RelayObjective?.RemainingSeconds > 0f,
            _ => false,
        };
        if (!waveClear || !shouldReinforce)
        {
            _generatedWaveBreakActive = false;
            _generatedWaveBreakSeconds = 0f;
            return;
        }

        if (!_generatedWaveBreakActive)
        {
            _generatedWaveBreakActive = true;
            _generatedWaveBreakSeconds = MathF.Max(0f, encounter.PressureWaveDelaySeconds);
        }

        _generatedWaveBreakSeconds = MathF.Max(0f, _generatedWaveBreakSeconds - deltaSeconds);
        if (_generatedWaveBreakSeconds > 0f)
        {
            return;
        }

        _generatedWaveBreakActive = false;
        QueueGeneratedPressureWave(encounter);
    }

    private void UpdatePendingEnemySpawns(float deltaSeconds)
    {
        for (int index = PendingEnemySpawns.Count - 1; index >= 0; index--)
        {
            PendingEnemySpawn pending = PendingEnemySpawns[index];
            pending.RemainingSeconds -= deltaSeconds;
            if (pending.RemainingSeconds > 0f)
            {
                continue;
            }

            if (!IsPendingSpawnPositionValid(pending))
            {
                if (pending.PortalId.Equals("boss-core", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyBossCoreSafetyImpulse(pending);
                    pending.SafetyImpulseApplied = true;
                    pending.RemainingSeconds = 0.25f;
                    continue;
                }

                SpawnPortalDefinition? replacement = SelectReplacementPortal(pending);
                if (replacement is null)
                {
                    // Keep the visible portal charged, but never materialize an enemy on top
                    // of the player or another actor. It is reconsidered on a short cadence.
                    pending.RemainingSeconds = 0.1f;
                    continue;
                }

                pending.PortalId = replacement.Id;
                pending.Position = replacement.Position;
                pending.RemainingSeconds = MathF.Max(0.75f, replacement.TelegraphSeconds);
                AddEvent(CombatEventType.EnemySpawnTelegraph, replacement.Position, Player.Position,
                    EntityId.None, Player.Id, replacement.Id, pending.RemainingSeconds);
                continue;
            }

            EnemyState enemy = SpawnEnemyAt(
                pending.EnemyId,
                pending.Position,
                pending.IsElite,
                pending.HealthFraction);
            if (pending.IsElite)
            {
                _eliteTargetId = enemy.Id;
            }

            AddEvent(CombatEventType.EnemySpawned, pending.Position, Player.Position,
                enemy.Id, Player.Id, pending.PortalId, pending.IsElite ? 1f : 0f);
            PendingEnemySpawns.RemoveAt(index);
        }
    }

    private bool IsPendingSpawnPositionValid(PendingEnemySpawn pending)
    {
        float distance = Vector3.Distance(pending.Position, Player.Position);
        bool navigable = pending.PortalId.Equals("boss-core", StringComparison.OrdinalIgnoreCase) ||
            _navigation.IsWalkable(pending.Position);
        return distance is >= 12f and <= 32f &&
            Enemies.All(enemy => enemy.IsDead ||
                Vector3.DistanceSquared(enemy.Position, pending.Position) >= 2.25f) &&
            PendingEnemySpawns.All(other => ReferenceEquals(other, pending) ||
                Vector3.DistanceSquared(other.Position, pending.Position) >= 2.25f) &&
            navigable;
    }

    private SpawnPortalDefinition? SelectReplacementPortal(PendingEnemySpawn pending)
    {
        if (pending.PortalId.StartsWith("boss-summon-", StringComparison.OrdinalIgnoreCase))
        {
            return SelectBossSummonPortal(pending);
        }

        // The Breach Walker is authored at the central core. If the player deliberately
        // occupies its safety radius, preserve the tell and defer rather than relocating it.
        if (pending.PortalId.Equals("boss-core", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ActiveSector is ArenaSectorDefinition sector
            ? SelectSpawnPortal(sector, pending)
            : null;
    }

    private void ApplyBossCoreSafetyImpulse(PendingEnemySpawn pending)
    {
        Vector3 direction = Player.Position - pending.Position;
        direction.Y = 0f;
        if (direction.LengthSquared() <= 0.001f)
        {
            direction = GetViewDirection();
            direction.Y = 0f;
        }

        direction = Vector3.Normalize(direction);
        Player.ExternalVelocity += direction * 36f;
        AddEvent(CombatEventType.EnemyTelegraph, pending.Position, Player.Position,
            EntityId.None, Player.Id, "boss-core-repulse", 12f);
    }

    private SpawnPortalDefinition? SelectSpawnPortal(
        ArenaSectorDefinition sector,
        PendingEnemySpawn? ignoredPending = null)
    {
        Vector3 viewDirection = GetViewDirection();
        HashSet<string> preferredPortalIds = sector.SpawnPortals
            .Select(portal => portal.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Arena.Sectors.SelectMany(candidate => candidate.SpawnPortals)
            .Where(portal =>
            {
                float distance = Vector3.Distance(portal.Position, Player.Position);
                bool validDistance = distance is >= 12f and <= 32f;
                bool unoccupied = Enemies.All(enemy => enemy.IsDead ||
                    Vector3.DistanceSquared(enemy.Position, portal.Position) >= 2.25f) &&
                    PendingEnemySpawns.All(pending => ReferenceEquals(pending, ignoredPending) ||
                        Vector3.DistanceSquared(pending.Position, portal.Position) >= 2.25f);
                return validDistance && unoccupied && _navigation.IsWalkable(portal.Position);
            })
            .OrderBy(portal => preferredPortalIds.Contains(portal.Id) ? 0 : 1)
            .ThenBy(portal =>
            {
                Vector3 direction = portal.Position - Player.Position;
                direction.Y = 0f;
                return direction.LengthSquared() <= 0.001f ||
                    Vector3.Dot(Vector3.Normalize(direction), viewDirection) < 0f ? 0 : 1;
            })
            .ThenBy(portal =>
            {
                float distance = Vector3.Distance(portal.Position, Player.Position);
                return distance is >= 14f and <= 28f ? 0 : 1;
            })
            .ThenBy(portal => StablePortalOrder(portal.Id))
            .FirstOrDefault();
    }

    private int StablePortalOrder(string portalId)
    {
        int value = _runDirector?.Seed ?? 0;
        foreach (char character in portalId)
        {
            value = unchecked((value * 31) + character);
        }

        return value;
    }

    private void UpdateRelayObjective(float deltaSeconds)
    {
        if (RelayObjective is null)
        {
            return;
        }

        RelayObjective.RemainingSeconds = MathF.Max(0f, RelayObjective.RemainingSeconds - deltaSeconds);
    }

    public void ApplyRelayDamage(float damage, EntityId sourceId = default)
    {
        if (RelayObjective is null || damage <= 0f)
        {
            return;
        }

        float appliedDamage = damage * RpgProgressionMath.EnemyDamageMultiplier(ThreatTier) *
            DifficultyCatalog.Get(Difficulty).EnemyDamageMultiplier * 0.5f;
        RelayObjective.Health = MathF.Max(0f, RelayObjective.Health - appliedDamage);
        AddEvent(CombatEventType.RelayDamaged, RelayObjective.Position, RelayObjective.Position,
            sourceId, EntityId.None, "relay", appliedDamage);
    }

    private void EvaluateGeneratedEncounter()
    {
        if (_runDirector?.CurrentEncounter is not EncounterDefinition encounter)
        {
            return;
        }

        if (RelayObjective?.Health <= 0f)
        {
            AddEvent(CombatEventType.EncounterFailed, RelayObjective.Position, Player.Position,
                EntityId.None, Player.Id, encounter.Id);
            _runDirector.Fail();
            Phase = GamePhase.Defeat;
            return;
        }

        bool completed = encounter.ObjectiveType switch
        {
            EncounterObjectiveType.Purge => _generatedSpawnQueue.Count == 0 &&
                PendingEnemySpawns.Count == 0 && Enemies.All(enemy => enemy.IsDead) &&
                _generatedFiniteWavesRemaining == 0 && !_generatedWaveBreakActive,
            EncounterObjectiveType.RelayDefense => RelayObjective?.RemainingSeconds <= 0f,
            EncounterObjectiveType.EliteHunt => _eliteTargetId != EntityId.None &&
                Enemies.All(enemy => enemy.Id != _eliteTargetId || enemy.IsDead),
            _ => false,
        };
        if (!completed)
        {
            return;
        }

        CompleteGeneratedEncounter(encounter);
    }

    private void CompleteGeneratedEncounter(EncounterDefinition encounter)
    {
        float completionMetric = encounter.ObjectiveType switch
        {
            EncounterObjectiveType.RelayDefense when RelayObjective is not null =>
                RelayObjective.Health / RelayObjective.MaximumHealth,
            EncounterObjectiveType.EliteHunt => _encounterElapsedSeconds,
            _ => 1f,
        };
        LastCompletedEncounterObjective = encounter.ObjectiveType;
        LastCompletedEncounterMetric = completionMetric;
        switch (encounter.ObjectiveType)
        {
            case EncounterObjectiveType.Purge:
                _completedPurgeEncounters++;
                break;
            case EncounterObjectiveType.RelayDefense:
                _completedRelayEncounters++;
                break;
            case EncounterObjectiveType.EliteHunt:
                _completedEliteEncounters++;
                break;
        }

        foreach (EnemyState enemy in Enemies.Where(enemy => !enemy.IsDead))
        {
            enemy.Health = 0f;
            enemy.ActionState = EnemyActionState.Death;
        }

        Projectiles.RemoveAll(projectile => projectile.IsHostile);
        _generatedSpawnQueue.Clear();
        PendingEnemySpawns.Clear();
        RelayObjective = null;
        Score += 500 * (_runDirector!.CurrentEncounterIndex + 1);
        Player.Health = MathF.Min(Player.MaximumHealth,
            Player.Health + _runDirector.Modifiers.EncounterHealing);
        while (_guaranteedWeaponFamilies.TryDequeue(out WeaponFamily family))
        {
            EquipmentInstance item = GenerateLoot(-7_000 - _lootDropSerial, requiredWeaponFamily: family);
            AddEquipmentDrop(item, Player.Position + new Vector3(0f, -PlayerEyeHeight, 1.5f));
        }
        GatherRecoveryLoot(encounter);
        CreditEquippedAbilityPoints();
        TeleportPlayerToRecoveryHub();
        UpgradeOffer offer = _runDirector.CompleteEncounter();
        AddEvent(CombatEventType.EncounterCompleted, Player.Position, Player.Position,
            EntityId.None, Player.Id, encounter.Id, completionMetric);
        AddEvent(CombatEventType.UpgradeOffered, Player.Position, Player.Position,
            EntityId.None, Player.Id, string.Join(',', offer.Choices.Select(choice => choice.Id)));
    }

    private void GatherRecoveryLoot(EncounterDefinition encounter)
    {
        EquipmentInstance[] uncollected = Pickups
            .Where(pickup => pickup.IsAvailable && pickup.Type == PickupType.Equipment && pickup.Equipment is not null)
            .Select(pickup => pickup.Equipment!)
            .ToArray();
        Pickups.RemoveAll(pickup => pickup.Type == PickupType.Equipment && pickup.IsDropped);
        _recoveryCache.Gather(uncollected);
        for (int index = 0; index < StandardRpgCatalog.StandardLootTable.EncounterCacheDropCount; index++)
        {
            _recoveryCache.Gather([GenerateLoot(-1000 - (_runDirector!.CurrentEncounterIndex * 10) - index)]);
        }

        AddEvent(CombatEventType.RecoveryCacheOpened, GetArmoryPosition(), Player.Position,
            EntityId.None, Player.Id, encounter.Id, _recoveryCache.Items.Count);
    }

    private void CreditEquippedAbilityPoints()
    {
        int amount = Math.Max(1, (int)MathF.Round(
            10f * RpgProgressionMath.PersistentRewardMultiplier(ThreatTier) *
            CalculateRewardMultiplier(AffixEffectType.AbilityPointGain)));
        foreach (string itemId in EquipmentLoadout.EquippedItemIds.Values
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_equipmentItems.TryGetValue(itemId, out EquipmentInstance? item))
            {
                continue;
            }

            string? abilityId = item.WeaponBaseId is string weaponId &&
                _catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? weapon)
                    ? weapon.TaughtAbilityId
                    : item.EquipmentBaseId is string equipmentId &&
                        _catalog.EquipmentBases.TryGetValue(equipmentId, out EquipmentBaseDefinition? equipment)
                            ? equipment.TaughtAbilityId
                            : null;
            if (!string.IsNullOrWhiteSpace(abilityId))
            {
                _pendingProgression.AbilityPoints[abilityId] =
                    _pendingProgression.AbilityPoints.GetValueOrDefault(abilityId) + amount;
            }
        }
    }

    private void ActivateArmoryPickup(int completedEncounterIndex)
    {
        if (completedEncounterIndex is < 0 or >= 5)
        {
            return;
        }

        HashSet<string> unavailable = Player.Weapons
            .Select(weapon => weapon.Definition.Id)
            .Concat(Pickups.Where(pickup => pickup.Type == PickupType.Weapon && pickup.IsAvailable)
                .Select(pickup => pickup.WeaponId)
                .OfType<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? weaponId = _catalog.Weapons.Keys
            .Where(id => !unavailable.Contains(id))
            .OrderBy(id => StableArmoryOrder(id, completedEncounterIndex))
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (weaponId is null)
        {
            return;
        }

        AddArmoryPickup(weaponId);
        AddEvent(CombatEventType.ArmoryActivated, GetArmoryPosition(), Player.Position,
            EntityId.None, Player.Id, weaponId, completedEncounterIndex + 1);
    }

    private void AddArmoryPickup(string weaponId) => Pickups.Add(new PickupState
    {
        Id = NextEntity(),
        Type = PickupType.Weapon,
        Position = GetArmoryPosition(),
        Amount = 40,
        WeaponId = weaponId,
        IsDropped = true,
    });

    private static bool IsActiveArmoryPickup(PickupState pickup) =>
        pickup.IsDropped && pickup.IsAvailable && pickup.Type == PickupType.Weapon &&
        !string.IsNullOrWhiteSpace(pickup.WeaponId);

    private void TeleportPlayerToRecoveryHub()
    {
        Vector3 armory = GetArmoryPosition();
        Vector3 recoverySpawn = new(
            armory.X,
            MathF.Max(PlayerEyeHeight, armory.Y),
            Math.Clamp(armory.Z + 5f, Arena.BoundsMin.Z + 1f, Arena.BoundsMax.Z - 1f));
        _playerController.Teleport(recoverySpawn);
        Player.Position = recoverySpawn;
        Player.PreviousPosition = recoverySpawn;
        Player.VerticalVelocity = 0f;
        Player.IsGrounded = true;
    }

    private Vector3 GetArmoryPosition()
    {
        Vector3 position = Arena.BossArenaAnchor == Vector3.Zero
            ? Arena.PlayerSpawn
            : Arena.BossArenaAnchor;
        return new Vector3(position.X, MathF.Max(0.7f, position.Y), position.Z);
    }

    private int StableArmoryOrder(string weaponId, int encounterIndex)
    {
        int value = (_runDirector?.Seed ?? 0) ^ ((encounterIndex + 1) * 104729);
        foreach (char character in weaponId)
        {
            value = unchecked((value * 31) + character);
        }

        return value;
    }

    private void BeginGeneratedBoss()
    {
        _generatedBossStarted = true;
        CurrentWaveIndex = RunDirector.EncounterCount;
        Enemies.RemoveAll(enemy => !enemy.IsDead);
        Vector3 bossCenter = Arena.BossArenaAnchor == Vector3.Zero
            ? new Vector3(0f, 0.7f, 0f)
            : Arena.BossArenaAnchor;
        Vector3 playerSpawn = new(
            bossCenter.X + MathF.Max(2f, Arena.BossArenaHalfExtents.X - 2f),
            MathF.Max(PlayerEyeHeight, bossCenter.Y),
            bossCenter.Z);
        _playerController.Teleport(playerSpawn);
        Player.Position = playerSpawn;
        Player.PreviousPosition = playerSpawn;
        Vector3 bossViewDirection = bossCenter - playerSpawn;
        Player.Yaw = MathF.Atan2(bossViewDirection.X, -bossViewDirection.Z);
        Player.Pitch = 0f;
        Player.VerticalVelocity = 0f;
        Player.IsGrounded = true;
        Projectiles.Clear();
        EnemyDefinition? boss = _catalog.Enemies.Values
            .Where(definition => definition.SchemaVersion >= 2 && definition.IsBoss)
            .OrderBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (boss is null)
        {
            throw new InvalidOperationException("The sector campaign needs a boss enemy.");
        }

        Vector3 position = Arena.BossArenaAnchor == Vector3.Zero
            ? Arena.EnemySpawns[0]
            : Arena.BossArenaAnchor;
        PendingEnemySpawns.Add(new PendingEnemySpawn
        {
            EnemyId = boss.Id,
            PortalId = "boss-core",
            Position = position,
            RemainingSeconds = 0.75f,
        });
        AddEvent(CombatEventType.EnemySpawnTelegraph, position, Player.Position,
            EntityId.None, Player.Id, "boss-core", 0.75f);
    }

    private static bool IsRoleAvailable(EnemyBehavior behavior, int sectorNumber) => behavior switch
    {
        EnemyBehavior.Chaser or EnemyBehavior.Skirmisher => true,
        EnemyBehavior.Spitter or EnemyBehavior.Charger => sectorNumber >= 2,
        EnemyBehavior.Warden => sectorNumber >= 3,
        _ => false,
    };

    private static float ThreatWeight(EnemyDefinition definition)
    {
        if (MathF.Abs(definition.ThreatWeight - 1f) > 0.001f)
        {
            return definition.ThreatWeight;
        }

        return definition.Behavior switch
        {
            EnemyBehavior.Chaser => 1f,
            EnemyBehavior.Skirmisher => 1.2f,
            EnemyBehavior.Spitter => 1.3f,
            EnemyBehavior.Warden => 2.2f,
            EnemyBehavior.Charger => 2.5f,
            _ => 1f,
        };
    }

    private float CalculateObjectiveProgress()
    {
        if (_runDirector is null)
        {
            return TotalWaves <= 0 ? 0f : Math.Clamp((float)CurrentWaveIndex / TotalWaves, 0f, 1f);
        }

        if (_runDirector.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.RewardSelection)
        {
            return 1f;
        }

        if (_runDirector.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.BossActive)
        {
            EnemyState? boss = ActiveBoss;
            return boss is null ? 0f : 1f - Math.Clamp(boss.Health / boss.MaximumHealth, 0f, 1f);
        }

        EncounterDefinition? encounter = _runDirector.CurrentEncounter;
        if (encounter is null)
        {
            return _runDirector.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.Victory ? 1f : 0f;
        }

        return encounter.ObjectiveType switch
        {
            EncounterObjectiveType.RelayDefense when RelayObjective is not null => 1f - Math.Clamp(
                RelayObjective.RemainingSeconds / MathF.Max(0.001f, encounter.RelayDurationSeconds), 0f, 1f),
            EncounterObjectiveType.EliteHunt => CalculateEliteProgress(),
            _ => _generatedEncounterEnemyTotal <= 0 ? 0f : 1f - Math.Clamp(
                (float)(_generatedSpawnQueue.Count + PendingEnemySpawns.Count +
                    Enemies.Count(enemy => !enemy.IsDead)) / _generatedEncounterEnemyTotal,
                0f,
                1f),
        };
    }

    private float CalculateEliteProgress()
    {
        EnemyState? elite = Enemies.FirstOrDefault(enemy => enemy.Id == _eliteTargetId);
        return elite is null || _eliteTargetId == EntityId.None
            ? 0f
            : 1f - Math.Clamp(elite.Health / elite.MaximumHealth, 0f, 1f);
    }

    private void UpdateWaveDirector(float deltaSeconds)
    {
        if (!_waveActive)
        {
            _interWaveRemainingSeconds -= deltaSeconds;
            if (_interWaveRemainingSeconds <= 0f)
            {
                BeginWave();
            }

            return;
        }

        WaveDefinition wave = _runWaves[CurrentWaveIndex];
        _spawnRemainingSeconds -= deltaSeconds;
        int living = Enemies.Count(enemy => !enemy.IsDead);
        if (_pendingEnemies > 0 && living < wave.MaximumConcurrentEnemies && _spawnRemainingSeconds <= 0f)
        {
            while (_pendingInSpawnGroup == 0 && _spawnGroupCursor < wave.SpawnGroups.Count - 1)
            {
                _spawnGroupCursor++;
                _pendingInSpawnGroup = wave.SpawnGroups[_spawnGroupCursor].Count;
            }

            SpawnEnemy(wave.SpawnGroups[_spawnGroupCursor].EnemyId);
            _pendingEnemies--;
            _pendingInSpawnGroup--;
            _spawnRemainingSeconds = wave.SpawnIntervalSeconds;
            living = Enemies.Count(enemy => !enemy.IsDead);
        }

        if (_pendingEnemies == 0 && living == 0)
        {
            _waveActive = false;
            Score += 500 * (CurrentWaveIndex + 1);
            CurrentWaveIndex++;
            if (CurrentWaveIndex >= _runWaves.Count)
            {
                Score += (int)(Player.Health * 10f);
                Score += Math.Max(0, 3000 - (int)(ElapsedRunSeconds * 25f));
                Phase = GamePhase.Victory;
            }
            else
            {
                _interWaveRemainingSeconds = _waveSet.InterWaveDelaySeconds;
            }
        }
    }

    private void BeginWave()
    {
        WaveDefinition wave = _runWaves[CurrentWaveIndex];
        _pendingEnemies = wave.SpawnGroups.Sum(group => group.Count);
        _spawnGroupCursor = 0;
        _pendingInSpawnGroup = wave.SpawnGroups[0].Count;
        _spawnRemainingSeconds = 0f;
        _waveActive = true;
        AddEvent(CombatEventType.WaveStarted, Player.Position, Player.Position,
            EntityId.None, Player.Id, IsBossWave ? "boss-wave" : wave.Id, CurrentWaveIndex + 1);
    }

    private void SpawnEnemy(string enemyId)
    {
        Vector3 spawn = Arena.EnemySpawns[_spawnCursor++ % Arena.EnemySpawns.Count];
        SpawnEnemyAt(enemyId, spawn, false);
    }

    private EnemyState SpawnEnemyAt(string enemyId, Vector3 spawn, bool isElite, float healthFraction = 1f)
    {
        EnemyDefinition definition = _catalog.Enemies[enemyId];
        float eliteHealthMultiplier = _runDirector?.CurrentEncounter?.EliteHealthMultiplier ?? 1.4f;
        float maximumHealth = definition.MaxHealth * (isElite ? eliteHealthMultiplier : 1f) *
            RpgProgressionMath.EnemyHealthMultiplier(ThreatTier) *
            DifficultyCatalog.Get(Difficulty).EnemyHealthMultiplier;
        EntityId id = NextEntity();
        EnemyState enemy = new()
        {
            Id = id,
            Definition = definition,
            Position = spawn,
            PreviousPosition = spawn,
            LastProgressPosition = spawn,
            Health = maximumHealth * Math.Clamp(healthFraction, 0.01f, 1f),
            MaximumHealth = maximumHealth,
            PathRefreshRemainingSeconds = 0f,
            SupportPulseRemainingSeconds = definition.SupportPulseSeconds,
            StrafeDirection = (id.Value & 1) == 0 ? 1 : -1,
            IsElite = isElite,
            TargetsRelay = RelayObjective is not null && id.Value % 3 != 0,
            ActionState = EnemyActionState.Idle,
        };
        Enemies.Add(enemy);
        return enemy;
    }

    private void UpdateEnemies(float deltaSeconds)
    {
        _queuedRecycles.Clear();
        int enemyCount = Enemies.Count;
        for (int enemyIndex = 0; enemyIndex < enemyCount; enemyIndex++)
        {
            EnemyState enemy = Enemies[enemyIndex];
            enemy.PreviousPosition = enemy.Position;
            enemy.AttackCooldownSeconds = MathF.Max(0f, enemy.AttackCooldownSeconds - deltaSeconds);
            enemy.HitFlashSeconds = MathF.Max(0f, enemy.HitFlashSeconds - deltaSeconds);
            enemy.AttackAnimationSeconds = MathF.Max(0f, enemy.AttackAnimationSeconds - deltaSeconds);
            enemy.AiTimerSeconds = MathF.Max(0f, enemy.AiTimerSeconds - deltaSeconds);
            enemy.ControlImpairmentSeconds = MathF.Max(0f, enemy.ControlImpairmentSeconds - deltaSeconds);
            if (!enemy.IsDead && enemy.DamageOverTimeRemaining > 0f)
            {
                enemy.DamageOverTimeRemaining = MathF.Max(0f, enemy.DamageOverTimeRemaining - deltaSeconds);
                DamageEnemy(enemy, enemy.DamageOverTimePerSecond * deltaSeconds, enemy.Position,
                    Player.Id, enemy.DamageOverTimeSourceId);
            }
            if (enemy.IsDead)
            {
                enemy.ActionState = EnemyActionState.Death;
                enemy.DeathSeconds += deltaSeconds;
                continue;
            }

            if (enemy.ControlImpairmentSeconds > 0f)
            {
                enemy.ActionState = EnemyActionState.HitReaction;
                continue;
            }

            if (enemy.ActionState == EnemyActionState.HitReaction)
            {
                if (enemy.AiTimerSeconds > 0f)
                {
                    continue;
                }

                enemy.ActionState = EnemyActionState.Idle;
            }

            Vector3 targetPosition = GetEnemyTargetPosition(enemy);
            Vector3 toPlayer = targetPosition - enemy.Position;
            toPlayer.Y = 0f;
            float distance = toPlayer.Length();
            if (distance > 0.001f)
            {
                enemy.FacingYaw = MathF.Atan2(toPlayer.X, toPlayer.Z);
            }

            if (enemy.Definition.IsBoss)
            {
                UpdateBossPhase(enemy);
            }

            if (UpdatePendingAttack(enemy))
            {
                continue;
            }

            if (enemy.ActionState is EnemyActionState.Idle or EnemyActionState.Locomotion or EnemyActionState.Navigating)
            {
                enemy.ActionState = EnemyActionState.Idle;
            }

            switch (enemy.Definition.Behavior)
            {
                case EnemyBehavior.Chaser:
                    UpdateChaser(enemy, distance, deltaSeconds);
                    break;
                case EnemyBehavior.Skirmisher:
                    UpdateRangedEnemy(enemy, distance, deltaSeconds, spreadDegrees: 0f);
                    break;
                case EnemyBehavior.Charger:
                    UpdateCharger(enemy, toPlayer, distance, deltaSeconds, 1f, 1f);
                    break;
                case EnemyBehavior.Spitter:
                    UpdateRangedEnemy(enemy, distance, deltaSeconds, spreadDegrees: 2.5f);
                    break;
                case EnemyBehavior.Warden:
                    UpdateWarden(enemy, distance, deltaSeconds);
                    break;
                case EnemyBehavior.Boss:
                    UpdateBoss(enemy, toPlayer, distance, deltaSeconds);
                    break;
            }

            UpdateEnemyProgress(enemy, deltaSeconds);
        }

        for (int summonIndex = _queuedSummons.Count - 1; summonIndex >= 0; summonIndex--)
        {
            if (TryQueueBossSummon(_queuedSummons[summonIndex]))
            {
                _queuedSummons.RemoveAt(summonIndex);
            }
        }

        RecycleStalledEnemies();

        ApplyEnemySeparation();
        ApplyPlayerEnemySeparation();
        UpdateEnemyMovementFacing();
    }

    private void UpdateEnemyMovementFacing()
    {
        foreach (EnemyState enemy in Enemies)
        {
            if (enemy.IsDead || enemy.ActionState is not (EnemyActionState.Locomotion or EnemyActionState.Charging))
            {
                continue;
            }

            Vector3 movement = enemy.Position - enemy.PreviousPosition;
            movement.Y = 0f;
            if (movement.LengthSquared() > 0.000001f)
            {
                enemy.FacingYaw = MathF.Atan2(movement.X, movement.Z);
            }
        }
    }

    private bool TryQueueBossSummon(string enemyId)
    {
        int active = Enemies.Count(enemy => !enemy.IsDead) + PendingEnemySpawns.Count;
        if (!_catalog.Enemies.TryGetValue(enemyId, out EnemyDefinition? definition) ||
            definition.SchemaVersion < 2 || definition.IsBoss)
        {
            return true;
        }

        if (active >= 8)
        {
            return false;
        }

        SpawnPortalDefinition? portal = SelectBossSummonPortal();
        if (portal is null)
        {
            return false;
        }

        PendingEnemySpawns.Add(new PendingEnemySpawn
        {
            EnemyId = enemyId,
            PortalId = portal.Id,
            Position = portal.Position,
            RemainingSeconds = MathF.Max(0.75f, portal.TelegraphSeconds),
        });
        AddEvent(CombatEventType.EnemySpawnTelegraph, portal.Position, Player.Position,
            EntityId.None, Player.Id, portal.Id, MathF.Max(0.75f, portal.TelegraphSeconds));
        return true;
    }

    private SpawnPortalDefinition? SelectBossSummonPortal(PendingEnemySpawn? ignoredPending = null)
    {
        Vector3 center = Arena.BossArenaAnchor == Vector3.Zero
            ? new Vector3(0f, 0.7f, 0f)
            : Arena.BossArenaAnchor;
        ReadOnlySpan<Vector2> offsets =
        [
            new(-12.5f, 0f),
            new(12.5f, 0f),
            new(-9f, -8.5f),
            new(9f, -8.5f),
            new(-9f, 8.5f),
            new(9f, 8.5f),
            new(0f, -10.5f),
            new(0f, 10.5f),
        ];
        for (int attempt = 0; attempt < offsets.Length; attempt++)
        {
            int index = (_bossSummonPortalCursor + attempt) % offsets.Length;
            Vector2 offset = offsets[index];
            Vector3 position = new(center.X + offset.X, center.Y, center.Z + offset.Y);
            float playerDistance = Vector3.Distance(position, Player.Position);
            bool occupied = Enemies.Any(enemy => !enemy.IsDead &&
                Vector3.DistanceSquared(enemy.Position, position) < 2.25f) ||
                PendingEnemySpawns.Any(pending => !ReferenceEquals(pending, ignoredPending) &&
                    Vector3.DistanceSquared(pending.Position, position) < 2.25f);
            if (playerDistance is < 12f or > 32f || occupied || !_navigation.IsWalkable(position))
            {
                continue;
            }

            _bossSummonPortalCursor = (index + 1) % offsets.Length;
            return new SpawnPortalDefinition
            {
                Id = $"boss-summon-{index + 1}",
                Position = position,
                TelegraphSeconds = 0.75f,
            };
        }

        return null;
    }

    private void UpdateEnemyProgress(EnemyState enemy, float deltaSeconds)
    {
        Vector2 previous = new(enemy.PreviousPosition.X, enemy.PreviousPosition.Z);
        Vector2 current = new(enemy.Position.X, enemy.Position.Z);
        if (Vector2.DistanceSquared(previous, current) > 0.000025f)
        {
            enemy.StalledSeconds = 0f;
            enemy.LastProgressPosition = enemy.Position;
            return;
        }

        if (enemy.ActionState is not (EnemyActionState.Locomotion or EnemyActionState.Navigating))
        {
            enemy.StalledSeconds = 0f;
            return;
        }

        enemy.StalledSeconds += deltaSeconds;
        if (enemy.StalledSeconds >= 2f)
        {
            enemy.PathRefreshRemainingSeconds = 0f;
            enemy.Path.Clear();
            enemy.PathIndex = 0;
        }

        if (enemy.StalledSeconds >= 8f && _runDirector?.CurrentEncounter is not null && !enemy.Definition.IsBoss)
        {
            _queuedRecycles.Add(enemy.Id);
        }
    }

    private void RecycleStalledEnemies()
    {
        if (_queuedRecycles.Count == 0)
        {
            return;
        }

        ArenaSectorDefinition? sector = ActiveSector ?? Arena.Sectors.FirstOrDefault();
        if (sector is null)
        {
            return;
        }

        foreach (EntityId enemyId in _queuedRecycles)
        {
            EnemyState? enemy = Enemies.FirstOrDefault(candidate => candidate.Id == enemyId && !candidate.IsDead);
            SpawnPortalDefinition? portal = SelectSpawnPortal(sector);
            if (enemy is null || portal is null)
            {
                if (enemy is not null)
                {
                    enemy.StalledSeconds = 4f;
                }

                continue;
            }

            PendingEnemySpawns.Add(new PendingEnemySpawn
            {
                EnemyId = enemy.Definition.Id,
                PortalId = portal.Id,
                Position = portal.Position,
                RemainingSeconds = MathF.Max(0.75f, portal.TelegraphSeconds),
                IsElite = enemy.IsElite,
                HealthFraction = enemy.Health / enemy.MaximumHealth,
            });
            if (enemy.Id == _eliteTargetId)
            {
                _eliteTargetId = EntityId.None;
            }

            Enemies.Remove(enemy);
            AddEvent(CombatEventType.EnemySpawnTelegraph, portal.Position, Player.Position,
                EntityId.None, Player.Id, "recycle", MathF.Max(0.75f, portal.TelegraphSeconds));
        }
    }

    private void UpdateChaser(EnemyState enemy, float distance, float deltaSeconds)
    {
        if (!TryMeleeAttack(enemy, distance, 1f))
        {
            NavigateEnemyTowardPlayer(enemy, enemy.Definition.MoveSpeed, deltaSeconds);
        }
    }

    private void UpdateRangedEnemy(EnemyState enemy, float distance, float deltaSeconds, float spreadDegrees)
    {
        MaintainEnemyRange(enemy, distance, deltaSeconds);
        if (distance <= enemy.Definition.RangedAttackRange && enemy.AttackCooldownSeconds <= 0f)
        {
            BeginEnemyAttack(
                enemy,
                EnemyAttackKind.Projectile,
                enemy.Definition.IsBoss ? "boss-shot" : "enemy-shot",
                enemy.Definition.AttackWindupSeconds,
                1f,
                spreadDegrees);
        }
    }

    private void UpdateCharger(
        EnemyState enemy,
        Vector3 toPlayer,
        float distance,
        float deltaSeconds,
        float speedMultiplier,
        float damageMultiplier)
    {
        switch (enemy.ActionState)
        {
            case EnemyActionState.Windup:
                if (enemy.AiTimerSeconds <= 0f)
                {
                    enemy.ActionState = EnemyActionState.Charging;
                    enemy.AiTimerSeconds = enemy.Definition.ChargeDurationSeconds;
                    enemy.ChargeDirection = distance > 0.001f ? Vector3.Normalize(toPlayer) : Vector3.UnitZ;
                    AddEvent(CombatEventType.EnemyAttack, enemy.Position, GetEnemyTargetPosition(enemy),
                        enemy.Id, enemy.TargetsRelay ? EntityId.None : Player.Id, "charge");
                }

                return;
            case EnemyActionState.Charging:
                if (distance <= enemy.Definition.AttackRange + 0.65f)
                {
                    DamageEnemyTarget(enemy, enemy.Definition.AttackDamage * damageMultiplier, "charge-impact");
                    AddEvent(CombatEventType.EnemyAttackImpact, enemy.Position, GetEnemyTargetPosition(enemy),
                        enemy.Id, enemy.TargetsRelay ? EntityId.None : Player.Id, "charge-impact",
                        enemy.Definition.AttackDamage * damageMultiplier);
                    enemy.ActionState = EnemyActionState.Recovering;
                    enemy.AiTimerSeconds = 0.55f;
                    return;
                }

                if (enemy.AiTimerSeconds <= 0f ||
                    !MoveEnemy(enemy, enemy.ChargeDirection, enemy.Definition.ChargeSpeed * speedMultiplier, deltaSeconds))
                {
                    enemy.ActionState = EnemyActionState.Recovering;
                    enemy.AiTimerSeconds = 0.55f;
                }

                return;
            case EnemyActionState.Recovering:
                if (enemy.AiTimerSeconds <= 0f)
                {
                    enemy.ActionState = EnemyActionState.Navigating;
                }

                return;
        }

        if (TryMeleeAttack(enemy, distance, damageMultiplier))
        {
            return;
        }

        if (distance <= enemy.Definition.ChargeRange && enemy.AttackCooldownSeconds <= 0f)
        {
            float chargeWindup = ScaleAttackTell(enemy, enemy.Definition.ChargeWindupSeconds);
            enemy.ActionState = EnemyActionState.Windup;
            enemy.AiTimerSeconds = chargeWindup;
            enemy.AttackCooldownSeconds = ScaleAttackInterval(enemy.Definition.ChargeCooldownSeconds);
            enemy.AttackAnimationSeconds = chargeWindup;
            AddEvent(CombatEventType.EnemyTelegraph, enemy.Position, GetEnemyTargetPosition(enemy),
                enemy.Id, enemy.TargetsRelay ? EntityId.None : Player.Id,
                "charge-windup", chargeWindup);
            AddEvent(CombatEventType.EnemyAttackStarted, enemy.Position, GetEnemyTargetPosition(enemy),
                enemy.Id, enemy.TargetsRelay ? EntityId.None : Player.Id,
                "charge-windup", chargeWindup);
            return;
        }

        NavigateEnemyTowardPlayer(enemy, enemy.Definition.MoveSpeed * speedMultiplier, deltaSeconds);
    }

    private void UpdateWarden(EnemyState enemy, float distance, float deltaSeconds)
    {
        enemy.SupportPulseRemainingSeconds -= deltaSeconds;
        if (enemy.SupportPulseRemainingSeconds <= 0f)
        {
            enemy.SupportPulseRemainingSeconds = enemy.Definition.SupportPulseSeconds;
            foreach (EnemyState ally in Enemies)
            {
                if (ally.IsDead || Vector3.DistanceSquared(enemy.Position, ally.Position) >
                    enemy.Definition.SupportRadius * enemy.Definition.SupportRadius)
                {
                    continue;
                }

                ally.Health = MathF.Min(ally.MaximumHealth, ally.Health + enemy.Definition.SupportHealAmount);
            }

            AddEvent(CombatEventType.SupportPulse, enemy.Position, enemy.Position,
                enemy.Id, EntityId.None, "warden-pulse", enemy.Definition.SupportRadius);
        }

        UpdateRangedEnemy(enemy, distance, deltaSeconds, spreadDegrees: 1.5f);
    }

    private void UpdateBoss(EnemyState enemy, Vector3 toPlayer, float distance, float deltaSeconds)
    {
        BossPhaseDefinition phase = enemy.Definition.BossPhases[enemy.CurrentBossPhaseIndex];
        if (enemy.CurrentBossPhaseIndex >= 2)
        {
            UpdateCharger(enemy, toPlayer, distance, deltaSeconds,
                phase.MoveSpeedMultiplier, phase.DamageMultiplier);
            if (distance > enemy.Definition.ChargeRange && enemy.AttackCooldownSeconds <= 0f)
            {
                BeginBossVolley(enemy, 5, 9f, phase);
            }

            return;
        }

        MaintainEnemyRange(enemy, distance, deltaSeconds, phase.MoveSpeedMultiplier);
        if (distance <= enemy.Definition.RangedAttackRange && enemy.AttackCooldownSeconds <= 0f)
        {
            int bolts = enemy.CurrentBossPhaseIndex == 0 ? 1 : 3;
            float spread = enemy.CurrentBossPhaseIndex == 0 ? 0f : 7f;
            BeginBossVolley(enemy, bolts, spread, phase);
        }
    }

    private void BeginBossVolley(EnemyState enemy, int projectileCount, float spreadDegrees, BossPhaseDefinition phase)
    {
        BeginEnemyAttack(
            enemy,
            EnemyAttackKind.BossVolley,
            "boss-volley",
            MathF.Max(0.7f, enemy.Definition.AttackWindupSeconds),
            phase.DamageMultiplier,
            0f,
            projectileCount,
            spreadDegrees,
            phase.ProjectileSpeedMultiplier);
        enemy.AttackCooldownSeconds = ScaleAttackInterval(
            enemy.Definition.AttackCooldownSeconds * phase.AttackCooldownMultiplier);
    }

    private void UpdateBossPhase(EnemyState enemy)
    {
        float healthFraction = enemy.Health / enemy.MaximumHealth;
        int phaseIndex = 0;
        for (int index = 1; index < enemy.Definition.BossPhases.Count; index++)
        {
            if (healthFraction <= enemy.Definition.BossPhases[index].HealthThreshold)
            {
                phaseIndex = index;
            }
        }

        if (phaseIndex <= enemy.CurrentBossPhaseIndex)
        {
            return;
        }

        enemy.CurrentBossPhaseIndex = phaseIndex;
        enemy.PendingAttackKind = EnemyAttackKind.None;
        enemy.PendingAttackDamageMultiplier = 1f;
        enemy.PendingAttackSpreadDegrees = 0f;
        enemy.PendingAttackSpeedMultiplier = 1f;
        enemy.PendingAttackProjectileCount = 1;
        enemy.PendingAttackProjectileSpreadDegrees = 0f;
        enemy.PendingAttackTargetsRelay = false;
        enemy.AiTimerSeconds = 0f;
        enemy.AttackAnimationSeconds = 0f;
        enemy.ActionState = EnemyActionState.Navigating;
        enemy.AttackCooldownSeconds = 0f;
        BossPhaseDefinition phase = enemy.Definition.BossPhases[phaseIndex];
        AddEvent(CombatEventType.BossPhaseChanged, enemy.Position, Player.Position,
            enemy.Id, Player.Id, phase.DisplayName, phaseIndex + 1);
        if (phase.SummonCount > 0 && phase.SummonEnemyId is not null)
        {
            for (int summon = 0; summon < phase.SummonCount; summon++)
            {
                _queuedSummons.Add(phase.SummonEnemyId);
            }
        }
    }

    private bool TryMeleeAttack(EnemyState enemy, float distance, float damageMultiplier)
    {
        if (distance > enemy.Definition.AttackRange || enemy.AttackCooldownSeconds > 0f)
        {
            return false;
        }

        BeginEnemyAttack(
            enemy,
            EnemyAttackKind.Melee,
            "melee",
            enemy.Definition.AttackWindupSeconds,
            damageMultiplier);
        return true;
    }

    private void BeginEnemyAttack(
        EnemyState enemy,
        EnemyAttackKind kind,
        string cueId,
        float windupSeconds,
        float damageMultiplier,
        float spreadDegrees = 0f,
        int projectileCount = 1,
        float projectileSpreadDegrees = 0f,
        float speedMultiplier = 1f)
    {
        enemy.PendingAttackKind = kind;
        enemy.PendingAttackDamageMultiplier = damageMultiplier;
        enemy.PendingAttackSpreadDegrees = spreadDegrees;
        enemy.PendingAttackProjectileCount = projectileCount;
        enemy.PendingAttackProjectileSpreadDegrees = projectileSpreadDegrees;
        enemy.PendingAttackSpeedMultiplier = speedMultiplier;
        enemy.PendingAttackTargetsRelay = enemy.TargetsRelay && RelayObjective is not null;
        windupSeconds = ScaleAttackTell(enemy, windupSeconds);
        enemy.ActionState = EnemyActionState.Windup;
        enemy.AiTimerSeconds = windupSeconds;
        enemy.AttackCooldownSeconds = ScaleAttackInterval(enemy.Definition.AttackCooldownSeconds);
        enemy.AttackAnimationSeconds = windupSeconds + enemy.Definition.AttackRecoverySeconds;
        Vector3 targetPosition = GetEnemyTargetPosition(enemy);
        EntityId targetId = enemy.PendingAttackTargetsRelay ? EntityId.None : Player.Id;
        AddEvent(CombatEventType.EnemyTelegraph, enemy.Position, targetPosition,
            enemy.Id, targetId, cueId, windupSeconds);
        AddEvent(CombatEventType.EnemyAttackStarted, enemy.Position, targetPosition,
            enemy.Id, targetId, cueId, windupSeconds);
    }

    private float ScaleAttackTell(EnemyState enemy, float authoredSeconds)
    {
        float minimum = enemy.Definition.IsBoss ? 0.70f : 0.45f;
        return MathF.Max(minimum,
            authoredSeconds * DifficultyCatalog.Get(Difficulty).TellDurationMultiplier);
    }

    private float ScaleAttackInterval(float authoredSeconds) => authoredSeconds *
        DifficultyCatalog.Get(Difficulty).AttackIntervalMultiplier;

    private bool UpdatePendingAttack(EnemyState enemy)
    {
        if (enemy.PendingAttackKind == EnemyAttackKind.None)
        {
            return false;
        }

        if (enemy.ActionState == EnemyActionState.Windup && enemy.AiTimerSeconds <= 0f)
        {
            enemy.ActionState = EnemyActionState.ActiveAttack;
            ResolvePendingAttack(enemy);
            enemy.AiTimerSeconds = 0.05f;
            return true;
        }

        if (enemy.ActionState == EnemyActionState.ActiveAttack && enemy.AiTimerSeconds <= 0f)
        {
            enemy.ActionState = EnemyActionState.Recovering;
            enemy.AiTimerSeconds = enemy.Definition.AttackRecoverySeconds;
            return true;
        }

        if (enemy.ActionState == EnemyActionState.Recovering && enemy.AiTimerSeconds <= 0f)
        {
            enemy.PendingAttackKind = EnemyAttackKind.None;
            enemy.ActionState = EnemyActionState.Idle;
            return false;
        }

        return true;
    }

    private void ResolvePendingAttack(EnemyState enemy)
    {
        float damage = enemy.Definition.AttackDamage * enemy.PendingAttackDamageMultiplier;
        switch (enemy.PendingAttackKind)
        {
            case EnemyAttackKind.Melee:
                float distance = Vector3.Distance(enemy.Position, GetEnemyTargetPosition(enemy));
                if (distance <= enemy.Definition.AttackRange + 0.65f)
                {
                    DamageEnemyTarget(enemy, damage, "melee");
                }

                break;
            case EnemyAttackKind.Projectile:
                FireEnemyProjectile(
                    enemy,
                    enemy.PendingAttackSpreadDegrees,
                    damage,
                    enemy.PendingAttackSpeedMultiplier,
                    enemy.PendingAttackTargetsRelay);
                break;
            case EnemyAttackKind.BossVolley:
                float center = (enemy.PendingAttackProjectileCount - 1) * 0.5f;
                for (int index = 0; index < enemy.PendingAttackProjectileCount; index++)
                {
                    FireEnemyProjectile(
                        enemy,
                        (index - center) * enemy.PendingAttackProjectileSpreadDegrees,
                        damage,
                        enemy.PendingAttackSpeedMultiplier,
                        enemy.PendingAttackTargetsRelay);
                }

                break;
        }

        AddEvent(CombatEventType.EnemyAttackImpact, enemy.Position, GetEnemyTargetPosition(enemy),
            enemy.Id, enemy.PendingAttackTargetsRelay ? EntityId.None : Player.Id,
            enemy.PendingAttackKind.ToString(), damage);
    }

    private void MaintainEnemyRange(EnemyState enemy, float distance, float deltaSeconds, float speedMultiplier = 1f)
    {
        Vector3 targetPosition = GetEnemyTargetPosition(enemy);
        float preferredRange = MathF.Max(enemy.Definition.AttackRange + 1f, enemy.Definition.PreferredRange);
        if (distance > preferredRange + 1.5f)
        {
            NavigateEnemyTowardPlayer(enemy, enemy.Definition.MoveSpeed * speedMultiplier, deltaSeconds);
            return;
        }

        Vector3 away = enemy.Position - targetPosition;
        away.Y = 0f;
        if (distance < preferredRange - 1.5f)
        {
            MoveEnemy(enemy, away, enemy.Definition.MoveSpeed * speedMultiplier, deltaSeconds);
            return;
        }

        Vector3 strafe = new Vector3(-away.Z, 0f, away.X) * enemy.StrafeDirection;
        float speed = MathF.Max(enemy.Definition.MoveSpeed, enemy.Definition.StrafeSpeed) * speedMultiplier;
        if (!MoveEnemy(enemy, strafe, speed, deltaSeconds))
        {
            enemy.StrafeDirection *= -1;
        }
    }

    private void NavigateEnemyTowardPlayer(EnemyState enemy, float speed, float deltaSeconds)
    {
        Vector3 targetPosition = GetEnemyTargetPosition(enemy);
        enemy.PathRefreshRemainingSeconds -= deltaSeconds;
        if (enemy.PathRefreshRemainingSeconds <= 0f || enemy.PathIndex >= enemy.Path.Count)
        {
            _navigation.FindPath(enemy.Position, targetPosition, enemy.Path);
            enemy.PathIndex = Math.Min(1, enemy.Path.Count);
            enemy.PathRefreshRemainingSeconds = enemy.Definition.PathRefreshSeconds;
        }

        Vector3 target = enemy.PathIndex < enemy.Path.Count ? enemy.Path[enemy.PathIndex] : targetPosition;
        Vector3 direction = target - enemy.Position;
        direction.Y = 0f;
        if (direction.LengthSquared() < 0.2f)
        {
            enemy.PathIndex++;
            return;
        }

        MoveEnemy(enemy, direction, speed, deltaSeconds);
    }

    private Vector3 GetEnemyTargetPosition(EnemyState enemy) =>
        enemy.TargetsRelay && RelayObjective is not null
            ? RelayObjective.Position
            : Player.Position;

    private void DamageEnemyTarget(EnemyState enemy, float damage, string cueId)
    {
        if ((enemy.PendingAttackTargetsRelay || enemy.TargetsRelay) && RelayObjective is not null)
        {
            ApplyRelayDamage(damage, enemy.Id);
            return;
        }

        DamagePlayer(damage, enemy.Id, enemy.Position, cueId);
    }

    private bool MoveEnemy(EnemyState enemy, Vector3 direction, float speed, float deltaSeconds)
    {
        direction.Y = 0f;
        if (direction.LengthSquared() <= 0.0001f)
        {
            return false;
        }

        direction = Vector3.Normalize(direction);
        if (enemy.ActionState is EnemyActionState.Idle or EnemyActionState.Locomotion or EnemyActionState.Navigating)
        {
            enemy.ActionState = EnemyActionState.Locomotion;
        }

        Vector3 delta = direction * speed * deltaSeconds;
        if (TrySetEnemyPosition(enemy, enemy.Position + delta))
        {
            return true;
        }

        if (MathF.Abs(delta.X) > 0.0001f && TrySetEnemyPosition(enemy, enemy.Position + new Vector3(delta.X, 0f, 0f)))
        {
            return true;
        }

        return MathF.Abs(delta.Z) > 0.0001f &&
            TrySetEnemyPosition(enemy, enemy.Position + new Vector3(0f, 0f, delta.Z));
    }

    private bool TrySetEnemyPosition(EnemyState enemy, Vector3 candidate)
    {
        float radius = enemy.Definition.ColliderRadius;
        candidate.X = Math.Clamp(candidate.X, Arena.BoundsMin.X + radius + 0.05f, Arena.BoundsMax.X - radius - 0.05f);
        candidate.Z = Math.Clamp(candidate.Z, Arena.BoundsMin.Z + radius + 0.05f, Arena.BoundsMax.Z - radius - 0.05f);
        Vector3 eye = candidate + new Vector3(0f, PlayerEyeHeight - candidate.Y, 0f);
        if (CollidesWithArena(eye, radius, enemy.Definition.ColliderHeight))
        {
            return false;
        }

        enemy.Position = candidate;
        return true;
    }

    private void FireEnemyProjectile(
        EnemyState enemy,
        float yawOffsetDegrees,
        float damage,
        float speedMultiplier,
        bool targetsRelay = false)
    {
        Vector3 origin = enemy.Position + new Vector3(0f, enemy.Definition.ColliderHeight * 0.55f, 0f);
        Vector3 targetPosition = targetsRelay && RelayObjective is not null
            ? RelayObjective.Position
            : Player.Position;
        Vector3 direction = Vector3.Normalize(targetPosition - origin);
        direction = RotateAroundY(direction, yawOffsetDegrees * (MathF.PI / 180f));
        float speed = enemy.Definition.ProjectileSpeed * speedMultiplier *
            DifficultyCatalog.Get(Difficulty).ProjectileSpeedMultiplier;
        float lifetimeSeconds = enemy.Definition.RangedAttackRange / MathF.Max(1f, speed);
        Projectiles.Add(new ProjectileState
        {
            Id = NextEntity(),
            OwnerId = enemy.Id,
            Position = origin + (direction * (enemy.Definition.ColliderRadius + 0.2f)),
            PreviousPosition = origin,
            Origin = origin,
            Velocity = direction * speed,
            Radius = enemy.Definition.ProjectileRadius,
            Damage = damage,
            IsHostile = true,
            TargetsRelay = targetsRelay,
            SplashRadius = enemy.Definition.ProjectileSplashRadius,
            Color = enemy.Definition.Tint,
            InitialLifetimeSeconds = lifetimeSeconds,
            RemainingSeconds = lifetimeSeconds,
        });
        AddEvent(CombatEventType.EnemyAttack, origin, targetPosition,
            enemy.Id, targetsRelay ? EntityId.None : Player.Id,
            enemy.Definition.IsBoss ? "boss-bolt" : "enemy-shot");
    }

    private void ApplyEnemySeparation()
    {
        for (int firstIndex = 0; firstIndex < Enemies.Count; firstIndex++)
        {
            EnemyState first = Enemies[firstIndex];
            if (first.IsDead)
            {
                continue;
            }

            for (int secondIndex = firstIndex + 1; secondIndex < Enemies.Count; secondIndex++)
            {
                EnemyState second = Enemies[secondIndex];
                if (second.IsDead)
                {
                    continue;
                }

                Vector3 delta = first.Position - second.Position;
                delta.Y = 0f;
                float minimum = first.Definition.ColliderRadius + second.Definition.ColliderRadius;
                float lengthSquared = delta.LengthSquared();
                if (lengthSquared >= minimum * minimum)
                {
                    continue;
                }

                if (lengthSquared <= 0.0001f)
                {
                    delta = (first.Id.Value < second.Id.Value ? Vector3.UnitX : -Vector3.UnitX) * 0.001f;
                    lengthSquared = delta.LengthSquared();
                }

                float length = MathF.Sqrt(lengthSquared);
                Vector3 correction = (delta / length) * ((minimum - length) * 0.25f);
                TrySetEnemyPosition(first, first.Position + correction);
                TrySetEnemyPosition(second, second.Position - correction);
            }
        }
    }

    private void ApplyPlayerEnemySeparation()
    {
        foreach (EnemyState enemy in Enemies)
        {
            if (enemy.IsDead)
            {
                continue;
            }

            Vector3 delta = enemy.Position - Player.Position;
            delta.Y = 0f;
            float minimumDistance = enemy.Definition.ColliderRadius + 0.45f;
            float distanceSquared = delta.LengthSquared();
            if (distanceSquared >= minimumDistance * minimumDistance)
            {
                continue;
            }

            if (distanceSquared <= 0.000001f)
            {
                delta = (enemy.Id.Value & 1) == 0 ? Vector3.UnitX : -Vector3.UnitX;
                distanceSquared = 1f;
            }

            float distance = MathF.Sqrt(distanceSquared);
            Vector3 correction = (delta / distance) * (minimumDistance - distance);
            TrySetEnemyPosition(enemy, enemy.Position + correction);
        }
    }

    private void UpdateProjectileMotion(ProjectileState projectile, float deltaSeconds)
    {
        float speed = projectile.Velocity.Length();
        if (speed <= 0.0001f)
        {
            return;
        }

        if (projectile.Motion == ProjectileMotionMode.Ballistic)
        {
            projectile.Velocity += new Vector3(0f, -9.81f * 0.35f * deltaSeconds, 0f);
            return;
        }

        if (projectile.Motion == ProjectileMotionMode.Returning &&
            projectile.InitialLifetimeSeconds > 0f &&
            projectile.RemainingSeconds <= projectile.InitialLifetimeSeconds * 0.5f)
        {
            projectile.HasReversed = true;
            Vector3 returnOffset = projectile.Origin - projectile.Position;
            if (returnOffset.LengthSquared() > 0.01f)
            {
                Vector3 desired = Vector3.Normalize(returnOffset);
                Vector3 current = Vector3.Normalize(projectile.Velocity);
                Vector3 steered = Vector3.Lerp(current, desired, Math.Clamp(deltaSeconds * 8f, 0f, 1f));
                projectile.Velocity = Vector3.Normalize(steered) * speed;
            }
            return;
        }

        if (projectile.Motion != ProjectileMotionMode.Homing || projectile.IsHostile)
        {
            return;
        }

        EnemyState? target = Enemies
            .Where(enemy => !enemy.IsDead)
            .OrderBy(enemy => Vector3.DistanceSquared(projectile.Position, enemy.Position))
            .ThenBy(enemy => enemy.Id.Value)
            .FirstOrDefault();
        if (target is null)
        {
            return;
        }

        Vector3 targetPosition = target.Position +
            new Vector3(0f, target.Definition.ColliderHeight * 0.35f, 0f);
        Vector3 targetOffset = targetPosition - projectile.Position;
        if (targetOffset.LengthSquared() <= 0.01f)
        {
            return;
        }

        Vector3 currentDirection = Vector3.Normalize(projectile.Velocity);
        Vector3 desiredDirection = Vector3.Normalize(targetOffset);
        Vector3 homingDirection = Vector3.Lerp(
            currentDirection,
            desiredDirection,
            Math.Clamp(deltaSeconds * 5f, 0f, 1f));
        projectile.Velocity = Vector3.Normalize(homingDirection) * speed;
    }

    private void UpdateProjectiles(float deltaSeconds)
    {
        for (int index = Projectiles.Count - 1; index >= 0; index--)
        {
            ProjectileState projectile = Projectiles[index];
            projectile.PreviousPosition = projectile.Position;
            UpdateProjectileMotion(projectile, deltaSeconds);
            Vector3 nextPosition = projectile.Position + (projectile.Velocity * deltaSeconds);
            projectile.RemainingSeconds -= deltaSeconds;
            bool remove = projectile.RemainingSeconds <= 0f;
            float nearestFraction = 1f;
            EnemyState? directEnemy = null;
            bool directWeakPoint = false;
            bool playerHit = false;
            bool relayHit = false;

            foreach (ArenaPrimitiveDefinition primitive in Arena.Primitives)
            {
                if (!primitive.HasCollision)
                {
                    continue;
                }

                if (SegmentIntersectsBox(projectile.Position, nextPosition, primitive, out float fraction) &&
                    fraction < nearestFraction)
                {
                    nearestFraction = fraction;
                    remove = true;
                }
            }

            if (projectile.IsHostile)
            {
                if (projectile.TargetsRelay && RelayObjective is not null && SegmentIntersectsSphere(
                        projectile.Position,
                        nextPosition,
                        RelayObjective.Position,
                        0.8f + projectile.Radius,
                        out float relayFraction) && relayFraction < nearestFraction)
                {
                    nearestFraction = relayFraction;
                    relayHit = true;
                    remove = true;
                }
                else if (!projectile.TargetsRelay)
                {
                    Vector3 playerCenter = Player.Position - new Vector3(0f, 0.45f, 0f);
                    if (SegmentIntersectsSphere(
                        projectile.Position,
                        nextPosition,
                        playerCenter,
                        0.55f + projectile.Radius,
                        out float fraction) && fraction < nearestFraction)
                    {
                        nearestFraction = fraction;
                        playerHit = true;
                        remove = true;
                    }
                }
            }
            else
            {
                foreach (EnemyState enemy in Enemies)
                {
                    if (enemy.IsDead || !TrySegmentEnemyHit(
                        projectile.Position,
                        nextPosition,
                        projectile.Radius,
                        enemy,
                        out float fraction,
                        out bool weakPoint) || fraction >= nearestFraction)
                    {
                        continue;
                    }

                    nearestFraction = fraction;
                    directEnemy = enemy;
                    directWeakPoint = weakPoint;
                    remove = true;
                }
            }

            if (remove)
            {
                if (nearestFraction < 1f)
                {
                    projectile.Position = Vector3.Lerp(projectile.Position, nextPosition, nearestFraction);
                    ImpactProjectile(projectile, directEnemy, directWeakPoint, playerHit, relayHit);
                }

                Projectiles.RemoveAt(index);
            }
            else
            {
                projectile.Position = nextPosition;
            }
        }
    }

    private void ImpactProjectile(
        ProjectileState projectile,
        EnemyState? directEnemy,
        bool directWeakPoint,
        bool playerHit,
        bool relayHit)
    {
        AddEvent(CombatEventType.WorldImpact, projectile.Position, projectile.PreviousPosition,
            projectile.OwnerId, directEnemy?.Id ?? EntityId.None, projectile.WeaponId);
        if (projectile.IsHostile)
        {
            if (relayHit)
            {
                ApplyRelayDamage(projectile.Damage, projectile.OwnerId);
                return;
            }

            float damage = projectile.Damage;
            if (!playerHit && projectile.SplashRadius > 0f)
            {
                float distance = Vector3.Distance(projectile.Position, Player.Position);
                if (distance > projectile.SplashRadius)
                {
                    return;
                }

                damage *= 1f - (0.7f * (distance / projectile.SplashRadius));
            }
            else if (!playerHit)
            {
                return;
            }

            DamagePlayer(damage, projectile.OwnerId, projectile.Position, "enemy-projectile");
            return;
        }

        List<EnemyState> damaged = [];
        if (projectile.SplashRadius > 0f)
        {
            foreach (EnemyState enemy in Enemies)
            {
                if (enemy.IsDead)
                {
                    continue;
                }

                float distance = Vector3.Distance(projectile.Position, enemy.Position);
                if (distance > projectile.SplashRadius + enemy.Definition.ColliderRadius)
                {
                    continue;
                }

                float multiplier = 1f - (0.65f * Math.Clamp(distance / projectile.SplashRadius, 0f, 1f));
                float runMultiplier = _runDirector?.Modifiers.DamageMultiplier(
                    projectile.WeaponId ?? string.Empty,
                    Vector3.Distance(projectile.Origin, enemy.Position)) ?? 1f;
                DamageEnemy(enemy, projectile.Damage * multiplier * runMultiplier,
                    projectile.Position, Player.Id, projectile.WeaponId);
                ApplyWeaponControl(enemy, projectile.BehaviorFlags, projectile.Position,
                    projectile.WeaponId ?? string.Empty, projectile.Damage);
                AddEvent(CombatEventType.EnemyHit, projectile.Position, projectile.PreviousPosition,
                    Player.Id, enemy.Id, projectile.WeaponId, projectile.Damage * multiplier * runMultiplier);
                damaged.Add(enemy);
            }
        }
        else if (directEnemy is not null)
        {
            float runMultiplier = _runDirector?.Modifiers.DamageMultiplier(
                projectile.WeaponId ?? string.Empty,
                Vector3.Distance(projectile.Origin, directEnemy.Position)) ?? 1f;
            float directDamage = projectile.Damage * runMultiplier *
                (directWeakPoint ? projectile.WeakPointMultiplier : 1f);
            DamageEnemy(directEnemy, directDamage,
                projectile.Position, Player.Id, projectile.WeaponId);
            ApplyWeaponControl(directEnemy, projectile.BehaviorFlags, projectile.Position,
                projectile.WeaponId ?? string.Empty, projectile.Damage);
            AddEvent(CombatEventType.EnemyHit, projectile.Position, projectile.PreviousPosition,
                Player.Id, directEnemy.Id, projectile.WeaponId, directDamage);
            damaged.Add(directEnemy);
        }

        if ((projectile.BehaviorFlags & WeaponBehavior.Cluster) != 0)
        {
            float clusterRadius = MathF.Max(6f, projectile.SplashRadius * 1.6f);
            foreach (EnemyState enemy in Enemies
                .Where(enemy => !enemy.IsDead && !damaged.Contains(enemy) &&
                    Vector3.DistanceSquared(enemy.Position, projectile.Position) <= clusterRadius * clusterRadius)
                .OrderBy(enemy => Vector3.DistanceSquared(enemy.Position, projectile.Position))
                .ThenBy(enemy => enemy.Id.Value)
                .Take(3)
                .ToArray())
            {
                float clusterDamage = projectile.Damage * 0.35f;
                DamageEnemy(enemy, clusterDamage, enemy.Position, Player.Id, projectile.WeaponId);
                ApplyWeaponControl(enemy, projectile.BehaviorFlags, projectile.Position,
                    projectile.WeaponId ?? string.Empty, projectile.Damage);
                AddEvent(CombatEventType.EnemyHit, enemy.Position, projectile.Position,
                    Player.Id, enemy.Id, projectile.WeaponId, clusterDamage);
                damaged.Add(enemy);
            }
        }

        if (projectile.ChainTargets <= 0 || projectile.ChainRadius <= 0f)
        {
            return;
        }

        Vector3 chainOrigin = projectile.Position;
        for (int chain = 0; chain < projectile.ChainTargets; chain++)
        {
            EnemyState? next = Enemies
                .Where(enemy => !enemy.IsDead && !damaged.Contains(enemy) &&
                    Vector3.DistanceSquared(chainOrigin, enemy.Position) <= projectile.ChainRadius * projectile.ChainRadius)
                .OrderBy(enemy => Vector3.DistanceSquared(chainOrigin, enemy.Position))
                .ThenBy(enemy => enemy.Id.Value)
                .FirstOrDefault();
            if (next is null)
            {
                break;
            }

            float runMultiplier = _runDirector?.Modifiers.DamageMultiplier(
                projectile.WeaponId ?? string.Empty,
                Vector3.Distance(projectile.Origin, next.Position)) ?? 1f;
            float damage = projectile.Damage * runMultiplier * MathF.Pow(0.68f, chain + 1);
            DamageEnemy(next, damage, next.Position, Player.Id, projectile.WeaponId);
            ApplyWeaponControl(next, projectile.BehaviorFlags, chainOrigin,
                projectile.WeaponId ?? string.Empty, projectile.Damage);
            AddEvent(CombatEventType.EnemyHit, next.Position, chainOrigin,
                Player.Id, next.Id, projectile.WeaponId, damage);
            damaged.Add(next);
            chainOrigin = next.Position;
        }
    }

    private void DamageEnemy(
        EnemyState enemy,
        float damage,
        Vector3 impactPosition,
        EntityId sourceId,
        string? weaponId)
    {
        if (enemy.IsDead)
        {
            return;
        }

        float actualDamage = MathF.Min(enemy.Health, MathF.Max(0f, damage));
        enemy.Health = MathF.Max(0f, enemy.Health - damage);
        enemy.HitFlashSeconds = 0.12f;
        if (!enemy.IsDead && enemy.ActionState == EnemyActionState.Windup &&
            enemy.PendingAttackKind != EnemyAttackKind.None && enemy.Definition.StaggerableDuringWindup &&
            !enemy.IsElite && enemy.Definition.Behavior is EnemyBehavior.Chaser or EnemyBehavior.Skirmisher or EnemyBehavior.Spitter)
        {
            enemy.PendingAttackKind = EnemyAttackKind.None;
            enemy.ActionState = EnemyActionState.HitReaction;
            enemy.AiTimerSeconds = 0.12f;
        }

        if (sourceId == Player.Id)
        {
            LastHitSeconds = 0f;
            CreditWeaponProficiency(weaponId, actualDamage, killCredit: false);
        }

        if (!enemy.IsDead)
        {
            return;
        }

        Kills++;
        _pendingProgression.Experience += (int)MathF.Round(
            (18f + (enemy.Definition.ThreatWeight * 12f)) *
            RpgProgressionMath.PersistentRewardMultiplier(ThreatTier) *
            CalculateRewardMultiplier(AffixEffectType.ExperienceGain));
        CreditWeaponProficiency(weaponId, 20f * enemy.Definition.ThreatWeight, killCredit: true);
        LastKillSeconds = 0f;
        float killDistance = 0f;
        if (sourceId == Player.Id)
        {
            killDistance = Vector3.Distance(Player.Position, enemy.Position);
            if (killDistance <= 8f)
            {
                CloseRangeKills++;
            }

            if (killDistance >= 18f)
            {
                LongRangeKills++;
            }
        }

        if ((_runDirector?.Modifiers.KillMovementSpeedMultiplier ?? 1f) > 1f)
        {
            Player.AdrenalSeconds = 3f;
        }

        enemy.ActionState = EnemyActionState.Death;
        Score += enemy.Definition.ScoreValue;
        AddEvent(CombatEventType.EnemyKilled, impactPosition, enemy.Position,
            sourceId, enemy.Id, weaponId, enemy.Definition.ScoreValue, killDistance);
        float roll = NextRandomSingle();
        if (roll < enemy.Definition.HealthDropChance)
        {
            AddDroppedPickup(PickupType.Health, enemy.Position, 20);
        }
        else if (roll < enemy.Definition.HealthDropChance + enemy.Definition.AmmoDropChance)
        {
            AddDroppedPickup(PickupType.Ammo, enemy.Position, 16);
        }

        DropEquipmentForEnemy(enemy);
        DropGuaranteedWeaponForEnemy(enemy);
    }

    private void DropGuaranteedWeaponForEnemy(EnemyState enemy)
    {
        if (_runDirector?.Phase != global::FpsFrenzy.Core.Simulation.RunPhase.EncounterActive ||
            _guaranteedWeaponFamilies.Count == 0)
        {
            return;
        }

        WeaponFamily family = _guaranteedWeaponFamilies.Dequeue();
        EquipmentInstance item = GenerateLoot(enemy.Id.Value, requiredWeaponFamily: family);
        float angle = (family.GetHashCode() & 7) * (MathF.Tau / 8f);
        Vector3 offset = new(MathF.Sin(angle) * 1.45f, 0f, MathF.Cos(angle) * 1.45f);
        AddEquipmentDrop(item, enemy.Position + offset);
    }

    private void CreditWeaponProficiency(string? weaponId, float damage, bool killCredit)
    {
        if (string.IsNullOrWhiteSpace(weaponId) || damage <= 0f ||
            !_catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? weapon) ||
            weapon.Family == WeaponFamily.None)
        {
            return;
        }

        int amount = Math.Max(1, (int)MathF.Round(damage * (killCredit ? 1f : 0.25f)));
        amount = Math.Max(1, (int)MathF.Round(amount *
            CalculateRewardMultiplier(AffixEffectType.ProficiencyGain)));
        _pendingProgression.ProficiencyExperience[weapon.Family] =
            _pendingProgression.ProficiencyExperience.GetValueOrDefault(weapon.Family) + amount;
    }

    private void DropEquipmentForEnemy(EnemyState enemy)
    {
        int count;
        ItemRarity? minimumRarity = null;
        if (enemy.Definition.IsBoss)
        {
            count = StandardRpgCatalog.StandardLootTable.BossDropCount;
        }
        else if (enemy.IsElite)
        {
            count = StandardRpgCatalog.StandardLootTable.EliteDropCount;
        }
        else
        {
            float baseChance = MathF.Min(StandardRpgCatalog.StandardLootTable.MaximumEnemyDropChance,
                StandardRpgCatalog.StandardLootTable.BaseEnemyDropChancePerThreat * enemy.Definition.ThreatWeight);
            float chance = MathF.Min(0.30f,
                baseChance * CalculateRewardMultiplier(AffixEffectType.LootChance, maximumBonus: 1f));
            count = NextRandomSingle() < chance ? 1 : 0;
        }

        for (int index = 0; index < count; index++)
        {
            if (enemy.Definition.IsBoss && index == 0)
            {
                minimumRarity = ItemRarity.Epic;
            }

            EquipmentInstance item = GenerateLoot(enemy.Id.Value, minimumRarity);
            float angle = ((enemy.Id.Value & 7) * (MathF.Tau / 8f)) +
                (index * (MathF.Tau / Math.Max(1, count)));
            float radius = count == 1 ? 0.95f : 1.3f;
            Vector3 offset = new(MathF.Sin(angle) * radius, 0f, MathF.Cos(angle) * radius);
            AddEquipmentDrop(item, enemy.Position + offset);
            minimumRarity = null;
        }
    }

    private EquipmentInstance GenerateLoot(
        int sourceEntity,
        ItemRarity? minimumRarity = null,
        WeaponFamily? requiredWeaponFamily = null) =>
        LootGenerator.Generate(RunSeed, Tick, sourceEntity, _lootDropSerial++, ThreatTier,
            _catalog, Progression.Proficiencies, minimumRarity,
            CalculateRewardMultiplier(AffixEffectType.RarityLuck, maximumBonus: 1f) - 1f,
            requiredWeaponFamily);

    private void AddEquipmentDrop(EquipmentInstance item, Vector3 position)
    {
        Vector3 openPosition = FindOpenDropPosition(position);
        Pickups.Add(new PickupState
        {
            Id = NextEntity(),
            Type = PickupType.Equipment,
            Position = new Vector3(openPosition.X, 0.2f, openPosition.Z),
            Equipment = item,
            IsDropped = true,
        });
        AddEvent(CombatEventType.EquipmentDropped, openPosition, Player.Position,
            EntityId.None, Player.Id, item.Id, item.ItemPower);
    }

    private void AddDroppedPickup(PickupType type, Vector3 position, int amount)
    {
        Vector3 openPosition = FindOpenDropPosition(position);
        Pickups.Add(new PickupState
        {
            Id = NextEntity(),
            Type = type,
            Position = new Vector3(openPosition.X, 0.5f, openPosition.Z),
            Amount = amount,
            RespawnSeconds = 0f,
            IsDropped = true,
        });
    }

    private Vector3 FindOpenDropPosition(Vector3 desired) => PickupDropLayout.FindOpenPosition(
        desired,
        Pickups.Where(pickup => pickup.IsAvailable).Select(pickup => pickup.Position),
        Arena.BoundsMin,
        Arena.BoundsMax);

    private void UpdatePickups(PlayerCommand command, float deltaSeconds)
    {
        bool interactPressed = command.Has(PlayerButtons.Interact) &&
            !_previousButtons.HasFlag(PlayerButtons.Interact);
        float pickupRadius = 1.2f * (_runDirector?.Modifiers.PickupRadiusMultiplier ?? 1f) *
            CalculateRewardMultiplier(AffixEffectType.PickupRadius, maximumBonus: 1f);
        Vector2 playerPosition = new(Player.Position.X, Player.Position.Z);

        foreach (EntityId dismissedId in _dismissedWeaponPickups.ToArray())
        {
            PickupState? dismissed = Pickups.FirstOrDefault(pickup => pickup.Id == dismissedId);
            if (dismissed is null || Vector2.DistanceSquared(playerPosition,
                    new Vector2(dismissed.Position.X, dismissed.Position.Z)) > pickupRadius * pickupRadius)
            {
                _dismissedWeaponPickups.Remove(dismissedId);
            }
        }

        PickupState? competingWeapon = Pickups
            .Where(pickup => pickup.IsAvailable &&
                (!_dismissedWeaponPickups.Contains(pickup.Id) || interactPressed) &&
                TryGetWeaponPickupItem(pickup, out EquipmentInstance? item) &&
                _weaponSets[WeaponSlotForItem(item!)].RightHand is not null &&
                Vector2.DistanceSquared(playerPosition,
                    new Vector2(pickup.Position.X, pickup.Position.Z)) <= pickupRadius * pickupRadius)
            .OrderBy(pickup => Vector2.DistanceSquared(playerPosition,
                new Vector2(pickup.Position.X, pickup.Position.Z)))
            .FirstOrDefault();
        if (competingWeapon is not null && TryGetWeaponPickupItem(
                competingWeapon, out EquipmentInstance? competingItem))
        {
            int slotIndex = WeaponSlotForItem(competingItem!);
            _pendingWeaponPickupDecision = new PendingWeaponPickupDecision(
                competingWeapon.Id,
                slotIndex,
                competingItem!,
                GetWeaponSlotItem(slotIndex));
            AddEvent(CombatEventType.WeaponPickupDecisionRequired,
                competingWeapon.Position, Player.Position, competingWeapon.Id, Player.Id,
                competingItem!.Id, competingItem.ItemPower);
            SetPaused(true);
            return;
        }

        for (int index = Pickups.Count - 1; index >= 0; index--)
        {
            PickupState pickup = Pickups[index];
            if (!pickup.IsAvailable)
            {
                pickup.RespawnRemainingSeconds -= deltaSeconds;
                if (pickup.RespawnRemainingSeconds <= 0f && !pickup.IsDropped)
                {
                    pickup.IsAvailable = true;
                }

                continue;
            }

            Vector2 pickupPosition = new(pickup.Position.X, pickup.Position.Z);
            if (Vector2.DistanceSquared(playerPosition, pickupPosition) > pickupRadius * pickupRadius)
            {
                continue;
            }

            if (TryGetWeaponPickupItem(pickup, out EquipmentInstance? weaponItem))
            {
                int weaponSlot = WeaponSlotForItem(weaponItem!);
                if (_weaponSets[weaponSlot].RightHand is null && RegisterCollectedWeapon(weaponItem!) &&
                    EquipWeaponItem(weaponItem!, activateIfCurrent: false, forceActivate: false))
                {
                    ConsumeWeaponPickup(pickup, weaponItem!);
                }
                continue;
            }

            if (pickup.Type == PickupType.Equipment && !interactPressed)
            {
                continue;
            }

            int pickupAmount = Math.Max(1, (int)MathF.Round(
                pickup.Amount * (_runDirector?.Modifiers.PickupAmountMultiplier ?? 1f)));
            bool consumed = pickup.Type switch
            {
                PickupType.Health => TryApplyHealth(pickupAmount),
                PickupType.Ammo => TryApplyAmmo(pickupAmount),
                PickupType.Weapon => TryApplyWeapon(pickup.WeaponId, pickupAmount),
                PickupType.Equipment => TryCollectEquipment(pickup.Equipment),
                _ => false,
            };

            if (!consumed)
            {
                continue;
            }

            AddEvent(CombatEventType.PickupCollected, pickup.Position, Player.Position,
                pickup.Id, Player.Id, pickup.Type.ToString(), pickupAmount);
            if (pickup.Type == PickupType.Equipment)
            {
                AddEvent(CombatEventType.EquipmentCollected, pickup.Position, Player.Position,
                    pickup.Id, Player.Id, pickup.Equipment?.Id, pickup.Equipment?.ItemPower ?? 0f);
            }

            if (pickup.IsDropped)
            {
                Pickups.RemoveAt(index);
                if (pickup.Type == PickupType.Weapon)
                {
                    _awaitingArmoryCollection = Pickups.Any(IsActiveArmoryPickup);
                }
            }
            else
            {
                pickup.IsAvailable = false;
                pickup.RespawnRemainingSeconds = pickup.RespawnSeconds;
            }
        }
    }

    private bool TryGetWeaponPickupItem(PickupState pickup, out EquipmentInstance? item)
    {
        if (pickup.Equipment is { IsWeapon: true } equipment)
        {
            item = equipment;
            return WeaponSlotForItem(equipment) >= 0;
        }
        if (pickup.Type == PickupType.Weapon && pickup.WeaponId is string weaponId &&
            _catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? weapon) &&
            weapon.Family != WeaponFamily.None)
        {
            item = new EquipmentInstance
            {
                Id = $"armory-{RunSeed}-{pickup.Id.Value}-{weapon.Id}",
                WeaponBaseId = weapon.Id,
                DisplayName = weapon.DisplayName,
                PrimarySlot = EquipmentSlot.RightHand,
                Rarity = ItemRarity.Common,
                ItemPower = RpgProgressionMath.MinimumItemPower(ThreatTier),
                IsLocked = true,
                IsRunBound = true,
            };
            return true;
        }
        item = null;
        return false;
    }

    private bool TryCollectEquipment(EquipmentInstance? item)
    {
        if (item is null || _pendingProgression.Equipment.Any(existing =>
            existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        _pendingProgression.Equipment.Add(item);
        _equipmentItems[item.Id] = item;
        return true;
    }

    private bool TryApplyHealth(int amount)
    {
        if (Player.Health >= Player.MaximumHealth)
        {
            return false;
        }

        Player.Health = MathF.Min(Player.MaximumHealth, Player.Health + amount);
        return true;
    }

    private bool TryApplyAmmo(int amount)
    {
        WeaponState weapon = Player.CurrentWeapon;
        int beforeReserve = weapon.Reserve;
        float beforeEnergy = weapon.Energy;
        float beforeHeat = weapon.Heat;
        Player.CurrentWeapon.AddAmmo(amount);
        return beforeReserve != weapon.Reserve || beforeEnergy != weapon.Energy || beforeHeat != weapon.Heat;
    }

    private bool TryApplyWeapon(string? weaponId, int amount)
    {
        if (string.IsNullOrWhiteSpace(weaponId) || !_catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? definition))
        {
            return false;
        }

        int existingIndex = Player.Weapons.FindIndex(weapon => weapon.Definition.Id.Equals(weaponId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            WeaponState existing = Player.Weapons[existingIndex];
            int beforeReserve = existing.Reserve;
            float beforeEnergy = existing.Energy;
            float beforeHeat = existing.Heat;
            existing.AddAmmo(amount);
            if (beforeReserve == existing.Reserve && beforeEnergy == existing.Energy && beforeHeat == existing.Heat)
            {
                return false;
            }

            Player.SelectedWeaponIndex = existingIndex;
            return true;
        }

        Player.Weapons.Add(new WeaponState(definition, _runDirector?.Modifiers));
        Player.SelectedWeaponIndex = Player.Weapons.Count - 1;
        return true;
    }

    private void DamagePlayer(float damage, EntityId sourceId, Vector3 sourcePosition, string cueId)
    {
        if (damage <= 0f || Player.Health <= 0f || GodModeEnabled || _emergencyBarrierSeconds > 0f)
        {
            return;
        }

        float appliedDamage = MathF.Min(
            MathF.Max(0f, 35f - _damageAppliedThisTick),
            damage * RpgProgressionMath.EnemyDamageMultiplier(ThreatTier) *
            DifficultyCatalog.Get(Difficulty).EnemyDamageMultiplier *
            CalculatePermanentDamageMultiplier() *
            (_runDirector?.Modifiers.IncomingDamageMultiplier ?? 1f));
        if (appliedDamage <= 0f)
        {
            return;
        }

        if (_emergencyBarrierAvailable && Player.Health - appliedDamage < 20f)
        {
            appliedDamage = MathF.Max(0f, Player.Health - 20f);
            _emergencyBarrierAvailable = false;
            _emergencyBarrierSeconds = 1f;
        }

        Player.Health = MathF.Max(0f, Player.Health - appliedDamage);
        _damageAppliedThisTick += appliedDamage;
        DamageTaken += appliedDamage;
        PlayerDamageFlashSeconds = 0.32f;
        AddEvent(CombatEventType.PlayerDamaged, Player.Position, sourcePosition,
            sourceId, Player.Id, cueId, appliedDamage);
    }

    private float CalculatePermanentDamageMultiplier()
    {
        float armor = 0f;
        float additionalMultiplier = PassiveEffectProduct(AffixEffectType.IncomingDamage);
        foreach (string itemId in EquipmentLoadout.EquippedItemIds.Values
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_equipmentItems.TryGetValue(itemId, out EquipmentInstance? item))
            {
                continue;
            }

            float powerScale = RpgProgressionMath.ItemPowerScale(item.ItemPower);
            if (item.EquipmentBaseId is string baseId &&
                _catalog.EquipmentBases.TryGetValue(baseId, out EquipmentBaseDefinition? definition))
            {
                armor += definition.BaseArmor * powerScale;
            }

            foreach (RolledAffix rolled in item.Affixes)
            {
                if (!_catalog.Affixes.TryGetValue(rolled.AffixId, out AffixDefinition? affix))
                {
                    continue;
                }

                if (affix.EffectType == AffixEffectType.Armor)
                {
                    armor += rolled.Value;
                }
                else if (affix.EffectType == AffixEffectType.IncomingDamage)
                {
                    additionalMultiplier *= Math.Clamp(rolled.Value, 0.5f, 1f);
                }
            }
        }

        float talentReduction = _catalog.Talents.Values
            .Where(talent => talent.EffectType == AffixEffectType.IncomingDamage)
            .Sum(talent => talent.ValuePerRank * Progression.TalentRanks.GetValueOrDefault(talent.Id));
        armor += TalentBonus(AffixEffectType.Armor) + PassiveEffectAdditive(AffixEffectType.Armor);
        float multiplier = RpgProgressionMath.ArmorDamageMultiplier(armor) *
            additionalMultiplier * (1f - Math.Clamp(talentReduction, 0f, 0.65f));
        return MathF.Max(0.35f, multiplier);
    }

    private Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
    {
        if (spreadDegrees <= 0f)
        {
            return direction;
        }

        float spread = spreadDegrees * (MathF.PI / 180f);
        float yawOffset = (NextRandomSingle() - 0.5f) * spread;
        float pitchOffset = (NextRandomSingle() - 0.5f) * spread;
        float yaw = MathF.Atan2(direction.X, -direction.Z) + yawOffset;
        float pitch = MathF.Asin(Math.Clamp(direction.Y, -1f, 1f)) + pitchOffset;
        float cosinePitch = MathF.Cos(pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * cosinePitch,
            MathF.Sin(pitch),
            -MathF.Cos(yaw) * cosinePitch));
    }

    private float CalculateDamageFalloff(WeaponDefinition weapon, float distance)
    {
        float falloffStart = MathF.Min(
            weapon.Range,
            weapon.DamageFalloffStart * (_runDirector?.Modifiers.FalloffStartMultiplier(weapon.Id) ?? 1f));
        if (falloffStart <= 0f || distance <= falloffStart || weapon.Range <= falloffStart)
        {
            return 1f;
        }

        float normalized = Math.Clamp(
            (distance - falloffStart) / (weapon.Range - falloffStart),
            0f,
            1f);
        return float.Lerp(1f, weapon.MinimumDamageMultiplier, normalized);
    }

    private static Vector3 RotateAroundY(Vector3 direction, float radians)
    {
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return Vector3.Normalize(new Vector3(
            (direction.X * cosine) - (direction.Z * sine),
            direction.Y,
            (direction.X * sine) + (direction.Z * cosine)));
    }

    private static uint CreateInitialRandomState(int seed)
    {
        uint state = unchecked((uint)seed) ^ 0xA3C5_9AC3u;
        return state == 0u ? 0x6D2B_79F5u : state;
    }

    private float NextRandomSingle()
    {
        uint state = _randomState;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        _randomState = state == 0u ? 0x6D2B_79F5u : state;
        return (_randomState >> 8) * (1f / 16_777_216f);
    }

    private void AddEvent(
        CombatEventType type,
        Vector3 position,
        Vector3 secondaryPosition,
        EntityId sourceId,
        EntityId targetId,
        string? cueId,
        float value = 0f,
        float rangeMeters = 0f) =>
        _combatEvents.Add(new CombatEvent(
            type,
            position,
            secondaryPosition,
            sourceId,
            targetId,
            cueId,
            value,
            rangeMeters));

    private void CleanupEnemies() => Enemies.RemoveAll(enemy => enemy.IsDead &&
        enemy.DeathSeconds >= enemy.Definition.Visual.CorpseLifetimeSeconds);

    private bool CollidesWithArena(Vector3 eyePosition, float radius, float bodyHeight)
    {
        if (eyePosition.X - radius <= Arena.BoundsMin.X || eyePosition.X + radius >= Arena.BoundsMax.X ||
            eyePosition.Z - radius <= Arena.BoundsMin.Z || eyePosition.Z + radius >= Arena.BoundsMax.Z)
        {
            return true;
        }

        float feet = eyePosition.Y - PlayerEyeHeight;
        float head = feet + bodyHeight;
        foreach (ArenaPrimitiveDefinition primitive in Arena.Primitives)
        {
            if (!primitive.HasCollision || primitive.Id.Equals("floor", StringComparison.OrdinalIgnoreCase) ||
                !primitive.IsNavigationObstacle)
            {
                continue;
            }

            Vector3 half = primitive.Size * 0.5f;
            float minimumY = primitive.Position.Y - half.Y;
            float maximumY = primitive.Position.Y + half.Y;
            if (head <= minimumY || feet >= maximumY)
            {
                continue;
            }

            if (eyePosition.X + radius > primitive.Position.X - half.X &&
                eyePosition.X - radius < primitive.Position.X + half.X &&
                eyePosition.Z + radius > primitive.Position.Z - half.Z &&
                eyePosition.Z - radius < primitive.Position.Z + half.Z)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RayIntersectsSphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float distance)
    {
        Vector3 offset = center - origin;
        float projection = Vector3.Dot(offset, direction);
        float perpendicularSquared = offset.LengthSquared() - (projection * projection);
        float radiusSquared = radius * radius;
        if (perpendicularSquared > radiusSquared)
        {
            distance = 0f;
            return false;
        }

        float halfChord = MathF.Sqrt(radiusSquared - perpendicularSquared);
        distance = projection - halfChord;
        return distance >= 0f;
    }

    private static bool SegmentIntersectsSphere(
        Vector3 start,
        Vector3 end,
        Vector3 center,
        float radius,
        out float fraction)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.000001f)
        {
            fraction = 0f;
            return Vector3.DistanceSquared(start, center) <= radius * radius;
        }

        float projected = Math.Clamp(Vector3.Dot(center - start, segment) / lengthSquared, 0f, 1f);
        Vector3 closest = start + (segment * projected);
        fraction = projected;
        return Vector3.DistanceSquared(closest, center) <= radius * radius;
    }

    private static bool TrySegmentEnemyHit(
        Vector3 start,
        Vector3 end,
        float projectileRadius,
        EnemyState enemy,
        out float fraction,
        out bool weakPoint)
    {
        float nearestWeakPoint = float.PositiveInfinity;
        foreach (EnemyWeakPointDefinition definition in enemy.Definition.WeakPoints)
        {
            if (SegmentIntersectsSphere(start, end, enemy.Position + definition.Offset,
                    projectileRadius + definition.Radius, out float weakPointFraction))
            {
                nearestWeakPoint = MathF.Min(nearestWeakPoint, weakPointFraction);
            }
        }

        if (float.IsFinite(nearestWeakPoint))
        {
            fraction = nearestWeakPoint;
            weakPoint = true;
            return true;
        }

        Vector3 enemyCenter = enemy.Position +
            new Vector3(0f, enemy.Definition.ColliderHeight * 0.35f, 0f);
        weakPoint = false;
        return SegmentIntersectsSphere(start, end, enemyCenter,
            projectileRadius + enemy.Definition.ColliderRadius, out fraction);
    }

    private static bool SegmentIntersectsBox(
        Vector3 start,
        Vector3 end,
        ArenaPrimitiveDefinition primitive,
        out float fraction)
    {
        Vector3 segment = end - start;
        float length = segment.Length();
        if (length <= 0.000001f || !RayIntersectsBox(start, segment / length, primitive, out float distance) ||
            distance > length)
        {
            fraction = 0f;
            return false;
        }

        fraction = Math.Clamp(distance / length, 0f, 1f);
        return true;
    }

    private static bool RayIntersectsBox(Vector3 origin, Vector3 direction, ArenaPrimitiveDefinition primitive, out float distance)
    {
        Vector3 half = primitive.Size * 0.5f;
        Vector3 minimum = primitive.Position - half;
        Vector3 maximum = primitive.Position + half;
        float near = 0f;
        float far = float.MaxValue;

        for (int axis = 0; axis < 3; axis++)
        {
            float axisOrigin = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float axisDirection = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            float axisMinimum = axis == 0 ? minimum.X : axis == 1 ? minimum.Y : minimum.Z;
            float axisMaximum = axis == 0 ? maximum.X : axis == 1 ? maximum.Y : maximum.Z;
            if (MathF.Abs(axisDirection) < 0.00001f)
            {
                if (axisOrigin < axisMinimum || axisOrigin > axisMaximum)
                {
                    distance = 0f;
                    return false;
                }

                continue;
            }

            float inverse = 1f / axisDirection;
            float first = (axisMinimum - axisOrigin) * inverse;
            float second = (axisMaximum - axisOrigin) * inverse;
            if (first > second)
            {
                (first, second) = (second, first);
            }

            near = MathF.Max(near, first);
            far = MathF.Min(far, second);
            if (near > far)
            {
                distance = 0f;
                return false;
            }
        }

        distance = near;
        return true;
    }

    private EntityId NextEntity() => new(_nextEntityValue++);
    private static float WrapAngle(float angle) => MathF.Atan2(MathF.Sin(angle), MathF.Cos(angle));

    private readonly record struct GeneratedEnemySpawn(string EnemyId, bool IsElite);
}
