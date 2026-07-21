using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Core.Simulation;

public enum AdventureRunPhase
{
    Exploring,
    FloorReward,
    BossLocked,
    BossActive,
    Victory,
    Defeat,
}

public sealed record AdventureCheckpoint
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string AdventureId { get; init; }
    public int Seed { get; init; }
    public required string GeneratorVersion { get; init; }
    public string? EntryLayoutHash { get; init; }
    public int NextStageIndex { get; init; }
    public int StoryPosition { get; init; }
    public List<string> BoonIds { get; init; } = [];
    public DifficultyMode Difficulty { get; init; } = DifficultyMode.Normal;
    public ThreatTier ThreatTier { get; init; } = ThreatTier.TierI;
    public bool GodModeUsed { get; init; }
    public float ElapsedRunSeconds { get; init; }
    public int Score { get; init; }
    public int Kills { get; init; }
    public float DamageTaken { get; init; }
    public int FloorsCompleted { get; init; }
    public int SecretsFound { get; init; }
    public int LoreFound { get; init; }
    public EquipmentLoadout EquipmentLoadout { get; init; } = new();
    public List<EquipmentInstance> EquipmentItems { get; init; } = [];
    public WeaponSetLoadout WeaponSetA { get; init; } = new();
    public WeaponSetLoadout WeaponSetB { get; init; } = new();
    public int ActiveWeaponSetIndex { get; init; }
    public WeaponQuickbarLoadout WeaponQuickbar { get; init; } = new();
    public int ActiveWeaponSlotIndex { get; init; }
    public Dictionary<int, WeaponSetCheckpointState> WeaponSetStates { get; init; } = [];
    public List<WeaponCheckpointState> WeaponStates { get; init; } = [];
    public PendingRunProgression CommittedProgression { get; init; } = new();
    public List<string> CommittedRewardItemIds { get; init; } = [];
    public int RunExperienceEarned { get; init; }
    public int RunLevelsGained { get; init; }
    public Dictionary<WeaponFamily, int> RunProficiencyExperience { get; init; } = [];
    public List<string> RunAbilitiesMastered { get; init; } = [];
    public List<string> RunCollectedItemIds { get; init; } = [];
    public Dictionary<ItemRarity, int> RunRarityTotals { get; init; } = [];
    public int RunHighestItemPower { get; init; }
}

public sealed record AdventureObjectiveSnapshot(
    string Id,
    string DisplayName,
    int Current,
    int Required,
    bool Available,
    bool Complete);

public sealed record AdventureSnapshot
{
    public int Seed { get; init; }
    public required string AdventureId { get; init; }
    public required string GeneratorVersion { get; init; }
    public string? LayoutHash { get; init; }
    public int StageIndex { get; init; }
    public AdventureStageKind StageKind { get; init; }
    public AdventureRunPhase Phase { get; init; }
    public IReadOnlyList<AdventureObjectiveSnapshot> Objectives { get; init; } = [];
    public IReadOnlySet<int> DiscoveredRooms { get; init; } = new HashSet<int>();
    public IReadOnlySet<DungeonGridCell> DiscoveredMapCells { get; init; } = new HashSet<DungeonGridCell>();
    public IReadOnlySet<string> CompletedInteractables { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> AlertedGroups { get; init; } = new HashSet<string>();
    public IReadOnlyList<string> BoonIds { get; init; } = [];
    public int StoryPosition { get; init; }
    public int SecretsFound { get; init; }
    public int LoreFound { get; init; }
    public bool BossInvulnerable { get; init; }
}

/// <summary>
/// Adventure progression is intentionally independent from Arena's RunDirector.
/// A director instance represents the current stage; committed checkpoints only
/// contain stage-entry state, never live enemies, gates, or minimap discovery.
/// </summary>
public sealed class AdventureDirector
{
    public const int MaximumAlertedEnemies = 8;

