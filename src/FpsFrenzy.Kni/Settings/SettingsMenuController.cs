using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Progression;
using Microsoft.Xna.Framework;

namespace FpsFrenzy.Kni.Settings;

public enum MenuPage
{
    None,
    Main,
    Pause,
    Loadout,
    Armory,
    Character,
    Inventory,
    InventoryItem,
    Abilities,
    Proficiencies,
    Crafting,
    CraftingItem,
    Stats,
    Difficulty,
    ThreatTier,
    Recovery,
    Records,
    Tutorial,
    Reward,
    Settings,
    Accessibility,
    Results,
}

public enum MenuAction
{
    None,
    Pause,
    Resume,
    StartRun,
    BeginRun,
    ContinueRun,
    Restart,
    ReturnToMain,
    Quit,
    SettingsChanged,
    StartingWeaponChanged,
    UpgradeSelected,
    ProfileChanged,
    RecoveryItemSelected,
    RecoveryContinue,
    StartDebugLab,
}

public sealed class SettingsMenuController
{
    private static readonly MenuPage[] ProfileTabs =
    [
        MenuPage.Character, MenuPage.Inventory, MenuPage.Loadout, MenuPage.Abilities,
        MenuPage.Proficiencies, MenuPage.Crafting, MenuPage.Stats,
    ];
    private sealed record ArmoryChoice(WeaponDefinition Weapon, EquipmentInstance? Item);
    public static readonly string[] MainRows =
    [
        "START NEW RUN", "CHARACTER", "INVENTORY", "ABILITIES", "PROFICIENCIES", "DIFFICULTY", "THREAT TIER",
        "LOADOUT", "DEBUG LAB", "RECORDS", "SETTINGS", "ACCESSIBILITY", "QUIT TO DESKTOP",
    ];
    private static readonly string[] MainRowsWithContinue = ["CONTINUE RUN", .. MainRows];
    public static readonly string[] PauseRows =
    [
        "RESUME", "CHARACTER", "INVENTORY", "LOADOUT", "ABILITIES", "PROFICIENCIES", "CRAFTING", "STATS",
        "SETTINGS", "ACCESSIBILITY", "RESTART STANDARD RUN", "MAIN MENU", "QUIT TO DESKTOP",
    ];
    public static readonly string[] SettingsRows = ["MASTER VOLUME", "MUSIC VOLUME", "SFX VOLUME", "MOUSE SENSITIVITY", "GAMEPAD SENSITIVITY", "FIELD OF VIEW", "FRAME RATE", "GOD MODE", "BACK"];
    public static readonly string[] AccessibilityRows = ["REDUCED FLASH", "SCREEN SHAKE", "CAMERA BOB", "HIGH CONTRAST RETICLE", "LARGE HUD TEXT", "SUBTITLES", "TOGGLE ADS", "COLOR VISION", "BACK"];
    public static readonly string[] ResultsRows = ["PLAY AGAIN", "MAIN MENU", "QUIT TO DESKTOP"];
    public static readonly string[] RecordsRows = ["BACK"];
    public static readonly string[] TutorialRows = ["BEGIN RUN", "BACK"];

    private MenuInputSnapshot _previousInput;
    private MenuPage _returnPage = MenuPage.Pause;
    private readonly List<string> _loadoutRows = [];
    private readonly List<string> _loadoutWeaponIds = [];
    private readonly List<string> _rewardRows = [];
    private readonly List<string> _rewardUpgradeIds = [];
    private readonly List<string> _rewardDescriptions = [];
    private readonly List<string> _recoveryRows = [];
    private readonly List<string> _recoveryItemIds = [];
    private const int InventoryPageSize = 12;
    private const int AbilityPageSize = 12;
    private const int ArmoryPageSize = 10;
    private static readonly EquipmentSlot[] LoadoutEquipmentSlots =
    [
        EquipmentSlot.Head, EquipmentSlot.Chest, EquipmentSlot.Hands, EquipmentSlot.Legs,
        EquipmentSlot.Feet, EquipmentSlot.Accessory1, EquipmentSlot.Accessory2,
        EquipmentSlot.Ring1, EquipmentSlot.Ring2,
    ];
    private ProfileData? _profile;
    private ContentCatalog? _catalog;
    private bool _hasCheckpoint;
    private int _inventoryPage;
    private EquipmentSlot? _inventorySlotFilter;
    private ItemRarity? _inventoryRarityFilter;
    private int _inventoryMinimumPower = 1;
    private string? _selectedInventoryItemId;
    private string? _selectedCraftingItemId;
    private bool _confirmLegendaryDismantle;
    private bool _confirmBatchDismantle;
    private MenuPage _craftingReturnPage = MenuPage.Crafting;
    private int _abilityPage;
    private TalentBranch _talentBranch = TalentBranch.Arsenal;
    private int _armoryPage;
    private int _armorySetIndex;
    private EquipmentSlot _armoryHand = EquipmentSlot.RightHand;

    public MenuPage Page { get; private set; }
    public int SelectedIndex { get; private set; }
    public bool IsOpen => Page != MenuPage.None;
    public ProfileData? Profile => _profile;
    public string StartingWeaponId => _profile?.SelectedStartingWeaponId ?? "pulse-sidearm";
    public string? SelectedUpgradeId { get; private set; }
    public string? SelectedRecoveryItemId { get; private set; }

    public void ConfigureProfile(
        ProfileData profile,
        IEnumerable<(string Id, string DisplayName)> weapons,
        bool hasCheckpoint = false,
        ContentCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(weapons);
        _profile = profile;
        _catalog = catalog;
        _hasCheckpoint = hasCheckpoint;
        _loadoutRows.Clear();
        _loadoutWeaponIds.Clear();
        foreach ((string id, string displayName) in weapons)
        {
            _loadoutWeaponIds.Add(id);
            _loadoutRows.Add(displayName.ToUpperInvariant());
        }

        _loadoutRows.Add("BACK");
    }

    public void OpenMain()
    {
        Page = MenuPage.Main;
        SelectedIndex = 0;
        _returnPage = MenuPage.Main;
    }

    public void OpenPause()
    {
        Page = MenuPage.Pause;
        SelectedIndex = 0;
    }

    public void OpenLoadout()
    {
        MenuPage returnPage = Page is MenuPage.Pause or MenuPage.Recovery ? Page : MenuPage.Main;
        if (_catalog is not null)
        {
            Page = MenuPage.Loadout;
            SelectedIndex = 0;
            _returnPage = returnPage;
            return;
        }

        Page = MenuPage.Loadout;
        SelectedIndex = Math.Max(0, _loadoutWeaponIds.FindIndex(
            id => string.Equals(id, StartingWeaponId, StringComparison.OrdinalIgnoreCase)));
        _returnPage = returnPage;
    }

    public void OpenRecords()
    {
        Page = MenuPage.Records;
        SelectedIndex = 0;
        _returnPage = MenuPage.Main;
    }

    public void OpenCharacter() => OpenProfilePage(MenuPage.Character);
    public void OpenInventory()
    {
        _inventoryPage = 0;
        _confirmBatchDismantle = false;
        OpenProfilePage(MenuPage.Inventory);
    }
    public void OpenAbilities() => OpenProfilePage(MenuPage.Abilities);
    public void OpenProficiencies() => OpenProfilePage(MenuPage.Proficiencies);
    public void OpenCrafting() => OpenProfilePage(MenuPage.Crafting);
    public void OpenStats() => OpenProfilePage(MenuPage.Stats);
    public void OpenDifficulty() => OpenProfilePage(MenuPage.Difficulty);
    public void OpenThreatTier() => OpenProfilePage(MenuPage.ThreatTier);

    public void OpenRecovery(IEnumerable<EquipmentInstance> items)
    {
        _recoveryRows.Clear();
        _recoveryItemIds.Clear();
        foreach (EquipmentInstance item in items.OrderByDescending(item => item.Rarity)
            .ThenByDescending(item => item.ItemPower).ThenBy(item => item.DisplayName))
        {
            _recoveryRows.Add($"{item.Rarity.ToString().ToUpperInvariant()}  {item.DisplayName.ToUpperInvariant()}  IP {item.ItemPower}");
            _recoveryItemIds.Add(item.Id);
        }

        _recoveryRows.Add("TAKE ALL AND CONTINUE");
        SelectedRecoveryItemId = null;
        Page = MenuPage.Recovery;
        SelectedIndex = 0;
        _returnPage = MenuPage.Recovery;
    }

    private void OpenProfilePage(MenuPage page)
    {
        MenuPage returnPage = Page is MenuPage.Pause or MenuPage.Recovery ? Page : MenuPage.Main;
        Page = page;
        SelectedIndex = 0;
        _returnPage = returnPage;
    }

    private void ReturnToProfileParent()
    {
        Page = _returnPage is MenuPage.Pause or MenuPage.Recovery ? _returnPage : MenuPage.Main;
        SelectedIndex = 0;
    }

    public void OpenTutorial()
    {
        Page = MenuPage.Tutorial;
        SelectedIndex = 0;
        _returnPage = MenuPage.Main;
    }

