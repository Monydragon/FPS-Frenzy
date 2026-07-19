using System.Numerics;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Input;

namespace FpsFrenzy.Core.Simulation;

public sealed class GameSimulation : IDisposable
{
    public const float FixedDeltaSeconds = 1f / 60f;
    public const float PlayerMoveSpeed = 7.5f;
    public const float PlayerEyeHeight = 1.65f;

    private readonly ContentCatalog _catalog;
    private readonly WaveSetDefinition _waveSet;
    private readonly List<WaveDefinition> _runWaves;
    private readonly NavigationGrid _navigation;
    private readonly Random _random;
    private readonly BepuPlayerController _playerController;
    private readonly List<CombatEvent> _combatEvents = [];
    private readonly List<string> _queuedSummons = [];
    private int _nextEntityValue = 2;
    private int _pendingEnemies;
    private float _spawnRemainingSeconds;
    private float _interWaveRemainingSeconds = 1.5f;
    private int _spawnCursor;
    private int _spawnGroupCursor;
    private int _pendingInSpawnGroup;
    private bool _waveActive;
    private PlayerButtons _previousButtons;

    public GameSimulation(
        ContentCatalog catalog,
        string arenaId = "training-ring",
        int randomSeed = 1337,
        int startingWaveIndex = 0,
        string startingWeaponId = "pulse-sidearm",
        bool godModeEnabled = false)
    {
        _catalog = catalog;
        Arena = catalog.Arenas[arenaId];
        _waveSet = catalog.WaveSets[Arena.WaveSetId];
        _runWaves = _waveSet.BossWave is null ? _waveSet.Waves : [.. _waveSet.Waves, _waveSet.BossWave];
        if (startingWaveIndex < 0 || startingWaveIndex >= _runWaves.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startingWaveIndex));
        }

        if (!catalog.Weapons.TryGetValue(startingWeaponId, out WeaponDefinition? startingWeapon))
        {
            throw new ArgumentException($"Unknown starting weapon '{startingWeaponId}'.", nameof(startingWeaponId));
        }

        CurrentWaveIndex = startingWaveIndex;
        _navigation = new NavigationGrid(Arena);
        _random = new Random(randomSeed);
        _playerController = new BepuPlayerController(Arena);
        SetPlayerInvulnerable(godModeEnabled);

        Player = new PlayerState
        {
            Id = new EntityId(1),
            Position = Arena.PlayerSpawn,
            PreviousPosition = Arena.PlayerSpawn,
        };
        Player.Weapons.Add(new WeaponState(startingWeapon));

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
    }

    public ArenaDefinition Arena { get; }
    public PlayerState Player { get; }
    public List<EnemyState> Enemies { get; } = [];
    public List<PickupState> Pickups { get; } = [];
    public List<ProjectileState> Projectiles { get; } = [];
    public GamePhase Phase { get; private set; } = GamePhase.Playing;
    public DifficultyMode Difficulty => _waveSet.Difficulty;
    public bool GodModeEnabled { get; private set; }
    public uint Tick { get; private set; }
    public int CurrentWaveIndex { get; private set; }
    public int TotalWaves => _runWaves.Count;
    public int Score { get; private set; }
    public int Kills { get; private set; }
    public float ElapsedRunSeconds { get; private set; }
    public float InterWaveRemainingSeconds => _interWaveRemainingSeconds;
    public int RemainingEnemies => Enemies.Count(enemy => !enemy.IsDead) + _pendingEnemies;
    public float LastShotSeconds { get; private set; } = 99f;
    public float LastHitSeconds { get; private set; } = 99f;
    public float LastKillSeconds { get; private set; } = 99f;
    public float PlayerDamageFlashSeconds { get; private set; }
    public IReadOnlyList<CombatEvent> CombatEvents => _combatEvents;
    public bool IsBossWave => CurrentWaveIndex == _runWaves.Count - 1 && _waveSet.BossWave is not null;
    public EnemyState? ActiveBoss => Enemies.FirstOrDefault(enemy => enemy.Definition.IsBoss && !enemy.IsDead);

    public void Step(ReadOnlySpan<PlayerCommand> commands, float fixedDeltaSeconds = FixedDeltaSeconds)
    {
        if (fixedDeltaSeconds <= 0f || fixedDeltaSeconds > 0.1f)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
        }

        _combatEvents.Clear();
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

        Tick++;
        ElapsedRunSeconds += fixedDeltaSeconds;
        LastShotSeconds += fixedDeltaSeconds;
        LastHitSeconds += fixedDeltaSeconds;
        LastKillSeconds += fixedDeltaSeconds;
        PlayerDamageFlashSeconds = MathF.Max(0f, PlayerDamageFlashSeconds - fixedDeltaSeconds);
        Player.PreviousPosition = Player.Position;
        UpdatePlayer(command, fixedDeltaSeconds);
        UpdateWeapons(command, fixedDeltaSeconds);
        UpdateWaveDirector(fixedDeltaSeconds);
        UpdateEnemies(fixedDeltaSeconds);
        UpdateProjectiles(fixedDeltaSeconds);
        UpdatePickups(fixedDeltaSeconds);
        CleanupEnemies();

        if (Player.Health <= 0f)
        {
            Player.Health = 0f;
            Phase = GamePhase.Defeat;
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
        _previousButtons &= ~PlayerButtons.Pause;
    }

    public void SetPlayerInvulnerable(bool invulnerable) => GodModeEnabled = invulnerable;

    public Vector3 GetViewDirection()
    {
        float cosinePitch = MathF.Cos(Player.Pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Sin(Player.Yaw) * cosinePitch,
            MathF.Sin(Player.Pitch),
            -MathF.Cos(Player.Yaw) * cosinePitch));
    }

    public Vector3 GetWeaponMuzzlePosition()
    {
        Vector3 forward = GetViewDirection();
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
        WeaponDefinition weapon = Player.CurrentWeapon.Definition;
        Vector3 offset = Player.IsAiming ? weapon.ViewModelAdsOffset : weapon.ViewModelHipOffset;
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

        if (command.WeaponSlot >= 0 && command.WeaponSlot < Player.Weapons.Count)
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
        Vector3 desiredVelocity = ((right * moveInput.X) + (forward * moveInput.Y)) * PlayerMoveSpeed;

        bool jumpPressed = command.Has(PlayerButtons.Jump) && !_previousButtons.HasFlag(PlayerButtons.Jump);
        PlayerPhysicsResult result = _playerController.Step(
            desiredVelocity, jumpPressed, Player.IsGrounded, deltaSeconds);
        Player.Position = result.Position;
        Player.VerticalVelocity = result.VerticalVelocity;
        Player.IsGrounded = result.IsGrounded;
    }

    public void Dispose() => _playerController.Dispose();

    private void UpdateWeapons(PlayerCommand command, float deltaSeconds)
    {
        foreach (WeaponState weapon in Player.Weapons)
        {
            bool wasReloading = weapon.IsReloading;
            weapon.Tick(deltaSeconds);
            if (wasReloading && !weapon.IsReloading)
            {
                AddEvent(CombatEventType.ReloadCompleted, Player.Position, Player.Position,
                    Player.Id, EntityId.None, weapon.Definition.Id);
            }
        }

        WeaponState current = Player.CurrentWeapon;
        bool wasReloadingBeforeInput = current.IsReloading;
        if (command.Has(PlayerButtons.Reload))
        {
            current.BeginReload();
        }

        if (!wasReloadingBeforeInput && current.IsReloading)
        {
            AddEvent(CombatEventType.ReloadStarted, Player.Position, Player.Position,
                Player.Id, EntityId.None, current.Definition.Id);
        }

        bool fireHeld = command.Has(PlayerButtons.Fire);
        bool firePressed = fireHeld && !_previousButtons.HasFlag(PlayerButtons.Fire);
        if (firePressed && current.Definition.TriggerMode == TriggerMode.Burst)
        {
            current.StartBurst();
        }

        bool wantsShot = current.Definition.TriggerMode switch
        {
            TriggerMode.SemiAutomatic => firePressed,
            TriggerMode.Automatic => fireHeld,
            TriggerMode.Burst => current.BurstShotsRemaining > 0,
            _ => false,
        };
        if (!wantsShot)
        {
            return;
        }

        if (!current.TryFire())
        {
            if (firePressed)
            {
                AddEvent(CombatEventType.DryFire, Player.Position, Player.Position,
                    Player.Id, EntityId.None, current.Definition.Id);
            }

            return;
        }

        current.CompleteBurstShot();

        LastShotSeconds = 0f;
        Vector3 direction = GetViewDirection();
        Vector3 muzzle = GetWeaponMuzzlePosition();
        AddEvent(CombatEventType.WeaponFired, muzzle, Player.Position + direction,
            Player.Id, EntityId.None, current.Definition.Id, current.Definition.ScreenShake);
        if (current.Definition.ShotMode == ShotMode.Hitscan)
        {
            for (int pellet = 0; pellet < current.Definition.PelletCount; pellet++)
            {
                Vector3 pelletDirection = ApplySpread(direction, current.Definition.SpreadDegrees);
                FireHitscan(Player.Position, muzzle, pelletDirection, current.Definition);
            }
        }
        else
        {
            Vector3 aimPoint = Player.Position + (direction * current.Definition.Range);
            Vector3 projectileDirection = Vector3.Normalize(aimPoint - muzzle);
            Projectiles.Add(new ProjectileState
            {
                Id = NextEntity(),
                OwnerId = Player.Id,
                Position = muzzle,
                PreviousPosition = muzzle,
                Velocity = projectileDirection * current.Definition.ProjectileSpeed,
                Radius = current.Definition.ProjectileRadius,
                Damage = current.Definition.Damage,
                WeaponId = current.Definition.Id,
                SplashRadius = current.Definition.SplashRadius,
                ChainRadius = current.Definition.ChainRadius,
                ChainTargets = current.Definition.ChainTargets,
                Color = current.Definition.ProjectileColor,
                RemainingSeconds = current.Definition.Range / MathF.Max(1f, current.Definition.ProjectileSpeed),
            });
        }
    }

    private void FireHitscan(Vector3 origin, Vector3 visualOrigin, Vector3 direction, WeaponDefinition weapon)
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
        float nearestEnemyDistance = nearestArenaDistance;
        foreach (EnemyState enemy in Enemies)
        {
            if (enemy.IsDead)
            {
                continue;
            }

            Vector3 center = enemy.Position + new Vector3(0f, enemy.Definition.ColliderHeight * 0.35f, 0f);
            if (RayIntersectsSphere(origin, direction, center, enemy.Definition.ColliderRadius, out float distance) && distance < nearestEnemyDistance)
            {
                nearestEnemyDistance = distance;
                hit = enemy;
            }
        }

        if (hit is not null)
        {
            float multiplier = CalculateDamageFalloff(weapon, nearestEnemyDistance);
            Vector3 impact = origin + (direction * nearestEnemyDistance);
            DamageEnemy(hit, weapon.Damage * multiplier, impact, Player.Id, weapon.Id);
            AddEvent(CombatEventType.EnemyHit, impact, visualOrigin, Player.Id, hit.Id, weapon.Id,
                weapon.Damage * multiplier);
        }
        else
        {
            Vector3 impact = origin + (direction * nearestArenaDistance);
            AddEvent(CombatEventType.WorldImpact, impact, visualOrigin, Player.Id, EntityId.None, weapon.Id);
        }
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
        EnemyDefinition definition = _catalog.Enemies[enemyId];
        Vector3 spawn = Arena.EnemySpawns[_spawnCursor++ % Arena.EnemySpawns.Count];
        Enemies.Add(new EnemyState
        {
            Id = NextEntity(),
            Definition = definition,
            Position = spawn,
            PreviousPosition = spawn,
            Health = definition.MaxHealth,
            PathRefreshRemainingSeconds = 0f,
            SupportPulseRemainingSeconds = definition.SupportPulseSeconds,
            StrafeDirection = (_nextEntityValue & 1) == 0 ? 1 : -1,
        });
    }

    private void UpdateEnemies(float deltaSeconds)
    {
        _queuedSummons.Clear();
        int enemyCount = Enemies.Count;
        for (int enemyIndex = 0; enemyIndex < enemyCount; enemyIndex++)
        {
            EnemyState enemy = Enemies[enemyIndex];
            enemy.PreviousPosition = enemy.Position;
            enemy.AttackCooldownSeconds = MathF.Max(0f, enemy.AttackCooldownSeconds - deltaSeconds);
            enemy.HitFlashSeconds = MathF.Max(0f, enemy.HitFlashSeconds - deltaSeconds);
            enemy.AttackAnimationSeconds = MathF.Max(0f, enemy.AttackAnimationSeconds - deltaSeconds);
            enemy.AiTimerSeconds = MathF.Max(0f, enemy.AiTimerSeconds - deltaSeconds);
            if (enemy.IsDead)
            {
                enemy.DeathSeconds += deltaSeconds;
                continue;
            }

            Vector3 toPlayer = Player.Position - enemy.Position;
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
        }

        foreach (string summon in _queuedSummons)
        {
            SpawnEnemy(summon);
        }

        ApplyEnemySeparation();
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
            FireEnemyProjectile(enemy, spreadDegrees, enemy.Definition.AttackDamage, 1f);
            enemy.AttackCooldownSeconds = enemy.Definition.AttackCooldownSeconds;
            enemy.AttackAnimationSeconds = 0.28f;
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
                    AddEvent(CombatEventType.EnemyAttack, enemy.Position, Player.Position,
                        enemy.Id, Player.Id, "charge");
                }

                return;
            case EnemyActionState.Charging:
                if (distance <= enemy.Definition.AttackRange + 0.65f)
                {
                    DamagePlayer(enemy.Definition.AttackDamage * damageMultiplier, enemy.Id, enemy.Position, "charge-impact");
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
            enemy.ActionState = EnemyActionState.Windup;
            enemy.AiTimerSeconds = enemy.Definition.ChargeWindupSeconds;
            enemy.AttackCooldownSeconds = enemy.Definition.ChargeCooldownSeconds;
            enemy.AttackAnimationSeconds = enemy.Definition.ChargeWindupSeconds;
            AddEvent(CombatEventType.EnemyTelegraph, enemy.Position, Player.Position,
                enemy.Id, Player.Id, "charge-windup", enemy.Definition.ChargeWindupSeconds);
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

                ally.Health = MathF.Min(ally.Definition.MaxHealth, ally.Health + enemy.Definition.SupportHealAmount);
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
                FireBossVolley(enemy, 5, 9f, phase);
            }

            return;
        }

        MaintainEnemyRange(enemy, distance, deltaSeconds, phase.MoveSpeedMultiplier);
        if (distance <= enemy.Definition.RangedAttackRange && enemy.AttackCooldownSeconds <= 0f)
        {
            int bolts = enemy.CurrentBossPhaseIndex == 0 ? 1 : 3;
            float spread = enemy.CurrentBossPhaseIndex == 0 ? 0f : 7f;
            FireBossVolley(enemy, bolts, spread, phase);
        }
    }

    private void FireBossVolley(EnemyState enemy, int projectileCount, float spreadDegrees, BossPhaseDefinition phase)
    {
        float center = (projectileCount - 1) * 0.5f;
        for (int index = 0; index < projectileCount; index++)
        {
            FireEnemyProjectile(
                enemy,
                (index - center) * spreadDegrees,
                enemy.Definition.AttackDamage * phase.DamageMultiplier,
                phase.ProjectileSpeedMultiplier);
        }

        enemy.AttackCooldownSeconds = enemy.Definition.AttackCooldownSeconds * phase.AttackCooldownMultiplier;
        enemy.AttackAnimationSeconds = 0.35f;
    }

    private void UpdateBossPhase(EnemyState enemy)
    {
        float healthFraction = enemy.Health / enemy.Definition.MaxHealth;
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

        DamagePlayer(enemy.Definition.AttackDamage * damageMultiplier, enemy.Id, enemy.Position, "melee");
        enemy.AttackCooldownSeconds = enemy.Definition.AttackCooldownSeconds;
        enemy.AttackAnimationSeconds = 0.3f;
        return true;
    }

    private void MaintainEnemyRange(EnemyState enemy, float distance, float deltaSeconds, float speedMultiplier = 1f)
    {
        float preferredRange = MathF.Max(enemy.Definition.AttackRange + 1f, enemy.Definition.PreferredRange);
        if (distance > preferredRange + 1.5f)
        {
            NavigateEnemyTowardPlayer(enemy, enemy.Definition.MoveSpeed * speedMultiplier, deltaSeconds);
            return;
        }

        Vector3 away = enemy.Position - Player.Position;
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
        enemy.PathRefreshRemainingSeconds -= deltaSeconds;
        if (enemy.PathRefreshRemainingSeconds <= 0f || enemy.PathIndex >= enemy.Path.Count)
        {
            _navigation.FindPath(enemy.Position, Player.Position, enemy.Path);
            enemy.PathIndex = Math.Min(1, enemy.Path.Count);
            enemy.PathRefreshRemainingSeconds = enemy.Definition.PathRefreshSeconds;
        }

        Vector3 target = enemy.PathIndex < enemy.Path.Count ? enemy.Path[enemy.PathIndex] : Player.Position;
        Vector3 direction = target - enemy.Position;
        direction.Y = 0f;
        if (direction.LengthSquared() < 0.2f)
        {
            enemy.PathIndex++;
            return;
        }

        MoveEnemy(enemy, direction, speed, deltaSeconds);
    }

    private bool MoveEnemy(EnemyState enemy, Vector3 direction, float speed, float deltaSeconds)
    {
        direction.Y = 0f;
        if (direction.LengthSquared() <= 0.0001f)
        {
            return false;
        }

        direction = Vector3.Normalize(direction);
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

    private void FireEnemyProjectile(EnemyState enemy, float yawOffsetDegrees, float damage, float speedMultiplier)
    {
        Vector3 origin = enemy.Position + new Vector3(0f, enemy.Definition.ColliderHeight * 0.55f, 0f);
        Vector3 direction = Vector3.Normalize(Player.Position - origin);
        direction = RotateAroundY(direction, yawOffsetDegrees * (MathF.PI / 180f));
        float speed = enemy.Definition.ProjectileSpeed * speedMultiplier;
        Projectiles.Add(new ProjectileState
        {
            Id = NextEntity(),
            OwnerId = enemy.Id,
            Position = origin + (direction * (enemy.Definition.ColliderRadius + 0.2f)),
            PreviousPosition = origin,
            Velocity = direction * speed,
            Radius = enemy.Definition.ProjectileRadius,
            Damage = damage,
            IsHostile = true,
            SplashRadius = enemy.Definition.ProjectileSplashRadius,
            Color = enemy.Definition.Tint,
            RemainingSeconds = enemy.Definition.RangedAttackRange / MathF.Max(1f, speed),
        });
        AddEvent(CombatEventType.EnemyAttack, origin, Player.Position,
            enemy.Id, Player.Id, enemy.Definition.IsBoss ? "boss-bolt" : "enemy-shot");
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

    private void UpdateProjectiles(float deltaSeconds)
    {
        for (int index = Projectiles.Count - 1; index >= 0; index--)
        {
            ProjectileState projectile = Projectiles[index];
            projectile.PreviousPosition = projectile.Position;
            Vector3 nextPosition = projectile.Position + (projectile.Velocity * deltaSeconds);
            projectile.RemainingSeconds -= deltaSeconds;
            bool remove = projectile.RemainingSeconds <= 0f;
            float nearestFraction = 1f;
            EnemyState? directEnemy = null;
            bool playerHit = false;

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
            else
            {
                foreach (EnemyState enemy in Enemies)
                {
                    Vector3 enemyCenter = enemy.Position + new Vector3(0f, enemy.Definition.ColliderHeight * 0.35f, 0f);
                    if (enemy.IsDead || !SegmentIntersectsSphere(
                        projectile.Position,
                        nextPosition,
                        enemyCenter,
                        projectile.Radius + enemy.Definition.ColliderRadius,
                        out float fraction) || fraction >= nearestFraction)
                    {
                        continue;
                    }

                    nearestFraction = fraction;
                    directEnemy = enemy;
                    remove = true;
                }
            }

            if (remove)
            {
                if (nearestFraction < 1f)
                {
                    projectile.Position = Vector3.Lerp(projectile.Position, nextPosition, nearestFraction);
                    ImpactProjectile(projectile, directEnemy, playerHit);
                }

                Projectiles.RemoveAt(index);
            }
            else
            {
                projectile.Position = nextPosition;
            }
        }
    }

    private void ImpactProjectile(ProjectileState projectile, EnemyState? directEnemy, bool playerHit)
    {
        AddEvent(CombatEventType.WorldImpact, projectile.Position, projectile.PreviousPosition,
            projectile.OwnerId, directEnemy?.Id ?? EntityId.None, projectile.WeaponId);
        if (projectile.IsHostile)
        {
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
                DamageEnemy(enemy, projectile.Damage * multiplier, projectile.Position, Player.Id, projectile.WeaponId);
                AddEvent(CombatEventType.EnemyHit, projectile.Position, projectile.PreviousPosition,
                    Player.Id, enemy.Id, projectile.WeaponId, projectile.Damage * multiplier);
                damaged.Add(enemy);
            }
        }
        else if (directEnemy is not null)
        {
            DamageEnemy(directEnemy, projectile.Damage, projectile.Position, Player.Id, projectile.WeaponId);
            AddEvent(CombatEventType.EnemyHit, projectile.Position, projectile.PreviousPosition,
                Player.Id, directEnemy.Id, projectile.WeaponId, projectile.Damage);
            damaged.Add(directEnemy);
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

            float damage = projectile.Damage * MathF.Pow(0.68f, chain + 1);
            DamageEnemy(next, damage, next.Position, Player.Id, projectile.WeaponId);
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

        enemy.Health = MathF.Max(0f, enemy.Health - damage);
        enemy.HitFlashSeconds = 0.12f;
        if (sourceId == Player.Id)
        {
            LastHitSeconds = 0f;
        }

        if (!enemy.IsDead)
        {
            return;
        }

        Kills++;
        LastKillSeconds = 0f;
        Score += enemy.Definition.ScoreValue;
        AddEvent(CombatEventType.EnemyKilled, impactPosition, enemy.Position,
            sourceId, enemy.Id, weaponId, enemy.Definition.ScoreValue);
        float roll = _random.NextSingle();
        if (roll < enemy.Definition.HealthDropChance)
        {
            AddDroppedPickup(PickupType.Health, enemy.Position, 20);
        }
        else if (roll < enemy.Definition.HealthDropChance + enemy.Definition.AmmoDropChance)
        {
            AddDroppedPickup(PickupType.Ammo, enemy.Position, 16);
        }
    }

    private void AddDroppedPickup(PickupType type, Vector3 position, int amount) => Pickups.Add(new PickupState
    {
        Id = NextEntity(),
        Type = type,
        Position = new Vector3(position.X, 0.5f, position.Z),
        Amount = amount,
        RespawnSeconds = 0f,
        IsDropped = true,
    });

    private void UpdatePickups(float deltaSeconds)
    {
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

            Vector2 playerPosition = new(Player.Position.X, Player.Position.Z);
            Vector2 pickupPosition = new(pickup.Position.X, pickup.Position.Z);
            if (Vector2.DistanceSquared(playerPosition, pickupPosition) > 1.2f * 1.2f)
            {
                continue;
            }

            bool consumed = pickup.Type switch
            {
                PickupType.Health => TryApplyHealth(pickup.Amount),
                PickupType.Ammo => TryApplyAmmo(pickup.Amount),
                PickupType.Weapon => TryApplyWeapon(pickup.WeaponId, pickup.Amount),
                _ => false,
            };

            if (!consumed)
            {
                continue;
            }

            AddEvent(CombatEventType.PickupCollected, pickup.Position, Player.Position,
                pickup.Id, Player.Id, pickup.Type.ToString(), pickup.Amount);

            if (pickup.IsDropped)
            {
                Pickups.RemoveAt(index);
            }
            else
            {
                pickup.IsAvailable = false;
                pickup.RespawnRemainingSeconds = pickup.RespawnSeconds;
            }
        }
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

        Player.Weapons.Add(new WeaponState(definition));
        Player.SelectedWeaponIndex = Player.Weapons.Count - 1;
        return true;
    }

    private void DamagePlayer(float damage, EntityId sourceId, Vector3 sourcePosition, string cueId)
    {
        if (damage <= 0f || Player.Health <= 0f || GodModeEnabled)
        {
            return;
        }

        Player.Health = MathF.Max(0f, Player.Health - damage);
        PlayerDamageFlashSeconds = 0.32f;
        AddEvent(CombatEventType.PlayerDamaged, Player.Position, sourcePosition,
            sourceId, Player.Id, cueId, damage);
    }

    private Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
    {
        if (spreadDegrees <= 0f)
        {
            return direction;
        }

        float spread = spreadDegrees * (MathF.PI / 180f);
        float yawOffset = (_random.NextSingle() - 0.5f) * spread;
        float pitchOffset = (_random.NextSingle() - 0.5f) * spread;
        float yaw = MathF.Atan2(direction.X, -direction.Z) + yawOffset;
        float pitch = MathF.Asin(Math.Clamp(direction.Y, -1f, 1f)) + pitchOffset;
        float cosinePitch = MathF.Cos(pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * cosinePitch,
            MathF.Sin(pitch),
            -MathF.Cos(yaw) * cosinePitch));
    }

    private static float CalculateDamageFalloff(WeaponDefinition weapon, float distance)
    {
        if (weapon.DamageFalloffStart <= 0f || distance <= weapon.DamageFalloffStart ||
            weapon.Range <= weapon.DamageFalloffStart)
        {
            return 1f;
        }

        float normalized = Math.Clamp(
            (distance - weapon.DamageFalloffStart) / (weapon.Range - weapon.DamageFalloffStart),
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

    private void AddEvent(
        CombatEventType type,
        Vector3 position,
        Vector3 secondaryPosition,
        EntityId sourceId,
        EntityId targetId,
        string? cueId,
        float value = 0f) =>
        _combatEvents.Add(new CombatEvent(type, position, secondaryPosition, sourceId, targetId, cueId, value));

    private void CleanupEnemies() => Enemies.RemoveAll(enemy => enemy.IsDead && enemy.DeathSeconds >= 0.8f);

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
}
