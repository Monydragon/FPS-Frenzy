using FpsFrenzy.Core;
using FpsFrenzy.Core.Input;
using FpsFrenzy.Kni.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using CoreVector2 = System.Numerics.Vector2;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace FpsFrenzy.Kni.Input;

public sealed class KniInputSource
{
    private readonly IPlatformLookSource? _platformLookSource;
    private readonly IPlatformMouseCapture? _mouseCapture;
    private readonly GameSettings _settings;
    private readonly Dictionary<int, XnaVector2> _touchOrigins = [];
    private readonly Dictionary<int, XnaVector2> _touchPrevious = [];
    private bool _mouseInitialized;
    private bool _mouseWheelInitialized;
    private bool _touchAdsToggled;
    private bool _desktopAdsToggled;
    private bool _previousDesktopAim;
    private bool _previousDPadLeft;
    private bool _previousDPadRight;
    private int _previousMouseWheel;

    public KniInputSource(
        GameSettings settings,
        IPlatformLookSource? platformLookSource = null,
        IPlatformMouseCapture? mouseCapture = null)
    {
        _settings = settings;
        _platformLookSource = platformLookSource;
        _mouseCapture = mouseCapture;
    }

    public PlayerCommand Sample(
        uint tick,
        EntityId playerId,
        GraphicsDevice graphicsDevice,
        bool mouseLookEnabled,
        bool aiming,
        int currentWeaponSlot,
        int weaponCount,
        float aimSensitivityMultiplier = 0.68f,
        bool isDualWielding = false,
        IReadOnlyList<bool>? populatedWeaponSlots = null)
    {
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
        GamePadState gamePad = GamePad.GetState(PlayerIndex.One);
        int centerX = graphicsDevice.Viewport.Width / 2;
        int centerY = graphicsDevice.Viewport.Height / 2;

        CoreVector2 movement = new(
            Axis(keyboard, Keys.D, Keys.A) + gamePad.ThumbSticks.Left.X,
            Axis(keyboard, Keys.W, Keys.S) + gamePad.ThumbSticks.Left.Y);
        CoreVector2 look = CoreVector2.Zero;
        if (mouseLookEnabled && _mouseCapture is not null)
        {
            CoreVector2 relativeMouse = _mouseCapture.ConsumeRelativeLookDelta();
            float sensitivity = 0.0025f * (aiming ? aimSensitivityMultiplier : 1f) *
                _settings.MouseSensitivity;
            look += new CoreVector2(relativeMouse.X * sensitivity, -relativeMouse.Y * sensitivity);
            _mouseInitialized = true;
        }
        else if (mouseLookEnabled && _mouseInitialized)
        {
            float sensitivity = 0.0025f * (aiming ? aimSensitivityMultiplier : 1f) *
                _settings.MouseSensitivity;
            look += new CoreVector2((mouse.X - centerX) * sensitivity, (centerY - mouse.Y) * sensitivity);
        }
        else if (mouseLookEnabled)
        {
            _mouseInitialized = true;
        }
        else
        {
            _mouseInitialized = false;
        }

        if (mouseLookEnabled && _mouseCapture is null)
        {
            Mouse.SetPosition(centerX, centerY);
        }
        look += new CoreVector2(gamePad.ThumbSticks.Right.X * 0.045f, gamePad.ThumbSticks.Right.Y * 0.045f) *
            _settings.GamepadSensitivity;

        PlayerButtons buttons = PlayerButtons.None;
        Set(ref buttons, PlayerButtons.FireRight,
            mouse.LeftButton == ButtonState.Pressed || gamePad.Triggers.Right > 0.2f);
        bool secondaryPressed = mouse.RightButton == ButtonState.Pressed || gamePad.Triggers.Left > 0.2f;
        bool dedicatedFocus = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ||
            gamePad.Buttons.LeftShoulder == ButtonState.Pressed;
        ContextualSecondaryAction secondary = ContextualWeaponInput.Resolve(
            secondaryPressed, dedicatedFocus, isDualWielding);
        Set(ref buttons, PlayerButtons.FireLeft, secondary.FireLeft);
        bool desktopAim = secondary.Focus;
        if (_settings.ToggleAimDownSights && desktopAim && !_previousDesktopAim)
        {
            _desktopAdsToggled = !_desktopAdsToggled;
        }

        _previousDesktopAim = desktopAim;
        Set(ref buttons, PlayerButtons.AimDownSights,
            _settings.ToggleAimDownSights ? _desktopAdsToggled : desktopAim);
        Set(ref buttons, PlayerButtons.Reload, keyboard.IsKeyDown(Keys.R) || gamePad.Buttons.X == ButtonState.Pressed);
        Set(ref buttons, PlayerButtons.Jump, keyboard.IsKeyDown(Keys.Space) || gamePad.Buttons.A == ButtonState.Pressed);
        Set(ref buttons, PlayerButtons.Interact, keyboard.IsKeyDown(Keys.E) ||
            gamePad.Buttons.Y == ButtonState.Pressed);
        Set(ref buttons, PlayerButtons.Ability1, keyboard.IsKeyDown(Keys.Q) ||
            gamePad.Buttons.B == ButtonState.Pressed);
        Set(ref buttons, PlayerButtons.Ability2, keyboard.IsKeyDown(Keys.F) ||
            gamePad.Buttons.RightShoulder == ButtonState.Pressed);
        int selectedSlot = ReadWeaponSlot(keyboard);
        if (_mouseWheelInitialized && mouse.ScrollWheelValue != _previousMouseWheel)
        {
            selectedSlot = CycleWeapon(
                currentWeaponSlot,
                mouse.ScrollWheelValue > _previousMouseWheel ? -1 : 1,
                weaponCount,
                populatedWeaponSlots);
        }
        bool dPadLeft = gamePad.DPad.Left == ButtonState.Pressed;
        bool dPadRight = gamePad.DPad.Right == ButtonState.Pressed;
        if (dPadLeft && !_previousDPadLeft)
        {
            selectedSlot = CycleWeapon(currentWeaponSlot, -1, weaponCount, populatedWeaponSlots);
        }
        else if (dPadRight && !_previousDPadRight)
        {
            selectedSlot = CycleWeapon(currentWeaponSlot, 1, weaponCount, populatedWeaponSlots);
        }
        _previousDPadLeft = dPadLeft;
        _previousDPadRight = dPadRight;

        _mouseWheelInitialized = true;
        _previousMouseWheel = mouse.ScrollWheelValue;
        ReadTouch(
            graphicsDevice.Viewport,
            currentWeaponSlot,
            weaponCount,
            ref movement,
            ref look,
            ref buttons,
            ref selectedSlot,
            populatedWeaponSlots);
        if (selectedSlot >= 0 && selectedSlot != currentWeaponSlot)
        {
            ClearAimLatch();
            buttons &= ~PlayerButtons.AimDownSights;
        }
        if (_platformLookSource?.IsAvailable == true)
        {
            look += _platformLookSource.ConsumeLookDelta(FpsFrenzy.Core.Simulation.GameSimulation.FixedDeltaSeconds) *
                _settings.GamepadSensitivity;
        }

        if (movement.LengthSquared() > 1f)
        {
            movement = CoreVector2.Normalize(movement);
        }

        return new PlayerCommand(tick, playerId, movement, look, buttons, selectedSlot);
    }

