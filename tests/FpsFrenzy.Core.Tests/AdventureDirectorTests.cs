using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Core.Tests;

public sealed class AdventureDirectorTests
{
    [Fact]
    public void FloorRequiresObjectivesThenBoonBeforeAdvancing()
    {
        AdventureDefinition adventure = LoadAdventure();
        AdventureDirector director = new(adventure, 77);
        GeneratedDungeonFloor floor = director.BeginGeneratedFloor();
        GeneratedDungeonInteractable lift = Assert.Single(floor.Interactables,
            item => item.Kind == AdventureInteractableKind.Lift);

        Assert.False(director.TryInteract(lift.Id));
        foreach (GeneratedDungeonInteractable objective in floor.Interactables.Where(item => item.ObjectiveId is not null))
        {
            Assert.True(director.TryInteract(objective.Id));
        }

        Assert.True(director.IsStageComplete);
        Assert.True(director.TryInteract(lift.Id));
        Assert.Equal(AdventureRunPhase.FloorReward, director.Snapshot().Phase);
        Assert.True(director.SelectFloorBoon("boon-overclock"));
        Assert.Equal(1, director.StageIndex);
        Assert.Equal(AdventureRunPhase.Exploring, director.Snapshot().Phase);
    }

    [Fact]
    public void FabricationObjectivesEnforceCommandKeyDependency()
    {
        AdventureDefinition adventure = LoadAdventure();
        AdventureCheckpoint checkpoint = new()
        {
            AdventureId = adventure.Id,
            Seed = 88,
            GeneratorVersion = adventure.GeneratorVersion,
            NextStageIndex = 1,
        };
        AdventureDirector director = new(adventure, 88, checkpoint);
        GeneratedDungeonFloor floor = director.BeginGeneratedFloor();
        GeneratedDungeonInteractable key = Assert.Single(floor.Interactables,
            item => item.Kind == AdventureInteractableKind.CommandKey);
        GeneratedDungeonInteractable fabricator = floor.Interactables.First(item =>
            item.Kind == AdventureInteractableKind.FabricatorConsole);

        Assert.False(director.TryInteract(fabricator.Id));
        Assert.True(director.TryInteract(key.Id));
        Assert.True(director.TryInteract(fabricator.Id));
    }

    [Fact]
    public void BossRemainsInvulnerableUntilBothControlsAreDisabled()
    {
        AdventureDefinition adventure = LoadAdventure();
        AdventureCheckpoint checkpoint = new()
        {
            AdventureId = adventure.Id,
            Seed = 99,
            GeneratorVersion = adventure.GeneratorVersion,
            NextStageIndex = 3,
        };
        AdventureDirector director = new(adventure, 99, checkpoint);
        director.BeginBossStage();

        Assert.True(director.BossInvulnerable);
        Assert.True(director.TryDisableBossShieldControl("shield-a"));
        Assert.True(director.BossInvulnerable);
        Assert.False(director.TryDisableBossShieldControl("shield-a"));
        Assert.True(director.TryDisableBossShieldControl("shield-b"));
        Assert.False(director.BossInvulnerable);
        Assert.Equal(AdventureRunPhase.BossActive, director.Snapshot().Phase);
        Assert.True(director.NotifyBossDefeated());
        Assert.Equal(AdventureRunPhase.Victory, director.Snapshot().Phase);
    }

    [Fact]
    public void AlertedPatrolCapNeverExceedsEightEnemies()
    {
        AdventureDefinition adventure = LoadAdventure();
        AdventureDirector director = new(adventure, 123);
        GeneratedDungeonFloor floor = director.BeginGeneratedFloor();

        foreach (GeneratedDungeonEnemyGroup group in floor.EnemyGroups)
        {
            director.TryAlertGroup(group.Id);
        }

        HashSet<string> alerted = director.Snapshot().AlertedGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int count = floor.EnemyGroups.Where(group => alerted.Contains(group.Id))
            .Sum(group => group.Members.Sum(member => member.Count));
        Assert.InRange(count, 0, AdventureDirector.MaximumAlertedEnemies);
    }

    [Fact]
    public void CheckpointOnlyPersistsCommittedStageEntryState()
    {
        AdventureDefinition adventure = LoadAdventure();
        AdventureDirector director = new(adventure, 321);
        GeneratedDungeonFloor floor = director.BeginGeneratedFloor();
        director.DiscoverRoom(2);
        director.TryAlertGroup(floor.EnemyGroups[0].Id);

        AdventureCheckpoint checkpoint = director.CreateEntryCheckpoint(
            DifficultyMode.Hard, ThreatTier.TierIII, true, floor.LayoutHash);

        Assert.Equal(0, checkpoint.NextStageIndex);
        Assert.Equal(floor.LayoutHash, checkpoint.EntryLayoutHash);
        Assert.Empty(checkpoint.CommittedRewardItemIds);
        Assert.DoesNotContain("visited", checkpoint.GetType().GetProperties()
            .Select(property => property.Name), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("enemy", checkpoint.GetType().GetProperties()
            .Select(property => property.Name), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("gate", checkpoint.GetType().GetProperties()
            .Select(property => property.Name), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapRevealsCorridorsWithoutPersistingThemAndRestoresCommittedDiscoveryTotals()
    {
        AdventureDefinition adventure = LoadAdventure();
        AdventureCheckpoint checkpoint = new()
        {
            AdventureId = adventure.Id,
            Seed = 654321,
            GeneratorVersion = adventure.GeneratorVersion,
            SecretsFound = 2,
            LoreFound = 1,
        };
        AdventureDirector director = new(adventure, checkpoint.Seed, checkpoint);
        GeneratedDungeonFloor floor = director.BeginGeneratedFloor();
        HashSet<DungeonGridCell> roomCells = floor.Minimap.RoomCells.Values.SelectMany(cells => cells).ToHashSet();
        DungeonGridCell corridor = floor.Minimap.WalkableCells.First(cell => !roomCells.Contains(cell));

        Assert.True(director.DiscoverMapCell(corridor));
        AdventureSnapshot snapshot = director.Snapshot();
        Assert.Contains(corridor, snapshot.DiscoveredMapCells);
        Assert.Equal(2, snapshot.SecretsFound);
        Assert.Equal(1, snapshot.LoreFound);
        Assert.DoesNotContain("map", checkpoint.GetType().GetProperties()
            .Select(property => property.Name), StringComparer.OrdinalIgnoreCase);
    }

    private static AdventureDefinition LoadAdventure()
    {
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
        return catalog.Adventures["null-signal"];
    }
}
