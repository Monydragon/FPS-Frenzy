using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using GamePadButton = Microsoft.Xna.Framework.Input.Buttons;

namespace FpsFrenzy.Kni.Settings;

[Flags]
public enum MenuInputButtons
{
    None = 0,
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
    Accept = 1 << 4,
    Back = 1 << 5,
    Pause = 1 << 6,
    OpenSettings = 1 << 7,
    OpenAccessibility = 1 << 8,
    PreviousTab = 1 << 9,
    NextTab = 1 << 10,
}

public readonly record struct MenuInputSnapshot(
    MenuInputButtons Buttons,
    bool HasPointer = false,
    Point PointerPosition = default,
    bool PointerDown = false,
    int? Digit = null,
    bool Backspace = false,
    int ScrollDirection = 0,
    GamepadBindingButton? HeldGamepadButton = null,
    bool IsTouch = false)
{
    private static int? _previousMouseWheel;
    private static int? _previousTouchY;
    private static float _touchScrollAccumulator;

    public bool IsDown(MenuInputButtons button) => (Buttons & button) != 0;

    internal bool HasPointerSelectionIntent(MenuInputSnapshot previous) =>
        HasPointer &&
        (!previous.HasPointer || PointerPosition != previous.PointerPosition || PointerDown && !previous.PointerDown);

    public static MenuInputSnapshot Capture(MenuPage page, Rectangle safeArea)
    {
        KeyboardState keyboard = Keyboard.GetState();
        GamePadState gamePad = GamePad.GetState(PlayerIndex.One);
        MenuInputButtons buttons = MenuInputButtons.None;
        Set(ref buttons, MenuInputButtons.Up,
            keyboard.IsKeyDown(Keys.Up) || gamePad.IsButtonDown(GamePadButton.DPadUp) || gamePad.ThumbSticks.Left.Y > 0.55f);
        Set(ref buttons, MenuInputButtons.Down,
            keyboard.IsKeyDown(Keys.Down) || gamePad.IsButtonDown(GamePadButton.DPadDown) || gamePad.ThumbSticks.Left.Y < -0.55f);
        Set(ref buttons, MenuInputButtons.Left,
            keyboard.IsKeyDown(Keys.Left) || gamePad.IsButtonDown(GamePadButton.DPadLeft) || gamePad.ThumbSticks.Left.X < -0.55f);
        Set(ref buttons, MenuInputButtons.Right,
            keyboard.IsKeyDown(Keys.Right) || gamePad.IsButtonDown(GamePadButton.DPadRight) || gamePad.ThumbSticks.Left.X > 0.55f);
        Set(ref buttons, MenuInputButtons.Accept,
            keyboard.IsKeyDown(Keys.Enter) || keyboard.IsKeyDown(Keys.Space) || gamePad.IsButtonDown(GamePadButton.A));

        bool keyboardBack = keyboard.IsKeyDown(Keys.Escape) ||
            page != MenuPage.SeedKeypad && keyboard.IsKeyDown(Keys.Back);
        Set(ref buttons, MenuInputButtons.Back, keyboardBack);
        Set(ref buttons, MenuInputButtons.Back, gamePad.IsButtonDown(GamePadButton.B));
        Set(ref buttons, MenuInputButtons.Pause, gamePad.IsButtonDown(GamePadButton.Start));
        Set(ref buttons, MenuInputButtons.OpenSettings, keyboard.IsKeyDown(Keys.F2));
        Set(ref buttons, MenuInputButtons.OpenAccessibility, keyboard.IsKeyDown(Keys.F3));
        Set(ref buttons, MenuInputButtons.PreviousTab,
            keyboard.IsKeyDown(Keys.PageUp) || gamePad.IsButtonDown(GamePadButton.LeftShoulder));
        Set(ref buttons, MenuInputButtons.NextTab,
            keyboard.IsKeyDown(Keys.PageDown) || gamePad.IsButtonDown(GamePadButton.RightShoulder));

        bool hasPointer;
        bool pointerDown;
        Point pointerPosition;
        TouchCollection touches = TouchPanel.GetState();
        bool pauseTouched = false;
        int scrollDirection = 0;
        if (touches.Count > 0)
        {
            TouchLocation touch = touches[0];
            hasPointer = true;
            pointerDown = touch.State is TouchLocationState.Pressed or TouchLocationState.Moved;
            pointerPosition = new Point((int)touch.Position.X, (int)touch.Position.Y);
            if (_previousTouchY is int previousTouchY && pointerDown)
            {
                _touchScrollAccumulator += pointerPosition.Y - previousTouchY;
                if (MathF.Abs(_touchScrollAccumulator) >= 28f)
                {
                    scrollDirection = -Math.Sign(_touchScrollAccumulator);
                    _touchScrollAccumulator = 0f;
                }
            }
            _previousTouchY = pointerPosition.Y;

            if (page == MenuPage.None)
            {
                Rectangle pauseButton = MenuLayout.GetPauseButtonBounds(safeArea);
                foreach (TouchLocation activeTouch in touches)
                {
                    if (activeTouch.State is TouchLocationState.Pressed or TouchLocationState.Moved &&
                        pauseButton.Contains((int)activeTouch.Position.X, (int)activeTouch.Position.Y))
                    {
                        pauseTouched = true;
                        break;
                    }
                }
            }
        }
        else
        {
            _previousTouchY = null;
            _touchScrollAccumulator = 0f;
            // A connected touchscreen does not imply the user stopped using a mouse. Reading
            // its idle state is harmless on touch-only platforms and keeps hybrid PCs usable.
            MouseState mouse = Mouse.GetState();
            hasPointer = true;
            pointerDown = mouse.LeftButton == ButtonState.Pressed;
            pointerPosition = new Point(mouse.X, mouse.Y);
            if (_previousMouseWheel is int previousWheel && mouse.ScrollWheelValue != previousWheel)
            {
                scrollDirection = mouse.ScrollWheelValue < previousWheel ? 1 : -1;
            }
            _previousMouseWheel = mouse.ScrollWheelValue;
        }

        if (page == MenuPage.None &&
            (pauseTouched || pointerDown && MenuLayout.GetPauseButtonBounds(safeArea).Contains(pointerPosition)))
        {
            buttons |= MenuInputButtons.Pause;
        }

        int? digit = null;
        for (int value = 0; value <= 9; value++)
        {
            if (keyboard.IsKeyDown((Keys)((int)Keys.D0 + value)) ||
                keyboard.IsKeyDown((Keys)((int)Keys.NumPad0 + value)))
            {
                digit = value;
                break;
            }
        }

        return new MenuInputSnapshot(
            buttons, hasPointer, pointerPosition, pointerDown, digit, keyboard.IsKeyDown(Keys.Back),
            scrollDirection, ReadHeldGamepadButton(gamePad), touches.Count > 0);
    }

    private static GamepadBindingButton? ReadHeldGamepadButton(GamePadState gamePad)
    {
        foreach (GamepadBindingButton button in GamepadBindingCatalog.Buttons)
        {
            if (gamePad.IsButtonDown(ToGamePadButton(button)))
            {
                return button;
            }
        }

        return null;
    }

    private static GamePadButton ToGamePadButton(GamepadBindingButton button) => button switch
    {
        GamepadBindingButton.A => GamePadButton.A,
        GamepadBindingButton.B => GamePadButton.B,
        GamepadBindingButton.X => GamePadButton.X,
        GamepadBindingButton.Y => GamePadButton.Y,
        GamepadBindingButton.LeftShoulder => GamePadButton.LeftShoulder,
        GamepadBindingButton.RightShoulder => GamePadButton.RightShoulder,
        GamepadBindingButton.LeftStick => GamePadButton.LeftStick,
        GamepadBindingButton.RightStick => GamePadButton.RightStick,
        GamepadBindingButton.DPadUp => GamePadButton.DPadUp,
        GamepadBindingButton.DPadDown => GamePadButton.DPadDown,
        GamepadBindingButton.DPadLeft => GamePadButton.DPadLeft,
        GamepadBindingButton.DPadRight => GamePadButton.DPadRight,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    private static void Set(ref MenuInputButtons buttons, MenuInputButtons button, bool enabled)
    {
        if (enabled)
        {
            buttons |= button;
        }
    }
}

public readonly record struct MenuLayoutMetrics(
    Rectangle Panel,
    int RowLeft,
    int RowTop,
    int RowWidth,
    int RowHeight,
    int RowCount)
{
    public Rectangle GetRowBounds(int index)
    {
        if (index < 0 || index >= RowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new Rectangle(RowLeft, RowTop + (index * RowHeight), RowWidth, RowHeight);
    }

    public int HitTest(Point point)
    {
        for (int index = 0; index < RowCount; index++)
        {
            if (GetRowBounds(index).Contains(point))
            {
                return index;
            }
        }

        return -1;
    }
}

public readonly record struct MenuRowWindow(int Start, int Count);

public static class MenuLayout
{
    public static MenuRowWindow GetRowWindow(
        Rectangle safeArea,
        int rowCount,
        bool largeText,
        MenuPage page,
        int selectedIndex)
    {
        if (rowCount <= 0)
        {
            return new MenuRowWindow(0, Math.Max(0, rowCount));
        }

        float referenceScale = MathF.Min(safeArea.Width / 1280f, safeArea.Height / 720f);
        float uiScale = Math.Clamp(referenceScale, 0.72f, 1.75f) * (largeText ? 1.25f : 1f);
        int rowHeight = CalculateRowHeight(page, uiScale);
        int rowTop = IsDensePage(page)
            ? safeArea.Top + (int)MathF.Round((largeText ? 225f : 205f) * referenceScale)
            : page switch
            {
                MenuPage.Main => safeArea.Top + (int)MathF.Round(188f * referenceScale),
                MenuPage.Results => safeArea.Center.Y + 94,
                MenuPage.Reward => safeArea.Center.Y - 72,
                MenuPage.WeaponPickup => safeArea.Center.Y - 20,
                _ => safeArea.Center.Y - (int)MathF.Round(105f * uiScale),
            };
        int panelTop = rowTop - (int)MathF.Round(22f * uiScale);
        int panelBottom = safeArea.Bottom - (int)MathF.Round(70f * referenceScale);
        int panelPadding = (int)MathF.Round(42f * uiScale);
        int capacity = Math.Max(1, (panelBottom - panelTop - panelPadding) / rowHeight);
        int count = Math.Min(rowCount, Math.Min(capacity, MaximumVisibleRows(page, largeText)));
        int centeredStart = Math.Clamp(selectedIndex - (count / 2), 0, Math.Max(0, rowCount - count));
        return new MenuRowWindow(centeredStart, count);
    }

    public static MenuLayoutMetrics Create(Rectangle safeArea, int rowCount, bool largeText, MenuPage page)
    {
        float referenceScale = MathF.Min(safeArea.Width / 1280f, safeArea.Height / 720f);
        float uiScale = Math.Clamp(referenceScale, 0.72f, 1.75f) * (largeText ? 1.25f : 1f);
        bool densePage = IsDensePage(page);
        int rowHeight = CalculateRowHeight(page, uiScale);
        int panelWidth;
        int panelLeft;
        if (page == MenuPage.Pause)
        {
            int expandedMapWidth = Math.Min(340, safeArea.Width / 3);
            panelWidth = Math.Min((int)MathF.Round(610f * uiScale),
                Math.Max(320, safeArea.Width - expandedMapWidth - 80));
            panelLeft = safeArea.Left + 40;
        }
        else if (page == MenuPage.Main)
        {
            panelWidth = Math.Min((int)MathF.Round(456f * uiScale),
                Math.Max(280, safeArea.Width - 40));
            panelLeft = safeArea.Right - (int)MathF.Round(36f * referenceScale) - panelWidth;
        }
        else
        {
            panelWidth = Math.Min((int)MathF.Round(720f * uiScale),
                Math.Max(280, safeArea.Width - 40));
            panelLeft = safeArea.Center.X - (panelWidth / 2);
        }
        int rowTop = page switch
        {
            MenuPage.Main => safeArea.Top + (int)MathF.Round(188f * referenceScale),
            MenuPage.Results => safeArea.Center.Y + 94,
            MenuPage.Reward => safeArea.Center.Y - 72,
            MenuPage.WeaponPickup => safeArea.Center.Y - 20,
            _ when densePage => safeArea.Top + (int)MathF.Round((largeText ? 225f : 205f) * referenceScale),
            _ => safeArea.Center.Y - (int)MathF.Round(105f * uiScale),
        };
        int panelHeight = (rowCount * rowHeight) + (int)MathF.Round(42f * uiScale);
        Rectangle panel = new(panelLeft - (int)MathF.Round(18f * uiScale),
            rowTop - (int)MathF.Round(22f * uiScale),
            panelWidth + (int)MathF.Round(36f * uiScale), panelHeight);
        return new MenuLayoutMetrics(panel, panelLeft - (int)MathF.Round(5f * uiScale),
            rowTop - (int)MathF.Round(7f * uiScale), panelWidth + (int)MathF.Round(10f * uiScale),
            rowHeight, rowCount);
    }

    public static Rectangle GetPauseButtonBounds(Rectangle safeArea) =>
        new(safeArea.Right - 148, safeArea.Top + 66, 104, 48);

    public static Rectangle GetScrollUpBounds(MenuLayoutMetrics layout) =>
        new(layout.Panel.Right - 29, layout.Panel.Top + 4, 22, 20);

    public static Rectangle GetScrollDownBounds(MenuLayoutMetrics layout) =>
        new(layout.Panel.Right - 29, layout.Panel.Bottom - 24, 22, 20);

    public static Rectangle GetScrollTrackBounds(MenuLayoutMetrics layout) =>
        new(layout.Panel.Right - 19, layout.Panel.Top + 28, 5, Math.Max(12, layout.Panel.Height - 56));

    public static Rectangle GetProfileTabBounds(Rectangle safeArea, int index)
    {
        const int count = 7;
        float referenceScale = MathF.Min(safeArea.Width / 1280f, safeArea.Height / 720f);
        int totalWidth = Math.Min(700, safeArea.Width - 40);
        int width = totalWidth / count;
        int left = safeArea.Center.X - (totalWidth / 2);
        return new Rectangle(left + (index * width), safeArea.Top + (int)MathF.Round(145f * referenceScale),
            width, Math.Max(24, (int)MathF.Round(27f * referenceScale)));
    }

    public static int HitTestProfileTab(Rectangle safeArea, Point point)
    {
        for (int index = 0; index < 7; index++)
        {
            if (GetProfileTabBounds(safeArea, index).Contains(point))
            {
                return index;
            }
        }
        return -1;
    }

    private static bool IsDensePage(MenuPage page) =>
        page is MenuPage.Character or MenuPage.Inventory or MenuPage.Loadout or MenuPage.Abilities or
            MenuPage.Proficiencies or MenuPage.Crafting or MenuPage.CraftingItem or MenuPage.Stats;

    private static int CalculateRowHeight(MenuPage page, float uiScale)
    {
        int baseRowHeight = page == MenuPage.Reward ? 62
            : page == MenuPage.Loadout ? 26
            : IsDensePage(page) ? 28
            : page == MenuPage.Main ? 48
            : 40;
        return Math.Max(16, (int)MathF.Round(baseRowHeight * uiScale));
    }

    private static int MaximumVisibleRows(MenuPage page, bool largeText) => page switch
    {
        MenuPage.Main => 6,
        MenuPage.Reward or MenuPage.Results or MenuPage.WeaponPickup => 3,
        MenuPage.Settings or MenuPage.Accessibility or MenuPage.Controls or MenuPage.Pause => largeText ? 6 : 7,
        MenuPage.SeedKeypad => largeText ? 6 : 7,
        MenuPage.Loadout => largeText ? 7 : 9,
        _ when IsDensePage(page) => largeText ? 9 : 12,
        _ => largeText ? 7 : 8,
    };
}
