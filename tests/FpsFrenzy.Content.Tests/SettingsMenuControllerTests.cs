using FpsFrenzy.Kni.Settings;
using FpsFrenzy.Core;
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

    [Theory]
    [InlineData(1280, 720, false)]
    [InlineData(1280, 720, true)]
    [InlineData(2400, 1080, false)]
    [InlineData(1920, 864, false)]
    public void MainMenuCardsRemainSpacedInsideTheSafeArea(int width, int height, bool largeText)
    {
        Rectangle safeArea = new(0, 0, width, height);
        MenuRowWindow window = MenuLayout.GetRowWindow(
            safeArea, SettingsMenuController.MainRows.Length, largeText, MenuPage.Main, selectedIndex: 0);
        MenuLayoutMetrics layout = MenuLayout.Create(safeArea, window.Count, largeText, MenuPage.Main);

        Assert.Equal(SettingsMenuController.MainRows.Length, window.Count);
        Assert.True(safeArea.Contains(layout.Panel));
        Assert.True(layout.Panel.Bottom <= safeArea.Bottom - 70);
        Assert.True(layout.RowHeight >= 48);
        for (int index = 1; index < layout.RowCount; index++)
        {
            Assert.Equal(layout.GetRowBounds(index - 1).Bottom, layout.GetRowBounds(index).Top);
        }
    }

    [Fact]
    public void WeaponPickupMenuSupportsControllerResolutionAndBackLeavesTheDrop()
    {
        EquipmentInstance offered = new()
        {
            Id = "offered",
            WeaponBaseId = "pulse-sidearm",
            DisplayName = "Pulse Sidearm",
            PrimarySlot = EquipmentSlot.RightHand,
            Rarity = ItemRarity.Epic,
            ItemPower = 30,
        };
        EquipmentInstance current = offered with
        {
            Id = "current",
            Rarity = ItemRarity.Common,
            ItemPower = 5,
        };
        PendingWeaponPickupDecision decision = new(new EntityId(44), 0, offered, current);
        SettingsMenuController menu = new();
        menu.OpenWeaponPickup(decision);

        Assert.Equal(MenuPage.WeaponPickup, menu.Page);
        Assert.Equal(3, menu.GetRows().Count);
        Assert.Contains("+25 IP", menu.GetSupplementalValue(0));
        MenuAction action = menu.Update(
            new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuAction.WeaponPickupResolved, action);
        Assert.Equal(WeaponPickupDecisionAction.Replace, menu.SelectedWeaponPickupAction);

        SettingsMenuController leaveMenu = new();
        leaveMenu.OpenWeaponPickup(decision);
        action = leaveMenu.Update(
            new GameSettings(), new MenuInputSnapshot(MenuInputButtons.Back), SafeArea);
        Assert.Equal(MenuAction.WeaponPickupResolved, action);
        Assert.Equal(WeaponPickupDecisionAction.Leave, leaveMenu.SelectedWeaponPickupAction);
    }

    [Fact]
    public void MainMenuDebugLabStartsTheSandboxDirectly()
    {
        SettingsMenuController menu = new();
        menu.OpenMain();
        menu.Update(
            new GameSettings(),
            new MenuInputSnapshot(MenuInputButtons.Accept),
            SafeArea);
        Assert.Equal(MenuPage.Play, menu.Page);
        int labIndex = menu.GetRows().ToList().IndexOf("DEBUG LAB");
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
    public void StartingNewArenaWithCheckpointRequiresExplicitConfirmation()
    {
        SettingsMenuController menu = new();
        menu.ConfigureProfile(ProfileData.CreateDefault(), [], hasCheckpoint: true);
        menu.OpenMain();
        GameSettings settings = new();
        menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);
        menu.Update(settings, default, SafeArea);
        int newArenaIndex = menu.GetRows().ToList().IndexOf("NEW ARENA");
        PressDown(menu, newArenaIndex);

        Assert.Equal(MenuAction.None, menu.Update(
            settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea));
        Assert.Equal(MenuPage.ConfirmNewRun, menu.Page);
        Assert.Equal(1, menu.SelectedIndex);

        menu.Update(settings, default, SafeArea);
        menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Up), SafeArea);
        menu.Update(settings, default, SafeArea);
        MenuCommand command = menu.UpdateCommand(
            settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuAction.StartRun, command.Type);
        Assert.Equal(GameMode.Arena, command.Mode);
        Assert.Null(command.Seed);
    }

    [Fact]
    public void AdventureSeedSupportsDirectTypingKeypadBoundsAndTypedCommand()
    {
        SettingsMenuController menu = new();
        GameSettings settings = new();
        menu.OpenAdventureSetup(999_999_999);

        foreach (int digit in new[] { 2, 1, 4, 7, 4, 8, 3, 6, 4, 7 })
        {
            menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.None, Digit: digit), SafeArea);
            menu.Update(settings, default, SafeArea);
        }

        Assert.Equal(int.MaxValue, menu.AdventureSeed);
        menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.None, Digit: 8), SafeArea);
        Assert.Equal(int.MaxValue, menu.AdventureSeed);

        menu.Update(settings, default, SafeArea);
        PressDown(menu, 1);
        menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);
        Assert.Equal(MenuPage.SeedKeypad, menu.Page);
        menu.Update(settings, default, SafeArea);
        menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);
        Assert.Equal(1, menu.AdventureSeed);

        menu.OpenAdventureSetup(77);
        PressDown(menu, 3);
        MenuCommand command = menu.UpdateCommand(
            settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuAction.StartRun, command.Type);
        Assert.Equal(GameMode.Adventure, command.Mode);
        Assert.Equal(77, command.Seed);
    }

    [Fact]
    public void StartingNewAdventureWithCheckpointRequiresExplicitConfirmation()
    {
        SettingsMenuController menu = new();
        menu.ConfigureProfile(ProfileData.CreateDefault(), [], hasAdventureCheckpoint: true);
        menu.OpenAdventureSetup(4_242);
        GameSettings settings = new();
        PressDown(menu, 3);

        Assert.Equal(MenuAction.None, menu.Update(
            settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea));
        Assert.Equal(MenuPage.ConfirmNewRun, menu.Page);
        Assert.Equal(1, menu.SelectedIndex);
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

        Assert.Equal(MenuAction.Quit, action);
        Assert.Equal(MenuPage.Main, menu.Page);
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
        for (int index = 0; index < 7; index++)
        {
            menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Down), SafeArea);
            menu.Update(settings, default, SafeArea);
        }
        MenuRowWindow window = MenuLayout.GetRowWindow(
            SafeArea, SettingsMenuController.SettingsRows.Length, largeText: false,
            MenuPage.Settings, menu.SelectedIndex);
        MenuLayoutMetrics layout = MenuLayout.Create(
            SafeArea, window.Count, largeText: false, MenuPage.Settings);
        Point godModeRow = layout.GetRowBounds(7 - window.Start).Center;

        MenuAction action = menu.Update(
            settings,
            new MenuInputSnapshot(MenuInputButtons.None, true, godModeRow, true),
            SafeArea);

        Assert.Equal(MenuAction.SettingsChanged, action);
        Assert.True(settings.GodMode);
    }

    [Fact]
    public void ControllerBindingsPageCapturesButtonsAndSwapsConflicts()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenControls(MenuPage.Main);

        menu.Update(settings, new MenuInputSnapshot(
            MenuInputButtons.Accept, HeldGamepadButton: GamepadBindingButton.A), SafeArea);
        Assert.Equal(GamepadBindingAction.Jump, menu.BindingCaptureAction);
        menu.Update(settings, default, SafeArea);
        MenuAction action = menu.Update(settings,
            new MenuInputSnapshot(MenuInputButtons.None, HeldGamepadButton: GamepadBindingButton.X), SafeArea);

        Assert.Equal(MenuAction.SettingsChanged, action);
        Assert.Null(menu.BindingCaptureAction);
        Assert.Equal(GamepadBindingButton.X, settings.ControllerBindings.Jump);
        Assert.Equal(GamepadBindingButton.A, settings.ControllerBindings.Interact);
    }

    [Fact]
    public void SettingsMenuNavigatesToControllerBindingsThroughTheScrolledRows()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenSettings(MenuPage.Main);
        for (int index = 0; index < 8; index++)
        {
            menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Down), SafeArea);
            menu.Update(settings, default, SafeArea);
        }

        menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.Accept), SafeArea);

        Assert.Equal(MenuPage.Controls, menu.Page);
        Assert.Equal(0, menu.SelectedIndex);
        Assert.Equal(10, menu.GetRows().Count);
    }

    [Fact]
    public void CrowdedSettingsUseAViewportWithWheelAndClickableScrollControls()
    {
        GameSettings settings = new();
        SettingsMenuController menu = new();
        menu.OpenSettings(MenuPage.Main);
        MenuRowWindow window = MenuLayout.GetRowWindow(
            SafeArea, menu.GetRows().Count, largeText: false, MenuPage.Settings, selectedIndex: 0);
        MenuLayoutMetrics layout = MenuLayout.Create(SafeArea, window.Count, largeText: false, MenuPage.Settings);

        Assert.Equal(7, window.Count);
        Assert.True(layout.Panel.Bottom <= SafeArea.Bottom - 70);

        menu.Update(settings, new MenuInputSnapshot(MenuInputButtons.None, ScrollDirection: 1), SafeArea);
        Assert.Equal(1, menu.SelectedIndex);
        menu.Update(settings, default, SafeArea);
        menu.Update(settings, new MenuInputSnapshot(
            MenuInputButtons.None, true, MenuLayout.GetScrollDownBounds(layout).Center, true), SafeArea);
        Assert.Equal(2, menu.SelectedIndex);
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
        Assert.Contains(rows, row => row.StartsWith("SLOT 0 EXPERIMENTAL  RIGHT", StringComparison.Ordinal));
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

    [Fact]
    public void DenseProfilePagesScrollInsideThePanelWithoutCoveringHelp()
    {
        MenuRowWindow first = MenuLayout.GetRowWindow(
            SafeArea, 30, largeText: false, MenuPage.Loadout, selectedIndex: 0);
        MenuRowWindow last = MenuLayout.GetRowWindow(
            SafeArea, 30, largeText: false, MenuPage.Loadout, selectedIndex: 29);
        MenuLayoutMetrics layout = MenuLayout.Create(
            SafeArea, last.Count, largeText: false, MenuPage.Loadout);

        Assert.True(first.Count < 30);
        Assert.Equal(0, first.Start);
        Assert.True(last.Start > 0);
        Assert.Equal(30, last.Start + last.Count);
        Assert.True(layout.Panel.Bottom <= SafeArea.Bottom - 70);
        Assert.True(layout.Panel.Top > MenuLayout.GetProfileTabBounds(SafeArea, 0).Bottom);

        MenuRowWindow pauseWindow = MenuLayout.GetRowWindow(
            SafeArea, SettingsMenuController.PauseRows.Length, largeText: false, MenuPage.Pause, selectedIndex: 0);
        MenuLayoutMetrics pauseLayout = MenuLayout.Create(
            SafeArea, pauseWindow.Count, largeText: false, MenuPage.Pause);
        int expandedMapLeft = SafeArea.Right - Math.Min(340, SafeArea.Width / 3) - 22;
        Assert.True(pauseWindow.Count < SettingsMenuController.PauseRows.Length);
        Assert.True(pauseLayout.Panel.Right <= expandedMapLeft);
        Assert.True(pauseLayout.Panel.Bottom <= SafeArea.Bottom - 70);
    }

    [Fact]
    public void AdventurePauseOffersAStageRestartInsteadOfStartingOver()
    {
        SettingsMenuController menu = new();
        menu.SetActiveMode(GameMode.Adventure);
        menu.OpenPause();

        Assert.Equal("RESTART CURRENT STAGE", menu.GetRows()[10]);
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
