using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Core.Data;
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

    [Fact]
    public void VersionThreeCheckpointMigratesLegacyHandsIntoSetA()
    {
        RunCheckpointStore store = CreateStore();
        RunCheckpoint checkpoint = new()
        {
            Seed = 19,
            ArenaId = "orbital-depot",
            StartingWeaponId = "pulse-sidearm",
            Difficulty = DifficultyMode.Normal,
            EquipmentItems =
            [
                new EquipmentInstance
                {
                    Id = "legacy-pulse",
                    WeaponBaseId = "pulse-sidearm",
                    DisplayName = "Pulse Sidearm",
                    PrimarySlot = EquipmentSlot.RightHand,
                    ItemPower = 1,
                },
            ],
            EquipmentLoadout = new EquipmentLoadout
            {
                EquippedItemIds = { [EquipmentSlot.RightHand] = "legacy-pulse" },
            },
            HandWeaponStates = new Dictionary<EquipmentSlot, WeaponCheckpointState>
            {
                [EquipmentSlot.RightHand] = new() { WeaponId = "pulse-sidearm" },
            },
        };
        Assert.True(store.Save(checkpoint));
        string path = Path.Combine(_directory, "checkpoint.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace(
            $"\"SchemaVersion\": {RunCheckpoint.CurrentSchemaVersion}", "\"SchemaVersion\": 3",
            StringComparison.Ordinal));

        RunCheckpoint migrated = Assert.IsType<RunCheckpoint>(store.Load());

        Assert.Equal(RunCheckpoint.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal("pulse-sidearm", migrated.WeaponSetA.RightHand?.WeaponBaseId);
        Assert.Null(migrated.WeaponSetB.RightHand);
        Assert.Equal(0, migrated.ActiveWeaponSetIndex);
        Assert.Equal("pulse-sidearm", migrated.WeaponQuickbar[0].RightHand?.WeaponBaseId);
        Assert.True(migrated.WeaponQuickbar[1].IsEmpty);
    }

    [Fact]
    public void VersionFiveCheckpointMigratesPendingProgressionDefaults()
    {
        RunCheckpointStore store = CreateStore();
        RunCheckpoint checkpoint = new()
        {
            Seed = 20,
            ArenaId = "orbital-depot",
            StartingWeaponId = "pulse-sidearm",
        };
        Assert.True(store.Save(checkpoint));
        string path = Path.Combine(_directory, "checkpoint.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace(
            $"\"SchemaVersion\": {RunCheckpoint.CurrentSchemaVersion}", "\"SchemaVersion\": 5",
            StringComparison.Ordinal));

        RunCheckpoint migrated = Assert.IsType<RunCheckpoint>(store.Load());

        Assert.Equal(RunCheckpoint.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(CraftingMaterialBundle.Zero, migrated.PendingProgression.DismantledMaterials);
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
