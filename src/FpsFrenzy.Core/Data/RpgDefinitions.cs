using System.Numerics;
using System.Text.Json.Serialization;

namespace FpsFrenzy.Core.Data;

[JsonConverter(typeof(JsonStringEnumConverter<EquipmentSlot>))]
public enum EquipmentSlot
{
    Head,
    Chest,
    Hands,
    RightHand,
    LeftHand,
    Legs,
    Feet,
    Accessory1,
    Accessory2,
    Ring1,
    Ring2,
}

[JsonConverter(typeof(JsonStringEnumConverter<Handedness>))]
public enum Handedness
{
    None,
    OneHanded,
    TwoHanded,
}

[JsonConverter(typeof(JsonStringEnumConverter<ItemRarity>))]
public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
}

[JsonConverter(typeof(JsonStringEnumConverter<WeaponFamily>))]
public enum WeaponFamily
{
    None,
    Pulse,
    SMG,
    Burst,
    Scatter,
    Precision,
    Beam,
    Plasma,
    Arc,
    Heavy,
    Experimental,
}

[JsonConverter(typeof(JsonStringEnumConverter<ProjectileMotionMode>))]
public enum ProjectileMotionMode
{
    Straight,
    Ballistic,
    Homing,
    Returning,
}

[JsonConverter(typeof(JsonStringEnumConverter<WeaponEffectTrigger>))]
public enum WeaponEffectTrigger
{
    OnFire,
    OnHit,
    OnKill,
    OnExpire,
}

[JsonConverter(typeof(JsonStringEnumConverter<WeaponEffectType>))]
public enum WeaponEffectType
{
    Charge,
    Pierce,
    Ricochet,
    Splash,
    Chain,
    Cluster,
    Split,
    Pull,
    Knockback,
    DamageOverTime,
    Stun,
    RampDamage,
    Homing,
    Returning,
    WeakPointBonus,
}

[JsonConverter(typeof(JsonStringEnumConverter<AbilityKind>))]
public enum AbilityKind
{
    Active,
    Passive,
}

[JsonConverter(typeof(JsonStringEnumConverter<AffixEffectType>))]
public enum AffixEffectType
{
    WeaponDamage,
    FireInterval,
    ReloadAndRecovery,
    Capacity,
    Stability,
    MaximumHealth,
    Armor,
    IncomingDamage,
    MovementSpeed,
    AbilityPower,
    CooldownRecovery,
    PickupRadius,
    ExperienceGain,
    AbilityPointGain,
    ProficiencyGain,
    LootChance,
    RarityLuck,
    SalvageYield,
}

[JsonConverter(typeof(JsonStringEnumConverter<TalentBranch>))]
public enum TalentBranch
{
    Arsenal,
    Bulwark,
    Salvage,
}

[JsonConverter(typeof(JsonStringEnumConverter<ThreatTier>))]
public enum ThreatTier
{
    TierI = 1,
    TierII = 2,
    TierIII = 3,
    TierIV = 4,
    TierV = 5,
    TierVI = 6,
    TierVII = 7,
    TierVIII = 8,
    TierIX = 9,
    TierX = 10,
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<WeaponBehavior>))]
public enum WeaponBehavior
{
    None = 0,
    Charge = 1 << 0,
    Pierce = 1 << 1,
    Ricochet = 1 << 2,
    Knockback = 1 << 3,
    RampDamage = 1 << 4,
    SplitShot = 1 << 5,
    Cluster = 1 << 6,
    Pull = 1 << 7,
    DamageOverTime = 1 << 8,
    ChainField = 1 << 9,
    Stun = 1 << 10,
    Homing = 1 << 11,
    Returning = 1 << 12,
    WeakPointBonus = 1 << 13,
}

public sealed record WeaponEffectDefinition
{
    public WeaponEffectType Type { get; init; }
    public WeaponEffectTrigger Trigger { get; init; } = WeaponEffectTrigger.OnHit;
    public float Magnitude { get; init; } = 1f;
    public float Radius { get; init; }
    public float DurationSeconds { get; init; }
    public int Count { get; init; }
    public int MaximumTargets { get; init; }
}

