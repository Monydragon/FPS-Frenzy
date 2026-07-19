using FpsFrenzy.Kni.Progression;
using Microsoft.Xna.Framework;

namespace FpsFrenzy.Kni.Settings;

public enum MenuPage
{
    None,
    Main,
    Pause,
    Loadout,
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
}

public sealed class SettingsMenuController
{
    public static readonly string[] MainRows = ["START NEW RUN", "LOADOUT", "RECORDS", "SETTINGS", "ACCESSIBILITY", "QUIT TO DESKTOP"];
    private static readonly string[] MainRowsWithContinue = ["CONTINUE RUN", .. MainRows];
    public static readonly string[] PauseRows = ["RESUME", "SETTINGS", "ACCESSIBILITY", "RESTART STANDARD RUN", "MAIN MENU", "QUIT TO DESKTOP"];
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
    private ProfileData? _profile;
    private bool _hasCheckpoint;

    public MenuPage Page { get; private set; }
    public int SelectedIndex { get; private set; }
    public bool IsOpen => Page != MenuPage.None;
    public ProfileData? Profile => _profile;
    public string StartingWeaponId => _profile?.SelectedStartingWeaponId ?? "pulse-sidearm";
    public string? SelectedUpgradeId { get; private set; }

    public void ConfigureProfile(
        ProfileData profile,
        IEnumerable<(string Id, string DisplayName)> weapons,
        bool hasCheckpoint = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(weapons);
        _profile = profile;
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
        Page = MenuPage.Loadout;
        SelectedIndex = Math.Max(0, _loadoutWeaponIds.FindIndex(
            id => string.Equals(id, StartingWeaponId, StringComparison.OrdinalIgnoreCase)));
        _returnPage = MenuPage.Main;
    }

    public void OpenRecords()
    {
        Page = MenuPage.Records;
        SelectedIndex = 0;
        _returnPage = MenuPage.Main;
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

            if (Page is MenuPage.Loadout or MenuPage.Records or MenuPage.Tutorial)
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

        int rowCount = GetRows().Count;
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

    public IReadOnlyList<string> GetRows() => Page switch
    {
        MenuPage.Main => _hasCheckpoint ? MainRowsWithContinue : MainRows,
        MenuPage.Pause => PauseRows,
        MenuPage.Loadout => _loadoutRows,
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

        if (Page != MenuPage.Loadout || index < 0 || index >= _loadoutWeaponIds.Count || _profile is null)
        {
            return string.Empty;
        }

        string weaponId = _loadoutWeaponIds[index];
        if (string.Equals(weaponId, _profile.SelectedStartingWeaponId, StringComparison.OrdinalIgnoreCase))
        {
            return "EQUIPPED";
        }

        return _profile.UnlockedStartingWeaponIds.Contains(weaponId) ? "READY" : "LOCKED";
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
                    OpenLoadout();
                    return MenuAction.None;
                case 2:
                    OpenRecords();
                    return MenuAction.None;
                case 3:
                    OpenSettings(MenuPage.Main);
                    return MenuAction.None;
                case 4:
                    OpenAccessibility(MenuPage.Main);
                    return MenuAction.None;
                case 5:
                    return MenuAction.Quit;
            }
        }

        else if (Page == MenuPage.Loadout)
        {
            if (SelectedIndex == _loadoutRows.Count - 1)
            {
                OpenMain();
                return MenuAction.None;
            }

            if (_profile is not null && SelectedIndex >= 0 && SelectedIndex < _loadoutWeaponIds.Count)
            {
                string weaponId = _loadoutWeaponIds[SelectedIndex];
                if (_profile.UnlockedStartingWeaponIds.Contains(weaponId))
                {
                    _profile.SelectedStartingWeaponId = weaponId;
                    return MenuAction.StartingWeaponChanged;
                }
            }

            return MenuAction.None;
        }

        else if (Page == MenuPage.Records)
        {
            OpenMain();
            return MenuAction.None;
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
                    OpenSettings(MenuPage.Pause);
                    return MenuAction.None;
                case 2:
                    OpenAccessibility(MenuPage.Pause);
                    return MenuAction.None;
                case 3:
                    Page = MenuPage.None;
                    return MenuAction.Restart;
                case 4:
                    return MenuAction.ReturnToMain;
                case 5:
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
