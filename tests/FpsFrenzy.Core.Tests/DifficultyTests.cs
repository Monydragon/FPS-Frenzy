using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Core.Tests;

public sealed class DifficultyTests
{
    [Fact]
    public void CatalogDefinesSixOrderedCombatPackages()
    {
        Assert.Equal(
            [DifficultyMode.Casual, DifficultyMode.Easy, DifficultyMode.Normal,
                DifficultyMode.Hard, DifficultyMode.VeryHard, DifficultyMode.Extreme],
            DifficultyCatalog.All.Select(definition => definition.Mode));
        Assert.Equal(0.70f, DifficultyCatalog.Get(DifficultyMode.Casual).EnemyHealthMultiplier);
        Assert.Equal(1.62f, DifficultyCatalog.Get(DifficultyMode.Extreme).EnemyDamageMultiplier);
        Assert.Equal(DifficultyMode.Normal, DifficultyCatalog.Normalize(DifficultyMode.Standard));
    }

    [Theory]
    [InlineData(DifficultyMode.Casual, 0.70f)]
    [InlineData(DifficultyMode.Normal, 1.00f)]
    [InlineData(DifficultyMode.Extreme, 1.32f)]
    public void EnemyHealthComposesDifficultyWithThreatTier(DifficultyMode difficulty, float expectedDifficultyScale)
    {
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Seed = 1337,
            Difficulty = difficulty,
            ThreatTier = ThreatTier.TierI,
            IsFirstRun = true,
        });

        for (int tick = 0; tick < 90 && simulation.Enemies.Count == 0; tick++)
        {
            simulation.Step([]);
        }

        EnemyState enemy = Assert.Single(simulation.Enemies.Take(1));
        Assert.Equal(enemy.Definition.MaxHealth * expectedDifficultyScale, enemy.MaximumHealth, 3);
    }

    [Fact]
    public void CheckpointPreservesNamedDifficultySeparatelyFromThreatTier()
    {
        ContentCatalog catalog = ContentCatalog.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Content", "Data"));
        using GameSimulation simulation = new(catalog, new RunConfiguration
        {
            ArenaId = "orbital-depot",
            Difficulty = DifficultyMode.VeryHard,
            ThreatTier = ThreatTier.TierI,
            IsFirstRun = true,
        });

        RunCheckpoint checkpoint = Assert.IsType<RunCheckpoint>(simulation.CreateRunCheckpoint());
        Assert.Equal(DifficultyMode.VeryHard, checkpoint.Difficulty);
        Assert.Equal(ThreatTier.TierI, checkpoint.ThreatTier);
    }
}
