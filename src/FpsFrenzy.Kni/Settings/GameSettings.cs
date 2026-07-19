using System.Text.Json;

namespace FpsFrenzy.Kni.Settings;

public enum ColorVisionMode
{
    Standard,
    Deuteranopia,
    Protanopia,
    Tritanopia,
}

public sealed record GameSettings
{
    public float MasterVolume { get; set; } = 0.85f;
    public float SoundEffectsVolume { get; set; } = 0.9f;
    public float MouseSensitivity { get; set; } = 1f;
    public float GamepadSensitivity { get; set; } = 1f;
    public float FieldOfViewScale { get; set; } = 1f;
    public int RenderFrameRate { get; set; } = 60;
    public float ScreenShakeScale { get; set; } = 1f;
    public float CameraBobScale { get; set; } = 1f;
    public bool ReducedFlash { get; set; }
    public bool HighContrastReticle { get; set; }
    public bool LargeHudText { get; set; }
    public bool Subtitles { get; set; } = true;
    public bool ToggleAimDownSights { get; set; }
    public bool GodMode { get; set; }
    public ColorVisionMode ColorVisionMode { get; set; }

    public void Clamp()
    {
        MasterVolume = Math.Clamp(MasterVolume, 0f, 1f);
        SoundEffectsVolume = Math.Clamp(SoundEffectsVolume, 0f, 1f);
        MouseSensitivity = Math.Clamp(MouseSensitivity, 0.35f, 2.5f);
        GamepadSensitivity = Math.Clamp(GamepadSensitivity, 0.35f, 2.5f);
        FieldOfViewScale = Math.Clamp(FieldOfViewScale, 0.85f, 1.15f);
        RenderFrameRate = RenderFrameRate == 30 ? 30 : 60;
        ScreenShakeScale = Math.Clamp(ScreenShakeScale, 0f, 1f);
        CameraBobScale = Math.Clamp(CameraBobScale, 0f, 1f);
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
