namespace FpsFrenzy.Kni.Rendering;

public readonly record struct DebugOverlayState(
    bool Enabled,
    bool ShowCollision,
    bool SandboxActive,
    bool GodModeOverride,
    bool LabVisible,
    bool AiFrozen,
    string WeaponName,
    string DifficultyName,
    int ThreatTier,
    string CalibrationAxes,
    string CalibrationAnchors,
    string Status);
