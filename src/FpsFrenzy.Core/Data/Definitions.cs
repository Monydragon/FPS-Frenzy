using System.Numerics;
using System.Text.Json.Serialization;

namespace FpsFrenzy.Core.Data;

[JsonConverter(typeof(JsonStringEnumConverter<AmmoMode>))]
public enum AmmoMode
{
    MagazineReserve,
    RegeneratingEnergy,
    Heat,
}

[JsonConverter(typeof(JsonStringEnumConverter<ShotMode>))]
public enum ShotMode
{
    Hitscan,
    Projectile,
}

[JsonConverter(typeof(JsonStringEnumConverter<TriggerMode>))]
public enum TriggerMode
{
    SemiAutomatic,
    Automatic,
    Burst,
}

[JsonConverter(typeof(JsonStringEnumConverter<EnemyBehavior>))]
public enum EnemyBehavior
{
    Chaser,
    Skirmisher,
    Charger,
    Spitter,
    Warden,
    Boss,
}

[JsonConverter(typeof(JsonStringEnumConverter<PickupType>))]
public enum PickupType
{
    Health,
    Ammo,
    Weapon,
}

[JsonConverter(typeof(JsonStringEnumConverter<DifficultyMode>))]
public enum DifficultyMode
{
    Standard,
}

public sealed record WeaponDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ModelAsset { get; init; }
    public AmmoMode AmmoMode { get; init; }
    public ShotMode ShotMode { get; init; }
    public TriggerMode TriggerMode { get; init; } = TriggerMode.Automatic;
    public float Damage { get; init; }
    public float FireIntervalSeconds { get; init; }
    public int BurstCount { get; init; } = 1;
    public float BurstRecoverySeconds { get; init; }
    public int PelletCount { get; init; } = 1;
    public float SpreadDegrees { get; init; }
    public float DamageFalloffStart { get; init; }
    public float MinimumDamageMultiplier { get; init; } = 1f;
    public int MagazineSize { get; init; }
    public int ReserveCapacity { get; init; }
    public float ReloadSeconds { get; init; }
    public float EnergyCapacity { get; init; }
    public float EnergyRegenerationPerSecond { get; init; }
    public float EnergyPerShot { get; init; }
    public float HeatPerShot { get; init; }
    public float HeatDissipationPerSecond { get; init; }
    public float ProjectileSpeed { get; init; }
    public float ProjectileRadius { get; init; } = 0.12f;
    public float SplashRadius { get; init; }
    public float ChainRadius { get; init; }
    public int ChainTargets { get; init; }
    public float Range { get; init; } = 80f;
    public Vector3 ProjectileColor { get; init; } = new(0.35f, 0.92f, 1f);
    public Vector3 ImpactColor { get; init; } = new(0.55f, 0.95f, 1f);
    public float RecoilKick { get; init; } = 1f;
    public float ScreenShake { get; init; } = 0.35f;
    public float HipFieldOfViewDegrees { get; init; } = 80f;
    public float AdsFieldOfViewDegrees { get; init; } = 55f;
    public Vector3 ViewModelHipOffset { get; init; } = new(0.32f, -0.28f, -0.55f);
    public Vector3 ViewModelAdsOffset { get; init; } = new(0f, -0.17f, -0.42f);
}

public sealed record EnemyDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ModelAsset { get; init; }
    public EnemyBehavior Behavior { get; init; } = EnemyBehavior.Chaser;
    public Dictionary<string, string> AnimationClips { get; init; } = [];
    public EnemyVisualDefinition Visual { get; init; } = new();
    public float MaxHealth { get; init; }
    public float MoveSpeed { get; init; }
    public float ColliderRadius { get; init; }
    public float ColliderHeight { get; init; }
    public float AttackRange { get; init; }
    public float AttackDamage { get; init; }
    public float AttackCooldownSeconds { get; init; }
    public float AttackWindupSeconds { get; init; } = 0.45f;
    public float AttackRecoverySeconds { get; init; } = 0.25f;
    public bool StaggerableDuringWindup { get; init; } = true;
    public float ThreatWeight { get; init; } = 1f;
    public float PreferredRange { get; init; } = 7f;
    public float RangedAttackRange { get; init; }
    public float ProjectileSpeed { get; init; }
    public float ProjectileRadius { get; init; } = 0.16f;
    public float ProjectileSplashRadius { get; init; }
    public float StrafeSpeed { get; init; }
    public float ChargeSpeed { get; init; }
    public float ChargeRange { get; init; }
    public float ChargeWindupSeconds { get; init; } = 0.65f;
    public float ChargeDurationSeconds { get; init; } = 0.7f;
    public float ChargeCooldownSeconds { get; init; } = 4f;
    public float SupportRadius { get; init; }
    public float SupportPulseSeconds { get; init; } = 4f;
    public float SupportHealAmount { get; init; }
    public float RenderScale { get; init; } = 1f;
    public Vector3 Tint { get; init; } = Vector3.One;
    public bool IsBoss { get; init; }
    public List<BossPhaseDefinition> BossPhases { get; init; } = [];
    public int ScoreValue { get; init; }
    public float PathRefreshSeconds { get; init; } = 0.4f;
    public float HealthDropChance { get; init; }
    public float AmmoDropChance { get; init; }
}

