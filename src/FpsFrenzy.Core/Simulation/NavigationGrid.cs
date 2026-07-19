using System.Numerics;
using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Core.Simulation;

public sealed class NavigationGrid
{
    private readonly Vector2 _minimum;
    private readonly float _cellSize;
    private readonly int _width;
    private readonly int _height;
    private readonly bool[] _blocked;
    private readonly PriorityQueue<int, float> _frontier;
    private readonly float[] _costs;
    private readonly int[] _previous;

    public NavigationGrid(ArenaDefinition arena, float clearanceRadius = 0.65f)
    {
        _minimum = new Vector2(arena.BoundsMin.X, arena.BoundsMin.Z);
        _cellSize = arena.NavigationCellSize;
        _width = Math.Max(1, (int)MathF.Ceiling((arena.BoundsMax.X - arena.BoundsMin.X) / _cellSize));
        _height = Math.Max(1, (int)MathF.Ceiling((arena.BoundsMax.Z - arena.BoundsMin.Z) / _cellSize));
        _blocked = new bool[_width * _height];
        _frontier = new PriorityQueue<int, float>(_blocked.Length);
        _costs = new float[_blocked.Length];
        _previous = new int[_blocked.Length];

        foreach (ArenaPrimitiveDefinition primitive in arena.Primitives)
        {
            if (!primitive.IsNavigationObstacle)
            {
                continue;
            }

            float top = primitive.Position.Y + (primitive.Size.Y * 0.5f);
            if (top <= 0.2f || primitive.Id.Equals("floor", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            float minimumX = primitive.Position.X - (primitive.Size.X * 0.5f) - clearanceRadius;
            float maximumX = primitive.Position.X + (primitive.Size.X * 0.5f) + clearanceRadius;
            float minimumZ = primitive.Position.Z - (primitive.Size.Z * 0.5f) - clearanceRadius;
            float maximumZ = primitive.Position.Z + (primitive.Size.Z * 0.5f) + clearanceRadius;

            for (int z = 0; z < _height; z++)
            {
                for (int x = 0; x < _width; x++)
                {
                    Vector3 center = ToWorld(x, z, 0f);
                    if (center.X >= minimumX && center.X <= maximumX && center.Z >= minimumZ && center.Z <= maximumZ)
                    {
                        _blocked[ToIndex(x, z)] = true;
                    }
                }
            }
        }
    }

    public List<Vector3> FindPath(Vector3 start, Vector3 goal)
    {
        List<Vector3> path = [];
        FindPath(start, goal, path);
        return path;
    }

    public void FindPath(Vector3 start, Vector3 goal, List<Vector3> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        (int startX, int startZ) = ToCell(start);
        (int goalX, int goalZ) = ToCell(goal);
        startX = Math.Clamp(startX, 0, _width - 1);
        startZ = Math.Clamp(startZ, 0, _height - 1);
        goalX = Math.Clamp(goalX, 0, _width - 1);
        goalZ = Math.Clamp(goalZ, 0, _height - 1);

        int startIndex = FindNearestWalkable(startX, startZ);
        int goalIndex = FindNearestWalkable(goalX, goalZ);
        if (startIndex < 0 || goalIndex < 0)
        {
            return;
        }

        _frontier.Clear();
        Array.Fill(_costs, float.PositiveInfinity);
        Array.Fill(_previous, -1);
        _costs[startIndex] = 0f;
        _frontier.Enqueue(startIndex, 0f);

        ReadOnlySpan<(int X, int Z)> offsets = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (_frontier.TryDequeue(out int current, out _))
        {
            if (current == goalIndex)
            {
                break;
            }

            (int currentX, int currentZ) = FromIndex(current);
            foreach ((int offsetX, int offsetZ) in offsets)
            {
                int nextX = currentX + offsetX;
                int nextZ = currentZ + offsetZ;
                if (!IsWalkable(nextX, nextZ))
                {
                    continue;
                }

                int next = ToIndex(nextX, nextZ);
                float nextCost = _costs[current] + 1f;
                if (nextCost >= _costs[next])
                {
                    continue;
                }

                _costs[next] = nextCost;
                _previous[next] = current;
                float heuristic = Math.Abs(nextX - goalX) + Math.Abs(nextZ - goalZ);
                _frontier.Enqueue(next, nextCost + heuristic);
            }
        }

        if (startIndex != goalIndex && _previous[goalIndex] < 0)
        {
            return;
        }

        for (int current = goalIndex; current >= 0; current = _previous[current])
        {
            (int x, int z) = FromIndex(current);
            destination.Add(ToWorld(x, z, start.Y));
            if (current == startIndex)
            {
                break;
            }
        }

        destination.Reverse();
    }

    public bool IsWalkable(Vector3 position)
    {
        (int x, int z) = ToCell(position);
        return IsWalkable(x, z);
    }

    private int FindNearestWalkable(int originX, int originZ)
    {
        for (int radius = 0; radius <= 6; radius++)
        {
            for (int z = originZ - radius; z <= originZ + radius; z++)
            {
                for (int x = originX - radius; x <= originX + radius; x++)
                {
                    if (IsWalkable(x, z))
                    {
                        return ToIndex(x, z);
                    }
                }
            }
        }

        return -1;
    }

    private bool IsWalkable(int x, int z) => x >= 0 && z >= 0 && x < _width && z < _height && !_blocked[ToIndex(x, z)];
    private int ToIndex(int x, int z) => (z * _width) + x;
    private (int X, int Z) FromIndex(int index) => (index % _width, index / _width);
    private (int X, int Z) ToCell(Vector3 position) => ((int)((position.X - _minimum.X) / _cellSize), (int)((position.Z - _minimum.Y) / _cellSize));
    private Vector3 ToWorld(int x, int z, float y) => new(_minimum.X + ((x + 0.5f) * _cellSize), y, _minimum.Y + ((z + 0.5f) * _cellSize));
}