    public void OpenReward(IEnumerable<(string Id, string DisplayName, string Description)> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        _rewardRows.Clear();
        _rewardUpgradeIds.Clear();
        _rewardDescriptions.Clear();
        foreach ((string id, string displayName, string description) in choices)
        {
            _rewardUpgradeIds.Add(id);
            _rewardRows.Add(displayName.ToUpperInvariant());
            _rewardDescriptions.Add(description.ToUpperInvariant());
        }

        if (_rewardRows.Count == 0)
        {
            throw new ArgumentException("A reward screen requires at least one choice.", nameof(choices));
        }

        SelectedUpgradeId = null;
        Page = MenuPage.Reward;
        SelectedIndex = 0;
        _returnPage = MenuPage.Reward;
    }

    public void OpenSettings(MenuPage returnPage = MenuPage.Pause)
    {
        Page = MenuPage.Settings;
        SelectedIndex = 0;
        _returnPage = returnPage;
    }

    public void OpenAccessibility(MenuPage returnPage = MenuPage.Pause)
    {
        Page = MenuPage.Accessibility;
        SelectedIndex = 0;
        _returnPage = returnPage;
    }

    public void OpenResults()
    {
        Page = MenuPage.Results;
        SelectedIndex = 0;
        _returnPage = MenuPage.Results;
    }

    public void Close()
    {
        Page = MenuPage.None;
        SelectedIndex = 0;
    }

    public MenuAction Update(GameSettings settings, MenuInputSnapshot input, Rectangle safeArea)
    {
        if (Pressed(MenuInputButtons.OpenSettings, input) && Page is not (MenuPage.Settings or MenuPage.Accessibility))
        {
            MenuPage returnPage = Page == MenuPage.None ? MenuPage.Pause : Page;
            bool pausesGameplay = Page == MenuPage.None;
            OpenSettings(returnPage);
            return Finish(input, pausesGameplay ? MenuAction.Pause : MenuAction.None);
        }

        if (Pressed(MenuInputButtons.OpenAccessibility, input) && Page is not (MenuPage.Settings or MenuPage.Accessibility))
        {
            MenuPage returnPage = Page == MenuPage.None ? MenuPage.Pause : Page;
            bool pausesGameplay = Page == MenuPage.None;
            OpenAccessibility(returnPage);
            return Finish(input, pausesGameplay ? MenuAction.Pause : MenuAction.None);
        }

        if (Pressed(MenuInputButtons.Pause, input))
        {
            if (Page == MenuPage.None)
            {
                OpenPause();
                return Finish(input, MenuAction.Pause);
            }

            if (Page == MenuPage.Pause ||
                (Page is MenuPage.Settings or MenuPage.Accessibility && _returnPage == MenuPage.Pause))
            {
                Close();
                return Finish(input, MenuAction.Resume);
            }
        }

        if (Pressed(MenuInputButtons.Back, input))
        {
            if (Page == MenuPage.None)
            {
                OpenPause();
                return Finish(input, MenuAction.Pause);
            }

            if (Page is MenuPage.Settings or MenuPage.Accessibility)
            {
                Page = _returnPage;
                SelectedIndex = 0;
                return Finish(input, MenuAction.None);
            }

            if (Page == MenuPage.InventoryItem)
            {
                Page = MenuPage.Inventory;
                SelectedIndex = 4;
                return Finish(input, MenuAction.None);
            }

            if (Page == MenuPage.CraftingItem)
            {
                Page = _craftingReturnPage;
                SelectedIndex = Page == MenuPage.Inventory ? 4 : 1;
                _confirmLegendaryDismantle = false;
                return Finish(input, MenuAction.None);
            }

            if (Page == MenuPage.Armory)
            {
                Page = MenuPage.Loadout;
                SelectedIndex = (_armorySetIndex * 2) +
                    (_armoryHand == EquipmentSlot.LeftHand ? 1 : 0);
                return Finish(input, MenuAction.None);
            }

            if (Page is MenuPage.Loadout or MenuPage.Character or MenuPage.Inventory or MenuPage.Abilities or
                MenuPage.Proficiencies or MenuPage.Crafting or MenuPage.Stats or MenuPage.Difficulty or
                MenuPage.ThreatTier)
            {
                ReturnToProfileParent();
                return Finish(input, MenuAction.None);
            }

            if (Page is MenuPage.Records or MenuPage.Tutorial)
            {
                OpenMain();
                return Finish(input, MenuAction.None);
            }

            if (Page == MenuPage.Results)
            {
                return Finish(input, MenuAction.ReturnToMain);
            }

            if (Page == MenuPage.Pause)
            {
                Close();
                return Finish(input, MenuAction.Resume);
            }
        }

        if (Page == MenuPage.None)
        {
            return Finish(input, MenuAction.None);
        }

        int currentTab = GetProfileTabIndex(Page);
        if (currentTab >= 0)
        {
            int pointerTab = input.HasPointer
                ? MenuLayout.HitTestProfileTab(safeArea, input.PointerPosition)
                : -1;
            bool pointerTabActivated = pointerTab >= 0 && input.PointerDown && !_previousInput.PointerDown;
            int tabDirection = Pressed(MenuInputButtons.PreviousTab, input) ? -1 :
                Pressed(MenuInputButtons.NextTab, input) ? 1 : 0;
            if (pointerTabActivated || tabDirection != 0)
            {
                int targetTab = pointerTabActivated
                    ? pointerTab
                    : (currentTab + tabDirection + ProfileTabs.Length) % ProfileTabs.Length;
                SwitchProfileTab(targetTab);
                return Finish(input, MenuAction.None);
            }
        }

        int rowCount = GetRows().Count;
        if (Page == MenuPage.Inventory && _confirmBatchDismantle &&
            (Pressed(MenuInputButtons.Up, input) || Pressed(MenuInputButtons.Down, input)))
        {
            _confirmBatchDismantle = false;
        }
        if (Pressed(MenuInputButtons.Up, input))
        {
            SelectedIndex = (SelectedIndex + rowCount - 1) % rowCount;
        }
        else if (Pressed(MenuInputButtons.Down, input))
        {
            SelectedIndex = (SelectedIndex + 1) % rowCount;
        }

        MenuLayoutMetrics layout = MenuLayout.Create(safeArea, rowCount, settings.LargeHudText, Page);
        int pointerRow = input.HasPointer ? layout.HitTest(input.PointerPosition) : -1;
        bool pointerActivated = pointerRow >= 0 && input.PointerDown && !_previousInput.PointerDown;
        if (pointerRow >= 0 && input.HasPointerSelectionIntent(_previousInput))
        {
            if (Page == MenuPage.Inventory && pointerRow != SelectedIndex)
            {
                _confirmBatchDismantle = false;
            }
            SelectedIndex = pointerRow;
        }

        int adjustment = 0;
        if (Pressed(MenuInputButtons.Left, input))
        {
            adjustment = -1;
        }
        else if (Pressed(MenuInputButtons.Right, input))
        {
            adjustment = 1;
        }

        MenuAction action = adjustment == 0 ? MenuAction.None : Adjust(settings, adjustment);
        if (Pressed(MenuInputButtons.Accept, input) || pointerActivated)
        {
            action = pointerActivated
                ? ActivatePointer(settings, pointerRow, input.PointerPosition, layout)
                : Activate(settings);
        }

        return Finish(input, action);
    }

    private static int GetProfileTabIndex(MenuPage page) => page switch
    {
        MenuPage.InventoryItem => 1,
        MenuPage.Armory => 2,
        MenuPage.CraftingItem => 5,
        _ => Array.IndexOf(ProfileTabs, page),
    };

    private void SwitchProfileTab(int index)
    {
        Page = ProfileTabs[Math.Clamp(index, 0, ProfileTabs.Length - 1)];
        SelectedIndex = 0;
        if (Page == MenuPage.Inventory)
        {
            _inventoryPage = 0;
            _confirmBatchDismantle = false;
        }
    }

    public IReadOnlyList<string> GetRows() => Page switch
    {
        MenuPage.Main => _hasCheckpoint ? MainRowsWithContinue : MainRows,
        MenuPage.Pause => PauseRows,
        MenuPage.Loadout => _catalog is null ? _loadoutRows : GetEquipmentLoadoutRows(),
        MenuPage.Armory => GetArmoryRows(),
        MenuPage.Character => GetCharacterRows(),
        MenuPage.Inventory => GetInventoryRows(),
        MenuPage.InventoryItem => GetInventoryItemRows(),
        MenuPage.Abilities => GetAbilityRows(),
        MenuPage.Proficiencies => GetProficiencyRows(),
        MenuPage.Crafting => GetCraftingRows(),
        MenuPage.CraftingItem => GetCraftingItemRows(),
        MenuPage.Stats => GetStatsRows(),
        MenuPage.Difficulty => [.. DifficultyCatalog.All.Select(definition => definition.DisplayName), "BACK"],
        MenuPage.ThreatTier => [.. Enum.GetValues<ThreatTier>().Select(TierLabel), "BACK"],
        MenuPage.Recovery => _recoveryRows,
        MenuPage.Records => RecordsRows,
        MenuPage.Tutorial => TutorialRows,
        MenuPage.Reward => _rewardRows,
        MenuPage.Settings => SettingsRows,
        MenuPage.Accessibility => AccessibilityRows,
        MenuPage.Results => ResultsRows,
        _ => [],
    };