    private readonly AdventureDefinition _definition;
    private readonly Dictionary<string, int> _objectiveProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _discoveredRooms = [];
    private readonly HashSet<DungeonGridCell> _discoveredMapCells = [];
    private readonly HashSet<string> _completedInteractables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _alertedGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _boonIds = [];
    private GeneratedDungeonFloor? _floor;
    private int _storyPosition;
    private int _secretsFound;
    private int _loreFound;
    private int _bossShieldControls;

    public AdventureDirector(AdventureDefinition definition, int seed, AdventureCheckpoint? checkpoint = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (seed is < DungeonGenerator.MinimumSeed or > DungeonGenerator.MaximumSeed)
        {
            throw new ArgumentOutOfRangeException(nameof(seed));
        }

        Seed = seed;
        StageIndex = checkpoint?.NextStageIndex ?? 0;
        _storyPosition = checkpoint?.StoryPosition ?? 0;
        _secretsFound = Math.Max(0, checkpoint?.SecretsFound ?? 0);
        _loreFound = Math.Max(0, checkpoint?.LoreFound ?? 0);
        _boonIds.AddRange(checkpoint?.BoonIds ?? []);
        if (StageIndex < _definition.Floors.Count)
        {
            Phase = AdventureRunPhase.Exploring;
        }
        else if (StageIndex == _definition.Floors.Count)
        {
            Phase = AdventureRunPhase.BossLocked;
        }
        else
        {
            Phase = AdventureRunPhase.Victory;
        }
        ResetObjectives();
    }

    public int Seed { get; }
    public int StageIndex { get; private set; }
    public AdventureRunPhase Phase { get; private set; }
    public bool BossInvulnerable => StageIndex == _definition.Floors.Count && _bossShieldControls < 2;
    public bool IsStageComplete => CurrentObjectives().All(objective =>
        _objectiveProgress.GetValueOrDefault(objective.Id) >= objective.RequiredCount);

    public GeneratedDungeonFloor BeginGeneratedFloor()
    {
        if (StageIndex < 0 || StageIndex >= _definition.Floors.Count)
        {
            throw new InvalidOperationException("The current Adventure stage is not a generated floor.");
        }

        _floor = DungeonGenerator.Generate(
            _definition, Seed, StageIndex, _definition.GeneratorVersion);
        _discoveredRooms.Clear();
        _discoveredMapCells.Clear();
        _completedInteractables.Clear();
        _alertedGroups.Clear();
        Phase = AdventureRunPhase.Exploring;
        ResetObjectives();
        DiscoverRoom(0);
        return _floor;
    }

    public void BeginBossStage()
    {
        if (StageIndex != _definition.Floors.Count)
        {
            throw new InvalidOperationException("The boss stage is not active.");
        }

        _floor = null;
        _discoveredRooms.Clear();
        _discoveredMapCells.Clear();
        _completedInteractables.Clear();
        _alertedGroups.Clear();
        _bossShieldControls = 0;
        Phase = AdventureRunPhase.BossLocked;
        ResetObjectives();
    }

    public bool DiscoverRoom(int roomIndex)
    {
        if (_floor is null || roomIndex < 0 || roomIndex >= _floor.Rooms.Count)
        {
            return false;
        }

        _discoveredMapCells.UnionWith(_floor.Minimap.RoomCells.GetValueOrDefault(roomIndex) ?? []);
        return _discoveredRooms.Add(roomIndex);
    }

    public bool DiscoverMapCell(DungeonGridCell cell) =>
        _floor is not null && _floor.Minimap.WalkableCells.Contains(cell) && _discoveredMapCells.Add(cell);

    public bool CanInteract(string interactableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interactableId);
        if (_floor is null || Phase != AdventureRunPhase.Exploring ||
            _completedInteractables.Contains(interactableId))
        {
            return false;
        }

