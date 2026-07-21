using FpsFrenzy.Kni.Settings;
using Microsoft.Xna.Framework.Input;

namespace FpsFrenzy.Kni.Input;

internal readonly record struct GamepadGameplayActions(
    bool Ability1,
    bool Ability2,
    bool DedicatedFocus,
    bool Interact,
    bool Jump,
    bool Reload,
    int WeaponCycleDirection);

internal static class GamepadGameplayBindings
{
    private static readonly Buttons[] TrackedButtons =
    [
        Buttons.A, Buttons.B, Buttons.X, Buttons.Y,
        Buttons.LeftShoulder, Buttons.RightShoulder,
        Buttons.LeftStick, Buttons.RightStick,
        Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight,
        Buttons.Start, Buttons.Back,
    ];

    internal static GamepadGameplayActions Resolve(
        Buttons current,
        Buttons previous,
        GamepadControlBindings? bindings = null)
    {
        bindings ??= new GamepadControlBindings();
        int weaponCycleDirection = Pressed(current, previous, bindings.WeaponNext)
                ? 1
                : Pressed(current, previous, bindings.WeaponPrevious)
                    ? -1
                    : 0;
        return new GamepadGameplayActions(
            IsDown(current, bindings.Ability1),
            IsDown(current, bindings.Ability2),
            IsDown(current, bindings.Focus),
            IsDown(current, bindings.Interact),
            IsDown(current, bindings.Jump),
            IsDown(current, bindings.Reload),
            weaponCycleDirection);
    }

    internal static Buttons CaptureButtons(GamePadState state)
    {
        Buttons buttons = 0;
        foreach (Buttons button in TrackedButtons)
        {
            if (state.IsButtonDown(button))
            {
                buttons |= button;
            }
        }
        return buttons;
    }

    private static bool Pressed(Buttons current, Buttons previous, Buttons button) =>
        IsDown(current, button) && !IsDown(previous, button);

    private static bool Pressed(Buttons current, Buttons previous, GamepadBindingButton button) =>
        Pressed(current, previous, ToButtons(button));

    private static bool IsDown(Buttons buttons, Buttons button) => (buttons & button) != 0;

    private static bool IsDown(Buttons buttons, GamepadBindingButton button) =>
        IsDown(buttons, ToButtons(button));

    internal static Buttons ToButtons(GamepadBindingButton button) => button switch
    {
        GamepadBindingButton.A => Buttons.A,
        GamepadBindingButton.B => Buttons.B,
        GamepadBindingButton.X => Buttons.X,
        GamepadBindingButton.Y => Buttons.Y,
        GamepadBindingButton.LeftShoulder => Buttons.LeftShoulder,
        GamepadBindingButton.RightShoulder => Buttons.RightShoulder,
        GamepadBindingButton.LeftStick => Buttons.LeftStick,
        GamepadBindingButton.RightStick => Buttons.RightStick,
        GamepadBindingButton.DPadUp => Buttons.DPadUp,
        GamepadBindingButton.DPadDown => Buttons.DPadDown,
        GamepadBindingButton.DPadLeft => Buttons.DPadLeft,
        GamepadBindingButton.DPadRight => Buttons.DPadRight,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };
}
