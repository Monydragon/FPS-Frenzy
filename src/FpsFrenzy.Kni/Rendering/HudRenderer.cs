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
    private static readonly Color Glass = new(7, 19, 32, 242);
    private static readonly Color FocusCyan = new(87, 220, 245);
    private static readonly Color WarningAmber = new(255, 191, 85);
    private static readonly Color SuccessTeal = new(89, 230, 178);
    private static readonly Color AlertCoral = new(255, 102, 125);
    private static readonly Color SecondaryText = new(154, 181, 199);
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
    private readonly Texture2D _titleMenuBackground;
    private readonly OxaniumFont _font;
    private readonly int[] _compassMarkerCounts = new int[64];
    private readonly int[] _compassVerticalDirections = new int[64];
    private readonly bool[] _compassBehindMarkers = new bool[64];
    private readonly bool[] _compassEliteMarkers = new bool[64];
    private MenuPage _animatedMenuPage;
    private int _animatedMenuIndex = -1;
    private float _selectionFill = 1f;
    private double _selectionStartedSeconds;
    private bool _disposed;

    public HudRenderer(
        GraphicsDevice graphicsDevice,
        Texture2D menuButton,
        Texture2D menuButtonSelected,
        Texture2D menuEmblem,
        Texture2D titleMenuBackground,
        SpriteFont hudFont,
        SpriteFont bodyFont,
        SpriteFont headingFont)
    {
        _graphicsDevice = graphicsDevice;
        _menuButton = menuButton;
        _menuButtonSelected = menuButtonSelected;
        _menuEmblem = menuEmblem;
        _titleMenuBackground = titleMenuBackground;
        _spriteBatch = new SpriteBatch(graphicsDevice);
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _font = new OxaniumFont(hudFont, bodyFont, headingFont);
    }

    public void Draw(
        GameSimulation simulation,
        GameSettings settings,
        SettingsMenuController menu,
        string? caption,
        bool showGameplayHud,
        DebugOverlayState debug,
        double presentationSeconds)
    {
        Rectangle safe = _graphicsDevice.Viewport.TitleSafeArea;
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
        if (showGameplayHud)
        {
            DrawDamageFeedback(simulation, settings, safe);
            DrawCompass(simulation, safe);
            DrawHealth(simulation, safe);
            DrawWeapon(simulation, safe);
            DrawAbilities(simulation, safe);
            DrawWeaponQuickbar(simulation, safe);
            DrawWaveAndScore(simulation, safe);
            DrawAdventureHud(simulation, settings, safe);
            DrawBossHealth(simulation, safe);
            DrawEquipmentPrompt(simulation, settings, safe);
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

            DrawAdventureStageTransition(simulation, settings, safe);
        }

        if (!showGameplayHud || simulation.Phase != GamePhase.Playing || menu.IsOpen)
        {
            DrawPhaseOverlay(simulation, settings, menu, safe, presentationSeconds);
        }

        _spriteBatch.End();
    }

    private void DrawDebugOverlay(
        GameSimulation simulation,
        DebugOverlayState debug,
        Rectangle safe)
    {
        const int width = 392;
        int height = debug.LabVisible ? 344 : 160;
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
                $"WEAPON {debug.WeaponIndex + 1}/{debug.WeaponCount}  {debug.WeaponName.ToUpperInvariant()}",
                new Vector2(left + 12, top + 180), value, 1);
            _font.Draw(_spriteBatch,
                $"LB {debug.Ability1Name.ToUpperInvariant()}  RB {debug.Ability2Name.ToUpperInvariant()}",
                new Vector2(left + 12, top + 198), value, 1);
            _font.Draw(_spriteBatch,
                $"{debug.DifficultyName.ToUpperInvariant()}  THREAT {debug.ThreatTier}  AI {(debug.AiFrozen ? "FROZEN" : "LIVE")}",
                new Vector2(left + 12, top + 216), heading, 1);
            _font.Draw(_spriteBatch, "PAD Y/B WEAPON  LB/RB USE ABILITY",
                new Vector2(left + 12, top + 234), heading, 1);
            _font.Draw(_spriteBatch, "DPAD U/D ABILITY  R SPAWN  L FREEZE",
                new Vector2(left + 12, top + 252), value, 1);
            _font.Draw(_spriteBatch, "J/K WEAPON  I SPAWN  O FREEZE  F12 RELOAD",
                new Vector2(left + 12, top + 270), value, 1);
            _font.Draw(_spriteBatch, debug.Status,
                new Vector2(left + 12, top + 288), heading, 1);
            _font.Draw(_spriteBatch, debug.CalibrationAxes,
                new Vector2(left + 12, top + 306), value, 1);
            _font.Draw(_spriteBatch, debug.CalibrationAnchors,
                new Vector2(left + 12, top + 324), value, 1);
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
            new Vector2(x + OxaniumFont.Measure("HEALTH ", 2).X, y - 22), Color.White, 2);
    }

    private void DrawWeapon(GameSimulation simulation, Rectangle safe)
    {
        WeaponState weapon = simulation.Player.CurrentWeapon;
        int scale = safe.Width < 1000 ? 1 : 2;
        Vector2 nameSize = OxaniumFont.Measure(weapon.Definition.DisplayName, scale);
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
        if (simulation.Mode == GameMode.Adventure)
        {
            cursor = DrawText("ALERT ", cursor, y, FocusCyan, scale);
            DrawNumber(simulation.AdventureSnapshot?.AlertedGroups.Count ?? 0, cursor, y, Color.White, scale);
            float adventureScoreWidth = OxaniumFont.Measure("SCORE ", 2).X +
                OxaniumFont.MeasureNumber(simulation.Score, 2).X;
            float adventureScoreX = safe.Right - adventureScoreWidth - 24;
            adventureScoreX = DrawText("SCORE ", adventureScoreX, y, Color.White, 2);
            DrawNumber(simulation.Score, adventureScoreX, y, Color.White, 2);
            _font.Draw(_spriteBatch, DifficultyCatalog.Get(simulation.Difficulty).DisplayName,
                new Vector2(safe.Left + 24, safe.Top + 48), Color.LightCyan, 1);
            return;
        }

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

        float scoreWidth = OxaniumFont.Measure("SCORE ", 2).X + OxaniumFont.MeasureNumber(simulation.Score, 2).X;
        float scoreX = safe.Right - scoreWidth - 24;
        scoreX = DrawText("SCORE ", scoreX, y, Color.White, 2);
        DrawNumber(simulation.Score, scoreX, y, Color.White, 2);

        string difficulty = DifficultyCatalog.Get(simulation.Difficulty).DisplayName;
        _font.Draw(_spriteBatch, difficulty, new Vector2(safe.Left + 24, safe.Top + 48), Color.LightCyan, 1);
        if (simulation.GodModeEnabled)
        {
            const string godMode = "GOD MODE";
            Vector2 badgeSize = OxaniumFont.Measure(godMode, 1);
            int badgeX = safe.Left + 24 + (int)OxaniumFont.Measure(difficulty, 1).X + 14;
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
            float incomingWidth = OxaniumFont.Measure("WAVE IN ", 3).X + OxaniumFont.MeasureNumber(seconds, 3).X;
            float incomingX = safe.Center.X - (incomingWidth / 2f);
            incomingX = DrawText("WAVE IN ", incomingX, safe.Center.Y - 100, Color.LightCyan, 3);
            DrawNumber(seconds, incomingX, safe.Center.Y - 100, Color.LightCyan, 3);
        }
        else if (simulation.PressureWaveBreakRemainingSeconds > 0f && simulation.RemainingEnemies == 0 &&
                 simulation.Phase == GamePhase.Playing)
        {
            int seconds = Math.Max(1, (int)MathF.Ceiling(simulation.PressureWaveBreakRemainingSeconds));
            float incomingWidth = OxaniumFont.Measure("REINFORCEMENTS IN ", 2).X +
                OxaniumFont.MeasureNumber(seconds, 2).X;
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
        Vector2 titleSize = OxaniumFont.Measure(phase, 2);
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

    private void DrawAbilities(GameSimulation simulation, Rectangle safe)
    {
        List<string> abilityIds = simulation.Progression.AbilityMastery.EquippedActiveAbilityIds;
        if (abilityIds.Count == 0)
        {
            return;
        }

        const int slotWidth = 224;
        const int slotHeight = 29;
        const int gap = 8;
        int left = safe.Center.X - slotWidth - (gap / 2);
        int top = safe.Bottom - (simulation.Mode == GameMode.Adventure &&
            simulation.AdventureStoryBeat is not null ? 360 : 228);
        for (int index = 0; index < 2; index++)
        {
            string controllerButton = index == 0 ? "LB" : "RB";
            string? abilityId = index < abilityIds.Count ? abilityIds[index] : null;
            string abilityName = abilityId is null
                ? "EMPTY"
                : abilityId.Replace('-', ' ').ToUpperInvariant();
            float cooldown = abilityId is null ? 0f : simulation.AbilityCooldowns.GetValueOrDefault(abilityId);
            bool ready = abilityId is not null && cooldown <= 0f;
            int x = left + (index * (slotWidth + gap));
            Color edge = ready ? new Color(90, 235, 205) : new Color(80, 105, 125);
            DrawOutlined(new Rectangle(x, top, slotWidth, slotHeight),
                new Color(5, 18, 28, 215), edge);
            _font.Draw(_spriteBatch, $"{controllerButton}  {abilityName}",
                new Vector2(x + 9, top + 9), ready ? Color.LightCyan : new Color(145, 164, 176), 1);
            if (cooldown > 0f)
            {
                string seconds = $"{MathF.Ceiling(cooldown):0}";
                Vector2 secondsSize = OxaniumFont.Measure(seconds, 1);
                _font.Draw(_spriteBatch, seconds,
                    new Vector2(x + slotWidth - secondsSize.X - 9, top + 9),
                    new Color(255, 205, 75), 1);
            }
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
        _font.Draw(_spriteBatch, "B  PREVIOUS", new Vector2(left, top - 16), new Color(125, 210, 235), 1);
        string nextHint = "Y  NEXT";
        _font.Draw(_spriteBatch, nextHint,
            new Vector2(left + totalWidth - OxaniumFont.Measure(nextHint, 1).X, top - 16),
            new Color(125, 210, 235), 1);

        for (int slotIndex = 0; slotIndex < WeaponQuickbarLoadout.SlotCount; slotIndex++)
        {
            RuntimeWeaponSet set = simulation.GetWeaponSlotState(slotIndex);
            EquipmentInstance? slotItem = simulation.GetWeaponSlotEquipment(slotIndex);
            bool active = slotIndex == simulation.ActiveWeaponSlotIndex;
            int x = left + (slotIndex * (cellWidth + gap));
            Rectangle cell = new(x, top, cellWidth, 43);
            Color rarityEdge = slotItem is null ? new Color(62, 91, 112, 205) : RarityColor(slotItem.Rarity);
            Color edge = active ? new Color(95, 238, 255) : rarityEdge;
            DrawOutlined(cell, active ? new Color(10, 38, 51, 225) : new Color(5, 13, 22, 195), edge);

            string number = slotIndex == 9 ? "0" : (slotIndex + 1).ToString(CultureInfo.InvariantCulture);
            string family = CompactFamilyName(WeaponQuickbarLoadout.FamilyForSlot(slotIndex));
            _font.Draw(_spriteBatch, $"{number} {family}", new Vector2(x + 5, top + 5),
                active ? Color.White : rarityEdge, 1);
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

    private static string CompactFamilyName(WeaponFamily family) => family switch
    {
        WeaponFamily.Precision => "PREC",
        WeaponFamily.Experimental => "EXPR",
        WeaponFamily.Scatter => "SCAT",
        WeaponFamily.Plasma => "PLAS",
        WeaponFamily.Heavy => "HVY",
        _ => family.ToString().ToUpperInvariant()[..Math.Min(4, family.ToString().Length)],
    };

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
        Vector2 size = OxaniumFont.Measure(caption, scale);
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

    private void DrawEquipmentPrompt(GameSimulation simulation, GameSettings settings, Rectangle safe)
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
        string details = item.IsWeapon
            ? $"{item.PrimarySlot.ToString().ToUpperInvariant()}  ITEM POWER {item.ItemPower}  WALK OVER TO COLLECT OR COMPARE"
            : $"{item.PrimarySlot.ToString().ToUpperInvariant()}  ITEM POWER {item.ItemPower}  " +
                $"[E / {GamepadBindingCatalog.ButtonLabel(settings.ControllerBindings.Interact)}] TAKE";
        int width = Math.Max((int)OxaniumFont.Measure(title, 2).X, (int)OxaniumFont.Measure(details, 1).X) + 34;
        int left = safe.Center.X - (width / 2);
        int top = simulation.Mode == GameMode.Adventure
            ? safe.Bottom - (simulation.AdventureStoryBeat is null ? 300 : 430)
            : safe.Bottom - 190;
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
        Rectangle safe,
        double presentationSeconds)
    {
        if (menu.Page == MenuPage.Main)
        {
            DrawTitleMenuBackground(safe);
            DrawRect(new Rectangle(safe.Left, safe.Top, safe.Width, safe.Height), new Color(3, 9, 18, 118));
        }
        else
        {
            DrawRect(new Rectangle(safe.Left, safe.Top, safe.Width, safe.Height), new Color(4, 8, 16, 190));
        }
        if (menu.Page == MenuPage.Pause && simulation.Mode == GameMode.Adventure)
        {
            DrawAdventureMinimap(simulation, safe, expanded: true);
        }
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
            MenuPage.Play => "PLAY",
            MenuPage.Operative => "OPERATIVE",
            MenuPage.Arsenal => "ARSENAL",
            MenuPage.AdventureSetup => "THE NULL SIGNAL",
            MenuPage.SeedKeypad => "SEED INPUT",
            MenuPage.ConfirmNewRun => "REPLACE SAVED CHECKPOINT?",
            MenuPage.AdventureMap => "DUNGEON MAP",
            MenuPage.Transmission => "TRANSMISSION",
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
            MenuPage.WeaponPickup => "WEAPON PICKUP",
            MenuPage.Records => "RECORDS",
            MenuPage.Tutorial => "BREACH BRIEFING",
            MenuPage.Reward => simulation.Mode == GameMode.Adventure ? "FLOOR SECURED" : "CHOOSE AUGMENT",
            MenuPage.Settings => "SETTINGS",
            MenuPage.Controls => "CONTROLLER BINDINGS",
            MenuPage.Accessibility => "ACCESSIBILITY",
            MenuPage.Pause => "PAUSED",
            MenuPage.Results => simulation.Mode == GameMode.Adventure
                ? simulation.Phase == GamePhase.Victory ? "NULL SIGNAL SILENCED" : "ADVENTURE LOST"
                : simulation.Phase == GamePhase.Victory ? "ARENA CLEARED" : "RUN OVER",
            _ => simulation.Phase switch
            {
                GamePhase.Paused => "PAUSED",
                GamePhase.Victory => "ARENA CLEARED",
                GamePhase.Defeat => "RUN OVER",
                _ => string.Empty,
            },
        };
        bool profileSurface = menu.Page is MenuPage.Character or MenuPage.Inventory or MenuPage.InventoryItem or
            MenuPage.Loadout or MenuPage.Armory or MenuPage.Abilities or MenuPage.Proficiencies or
            MenuPage.Crafting or MenuPage.CraftingItem or MenuPage.Stats;
        int titleScale = settings.LargeHudText ? 6 : 5;
        Vector2 titleSize = OxaniumFont.Measure(title, titleScale);
        float titleY = profileSurface
            ? safe.Top + (settings.LargeHudText ? 42f : 54f)
            : safe.Center.Y - 230;
        _font.Draw(_spriteBatch, title,
            new Vector2(safe.Center.X - (titleSize.X / 2f), titleY), Color.White, titleScale);
        if (menu.IsOpen)
        {
            if (profileSurface)
            {
                DrawProfileTabs(menu.Page, safe);
            }
            if (menu.Page == MenuPage.Main)
            {
                DrawCentered("ORBITAL COMMAND // LIVE OPERATIVE LINK", safe.Center.X,
                    safe.Center.Y - 164, FocusCyan, 2);
                DrawCentered("CHOOSE A PROTOCOL. BUILD YOUR OPERATIVE. FOLLOW THE SIGNAL.", safe.Center.X,
                    safe.Center.Y - 130, SecondaryText, 1);
            }
            else if (menu.Page == MenuPage.AdventureSetup)
            {
                DrawCentered("THREE DERELICT FLOORS // ONE REPRODUCIBLE SIGNAL", safe.Center.X,
                    safe.Center.Y - 156, FocusCyan, 1);
                DrawCentered("DECIMAL SEED 1 - 2,147,483,647", safe.Center.X,
                    safe.Center.Y - 130, SecondaryText, 1);
            }
            else if (menu.Page == MenuPage.Loadout)
            {
                DrawCentered("TEN FAMILY SLOTS // SELECT A SLOT TO FILTER YOUR STASH", safe.Center.X,
                    safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Armory)
            {
                DrawCentered("ALL 50 BASES // CHOOSE A COMMON RUN-BOUND ISSUE",
                    safe.Center.X, safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Character)
            {
                DrawCentered("LEVEL, TALENTS, AND FREE BETWEEN-RUN RESPEC", safe.Center.X,
                    safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Inventory)
            {
                DrawCentered("FILTER WITH LEFT/RIGHT  ENTER INSPECTS, COMPARES, EQUIPS, LOCKS, OR SALVAGES",
                    safe.Center.X, safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.InventoryItem)
            {
                DrawCentered("EQUIPPED, LOCKED, AND FAVORITED ITEMS ARE PROTECTED FROM BATCH SALVAGE",
                    safe.Center.X, safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Abilities)
            {
                DrawCentered("EQUIPPED ITEMS TEACH AP  MASTERED ABILITIES STAY AVAILABLE",
                    safe.Center.X, safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Proficiencies)
            {
                DrawCentered("DAMAGE AND KILLS ADVANCE EACH WEAPON FAMILY TO RANK 25",
                    safe.Center.X, safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Crafting)
            {
                DrawCentered("GROUP DUPLICATES, INFUSE ITEM POWER, OR DISMANTLE FOR MATERIALS",
                    safe.Center.X, safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.CraftingItem)
            {
                DrawCentered("INFUSION PRESERVES RARITY, AFFIXES, UNIQUE EFFECTS, AND ITEM ID",
                    safe.Center.X, safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Stats)
            {
                DrawCentered("CURRENT VALUES INCLUDE EQUIPMENT, TALENTS, AND MASTERED PASSIVES",
                    safe.Center.X, safe.Top + 119, Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Difficulty)
            {
                DifficultyDefinition selectedDifficulty = menu.SelectedIndex < DifficultyCatalog.All.Count
                    ? DifficultyCatalog.All[menu.SelectedIndex]
                    : DifficultyCatalog.Get(menu.Profile?.SelectedDifficulty ?? DifficultyMode.Normal);
                DrawCentered("HARDER MODES REDUCE SUPPLIES AND IMPROVE EQUIPMENT RARITY",
                    safe.Center.X, safe.Center.Y - 168, Color.LightCyan, 1);
                DrawCentered(
                    $"{selectedDifficulty.DisplayName}  HEALTH x{selectedDifficulty.HealthDropMultiplier:0.00}" +
                    $"  AMMO x{selectedDifficulty.AmmoDropMultiplier:0.00}" +
                    $"  SUPPLY x{selectedDifficulty.SupplyAmountMultiplier:0.00}" +
                    $"  RARITY +{selectedDifficulty.RarityLuckBonus * 100f:0}%",
                    safe.Center.X, safe.Center.Y - 144, WarningAmber, 1);
            }
            else if (menu.Page == MenuPage.Controls)
            {
                DrawCentered(menu.BindingCaptureAction is { } captureAction
                        ? $"PRESS A CONTROLLER BUTTON FOR {GamepadBindingCatalog.ActionLabel(captureAction)}"
                        : "SELECT AN ACTION  //  PRESS A BUTTON  //  CONFLICTS SWAP AUTOMATICALLY",
                    safe.Center.X, safe.Center.Y - 162,
                    menu.BindingCaptureAction is null ? Color.LightCyan : WarningAmber, 1);
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
            else if (menu.Page == MenuPage.WeaponPickup && menu.WeaponPickupDecision is { } pickupDecision)
            {
                EquipmentInstance offered = pickupDecision.OfferedItem;
                EquipmentInstance? equipped = pickupDecision.EquippedItem;
                DrawCentered(
                    $"NEW  {offered.Rarity.ToString().ToUpperInvariant()}  {offered.DisplayName.ToUpperInvariant()}  IP {offered.ItemPower}",
                    safe.Center.X, safe.Center.Y - 160, RarityColor(offered.Rarity), 2);
                DrawCentered(
                    equipped is null
                        ? "CURRENT  EMPTY"
                        : $"CURRENT  {equipped.Rarity.ToString().ToUpperInvariant()}  {equipped.DisplayName.ToUpperInvariant()}  IP {equipped.ItemPower}",
                    safe.Center.X, safe.Center.Y - 128,
                    equipped is null ? new Color(150, 170, 185) : RarityColor(equipped.Rarity), 1);
                DrawCentered("REPLACE KEEPS THE OLD WEAPON IN INVENTORY",
                    safe.Center.X, safe.Center.Y - 102, Color.LightCyan, 1);
                IReadOnlyList<string> comparisonLines = menu.GetWeaponPickupComparisonLines();
                for (int lineIndex = 0; lineIndex < comparisonLines.Count; lineIndex++)
                {
                    DrawCentered(comparisonLines[lineIndex], safe.Center.X,
                        safe.Center.Y - 80 + (lineIndex * 16), new Color(190, 220, 232), 1);
                }
            }
            else if (menu.Page == MenuPage.Records)
            {
                ProfileData? profile = menu.Profile;
                bool adventureRecords = menu.RecordsMode == GameMode.Adventure;
                int bestScore = adventureRecords
                    ? profile?.BestUnassistedAdventureRun?.Score ?? 0
                    : profile?.BestUnassistedRun?.Score ?? 0;
                int wins = adventureRecords ? profile?.AdventureRunsWon ?? 0 : profile?.RunsWon ?? 0;
                int lifetimeKills = profile?.LifetimeKills ?? 0;
                DrawCentered($"BEST SCORE {bestScore}", safe.Center.X, safe.Center.Y - 145, Color.LightCyan, 2);
                DrawCentered($"WINS {wins}   LIFETIME KILLS {lifetimeKills}", safe.Center.X, safe.Center.Y - 105,
                    new Color(190, 220, 232), 1);
                DrawCentered(adventureRecords ? "ADVENTURE // THE NULL SIGNAL" : "ARENA // ORBITAL DEPOT",
                    safe.Center.X, safe.Center.Y - 72, WarningAmber, 1);
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
                DrawCentered(simulation.Mode == GameMode.Adventure
                        ? "SELECT A RUN BOON // PROGRESS SAVES BEFORE DESCENT"
                        : "SELECT ONE PERMANENT UPGRADE FOR THIS RUN",
                    safe.Center.X, safe.Center.Y - 150,
                    Color.LightCyan, 1);
            }
            else if (menu.Page == MenuPage.Results)
            {
                bool adventureResult = simulation.Mode == GameMode.Adventure;
                RunRecord? record = adventureResult
                    ? menu.Profile?.MostRecentAdventureRun
                    : menu.Profile?.MostRecentRun;
                float resultWidth = OxaniumFont.Measure("SCORE ", 2).X + OxaniumFont.MeasureNumber(simulation.Score, 2).X +
                    OxaniumFont.Measure("  KILLS ", 2).X + OxaniumFont.MeasureNumber(simulation.Kills, 2).X;
                float resultX = safe.Center.X - (resultWidth / 2f);
                resultX = DrawText("SCORE ", resultX, safe.Center.Y - 146, Color.LightCyan, 2);
                resultX = DrawNumber(simulation.Score, resultX, safe.Center.Y - 146, Color.LightCyan, 2);
                resultX = DrawText("  KILLS ", resultX, safe.Center.Y - 146, Color.LightCyan, 2);
                DrawNumber(simulation.Kills, resultX, safe.Center.Y - 146, Color.LightCyan, 2);
                DrawCentered(
                    simulation.Phase == GamePhase.Victory
                        ? adventureResult ? "CORE WARDEN DESTROYED" : "BREACH WALKER DESTROYED"
                        : adventureResult ? "EXPEDITION CHECKPOINT RESTORED" : "RUN TERMINATED",
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
                        adventureResult
                            ? $"GEN {record.GeneratorVersion?.ToUpperInvariant() ?? "N/A"}   HASH {record.LayoutHash ?? "N/A"}"
                            : $"LOOT {record.EquipmentCollected}   HIGHEST IP {record.HighestItemPower}   " +
                              $"MASTERED {record.AbilitiesMastered.Count}",
                        safe.Center.X, safe.Center.Y - 36, Color.LightCyan, 1);
                    DrawCentered(
                        (adventureResult
                            ? $"FLOORS {record.FloorsCompleted}/3   SECRETS {record.SecretsFound}   " +
                              $"LORE {record.LoreFound}   BOONS {record.UpgradeIds.Count}/3   "
                            : $"SECTORS {record.SectorsCompleted}/3   BOONS {record.UpgradeIds.Count}/9   ") +
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
            MenuRowWindow rowWindow = MenuLayout.GetRowWindow(
                safe, rows.Count, settings.LargeHudText, menu.Page, menu.SelectedIndex);
            MenuLayoutMetrics layout = MenuLayout.Create(
                safe, rowWindow.Count, settings.LargeHudText, menu.Page);
            int rowHeight = layout.RowHeight;
            int panelWidth = layout.RowWidth - 10;
            int panelLeft = layout.RowLeft + 5;
            int top = layout.RowTop + 7;
            Rectangle panel = layout.Panel;
            DrawRect(new Rectangle(panel.X + 9, panel.Y + 11, panel.Width, panel.Height), new Color(0, 0, 0, 125));
            DrawOutlined(panel, Glass, new Color(56, 174, 210, 220));
            DrawRect(new Rectangle(panel.X, panel.Y, 7, panel.Height), FocusCyan);
            if (_animatedMenuPage != menu.Page || _animatedMenuIndex != menu.SelectedIndex)
            {
                _animatedMenuPage = menu.Page;
                _animatedMenuIndex = menu.SelectedIndex;
                _selectionStartedSeconds = presentationSeconds;
                _selectionFill = settings.ReducedUiMotion ? 1f : 0f;
            }
            else
            {
                _selectionFill = settings.ReducedUiMotion
                    ? 1f
                    : (float)Math.Clamp((presentationSeconds - _selectionStartedSeconds) / 0.160, 0d, 1d);
            }
            for (int visibleIndex = 0; visibleIndex < rowWindow.Count; visibleIndex++)
            {
                int index = rowWindow.Start + visibleIndex;
                int y = top + (visibleIndex * rowHeight);
                bool selected = index == menu.SelectedIndex;
                if (menu.Page == MenuPage.Main)
                {
                    Color accent = index switch
                    {
                        0 => FocusCyan,
                        1 => SuccessTeal,
                        2 => WarningAmber,
                        3 => new Color(126, 170, 235),
                        4 => new Color(154, 181, 199),
                        _ => AlertCoral,
                    };
                    Rectangle card = new(panelLeft - 5, y - 3, panelWidth + 10, rowHeight - 8);
                    DrawRect(card, selected ? new Color(14, 47, 64, 238) : new Color(8, 25, 39, 224));
                    DrawRect(new Rectangle(card.Left, card.Top, selected ? 7 : 3, card.Height),
                        accent * (selected ? 1f : 0.65f));
                    DrawRect(new Rectangle(card.Left, card.Bottom - 1, card.Width, 1),
                        accent * (selected ? 0.8f : 0.22f));
                    if (selected)
                    {
                        int fillWidth = (int)(card.Width * _selectionFill);
                        DrawRect(new Rectangle(card.Left, card.Top, fillWidth, card.Height),
                            new Color(accent, settings.ReducedFlash ? 28 : 44));
                        DrawSelectionBrackets(card);
                    }

                    string number = $"0{index + 1}";
                    _font.Draw(_spriteBatch, number, new Vector2(card.Left + 18, card.Top + 12),
                        selected ? accent : SecondaryText, 1);
                    _font.Draw(_spriteBatch, rows[index], new Vector2(card.Left + 58, card.Top + 7),
                        Color.White, rowScale);
                    string description = index switch
                    {
                        0 => "ARENA + THE NULL SIGNAL",
                        1 => "CHARACTER + ABILITIES + STATS",
                        2 => "INVENTORY + LOADOUT + CRAFTING",
                        3 => "ARENA + ADVENTURE HISTORY",
                        4 => "GAMEPLAY + ACCESSIBILITY",
                        _ => "END OPERATIVE SESSION",
                    };
                    Vector2 descriptionSize = OxaniumFont.Measure(description, 1);
                    _font.Draw(_spriteBatch, description,
                        new Vector2(card.Right - descriptionSize.X - 22, card.Top + 14),
                        selected ? accent : SecondaryText, 1);
                    continue;
                }
                _spriteBatch.Draw(_menuButton,
                    new Rectangle(panelLeft - 5, y - 9, panelWidth + 10, rowHeight + 3),
                    new Color(16, 44, 61, 220));
                if (selected)
                {
                    int fillWidth = (int)((panelWidth + 10) * _selectionFill);
                    DrawRect(new Rectangle(panelLeft - 5, y - 9, fillWidth, rowHeight + 3),
                        new Color(FocusCyan, settings.ReducedFlash ? 55 : 92));
                    DrawSelectionBrackets(new Rectangle(panelLeft - 5, y - 9, panelWidth + 10, rowHeight + 3));
                }

                string rowText = FitText(rows[index], panelWidth * 0.66f, rowScale);
                _font.Draw(_spriteBatch, rowText, new Vector2(panelLeft + 18, y), Color.White, rowScale);
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
                        Vector2 valueSize = OxaniumFont.Measure(value, rowScale);
                        _font.Draw(_spriteBatch, value,
                            new Vector2(panelLeft + panelWidth - valueSize.X - 18, y), Color.LightCyan, rowScale);
                    }
                }
            }

            if (rowWindow.Count < rows.Count)
            {
                Rectangle up = MenuLayout.GetScrollUpBounds(layout);
                Rectangle down = MenuLayout.GetScrollDownBounds(layout);
                Rectangle track = MenuLayout.GetScrollTrackBounds(layout);
                DrawOutlined(up, new Color(8, 29, 43, 235), rowWindow.Start > 0 ? FocusCyan : SecondaryText);
                DrawOutlined(down, new Color(8, 29, 43, 235),
                    rowWindow.Start + rowWindow.Count < rows.Count ? FocusCyan : SecondaryText);
                DrawCentered("^", up.Center.X, up.Top + 4,
                    rowWindow.Start > 0 ? FocusCyan : SecondaryText, 1);
                DrawCentered("v", down.Center.X, down.Top + 3,
                    rowWindow.Start + rowWindow.Count < rows.Count ? FocusCyan : SecondaryText, 1);
                DrawRect(track, new Color(32, 61, 78, 220));
                int thumbHeight = Math.Max(14, (int)MathF.Round(track.Height *
                    (rowWindow.Count / (float)rows.Count)));
                float progress = rowWindow.Start / (float)Math.Max(1, rows.Count - rowWindow.Count);
                int thumbY = track.Top + (int)MathF.Round((track.Height - thumbHeight) * progress);
                DrawRect(new Rectangle(track.Left - 2, thumbY, track.Width + 4, thumbHeight), FocusCyan);
                string range = $"{rowWindow.Start + 1}-{rowWindow.Start + rowWindow.Count} / {rows.Count}";
                DrawRect(new Rectangle(panel.Left + 9, panel.Top - 9, 118, 18),
                    new Color(5, 20, 31, 245));
                _font.Draw(_spriteBatch, range, new Vector2(panel.Left + 16, panel.Top - 7),
                    SecondaryText, 1);
            }

            string help = menu.Page == MenuPage.Main
                ? "DPAD / LEFT STICK NAVIGATE   ENTER / A / PS CROSS SELECT"
                : menu.Page == MenuPage.Controls
                    ? menu.BindingCaptureAction is null
                        ? "SCROLL/DPAD NAVIGATE  ENTER/A REBIND  LEFT/RIGHT CYCLE  ESC/B BACK"
                        : "PRESS ANY CONTROLLER BUTTON  ESC CANCELS"
                : menu.Page == MenuPage.Reward
                ? "ARROWS/DPAD CHOOSE  ENTER/A INSTALL"
                : menu.Page == MenuPage.Recovery
                    ? "ARROWS/DPAD INSPECT  ENTER/A TAKE  TAKE ALL TO CONTINUE"
                : menu.Page == MenuPage.WeaponPickup
                    ? "ARROWS/DPAD/LS CHOOSE  ENTER/A CONFIRM  ESC/B LEAVE"
                : "ARROWS/DPAD/LS ADJUST  ENTER/A SELECT  ESC/B BACK  LB/RB TABS";
            Vector2 helpSize = OxaniumFont.Measure(help, 1);
            _font.Draw(_spriteBatch, help,
                new Vector2(safe.Center.X - (helpSize.X / 2f), safe.Bottom - 52), new Color(180, 220, 235), 1);
            return;
        }

        if (simulation.Phase is GamePhase.Victory or GamePhase.Defeat)
        {
            float resultWidth = OxaniumFont.Measure("SCORE ", 2).X + OxaniumFont.MeasureNumber(simulation.Score, 2).X +
                OxaniumFont.Measure("  KILLS ", 2).X + OxaniumFont.MeasureNumber(simulation.Kills, 2).X;
            float resultX = safe.Center.X - (resultWidth / 2f);
            resultX = DrawText("SCORE ", resultX, safe.Center.Y + 5, Color.LightCyan, 2);
            resultX = DrawNumber(simulation.Score, resultX, safe.Center.Y + 5, Color.LightCyan, 2);
            resultX = DrawText("  KILLS ", resultX, safe.Center.Y + 5, Color.LightCyan, 2);
            DrawNumber(simulation.Kills, resultX, safe.Center.Y + 5, Color.LightCyan, 2);
            const string restart = "PRESS ENTER TO RUN AGAIN";
            Vector2 restartSize = OxaniumFont.Measure(restart, 2);
            _font.Draw(_spriteBatch, restart, new Vector2(safe.Center.X - (restartSize.X / 2f), safe.Center.Y + 55), new Color(255, 210, 70), 2);
        }
    }

    private void DrawTitleMenuBackground(Rectangle destination)
    {
        float sourceAspect = _titleMenuBackground.Width / (float)_titleMenuBackground.Height;
        float destinationAspect = destination.Width / (float)Math.Max(1, destination.Height);
        Rectangle source;
        if (sourceAspect > destinationAspect)
        {
            int width = Math.Max(1, (int)MathF.Round(_titleMenuBackground.Height * destinationAspect));
            source = new Rectangle((_titleMenuBackground.Width - width) / 2, 0,
                width, _titleMenuBackground.Height);
        }
        else
        {
            int height = Math.Max(1, (int)MathF.Round(_titleMenuBackground.Width / destinationAspect));
            source = new Rectangle(0, (_titleMenuBackground.Height - height) / 2,
                _titleMenuBackground.Width, height);
        }

        _spriteBatch.Draw(_titleMenuBackground, destination, source, Color.White);
    }

    private void DrawAdventureHud(GameSimulation simulation, GameSettings settings, Rectangle safe)
    {
        if (simulation.AdventureSnapshot is not AdventureSnapshot snapshot)
        {
            return;
        }

        if (simulation.GeneratedDungeonFloor is null)
        {
            DrawAdventureBossObjectives(simulation, snapshot, settings, safe);
            DrawAdventureTransmission(simulation, settings, safe);
            return;
        }

        DrawAdventureMinimap(simulation, safe, expanded: false);
        int left = safe.Left + 22;
        int top = safe.Top + 78;
        int objectiveHeight = 74 + (snapshot.Objectives.Count * 21);
        const int panelWidth = 404;
        DrawOutlined(new Rectangle(left, top, panelWidth, objectiveHeight),
            new Color(7, 19, 32, 220), new Color(87, 220, 245, 160));
        _font.Draw(_spriteBatch,
            $"FLOOR {snapshot.StageIndex + 1}/3 // {simulation.Arena.DisplayName.ToUpperInvariant()}",
            new Vector2(left + 12, top + 9), FocusCyan, 1);
        for (int index = 0; index < snapshot.Objectives.Count; index++)
        {
            AdventureObjectiveSnapshot objective = snapshot.Objectives[index];
            Color color = objective.Complete ? SuccessTeal : objective.Available ? Color.White : SecondaryText;
            string status = objective.Complete ? "[OK]" : objective.Available
                ? $"[{objective.Current}/{objective.Required}]"
                : "[LOCK]";
            _font.Draw(_spriteBatch,
                FitText($"{status} {objective.DisplayName.ToUpperInvariant()}", panelWidth - 24f, 1),
                new Vector2(left + 12, top + 31 + (index * 22)), color, 1);
        }

        HashSet<string> completed = snapshot.CompletedInteractables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int chestsTotal = simulation.GeneratedDungeonFloor.Interactables.Count(item =>
            item.Kind == AdventureInteractableKind.EquipmentCache);
        int chestsOpened = simulation.GeneratedDungeonFloor.Interactables.Count(item =>
            item.Kind == AdventureInteractableKind.EquipmentCache && completed.Contains(item.Id));
        int loreTotal = simulation.GeneratedDungeonFloor.Interactables.Count(item =>
            item.Kind == AdventureInteractableKind.LoreTerminal);
        int loreOpened = simulation.GeneratedDungeonFloor.Interactables.Count(item =>
            item.Kind == AdventureInteractableKind.LoreTerminal && completed.Contains(item.Id));
        int detailY = top + 36 + snapshot.Objectives.Count * 21;
        _font.Draw(_spriteBatch,
            $"EXPLORED {snapshot.DiscoveredRooms.Count}/{simulation.GeneratedDungeonFloor.Rooms.Count}  " +
            $"CHESTS {chestsOpened}/{chestsTotal}  LORE {loreOpened}/{loreTotal}",
            new Vector2(left + 12, detailY), SecondaryText, 1);
        GeneratedDungeonInteractable? lift = simulation.GeneratedDungeonFloor.Interactables.FirstOrDefault(item =>
            item.Kind == AdventureInteractableKind.Lift);
        if (lift is not null)
        {
            int distance = (int)MathF.Ceiling(System.Numerics.Vector3.Distance(
                simulation.Player.Position, lift.Position));
            bool ready = simulation.CanInteractWithAdventure(lift.Id);
            _font.Draw(_spriteBatch, ready
                    ? $"[EXIT READY] LIFT {distance}M"
                    : $"[EXIT LOCKED] COMPLETE REQUIRED OBJECTIVES  //  {distance}M",
                new Vector2(left + 12, detailY + 19), ready ? SuccessTeal : WarningAmber, 1);
        }

        DrawAdventureInteractionPrompt(simulation, snapshot, settings, safe,
            simulation.AdventureStoryBeat is not null);

        DrawAdventureTransmission(simulation, settings, safe);
    }

    private void DrawAdventureTransmission(GameSimulation simulation, GameSettings settings, Rectangle safe)
    {
        if (simulation.AdventureStoryBeat is AdventureStoryBeatDefinition beat)
        {
            int width = Math.Min(720, safe.Width - 80);
            int boxLeft = safe.Center.X - width / 2;
            int boxTop = safe.Bottom - 270;
            DrawOutlined(new Rectangle(boxLeft, boxTop, width, 92),
                new Color(7, 19, 32, 238), new Color(87, 220, 245, 210));
            DrawRect(new Rectangle(boxLeft, boxTop, 6, 92), FocusCyan);
            _font.Draw(_spriteBatch, $"TRANSMISSION // {beat.Speaker}",
                new Vector2(boxLeft + 18, boxTop + 12), FocusCyan, 1);
            string[] lines = WrapText(beat.Text.ToUpperInvariant(), width - 36, 1, maximumLines: 2);
            for (int index = 0; index < lines.Length; index++)
            {
                _font.Draw(_spriteBatch, lines[index], new Vector2(boxLeft + 18, boxTop + 35 + index * 18),
                    Color.White, 1);
            }
            string skip = $"E / {GamepadBindingCatalog.ButtonLabel(settings.ControllerBindings.Interact)}  SKIP";
            Vector2 skipSize = OxaniumFont.Measure(skip, 1);
            _font.Draw(_spriteBatch, skip, new Vector2(boxLeft + width - skipSize.X - 16, boxTop + 72),
                SecondaryText, 1);
        }
    }

    private void DrawAdventureBossObjectives(
        GameSimulation simulation,
        AdventureSnapshot snapshot,
        GameSettings settings,
        Rectangle safe)
    {
        int left = safe.Left + 22;
        int top = safe.Top + 78;
        const int width = 340;
        int height = 52 + snapshot.Objectives.Count * 22;
        DrawOutlined(new Rectangle(left, top, width, height),
            new Color(7, 19, 32, 220), new Color(255, 102, 125, 180));
        _font.Draw(_spriteBatch, "CORE CHAMBER // SIGNAL LOCK",
            new Vector2(left + 12, top + 9), AlertCoral, 1);
        for (int index = 0; index < snapshot.Objectives.Count; index++)
        {
            AdventureObjectiveSnapshot objective = snapshot.Objectives[index];
            string status = objective.Complete ? "[OK]" : objective.Available
                ? $"[{objective.Current}/{objective.Required}]"
                : "[LOCK]";
            _font.Draw(_spriteBatch,
                FitText($"{status} {objective.DisplayName.ToUpperInvariant()}", width - 24, 1),
                new Vector2(left + 12, top + 31 + index * 22),
                objective.Complete ? SuccessTeal : objective.Available ? Color.White : SecondaryText, 1);
        }

        if (simulation.NearbyAdventureBossControlId is not null)
        {
            string prompt = $"[E / {GamepadBindingCatalog.ButtonLabel(settings.ControllerBindings.Interact)}]  " +
                "DISABLE SHIELD CONTROL";
            int promptWidth = Math.Min(520, safe.Width - 60);
            int promptLeft = safe.Center.X - promptWidth / 2;
            int promptTop = safe.Bottom - (simulation.AdventureStoryBeat is null ? 190 : 325);
            DrawOutlined(new Rectangle(promptLeft, promptTop, promptWidth, 42),
                new Color(7, 19, 32, 232), FocusCyan);
            DrawCentered(prompt, safe.Center.X, promptTop + 12, FocusCyan, 1);
        }
    }

    private void DrawAdventureInteractionPrompt(
        GameSimulation simulation,
        AdventureSnapshot snapshot,
        GameSettings settings,
        Rectangle safe,
        bool transmissionVisible)
    {
        GeneratedDungeonInteractable? interactable = simulation.NearbyAdventureInteractable;
        if (interactable is null)
        {
            return;
        }

        bool available = simulation.CanInteractWithAdventure(interactable.Id);
        string action = interactable.Kind switch
        {
            AdventureInteractableKind.EquipmentCache => "OPEN LOOT CHEST",
            AdventureInteractableKind.LoreTerminal => "ACCESS LORE TERMINAL",
            AdventureInteractableKind.PowerRelay => "RESTORE POWER RELAY",
            AdventureInteractableKind.CommandKey => "RECOVER COMMAND KEY",
            AdventureInteractableKind.FabricatorConsole => "DISABLE FABRICATOR",
            AdventureInteractableKind.SignalAnchor => "DISABLE SIGNAL ANCHOR",
            AdventureInteractableKind.Lift => "DESCEND TO NEXT STAGE",
            _ => "INTERACT",
        };
        string text;
        Color color;
        if (available)
        {
            text = $"[E / {GamepadBindingCatalog.ButtonLabel(settings.ControllerBindings.Interact)}]  {action}";
            color = interactable.Kind == AdventureInteractableKind.Lift ? SuccessTeal : FocusCyan;
        }
        else if (interactable.Kind == AdventureInteractableKind.Lift)
        {
            int remaining = snapshot.Objectives.Count(objective => !objective.Complete);
            text = $"EXIT LOCKED  //  {remaining} OBJECTIVE{(remaining == 1 ? string.Empty : "S")} REMAIN";
            color = WarningAmber;
        }
        else
        {
            AdventureObjectiveSnapshot? dependency = snapshot.Objectives.FirstOrDefault(objective =>
                objective.Id.Equals(interactable.RequiresObjectiveId, StringComparison.OrdinalIgnoreCase));
            text = dependency is null
                ? "ACCESS LOCKED"
                : $"LOCKED  //  COMPLETE {dependency.DisplayName.ToUpperInvariant()}";
            color = WarningAmber;
        }

        int width = Math.Min(620, safe.Width - 60);
        int top = safe.Bottom - (transmissionVisible ? 325 : 190);
        int left = safe.Center.X - width / 2;
        DrawOutlined(new Rectangle(left, top, width, 42), new Color(7, 19, 32, 232), color);
        Vector2 size = OxaniumFont.Measure(text, 1);
        _font.Draw(_spriteBatch, FitText(text, width - 28f, 1),
            new Vector2(left + Math.Max(14f, (width - size.X) * 0.5f), top + 12), color, 1);
    }

    private void DrawAdventureStageTransition(
        GameSimulation simulation,
        GameSettings settings,
        Rectangle safe)
    {
        if (simulation.Mode != GameMode.Adventure || simulation.AdventureSnapshot is not AdventureSnapshot snapshot)
        {
            return;
        }

        float duration = settings.ReducedUiMotion ? 0.45f : 1.4f;
        float elapsed = simulation.AdventureStageElapsedSeconds;
        if (elapsed >= duration)
        {
            return;
        }

        float hold = settings.ReducedUiMotion ? 0.08f : 0.28f;
        float opacity = elapsed <= hold ? 1f : Math.Clamp((duration - elapsed) / (duration - hold), 0f, 1f);
        DrawRect(safe, new Color(3, 9, 16, (int)(242f * opacity)));
        string kicker = snapshot.StageKind == AdventureStageKind.Boss
            ? "FINAL DESCENT"
            : $"DESCENDING // FLOOR {snapshot.StageIndex + 1}/3";
        string title = snapshot.StageKind == AdventureStageKind.Boss
            ? "CORE CHAMBER"
            : simulation.Arena.DisplayName.ToUpperInvariant();
        Vector2 kickerSize = OxaniumFont.Measure(kicker, 1);
        Vector2 titleSize = OxaniumFont.Measure(title, 5);
        _font.Draw(_spriteBatch, kicker,
            new Vector2(safe.Center.X - kickerSize.X / 2f, safe.Center.Y - 42), FocusCyan * opacity, 1);
        _font.Draw(_spriteBatch, title,
            new Vector2(safe.Center.X - titleSize.X / 2f, safe.Center.Y - 12), Color.White * opacity, 5);
        DrawRect(new Rectangle(safe.Center.X - 110, safe.Center.Y + 47, 220, 3), FocusCyan * opacity);
    }

    private void DrawAdventureMinimap(GameSimulation simulation, Rectangle safe, bool expanded)
    {
        GeneratedDungeonFloor? floor = simulation.GeneratedDungeonFloor;
        AdventureSnapshot? snapshot = simulation.AdventureSnapshot;
        if (floor is null || snapshot is null || floor.Minimap.WalkableCells.Count == 0)
        {
            return;
        }

        int width = expanded ? Math.Min(340, safe.Width / 3) : 190;
        int height = expanded ? 270 : 150;
        int left = safe.Right - width - 22;
        int top = expanded ? safe.Top + 96 : safe.Top + 76;
        DrawOutlined(new Rectangle(left, top, width, height),
            new Color(7, 19, 32, expanded ? 238 : 212), new Color(87, 220, 245, 170));
        _font.Draw(_spriteBatch, expanded ? "NORTH-UP DUNGEON MAP" : "N  //  MAP",
            new Vector2(left + 10, top + 8), FocusCyan, 1);

        int minX = floor.Minimap.WalkableCells.Min(cell => cell.X);
        int maxX = floor.Minimap.WalkableCells.Max(cell => cell.X);
        int minZ = floor.Minimap.WalkableCells.Min(cell => cell.Z);
        int maxZ = floor.Minimap.WalkableCells.Max(cell => cell.Z);
        float availableMapHeight = height - (expanded ? 70f : 38f);
        float cellPixels = MathF.Min(
            (width - 20f) / Math.Max(1, maxX - minX + 1),
            availableMapHeight / Math.Max(1, maxZ - minZ + 1));
        float mapWidth = (maxX - minX + 1) * cellPixels;
        float mapHeight = (maxZ - minZ + 1) * cellPixels;
        float mapLeft = left + (width - mapWidth) * 0.5f;
        float mapTop = top + 28 + ((availableMapHeight - mapHeight) * 0.5f);
        foreach (DungeonGridCell cell in snapshot.DiscoveredMapCells)
        {
            Rectangle bounds = CellBounds(cell, 0);
            DrawRect(bounds, new Color(68, 117, 139, 190));
        }

        HashSet<string> completed = snapshot.CompletedInteractables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (GeneratedDungeonInteractable item in floor.Interactables)
        {
            bool roomDiscovered = snapshot.DiscoveredRooms.Contains(item.RoomIndex);
            bool activeObjective = item.ObjectiveId is not null && snapshot.Objectives.Any(objective =>
                objective.Id.Equals(item.ObjectiveId, StringComparison.OrdinalIgnoreCase) &&
                objective.Available && !objective.Complete);
            if (!roomDiscovered && !activeObjective)
            {
                continue;
            }

            DungeonGridCell cell = ToCell(item.Position);
            Color marker = item.Kind switch
            {
                AdventureInteractableKind.EquipmentCache => FocusCyan,
                AdventureInteractableKind.LoreTerminal => Color.White,
                AdventureInteractableKind.Lift => SuccessTeal,
                _ => WarningAmber,
            };
            if (completed.Contains(item.Id))
            {
                marker = SuccessTeal;
            }
            DrawMarker(cell, marker, expanded ? 6 : 4);
        }

        if (expanded)
        {
            foreach (GeneratedDungeonGate gate in floor.Gates.Where(gate =>
                snapshot.DiscoveredRooms.Contains(gate.FromRoomIndex) ||
                snapshot.DiscoveredRooms.Contains(gate.ToRoomIndex)))
            {
                bool unlocked = snapshot.Objectives.Any(objective =>
                    objective.Id.Equals(gate.UnlockObjectiveId, StringComparison.OrdinalIgnoreCase) &&
                    objective.Complete);
                if (!unlocked)
                {
                    DrawMarker(ToCell(gate.Position), AlertCoral, 6);
                }
            }
        }

        DungeonGridCell playerCell = ToCell(simulation.Player.Position);
        DrawMarker(playerCell, Color.White, expanded ? 8 : 6);
        if (expanded)
        {
            _font.Draw(_spriteBatch,
                "C CHEST  O OBJ  ! LOCK  E EXIT",
                new Vector2(left + 10, top + height - 54), SecondaryText, 1);
            _font.Draw(_spriteBatch, $"SEED {snapshot.Seed}  {snapshot.GeneratorVersion.ToUpperInvariant()}",
                new Vector2(left + 10, top + height - 37), SecondaryText, 1);
            _font.Draw(_spriteBatch, $"HASH {snapshot.LayoutHash}",
                new Vector2(left + 10, top + height - 20), SecondaryText, 1);
        }
        return;

        DungeonGridCell ToCell(System.Numerics.Vector3 position)
        {
            int gridX = (int)MathF.Floor((position.X - floor.Minimap.WorldOrigin.X) / floor.Minimap.CellSize);
            int gridZ = (int)MathF.Floor((position.Z - floor.Minimap.WorldOrigin.Y) / floor.Minimap.CellSize);
            return new DungeonGridCell(gridX, gridZ);
        }

        Rectangle CellBounds(DungeonGridCell cell, int inset)
        {
            int x = (int)MathF.Round(mapLeft + (cell.X - minX) * cellPixels) + inset;
            int y = (int)MathF.Round(mapTop + (cell.Z - minZ) * cellPixels) + inset;
            int size = Math.Max(1, (int)MathF.Ceiling(cellPixels) - inset * 2);
            return new Rectangle(x, y, size, size);
        }

        void DrawMarker(DungeonGridCell cell, Color color, int size)
        {
            Rectangle cellBounds = CellBounds(cell, 0);
            DrawRect(new Rectangle(cellBounds.Center.X - size / 2, cellBounds.Center.Y - size / 2, size, size), color);
        }
    }

    private void DrawSelectionBrackets(Rectangle bounds)
    {
        const int edge = 10;
        const int thickness = 2;
        DrawRect(new Rectangle(bounds.Left, bounds.Top, edge, thickness), FocusCyan);
        DrawRect(new Rectangle(bounds.Left, bounds.Top, thickness, edge), FocusCyan);
        DrawRect(new Rectangle(bounds.Right - edge, bounds.Top, edge, thickness), FocusCyan);
        DrawRect(new Rectangle(bounds.Right - thickness, bounds.Top, thickness, edge), FocusCyan);
        DrawRect(new Rectangle(bounds.Left, bounds.Bottom - thickness, edge, thickness), FocusCyan);
        DrawRect(new Rectangle(bounds.Left, bounds.Bottom - edge, thickness, edge), FocusCyan);
        DrawRect(new Rectangle(bounds.Right - edge, bounds.Bottom - thickness, edge, thickness), FocusCyan);
        DrawRect(new Rectangle(bounds.Right - thickness, bounds.Bottom - edge, thickness, edge), FocusCyan);
    }

    private static string FitText(string text, float maximumWidth, int scale)
    {
        if (OxaniumFont.Measure(text, scale).X <= maximumWidth)
        {
            return text;
        }
        const string ellipsis = "...";
        int length = text.Length;
        while (length > 0 && OxaniumFont.Measure(text.AsSpan(0, length), scale).X +
            OxaniumFont.Measure(ellipsis, scale).X > maximumWidth)
        {
            length--;
        }
        return length == 0 ? ellipsis : text[..length].TrimEnd() + ellipsis;
    }

    private static string[] WrapText(string text, float maximumWidth, int scale, int maximumLines)
    {
        List<string> lines = [];
        string current = string.Empty;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : $"{current} {word}";
            if (OxaniumFont.Measure(candidate, scale).X <= maximumWidth)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0)
            {
                lines.Add(current);
            }
            current = word;
            if (lines.Count == maximumLines)
            {
                break;
            }
        }
        if (lines.Count < maximumLines && current.Length > 0)
        {
            lines.Add(current);
        }
        if (lines.Count == maximumLines && string.Join(' ', lines).Length < text.Length)
        {
            lines[^1] = FitText(lines[^1] + "...", maximumWidth, scale);
        }
        return lines.ToArray();
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

        if (page == MenuPage.Controls)
        {
            if (index >= 0 && index < GamepadBindingCatalog.Actions.Count)
            {
                GamepadBindingAction action = GamepadBindingCatalog.Actions[index];
                return menu.BindingCaptureAction == action
                    ? "LISTENING..."
                    : GamepadBindingCatalog.ButtonLabel(settings.ControllerBindings[action]);
            }

            return string.Empty;
        }

        if (page is MenuPage.Loadout or MenuPage.Armory or MenuPage.ThreatTier or MenuPage.InventoryItem or
            MenuPage.AdventureSetup or MenuPage.SeedKeypad)
        {
            return menu.GetSupplementalValue(index);
        }

        if (page == MenuPage.Accessibility)
        {
            return index switch
            {
                0 => OnOff(settings.ReducedFlash),
                1 => OnOff(settings.ReducedUiMotion),
                2 => Percent(settings.ScreenShakeScale),
                3 => Percent(settings.CameraBobScale),
                4 => OnOff(settings.HighContrastReticle),
                5 => OnOff(settings.LargeHudText),
                6 => OnOff(settings.Subtitles),
                7 => OnOff(settings.ToggleAimDownSights),
                8 => settings.ColorVisionMode.ToString().ToUpperInvariant(),
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
            Vector2 size = OxaniumFont.Measure(labels[index], 1);
            _font.Draw(_spriteBatch, labels[index],
                new Vector2(bounds.Center.X - (size.X / 2f), bounds.Y + 9),
                selected ? Color.White : new Color(115, 210, 238), 1);
        }
    }

    private void DrawCentered(string text, int centerX, int y, Color color, int scale)
    {
        Vector2 size = OxaniumFont.Measure(text, scale);
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
            float width = OxaniumFont.MeasureNumber(weapon.Magazine, scale).X + OxaniumFont.Measure("/", scale).X +
                OxaniumFont.MeasureNumber(weapon.Reserve, scale).X;
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
        float totalWidth = OxaniumFont.Measure(label, scale).X + OxaniumFont.MeasureNumber(value, scale).X;
        float x = right - totalWidth;
        x = DrawText(label, x, y, Color.White, scale);
        DrawNumber(value, x, y, Color.White, scale);
    }

    private float DrawText(ReadOnlySpan<char> text, float x, float y, Color color, int scale)
    {
        _font.Draw(_spriteBatch, text, new Vector2(x, y), color, scale);
        return x + OxaniumFont.Measure(text, scale).X;
    }

    private float DrawNumber(int value, float x, float y, Color color, int scale)
    {
        _font.DrawNumber(_spriteBatch, value, new Vector2(x, y), color, scale);
        return x + OxaniumFont.MeasureNumber(value, scale).X;
    }
}
