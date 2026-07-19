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
}

public readonly record struct MenuInputSnapshot(
    MenuInputButtons Buttons,
    bool HasPointer = false,
    Point PointerPosition = default,
    bool PointerDown = false)
{
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

        bool keyboardBack = keyboard.IsKeyDown(Keys.Escape) || keyboard.IsKeyDown(Keys.Back);
        Set(ref buttons, MenuInputButtons.Back, keyboardBack);
        Set(ref buttons, MenuInputButtons.Back, gamePad.IsButtonDown(GamePadButton.B));
        Set(ref buttons, MenuInputButtons.Pause, gamePad.IsButtonDown(GamePadButton.Start));
        Set(ref buttons, MenuInputButtons.OpenSettings, keyboard.IsKeyDown(Keys.F2));
        Set(ref buttons, MenuInputButtons.OpenAccessibility, keyboard.IsKeyDown(Keys.F3));

        bool hasPointer;
        bool pointerDown;
        Point pointerPosition;
        TouchCollection touches = TouchPanel.GetState();
        bool pauseTouched = false;
        if (touches.Count > 0)
        {
            TouchLocation touch = touches[0];
            hasPointer = true;
            pointerDown = touch.State is TouchLocationState.Pressed or TouchLocationState.Moved;
            pointerPosition = new Point((int)touch.Position.X, (int)touch.Position.Y);

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
            // A connected touchscreen does not imply the user stopped using a mouse. Reading
            // its idle state is harmless on touch-only platforms and keeps hybrid PCs usable.
            MouseState mouse = Mouse.GetState();
            hasPointer = true;
            pointerDown = mouse.LeftButton == ButtonState.Pressed;
            pointerPosition = new Point(mouse.X, mouse.Y);
        }

        if (page == MenuPage.None &&
            (pauseTouched || pointerDown && MenuLayout.GetPauseButtonBounds(safeArea).Contains(pointerPosition)))
        {
            buttons |= MenuInputButtons.Pause;
        }

        return new MenuInputSnapshot(buttons, hasPointer, pointerPosition, pointerDown);
    }

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

public static class MenuLayout
{
    public static MenuLayoutMetrics Create(Rectangle safeArea, int rowCount, bool largeText, MenuPage page)
    {
        int rowHeight = page == MenuPage.Reward
            ? (largeText ? 70 : 62)
            : (largeText ? 39 : 31);
        int panelWidth = Math.Min(720, Math.Max(280, safeArea.Width - 120));
        int panelLeft = safeArea.Center.X - (panelWidth / 2);
        int rowTop = page switch
        {
            MenuPage.Main => safeArea.Center.Y - 72,
            MenuPage.Results => safeArea.Center.Y + 94,
            MenuPage.Reward => safeArea.Center.Y - 72,
            _ => safeArea.Center.Y - 105,
        };
        int panelHeight = (rowCount * rowHeight) + 42;
        Rectangle panel = new(panelLeft - 18, rowTop - 22, panelWidth + 36, panelHeight);
        return new MenuLayoutMetrics(panel, panelLeft - 5, rowTop - 7, panelWidth + 10, rowHeight, rowCount);
    }

    public static Rectangle GetPauseButtonBounds(Rectangle safeArea) =>
        new(safeArea.Right - 148, safeArea.Top + 66, 104, 48);
}
