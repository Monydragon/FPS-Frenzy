using FpsFrenzy.Kni.Settings;
using Microsoft.Xna.Framework;

namespace FpsFrenzy.Content.Tests;

public sealed class MenuInteractionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainMenuUsesAContainedRightSideCommandRail(bool largeText)
    {
        Rectangle safeArea = new(0, 0, 1280, 720);

        MenuLayoutMetrics layout = MenuLayout.Create(safeArea, 6, largeText, MenuPage.Main);

        Assert.True(layout.Panel.Center.X > safeArea.Center.X);
        Assert.True(layout.Panel.Left >= safeArea.Center.X);
        Assert.True(layout.Panel.Right <= safeArea.Right);
        Assert.True(layout.Panel.Top >= safeArea.Top);
        Assert.True(layout.Panel.Bottom < safeArea.Bottom - 70);
        Assert.Equal(6, MenuLayout.GetRowWindow(
            safeArea, 6, largeText, MenuPage.Main, selectedIndex: 5).Count);
    }

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
