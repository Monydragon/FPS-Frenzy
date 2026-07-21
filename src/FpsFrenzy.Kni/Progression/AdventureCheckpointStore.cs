using System.Text.Json;
using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Kni.Progression;

public sealed class AdventureCheckpointStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly string _backupPath;

    public AdventureCheckpointStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _backupPath = _path + ".bak";
    }

    public static AdventureCheckpointStore CreateDefault()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FPSFrenzy");
        return new AdventureCheckpointStore(Path.Combine(directory, "adventure-checkpoint-v1.json"));
    }

    public bool Exists => File.Exists(_path) || File.Exists(_backupPath);

    public AdventureCheckpoint? Load()
    {
        AdventureCheckpoint? primary = TryLoad(_path);
        if (IsValid(primary))
        {
            return primary;
        }

        AdventureCheckpoint? backup = TryLoad(_backupPath);
        return IsValid(backup) ? backup : null;
    }

    public bool Save(AdventureCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!IsValid(checkpoint))
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(_path);
        string temporaryPath = _path + ".tmp";
        try
        {
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(checkpoint, SerializerOptions));
            if (File.Exists(_path))
            {
                File.Copy(_path, _backupPath, overwrite: true);
            }
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
            TryDelete(_path);
            TryDelete(_path + ".tmp");
            TryDelete(_backupPath);
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

    internal static bool IsValid(AdventureCheckpoint? checkpoint)
    {
        if (checkpoint is null || checkpoint.SchemaVersion != AdventureCheckpoint.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(checkpoint.AdventureId) ||
            string.IsNullOrWhiteSpace(checkpoint.GeneratorVersion) ||
            checkpoint.Seed is < DungeonGenerator.MinimumSeed or > DungeonGenerator.MaximumSeed ||
            checkpoint.NextStageIndex is < 0 or > 4 || checkpoint.StoryPosition < 0 ||
            checkpoint.BoonIds is null || checkpoint.BoonIds.Count > 3 ||
            checkpoint.BoonIds.Any(string.IsNullOrWhiteSpace) ||
            checkpoint.BoonIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != checkpoint.BoonIds.Count ||
            checkpoint.ThreatTier is < ThreatTier.TierI or > ThreatTier.TierX ||
            !DifficultyCatalog.All.Any(definition =>
                definition.Mode == DifficultyCatalog.Normalize(checkpoint.Difficulty)) ||
            !IsFiniteNonNegative(checkpoint.ElapsedRunSeconds) || checkpoint.Score < 0 || checkpoint.Kills < 0 ||
            !IsFiniteNonNegative(checkpoint.DamageTaken) || checkpoint.FloorsCompleted is < 0 or > 3 ||
            checkpoint.SecretsFound < 0 || checkpoint.LoreFound < 0 ||
            checkpoint.EquipmentLoadout is null || checkpoint.EquipmentItems is null ||
            checkpoint.WeaponSetA is null || checkpoint.WeaponSetB is null ||
            checkpoint.WeaponQuickbar is null || checkpoint.WeaponQuickbar.Slots is null ||
            checkpoint.WeaponQuickbar.Slots.Count != WeaponQuickbarLoadout.SlotCount ||
            checkpoint.ActiveWeaponSlotIndex is < 0 or >= WeaponQuickbarLoadout.SlotCount ||
            checkpoint.WeaponSetStates is null || checkpoint.WeaponStates is null ||
            checkpoint.CommittedProgression is null || checkpoint.CommittedRewardItemIds is null ||
            checkpoint.CommittedRewardItemIds.Any(string.IsNullOrWhiteSpace) ||
            checkpoint.RunExperienceEarned < 0 || checkpoint.RunLevelsGained < 0 ||
            checkpoint.RunProficiencyExperience is null ||
            checkpoint.RunProficiencyExperience.Values.Any(amount => amount < 0) ||
            checkpoint.RunAbilitiesMastered is null || checkpoint.RunAbilitiesMastered.Any(string.IsNullOrWhiteSpace) ||
            checkpoint.RunCollectedItemIds is null || checkpoint.RunCollectedItemIds.Any(string.IsNullOrWhiteSpace) ||
            checkpoint.RunRarityTotals is null || checkpoint.RunRarityTotals.Values.Any(count => count < 0) ||
            checkpoint.RunHighestItemPower < 0)
        {
            return false;
        }

        return checkpoint.WeaponStates.All(IsValid) &&
            checkpoint.WeaponSetStates.Values.SelectMany(state =>
                new[] { state.RightHand, state.LeftHand }.OfType<WeaponCheckpointState>()).All(IsValid);
    }

    private static AdventureCheckpoint? TryLoad(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AdventureCheckpoint>(File.ReadAllText(path), SerializerOptions)
                : null;
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

    private static bool IsValid(WeaponCheckpointState state) =>
        state is not null && !string.IsNullOrWhiteSpace(state.WeaponId) &&
        state.Magazine >= 0 && state.Reserve >= 0 &&
        IsFiniteNonNegative(state.Energy) && IsFiniteNonNegative(state.Heat) &&
        IsFiniteNonNegative(state.FireCooldownSeconds) &&
        IsFiniteNonNegative(state.ReloadRemainingSeconds) &&
        IsFiniteNonNegative(state.ReloadDurationSeconds) &&
        state.BurstShotsRemaining >= 0 && IsFiniteNonNegative(state.MagazineConsumptionAccumulator);

    private static bool IsFiniteNonNegative(float value) => float.IsFinite(value) && value >= 0f;

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
