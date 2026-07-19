using Microsoft.Xna.Framework;

namespace FpsFrenzy.Kni.Settings;

public enum MenuPage
{
    None,
    Main,
    Pause,
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
    Restart,
    ReturnToMain,
    Quit,
    SettingsChanged,
}

public sealed class SettingsMenuController
{
    public static readonly string[] MainRows = ["START STANDARD RUN", "SETTINGS", "ACCESSIBILITY", "QUIT TO DESKTOP"];
    public static readonly string[] PauseRows = ["RESUME", "SETTINGS", "ACCESSIBILITY", "RESTART STANDARD RUN", "MAIN MENU", "QUIT TO DESKTOP"];
    public static readonly string[] SettingsRows = ["MASTER VOLUME", "SFX VOLUME", "MOUSE SENSITIVITY", "GAMEPAD SENSITIVITY", "FIELD OF VIEW", "FRAME RATE", "GOD MODE", "BACK"];
    public static readonly string[] AccessibilityRows = ["REDUCED FLASH", "SCREEN SHAKE", "CAMERA BOB", "HIGH CONTRAST RETICLE", "LARGE HUD TEXT", "SUBTITLES", "TOGGLE ADS", "COLOR VISION", "BACK"];
    public static readonly string[] ResultsRows = ["PLAY AGAIN", "MAIN MENU", "QUIT TO DESKTOP"];

    private MenuInputSnapshot _previousInput;
    private MenuPage _returnPage = MenuPage.Pause;

    public MenuPage Page { get; private set; }
    public int SelectedIndex { get; private set; }
    public bool IsOpen => Page != MenuPage.None;

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
        if (pointerRow >= 0)
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
        bool pointerActivated = pointerRow >= 0 && input.PointerDown && !_previousInput.PointerDown;
        if (Pressed(MenuInputButtons.Accept, input) || pointerActivated)
        {
            action = Activate(settings);
        }

        return Finish(input, action);
    }

    public IReadOnlyList<string> GetRows() => Page switch
    {
        MenuPage.Main => MainRows,
        MenuPage.Pause => PauseRows,
        MenuPage.Settings => SettingsRows,
        MenuPage.Accessibility => AccessibilityRows,
        MenuPage.Results => ResultsRows,
        _ => [],
    };

    private MenuAction Activate(GameSettings settings)
    {
        if (Page == MenuPage.Main)
        {
            switch (SelectedIndex)
            {
                case 0:
                    Page = MenuPage.None;
                    return MenuAction.StartRun;
                case 1:
                    OpenSettings(MenuPage.Main);
                    return MenuAction.None;
                case 2:
                    OpenAccessibility(MenuPage.Main);
                    return MenuAction.None;
                case 3:
                    return MenuAction.Quit;
            }
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

    private MenuAction Adjust(GameSettings settings, int direction)
    {
        if (Page == MenuPage.Settings)
        {
            switch (SelectedIndex)
            {
                case 0: settings.MasterVolume += direction * 0.05f; break;
                case 1: settings.SoundEffectsVolume += direction * 0.05f; break;
                case 2: settings.MouseSensitivity += direction * 0.1f; break;
                case 3: settings.GamepadSensitivity += direction * 0.1f; break;
                case 4: settings.FieldOfViewScale += direction * 0.025f; break;
                case 5: settings.RenderFrameRate = settings.RenderFrameRate == 60 ? 30 : 60; break;
                case 6: settings.GodMode = !settings.GodMode; break;
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
