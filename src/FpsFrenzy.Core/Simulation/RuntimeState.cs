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
    Navigating,
    Windup,
    Charging,
    Recovering,
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
    PlayerDamaged,
    PickupCollected,
    WaveStarted,
    BossPhaseChanged,
    SupportPulse,
}

public readonly record struct CombatEvent(
    CombatEventType Type,
    Vector3 Position,
    Vector3 SecondaryPosition,
    EntityId SourceId,
    EntityId TargetId,
    string? CueId,
    float Value = 0f);

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
    public float MaximumHealth { get; init; } = 100f;
    public List<WeaponState> Weapons { get; } = [];
    public int SelectedWeaponIndex { get; internal set; }

    public WeaponState CurrentWeapon => Weapons[SelectedWeaponIndex];
}

public sealed class WeaponState
{
    public WeaponState(WeaponDefinition definition)
    {
        Definition = definition;
        Magazine = definition.MagazineSize;
        Reserve = definition.ReserveCapacity;
        Energy = definition.EnergyCapacity;
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

    public void Tick(float deltaSeconds)
    {
        FireCooldownSeconds = MathF.Max(0f, FireCooldownSeconds - deltaSeconds);
        Heat = MathF.Max(0f, Heat - (Definition.HeatDissipationPerSecond * deltaSeconds));
        Energy = MathF.Min(Definition.EnergyCapacity, Energy + (Definition.EnergyRegenerationPerSecond * deltaSeconds));
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
            int needed = Definition.MagazineSize - Magazine;
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
            AmmoMode.RegeneratingEnergy => Energy >= Definition.EnergyPerShot,
            AmmoMode.Heat => !IsOverheated && Heat + Definition.HeatPerShot <= 1f,
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
                Magazine--;
                break;
            case AmmoMode.RegeneratingEnergy:
                Energy -= Definition.EnergyPerShot;
                break;
            case AmmoMode.Heat:
                Heat += Definition.HeatPerShot;
                break;
        }

        FireCooldownSeconds = Definition.FireIntervalSeconds;
        return true;
    }

    public void StartBurst()
    {
        if (Definition.TriggerMode == TriggerMode.Burst && BurstShotsRemaining == 0)
        {
            BurstShotsRemaining = Math.Max(1, Definition.BurstCount);
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
            Magazine < Definition.MagazineSize && Reserve > 0)
        {
            ReloadRemainingSeconds = Definition.ReloadSeconds;
        }
    }

    public void AddAmmo(int amount)
    {
        if (Definition.AmmoMode == AmmoMode.MagazineReserve)
        {
            Reserve = Math.Min(Definition.ReserveCapacity, Reserve + amount);
        }
        else
        {
            Energy = MathF.Min(Definition.EnergyCapacity, Energy + amount);
            Heat = MathF.Max(0f, Heat - (amount / 100f));
        }
    }
}

public sealed class EnemyState
{
    public required EntityId Id { get; init; }
    public required EnemyDefinition Definition { get; init; }
    public Vector3 Position { get; internal set; }
    public Vector3 PreviousPosition { get; internal set; }
    public float Health { get; internal set; }
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
    public bool IsDead => Health <= 0f;
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
    public Vector3 Velocity { get; init; }
    public float Radius { get; init; }
    public float Damage { get; init; }
    public string? WeaponId { get; init; }
    public bool IsHostile { get; init; }
    public float SplashRadius { get; init; }
    public float ChainRadius { get; init; }
    public int ChainTargets { get; init; }
    public Vector3 Color { get; init; } = new(0.35f, 0.92f, 1f);
    public float RemainingSeconds { get; internal set; }
}
