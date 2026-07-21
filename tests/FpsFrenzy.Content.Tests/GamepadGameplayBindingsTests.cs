using FpsFrenzy.Kni.Input;
using FpsFrenzy.Kni.Settings;
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
    public void DefaultFaceButtonsCycleWeaponsInBothDirections(Buttons button, int expectedDirection)
    {
        Assert.Equal(expectedDirection, GamepadGameplayBindings.Resolve(button, 0)
            .WeaponCycleDirection);
        Assert.Equal(0, GamepadGameplayBindings.Resolve(button, button)
            .WeaponCycleDirection);
    }

    [Fact]
    public void XSquareActivatesAndAJumpRemainsDedicated()
    {
        GamepadGameplayActions actions = GamepadGameplayBindings.Resolve(Buttons.X | Buttons.A, 0);

        Assert.True(actions.Interact);
        Assert.True(actions.Jump);
        Assert.False(actions.Reload);
    }

    [Fact]
    public void ReloadDefaultsToDPadDownInsteadOfConflictingWithActivate()
    {
        GamepadGameplayActions actions = GamepadGameplayBindings.Resolve(Buttons.DPadDown, 0);

        Assert.True(actions.Reload);
        Assert.False(actions.Interact);
    }

    [Fact]
    public void CustomBindingsDriveTheSameGameplayActions()
    {
        GamepadControlBindings bindings = new();
        bindings.AssignWithSwap(GamepadBindingAction.Interact, GamepadBindingButton.DPadUp);
        GamepadGameplayActions actions = GamepadGameplayBindings.Resolve(Buttons.DPadUp, 0, bindings);

        Assert.True(actions.Interact);
        Assert.False(actions.Jump);
    }
}
