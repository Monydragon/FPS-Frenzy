using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Progression;

namespace FpsFrenzy.Content.Tests;

public sealed class AdventureCheckpointStoreTests
{
    [Fact]
    public void RoundTripPreservesCommittedAdventureState()
    {
        using TemporaryDirectory temporary = new();
        AdventureCheckpointStore store = new(Path.Combine(temporary.Path, "adventure-checkpoint-v1.json"));
        AdventureCheckpoint checkpoint = CreateCheckpoint() with
        {
            NextStageIndex = 2,
            StoryPosition = 3,
            BoonIds = ["boon-a", "boon-b"],
            FloorsCompleted = 2,
            LoreFound = 2,
            SecretsFound = 1,
        };

        Assert.True(store.Save(checkpoint));
        AdventureCheckpoint loaded = Assert.IsType<AdventureCheckpoint>(store.Load());

        Assert.Equal(checkpoint.AdventureId, loaded.AdventureId);
        Assert.Equal(checkpoint.Seed, loaded.Seed);
        Assert.Equal(checkpoint.GeneratorVersion, loaded.GeneratorVersion);
        Assert.Equal(checkpoint.EntryLayoutHash, loaded.EntryLayoutHash);
        Assert.Equal(checkpoint.NextStageIndex, loaded.NextStageIndex);
        Assert.Equal(checkpoint.StoryPosition, loaded.StoryPosition);
        Assert.Equal(checkpoint.BoonIds, loaded.BoonIds);
        Assert.Equal(checkpoint.FloorsCompleted, loaded.FloorsCompleted);
        Assert.Equal(checkpoint.LoreFound, loaded.LoreFound);
        Assert.Equal(checkpoint.SecretsFound, loaded.SecretsFound);
        Assert.Equal(checkpoint.RunExperienceEarned, loaded.RunExperienceEarned);
        Assert.Equal(checkpoint.RunProficiencyExperience, loaded.RunProficiencyExperience);
        Assert.Equal(checkpoint.RunCollectedItemIds, loaded.RunCollectedItemIds);
        Assert.Equal(checkpoint.RunRarityTotals, loaded.RunRarityTotals);
        Assert.True(store.Exists);
    }

    [Fact]
    public void CorruptPrimaryRecoversLastGoodBackup()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "adventure-checkpoint-v1.json");
        AdventureCheckpointStore store = new(path);
        AdventureCheckpoint first = CreateCheckpoint();
        AdventureCheckpoint second = first with { NextStageIndex = 1, BoonIds = ["boon-a"], FloorsCompleted = 1 };
        Assert.True(store.Save(first));
        Assert.True(store.Save(second));

        File.WriteAllText(path, "{not-json");
        AdventureCheckpoint recovered = Assert.IsType<AdventureCheckpoint>(store.Load());

        Assert.Equal(first.AdventureId, recovered.AdventureId);
        Assert.Equal(first.Seed, recovered.Seed);
        Assert.Equal(first.NextStageIndex, recovered.NextStageIndex);
        Assert.Equal(first.BoonIds, recovered.BoonIds);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 5, 1)]
    [InlineData(1, 0, 999)]
    public void InvalidOrUnsupportedCheckpointsAreRejected(int seed, int stage, int schema)
    {
        using TemporaryDirectory temporary = new();
        AdventureCheckpointStore store = new(Path.Combine(temporary.Path, "adventure-checkpoint-v1.json"));
        AdventureCheckpoint checkpoint = CreateCheckpoint() with
        {
            Seed = seed,
            NextStageIndex = stage,
            SchemaVersion = schema,
        };

        Assert.False(store.Save(checkpoint));
        Assert.Null(store.Load());
    }

    [Fact]
    public void ClearingAdventureCheckpointDoesNotTouchArenaCheckpoint()
    {
        using TemporaryDirectory temporary = new();
        string adventurePath = Path.Combine(temporary.Path, "adventure-checkpoint-v1.json");
        string arenaPath = Path.Combine(temporary.Path, "run-checkpoint-v4.json");
        File.WriteAllText(arenaPath, "arena-data");
        AdventureCheckpointStore store = new(adventurePath);
        Assert.True(store.Save(CreateCheckpoint()));

        Assert.True(store.Clear());

        Assert.False(store.Exists);
        Assert.True(File.Exists(arenaPath));
    }

    private static AdventureCheckpoint CreateCheckpoint() => new()
    {
        AdventureId = "null-signal",
        Seed = 424242,
        GeneratorVersion = "pcg-v2",
        EntryLayoutHash = "f1ce047699b5acb1",
        Difficulty = DifficultyMode.Hard,
        ThreatTier = ThreatTier.TierIII,
        RunExperienceEarned = 450,
        RunLevelsGained = 1,
        RunProficiencyExperience = new Dictionary<WeaponFamily, int> { [WeaponFamily.Pulse] = 90 },
        RunCollectedItemIds = ["cache-item"],
        RunRarityTotals = new Dictionary<ItemRarity, int> { [ItemRarity.Rare] = 1 },
        RunHighestItemPower = 12,
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fpsfrenzy-adventure-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