    public string GetSupplementalValue(int index)
    {
        if (Page == MenuPage.Reward)
        {
            return index >= 0 && index < _rewardDescriptions.Count
                ? _rewardDescriptions[index]
                : string.Empty;
        }

        if (Page == MenuPage.ThreatTier && _profile is not null && index is >= 0 and < 10)
        {
            ThreatTier tier = (ThreatTier)(index + 1);
            return tier == _profile.SelectedThreatTier ? "SELECTED" :
                tier <= _profile.HighestUnlockedThreatTier ? "READY" : "LOCKED";
        }

        if (Page == MenuPage.Difficulty && _profile is not null &&
            index is >= 0 && index < DifficultyCatalog.All.Count)
        {
            return DifficultyCatalog.All[index].Mode == DifficultyCatalog.Normalize(_profile.SelectedDifficulty)
                ? "SELECTED"
                : "READY";
        }

        if (Page == MenuPage.InventoryItem && index == 0)
        {
            EquipmentInstance? item = SelectedInventoryItem();
            if (item is not null)
            {
                EquipmentInstance? equipped = FindComparisonItem(item);
                return equipped is null
                    ? $"{item.PrimarySlot.ToString().ToUpperInvariant()}  NO ITEM EQUIPPED"
                    : $"COMPARE {equipped.DisplayName.ToUpperInvariant()}  IP {equipped.ItemPower}  " +
                      $"{Signed(item.ItemPower - equipped.ItemPower)} IP";
            }
        }

        if (Page == MenuPage.Loadout && _catalog is not null)
        {
            return index < (WeaponQuickbarLoadout.SlotCount * 2) + LoadoutEquipmentSlots.Length
                ? "SELECT"
                : string.Empty;
        }

        if (Page == MenuPage.Armory && _catalog is not null)
        {
            if (index == 0)
            {
                return "CLEAR SLOT";
            }
            ArmoryChoice[] choices = GetArmoryPageChoices();
            int weaponIndex = index - 1;
            return weaponIndex >= 0 && weaponIndex < choices.Length
                ? $"{choices[weaponIndex].Weapon.Family.ToString().ToUpperInvariant()}  " +
                  (choices[weaponIndex].Weapon.Handedness == Handedness.TwoHanded ? "TWO-HANDED" : "ONE-HANDED") +
                  (choices[weaponIndex].Item is null ? "  COMMON ISSUE" :
                      $"  OWNED IP {choices[weaponIndex].Item!.ItemPower}")
                : string.Empty;
        }

        if (Page != MenuPage.Loadout || index < 0 || index >= _loadoutWeaponIds.Count || _profile is null)
        {
            return string.Empty;
        }

        string weaponId = _loadoutWeaponIds[index];
        if (string.Equals(weaponId, _profile.SelectedStartingWeaponId, StringComparison.OrdinalIgnoreCase))
        {
            return "EQUIPPED";
        }

        return "ARMORY ISSUE";
    }

    private MenuAction Activate(GameSettings settings)
    {
        if (Page == MenuPage.Main)
        {
            int offset = _hasCheckpoint ? 1 : 0;
            if (_hasCheckpoint && SelectedIndex == 0)
            {
                Page = MenuPage.None;
                return MenuAction.ContinueRun;
            }

            switch (SelectedIndex - offset)
            {
                case 0:
                    Page = MenuPage.None;
                    return MenuAction.StartRun;
                case 1:
                    OpenCharacter();
                    return MenuAction.None;
                case 2:
                    OpenInventory();
                    return MenuAction.None;
                case 3:
                    OpenAbilities();
                    return MenuAction.None;
                case 4:
                    OpenProficiencies();
                    return MenuAction.None;
                case 5:
                    OpenDifficulty();
                    return MenuAction.None;
                case 6:
                    OpenThreatTier();
                    return MenuAction.None;
                case 7:
                    OpenLoadout();
                    return MenuAction.None;
                case 8:
                    Page = MenuPage.None;
                    return MenuAction.StartDebugLab;
                case 9:
                    OpenRecords();
                    return MenuAction.None;
                case 10:
                    OpenSettings(MenuPage.Main);
                    return MenuAction.None;
                case 11:
                    OpenAccessibility(MenuPage.Main);
                    return MenuAction.None;
                case 12:
                    return MenuAction.Quit;
            }
        }

        else if (Page == MenuPage.Loadout)
        {
            if (_catalog is not null)
            {
                int weaponRowCount = WeaponQuickbarLoadout.SlotCount * 2;
                if (SelectedIndex < weaponRowCount)
                {
                    _armorySetIndex = SelectedIndex / 2;
                    _armoryHand = SelectedIndex % 2 == 0
                        ? EquipmentSlot.RightHand
                        : EquipmentSlot.LeftHand;
                    _armoryPage = 0;
                    Page = MenuPage.Armory;
                    SelectedIndex = 1;
                    return MenuAction.None;
                }

                int equipmentIndex = SelectedIndex - weaponRowCount;
                if (equipmentIndex >= LoadoutEquipmentSlots.Length)
                {
                    ReturnToProfileParent();
                    return MenuAction.None;
                }

                _inventorySlotFilter = LoadoutEquipmentSlots[equipmentIndex];
                _inventoryRarityFilter = null;
                _inventoryMinimumPower = 1;
                _inventoryPage = 0;
                Page = MenuPage.Inventory;
                SelectedIndex = 4;
                return MenuAction.None;
            }

            if (SelectedIndex == _loadoutRows.Count - 1)
            {
                ReturnToProfileParent();
                return MenuAction.None;
            }

            if (_profile is not null && SelectedIndex >= 0 && SelectedIndex < _loadoutWeaponIds.Count)
            {
                string weaponId = _loadoutWeaponIds[SelectedIndex];
                _profile.SelectedStartingWeaponId = weaponId;
                return MenuAction.StartingWeaponChanged;
            }

            return MenuAction.None;
        }

        else if (Page == MenuPage.Armory)
        {
            return ActivateArmory();
        }

        else if (Page == MenuPage.Records)
        {
            OpenMain();
            return MenuAction.None;
        }

        else if (Page == MenuPage.Character)
        {
            if (_profile is not null && _catalog is not null && SelectedIndex is >= 4 and < 14)
            {
                TalentDefinition talent = GetBranchTalents()[SelectedIndex - 4];
                PlayerProgressionState progression = _profile.CreateProgressionState();
                if (progression.TrySpendTalent(talent.Id, _catalog, out _))
                {
                    _profile.ApplyProgressionState(progression);
                    return MenuAction.ProfileChanged;
                }

                return MenuAction.None;
            }

            int respecIndex = _catalog is null ? 3 : 14;
            if (_profile is not null && SelectedIndex == respecIndex && _returnPage == MenuPage.Main)
            {
                int refund = _profile.TalentRanks.Values.Sum();
                _profile.TalentRanks.Clear();
                _profile.UnspentTalentPoints += refund;
                return MenuAction.ProfileChanged;
            }

            if (SelectedIndex == GetCharacterRows().Count - 1)
            {
                ReturnToProfileParent();
            }
            return MenuAction.None;
        }

        else if (Page == MenuPage.Inventory)
        {
            List<EquipmentInstance> pageItems = GetInventoryPageItems();
            if (_profile is not null && SelectedIndex is >= 4 && SelectedIndex < 4 + pageItems.Count)
            {
                _selectedInventoryItemId = pageItems[SelectedIndex - 4].Id;
                Page = MenuPage.InventoryItem;
                SelectedIndex = 0;
                return MenuAction.None;
            }

            if (_profile is not null && SelectedIndex == 4 + pageItems.Count)
            {
                List<EquipmentInstance> candidates = GetBatchDismantleCandidates();
                if (candidates.Count == 0)
                {
                    _confirmBatchDismantle = false;
                    return MenuAction.None;
                }
                if (!_confirmBatchDismantle)
                {
                    _confirmBatchDismantle = true;
                    return MenuAction.None;
                }

                PlayerProgressionState progression = _profile.CreateProgressionState();
                HashSet<string> reserved = ReservedQuickbarItemIds();
                foreach (string itemId in candidates.Select(item => item.Id))
                {
                    EquipmentCrafting.TryDismantle(progression, itemId, reserved,
                        confirmLegendary: false, SalvageYieldMultiplier(), out _, out _);
                }
                _profile.ApplyProgressionState(progression);
                _confirmBatchDismantle = false;
                ClampInventoryPage();
                return MenuAction.ProfileChanged;
            }

            if (SelectedIndex == 5 + pageItems.Count)
            {
                ReturnToProfileParent();
            }
            return MenuAction.None;
        }

        else if (Page == MenuPage.InventoryItem)
        {
            return ActivateInventoryItem();
        }

        else if (Page == MenuPage.Abilities)
        {
            return ActivateAbility();
        }

        else if (Page == MenuPage.Proficiencies)
        {
            ReturnToProfileParent();
            return MenuAction.None;
        }

        else if (Page == MenuPage.Crafting)
        {
            return ActivateCrafting();
        }

        else if (Page == MenuPage.CraftingItem)
        {
            return ActivateCraftingItem();
        }

        else if (Page == MenuPage.Stats)
        {
            ReturnToProfileParent();
            return MenuAction.None;
        }

        else if (Page == MenuPage.Difficulty)
        {
            if (_profile is not null && SelectedIndex is >= 0 && SelectedIndex < DifficultyCatalog.All.Count)
            {
                _profile.SelectedDifficulty = DifficultyCatalog.All[SelectedIndex].Mode;
                return MenuAction.ProfileChanged;
            }

            ReturnToProfileParent();
            return MenuAction.None;
        }

        else if (Page == MenuPage.ThreatTier)
        {
            if (_profile is not null && SelectedIndex is >= 0 and < 10)
            {
                ThreatTier selected = (ThreatTier)(SelectedIndex + 1);
                if (selected <= _profile.HighestUnlockedThreatTier)
                {
                    _profile.SelectedThreatTier = selected;
                    return MenuAction.ProfileChanged;
                }
                return MenuAction.None;
            }

            ReturnToProfileParent();
            return MenuAction.None;
        }

        else if (Page == MenuPage.Recovery)
        {
            if (SelectedIndex < _recoveryItemIds.Count)
            {
                SelectedRecoveryItemId = _recoveryItemIds[SelectedIndex];
                return MenuAction.RecoveryItemSelected;
            }

            Close();
            return MenuAction.RecoveryContinue;
        }

        else if (Page == MenuPage.Tutorial)
        {
            if (SelectedIndex == 0)
            {
                Close();
                return MenuAction.BeginRun;
            }

            OpenMain();
            return MenuAction.None;
        }

        else if (Page == MenuPage.Reward)
        {
            if (SelectedIndex < 0 || SelectedIndex >= _rewardUpgradeIds.Count)
            {
                return MenuAction.None;
            }

            SelectedUpgradeId = _rewardUpgradeIds[SelectedIndex];
            Close();
            return MenuAction.UpgradeSelected;
        }

        if (Page == MenuPage.Pause)
        {
            switch (SelectedIndex)
            {
                case 0:
                    Page = MenuPage.None;
                    return MenuAction.Resume;
                case 1:
                    OpenCharacter();
                    return MenuAction.None;
                case 2:
                    OpenInventory();
                    return MenuAction.None;
                case 3:
                    OpenLoadout();
                    return MenuAction.None;
                case 4:
                    OpenAbilities();
                    return MenuAction.None;
                case 5:
                    OpenProficiencies();
                    return MenuAction.None;
                case 6:
                    OpenCrafting();
                    return MenuAction.None;
                case 7:
                    OpenStats();
                    return MenuAction.None;
                case 8:
                    OpenSettings(MenuPage.Pause);
                    return MenuAction.None;
                case 9:
                    OpenAccessibility(MenuPage.Pause);
                    return MenuAction.None;
                case 10:
                    Page = MenuPage.None;
                    return MenuAction.Restart;
                case 11:
                    return MenuAction.ReturnToMain;
                case 12:
                    return MenuAction.Quit;
            }
        }

        if (Page == MenuPage.Results)
        {
            MenuAction action = SelectedIndex switch
            {
                0 => MenuAction.Restart,
                1 => MenuAction.ReturnToMain,
                2 => MenuAction.Quit,
                _ => MenuAction.None,
            };
            if (action == MenuAction.Restart)
            {
                Close();
            }

            return action;
        }

        string[] rows = Page == MenuPage.Settings ? SettingsRows : AccessibilityRows;
        if (SelectedIndex == rows.Length - 1)
        {
            Page = _returnPage;
            SelectedIndex = 0;
            return MenuAction.None;
        }

        return Adjust(settings, 1);
    }

