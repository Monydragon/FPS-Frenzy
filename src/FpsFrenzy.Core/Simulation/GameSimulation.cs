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

    public GameSimulation(ContentCatalog catalog, RunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        _catalog = catalog;
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
                configuration.IsFirstRun);
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
        Player.Weapons.Add(new WeaponState(startingWeapon, _runDirector?.Modifiers));
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
    public DifficultyMode Difficulty => _configuration.Difficulty;
    public global::FpsFrenzy.Core.Simulation.RunPhase RunPhase =>
        _runDirector?.Phase ?? global::FpsFrenzy.Core.Simulation.RunPhase.LegacyWaves;
    public RunSnapshot? RunSnapshot => _runDirector?.CreateSnapshot();
    public EncounterDefinition? CurrentEncounter => _runDirector?.CurrentEncounter;
    public ArenaSectorDefinition? ActiveSector => _runDirector?.CurrentEncounter is { } encounter
        ? _runDirector.SelectedSectors[encounter.SectorNumber - 1]
        : null;
    public IReadOnlyList<string> ClosedEnergyGateIds =>
        !_awaitingArmoryCollection &&
        _runDirector?.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.EncounterActive
            ? ActiveSector?.EnergyGateIds ?? []
            : [];
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
    public float ObjectiveProgress => CalculateObjectiveProgress();
    public int CurrentPressureWaveNumber => _generatedPressureWaveIndex;
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
        UpdateWeapons(command, fixedDeltaSeconds);
        if (_runDirector is null)
        {
            UpdateWaveDirector(fixedDeltaSeconds);
        }
        else
        {
            UpdateRunDirector(fixedDeltaSeconds);
        }
        if (Phase == GamePhase.Playing)
        {
            UpdateEnemies(fixedDeltaSeconds);
            UpdateProjectiles(fixedDeltaSeconds);
            UpdatePickups(fixedDeltaSeconds);
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

    public void SetPlayerInvulnerable(bool invulnerable)
    {
        GodModeEnabled = invulnerable;
        if (invulnerable)
        {
            _runDirector?.MarkGodModeUsed();
        }
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
        Phase = GamePhase.Victory;
        AddEvent(CombatEventType.EncounterCompleted, Arena.BossArenaAnchor, Player.Position,
            EntityId.None, Player.Id, "breach-walker-debug");
        return true;
    }

    public UpgradeDefinition ChooseUpgrade(string upgradeId)
    {
        if (_runDirector is null)
        {
            throw new InvalidOperationException("Upgrade choices are only available in a sector run.");
        }

        float previousMaximumHealth = Player.MaximumHealth;
        UpgradeDefinition selected = _runDirector.ChooseUpgrade(upgradeId);
        Player.MaximumHealth = 100f + _runDirector.Modifiers.MaximumHealthBonus;
        if (Player.MaximumHealth > previousMaximumHealth)
        {
            Player.Health = MathF.Min(Player.MaximumHealth,
                Player.Health + (Player.MaximumHealth - previousMaximumHealth));
        }

        _emergencyBarrierAvailable = _runDirector.Modifiers.HasEmergencyBarrier;
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
        };
    }

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
        float movementMultiplier = Player.AdrenalSeconds > 0f
            ? _runDirector?.Modifiers.KillMovementSpeedMultiplier ?? 1f
            : 1f;
        Vector3 desiredVelocity = ((right * moveInput.X) + (forward * moveInput.Y)) *
            PlayerMoveSpeed * movementMultiplier;

        bool jumpPressed = command.Has(PlayerButtons.Jump) && !_previousButtons.HasFlag(PlayerButtons.Jump);
        PlayerPhysicsResult result = _playerController.Step(
            desiredVelocity, jumpPressed, Player.IsGrounded, deltaSeconds);
        Player.Position = result.Position;
        Player.VerticalVelocity = result.VerticalVelocity;
        Player.IsGrounded = result.IsGrounded;
        ConstrainPlayerToActiveSector();
    }

    private void ConstrainPlayerToActiveSector()
    {
        if (_awaitingArmoryCollection)
        {
            return;
        }

        Vector3 constrained = Player.Position;
        if (_runDirector?.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.BossActive)
        {
            Vector3 halfExtents = Arena.BossArenaHalfExtents;
            constrained.X = Math.Clamp(
                constrained.X,
                Arena.BossArenaAnchor.X - halfExtents.X + 0.5f,
                Arena.BossArenaAnchor.X + halfExtents.X - 0.5f);
            constrained.Z = Math.Clamp(
                constrained.Z,
                Arena.BossArenaAnchor.Z - halfExtents.Z + 0.5f,
                Arena.BossArenaAnchor.Z + halfExtents.Z - 0.5f);
            if (PendingEnemySpawns.Any(pending =>
                    pending.PortalId.Equals("boss-core", StringComparison.OrdinalIgnoreCase)))
            {
                Vector3 fromCore = constrained - Arena.BossArenaAnchor;
                fromCore.Y = 0f;
                const float bossCoreSafetyRadius = 12f;
                if (fromCore.LengthSquared() < bossCoreSafetyRadius * bossCoreSafetyRadius)
                {
                    float z = Math.Clamp(fromCore.Z, -bossCoreSafetyRadius + 0.01f,
                        bossCoreSafetyRadius - 0.01f);
                    float requiredX = MathF.Sqrt(MathF.Max(0f,
                        (bossCoreSafetyRadius * bossCoreSafetyRadius) - (z * z)));
                    float sign = fromCore.X < 0f ? -1f : 1f;
                    constrained.X = Arena.BossArenaAnchor.X + (requiredX * sign);
                    constrained.Z = Arena.BossArenaAnchor.Z + z;
                }
            }
        }
        else if (_runDirector?.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.EncounterActive &&
                 ActiveSector is ArenaSectorDefinition sector)
        {
            constrained.X = Math.Clamp(constrained.X, sector.BoundsMin.X + 0.5f, sector.BoundsMax.X - 0.5f);
            constrained.Z = Math.Clamp(constrained.Z, sector.BoundsMin.Z + 0.5f, sector.BoundsMax.Z - 0.5f);
        }
        else
        {
            return;
        }
        if (Vector3.DistanceSquared(constrained, Player.Position) <= 0.000001f)
        {
            return;
        }

        _playerController.Teleport(constrained);
        Player.Position = constrained;
        Player.VerticalVelocity = 0f;
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
                float spreadMultiplier = _runDirector?.Modifiers.SpreadMultiplier(current.Definition.Id) ?? 1f;
                Vector3 pelletDirection = ApplySpread(direction,
                    current.Definition.SpreadDegrees * spreadMultiplier);
                FireHitscan(Player.Position, muzzle, pelletDirection, current.Definition);
            }
        }
        else
        {
            Vector3 aimPoint = Player.Position + (direction * current.Definition.Range);
            Vector3 projectileDirection = Vector3.Normalize(aimPoint - muzzle);
            float projectileSpeed = current.Definition.ProjectileSpeed *
                (_runDirector?.Modifiers.ProjectileSpeedMultiplier(current.Definition.Id) ?? 1f);
            Projectiles.Add(new ProjectileState
            {
                Id = NextEntity(),
                OwnerId = Player.Id,
                Position = muzzle,
                PreviousPosition = muzzle,
                Origin = muzzle,
                Velocity = projectileDirection * projectileSpeed,
                Radius = current.Definition.ProjectileRadius,
                Damage = current.Definition.Damage,
                WeaponId = current.Definition.Id,
                SplashRadius = current.Definition.SplashRadius *
                    (_runDirector?.Modifiers.SplashRadiusMultiplier(current.Definition.Id) ?? 1f),
                ChainRadius = current.Definition.ChainRadius *
                    (_runDirector?.Modifiers.ChainRadiusMultiplier(current.Definition.Id) ?? 1f),
                ChainTargets = current.Definition.ChainTargets +
                    (_runDirector?.Modifiers.ChainTargetBonus(current.Definition.Id) ?? 0),
                Color = current.Definition.ProjectileColor,
                RemainingSeconds = current.Definition.Range / MathF.Max(1f, projectileSpeed),
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
            multiplier *= _runDirector?.Modifiers.DamageMultiplier(weapon.Id, nearestEnemyDistance) ?? 1f;
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
        Phase = GamePhase.Victory;
        Score += (int)(Player.Health * 10f);
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

    private SpawnPortalDefinition? SelectSpawnPortal(
        ArenaSectorDefinition sector,
        PendingEnemySpawn? ignoredPending = null)
    {
        Vector3 viewDirection = GetViewDirection();
        return sector.SpawnPortals
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
            .OrderBy(portal =>
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

        RelayObjective.Health = MathF.Max(0f, RelayObjective.Health - (damage * 0.5f));
        AddEvent(CombatEventType.RelayDamaged, RelayObjective.Position, RelayObjective.Position,
            sourceId, EntityId.None, "relay", damage * 0.5f);
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
        ActivateArmoryPickup(_runDirector.CurrentEncounterIndex);
        TeleportPlayerToRecoveryHub();
        UpgradeOffer offer = _runDirector.CompleteEncounter();
        AddEvent(CombatEventType.EncounterCompleted, Player.Position, Player.Position,
            EntityId.None, Player.Id, encounter.Id, completionMetric);
        AddEvent(CombatEventType.UpgradeOffered, Player.Position, Player.Position,
            EntityId.None, Player.Id, string.Join(',', offer.Choices.Select(choice => choice.Id)));
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
        float maximumHealth = definition.MaxHealth * (isElite ? eliteHealthMultiplier : 1f);
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
            if (enemy.IsDead)
            {
                enemy.ActionState = EnemyActionState.Death;
                enemy.DeathSeconds += deltaSeconds;
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
        if (_queuedRecycles.Count == 0 || ActiveSector is not ArenaSectorDefinition sector)
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
            enemy.ActionState = EnemyActionState.Windup;
            enemy.AiTimerSeconds = enemy.Definition.ChargeWindupSeconds;
            enemy.AttackCooldownSeconds = enemy.Definition.ChargeCooldownSeconds;
            enemy.AttackAnimationSeconds = enemy.Definition.ChargeWindupSeconds;
            AddEvent(CombatEventType.EnemyTelegraph, enemy.Position, GetEnemyTargetPosition(enemy),
                enemy.Id, enemy.TargetsRelay ? EntityId.None : Player.Id,
                "charge-windup", enemy.Definition.ChargeWindupSeconds);
            AddEvent(CombatEventType.EnemyAttackStarted, enemy.Position, GetEnemyTargetPosition(enemy),
                enemy.Id, enemy.TargetsRelay ? EntityId.None : Player.Id,
                "charge-windup", enemy.Definition.ChargeWindupSeconds);
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
        enemy.AttackCooldownSeconds = enemy.Definition.AttackCooldownSeconds * phase.AttackCooldownMultiplier;
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
        enemy.ActionState = EnemyActionState.Windup;
        enemy.AiTimerSeconds = windupSeconds;
        enemy.AttackCooldownSeconds = enemy.Definition.AttackCooldownSeconds;
        enemy.AttackAnimationSeconds = windupSeconds + enemy.Definition.AttackRecoverySeconds;
        Vector3 targetPosition = GetEnemyTargetPosition(enemy);
        EntityId targetId = enemy.PendingAttackTargetsRelay ? EntityId.None : Player.Id;
        AddEvent(CombatEventType.EnemyTelegraph, enemy.Position, targetPosition,
            enemy.Id, targetId, cueId, windupSeconds);
        AddEvent(CombatEventType.EnemyAttackStarted, enemy.Position, targetPosition,
            enemy.Id, targetId, cueId, windupSeconds);
    }

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
        if (_runDirector?.Phase == global::FpsFrenzy.Core.Simulation.RunPhase.BossActive)
        {
            candidate.X = Math.Clamp(
                candidate.X,
                Arena.BossArenaAnchor.X - Arena.BossArenaHalfExtents.X + radius,
                Arena.BossArenaAnchor.X + Arena.BossArenaHalfExtents.X - radius);
            candidate.Z = Math.Clamp(
                candidate.Z,
                Arena.BossArenaAnchor.Z - Arena.BossArenaHalfExtents.Z + radius,
                Arena.BossArenaAnchor.Z + Arena.BossArenaHalfExtents.Z - radius);
        }
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
        float speed = enemy.Definition.ProjectileSpeed * speedMultiplier;
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
            RemainingSeconds = enemy.Definition.RangedAttackRange / MathF.Max(1f, speed),
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
                    ImpactProjectile(projectile, directEnemy, playerHit, relayHit);
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
            DamageEnemy(directEnemy, projectile.Damage * runMultiplier,
                projectile.Position, Player.Id, projectile.WeaponId);
            AddEvent(CombatEventType.EnemyHit, projectile.Position, projectile.PreviousPosition,
                Player.Id, directEnemy.Id, projectile.WeaponId, projectile.Damage * runMultiplier);
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

            float runMultiplier = _runDirector?.Modifiers.DamageMultiplier(
                projectile.WeaponId ?? string.Empty,
                Vector3.Distance(projectile.Origin, next.Position)) ?? 1f;
            float damage = projectile.Damage * runMultiplier * MathF.Pow(0.68f, chain + 1);
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
        }

        if (!enemy.IsDead)
        {
            return;
        }

        Kills++;
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
            float pickupRadius = 1.2f * (_runDirector?.Modifiers.PickupRadiusMultiplier ?? 1f);
            if (Vector2.DistanceSquared(playerPosition, pickupPosition) > pickupRadius * pickupRadius)
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
                _ => false,
            };

            if (!consumed)
            {
                continue;
            }

            AddEvent(CombatEventType.PickupCollected, pickup.Position, Player.Position,
                pickup.Id, Player.Id, pickup.Type.ToString(), pickupAmount);

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
            damage * (_runDirector?.Modifiers.IncomingDamageMultiplier ?? 1f));
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
