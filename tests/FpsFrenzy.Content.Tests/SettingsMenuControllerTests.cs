using FpsFrenzy.Kni.Settings;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Progression;
using Microsoft.Xna.Framework;

namespace FpsFrenzy.Content.Tests;

public sealed class SettingsMenuControllerTests
{
    private static readonly Rectangle SafeArea = new(0, 0, 1280, 720);

    [Fact]
    public void DifficultyPageSelectsACombatPackageWithoutChangingThreatTier()
    {
        GameSettings settings = new();
        ProfileData profile = ProfileData.CreateDefault();
        SettingsMenuController menu = new();
        menu.ConfigureProfile(profile, []);
        menu.OpenDifficulty();
        MenuLayoutMetrics layout = MenuLayout.Create(
            SafeArea, menu.GetRows().Count, largeText: false, MenuPage.Difficulty);
        Point veryHardRow = layout.GetRowBounds(4).Center;

        MenuAction action = menu.Update(settings,
            new MenuInputSnapshot(MenuInputButtons.None, true, veryHardRow, true), SafeArea);

        Assert.Equal(MenuAction.ProfileChanged, action);
        Assert.Equal(DifficultyMode.VeryHard, profile.SelectedDifficulty);
        Assert.Equal(ThreatTier.TierI, profile.SelectedThreatTier);
    }

    [Fact]
    public void PointerActivationUsesTheSameLayoutAsTheRenderedRows()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenMain();
        MenuLayoutMetrics layout = MenuLayout.Create(
            SafeArea, SettingsMenuController.MainRows.Length, largeText: false, MenuPage.Main);
        int settingsIndex = Array.IndexOf(SettingsMenuController.MainRows, "SETTINGS");
        Point settingsRow = layout.GetRowBounds(settingsIndex).Center;

        MenuAction action = menu.Update(
            settings,
            new MenuInputSnapshot(MenuInputButtons.None, true, settingsRow, true),
            SafeArea);

