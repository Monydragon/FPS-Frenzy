using FpsFrenzy.Kni.Progression;

namespace FpsFrenzy.Content.Tests;

public sealed class ProfileStoreTests
{
    [Fact]
    public void MissingProfileStartsWithChoiceUnlocksButNoPermanentPowerRecord()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profile.json");
        ProfileData profile = new ProfileStore(path).Load();

        Assert.Contains("pulse-sidearm", profile.UnlockedStartingWeaponIds);
        Assert.Equal(12, profile.UnlockedUpgradeIds.Count);
        Assert.Null(profile.BestUnassistedRun);
    }

    [Fact]
    public void AtomicRoundTripPreservesUnlocksAndRunRecords()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "profile.json");
        ProfileStore store = new(path);
        ProfileData profile = ProfileData.CreateDefault();
        profile.UnlockStartingWeapon("arc-cannon");
        profile.RecordRun(new RunRecord
        {
            CompletedAtUtc = DateTimeOffset.UnixEpoch,
            Seed = 42,
            Victory = true,
            GodModeUsed = false,
            Score = 1200,
            Kills = 30,
            SectorsCompleted = 3,
            ElapsedSeconds = 1234f,
            DamageTaken = 45f,
        });

        Assert.True(store.Save(profile));
        ProfileData loaded = store.Load();

        Assert.Contains("arc-cannon", loaded.UnlockedStartingWeaponIds);
        Assert.Equal(1200, loaded.BestUnassistedRun?.Score);
        Assert.Equal(1, loaded.RunsWon);
    }

    [Fact]
    public void CorruptOrFutureProfileFallsBackWithoutTouchingSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "profile.json");
        File.WriteAllText(path, "{ not-json");
        ProfileStore store = new(path);

        Assert.Contains("pulse-sidearm", store.Load().UnlockedStartingWeaponIds);

        File.WriteAllText(path, "{\"SchemaVersion\":999}");
        Assert.Equal(ProfileData.CurrentSchemaVersion, store.Load().SchemaVersion);

        File.WriteAllText(path,
            "{\"SchemaVersion\":1,\"UnlockedUpgradeIds\":null," +
            "\"UnlockedStartingWeaponIds\":null,\"CompletedChallengeIds\":null," +
            "\"SelectedStartingWeaponId\":\"pulse-sidearm\"}");
        Assert.Contains("pulse-sidearm", store.Load().UnlockedStartingWeaponIds);
    }

    [Fact]
    public void GodModeRunRemainsProgressionEligibleButNotUnassistedBest()
    {
        ProfileData profile = ProfileData.CreateDefault();
        profile.RecordRun(new RunRecord
        {
            CompletedAtUtc = DateTimeOffset.UnixEpoch,
            Seed = 7,
            Victory = true,
            GodModeUsed = true,
            Score = 9999,
            Kills = 10,
            SectorsCompleted = 3,
            ElapsedSeconds = 100f,
            DamageTaken = 0f,
        });

        Assert.Equal(1, profile.RunsWon);
        Assert.Null(profile.BestUnassistedRun);
        Assert.True(profile.MostRecentRun?.GodModeUsed);
    }
}
