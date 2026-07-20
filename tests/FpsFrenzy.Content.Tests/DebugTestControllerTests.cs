using FpsFrenzy.Kni.Development;
using Microsoft.Xna.Framework.Input;

namespace FpsFrenzy.Content.Tests;

public sealed class DebugTestControllerTests
{
    [Fact]
    public void DebugModeUsesEdgeTriggeredStageControls()
    {
        DebugTestController controller = new(enabled: false);

        Assert.Equal(
            DebugTestAction.ModeChanged,
            controller.Update(new KeyboardState(Keys.F1), runAvailable: true));
        controller.Update(new KeyboardState(), runAvailable: true);
        Assert.Equal(
            DebugTestAction.NextStage,
            controller.Update(new KeyboardState(Keys.F7), runAvailable: true));
        Assert.Equal(
            DebugTestAction.None,
            controller.Update(new KeyboardState(Keys.F7), runAvailable: true));
    }

    [Fact]
    public void DisablingDebugModeClearsTemporaryCollisionAndGodOverrides()
    {
        DebugTestController controller = new(enabled: true);
        controller.Update(new KeyboardState(Keys.F4, Keys.F8), runAvailable: true);
        Assert.True(controller.ShowCollision);
        Assert.True(controller.GodModeOverride);
        controller.Update(new KeyboardState(), runAvailable: true);

        DebugTestAction action = controller.Update(new KeyboardState(Keys.F1), runAvailable: true);

        Assert.Equal(DebugTestAction.ModeChanged, action);
        Assert.False(controller.Enabled);
        Assert.False(controller.ShowCollision);
        Assert.False(controller.GodModeOverride);
    }

    [Fact]
    public void StageMutationHotkeysAreIgnoredWithoutAnActiveRun()
    {
        DebugTestController controller = new(enabled: true);

        DebugTestAction action = controller.Update(
            new KeyboardState(Keys.F2, Keys.F3, Keys.F5, Keys.F7, Keys.F9),
            runAvailable: false);

        Assert.Equal(DebugTestAction.None, action);
    }

    [Fact]
    public void RpgAndLootHotkeysAreAvailableOnlyInsideDebugRuns()
    {
        DebugTestController controller = new(enabled: true);

        DebugTestAction action = controller.Update(
            new KeyboardState(Keys.F2, Keys.F3),
            runAvailable: true);

        Assert.True(action.HasFlag(DebugTestAction.GrantProgression));
        Assert.True(action.HasFlag(DebugTestAction.SpawnLootShowcase));
    }

    [Fact]
    public void F11LabExposesWeaponDifficultyThreatAndArenaShortcuts()
    {
        DebugTestController controller = new(enabled: false);
        DebugTestAction opened = controller.Update(new KeyboardState(Keys.F11), runAvailable: true);
        Assert.True(opened.HasFlag(DebugTestAction.LabModeChanged));
        Assert.True(controller.Enabled);
        Assert.True(controller.LabVisible);
        controller.Update(new KeyboardState(), runAvailable: true);

        DebugTestAction actions = controller.Update(new KeyboardState(
            Keys.OemCloseBrackets, Keys.OemPlus, Keys.PageUp, Keys.I, Keys.O, Keys.T, Keys.F12),
            runAvailable: true);

        Assert.True(actions.HasFlag(DebugTestAction.NextWeapon));
        Assert.True(actions.HasFlag(DebugTestAction.NextDifficulty));
        Assert.True(actions.HasFlag(DebugTestAction.NextThreatTier));
        Assert.True(actions.HasFlag(DebugTestAction.SpawnEnemy));
        Assert.True(actions.HasFlag(DebugTestAction.ToggleAiFreeze));
        Assert.True(actions.HasFlag(DebugTestAction.TeleportSector));
        Assert.True(actions.HasFlag(DebugTestAction.ReloadWeaponData));
    }
}