public sealed record WeaponVisualDefinition
{
    public Vector3 ForwardAxis { get; init; } = Vector3.UnitX;
    public Vector3 UpAxis { get; init; } = Vector3.UnitY;
    public Vector3 PivotOffset { get; init; } = Vector3.Zero;
    public Vector3 BarrelTip { get; init; } = new(0.65f, 0f, 0f);
    public Vector3 RearAnchor { get; init; } = new(-0.35f, 0f, 0f);
    public Vector3 SightAnchor { get; init; } = new(0.05f, 0.10f, 0f);
    public float SourceSpanScale { get; init; } = 1f;
    public float AdsTargetSpanScale { get; init; } = 0.62f;
    public Vector3? HipOffset { get; init; }
    public Vector3? AdsOffset { get; init; }
    public float? TargetSpan { get; init; }
    public float? YawDegrees { get; init; }
    public float? PitchDegrees { get; init; }
    public float? RollDegrees { get; init; }
    public Vector3 MuzzleOffset { get; init; } = new(0.65f, 0f, 0f);
    public Vector3 RightGripOffset { get; init; } = new(-0.05f, -0.10f, 0.11f);
    public Vector3 LeftGripOffset { get; init; } = new(0.05f, -0.10f, 0.11f);
    public Vector3 ForegripOffset { get; init; } = new(-0.13f, -0.06f, -0.16f);
    public Vector3 FamilyColor { get; init; } = Vector3.One;
    public float EquipSeconds { get; init; } = 0.22f;
    public float HolsterSeconds { get; init; } = 0.13f;
    public WeaponAnimationDefinition Animation { get; init; } = new();
}

[JsonConverter(typeof(JsonStringEnumConverter<WeaponReloadStyle>))]
public enum WeaponReloadStyle
{
    PistolTilt,
    RifleDip,
    LauncherRoll,
    EnergyVent,
    HeavyBrace,
}

public sealed record WeaponAnimationDefinition
{
    public WeaponReloadStyle ReloadStyle { get; init; } = WeaponReloadStyle.RifleDip;
    public float EquipSeconds { get; init; } = 0.28f;
    public float FireKickSeconds { get; init; } = 0.18f;
    public float RecoilDistance { get; init; } = 0.065f;
    public float RecoilPitchDegrees { get; init; } = 5.5f;
    public float ReloadPitchDegrees { get; init; } = 8f;
    public float ReloadRollDegrees { get; init; } = 34f;
    public float ReloadDropDistance { get; init; } = 0.13f;
    public float BobScale { get; init; } = 1f;
    public float SwayScale { get; init; } = 1f;
    public float OverheatShakeScale { get; init; } = 1f;
}

public sealed record WeaponArchetypeDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public required string Id { get; init; }
    public required string TemplateWeaponId { get; init; }
    public WeaponFamily Family { get; init; }
    public Handedness Handedness { get; init; } = Handedness.TwoHanded;
    public required string TaughtAbilityId { get; init; }
}

public sealed record WeaponBaseDefinition
{
    public int SchemaVersion { get; init; } = 2;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ArchetypeId { get; init; }
    public int BaseTier { get; init; } = 1;
    public required string ModelAsset { get; init; }
    public string IconAsset { get; init; } = "Textures/UI/menu-emblem";
    public Handedness? Handedness { get; init; }
    public float DamageMultiplier { get; init; } = 1f;
    public float FireIntervalMultiplier { get; init; } = 1f;
    public float? AdsFieldOfViewDegrees { get; init; }
    public float WeakPointMultiplier { get; init; } = 1f;
    public float ScopedSensitivityMultiplier { get; init; } = 0.68f;
    public ProjectileMotionMode ProjectileMotion { get; init; } = ProjectileMotionMode.Straight;
    public List<WeaponEffectDefinition> Effects { get; init; } = [];
    public WeaponVisualDefinition? Visual { get; init; }
}

public sealed record WeaponBaseSetDefinition
{
    public List<WeaponBaseDefinition> Bases { get; init; } = [];
}

public sealed record WeaponVisualCalibrationDefinition
{
    public required string WeaponId { get; init; }
    public WeaponVisualDefinition Visual { get; init; } = new();
}

public sealed record WeaponVisualCalibrationSetDefinition
{
    public List<WeaponVisualCalibrationDefinition> Calibrations { get; init; } = [];
}

