using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Settings;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;

namespace FpsFrenzy.Kni.Rendering;

public sealed class HudRenderer : IDisposable
{
    private static readonly string[] CardinalNames = ["N", "E", "S", "W"];
    private static readonly float[] CardinalAngles = [0f, MathF.PI / 2f, MathF.PI, -MathF.PI / 2f];
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _pixel;
    private readonly Texture2D _menuButton;
    private readonly Texture2D _menuButtonSelected;
    private readonly Texture2D _menuEmblem;
    private readonly PixelFont _font;
    private readonly int[] _compassMarkerCounts = new int[64];
    private readonly int[] _compassVerticalDirections = new int[64];
    private readonly bool[] _compassBehindMarkers = new bool[64];
    private bool _disposed;

    public HudRenderer(
        GraphicsDevice graphicsDevice,
        Texture2D menuButton,
        Texture2D menuButtonSelected,
        Texture2D menuEmblem)
    {
        _graphicsDevice = graphicsDevice;
        _menuButton = menuButton;
        _menuButtonSelected = menuButtonSelected;
        _menuEmblem = menuEmblem;
        _spriteBatch = new SpriteBatch(graphicsDevice);
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _font = new PixelFont(_pixel);
    }

    public void Draw(
        GameSimulation simulation,
        GameSettings settings,
        SettingsMenuController menu,
        string? caption,
        bool showGameplayHud)
    {
        Rectangle safe = _graphicsDevice.Viewport.TitleSafeArea;
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        if (showGameplayHud)
        {
            DrawDamageFeedback(simulation, settings, safe);
            DrawCompass(simulation, safe);
            DrawHealth(simulation, safe);
            DrawWeapon(simulation, safe);
            DrawWaveAndScore(simulation, safe);
            DrawBossHealth(simulation, safe);
            DrawReticle(simulation, settings, safe);
            DrawTouchControls(safe);
            if (settings.Subtitles && caption is not null)
            {
                DrawCaption(caption, safe, settings.LargeHudText ? 3 : 2);
            }
        }

        if (!showGameplayHud || simulation.Phase != GamePhase.Playing || menu.IsOpen)
        {
            DrawPhaseOverlay(simulation, settings, menu, safe);
        }

        _spriteBatch.End();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pixel.Dispose();
        _spriteBatch.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void DrawCompass(GameSimulation simulation, Rectangle safe)
    {
        int centerX = safe.Center.X;
        int top = safe.Top + 18;
        int compassWidth = Math.Min(720, safe.Width - 80);
        int left = centerX - (compassWidth / 2);
        DrawRect(new Rectangle(left, top + 28, compassWidth, 2), new Color(140, 190, 220, 180));
        DrawRect(new Rectangle(centerX - 1, top + 24, 3, 10), Color.White);

        for (int index = 0; index < CardinalNames.Length; index++)
        {
            float relative = HudMath.WrapAngle(CardinalAngles[index] - simulation.Player.Yaw);
            if (MathF.Abs(relative) <= MathF.PI / 2f)
            {
                float normalized = (relative / MathF.PI) + 0.5f;
                float x = left + (normalized * compassWidth);
                _font.Draw(_spriteBatch, CardinalNames[index], new Vector2(x - 5, top), Color.LightCyan, 2);
            }
        }

        Array.Clear(_compassMarkerCounts);
        Array.Clear(_compassVerticalDirections);
        Array.Clear(_compassBehindMarkers);
        foreach (EnemyState enemy in simulation.Enemies)
        {
            if (enemy.IsDead)
            {
                continue;
            }

            CompassProjection projection = HudMath.ProjectEnemy(
                simulation.Player.Yaw,
                simulation.Player.Position,
                enemy.Position);
            float x = projection.IsBehind
                ? (projection.RelativeBearing < 0f ? left + 5 : left + compassWidth - 5)
                : left + (projection.NormalizedPosition * compassWidth);
            int bin = Math.Clamp((int)((x - left) / 14f), 0, _compassMarkerCounts.Length - 1);
            _compassMarkerCounts[bin]++;
            _compassVerticalDirections[bin] = projection.VerticalDirection;
            _compassBehindMarkers[bin] |= projection.IsBehind;
        }

        for (int bin = 0; bin < _compassMarkerCounts.Length; bin++)
        {
            int count = _compassMarkerCounts[bin];
            if (count == 0)
            {
                continue;
            }

            int x = left + (bin * 14);
            DrawRect(new Rectangle(x - 3, top + 35, 7, 7), new Color(255, 78, 110));
            if (_compassBehindMarkers[bin])
            {
                int direction = x < centerX ? 1 : -1;
                DrawRect(new Rectangle(x + (direction * 5), top + 36, 5, 2), new Color(255, 78, 110));
                DrawRect(new Rectangle(x + (direction * 8), top + 34, 2, 6), new Color(255, 78, 110));
            }

            int vertical = _compassVerticalDirections[bin];
            if (vertical != 0)
            {
                int chevronY = vertical > 0 ? top + 31 : top + 44;
                DrawRect(new Rectangle(x - 3, chevronY, 7, 2), Color.White);
                DrawRect(new Rectangle(x - 1, chevronY + (vertical > 0 ? -2 : 2), 3, 2), Color.White);
            }

            if (count > 1)
            {
                _font.DrawNumber(_spriteBatch, count, new Vector2(x + 7, top + 32), Color.White, 1);
            }
        }
    }

    private void DrawHealth(GameSimulation simulation, Rectangle safe)
    {
        const int width = 240;
        int x = safe.Left + 24;
        int y = safe.Bottom - 62;
        DrawRect(new Rectangle(x, y, width, 20), new Color(10, 16, 25, 220));
        int fill = (int)(width * (simulation.Player.Health / simulation.Player.MaximumHealth));
        DrawRect(new Rectangle(x + 2, y + 2, Math.Max(0, fill - 4), 16), new Color(50, 225, 140));
        _font.Draw(_spriteBatch, "HEALTH", new Vector2(x, y - 22), Color.White, 2);
        _font.DrawNumber(_spriteBatch, (int)MathF.Ceiling(simulation.Player.Health),
            new Vector2(x + PixelFont.Measure("HEALTH ", 2).X, y - 22), Color.White, 2);
    }

    private void DrawWeapon(GameSimulation simulation, Rectangle safe)
    {
        WeaponState weapon = simulation.Player.CurrentWeapon;
        int scale = safe.Width < 1000 ? 1 : 2;
        Vector2 nameSize = PixelFont.Measure(weapon.Definition.DisplayName, scale);
        _font.Draw(_spriteBatch, weapon.Definition.DisplayName,
            new Vector2(safe.Right - nameSize.X - 24, safe.Bottom - 78), Color.LightCyan, scale);
        DrawAmmo(weapon, safe.Right - 24, safe.Bottom - 48, scale + 1);
        if (weapon.IsReloading)
        {
            _font.Draw(_spriteBatch, "RELOADING", new Vector2(safe.Right - 150, safe.Bottom - 104), new Color(255, 210, 70), 2);
        }
    }

    private void DrawWaveAndScore(GameSimulation simulation, Rectangle safe)
    {
        int scale = safe.Width < 1000 ? 1 : 2;
        float cursor = safe.Left + 24;
        float y = safe.Top + 20;
        cursor = DrawText("WAVE ", cursor, y, Color.White, scale);
        cursor = DrawNumber(Math.Min(simulation.CurrentWaveIndex + 1, simulation.TotalWaves), cursor, y, Color.White, scale);
        cursor = DrawText("/", cursor, y, Color.White, scale);
        cursor = DrawNumber(simulation.TotalWaves, cursor, y, Color.White, scale);
        cursor = DrawText("  ENEMIES ", cursor, y, Color.White, scale);
        DrawNumber(simulation.RemainingEnemies, cursor, y, Color.White, scale);

        float scoreWidth = PixelFont.Measure("SCORE ", 2).X + PixelFont.MeasureNumber(simulation.Score, 2).X;
        float scoreX = safe.Right - scoreWidth - 24;
        scoreX = DrawText("SCORE ", scoreX, y, Color.White, 2);
        DrawNumber(simulation.Score, scoreX, y, Color.White, 2);

        string difficulty = simulation.Difficulty.ToString().ToUpperInvariant();
        _font.Draw(_spriteBatch, difficulty, new Vector2(safe.Left + 24, safe.Top + 48), Color.LightCyan, 1);
        if (simulation.GodModeEnabled)
        {
            const string godMode = "GOD MODE";
            Vector2 badgeSize = PixelFont.Measure(godMode, 1);
            int badgeX = safe.Left + 24 + (int)PixelFont.Measure(difficulty, 1).X + 14;
            DrawOutlined(new Rectangle(badgeX - 6, safe.Top + 43, (int)badgeSize.X + 12, 18),
                new Color(80, 44, 6, 215), new Color(255, 194, 65));
            _font.Draw(_spriteBatch, godMode, new Vector2(badgeX, safe.Top + 48), new Color(255, 226, 140), 1);
        }

        if (simulation.InterWaveRemainingSeconds > 0f && simulation.RemainingEnemies == 0 && simulation.Phase == GamePhase.Playing)
        {
            int seconds = (int)MathF.Ceiling(simulation.InterWaveRemainingSeconds);
            float incomingWidth = PixelFont.Measure("WAVE IN ", 3).X + PixelFont.MeasureNumber(seconds, 3).X;
            float incomingX = safe.Center.X - (incomingWidth / 2f);
            incomingX = DrawText("WAVE IN ", incomingX, safe.Center.Y - 100, Color.LightCyan, 3);
            DrawNumber(seconds, incomingX, safe.Center.Y - 100, Color.LightCyan, 3);
        }
    }

    private void DrawBossHealth(GameSimulation simulation, Rectangle safe)
    {
        EnemyState? boss = simulation.ActiveBoss;
        if (boss is null)
        {
            return;
        }

        const int width = 520;
        int left = safe.Center.X - (width / 2);
        int top = safe.Top + 78;
        DrawRect(new Rectangle(left, top, width, 18), new Color(8, 10, 20, 230));
        int fill = (int)((width - 4) * (boss.Health / boss.Definition.MaxHealth));
        DrawRect(new Rectangle(left + 2, top + 2, Math.Max(0, fill), 14), new Color(230, 50, 145));
        string phase = boss.Definition.BossPhases[boss.CurrentBossPhaseIndex].DisplayName;
        Vector2 titleSize = PixelFont.Measure(phase, 2);
        _font.Draw(_spriteBatch, phase, new Vector2(safe.Center.X - (titleSize.X / 2f), top - 22), Color.White, 2);
    }

    private void DrawReticle(GameSimulation simulation, GameSettings settings, Rectangle safe)
    {
        Color reticle = GetReticleColor(settings);
        if (HudMath.GetReticleMode(simulation.Player.IsAiming) == ReticleMode.AimDot)
        {
            int size = settings.LargeHudText ? 5 : 3;
            DrawRect(new Rectangle(safe.Center.X - (size / 2), safe.Center.Y - (size / 2), size, size), reticle);
            return;
        }

        int expansion = 7 + (int)(MathF.Max(0f, 0.15f - simulation.LastShotSeconds) * 65f);
        int thickness = settings.LargeHudText ? 3 : 2;
        DrawRect(new Rectangle(safe.Center.X - 1, safe.Center.Y - expansion - 8, thickness, 8), reticle);
        DrawRect(new Rectangle(safe.Center.X - 1, safe.Center.Y + expansion, thickness, 8), reticle);
        DrawRect(new Rectangle(safe.Center.X - expansion - 8, safe.Center.Y - 1, 8, thickness), reticle);
        DrawRect(new Rectangle(safe.Center.X + expansion, safe.Center.Y - 1, 8, thickness), reticle);

        if (simulation.LastHitSeconds < 0.16f)
        {
            int offset = 13;
            Color hitColor = simulation.LastKillSeconds < 0.2f ? new Color(255, 75, 125) : Color.White;
            DrawRect(new Rectangle(safe.Center.X - offset, safe.Center.Y - offset, 6, 2), hitColor);
            DrawRect(new Rectangle(safe.Center.X + offset - 6, safe.Center.Y - offset, 6, 2), hitColor);
            DrawRect(new Rectangle(safe.Center.X - offset, safe.Center.Y + offset, 6, 2), hitColor);
            DrawRect(new Rectangle(safe.Center.X + offset - 6, safe.Center.Y + offset, 6, 2), hitColor);
        }
    }

    private void DrawDamageFeedback(GameSimulation simulation, GameSettings settings, Rectangle safe)
    {
        if (simulation.PlayerDamageFlashSeconds <= 0f)
        {
            return;
        }

        float intensity = Math.Clamp(simulation.PlayerDamageFlashSeconds / 0.32f, 0f, 1f);
        int alpha = (int)((settings.ReducedFlash ? 36f : 105f) * intensity);
        Color color = settings.ColorVisionMode == ColorVisionMode.Protanopia
            ? new Color(255, 210, 40, alpha)
            : new Color(255, 35, 70, alpha);
        const int edge = 28;
        DrawRect(new Rectangle(safe.Left, safe.Top, safe.Width, edge), color);
        DrawRect(new Rectangle(safe.Left, safe.Bottom - edge, safe.Width, edge), color);
        DrawRect(new Rectangle(safe.Left, safe.Top, edge, safe.Height), color);
        DrawRect(new Rectangle(safe.Right - edge, safe.Top, edge, safe.Height), color);
    }

    private void DrawCaption(string caption, Rectangle safe, int scale)
    {
        Vector2 size = PixelFont.Measure(caption, scale);
        int x = safe.Center.X - (int)(size.X / 2f);
        int y = safe.Bottom - 125;
        DrawRect(new Rectangle(x - 12, y - 8, (int)size.X + 24, (int)size.Y + 16), new Color(4, 8, 16, 205));
        _font.Draw(_spriteBatch, caption, new Vector2(x, y), Color.White, scale);
    }

    private static Color GetReticleColor(GameSettings settings) => settings.ColorVisionMode switch
    {
        ColorVisionMode.Deuteranopia => settings.HighContrastReticle ? Color.White : new Color(255, 205, 55),
        ColorVisionMode.Protanopia => settings.HighContrastReticle ? Color.White : new Color(70, 205, 255),
        ColorVisionMode.Tritanopia => settings.HighContrastReticle ? Color.White : new Color(255, 95, 125),
        _ => settings.HighContrastReticle ? Color.White : new Color(205, 245, 255, 230),
    };

    private void DrawTouchControls(Rectangle safe)
    {
        if (!TouchPanel.GetCapabilities().IsConnected)
        {
            return;
        }

        Color fill = new(120, 200, 230, 55);
        Color edge = new(160, 230, 250, 130);
        DrawOutlined(new Rectangle(safe.Left + 35, safe.Bottom - 190, 145, 145), fill, edge);
        DrawOutlined(new Rectangle(safe.Right - 165, safe.Bottom - 165, 125, 125), fill, edge);
        DrawOutlined(new Rectangle(safe.Right - 145, safe.Top + 125, 100, 68), fill, edge);
        DrawOutlined(new Rectangle(safe.Right - 300, safe.Top + 140, 95, 60), fill, edge);
        DrawOutlined(new Rectangle(safe.Right - 330, safe.Bottom - 130, 90, 70), fill, edge);
        DrawOutlined(new Rectangle(safe.Left + (safe.Width / 2), safe.Bottom - 112, 74, 56), fill, edge);
        DrawOutlined(new Rectangle(safe.Left + (safe.Width / 2) + 82, safe.Bottom - 112, 74, 56), fill, edge);
        Rectangle pause = MenuLayout.GetPauseButtonBounds(safe);
        DrawOutlined(pause, fill, edge);
        _font.Draw(_spriteBatch, "FIRE", new Vector2(safe.Right - 140, safe.Bottom - 104), Color.White, 2);
        _font.Draw(_spriteBatch, "ADS", new Vector2(safe.Right - 118, safe.Top + 150), Color.White, 2);
        _font.Draw(_spriteBatch, "R", new Vector2(safe.Right - 263, safe.Top + 160), Color.White, 2);
        _font.Draw(_spriteBatch, "JUMP", new Vector2(safe.Right - 320, safe.Bottom - 96), Color.White, 1);
        _font.Draw(_spriteBatch, "PREV", new Vector2(safe.Left + (safe.Width / 2) + 8, safe.Bottom - 91), Color.White, 1);
        _font.Draw(_spriteBatch, "NEXT", new Vector2(safe.Left + (safe.Width / 2) + 90, safe.Bottom - 91), Color.White, 1);
        _font.Draw(_spriteBatch, "PAUSE", new Vector2(pause.X + 19, pause.Y + 17), Color.White, 1);
    }

    private void DrawPhaseOverlay(
        GameSimulation simulation,
        GameSettings settings,
        SettingsMenuController menu,
        Rectangle safe)
    {
        DrawRect(new Rectangle(safe.Left, safe.Top, safe.Width, safe.Height), new Color(4, 8, 16, 190));
        if (menu.Page is MenuPage.Main or MenuPage.Results)
        {
            _spriteBatch.Draw(_menuEmblem,
                new Rectangle(safe.Center.X - 28, safe.Center.Y - 304, 56, 56),
                menu.Page == MenuPage.Results && simulation.Phase == GamePhase.Defeat
                    ? new Color(255, 105, 125)
                    : Color.White);
        }

        string title = menu.Page switch
        {
            MenuPage.Main => "FPS FRENZY",
            MenuPage.Settings => "SETTINGS",
            MenuPage.Accessibility => "ACCESSIBILITY",
            MenuPage.Pause => "PAUSED",
            MenuPage.Results => simulation.Phase == GamePhase.Victory ? "ARENA CLEARED" : "RUN OVER",
            _ => simulation.Phase switch
            {
                GamePhase.Paused => "PAUSED",
                GamePhase.Victory => "ARENA CLEARED",
                GamePhase.Defeat => "RUN OVER",
                _ => string.Empty,
            },
        };
        int titleScale = settings.LargeHudText ? 6 : 5;
        Vector2 titleSize = PixelFont.Measure(title, titleScale);
        _font.Draw(_spriteBatch, title,
            new Vector2(safe.Center.X - (titleSize.X / 2f), safe.Center.Y - 230), Color.White, titleScale);
        if (menu.IsOpen)
        {
            if (menu.Page == MenuPage.Main)
            {
                DrawCentered("ORBITAL DEPOT // STANDARD", safe.Center.X, safe.Center.Y - 160, Color.LightCyan, 2);
                DrawCentered("SURVIVE TEN WAVES AND BREAK THE OVERSEER", safe.Center.X, safe.Center.Y - 126,
                    new Color(190, 220, 232), 1);
            }
            else if (menu.Page == MenuPage.Results)
            {
                float resultWidth = PixelFont.Measure("SCORE ", 2).X + PixelFont.MeasureNumber(simulation.Score, 2).X +
                    PixelFont.Measure("  KILLS ", 2).X + PixelFont.MeasureNumber(simulation.Kills, 2).X;
                float resultX = safe.Center.X - (resultWidth / 2f);
                resultX = DrawText("SCORE ", resultX, safe.Center.Y - 130, Color.LightCyan, 2);
                resultX = DrawNumber(simulation.Score, resultX, safe.Center.Y - 130, Color.LightCyan, 2);
                resultX = DrawText("  KILLS ", resultX, safe.Center.Y - 130, Color.LightCyan, 2);
                DrawNumber(simulation.Kills, resultX, safe.Center.Y - 130, Color.LightCyan, 2);
                DrawCentered("STANDARD RUN COMPLETE", safe.Center.X, safe.Center.Y - 92,
                    simulation.Phase == GamePhase.Victory ? new Color(90, 245, 185) : new Color(255, 194, 65), 1);
            }

            IReadOnlyList<string> rows = menu.GetRows();
            int rowScale = settings.LargeHudText ? 3 : 2;
            MenuLayoutMetrics layout = MenuLayout.Create(safe, rows.Count, settings.LargeHudText, menu.Page);
            int rowHeight = layout.RowHeight;
            int panelWidth = layout.RowWidth - 10;
            int panelLeft = layout.RowLeft + 5;
            int top = layout.RowTop + 7;
            Rectangle panel = layout.Panel;
            DrawRect(new Rectangle(panel.X + 9, panel.Y + 11, panel.Width, panel.Height), new Color(0, 0, 0, 125));
            DrawOutlined(panel, new Color(7, 19, 32, 242), new Color(56, 174, 210, 220));
            DrawRect(new Rectangle(panel.X, panel.Y, 7, panel.Height), new Color(87, 220, 245, 235));
            for (int index = 0; index < rows.Count; index++)
            {
                int y = top + (index * rowHeight);
                bool selected = index == menu.SelectedIndex;
                _spriteBatch.Draw(
                    selected ? _menuButtonSelected : _menuButton,
                    new Rectangle(panelLeft - 5, y - 9, panelWidth + 10, rowHeight + 3),
                    selected ? new Color(42, 155, 188, 245) : new Color(42, 72, 92, 220));
                if (selected)
                {
                    DrawRect(new Rectangle(panelLeft - 5, y - 7, 5, rowHeight - 2), Color.White);
                }

                _font.Draw(_spriteBatch, rows[index], new Vector2(panelLeft + 18, y), Color.White, rowScale);
                string value = GetMenuValue(menu.Page, index, settings);
                if (value.Length > 0)
                {
                    Vector2 valueSize = PixelFont.Measure(value, rowScale);
                    _font.Draw(_spriteBatch, value,
                        new Vector2(panelLeft + panelWidth - valueSize.X - 18, y), Color.LightCyan, rowScale);
                }
            }

            const string help = "CURSOR FREE  ARROWS ADJUST  ENTER SELECT  ESC BACK";
            Vector2 helpSize = PixelFont.Measure(help, 1);
            _font.Draw(_spriteBatch, help,
                new Vector2(safe.Center.X - (helpSize.X / 2f), safe.Bottom - 52), new Color(180, 220, 235), 1);
            return;
        }

        if (simulation.Phase is GamePhase.Victory or GamePhase.Defeat)
        {
            float resultWidth = PixelFont.Measure("SCORE ", 2).X + PixelFont.MeasureNumber(simulation.Score, 2).X +
                PixelFont.Measure("  KILLS ", 2).X + PixelFont.MeasureNumber(simulation.Kills, 2).X;
            float resultX = safe.Center.X - (resultWidth / 2f);
            resultX = DrawText("SCORE ", resultX, safe.Center.Y + 5, Color.LightCyan, 2);
            resultX = DrawNumber(simulation.Score, resultX, safe.Center.Y + 5, Color.LightCyan, 2);
            resultX = DrawText("  KILLS ", resultX, safe.Center.Y + 5, Color.LightCyan, 2);
            DrawNumber(simulation.Kills, resultX, safe.Center.Y + 5, Color.LightCyan, 2);
            const string restart = "PRESS ENTER TO RUN AGAIN";
            Vector2 restartSize = PixelFont.Measure(restart, 2);
            _font.Draw(_spriteBatch, restart, new Vector2(safe.Center.X - (restartSize.X / 2f), safe.Center.Y + 55), new Color(255, 210, 70), 2);
        }
    }

    private static string GetMenuValue(MenuPage page, int index, GameSettings settings)
    {
        if (page == MenuPage.Settings)
        {
            return index switch
            {
                0 => Percent(settings.MasterVolume),
                1 => Percent(settings.SoundEffectsVolume),
                2 => Percent(settings.MouseSensitivity),
                3 => Percent(settings.GamepadSensitivity),
                4 => Percent(settings.FieldOfViewScale),
                5 => settings.RenderFrameRate.ToString(CultureInfo.InvariantCulture),
                6 => OnOff(settings.GodMode),
                _ => string.Empty,
            };
        }

        if (page == MenuPage.Accessibility)
        {
            return index switch
            {
                0 => OnOff(settings.ReducedFlash),
                1 => Percent(settings.ScreenShakeScale),
                2 => Percent(settings.CameraBobScale),
                3 => OnOff(settings.HighContrastReticle),
                4 => OnOff(settings.LargeHudText),
                5 => OnOff(settings.Subtitles),
                6 => OnOff(settings.ToggleAimDownSights),
                7 => settings.ColorVisionMode.ToString().ToUpperInvariant(),
                _ => string.Empty,
            };
        }

        return string.Empty;
    }

    private static string Percent(float value) =>
        ((int)MathF.Round(value * 100f)).ToString(CultureInfo.InvariantCulture);

    private static string OnOff(bool value) => value ? "ON" : "OFF";

    private void DrawCentered(string text, int centerX, int y, Color color, int scale)
    {
        Vector2 size = PixelFont.Measure(text, scale);
        _font.Draw(_spriteBatch, text, new Vector2(centerX - (size.X / 2f), y), color, scale);
    }

    private void DrawOutlined(Rectangle rectangle, Color fill, Color edge)
    {
        DrawRect(rectangle, fill);
        DrawRect(new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, 2), edge);
        DrawRect(new Rectangle(rectangle.X, rectangle.Bottom - 2, rectangle.Width, 2), edge);
        DrawRect(new Rectangle(rectangle.X, rectangle.Y, 2, rectangle.Height), edge);
        DrawRect(new Rectangle(rectangle.Right - 2, rectangle.Y, 2, rectangle.Height), edge);
    }