    public void ClearAimLatch()
    {
        _desktopAdsToggled = false;
        _touchAdsToggled = false;
        _previousDesktopAim = false;
    }

    private void ReadTouch(
        Viewport viewport,
        int currentWeaponSlot,
        int weaponCount,
        ref CoreVector2 movement,
        ref CoreVector2 look,
        ref PlayerButtons buttons,
        ref int selectedSlot,
        IReadOnlyList<bool>? populatedWeaponSlots)
    {
        TouchCollection touches = TouchPanel.GetState();
        HashSet<int> active = [];
        foreach (TouchLocation touch in touches)
        {
            active.Add(touch.Id);
            XnaVector2 position = touch.Position;
            bool isNewTouch = touch.State == TouchLocationState.Pressed;
            if (isNewTouch || !_touchOrigins.TryGetValue(touch.Id, out XnaVector2 origin))
            {
                _touchOrigins[touch.Id] = position;
                _touchPrevious[touch.Id] = position;
                origin = position;
                if (IsAdsButton(position, viewport))
                {
                    _touchAdsToggled = !_touchAdsToggled;
                }
            }

            XnaVector2 previous = _touchPrevious[touch.Id];
            if (origin.X < viewport.Width * 0.43f && origin.Y > viewport.Height * 0.35f)
            {
                XnaVector2 delta = position - origin;
                float radius = MathF.Max(72f, viewport.Height * 0.13f);
                movement += new CoreVector2(
                    Math.Clamp(delta.X / radius, -1f, 1f),
                    Math.Clamp(-delta.Y / radius, -1f, 1f));
            }
            else if (!IsActionButton(origin, viewport))
            {
                XnaVector2 delta = position - previous;
                look += new CoreVector2(delta.X * 0.0035f, -delta.Y * 0.0035f);
            }

            Set(ref buttons, PlayerButtons.FireRight,
                buttons.HasFlag(PlayerButtons.FireRight) || IsFireButton(position, viewport));
            Set(ref buttons, PlayerButtons.FireLeft,
                buttons.HasFlag(PlayerButtons.FireLeft) || IsLeftFireButton(position, viewport));
            Set(ref buttons, PlayerButtons.Reload, buttons.HasFlag(PlayerButtons.Reload) || IsReloadButton(position, viewport));
            Set(ref buttons, PlayerButtons.Jump, buttons.HasFlag(PlayerButtons.Jump) || IsJumpButton(position, viewport));
            Set(ref buttons, PlayerButtons.Interact,
                buttons.HasFlag(PlayerButtons.Interact) || IsInteractButton(position, viewport));
            Set(ref buttons, PlayerButtons.Ability1,
                buttons.HasFlag(PlayerButtons.Ability1) || IsAbility1Button(position, viewport));
            Set(ref buttons, PlayerButtons.Ability2,
                buttons.HasFlag(PlayerButtons.Ability2) || IsAbility2Button(position, viewport));
            Set(ref buttons, PlayerButtons.SwapWeaponSet,
                buttons.HasFlag(PlayerButtons.SwapWeaponSet) || IsWeaponSetButton(position, viewport));
            if (isNewTouch && IsPreviousWeaponButton(position, viewport))
            {
                selectedSlot = CycleWeapon(currentWeaponSlot, -1, weaponCount, populatedWeaponSlots);
            }
            else if (isNewTouch && IsNextWeaponButton(position, viewport))
            {
                selectedSlot = CycleWeapon(currentWeaponSlot, 1, weaponCount, populatedWeaponSlots);
            }

            _touchPrevious[touch.Id] = position;
        }

        Set(ref buttons, PlayerButtons.AimDownSights, buttons.HasFlag(PlayerButtons.AimDownSights) || _touchAdsToggled);
        foreach (int id in _touchOrigins.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            _touchOrigins.Remove(id);
            _touchPrevious.Remove(id);
        }
    }

