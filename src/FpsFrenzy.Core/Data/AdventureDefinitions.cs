using System.Numerics;
using System.Text.Json.Serialization;

namespace FpsFrenzy.Core.Data;

[JsonConverter(typeof(JsonStringEnumConverter<GameMode>))]
public enum GameMode
{
    Arena,
    Adventure,
}

[JsonConverter(typeof(JsonStringEnumConverter<DungeonRoomRole>))]
public enum DungeonRoomRole
{
    Entry,
    Transit,
    Objective,
    Key,
    Cache,
    Lore,
    Exit,
    Boss,
}

[JsonConverter(typeof(JsonStringEnumConverter<AdventureObjectiveKind>))]
public enum AdventureObjectiveKind
{
    RestoreRelay,
    DiscoverSignal,
    RecoverCommandKey,
    DisableFabricator,
    DisableSignalAnchor,
    DisableShieldControl,
    DefeatBoss,
}

[JsonConverter(typeof(JsonStringEnumConverter<AdventureInteractableKind>))]
public enum AdventureInteractableKind
{
    PowerRelay,
    CommandKey,
    FabricatorConsole,
    SignalAnchor,
    ShieldControl,
    EnergyGate,
    EquipmentCache,
    LoreTerminal,
    Lift,
    StoryTerminal,
}

[JsonConverter(typeof(JsonStringEnumConverter<AdventureHazardKind>))]
public enum AdventureHazardKind
{
    ArcingConduit,
    FabricationLaser,
    SignalSurgePad,
}

[JsonConverter(typeof(JsonStringEnumConverter<AdventureStageKind>))]
public enum AdventureStageKind
{
    GeneratedFloor,
    Boss,
    Complete,
}

public sealed record AdventureDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string GeneratorVersion { get; init; }
    public List<DungeonFloorRecipe> Floors { get; init; } = [];
    public AdventureBossRecipe Boss { get; init; } = new();
    public List<AdventureStoryBeatDefinition> StoryBeats { get; init; } = [];
}

public sealed record DungeonFloorRecipe
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public int RoomCount { get; init; }
    public string PaletteId { get; init; } = "maintenance";
    public int ChestCount { get; init; } = 2;
    public AdventureHazardKind Hazard { get; init; }
    public List<AdventureObjectiveDefinition> Objectives { get; init; } = [];
    public List<AdventureEnemyGroupRecipe> EnemyGroups { get; init; } = [];
}

public sealed record AdventureObjectiveDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public AdventureObjectiveKind Kind { get; init; }
    public int RequiredCount { get; init; } = 1;
    public string? RequiresObjectiveId { get; init; }
}

public sealed record AdventureEnemyGroupRecipe
{
    public required string Id { get; init; }
    public bool Dormant { get; init; } = true;
    public int MinimumRoomIndex { get; init; } = 1;
    public List<SpawnGroupDefinition> Members { get; init; } = [];
}

public sealed record AdventureBossRecipe
{
    public string EnemyId { get; init; } = "core-warden";
    public string ArenaId { get; init; } = "null-signal-core-chamber";
    public List<AdventureObjectiveDefinition> Objectives { get; init; } = [];
}

public sealed record AdventureStoryBeatDefinition
{
    public required string Id { get; init; }
    public int StageIndex { get; init; }
    public string Speaker { get; init; } = "UNKNOWN";
    public required string Text { get; init; }
    public bool IsLore { get; init; }
}

public readonly record struct DungeonGridCell(int X, int Z);

public sealed record GeneratedDungeonRoom
{
    public required string Id { get; init; }
    public int Index { get; init; }
    public DungeonRoomRole Role { get; init; }
    public int GridX { get; init; }
    public int GridZ { get; init; }
    public int GridWidth { get; init; }
    public int GridDepth { get; init; }
    public bool IsMainPath { get; init; }
    public List<int> Connections { get; init; } = [];
    public Vector3 Center { get; init; }
}

public sealed record GeneratedDungeonInteractable
{
    public required string Id { get; init; }
    public AdventureInteractableKind Kind { get; init; }
    public int RoomIndex { get; init; }
    public Vector3 Position { get; init; }
    public string? ObjectiveId { get; init; }
    public string? RequiresObjectiveId { get; init; }
    public bool Optional { get; init; }
}

public sealed record GeneratedDungeonGate
{
    public required string Id { get; init; }
    public int FromRoomIndex { get; init; }
    public int ToRoomIndex { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Size { get; init; }
    public string? UnlockObjectiveId { get; init; }
    public bool InitiallyEnabled { get; init; } = true;
}

public sealed record GeneratedDungeonHazard
{
    public required string Id { get; init; }
    public AdventureHazardKind Kind { get; init; }
    public int RoomIndex { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Size { get; init; }
    public float ActiveSeconds { get; init; } = 1.2f;
    public float InactiveSeconds { get; init; } = 1.8f;
    public float PhaseOffsetSeconds { get; init; }
}

public sealed record GeneratedDungeonEnemyGroup
{
    public required string Id { get; init; }
    public int RoomIndex { get; init; }
    public bool Dormant { get; init; }
    public Vector3 SpawnCenter { get; init; }
    public List<SpawnGroupDefinition> Members { get; init; } = [];
}

public sealed record DungeonMinimapDefinition
{
    public List<DungeonGridCell> WalkableCells { get; init; } = [];
    public Dictionary<int, List<DungeonGridCell>> RoomCells { get; init; } = [];
    public Vector2 WorldOrigin { get; init; }
    public float CellSize { get; init; } = 2f;
}

public sealed record GeneratedDungeonFloor
{
    public required string AdventureId { get; init; }
    public required string GeneratorVersion { get; init; }
    public int Seed { get; init; }
    public int FloorIndex { get; init; }
    public int AttemptSalt { get; init; }
    public bool UsedFallback { get; init; }
    public required string LayoutHash { get; init; }
    public required ArenaDefinition Arena { get; init; }
    public List<GeneratedDungeonRoom> Rooms { get; init; } = [];
    public List<GeneratedDungeonInteractable> Interactables { get; init; } = [];
    public List<GeneratedDungeonGate> Gates { get; init; } = [];
    public List<GeneratedDungeonHazard> Hazards { get; init; } = [];
    public List<GeneratedDungeonEnemyGroup> EnemyGroups { get; init; } = [];
    public required DungeonMinimapDefinition Minimap { get; init; }
}
