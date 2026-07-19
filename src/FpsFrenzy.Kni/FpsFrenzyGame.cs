using FpsFrenzy.Core;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Input;
using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Audio;
using FpsFrenzy.Kni.Input;
using FpsFrenzy.Kni.Rendering;
using FpsFrenzy.Kni.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using CoreVector2 = System.Numerics.Vector2;
using CoreVector3 = System.Numerics.Vector3;

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
    private readonly string _startingWeaponId;
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

    public FpsFrenzyGame(
        IPlatformLookSource? platformLookSource = null,
        IPlatformMouseCapture? mouseCapture = null,
        bool fullScreen = false)
    {
        _platformLookSource = platformLookSource;
        _mouseCapture = mouseCapture;
        _settings = GameSettingsStore.Load();
        _automaticCapture = Environment.GetEnvironmentVariable("FPS_FRENZY_AUTOCAPTURE") == "1";
        _automaticMenuCapture = Environment.GetEnvironmentVariable("FPS_FRENZY_AUTOCAPTURE_MENUS") == "1";
        _startingWaveIndex = int.TryParse(
            Environment.GetEnvironmentVariable("FPS_FRENZY_START_WAVE"),
            out int configuredStartingWave)
            ? Math.Max(0, configuredStartingWave)
            : 0;
        _startingWeaponId = Environment.GetEnvironmentVariable("FPS_FRENZY_START_WEAPON") ?? "pulse-sidearm";
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
        _simulation = new GameSimulation(
            _catalog,
            "orbital-depot",
            startingWaveIndex: _startingWaveIndex,
            startingWeaponId: _startingWeaponId,
            godModeEnabled: _settings.GodMode);
        _primitives = new PrimitiveRenderer(GraphicsDevice);
        foreach (WeaponDefinition weapon in _catalog.Weapons.Values)
        {
            _weaponModels.Add(weapon.Id, new StaticModelPresenter(Content.Load<Model>(weapon.ModelAsset)));
        }

        foreach (string modelAsset in _catalog.Enemies.Values.Select(enemy => enemy.ModelAsset).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _enemyModelPresenters.Add(modelAsset, new SkinnedModelPresenter(Content.Load<Model>(modelAsset)));
        }

        _pickupModels.Add("health", new StaticModelPresenter(Content.Load<Model>("Models/Pickups/health-crate")));
        _pickupModels.Add("ammo", new StaticModelPresenter(Content.Load<Model>("Models/Pickups/ammo-cache")));
        _pickupModels.Add("pedestal", new StaticModelPresenter(Content.Load<Model>("Models/Arenas/OrbitalDepot/Station/table-display-small")));
        _pickupModels.Add("container", new StaticModelPresenter(Content.Load<Model>("Models/Arenas/OrbitalDepot/Station/container-flat-open")));
        _pickupModels.Add("ring", new StaticModelPresenter(Content.Load<Model>("Models/Arenas/OrbitalDepot/Station/pipe-ring-colored")));
        foreach (string modelAsset in _catalog.Arenas.Values
                     .SelectMany(arena => arena.Props)
                     .Select(prop => prop.ModelAsset)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _arenaModels.Add(modelAsset, new StaticModelPresenter(Content.Load<Model>(modelAsset)));
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
        _audio = new CombatAudio(_settings);
        _runActive = _automaticCapture;
        if (!_automaticCapture)
        {
            _simulation.SetPaused(true);
            _menu.OpenMain();
        }

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
        Rectangle safeArea = GraphicsDevice.Viewport.TitleSafeArea;
        MenuInputSnapshot menuInput = MenuInputSnapshot.Capture(_menu.Page, safeArea);
        MenuAction menuAction = _menu.Update(_settings, menuInput, safeArea);
        switch (menuAction)
        {
            case MenuAction.Pause:
                _simulation.SetPaused(true);
                break;
            case MenuAction.Resume:
                _simulation.SetPaused(false);
                _suppressGameplayInputUntilNeutral = true;
                break;
            case MenuAction.StartRun:
            case MenuAction.Restart:
                RestartSimulation();
                _runActive = true;
                _menu.Close();
                _suppressGameplayInputUntilNeutral = true;
                break;
            case MenuAction.ReturnToMain:
                _runActive = false;
                _simulation.SetPaused(true);
                _menu.OpenMain();
                break;
            case MenuAction.Quit:
                Exit();
                return;
            case MenuAction.SettingsChanged:
                GameSettingsStore.Save(_settings);
                ApplySettings();
                _simulation.SetPlayerInvulnerable(_settings.GodMode);
                break;
        }
        RefreshMouseCapture();

        if (Keyboard.GetState().IsKeyDown(Keys.F10))
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
                Movement = CoreVector2.Zero,
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
            _accumulatorSeconds = Math.Min(0.25, _accumulatorSeconds + gameTime.ElapsedGameTime.TotalSeconds);
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
                    _audio?.Consume(_simulation.CombatEvents, _settings);
                    _accumulatorSeconds -= GameSimulation.FixedDeltaSeconds;
                }

                _pendingLook = CoreVector2.Zero;
            }
        }

        _interpolationAlpha = (float)(_accumulatorSeconds / GameSimulation.FixedDeltaSeconds);
        _adsBlend = HudMath.InterpolateAds(
            _adsBlend,
            _simulation.Player.IsAiming,
            (float)gameTime.ElapsedGameTime.TotalSeconds);
        float elapsedSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _presentationSeconds += gameTime.ElapsedGameTime.TotalSeconds;
        if (!(_automaticCapture && _capture.HasPendingCapture))
        {
            _feedback?.Update(elapsedSeconds);
        }
        _audio?.Update(elapsedSeconds, _settings);
        if (_simulation.Phase == GamePhase.Paused && !_menu.IsOpen && _runActive)
        {
            _menu.OpenPause();
            RefreshMouseCapture();
        }
        else if (_simulation.Phase is GamePhase.Victory or GamePhase.Defeat && _menu.Page != MenuPage.Results)
        {
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
        if (automaticCaptureComplete)
        {
            Exit();
            return;
        }

        RefreshMouseCapture();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
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
        CoreVector3 cameraPositionCore = CoreVector3.Lerp(
            _simulation.Player.PreviousPosition,
            _simulation.Player.Position,
            _interpolationAlpha);
        Vector3 cameraPosition = cameraPositionCore.ToXna();
        Vector3 forward = _simulation.GetViewDirection().ToXna();
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.Up));
        bool moving = CoreVector3.DistanceSquared(_simulation.Player.PreviousPosition, _simulation.Player.Position) > 0.00001f;
        float bob = moving && _simulation.Player.IsGrounded
            ? MathF.Sin(_simulation.ElapsedRunSeconds * 12f) * 0.025f * _settings.CameraBobScale
            : 0f;
        float weaponShake = MathF.Max(0f, 0.09f - _simulation.LastShotSeconds) *
            _simulation.Player.CurrentWeapon.Definition.ScreenShake * _settings.ScreenShakeScale;
        float damageShake = _simulation.PlayerDamageFlashSeconds * 0.08f * _settings.ScreenShakeScale;
        float shake = weaponShake + damageShake;
        cameraPosition += (Vector3.Up * (bob + (MathF.Sin(_simulation.ElapsedRunSeconds * 83f) * shake))) +
            (right * (MathF.Cos(_simulation.ElapsedRunSeconds * 67f) * shake));
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
        DrawWorld(_simulation, _primitives, view, projection, gameTime.ElapsedGameTime);
        GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);
        if (_runActive)
        {
            DrawViewModel(_simulation, weaponModel, projection);
        }
        _hud.Draw(_simulation, _settings, _menu, _audio?.Caption, _runActive);
        base.Draw(gameTime);
        string? capturedPath = _capture.CaptureIfRequested(GraphicsDevice);
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
        using Stream grunt = TitleContainer.OpenStream("Content/Data/Enemies/alien-grunt.json");
        using Stream skirmisher = TitleContainer.OpenStream("Content/Data/Enemies/alien-skirmisher.json");
        using Stream brute = TitleContainer.OpenStream("Content/Data/Enemies/alien-brute.json");
        using Stream spitter = TitleContainer.OpenStream("Content/Data/Enemies/alien-spitter.json");
        using Stream warden = TitleContainer.OpenStream("Content/Data/Enemies/alien-warden.json");
        using Stream boss = TitleContainer.OpenStream("Content/Data/Enemies/big-alien.json");
        using Stream trainingArena = TitleContainer.OpenStream("Content/Data/Arenas/training-ring.json");
        using Stream orbitalArena = TitleContainer.OpenStream("Content/Data/Arenas/orbital-depot.json");
        using Stream trainingWaves = TitleContainer.OpenStream("Content/Data/Waves/training-waves.json");
        using Stream orbitalWaves = TitleContainer.OpenStream("Content/Data/Waves/orbital-depot-waves.json");
        using Stream releaseWaves = TitleContainer.OpenStream("Content/Data/Waves/release-wave-template.json");
        return ContentCatalog.Load(
            [pulse, burst, scatter, beam, plasma, arc],
            [grunt, skirmisher, brute, spitter, warden, boss],
            [trainingArena, orbitalArena],
            [trainingWaves, orbitalWaves, releaseWaves]);
    }

    private void DrawWorld(
        GameSimulation simulation,
        PrimitiveRenderer primitives,
        Matrix view,
        Matrix projection,
        TimeSpan elapsed)
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

            string alias = enemy.IsDead ? "death" : enemy.HitFlashSeconds > 0f ? "hit" :
                enemy.AttackAnimationSeconds > 0f ? "attack" : "walk";
            CoreVector3 tintCore = enemy.Definition.IsBoss
                ? enemy.Definition.BossPhases[enemy.CurrentBossPhaseIndex].Tint
                : enemy.Definition.Tint;
            Vector3 tint = tintCore.ToXna();
            if (enemy.HitFlashSeconds > 0f)
            {
                tint = Vector3.Lerp(tint, Vector3.One, Math.Clamp(enemy.HitFlashSeconds / 0.12f, 0f, 1f));
            }

            float scale = enemy.Definition.RenderScale;
            float modelHeight = enemy.Definition.ColliderHeight * Math.Clamp(scale, 0.82f, 1.28f);
            enemyModel.Draw(instance, enemy.Definition.AnimationClips[alias], elapsed,
                position + new Vector3(0f, modelHeight * 0.5f, 0f), modelHeight, enemy.FacingYaw,
                tint, view, projection, simulation.Arena);
            if (!enemy.IsDead)
            {
                Color visibilityColor = new(tint.X, tint.Y, tint.Z);
                Vector3 ground = position + new Vector3(0f, 0.08f, 0f);
                float groundRadius = MathF.Max(0.32f, enemy.Definition.ColliderRadius * 0.8f);
                float markerSegment = groundRadius * 0.72f;
                primitives.DrawBeam(ground + new Vector3(-groundRadius, 0f, -groundRadius),
                    ground + new Vector3(-groundRadius + markerSegment, 0f, -groundRadius), 0.018f, visibilityColor);
                primitives.DrawBeam(ground + new Vector3(groundRadius - markerSegment, 0f, groundRadius),
                    ground + new Vector3(groundRadius, 0f, groundRadius), 0.018f, visibilityColor);
                primitives.DrawCube(position + new Vector3(0f, modelHeight + 0.18f, 0f),
                    new Vector3(enemy.Definition.IsBoss ? 0.14f : 0.085f), visibilityColor,
                    (float)GameTimeSeconds(simulation) + enemy.Id.Value, emissive: true);

                float healthFraction = Math.Clamp(enemy.Health / enemy.Definition.MaxHealth, 0f, 1f);
                Vector3 barStart = position + new Vector3(-0.42f * scale, modelHeight + (0.34f * scale), 0f);
                primitives.DrawBeam(barStart, barStart + (Vector3.Right * 0.84f * scale),
                    enemy.Definition.IsBoss ? 0.075f : 0.048f, new Color(18, 24, 34));
                primitives.DrawBeam(barStart,
                    barStart + (Vector3.Right * 0.84f * scale * healthFraction),
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
        bool moving = CoreVector3.DistanceSquared(
            simulation.Player.PreviousPosition, simulation.Player.Position) > 0.00001f;
        float moveBlend = moving && simulation.Player.IsGrounded ? 1f : 0f;
        float stride = simulation.ElapsedRunSeconds * 11f;
        float horizontalBob = MathF.Sin(stride) * 0.012f * moveBlend * _settings.CameraBobScale;
        float verticalBob = -MathF.Abs(MathF.Cos(stride)) * 0.009f * moveBlend * _settings.CameraBobScale;
        float idleSway = MathF.Sin(simulation.ElapsedRunSeconds * 2.1f) * 0.0035f * _settings.CameraBobScale;
        Vector3 cameraSpacePosition = new(
            offsetCore.X + horizontalBob + idleSway,
            offsetCore.Y - recoil + verticalBob,
            offsetCore.Z);
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
        weaponModel.Draw(cameraSpacePosition, viewModelScale, MathF.PI, 0f, Matrix.Identity, projection);
    }

    private void RestartSimulation()
    {
        if (_catalog is null)
        {
            return;
        }

        _simulation?.Dispose();
        _simulation = new GameSimulation(
            _catalog,
            "orbital-depot",
            startingWaveIndex: _startingWaveIndex,
            startingWeaponId: _startingWeaponId,
            godModeEnabled: _settings.GodMode);
        _enemyModels.Clear();
        _feedback = new CombatFeedbackPresenter(_catalog);
        _pendingLook = CoreVector2.Zero;
        _accumulatorSeconds = 0d;
        _interpolationAlpha = 0f;
        _adsBlend = 0f;
    }

    private void ApplySettings()
    {
        _settings.Clamp();
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
