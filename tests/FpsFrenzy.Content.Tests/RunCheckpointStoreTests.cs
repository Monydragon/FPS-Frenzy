using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Progression;

namespace FpsFrenzy.Content.Tests;

public sealed class RunCheckpointStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "fps-frenzy-checkpoint-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingCheckpointLoadsAsNull()
    {
        Assert.Null(CreateStore().Load());
    }

    [Fact]
    public void CheckpointRoundTripsAndCanBeCleared()
    {
        RunCheckpointStore store = CreateStore();
        RunCheckpoint checkpoint = new()
        {
            Seed = 7421,
            ArenaId = "orbital-depot",
            NextEncounterIndex = 2,
            StartingWeaponId = "arc-cannon",
            IsFirstRun = true,
            GodModeUsed = true,
            OwnedUpgradeIds = ["calibrated-cells", "field-loader"],
            NewlyUnlockedIds = ["arc-cannon"],
            ProfileUnlockBaselineIds = ["pulse-sidearm", "calibrated-cells"],
        };

        Assert.True(store.Save(checkpoint));
        RunCheckpoint restored = Assert.IsType<RunCheckpoint>(store.Load());
        Assert.Equal(checkpoint.Seed, restored.Seed);
        Assert.Equal(checkpoint.NextEncounterIndex, restored.NextEncounterIndex);
        Assert.True(restored.IsFirstRun);
        Assert.Equal(checkpoint.OwnedUpgradeIds, restored.OwnedUpgradeIds);
        Assert.Equal(checkpoint.NewlyUnlockedIds, restored.NewlyUnlockedIds);
        Assert.Equal(checkpoint.ProfileUnlockBaselineIds, restored.ProfileUnlockBaselineIds);
        Assert.True(store.Clear());
        Assert.Null(store.Load());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"SchemaVersion\":999,\"ArenaId\":\"orbital-depot\",\"StartingWeaponId\":\"pulse-sidearm\"}")]
    [InlineData("{\"SchemaVersion\":2,\"ArenaId\":\"orbital-depot\",\"StartingWeaponId\":\"pulse-sidearm\",\"OwnedUpgradeIds\":null}")]
    [InlineData("{\"SchemaVersion\":2,\"ArenaId\":\"orbital-depot\",\"StartingWeaponId\":\"pulse-sidearm\",\"NextEncounterIndex\":0,\"OwnedUpgradeIds\":[\"calibrated-cells\"]}")]
    [InlineData("{\"SchemaVersion\":2,\"ArenaId\":\"orbital-depot\",\"StartingWeaponId\":\"pulse-sidearm\",\"NextEncounterIndex\":2,\"OwnedUpgradeIds\":[\"calibrated-cells\",\"calibrated-cells\"]}")]
    public void CorruptOrUnsupportedCheckpointFallsBackSafely(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "checkpoint.json"), content);

        Assert.Null(CreateStore().Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private RunCheckpointStore CreateStore() => new(Path.Combine(_directory, "checkpoint.json"));
}
