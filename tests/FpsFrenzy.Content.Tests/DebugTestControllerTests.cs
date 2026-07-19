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
            new KeyboardState(Keys.F5, Keys.F7, Keys.F9),
            runAvailable: false);

        Assert.Equal(DebugTestAction.None, action);
    }
}
