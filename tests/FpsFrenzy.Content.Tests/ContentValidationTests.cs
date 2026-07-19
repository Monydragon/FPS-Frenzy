using System.Text;
using System.Text.Json;
using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Content.Tests;

public sealed class ContentValidationTests
{
    private static readonly string[] ExpectedBossPhases = ["breach", "overload", "rupture"];

    [Fact]
    public void ShippedDefinitionsHaveValidIdsReferencesAndRanges()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));

        Assert.True(catalog.Validate().IsValid);
        Assert.Equal(6, catalog.Weapons.Count);
        Assert.Equal(6, catalog.Enemies.Count);
        Assert.Contains("training-ring", catalog.Arenas);
        Assert.Contains("orbital-depot", catalog.Arenas);
    }

    [Fact]
    public void SixWeaponsHaveDistinctProductionBehaviors()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));

        Assert.Equal(TriggerMode.SemiAutomatic, catalog.Weapons["pulse-sidearm"].TriggerMode);
        Assert.Equal(3, catalog.Weapons["burst-carbine"].BurstCount);
        Assert.True(catalog.Weapons["scatter-blaster"].PelletCount >= 8);
        Assert.Equal(AmmoMode.Heat, catalog.Weapons["beam-rifle"].AmmoMode);
        Assert.True(catalog.Weapons["plasma-launcher"].SplashRadius >= 3f);
        Assert.True(catalog.Weapons["arc-cannon"].ChainTargets >= 3);
    }

    [Fact]
    public void OrbitalDepotIsACompleteMixedTenWaveBossArena()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));
        ArenaDefinition arena = catalog.Arenas["orbital-depot"];
        WaveSetDefinition waves = catalog.WaveSets[arena.WaveSetId];
        HashSet<string?> pickupWeapons = arena.PickupSpawns
            .Where(pickup => pickup.Type == PickupType.Weapon)
            .Select(pickup => pickup.WeaponId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(10, waves.Waves.Count);
        Assert.NotNull(waves.BossWave);
        Assert.Contains(waves.BossWave!.SpawnGroups, group => group.EnemyId == "big-alien" && group.Count == 1);
        Assert.Equal(catalog.Weapons.Keys.Order(StringComparer.OrdinalIgnoreCase),
            pickupWeapons.OfType<string>().Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains(waves.Waves.SelectMany(wave => wave.SpawnGroups), group => group.EnemyId == "alien-warden");
        Assert.True(arena.BoundsMax.X - arena.BoundsMin.X >= 70f);
        Assert.True(arena.BoundsMax.Z - arena.BoundsMin.Z >= 54f);
        Assert.True(arena.Props.Count >= 40);
        Assert.Contains(arena.Primitives, primitive => !string.IsNullOrWhiteSpace(primitive.TextureAsset));
        Assert.Contains(arena.Props, prop => prop.Id == "docked-cargo-craft");
        Assert.All(
            arena.Props.Where(prop => prop.Id is "north-comms-dish" or "north-west-generator" or "east-comms-relay"),
            prop => Assert.True(prop.AnchorToGround));
        Assert.True(arena.PickupSpawns.Count(pickup => pickup.Type == PickupType.Health) >= 3);
        Assert.True(arena.PickupSpawns.Count(pickup => pickup.Type == PickupType.Ammo) >= 3);

        int previousCount = 0;
        int previousConcurrency = 0;
        float previousHealth = 0f;
        foreach (WaveDefinition wave in waves.Waves)
        {
            int count = wave.SpawnGroups.Sum(group => group.Count);
            float health = wave.SpawnGroups.Sum(group => catalog.Enemies[group.EnemyId].MaxHealth * group.Count);
            Assert.True(count >= previousCount);
            Assert.True(wave.MaximumConcurrentEnemies >= previousConcurrency);
            Assert.InRange(wave.MaximumConcurrentEnemies, 4, 10);
            if (previousHealth > 0f)
            {
                Assert.InRange(health / previousHealth, 1f, 1.4f);
            }

            previousCount = count;
            previousConcurrency = wave.MaximumConcurrentEnemies;
            previousHealth = health;
        }

        Assert.All(arena.PickupSpawns.Where(pickup => pickup.Type == PickupType.Health), pickup =>
        {
            Assert.InRange(pickup.Amount, 20, 35);
            Assert.InRange(pickup.RespawnSeconds, 20f, 30f);
        });
        Assert.All(arena.PickupSpawns.Where(pickup => pickup.Type == PickupType.Ammo), pickup =>
        {
            Assert.InRange(pickup.Amount, 20, 40);
            Assert.InRange(pickup.RespawnSeconds, 18f, 30f);
        });
        Assert.All(arena.PickupSpawns.Where(pickup => pickup.Type == PickupType.Weapon), pickup =>
            Assert.InRange(pickup.RespawnSeconds, 24f, 32f));
    }

    [Fact]
    public void RegularEnemiesAndBigAlienExposeAuthoredAiRolesAndPhases()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));
        EnemyBehavior[] regularBehaviors = catalog.Enemies.Values
            .Where(enemy => !enemy.IsBoss)
            .Select(enemy => enemy.Behavior)
            .Order()
            .ToArray();
        EnemyDefinition boss = catalog.Enemies["big-alien"];

        Assert.Equal(Enum.GetValues<EnemyBehavior>().Where(behavior => behavior != EnemyBehavior.Boss).Order(),
            regularBehaviors);
        Assert.True(boss.IsBoss);
        Assert.Equal(ExpectedBossPhases, boss.BossPhases.Select(phase => phase.Id));
        Assert.All(boss.BossPhases.Skip(1), phase => Assert.True(phase.SummonCount > 0));
    }

    [Fact]
    public void ReleaseTemplateHasTenWavesAndBossEntry()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));
        WaveSetDefinition release = catalog.WaveSets["release-ten-plus-boss"];

        Assert.Equal(10, release.Waves.Count);
        Assert.NotNull(release.BossWave);
        Assert.Contains(release.BossWave!.SpawnGroups,
            group => catalog.Enemies[group.EnemyId].IsBoss && group.Count == 1);
    }

    [Fact]
    public void SelectedFbxAssetsAndRequiredAlienClipsArePresent()
    {
        string root = FindRepositoryRoot();
        string weapon = Path.Combine(root, "Content", "Models", "Weapons", "blaster-a.fbx");
        string alien = Path.Combine(root, "Content", "Models", "Enemies", "Alien.fbx");

        Assert.True(File.Exists(weapon));
        Assert.True(File.Exists(alien));
        string fbxText = Encoding.ASCII.GetString(File.ReadAllBytes(alien));
        foreach (string clip in new[] { "Idle", "Walk", "Bite_Front", "HitRecieve", "Death" })
        {
            Assert.Contains(clip, fbxText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ArenaArtReferencesResolveToSourceFilesAndContentBuildEntries()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));
        string mgcb = File.ReadAllText(Path.Combine(root, "Content", "FpsFrenzyContent.mgcb"));

        foreach (ArenaDefinition arena in catalog.Arenas.Values)
        {
            foreach (ArenaPropDefinition prop in arena.Props)
            {
                string sourcePath = Path.Combine(
                    root,
                    "Content",
                    $"{prop.ModelAsset}.fbx".Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(sourcePath), $"Missing source model for arena prop '{prop.Id}': {sourcePath}");
                Assert.Contains($"/build:{prop.ModelAsset}.fbx", mgcb, StringComparison.Ordinal);
            }

            foreach (ArenaPrimitiveDefinition primitive in arena.Primitives.Where(
                         primitive => !string.IsNullOrWhiteSpace(primitive.TextureAsset)))
            {
                string textureAsset = primitive.TextureAsset!;
                string sourcePath = Path.Combine(
                    root,
                    "Content",
                    $"{textureAsset}.png".Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(sourcePath), $"Missing source texture for arena primitive '{primitive.Id}': {sourcePath}");
                Assert.Contains($"/build:{textureAsset}.png", mgcb, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ThirdPartyManifestRecordsCc0Provenance()
    {
        string root = FindRepositoryRoot();
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "Content", "ThirdParty", "asset-manifest.json")));
        JsonElement assets = manifest.RootElement.GetProperty("assets");

        string[] expectedPacks =
        [
            "Kenney Blaster Kit",
            "Quaternius Ultimate Monsters",
            "Kenney Survival Kit",
            "Kenney Modular Space Kit",
            "Kenney Space Station Kit",
            "Kenney Space Kit",
            "Kenney Prototype Textures",
            "Kenney UI Pack - Sci-Fi",
        ];

        Assert.Equal(expectedPacks.Length, assets.GetArrayLength());
        Assert.Equal(
            expectedPacks.Order(),
            assets.EnumerateArray().Select(asset => asset.GetProperty("pack").GetString()).Order());
        Assert.All(assets.EnumerateArray(), asset =>
            Assert.Equal("CC0 1.0", asset.GetProperty("license").GetString()));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FPSFrenzy.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
