using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Core.Simulation;

/// <summary>
/// Deterministic, integer-only topology generator for Adventure floors. All random
/// decisions are made before converting grid coordinates to floating-point world space.
/// </summary>
public static class DungeonGenerator
{
    public const string CurrentGeneratorVersion = "pcg-v2";
    public const int MinimumSeed = 1;
    public const int MaximumSeed = int.MaxValue;
    public const int MaximumAttempts = 32;
    public const float CellSize = 2f;
    public const int GridWidth = 48;
    public const int GridDepth = 40;

    private const int SlotColumns = 7;
    private const int SlotRows = 5;
    private const int SlotStrideX = 6;
    private const int SlotStrideZ = 7;

    public static GeneratedDungeonFloor Generate(
        AdventureDefinition adventure,
        int seed,
        int floorIndex,
        string generatorVersion,
        int maximumAttempts = MaximumAttempts)
    {
        ArgumentNullException.ThrowIfNull(adventure);
        if (seed is < MinimumSeed or > MaximumSeed)
        {
            throw new ArgumentOutOfRangeException(nameof(seed),
                $"Adventure seeds must be between {MinimumSeed:N0} and {MaximumSeed:N0}.");
        }

        if (floorIndex < 0 || floorIndex >= adventure.Floors.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(floorIndex));
        }

        if (!string.Equals(generatorVersion, adventure.GeneratorVersion, StringComparison.Ordinal) ||
            !string.Equals(generatorVersion, CurrentGeneratorVersion, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Dungeon generator '{generatorVersion}' is not supported.");
        }

        DungeonFloorRecipe recipe = adventure.Floors[floorIndex];
        if (recipe.RoomCount is < 6 or > 12)
        {
            throw new InvalidDataException($"Floor '{recipe.Id}' must request between 6 and 12 rooms.");
        }

        int attempts = Math.Clamp(maximumAttempts, 0, MaximumAttempts);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            ulong subSeed = DeriveSubSeed(seed, floorIndex, generatorVersion, attempt);
            Pcg32 random = new(subSeed, 0x9E3779B97F4A7C15UL ^ (uint)floorIndex);
            if (!TryCreateTopology(recipe.RoomCount, ref random, out Topology topology))
            {
                continue;
            }

            GeneratedDungeonFloor floor = BuildFloor(
                adventure, recipe, seed, floorIndex, attempt, false, topology, ref random);
            if (Validate(floor).Count == 0)
            {
                return floor;
            }
        }

        Pcg32 fallbackRandom = new(DeriveSubSeed(seed, floorIndex, generatorVersion, 0x7FFF), 0xC0FFEEUL);
        Topology fallback = CreateFallbackTopology(recipe.RoomCount);
        GeneratedDungeonFloor fallbackFloor = BuildFloor(
            adventure, recipe, seed, floorIndex, MaximumAttempts, true, fallback, ref fallbackRandom);
        IReadOnlyList<string> fallbackErrors = Validate(fallbackFloor);
        if (fallbackErrors.Count > 0)
        {
            throw new InvalidDataException(
                $"Fixed fallback floor '{recipe.Id}' failed validation: {string.Join("; ", fallbackErrors)}");
        }

