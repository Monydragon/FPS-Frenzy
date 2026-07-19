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
    public Dictionary<string, EnemyDefinition> Enemies { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ArenaDefinition> Arenas { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WaveSetDefinition> WaveSets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ContentCatalog LoadFromDirectory(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ContentCatalog catalog = new();
        AddDirectory(Path.Combine(dataRoot, "Weapons"), catalog.Add, Read<WeaponDefinition>);
        AddDirectory(Path.Combine(dataRoot, "Enemies"), catalog.Add, Read<EnemyDefinition>);
        AddDirectory(Path.Combine(dataRoot, "Arenas"), catalog.Add, Read<ArenaDefinition>);
        AddDirectory(Path.Combine(dataRoot, "Waves"), catalog.Add, Read<WaveSetDefinition>);
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

        catalog.Validate().ThrowIfInvalid();
        return catalog;
    }

    public ContentValidationResult Validate() => ContentValidator.Validate(this);

    private void Add(WeaponDefinition definition) => Weapons.Add(definition.Id, definition);
    private void Add(EnemyDefinition definition) => Enemies.Add(definition.Id, definition);
    private void Add(ArenaDefinition definition) => Arenas.Add(definition.Id, definition);
    private void Add(WaveSetDefinition definition) => WaveSets.Add(definition.Id, definition);

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
