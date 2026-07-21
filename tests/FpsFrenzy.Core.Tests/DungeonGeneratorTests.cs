using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Core.Tests;

public sealed class DungeonGeneratorTests
{
    [Fact]
    public void CanonicalSeedsReconstructExactlyAndVaryAcrossSeeds()
    {
        AdventureDefinition adventure = LoadAdventure();
        GeneratedDungeonFloor first = DungeonGenerator.Generate(adventure, 1, 0, "pcg-v2");
        GeneratedDungeonFloor reconstruction = DungeonGenerator.Generate(adventure, 1, 0, "pcg-v2");
        GeneratedDungeonFloor different = DungeonGenerator.Generate(adventure, 8675309, 0, "pcg-v2");

        Assert.Equal(first.LayoutHash, reconstruction.LayoutHash);
        Assert.Equal(
            first.Rooms.Select(room => (room.Index, room.Role, room.GridX, room.GridZ,
                room.GridWidth, room.GridDepth, Connections: string.Join(',', room.Connections))),
            reconstruction.Rooms.Select(room => (room.Index, room.Role, room.GridX, room.GridZ,
                room.GridWidth, room.GridDepth, Connections: string.Join(',', room.Connections))));
        Assert.Equal(first.Minimap.WalkableCells, reconstruction.Minimap.WalkableCells);
        Assert.NotEqual(first.LayoutHash, different.LayoutHash);
    }

    [Fact]
    public void CanonicalFloorHashesStayVersioned()
    {
        AdventureDefinition adventure = LoadAdventure();

        string[] hashes = Enumerable.Range(0, 3)
            .Select(floor => DungeonGenerator.Generate(adventure, 424242, floor, "pcg-v2").LayoutHash)
            .ToArray();

        Assert.Equal(["d37a3ec1aebdd24c", "e3f94257d2267b08", "459456eda1603bac"], hashes);
    }

    [Fact]
    public void ThreeThousandCanonicalFloorsAreConnectedClearAndBounded()
    {
        AdventureDefinition adventure = LoadAdventure();
        for (int floorIndex = 0; floorIndex < adventure.Floors.Count; floorIndex++)
        {
            DungeonFloorRecipe recipe = adventure.Floors[floorIndex];
            for (int seed = 1; seed <= 1000; seed++)
            {
                GeneratedDungeonFloor floor = DungeonGenerator.Generate(adventure, seed, floorIndex, "pcg-v2");
                Assert.Equal(recipe.RoomCount, floor.Rooms.Count);
                Assert.Empty(DungeonGenerator.Validate(floor));
                Assert.Equal(GameSimulation.PlayerEyeHeight, floor.Arena.PlayerSpawn.Y);
                Assert.Contains(floor.Interactables,
                    interactable => interactable.Kind == AdventureInteractableKind.EquipmentCache);
                Assert.Contains(floor.Interactables,
                    interactable => interactable.Kind == AdventureInteractableKind.LoreTerminal);
                Assert.Equal(recipe.ChestCount, floor.Interactables.Count(
                    interactable => interactable.Kind == AdventureInteractableKind.EquipmentCache));
                Assert.All(floor.EnemyGroups.SelectMany(group => group.Members),
                    member => Assert.InRange(member.Count, 1, 8));
            }
        }
    }

    [Fact]
    public void ZeroAttemptsUsesValidatedFixedFallback()
    {
        AdventureDefinition adventure = LoadAdventure();

        GeneratedDungeonFloor floor = DungeonGenerator.Generate(adventure, 99, 1, "pcg-v2", maximumAttempts: 0);

        Assert.True(floor.UsedFallback);
        Assert.Equal(DungeonGenerator.MaximumAttempts, floor.AttemptSalt);
        Assert.Empty(DungeonGenerator.Validate(floor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveSeedsAreRejected(int seed)
    {
        AdventureDefinition adventure = LoadAdventure();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DungeonGenerator.Generate(adventure, seed, 0, "pcg-v2"));
    }

    [Fact]
    public void UnsupportedGeneratorVersionIsRejected()
    {
        AdventureDefinition adventure = LoadAdventure();
        Assert.Throws<NotSupportedException>(() =>
            DungeonGenerator.Generate(adventure, 1, 0, "pcg-unsupported"));
    }

    private static AdventureDefinition LoadAdventure()
    {
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
        return catalog.Adventures["null-signal"];
    }
}
