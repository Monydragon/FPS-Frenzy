using FpsFrenzy.Kni.Input;
using Microsoft.Xna.Framework.Input;

namespace FpsFrenzy.Content.Tests;

public sealed class GamepadGameplayBindingsTests
{
    [Fact]
    public void ShouldersMapToAbilitiesAndRightStickMapsToDedicatedFocus()
    {
        GamepadGameplayActions actions = GamepadGameplayBindings.Resolve(
            Buttons.LeftShoulder | Buttons.RightShoulder | Buttons.RightStick,
            0);

        Assert.True(actions.Ability1);
        Assert.True(actions.Ability2);
        Assert.True(actions.DedicatedFocus);
    }

    [Theory]
    [InlineData(Buttons.Y, 1)]
    [InlineData(Buttons.B, -1)]
    [InlineData(Buttons.DPadRight, 1)]
    [InlineData(Buttons.DPadLeft, -1)]
    public void FaceButtonsAndDPadCycleWeaponsInBothDirections(Buttons button, int expectedDirection)
    {
        Assert.Equal(expectedDirection, GamepadGameplayBindings.Resolve(button, 0)
            .WeaponCycleDirection);
        Assert.Equal(0, GamepadGameplayBindings.Resolve(button, button)
            .WeaponCycleDirection);
    }

    [Fact]
    public void DPadUpIsTheGameplayInteractButton()
    {
        Assert.True(GamepadGameplayBindings.Resolve(Buttons.DPadUp, 0).Interact);
    }
}
