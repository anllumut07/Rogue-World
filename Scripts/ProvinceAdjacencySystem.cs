using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// ProvinceAdjacencySystem - Province-level connection graph for pathfinding and movement.
/// Based on HoI4's map/adjacencies.csv (669 connections).
/// Adjacency types: sea, impassable, strait/canal.
/// </summary>
public static class ProvinceAdjacencySystem
{
    private static bool _initialized = false;

    // Adjacency graph: provinceId -> List of (neighborId, type, ruleName)
    private static Dictionary<int, List<Adjacency>> _adjacencies = new();

    // Reverse lookup for quick "are these connected" checks
    private static HashSet<long> _adjacencySet = new();

    // Impassable borders for quick checking
    private static HashSet<long> _impassableBorders = new();

    public enum AdjacencyType
    {
        Land,       // Normal land border
        Sea,        // Naval movement between sea provinces
        Impassable, // Blocks land movement (mountains, borders)
        Strait,     // Strait/canal with special rules
    }

    public sealed class Adjacency
    {
        public int NeighborId;
        public AdjacencyType Type;
        public string RuleName; // For straits/canals
    }

    public static bool IsInitialized => _initialized;

    public static void Initialize()
    {
        if (_initialized) return;

        const string path = "res://Data/hoi4_imported/province_adjacencies.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[ProvinceAdjacencySystem] {path} not found!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true
            };
            var json = FileAccess.GetFileAsString(path);
            var adjacencies = JsonSerializer.Deserialize<List<JsonAdjacency>>(json, options);

            if (adjacencies != null)
            {
                foreach (var adj in adjacencies)
                {
                    // Parse type
                    AdjacencyType type = AdjacencyType.Land;
                    if (!string.IsNullOrEmpty(adj.Type))
                    {
                        if (adj.Type.Equals("sea", StringComparison.OrdinalIgnoreCase))
                            type = AdjacencyType.Sea;
                        else if (adj.Type.Equals("impassable", StringComparison.OrdinalIgnoreCase))
                            type = AdjacencyType.Impassable;
                        else if (!string.IsNullOrEmpty(adj.AdjacencyRuleName))
                            type = AdjacencyType.Strait;
                    }

                    var adjacency = new Adjacency
                    {
                        NeighborId = adj.To,
                        Type = type,
                        RuleName = adj.AdjacencyRuleName ?? ""
                    };

                    // Add bidirectional
                    AddAdjacency(adj.From, adj.To, adjacency);

                    var reverseAdj = new Adjacency
                    {
                        NeighborId = adj.From,
                        Type = type,
                        RuleName = adj.AdjacencyRuleName ?? ""
                    };
                    AddAdjacency(adj.To, adj.From, reverseAdj);

                    // Track impassable borders
                    if (type == AdjacencyType.Impassable)
                    {
                        long key1 = ((long)adj.From << 32) | (uint)adj.To;
                        long key2 = ((long)adj.To << 32) | (uint)adj.From;
                        _impassableBorders.Add(key1);
                        _impassableBorders.Add(key2);
                    }
                }
            }

