using System.Text.Json;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Kni.Progression;

public sealed record RunLoadoutOverlay
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long ProfileGeneration { get; init; }
    public int RunSeed { get; init; }
    public int CheckpointEncounterIndex { get; init; }
    public WeaponQuickbarLoadout WeaponQuickbar { get; init; } = new();
    public int ActiveWeaponSlotIndex { get; init; }
    public EquipmentLoadout EquipmentLoadout { get; init; } = new();
    public List<EquipmentInstance> IssuedItemInstances { get; init; } = [];
    public Dictionary<string, float> AbilityCooldowns { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RunLoadoutOverlayStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _path;

    public RunLoadoutOverlayStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public static RunLoadoutOverlayStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FPSFrenzy",
        "run-loadout-overlay-v1.json"));

    public bool Save(RunLoadoutOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        string temporaryPath = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(overlay, SerializerOptions));
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

    public RunLoadoutOverlay? Load(ProfileData profile, RunCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(checkpoint);
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }
            RunLoadoutOverlay? overlay = JsonSerializer.Deserialize<RunLoadoutOverlay>(
                File.ReadAllText(_path), SerializerOptions);
            if (overlay is null || overlay.SchemaVersion != RunLoadoutOverlay.CurrentSchemaVersion ||
                overlay.ProfileGeneration <= 0 || overlay.ProfileGeneration > profile.Generation ||
                overlay.RunSeed != checkpoint.Seed ||
                overlay.CheckpointEncounterIndex != checkpoint.NextEncounterIndex ||
                overlay.ActiveWeaponSlotIndex is < 0 or >= WeaponQuickbarLoadout.SlotCount ||
                overlay.WeaponQuickbar.Slots.Count != WeaponQuickbarLoadout.SlotCount ||
                overlay.IssuedItemInstances.Any(item => !item.IsRunBound) ||
                overlay.AbilityCooldowns.Any(pair => string.IsNullOrWhiteSpace(pair.Key) ||
                    !float.IsFinite(pair.Value) || pair.Value < 0f))
            {
                return null;
            }

            HashSet<string> validIds = profile.Stash.Select(item => item.Id)
                .Concat(overlay.IssuedItemInstances.Select(item => item.Id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> referencedIds = overlay.WeaponQuickbar.Slots.SelectMany(slot =>
                    new[] { slot.RightHand?.ItemInstanceId, slot.LeftHand?.ItemInstanceId })
                .Concat(overlay.EquipmentLoadout.EquippedItemIds.Values)
                .OfType<string>();
            return referencedIds.All(validIds.Contains) ? overlay : null;
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

    public void Clear()
    {
        TryDelete(_path);
        TryDelete(_path + ".tmp");
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
}
