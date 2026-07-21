using System.Text.Json;

namespace FpsFrenzy.Kni.Settings;

public enum ColorVisionMode
{
    Standard,
    Deuteranopia,
    Protanopia,
    Tritanopia,
}

public enum GamepadBindingButton
{
    A,
    B,
    X,
    Y,
    LeftShoulder,
    RightShoulder,
    LeftStick,
    RightStick,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
}

public enum GamepadBindingAction
{
    Jump,
    Interact,
    Reload,
    Ability1,
    Ability2,
    Focus,
    WeaponNext,
    WeaponPrevious,
}

public sealed record GamepadControlBindings
{
    public GamepadBindingButton Jump { get; set; } = GamepadBindingButton.A;
    public GamepadBindingButton Interact { get; set; } = GamepadBindingButton.X;
    public GamepadBindingButton Reload { get; set; } = GamepadBindingButton.DPadDown;
    public GamepadBindingButton Ability1 { get; set; } = GamepadBindingButton.LeftShoulder;
    public GamepadBindingButton Ability2 { get; set; } = GamepadBindingButton.RightShoulder;
    public GamepadBindingButton Focus { get; set; } = GamepadBindingButton.RightStick;
    public GamepadBindingButton WeaponNext { get; set; } = GamepadBindingButton.Y;
    public GamepadBindingButton WeaponPrevious { get; set; } = GamepadBindingButton.B;

