namespace FpsFrenzy.Kni.Rendering;

public readonly record struct DebugOverlayState(
    bool Enabled,
    bool ShowCollision,
    bool SandboxActive,
    bool GodModeOverride,
    bool LabVisible,
    bool AiFrozen,
    string WeaponName,
    int WeaponIndex,
    int WeaponCount,
    string Ability1Name,
    string Ability2Name,
    string DifficultyName,
    int ThreatTier,
    string CalibrationAxes,
    string CalibrationAnchors,
    string Status);
