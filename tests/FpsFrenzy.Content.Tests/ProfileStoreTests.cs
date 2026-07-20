using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;
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
        Assert.Equal(1, profile.Level);
        Assert.Equal(ThreatTier.TierI, profile.HighestUnlockedThreatTier);
        Assert.Contains(profile.Stash, item => item.WeaponBaseId == "pulse-sidearm" && item.ItemPower == 1);
    }

    [Fact]
    public void MismatchedPrimaryGenerationRecoversNewestMatchingBackup()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "profile.json");
        ProfileStore store = new(path);
        ProfileData profile = ProfileData.CreateDefault();
        profile.Level = 4;
        Assert.True(store.Save(profile));
        profile.Level = 5;
        Assert.True(store.Save(profile));

        File.WriteAllText(path + ".stash", File.ReadAllText(path + ".stash").Replace(
            $"\"Generation\": {profile.Generation}", "\"Generation\": 9999", StringComparison.Ordinal));

        ProfileData recovered = store.Load();
        Assert.Equal(4, recovered.Level);
    }

    [Fact]
    public void VersionOneProfileMigratesUnlockHistoryAndReceivesStarterGear()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "profile.json");
        File.WriteAllText(path,
            "{\"SchemaVersion\":1,\"UnlockedUpgradeIds\":[\"calibrated-cells\"]," +
            "\"UnlockedStartingWeaponIds\":[\"pulse-sidearm\",\"arc-cannon\"]," +
            "\"CompletedChallengeIds\":[],\"SelectedStartingWeaponId\":\"arc-cannon\"}");

        ProfileData migrated = new ProfileStore(path).Load();
        Assert.Equal(ProfileData.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal("arc-cannon", migrated.SelectedStartingWeaponId);
        Assert.Contains(migrated.Stash, item => item.Id == "starter-pulse-sidearm");
    }

    [Fact]
    public void VersionTwoProfileMigratesCurrentEquipmentIntoSetAAndLeavesSetBEmpty()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "profile.json");
        ProfileStore store = new(path);
        ProfileData profile = ProfileData.CreateDefault() with
        {
            StarterWeaponSetB = new WeaponSetLoadout
            {
                RightHand = StarterWeaponReference.Issue("longshot-rifle"),
            },
        };
        Assert.True(store.Save(profile));
        File.WriteAllText(path, File.ReadAllText(path).Replace(
            "\"SchemaVersion\": 4", "\"SchemaVersion\": 2", StringComparison.Ordinal));
        File.WriteAllText(path + ".stash", File.ReadAllText(path + ".stash").Replace(
            "\"SchemaVersion\": 4", "\"SchemaVersion\": 2", StringComparison.Ordinal));

        ProfileData migrated = store.Load();

        Assert.Equal(ProfileData.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(DifficultyMode.Normal, migrated.SelectedDifficulty);
        Assert.NotNull(migrated.StarterWeaponSetA.RightHand);
        Assert.Null(migrated.StarterWeaponSetB.RightHand);
        Assert.Null(migrated.StarterWeaponSetB.LeftHand);
        Assert.NotNull(migrated.StarterWeaponQuickbar[0].RightHand);
        Assert.True(migrated.StarterWeaponQuickbar[1].IsEmpty);
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
