using System.Numerics;
using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Core.Simulation;

public enum GamePhase
{
    Playing,
    Paused,
    Victory,
    Defeat,
}

public enum EnemyActionState
{
    Idle,
    Locomotion,
    Navigating,
    Windup,
    ActiveAttack,
    Charging,
    Recovering,
    HitReaction,
    Death,
}

public enum EnemyAttackKind
{
    None,
    Melee,
    Projectile,
    BossVolley,
}

public enum CombatEventType
{
    WeaponFired,
    DryFire,
    ReloadStarted,
    ReloadCompleted,
    WorldImpact,
    EnemyHit,
    EnemyKilled,
    EnemyTelegraph,
    EnemyAttack,
    EnemyAttackStarted,
    EnemyAttackImpact,
    EnemySpawnTelegraph,
    EnemySpawned,
    PlayerDamaged,
    PickupCollected,
    WaveStarted,
    BossPhaseChanged,
    SupportPulse,
    EncounterStarted,
    EncounterCompleted,
    EncounterFailed,
    UpgradeOffered,
    UpgradeApplied,
    SectorActivated,
    RelayDamaged,
    ArmoryActivated,
}

public readonly record struct CombatEvent(
    CombatEventType Type,
    Vector3 Position,
    Vector3 SecondaryPosition,
    EntityId SourceId,
    EntityId TargetId,
    string? CueId,
    float Value = 0f,
    float RangeMeters = 0f);

public sealed class PlayerState
{
    public EntityId Id { get; init; }
    public Vector3 Position { get; internal set; }
    public Vector3 PreviousPosition { get; internal set; }
    public float Yaw { get; internal set; }
    public float Pitch { get; internal set; }
    public float VerticalVelocity { get; internal set; }
    public bool IsGrounded { get; internal set; } = true;
    public bool IsAiming { get; internal set; }
    public float Health { get; internal set; } = 100f;
    public float MaximumHealth { get; internal set; } = 100f;
    public float AdrenalSeconds { get; internal set; }
    public List<WeaponState> Weapons { get; } = [];
    public int SelectedWeaponIndex { get; internal set; }

    public WeaponState CurrentWeapon => Weapons[SelectedWeaponIndex];
}

public sealed class WeaponState
{
    private readonly RunModifiers? _modifiers;
    private float _magazineConsumptionAccumulator;

    public WeaponState(WeaponDefinition definition, RunModifiers? modifiers = null)
    {
        Definition = definition;
        _modifiers = modifiers;
        Magazine = MaximumMagazine;
        Reserve = MaximumReserve;
        Energy = MaximumEnergy;
    }

    public WeaponDefinition Definition { get; }
    public int Magazine { get; internal set; }
    public int Reserve { get; internal set; }
    public float Energy { get; internal set; }
    public float Heat { get; internal set; }
    public float FireCooldownSeconds { get; internal set; }
    public float ReloadRemainingSeconds { get; internal set; }
    public int BurstShotsRemaining { get; internal set; }
    public bool IsOverheated { get; internal set; }
    public bool IsReloading => ReloadRemainingSeconds > 0f;
    public int MaximumMagazine => ScaleCapacity(Definition.MagazineSize);
    public int MaximumReserve => ScaleCapacity(Definition.ReserveCapacity);
    public float MaximumEnergy => Definition.EnergyCapacity * (_modifiers?.CapacityMultiplier ?? 1f);

    public void Tick(float deltaSeconds)
    {
        FireCooldownSeconds = MathF.Max(0f, FireCooldownSeconds - deltaSeconds);
        float coolingMultiplier = _modifiers?.CoolingMultiplier(Definition.Id) ?? 1f;
        float recoveryMultiplier = _modifiers?.RecoveryMultiplier ?? 1f;
        Heat = MathF.Max(0f, Heat - (Definition.HeatDissipationPerSecond * coolingMultiplier * deltaSeconds));
        Energy = MathF.Min(MaximumEnergy,
            Energy + (Definition.EnergyRegenerationPerSecond * recoveryMultiplier * deltaSeconds));
        if (IsOverheated && Heat <= 0.35f)
        {
            IsOverheated = false;
        }

        if (ReloadRemainingSeconds <= 0f)
        {
            return;
        }

        ReloadRemainingSeconds -= deltaSeconds;
        if (ReloadRemainingSeconds <= 0f)
        {
            int needed = MaximumMagazine - Magazine;
            int loaded = Math.Min(needed, Reserve);
            Magazine += loaded;
            Reserve -= loaded;
            ReloadRemainingSeconds = 0f;
        }
    }

