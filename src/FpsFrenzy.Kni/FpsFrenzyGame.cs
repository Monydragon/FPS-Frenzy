using FpsFrenzy.Core;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Input;
using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Audio;
using FpsFrenzy.Kni.Development;
using FpsFrenzy.Kni.Input;
using FpsFrenzy.Kni.Progression;
using FpsFrenzy.Kni.Rendering;
using FpsFrenzy.Kni.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using CoreVector2 = System.Numerics.Vector2;
using CoreVector3 = System.Numerics.Vector3;
using KniAnimationClipBinding = FpsFrenzy.Kni.Rendering.AnimationClipBinding;
using KniAnimationLoopMode = FpsFrenzy.Kni.Animation.AnimationLoopMode;

namespace FpsFrenzy.Kni;

public sealed class FpsFrenzyGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly IPlatformLookSource? _platformLookSource;
    private readonly IPlatformMouseCapture? _mouseCapture;
    private KniInputSource? _input;
    private ContentCatalog? _catalog;
    private GameSimulation? _simulation;
    private PrimitiveRenderer? _primitives;
    private readonly Dictionary<string, StaticModelPresenter> _weaponModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SkinnedModelPresenter> _enemyModelPresenters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StaticModelPresenter> _pickupModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StaticModelPresenter> _arenaModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture2D> _arenaTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<EntityId, EnemyModelInstance> _enemyModels = [];
    private readonly List<EntityId> _inactiveEnemyModels = [];
    private HudRenderer? _hud;
    private CombatFeedbackPresenter? _feedback;
    private CombatAudio? _audio;
    private readonly GameSettings _settings;
    private readonly SettingsMenuController _menu = new();
    private readonly RenderCaptureService _capture = new();
    private double _accumulatorSeconds;
    private float _interpolationAlpha;
    private CoreVector2 _pendingLook;
    private PlayerCommand _latestInput;
    private float _adsBlend;
    private int _appliedRenderFrameRate;
    private readonly bool _automaticCapture;
    private readonly bool _automaticMenuCapture;
    private readonly int _startingWaveIndex;
    private string _startingWeaponId;
    private readonly string _capturePrefix;
    private bool _openingCaptured;
    private bool _openingCaptureRendered;
    private bool _lookFollowCaptured;
    private bool _lookFollowCaptureRendered;
    private bool _impactCaptured;
    private bool _impactCaptureRendered;
    private bool _combatCaptured;
    private double _presentationSeconds;
    private int _automaticMenuStage;
    private double _nextAutomaticMenuSeconds;
    private bool _mouseCaptured;
    private bool _runActive;
    private bool _suppressGameplayInputUntilNeutral;
    private string? _presentedWeaponId;
    private float _weaponPresentationSeconds;
    private readonly ProfileStore _profileStore;
    private readonly ProfileData _profile;
    private readonly RunCheckpointStore _checkpointStore;
    private readonly HashSet<string> _newUnlockIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _runUnlockBaselineIds = new(StringComparer.OrdinalIgnoreCase);
    private RunCheckpoint? _availableCheckpoint;
    private bool _resultRecorded;
    private readonly CharacterLabOptions? _characterLabOptions;
    private CharacterLabController? _characterLab;
    private CharacterLabRenderer? _characterLabRenderer;
    private EnemyDefinition? _characterLabEnemy;
    private SkinnedModelPresenter? _characterLabPresenter;
    private EnemyModelInstance? _characterLabInstance;
    private CharacterLabPose? _characterLabPresentedPose;
    private readonly DebugTestController _debug = new();
    private bool _debugSandboxActive;

    public FpsFrenzyGame(
        IPlatformLookSource? platformLookSource = null,
        IPlatformMouseCapture? mouseCapture = null,
        bool fullScreen = false)
    {
        _platformLookSource = platformLookSource;
        _mouseCapture = mouseCapture;
        _characterLabOptions = CharacterLabOptions.FromEnvironment();
        _settings = _characterLabOptions is null
            ? GameSettingsStore.Load()
            : new GameSettings
            {
                MasterVolume = 0f,
                MusicVolume = 0f,
                SoundEffectsVolume = 0f,
                RenderFrameRate = _characterLabOptions.FramesPerSecond,
                ScreenShakeScale = 0f,
                CameraBobScale = 0f,
            };
        if (_characterLabOptions is null &&
            bool.TryParse(Environment.GetEnvironmentVariable("FPS_FRENZY_CAPTURE_GOD_MODE"), out bool captureGodMode))
        {
            _settings = _settings with { GodMode = captureGodMode };
        }
        string isolatedStateDirectory = Path.Combine(_capture.DirectoryPath, ".character-lab-state");
        _profileStore = _characterLabOptions is null
            ? ProfileStore.CreateDefault()
            : new ProfileStore(Path.Combine(isolatedStateDirectory, "profile.json"));
        _profile = _characterLabOptions is null ? _profileStore.Load() : ProfileData.CreateDefault();
        _checkpointStore = _characterLabOptions is null
            ? RunCheckpointStore.CreateDefault()
            : new RunCheckpointStore(Path.Combine(isolatedStateDirectory, "checkpoint.json"));
        _availableCheckpoint = _characterLabOptions is null ? _checkpointStore.Load() : null;
        _automaticCapture = _characterLabOptions is null &&
            Environment.GetEnvironmentVariable("FPS_FRENZY_AUTOCAPTURE") == "1";
        _automaticMenuCapture = _characterLabOptions is null &&
            Environment.GetEnvironmentVariable("FPS_FRENZY_AUTOCAPTURE_MENUS") == "1";
        _startingWaveIndex = int.TryParse(
            Environment.GetEnvironmentVariable("FPS_FRENZY_START_WAVE"),
            out int configuredStartingWave)
            ? Math.Max(0, configuredStartingWave)
            : 0;
        _startingWeaponId = Environment.GetEnvironmentVariable("FPS_FRENZY_START_WEAPON") ??
            _profile.SelectedStartingWeaponId;
        _capturePrefix = Environment.GetEnvironmentVariable("FPS_FRENZY_CAPTURE_PREFIX") ?? string.Empty;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            IsFullScreen = fullScreen,
            SynchronizeWithVerticalRetrace = true,
            PreferMultiSampling = true,
            SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight,
        };
        Content.RootDirectory = "Content";
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.AllowUserResizing = !fullScreen;
    }

    protected override void Initialize()
    {
        _input = new KniInputSource(_settings, _platformLookSource, _mouseCapture);
        ApplySettings();
        base.Initialize();
    }

    public void SetRenderFrameRate(int framesPerSecond)
    {
        if (framesPerSecond is not (30 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "Rendering supports 30 or 60 FPS.");
        }

        TargetElapsedTime = TimeSpan.FromSeconds(1d / framesPerSecond);
        IsFixedTimeStep = true;
    }

    protected override void LoadContent()
    {
        _catalog = LoadCatalog();
        _startingWeaponId = ResolveStartingWeaponId(
            _catalog,
            _startingWeaponId,
            _profile.SelectedStartingWeaponId);

        ConfigureMenuProfile();
        RunCheckpoint? captureCheckpoint = _automaticCapture
            ? CreateCaptureCheckpoint(_startingWaveIndex)
            : null;
        _simulation = CreateSimulation(captureCheckpoint, isFirstRun: false, seed: 1337);
        _primitives = new PrimitiveRenderer(GraphicsDevice);
        foreach (WeaponDefinition weapon in _catalog.Weapons.Values)
        {
            _weaponModels.Add(weapon.Id, new StaticModelPresenter(
                Content.Load<Model>(weapon.ModelAsset), ModelTextureSampling.Palette));
        }

        foreach (IGrouping<string, EnemyDefinition> group in _catalog.Enemies.Values
                     .GroupBy(enemy => enemy.ModelAsset, StringComparer.OrdinalIgnoreCase))
        {
            EnemyDefinition enemy = group.First();
            Texture2D? albedo = string.IsNullOrWhiteSpace(enemy.Visual.AlbedoAsset)
                ? null
                : Content.Load<Texture2D>(enemy.Visual.AlbedoAsset);
            Texture2D? emissiveMask = string.IsNullOrWhiteSpace(enemy.Visual.EmissiveAsset)
                ? null
                : Content.Load<Texture2D>(enemy.Visual.EmissiveAsset);
            ModelTextureSampling sampling = enemy.Visual.TextureSampling == TextureSamplingMode.PointNoMipmaps
                ? ModelTextureSampling.Palette
                : ModelTextureSampling.Detailed;
            _enemyModelPresenters.Add(enemy.ModelAsset,
                new SkinnedModelPresenter(
                    Content.Load<Model>(enemy.ModelAsset),
                    albedo,
                    textureSampling: sampling,
                    emissiveMask: emissiveMask));
        }

        if (_characterLabOptions is not null)
        {
            if (!_catalog.Enemies.TryGetValue(_characterLabOptions.EnemyId, out _characterLabEnemy) ||
                _characterLabEnemy.SchemaVersion != 2)
            {
                throw new InvalidDataException(
                    $"Character Lab enemy '{_characterLabOptions.EnemyId}' must be a schema-v2 release robot.");
            }

            _characterLab = new CharacterLabController(_characterLabOptions);
            _characterLabPresenter = _enemyModelPresenters[_characterLabEnemy.ModelAsset];
            _characterLabInstance = _characterLabPresenter.CreateInstance();
            _characterLabRenderer = new CharacterLabRenderer(GraphicsDevice);
            WriteCharacterLabCalibrationDiagnostics(
                _characterLabEnemy,
                _characterLabPresenter,
                Content.Load<Model>(_characterLabEnemy.ModelAsset));
            if (_characterLabOptions.Mode == CharacterLabCaptureMode.Reel)
            {
                _capture.StartRecording(
                    _characterLabOptions.ReelName,
                    CharacterLabOptions.ReelDurationSeconds,
                    _characterLabOptions.FramesPerSecond,
                    captureEveryDraw: true);
            }
        }

        _pickupModels.Add("health", new StaticModelPresenter(
            Content.Load<Model>("Models/Pickups/health-crate"), ModelTextureSampling.Palette));
        _pickupModels.Add("ammo", new StaticModelPresenter(
            Content.Load<Model>("Models/Pickups/ammo-cache"), ModelTextureSampling.Palette));
        _pickupModels.Add("pedestal", new StaticModelPresenter(
            Content.Load<Model>("Models/Arenas/OrbitalDepot/Station/table-display-small"), ModelTextureSampling.Palette));
        _pickupModels.Add("container", new StaticModelPresenter(
            Content.Load<Model>("Models/Arenas/OrbitalDepot/Station/container-flat-open"), ModelTextureSampling.Palette));
        _pickupModels.Add("ring", new StaticModelPresenter(
            Content.Load<Model>("Models/Arenas/OrbitalDepot/Station/pipe-ring-colored"), ModelTextureSampling.Palette));
        foreach (string modelAsset in _catalog.Arenas.Values
                     .SelectMany(arena => arena.Props)
                     .Select(prop => prop.ModelAsset)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _arenaModels.Add(modelAsset, new StaticModelPresenter(
                Content.Load<Model>(modelAsset), ModelTextureSampling.Palette));
        }

        foreach (string textureAsset in _catalog.Arenas.Values
                     .SelectMany(arena => arena.Primitives)
                     .Select(primitive => primitive.TextureAsset)
                     .OfType<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _arenaTextures.Add(textureAsset, Content.Load<Texture2D>(textureAsset));
        }
        _hud = new HudRenderer(
            GraphicsDevice,
            Content.Load<Texture2D>("Textures/UI/menu-button"),
            Content.Load<Texture2D>("Textures/UI/menu-button-selected"),
            Content.Load<Texture2D>("Textures/UI/menu-emblem"));
        _feedback = new CombatFeedbackPresenter(_catalog);
        if (_characterLabOptions is not null)
        {
            _simulation.SetPaused(true);
            _menu.Close();
            _runActive = false;
            ApplySettings();
        }
        else
        {
            _audio = new CombatAudio(Content, _settings);
            _runActive = _automaticCapture;
            if (!_automaticCapture)
            {
                _simulation.SetPaused(true);
                _menu.OpenMain();
            }
        }

        _debugSandboxActive = _debug.Enabled && _runActive;
        _simulation.SetPlayerInvulnerable(_settings.GodMode || _debug.GodModeOverride);

        base.LoadContent();
        RefreshMouseCapture();
    }

    protected override void Update(GameTime gameTime)
    {
        if (_simulation is null || _input is null || _catalog is null)
        {
            base.Update(gameTime);
            return;
        }

        _capture.UpdateInput();
        if (_characterLab is not null)
        {
            UpdateCharacterLab();
            base.Update(gameTime);
            return;
        }

        Rectangle safeArea = GraphicsDevice.Viewport.TitleSafeArea;
        MenuInputSnapshot menuInput = MenuInputSnapshot.Capture(_menu.Page, safeArea);
        MenuPage previousMenuPage = _menu.Page;
        int previousMenuIndex = _menu.SelectedIndex;
        MenuAction menuAction = _menu.Update(_settings, menuInput, safeArea);
        if (_menu.SelectedIndex != previousMenuIndex && _menu.Page == previousMenuPage)
        {
            _audio?.PlayInterfaceCue("ui-hover", _settings);
        }
        else if (_menu.Page != previousMenuPage)
        {
            _audio?.PlayInterfaceCue("ui-confirm", _settings);
        }

        switch (menuAction)
        {
            case MenuAction.Pause:
                _simulation.SetPaused(true);
                break;
            case MenuAction.Resume:
                _simulation.SetPaused(false);
                _suppressGameplayInputUntilNeutral = true;
                break;
            case MenuAction.Restart:
                StartNewRun();
                break;
            case MenuAction.ContinueRun:
                ContinueRun();
                break;
            case MenuAction.StartRun:
                if (!_automaticCapture && !_profile.TutorialSeen)
                {
                    _simulation.SetPaused(true);
                    _menu.OpenTutorial();
                    break;
                }

                StartNewRun();
                break;
            case MenuAction.BeginRun:
                _profile.TutorialSeen = true;
                _profileStore.Save(_profile);
                StartNewRun();
                break;
            case MenuAction.ReturnToMain:
                if (_runActive && !_debugSandboxActive &&
                    _simulation.Phase is not (GamePhase.Victory or GamePhase.Defeat))
                {
                    AbandonCheckpoint();
                }
                _runActive = false;
                _simulation.SetPaused(true);
                ConfigureMenuProfile();
                _menu.OpenMain();
                break;
            case MenuAction.Quit:
                if (_runActive && !_debugSandboxActive)
                {
                    AbandonCheckpoint();
                }
                Exit();
                return;
            case MenuAction.SettingsChanged:
                GameSettingsStore.Save(_settings);
                ApplySettings();
                _simulation.SetPlayerInvulnerable(_settings.GodMode || _debug.GodModeOverride);
                _audio?.PlayInterfaceCue("ui-toggle", _settings);
                break;
            case MenuAction.StartingWeaponChanged:
                _startingWeaponId = _menu.StartingWeaponId;
                _profileStore.Save(_profile);
                _audio?.PlayInterfaceCue("ui-confirm", _settings);
                break;
            case MenuAction.UpgradeSelected:
                if (_menu.SelectedUpgradeId is not null && _simulation.RunPhase == RunPhase.RewardSelection)
                {
                    _simulation.ChooseUpgrade(_menu.SelectedUpgradeId);
                    _audio?.PlayInterfaceCue("upgrade", _settings);
                    SaveCheckpoint();
                    _suppressGameplayInputUntilNeutral = true;
                }
                break;
        }
        RefreshMouseCapture();

        KeyboardState keyboard = Keyboard.GetState();
        DebugTestAction debugAction = _debug.Update(keyboard, _runActive);
        HandleDebugTestAction(debugAction);

        if (keyboard.IsKeyDown(Keys.F10))
        {
            Exit();
            return;
        }

        PlayerCommand sampled = _input.Sample(
            _simulation.Tick + 1,
            _simulation.Player.Id,
            GraphicsDevice,
            _mouseCaptured,
            _simulation.Player.IsAiming,
            _simulation.Player.SelectedWeaponIndex,
            _simulation.Player.Weapons.Count);
        // Only controls that can activate a menu need a release gate. AimDownSights may be
        // logically latched by Toggle ADS, so including it here could suppress gameplay forever.
        PlayerButtons transitionButtons = PlayerButtons.Fire | PlayerButtons.Reload | PlayerButtons.Jump;
        if (_suppressGameplayInputUntilNeutral)
        {
            bool neutral = (sampled.Buttons & transitionButtons) == PlayerButtons.None;
            sampled = sampled with
            {
                Movement = CoreVector2.Zero,
                LookDelta = CoreVector2.Zero,
                Buttons = PlayerButtons.None,
                WeaponSlot = -1,
            };
            _suppressGameplayInputUntilNeutral = !neutral;
        }
        EnemyState? automaticTarget = _automaticCapture && _openingCaptureRendered && !_menu.IsOpen
            ? _simulation.Enemies.FirstOrDefault(enemy => !enemy.IsDead)
            : null;
        if (automaticTarget is not null)
        {
            CoreVector3 targetCenter = automaticTarget.Position +
                new CoreVector3(0f, automaticTarget.Definition.ColliderHeight * 0.35f, 0f);
            CoreVector3 aimDirection = CoreVector3.Normalize(targetCenter - _simulation.Player.Position);
            float targetYaw = MathF.Atan2(aimDirection.X, -aimDirection.Z);
            float targetPitch = MathF.Asin(Math.Clamp(aimDirection.Y, -1f, 1f));
            sampled = sampled with
            {
                Movement = new CoreVector2(
                    MathF.Sin((float)_presentationSeconds * 0.9f) * 0.7f,
                    _startingWaveIndex == RunDirector.EncounterCount ? 0.15f : 0.3f),
                LookDelta = new CoreVector2(
                    HudMath.WrapAngle(targetYaw - _simulation.Player.Yaw),
                    targetPitch - _simulation.Player.Pitch),
                Buttons = _lookFollowCaptureRendered
                    ? sampled.Buttons | PlayerButtons.Fire
                    : sampled.Buttons & ~PlayerButtons.Fire,
            };
        }

        if (_menu.IsOpen)
        {
            sampled = sampled with
            {
                Movement = CoreVector2.Zero,
                LookDelta = CoreVector2.Zero,
                Buttons = PlayerButtons.None,
                WeaponSlot = -1,
            };
        }

        _latestInput = sampled;
        _pendingLook += sampled.LookDelta;
        bool holdSimulationForCapture = _automaticCapture && _capture.HasPendingCapture;
        if (holdSimulationForCapture)
        {
            _accumulatorSeconds = 0d;
            _pendingLook = CoreVector2.Zero;
        }
        else
        {
            TimeSpan simulationElapsed = _automaticCapture && _capture.IsRecording
                ? TimeSpan.FromSeconds(1d / _capture.RecordingFramesPerSecond)
                : gameTime.ElapsedGameTime;
            _accumulatorSeconds = Math.Min(0.25, _accumulatorSeconds + simulationElapsed.TotalSeconds);
            int steps = (int)(_accumulatorSeconds / GameSimulation.FixedDeltaSeconds);
            if (steps > 0)
            {
                CoreVector2 lookPerStep = _pendingLook / steps;
                Span<PlayerCommand> commands = stackalloc PlayerCommand[1];
                for (int index = 0; index < steps; index++)
                {
                    PlayerCommand command = _latestInput with
                    {
                        Tick = _simulation.Tick + 1,
                        LookDelta = lookPerStep,
                    };
                    commands[0] = command;
                    _simulation.Step(commands);
                    _feedback?.Consume(_simulation.CombatEvents);
                    _audio?.Consume(
                        _simulation.CombatEvents,
                        _settings,
                        _simulation.Player.Position,
                        _simulation.GetViewDirection());
                    ProcessProgressionEvents(_simulation.CombatEvents);
                    _accumulatorSeconds -= GameSimulation.FixedDeltaSeconds;
                }

                bool profileChanged = false;
                if (!_automaticCapture && !_debugSandboxActive)
                {
                    foreach (WeaponState weapon in _simulation.Player.Weapons)
                    {
                        if (_profile.UnlockStartingWeapon(weapon.Definition.Id))
                        {
                            _newUnlockIds.Add(weapon.Definition.Id);
                            profileChanged = true;
                        }
                    }
                }

                if (profileChanged)
                {
                    ConfigureMenuProfile();
                    _profileStore.Save(_profile);
                }

                _pendingLook = CoreVector2.Zero;
            }
        }

        _interpolationAlpha = (float)(_accumulatorSeconds / GameSimulation.FixedDeltaSeconds);
        TimeSpan presentationElapsed = _automaticCapture && _capture.IsRecording
            ? TimeSpan.FromSeconds(1d / _capture.RecordingFramesPerSecond)
            : gameTime.ElapsedGameTime;
        _adsBlend = HudMath.InterpolateAds(
            _adsBlend,
            _simulation.Player.IsAiming,
            (float)presentationElapsed.TotalSeconds);
        float elapsedSeconds = (float)presentationElapsed.TotalSeconds;
        _presentationSeconds += presentationElapsed.TotalSeconds;
        string currentWeaponId = _simulation.Player.CurrentWeapon.Definition.Id;
        if (!string.Equals(_presentedWeaponId, currentWeaponId, StringComparison.OrdinalIgnoreCase))
        {
            _presentedWeaponId = currentWeaponId;
            _weaponPresentationSeconds = 0f;
        }
        else if (_simulation.Phase != GamePhase.Paused)
        {
            _weaponPresentationSeconds += elapsedSeconds;
        }

        if (!(_automaticCapture && _capture.HasPendingCapture))
        {
            _feedback?.Update(elapsedSeconds);
        }
        UpdateEnemyPresentation(presentationElapsed);
        _audio?.Update(elapsedSeconds, _settings);
        _audio?.SetMusicState(GetMusicState(), _settings);
        if (_simulation.RunPhase == RunPhase.RewardSelection && _menu.Page == MenuPage.None)
        {
            _menu.OpenReward(_simulation.PendingUpgradeOffers.Select(upgrade =>
                (upgrade.Id, upgrade.DisplayName, upgrade.Description)));
            _audio?.PlayInterfaceCue("upgrade", _settings);
            RefreshMouseCapture();
        }

        if (_simulation.Phase == GamePhase.Paused && !_menu.IsOpen && _runActive)
        {
            _menu.OpenPause();
            RefreshMouseCapture();
        }
        else if (_simulation.Phase is GamePhase.Victory or GamePhase.Defeat && _menu.Page != MenuPage.Results)
        {
            FinalizeRun();
            _runActive = false;
            _menu.OpenResults();
            RefreshMouseCapture();
        }

        if (_automaticCapture && !_capture.HasPendingCapture && !_openingCaptured &&
            _simulation.ElapsedRunSeconds >= 0.65f)
        {
            _capture.Queue(CaptureName("01-orbital-depot-opening"));
            _openingCaptured = true;
        }
        else if (_automaticCapture && !_capture.HasPendingCapture && automaticTarget is not null &&
            !_lookFollowCaptured)
        {
            _capture.Queue(CaptureName(_startingWaveIndex > 0 ? "02-boss-look-follow" : "02-look-follow"));
            _lookFollowCaptured = true;
        }
        else if (_automaticCapture && !_capture.HasPendingCapture && !_impactCaptured &&
            _simulation.LastShotSeconds < 0.04f &&
            _simulation.Enemies.Any(enemy => !enemy.IsDead))
        {
            string impactCaptureName = _startingWaveIndex > 0
                ? "03-boss-impact"
                : _simulation.Player.CurrentWeapon.Definition.ShotMode == ShotMode.Projectile
                    ? "03-projectile-muzzle"
                    : "03-shot-impact";
            _capture.Queue(CaptureName(impactCaptureName));
            _impactCaptured = true;
        }
        float combatCaptureDelay = _simulation.Player.CurrentWeapon.Definition.ShotMode == ShotMode.Projectile
            ? 0.08f
            : _startingWaveIndex > 0 ? 0.25f : 0.08f;
        if (_automaticCapture && !_capture.HasPendingCapture && _impactCaptureRendered && !_combatCaptured &&
            _simulation.LastShotSeconds >= combatCaptureDelay &&
            _simulation.Enemies.Any(enemy => !enemy.IsDead))
        {
            string combatCaptureName = _startingWaveIndex > 0
                ? "04-boss-combat"
                : _simulation.Player.CurrentWeapon.Definition.ShotMode == ShotMode.Projectile
                    ? "04-projectile-flight"
                    : "04-enemy-visibility";
            _capture.Queue(CaptureName(combatCaptureName));
            _combatCaptured = true;
            _nextAutomaticMenuSeconds = _presentationSeconds + 0.4d;
        }

        if (_automaticMenuCapture && _combatCaptured && _startingWaveIndex == 0)
        {
            if (_automaticMenuStage == 0 && _presentationSeconds >= _nextAutomaticMenuSeconds)
            {
                _simulation.SetPaused(true);
                _menu.OpenSettings();
                _capture.Queue(CaptureName("05-settings-menu"));
                _automaticMenuStage = 1;
                _nextAutomaticMenuSeconds = _presentationSeconds + 0.45d;
            }
            else if (_automaticMenuStage == 1 && _presentationSeconds >= _nextAutomaticMenuSeconds)
            {
                _menu.OpenAccessibility();
                _capture.Queue(CaptureName("06-accessibility-menu"));
                _automaticMenuStage = 2;
                _nextAutomaticMenuSeconds = _presentationSeconds + 0.25d;
            }
            else if (_automaticMenuStage == 2 && _presentationSeconds >= _nextAutomaticMenuSeconds)
            {
                RestartSimulation();
                _simulation.SetPaused(true);
                _runActive = false;
                _menu.OpenMain();
                _capture.Queue(CaptureName("07-main-menu"));
                _automaticMenuStage = 3;
                _nextAutomaticMenuSeconds = _presentationSeconds + 0.25d;
            }
        }

        bool automaticCaptureComplete = _automaticCapture && _combatCaptured &&
            ((!_automaticMenuCapture && _presentationSeconds >= _nextAutomaticMenuSeconds) ||
             (_automaticMenuCapture && _automaticMenuStage >= 3 &&
              _presentationSeconds >= _nextAutomaticMenuSeconds));
        if (automaticCaptureComplete && !_capture.IsRecording)
        {
            Exit();
            return;
        }

        RefreshMouseCapture();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_characterLab is not null)
        {
            DrawCharacterLab(gameTime);
            return;
        }

        if (_simulation is null || _primitives is null || _enemyModelPresenters.Count == 0 || _hud is null ||
            !_weaponModels.TryGetValue(_simulation.Player.CurrentWeapon.Definition.Id, out StaticModelPresenter? weaponModel))
        {
            GraphicsDevice.Clear(Color.Black);
            base.Draw(gameTime);
            return;
        }

        GraphicsDevice.Clear(new Color(
            _simulation.Arena.SkyColor.X,
            _simulation.Arena.SkyColor.Y,
            _simulation.Arena.SkyColor.Z));
        bool useMenuCamera = _menu.Page == MenuPage.Main && !_runActive;
        CoreVector3 cameraPositionCore = CoreVector3.Lerp(
            _simulation.Player.PreviousPosition,
            _simulation.Player.Position,
            _interpolationAlpha);
        Vector3 cameraPosition;
        Vector3 forward;
        if (useMenuCamera)
        {
            float menuAngle = (float)_presentationSeconds * 0.08f;
            Vector3 menuTarget = new(0f, 1.25f, 0f);
            cameraPosition = new Vector3(
                MathF.Sin(menuAngle) * 18f,
                5.8f + (MathF.Sin(menuAngle * 0.7f) * 0.35f),
                MathF.Cos(menuAngle) * 18f);
            forward = Vector3.Normalize(menuTarget - cameraPosition);
        }
        else
        {
            cameraPosition = cameraPositionCore.ToXna();
            forward = _simulation.GetViewDirection().ToXna();
        }

        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.Up));
        bool moving = CoreVector3.DistanceSquared(_simulation.Player.PreviousPosition, _simulation.Player.Position) > 0.00001f;
        float bob = moving && _simulation.Player.IsGrounded
            ? MathF.Sin(_simulation.ElapsedRunSeconds * 12f) * 0.025f * _settings.CameraBobScale
            : 0f;
        float weaponShake = MathF.Max(0f, 0.09f - _simulation.LastShotSeconds) *
            _simulation.Player.CurrentWeapon.Definition.ScreenShake * _settings.ScreenShakeScale;
        float damageShake = _simulation.PlayerDamageFlashSeconds * 0.08f * _settings.ScreenShakeScale;
        float shake = weaponShake + damageShake;
        if (!useMenuCamera)
        {
            cameraPosition += (Vector3.Up * (bob + (MathF.Sin(_simulation.ElapsedRunSeconds * 83f) * shake))) +
                (right * (MathF.Cos(_simulation.ElapsedRunSeconds * 67f) * shake));
        }
        Matrix view = Matrix.CreateLookAt(cameraPosition, cameraPosition + forward, Vector3.Up);
        float fieldOfView = MathHelper.ToRadians(MathHelper.Lerp(
            _simulation.Player.CurrentWeapon.Definition.HipFieldOfViewDegrees,
            _simulation.Player.CurrentWeapon.Definition.AdsFieldOfViewDegrees,
            _adsBlend) * _settings.FieldOfViewScale);
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(
            fieldOfView,
            GraphicsDevice.Viewport.AspectRatio,
            0.08f,
            100f);

        _primitives.Begin(view, projection, _simulation.Arena);
        DrawWorld(_simulation, _primitives, view, projection);
        GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);
        if (_runActive)
        {
            DrawViewModel(_simulation, weaponModel, projection);
        }
        _hud.Draw(
            _simulation,
            _settings,
            _menu,
            _audio?.Caption,
            _runActive,
            new DebugOverlayState(
                _debug.Enabled,
                _debug.ShowCollision,
                _debugSandboxActive,
                _debug.GodModeOverride));
        base.Draw(gameTime);
        string? capturedPath = _capture.CaptureIfRequested(GraphicsDevice);
        _capture.CaptureRecordingFrame(GraphicsDevice, gameTime.ElapsedGameTime);
        if (_automaticCapture && capturedPath is not null)
        {
            string capturedName = Path.GetFileNameWithoutExtension(capturedPath);
            _openingCaptureRendered |= capturedName.EndsWith("01-orbital-depot-opening", StringComparison.Ordinal);
            _lookFollowCaptureRendered |= capturedName.EndsWith("02-look-follow", StringComparison.Ordinal) ||
                capturedName.EndsWith("02-boss-look-follow", StringComparison.Ordinal);
            _impactCaptureRendered |= capturedName.EndsWith("03-shot-impact", StringComparison.Ordinal) ||
                capturedName.EndsWith("03-boss-impact", StringComparison.Ordinal) ||
                capturedName.EndsWith("03-projectile-muzzle", StringComparison.Ordinal);
        }
    }

    protected override void UnloadContent()
    {
        ApplyMouseCapture(false);
        _simulation?.Dispose();
        _audio?.Dispose();
        foreach (SkinnedModelPresenter presenter in _enemyModelPresenters.Values)
        {
            presenter.Dispose();
        }
        _characterLabRenderer?.Dispose();
        _hud?.Dispose();
        _primitives?.Dispose();
        base.UnloadContent();
    }

    protected override void OnActivated(EventArgs args)
    {
        base.OnActivated(args);
        RefreshMouseCapture();
    }

    protected override void OnDeactivated(EventArgs args)
    {
        if (_runActive && _simulation?.Phase == GamePhase.Playing)
        {
            _simulation.SetPaused(true);
            _menu.OpenPause();
            _pendingLook = CoreVector2.Zero;
        }

        ApplyMouseCapture(false);
        base.OnDeactivated(args);
    }

    private static ContentCatalog LoadCatalog()
    {
        using Stream pulse = TitleContainer.OpenStream("Content/Data/Weapons/pulse-sidearm.json");
        using Stream burst = TitleContainer.OpenStream("Content/Data/Weapons/burst-carbine.json");
        using Stream scatter = TitleContainer.OpenStream("Content/Data/Weapons/scatter-blaster.json");
        using Stream beam = TitleContainer.OpenStream("Content/Data/Weapons/beam-rifle.json");
        using Stream plasma = TitleContainer.OpenStream("Content/Data/Weapons/plasma-launcher.json");
        using Stream arc = TitleContainer.OpenStream("Content/Data/Weapons/arc-cannon.json");
        using Stream striker = TitleContainer.OpenStream("Content/Data/Enemies/robot-striker.json");
        using Stream interceptor = TitleContainer.OpenStream("Content/Data/Enemies/robot-interceptor.json");
        using Stream juggernaut = TitleContainer.OpenStream("Content/Data/Enemies/robot-juggernaut.json");
        using Stream wasp = TitleContainer.OpenStream("Content/Data/Enemies/robot-wasp.json");
        using Stream robotWarden = TitleContainer.OpenStream("Content/Data/Enemies/robot-warden.json");
        using Stream breachWalker = TitleContainer.OpenStream("Content/Data/Enemies/breach-walker.json");
        using Stream orbitalArena = TitleContainer.OpenStream("Content/Data/Arenas/orbital-depot.json");
        using Stream orbitalWaves = TitleContainer.OpenStream("Content/Data/Waves/orbital-depot-waves.json");
        return ContentCatalog.Load(
            [pulse, burst, scatter, beam, plasma, arc],
            [striker, interceptor, juggernaut, wasp, robotWarden, breachWalker],
            [orbitalArena],
            [orbitalWaves]);
    }

    private void UpdateCharacterLab()
    {
        if (_characterLab is null || _characterLabEnemy is null || _characterLabPresenter is null)
        {
            return;
        }

        if (_characterLab.IsComplete || Keyboard.GetState().IsKeyDown(Keys.F10))
        {
            Exit();
            return;
        }

        CharacterLabPose pose = _characterLab.CurrentPose;
        if (_characterLabInstance is null)
        {
            _characterLabInstance = _characterLabPresenter.CreateInstance();
        }

        if (_characterLabPresentedPose != pose)
        {
            _characterLabInstance.Play(ResolveCharacterLabBinding(_characterLabEnemy, pose));
            _characterLabPresentedPose = pose;
        }

        TimeSpan frameElapsed = TimeSpan.FromSeconds(1d / _characterLab.FramesPerSecond);
        _characterLabInstance.Update(frameElapsed, _characterLab.ShouldPauseAnimation);
        if (_characterLab.Mode == CharacterLabCaptureMode.Stills)
        {
            _characterLab.AdvanceStillAnimationFrame();
            if (_characterLab.TryBeginStillCapture(out string captureName))
            {
                _capture.Queue(captureName);
            }
        }
    }

    private static void WriteCharacterLabCalibrationDiagnostics(
        EnemyDefinition enemy,
        SkinnedModelPresenter presenter,
        Model model)
    {
        EnemyVisualCalibration calibration = presenter.Calibration;
        Console.WriteLine(
            $"CHARACTER_LAB_CALIBRATION enemy={enemy.Id} sourceHeight={calibration.SourceHeight:R} " +
            $"groundAnchor={calibration.SourceGroundAnchor} targetHeight={enemy.Visual.TargetHeight:R}");
        Matrix[] absoluteTransforms = new Matrix[model.Bones.Count];
        model.CopyAbsoluteBoneTransformsTo(absoluteTransforms);
        foreach (ModelMesh mesh in model.Meshes)
        {
            Console.WriteLine(
                $"CHARACTER_LAB_MESH name={mesh.Name} parentBone={mesh.ParentBone.Index} " +
                $"sphereCenter={mesh.BoundingSphere.Center} sphereRadius={mesh.BoundingSphere.Radius:R} " +
                $"parentAbsolute={absoluteTransforms[mesh.ParentBone.Index]}");
        }
    }

    private void DrawCharacterLab(GameTime gameTime)
    {
        if (_characterLab is null || _characterLabRenderer is null || _characterLabEnemy is null ||
            _characterLabPresenter is null || _characterLabInstance is null || _primitives is null ||
            _simulation is null)
        {
            GraphicsDevice.Clear(Color.Black);
            base.Draw(gameTime);
            return;
        }

        _characterLabRenderer.Draw(
            _primitives,
            _characterLabPresenter,
            _characterLabInstance,
            _characterLabEnemy,
            _characterLab,
            _simulation.Arena);
        base.Draw(gameTime);

        string? stillPath = _capture.CaptureIfRequested(GraphicsDevice);
        if (stillPath is not null && _characterLab.Mode == CharacterLabCaptureMode.Stills)
        {
            _characterLab.NotifyStillCaptured();
        }

        string? recordingPath = _capture.CaptureRecordingFrame(GraphicsDevice, gameTime.ElapsedGameTime);
        if (recordingPath is not null && _characterLab.Mode == CharacterLabCaptureMode.Reel)
        {
            _characterLab.NotifyRecordingFrameCaptured();
        }
    }

    private static KniAnimationClipBinding ResolveCharacterLabBinding(
        EnemyDefinition enemy,
        CharacterLabPose pose)
    {
        (string[] keys, KniAnimationLoopMode fallbackLoopMode) = pose switch
        {
            CharacterLabPose.Idle => (new[] { "idle" }, KniAnimationLoopMode.Loop),
            CharacterLabPose.Locomotion => (new[] { "locomotion", "walk" }, KniAnimationLoopMode.Loop),
            CharacterLabPose.Attack => (new[] { "activeAttack", "windup", "attack" }, KniAnimationLoopMode.Clamp),
            CharacterLabPose.Hit => (new[] { "hitReaction", "hit" }, KniAnimationLoopMode.Clamp),
            CharacterLabPose.Death => (new[] { "death" }, KniAnimationLoopMode.Clamp),
            _ => throw new InvalidOperationException("Unsupported Character Lab pose."),
        };

        foreach (string key in keys)
        {
            FpsFrenzy.Core.Data.AnimationClipBinding? authored = FindBinding(
                enemy.Visual.AnimationBindings,
                key);
            if (authored is null)
            {
                continue;
            }

            KniAnimationLoopMode loopMode = authored.LoopMode == EnemyAnimationLoopMode.Loop
                ? KniAnimationLoopMode.Loop
                : KniAnimationLoopMode.Clamp;
            return new KniAnimationClipBinding(
                authored.ClipName,
                loopMode,
                authored.PlaybackRate,
                authored.TransitionSeconds,
                authored.StartNormalized,
                authored.EndNormalized);
        }

        foreach (string key in keys)
        {
            string? clipName = FindClip(enemy.AnimationClips, key);
            if (clipName is not null)
            {
                return new KniAnimationClipBinding(clipName, fallbackLoopMode);
            }
        }

        throw new InvalidDataException(
            $"Character Lab enemy '{enemy.Id}' has no clip for pose '{pose}'.");
    }

    private void DrawWorld(
        GameSimulation simulation,
        PrimitiveRenderer primitives,
        Matrix view,
        Matrix projection)
    {
        foreach (ArenaPrimitiveDefinition primitive in simulation.Arena.Primitives)
        {
            if (!primitive.IsVisible)
            {
                continue;
            }

            Texture2D? texture = primitive.TextureAsset is not null &&
                _arenaTextures.TryGetValue(primitive.TextureAsset, out Texture2D? loadedTexture)
                    ? loadedTexture
                    : null;
            primitives.Draw(primitive, texture);
        }
        DrawArenaModels(simulation.Arena, view, projection);
        DrawSectorPresentation(simulation, primitives);
        if (_debug.Enabled && _debug.ShowCollision)
        {
            DrawCollisionOverlay(simulation, primitives);
        }

        foreach (EnemyState enemy in simulation.Enemies)
        {
            CoreVector3 positionCore = CoreVector3.Lerp(enemy.PreviousPosition, enemy.Position, _interpolationAlpha);
            Vector3 position = positionCore.ToXna();
            if (!_enemyModelPresenters.TryGetValue(enemy.Definition.ModelAsset, out SkinnedModelPresenter? enemyModel))
            {
                continue;
            }

            if (!_enemyModels.TryGetValue(enemy.Id, out EnemyModelInstance? instance))
            {
                instance = enemyModel.CreateInstance();
                _enemyModels.Add(enemy.Id, instance);
            }

            CoreVector3 tintCore = enemy.Definition.IsBoss
                ? enemy.Definition.BossPhases[enemy.CurrentBossPhaseIndex].Tint
                : enemy.Definition.Visual.EmissiveAccent == CoreVector3.Zero
                    ? enemy.Definition.Tint
                    : enemy.Definition.Visual.EmissiveAccent;
            Vector3 emissiveAccent = tintCore.ToXna();

            float scale = enemy.Definition.RenderScale;
            float modelHeight = enemy.Definition.Visual.TargetHeight;
            Vector3 visualPosition = position + new Vector3(
                0f,
                enemy.Definition.Visual.GroundOffset + enemy.Definition.Visual.HoverOffset,
                0f);
            float visualYaw = enemy.FacingYaw + MathHelper.ToRadians(enemy.Definition.Visual.ForwardYawDegrees);
            float hitFlash = Math.Clamp(enemy.HitFlashSeconds / 0.12f, 0f, 1f);
            enemyModel.Draw(instance, visualPosition, modelHeight, visualYaw,
                emissiveAccent, hitFlash, view, projection, simulation.Arena);
            if (!enemy.IsDead)
            {
                Color visibilityColor = enemy.IsElite
                    ? new Color(255, 92, 185)
                    : new Color(emissiveAccent.X, emissiveAccent.Y, emissiveAccent.Z);
                Vector3 ground = position + new Vector3(0f, 0.08f, 0f);
                float groundRadius = MathF.Max(0.32f, enemy.Definition.ColliderRadius * 0.8f);
                float markerSegment = groundRadius * 0.72f;
                primitives.DrawBeam(ground + new Vector3(-groundRadius, 0f, -groundRadius),
                    ground + new Vector3(-groundRadius + markerSegment, 0f, -groundRadius), 0.018f, visibilityColor);
                primitives.DrawBeam(ground + new Vector3(groundRadius - markerSegment, 0f, groundRadius),
                    ground + new Vector3(groundRadius, 0f, groundRadius), 0.018f, visibilityColor);
                Vector3 healthAnchor = visualPosition + Vector3.Transform(
                    enemy.Definition.Visual.HealthBarAnchor.ToXna(),
                    Matrix.CreateRotationY(enemy.FacingYaw));
                if (enemy.IsElite)
                {
                    float pulse = 0.82f + (MathF.Sin((float)GameTimeSeconds(simulation) * 5f) * 0.12f);
                    Vector3 crownCenter = healthAnchor + new Vector3(0f, 0.46f, 0f);
                    float crownRadius = 0.28f * pulse;
                    primitives.DrawBeam(crownCenter + new Vector3(-crownRadius, 0f, 0f),
                        crownCenter + new Vector3(0f, crownRadius, 0f), 0.035f, visibilityColor);
                    primitives.DrawBeam(crownCenter + new Vector3(0f, crownRadius, 0f),
                        crownCenter + new Vector3(crownRadius, 0f, 0f), 0.035f, visibilityColor);
                    primitives.DrawBeam(crownCenter + new Vector3(crownRadius, 0f, 0f),
                        crownCenter + new Vector3(0f, -crownRadius, 0f), 0.035f, visibilityColor);
                    primitives.DrawBeam(crownCenter + new Vector3(0f, -crownRadius, 0f),
                        crownCenter + new Vector3(-crownRadius, 0f, 0f), 0.035f, visibilityColor);
                }
                primitives.DrawCube(healthAnchor + new Vector3(0f, 0.16f, 0f),
                    new Vector3(enemy.Definition.IsBoss ? 0.14f : 0.085f), visibilityColor,
                    (float)GameTimeSeconds(simulation) + enemy.Id.Value, emissive: true);

                float healthFraction = Math.Clamp(enemy.Health / enemy.MaximumHealth, 0f, 1f);
                Vector3 cameraRight = Vector3.Normalize(new Vector3(view.M11, view.M21, view.M31));
                float barWidth = enemy.Definition.IsBoss ? 1.25f : 0.84f * scale;
                Vector3 barStart = healthAnchor - (cameraRight * barWidth * 0.5f);
                primitives.DrawBeam(barStart, barStart + (cameraRight * barWidth),
                    enemy.Definition.IsBoss ? 0.075f : 0.048f, new Color(18, 24, 34));
                primitives.DrawBeam(barStart,
                    barStart + (cameraRight * barWidth * healthFraction),
                    enemy.Definition.IsBoss ? 0.055f : 0.032f,
                    visibilityColor);
            }
        }

        _inactiveEnemyModels.Clear();
        foreach (EntityId enemyId in _enemyModels.Keys)
        {
            if (!ContainsEnemy(simulation.Enemies, enemyId))
            {
                _inactiveEnemyModels.Add(enemyId);
            }
        }

        foreach (EntityId enemyId in _inactiveEnemyModels)
        {
            _enemyModels.Remove(enemyId);
        }

        foreach (PickupState pickup in simulation.Pickups)
        {
            if (!pickup.IsAvailable)
            {
                continue;
            }

            Color color = pickup.Type switch
            {
                PickupType.Health => new Color(50, 240, 145),
                PickupType.Ammo => new Color(70, 190, 255),
                PickupType.Weapon when pickup.WeaponId is not null && _catalog is not null &&
                    _catalog.Weapons.TryGetValue(pickup.WeaponId, out WeaponDefinition? weapon) =>
                    new Color(weapon.ImpactColor.X, weapon.ImpactColor.Y, weapon.ImpactColor.Z),
                _ => Color.White,
            };
            float bob = MathF.Sin((float)GameTimeSeconds(simulation) * 3f + pickup.Id.Value) * 0.12f;
            Vector3 pickupPosition = pickup.Position.ToXna() + new Vector3(0f, bob + 0.18f, 0f);
            float spin = ((float)GameTimeSeconds(simulation) * 0.8f) + (pickup.Id.Value * 0.35f);
            Vector3 tint = Vector3.Lerp(Vector3.One, color.ToVector3(), 0.28f);
            Vector3 stationPosition = new(pickup.Position.X, 0.06f, pickup.Position.Z);
            string stationModelId = pickup.Type == PickupType.Weapon ? "pedestal" : "container";
            if (_pickupModels.TryGetValue(stationModelId, out StaticModelPresenter? stationModel))
            {
                stationModel.Draw(stationPosition, pickup.Type == PickupType.Weapon ? 1.35f : 1.1f,
                    0f, 0f, view, projection, Vector3.Lerp(Vector3.One, color.ToVector3(), 0.18f),
                    color.ToVector3() * 0.045f);
            }

            if (_pickupModels.TryGetValue("ring", out StaticModelPresenter? ringModel))
            {
                ringModel.Draw(pickupPosition + new Vector3(0f, 0.46f, 0f),
                    pickup.Type == PickupType.Weapon ? 1.12f : 0.88f,
                    spin * 1.35f, 0f, view, projection, tint, color.ToVector3() * 0.12f);
            }

            if (pickup.Type == PickupType.Weapon && pickup.WeaponId is not null &&
                _weaponModels.TryGetValue(pickup.WeaponId, out StaticModelPresenter? pickupWeapon))
            {
                pickupWeapon.Draw(pickupPosition, 0.92f, spin, -0.12f, view, projection, tint, color.ToVector3() * 0.12f);
            }
            else if (pickup.Type == PickupType.Health && _pickupModels.TryGetValue("health", out StaticModelPresenter? healthModel))
            {
                healthModel.Draw(pickupPosition, 0.72f, spin, 0f, view, projection, tint, color.ToVector3() * 0.1f);
                primitives.DrawCube(pickupPosition + new Vector3(0f, 0.62f, 0f),
                    new Vector3(0.12f, 0.34f, 0.08f), color, emissive: true);
                primitives.DrawCube(pickupPosition + new Vector3(0f, 0.62f, 0f),
                    new Vector3(0.34f, 0.12f, 0.08f), color, emissive: true);
            }
            else if (pickup.Type == PickupType.Ammo && _pickupModels.TryGetValue("ammo", out StaticModelPresenter? ammoModel))
            {
                ammoModel.Draw(pickupPosition, 0.76f, spin, 0f, view, projection, tint, color.ToVector3() * 0.1f);
                for (int band = -1; band <= 1; band++)
                {
                    primitives.DrawCube(pickupPosition + new Vector3(band * 0.18f, 0.58f, 0f),
                        new Vector3(0.08f, 0.26f, 0.08f), color, emissive: true);
                }
            }

            float beaconRadius = pickup.Type == PickupType.Weapon ? 0.72f : 0.52f;
            primitives.DrawBeam(pickup.Position.ToXna() + new Vector3(-beaconRadius, 0.05f, 0f),
                pickup.Position.ToXna() + new Vector3(beaconRadius, 0.05f, 0f), 0.025f, color);
            primitives.DrawBeam(pickup.Position.ToXna() + new Vector3(0f, 0.05f, -beaconRadius),
                pickup.Position.ToXna() + new Vector3(0f, 0.05f, beaconRadius), 0.025f, color);
        }

        foreach (ProjectileState projectile in simulation.Projectiles)
        {
            CoreVector3 positionCore = CoreVector3.Lerp(
                projectile.PreviousPosition, projectile.Position, _interpolationAlpha);
            Vector3 position = positionCore.ToXna();
            Color color = new(projectile.Color.X, projectile.Color.Y, projectile.Color.Z);
            float cameraDistance = Vector3.Distance(position, simulation.Player.Position.ToXna());
            float nearCameraScale = Math.Clamp(cameraDistance / 3f, 0.18f, 1f);
            float radius = Math.Clamp(projectile.Radius * 0.9f, 0.12f, 0.28f) * nearCameraScale;
            float thickness = Math.Clamp(radius * 0.22f, 0.01f, 0.055f);
            Vector3 velocity = projectile.Velocity.ToXna();
            Vector3 direction = velocity.LengthSquared() > 0.0001f ? Vector3.Normalize(velocity) : Vector3.Forward;
            float trailLength = Math.Clamp(velocity.Length() * 0.04f, 0.55f, 1.45f) * nearCameraScale;
            trailLength = MathF.Min(trailLength, MathF.Max(0.06f, cameraDistance - 0.4f));
            primitives.DrawBeam(position - (direction * trailLength), position, thickness * 0.72f, color);
            primitives.DrawBeam(position - (Vector3.Right * radius), position + (Vector3.Right * radius), thickness, color);
            primitives.DrawBeam(position - (Vector3.Up * radius), position + (Vector3.Up * radius), thickness, color);
            primitives.DrawBeam(position - (Vector3.Forward * radius), position + (Vector3.Forward * radius), thickness, color);
            float coreSize = Math.Clamp(projectile.Radius * 0.9f, 0.16f, 0.28f) * nearCameraScale;
            primitives.DrawCube(position, new Vector3(coreSize), Color.White, emissive: true);
        }

        _feedback?.Draw(primitives);
    }

    private void DrawViewModel(
        GameSimulation simulation,
        StaticModelPresenter weaponModel,
        Matrix projection)
    {
        CoreVector3 offsetCore = CoreVector3.Lerp(
            simulation.Player.CurrentWeapon.Definition.ViewModelHipOffset,
            simulation.Player.CurrentWeapon.Definition.ViewModelAdsOffset,
            _adsBlend);
        float recoil = MathF.Max(0f, 0.11f - simulation.LastShotSeconds) * 0.42f *
            simulation.Player.CurrentWeapon.Definition.RecoilKick;
        WeaponState weaponState = simulation.Player.CurrentWeapon;
        float equipBlend = MathHelper.SmoothStep(0f, 1f, Math.Clamp(_weaponPresentationSeconds / 0.22f, 0f, 1f));
        float equipDrop = (1f - equipBlend) * 0.24f;
        float equipYaw = (1f - equipBlend) * 0.28f;
        float reloadProgress = weaponState.IsReloading && weaponState.Definition.ReloadSeconds > 0f
            ? 1f - Math.Clamp(weaponState.ReloadRemainingSeconds / weaponState.Definition.ReloadSeconds, 0f, 1f)
            : 0f;
        float reloadArc = weaponState.IsReloading ? MathF.Sin(reloadProgress * MathF.PI) : 0f;
        float ventShake = weaponState.IsOverheated
            ? MathF.Sin(simulation.ElapsedRunSeconds * 42f) * 0.006f
            : 0f;
        bool moving = CoreVector3.DistanceSquared(
            simulation.Player.PreviousPosition, simulation.Player.Position) > 0.00001f;
        float moveBlend = moving && simulation.Player.IsGrounded ? 1f : 0f;
        float stride = simulation.ElapsedRunSeconds * 11f;
        float horizontalBob = MathF.Sin(stride) * 0.012f * moveBlend * _settings.CameraBobScale;
        float verticalBob = -MathF.Abs(MathF.Cos(stride)) * 0.009f * moveBlend * _settings.CameraBobScale;
        float idleSway = MathF.Sin(simulation.ElapsedRunSeconds * 2.1f) * 0.0035f * _settings.CameraBobScale;
        Vector3 cameraSpacePosition = new(
            offsetCore.X + horizontalBob + idleSway + ventShake,
            offsetCore.Y - recoil + verticalBob - equipDrop - (reloadArc * 0.13f),
            offsetCore.Z + (reloadArc * 0.06f));
        float viewModelScale = simulation.Player.CurrentWeapon.Definition.Id switch
        {
            "arc-cannon" => 0.34f,
            "scatter-blaster" => 0.36f,
            "plasma-launcher" or "beam-rifle" => 0.42f,
            "burst-carbine" => 0.44f,
            _ => 0.56f,
        };
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        weaponModel.Draw(
            cameraSpacePosition,
            viewModelScale,
            MathF.PI + equipYaw + (reloadArc * 0.24f),
            reloadArc * -0.18f,
            Matrix.Identity,
            projection);
    }

    private void UpdateEnemyPresentation(TimeSpan elapsed)
    {
        if (_simulation is null)
        {
            return;
        }

        bool paused = _simulation.Phase == GamePhase.Paused ||
            _simulation.RunPhase == RunPhase.RewardSelection;
        foreach (EnemyState enemy in _simulation.Enemies)
        {
            if (!_enemyModelPresenters.TryGetValue(enemy.Definition.ModelAsset, out SkinnedModelPresenter? presenter))
            {
                continue;
            }

            if (!_enemyModels.TryGetValue(enemy.Id, out EnemyModelInstance? instance))
            {
                instance = presenter.CreateInstance();
                _enemyModels.Add(enemy.Id, instance);
            }

            float horizontalSpeed = EnemyPresentationMath.MeasureHorizontalSpeed(
                enemy.PreviousPosition,
                enemy.Position,
                GameSimulation.FixedDeltaSeconds);
            instance.IsLocomoting = EnemyPresentationMath.UpdateLocomotionLatch(
                instance.IsLocomoting,
                enemy.ActionState,
                horizontalSpeed);
            EnemyActionState presentationState = enemy.ActionState is
                EnemyActionState.Locomotion or EnemyActionState.Navigating or EnemyActionState.Charging
                    ? instance.IsLocomoting ? enemy.ActionState : EnemyActionState.Idle
                    : enemy.ActionState;
            KniAnimationClipBinding binding = ResolveAnimationBinding(enemy, presentationState);
            float playbackScale = instance.IsLocomoting
                ? EnemyPresentationMath.GetLocomotionPlaybackScale(
                    horizontalSpeed,
                    EnemyPresentationMath.GetAuthoredLocomotionSpeed(
                        presentationState,
                        enemy.Definition.MoveSpeed,
                        enemy.Definition.ChargeSpeed))
                : 1f;
            instance.Play(binding, playbackScale);
            instance.Update(elapsed, paused);
        }
    }

    private static KniAnimationClipBinding ResolveAnimationBinding(
        EnemyState enemy,
        EnemyActionState presentationState)
    {
        (string[] keys, KniAnimationLoopMode defaultLoopMode) = presentationState switch
        {
            EnemyActionState.Death => (new[] { "death" }, KniAnimationLoopMode.Clamp),
            EnemyActionState.HitReaction => (new[] { "hitReaction", "hit" }, KniAnimationLoopMode.Clamp),
            EnemyActionState.Windup => (new[] { "windup", "attack" }, KniAnimationLoopMode.Clamp),
            EnemyActionState.ActiveAttack => (new[] { "activeAttack", "attack" }, KniAnimationLoopMode.Clamp),
            EnemyActionState.Recovering => (new[] { "recovery", "attack" }, KniAnimationLoopMode.Clamp),
            EnemyActionState.Charging =>
                (new[] { "charge", "run", "locomotion", "walk" }, KniAnimationLoopMode.Loop),
            EnemyActionState.Locomotion or EnemyActionState.Navigating =>
                (new[] { "locomotion", "walk" }, KniAnimationLoopMode.Loop),
            _ when enemy.HitFlashSeconds > 0f => (new[] { "hitReaction", "hit" }, KniAnimationLoopMode.Clamp),
            _ => (new[] { "idle" }, KniAnimationLoopMode.Loop),
        };

        foreach (string key in keys)
        {
            FpsFrenzy.Core.Data.AnimationClipBinding? authored = FindBinding(
                enemy.Definition.Visual.AnimationBindings, key);
            if (authored is not null)
            {
                KniAnimationLoopMode loopMode = authored.LoopMode == EnemyAnimationLoopMode.Loop
                    ? KniAnimationLoopMode.Loop
                    : KniAnimationLoopMode.Clamp;
                return new KniAnimationClipBinding(
                    authored.ClipName,
                    loopMode,
                    authored.PlaybackRate,
                    authored.TransitionSeconds,
                    authored.StartNormalized,
                    authored.EndNormalized);
            }
        }

        foreach (string key in keys)
        {
            string? clipName = FindClip(enemy.Definition.AnimationClips, key);
            if (clipName is not null)
            {
                return new KniAnimationClipBinding(clipName, defaultLoopMode);
            }
        }

        throw new InvalidDataException(
            $"Enemy '{enemy.Definition.Id}' has no animation binding for state '{presentationState}'.");
    }

    private static FpsFrenzy.Core.Data.AnimationClipBinding? FindBinding(
        Dictionary<string, FpsFrenzy.Core.Data.AnimationClipBinding> bindings,
        string key)
    {
        if (bindings.TryGetValue(key, out FpsFrenzy.Core.Data.AnimationClipBinding? binding))
        {
            return binding;
        }

        return bindings.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static string? FindClip(Dictionary<string, string> clips, string key)
    {
        if (clips.TryGetValue(key, out string? clipName))
        {
            return clipName;
        }

        return clips.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private void StartNewRun()
    {
        bool isFirstRun = _profile.RunsStarted == 0;
        _debugSandboxActive = _debug.Enabled;
        if (!_debugSandboxActive)
        {
            AbandonCheckpoint();
        }

        _newUnlockIds.Clear();
        _runUnlockBaselineIds.Clear();
        _runUnlockBaselineIds.UnionWith(EnumerateProfileUnlockIds());
        _resultRecorded = false;
        if (!_debugSandboxActive)
        {
            _profile.RunsStarted++;
            _profileStore.Save(_profile);
        }

        RestartSimulation(isFirstRun: isFirstRun);
        _simulation?.SetPlayerInvulnerable(_settings.GodMode || _debug.GodModeOverride);
        _runActive = true;
        _menu.Close();
        _suppressGameplayInputUntilNeutral = true;
    }

    private void ContinueRun()
    {
        RunCheckpoint? checkpoint = _checkpointStore.Load();
        if (checkpoint is null)
        {
            StartNewRun();
            return;
        }

        checkpoint = SanitizeReleaseCheckpoint(
            _catalog!,
            checkpoint,
            _profile.SelectedStartingWeaponId);
        _debugSandboxActive = _debug.Enabled;

        _newUnlockIds.Clear();
        _runUnlockBaselineIds.Clear();
        _runUnlockBaselineIds.UnionWith(checkpoint.ProfileUnlockBaselineIds);
        if (_runUnlockBaselineIds.Count == 0)
        {
            // Compatibility for checkpoints written before the run baseline was recorded.
            // Saved run unlocks are removed to reconstruct the original profile snapshot.
            _runUnlockBaselineIds.UnionWith(EnumerateProfileUnlockIds());
            _runUnlockBaselineIds.ExceptWith(checkpoint.NewlyUnlockedIds);
        }
        foreach (string unlockId in checkpoint.NewlyUnlockedIds)
        {
            if (!string.IsNullOrWhiteSpace(unlockId))
            {
                _newUnlockIds.Add(unlockId);
            }
        }
        foreach (string unlockId in EnumerateProfileUnlockIds())
        {
            if (!_runUnlockBaselineIds.Contains(unlockId))
            {
                _newUnlockIds.Add(unlockId);
            }
        }
        _resultRecorded = false;
        _startingWeaponId = ResolveStartingWeaponId(
            _catalog!,
            checkpoint.StartingWeaponId,
            _profile.SelectedStartingWeaponId);
        RestartSimulation(checkpoint);
        _simulation?.SetPlayerInvulnerable(_settings.GodMode || _debug.GodModeOverride);
        _availableCheckpoint = checkpoint;
        _runActive = true;
        _menu.Close();
        _suppressGameplayInputUntilNeutral = true;
    }

    private void HandleDebugTestAction(DebugTestAction action)
    {
        if (_simulation is null || action == DebugTestAction.None)
        {
            return;
        }

        if (action.HasFlag(DebugTestAction.ModeChanged))
        {
            if (_debug.Enabled && _runActive)
            {
                _debugSandboxActive = true;
            }

            _simulation.SetPlayerInvulnerable(_settings.GodMode || _debug.GodModeOverride);
        }

        if (action.HasFlag(DebugTestAction.GodModeChanged))
        {
            _debugSandboxActive = true;
            _simulation.SetPlayerInvulnerable(_settings.GodMode || _debug.GodModeOverride);
        }

        int stage = GetCurrentDebugStage();
        if (action.HasFlag(DebugTestAction.RestartStage))
        {
            StartDebugStage(stage);
        }
        else if (action.HasFlag(DebugTestAction.PreviousStage))
        {
            StartDebugStage(Math.Max(0, stage - 1));
        }
        else if (action.HasFlag(DebugTestAction.NextStage))
        {
            StartDebugStage(Math.Min(RunDirector.EncounterCount, stage + 1));
        }

        if (!action.HasFlag(DebugTestAction.CompleteStage))
        {
            return;
        }

        _debugSandboxActive = true;
        if (_simulation.RunPhase == RunPhase.RewardSelection &&
            _simulation.PendingUpgradeOffers.Count > 0)
        {
            UpgradeDefinition upgrade = _simulation.PendingUpgradeOffers[0];
            _simulation.ChooseUpgrade(upgrade.Id);
            _menu.Close();
            _suppressGameplayInputUntilNeutral = true;
        }
        else
        {
            _simulation.DebugCompleteCurrentStage();
        }
    }

    private int GetCurrentDebugStage()
    {
        if (_simulation?.RunPhase == RunPhase.BossActive)
        {
            return RunDirector.EncounterCount;
        }

        return Math.Clamp(
            _simulation?.RunSnapshot?.EncounterIndex ?? 0,
            0,
            RunDirector.EncounterCount);
    }

    private void StartDebugStage(int stage)
    {
        if (_simulation is null || _catalog is null)
        {
            return;
        }

        _debugSandboxActive = true;
        int clampedStage = Math.Clamp(stage, 0, RunDirector.EncounterCount);
        RunCheckpoint checkpoint = new()
        {
            Seed = _simulation.RunSeed,
            ArenaId = "orbital-depot",
            NextEncounterIndex = clampedStage,
            StartingWeaponId = _startingWeaponId,
            GodModeUsed = _settings.GodMode || _debug.GodModeOverride,
            OwnedUpgradeIds = _simulation.OwnedUpgradeIds
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CollectedWeaponIds = _catalog.Weapons.Keys
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SelectedWeaponId = _simulation.SelectedWeaponId,
            PlayerHealth = _simulation.Player.MaximumHealth,
            PlayerMaximumHealth = _simulation.Player.MaximumHealth,
        };
        _resultRecorded = false;
        RestartSimulation(checkpoint);
        _simulation.SetPlayerInvulnerable(_settings.GodMode || _debug.GodModeOverride);
        _runActive = true;
        _menu.Close();
        _suppressGameplayInputUntilNeutral = true;
    }

    private void RestartSimulation(
        RunCheckpoint? checkpoint = null,
        bool isFirstRun = false,
        int? seed = null)
    {
        if (_catalog is null)
        {
            return;
        }

        _simulation?.Dispose();
        _simulation = CreateSimulation(checkpoint, isFirstRun, seed);
        _enemyModels.Clear();
        _feedback = new CombatFeedbackPresenter(_catalog);
        _pendingLook = CoreVector2.Zero;
        _accumulatorSeconds = 0d;
        _interpolationAlpha = 0f;
        _adsBlend = 0f;
        _presentedWeaponId = null;
        _weaponPresentationSeconds = 0f;
    }

    private GameSimulation CreateSimulation(RunCheckpoint? checkpoint, bool isFirstRun, int? seed = null)
    {
        if (_catalog is null)
        {
            throw new InvalidOperationException("Content must be loaded before creating a run.");
        }

        RunCheckpoint? releaseCheckpoint = checkpoint is null
            ? null
            : SanitizeReleaseCheckpoint(_catalog, checkpoint, _startingWeaponId);
        int runSeed = releaseCheckpoint?.Seed ?? seed ?? GetNewRunSeed();
        return new GameSimulation(_catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Seed = runSeed,
            Difficulty = DifficultyMode.Standard,
            StartingWeaponId = ResolveStartingWeaponId(
                _catalog,
                releaseCheckpoint?.StartingWeaponId,
                _startingWeaponId),
            GodModeEnabled = _settings.GodMode,
            IsFirstRun = isFirstRun,
            UnlockedUpgradeIds = _profile.UnlockedUpgradeIds,
            Checkpoint = releaseCheckpoint,
        });
    }

    private RunCheckpoint CreateCaptureCheckpoint(int startingEncounter) => new()
    {
        Seed = GetNewRunSeed(),
        ArenaId = "orbital-depot",
        NextEncounterIndex = Math.Clamp(startingEncounter, 0, RunDirector.EncounterCount),
        StartingWeaponId = _startingWeaponId,
        GodModeUsed = _settings.GodMode,
        IsFirstRun = true,
        CollectedWeaponIds = [_startingWeaponId],
        SelectedWeaponId = _startingWeaponId,
    };

    private int GetNewRunSeed()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("FPS_FRENZY_RUN_SEED"), out int configuredSeed))
        {
            return configuredSeed;
        }

        return _automaticCapture ? 1337 : Random.Shared.Next(1, int.MaxValue);
    }

    internal static string ResolveStartingWeaponId(
        ContentCatalog catalog,
        string? requestedWeaponId,
        string? fallbackWeaponId = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (!string.IsNullOrWhiteSpace(requestedWeaponId) &&
            catalog.Weapons.ContainsKey(requestedWeaponId))
        {
            return requestedWeaponId;
        }

        if (!string.IsNullOrWhiteSpace(fallbackWeaponId) &&
            catalog.Weapons.ContainsKey(fallbackWeaponId))
        {
            return fallbackWeaponId;
        }

        if (catalog.Weapons.ContainsKey("pulse-sidearm"))
        {
            return "pulse-sidearm";
        }

        return catalog.Weapons.Keys.Order(StringComparer.Ordinal).FirstOrDefault() ??
            throw new InvalidDataException("The content catalog does not define any weapons.");
    }

    internal static RunCheckpoint SanitizeReleaseCheckpoint(
        ContentCatalog catalog,
        RunCheckpoint checkpoint,
        string? fallbackWeaponId = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return checkpoint with
        {
            ArenaId = "orbital-depot",
            StartingWeaponId = ResolveStartingWeaponId(
                catalog,
                checkpoint.StartingWeaponId,
                fallbackWeaponId),
        };
    }

    private void SaveCheckpoint()
    {
        if (_automaticCapture || _debugSandboxActive)
        {
            return;
        }

        RunCheckpoint? checkpoint = _simulation?.CreateRunCheckpoint();
        if (checkpoint is null)
        {
            return;
        }

        checkpoint = checkpoint with
        {
            NewlyUnlockedIds = _newUnlockIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            ProfileUnlockBaselineIds = _runUnlockBaselineIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
        };
        if (_checkpointStore.Save(checkpoint))
        {
            _availableCheckpoint = checkpoint;
            ConfigureMenuProfile();
        }
    }

    private void AbandonCheckpoint()
    {
        _checkpointStore.Clear();
        _availableCheckpoint = null;
        if (_catalog is not null)
        {
            ConfigureMenuProfile();
        }
    }

    private void ConfigureMenuProfile()
    {
        if (_catalog is null)
        {
            return;
        }

        _menu.ConfigureProfile(
            _profile,
            _catalog.Weapons.Values
                .OrderBy(WeaponMenuOrder)
                .Select(weapon => (weapon.Id, weapon.DisplayName)),
            _availableCheckpoint is not null);
    }

    private void ProcessProgressionEvents(IReadOnlyList<CombatEvent> events)
    {
        if (_simulation is null || _automaticCapture || _debugSandboxActive)
        {
            return;
        }

        bool changed = false;
        foreach (CombatEvent combatEvent in events)
        {
            if (combatEvent.Type == CombatEventType.EnemyKilled && combatEvent.SourceId == _simulation.Player.Id)
            {
                // Encounter completion can move the player to the recovery hub later in this
                // same simulation step. Use the range captured at the lethal impact instead of
                // measuring against the post-step player position.
                float distance = combatEvent.RangeMeters;
                if (distance <= 8f)
                {
                    _profile.CloseRangeKills++;
                    changed = true;
                }

                if (distance >= 18f)
                {
                    _profile.LongRangeKills++;
                    changed = true;
                }

                if (_profile.CloseRangeKills >= 50)
                {
                    changed |= UnlockUpgrade("close-range-kills-50", "close-quarters");
                }

                if (_profile.LongRangeKills >= 25)
                {
                    changed |= UnlockUpgrade("long-range-kills-25", "longshot");
                }
            }
            else if (combatEvent.Type == CombatEventType.EncounterCompleted)
            {
                if (_simulation.SectorsCompleted >= 1)
                {
                    changed |= UnlockUpgrade("complete-sector-1", "accelerated-cycler");
                }

                if (_simulation.LastCompletedEncounterObjective == EncounterObjectiveType.RelayDefense &&
                    combatEvent.Value > 0.5f)
                {
                    changed |= UnlockUpgrade("relay-above-half", "phase-stabilizer");
                }

                if (_simulation.LastCompletedEncounterObjective == EncounterObjectiveType.EliteHunt &&
                    combatEvent.Value <= 60f)
                {
                    changed |= UnlockUpgrade("elite-under-60-seconds", "adrenal-circuit");
                }
            }
        }

        if (changed)
        {
            _profileStore.Save(_profile);
        }
    }

    private bool UnlockUpgrade(string challengeId, string upgradeId)
    {
        bool unlocked = _profile.CompleteChallenge(challengeId, upgradeId);
        if (unlocked)
        {
            _newUnlockIds.Add(upgradeId);
        }

        return unlocked;
    }

    private IEnumerable<string> EnumerateProfileUnlockIds() =>
        _profile.UnlockedUpgradeIds.Concat(_profile.UnlockedStartingWeaponIds);

    private void FinalizeRun()
    {
        if (_simulation is null || _resultRecorded)
        {
            return;
        }

        if (_debugSandboxActive)
        {
            ConfigureMenuProfile();
            _resultRecorded = true;
            return;
        }

        if (_simulation.Phase == GamePhase.Defeat)
        {
            UnlockUpgrade("first-defeat", "emergency-barrier");
        }

        bool godModeUsed = _simulation.RunSnapshot?.GodModeUsed ?? _simulation.GodModeEnabled;
        _profile.RecordRun(new RunRecord
        {
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Seed = _simulation.RunSeed,
            Victory = _simulation.Phase == GamePhase.Victory,
            GodModeUsed = godModeUsed,
            Score = _simulation.Score,
            Kills = _simulation.Kills,
            SectorsCompleted = _simulation.SectorsCompleted,
            ElapsedSeconds = _simulation.ElapsedRunSeconds,
            DamageTaken = _simulation.DamageTaken,
            UpgradeIds = _simulation.OwnedUpgradeIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            NewlyUnlockedIds = _newUnlockIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
        });
        if (!_automaticCapture)
        {
            _profileStore.Save(_profile);
            _checkpointStore.Clear();
            _availableCheckpoint = null;
        }
        ConfigureMenuProfile();
        _resultRecorded = true;
    }

    private void ApplySettings()
    {
        _settings.Clamp();
        if ((_automaticCapture || _characterLabOptions is not null) && _capture.IsRecording)
        {
            // Deterministic capture advances once per rendered frame. Variable-step hosting
            // prevents a slow PNG write from causing catch-up updates between recorded frames.
            IsFixedTimeStep = false;
            _appliedRenderFrameRate = _capture.RecordingFramesPerSecond;
            return;
        }

        if (_appliedRenderFrameRate != _settings.RenderFrameRate)
        {
            SetRenderFrameRate(_settings.RenderFrameRate);
            _appliedRenderFrameRate = _settings.RenderFrameRate;
        }
    }

    private void RefreshMouseCapture()
    {
        bool shouldCapture = IsActive && _runActive && _simulation is not null && !_menu.IsOpen &&
            _simulation.Phase is not (GamePhase.Paused or GamePhase.Victory or GamePhase.Defeat);
        ApplyMouseCapture(shouldCapture);
    }

    private void ApplyMouseCapture(bool captured)
    {
        if (_mouseCaptured == captured)
        {
            IsMouseVisible = !captured;
            return;
        }

        _mouseCapture?.SetCaptured(captured);
        _mouseCaptured = captured;
        IsMouseVisible = !captured;
    }

    private static double GameTimeSeconds(GameSimulation simulation) => simulation.ElapsedRunSeconds;

    private static int WeaponMenuOrder(WeaponDefinition weapon) => weapon.Id switch
    {
        "pulse-sidearm" => 0,
        "burst-carbine" => 1,
        "scatter-blaster" => 2,
        "beam-rifle" => 3,
        "plasma-launcher" => 4,
        "arc-cannon" => 5,
        _ => 99,
    };

    private AudioMusicState GetMusicState()
    {
        if (_simulation is null)
        {
            return AudioMusicState.None;
        }

        if (_menu.Page == MenuPage.Main)
        {
            return AudioMusicState.Menu;
        }

        if (_simulation.Phase == GamePhase.Victory)
        {
            return AudioMusicState.Victory;
        }

        if (_simulation.Phase == GamePhase.Defeat)
        {
            return AudioMusicState.Defeat;
        }

        if (_simulation.RunPhase == RunPhase.BossActive || _simulation.IsBossWave)
        {
            return AudioMusicState.Boss;
        }

        if (_simulation.RunPhase == RunPhase.RewardSelection)
        {
            return AudioMusicState.Intermission;
        }

        if (_simulation.AwaitingArmoryCollection)
        {
            return AudioMusicState.Intermission;
        }

        if (_simulation.RunPhase == RunPhase.EncounterActive)
        {
            return AudioMusicState.Combat;
        }

        return _simulation.InterWaveRemainingSeconds > 0f
            ? AudioMusicState.Intermission
            : AudioMusicState.Combat;
    }

    private string CaptureName(string name) => string.IsNullOrWhiteSpace(_capturePrefix)
        ? name
        : $"{_capturePrefix}-{name}";

    private static bool ContainsEnemy(List<EnemyState> enemies, EntityId enemyId)
    {
        foreach (EnemyState enemy in enemies)
        {
            if (enemy.Id == enemyId)
            {
                return true;
            }
        }

        return false;
    }

    private static void DrawSectorPresentation(GameSimulation simulation, PrimitiveRenderer primitives)
    {
        if (simulation.RunPhase == RunPhase.BossActive)
        {
            Vector3 center = simulation.Arena.BossArenaAnchor.ToXna();
            Vector3 halfExtents = simulation.Arena.BossArenaHalfExtents.ToXna();
            Color bossColor = new(255, 68, 165);
            DrawBossEnergyBoundary(
                primitives,
                center.X - halfExtents.X,
                center.X + halfExtents.X,
                center.Z - halfExtents.Z,
                center.Z + halfExtents.Z,
                bossColor);
        }

        ArenaSectorDefinition? sector = simulation.AwaitingArmoryCollection ? null : simulation.ActiveSector;
        if (sector is not null)
        {
            Color color = sector.Id switch
            {
                "cryo-works" => new Color(75, 205, 255),
                "signal-foundry" => new Color(82, 240, 175),
                "cargo-breach" => new Color(255, 172, 72),
                "reactor-vault" => new Color(205, 105, 255),
                _ => new Color(95, 220, 245),
            };
            float minimumX = sector.BoundsMin.X;
            float maximumX = sector.BoundsMax.X;
            float minimumZ = sector.BoundsMin.Z;
            float maximumZ = sector.BoundsMax.Z;
            const float floorHeight = 0.09f;
            primitives.DrawBeam(new Vector3(minimumX, floorHeight, minimumZ),
                new Vector3(maximumX, floorHeight, minimumZ), 0.045f, color);
            primitives.DrawBeam(new Vector3(maximumX, floorHeight, minimumZ),
                new Vector3(maximumX, floorHeight, maximumZ), 0.045f, color);
            primitives.DrawBeam(new Vector3(maximumX, floorHeight, maximumZ),
                new Vector3(minimumX, floorHeight, maximumZ), 0.045f, color);
            primitives.DrawBeam(new Vector3(minimumX, floorHeight, maximumZ),
                new Vector3(minimumX, floorHeight, minimumZ), 0.045f, color);

            DrawEnergyBoundary(primitives, minimumX, maximumX, minimumZ, maximumZ, color);
            Vector3 objective = sector.ObjectiveAnchor.ToXna();
            float objectivePulse = 0.65f + (MathF.Sin(simulation.ElapsedRunSeconds * 4f) * 0.18f);
            primitives.DrawBeam(objective + new Vector3(0f, 0.05f, 0f),
                objective + new Vector3(0f, 2.5f, 0f), 0.035f, color);
            primitives.DrawCube(objective + new Vector3(0f, 0.28f, 0f),
                new Vector3(0.22f + (objectivePulse * 0.08f)), color, emissive: true);
        }

        foreach (PendingEnemySpawn pending in simulation.PendingEnemySpawns)
        {
            Vector3 position = pending.Position.ToXna();
            float pulse = 0.6f + (MathF.Sin((pending.RemainingSeconds * 18f) + pending.Position.X) * 0.22f);
            Color portalColor = pending.IsElite ? new Color(255, 92, 185) : new Color(75, 220, 255);
            float radius = pending.IsElite ? 0.9f : 0.68f;
            primitives.DrawBeam(position + new Vector3(-radius, 0.05f, 0f),
                position + new Vector3(radius, 0.05f, 0f), 0.04f, portalColor);
            primitives.DrawBeam(position + new Vector3(0f, 0.05f, -radius),
                position + new Vector3(0f, 0.05f, radius), 0.04f, portalColor);
            primitives.DrawBeam(position + new Vector3(0f, 0.05f, 0f),
                position + new Vector3(0f, 2.2f * pulse, 0f), 0.05f, portalColor);
            primitives.DrawCube(position + new Vector3(0f, 0.3f, 0f),
                new Vector3(0.18f + (pulse * 0.08f)), portalColor, emissive: true);
        }

        EnemyState[] living = simulation.Enemies.Where(enemy => !enemy.IsDead).ToArray();
        if (living.Length == 1 && CoreVector3.Distance(living[0].Position, simulation.Player.Position) > 12f)
        {
            EnemyState enemy = living[0];
            Vector3 basePosition = enemy.Position.ToXna();
            Color cleanupColor = new(255, 205, 72);
            primitives.DrawBeam(basePosition + new Vector3(0f, enemy.Definition.Visual.TargetHeight, 0f),
                basePosition + new Vector3(0f, enemy.Definition.Visual.TargetHeight + 4f, 0f),
                0.045f,
                cleanupColor);
        }
    }

    private static void DrawCollisionOverlay(GameSimulation simulation, PrimitiveRenderer primitives)
    {
        Color worldCollider = new(75, 255, 170);
        foreach (ArenaPrimitiveDefinition primitive in simulation.Arena.Primitives.Where(
                     primitive => primitive.HasCollision &&
                         !primitive.Id.Equals("floor", StringComparison.OrdinalIgnoreCase)))
        {
            DrawCollisionBox(primitives, primitive, worldCollider);
        }

        Color enemyCollider = new(70, 205, 255);
        foreach (EnemyState enemy in simulation.Enemies.Where(enemy => !enemy.IsDead))
        {
            Vector3 ground = enemy.Position.ToXna();
            float radius = enemy.Definition.ColliderRadius;
            float height = enemy.Definition.ColliderHeight;
            float middle = ground.Y + (height * 0.5f);
            primitives.DrawBeam(ground, ground + new Vector3(0f, height, 0f), 0.025f, enemyCollider);
            primitives.DrawBeam(
                new Vector3(ground.X - radius, middle, ground.Z),
                new Vector3(ground.X + radius, middle, ground.Z),
                0.025f,
                enemyCollider);
            primitives.DrawBeam(
                new Vector3(ground.X, middle, ground.Z - radius),
                new Vector3(ground.X, middle, ground.Z + radius),
                0.025f,
                enemyCollider);
        }

        Vector3 player = simulation.Player.Position.ToXna();
        Color playerCollider = new(255, 220, 80);
        primitives.DrawBeam(
            player - new Vector3(0f, 1.65f, 0f),
            player,
            0.035f,
            playerCollider);
    }

    private static void DrawCollisionBox(
        PrimitiveRenderer primitives,
        ArenaPrimitiveDefinition primitive,
        Color color)
    {
        Vector3 half = primitive.Size.ToXna() * 0.5f;
        Vector3 rotation = primitive.RotationDegrees.ToXna() * (MathF.PI / 180f);
        Matrix transform = Matrix.CreateRotationX(rotation.X) *
            Matrix.CreateRotationY(rotation.Y) *
            Matrix.CreateRotationZ(rotation.Z) *
            Matrix.CreateTranslation(primitive.Position.ToXna());
        Vector3[] corners =
        [
            Vector3.Transform(new Vector3(-half.X, -half.Y, -half.Z), transform),
            Vector3.Transform(new Vector3(half.X, -half.Y, -half.Z), transform),
            Vector3.Transform(new Vector3(half.X, -half.Y, half.Z), transform),
            Vector3.Transform(new Vector3(-half.X, -half.Y, half.Z), transform),
            Vector3.Transform(new Vector3(-half.X, half.Y, -half.Z), transform),
            Vector3.Transform(new Vector3(half.X, half.Y, -half.Z), transform),
            Vector3.Transform(new Vector3(half.X, half.Y, half.Z), transform),
            Vector3.Transform(new Vector3(-half.X, half.Y, half.Z), transform),
        ];
        ReadOnlySpan<(int Start, int End)> edges =
        [
            (0, 1), (1, 2), (2, 3), (3, 0),
            (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7),
        ];
        foreach ((int start, int end) in edges)
        {
            primitives.DrawBeam(corners[start], corners[end], 0.025f, color);
        }
    }

    private static void DrawEnergyBoundary(
        PrimitiveRenderer primitives,
        float minimumX,
        float maximumX,
        float minimumZ,
        float maximumZ,
        Color color)
    {
        bool innerVerticalAtMinimum = MathF.Abs(minimumX) <= 3.1f;
        float verticalX = innerVerticalAtMinimum ? minimumX : maximumX;
        bool innerHorizontalAtMinimum = MathF.Abs(minimumZ) <= 3.1f;
        float horizontalZ = innerHorizontalAtMinimum ? minimumZ : maximumZ;
        for (float z = minimumZ + 1.5f; z < maximumZ; z += 3.5f)
        {
            primitives.DrawBeam(new Vector3(verticalX, 0.08f, z),
                new Vector3(verticalX, 2.35f, z), 0.035f, color);
        }

        for (float x = minimumX + 1.5f; x < maximumX; x += 3.5f)
        {
            primitives.DrawBeam(new Vector3(x, 0.08f, horizontalZ),
                new Vector3(x, 2.35f, horizontalZ), 0.035f, color);
        }
    }

    private static void DrawBossEnergyBoundary(
        PrimitiveRenderer primitives,
        float minimumX,
        float maximumX,
        float minimumZ,
        float maximumZ,
        Color color)
    {
        const float floorHeight = 0.09f;
        primitives.DrawBeam(new Vector3(minimumX, floorHeight, minimumZ),
            new Vector3(maximumX, floorHeight, minimumZ), 0.065f, color);
        primitives.DrawBeam(new Vector3(maximumX, floorHeight, minimumZ),
            new Vector3(maximumX, floorHeight, maximumZ), 0.065f, color);
        primitives.DrawBeam(new Vector3(maximumX, floorHeight, maximumZ),
            new Vector3(minimumX, floorHeight, maximumZ), 0.065f, color);
        primitives.DrawBeam(new Vector3(minimumX, floorHeight, maximumZ),
            new Vector3(minimumX, floorHeight, minimumZ), 0.065f, color);

        for (float x = minimumX + 1f; x < maximumX; x += 2.5f)
        {
            primitives.DrawBeam(new Vector3(x, 0.08f, minimumZ),
                new Vector3(x, 2.6f, minimumZ), 0.032f, color);
            primitives.DrawBeam(new Vector3(x, 0.08f, maximumZ),
                new Vector3(x, 2.6f, maximumZ), 0.032f, color);
        }

        for (float z = minimumZ + 1f; z < maximumZ; z += 2.5f)
        {
            primitives.DrawBeam(new Vector3(minimumX, 0.08f, z),
                new Vector3(minimumX, 2.6f, z), 0.032f, color);
            primitives.DrawBeam(new Vector3(maximumX, 0.08f, z),
                new Vector3(maximumX, 2.6f, z), 0.032f, color);
        }
    }

    private void DrawArenaModels(ArenaDefinition arena, Matrix view, Matrix projection)
    {
        foreach (ArenaPropDefinition prop in arena.Props)
        {
            if (!_arenaModels.TryGetValue(prop.ModelAsset, out StaticModelPresenter? presenter))
            {
                continue;
            }

            presenter.Draw(
                prop.Position.ToXna(),
                prop.TargetSpan,
                prop.YawDegrees * (MathF.PI / 180f),
                prop.PitchDegrees * (MathF.PI / 180f),
                view,
                projection,
                prop.DiffuseTint.ToXna(),
                prop.EmissiveTint.ToXna(),
                arena,
                prop.AnchorToGround);
        }
    }
}
