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
        Point settingsRow = layout.GetRowBounds(3).Center;

        MenuAction action = menu.Update(
            settings,
            new MenuInputSnapshot(MenuInputButtons.None, true, settingsRow, true),
            SafeArea);

        Assert.Equal(MenuAction.None, action);
        Assert.Equal(MenuPage.Settings, menu.Page);
        Assert.Equal(3, layout.HitTest(settingsRow));
    }

    [Fact]
    public void StationaryMouseDoesNotOverrideKeyboardOrGamepadSelection()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenMain();
        MenuLayoutMetrics layout = MenuLayout.Create(
            SafeArea, SettingsMenuController.MainRows.Length, largeText: false, MenuPage.Main);
        Point stationaryPointer = layout.GetRowBounds(3).Center;
        MenuInputSnapshot pointer = new(MenuInputButtons.None, true, stationaryPointer, false);

        menu.Update(settings, pointer, SafeArea);
        Assert.Equal(3, menu.SelectedIndex);

        menu.Update(settings, pointer with { Buttons = MenuInputButtons.Down }, SafeArea);
        Assert.Equal(4, menu.SelectedIndex);
        menu.Update(settings, pointer, SafeArea);
        Assert.Equal(4, menu.SelectedIndex);

        MenuAction action = menu.Update(
            settings,
            pointer with { Buttons = MenuInputButtons.Accept },
            SafeArea);

        Assert.Equal(MenuAction.None, action);
        Assert.Equal(MenuPage.Accessibility, menu.Page);
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
        Point godModeRow = layout.GetRowBounds(7).Center;

        MenuAction action = menu.Update(
            settings,
            new MenuInputSnapshot(MenuInputButtons.None, true, godModeRow, true),
            SafeArea);

        Assert.Equal(MenuAction.SettingsChanged, action);
        Assert.True(settings.GodMode);
    }

    [Fact]
    public void TouchCanPositionContinuousSettingsInEitherDirection()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenSettings(MenuPage.Main);
        MenuLayoutMetrics layout = MenuLayout.Create(
            SafeArea, SettingsMenuController.SettingsRows.Length, largeText: false, MenuPage.Settings);
        Rectangle musicRow = layout.GetRowBounds(1);

        MenuAction increase = menu.Update(
            settings,
            new MenuInputSnapshot(
                MenuInputButtons.None,
                true,
                new Point(musicRow.Right - 1, musicRow.Center.Y),
                true),
            SafeArea);
        Assert.Equal(MenuAction.SettingsChanged, increase);
        Assert.Equal(1f, settings.MusicVolume);

        menu.Update(settings, default, SafeArea);
        MenuAction decrease = menu.Update(
            settings,
            new MenuInputSnapshot(
                MenuInputButtons.None,
                true,
                new Point(musicRow.Left, musicRow.Center.Y),
                true),
            SafeArea);

        Assert.Equal(MenuAction.SettingsChanged, decrease);
        Assert.Equal(0f, settings.MusicVolume);
    }

    [Fact]
    public void LoadoutOnlyEquipsUnlockedStartingWeapons()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        FpsFrenzy.Kni.Progression.ProfileData profile =
            FpsFrenzy.Kni.Progression.ProfileData.CreateDefault();
        menu.ConfigureProfile(profile,
        [
            ("pulse-sidearm", "Pulse Sidearm"),
            ("arc-cannon", "Arc Cannon"),
        ]);
        menu.OpenLoadout();

        menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Down), SafeArea);
        menu.Update(settings, default, SafeArea);
        MenuAction lockedAction = menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);
        Assert.Equal(MenuAction.None, lockedAction);
        Assert.Equal("pulse-sidearm", menu.StartingWeaponId);

        profile.UnlockStartingWeapon("arc-cannon");
        menu.Update(settings, default, SafeArea);
        MenuAction equippedAction = menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);
        Assert.Equal(MenuAction.StartingWeaponChanged, equippedAction);
        Assert.Equal("arc-cannon", menu.StartingWeaponId);
    }

    [Fact]
    public void FirstRunBriefingHasExplicitBeginAction()
    {
        SettingsMenuController menu = new();
        menu.OpenTutorial();

        MenuAction action = menu.Update(
            new GameSettings(),
            new MenuInputSnapshot(MenuInputButtons.Accept),
            SafeArea);

        Assert.Equal(MenuAction.BeginRun, action);
        Assert.Equal(MenuPage.None, menu.Page);
    }

    [Fact]
    public void RewardSelectionIsMandatoryAndReturnsChosenUpgrade()
    {
        SettingsMenuController menu = new();
        GameSettings settings = new();
        menu.OpenReward(
        [
            ("calibrated-cells", "Calibrated Cells", "+12% damage."),
            ("reinforced-shell", "Reinforced Shell", "+20 maximum health."),
            ("field-loader", "Field Loader", "Faster reload and recovery."),
        ]);

        Assert.Equal(MenuAction.None, menu.Update(
            settings,
            new MenuInputSnapshot(MenuInputButtons.Back),
            SafeArea));
        Assert.Equal(MenuPage.Reward, menu.Page);

        menu.Update(settings, default, SafeArea);
        menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Down), SafeArea);
        menu.Update(settings, default, SafeArea);
        Assert.Equal(MenuAction.UpgradeSelected, menu.Update(
            settings,
            new MenuInputSnapshot(MenuInputButtons.Accept),
            SafeArea));
        Assert.Equal("reinforced-shell", menu.SelectedUpgradeId);
        Assert.Equal(MenuPage.None, menu.Page);
    }

    [Fact]
    public void TouchPauseButtonDoesNotCoverEncounterObjective()
    {
        Rectangle objectiveBlock = new(SafeArea.Left + 24, SafeArea.Top + 68, 222, 25);

        Rectangle pauseButton = MenuLayout.GetPauseButtonBounds(SafeArea);

        Assert.False(pauseButton.Intersects(objectiveBlock));
        Assert.True(SafeArea.Contains(pauseButton));
    }
}
