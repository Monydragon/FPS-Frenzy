using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace FpsFrenzy.Kni.Progression;

public sealed record ProfileData
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public HashSet<string> UnlockedUpgradeIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UnlockedStartingWeaponIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> CompletedChallengeIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string SelectedStartingWeaponId { get; set; } = "pulse-sidearm";
    public int LifetimeKills { get; set; }
    public int CloseRangeKills { get; set; }
    public int LongRangeKills { get; set; }
    public int RunsStarted { get; set; }
    public int RunsWon { get; set; }
    public bool TutorialSeen { get; set; }
    public RunRecord? BestUnassistedRun { get; set; }
    public RunRecord? MostRecentRun { get; set; }

    public static ProfileData CreateDefault()
    {
        ProfileData profile = new();
        profile.UnlockedStartingWeaponIds.Add("pulse-sidearm");
        profile.SelectedStartingWeaponId = "pulse-sidearm";
        foreach (string upgradeId in InitialUpgradeIds)
        {
            profile.UnlockedUpgradeIds.Add(upgradeId);
        }

        return profile;
    }

    public static IReadOnlyList<string> InitialUpgradeIds { get; } =
    [
        "pulse-capacitor",
        "burst-synchronizer",
        "tight-choke",
        "beam-heat-sink",
        "plasma-payload",
        "arc-relay",
        "calibrated-cells",
        "expanded-stores",
        "field-loader",
        "reinforced-shell",
        "salvage-repair",
        "magnetic-salvage",
    ];

    public bool UnlockStartingWeapon(string weaponId) =>
        !string.IsNullOrWhiteSpace(weaponId) && UnlockedStartingWeaponIds.Add(weaponId);

    public bool CompleteChallenge(string challengeId, string upgradeId)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(upgradeId))
        {
            return false;
        }

        bool newlyCompleted = CompletedChallengeIds.Add(challengeId);
        UnlockedUpgradeIds.Add(upgradeId);
        return newlyCompleted;
    }

    public void RecordRun(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        MostRecentRun = record;
        LifetimeKills += Math.Max(0, record.Kills);
        if (record.Victory)
        {
            RunsWon++;
        }

        if (!record.GodModeUsed &&
            (BestUnassistedRun is null || record.Score > BestUnassistedRun.Score))
        {
            BestUnassistedRun = record;
        }
    }
}

public sealed record RunRecord
{
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required int Seed { get; init; }
    public required bool Victory { get; init; }
    public required bool GodModeUsed { get; init; }
    public required int Score { get; init; }
    public required int Kills { get; init; }
    public required int SectorsCompleted { get; init; }
    public required float ElapsedSeconds { get; init; }
    public required float DamageTaken { get; init; }
    public List<string> UpgradeIds { get; init; } = [];
    public List<string> NewlyUnlockedIds { get; init; } = [];
}

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public ProfileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public static ProfileStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FPSFrenzy",
        "profile-v1.json"));

    public ProfileData Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return ProfileData.CreateDefault();
            }

            ProfileData? profile = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(_path), SerializerOptions);
            if (!IsValid(profile))
            {
                return ProfileData.CreateDefault();
            }

            EnsureBaselineUnlocks(profile);
            return profile;
        }
        catch (IOException)
        {
            return ProfileData.CreateDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return ProfileData.CreateDefault();
        }
        catch (JsonException)
        {
            return ProfileData.CreateDefault();
        }
    }

    public bool Save(ProfileData profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string? directory = Path.GetDirectoryName(_path);
        string temporaryPath = _path + ".tmp";
        try
        {
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profile, SerializerOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            TryDeleteTemporary(temporaryPath);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteTemporary(temporaryPath);
            return false;
        }
    }

    private static void EnsureBaselineUnlocks(ProfileData profile)
    {
        profile.UnlockedStartingWeaponIds.Add("pulse-sidearm");
        if (!profile.UnlockedStartingWeaponIds.Contains(profile.SelectedStartingWeaponId))
        {
            profile.SelectedStartingWeaponId = "pulse-sidearm";
        }
        foreach (string upgradeId in ProfileData.InitialUpgradeIds)
        {
            profile.UnlockedUpgradeIds.Add(upgradeId);
        }
    }

    private static bool IsValid([NotNullWhen(true)] ProfileData? profile) =>
        profile is not null &&
        profile.SchemaVersion == ProfileData.CurrentSchemaVersion &&
        profile.UnlockedUpgradeIds is not null &&
        profile.UnlockedStartingWeaponIds is not null &&
        profile.CompletedChallengeIds is not null &&
        !string.IsNullOrWhiteSpace(profile.SelectedStartingWeaponId) &&
        profile.LifetimeKills >= 0 && profile.CloseRangeKills >= 0 &&
        profile.LongRangeKills >= 0 && profile.RunsStarted >= 0 && profile.RunsWon >= 0 &&
        IsValid(profile.BestUnassistedRun) && IsValid(profile.MostRecentRun);

    private static bool IsValid(RunRecord? record) =>
        record is null ||
        (record.UpgradeIds is not null && record.NewlyUnlockedIds is not null &&
         float.IsFinite(record.ElapsedSeconds) && record.ElapsedSeconds >= 0f &&
         float.IsFinite(record.DamageTaken) && record.DamageTaken >= 0f &&
         record.Score >= 0 && record.Kills >= 0 && record.SectorsCompleted is >= 0 and <= 3);

    private static void TryDeleteTemporary(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
