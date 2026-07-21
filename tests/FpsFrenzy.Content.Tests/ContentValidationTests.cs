using System.Security.Cryptography;
using System.Text.Json;
using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Content.Tests;

public sealed class ContentValidationTests
{
    private static readonly string[] ExpectedBossPhases = ["containment", "overload", "rupture"];
    private static readonly string[] EmissiveRobotIds = ["robot-wasp", "breach-walker"];

    [Fact]
    public void ShippedDefinitionsHaveValidIdsReferencesAndRanges()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));

        Assert.True(catalog.Validate().IsValid);
        Assert.Equal(50, catalog.Weapons.Count);
        Assert.Equal(13, catalog.Enemies.Count);
        Assert.Equal(7, catalog.Enemies.Values.Count(enemy => enemy.SchemaVersion == 2));
        Assert.Contains("null-signal", catalog.Adventures);
        Assert.Contains("core-warden", catalog.Enemies);
        Assert.Contains("training-ring", catalog.Arenas);
        Assert.Contains("orbital-depot", catalog.Arenas);
    }

    [Fact]
    public void PackagedStreamCatalogResolvesTheDataDrivenReleaseArsenalBeforeValidation()
    {
        string root = FindRepositoryRoot();
        string data = Path.Combine(root, "Content", "Data");
        string[] weaponIds = ["pulse-sidearm", "burst-carbine", "scatter-blaster", "beam-rifle",
            "plasma-launcher", "arc-cannon", "smg", "precision", "heavy", "experimental"];
        string[] archetypeIds = ["pulse", "smg", "burst", "scatter", "precision", "beam", "plasma", "arc",
            "heavy", "experimental"];
        string[] enemyIds = ["robot-striker", "robot-interceptor", "robot-juggernaut", "robot-wasp", "robot-warden", "breach-walker"];
        List<Stream> streams = [];
        try
        {
            Stream[] weapons = weaponIds.Select(id => (Stream)File.OpenRead(
                Path.Combine(data, "Weapons", $"{id}.json"))).ToArray();
            Stream[] enemies = enemyIds.Select(id => (Stream)File.OpenRead(
                Path.Combine(data, "Enemies", $"{id}.json"))).ToArray();
            Stream[] arenas = [(Stream)File.OpenRead(Path.Combine(data, "Arenas", "orbital-depot.json"))];
            Stream[] waves = [(Stream)File.OpenRead(Path.Combine(data, "Waves", "orbital-depot-waves.json"))];
            Stream[] archetypes = archetypeIds.Select(id => (Stream)File.OpenRead(
                Path.Combine(data, "WeaponArchetypes", $"{id}.json"))).ToArray();
            Stream[] bases = [(Stream)File.OpenRead(Path.Combine(data, "WeaponBases", "release-arsenal.json"))];
            Stream[] visuals = [(Stream)File.OpenRead(Path.Combine(
                data, "WeaponVisuals", "release-calibrations.json"))];
            streams.AddRange(weapons);
            streams.AddRange(enemies);
            streams.AddRange(arenas);
            streams.AddRange(waves);
            streams.AddRange(archetypes);
            streams.AddRange(bases);
            streams.AddRange(visuals);

            ContentCatalog catalog = ContentCatalog.Load(
                weapons, enemies, arenas, waves, [], archetypes, bases, visuals);

            Assert.True(catalog.Validate().IsValid);
            Assert.Equal(50, catalog.Weapons.Count);
            Assert.Equal(50, catalog.WeaponVisualCalibrations.Count);
            Assert.All(catalog.Weapons.Values, weapon =>
                Assert.Same(catalog.WeaponVisualCalibrations[weapon.Id], weapon.Visual));
        }
        finally
        {
            foreach (Stream stream in streams)
            {
                stream.Dispose();
            }
        }
    }

    [Fact]
    public void ShippingCatalogContainsOnlyTheReleaseRobotCampaign()
    {
        string root = FindRepositoryRoot();
        string mgcb = File.ReadAllText(Path.Combine(root, "Content", "FpsFrenzyContent.mgcb"));
        string[] devOnlyEntries =
        [
            "/copy:Data/Arenas/training-ring.json",
            "/copy:Data/Waves/training-waves.json",
            "/copy:Data/Waves/release-wave-template.json",
            "/copy:Data/Enemies/alien-grunt.json",
            "#begin Models/Enemies/Alien.fbx",
            "#begin Models/Enemies/Armabee_Evolved.fbx",
            "#begin Models/Enemies/Orc.fbx",
            "#begin Models/Enemies/GreenSpikyBlob.fbx",
            "#begin Models/Enemies/MushroomKing.fbx",
            "#begin Models/Enemies/BlueDemon.fbx",
        ];

        Assert.All(devOnlyEntries, entry =>
            Assert.DoesNotContain(entry, mgcb, StringComparison.Ordinal));
        Assert.DoesNotContain(
            "alien-",
            File.ReadAllText(Path.Combine(root, "Content", "Data", "Waves", "orbital-depot-waves.json")),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "big-alien",
            File.ReadAllText(Path.Combine(root, "Content", "Data", "Waves", "orbital-depot-waves.json")),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "alien-",
            File.ReadAllText(Path.Combine(root, "Content", "Data", "Waves", "release-wave-template.json")),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OxaniumFontsAndAdventureMusicArePackagedAtAuthoredSettings()
    {
        string root = FindRepositoryRoot();
        string content = Path.Combine(root, "Content");
        string mgcb = File.ReadAllText(Path.Combine(content, "FpsFrenzyContent.mgcb"));
        string hudFont = File.ReadAllText(Path.Combine(content, "Fonts", "OxaniumHud.spritefont"));
        string bodyFont = File.ReadAllText(Path.Combine(content, "Fonts", "OxaniumBody.spritefont"));
        string headingFont = File.ReadAllText(Path.Combine(content, "Fonts", "OxaniumHeading.spritefont"));
        string sectorPath = Path.Combine(content, "Audio", "Music", "sector.ogg");

        Assert.Contains("/build:Fonts/OxaniumHud.spritefont", mgcb, StringComparison.Ordinal);
        Assert.Contains("/build:Fonts/OxaniumBody.spritefont", mgcb, StringComparison.Ordinal);
        Assert.Contains("/build:Fonts/OxaniumHeading.spritefont", mgcb, StringComparison.Ordinal);
        Assert.Contains("/build:Audio/Music/sector.ogg", mgcb, StringComparison.Ordinal);
        Assert.Contains("<FontName>Oxanium-Regular.ttf</FontName>", hudFont, StringComparison.Ordinal);
        Assert.Contains("<Size>20</Size>", hudFont, StringComparison.Ordinal);
        Assert.Contains("<Size>22</Size>", bodyFont, StringComparison.Ordinal);
        Assert.Contains("<FontName>Oxanium-SemiBold.ttf</FontName>", headingFont, StringComparison.Ordinal);
        Assert.Contains("<Size>44</Size>", headingFont, StringComparison.Ordinal);
        Assert.Equal(
            "D5862627E6D0C424FC157EAFFAA5421BAE4F0BB177E7628D520BFB6A45AA9676",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sectorPath))));
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
    public void OrbitalDepotIsACleanOpenRobotCampaignArena()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));
        ArenaDefinition arena = catalog.Arenas["orbital-depot"];
        EnemyDefinition[] releaseEnemies = catalog.Enemies.Values
            .Where(enemy => enemy.SchemaVersion == 2 && enemy.Id != "core-warden")
            .ToArray();

        Assert.Equal(3, arena.SchemaVersion);
        Assert.Equal(ArenaTraversalMode.OpenArena, arena.TraversalMode);
        Assert.Equal(4, arena.Sectors.Count);
        Assert.NotEqual(System.Numerics.Vector3.Zero, arena.BossArenaAnchor);
        Assert.DoesNotContain(arena.PickupSpawns, pickup => pickup.Type == PickupType.Weapon);
        Assert.Equal(5, releaseEnemies.Count(enemy => !enemy.IsBoss));
        Assert.Single(releaseEnemies, enemy => enemy.IsBoss && enemy.Id == "breach-walker");
        Assert.All(arena.Sectors, sector =>
        {
            Assert.True(sector.SpawnPortals.Count >= 4);
            Assert.All(sector.SpawnPortals, portal => Assert.True(portal.TelegraphSeconds >= 0.75f));
        });
        Assert.True(arena.BoundsMax.X - arena.BoundsMin.X >= 70f);
        Assert.True(arena.BoundsMax.Z - arena.BoundsMin.Z >= 54f);
        Assert.Equal(24, arena.Props.Count);
        string[] expectedColliders =
        [
            "floor",
            "north-bulkhead",
            "south-bulkhead",
            "west-bulkhead",
            "east-bulkhead",
        ];
        Assert.Equal(
            expectedColliders.Order(StringComparer.OrdinalIgnoreCase),
            arena.Primitives
                .Where(primitive => primitive.HasCollision)
                .Select(primitive => primitive.Id)
                .Order(StringComparer.OrdinalIgnoreCase));
        Assert.Single(arena.Primitives, primitive => primitive.CollisionRole == ArenaCollisionRole.Floor);
        Assert.Equal(4, arena.Primitives.Count(primitive =>
            primitive.CollisionRole == ArenaCollisionRole.OuterWall));
        Assert.DoesNotContain(
            arena.Props,
            prop => prop.Position.Y < 2.5f &&
                MathF.Abs(prop.Position.X) < 32f &&
                MathF.Abs(prop.Position.Z) < 24f);
        Assert.Contains(arena.Primitives, primitive => !string.IsNullOrWhiteSpace(primitive.TextureAsset));
        Assert.Empty(ArenaPlacementValidator.Validate(arena));
        Assert.False(arena.Primitives.Single(primitive => primitive.Id == "floor").IsVisible);
        Assert.DoesNotContain(arena.Primitives, primitive =>
            primitive.Id.Contains("truss", StringComparison.OrdinalIgnoreCase) ||
            primitive.Id.Contains("overhead", StringComparison.OrdinalIgnoreCase) ||
            primitive.Id.Contains("lane", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(arena.Props, prop =>
            prop.Id.Contains("craft", StringComparison.OrdinalIgnoreCase) ||
            prop.Id.Contains("balcony", StringComparison.OrdinalIgnoreCase) ||
            prop.Id.Contains("gallery", StringComparison.OrdinalIgnoreCase) ||
            prop.Id.Contains("rail", StringComparison.OrdinalIgnoreCase) ||
            prop.Id.Contains("machine", StringComparison.OrdinalIgnoreCase));
        Assert.All(arena.Props, prop =>
        {
            Assert.NotEqual(ArenaMountSurface.None, prop.MountSurface);
            Assert.True(prop.PlacementSize.X > 0f && prop.PlacementSize.Y > 0f && prop.PlacementSize.Z > 0f);
            Assert.True(prop.PlayerClearance >= 1.2f);
        });
        Assert.True(arena.PickupSpawns.Count(pickup => pickup.Type == PickupType.Health) >= 3);
        Assert.True(arena.PickupSpawns.Count(pickup => pickup.Type == PickupType.Ammo) >= 3);

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
    }

    [Fact]
    public void RobotRosterExposesAuthoredAiRolesVisualsAndBossPhases()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));
        string mgcb = File.ReadAllText(Path.Combine(root, "Content", "FpsFrenzyContent.mgcb"));
        EnemyBehavior[] regularBehaviors = catalog.Enemies.Values
            .Where(enemy => enemy.SchemaVersion == 2 && !enemy.IsBoss)
            .Select(enemy => enemy.Behavior)
            .Order()
            .ToArray();
        EnemyDefinition boss = catalog.Enemies["breach-walker"];

        Assert.Equal(Enum.GetValues<EnemyBehavior>().Where(behavior => behavior != EnemyBehavior.Boss).Order(),
            regularBehaviors);
        Assert.True(boss.IsBoss);
        Assert.Equal(ExpectedBossPhases, boss.BossPhases.Select(phase => phase.Id));
        Assert.All(boss.BossPhases.Skip(1), phase => Assert.True(phase.SummonCount > 0));
        Assert.All(catalog.Enemies.Values.Where(enemy => enemy.SchemaVersion == 2), enemy =>
        {
            Assert.StartsWith("Models/Enemies/Robots/", enemy.ModelAsset, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(enemy.Visual.AlbedoAsset));
            Assert.True(enemy.Visual.TargetHeight > 0f);
            Assert.True(enemy.Visual.CorpseLifetimeSeconds > 0f);
            Assert.Contains("death", enemy.Visual.AnimationBindings.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (string textureAsset in new[] { enemy.Visual.AlbedoAsset, enemy.Visual.EmissiveAsset }
                         .OfType<string>())
            {
                string sourcePath = Path.Combine(
                    root,
                    "Content",
                    $"{textureAsset}.png".Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(sourcePath),
                    $"Enemy '{enemy.Id}' references missing texture '{textureAsset}'.");
                Assert.Contains($"/build:{textureAsset}.png", mgcb, StringComparison.Ordinal);
            }
        });
        Assert.All(EmissiveRobotIds, enemyId =>
            Assert.False(string.IsNullOrWhiteSpace(catalog.Enemies[enemyId].Visual.EmissiveAsset)));
    }

    [Fact]
    public void ReleaseTemplateHasTenWavesAndBossEntry()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));
        WaveSetDefinition release = catalog.WaveSets["release-ten-plus-boss"];

        Assert.Equal(2, release.SchemaVersion);
        Assert.Equal(10, release.Waves.Count);
        Assert.NotNull(release.BossWave);
        Assert.Contains(release.BossWave!.SpawnGroups,
            group => catalog.Enemies[group.EnemyId].IsBoss && group.Count == 1);
    }

    [Fact]
    public void SelectedRobotModelAssetsAndRequiredClipsArePresent()
    {
        string root = FindRepositoryRoot();
        string weapon = Path.Combine(root, "Content", "Models", "Weapons", "blaster-a.fbx");
        string robotDirectory = Path.Combine(root, "Content", "Models", "Enemies", "Robots");
        string mgcb = File.ReadAllText(Path.Combine(root, "Content", "FpsFrenzyContent.mgcb"));

        Assert.True(File.Exists(weapon));
        foreach (string robot in new[] { "Leela.assbin", "Stan.assbin", "George.assbin", "Mike.assbin", "Enemy_EyeDrone.assbin", "Enemy_Trilobite.assbin" })
        {
            Assert.True(File.Exists(Path.Combine(robotDirectory, robot)));
        }
        Assert.False(File.Exists(Path.Combine(robotDirectory, "Enemy_EyeDrone.fbx")));
        Assert.False(File.Exists(Path.Combine(robotDirectory, "Enemy_Trilobite.fbx")));

        foreach (string clip in new[] { "Idle", "Run", "Punch", "HitRecieve_1", "Death" })
        {
            Assert.Contains(clip, GetMgcbBlock(mgcb, "Models/Enemies/Robots/Leela.assbin"), StringComparison.Ordinal);
        }

        string eyeDroneBlock = GetMgcbBlock(mgcb, "Models/Enemies/Robots/Enemy_EyeDrone.assbin");
        string trilobiteBlock = GetMgcbBlock(mgcb, "Models/Enemies/Robots/Enemy_Trilobite.assbin");
        Assert.Contains("/processorParam:RequiredAnimationClips=Idle,Charging,Attack,Hit,BackFlip", eyeDroneBlock, StringComparison.Ordinal);
        Assert.Contains("/processorParam:RequiredAnimationClips=Idle,Walk,Run,Attack,Hit,TurnOff", trilobiteBlock, StringComparison.Ordinal);
        Assert.Contains("/processorParam:NormalizeImportedBoneBasis=False", eyeDroneBlock, StringComparison.Ordinal);
        Assert.Contains("/processorParam:NormalizeImportedBoneBasis=False", trilobiteBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterArmature|", eyeDroneBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterArmature|", trilobiteBlock, StringComparison.Ordinal);
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
    public void PaletteModelsDisableMipmapsWhileDetailedRobotsRetainThem()
    {
        string root = FindRepositoryRoot();
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(Path.Combine(root, "Content", "Data"));
        string mgcb = File.ReadAllText(Path.Combine(root, "Content", "FpsFrenzyContent.mgcb"));
        IEnumerable<string> paletteModels = catalog.Weapons.Values.Select(weapon => weapon.ModelAsset)
            .Concat(catalog.Arenas.Values.SelectMany(arena => arena.Props).Select(prop => prop.ModelAsset))
            .Concat(
            [
                "Models/Pickups/health-crate",
                "Models/Pickups/ammo-cache",
                "Models/Arenas/OrbitalDepot/Station/table-display-small",
                "Models/Arenas/OrbitalDepot/Station/container-flat-open",
                "Models/Arenas/OrbitalDepot/Station/pipe-ring-colored",
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        Assert.All(paletteModels, modelAsset =>
        {
            string block = GetMgcbBlock(mgcb, ResolveModelSource(mgcb, modelAsset));
            Assert.Contains("/processorParam:GenerateMipmaps=False", block, StringComparison.Ordinal);
            Assert.DoesNotContain("/processorParam:GenerateMipmaps=True", block, StringComparison.Ordinal);
        });
        Assert.All(catalog.Enemies.Values.Where(enemy => enemy.SchemaVersion == 2), enemy =>
        {
            string block = GetMgcbBlock(mgcb, ResolveModelSource(mgcb, enemy.ModelAsset));
            Assert.Contains("/processorParam:GenerateMipmaps=True", block, StringComparison.Ordinal);
            Assert.Equal(TextureSamplingMode.LinearMipmapped, enemy.Visual.TextureSampling);
        });
    }

    [Fact]
    public void ThirdPartyManifestRecordsLicenseProvenance()
    {
        string root = FindRepositoryRoot();
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "Content", "ThirdParty", "asset-manifest.json")));
        JsonElement assets = manifest.RootElement.GetProperty("assets");

        string[] expectedPacks =
        [
            "Kenney Blaster Kit",
            "Quaternius Sci-Fi Modular Gun Pack",
            "Quaternius Animated Robot Pack",
            "Quaternius Ultimate Monsters",
            "Kenney Survival Kit",
            "Kenney Modular Space Kit",
            "Kenney Space Station Kit",
            "Kenney Space Kit",
            "Kenney Prototype Textures",
            "Kenney UI Pack - Sci-Fi",
            "FPS Frenzy Title Menu Key Art",
            "Quaternius Animated Mech Pack",
            "Quaternius Sci-Fi Essentials Kit - Standard",
            "Kenney Sci-Fi Sounds",
            "Kenney Digital Audio",
            "Kenney UI Audio",
            "OpenGameArt Dark Sci-Fi Audio Pack",
            "Oxanium",
        ];

        Assert.Equal(expectedPacks.Length, assets.GetArrayLength());
        Assert.Equal(
            expectedPacks.Order(),
            assets.EnumerateArray().Select(asset => asset.GetProperty("pack").GetString()).Order());
        Assert.All(assets.EnumerateArray(), asset =>
        {
            string pack = asset.GetProperty("pack").GetString()!;
            string expectedLicense = pack switch
            {
                "Oxanium" => "SIL Open Font License 1.1",
                "FPS Frenzy Title Menu Key Art" =>
                    "Original project-generated asset; no third-party source image",
                _ => "CC0 1.0",
            };
            Assert.Equal(expectedLicense, asset.GetProperty("license").GetString());
            Assert.False(string.IsNullOrWhiteSpace(asset.GetProperty("source").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(asset.GetProperty("version").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(asset.GetProperty("modifications").GetString()));
            string localRoot = asset.GetProperty("localRoot").GetString()!;
            JsonElement originals = asset.GetProperty("originalFiles");
            JsonElement locals = asset.GetProperty("localFiles");
            Assert.True(originals.GetArrayLength() > 0 || pack == "FPS Frenzy Title Menu Key Art");
            Assert.True(locals.GetArrayLength() > 0);
            foreach (JsonElement localFile in locals.EnumerateArray())
            {
                string path = Path.Combine(
                    root,
                    localRoot.Replace('/', Path.DirectorySeparatorChar),
                    localFile.GetString()!.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), $"Manifest local file does not exist: {path}");
            }

            if (asset.TryGetProperty("retainedSourceFiles", out JsonElement retainedSources))
            {
                string retainedRoot = asset.GetProperty("retainedSourceRoot").GetString()!;
                foreach (JsonElement retainedSource in retainedSources.EnumerateArray())
                {
                    string path = Path.Combine(
                        root,
                        retainedRoot.Replace('/', Path.DirectorySeparatorChar),
                        retainedSource.GetString()!.Replace('/', Path.DirectorySeparatorChar));
                    Assert.True(File.Exists(path), $"Manifest retained source does not exist: {path}");
                }
            }
        });

        JsonElement animatedMechs = assets.EnumerateArray().Single(
            asset => asset.GetProperty("pack").GetString() == "Quaternius Animated Mech Pack");
        Assert.Equal(4, animatedMechs.GetProperty("sourceDownloads").EnumerateObject().Count());
        Assert.Equal(4, animatedMechs.GetProperty("retainedSourceFiles").GetArrayLength());
        JsonElement conversion = animatedMechs.GetProperty("conversion");
        Assert.Contains("AssimpNet 5.0.0", conversion.GetProperty("tool").GetString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            root,
            conversion.GetProperty("script").GetString()!.Replace('/', Path.DirectorySeparatorChar))));

        JsonElement sciFiEssentials = assets.EnumerateArray().Single(
            asset => asset.GetProperty("pack").GetString() == "Quaternius Sci-Fi Essentials Kit - Standard");
        Assert.Equal(10, sciFiEssentials.GetProperty("retainedSourceFiles").GetArrayLength());
        Assert.Contains(
            sciFiEssentials.GetProperty("retainedSourceFiles").EnumerateArray().Select(file => file.GetString()),
            file => file == "Enemy_EyeDrone.bin");
        Assert.Contains(
            sciFiEssentials.GetProperty("retainedSourceFiles").EnumerateArray().Select(file => file.GetString()),
            file => file == "Enemy_Trilobite.bin");
        JsonElement sciFiConversion = sciFiEssentials.GetProperty("conversion");
        Assert.Contains(
            sciFiConversion.GetProperty("options").EnumerateArray().Select(option => option.GetString()),
            option => option!.Contains("no animation-key", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(
            root,
            sciFiConversion.GetProperty("script").GetString()!.Replace('/', Path.DirectorySeparatorChar))));
        string sciFiSourceRoot = sciFiEssentials.GetProperty("retainedSourceRoot").GetString()!;
        foreach (JsonProperty expectedHash in sciFiEssentials.GetProperty("retainedSourceSha256").EnumerateObject())
        {
            string sourcePath = Path.Combine(
                root,
                sciFiSourceRoot.Replace('/', Path.DirectorySeparatorChar),
                expectedHash.Name);
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)));
            Assert.Equal(expectedHash.Value.GetString(), actualHash);
        }
    }

    [Fact]
    public void CanonicalRobotSourcesAndDerivedModelsUseGitLfs()
    {
        string attributes = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".gitattributes"));

        Assert.Contains("*.gltf filter=lfs diff=lfs merge=lfs -text", attributes, StringComparison.Ordinal);
        Assert.Contains("*.bin filter=lfs diff=lfs merge=lfs -text", attributes, StringComparison.Ordinal);
        Assert.Contains("*.assbin filter=lfs diff=lfs merge=lfs -text", attributes, StringComparison.Ordinal);
    }

    private static string ResolveModelSource(string mgcb, string modelAsset)
    {
        foreach (string extension in new[] { ".assbin", ".gltf", ".fbx" })
        {
            string candidate = $"{modelAsset}{extension}";
            if (mgcb.Contains($"#begin {candidate}", StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        Assert.Fail($"Missing MGCB model source for '{modelAsset}'.");
        return string.Empty;
    }

    private static string GetMgcbBlock(string mgcb, string assetPath)
    {
        string marker = $"#begin {assetPath}";
        int start = mgcb.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing MGCB block for '{assetPath}'.");
        int end = mgcb.IndexOf("#begin ", start + marker.Length, StringComparison.Ordinal);
        return end < 0 ? mgcb[start..] : mgcb[start..end];
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
