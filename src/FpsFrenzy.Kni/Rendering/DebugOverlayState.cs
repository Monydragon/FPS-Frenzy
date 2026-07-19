namespace FpsFrenzy.Kni.Rendering;

public readonly record struct DebugOverlayState(
    bool Enabled,
    bool ShowCollision,
    bool SandboxActive,
    bool GodModeOverride);
