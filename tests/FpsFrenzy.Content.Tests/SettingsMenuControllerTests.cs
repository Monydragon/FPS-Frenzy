using FpsFrenzy.Kni.Settings;
using Microsoft.Xna.Framework;

namespace FpsFrenzy.Content.Tests;

public sealed class SettingsMenuControllerTests
{
    private static readonly Rectangle SafeArea = new(0, 0, 1280, 720);

    [Fact]
    public void PointerActivationUsesTheSameLayoutAsTheRenderedRows()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenMain();
        MenuLayoutMetrics layout = MenuLayout.Create(
            SafeArea, SettingsMenuController.MainRows.Length, largeText: false, MenuPage.Main);
        Point settingsRow = layout.GetRowBounds(1).Center;

        MenuAction action = menu.Update(
            settings,
            new MenuInputSnapshot(MenuInputButtons.None, true, settingsRow, true),
            SafeArea);

        Assert.Equal(MenuAction.None, action);
        Assert.Equal(MenuPage.Settings, menu.Page);
        Assert.Equal(1, layout.HitTest(settingsRow));
    }

    [Fact]
    public void SettingsBackReturnsToTheMenuThatOpenedIt()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenSettings(MenuPage.Main);

        MenuAction action = menu.Update(
            settings,
            new MenuInputSnapshot(MenuInputButtons.Back),
            SafeArea);

        Assert.Equal(MenuAction.None, action);
        Assert.Equal(MenuPage.Main, menu.Page);
    }

    [Fact]
    public void HeldPauseProducesOnePauseAndOneResumeOnlyAfterRelease()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        MenuInputSnapshot pause = new(MenuInputButtons.Pause);

        Assert.Equal(MenuAction.Pause, menu.Update(settings, pause, SafeArea));
        Assert.Equal(MenuPage.Pause, menu.Page);
        Assert.Equal(MenuAction.None, menu.Update(settings, pause, SafeArea));
        Assert.Equal(MenuPage.Pause, menu.Page);

        menu.Update(settings, default, SafeArea);
        Assert.Equal(MenuAction.Resume, menu.Update(settings, pause, SafeArea));
        Assert.Equal(MenuPage.None, menu.Page);
    }

    [Fact]
    public void HeldEscapeCannotResumeAndImmediatelyReopenPause()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        MenuInputSnapshot escape = new(MenuInputButtons.Back);

        Assert.Equal(MenuAction.Pause, menu.Update(settings, escape, SafeArea));
        menu.Update(settings, default, SafeArea);
        Assert.Equal(MenuAction.Resume, menu.Update(settings, escape, SafeArea));
        Assert.Equal(MenuAction.None, menu.Update(settings, escape, SafeArea));
        Assert.Equal(MenuPage.None, menu.Page);
    }

    [Fact]
    public void GodModePointerToggleIsPersistableSettingsChange()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenSettings(MenuPage.Main);
        MenuLayoutMetrics layout = MenuLayout.Create(
            SafeArea, SettingsMenuController.SettingsRows.Length, largeText: false, MenuPage.Settings);
        Point godModeRow = layout.GetRowBounds(6).Center;

        MenuAction action = menu.Update(
            settings,
            new MenuInputSnapshot(MenuInputButtons.None, true, godModeRow, true),
            SafeArea);

        Assert.Equal(MenuAction.SettingsChanged, action);
        Assert.True(settings.GodMode);
    }
}