        return fallbackFloor;
    }

    public static IReadOnlyList<string> Validate(GeneratedDungeonFloor floor)
    {
        ArgumentNullException.ThrowIfNull(floor);
        List<string> errors = [];
        if (floor.Rooms.Count == 0)
        {
            errors.Add("The floor has no rooms.");
            return errors;
        }

        if (floor.Rooms.Select(room => room.Index).Distinct().Count() != floor.Rooms.Count)
        {
            errors.Add("Room indices are not unique.");
        }

        if (floor.Rooms.Count(room => room.Role == DungeonRoomRole.Entry) != 1 ||
            floor.Rooms.Count(room => room.Role == DungeonRoomRole.Exit) != 1)
        {
            errors.Add("The floor must contain exactly one entry and one exit.");
        }

        HashSet<int> visited = [];
        Queue<int> frontier = new();
        frontier.Enqueue(0);
        while (frontier.TryDequeue(out int roomIndex))
        {
            if (!visited.Add(roomIndex) || roomIndex < 0 || roomIndex >= floor.Rooms.Count)
            {
                continue;
            }

            foreach (int connection in floor.Rooms[roomIndex].Connections)
            {
                if (connection >= 0 && connection < floor.Rooms.Count)
                {
                    frontier.Enqueue(connection);
                }
                else
                {
                    errors.Add($"Room {roomIndex} has an invalid connection.");
                }
            }
        }

        if (visited.Count != floor.Rooms.Count)
        {
            errors.Add("The room graph is disconnected.");
        }

        HashSet<DungeonGridCell> uniqueCells = [];
        foreach (DungeonGridCell cell in floor.Minimap.WalkableCells)
        {
            if (cell.X < 1 || cell.X >= GridWidth - 1 || cell.Z < 1 || cell.Z >= GridDepth - 1)
            {
                errors.Add("Walkable geometry exceeds the 96 x 80 metre generation bound.");
                break;
            }

            if (!uniqueCells.Add(cell))
            {
                errors.Add("The minimap contains duplicate occupancy.");
                break;
            }
        }

        if (!floor.Interactables.Any(item => item.Kind == AdventureInteractableKind.EquipmentCache) ||
            !floor.Interactables.Any(item => item.Kind == AdventureInteractableKind.LoreTerminal))
        {
            errors.Add("Every floor must contain an equipment cache and lore terminal.");
        }

        foreach (GeneratedDungeonInteractable interactable in floor.Interactables)
        {
            if (interactable.RoomIndex < 0 || interactable.RoomIndex >= floor.Rooms.Count)
            {
                errors.Add($"Interactable '{interactable.Id}' has an invalid room.");
            }
        }

        for (int leftIndex = 0; leftIndex < floor.Arena.Primitives.Count; leftIndex++)
        {
            ArenaPrimitiveDefinition left = floor.Arena.Primitives[leftIndex];
            if (!left.IsVisible)
            {
                continue;
            }

            for (int rightIndex = leftIndex + 1; rightIndex < floor.Arena.Primitives.Count; rightIndex++)
            {
                ArenaPrimitiveDefinition right = floor.Arena.Primitives[rightIndex];
                if (right.IsVisible && HasPositiveVolumeIntersection(left, right))
                {
                    errors.Add($"Visible primitives '{left.Id}' and '{right.Id}' overlap.");
                    return errors;
                }
            }
        }

        return errors;
    }

    private static bool TryCreateTopology(int roomCount, ref Pcg32 random, out Topology topology)
    {
        int mainCount = roomCount - 2;
        for (int retry = 0; retry < 12; retry++)
        {
            Slot start = new(random.NextInt(2, SlotColumns - 2), random.NextInt(1, SlotRows - 1));
            List<Slot> path = [start];
            HashSet<Slot> occupied = [start];
            if (!ExtendPath(path, occupied, mainCount, ref random))
            {
                continue;
            }

            List<(int Parent, Slot Cell)> branches = [];
            for (int branch = 0; branch < 2; branch++)
            {
                List<(int Parent, Slot Cell)> options = [];
                for (int parent = 1; parent < path.Count - 1; parent++)
                {
                    foreach (Slot neighbor in GetNeighbors(path[parent]))
                    {
                        if (IsValidSlot(neighbor) && !occupied.Contains(neighbor))
                        {
                            options.Add((parent, neighbor));
                        }
                    }
                }

                if (options.Count == 0)
                {
                    break;
                }

                (int Parent, Slot Cell) selected = options[random.NextInt(options.Count)];
                occupied.Add(selected.Cell);
                branches.Add(selected);
            }

            if (branches.Count != 2)
            {
                continue;
            }

            List<Slot> slots = [.. path, .. branches.Select(branch => branch.Cell)];
            List<(int A, int B)> edges = [];
            for (int index = 1; index < path.Count; index++)
            {
                edges.Add((index - 1, index));
            }
            for (int branch = 0; branch < branches.Count; branch++)
            {
                edges.Add((branches[branch].Parent, mainCount + branch));
            }

            HashSet<(int, int)> normalizedEdges = edges.Select(NormalizeEdge).ToHashSet();
            List<(int A, int B)> loopOptions = [];
            for (int a = 0; a < slots.Count; a++)
            {
                for (int b = a + 1; b < slots.Count; b++)
                {
                    if (Manhattan(slots[a], slots[b]) == 1 && !normalizedEdges.Contains((a, b)))
                    {
                        loopOptions.Add((a, b));
                    }
                }
            }

            if (loopOptions.Count > 0)
            {
                (int A, int B) loop = loopOptions[random.NextInt(loopOptions.Count)];
                edges.Add(loop);
            }

            topology = new Topology(slots, edges, mainCount);
            return true;
        }

        topology = default;
        return false;
    }

    private static bool ExtendPath(
        List<Slot> path,
        HashSet<Slot> occupied,
        int targetCount,
        ref Pcg32 random)
    {
        if (path.Count == targetCount)
        {
            return true;
        }

        Slot current = path[^1];
        List<Slot> candidates = GetNeighbors(current)
            .Where(candidate => IsValidSlot(candidate) && !occupied.Contains(candidate))
            .ToList();
        random.Shuffle(candidates);
        foreach (Slot candidate in candidates)
        {
            occupied.Add(candidate);
            path.Add(candidate);
            if (ExtendPath(path, occupied, targetCount, ref random))
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
            occupied.Remove(candidate);
        }

        return false;
    }

    private static GeneratedDungeonFloor BuildFloor(
        AdventureDefinition adventure,
        DungeonFloorRecipe recipe,
        int seed,
        int floorIndex,
        int attemptSalt,
        bool usedFallback,
        Topology topology,
        ref Pcg32 random)
    {
        List<MutableRoom> rooms = [];
        HashSet<DungeonGridCell> walkable = [];
        Dictionary<int, List<DungeonGridCell>> roomCells = [];
        for (int index = 0; index < topology.Slots.Count; index++)
        {
            Slot slot = topology.Slots[index];
            int width = random.NextInt(4, 6);
            int depth = random.NextInt(4, 6);
            int centerX = 5 + slot.X * SlotStrideX;
            int centerZ = 5 + slot.Z * SlotStrideZ;
            int minX = centerX - width / 2;
            int minZ = centerZ - depth / 2;
            MutableRoom room = new(index, slot, minX, minZ, width, depth, index < topology.MainCount);
            rooms.Add(room);
            List<DungeonGridCell> cells = [];
            for (int x = minX; x < minX + width; x++)
            {
                for (int z = minZ; z < minZ + depth; z++)
                {
                    DungeonGridCell cell = new(x, z);
                    walkable.Add(cell);
                    cells.Add(cell);
                }
            }
            roomCells[index] = cells;
        }

        foreach ((int a, int b) in topology.Edges)
        {
            rooms[a].Connections.Add(b);
            rooms[b].Connections.Add(a);
            AddCorridor(walkable, rooms[a].CenterCell, rooms[b].CenterCell);
        }

        rooms[0].Role = DungeonRoomRole.Entry;
        rooms[topology.MainCount - 1].Role = DungeonRoomRole.Exit;
        rooms[topology.MainCount].Role = DungeonRoomRole.Cache;
        rooms[topology.MainCount + 1].Role = DungeonRoomRole.Lore;

        List<(AdventureObjectiveDefinition Definition, int Ordinal)> objectiveTargets = [];
        foreach (AdventureObjectiveDefinition objective in recipe.Objectives)
        {
            for (int ordinal = 0; ordinal < Math.Max(1, objective.RequiredCount); ordinal++)
            {
                objectiveTargets.Add((objective, ordinal));
            }
        }

        List<int> objectiveRooms = SelectObjectiveRooms(topology.MainCount, objectiveTargets.Count);
        List<GeneratedDungeonInteractable> interactables = [];
        for (int index = 0; index < objectiveTargets.Count; index++)
        {
            (AdventureObjectiveDefinition objective, int ordinal) = objectiveTargets[index];
            int roomIndex = objectiveRooms[index];
            rooms[roomIndex].Role = objective.Kind == AdventureObjectiveKind.RecoverCommandKey
                ? DungeonRoomRole.Key
                : DungeonRoomRole.Objective;
            interactables.Add(new GeneratedDungeonInteractable
            {
                Id = $"{objective.Id}-{ordinal + 1}",
                Kind = ToInteractableKind(objective.Kind),
                RoomIndex = roomIndex,
                Position = ToWorld(rooms[roomIndex].CenterCell) + ObjectiveOffset(index),
                ObjectiveId = objective.Id,
                RequiresObjectiveId = objective.RequiresObjectiveId,
            });
        }

        interactables.Add(new GeneratedDungeonInteractable
        {
            Id = $"{recipe.Id}-equipment-cache",
            Kind = AdventureInteractableKind.EquipmentCache,
            RoomIndex = topology.MainCount,
            Position = ToWorld(rooms[topology.MainCount].CenterCell),
            Optional = true,
        });
        List<int> chestRooms = Enumerable.Range(1, Math.Max(0, topology.MainCount - 2))
            .Where(roomIndex => !objectiveRooms.Contains(roomIndex) && roomIndex % 2 == 0)
            .ToList();
        chestRooms.AddRange(Enumerable.Range(1, Math.Max(0, topology.MainCount - 2))
            .Where(roomIndex => !objectiveRooms.Contains(roomIndex) && !chestRooms.Contains(roomIndex)));
        for (int chestOrdinal = 1; chestOrdinal < recipe.ChestCount && chestRooms.Count > 0; chestOrdinal++)
        {
            int selectedIndex = random.NextInt(0, chestRooms.Count);
            int roomIndex = chestRooms[selectedIndex];
            chestRooms.RemoveAt(selectedIndex);
            Vector3 offset = chestOrdinal % 2 == 0
                ? new Vector3(-1.35f, 0f, 1.15f)
                : new Vector3(1.35f, 0f, -1.15f);
            interactables.Add(new GeneratedDungeonInteractable
            {
                Id = $"{recipe.Id}-equipment-chest-{chestOrdinal + 1}",
                Kind = AdventureInteractableKind.EquipmentCache,
                RoomIndex = roomIndex,
                Position = ToWorld(rooms[roomIndex].CenterCell) + offset,
                Optional = true,
            });
        }
        interactables.Add(new GeneratedDungeonInteractable
        {
            Id = $"{recipe.Id}-lore-terminal",
            Kind = AdventureInteractableKind.LoreTerminal,
            RoomIndex = topology.MainCount + 1,
            Position = ToWorld(rooms[topology.MainCount + 1].CenterCell),
            Optional = true,
        });
        interactables.Add(new GeneratedDungeonInteractable
        {
            Id = $"{recipe.Id}-lift",
            Kind = AdventureInteractableKind.Lift,
            RoomIndex = topology.MainCount - 1,
            Position = ToWorld(rooms[topology.MainCount - 1].CenterCell),
            RequiresObjectiveId = recipe.Objectives.LastOrDefault()?.Id,
        });

        List<GeneratedDungeonHazard> hazards = [];
        int hazardOrdinal = 0;
        for (int roomIndex = 1; roomIndex < topology.MainCount - 1; roomIndex += 2)
        {
            MutableRoom room = rooms[roomIndex];
            hazards.Add(new GeneratedDungeonHazard
            {
                Id = $"{recipe.Id}-hazard-{hazardOrdinal + 1}",
                Kind = recipe.Hazard,
                RoomIndex = roomIndex,
                Position = ToWorld(room.CenterCell) + new Vector3(0f, 0.03f, 0f),
                Size = new Vector3(MathF.Min(5f, room.Width * CellSize - 2f), 0.08f,
                    MathF.Min(5f, room.Depth * CellSize - 2f)),
                PhaseOffsetSeconds = random.NextInt(0, 20) / 10f,
            });
            hazardOrdinal++;
        }

        List<GeneratedDungeonEnemyGroup> enemyGroups = [];
        if (recipe.EnemyGroups.Count > 0)
        {
            for (int roomIndex = 1; roomIndex < topology.MainCount - 1; roomIndex++)
            {
                AdventureEnemyGroupRecipe group = recipe.EnemyGroups[(roomIndex - 1) % recipe.EnemyGroups.Count];
                if (roomIndex < group.MinimumRoomIndex)
                {
                    continue;
                }

                enemyGroups.Add(new GeneratedDungeonEnemyGroup
                {
                    Id = $"{group.Id}-{roomIndex}",
                    RoomIndex = roomIndex,
                    Dormant = group.Dormant,
                    SpawnCenter = ToWorld(rooms[roomIndex].CenterCell),
                    Members = group.Members.Select(member => member with { Count = Math.Min(member.Count, 8) }).ToList(),
                });
            }
        }

        List<GeneratedDungeonGate> gates = [];
        if (topology.MainCount >= 3 && recipe.Objectives.Count > 0)
        {
            int beforeExit = topology.MainCount - 2;
            Vector3 midpoint = (ToWorld(rooms[beforeExit].CenterCell) +
                ToWorld(rooms[topology.MainCount - 1].CenterCell)) * 0.5f;
            bool alongX = rooms[beforeExit].CenterCell.Z == rooms[topology.MainCount - 1].CenterCell.Z;
            // Corridors occupy two cells on the negative perpendicular axis. Center the
            // gate on that full four-metre aperture so it meets, but never enters, its walls.
            midpoint += alongX
                ? new Vector3(0f, 0f, -CellSize * 0.5f)
                : new Vector3(-CellSize * 0.5f, 0f, 0f);
            gates.Add(new GeneratedDungeonGate
            {
                Id = $"{recipe.Id}-exit-gate",
                FromRoomIndex = beforeExit,
                ToRoomIndex = topology.MainCount - 1,
                Position = midpoint + new Vector3(0f, 1.8f, 0f),
                Size = alongX ? new Vector3(0.25f, 3.6f, 3.6f) : new Vector3(3.6f, 3.6f, 0.25f),
                UnlockObjectiveId = recipe.Objectives[^1].Id,
            });
        }

        List<ArenaPrimitiveDefinition> primitives = BuildGeometry(walkable, floorIndex);
        primitives.AddRange(gates.Select(gate => new ArenaPrimitiveDefinition
        {
            Id = gate.Id,
            Position = gate.Position,
            Size = gate.Size,
            Color = new Vector3(0.18f, 0.88f, 1f),
            IsNavigationObstacle = true,
            HasCollision = true,
            IsVisible = true,
            IsEmissive = true,
        }));
        Vector3 playerSpawn = ToWorld(rooms[0].CenterCell) + new Vector3(0f, GameSimulation.PlayerEyeHeight, 0f);
        ArenaDefinition arena = new()
        {
            SchemaVersion = 3,
            Id = $"{adventure.Id}-{recipe.Id}-{seed}",
            DisplayName = recipe.DisplayName,
            WaveSetId = "adventure-runtime",
            BoundsMin = new Vector3(-GridWidth * CellSize * 0.5f, -0.2f, -GridDepth * CellSize * 0.5f),
            BoundsMax = new Vector3(GridWidth * CellSize * 0.5f, 4.1f, GridDepth * CellSize * 0.5f),
            PlayerSpawn = playerSpawn,
            NavigationCellSize = 0.5f,
            TraversalMode = ArenaTraversalMode.OpenArena,
            SkyColor = new Vector3(0.012f, 0.025f, 0.04f),
            FogColor = floorIndex switch
            {
                0 => new Vector3(0.055f, 0.13f, 0.15f),
                1 => new Vector3(0.16f, 0.09f, 0.035f),
                _ => new Vector3(0.12f, 0.04f, 0.16f),
            },
            FogStart = 24f,
            FogEnd = 68f,
            EnemySpawns = enemyGroups.Select(group => group.SpawnCenter).ToList(),
            Primitives = primitives,
            Props = [],
            PickupSpawns = [],
            Sectors = [],
            BossArenaAnchor = ToWorld(rooms[^1].CenterCell),
        };

        List<GeneratedDungeonRoom> generatedRooms = rooms.Select(room => new GeneratedDungeonRoom
        {
            Id = $"room-{room.Index + 1:00}",
            Index = room.Index,
            Role = room.Role,
            GridX = room.MinX,
            GridZ = room.MinZ,
            GridWidth = room.Width,
            GridDepth = room.Depth,
            IsMainPath = room.IsMainPath,
            Connections = room.Connections.Order().ToList(),
            Center = ToWorld(room.CenterCell),
        }).ToList();

        DungeonMinimapDefinition minimap = new()
        {
            WalkableCells = walkable.OrderBy(cell => cell.Z).ThenBy(cell => cell.X).ToList(),
            RoomCells = roomCells,
            WorldOrigin = new Vector2(-GridWidth * CellSize * 0.5f, -GridDepth * CellSize * 0.5f),
            CellSize = CellSize,
        };
        string hash = ComputeLayoutHash(adventure.Id, generatorVersion: adventure.GeneratorVersion,
            seed, floorIndex, generatedRooms, minimap.WalkableCells, interactables, enemyGroups);
        return new GeneratedDungeonFloor
        {
            AdventureId = adventure.Id,
            GeneratorVersion = adventure.GeneratorVersion,
            Seed = seed,
            FloorIndex = floorIndex,
            AttemptSalt = attemptSalt,
            UsedFallback = usedFallback,
            LayoutHash = hash,
            Arena = arena,
            Rooms = generatedRooms,
            Interactables = interactables,
            Gates = gates,
            Hazards = hazards,
            EnemyGroups = enemyGroups,
            Minimap = minimap,
        };
    }

    private static List<int> SelectObjectiveRooms(int mainCount, int targetCount)
    {
        List<int> result = [];
        if (targetCount == 0)
        {
            return result;
        }

        int usable = Math.Max(1, mainCount - 2);
        for (int index = 0; index < targetCount; index++)
        {
            int selected = 1 + ((index + 1) * usable / (targetCount + 1));
            while (result.Contains(selected) && selected < mainCount - 2)
            {
                selected++;
            }
            result.Add(Math.Clamp(selected, 1, mainCount - 2));
        }

        return result;
    }

    private static List<ArenaPrimitiveDefinition> BuildGeometry(HashSet<DungeonGridCell> walkable, int floorIndex)
    {
        Vector3 floorColor = floorIndex switch
        {
            0 => new Vector3(0.22f, 0.34f, 0.37f),
            1 => new Vector3(0.36f, 0.25f, 0.13f),
            _ => new Vector3(0.28f, 0.15f, 0.35f),
        };
        Vector3 wallColor = floorColor * 1.25f;
        List<ArenaPrimitiveDefinition> primitives = [];
        int ordinal = 0;
        foreach (CellRectangle rectangle in MergeCells(walkable))
        {
            Vector3 center = RectangleCenter(rectangle);
            Vector3 size = new(rectangle.Width * CellSize, 0.2f, rectangle.Depth * CellSize);
            primitives.Add(new ArenaPrimitiveDefinition
            {
                Id = $"floor-{ordinal:000}",
                Position = center + new Vector3(0f, -0.1f, 0f),
                Size = size,
                Color = floorColor,
                TextureAsset = "Textures/Arena/floor-panel",
                TextureMetersPerTile = 2f,
                IsNavigationObstacle = false,
                CollisionRole = ArenaCollisionRole.Floor,
            });
            primitives.Add(new ArenaPrimitiveDefinition
            {
                Id = $"ceiling-{ordinal:000}",
                Position = center + new Vector3(0f, 4f, 0f),
                Size = size,
                Color = wallColor * 0.7f,
                TextureAsset = "Textures/Arena/wall-panel",
                TextureMetersPerTile = 2f,
                IsNavigationObstacle = false,
                HasCollision = false,
            });
            ordinal++;
        }

        int wallOrdinal = 0;
        foreach (DungeonGridCell cell in walkable.OrderBy(cell => cell.Z).ThenBy(cell => cell.X))
        {
            AddBoundaryWall(cell, -1, 0, true);
            AddBoundaryWall(cell, 1, 0, true);
            AddBoundaryWall(cell, 0, -1, false);
            AddBoundaryWall(cell, 0, 1, false);
        }

        return primitives;

        void AddBoundaryWall(DungeonGridCell cell, int dx, int dz, bool verticalAlongZ)
        {
            if (walkable.Contains(new DungeonGridCell(cell.X + dx, cell.Z + dz)))
            {
                return;
            }

            Vector3 cellCenter = ToWorld(cell);
            Vector3 position = cellCenter + new Vector3(dx * CellSize * 0.5f, 1.95f, dz * CellSize * 0.5f);
            Vector3 size = verticalAlongZ
                ? new Vector3(0.2f, 3.9f, CellSize - 0.2f)
                : new Vector3(CellSize - 0.2f, 3.9f, 0.2f);
            primitives.Add(new ArenaPrimitiveDefinition
            {
                Id = $"wall-{wallOrdinal++:0000}",
                Position = position,
                Size = size,
                Color = wallColor,
                TextureAsset = "Textures/Arena/wall-panel",
                TextureMetersPerTile = 2f,
                IsNavigationObstacle = true,
                CollisionRole = ArenaCollisionRole.OuterWall,
            });
        }
    }

    private static IEnumerable<CellRectangle> MergeCells(HashSet<DungeonGridCell> cells)
    {
        HashSet<DungeonGridCell> remaining = [.. cells];
        while (remaining.Count > 0)
        {
            DungeonGridCell start = remaining.OrderBy(cell => cell.Z).ThenBy(cell => cell.X).First();
            int width = 1;
            while (remaining.Contains(new DungeonGridCell(start.X + width, start.Z)))
            {
                width++;
            }

            int depth = 1;
            bool canGrow = true;
            while (canGrow)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!remaining.Contains(new DungeonGridCell(start.X + x, start.Z + depth)))
                    {
                        canGrow = false;
                        break;
                    }
                }

                if (canGrow)
                {
                    depth++;
                }
            }

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < depth; z++)
                {
                    remaining.Remove(new DungeonGridCell(start.X + x, start.Z + z));
                }
            }

            yield return new CellRectangle(start.X, start.Z, width, depth);
        }
    }

    private static bool HasPositiveVolumeIntersection(
        ArenaPrimitiveDefinition left,
        ArenaPrimitiveDefinition right)
    {
        Vector3 distance = Vector3.Abs(left.Position - right.Position);
        Vector3 half = (left.Size + right.Size) * 0.5f;
        const float epsilon = 0.0001f;
        return distance.X < half.X - epsilon &&
            distance.Y < half.Y - epsilon &&
            distance.Z < half.Z - epsilon;
    }

    private static string ComputeLayoutHash(
        string adventureId,
        string generatorVersion,
        int seed,
        int floorIndex,
        IEnumerable<GeneratedDungeonRoom> rooms,
        IEnumerable<DungeonGridCell> cells,
        IEnumerable<GeneratedDungeonInteractable> interactables,
        IEnumerable<GeneratedDungeonEnemyGroup> enemyGroups)
    {
        StringBuilder canonical = new();
        canonical.Append(adventureId).Append('|').Append(generatorVersion).Append('|')
            .Append(seed.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(floorIndex.ToString(CultureInfo.InvariantCulture));
        foreach (GeneratedDungeonRoom room in rooms.OrderBy(room => room.Index))
        {
            canonical.Append("|r:").Append(room.Index).Append(',').Append((int)room.Role).Append(',')
                .Append(room.GridX).Append(',').Append(room.GridZ).Append(',')
                .Append(room.GridWidth).Append(',').Append(room.GridDepth).Append(':')
                .AppendJoin(',', room.Connections.Order());
        }
        foreach (DungeonGridCell cell in cells.OrderBy(cell => cell.Z).ThenBy(cell => cell.X))
        {
            canonical.Append("|c:").Append(cell.X).Append(',').Append(cell.Z);
        }
        foreach (GeneratedDungeonInteractable item in interactables.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            canonical.Append("|i:").Append(item.Id).Append(',').Append((int)item.Kind).Append(',')
                .Append(item.RoomIndex);
        }
        foreach (GeneratedDungeonEnemyGroup group in enemyGroups.OrderBy(group => group.Id, StringComparer.Ordinal))
        {
            canonical.Append("|e:").Append(group.Id).Append(',').Append(group.RoomIndex);
            foreach (SpawnGroupDefinition member in group.Members.OrderBy(member => member.EnemyId, StringComparer.Ordinal))
            {
                canonical.Append(',').Append(member.EnemyId).Append(':').Append(member.Count);
            }
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static AdventureInteractableKind ToInteractableKind(AdventureObjectiveKind kind) => kind switch
    {
        AdventureObjectiveKind.RestoreRelay => AdventureInteractableKind.PowerRelay,
        AdventureObjectiveKind.DiscoverSignal => AdventureInteractableKind.StoryTerminal,
        AdventureObjectiveKind.RecoverCommandKey => AdventureInteractableKind.CommandKey,
        AdventureObjectiveKind.DisableFabricator => AdventureInteractableKind.FabricatorConsole,
        AdventureObjectiveKind.DisableSignalAnchor => AdventureInteractableKind.SignalAnchor,
        AdventureObjectiveKind.DisableShieldControl => AdventureInteractableKind.ShieldControl,
        _ => AdventureInteractableKind.StoryTerminal,
    };

    private static Vector3 ObjectiveOffset(int index) => (index % 3) switch
    {
        0 => new Vector3(-1.2f, 0f, 0f),
        1 => new Vector3(1.2f, 0f, 0f),
        _ => new Vector3(0f, 0f, 1.2f),
    };

    private static void AddCorridor(HashSet<DungeonGridCell> cells, DungeonGridCell from, DungeonGridCell to)
    {
        if (from.X != to.X)
        {
            int min = Math.Min(from.X, to.X);
            int max = Math.Max(from.X, to.X);
            for (int x = min; x <= max; x++)
            {
                cells.Add(new DungeonGridCell(x, from.Z));
                cells.Add(new DungeonGridCell(x, from.Z - 1));
            }
        }
        else
        {
            int min = Math.Min(from.Z, to.Z);
            int max = Math.Max(from.Z, to.Z);
            for (int z = min; z <= max; z++)
            {
                cells.Add(new DungeonGridCell(from.X, z));
                cells.Add(new DungeonGridCell(from.X - 1, z));
            }
        }
    }

    private static Vector3 ToWorld(DungeonGridCell cell) => new(
        (cell.X + 0.5f) * CellSize - GridWidth * CellSize * 0.5f,
        0f,
        (cell.Z + 0.5f) * CellSize - GridDepth * CellSize * 0.5f);

    private static Vector3 RectangleCenter(CellRectangle rectangle) => new(
        (rectangle.X + rectangle.Width * 0.5f) * CellSize - GridWidth * CellSize * 0.5f,
        0f,
        (rectangle.Z + rectangle.Depth * 0.5f) * CellSize - GridDepth * CellSize * 0.5f);

    private static Topology CreateFallbackTopology(int roomCount)
    {
        Slot[] fixedSlots =
        [
            new(1, 1), new(2, 1), new(3, 1), new(4, 1), new(5, 1),
            new(5, 2), new(4, 2), new(3, 2), new(2, 2), new(1, 2),
            new(1, 3), new(2, 3),
        ];
        int mainCount = roomCount - 2;
        List<Slot> slots = fixedSlots.Take(mainCount).ToList();
        Slot cache = new(2, 0);
        Slot lore = new(4, 0);
        slots.Add(cache);
        slots.Add(lore);
        List<(int A, int B)> edges = [];
        for (int index = 1; index < mainCount; index++)
        {
            edges.Add((index - 1, index));
        }
        edges.Add((1, mainCount));
        edges.Add((3, mainCount + 1));
        return new Topology(slots, edges, mainCount);
    }

    private static ulong DeriveSubSeed(int seed, int floorIndex, string version, int attempt)
    {
        ulong value = (uint)seed;
        value ^= (ulong)(uint)(floorIndex + 1) * 0x9E3779B97F4A7C15UL;
        value ^= (ulong)(uint)attempt * 0xBF58476D1CE4E5B9UL;
        foreach (char character in version)
        {
            value = (value ^ character) * 0x100000001B3UL;
        }
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static List<Slot> GetNeighbors(Slot slot) =>
    [
        new(slot.X - 1, slot.Z),
        new(slot.X + 1, slot.Z),
        new(slot.X, slot.Z - 1),
        new(slot.X, slot.Z + 1),
    ];

    private static bool IsValidSlot(Slot slot) =>
        slot.X >= 0 && slot.X < SlotColumns && slot.Z >= 0 && slot.Z < SlotRows;

    private static int Manhattan(Slot left, Slot right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Z - right.Z);

    private static (int, int) NormalizeEdge((int A, int B) edge) =>
        edge.A < edge.B ? edge : (edge.B, edge.A);

    private readonly record struct Slot(int X, int Z);
    private readonly record struct Topology(List<Slot> Slots, List<(int A, int B)> Edges, int MainCount);
    private readonly record struct CellRectangle(int X, int Z, int Width, int Depth);

    private sealed class MutableRoom(
        int index,
        Slot slot,
        int minX,
        int minZ,
        int width,
        int depth,
        bool isMainPath)
    {
        public int Index { get; } = index;
        public Slot Slot { get; } = slot;
        public int MinX { get; } = minX;
        public int MinZ { get; } = minZ;
        public int Width { get; } = width;
        public int Depth { get; } = depth;
        public bool IsMainPath { get; } = isMainPath;
        public DungeonRoomRole Role { get; set; } = DungeonRoomRole.Transit;
        public HashSet<int> Connections { get; } = [];
        public DungeonGridCell CenterCell => new(MinX + Width / 2, MinZ + Depth / 2);
    }

    private struct Pcg32
    {
        private ulong _state;
        private readonly ulong _increment;

        public Pcg32(ulong seed, ulong sequence)
        {
            _state = 0UL;
            _increment = (sequence << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong oldState = _state;
            _state = oldState * 6364136223846793005UL + _increment;
            uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rotation = (int)(oldState >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        public int NextInt(int exclusiveMaximum)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);

            uint bound = (uint)exclusiveMaximum;
            uint threshold = unchecked((uint)(-bound)) % bound;
            uint value;
            do
            {
                value = NextUInt();
            }
            while (value < threshold);
            return (int)(value % bound);
        }

        public int NextInt(int inclusiveMinimum, int exclusiveMaximum) =>
            inclusiveMinimum + NextInt(exclusiveMaximum - inclusiveMinimum);

        public void Shuffle<T>(IList<T> values)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swap = NextInt(index + 1);
                (values[index], values[swap]) = (values[swap], values[index]);
            }
        }
    }
}
