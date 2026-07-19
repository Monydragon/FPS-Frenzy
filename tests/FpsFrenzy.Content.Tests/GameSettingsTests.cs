using FpsFrenzy.Kni.Settings;

namespace FpsFrenzy.Content.Tests;

public sealed class GameSettingsTests
{
    [Fact]
    public void AccessibilityAndInputRangesClampToSafeRuntimeValues()
    {
        GameSettings settings = new()
        {
            MasterVolume = 4f,
            SoundEffectsVolume = -2f,
            MouseSensitivity = 12f,
            GamepadSensitivity = 0f,
            FieldOfViewScale = 2f,
            ScreenShakeScale = -1f,
            CameraBobScale = 3f,
            RenderFrameRate = 144,
        };

        settings.Clamp();

        Assert.Equal(1f, settings.MasterVolume);
        Assert.Equal(0f, settings.SoundEffectsVolume);
        Assert.Equal(2.5f, settings.MouseSensitivity);
        Assert.Equal(0.35f, settings.GamepadSensitivity);
        Assert.Equal(1.15f, settings.FieldOfViewScale);
        Assert.Equal(0f, settings.ScreenShakeScale);
        Assert.Equal(1f, settings.CameraBobScale);
        Assert.Equal(60, settings.RenderFrameRate);
    }
}