            _initialized = true;
            GD.Print($"[ProvinceAdjacencySystem] Initialized: {_adjacencies.Count} provinces with adjacencies, {_impassableBorders.Count / 2} impassable borders");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProvinceAdjacencySystem] Failed to load: {e.Message}");
        }
    }

    private static void AddAdjacency(int from, int to, Adjacency adj)
    {
        if (!_adjacencies.TryGetValue(from, out var list))
        {
            list = new List<Adjacency>();
            _adjacencies[from] = list;
        }

        // Avoid duplicates
        if (!list.Any(a => a.NeighborId == to && a.Type == adj.Type))
        {
            list.Add(adj);
            long key = ((long)from << 32) | (uint)to;
            _adjacencySet.Add(key);
        }
    }

    public static void ResetForNewGame()
    {
        // Static data, no per-game state
    }

    /// <summary>
    /// Get all adjacencies for a province.
    /// </summary>
    public static IReadOnlyList<Adjacency> GetAdjacencies(int provinceId)
    {
        return _adjacencies.GetValueOrDefault(provinceId) ?? new List<Adjacency>();
    }

    /// <summary>
    /// Get adjacencies of a specific type for a province.
    /// </summary>
    public static IEnumerable<Adjacency> GetAdjacenciesOfType(int provinceId, AdjacencyType type)
    {
        if (_adjacencies.TryGetValue(provinceId, out var list))
        {
            foreach (var adj in list)
                if (adj.Type == type)
                    yield return adj;
        }
    }

    /// <summary>
    /// Check if two provinces are adjacent (any type).
    /// </summary>
    public static bool AreAdjacent(int provinceA, int provinceB)
    {
        long key1 = ((long)provinceA << 32) | (uint)provinceB;
        long key2 = ((long)provinceB << 32) | (uint)provinceA;
        return _adjacencySet.Contains(key1) || _adjacencySet.Contains(key2);
    }

    /// <summary>
    /// Check if land movement is possible between two provinces.
    /// Returns false if impassable border exists.
    /// </summary>
    public static bool CanMoveLand(int fromProvince, int toProvince)
    {
        long key1 = ((long)fromProvince << 32) | (uint)toProvince;
        long key2 = ((long)toProvince << 32) | (uint)fromProvince;

        if (_impassableBorders.Contains(key1) || _impassableBorders.Contains(key2))
            return false;

        // Check if they have a land or strait adjacency
        if (_adjacencies.TryGetValue(fromProvince, out var list))
        {
            return list.Any(a => a.NeighborId == toProvince &&
                (a.Type == AdjacencyType.Land || a.Type == AdjacencyType.Strait));
        }

        return false;
    }

    /// <summary>
    /// Check if naval movement is possible between two provinces.
    /// </summary>
    public static bool CanMoveNaval(int fromProvince, int toProvince)
    {
        if (_adjacencies.TryGetValue(fromProvince, out var list))
        {
            return list.Any(a => a.NeighborId == toProvince &&
                (a.Type == AdjacencyType.Sea || a.Type == AdjacencyType.Strait));
        }
        return false;
    }

    /// <summary>
    /// Get all land neighbors (for army pathfinding).
    /// </summary>
    public static IEnumerable<int> GetLandNeighbors(int provinceId)
    {
        if (_adjacencies.TryGetValue(provinceId, out var list))
        {
            foreach (var adj in list)
            {
                if (adj.Type == AdjacencyType.Land || adj.Type == AdjacencyType.Strait)
                    yield return adj.NeighborId;
            }
        }
    }

    /// <summary>
    /// Get all naval neighbors (for fleet pathfinding).
    /// </summary>
    public static IEnumerable<int> GetNavalNeighbors(int provinceId)
    {
        if (_adjacencies.TryGetValue(provinceId, out var list))
        {
            foreach (var adj in list)
            {
                if (adj.Type == AdjacencyType.Sea || adj.Type == AdjacencyType.Strait)
                    yield return adj.NeighborId;
            }
        }
    }

    /// <summary>
    /// Get the strait/canal rule name for an adjacency (if any).
    /// </summary>
    public static string GetStraitRuleName(int fromProvince, int toProvince)
    {
        if (_adjacencies.TryGetValue(fromProvince, out var list))
        {
            var adj = list.FirstOrDefault(a => a.NeighborId == toProvince && a.Type == AdjacencyType.Strait);
            return adj?.RuleName ?? "";
        }
        return "";
    }

    /// <summary>
    /// A* pathfinding with terrain/weather/supply costs.
    /// </summary>
    public static List<int> FindPath(int fromProvince, int toProvince, bool naval,
        Func<int, float> costFunc = null)
    {
        if (fromProvince == toProvince) return new List<int> { fromProvince };

        var openSet = new PriorityQueue<int, float>();
        var cameFrom = new Dictionary<int, int>();
        var gScore = new Dictionary<int, float> { [fromProvince] = 0f };
        var fScore = new Dictionary<int, float> { [fromProvince] = Heuristic(fromProvince, toProvince) };

        openSet.Enqueue(fromProvince, fScore[fromProvince]);

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            if (current == toProvince)
            {
                return ReconstructPath(cameFrom, current);
            }

            var neighbors = naval ? GetNavalNeighbors(current) : GetLandNeighbors(current);

            foreach (var neighbor in neighbors)
            {
                // Check passability
                if (naval ? !CanMoveNaval(current, neighbor) : !CanMoveLand(current, neighbor))
                    continue;

                float edgeCost = 1f;
                if (costFunc != null)
                    edgeCost = costFunc(neighbor);
                else
                    edgeCost = GetBaseMovementCost(current, neighbor);

                float tentativeG = gScore[current] + edgeCost;

                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, toProvince);
                    openSet.Enqueue(neighbor, fScore[neighbor]);
                }
            }
        }

        return new List<int>(); // No path found
    }

    private static float Heuristic(int a, int b)
    {
        // Simple heuristic: straight-line distance approximation
        // In practice, use province centroids
        return Math.Abs(a - b) * 0.01f;
    }

    private static float GetBaseMovementCost(int from, int to)
    {
        // Could be enhanced with terrain data
        return 1f;
    }

    private static List<int> ReconstructPath(Dictionary<int, int> cameFrom, int current)
    {
        var path = new List<int> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

    // JSON deserialization helper
    private class JsonAdjacency
    {
        public int From;
        public int To;
        public string Type;
        public int Through;
        public string AdjacencyRuleName;
        public string Comment;
    }
}