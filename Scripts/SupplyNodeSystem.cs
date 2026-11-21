using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// SupplyNodeSystem - Supply network with 895 nodes across 3 levels.
/// Based on HoI4's map/supply_nodes.txt and map/supplyareas/.
/// </summary>
public static class SupplyNodeSystem
{
    private static bool _initialized = false;

    // Supply nodes by level
    private static Dictionary<int, List<SupplyNode>> _nodesByLevel = new()
    {
        [1] = new List<SupplyNode>(),
        [2] = new List<SupplyNode>(),
        [3] = new List<SupplyNode>()
    };

    // All nodes flat list
    private static List<SupplyNode> _allNodes = new();

    // Province -> node mapping (province can have multiple nodes)
    private static Dictionary<int, List<SupplyNode>> _provinceNodes = new();

    // Supply areas per strategic region
    private static Dictionary<int, SupplyArea> _supplyAreas = new();

    public sealed class SupplyNode
    {
        public int Id;
        public int Level;           // 1, 2, or 3
        public int ProvinceId;
        public float Capacity;      // Base supply capacity
        public float CurrentSupply; // Current available supply
        public bool IsActive = true;
    }

    public sealed class SupplyArea
    {
        public int RegionId;
        public List<int> NodeIds = new();
        public float PortCapacity = 0f;
        public Dictionary<int, float> ProvinceMotorization = new(); // province -> motorization level
    }

    // Level capacities (base values)
    private static readonly Dictionary<int, float> LevelBaseCapacity = new()
    {
        [1] = 1.0f,    // Basic supply points
        [2] = 3.0f,    // Regional hubs
        [3] = 10.0f,   // Major logistical centers
    };

    public static bool IsInitialized => _initialized;

    public static void Initialize()
    {
        if (_initialized) return;

        const string path = "res://Data/hoi4_imported/supply_nodes.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[SupplyNodeSystem] {path} not found!");
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
            var nodesList = JsonSerializer.Deserialize<List<JsonNode>>(json, options);

            if (nodesList != null)
            {
                int id = 0;
                foreach (var jn in nodesList)
                {
                    // Validate level
                    int level = Math.Clamp(jn.Level, 1, 3);

                    var node = new SupplyNode
                    {
                        Id = id++,
                        Level = level,
                        ProvinceId = jn.ProvinceId,
                        Capacity = LevelBaseCapacity[level],
                        CurrentSupply = LevelBaseCapacity[level]
                    };

                    _allNodes.Add(node);
                    _nodesByLevel[level].Add(node);

                    // Map province to nodes
                    if (!_provinceNodes.TryGetValue(jn.ProvinceId, out var list))
                    {
                        list = new List<SupplyNode>();
                        _provinceNodes[jn.ProvinceId] = list;
                    }
                    list.Add(node);
                }
            }

            // Create supply areas per strategic region
            foreach (var region in StrategicRegionSystem.Regions.Values)
            {
                var area = new SupplyArea { RegionId = region.Id };

                // Find nodes in this region's provinces
                foreach (var pid in region.Provinces)
                {
                    if (_provinceNodes.TryGetValue(pid, out var nodes))
                    {
                        foreach (var node in nodes)
                            area.NodeIds.Add(node.Id);
                    }
                }

                // Calculate port capacity for coastal provinces in region
                area.PortCapacity = CalculatePortCapacity(region);

                _supplyAreas[region.Id] = area;
            }

