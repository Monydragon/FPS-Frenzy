using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni;

namespace FpsFrenzy.Content.Tests;

public sealed class FpsFrenzyGameRunRestoreTests
{
    [Fact]
    public void UnknownCheckpointWeaponFallsBackToKnownProfileWeapon()
    {
        ContentCatalog catalog = LoadCatalog();

        string resolved = FpsFrenzyGame.ResolveStartingWeaponId(
            catalog,
            requestedWeaponId: "future-weapon",
            fallbackWeaponId: "arc-cannon");

        Assert.Equal("arc-cannon", resolved);
    }

    [Fact]
    public void UnknownCheckpointAndProfileWeaponsFallBackToPulseSidearm()
    {
        ContentCatalog catalog = LoadCatalog();

        string resolved = FpsFrenzyGame.ResolveStartingWeaponId(
            catalog,
            requestedWeaponId: "future-weapon",
            fallbackWeaponId: "removed-weapon");

        Assert.Equal("pulse-sidearm", resolved);
    }

    [Fact]
    public void ReleaseContinueCannotBeRedirectedToTheTrainingFixture()
    {
        ContentCatalog catalog = LoadCatalog();
        RunCheckpoint checkpoint = new()
        {
            ArenaId = "training-ring",
            StartingWeaponId = "future-weapon",
        };

        RunCheckpoint sanitized = FpsFrenzyGame.SanitizeReleaseCheckpoint(
            catalog,
            checkpoint,
            fallbackWeaponId: "arc-cannon");

        Assert.Equal("orbital-depot", sanitized.ArenaId);
        Assert.Equal("arc-cannon", sanitized.StartingWeaponId);
    }

    private static ContentCatalog LoadCatalog() => ContentCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
}