        GeneratedDungeonInteractable? interactable = _floor.Interactables.FirstOrDefault(item =>
            item.Id.Equals(interactableId, StringComparison.OrdinalIgnoreCase));
        return interactable is not null &&
            DependencyComplete(interactable.RequiresObjectiveId) &&
            (interactable.Kind != AdventureInteractableKind.Lift || IsStageComplete);
    }

    public bool TryInteract(string interactableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interactableId);
        if (_floor is null || !CanInteract(interactableId))
        {
            return false;
        }

        GeneratedDungeonInteractable? interactable = _floor.Interactables.FirstOrDefault(item =>
            item.Id.Equals(interactableId, StringComparison.OrdinalIgnoreCase));
        if (interactable is null)
        {
            return false;
        }

        _completedInteractables.Add(interactable.Id);
        if (interactable.ObjectiveId is not null)
        {
            AdventureObjectiveDefinition? objective = CurrentObjectives().FirstOrDefault(candidate =>
                candidate.Id.Equals(interactable.ObjectiveId, StringComparison.OrdinalIgnoreCase));
            if (objective is not null)
            {
                _objectiveProgress[objective.Id] = Math.Min(objective.RequiredCount,
                    _objectiveProgress.GetValueOrDefault(objective.Id) + 1);
            }
        }

        switch (interactable.Kind)
        {
            case AdventureInteractableKind.EquipmentCache:
                _secretsFound++;
                break;
            case AdventureInteractableKind.LoreTerminal:
                _loreFound++;
                break;
            case AdventureInteractableKind.Lift:
                Phase = AdventureRunPhase.FloorReward;
                break;
        }

