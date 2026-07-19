using System.Numerics;
using System.Text.Json.Serialization;

namespace FpsFrenzy.Core.Data;

[JsonConverter(typeof(JsonStringEnumConverter<EncounterObjectiveType>))]
public enum EncounterObjectiveType
{
    Purge,
    RelayDefense,
    EliteHunt,
    Boss,
}

[JsonConverter(typeof(JsonStringEnumConverter<UpgradeEffectType>))]
public enum UpgradeEffectType
{
    WeaponDamage,
    WeaponAmmoCost,
    WeaponBurstRounds,
    WeaponSpread,
    WeaponFalloffStart,
    WeaponHeatGeneration,
    WeaponCooling,
    WeaponSplashRadius,
    WeaponProjectileSpeed,
    WeaponChainTargets,
    WeaponChainRadius,
    GlobalDamage,
    FireInterval,
    WeaponCapacity,
    ReloadAndRecovery,
    CloseRangeDamage,
    LongRangeDamage,
    MaximumHealth,
    EncounterHealing,
    EmergencyBarrier,
    PickupRadius,
    PickupAmount,
    IncomingDamage,
    KillMovementSpeed,
}

public sealed record SpawnPortalDefinition
{
    public required string Id { get; init; }
    public Vector3 Position { get; init; }
    public float FacingYawDegrees { get; init; }
    public float TelegraphSeconds { get; init; } = 0.75f;
}

public sealed record ArenaSectorDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    public Vector3 EntryPoint { get; init; }
    public Vector3 ObjectiveAnchor { get; init; }
    public List<SpawnPortalDefinition> SpawnPortals { get; init; } = [];
    public List<string> EnergyGateIds { get; init; } = [];
}

public sealed record EncounterDefinition
{
    public required string Id { get; init; }
    public required string SectorId { get; init; }
    public EncounterObjectiveType ObjectiveType { get; init; }
    public int SectorNumber { get; init; }
    public float ThreatBudget { get; init; }
    public int MaximumConcurrentEnemies { get; init; }
    public float RelayDurationSeconds { get; init; }
    public float RelayMaximumHealth { get; init; } = 450f;
    public float EliteHealthMultiplier { get; init; } = 1.4f;
    public int PressureWaveCount { get; init; } = 1;
    public float PressureWaveDelaySeconds { get; init; } = 2f;
}

public sealed record UpgradeEffectDefinition
{
    public UpgradeEffectType Type { get; init; }
    public float Value { get; init; }
    public string? WeaponId { get; init; }
}

public sealed record UpgradeDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public bool InitiallyUnlocked { get; init; }
    public string? UnlockConditionId { get; init; }
    public List<UpgradeEffectDefinition> Effects { get; init; } = [];
}