    private List<string> GetCharacterRows()
    {
        int level = _profile?.Level ?? 1;
        int experience = _profile?.Experience ?? 0;
        List<string> rows =
        [
            $"LEVEL {level} / 100",
            $"XP {experience} / {RpgProgressionMath.ExperienceToNextLevel(level)}",
            $"TALENT POINTS {_profile?.UnspentTalentPoints ?? 0}   " +
            $"SCRAP {_profile?.Materials.Scrap ?? 0}  COMPONENTS {_profile?.Materials.Components ?? 0}  " +
            $"CORES {_profile?.Materials.Cores ?? 0}",
            $"BRANCH  {_talentBranch.ToString().ToUpperInvariant()}  < >",
        ];
        if (_catalog is not null)
        {
            rows.AddRange(GetBranchTalents().Select(talent =>
                $"T{talent.Tier}  {talent.DisplayName.ToUpperInvariant()}  " +
                $"{_profile?.TalentRanks.GetValueOrDefault(talent.Id) ?? 0}/{talent.MaximumRanks}"));
        }

        rows.Add(_returnPage == MenuPage.Main ? "FREE TALENT RESPEC" : "TALENT RESPEC  MAIN MENU ONLY");
        rows.Add("BACK");
        return rows;
    }

    private List<string> GetInventoryRows()
    {
        List<EquipmentInstance> allItems = GetFilteredInventoryItems();
        List<EquipmentInstance> batchItems = GetBatchDismantleCandidates();
        CraftingMaterialBundle batchYield = GetBatchDismantleYield(batchItems);
        int pageCount = Math.Max(1, (int)Math.Ceiling(allItems.Count / (double)InventoryPageSize));
        _inventoryPage = Math.Clamp(_inventoryPage, 0, pageCount - 1);
        List<string> rows =
        [
            $"PAGE {_inventoryPage + 1}/{pageCount}  < >",
            $"SLOT  {(_inventorySlotFilter?.ToString() ?? "ALL").ToUpperInvariant()}  < >",
            $"RARITY  {(_inventoryRarityFilter?.ToString() ?? "ALL").ToUpperInvariant()}  < >",
            $"MIN ITEM POWER  {_inventoryMinimumPower}  < >",
        ];
        rows.AddRange(allItems
            .Skip(_inventoryPage * InventoryPageSize)
            .Take(InventoryPageSize)
            .Select(item => $"{(item.IsFavorite ? "* " : string.Empty)}{item.Rarity.ToString().ToUpperInvariant()}  " +
                $"{item.DisplayName.ToUpperInvariant()}  IP {item.ItemPower}  " +
                $"COPIES {allItems.Count(candidate => SameItemBase(candidate, item))}"));
        rows.Add(_confirmBatchDismantle
            ? $"CONFIRM DISMANTLE {batchItems.Count} FILTERED  +{batchYield.Scrap}S/{batchYield.Components}C/{batchYield.Cores}Q"
            : $"BATCH DISMANTLE FILTERED  {batchItems.Count} ELIGIBLE");
        rows.Add("BACK");
        return rows;
    }

    private List<string> GetAbilityRows()
    {
        EquipmentAbilityDefinition[] abilities = GetVisibleAbilities();
        int pages = Math.Max(1, (int)Math.Ceiling(abilities.Length / (double)AbilityPageSize));
        _abilityPage = Math.Clamp(_abilityPage, 0, pages - 1);
        int usedCapacity = (_profile?.AbilityMastery.EquippedPassiveAbilityIds ?? [])
            .Where(id => _catalog?.Abilities.ContainsKey(id) == true)
            .Sum(id => _catalog!.Abilities[id].CapacityCost);
        List<string> rows =
        [
            $"PAGE {_abilityPage + 1}/{pages}  < >",
            $"ACTIVE 1  {AbilitySlotName(0)}",
            $"ACTIVE 2  {AbilitySlotName(1)}",
            $"PASSIVE CAPACITY  {usedCapacity}/{RpgProgressionMath.PassiveAbilityCapacity(_profile?.Level ?? 1)}",
        ];
        rows.AddRange(abilities.Skip(_abilityPage * AbilityPageSize).Take(AbilityPageSize).Select(ability =>
        {
            AbilityProgress progress = _profile?.AbilityMastery.Abilities.GetValueOrDefault(ability.Id) ?? new();
            bool equipped = (_profile?.AbilityMastery.EquippedActiveAbilityIds.Contains(ability.Id,
                    StringComparer.OrdinalIgnoreCase) ?? false) ||
                (_profile?.AbilityMastery.EquippedPassiveAbilityIds.Contains(ability.Id,
                    StringComparer.OrdinalIgnoreCase) ?? false);
            string state = progress.IsMastered ? "MASTERED" : $"AP {progress.AbilityPoints}/{ability.RequiredAbilityPoints}";
            return $"{(equipped ? "> " : string.Empty)}{ability.DisplayName.ToUpperInvariant()}  {ability.Kind.ToString().ToUpperInvariant()}  {state}";
        }));
        rows.Add("BACK");
        return rows;
    }