        Assert.Equal(MenuAction.None, action);
        Assert.Equal(MenuPage.Settings, menu.Page);
        Assert.Equal(settingsIndex, layout.HitTest(settingsRow));
    }

    [Fact]
    public void MainMenuDebugLabStartsTheSandboxDirectly()
    {
        SettingsMenuController menu = new();
        menu.OpenMain();
        int labIndex = Array.IndexOf(SettingsMenuController.MainRows, "DEBUG LAB");
        Assert.True(labIndex >= 0);
        PressDown(menu, labIndex);

        MenuAction action = menu.Update(
            new GameSettings(),
            new MenuInputSnapshot(MenuInputButtons.Accept),
            SafeArea);

        Assert.Equal(MenuAction.StartDebugLab, action);
        Assert.Equal(MenuPage.None, menu.Page);
    }

    [Fact]
    public void StationaryMouseDoesNotOverrideKeyboardOrGamepadSelection()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenMain();
        MenuLayoutMetrics layout = MenuLayout.Create(
            SafeArea, SettingsMenuController.MainRows.Length, largeText: false, MenuPage.Main);
        int settingsIndex = Array.IndexOf(SettingsMenuController.MainRows, "SETTINGS");
        Point stationaryPointer = layout.GetRowBounds(settingsIndex).Center;
        MenuInputSnapshot pointer = new(MenuInputButtons.None, true, stationaryPointer, false);

        menu.Update(settings, pointer, SafeArea);
        Assert.Equal(settingsIndex, menu.SelectedIndex);

        menu.Update(settings, pointer with { Buttons = MenuInputButtons.Down }, SafeArea);
        Assert.Equal(settingsIndex + 1, menu.SelectedIndex);
        menu.Update(settings, pointer, SafeArea);
        Assert.Equal(settingsIndex + 1, menu.SelectedIndex);

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
    public void LoadoutAllowsEveryArmoryIssueStartingWeapon()
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
    public void SlotLoadoutCanEquipASecondOneHandedItemIntoTheLeftHand()
    {
        ContentCatalog catalog = LoadCatalog();
        ProfileData profile = ProfileData.CreateDefault();
        EquipmentInstance second = new()
        {
            Id = "test-second-pulse",
            WeaponBaseId = "pulse-sidearm",
            DisplayName = "Pulse Sidearm Mk II",
            PrimarySlot = EquipmentSlot.LeftHand,
            Rarity = ItemRarity.Rare,
            ItemPower = 8,
        };
        profile.Stash.Add(second);
        SettingsMenuController menu = new();
        menu.ConfigureProfile(profile, [], catalog: catalog);
        menu.OpenLoadout();

        PressDown(menu, 1); // Set A Left Hand slot.
        menu.Update(new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);
        menu.Update(new GameSettings(), default, SafeArea);
        Assert.Equal(MenuPage.Armory, menu.Page);
        // Owned gear appears before Common armory issues; the strongest owned item is selected first.
        MenuAction action = menu.Update(new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuAction.ProfileChanged, action);
        Assert.Equal(second.Id, profile.StarterWeaponSetA.LeftHand?.ItemInstanceId);
        Assert.Equal("pulse-sidearm", profile.StarterWeaponSetA.RightHand?.WeaponBaseId);
    }

    [Fact]
    public void CharacterPageSpendsTalentPointsAndSupportsBranchSelection()
    {
        ContentCatalog catalog = LoadCatalog();
        ProfileData profile = ProfileData.CreateDefault();
        profile.UnspentTalentPoints = 1;
        SettingsMenuController menu = new();
        menu.ConfigureProfile(profile, [], catalog: catalog);
        menu.OpenCharacter();

        PressDown(menu, 4);
        MenuAction action = menu.Update(new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuAction.ProfileChanged, action);
        Assert.Equal(0, profile.UnspentTalentPoints);
        Assert.Equal(1, profile.TalentRanks["arsenal-1"]);
    }

    [Fact]
    public void MasteredActiveAbilityCanBeAddedToOneOfTwoCooldownSlots()
    {
        ContentCatalog catalog = LoadCatalog();
        ProfileData profile = ProfileData.CreateDefault();
        profile.AbilityMastery.Abilities["barrier-pulse"] = new AbilityProgress
        {
            AbilityPoints = catalog.Abilities["barrier-pulse"].RequiredAbilityPoints,
            IsMastered = true,
        };
        SettingsMenuController menu = new();
        menu.ConfigureProfile(profile, [], catalog: catalog);
        menu.OpenAbilities();
        int barrierRow = menu.GetRows().ToList().FindIndex(row => row.Contains("BARRIER PULSE", StringComparison.Ordinal));

        PressDown(menu, barrierRow);
        MenuAction action = menu.Update(new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuAction.ProfileChanged, action);
        Assert.Contains("barrier-pulse", profile.AbilityMastery.EquippedActiveAbilityIds);
        Assert.True(profile.AbilityMastery.EquippedActiveAbilityIds.Count <= 2);
    }

    [Fact]
    public void PausedCharacterPagesReturnToPauseAndExposeTenWeaponPresets()
    {
        ContentCatalog catalog = LoadCatalog();
        ProfileData profile = ProfileData.CreateDefault();
        SettingsMenuController menu = new();
        menu.ConfigureProfile(profile, [], catalog: catalog);
        menu.OpenPause();
        int craftingRow = Array.IndexOf(SettingsMenuController.PauseRows, "CRAFTING");
        PressDown(menu, craftingRow);

        menu.Update(new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);
        Assert.Equal(MenuPage.Crafting, menu.Page);
        menu.Update(new GameSettings(), default, SafeArea);
        menu.Update(new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Back), SafeArea);
        Assert.Equal(MenuPage.Pause, menu.Page);

        menu.OpenLoadout();
        IReadOnlyList<string> rows = menu.GetRows();
        Assert.Equal((WeaponQuickbarLoadout.SlotCount * 2) + 9 + 1, rows.Count);
        Assert.Contains(rows, row => row.StartsWith("SLOT 0  RIGHT", StringComparison.Ordinal));
    }

    [Fact]
    public void CharacterMenuTabsSupportShouldersKeysPointerAndTouchLayout()
    {
        SettingsMenuController menu = new();
        menu.ConfigureProfile(ProfileData.CreateDefault(), [], catalog: LoadCatalog());
        menu.OpenPause();
        menu.OpenCharacter();

        menu.Update(new GameSettings(), new MenuInputSnapshot(MenuInputButtons.NextTab), SafeArea);
        Assert.Equal(MenuPage.Inventory, menu.Page);
        menu.Update(new GameSettings(), default, SafeArea);
        Point statsTab = MenuLayout.GetProfileTabBounds(SafeArea, 6).Center;
        menu.Update(new GameSettings(),
            new MenuInputSnapshot(MenuInputButtons.None, true, statsTab, true), SafeArea);
        Assert.Equal(MenuPage.Stats, menu.Page);
        menu.Update(new GameSettings(), default, SafeArea);
        menu.Update(new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Back), SafeArea);
        Assert.Equal(MenuPage.Pause, menu.Page);
    }

    [Fact]
    public void CraftingMenuInfusesAnExactBaseDuplicateAndSpendsWalletMaterials()
    {
        ContentCatalog catalog = LoadCatalog();
        ProfileData profile = ProfileData.CreateDefault();
        profile.HighestUnlockedThreatTier = ThreatTier.TierII;
        profile.Materials.Scrap = 20;
        profile.Materials.Components = 10;
        EquipmentInstance target = new()
        {
            Id = "craft-target",
            WeaponBaseId = "ion-repeater",
            DisplayName = "Ion Repeater Alpha",
            PrimarySlot = EquipmentSlot.RightHand,
            Rarity = ItemRarity.Rare,
            ItemPower = 5,
        };
        EquipmentInstance donor = target with
        {
            Id = "craft-donor",
            DisplayName = "Ion Repeater Beta",
            ItemPower = 15,
        };
        profile.Stash.AddRange([target, donor]);
        SettingsMenuController menu = new();
        menu.ConfigureProfile(profile, [], catalog: catalog);
        menu.OpenCrafting();
        int targetRow = menu.GetRows().ToList().FindIndex(row =>
            row.Contains("ION REPEATER ALPHA", StringComparison.Ordinal));
        Assert.True(targetRow >= 2);
        PressDown(menu, targetRow);
        menu.Update(new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);
        Assert.Equal(MenuPage.CraftingItem, menu.Page);

        menu.Update(new GameSettings(), default, SafeArea);
        PressDown(menu, 1);
        MenuAction action = menu.Update(
            new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuAction.ProfileChanged, action);
        Assert.DoesNotContain(profile.Stash, item => item.Id == donor.Id);
        Assert.Equal(15, profile.Stash.Single(item => item.Id == target.Id).ItemPower);
        Assert.Equal(10, profile.Materials.Scrap);
        Assert.Equal(8, profile.Materials.Components);
    }

    [Fact]
    public void BatchDismantlePreviewsAndRequiresASecondConfirmation()
    {
        ContentCatalog catalog = LoadCatalog();
        ProfileData profile = ProfileData.CreateDefault();
        EquipmentInstance disposable = new()
        {
            Id = "batch-disposable",
            WeaponBaseId = "ion-repeater",
            DisplayName = "Disposable Ion Repeater",
            PrimarySlot = EquipmentSlot.RightHand,
            Rarity = ItemRarity.Rare,
            ItemPower = 12,
        };
        EquipmentInstance protectedItem = disposable with
        {
            Id = "batch-favorite",
            DisplayName = "Favorite Ion Repeater",
            IsFavorite = true,
        };
        profile.Stash.AddRange([disposable, protectedItem]);
        SettingsMenuController menu = new();
        menu.ConfigureProfile(profile, [], catalog: catalog);
        menu.OpenInventory();
        int batchRow = menu.GetRows().Count - 2;
        PressDown(menu, batchRow);

        MenuAction preview = menu.Update(
            new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuAction.None, preview);
        Assert.Contains(profile.Stash, item => item.Id == disposable.Id);
        Assert.StartsWith("CONFIRM DISMANTLE", menu.GetRows()[batchRow], StringComparison.Ordinal);

        menu.Update(new GameSettings(), default, SafeArea);
        MenuAction dismantled = menu.Update(
            new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuAction.ProfileChanged, dismantled);
        Assert.DoesNotContain(profile.Stash, item => item.Id == disposable.Id);
        Assert.Contains(profile.Stash, item => item.Id == protectedItem.Id);
        Assert.True(profile.Materials.Scrap > 0);
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

    private static ContentCatalog LoadCatalog() => ContentCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "Content", "Data"));

    private static void PressDown(SettingsMenuController menu, int count)
    {
        GameSettings settings = new();
        for (int index = 0; index < count; index++)
        {
            menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Down), SafeArea);
            menu.Update(settings, default, SafeArea);
        }
    }
}
