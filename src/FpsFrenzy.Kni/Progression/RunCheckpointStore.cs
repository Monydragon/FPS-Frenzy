using System.Text.Json;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Kni.Progression;

public sealed class RunCheckpointStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly string? _legacyPath;

    public RunCheckpointStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    private RunCheckpointStore(string path, string legacyPath) : this(path)
    {
        _legacyPath = Path.GetFullPath(legacyPath);
    }

    public static RunCheckpointStore CreateDefault()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FPSFrenzy");
        return new RunCheckpointStore(
            Path.Combine(directory, "run-checkpoint-v4.json"),
            Path.Combine(directory, "run-checkpoint-v3.json"));
    }

    public bool Exists => File.Exists(_path) || (_legacyPath is not null && File.Exists(_legacyPath));

    public RunCheckpoint? Load()
    {
        try
        {
            string? loadPath = File.Exists(_path)
                ? _path
                : _legacyPath is not null && File.Exists(_legacyPath) ? _legacyPath : null;
            if (loadPath is null)
            {
                return null;
            }

            RunCheckpoint? checkpoint = JsonSerializer.Deserialize<RunCheckpoint>(
                File.ReadAllText(loadPath), SerializerOptions);
            if (checkpoint?.SchemaVersion == 3)
            {
                checkpoint = MigrateVersionThree(checkpoint);
            }
            else if (checkpoint?.SchemaVersion == 4)
            {
                checkpoint = MigrateVersionFour(checkpoint);
            }
            else if (checkpoint?.SchemaVersion == 5)
            {
                checkpoint = MigrateVersionFive(checkpoint);
            }
            return IsValid(checkpoint) ? checkpoint : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool Save(RunCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        string? directory = Path.GetDirectoryName(_path);
        string temporaryPath = _path + ".tmp";
        try
        {
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(checkpoint, SerializerOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            TryDelete(temporaryPath);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            return false;
        }
    }

    public bool Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            TryDelete(_path + ".tmp");
            if (_legacyPath is not null)
            {
                TryDelete(_legacyPath);
                TryDelete(_legacyPath + ".tmp");
            }
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsValid(RunCheckpoint? checkpoint)
    {
        if (checkpoint is null || checkpoint.SchemaVersion != RunCheckpoint.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(checkpoint.ArenaId) ||
            string.IsNullOrWhiteSpace(checkpoint.StartingWeaponId) ||
            checkpoint.NextEncounterIndex is < 0 or > RunDirector.EncounterCount ||
            checkpoint.OwnedUpgradeIds is null || checkpoint.CollectedWeaponIds is null ||
            checkpoint.WeaponStates is null || checkpoint.ActiveArmoryWeaponIds is null ||
            checkpoint.NewlyUnlockedIds is null || checkpoint.ProfileUnlockBaselineIds is null ||
            !IsFiniteNonNegative(checkpoint.ElapsedRunSeconds) ||
            !IsFiniteNonNegative(checkpoint.DamageTaken) || checkpoint.Score < 0 || checkpoint.Kills < 0 ||
            checkpoint.CloseRangeKills < 0 || checkpoint.LongRangeKills < 0 ||
            checkpoint.SectorsCompleted is < 0 or > 3 ||
            checkpoint.CompletedPurgeEncounters < 0 || checkpoint.CompletedRelayEncounters < 0 ||
            checkpoint.CompletedEliteEncounters < 0 || !float.IsFinite(checkpoint.LastCompletedEncounterMetric) ||
            checkpoint.ThreatTier is < FpsFrenzy.Core.Data.ThreatTier.TierI or
                > FpsFrenzy.Core.Data.ThreatTier.TierX ||
            !FpsFrenzy.Core.Data.DifficultyCatalog.All.Any(definition => definition.Mode ==
                FpsFrenzy.Core.Data.DifficultyCatalog.Normalize(checkpoint.Difficulty)) ||
            checkpoint.EquipmentLoadout is null || checkpoint.EquipmentItems is null ||
            checkpoint.HandWeaponStates is null || checkpoint.PendingProgression is null ||
            checkpoint.WeaponSetA is null || checkpoint.WeaponSetB is null ||
            checkpoint.WeaponSetStates is null ||
            checkpoint.WeaponQuickbar is null || checkpoint.WeaponQuickbar.Slots is null ||
            checkpoint.WeaponQuickbar.Slots.Count != WeaponQuickbarLoadout.SlotCount ||
            checkpoint.ActiveWeaponSlotIndex is < 0 or >= WeaponQuickbarLoadout.SlotCount ||
            checkpoint.IssuedItemInstances is null ||
            checkpoint.RecoveryCache is null || checkpoint.AbilityCooldowns is null ||
            checkpoint.RunExperienceEarned < 0 || checkpoint.RunLevelsGained < 0 ||
            checkpoint.RunProficiencyExperience is null || checkpoint.RunAbilitiesMastered is null ||
            checkpoint.RunCollectedItemIds is null || checkpoint.RunRarityTotals is null ||
            checkpoint.RunHighestItemPower is < 0 or > 100 ||
            checkpoint.RunProficiencyExperience.Any(pair => pair.Value < 0) ||
            checkpoint.RunRarityTotals.Any(pair => pair.Value < 0) ||
            checkpoint.RunAbilitiesMastered.Any(string.IsNullOrWhiteSpace) ||
            checkpoint.RunCollectedItemIds.Any(string.IsNullOrWhiteSpace) ||
            checkpoint.AbilityCooldowns.Any(pair => string.IsNullOrWhiteSpace(pair.Key) ||
                !IsFiniteNonNegative(pair.Value)) || checkpoint.LootDropSerial < 0 ||
            checkpoint.PendingProgression.Equipment is null ||
            checkpoint.PendingProgression.ProficiencyExperience is null ||
            checkpoint.PendingProgression.AbilityPoints is null ||
            checkpoint.PendingProgression.DismantledMaterials.Scrap < 0 ||
            checkpoint.PendingProgression.DismantledMaterials.Components < 0 ||
            checkpoint.PendingProgression.DismantledMaterials.Cores < 0 ||
            checkpoint.RecoveryCache.Items is null ||
            checkpoint.OwnedUpgradeIds.Count != checkpoint.NextEncounterIndex ||
            checkpoint.OwnedUpgradeIds.Any(string.IsNullOrWhiteSpace) ||
            checkpoint.OwnedUpgradeIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                checkpoint.OwnedUpgradeIds.Count ||
            (checkpoint.PlayerHealth is float playerHealth && !IsFiniteNonNegative(playerHealth)) ||
            (checkpoint.PlayerMaximumHealth is float maximumHealth &&
                (!float.IsFinite(maximumHealth) || maximumHealth <= 0f)))
        {
            return false;
        }

        return checkpoint.WeaponStates.Concat(checkpoint.HandWeaponStates.Values)
            .Concat(checkpoint.WeaponSetStates.Values.SelectMany(state =>
                new[] { state.RightHand, state.LeftHand }.OfType<WeaponCheckpointState>())).All(IsValid) &&
            checkpoint.EquipmentItems.Concat(checkpoint.PendingProgression.Equipment)
                .Concat(checkpoint.RecoveryCache.Items).Concat(checkpoint.IssuedItemInstances).All(item =>
                    item is not null && !string.IsNullOrWhiteSpace(item.Id) &&
                    !string.IsNullOrWhiteSpace(item.DisplayName) && item.ItemPower is >= 1 and <= 100 &&
                    item.Affixes is not null && (item.WeaponBaseId is not null ^ item.EquipmentBaseId is not null)) &&
            IsValid(checkpoint.WeaponSetA) && IsValid(checkpoint.WeaponSetB) &&
            checkpoint.WeaponQuickbar.Slots.All(IsValid) &&
            checkpoint.IssuedItemInstances.All(item => item.IsRunBound);
    }

    private static bool IsValid(WeaponSetLoadout set) =>
        IsValid(set.RightHand) && IsValid(set.LeftHand);

    private static bool IsValid(WeaponPresetSlot set) =>
        set is not null && IsValid(set.RightHand) && IsValid(set.LeftHand);

    private static bool IsValid(StarterWeaponReference? reference) =>
        reference is null || !string.IsNullOrWhiteSpace(reference.WeaponBaseId);

    private static RunCheckpoint MigrateVersionThree(RunCheckpoint legacy)
    {
        StarterWeaponReference? ReferenceFor(FpsFrenzy.Core.Data.EquipmentSlot slot)
        {
            string? itemId = legacy.EquipmentLoadout[slot];
            EquipmentInstance? item = legacy.EquipmentItems.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, itemId, StringComparison.OrdinalIgnoreCase));
            return item?.WeaponBaseId is string weaponId
                ? new StarterWeaponReference { WeaponBaseId = weaponId, ItemInstanceId = item.Id }
                : null;
        }

        StarterWeaponReference? right = ReferenceFor(FpsFrenzy.Core.Data.EquipmentSlot.RightHand) ??
            StarterWeaponReference.Issue(legacy.StartingWeaponId);
        StarterWeaponReference? left = ReferenceFor(FpsFrenzy.Core.Data.EquipmentSlot.LeftHand);
        return legacy with
        {
            SchemaVersion = RunCheckpoint.CurrentSchemaVersion,
            Difficulty = FpsFrenzy.Core.Data.DifficultyCatalog.Normalize(legacy.Difficulty),
            WeaponSetA = new WeaponSetLoadout { RightHand = right, LeftHand = left },
            WeaponSetB = new WeaponSetLoadout(),
            WeaponQuickbar = WeaponQuickbarLoadout.FromLegacy(
                new WeaponSetLoadout { RightHand = right, LeftHand = left }, new WeaponSetLoadout()),
            ActiveWeaponSlotIndex = 0,
            ActiveWeaponSetIndex = 0,
            WeaponSetStates = new Dictionary<int, WeaponSetCheckpointState>
            {
                [0] = new WeaponSetCheckpointState
                {
                    RightHand = legacy.HandWeaponStates.GetValueOrDefault(
                        FpsFrenzy.Core.Data.EquipmentSlot.RightHand),
                    LeftHand = legacy.HandWeaponStates.GetValueOrDefault(
                        FpsFrenzy.Core.Data.EquipmentSlot.LeftHand),
                },
            },
            IssuedItemInstances = [],
        };
    }

    private static RunCheckpoint MigrateVersionFour(RunCheckpoint legacy) => legacy with
    {
        SchemaVersion = RunCheckpoint.CurrentSchemaVersion,
        Difficulty = FpsFrenzy.Core.Data.DifficultyCatalog.Normalize(legacy.Difficulty),
        WeaponQuickbar = WeaponQuickbarLoadout.FromLegacy(legacy.WeaponSetA, legacy.WeaponSetB),
        ActiveWeaponSlotIndex = Math.Clamp(legacy.ActiveWeaponSetIndex, 0, 1),
    };

    private static RunCheckpoint MigrateVersionFive(RunCheckpoint legacy) => legacy with
    {
        SchemaVersion = RunCheckpoint.CurrentSchemaVersion,
        Difficulty = FpsFrenzy.Core.Data.DifficultyCatalog.Normalize(legacy.Difficulty),
    };

    private static bool IsValid(WeaponCheckpointState state) =>
            state is not null && !string.IsNullOrWhiteSpace(state.WeaponId) &&
            state.Magazine >= 0 && state.Reserve >= 0 &&
            IsFiniteNonNegative(state.Energy) && IsFiniteNonNegative(state.Heat) &&
            IsFiniteNonNegative(state.FireCooldownSeconds) &&
            IsFiniteNonNegative(state.ReloadRemainingSeconds) &&
            IsFiniteNonNegative(state.ReloadDurationSeconds) &&
            state.BurstShotsRemaining >= 0 &&
            IsFiniteNonNegative(state.MagazineConsumptionAccumulator);

    private static bool IsFiniteNonNegative(float value) => float.IsFinite(value) && value >= 0f;
}