    private void DrawRect(Rectangle rectangle, Color color) => _spriteBatch.Draw(_pixel, rectangle, color);

    private void DrawAmmo(WeaponState weapon, float right, float y, int scale)
    {
        if (weapon.Definition.AmmoMode == AmmoMode.MagazineReserve)
        {
            float width = PixelFont.MeasureNumber(weapon.Magazine, scale).X + PixelFont.Measure("/", scale).X +
                PixelFont.MeasureNumber(weapon.Reserve, scale).X;
            float cursor = right - width;
            cursor = DrawNumber(weapon.Magazine, cursor, y, Color.White, scale);
            cursor = DrawText("/", cursor, y, Color.White, scale);
            DrawNumber(weapon.Reserve, cursor, y, Color.White, scale);
            return;
        }

        int value = weapon.Definition.AmmoMode == AmmoMode.Heat
            ? (int)(weapon.Heat * 100f)
            : (int)MathF.Ceiling(weapon.Energy);
        ReadOnlySpan<char> label = weapon.Definition.AmmoMode == AmmoMode.Heat ? "HEAT " : "";
        float totalWidth = PixelFont.Measure(label, scale).X + PixelFont.MeasureNumber(value, scale).X;
        float x = right - totalWidth;
        x = DrawText(label, x, y, Color.White, scale);
        DrawNumber(value, x, y, Color.White, scale);
    }

    private float DrawText(ReadOnlySpan<char> text, float x, float y, Color color, int scale)
    {
        _font.Draw(_spriteBatch, text, new Vector2(x, y), color, scale);
        return x + PixelFont.Measure(text, scale).X;
    }

    private float DrawNumber(int value, float x, float y, Color color, int scale)
    {
        _font.DrawNumber(_spriteBatch, value, new Vector2(x, y), color, scale);
        return x + PixelFont.MeasureNumber(value, scale).X;
    }
}