public sealed record BossPhaseDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public float HealthThreshold { get; init; } = 1f;
    public float MoveSpeedMultiplier { get; init; } = 1f;
    public float AttackCooldownMultiplier { get; init; } = 1f;
    public float DamageMultiplier { get; init; } = 1f;
    public float ProjectileSpeedMultiplier { get; init; } = 1f;
    public Vector3 Tint { get; init; } = Vector3.One;
    public string? SummonEnemyId { get; init; }
    public int SummonCount { get; init; }
}

public sealed record ArenaDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string WaveSetId { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    public Vector3 PlayerSpawn { get; init; }
    public Vector3 SkyColor { get; init; } = new(0.03f, 0.05f, 0.09f);
    public Vector3 FogColor { get; init; } = new(0.055f, 0.075f, 0.12f);
    public float FogStart { get; init; } = 24f;
    public float FogEnd { get; init; } = 72f;
    public float NavigationCellSize { get; init; } = 0.5f;
    public List<Vector3> EnemySpawns { get; init; } = [];
    public List<ArenaPrimitiveDefinition> Primitives { get; init; } = [];
    public List<ArenaPropDefinition> Props { get; init; } = [];
    public List<PickupSpawnDefinition> PickupSpawns { get; init; } = [];
    public List<ArenaSectorDefinition> Sectors { get; init; } = [];
    public Vector3 BossArenaAnchor { get; init; }
    public Vector3 BossArenaHalfExtents { get; init; } = new(15f, 0f, 12f);
}

public sealed record ArenaPrimitiveDefinition
{
    public required string Id { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Size { get; init; } = Vector3.One;
    public Vector3 RotationDegrees { get; init; }
    public Vector3 Color { get; init; } = new(0.35f, 0.4f, 0.5f);
    public string? TextureAsset { get; init; }
    public float TextureMetersPerTile { get; init; } = 4f;
    public bool IsNavigationObstacle { get; init; } = true;
    public bool HasCollision { get; init; } = true;
    public bool IsVisible { get; init; } = true;
    public bool IsEmissive { get; init; }
}

public sealed record ArenaPropDefinition
{
    public required string Id { get; init; }
    public required string ModelAsset { get; init; }
    public Vector3 Position { get; init; }
    public float TargetSpan { get; init; } = 1f;
    public float YawDegrees { get; init; }
    public float PitchDegrees { get; init; }
    public Vector3 DiffuseTint { get; init; } = Vector3.One;
    public Vector3 EmissiveTint { get; init; }
    public bool AnchorToGround { get; init; }
}

public sealed record PickupSpawnDefinition
{
    public PickupType Type { get; init; }
    public Vector3 Position { get; init; }
    public int Amount { get; init; }
    public string? WeaponId { get; init; }
    public float RespawnSeconds { get; init; }
}

public sealed record WaveSetDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public DifficultyMode Difficulty { get; init; } = DifficultyMode.Standard;
    public float InterWaveDelaySeconds { get; init; } = 4f;
    public List<WaveDefinition> Waves { get; init; } = [];
    public WaveDefinition? BossWave { get; init; }
}

public sealed record WaveDefinition
{
    public required string Id { get; init; }
    public int MaximumConcurrentEnemies { get; init; } = 8;
    public float SpawnIntervalSeconds { get; init; } = 0.35f;
    public List<SpawnGroupDefinition> SpawnGroups { get; init; } = [];
}

public sealed record SpawnGroupDefinition
{
    public required string EnemyId { get; init; }
    public int Count { get; init; }
}