    public bool TryFire()
    {
        if (FireCooldownSeconds > 0f || IsReloading)
        {
            return false;
        }

        bool available = Definition.AmmoMode switch
        {
            AmmoMode.MagazineReserve => Magazine > 0,
            AmmoMode.RegeneratingEnergy => Energy >= EffectiveEnergyPerShot,
            AmmoMode.Heat => !IsOverheated && Heat + EffectiveHeatPerShot <= 1f,
            _ => false,
        };

        if (!available)
        {
            if (Definition.AmmoMode == AmmoMode.MagazineReserve)
            {
                BeginReload();
            }

            else if (Definition.AmmoMode == AmmoMode.Heat)
            {
                IsOverheated = true;
            }

            return false;
        }

        switch (Definition.AmmoMode)
        {
            case AmmoMode.MagazineReserve:
                _magazineConsumptionAccumulator += _modifiers?.AmmoCostMultiplier(Definition.Id) ?? 1f;
                int magazineCost = (int)_magazineConsumptionAccumulator;
                if (magazineCost > 0)
                {
                    Magazine = Math.Max(0, Magazine - magazineCost);
                    _magazineConsumptionAccumulator -= magazineCost;
                }

                break;
            case AmmoMode.RegeneratingEnergy:
                Energy -= EffectiveEnergyPerShot;
                break;
            case AmmoMode.Heat:
                Heat += EffectiveHeatPerShot;
                break;
        }

        FireCooldownSeconds = Definition.FireIntervalSeconds * (_modifiers?.FireIntervalMultiplier ?? 1f);
        return true;
    }

    public void StartBurst()
    {
        if (Definition.TriggerMode == TriggerMode.Burst && BurstShotsRemaining == 0)
        {
            BurstShotsRemaining = Math.Max(1,
                Definition.BurstCount + (_modifiers?.BurstCountBonus(Definition.Id) ?? 0));
        }
    }

    public void CompleteBurstShot()
    {
        if (Definition.TriggerMode != TriggerMode.Burst)
        {
            return;
        }

        BurstShotsRemaining = Math.Max(0, BurstShotsRemaining - 1);
        if (BurstShotsRemaining == 0)
        {
            FireCooldownSeconds = MathF.Max(FireCooldownSeconds, Definition.BurstRecoverySeconds);
        }
    }

    public void BeginReload()
    {
        if (Definition.AmmoMode == AmmoMode.MagazineReserve && !IsReloading &&
            Magazine < MaximumMagazine && Reserve > 0)
        {
            ReloadRemainingSeconds = Definition.ReloadSeconds * (_modifiers?.ReloadTimeMultiplier ?? 1f);
        }
    }

    public void AddAmmo(int amount)
    {
        if (Definition.AmmoMode == AmmoMode.MagazineReserve)
        {
            Reserve = Math.Min(MaximumReserve, Reserve + amount);
        }
        else
        {
            Energy = MathF.Min(MaximumEnergy, Energy + amount);
            Heat = MathF.Max(0f, Heat - (amount / 100f));
        }
    }

    internal WeaponCheckpointState CreateCheckpointState() => new()
    {
        WeaponId = Definition.Id,
        Magazine = Magazine,
        Reserve = Reserve,
        Energy = Energy,
        Heat = Heat,
        FireCooldownSeconds = FireCooldownSeconds,
        ReloadRemainingSeconds = ReloadRemainingSeconds,
        BurstShotsRemaining = BurstShotsRemaining,
        IsOverheated = IsOverheated,
        MagazineConsumptionAccumulator = _magazineConsumptionAccumulator,
    };

    internal void RestoreCheckpointState(WeaponCheckpointState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Definition.Id.Equals(state.WeaponId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Weapon checkpoint '{state.WeaponId}' cannot restore '{Definition.Id}'.",
                nameof(state));
        }