        return true;
    }

    public bool TryDisableBossShieldControl(string controlId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
        if (StageIndex != _definition.Floors.Count || Phase != AdventureRunPhase.BossLocked ||
            !_completedInteractables.Add(controlId))
        {
            return false;
        }

        _bossShieldControls = Math.Min(2, _bossShieldControls + 1);
        AdventureObjectiveDefinition shieldObjective = CurrentObjectives().First(objective =>
            objective.Kind == AdventureObjectiveKind.DisableShieldControl);
        _objectiveProgress[shieldObjective.Id] = _bossShieldControls;
        if (_bossShieldControls == 2)
        {
            Phase = AdventureRunPhase.BossActive;
        }
        return true;
    }

    public bool NotifyBossDefeated()
    {
        if (StageIndex != _definition.Floors.Count || Phase != AdventureRunPhase.BossActive)
        {
            return false;
        }

        AdventureObjectiveDefinition bossObjective = CurrentObjectives().First(objective =>
            objective.Kind == AdventureObjectiveKind.DefeatBoss);
        _objectiveProgress[bossObjective.Id] = bossObjective.RequiredCount;
        StageIndex++;
        Phase = AdventureRunPhase.Victory;
        return true;
    }

    public bool TryAlertGroup(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        if (_floor is null || _alertedGroups.Contains(groupId))
        {
            return false;
        }

        GeneratedDungeonEnemyGroup? group = _floor.EnemyGroups.FirstOrDefault(candidate =>
            candidate.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return false;
        }

        int activeCount = _floor.EnemyGroups.Where(candidate => _alertedGroups.Contains(candidate.Id))
            .Sum(candidate => candidate.Members.Sum(member => member.Count));
        int groupCount = group.Members.Sum(member => member.Count);
        if (activeCount + groupCount > MaximumAlertedEnemies)
        {
            return false;
        }

        return _alertedGroups.Add(group.Id);
    }

    public bool ClearAlertedGroup(string groupId) => _alertedGroups.Remove(groupId);

    public bool SelectFloorBoon(string boonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boonId);
        if (Phase != AdventureRunPhase.FloorReward || _boonIds.Contains(boonId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        _boonIds.Add(boonId);
        StageIndex++;
        _floor = null;
        Phase = StageIndex < _definition.Floors.Count
            ? AdventureRunPhase.Exploring
            : AdventureRunPhase.BossLocked;
        ResetObjectives();
        return true;
    }

    public AdventureStoryBeatDefinition? CurrentStoryBeat => _definition.StoryBeats
        .Where(beat => !beat.IsLore && beat.StageIndex <= StageIndex)
        .OrderBy(beat => beat.StageIndex)
        .ThenBy(beat => beat.Id, StringComparer.Ordinal)
        .Skip(_storyPosition)
        .FirstOrDefault();

    public bool AdvanceStory()
    {
        if (CurrentStoryBeat is null)
        {
            return false;
        }

        _storyPosition++;
        return true;
    }

    public void MarkDefeated() => Phase = AdventureRunPhase.Defeat;

    public AdventureCheckpoint CreateEntryCheckpoint(
        DifficultyMode difficulty,
        ThreatTier threatTier,
        bool godModeUsed,
        string? entryLayoutHash = null) => new()
    {
        AdventureId = _definition.Id,
        Seed = Seed,
        GeneratorVersion = _definition.GeneratorVersion,
        EntryLayoutHash = entryLayoutHash,
        NextStageIndex = StageIndex,
        StoryPosition = _storyPosition,
        BoonIds = [.. _boonIds],
        Difficulty = DifficultyCatalog.Normalize(difficulty),
        ThreatTier = threatTier,
        GodModeUsed = godModeUsed,
        FloorsCompleted = Math.Min(StageIndex, _definition.Floors.Count),
        SecretsFound = _secretsFound,
        LoreFound = _loreFound,
    };

    public AdventureSnapshot Snapshot() => new()
    {
        Seed = Seed,
        AdventureId = _definition.Id,
        GeneratorVersion = _definition.GeneratorVersion,
        LayoutHash = _floor?.LayoutHash,
        StageIndex = StageIndex,
        StageKind = StageIndex < _definition.Floors.Count
            ? AdventureStageKind.GeneratedFloor
            : StageIndex == _definition.Floors.Count ? AdventureStageKind.Boss : AdventureStageKind.Complete,
        Phase = Phase,
        Objectives = CurrentObjectives().Select(objective => new AdventureObjectiveSnapshot(
            objective.Id,
            objective.DisplayName,
            _objectiveProgress.GetValueOrDefault(objective.Id),
            objective.RequiredCount,
            DependencyComplete(objective.RequiresObjectiveId),
            _objectiveProgress.GetValueOrDefault(objective.Id) >= objective.RequiredCount)).ToArray(),
        DiscoveredRooms = new HashSet<int>(_discoveredRooms),
        DiscoveredMapCells = new HashSet<DungeonGridCell>(_discoveredMapCells),
        CompletedInteractables = new HashSet<string>(_completedInteractables, StringComparer.OrdinalIgnoreCase),
        AlertedGroups = new HashSet<string>(_alertedGroups, StringComparer.OrdinalIgnoreCase),
        BoonIds = [.. _boonIds],
        StoryPosition = _storyPosition,
        SecretsFound = _secretsFound,
        LoreFound = _loreFound,
        BossInvulnerable = BossInvulnerable,
    };

    private List<AdventureObjectiveDefinition> CurrentObjectives() =>
        StageIndex < _definition.Floors.Count
            ? _definition.Floors[StageIndex].Objectives
            : StageIndex == _definition.Floors.Count ? _definition.Boss.Objectives : [];

    private bool DependencyComplete(string? objectiveId) => objectiveId is null ||
        CurrentObjectives().FirstOrDefault(objective =>
            objective.Id.Equals(objectiveId, StringComparison.OrdinalIgnoreCase)) is { } required &&
        _objectiveProgress.GetValueOrDefault(required.Id) >= required.RequiredCount;

    private void ResetObjectives()
    {
        _objectiveProgress.Clear();
        foreach (AdventureObjectiveDefinition objective in CurrentObjectives())
        {
            _objectiveProgress[objective.Id] = 0;
        }
    }
}
