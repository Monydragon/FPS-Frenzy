using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Progression;

namespace FpsFrenzy.Content.Tests;

public sealed class RunLoadoutOverlayStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void MatchingOverlayRoundTripsTenSlotsWithoutCombatState()
    {
        Directory.CreateDirectory(_directory);
        ProfileStore profileStore = new(Path.Combine(_directory, "profile.json"));
        ProfileData profile = ProfileData.CreateDefault();
        Assert.True(profileStore.Save(profile));
        RunCheckpoint checkpoint = CreateCheckpoint();
        WeaponQuickbarLoadout quickbar = WeaponQuickbarLoadout.FromLegacy(
            new WeaponSetLoadout { RightHand = StarterWeaponReference.Issue("pulse-sidearm") },
            new WeaponSetLoadout());
        int precisionSlot = WeaponQuickbarLoadout.SlotForFamily(FpsFrenzy.Core.Data.WeaponFamily.Precision);
        quickbar.Slots[precisionSlot] = new WeaponPresetSlot
        {
            RightHand = StarterWeaponReference.Issue("longshot-rifle"),
        };
        RunLoadoutOverlayStore store = new(Path.Combine(_directory, "overlay.json"));

        Assert.True(store.Save(new RunLoadoutOverlay
        {
            ProfileGeneration = profile.Generation,
            RunSeed = checkpoint.Seed,
            CheckpointEncounterIndex = checkpoint.NextEncounterIndex,
            WeaponQuickbar = quickbar,
            ActiveWeaponSlotIndex = precisionSlot,
        }));
        RunLoadoutOverlay restored = Assert.IsType<RunLoadoutOverlay>(store.Load(profile, checkpoint));

        Assert.Equal(precisionSlot, restored.ActiveWeaponSlotIndex);
        Assert.Equal("longshot-rifle", restored.WeaponQuickbar[precisionSlot].RightHand?.WeaponBaseId);
    }

    [Fact]
    public void VersionOneOverlayMigratesToTheFamilySlotSchema()
    {
        Directory.CreateDirectory(_directory);
        ProfileStore profileStore = new(Path.Combine(_directory, "profile.json"));
        ProfileData profile = ProfileData.CreateDefault();
        Assert.True(profileStore.Save(profile));
        RunCheckpoint checkpoint = CreateCheckpoint();
        string path = Path.Combine(_directory, "overlay.json");
        RunLoadoutOverlayStore store = new(path);
        Assert.True(store.Save(new RunLoadoutOverlay
        {
            ProfileGeneration = profile.Generation,
            RunSeed = checkpoint.Seed,
            CheckpointEncounterIndex = checkpoint.NextEncounterIndex,
            ActiveWeaponSlotIndex = 0,
        }));
        File.WriteAllText(path, File.ReadAllText(path).Replace(
            $"\"SchemaVersion\": {RunLoadoutOverlay.CurrentSchemaVersion}", "\"SchemaVersion\": 1",
            StringComparison.Ordinal));

        RunLoadoutOverlay migrated = Assert.IsType<RunLoadoutOverlay>(store.Load(profile, checkpoint));

        Assert.Equal(RunLoadoutOverlay.CurrentSchemaVersion, migrated.SchemaVersion);
    }

    [Fact]
    public void OverlayFromAnotherRunOrUnknownPersistentItemIsRejected()
    {
        Directory.CreateDirectory(_directory);
        ProfileStore profileStore = new(Path.Combine(_directory, "profile.json"));
        ProfileData profile = ProfileData.CreateDefault();
        Assert.True(profileStore.Save(profile));
        RunCheckpoint checkpoint = CreateCheckpoint();
        WeaponQuickbarLoadout quickbar = new();
        quickbar.Slots[0] = new WeaponPresetSlot
        {
            RightHand = new StarterWeaponReference
            {
                WeaponBaseId = "pulse-sidearm",
                ItemInstanceId = "missing-item",
            },
        };
        RunLoadoutOverlayStore store = new(Path.Combine(_directory, "overlay.json"));
        Assert.True(store.Save(new RunLoadoutOverlay
        {
            ProfileGeneration = profile.Generation,
            RunSeed = checkpoint.Seed,
            CheckpointEncounterIndex = checkpoint.NextEncounterIndex,
            WeaponQuickbar = quickbar,
        }));

        Assert.Null(store.Load(profile, checkpoint));
        Assert.Null(store.Load(profile, checkpoint with { Seed = checkpoint.Seed + 1 }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static RunCheckpoint CreateCheckpoint() => new()
    {
        Seed = 1337,
        ArenaId = "orbital-depot",
        StartingWeaponId = "pulse-sidearm",
        NextEncounterIndex = 3,
    };
}