        Magazine = Math.Clamp(state.Magazine, 0, MaximumMagazine);
        Reserve = Math.Clamp(state.Reserve, 0, MaximumReserve);
        Energy = Math.Clamp(FiniteOrZero(state.Energy), 0f, MaximumEnergy);
        Heat = Math.Clamp(FiniteOrZero(state.Heat), 0f, 1f);
        FireCooldownSeconds = Math.Clamp(FiniteOrZero(state.FireCooldownSeconds), 0f, 60f);
        ReloadRemainingSeconds = Math.Clamp(FiniteOrZero(state.ReloadRemainingSeconds), 0f, 60f);
        int maximumBurst = Definition.TriggerMode == TriggerMode.Burst
            ? Math.Max(1, Definition.BurstCount + (_modifiers?.BurstCountBonus(Definition.Id) ?? 0))
            : 0;
        BurstShotsRemaining = Math.Clamp(state.BurstShotsRemaining, 0, maximumBurst);
        IsOverheated = Definition.AmmoMode == AmmoMode.Heat && state.IsOverheated;
        _magazineConsumptionAccumulator = Math.Clamp(
            FiniteOrZero(state.MagazineConsumptionAccumulator),
            0f,
            0.999999f);
    }

    private float EffectiveEnergyPerShot => Definition.EnergyPerShot *
        (_modifiers?.AmmoCostMultiplier(Definition.Id) ?? 1f);

    private float EffectiveHeatPerShot => Definition.HeatPerShot *
        (_modifiers?.HeatGenerationMultiplier(Definition.Id) ?? 1f);

    private int ScaleCapacity(int capacity) => capacity <= 0
        ? 0
        : Math.Max(1, (int)MathF.Round(capacity * (_modifiers?.CapacityMultiplier ?? 1f)));

    private static float FiniteOrZero(float value) => float.IsFinite(value) ? value : 0f;
}

public sealed class EnemyState
{
    public required EntityId Id { get; init; }
    public required EnemyDefinition Definition { get; init; }
    public Vector3 Position { get; internal set; }
    public Vector3 PreviousPosition { get; internal set; }
    public float Health { get; internal set; }
    public float MaximumHealth { get; internal set; }
    public float AttackCooldownSeconds { get; internal set; }
    public float PathRefreshRemainingSeconds { get; internal set; }
    public List<Vector3> Path { get; internal set; } = [];
    public int PathIndex { get; internal set; }
    public float HitFlashSeconds { get; internal set; }
    public float DeathSeconds { get; internal set; }
    public float AttackAnimationSeconds { get; internal set; }
    public float SupportPulseRemainingSeconds { get; internal set; }
    public float AiTimerSeconds { get; internal set; }
    public float FacingYaw { get; internal set; }
    public int StrafeDirection { get; internal set; } = 1;
    public int CurrentBossPhaseIndex { get; internal set; }
    public EnemyActionState ActionState { get; internal set; }
    public Vector3 ChargeDirection { get; internal set; }
    public EnemyAttackKind PendingAttackKind { get; internal set; }
    public float PendingAttackDamageMultiplier { get; internal set; } = 1f;
    public float PendingAttackSpreadDegrees { get; internal set; }
    public float PendingAttackSpeedMultiplier { get; internal set; } = 1f;
    public bool PendingAttackTargetsRelay { get; internal set; }
    public int PendingAttackProjectileCount { get; internal set; } = 1;
    public float PendingAttackProjectileSpreadDegrees { get; internal set; }
    public bool IsElite { get; internal set; }
    public bool TargetsRelay { get; internal set; }
    public float StalledSeconds { get; internal set; }
    public Vector3 LastProgressPosition { get; internal set; }
    public bool IsDead => Health <= 0f;
}

public sealed class PendingEnemySpawn
{
    public required string EnemyId { get; init; }
    public string PortalId { get; internal set; } = string.Empty;
    public Vector3 Position { get; internal set; }
    public float RemainingSeconds { get; internal set; }
    public bool IsElite { get; init; }
    public float HealthFraction { get; init; } = 1f;
}

public sealed class RelayObjectiveState
{
    public Vector3 Position { get; init; }
    public float MaximumHealth { get; init; } = 450f;
    public float Health { get; internal set; } = 450f;
    public float RemainingSeconds { get; internal set; }
}

public sealed class PickupState
{
    public required EntityId Id { get; init; }
    public required PickupType Type { get; init; }
    public required Vector3 Position { get; init; }
    public int Amount { get; init; }
    public string? WeaponId { get; init; }
    public float RespawnSeconds { get; init; }
    public float RespawnRemainingSeconds { get; internal set; }
    public bool IsAvailable { get; internal set; } = true;
    public bool IsDropped { get; init; }
}

public sealed class ProjectileState
{
    public required EntityId Id { get; init; }
    public required EntityId OwnerId { get; init; }
    public Vector3 Position { get; internal set; }
    public Vector3 PreviousPosition { get; internal set; }
    public Vector3 Origin { get; init; }
    public Vector3 Velocity { get; init; }
    public float Radius { get; init; }
    public float Damage { get; init; }
    public string? WeaponId { get; init; }
    public bool IsHostile { get; init; }
    public bool TargetsRelay { get; init; }
    public float SplashRadius { get; init; }
    public float ChainRadius { get; init; }
    public int ChainTargets { get; init; }
    public Vector3 Color { get; init; } = new(0.35f, 0.92f, 1f);
    public float RemainingSeconds { get; internal set; }
}