public sealed record EquipmentBaseDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Archetype { get; init; }
    public required List<EquipmentSlot> CompatibleSlots { get; init; }
    public string? ModelAsset { get; init; }
    public string? IconAsset { get; init; }
    public string? TaughtAbilityId { get; init; }
    public float BaseArmor { get; init; }
    public float BaseMaximumHealth { get; init; }
    public List<AffixEffectDefinition> IntrinsicEffects { get; init; } = [];
}

public sealed record AffixEffectDefinition
{
    public AffixEffectType Type { get; init; }
    public float Value { get; init; }
    public WeaponFamily WeaponFamily { get; init; }
}

public sealed record AffixDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public bool IsPrefix { get; init; }
    public List<EquipmentSlot> AllowedSlots { get; init; } = [];
    public ItemRarity MinimumRarity { get; init; } = ItemRarity.Uncommon;
    public AffixEffectType EffectType { get; init; }
    public float MinimumValue { get; init; }
    public float MaximumValue { get; init; }
}

public sealed record EquipmentAbilityDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public AbilityKind Kind { get; init; }
    public int CapacityCost { get; init; }
    public float CooldownSeconds { get; init; }
    public int RequiredAbilityPoints { get; init; } = 20;
    public List<AffixEffectDefinition> Effects { get; init; } = [];
}

public sealed record TalentDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public TalentBranch Branch { get; init; }
    public int Tier { get; init; }
    public int MaximumRanks { get; init; } = 5;
    public int RequiredBranchPoints { get; init; }
    public AffixEffectType EffectType { get; init; }
    public float ValuePerRank { get; init; }
}

public sealed record LootTableDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public float BaseEnemyDropChancePerThreat { get; init; } = 0.04f;
    public float MaximumEnemyDropChance { get; init; } = 0.18f;
    public int EliteDropCount { get; init; } = 2;
    public int EncounterCacheDropCount { get; init; } = 2;
    public int BossDropCount { get; init; } = 6;
}

public static class RpgProgressionMath
{
    public const int MaximumPlayerLevel = 100;
    public const int MaximumWeaponProficiencyRank = 25;

    public static int ExperienceToNextLevel(int level)
    {
        if (level is < 1 or >= MaximumPlayerLevel)
        {
            return 0;
        }

        return (int)MathF.Round(250f + (75f * level) + (5f * level * level));
    }

    public static int ExperienceToNextProficiencyRank(int rank)
    {
        if (rank is < 1 or >= MaximumWeaponProficiencyRank)
        {
            return 0;
        }

        return 200 + (80 * rank) + (10 * rank * rank);
    }

    public static int PassiveAbilityCapacity(int playerLevel) =>
        10 + (Math.Clamp(playerLevel, 1, MaximumPlayerLevel) / 10);

    public static float ItemPowerScale(int itemPower) =>
        1f + (0.0125f * (Math.Clamp(itemPower, 1, 100) - 1));

    public static int MinimumItemPower(ThreatTier tier) => (((int)tier - 1) * 10) + 1;
    public static int MaximumItemPower(ThreatTier tier) => (int)tier * 10;
    public static float EnemyHealthMultiplier(ThreatTier tier) => 1f + (0.18f * ((int)tier - 1));
    public static float EnemyDamageMultiplier(ThreatTier tier) => 1f + (0.07f * ((int)tier - 1));
    public static float PersistentRewardMultiplier(ThreatTier tier) => 1f + (0.12f * ((int)tier - 1));
    public static float ArmorDamageMultiplier(float armor) =>
        1f - MathF.Min(0.5f, MathF.Max(0f, armor) / (100f + MathF.Max(0f, armor)));

    public static int RequiredProficiencyRankForBaseTier(int baseTier) => baseTier switch
    {
        <= 1 => 1,
        2 => 5,
        3 => 10,
        4 => 15,
        _ => 20,
    };

    public static int AffixCount(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => 0,
        ItemRarity.Uncommon => 1,
        ItemRarity.Rare => 2,
        ItemRarity.Epic or ItemRarity.Legendary => 3,
        _ => 0,
    };

    public static float[] RarityProbabilities(ThreatTier tier)
    {
        ReadOnlySpan<float> first = [0.65f, 0.25f, 0.09f, 0.01f, 0f];
        ReadOnlySpan<float> last = [0.05f, 0.20f, 0.35f, 0.30f, 0.10f];
        float amount = ((int)tier - 1) / 9f;
        float[] result = new float[5];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = float.Lerp(first[index], last[index], amount);
        }

        return result;
    }
}
