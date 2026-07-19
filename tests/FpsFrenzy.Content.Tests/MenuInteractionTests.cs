using FpsFrenzy.Kni.Settings;
using Microsoft.Xna.Framework;

namespace FpsFrenzy.Content.Tests;

public sealed class MenuInteractionTests
{
    [Fact]
    public void PointerSelectionIntentRequiresArrivalMovementOrActivation()
    {
        Point position = new(420, 240);
        MenuInputSnapshot previous = new(MenuInputButtons.None, true, position, false);

        Assert.False(new MenuInputSnapshot(
            MenuInputButtons.None,
            true,
            position,
            false).HasPointerSelectionIntent(previous));
        Assert.True(new MenuInputSnapshot(
            MenuInputButtons.None,
            true,
            new Point(position.X + 1, position.Y),
            false).HasPointerSelectionIntent(previous));
        Assert.True(new MenuInputSnapshot(
            MenuInputButtons.None,
            true,
            position,
            true).HasPointerSelectionIntent(previous));
        Assert.True(new MenuInputSnapshot(
            MenuInputButtons.None,
            true,
            position,
            false).HasPointerSelectionIntent(default));
    }
}
