using Microsoft.Xna.Framework.Input;

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
}

public sealed class DebugTestController
{
    private KeyboardState _previousKeyboard;

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

    public DebugTestAction Update(KeyboardState keyboard, bool runAvailable)
    {
        DebugTestAction action = DebugTestAction.None;
        if (Pressed(keyboard, Keys.F1))
        {
            Enabled = !Enabled;
            if (!Enabled)
            {
                ShowCollision = false;
                GodModeOverride = false;
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
        }

        _previousKeyboard = keyboard;
        return action;
    }

    private bool Pressed(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

    private static bool EnvironmentFlag(string name) => string.Equals(
        Environment.GetEnvironmentVariable(name),
        "1",
        StringComparison.Ordinal);
}