            _initialized = true;
            GD.Print($"[SupplyNodeSystem] Initialized: {_allNodes.Count} nodes (L1:{_nodesByLevel[1].Count}, L2:{_nodesByLevel[2].Count}, L3:{_nodesByLevel[3].Count}), {_supplyAreas.Count} supply areas");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[SupplyNodeSystem] Failed to load: {e.Message}");
        }
    }

    private static float CalculatePortCapacity(StrategicRegionSystem.StrategicRegion region)
    {
        // Simplified: coastal provinces with naval adjacency contribute port capacity
        float capacity = 0f;
        foreach (var pid in region.Provinces)
        {
            var adjs = ProvinceAdjacencySystem.GetAdjacenciesOfType(pid, ProvinceAdjacencySystem.AdjacencyType.Sea);
            if (adjs.Any())
                capacity += 2.0f; // Base port capacity
        }
        return capacity;
    }

    public static void ResetForNewGame()
    {
        foreach (var node in _allNodes)
        {
            node.CurrentSupply = node.Capacity;
            node.IsActive = true;
        }
    }

    /// <summary>
    /// Get all supply nodes in a province.
    /// </summary>
    public static IReadOnlyList<SupplyNode> GetNodesInProvince(int provinceId)
    {
        return _provinceNodes.GetValueOrDefault(provinceId) ?? new List<SupplyNode>();
    }

    /// <summary>
    /// Get the highest level supply node in a province.
    /// </summary>
    public static SupplyNode GetBestNodeInProvince(int provinceId)
    {
        var nodes = GetNodesInProvince(provinceId);
        return nodes.OrderByDescending(n => n.Level).FirstOrDefault();
    }

    /// <summary>
    /// Get all nodes of a specific level.
    /// </summary>
    public static IReadOnlyList<SupplyNode> GetNodesByLevel(int level)
    {
        return _nodesByLevel.GetValueOrDefault(level) ?? new List<SupplyNode>();
    }

    /// <summary>
    /// Get supply area for a strategic region.
    /// </summary>
    public static SupplyArea GetSupplyArea(int regionId)
    {
        return _supplyAreas.GetValueOrDefault(regionId);
    }

    /// <summary>
    /// Get supply area for a province's region.
    /// </summary>
    public static SupplyArea GetSupplyAreaForProvince(int provinceId)
    {
        var region = StrategicRegionSystem.GetRegionForProvince(provinceId);
        return region != null ? GetSupplyArea(region.Id) : null;
    }

    /// <summary>
    /// Daily supply distribution tick.
    /// Flow: Level 3 -> Level 2 -> Level 1 -> provinces.
    /// </summary>
    public static void DailySupplyTick()
    {
        // Reset current supply
        foreach (var node in _allNodes)
            node.CurrentSupply = 0f;

        // Distribute from level 3 down to 1
        for (int level = 3; level >= 1; level--)
        {
            foreach (var node in _nodesByLevel[level])
            {
                if (!node.IsActive) continue;

                float flow = node.Capacity;
                DistributeSupply(node.ProvinceId, flow, level);
            }
        }
    }

    private static void DistributeSupply(int startProvince, float flow, int fromLevel)
    {
        // Add to starting province's nodes
        var startNodes = GetNodesInProvince(startProvince);
        foreach (var n in startNodes)
            n.CurrentSupply += flow;

        // If level 1, stop (reached provinces)
        if (fromLevel <= 1) return;

        // Distribute via rail to neighboring provinces
        var connections = RailwaySystem.GetConnections(startProvince);
        foreach (var conn in connections)
        {
            float gaugeMult = RailwaySystem.GetConnectionThroughput(conn);
            float terrainPenalty = GetTerrainSupplyPenalty(conn.ToProvince);
            float nextFlow = flow * gaugeMult * terrainPenalty * conn.Efficiency;

            if (nextFlow > 0.5f) // Minimum threshold
            {
                DistributeSupply(conn.ToProvince, nextFlow, fromLevel - 1);
            }
        }
    }

    private static float GetTerrainSupplyPenalty(int provinceId)
    {
        // Get terrain for province
        var prov = ProvinceDataCache.GetById(provinceId.ToString());
        if (prov != null)
        {
            var terrainDef = TerrainSystem.Get(prov.Terrain);
            return 1.0f - terrainDef.SupplyFlowPenalty;
        }
        return 1.0f;
    }

    /// <summary>
    /// Get total available supply in a province.
    /// </summary>
    public static float GetProvinceSupply(int provinceId)
    {
        var nodes = GetNodesInProvince(provinceId);
        return nodes.Sum(n => n.CurrentSupply);
    }

    /// <summary>
    /// Get the best supply level available in a province.
    /// </summary>
    public static int GetProvinceSupplyLevel(int provinceId)
    {
        var nodes = GetNodesInProvince(provinceId);
        return nodes.Any() ? nodes.Max(n => n.Level) : 0;
    }

    /// <summary>
    /// Damage a supply node (e.g., from bombing).
    /// </summary>
    public static void DamageNode(int provinceId, float damage)
    {
        var nodes = GetNodesInProvince(provinceId);
        foreach (var node in nodes)
        {
            node.Capacity = Math.Max(0f, node.Capacity - damage);
            node.CurrentSupply = Math.Min(node.CurrentSupply, node.Capacity);
        }
    }

    /// <summary>
    /// Repair a supply node.
    /// </summary>
    public static void RepairNode(int provinceId, float repair)
    {
        var nodes = GetNodesInProvince(provinceId);
        foreach (var node in nodes)
        {
            node.Capacity = Math.Min(LevelBaseCapacity[node.Level], node.Capacity + repair);
        }
    }

    // JSON deserialization helper
    private class JsonNode
    {
        public int Level;
        public int ProvinceId;
    }
}