    public GamepadBindingButton this[GamepadBindingAction action]
    {
        get => action switch
        {
            GamepadBindingAction.Jump => Jump,
            GamepadBindingAction.Interact => Interact,
            GamepadBindingAction.Reload => Reload,
            GamepadBindingAction.Ability1 => Ability1,
            GamepadBindingAction.Ability2 => Ability2,
            GamepadBindingAction.Focus => Focus,
            GamepadBindingAction.WeaponNext => WeaponNext,
            GamepadBindingAction.WeaponPrevious => WeaponPrevious,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        set
        {
            switch (action)
            {
                case GamepadBindingAction.Jump: Jump = value; break;
                case GamepadBindingAction.Interact: Interact = value; break;
                case GamepadBindingAction.Reload: Reload = value; break;
                case GamepadBindingAction.Ability1: Ability1 = value; break;
                case GamepadBindingAction.Ability2: Ability2 = value; break;
                case GamepadBindingAction.Focus: Focus = value; break;
                case GamepadBindingAction.WeaponNext: WeaponNext = value; break;
                case GamepadBindingAction.WeaponPrevious: WeaponPrevious = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }

    public void AssignWithSwap(GamepadBindingAction action, GamepadBindingButton button)
    {
        GamepadBindingButton previous = this[action];
        GamepadBindingAction? occupied = GamepadBindingCatalog.Actions
            .Cast<GamepadBindingAction?>()
            .FirstOrDefault(candidate => candidate != action && this[candidate!.Value] == button);
        this[action] = button;
        if (occupied is { } swappedAction)
        {
            this[swappedAction] = previous;
        }
    }

    public void Reset()
    {
        GamepadControlBindings defaults = new();
        foreach (GamepadBindingAction action in GamepadBindingCatalog.Actions)
        {
            this[action] = defaults[action];
        }
    }

    public void EnsureValid()
    {
        GamepadBindingButton[] assigned = GamepadBindingCatalog.Actions.Select(action => this[action]).ToArray();
        if (assigned.Any(button => !Enum.IsDefined(button)) || assigned.Distinct().Count() != assigned.Length)
        {
            Reset();
        }
    }
}

public static class GamepadBindingCatalog
{
    public static IReadOnlyList<GamepadBindingAction> Actions { get; } =
        Enum.GetValues<GamepadBindingAction>();
    public static IReadOnlyList<GamepadBindingButton> Buttons { get; } =
        Enum.GetValues<GamepadBindingButton>();

    public static string ActionLabel(GamepadBindingAction action) => action switch
    {
        GamepadBindingAction.Jump => "JUMP",
        GamepadBindingAction.Interact => "ACTIVATE / USE",
        GamepadBindingAction.Reload => "RELOAD",
        GamepadBindingAction.Ability1 => "ABILITY 1",
        GamepadBindingAction.Ability2 => "ABILITY 2",
        GamepadBindingAction.Focus => "ADS / FOCUS",
        GamepadBindingAction.WeaponNext => "NEXT WEAPON",
        GamepadBindingAction.WeaponPrevious => "PREVIOUS WEAPON",
        _ => action.ToString().ToUpperInvariant(),
    };

    public static string ButtonLabel(GamepadBindingButton button) => button switch
    {
        GamepadBindingButton.A => "A / PS CROSS",
        GamepadBindingButton.B => "B / PS CIRCLE",
        GamepadBindingButton.X => "X / PS SQUARE",
        GamepadBindingButton.Y => "Y / PS TRIANGLE",
        GamepadBindingButton.LeftShoulder => "LB / L1",
        GamepadBindingButton.RightShoulder => "RB / R1",
        GamepadBindingButton.LeftStick => "LS / L3",
        GamepadBindingButton.RightStick => "RS / R3",
        GamepadBindingButton.DPadUp => "DPAD UP",
        GamepadBindingButton.DPadDown => "DPAD DOWN",
        GamepadBindingButton.DPadLeft => "DPAD LEFT",
        GamepadBindingButton.DPadRight => "DPAD RIGHT",
        _ => button.ToString().ToUpperInvariant(),
    };
}

public sealed record GameSettings
{
    public float MasterVolume { get; set; } = 0.85f;
    public float MusicVolume { get; set; } = 0.65f;
    public float SoundEffectsVolume { get; set; } = 0.9f;
    public float MouseSensitivity { get; set; } = 1f;
    public float GamepadSensitivity { get; set; } = 1f;
    public float FieldOfViewScale { get; set; } = 1f;
    public int RenderFrameRate { get; set; } = 60;
    public float ScreenShakeScale { get; set; } = 1f;
    public float CameraBobScale { get; set; } = 1f;
    public bool ReducedFlash { get; set; }
    public bool ReducedUiMotion { get; set; }
    public bool HighContrastReticle { get; set; }
    public bool LargeHudText { get; set; }
    public bool Subtitles { get; set; } = true;
    public bool ToggleAimDownSights { get; set; }
    public bool GodMode { get; set; }
    public ColorVisionMode ColorVisionMode { get; set; }
    public GamepadControlBindings ControllerBindings { get; set; } = new();

    public void Clamp()
    {
        MasterVolume = Math.Clamp(MasterVolume, 0f, 1f);
        MusicVolume = Math.Clamp(MusicVolume, 0f, 1f);
        SoundEffectsVolume = Math.Clamp(SoundEffectsVolume, 0f, 1f);
        MouseSensitivity = Math.Clamp(MouseSensitivity, 0.35f, 2.5f);
        GamepadSensitivity = Math.Clamp(GamepadSensitivity, 0.35f, 2.5f);
        FieldOfViewScale = Math.Clamp(FieldOfViewScale, 0.85f, 1.15f);
        RenderFrameRate = RenderFrameRate == 30 ? 30 : 60;
        ScreenShakeScale = Math.Clamp(ScreenShakeScale, 0f, 1f);
        CameraBobScale = Math.Clamp(CameraBobScale, 0f, 1f);
        ControllerBindings ??= new GamepadControlBindings();
        ControllerBindings.EnsureValid();
    }
}

public static class GameSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FPSFrenzy",
        "settings.json");

    public static GameSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new GameSettings();
            }

            GameSettings settings = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(SettingsPath), SerializerOptions)
                ?? new GameSettings();
            settings.Clamp();
            return settings;
        }
        catch (IOException)
        {
            return new GameSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new GameSettings();
        }
        catch (JsonException)
        {
            return new GameSettings();
        }
    }

    public static void Save(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Clamp();
        try
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, SerializerOptions));
        }
        catch (IOException)
        {
            // Settings persistence is best-effort; gameplay must remain available on read-only storage.
        }
        catch (UnauthorizedAccessException)
        {
            // Settings persistence is best-effort; gameplay must remain available on read-only storage.
        }
    }
}