public static class StandardUpgradeCatalog
{
    private static readonly IReadOnlyList<UpgradeDefinition> Definitions =
    [
        Signature("pulse-capacitor", "Pulse Capacitor", "+25% sidearm damage and -15% energy cost.",
            "pulse-sidearm",
            Effect(UpgradeEffectType.WeaponDamage, 1.25f, "pulse-sidearm"),
            Effect(UpgradeEffectType.WeaponAmmoCost, 0.85f, "pulse-sidearm")),
        Signature("burst-synchronizer", "Burst Synchronizer", "+1 burst round and -20% spread.",
            "burst-carbine",
            Effect(UpgradeEffectType.WeaponBurstRounds, 1f, "burst-carbine"),
            Effect(UpgradeEffectType.WeaponSpread, 0.8f, "burst-carbine")),
        Signature("tight-choke", "Tight Choke", "-30% scatter spread and +25% falloff start.",
            "scatter-blaster",
            Effect(UpgradeEffectType.WeaponSpread, 0.7f, "scatter-blaster"),
            Effect(UpgradeEffectType.WeaponFalloffStart, 1.25f, "scatter-blaster")),
        Signature("beam-heat-sink", "Beam Heat Sink", "-25% heat and +25% cooling.",
            "beam-rifle",
            Effect(UpgradeEffectType.WeaponHeatGeneration, 0.75f, "beam-rifle"),
            Effect(UpgradeEffectType.WeaponCooling, 1.25f, "beam-rifle")),
        Signature("plasma-payload", "Plasma Payload", "+30% splash radius and +15% projectile speed.",
            "plasma-launcher",
            Effect(UpgradeEffectType.WeaponSplashRadius, 1.3f, "plasma-launcher"),
            Effect(UpgradeEffectType.WeaponProjectileSpeed, 1.15f, "plasma-launcher")),
        Signature("arc-relay", "Arc Relay", "+1 chain target and +20% chain radius.",
            "arc-cannon",
            Effect(UpgradeEffectType.WeaponChainTargets, 1f, "arc-cannon"),
            Effect(UpgradeEffectType.WeaponChainRadius, 1.2f, "arc-cannon")),
        General("calibrated-cells", "Calibrated Cells", "+12% damage.", true,
            Effect(UpgradeEffectType.GlobalDamage, 1.12f)),
        General("accelerated-cycler", "Accelerated Cycler", "-12% fire interval.", false,
            Effect(UpgradeEffectType.FireInterval, 0.88f), "complete-sector-1"),
        General("expanded-stores", "Expanded Stores", "+25% weapon capacity.", true,
            Effect(UpgradeEffectType.WeaponCapacity, 1.25f)),
        General("field-loader", "Field Loader", "-20% reload time and +20% recovery.", true,
            Effect(UpgradeEffectType.ReloadAndRecovery, 1.2f)),
        General("close-quarters", "Close Quarters", "+20% damage within 8m.", false,
            Effect(UpgradeEffectType.CloseRangeDamage, 1.2f), "close-range-kills-50"),
        General("longshot", "Longshot", "+20% damage beyond 18m.", false,
            Effect(UpgradeEffectType.LongRangeDamage, 1.2f), "long-range-kills-25"),
        General("reinforced-shell", "Reinforced Shell", "+20 maximum health and heal immediately.", true,
            Effect(UpgradeEffectType.MaximumHealth, 20f)),
        General("salvage-repair", "Salvage Repair", "Heal 12 health after encounters.", true,
            Effect(UpgradeEffectType.EncounterHealing, 12f)),
        General("emergency-barrier", "Emergency Barrier", "Once per encounter, survive lethal pressure at 20 health.", false,
            Effect(UpgradeEffectType.EmergencyBarrier, 1f), "first-defeat"),
        General("magnetic-salvage", "Magnetic Salvage", "Double pickup radius and gain +20% pickup amount.", true,
            Effect(UpgradeEffectType.PickupRadius, 2f),
            Effect(UpgradeEffectType.PickupAmount, 1.2f)),
        General("phase-stabilizer", "Phase Stabilizer", "-12% incoming damage.", false,
            Effect(UpgradeEffectType.IncomingDamage, 0.88f), "relay-above-half"),
        General("adrenal-circuit", "Adrenal Circuit", "+15% movement speed for 3 seconds after a kill.", false,
            Effect(UpgradeEffectType.KillMovementSpeed, 1.15f), "elite-under-60-seconds"),
    ];

    public static IReadOnlyList<UpgradeDefinition> All => Definitions;

    public static IReadOnlySet<string> InitiallyUnlockedIds { get; } = Definitions
        .Where(definition => definition.InitiallyUnlocked)
        .Select(definition => definition.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static UpgradeDefinition Signature(
        string id,
        string name,
        string description,
        string weaponId,
        params UpgradeEffectDefinition[] effects) => new()
        {
            Id = id,
            DisplayName = name,
            Description = description,
            InitiallyUnlocked = true,
            UnlockConditionId = $"collect-{weaponId}",
            Effects = [.. effects],
        };

    private static UpgradeDefinition General(
        string id,
        string name,
        string description,
        bool initiallyUnlocked,
        UpgradeEffectDefinition effect,
        string? unlockConditionId = null) => new()
        {
            Id = id,
            DisplayName = name,
            Description = description,
            InitiallyUnlocked = initiallyUnlocked,
            UnlockConditionId = unlockConditionId,
            Effects = [effect],
        };

    private static UpgradeDefinition General(
        string id,
        string name,
        string description,
        bool initiallyUnlocked,
        UpgradeEffectDefinition first,
        UpgradeEffectDefinition second) => new()
        {
            Id = id,
            DisplayName = name,
            Description = description,
            InitiallyUnlocked = initiallyUnlocked,
            Effects = [first, second],
        };

    private static UpgradeEffectDefinition Effect(UpgradeEffectType type, float value, string? weaponId = null) => new()
    {
        Type = type,
        Value = value,
        WeaponId = weaponId,
    };
}
