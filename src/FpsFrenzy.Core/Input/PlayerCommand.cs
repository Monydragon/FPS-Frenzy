using System.Numerics;

namespace FpsFrenzy.Core.Input;

[Flags]
public enum PlayerButtons : ushort
{
    None = 0,
    FireRight = 1 << 0,
    Fire = FireRight,
    AimDownSights = 1 << 1,
    Reload = 1 << 2,
    Jump = 1 << 3,
    Pause = 1 << 4,
    Restart = 1 << 5,
    FireLeft = 1 << 6,
    Interact = 1 << 7,
    Ability1 = 1 << 8,
    Ability2 = 1 << 9,
    SwapWeaponSet = 1 << 10,
}

public readonly record struct PlayerCommand(
    uint Tick,
    EntityId PlayerId,
    Vector2 Movement,
    Vector2 LookDelta,
    PlayerButtons Buttons,
    int WeaponSlot = -1)
{
    public bool Has(PlayerButtons button) => (Buttons & button) != 0;
    public int SelectedQuickbarSlot => WeaponSlot;
}
