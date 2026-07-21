using System.Text.Json;
using System.Text.Json.Serialization;

namespace FpsFrenzy.Core.Data;

public sealed class ContentCatalog
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Dictionary<string, WeaponDefinition> Weapons { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WeaponArchetypeDefinition> WeaponArchetypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WeaponBaseDefinition> WeaponBases { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WeaponVisualDefinition> WeaponVisualCalibrations { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EnemyDefinition> Enemies { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ArenaDefinition> Arenas { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AdventureDefinition> Adventures { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WaveSetDefinition> WaveSets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, UpgradeDefinition> Upgrades { get; } = StandardUpgradeCatalog.All
        .ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EquipmentBaseDefinition> EquipmentBases { get; } = StandardRpgCatalog.Equipment
        .ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AffixDefinition> Affixes { get; } = StandardRpgCatalog.Affixes
        .ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EquipmentAbilityDefinition> Abilities { get; } = StandardRpgCatalog.Abilities
        .ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TalentDefinition> Talents { get; } = StandardRpgCatalog.Talents
        .ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LootTableDefinition> LootTables { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [StandardRpgCatalog.StandardLootTable.Id] = StandardRpgCatalog.StandardLootTable,
    };

    public static ContentCatalog LoadFromDirectory(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ContentCatalog catalog = new();
        AddDirectory(Path.Combine(dataRoot, "Weapons"), catalog.Add, Read<WeaponDefinition>);
        AddDirectory(Path.Combine(dataRoot, "Enemies"), catalog.Add, Read<EnemyDefinition>);
        AddDirectory(Path.Combine(dataRoot, "Arenas"), catalog.Add, Read<ArenaDefinition>);
        AddDirectory(Path.Combine(dataRoot, "Waves"), catalog.Add, Read<WaveSetDefinition>);
        AddOptionalDirectory(Path.Combine(dataRoot, "Adventures"), catalog.Add, Read<AdventureDefinition>);
        string upgradesPath = Path.Combine(dataRoot, "Upgrades");
        if (Directory.Exists(upgradesPath))
        {
            AddDirectory(upgradesPath, catalog.Add, Read<UpgradeDefinition>);
        }
        AddOptionalDirectory(Path.Combine(dataRoot, "Equipment"), catalog.Add, Read<EquipmentBaseDefinition>);
        AddOptionalDirectory(Path.Combine(dataRoot, "Affixes"), catalog.Add, Read<AffixDefinition>);
        AddOptionalDirectory(Path.Combine(dataRoot, "Abilities"), catalog.Add, Read<EquipmentAbilityDefinition>);
        AddOptionalDirectory(Path.Combine(dataRoot, "Talents"), catalog.Add, Read<TalentDefinition>);
        AddOptionalDirectory(Path.Combine(dataRoot, "LootTables"), catalog.Add, Read<LootTableDefinition>);
        AddOptionalDirectory(Path.Combine(dataRoot, "WeaponArchetypes"), catalog.Add,
            Read<WeaponArchetypeDefinition>);
        AddOptionalCollectionDirectory(Path.Combine(dataRoot, "WeaponBases"), path =>
        {
            WeaponBaseSetDefinition set = Read<WeaponBaseSetDefinition>(path);
            foreach (WeaponBaseDefinition definition in set.Bases)
            {
                catalog.Add(definition);
            }
        });
        AddOptionalCollectionDirectory(Path.Combine(dataRoot, "WeaponVisuals"), path =>
        {
            WeaponVisualCalibrationSetDefinition set = Read<WeaponVisualCalibrationSetDefinition>(path);
            foreach (WeaponVisualCalibrationDefinition definition in set.Calibrations)
            {
                catalog.WeaponVisualCalibrations[definition.WeaponId] = definition.Visual;
            }
        });
        catalog.ResolveWeaponBases();
        catalog.Validate().ThrowIfInvalid();
        return catalog;
    }

    public static ContentCatalog Load(
        Stream weapon,
        Stream enemy,
        Stream arena,
        Stream waves)
        => Load([weapon], [enemy], [arena], [waves]);

    public static ContentCatalog Load(
        IEnumerable<Stream> weapons,
        IEnumerable<Stream> enemies,
        IEnumerable<Stream> arenas,
        IEnumerable<Stream> waveSets)
        => Load(weapons, enemies, arenas, waveSets, []);

    public static ContentCatalog Load(
        IEnumerable<Stream> weapons,
        IEnumerable<Stream> enemies,
        IEnumerable<Stream> arenas,
        IEnumerable<Stream> waveSets,
        IEnumerable<Stream> upgrades)
    {
        ContentCatalog catalog = new();
        foreach (Stream stream in weapons)
        {
            catalog.Add(Read<WeaponDefinition>(stream));
        }

        foreach (Stream stream in enemies)
        {
            catalog.Add(Read<EnemyDefinition>(stream));
        }

        foreach (Stream stream in arenas)
        {
            catalog.Add(Read<ArenaDefinition>(stream));
        }

        foreach (Stream stream in waveSets)
        {
            catalog.Add(Read<WaveSetDefinition>(stream));
        }

        foreach (Stream stream in upgrades)
        {
            catalog.Add(Read<UpgradeDefinition>(stream));
        }

        catalog.Validate().ThrowIfInvalid();
        return catalog;
    }

    public static ContentCatalog Load(
        IEnumerable<Stream> weapons,
        IEnumerable<Stream> enemies,
        IEnumerable<Stream> arenas,
        IEnumerable<Stream> waveSets,
        IEnumerable<Stream> upgrades,
        IEnumerable<Stream> weaponArchetypes,
        IEnumerable<Stream> weaponBaseSets,
        IEnumerable<Stream>? weaponVisualSets = null,
        IEnumerable<Stream>? adventures = null)
    {
        ContentCatalog catalog = new();
        foreach (Stream stream in weapons) catalog.Add(Read<WeaponDefinition>(stream));
        foreach (Stream stream in enemies) catalog.Add(Read<EnemyDefinition>(stream));
        foreach (Stream stream in arenas) catalog.Add(Read<ArenaDefinition>(stream));
        foreach (Stream stream in waveSets) catalog.Add(Read<WaveSetDefinition>(stream));
        foreach (Stream stream in upgrades) catalog.Add(Read<UpgradeDefinition>(stream));
        foreach (Stream stream in weaponArchetypes) catalog.Add(Read<WeaponArchetypeDefinition>(stream));
        foreach (Stream stream in weaponBaseSets)
        {
            foreach (WeaponBaseDefinition definition in Read<WeaponBaseSetDefinition>(stream).Bases)
            {
                catalog.Add(definition);
            }
        }
        foreach (Stream stream in weaponVisualSets ?? [])
        {
            foreach (WeaponVisualCalibrationDefinition definition in
                Read<WeaponVisualCalibrationSetDefinition>(stream).Calibrations)
            {
                catalog.WeaponVisualCalibrations[definition.WeaponId] = definition.Visual;
            }
        }
        foreach (Stream stream in adventures ?? []) catalog.Add(Read<AdventureDefinition>(stream));

        catalog.ResolveWeaponBases();
        catalog.Validate().ThrowIfInvalid();
        return catalog;
    }

    public ContentValidationResult Validate() => ContentValidator.Validate(this);

    private void Add(WeaponDefinition definition) => Weapons.Add(definition.Id, definition);
    private void Add(WeaponArchetypeDefinition definition) => WeaponArchetypes.Add(definition.Id, definition);
    private void Add(WeaponBaseDefinition definition) => WeaponBases.Add(definition.Id, definition);
    private void Add(EnemyDefinition definition) => Enemies.Add(definition.Id, definition);
    private void Add(ArenaDefinition definition) => Arenas.Add(definition.Id, definition);
    private void Add(AdventureDefinition definition) => Adventures.Add(definition.Id, definition);
    private void Add(WaveSetDefinition definition) => WaveSets.Add(definition.Id, definition);
    private void Add(UpgradeDefinition definition) => Upgrades[definition.Id] = definition;
    private void Add(EquipmentBaseDefinition definition) => EquipmentBases[definition.Id] = definition;
    private void Add(AffixDefinition definition) => Affixes[definition.Id] = definition;
    private void Add(EquipmentAbilityDefinition definition) => Abilities[definition.Id] = definition;
    private void Add(TalentDefinition definition) => Talents[definition.Id] = definition;
    private void Add(LootTableDefinition definition) => LootTables[definition.Id] = definition;

    private void ResolveWeaponBases()
    {
        foreach (WeaponBaseDefinition weaponBase in WeaponBases.Values.OrderBy(definition => definition.Id))
        {
            if (!WeaponArchetypes.TryGetValue(weaponBase.ArchetypeId, out WeaponArchetypeDefinition? archetype) ||
                !Weapons.TryGetValue(archetype.TemplateWeaponId, out WeaponDefinition? template))
            {
                continue;
            }

            WeaponVisualDefinition visual = weaponBase.Visual ??
                WeaponVisualCalibrations.GetValueOrDefault(weaponBase.Id) ?? template.Visual;
            bool hasBaseCalibration = weaponBase.Visual is not null ||
                WeaponVisualCalibrations.ContainsKey(weaponBase.Id);
            Weapons[weaponBase.Id] = template with
            {
                Id = weaponBase.Id,
                DisplayName = weaponBase.DisplayName,
                Family = archetype.Family,
                Handedness = weaponBase.Handedness ?? archetype.Handedness,
                BaseTier = weaponBase.BaseTier,
                BehaviorFlags = ToBehaviorFlags(weaponBase.Effects),
                Effects = [.. weaponBase.Effects],
                TaughtAbilityId = archetype.TaughtAbilityId,
                ModelAsset = weaponBase.ModelAsset,
                IconAsset = weaponBase.IconAsset,
                Damage = template.Damage * weaponBase.DamageMultiplier,
                FireIntervalSeconds = template.FireIntervalSeconds * weaponBase.FireIntervalMultiplier,
                AdsFieldOfViewDegrees = weaponBase.AdsFieldOfViewDegrees ?? template.AdsFieldOfViewDegrees,
                WeakPointMultiplier = weaponBase.WeakPointMultiplier,
                ScopedSensitivityMultiplier = weaponBase.ScopedSensitivityMultiplier,
                ProjectileMotion = weaponBase.ProjectileMotion,
                Visual = visual,
                ViewModelHipOffset = visual.HipOffset ?? template.ViewModelHipOffset,
                ViewModelAdsOffset = visual.AdsOffset ?? template.ViewModelAdsOffset,
                ViewModelTargetSpan = visual.TargetSpan ?? template.ViewModelTargetSpan,
                ViewModelYawDegrees = visual.YawDegrees ?? template.ViewModelYawDegrees,
                ViewModelPitchDegrees = visual.PitchDegrees ?? template.ViewModelPitchDegrees,
                ViewModelRollDegrees = visual.RollDegrees ??
                    (hasBaseCalibration ? 0f : template.ViewModelRollDegrees),
            };
        }

        HashSet<string> authoredBaseIds = WeaponBases.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string templateId in WeaponArchetypes.Values.Select(archetype => archetype.TemplateWeaponId)
            .Where(templateId => !authoredBaseIds.Contains(templateId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray())
        {
            Weapons.Remove(templateId);
        }
    }

    private static WeaponBehavior ToBehaviorFlags(IEnumerable<WeaponEffectDefinition> effects)
    {
        WeaponBehavior flags = WeaponBehavior.None;
        foreach (WeaponEffectDefinition effect in effects)
        {
            flags |= effect.Type switch
            {
                WeaponEffectType.Charge => WeaponBehavior.Charge,
                WeaponEffectType.Pierce => WeaponBehavior.Pierce,
                WeaponEffectType.Ricochet => WeaponBehavior.Ricochet,
                WeaponEffectType.Chain => WeaponBehavior.ChainField,
                WeaponEffectType.Cluster => WeaponBehavior.Cluster,
                WeaponEffectType.Split => WeaponBehavior.SplitShot,
                WeaponEffectType.Pull => WeaponBehavior.Pull,
                WeaponEffectType.Knockback => WeaponBehavior.Knockback,
                WeaponEffectType.DamageOverTime => WeaponBehavior.DamageOverTime,
                WeaponEffectType.Stun => WeaponBehavior.Stun,
                WeaponEffectType.RampDamage => WeaponBehavior.RampDamage,
                WeaponEffectType.Homing => WeaponBehavior.Homing,
                WeaponEffectType.Returning => WeaponBehavior.Returning,
                WeaponEffectType.WeakPointBonus => WeaponBehavior.WeakPointBonus,
                _ => WeaponBehavior.None,
            };
        }

        return flags;
    }

    private static T Read<T>(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read<T>(stream);
    }

    private static T Read<T>(Stream stream) => JsonSerializer.Deserialize<T>(stream, SerializerOptions)
        ?? throw new InvalidDataException($"Unable to deserialize {typeof(T).Name}.");

    private static void AddDirectory<T>(string path, Action<T> add, Func<string, T> read)
    {
        foreach (string file in Directory.EnumerateFiles(path, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            add(read(file));
        }
    }

    private static void AddOptionalDirectory<T>(string path, Action<T> add, Func<string, T> read)
    {
        if (Directory.Exists(path))
        {
            AddDirectory(path, add, read);
        }
    }

    private static void AddOptionalCollectionDirectory(string path, Action<string> add)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(path, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            add(file);
        }
    }
}

public sealed record ContentValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, Errors));
        }
    }
}