    private static int ReadWeaponSlot(KeyboardState keyboard)
    {
        Keys[] keys =
            [Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0];
        for (int index = 0; index < keys.Length; index++)
        {
            if (keyboard.IsKeyDown(keys[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static float Axis(KeyboardState keyboard, Keys positive, Keys negative) =>
        (keyboard.IsKeyDown(positive) ? 1f : 0f) - (keyboard.IsKeyDown(negative) ? 1f : 0f);

    private static void Set(ref PlayerButtons buttons, PlayerButtons button, bool value)
    {
        if (value)
        {
            buttons |= button;
        }
    }

    private static int CycleWeapon(
        int currentSlot,
        int direction,
        int weaponCount,
        IReadOnlyList<bool>? populatedWeaponSlots = null)
    {
        if (weaponCount <= 0)
        {
            return -1;
        }

        direction = direction < 0 ? -1 : 1;
        for (int offset = 1; offset <= weaponCount; offset++)
        {
            int candidate = (currentSlot + (offset * direction) + weaponCount) % weaponCount;
            if (populatedWeaponSlots is null || candidate >= populatedWeaponSlots.Count || populatedWeaponSlots[candidate])
            {
                return candidate;
            }
        }
        return currentSlot;
    }

    private static bool IsActionButton(XnaVector2 position, Viewport viewport) =>
        IsFireButton(position, viewport) || IsLeftFireButton(position, viewport) ||
        IsAdsButton(position, viewport) || IsReloadButton(position, viewport) ||
        IsJumpButton(position, viewport) || IsPreviousWeaponButton(position, viewport) ||
        IsNextWeaponButton(position, viewport) || IsInteractButton(position, viewport) ||
        IsAbility1Button(position, viewport) || IsAbility2Button(position, viewport) ||
        IsWeaponSetButton(position, viewport) ||
        IsPauseButton(position, viewport);

    private static bool IsFireButton(XnaVector2 position, Viewport viewport) => position.X > viewport.Width * 0.82f && position.Y > viewport.Height * 0.58f;
    private static bool IsLeftFireButton(XnaVector2 position, Viewport viewport) =>
        position.X > viewport.Width * 0.70f && position.X <= viewport.Width * 0.84f &&
        position.Y > viewport.Height * 0.44f && position.Y <= viewport.Height * 0.62f;
    private static bool IsAdsButton(XnaVector2 position, Viewport viewport) => position.X > viewport.Width * 0.84f && position.Y is > 0f && position.Y < viewport.Height * 0.38f;
    private static bool IsReloadButton(XnaVector2 position, Viewport viewport) => position.X is > 0f && position.X > viewport.Width * 0.70f && position.X < viewport.Width * 0.84f && position.Y < viewport.Height * 0.45f;
    private static bool IsJumpButton(XnaVector2 position, Viewport viewport) => position.X > viewport.Width * 0.66f && position.X < viewport.Width * 0.82f && position.Y > viewport.Height * 0.66f;
    private static bool IsPreviousWeaponButton(XnaVector2 position, Viewport viewport) => position.X > viewport.Width * 0.51f && position.X < viewport.Width * 0.61f && position.Y > viewport.Height * 0.7f;
    private static bool IsNextWeaponButton(XnaVector2 position, Viewport viewport) => position.X >= viewport.Width * 0.61f && position.X < viewport.Width * 0.70f && position.Y > viewport.Height * 0.7f;
    private static bool IsInteractButton(XnaVector2 position, Viewport viewport) =>
        position.X > viewport.Width * 0.43f && position.X < viewport.Width * 0.57f &&
        position.Y > viewport.Height * 0.78f;
    private static bool IsAbility1Button(XnaVector2 position, Viewport viewport) =>
        position.X > viewport.Width * 0.52f && position.X <= viewport.Width * 0.62f &&
        position.Y > viewport.Height * 0.54f && position.Y < viewport.Height * 0.7f;
    private static bool IsAbility2Button(XnaVector2 position, Viewport viewport) =>
        position.X > viewport.Width * 0.62f && position.X <= viewport.Width * 0.72f &&
        position.Y > viewport.Height * 0.54f && position.Y < viewport.Height * 0.7f;
    private static bool IsWeaponSetButton(XnaVector2 position, Viewport viewport) =>
        position.X > viewport.Width * 0.43f && position.X < viewport.Width * 0.52f &&
        position.Y > viewport.Height * 0.62f && position.Y < viewport.Height * 0.78f;
    private static bool IsPauseButton(XnaVector2 position, Viewport viewport) =>
        MenuLayout.GetPauseButtonBounds(viewport.TitleSafeArea).Contains((int)position.X, (int)position.Y);
}
