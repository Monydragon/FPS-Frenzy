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

    public RunCheckpointStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public static RunCheckpointStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FPSFrenzy",
        "run-checkpoint-v2.json"));

    public bool Exists => File.Exists(_path);

    public RunCheckpoint? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            RunCheckpoint? checkpoint = JsonSerializer.Deserialize<RunCheckpoint>(
                File.ReadAllText(_path), SerializerOptions);
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

        return checkpoint.WeaponStates.All(state =>
            state is not null && !string.IsNullOrWhiteSpace(state.WeaponId) &&
            state.Magazine >= 0 && state.Reserve >= 0 &&
            IsFiniteNonNegative(state.Energy) && IsFiniteNonNegative(state.Heat) &&
            IsFiniteNonNegative(state.FireCooldownSeconds) &&
            IsFiniteNonNegative(state.ReloadRemainingSeconds) &&
            state.BurstShotsRemaining >= 0 &&
            IsFiniteNonNegative(state.MagazineConsumptionAccumulator));
    }

    private static bool IsFiniteNonNegative(float value) => float.IsFinite(value) && value >= 0f;
}
