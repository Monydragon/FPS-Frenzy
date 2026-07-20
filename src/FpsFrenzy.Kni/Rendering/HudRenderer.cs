using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Progression;
using FpsFrenzy.Kni.Settings;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;

namespace FpsFrenzy.Kni.Rendering;

public sealed class HudRenderer : IDisposable
{
    internal const int MaximumResultDetailLines = 5;
    private const int ResultIdsPerLine = 5;
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
    private readonly bool[] _compassEliteMarkers = new bool[64];
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
        bool showGameplayHud,
        DebugOverlayState debug)
    {
        Rectangle safe = _graphicsDevice.Viewport.TitleSafeArea;
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        if (showGameplayHud)
        {
            DrawDamageFeedback(simulation, settings, safe);
            DrawCompass(simulation, safe);
            DrawHealth(simulation, safe);
            DrawWeapon(simulation, safe);
            DrawWeaponQuickbar(simulation, safe);
            DrawWaveAndScore(simulation, safe);
            DrawBossHealth(simulation, safe);
            DrawEquipmentPrompt(simulation, safe);
            DrawReticle(simulation, settings, safe);
            DrawTouchControls(safe);
            if (settings.Subtitles && caption is not null)
            {
                DrawCaption(caption, safe, settings.LargeHudText ? 3 : 2);
            }

            if (debug.Enabled)
            {
                DrawDebugOverlay(simulation, debug, safe);
            }
        }

        if (!showGameplayHud || simulation.Phase != GamePhase.Playing || menu.IsOpen)
        {
            DrawPhaseOverlay(simulation, settings, menu, safe);
        }

        _spriteBatch.End();
    }

    private void DrawDebugOverlay(
        GameSimulation simulation,
        DebugOverlayState debug,
        Rectangle safe)
    {
        const int width = 392;
        int height = debug.LabVisible ? 290 : 160;
        int left = safe.Right - width - 18;
        int top = safe.Top + 18;
        DrawOutlined(
            new Rectangle(left, top, width, height),
            new Color(3, 10, 16, 224),
            new Color(60, 235, 190, 220));

        Color heading = new(90, 255, 205);
        Color value = new(215, 245, 255);
        _font.Draw(_spriteBatch, "DEBUG TEST SANDBOX", new Vector2(left + 12, top + 10), heading, 1);
        string objective = simulation.CurrentEncounter?.ObjectiveType.ToString().ToUpperInvariant() ??
            (simulation.IsBossWave ? "BOSS" : "NONE");
        _font.Draw(
            _spriteBatch,
            $"STAGE {simulation.EncounterNumber}/10  {objective}",
            new Vector2(left + 12, top + 30),
            value,
            1);
        _font.Draw(
            _spriteBatch,
            $"ENEMIES {simulation.RemainingEnemies}  TICK {simulation.Tick}",
            new Vector2(left + 12, top + 48),
            value,
            1);
        _font.Draw(
            _spriteBatch,
            FormattableString.Invariant(
                $"POS {simulation.Player.Position.X:0.0} {simulation.Player.Position.Y:0.0} {simulation.Player.Position.Z:0.0}"),
            new Vector2(left + 12, top + 66),
            value,
            1);
        string flags = $"GOD {(debug.GodModeOverride ? "ON" : "OFF")}  COLLISION {(debug.ShowCollision ? "ON" : "OFF")}";
        _font.Draw(_spriteBatch, flags, new Vector2(left + 12, top + 84), value, 1);
        _font.Draw(_spriteBatch, "F2 RPG GRANT  F3 LOOT SHOWCASE", new Vector2(left + 12, top + 104), heading, 1);
        _font.Draw(_spriteBatch, "F5 RESET  F6 PREV  F7 NEXT", new Vector2(left + 12, top + 122), heading, 1);
        _font.Draw(_spriteBatch, "F8 GOD  F9 COMPLETE  F4 COLLISION", new Vector2(left + 12, top + 140), heading, 1);
        if (debug.LabVisible)
        {
            _font.Draw(_spriteBatch, "F11 WEAPON / ARENA LAB", new Vector2(left + 12, top + 162), heading, 1);
            _font.Draw(_spriteBatch,
                $"{debug.WeaponName.ToUpperInvariant()}  SLOT {simulation.ActiveWeaponSlotIndex + 1}",
                new Vector2(left + 12, top + 180), value, 1);
            _font.Draw(_spriteBatch,
                $"{debug.DifficultyName.ToUpperInvariant()}  THREAT {debug.ThreatTier}  AI {(debug.AiFrozen ? "FROZEN" : "LIVE")}",
                new Vector2(left + 12, top + 198), value, 1);
            _font.Draw(_spriteBatch, "J/K WEAPON  +/- DIFF  PGUP/DN THREAT",
                new Vector2(left + 12, top + 216), heading, 1);
            _font.Draw(_spriteBatch, "I SPAWN  O FREEZE  T SECTOR  F12 RELOAD",
                new Vector2(left + 12, top + 234), heading, 1);
            _font.Draw(_spriteBatch, debug.CalibrationAxes,
                new Vector2(left + 12, top + 252), value, 1);
            _font.Draw(_spriteBatch, debug.CalibrationAnchors,
                new Vector2(left + 12, top + 270), value, 1);
        }
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
        Array.Clear(_compassEliteMarkers);
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
            _compassEliteMarkers[bin] |= enemy.IsElite;
        }

        for (int bin = 0; bin < _compassMarkerCounts.Length; bin++)
        {
            int count = _compassMarkerCounts[bin];
            if (count == 0)
            {
                continue;
            }

            int x = left + (bin * 14);
            Color markerColor = _compassEliteMarkers[bin]
                ? new Color(255, 92, 185)
                : new Color(255, 78, 110);
            int markerSize = _compassEliteMarkers[bin] ? 11 : 7;
            DrawRect(new Rectangle(x - (markerSize / 2), top + 39 - (markerSize / 2), markerSize, markerSize), markerColor);
            if (_compassBehindMarkers[bin])
            {
                int direction = x < centerX ? 1 : -1;
                DrawRect(new Rectangle(x + (direction * 7), top + 36, 5, 2), markerColor);
                DrawRect(new Rectangle(x + (direction * 10), top + 34, 2, 6), markerColor);
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
        if (simulation.RunPhase == RunPhase.LegacyWaves)
        {
            cursor = DrawText("WAVE ", cursor, y, Color.White, scale);
            cursor = DrawNumber(Math.Min(simulation.CurrentWaveIndex + 1, simulation.TotalWaves), cursor, y, Color.White, scale);
            cursor = DrawText("/", cursor, y, Color.White, scale);
            cursor = DrawNumber(simulation.TotalWaves, cursor, y, Color.White, scale);
        }
        else if (simulation.RunPhase == RunPhase.BossActive)
        {
            cursor = DrawText("CENTRAL BREACH", cursor, y, new Color(255, 194, 65), scale);
        }
        else
        {
            cursor = DrawText("SECTOR ", cursor, y, Color.White, scale);
            cursor = DrawNumber(Math.Clamp(simulation.CurrentSectorNumber, 1, 3), cursor, y, Color.White, scale);
            cursor = DrawText("/3  ENCOUNTER ", cursor, y, Color.White, scale);
            cursor = DrawNumber(Math.Clamp(simulation.EncounterNumber, 1, 9), cursor, y, Color.White, scale);
            cursor = DrawText("/9", cursor, y, Color.White, scale);
        }

        cursor = DrawText("  ENEMIES ", cursor, y, Color.White, scale);
        DrawNumber(simulation.RemainingEnemies, cursor, y, Color.White, scale);

        float scoreWidth = PixelFont.Measure("SCORE ", 2).X + PixelFont.MeasureNumber(simulation.Score, 2).X;
        float scoreX = safe.Right - scoreWidth - 24;
        scoreX = DrawText("SCORE ", scoreX, y, Color.White, 2);
        DrawNumber(simulation.Score, scoreX, y, Color.White, 2);

        string difficulty = DifficultyCatalog.Get(simulation.Difficulty).DisplayName;
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

        if (simulation.AwaitingArmoryCollection)
        {
            int objectiveY = safe.Top + 68;
            _font.Draw(_spriteBatch, "RECOVERY HUB  COLLECT ARMORY WEAPON",
                new Vector2(safe.Left + 24, objectiveY), new Color(255, 210, 100), 1);
        }
        else if (simulation.RunPhase is RunPhase.EncounterActive or RunPhase.BossActive)
        {
            string objective = simulation.RunPhase == RunPhase.BossActive
                ? "DESTROY BREACH WALKER"
                : simulation.CurrentEncounter?.ObjectiveType switch
                {
                    EncounterObjectiveType.Purge =>
                        $"PURGE WAVE {simulation.CurrentPressureWaveNumber}/{simulation.CurrentPressureWaveTotal}",
                    EncounterObjectiveType.RelayDefense =>
                        $"DEFEND RELAY  WAVE {simulation.CurrentPressureWaveNumber}",
                    EncounterObjectiveType.EliteHunt when simulation.CurrentPressureWaveNumber <
                        simulation.CurrentPressureWaveTotal =>
                        $"TRACK ELITE  WAVE {simulation.CurrentPressureWaveNumber}/{simulation.CurrentPressureWaveTotal}",
                    EncounterObjectiveType.EliteHunt => "ELIMINATE MARKED ELITE",
                    _ => "SECURE SECTOR",
                };
            int objectiveY = safe.Top + 68;
            _font.Draw(_spriteBatch, objective, new Vector2(safe.Left + 24, objectiveY), Color.LightCyan, 1);
            int progressWidth = 210;
            int progressTop = objectiveY + 17;
            DrawRect(new Rectangle(safe.Left + 24, progressTop, progressWidth, 7), new Color(5, 12, 20, 220));
            DrawRect(new Rectangle(safe.Left + 25, progressTop + 1,
                (int)((progressWidth - 2) * Math.Clamp(simulation.ObjectiveProgress, 0f, 1f)), 5),
                new Color(87, 220, 245));
            if (simulation.RelayObjective is not null)
            {
                int relayPercent = (int)MathF.Round(
                    Math.Clamp(simulation.RelayObjective.Health / simulation.RelayObjective.MaximumHealth, 0f, 1f) * 100f);
                _font.Draw(_spriteBatch, $"RELAY {relayPercent}",
                    new Vector2(safe.Left + 246, objectiveY), new Color(255, 210, 100), 1);
            }
        }

        if (simulation.InterWaveRemainingSeconds > 0f && simulation.RemainingEnemies == 0 && simulation.Phase == GamePhase.Playing)
        {
            int seconds = (int)MathF.Ceiling(simulation.InterWaveRemainingSeconds);
            float incomingWidth = PixelFont.Measure("WAVE IN ", 3).X + PixelFont.MeasureNumber(seconds, 3).X;
            float incomingX = safe.Center.X - (incomingWidth / 2f);
            incomingX = DrawText("WAVE IN ", incomingX, safe.Center.Y - 100, Color.LightCyan, 3);
            DrawNumber(seconds, incomingX, safe.Center.Y - 100, Color.LightCyan, 3);
        }
        else if (simulation.PressureWaveBreakRemainingSeconds > 0f && simulation.RemainingEnemies == 0 &&
                 simulation.Phase == GamePhase.Playing)
        {
            int seconds = Math.Max(1, (int)MathF.Ceiling(simulation.PressureWaveBreakRemainingSeconds));
            float incomingWidth = PixelFont.Measure("REINFORCEMENTS IN ", 2).X +
                PixelFont.MeasureNumber(seconds, 2).X;
            float incomingX = safe.Center.X - (incomingWidth / 2f);
            incomingX = DrawText("REINFORCEMENTS IN ", incomingX, safe.Center.Y - 100, Color.LightCyan, 2);
            DrawNumber(seconds, incomingX, safe.Center.Y - 100, Color.LightCyan, 2);
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
        WeaponFamily family = simulation.Player.EffectiveRightHandWeapon.Definition.Family;
        if (simulation.Player.IsAiming && family == WeaponFamily.Precision)
        {
            DrawPrecisionScope(simulation, safe, reticle);
            return;
        }
        if (simulation.Player.IsAiming)
        {
            DrawWeaponFocus(simulation, safe, reticle);
        }
        if (HudMath.GetReticleMode(simulation.Player.IsAiming) == ReticleMode.AimDot)
        {
            int size = settings.LargeHudText ? 5 : 3;
            DrawRect(new Rectangle(safe.Center.X - (size / 2), safe.Center.Y - (size / 2), size, size), reticle);
            return;
        }

        int familyExpansion = family switch
        {
            WeaponFamily.Scatter => 8,
            WeaponFamily.SMG => 2,
            WeaponFamily.Heavy => 5,
            WeaponFamily.Experimental => 4,
            _ => 0,
        };
        int expansion = 7 + familyExpansion +
            (int)(MathF.Max(0f, 0.15f - simulation.LastShotSeconds) * 65f);
        int thickness = settings.LargeHudText || family == WeaponFamily.Heavy ? 3 : 2;
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

    private void DrawWeaponQuickbar(GameSimulation simulation, Rectangle safe)
    {
        int totalWidth = Math.Min(920, safe.Width - 32);
        int gap = 3;
        int cellWidth = Math.Max(48, (totalWidth - (gap * 9)) / WeaponQuickbarLoadout.SlotCount);
        totalWidth = (cellWidth * WeaponQuickbarLoadout.SlotCount) + (gap * 9);
        int left = safe.Center.X - (totalWidth / 2);
        int top = safe.Bottom - 136;

        for (int slotIndex = 0; slotIndex < WeaponQuickbarLoadout.SlotCount; slotIndex++)
        {
            RuntimeWeaponSet set = simulation.GetWeaponSlotState(slotIndex);
            bool active = slotIndex == simulation.ActiveWeaponSlotIndex;
            int x = left + (slotIndex * (cellWidth + gap));
            Rectangle cell = new(x, top, cellWidth, 43);
            Color edge = active ? new Color(95, 238, 255) : new Color(62, 91, 112, 205);
            DrawOutlined(cell, active ? new Color(10, 38, 51, 225) : new Color(5, 13, 22, 195), edge);

            string number = slotIndex == 9 ? "0" : (slotIndex + 1).ToString(CultureInfo.InvariantCulture);
            _font.Draw(_spriteBatch, number, new Vector2(x + 5, top + 5), active ? Color.White : Color.LightGray, 1);
            WeaponState? right = set.RightHand;
            WeaponState? leftWeapon = ReferenceEquals(set.LeftHand, right) ? null : set.LeftHand;
            if (right is null)
            {
                _font.Draw(_spriteBatch, "EMPTY", new Vector2(x + 5, top + 21), new Color(95, 112, 124), 1);
                continue;
            }

            DrawRect(new Rectangle(x + cellWidth - 9, top + 5, 4, 8), FamilyColor(right.Definition.Family));
            string shortName = CompactWeaponName(right.Definition.DisplayName, Math.Max(3, (cellWidth - 10) / 6));
            _font.Draw(_spriteBatch, shortName, new Vector2(x + 5, top + 18), active ? Color.LightCyan : Color.LightGray, 1);
            string ammo = CompactAmmo(right);
            if (leftWeapon is not null)
            {
                ammo = $"{ammo}+{CompactAmmo(leftWeapon)}";
            }
            _font.Draw(_spriteBatch, ammo, new Vector2(x + 5, top + 31), active ? Color.White : new Color(145, 164, 176), 1);
        }
    }

    private static string CompactWeaponName(string displayName, int maximumCharacters)
    {
        string compact = displayName.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return compact.Length <= maximumCharacters ? compact : compact[..maximumCharacters];
    }

    private static string CompactAmmo(WeaponState weapon) => weapon.Definition.AmmoMode switch
    {
        AmmoMode.MagazineReserve => $"{weapon.Magazine}/{weapon.Reserve}",
        AmmoMode.Heat => $"H{(int)MathF.Round(weapon.Heat * 100f)}",
        _ => $"E{(int)MathF.Ceiling(weapon.Energy)}",
    };

    private static Color FamilyColor(WeaponFamily family) => family switch
    {
        WeaponFamily.Pulse => new Color(80, 220, 255),
        WeaponFamily.SMG => new Color(125, 245, 175),
        WeaponFamily.Burst => new Color(255, 212, 92),
        WeaponFamily.Scatter => new Color(255, 142, 70),
        WeaponFamily.Precision => new Color(150, 190, 255),
        WeaponFamily.Beam => new Color(255, 95, 215),
        WeaponFamily.Plasma => new Color(185, 95, 255),
        WeaponFamily.Arc => new Color(95, 160, 255),
        WeaponFamily.Heavy => new Color(255, 92, 92),
        WeaponFamily.Experimental => new Color(115, 255, 225),
        _ => Color.White,
    };

    private void DrawPrecisionScope(GameSimulation simulation, Rectangle safe, Color reticle)
    {
        int radius = Math.Min(safe.Width, safe.Height) / 3;
        int centerX = safe.Center.X;
        int centerY = safe.Center.Y;
        DrawRect(new Rectangle(safe.Left, safe.Top, safe.Width, centerY - radius - safe.Top),
            new Color(0, 0, 0, 205));
        DrawRect(new Rectangle(safe.Left, centerY + radius, safe.Width, safe.Bottom - centerY - radius),
            new Color(0, 0, 0, 205));
        DrawRect(new Rectangle(safe.Left, centerY - radius, centerX - radius - safe.Left, radius * 2),
            new Color(0, 0, 0, 205));
        DrawRect(new Rectangle(centerX + radius, centerY - radius, safe.Right - centerX - radius, radius * 2),
            new Color(0, 0, 0, 205));
        Color scopeLine = reticle * 0.68f;
        DrawRect(new Rectangle(centerX - radius, centerY - 1, radius * 2, 2), scopeLine);
        DrawRect(new Rectangle(centerX - 1, centerY - radius, 2, radius * 2), scopeLine);
        DrawRect(new Rectangle(centerX - 3, centerY - 3, 7, 7), reticle);
        _font.Draw(_spriteBatch,
            $"4X  {simulation.Player.EffectiveRightHandWeapon.Definition.Range:0}M  WEAK POINT",
            new Vector2(centerX - radius + 12, centerY + radius - 24), reticle, 1);
    }

    private void DrawWeaponFocus(GameSimulation simulation, Rectangle safe, Color reticle)
    {
        WeaponDefinition weapon = simulation.Player.EffectiveRightHandWeapon.Definition;
        float magnification = weapon.HipFieldOfViewDegrees / weapon.AdsFieldOfViewDegrees;
        int halfWidth = Math.Min(230, safe.Width / 4);
        int halfHeight = Math.Min(150, safe.Height / 4);
        int left = safe.Center.X - halfWidth;
        int right = safe.Center.X + halfWidth;
        int top = safe.Center.Y - halfHeight;
        int bottom = safe.Center.Y + halfHeight;
        Color frame = reticle * 0.48f;
        const int corner = 28;
        const int thickness = 2;
        DrawRect(new Rectangle(left, top, corner, thickness), frame);
        DrawRect(new Rectangle(left, top, thickness, corner), frame);
        DrawRect(new Rectangle(right - corner, top, corner, thickness), frame);
        DrawRect(new Rectangle(right - thickness, top, thickness, corner), frame);
        DrawRect(new Rectangle(left, bottom - thickness, corner, thickness), frame);
        DrawRect(new Rectangle(left, bottom - corner, thickness, corner), frame);
        DrawRect(new Rectangle(right - corner, bottom - thickness, corner, thickness), frame);
        DrawRect(new Rectangle(right - thickness, bottom - corner, thickness, corner), frame);
        _font.Draw(_spriteBatch,
            $"{magnification:0.0}X  {weapon.Family.ToString().ToUpperInvariant()} FOCUS",
            new Vector2(left + 8, bottom - 19), frame, 1);
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

    private void DrawEquipmentPrompt(GameSimulation simulation, Rectangle safe)
    {
        PickupState? pickup = simulation.Pickups
            .Where(candidate => candidate.IsAvailable && candidate.Type == PickupType.Equipment &&
                candidate.Equipment is not null)
            .OrderBy(candidate => System.Numerics.Vector3.DistanceSquared(
                simulation.Player.Position, candidate.Position))
            .FirstOrDefault();
        if (pickup?.Equipment is not EquipmentInstance item ||
            System.Numerics.Vector3.DistanceSquared(simulation.Player.Position, pickup.Position) > 16f)
        {
            return;
        }

        string title = $"{item.Rarity.ToString().ToUpperInvariant()}  {item.DisplayName.ToUpperInvariant()}";
        string details = $"{item.PrimarySlot.ToString().ToUpperInvariant()}  ITEM POWER {item.ItemPower}  [E] TAKE";
        int width = Math.Max((int)PixelFont.Measure(title, 2).X, (int)PixelFont.Measure(details, 1).X) + 34;
        int left = safe.Center.X - (width / 2);
        int top = safe.Bottom - 190;
        DrawOutlined(new Rectangle(left, top, width, 64), new Color(4, 12, 22, 225),
            RarityColor(item.Rarity));
        _font.Draw(_spriteBatch, title, new Vector2(left + 17, top + 10), RarityColor(item.Rarity), 2);
        _font.Draw(_spriteBatch, details, new Vector2(left + 17, top + 39), Color.White, 1);
    }

    private static Color RarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => new Color(205, 215, 225),
        ItemRarity.Uncommon => new Color(90, 235, 145),
        ItemRarity.Rare => new Color(70, 160, 255),
        ItemRarity.Epic => new Color(210, 85, 255),
        ItemRarity.Legendary => new Color(255, 190, 55),
        _ => Color.White,
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
        _font.Draw(_spriteBatch, "R FIRE", new Vector2(safe.Right - 155, safe.Bottom - 104), Color.White, 1);
        _font.Draw(_spriteBatch, "L FIRE", new Vector2(safe.Right - 285, safe.Bottom - 155), Color.White, 1);
        _font.Draw(_spriteBatch, "ADS", new Vector2(safe.Right - 118, safe.Top + 150), Color.White, 2);
        _font.Draw(_spriteBatch, "R", new Vector2(safe.Right - 263, safe.Top + 160), Color.White, 2);
        _font.Draw(_spriteBatch, "JUMP", new Vector2(safe.Right - 320, safe.Bottom - 96), Color.White, 1);
        _font.Draw(_spriteBatch, "A1", new Vector2(safe.Left + (safe.Width / 2) + 22, safe.Bottom - 91), Color.White, 1);
        _font.Draw(_spriteBatch, "A2", new Vector2(safe.Left + (safe.Width / 2) + 104, safe.Bottom - 91), Color.White, 1);
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
            MenuPage.Loadout => "LOADOUT",
            MenuPage.Armory => "ARMORY ISSUE",
            MenuPage.Character => "CHARACTER",
            MenuPage.Inventory => "INVENTORY",
            MenuPage.InventoryItem => "ITEM INSPECTION",
            MenuPage.Abilities => "ABILITIES",
            MenuPage.Proficiencies => "PROFICIENCIES",
            MenuPage.Crafting => "CRAFTING",
            MenuPage.CraftingItem => "ITEM WORKBENCH",
            MenuPage.Stats => "STAT BREAKDOWN",
            MenuPage.Difficulty => "DIFFICULTY",
            MenuPage.ThreatTier => "THREAT TIER",
            MenuPage.Recovery => "RECOVERY CACHE",
            MenuPage.Records => "RECORDS",
            MenuPage.Tutorial => "BREACH BRIEFING",
            MenuPage.Reward => "CHOOSE AUGMENT",
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
            if (menu.Page is MenuPage.Character or MenuPage.Inventory or MenuPage.InventoryItem or
                MenuPage.Loadout or MenuPage.Armory or MenuPage.Abilities or MenuPage.Proficiencies or
                MenuPage.Crafting or MenuPage.CraftingItem or MenuPage.Stats)
            {
                DrawProfileTabs(menu.Page, safe);
            }
            if (menu.Page == MenuPage.Main)
            {
                DrawCentered("ORBITAL DEPOT // BREACH PROTOCOL", safe.Center.X, safe.Center.Y - 160, Color.LightCyan, 2);
                DrawCentered("CLEAR THREE SECTORS. BUILD YOUR ARSENAL. BREAK THE WALKER.", safe.Center.X, safe.Center.Y - 126,
                    new Color(190, 220, 232), 1);
            }
            else if (menu.Page == MenuPage.Loadout)
            {
                DrawCentered("TEN WEAPON PRESETS  INDEPENDENT HANDS  TWO-HANDED RESERVES BOTH", safe.Center.X, safe.Center.Y - 155,
                    new Color(190, 220, 232), 1);
                DrawCentered("SELECT A SLOT TO FILTER YOUR STASH", safe.Center.X, safe.Center.Y - 130,
                    Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Armory)
            {
                DrawCentered("ALL 50 BASES ARE AVAILABLE  ISSUED GEAR IS COMMON AND RUN-BOUND",
                    safe.Center.X, safe.Center.Y - 155, new Color(190, 220, 232), 1);
                DrawCentered("CHOOSE A WEAPON FOR THIS SET POSITION",
                    safe.Center.X, safe.Center.Y - 130, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Character)
            {
                DrawCentered("LEVEL, TALENTS, AND FREE BETWEEN-RUN RESPEC", safe.Center.X,
                    safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Inventory)
            {
                DrawCentered("FILTER WITH LEFT/RIGHT  ENTER INSPECTS, COMPARES, EQUIPS, LOCKS, OR SALVAGES",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.InventoryItem)
            {
                DrawCentered("EQUIPPED, LOCKED, AND FAVORITED ITEMS ARE PROTECTED FROM BATCH SALVAGE",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Abilities)
            {
                DrawCentered("EQUIPPED ITEMS TEACH AP  MASTERED ABILITIES STAY AVAILABLE",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Proficiencies)
            {
                DrawCentered("DAMAGE AND KILLS ADVANCE EACH WEAPON FAMILY TO RANK 25",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Crafting)
            {
                DrawCentered("GROUP DUPLICATES, INFUSE ITEM POWER, OR DISMANTLE FOR MATERIALS",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.CraftingItem)
            {
                DrawCentered("INFUSION PRESERVES RARITY, AFFIXES, UNIQUE EFFECTS, AND ITEM ID",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Stats)
            {
                DrawCentered("CURRENT VALUES INCLUDE EQUIPMENT, TALENTS, AND MASTERED PASSIVES",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Difficulty)
            {
                DrawCentered("DIFFICULTY CHANGES COMBAT STATS AND TEMPO, NOT LOOT OR PROGRESSION",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.ThreatTier)
            {
                DrawCentered("THREAT SETS ENEMY STRENGTH, REWARD RATE, AND ITEM POWER BAND",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Recovery)
            {
                DrawCentered("MISSED DROPS AND TWO GUARANTEED ITEMS HAVE BEEN RECOVERED",
                    safe.Center.X, safe.Center.Y - 150, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Records)
            {
                ProfileData? profile = menu.Profile;
                int bestScore = profile?.BestUnassistedRun?.Score ?? 0;
                int wins = profile?.RunsWon ?? 0;
                int lifetimeKills = profile?.LifetimeKills ?? 0;
                DrawCentered($"BEST SCORE {bestScore}", safe.Center.X, safe.Center.Y - 145, Color.LightCyan, 2);
                DrawCentered($"WINS {wins}   LIFETIME KILLS {lifetimeKills}", safe.Center.X, safe.Center.Y - 105,
                    new Color(190, 220, 232), 1);
                DrawCentered("GOD MODE RUNS REMAIN ELIGIBLE FOR UNLOCKS", safe.Center.X, safe.Center.Y - 72,
                    new Color(255, 210, 100), 1);
            }
            else if (menu.Page == MenuPage.Tutorial)
            {
                DrawCentered("CLEAR PURGE, RELAY, AND ELITE OBJECTIVES IN THREE SECTORS", safe.Center.X,
                    safe.Center.Y - 160, Color.LightCyan, 1);
                DrawCentered("CHOOSE ONE AUGMENT AFTER EVERY ENCOUNTER", safe.Center.X, safe.Center.Y - 132,
                    new Color(190, 220, 232), 1);
                DrawCentered("WASD MOVE  MOUSE AIM  LMB FIRE  R RELOAD  SPACE JUMP", safe.Center.X,
                    safe.Center.Y - 104, new Color(190, 220, 232), 1);
            }
            else if (menu.Page == MenuPage.Reward)
            {
                DrawCentered("SELECT ONE PERMANENT UPGRADE FOR THIS RUN", safe.Center.X, safe.Center.Y - 150,
                    Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Results)
            {
                RunRecord? record = menu.Profile?.MostRecentRun;
                float resultWidth = PixelFont.Measure("SCORE ", 2).X + PixelFont.MeasureNumber(simulation.Score, 2).X +
                    PixelFont.Measure("  KILLS ", 2).X + PixelFont.MeasureNumber(simulation.Kills, 2).X;
                float resultX = safe.Center.X - (resultWidth / 2f);
                resultX = DrawText("SCORE ", resultX, safe.Center.Y - 146, Color.LightCyan, 2);
                resultX = DrawNumber(simulation.Score, resultX, safe.Center.Y - 146, Color.LightCyan, 2);
                resultX = DrawText("  KILLS ", resultX, safe.Center.Y - 146, Color.LightCyan, 2);
                DrawNumber(simulation.Kills, resultX, safe.Center.Y - 146, Color.LightCyan, 2);
                DrawCentered(
                    simulation.Phase == GamePhase.Victory ? "BREACH WALKER DESTROYED" : "RUN TERMINATED",
                    safe.Center.X,
                    safe.Center.Y - 112,
                    simulation.Phase == GamePhase.Victory ? new Color(90, 245, 185) : new Color(255, 194, 65), 1);
                if (record is not null)
                {
                    int elapsedSeconds = Math.Max(0, (int)MathF.Round(record.ElapsedSeconds));
                    string time = $"{elapsedSeconds / 60:00}:{elapsedSeconds % 60:00}";
                    DrawCentered($"SEED {record.Seed}   TIME {time}   DAMAGE {(int)MathF.Round(record.DamageTaken)}",
                        safe.Center.X, safe.Center.Y - 76, new Color(190, 220, 232), 1);
                    DrawCentered(
                        $"{DifficultyCatalog.Get(record.Difficulty).DisplayName}   " +
                        $"THREAT {record.ThreatTier.ToString()[4..].ToUpperInvariant()}   LEVEL {record.PlayerLevel}  " +
                        $"(+{record.LevelsGained})   XP +{record.ExperienceGained}",
                        safe.Center.X, safe.Center.Y - 56, new Color(190, 220, 232), 1);
                    DrawCentered(
                        $"LOOT {record.EquipmentCollected}   HIGHEST IP {record.HighestItemPower}   " +
                        $"MASTERED {record.AbilitiesMastered.Count}",
                        safe.Center.X, safe.Center.Y - 36, Color.LightCyan, 1);
                    DrawCentered(
                        $"SECTORS {record.SectorsCompleted}/3   BOONS {record.UpgradeIds.Count}/9   " +
                        (record.GodModeUsed ? "GOD MODE USED" : "STANDARD VERIFIED"),
                        safe.Center.X, safe.Center.Y - 16,
                        record.GodModeUsed ? new Color(255, 210, 100) : new Color(150, 235, 205), 1);
                    List<(string Text, Color Color)> resultDetails = BuildResultDetailLines(
                        record.NewlyUnlockedIds,
                        record.UpgradeIds);
                    for (int detailIndex = 0; detailIndex < resultDetails.Count; detailIndex++)
                    {
                        (string detail, Color color) = resultDetails[detailIndex];
                        DrawCentered(detail, safe.Center.X, safe.Center.Y + 6 + (detailIndex * 18), color, 1);
                    }
                }
            }

            IReadOnlyList<string> rows = menu.GetRows();
            bool densePage = menu.Page is MenuPage.Character or MenuPage.Inventory or MenuPage.Loadout or
                MenuPage.Abilities or MenuPage.Proficiencies or MenuPage.Crafting or MenuPage.CraftingItem or
                MenuPage.Stats;
            int rowScale = densePage ? (settings.LargeHudText ? 2 : 1) : (settings.LargeHudText ? 3 : 2);
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
                string value = GetMenuValue(menu, index, settings);
                if (value.Length > 0)
                {
                    if (menu.Page == MenuPage.Reward)
                    {
                        _font.Draw(_spriteBatch, value,
                            new Vector2(panelLeft + 18, y + 28), new Color(190, 220, 232), 1);
                    }
                    else
                    {
                        Vector2 valueSize = PixelFont.Measure(value, rowScale);
                        _font.Draw(_spriteBatch, value,
                            new Vector2(panelLeft + panelWidth - valueSize.X - 18, y), Color.LightCyan, rowScale);
                    }
                }
            }

            string help = menu.Page == MenuPage.Reward
                ? "ARROWS CHOOSE  ENTER INSTALL"
                : menu.Page == MenuPage.Recovery
                    ? "ARROWS INSPECT  ENTER TAKE  TAKE ALL TO CONTINUE"
                : "CURSOR FREE  ARROWS ADJUST  ENTER SELECT  ESC BACK";
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

    internal static List<(string Text, Color Color)> BuildResultDetailLines(
        IReadOnlyList<string> newlyUnlockedIds,
        IReadOnlyList<string> upgradeIds)
    {
        List<(string Text, Color Color)> lines = [];
        if (newlyUnlockedIds.Count == 0)
        {
            lines.Add(("NO NEW PROFILE UNLOCKS", Color.LightCyan));
        }
        else
        {
            AddIdDetailLines(lines, "NEW UNLOCKS", newlyUnlockedIds, Color.LightCyan);
        }

        AddIdDetailLines(lines, "BUILD", upgradeIds, new Color(190, 220, 232));
        if (lines.Count <= MaximumResultDetailLines)
        {
            return lines;
        }

        int omittedLineCount = lines.Count - (MaximumResultDetailLines - 1);
        List<(string Text, Color Color)> bounded = lines.Take(MaximumResultDetailLines - 1).ToList();
        bounded.Add(($"PLUS {omittedLineCount} MORE RESULT LINES", new Color(190, 220, 232)));
        return bounded;
    }

    private static void AddIdDetailLines(
        List<(string Text, Color Color)> lines,
        string label,
        IReadOnlyList<string> ids,
        Color color)
    {
        if (ids.Count == 0)
        {
            lines.Add(($"{label} NONE", color));
            return;
        }

        for (int index = 0; index < ids.Count; index += ResultIdsPerLine)
        {
            string values = string.Join(" / ", ids
                .Skip(index)
                .Take(ResultIdsPerLine)
                .Select(id => id.Replace('-', ' ').ToUpperInvariant()));
            lines.Add(($"{(index == 0 ? label + "  " : "")}{values}", color));
        }
    }

    private static string GetMenuValue(SettingsMenuController menu, int index, GameSettings settings)
    {
        MenuPage page = menu.Page;
        if (page == MenuPage.Settings)
        {
            return index switch
            {
                0 => Percent(settings.MasterVolume),
                1 => Percent(settings.MusicVolume),
                2 => Percent(settings.SoundEffectsVolume),
                3 => Percent(settings.MouseSensitivity),
                4 => Percent(settings.GamepadSensitivity),
                5 => Percent(settings.FieldOfViewScale),
                6 => settings.RenderFrameRate.ToString(CultureInfo.InvariantCulture),
                7 => OnOff(settings.GodMode),
                _ => string.Empty,
            };
        }

        if (page is MenuPage.Loadout or MenuPage.Armory or MenuPage.ThreatTier or MenuPage.InventoryItem)
        {
            return menu.GetSupplementalValue(index);
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

    private void DrawProfileTabs(MenuPage page, Rectangle safe)
    {
        string[] labels = ["CHARACTER", "INVENTORY", "LOADOUT", "ABILITIES", "PROFICIENCY", "CRAFTING", "STATS"];
        int active = page switch
        {
            MenuPage.Character => 0,
            MenuPage.Inventory or MenuPage.InventoryItem => 1,
            MenuPage.Loadout or MenuPage.Armory => 2,
            MenuPage.Abilities => 3,
            MenuPage.Proficiencies => 4,
            MenuPage.Crafting or MenuPage.CraftingItem => 5,
            MenuPage.Stats => 6,
            _ => -1,
        };
        for (int index = 0; index < labels.Length; index++)
        {
            Rectangle bounds = MenuLayout.GetProfileTabBounds(safe, index);
            bool selected = index == active;
            if (selected)
            {
                DrawOutlined(bounds, new Color(10, 47, 62, 225), new Color(84, 226, 248));
            }
            Vector2 size = PixelFont.Measure(labels[index], 1);
            _font.Draw(_spriteBatch, labels[index],
                new Vector2(bounds.Center.X - (size.X / 2f), bounds.Y + 9),
                selected ? Color.White : new Color(115, 210, 238), 1);
        }
    }

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
