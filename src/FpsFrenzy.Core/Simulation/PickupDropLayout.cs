using System.Numerics;

namespace FpsFrenzy.Core.Simulation;

internal static class PickupDropLayout
{
    internal const float MinimumSpacing = 1.15f;
    private const int MaximumRings = 5;

    internal static Vector3 FindOpenPosition(
        Vector3 desired,
        IEnumerable<Vector3> occupiedPositions,
        Vector3 boundsMin,
        Vector3 boundsMax)
    {
        Vector2[] occupied = occupiedPositions
            .Select(position => new Vector2(position.X, position.Z))
            .ToArray();
        Vector3 boundedDesired = ClampToArena(desired, boundsMin, boundsMax);
        if (IsOpen(boundedDesired, occupied))
        {
            return boundedDesired;
        }

        for (int ring = 1; ring <= MaximumRings; ring++)
        {
            int candidateCount = ring * 8;
            float radius = MinimumSpacing * ring;
            for (int index = 0; index < candidateCount; index++)
            {
                float angle = (index + ((ring & 1) * 0.5f)) * (MathF.Tau / candidateCount);
                Vector3 candidate = ClampToArena(
                    desired + new Vector3(MathF.Sin(angle) * radius, 0f, MathF.Cos(angle) * radius),
                    boundsMin,
                    boundsMax);
                if (IsOpen(candidate, occupied))
                {
                    return candidate;
                }
            }
        }

        return boundedDesired;
    }

    private static Vector3 ClampToArena(Vector3 position, Vector3 boundsMin, Vector3 boundsMax) => new(
        Math.Clamp(position.X, boundsMin.X + 0.75f, boundsMax.X - 0.75f),
        position.Y,
        Math.Clamp(position.Z, boundsMin.Z + 0.75f, boundsMax.Z - 0.75f));

    private static bool IsOpen(Vector3 candidate, IReadOnlyList<Vector2> occupied)
    {
        Vector2 horizontal = new(candidate.X, candidate.Z);
        float minimumDistanceSquared = MinimumSpacing * MinimumSpacing;
        return occupied.All(position => Vector2.DistanceSquared(horizontal, position) >= minimumDistanceSquared);
    }
}