    private List<string> GetProficiencyRows()
    {
        List<string> rows = (_profile?.Proficiencies.Families ?? [])
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key.ToString().ToUpperInvariant()}  RANK {pair.Value.Rank} / 25  XP {pair.Value.Experience}")
            .ToList();
        rows.Add("BACK");
        return rows;
    }

    private List<string> GetCraftingRows()
    {
        List<EquipmentInstance> items = GetCraftingItems();
        int pageCount = Math.Max(1, (int)Math.Ceiling(items.Count / (double)InventoryPageSize));
        _inventoryPage = Math.Clamp(_inventoryPage, 0, pageCount - 1);
        List<string> rows =
        [
            $"MATERIALS  SCRAP {_profile?.Materials.Scrap ?? 0}  " +
            $"COMPONENTS {_profile?.Materials.Components ?? 0}  CORES {_profile?.Materials.Cores ?? 0}",
            $"PAGE {_inventoryPage + 1}/{pageCount}  < >",
        ];
        rows.AddRange(items.Skip(_inventoryPage * InventoryPageSize).Take(InventoryPageSize).Select(item =>
        {
            int duplicates = items.Count(candidate => SameItemBase(candidate, item));
            return $"{item.Rarity.ToString().ToUpperInvariant()}  {item.DisplayName.ToUpperInvariant()}  " +
                $"IP {item.ItemPower}  COPIES {duplicates}";
        }));
        rows.Add("BACK");
        return rows;
    }

    private List<string> GetCraftingItemRows()
    {
        EquipmentInstance? target = SelectedCraftingItem();
        if (target is null)
        {
            return ["ITEM NO LONGER AVAILABLE", "BACK"];
        }
        List<(EquipmentInstance Donor, ItemUpgradeQuote Quote)> donors = EligibleInfusionDonors(target);
        List<string> rows =
        [
            $"{target.Rarity.ToString().ToUpperInvariant()}  {target.DisplayName.ToUpperInvariant()}  IP {target.ItemPower}",
        ];
        rows.AddRange(donors.Select(entry =>
            $"INFUSE  {entry.Donor.DisplayName.ToUpperInvariant()} IP {entry.Donor.ItemPower}  " +
            $"-> IP {entry.Quote.ResultItemPower}  COST {entry.Quote.Cost.Scrap}S/" +
            $"{entry.Quote.Cost.Components}C/{entry.Quote.Cost.Cores}Q"));
        string dismantle = _confirmLegendaryDismantle && target.Rarity == ItemRarity.Legendary
            ? "CONFIRM DISMANTLE LEGENDARY"
            : "DISMANTLE ITEM";
        CraftingMaterialBundle yield = EquipmentCrafting.GetDismantleYield(target, SalvageYieldMultiplier());
        rows.Add($"{dismantle}  +{yield.Scrap}S/{yield.Components}C/{yield.Cores}Q");
        rows.Add("BACK");
        return rows;
    }

    private List<string> GetStatsRows()
    {
        CharacterStatSnapshot stats = CreateCharacterStatSnapshot();
        return
        [
            $"LEVEL {stats.Level}  XP {stats.Experience}/{stats.ExperienceToNextLevel}",
            $"MAX HEALTH {stats.MaximumHealth:0}",
            $"ARMOR {stats.Armor:0.0}  MITIGATION {stats.ArmorMitigation:P0}",
            $"WEAPON DAMAGE {stats.DamageMultiplier:P0}",
            $"INCOMING DAMAGE {stats.IncomingDamageMultiplier:P0}",
            $"MOVEMENT {stats.MovementSpeedMultiplier:P0}",
            $"COOLDOWN RECOVERY {stats.CooldownRecoveryMultiplier:P0}",
            $"PASSIVE CAPACITY {stats.PassiveCapacity}",
            $"GEAR CONTRIBUTIONS {stats.Contributions.GetValueOrDefault("gear-items"):0}",
            $"TALENT RANKS {stats.Contributions.GetValueOrDefault("talent-ranks"):0}",
            "BACK",
        ];
    }

    private MenuAction ActivateCrafting()
    {
        List<EquipmentInstance> pageItems = GetCraftingItems()
            .Skip(_inventoryPage * InventoryPageSize).Take(InventoryPageSize).ToList();
        if (SelectedIndex == GetCraftingRows().Count - 1)
        {
            ReturnToProfileParent();
            return MenuAction.None;
        }
        if (SelectedIndex is >= 2 && SelectedIndex < 2 + pageItems.Count)
        {
            _selectedCraftingItemId = pageItems[SelectedIndex - 2].Id;
            _confirmLegendaryDismantle = false;
            _craftingReturnPage = MenuPage.Crafting;
            Page = MenuPage.CraftingItem;
            SelectedIndex = 0;
        }
        return MenuAction.None;
    }

    private MenuAction ActivateCraftingItem()
    {
        if (_profile is null || SelectedCraftingItem() is not EquipmentInstance target)
        {
            Page = MenuPage.Crafting;
            SelectedIndex = 1;
            return MenuAction.None;
        }
        List<(EquipmentInstance Donor, ItemUpgradeQuote Quote)> donors = EligibleInfusionDonors(target);
        if (SelectedIndex is > 0 && SelectedIndex <= donors.Count)
        {
            PlayerProgressionState progression = _profile.CreateProgressionState();
            if (EquipmentCrafting.TryInfuse(progression, target.Id, donors[SelectedIndex - 1].Donor.Id,
                    ReservedQuickbarItemIds(), out _, out _))
            {
                _profile.ApplyProgressionState(progression);
                SelectedIndex = 0;
                return MenuAction.ProfileChanged;
            }
            return MenuAction.None;
        }

        int dismantleIndex = donors.Count + 1;
        if (SelectedIndex == dismantleIndex)
        {
            if (target.Rarity == ItemRarity.Legendary && !_confirmLegendaryDismantle)
            {
                _confirmLegendaryDismantle = true;
                return MenuAction.None;
            }
            PlayerProgressionState progression = _profile.CreateProgressionState();
            if (EquipmentCrafting.TryDismantle(progression, target.Id, ReservedQuickbarItemIds(),
                    _confirmLegendaryDismantle, SalvageYieldMultiplier(), out _, out _))
            {
                _profile.ApplyProgressionState(progression);
                _selectedCraftingItemId = null;
                _confirmLegendaryDismantle = false;
                Page = _craftingReturnPage;
                SelectedIndex = Page == MenuPage.Inventory ? 4 : 1;
                return MenuAction.ProfileChanged;
            }
            return MenuAction.None;
        }
        if (SelectedIndex == dismantleIndex + 1)
        {
            Page = _craftingReturnPage;
            SelectedIndex = Page == MenuPage.Inventory ? 4 : 1;
            _confirmLegendaryDismantle = false;
        }
        return MenuAction.None;
    }

    private List<EquipmentInstance> GetCraftingItems() => (_profile?.Stash ?? [])
        .Where(item => !item.IsRunBound)
        .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenByDescending(item => item.Rarity)
        .ThenByDescending(item => item.ItemPower)
        .ToList();

    private EquipmentInstance? SelectedCraftingItem() => _profile?.Stash.FirstOrDefault(item =>
        item.Id.Equals(_selectedCraftingItemId, StringComparison.OrdinalIgnoreCase));

    private List<(EquipmentInstance Donor, ItemUpgradeQuote Quote)> EligibleInfusionDonors(EquipmentInstance target)
    {
        List<(EquipmentInstance, ItemUpgradeQuote)> donors = [];
        HashSet<string> reserved = ReservedQuickbarItemIds();
        foreach (EquipmentInstance donor in _profile?.Stash ?? [])
        {
            if (EquipmentCrafting.TryCreateInfusionQuote(target, donor,
                    _profile?.HighestUnlockedThreatTier ?? ThreatTier.TierI, reserved,
                    out ItemUpgradeQuote? quote, out _))
            {
                donors.Add((donor, quote!));
            }
        }
        return donors.OrderByDescending(entry => entry.Item2.ResultItemPower)
            .ThenBy(entry => entry.Item1.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private CharacterStatSnapshot CreateCharacterStatSnapshot()
    {
        if (_profile is null || _catalog is null)
        {
            return new CharacterStatSnapshot();
        }
        IEnumerable<EquipmentInstance> equipped = _profile.EquipmentLoadout.EquippedItemIds.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => _profile.Stash.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .OfType<EquipmentInstance>();
        float healthBonus = 0f;
        float armor = 0f;
        float damage = 1f;
        float incoming = 1f;
        float movement = 1f;
        float cooldown = 1f;
        int equippedCount = 0;
        foreach (EquipmentInstance item in equipped)
        {
            equippedCount++;
            float scale = RpgProgressionMath.ItemPowerScale(item.ItemPower);
            if (item.EquipmentBaseId is string baseId &&
                _catalog.EquipmentBases.TryGetValue(baseId, out EquipmentBaseDefinition? definition))
            {
                healthBonus += definition.BaseMaximumHealth * scale;
                armor += definition.BaseArmor * scale;
            }
            foreach (RolledAffix rolled in item.Affixes)
            {
                if (!_catalog.Affixes.TryGetValue(rolled.AffixId, out AffixDefinition? affix))
                {
                    continue;
                }
                switch (affix.EffectType)
                {
                    case AffixEffectType.MaximumHealth: healthBonus += rolled.Value; break;
                    case AffixEffectType.Armor: armor += rolled.Value; break;
                    case AffixEffectType.WeaponDamage: damage *= rolled.Value; break;
                    case AffixEffectType.IncomingDamage: incoming *= rolled.Value; break;
                    case AffixEffectType.MovementSpeed: movement *= rolled.Value; break;
                    case AffixEffectType.CooldownRecovery: cooldown *= rolled.Value; break;
                }
            }
        }
        int talentRanks = _profile.TalentRanks.Values.Sum();
        foreach (TalentDefinition talent in _catalog.Talents.Values)
        {
            int ranks = _profile.TalentRanks.GetValueOrDefault(talent.Id);
            switch (talent.EffectType)
            {
                case AffixEffectType.MaximumHealth: healthBonus += talent.ValuePerRank * ranks; break;
                case AffixEffectType.Armor: armor += talent.ValuePerRank * ranks; break;
                case AffixEffectType.WeaponDamage: damage *= 1f + (talent.ValuePerRank * ranks); break;
                case AffixEffectType.IncomingDamage: incoming *= 1f - (talent.ValuePerRank * ranks); break;
                case AffixEffectType.MovementSpeed: movement *= 1f + (talent.ValuePerRank * ranks); break;
                case AffixEffectType.CooldownRecovery: cooldown *= 1f + (talent.ValuePerRank * ranks); break;
            }
        }
        float armorMultiplier = RpgProgressionMath.ArmorDamageMultiplier(armor);
        return new CharacterStatSnapshot
        {
            Level = _profile.Level,
            Experience = _profile.Experience,
            ExperienceToNextLevel = RpgProgressionMath.ExperienceToNextLevel(_profile.Level),
            UnspentTalentPoints = _profile.UnspentTalentPoints,
            MaximumHealth = 100f + healthBonus,
            Armor = armor,
            ArmorMitigation = 1f - armorMultiplier,
            DamageMultiplier = Math.Clamp(damage, 1f, 1.25f),
            IncomingDamageMultiplier = Math.Clamp(armorMultiplier * incoming, 0.35f, 1f),
            MovementSpeedMultiplier = Math.Clamp(movement, 1f, 1.15f),
            CooldownRecoveryMultiplier = Math.Clamp(cooldown, 1f, 1.30f),
            PassiveCapacity = RpgProgressionMath.PassiveAbilityCapacity(_profile.Level),
            Contributions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["gear-items"] = equippedCount,
                ["talent-ranks"] = talentRanks,
            },
        };
    }

    private static bool SameItemBase(EquipmentInstance left, EquipmentInstance right) =>
        (!string.IsNullOrWhiteSpace(left.WeaponBaseId) && left.WeaponBaseId.Equals(
            right.WeaponBaseId, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(left.EquipmentBaseId) && left.EquipmentBaseId.Equals(
            right.EquipmentBaseId, StringComparison.OrdinalIgnoreCase));

    private static string TierLabel(ThreatTier tier) => $"THREAT {tier.ToString()[4..].ToUpperInvariant()}  ITEM POWER " +
        $"{RpgProgressionMath.MinimumItemPower(tier)}-{RpgProgressionMath.MaximumItemPower(tier)}";

    private List<string> GetEquipmentLoadoutRows()
    {
        List<string> rows = [];
        for (int index = 0; index < WeaponQuickbarLoadout.SlotCount; index++)
        {
            AddWeaponPresetRows(rows, _profile?.StarterWeaponQuickbar[index], $"{(index + 1) % 10}");
        }
        foreach (EquipmentSlot slot in LoadoutEquipmentSlots)
        {
            string? itemId = _profile?.EquipmentLoadout[slot];
            EquipmentInstance? item = _profile?.Stash.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, itemId, StringComparison.OrdinalIgnoreCase));
            rows.Add($"{SlotLabel(slot)}  {(item?.DisplayName ?? "EMPTY").ToUpperInvariant()}" +
                (item is null ? string.Empty : $"  IP {item.ItemPower}"));
        }
        rows.Add("BACK");
        return rows;
    }

    private void AddWeaponPresetRows(List<string> rows, WeaponPresetSlot? set, string label)
    {
        rows.Add($"SLOT {label}  RIGHT  {WeaponReferenceLabel(set?.RightHand)}");
        rows.Add($"SLOT {label}  LEFT   {WeaponReferenceLabel(set?.LeftHand)}");
    }

    private string WeaponReferenceLabel(StarterWeaponReference? reference)
    {
        if (reference is null)
        {
            return "EMPTY";
        }
        return _catalog?.Weapons.GetValueOrDefault(reference.WeaponBaseId)?.DisplayName.ToUpperInvariant() ??
            reference.WeaponBaseId.Replace('-', ' ').ToUpperInvariant();
    }

    private List<string> GetArmoryRows()
    {
        ArmoryChoice[] page = GetArmoryPageChoices();
        int pageCount = ArmoryPageCount();
        List<string> rows = ["EMPTY", .. page.Select(choice =>
            $"{(choice.Item is null ? "ISSUE" : "OWNED")}  {choice.Item?.DisplayName ?? choice.Weapon.DisplayName}".ToUpperInvariant())];
        rows.Add($"PREVIOUS PAGE  {_armoryPage + 1}/{pageCount}");
        rows.Add($"NEXT PAGE  {_armoryPage + 1}/{pageCount}");
        rows.Add("BACK");
        return rows;
    }

    private ArmoryChoice[] GetArmoryPageChoices()
    {
        if (_catalog is null)
        {
            return [];
        }
        IEnumerable<ArmoryChoice> owned = (_profile?.Stash ?? [])
            .Where(item => item.WeaponBaseId is not null && _catalog.Weapons.ContainsKey(item.WeaponBaseId))
            .OrderByDescending(item => item.ItemPower)
            .ThenByDescending(item => item.Rarity)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ArmoryChoice(_catalog.Weapons[item.WeaponBaseId!], item));
        IEnumerable<ArmoryChoice> issued = _catalog.Weapons.Values
            .Where(weapon => weapon.Family != WeaponFamily.None)
            .OrderBy(weapon => weapon.Family)
            .ThenBy(weapon => weapon.BaseTier)
            .ThenBy(weapon => weapon.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(weapon => new ArmoryChoice(weapon, null));
        return owned.Concat(issued)
            .Skip(_armoryPage * ArmoryPageSize)
            .Take(ArmoryPageSize)
            .ToArray();
    }

    private int ArmoryPageCount() => Math.Max(1, (int)Math.Ceiling(
        (((_profile?.Stash.Count(item => item.IsWeapon) ?? 0) +
          (_catalog?.Weapons.Values.Count(weapon => weapon.Family != WeaponFamily.None) ?? 0))) /
        (double)ArmoryPageSize));

    private MenuAction ActivateArmory()
    {
        if (_profile is null || _catalog is null)
        {
            ReturnToProfileParent();
            return MenuAction.None;
        }

        ArmoryChoice[] choices = GetArmoryPageChoices();
        int previousIndex = 1 + choices.Length;
        int nextIndex = previousIndex + 1;
        int backIndex = nextIndex + 1;
        if (SelectedIndex == previousIndex || SelectedIndex == nextIndex)
        {
            int direction = SelectedIndex == previousIndex ? -1 : 1;
            int pageCount = ArmoryPageCount();
            _armoryPage = (_armoryPage + direction + pageCount) % pageCount;
            SelectedIndex = 1;
            return MenuAction.None;
        }
        if (SelectedIndex == backIndex)
        {
            Page = MenuPage.Loadout;
            SelectedIndex = (_armorySetIndex * 2) +
                (_armoryHand == EquipmentSlot.LeftHand ? 1 : 0);
            return MenuAction.None;
        }

        StarterWeaponReference? selected = SelectedIndex == 0
            ? null
            : SelectedIndex - 1 < choices.Length
                ? choices[SelectedIndex - 1].Item is EquipmentInstance owned
                    ? new StarterWeaponReference
                    {
                        WeaponBaseId = choices[SelectedIndex - 1].Weapon.Id,
                        ItemInstanceId = owned.Id,
                    }
                    : StarterWeaponReference.Issue(choices[SelectedIndex - 1].Weapon.Id)
                : null;
        if (SelectedIndex > 0 && selected is null)
        {
            return MenuAction.None;
        }

        if (selected?.ItemInstanceId is string selectedItemId && ReservedQuickbarItemIds(
                _armorySetIndex, _armoryHand).Contains(selectedItemId))
        {
            return MenuAction.None;
        }

        WeaponPresetSlot current = _profile.StarterWeaponQuickbar[_armorySetIndex].Clone();
        WeaponDefinition? selectedWeapon = selected is null ? null : _catalog.Weapons[selected.WeaponBaseId];
        StarterWeaponReference? right = current.RightHand;
        StarterWeaponReference? left = current.LeftHand;
        if (selectedWeapon?.Handedness == Handedness.TwoHanded)
        {
            right = selected;
            left = null;
        }
        else if (_armoryHand == EquipmentSlot.RightHand)
        {
            right = selected;
            if (ResolveStarterWeapon(left)?.Handedness == Handedness.TwoHanded)
            {
                left = null;
            }
        }
        else
        {
            left = selected;
            if (ResolveStarterWeapon(right)?.Handedness == Handedness.TwoHanded)
            {
                right = null;
            }
        }

        WeaponPresetSlot replacement = new() { RightHand = right, LeftHand = left };
        _profile.StarterWeaponQuickbar.Slots[_armorySetIndex] = replacement;
        _profile.StarterWeaponSetA = _profile.StarterWeaponQuickbar[0].ToWeaponSet();
        _profile.StarterWeaponSetB = _profile.StarterWeaponQuickbar[1].ToWeaponSet();
        Page = MenuPage.Loadout;
        SelectedIndex = (_armorySetIndex * 2) +
            (_armoryHand == EquipmentSlot.LeftHand ? 1 : 0);
        return MenuAction.ProfileChanged;
    }

    private HashSet<string> ReservedQuickbarItemIds(
        int excludedSlot = -1,
        EquipmentSlot? excludedHand = null)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        if (_profile is null)
        {
            return ids;
        }
        for (int index = 0; index < WeaponQuickbarLoadout.SlotCount; index++)
        {
            WeaponPresetSlot slot = _profile.StarterWeaponQuickbar[index];
            if (!(index == excludedSlot && excludedHand == EquipmentSlot.RightHand) &&
                slot.RightHand?.ItemInstanceId is string rightId)
            {
                ids.Add(rightId);
            }
            if (!(index == excludedSlot && excludedHand == EquipmentSlot.LeftHand) &&
                slot.LeftHand?.ItemInstanceId is string leftId)
            {
                ids.Add(leftId);
            }
        }
        foreach (string itemId in _profile.EquipmentLoadout.EquippedItemIds.Values)
        {
            ids.Add(itemId);
        }
        return ids;
    }

    private WeaponDefinition? ResolveStarterWeapon(StarterWeaponReference? reference) =>
        reference is not null ? _catalog?.Weapons.GetValueOrDefault(reference.WeaponBaseId) : null;

    private List<EquipmentInstance> GetFilteredInventoryItems() => (_profile?.Stash ?? [])
        .Where(item => ItemMatchesSlot(item, _inventorySlotFilter))
        .Where(item => _inventoryRarityFilter is null || item.Rarity == _inventoryRarityFilter)
        .Where(item => item.ItemPower >= _inventoryMinimumPower)
        .OrderByDescending(item => item.Rarity)
        .ThenByDescending(item => item.ItemPower)
        .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private List<EquipmentInstance> GetInventoryPageItems() => GetFilteredInventoryItems()
        .Skip(_inventoryPage * InventoryPageSize)
        .Take(InventoryPageSize)
        .ToList();

    private List<EquipmentInstance> GetBatchDismantleCandidates()
    {
        HashSet<string> reserved = ReservedQuickbarItemIds();
        return GetFilteredInventoryItems()
            .Where(item => !item.IsRunBound && !item.IsLocked && !item.IsFavorite &&
                item.Rarity != ItemRarity.Legendary && !reserved.Contains(item.Id))
            .ToList();
    }

    private CraftingMaterialBundle GetBatchDismantleYield(IEnumerable<EquipmentInstance> items)
    {
        int scrap = 0;
        int components = 0;
        int cores = 0;
        float multiplier = SalvageYieldMultiplier();
        foreach (EquipmentInstance item in items)
        {
            CraftingMaterialBundle value = EquipmentCrafting.GetDismantleYield(item, multiplier);
            scrap += value.Scrap;
            components += value.Components;
            cores += value.Cores;
        }
        return new CraftingMaterialBundle(scrap, components, cores);
    }

    private bool ItemMatchesSlot(EquipmentInstance item, EquipmentSlot? slot)
    {
        if (slot is null)
        {
            return true;
        }

        if (item.IsWeapon)
        {
            return slot is EquipmentSlot.RightHand or EquipmentSlot.LeftHand;
        }

        return item.EquipmentBaseId is not null && _catalog?.EquipmentBases.TryGetValue(
            item.EquipmentBaseId, out EquipmentBaseDefinition? definition) == true &&
            definition.CompatibleSlots.Contains(slot.Value);
    }

    private List<string> GetInventoryItemRows()
    {
        EquipmentInstance? item = SelectedInventoryItem();
        if (item is null)
        {
            return ["ITEM NO LONGER AVAILABLE", "BACK"];
        }

        List<string> rows =
        [
            $"{item.Rarity.ToString().ToUpperInvariant()}  {item.DisplayName.ToUpperInvariant()}  IP {item.ItemPower}",
        ];
        foreach (EquipmentSlot slot in CompatibleSlots(item))
        {
            rows.Add($"EQUIP {SlotLabel(slot)}");
        }
        rows.Add(item.IsFavorite ? "REMOVE FAVORITE" : "ADD FAVORITE");
        rows.Add(item.IsLocked ? "UNLOCK ITEM" : "LOCK ITEM");
        rows.Add("UPGRADE / DISMANTLE");
        rows.Add("BACK");
        return rows;
    }

    private MenuAction ActivateInventoryItem()
    {
        EquipmentInstance? item = SelectedInventoryItem();
        if (_profile is null || item is null)
        {
            Page = MenuPage.Inventory;
            SelectedIndex = 4;
            return MenuAction.None;
        }

        EquipmentSlot[] slots = CompatibleSlots(item).ToArray();
        if (SelectedIndex > 0 && SelectedIndex <= slots.Length && _catalog is not null)
        {
            Dictionary<string, EquipmentInstance> inventory = _profile.Stash.ToDictionary(
                candidate => candidate.Id, StringComparer.OrdinalIgnoreCase);
            if (_profile.EquipmentLoadout.TryEquip(item, slots[SelectedIndex - 1], inventory, _catalog, out _))
            {
                return MenuAction.ProfileChanged;
            }
            return MenuAction.None;
        }

        int favoriteIndex = 1 + slots.Length;
        if (SelectedIndex == favoriteIndex)
        {
            ReplaceStashItem(item with { IsFavorite = !item.IsFavorite });
            return MenuAction.ProfileChanged;
        }

        if (SelectedIndex == favoriteIndex + 1)
        {
            ReplaceStashItem(item with { IsLocked = !item.IsLocked });
            return MenuAction.ProfileChanged;
        }

        if (SelectedIndex == favoriteIndex + 2)
        {
            _selectedCraftingItemId = item.Id;
            _confirmLegendaryDismantle = false;
            _craftingReturnPage = MenuPage.Inventory;
            Page = MenuPage.CraftingItem;
            SelectedIndex = 0;
            return MenuAction.None;
        }

        if (SelectedIndex == favoriteIndex + 3)
        {
            Page = MenuPage.Inventory;
            SelectedIndex = 4;
        }
        return MenuAction.None;
    }

    private MenuAction ActivateAbility()
    {
        EquipmentAbilityDefinition[] abilities = GetVisibleAbilities();
        int index = SelectedIndex - 4 + (_abilityPage * AbilityPageSize);
        if (SelectedIndex == GetAbilityRows().Count - 1)
        {
            ReturnToProfileParent();
            return MenuAction.None;
        }

        if (_profile is null || _catalog is null || index < 0 || index >= abilities.Length)
        {
            return MenuAction.None;
        }

        EquipmentAbilityDefinition ability = abilities[index];
        List<string> active = [.. _profile.AbilityMastery.EquippedActiveAbilityIds];
        List<string> passive = [.. _profile.AbilityMastery.EquippedPassiveAbilityIds];
        List<string> target = ability.Kind == AbilityKind.Active ? active : passive;
        int removed = target.RemoveAll(id => string.Equals(id, ability.Id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            // Removed below through validated loadout replacement.
        }
        else if (ability.Kind == AbilityKind.Active)
        {
            if (active.Count >= 2)
            {
                active.RemoveAt(0);
            }
            active.Add(ability.Id);
        }
        else
        {
            passive.Add(ability.Id);
        }

        HashSet<string> taught = CurrentlyTaughtAbilities();
        if (_profile.AbilityMastery.TrySetLoadout(active, passive, _profile.Level, taught, _catalog, out _))
        {
            return MenuAction.ProfileChanged;
        }
        return MenuAction.None;
    }

    private EquipmentAbilityDefinition[] GetVisibleAbilities() =>
        (_catalog?.Abilities.Values ?? Enumerable.Empty<EquipmentAbilityDefinition>())
        .OrderBy(ability => ability.Kind)
        .ThenBy(ability => ability.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private HashSet<string> CurrentlyTaughtAbilities()
    {
        HashSet<string> taught = new(StringComparer.OrdinalIgnoreCase);
        if (_profile is null || _catalog is null)
        {
            return taught;
        }

        foreach (string itemId in _profile.EquipmentLoadout.EquippedItemIds.Values.Distinct(
            StringComparer.OrdinalIgnoreCase))
        {
            EquipmentInstance? item = _profile.Stash.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, itemId, StringComparison.OrdinalIgnoreCase));
            string? abilityId = null;
            if (item?.WeaponBaseId is string weaponId && _catalog.Weapons.TryGetValue(weaponId, out WeaponDefinition? weapon))
            {
                abilityId = weapon.TaughtAbilityId;
            }
            else if (item?.EquipmentBaseId is string equipmentId &&
                _catalog.EquipmentBases.TryGetValue(equipmentId, out EquipmentBaseDefinition? equipment))
            {
                abilityId = equipment.TaughtAbilityId;
            }
            if (!string.IsNullOrWhiteSpace(abilityId))
            {
                taught.Add(abilityId);
            }
        }
        return taught;
    }

    private IEnumerable<EquipmentSlot> CompatibleSlots(EquipmentInstance item)
    {
        if (item.IsWeapon)
        {
            yield return EquipmentSlot.RightHand;
            yield return EquipmentSlot.LeftHand;
            yield break;
        }
        if (item.EquipmentBaseId is not null && _catalog?.EquipmentBases.TryGetValue(
            item.EquipmentBaseId, out EquipmentBaseDefinition? definition) == true)
        {
            foreach (EquipmentSlot slot in definition.CompatibleSlots)
            {
                yield return slot;
            }
        }
    }

    private EquipmentInstance? SelectedInventoryItem() => _profile?.Stash.FirstOrDefault(item =>
        string.Equals(item.Id, _selectedInventoryItemId, StringComparison.OrdinalIgnoreCase));

    private EquipmentInstance? FindComparisonItem(EquipmentInstance item)
    {
        EquipmentSlot? slot = CompatibleSlots(item).Select(value => (EquipmentSlot?)value).FirstOrDefault();
        if (slot is null || _profile?.EquipmentLoadout[slot.Value] is not string itemId)
        {
            return null;
        }
        return _profile.Stash.FirstOrDefault(candidate => string.Equals(candidate.Id, itemId,
            StringComparison.OrdinalIgnoreCase));
    }

    private void ReplaceStashItem(EquipmentInstance item)
    {
        int index = _profile?.Stash.FindIndex(candidate => string.Equals(candidate.Id, item.Id,
            StringComparison.OrdinalIgnoreCase)) ?? -1;
        if (index >= 0)
        {
            _profile!.Stash[index] = item;
        }
    }

    private TalentDefinition[] GetBranchTalents() =>
        (_catalog?.Talents.Values ?? Enumerable.Empty<TalentDefinition>())
        .Where(talent => talent.Branch == _talentBranch)
        .OrderBy(talent => talent.Tier)
        .ThenBy(talent => talent.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private void ClampInventoryPage()
    {
        int pages = Math.Max(1, (int)Math.Ceiling(GetFilteredInventoryItems().Count / (double)InventoryPageSize));
        _inventoryPage = Math.Clamp(_inventoryPage, 0, pages - 1);
    }

    private string AbilitySlotName(int index) => _profile is not null &&
        index < _profile.AbilityMastery.EquippedActiveAbilityIds.Count && _catalog is not null &&
        _catalog.Abilities.TryGetValue(_profile.AbilityMastery.EquippedActiveAbilityIds[index], out EquipmentAbilityDefinition? ability)
            ? ability.DisplayName.ToUpperInvariant()
            : "EMPTY";

    private static string SlotLabel(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Accessory1 => "ACCESSORY 1",
        EquipmentSlot.Accessory2 => "ACCESSORY 2",
        EquipmentSlot.Ring1 => "RING 1",
        EquipmentSlot.Ring2 => "RING 2",
        EquipmentSlot.RightHand => "RIGHT HAND",
        EquipmentSlot.LeftHand => "LEFT HAND",
        _ => slot.ToString().ToUpperInvariant(),
    };

    private static string Signed(int value) => value >= 0 ? $"+{value}" :
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private int SalvageValue(EquipmentInstance item)
    {
        return Math.Max(1, (int)MathF.Round(
            (1f + ((int)item.Rarity * 2f) + (item.ItemPower / 10f)) * SalvageYieldMultiplier()));
    }

    private float SalvageYieldMultiplier()
    {
        float talentBonus = _catalog?.Talents.Values
            .Where(talent => talent.EffectType == AffixEffectType.SalvageYield)
            .Sum(talent => talent.ValuePerRank * (_profile?.TalentRanks.GetValueOrDefault(talent.Id) ?? 0)) ?? 0f;
        float passiveMultiplier = 1f;
        if (_profile is not null && _catalog is not null)
        {
            foreach (string abilityId in _profile.AbilityMastery.EquippedPassiveAbilityIds)
            {
                if (!_profile.AbilityMastery.IsMastered(abilityId) ||
                    !_catalog.Abilities.TryGetValue(abilityId, out EquipmentAbilityDefinition? ability))
                {
                    continue;
                }
                passiveMultiplier *= ability.Effects
                    .Where(effect => effect.Type == AffixEffectType.SalvageYield)
                    .Aggregate(1f, (value, effect) => value * effect.Value);
            }
        }
        return Math.Clamp((1f + talentBonus) * passiveMultiplier, 0f, 10f);
    }

    private MenuAction ActivatePointer(
        GameSettings settings,
        int row,
        Point pointerPosition,
        MenuLayoutMetrics layout)
    {
        if (TrySetPointerSlider(settings, row, pointerPosition, layout))
        {
            settings.Clamp();
            return MenuAction.SettingsChanged;
        }

        return Activate(settings);
    }

    private bool TrySetPointerSlider(
        GameSettings settings,
        int row,
        Point pointerPosition,
        MenuLayoutMetrics layout)
    {
        Rectangle bounds = layout.GetRowBounds(row);
        float amount = Math.Clamp(
            (pointerPosition.X - bounds.Left) / (float)Math.Max(1, bounds.Width - 1),
            0f,
            1f);
        if (Page == MenuPage.Settings)
        {
            switch (row)
            {
                case 0: settings.MasterVolume = amount; return true;
                case 1: settings.MusicVolume = amount; return true;
                case 2: settings.SoundEffectsVolume = amount; return true;
                case 3: settings.MouseSensitivity = MathHelper.Lerp(0.35f, 2.5f, amount); return true;
                case 4: settings.GamepadSensitivity = MathHelper.Lerp(0.35f, 2.5f, amount); return true;
                case 5: settings.FieldOfViewScale = MathHelper.Lerp(0.85f, 1.15f, amount); return true;
            }
        }
        else if (Page == MenuPage.Accessibility)
        {
            switch (row)
            {
                case 1: settings.ScreenShakeScale = amount; return true;
                case 2: settings.CameraBobScale = amount; return true;
            }
        }

        return false;
    }

    private MenuAction Adjust(GameSettings settings, int direction)
    {
        if (Page == MenuPage.Settings)
        {
            switch (SelectedIndex)
            {
                case 0: settings.MasterVolume += direction * 0.05f; break;
                case 1: settings.MusicVolume += direction * 0.05f; break;
                case 2: settings.SoundEffectsVolume += direction * 0.05f; break;
                case 3: settings.MouseSensitivity += direction * 0.1f; break;
                case 4: settings.GamepadSensitivity += direction * 0.1f; break;
                case 5: settings.FieldOfViewScale += direction * 0.025f; break;
                case 6: settings.RenderFrameRate = settings.RenderFrameRate == 60 ? 30 : 60; break;
                case 7: settings.GodMode = !settings.GodMode; break;
                default: return MenuAction.None;
            }
        }
        else if (Page == MenuPage.Accessibility)
        {
            switch (SelectedIndex)
            {
                case 0: settings.ReducedFlash = !settings.ReducedFlash; break;
                case 1: settings.ScreenShakeScale += direction * 0.1f; break;
                case 2: settings.CameraBobScale += direction * 0.1f; break;
                case 3: settings.HighContrastReticle = !settings.HighContrastReticle; break;
                case 4: settings.LargeHudText = !settings.LargeHudText; break;
                case 5: settings.Subtitles = !settings.Subtitles; break;
                case 6: settings.ToggleAimDownSights = !settings.ToggleAimDownSights; break;
                case 7:
                    int count = Enum.GetValues<ColorVisionMode>().Length;
                    settings.ColorVisionMode = (ColorVisionMode)(((int)settings.ColorVisionMode + direction + count) % count);
                    break;
                default: return MenuAction.None;
            }
        }
        else if (Page == MenuPage.Character && SelectedIndex == 3)
        {
            int branchCount = Enum.GetValues<TalentBranch>().Length;
            _talentBranch = (TalentBranch)(((int)_talentBranch + direction + branchCount) % branchCount);
            return MenuAction.None;
        }
        else if (Page == MenuPage.Inventory)
        {
            _confirmBatchDismantle = false;
            switch (SelectedIndex)
            {
                case 0:
                    int pageCount = Math.Max(1, (int)Math.Ceiling(
                        GetFilteredInventoryItems().Count / (double)InventoryPageSize));
                    _inventoryPage = (_inventoryPage + direction + pageCount) % pageCount;
                    break;
                case 1:
                    EquipmentSlot?[] slots =
                        [null, .. Enum.GetValues<EquipmentSlot>().Select(slot => (EquipmentSlot?)slot)];
                    int slotIndex = Array.IndexOf(slots, _inventorySlotFilter);
                    _inventorySlotFilter = slots[(slotIndex + direction + slots.Length) % slots.Length];
                    _inventoryPage = 0;
                    break;
                case 2:
                    ItemRarity?[] rarities =
                        [null, .. Enum.GetValues<ItemRarity>().Select(rarity => (ItemRarity?)rarity)];
                    int rarityIndex = Array.IndexOf(rarities, _inventoryRarityFilter);
                    _inventoryRarityFilter = rarities[(rarityIndex + direction + rarities.Length) % rarities.Length];
                    _inventoryPage = 0;
                    break;
                case 3:
                    _inventoryMinimumPower = Math.Clamp(_inventoryMinimumPower + (direction * 10), 1, 91);
                    _inventoryPage = 0;
                    break;
                default:
                    return MenuAction.None;
            }
            return MenuAction.None;
        }
        else if (Page == MenuPage.Abilities && SelectedIndex == 0)
        {
            int pageCount = Math.Max(1, (int)Math.Ceiling(GetVisibleAbilities().Length / (double)AbilityPageSize));
            _abilityPage = (_abilityPage + direction + pageCount) % pageCount;
            return MenuAction.None;
        }
        else if (Page == MenuPage.Crafting && SelectedIndex == 1)
        {
            int pageCount = Math.Max(1, (int)Math.Ceiling(GetCraftingItems().Count / (double)InventoryPageSize));
            _inventoryPage = (_inventoryPage + direction + pageCount) % pageCount;
            return MenuAction.None;
        }
        else
        {
            return MenuAction.None;
        }

        settings.Clamp();
        return MenuAction.SettingsChanged;
    }

    private bool Pressed(MenuInputButtons button, MenuInputSnapshot current) =>
        current.IsDown(button) && !_previousInput.IsDown(button);

    private MenuAction Finish(MenuInputSnapshot input, MenuAction action)
    {
        _previousInput = input;
        return action;
    }
}
