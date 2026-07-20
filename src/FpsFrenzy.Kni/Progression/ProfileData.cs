using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Kni.Progression;

public sealed record ProfileData
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Generation { get; set; }
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
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int UnspentTalentPoints { get; set; }
    public int Salvage { get; set; }
    public CraftingWallet Materials { get; init; } = new();
    public Dictionary<string, int> TalentRanks { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public WeaponProficiencyState Proficiencies { get; init; } = new();
    public AbilityMasteryState AbilityMastery { get; init; } = new();
    public ThreatTier HighestUnlockedThreatTier { get; set; } = ThreatTier.TierI;
    public ThreatTier SelectedThreatTier { get; set; } = ThreatTier.TierI;
    public DifficultyMode SelectedDifficulty { get; set; } = DifficultyMode.Normal;
    public EquipmentLoadout EquipmentLoadout { get; init; } = new();
    public WeaponSetLoadout StarterWeaponSetA { get; set; } = new();
    public WeaponSetLoadout StarterWeaponSetB { get; set; } = new();
    public WeaponQuickbarLoadout StarterWeaponQuickbar { get; set; } = new();
    public HashSet<string> CommittedRewardIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonIgnore]
    public List<EquipmentInstance> Stash { get; set; } = [];

    public static ProfileData CreateDefault()
    {
        ProfileData profile = new();
        profile.UnlockedStartingWeaponIds.Add("pulse-sidearm");
        profile.SelectedStartingWeaponId = "pulse-sidearm";
        foreach (string upgradeId in InitialUpgradeIds)
        {
            profile.UnlockedUpgradeIds.Add(upgradeId);
        }

        AddStarterEquipment(profile);
        profile.StarterWeaponSetA = new WeaponSetLoadout
        {
            RightHand = StarterWeaponReference.Issue("pulse-sidearm"),
            LeftHand = StarterWeaponReference.Issue("ion-sprayer"),
        };
        profile.StarterWeaponSetB = new WeaponSetLoadout
        {
            RightHand = StarterWeaponReference.Issue("longshot-rifle"),
        };
        profile.StarterWeaponQuickbar = WeaponQuickbarLoadout.FromLegacy(
            profile.StarterWeaponSetA, profile.StarterWeaponSetB);

        return profile;
    }

    public PlayerProgressionState CreateProgressionState() => new()
    {
        Level = Level,
        Experience = Experience,
        UnspentTalentPoints = UnspentTalentPoints,
        TalentRanks = new Dictionary<string, int>(TalentRanks, StringComparer.OrdinalIgnoreCase),
        Proficiencies = Proficiencies,
        AbilityMastery = AbilityMastery,
        HighestUnlockedThreatTier = HighestUnlockedThreatTier,
        Materials = Materials.Clone(),
        Stash = [.. Stash],
        Loadout = EquipmentLoadout.Clone(),
        CommittedRewardIds = new HashSet<string>(CommittedRewardIds, StringComparer.OrdinalIgnoreCase),
    };

    public void ApplyProgressionState(PlayerProgressionState progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        Level = progression.Level;
        Experience = progression.Experience;
        UnspentTalentPoints = progression.UnspentTalentPoints;
        Replace(TalentRanks, progression.TalentRanks);
        CopyProficiencies(progression.Proficiencies, Proficiencies);
        CopyAbilities(progression.AbilityMastery, AbilityMastery);
        HighestUnlockedThreatTier = progression.HighestUnlockedThreatTier;
        Materials.Scrap = progression.Materials.Scrap;
        Materials.Components = progression.Materials.Components;
        Materials.Cores = progression.Materials.Cores;
        Replace(EquipmentLoadout.EquippedItemIds, progression.Loadout.EquippedItemIds);
        Replace(CommittedRewardIds, progression.CommittedRewardIds);
        Stash = [.. progression.Stash];
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

    private static void AddStarterEquipment(ProfileData profile)
    {
        EquipmentInstance pulse = new()
        {
            Id = "starter-pulse-sidearm",
            WeaponBaseId = "pulse-sidearm",
            DisplayName = "Pulse Sidearm",
            PrimarySlot = EquipmentSlot.RightHand,
            Rarity = ItemRarity.Common,
            ItemPower = 1,
            IsLocked = true,
        };
        profile.Stash.Add(pulse);
        profile.EquipmentLoadout.EquippedItemIds[EquipmentSlot.RightHand] = pulse.Id;
        foreach (EquipmentSlot slot in new[]
            { EquipmentSlot.Head, EquipmentSlot.Chest, EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet })
        {
            string slotName = slot.ToString().ToLowerInvariant();
            EquipmentInstance armor = new()
            {
                Id = $"starter-scout-{slotName}",
                EquipmentBaseId = $"scout-{slotName}",
                DisplayName = $"Scout {slot}",
                PrimarySlot = slot,
                Rarity = ItemRarity.Common,
                ItemPower = 1,
                IsLocked = true,
            };
            profile.Stash.Add(armor);
            profile.EquipmentLoadout.EquippedItemIds[slot] = armor.Id;
        }
    }

    private static void Replace<TKey, TValue>(IDictionary<TKey, TValue> target, IEnumerable<KeyValuePair<TKey, TValue>> source)
        where TKey : notnull
    {
        target.Clear();
        foreach ((TKey key, TValue value) in source)
        {
            target[key] = value;
        }
    }

    private static void Replace(HashSet<string> target, IEnumerable<string> source)
    {
        target.Clear();
        foreach (string value in source)
        {
            target.Add(value);
        }
    }

    private static void CopyProficiencies(WeaponProficiencyState source, WeaponProficiencyState target) =>
        Replace(target.Families, source.Families);

    private static void CopyAbilities(AbilityMasteryState source, AbilityMasteryState target)
    {
        Replace(target.Abilities, source.Abilities);
        target.EquippedActiveAbilityIds.Clear();
        target.EquippedActiveAbilityIds.AddRange(source.EquippedActiveAbilityIds);
        target.EquippedPassiveAbilityIds.Clear();
        target.EquippedPassiveAbilityIds.AddRange(source.EquippedPassiveAbilityIds);
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
    public ThreatTier ThreatTier { get; init; } = ThreatTier.TierI;
    public DifficultyMode Difficulty { get; init; } = DifficultyMode.Normal;
    public int PlayerLevel { get; init; } = 1;
    public int LevelsGained { get; init; }
    public int ExperienceGained { get; init; }
    public Dictionary<WeaponFamily, int> ProficiencyExperienceGained { get; init; } = [];
    public List<string> AbilitiesMastered { get; init; } = [];
    public Dictionary<ItemRarity, int> RarityTotals { get; init; } = [];
    public int EquipmentCollected { get; init; }
    public int HighestItemPower { get; init; }
}

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly string _stashPath;

    public ProfileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _stashPath = _path + ".stash";
    }

    public static ProfileStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FPSFrenzy",
        "profile-v2.json"));

    public ProfileData Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return ProfileData.CreateDefault();
            }

            ProfileData? primary = LoadMatchingPair(_path, _stashPath);
            if (primary is not null)
            {
                EnsureBaselineUnlocks(primary);
                return primary;
            }

            ProfileData? backup = LoadMatchingPair(_path + ".bak", _stashPath + ".bak");
            if (backup is not null)
            {
                EnsureBaselineUnlocks(backup);
                return backup;
            }

            ProfileData? versionTwo = LoadVersionTwoPair(_path, _stashPath) ??
                LoadVersionTwoPair(_path + ".bak", _stashPath + ".bak");
            if (versionTwo is not null)
            {
                EnsureBaselineUnlocks(versionTwo);
                return versionTwo;
            }

            ProfileData? versionThree = LoadVersionThreePair(_path, _stashPath) ??
                LoadVersionThreePair(_path + ".bak", _stashPath + ".bak");
            if (versionThree is not null)
            {
                EnsureBaselineUnlocks(versionThree);
                return versionThree;
            }

            ProfileData? legacy = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(_path), SerializerOptions);
            return MigrateVersionOne(legacy) ?? ProfileData.CreateDefault();
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
        string temporaryProfilePath = _path + ".tmp";
        string temporaryStashPath = _stashPath + ".tmp";
        try
        {
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            profile.Generation = Math.Max(1, profile.Generation + 1);
            ProfileStashData stash = new()
            {
                Generation = profile.Generation,
                Items = [.. profile.Stash],
            };
            File.WriteAllText(temporaryProfilePath, JsonSerializer.Serialize(profile, SerializerOptions));
            File.WriteAllText(temporaryStashPath, JsonSerializer.Serialize(stash, SerializerOptions));
            BackupExisting(_path);
            BackupExisting(_stashPath);
            File.Move(temporaryStashPath, _stashPath, overwrite: true);
            File.Move(temporaryProfilePath, _path, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            TryDeleteTemporary(temporaryProfilePath);
            TryDeleteTemporary(temporaryStashPath);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteTemporary(temporaryProfilePath);
            TryDeleteTemporary(temporaryStashPath);
            return false;
        }
    }

    private static void EnsureBaselineUnlocks(ProfileData profile)
    {
        profile.UnlockedStartingWeaponIds.Add("pulse-sidearm");
        if (string.IsNullOrWhiteSpace(profile.SelectedStartingWeaponId))
        {
            profile.SelectedStartingWeaponId = "pulse-sidearm";
        }
        foreach (string upgradeId in ProfileData.InitialUpgradeIds)
        {
            profile.UnlockedUpgradeIds.Add(upgradeId);
        }

        if (profile.StarterWeaponQuickbar.Slots.All(slot => slot.IsEmpty))
        {
            profile.StarterWeaponQuickbar = WeaponQuickbarLoadout.FromLegacy(
                profile.StarterWeaponSetA, profile.StarterWeaponSetB);
        }

        if (profile.Stash.Count == 0)
        {
            ProfileData starter = ProfileData.CreateDefault();
            profile.Stash = starter.Stash;
            profile.EquipmentLoadout.EquippedItemIds.Clear();
            foreach ((EquipmentSlot slot, string id) in starter.EquipmentLoadout.EquippedItemIds)
            {
                profile.EquipmentLoadout.EquippedItemIds[slot] = id;
            }
        }
    }

    private static bool IsValid([NotNullWhen(true)] ProfileData? profile) =>
        profile is not null &&
        profile.SchemaVersion == ProfileData.CurrentSchemaVersion &&
        profile.Generation > 0 &&
        profile.UnlockedUpgradeIds is not null &&
        profile.UnlockedStartingWeaponIds is not null &&
        profile.CompletedChallengeIds is not null &&
        !string.IsNullOrWhiteSpace(profile.SelectedStartingWeaponId) &&
        profile.LifetimeKills >= 0 && profile.CloseRangeKills >= 0 &&
        profile.LongRangeKills >= 0 && profile.RunsStarted >= 0 && profile.RunsWon >= 0 &&
        profile.Level is >= 1 and <= RpgProgressionMath.MaximumPlayerLevel && profile.Experience >= 0 &&
        profile.UnspentTalentPoints >= 0 && profile.TalentRanks is not null &&
        profile.Salvage >= 0 && profile.Materials is not null &&
        profile.Materials.Scrap >= 0 && profile.Materials.Components >= 0 && profile.Materials.Cores >= 0 &&
        profile.Proficiencies is not null && profile.AbilityMastery is not null &&
        profile.EquipmentLoadout is not null && profile.StarterWeaponSetA is not null &&
        profile.StarterWeaponSetB is not null && profile.StarterWeaponQuickbar is not null &&
        profile.StarterWeaponQuickbar.Slots is not null &&
        profile.StarterWeaponQuickbar.Slots.Count == WeaponQuickbarLoadout.SlotCount &&
        profile.CommittedRewardIds is not null &&
        profile.HighestUnlockedThreatTier is >= ThreatTier.TierI and <= ThreatTier.TierX &&
        profile.SelectedThreatTier is >= ThreatTier.TierI and <= ThreatTier.TierX &&
        profile.SelectedThreatTier <= profile.HighestUnlockedThreatTier &&
        DifficultyCatalog.All.Any(definition => definition.Mode ==
            DifficultyCatalog.Normalize(profile.SelectedDifficulty)) &&
        IsValid(profile.BestUnassistedRun) && IsValid(profile.MostRecentRun);

    private static bool IsValid(RunRecord? record) =>
        record is null ||
        (record.UpgradeIds is not null && record.NewlyUnlockedIds is not null &&
         record.ProficiencyExperienceGained is not null && record.AbilitiesMastered is not null &&
         record.RarityTotals is not null &&
         float.IsFinite(record.ElapsedSeconds) && record.ElapsedSeconds >= 0f &&
         float.IsFinite(record.DamageTaken) && record.DamageTaken >= 0f &&
         record.Score >= 0 && record.Kills >= 0 && record.SectorsCompleted is >= 0 and <= 3 &&
         record.ThreatTier is >= ThreatTier.TierI and <= ThreatTier.TierX &&
         DifficultyCatalog.All.Any(definition => definition.Mode ==
             DifficultyCatalog.Normalize(record.Difficulty)) &&
         record.PlayerLevel is >= 1 and <= RpgProgressionMath.MaximumPlayerLevel &&
         record.LevelsGained >= 0 && record.ExperienceGained >= 0 &&
         record.EquipmentCollected >= 0 && record.HighestItemPower is >= 0 and <= 100 &&
         record.ProficiencyExperienceGained.All(pair => pair.Value >= 0) &&
         record.RarityTotals.All(pair => pair.Value >= 0));

    private static ProfileData? LoadMatchingPair(string profilePath, string stashPath)
    {
        if (!File.Exists(profilePath) || !File.Exists(stashPath))
        {
            return null;
        }

        ProfileData? profile = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(profilePath), SerializerOptions);
        ProfileStashData? stash = JsonSerializer.Deserialize<ProfileStashData>(File.ReadAllText(stashPath),
            SerializerOptions);
        if (!IsValid(profile) || stash is null || stash.SchemaVersion != ProfileData.CurrentSchemaVersion ||
            stash.Generation != profile.Generation || stash.Items is null ||
            stash.Items.Any(item => !IsValid(item)) ||
            stash.Items.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != stash.Items.Count)
        {
            return null;
        }

        profile.Stash = stash.Items;
        HashSet<string> itemIds = profile.Stash.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return profile.EquipmentLoadout.EquippedItemIds.Values.All(itemIds.Contains) ? profile : null;
    }

    private static ProfileData? LoadVersionTwoPair(string profilePath, string stashPath)
    {
        if (!File.Exists(profilePath) || !File.Exists(stashPath))
        {
            return null;
        }

        ProfileData? legacy = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(profilePath), SerializerOptions);
        ProfileStashData? stash = JsonSerializer.Deserialize<ProfileStashData>(File.ReadAllText(stashPath),
            SerializerOptions);
        if (legacy is null || legacy.SchemaVersion != 2 || stash is null || stash.SchemaVersion != 2 ||
            legacy.Generation <= 0 || legacy.Generation != stash.Generation || stash.Items is null ||
            stash.Items.Any(item => !IsValid(item)) ||
            stash.Items.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != stash.Items.Count)
        {
            return null;
        }

        legacy.Stash = stash.Items;
        WeaponSetLoadout setA = CreateMigratedSetA(legacy);
        ProfileData migrated = legacy with
        {
            SchemaVersion = ProfileData.CurrentSchemaVersion,
            SelectedDifficulty = DifficultyCatalog.Normalize(legacy.SelectedDifficulty),
            StarterWeaponSetA = setA,
            StarterWeaponSetB = new WeaponSetLoadout(),
            StarterWeaponQuickbar = WeaponQuickbarLoadout.FromLegacy(setA, new WeaponSetLoadout()),
        };
        return IsValid(migrated) ? migrated : null;
    }

    private static ProfileData? LoadVersionThreePair(string profilePath, string stashPath)
    {
        if (!File.Exists(profilePath) || !File.Exists(stashPath))
        {
            return null;
        }

        ProfileData? legacy = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(profilePath), SerializerOptions);
        ProfileStashData? stash = JsonSerializer.Deserialize<ProfileStashData>(File.ReadAllText(stashPath),
            SerializerOptions);
        if (legacy is null || legacy.SchemaVersion != 3 || stash is null || stash.SchemaVersion != 3 ||
            legacy.Generation <= 0 || legacy.Generation != stash.Generation || stash.Items is null ||
            stash.Items.Any(item => !IsValid(item)) ||
            stash.Items.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != stash.Items.Count)
        {
            return null;
        }

        legacy.Stash = stash.Items;
        CraftingWallet materials = new() { Scrap = Math.Max(0, legacy.Salvage) };
        ProfileData migrated = legacy with
        {
            SchemaVersion = ProfileData.CurrentSchemaVersion,
            SelectedDifficulty = DifficultyCatalog.Normalize(legacy.SelectedDifficulty),
            Materials = materials,
            StarterWeaponQuickbar = WeaponQuickbarLoadout.FromLegacy(
                legacy.StarterWeaponSetA, legacy.StarterWeaponSetB),
        };
        return IsValid(migrated) ? migrated : null;
    }

    private static WeaponSetLoadout CreateMigratedSetA(ProfileData profile)
    {
        StarterWeaponReference? ReferenceFor(EquipmentSlot slot)
        {
            string? itemId = profile.EquipmentLoadout[slot];
            EquipmentInstance? item = profile.Stash.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, itemId, StringComparison.OrdinalIgnoreCase));
            return item?.WeaponBaseId is string weaponId
                ? new StarterWeaponReference { WeaponBaseId = weaponId, ItemInstanceId = item.Id }
                : null;
        }

        return new WeaponSetLoadout
        {
            RightHand = ReferenceFor(EquipmentSlot.RightHand),
            LeftHand = ReferenceFor(EquipmentSlot.LeftHand),
        };
    }

    private static ProfileData? MigrateVersionOne(ProfileData? legacy)
    {
        if (legacy is null || legacy.SchemaVersion != 1 || legacy.UnlockedUpgradeIds is null ||
            legacy.UnlockedStartingWeaponIds is null || legacy.CompletedChallengeIds is null ||
            string.IsNullOrWhiteSpace(legacy.SelectedStartingWeaponId) || legacy.LifetimeKills < 0 ||
            legacy.CloseRangeKills < 0 || legacy.LongRangeKills < 0 || legacy.RunsStarted < 0 || legacy.RunsWon < 0)
        {
            return null;
        }

        ProfileData migrated = ProfileData.CreateDefault();
        migrated.UnlockedUpgradeIds.UnionWith(legacy.UnlockedUpgradeIds);
        migrated.UnlockedStartingWeaponIds.UnionWith(legacy.UnlockedStartingWeaponIds);
        migrated.CompletedChallengeIds.UnionWith(legacy.CompletedChallengeIds);
        migrated.SelectedStartingWeaponId = legacy.UnlockedStartingWeaponIds.Contains(legacy.SelectedStartingWeaponId)
            ? legacy.SelectedStartingWeaponId
            : "pulse-sidearm";
        migrated.LifetimeKills = legacy.LifetimeKills;
        migrated.CloseRangeKills = legacy.CloseRangeKills;
        migrated.LongRangeKills = legacy.LongRangeKills;
        migrated.RunsStarted = legacy.RunsStarted;
        migrated.RunsWon = legacy.RunsWon;
        migrated.TutorialSeen = legacy.TutorialSeen;
        migrated.BestUnassistedRun = legacy.BestUnassistedRun;
        migrated.MostRecentRun = legacy.MostRecentRun;
        return migrated;
    }

    private static bool IsValid(EquipmentInstance item) =>
        item is not null && !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.DisplayName) &&
        item.ItemPower is >= 1 and <= 100 && item.Affixes is not null &&
        (item.WeaponBaseId is not null ^ item.EquipmentBaseId is not null);

    private static void BackupExisting(string path)
    {
        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }
    }

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

    private sealed record ProfileStashData
    {
        public int SchemaVersion { get; init; } = ProfileData.CurrentSchemaVersion;
        public long Generation { get; init; }
        public List<EquipmentInstance> Items { get; init; } = [];
    }
}
