using Microsoft.Xna.Framework.Input;

namespace FpsFrenzy.Kni.Input;

internal readonly record struct GamepadGameplayActions(
    bool Ability1,
    bool Ability2,
    bool DedicatedFocus,
    bool Interact,
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

    internal static GamepadGameplayActions Resolve(Buttons current, Buttons previous)
    {
        int weaponCycleDirection = Pressed(current, previous, Buttons.Y) ||
            Pressed(current, previous, Buttons.DPadRight)
                ? 1
                : Pressed(current, previous, Buttons.B) || Pressed(current, previous, Buttons.DPadLeft)
                    ? -1
                    : 0;
        return new GamepadGameplayActions(
            IsDown(current, Buttons.LeftShoulder),
            IsDown(current, Buttons.RightShoulder),
            IsDown(current, Buttons.RightStick),
            IsDown(current, Buttons.DPadUp),
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

    private static bool IsDown(Buttons buttons, Buttons button) => (buttons & button) != 0;
}
