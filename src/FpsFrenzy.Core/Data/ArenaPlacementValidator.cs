using System.Numerics;

namespace FpsFrenzy.Core.Data;

/// <summary>
/// Validates the explicit, axis-aligned presentation volumes used by cleaned authored arenas.
/// Boundary contact is intentional; positive-volume intersections are not.
/// </summary>
public static class ArenaPlacementValidator
{
    private const float Epsilon = 0.001f;

    public static IReadOnlyList<string> Validate(ArenaDefinition arena)
    {
        List<string> errors = [];
        List<PlacementVolume> volumes = [];

        foreach (ArenaPrimitiveDefinition primitive in arena.Primitives.Where(value => value.IsVisible))
        {
            PlacementVolume volume = new(primitive.Id, primitive.Position, primitive.Size, null);
            ValidateInside(arena, volume, errors);
            volumes.Add(volume);
        }

        foreach (ArenaPropDefinition prop in arena.Props)
        {
            if (prop.PlacementSize.X <= 0f || prop.PlacementSize.Y <= 0f || prop.PlacementSize.Z <= 0f)
            {
                errors.Add($"Arena '{arena.Id}' prop '{prop.Id}' needs positive placement bounds.");
                continue;
            }

            if (prop.MountSurface == ArenaMountSurface.None)
            {
                errors.Add($"Arena '{arena.Id}' prop '{prop.Id}' needs mount-surface metadata.");
            }

            if (prop.PlayerClearance < 1.2f)
            {
                errors.Add($"Arena '{arena.Id}' prop '{prop.Id}' must preserve at least 1.2m player clearance.");
            }

            PlacementVolume volume = new(prop.Id, prop.Position, prop.PlacementSize, prop.MountSurface);
            ValidateInside(arena, volume, errors);
            ValidateMount(arena, volume, errors);
            volumes.Add(volume);
        }

        for (int leftIndex = 0; leftIndex < volumes.Count; leftIndex++)
        {
            PlacementVolume left = volumes[leftIndex];
            for (int rightIndex = leftIndex + 1; rightIndex < volumes.Count; rightIndex++)
            {
                PlacementVolume right = volumes[rightIndex];
                if (IntersectsWithPositiveVolume(left, right))
                {
                    errors.Add($"Arena '{arena.Id}' visible volumes '{left.Id}' and '{right.Id}' overlap.");
                }
            }
        }

        return errors;
    }

    private static void ValidateInside(ArenaDefinition arena, PlacementVolume volume, List<string> errors)
    {
        Vector3 minimum = volume.Minimum;
        Vector3 maximum = volume.Maximum;
        if (minimum.X < arena.BoundsMin.X - Epsilon || minimum.Y < arena.BoundsMin.Y - Epsilon ||
            minimum.Z < arena.BoundsMin.Z - Epsilon || maximum.X > arena.BoundsMax.X + Epsilon ||
            maximum.Y > arena.BoundsMax.Y + Epsilon || maximum.Z > arena.BoundsMax.Z + Epsilon)
        {
            errors.Add($"Arena '{arena.Id}' visible volume '{volume.Id}' exceeds arena bounds.");
        }
    }

    private static void ValidateMount(ArenaDefinition arena, PlacementVolume prop, List<string> errors)
    {
        if (prop.MountSurface is null or ArenaMountSurface.None)
        {
            return;
        }

        ArenaPrimitiveDefinition? wall = prop.MountSurface switch
        {
            ArenaMountSurface.NorthWall => arena.Primitives
                .Where(IsOuterWall).MinBy(value => value.Position.Z),
            ArenaMountSurface.SouthWall => arena.Primitives
                .Where(IsOuterWall).MaxBy(value => value.Position.Z),
            ArenaMountSurface.WestWall => arena.Primitives
                .Where(IsOuterWall).MinBy(value => value.Position.X),
            ArenaMountSurface.EastWall => arena.Primitives
                .Where(IsOuterWall).MaxBy(value => value.Position.X),
            _ => null,
        };

        bool mounted = prop.MountSurface switch
        {
            ArenaMountSurface.Ground => MathF.Abs(prop.Minimum.Y - arena.BoundsMin.Y) <= Epsilon,
            ArenaMountSurface.NorthWall when wall is not null =>
                MathF.Abs(prop.Minimum.Z - Maximum(wall).Z) <= Epsilon,
            ArenaMountSurface.SouthWall when wall is not null =>
                MathF.Abs(prop.Maximum.Z - Minimum(wall).Z) <= Epsilon,
            ArenaMountSurface.WestWall when wall is not null =>
                MathF.Abs(prop.Minimum.X - Maximum(wall).X) <= Epsilon,
            ArenaMountSurface.EastWall when wall is not null =>
                MathF.Abs(prop.Maximum.X - Minimum(wall).X) <= Epsilon,
            _ => false,
        };

        if (!mounted)
        {
            errors.Add($"Arena '{arena.Id}' prop '{prop.Id}' is not flush with its declared {prop.MountSurface} mount.");
        }
    }

    private static bool IsOuterWall(ArenaPrimitiveDefinition value) =>
        value.CollisionRole == ArenaCollisionRole.OuterWall;

    private static Vector3 Minimum(ArenaPrimitiveDefinition value) => value.Position - (value.Size * 0.5f);

    private static Vector3 Maximum(ArenaPrimitiveDefinition value) => value.Position + (value.Size * 0.5f);

    private static bool IntersectsWithPositiveVolume(PlacementVolume left, PlacementVolume right) =>
        MathF.Min(left.Maximum.X, right.Maximum.X) - MathF.Max(left.Minimum.X, right.Minimum.X) > Epsilon &&
        MathF.Min(left.Maximum.Y, right.Maximum.Y) - MathF.Max(left.Minimum.Y, right.Minimum.Y) > Epsilon &&
        MathF.Min(left.Maximum.Z, right.Maximum.Z) - MathF.Max(left.Minimum.Z, right.Minimum.Z) > Epsilon;

    private readonly record struct PlacementVolume(
        string Id,
        Vector3 Position,
        Vector3 Size,
        ArenaMountSurface? MountSurface)
    {
        public Vector3 Minimum => Position - (Size * 0.5f);
        public Vector3 Maximum => Position + (Size * 0.5f);
    }
}
