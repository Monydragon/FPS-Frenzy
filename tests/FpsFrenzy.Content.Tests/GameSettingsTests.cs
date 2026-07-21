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
            MusicVolume = 8f,
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
        Assert.Equal(1f, settings.MusicVolume);
        Assert.Equal(0f, settings.SoundEffectsVolume);
        Assert.Equal(2.5f, settings.MouseSensitivity);
        Assert.Equal(0.35f, settings.GamepadSensitivity);
        Assert.Equal(1.15f, settings.FieldOfViewScale);
        Assert.Equal(0f, settings.ScreenShakeScale);
        Assert.Equal(1f, settings.CameraBobScale);
        Assert.Equal(60, settings.RenderFrameRate);
    }

    [Fact]
    public void ControllerBindingAssignmentSwapsConflictsAndResetRestoresDefaults()
    {
        GamepadControlBindings bindings = new();

        bindings.AssignWithSwap(GamepadBindingAction.Interact, GamepadBindingButton.A);

        Assert.Equal(GamepadBindingButton.A, bindings.Interact);
        Assert.Equal(GamepadBindingButton.X, bindings.Jump);
        Assert.Equal(8, GamepadBindingCatalog.Actions.Select(action => bindings[action]).Distinct().Count());

        bindings.Reset();

        Assert.Equal(GamepadBindingButton.A, bindings.Jump);
        Assert.Equal(GamepadBindingButton.X, bindings.Interact);
        Assert.Equal(GamepadBindingButton.DPadDown, bindings.Reload);
    }

    [Fact]
    public void ControllerBindingsRoundTripWithTheSettingsPayload()
    {
        GameSettings settings = new();
        settings.ControllerBindings.AssignWithSwap(
            GamepadBindingAction.Ability1, GamepadBindingButton.B);

        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        GameSettings restored = Assert.IsType<GameSettings>(
            System.Text.Json.JsonSerializer.Deserialize<GameSettings>(json));
        restored.Clamp();

        Assert.Equal(GamepadBindingButton.B, restored.ControllerBindings.Ability1);
        Assert.Equal(GamepadBindingButton.LeftShoulder, restored.ControllerBindings.WeaponPrevious);
    }
}
