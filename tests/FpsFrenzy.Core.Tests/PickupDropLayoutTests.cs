using System.Numerics;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Core.Tests;

public sealed class PickupDropLayoutTests
{
    [Fact]
    public void FindsASeparatedGroundPositionWhenSeveralDropsShareAnOrigin()
    {
        Vector3 desired = new(4f, 0.5f, -3f);
        List<Vector3> placed = [];

        for (int index = 0; index < 10; index++)
        {
            Vector3 position = PickupDropLayout.FindOpenPosition(
                desired,
                placed,
                new Vector3(-36f, 0f, -28f),
                new Vector3(36f, 10f, 28f));
            placed.Add(position);
        }

        for (int first = 0; first < placed.Count; first++)
        {
            Assert.Equal(desired.Y, placed[first].Y);
            for (int second = first + 1; second < placed.Count; second++)
            {
                Vector2 a = new(placed[first].X, placed[first].Z);
                Vector2 b = new(placed[second].X, placed[second].Z);
                Assert.True(Vector2.Distance(a, b) >= PickupDropLayout.MinimumSpacing - 0.001f);
            }
        }
    }

    [Fact]
    public void KeepsDropsInsideTheArenaEdgeMargin()
    {
        Vector3 position = PickupDropLayout.FindOpenPosition(
            new Vector3(100f, 0.2f, -100f),
            [],
            new Vector3(-10f, 0f, -8f),
            new Vector3(10f, 4f, 8f));

        Assert.Equal(new Vector3(9.25f, 0.2f, -7.25f), position);
    }
}
