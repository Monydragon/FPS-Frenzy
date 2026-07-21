using Microsoft.Xna.Framework.Input;
using FpsFrenzy.Kni.Input;

namespace FpsFrenzy.Kni.Development;

[Flags]
public enum DebugTestAction
{
    None = 0,
    ModeChanged = 1 << 0,
    CollisionViewChanged = 1 << 1,
    RestartStage = 1 << 2,
    PreviousStage = 1 << 3,
    NextStage = 1 << 4,
    GodModeChanged = 1 << 5,
    CompleteStage = 1 << 6,
    GrantProgression = 1 << 7,
    SpawnLootShowcase = 1 << 8,
    LabModeChanged = 1 << 9,
    PreviousWeapon = 1 << 10,
    NextWeapon = 1 << 11,
    PreviousDifficulty = 1 << 12,
    NextDifficulty = 1 << 13,
    PreviousThreatTier = 1 << 14,
    NextThreatTier = 1 << 15,
    SpawnEnemy = 1 << 16,
    ToggleAiFreeze = 1 << 17,
    TeleportSector = 1 << 18,
    ReloadWeaponData = 1 << 19,
    NextAbility1 = 1 << 20,
    NextAbility2 = 1 << 21,
}

public sealed class DebugTestController
{
    private KeyboardState _previousKeyboard;
    private Buttons _previousGamePadButtons;

    public DebugTestController(bool? enabled = null)
    {
        Enabled = enabled ?? string.Equals(
            Environment.GetEnvironmentVariable("FPS_FRENZY_DEBUG"),
            "1",
            StringComparison.Ordinal);
        if (Enabled && enabled is null)
        {
            ShowCollision = EnvironmentFlag("FPS_FRENZY_DEBUG_COLLISION");
            GodModeOverride = EnvironmentFlag("FPS_FRENZY_DEBUG_GOD_MODE");
        }
    }

    public bool Enabled { get; private set; }
    public bool ShowCollision { get; private set; }
    public bool GodModeOverride { get; private set; }
    public bool LabVisible { get; private set; }

    public void EnterLab()
    {
        Enabled = true;
        LabVisible = true;
        GodModeOverride = true;
    }

    public DebugTestAction Update(KeyboardState keyboard, bool runAvailable) =>
        Update(keyboard, (Buttons)0, runAvailable);

    public DebugTestAction Update(KeyboardState keyboard, GamePadState gamePad, bool runAvailable)
        => Update(keyboard, GamepadGameplayBindings.CaptureButtons(gamePad), runAvailable);

    internal DebugTestAction Update(KeyboardState keyboard, Buttons gamePadButtons, bool runAvailable)
    {
        DebugTestAction action = DebugTestAction.None;
        bool captureModifier = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        if (!captureModifier && Pressed(keyboard, Keys.F11))
        {
            LabVisible = !LabVisible;
            Enabled |= LabVisible;
            action |= DebugTestAction.LabModeChanged;
        }
        if (Pressed(keyboard, Keys.F1))
        {
            Enabled = !Enabled;
            if (!Enabled)
            {
                ShowCollision = false;
                GodModeOverride = false;
                LabVisible = false;
            }

            action |= DebugTestAction.ModeChanged;
        }

        if (Enabled && Pressed(keyboard, Keys.F4))
        {
            ShowCollision = !ShowCollision;
            action |= DebugTestAction.CollisionViewChanged;
        }

        if (Enabled && runAvailable)
        {
            if (Pressed(keyboard, Keys.F2))
            {
                action |= DebugTestAction.GrantProgression;
            }

            if (Pressed(keyboard, Keys.F3))
            {
                action |= DebugTestAction.SpawnLootShowcase;
            }

            if (Pressed(keyboard, Keys.F5))
            {
                action |= DebugTestAction.RestartStage;
            }

            if (Pressed(keyboard, Keys.F6))
            {
                action |= DebugTestAction.PreviousStage;
            }

            if (Pressed(keyboard, Keys.F7))
            {
                action |= DebugTestAction.NextStage;
            }

            if (Pressed(keyboard, Keys.F8))
            {
                GodModeOverride = !GodModeOverride;
                action |= DebugTestAction.GodModeChanged;
            }

            if (Pressed(keyboard, Keys.F9))
            {
                action |= DebugTestAction.CompleteStage;
            }

            if (LabVisible)
            {
                if (Pressed(keyboard, Keys.OemOpenBrackets) || Pressed(keyboard, Keys.J) ||
                    Pressed(gamePadButtons, Buttons.B))
                    action |= DebugTestAction.PreviousWeapon;
                if (Pressed(keyboard, Keys.OemCloseBrackets) || Pressed(keyboard, Keys.K) ||
                    Pressed(gamePadButtons, Buttons.Y))
                    action |= DebugTestAction.NextWeapon;
                if (Pressed(keyboard, Keys.OemMinus)) action |= DebugTestAction.PreviousDifficulty;
                if (Pressed(keyboard, Keys.OemPlus)) action |= DebugTestAction.NextDifficulty;
                if (Pressed(keyboard, Keys.PageDown)) action |= DebugTestAction.PreviousThreatTier;
                if (Pressed(keyboard, Keys.PageUp)) action |= DebugTestAction.NextThreatTier;
                if (Pressed(keyboard, Keys.I) || Pressed(gamePadButtons, Buttons.DPadRight))
                    action |= DebugTestAction.SpawnEnemy;
                if (Pressed(keyboard, Keys.O) || Pressed(gamePadButtons, Buttons.DPadLeft))
                    action |= DebugTestAction.ToggleAiFreeze;
                if (Pressed(gamePadButtons, Buttons.DPadUp)) action |= DebugTestAction.NextAbility1;
                if (Pressed(gamePadButtons, Buttons.DPadDown)) action |= DebugTestAction.NextAbility2;
                if (Pressed(keyboard, Keys.T)) action |= DebugTestAction.TeleportSector;
                if (!captureModifier && Pressed(keyboard, Keys.F12)) action |= DebugTestAction.ReloadWeaponData;
            }
        }

        _previousKeyboard = keyboard;
        _previousGamePadButtons = gamePadButtons;
        return action;
    }

    private bool Pressed(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

    private bool Pressed(Buttons current, Buttons button) =>
        (current & button) != 0 && (_previousGamePadButtons & button) == 0;

    private static bool EnvironmentFlag(string name) => string.Equals(
        Environment.GetEnvironmentVariable(name),
        "1",
        StringComparison.Ordinal);
}
