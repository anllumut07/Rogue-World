using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Supply network manager - nodes, railways, flow calculation
/// Based on HoI4 supply system with terrain penalties
/// </summary>
public class SupplyNetworkManager
{
    // Supply nodes by level (1, 2, 3)
    private Dictionary<int, List<SupplyNode>> _supplyNodes = new();

    // Railway connections
    private List<RailwayLine> _railways = new();
    private Dictionary<int, List<RailwayConnection>> _railConnections = new(); // province -> connections

    // Current supply values per province
    private Dictionary<int, float> _provinceSupply = new();

    // Gauge throughput multipliers
    private static readonly Dictionary<int, float> GaugeThroughput = new()
    {
        { 1, 1.0f }, // Standard gauge
        { 2, 0.8f }, // Broad gauge (Russian)
        { 3, 0.6f }  // Narrow gauge
    };

    public void AddSupplyNode(int level, int provinceId)
    {
        if (!_supplyNodes.ContainsKey(level))
            _supplyNodes[level] = new List<SupplyNode>();

        _supplyNodes[level].Add(new SupplyNode
        {
            Level = level,
            ProvinceId = provinceId,
            BaseCapacity = level * 10.0f // Level 1=10, Level 2=20, Level 3=30
        });
    }

    public void AddRailway(int gauge, List<int> provinces)
    {
        if (provinces.Count < 2) return;

        var line = new RailwayLine
        {
            Gauge = gauge,
            Provinces = new List<int>(provinces),
            ThroughputMultiplier = GaugeThroughput.GetValueOrDefault(gauge, 1.0f)
        };
        _railways.Add(line);

        // Build connection graph
        for (int i = 0; i < provinces.Count - 1; i++)
        {
            int from = provinces[i];
            int to = provinces[i + 1];

            AddRailConnection(from, to, line.ThroughputMultiplier);
            AddRailConnection(to, from, line.ThroughputMultiplier); // Bidirectional
        }
    }

    private void AddRailConnection(int from, int to, float throughput)
    {
        if (!_railConnections.ContainsKey(from))
            _railConnections[from] = new List<RailwayConnection>();

        _railConnections[from].Add(new RailwayConnection
        {
            From = from,
            To = to,
            ThroughputMultiplier = throughput
        });
    }

    /// <summary>
    /// Daily supply tick - propagate supply from nodes through railways
    /// </summary>
    public void DailyTick(ProvinceManager provinceManager)
    {
        // Reset supply
        _provinceSupply.Clear();

        // Process each level (3 -> 2 -> 1, higher level first)
        for (int level = 3; level >= 1; level--)
        {
            if (!_supplyNodes.ContainsKey(level)) continue;

            foreach (var node in _supplyNodes[level])
            {
                if (provinceManager.GetProvince(node.ProvinceId) is not { } province) continue;

                float flow = node.BaseCapacity * (province.Supply > 0 ? province.Supply : 0.1f);
                DistributeSupply(node.ProvinceId, flow, new HashSet<int>(), provinceManager);
            }
        }

        // Apply terrain penalties and finalize
        foreach (var prov in provinceManager.GetAllProvinces())
        {
            float supply = _provinceSupply.GetValueOrDefault(prov.Key, 0f);
            var terrain = prov.Value.Definition.Terrain;

            if (terrain != null)
            {
                // Apply terrain supply flow penalty
                supply *= (1f - terrain.SupplyFlowPenalty);
            }

            _provinceSupply[prov.Key] = Mathf.Clamp(supply, 0f, 100f);
        }
    }

    private void DistributeSupply(int provinceId, float flow, HashSet<int> visited, ProvinceManager provinceManager)
    {
        if (flow < 0.1f) return; // Minimum flow threshold
        if (visited.Contains(provinceId)) return; // Prevent cycles

        visited.Add(provinceId);

        // Add to province supply
        _provinceSupply[provinceId] = _provinceSupply.GetValueOrDefault(provinceId, 0f) + flow;

        // Distribute through railways
        if (_railConnections.TryGetValue(provinceId, out var connections))
        {
            foreach (var conn in connections)
            {
                var targetProv = provinceManager.GetProvince(conn.To);
                if (targetProv == null) continue;

                // Calculate flow to next province
                float terrainPenalty = 1f;
                if (targetProv.Definition.Terrain != null)
                    terrainPenalty = 1f - targetProv.Definition.Terrain.SupplyFlowPenalty;

                float nextFlow = flow * conn.ThroughputMultiplier * terrainPenalty * 0.9f; // 10% loss per hop

                if (nextFlow > 0.1f)
                    DistributeSupply(conn.To, nextFlow, visited, provinceManager);
            }
        }

        // Also distribute to adjacent land provinces (truck-based, lower efficiency)
        var adjGraph = provinceManager.GetAdjacencyGraph();
        foreach (int neighborId in adjGraph.GetLandNeighbors(provinceId))
        {
            var targetProv = provinceManager.GetProvince(neighborId);
            if (targetProv == null) continue;

            float terrainPenalty = 1f;
            if (targetProv.Definition.Terrain != null)
                terrainPenalty = 1f - targetProv.Definition.Terrain.SupplyFlowPenalty;

            // Truck supply: much lower efficiency, affected by truck attrition
            float truckEfficiency = 0.3f; // Base truck efficiency
            if (targetProv.Definition.Terrain != null)
                truckEfficiency *= (1f - targetProv.Definition.Terrain.TruckAttritionFactor * 0.1f);

            float nextFlow = flow * truckEfficiency * terrainPenalty * 0.5f; // 50% loss per province

            if (nextFlow > 0.1f)
                DistributeSupply(neighborId, nextFlow, visited, provinceManager);
        }
    }

    public float GetProvinceSupply(int provinceId)
    {
        return _provinceSupply.GetValueOrDefault(provinceId, 0f);
    }

    public IReadOnlyDictionary<int, float> GetAllSupply()
    {
        return _provinceSupply;
    }

    public List<SupplyNode> GetSupplyNodes(int level)
    {
        return _supplyNodes.GetValueOrDefault(level, new List<SupplyNode>());
    }
}

public class SupplyNode
{
    public int Level { get; set; }
    public int ProvinceId { get; set; }
    public float BaseCapacity { get; set; }
}

public class RailwayLine
{
    public int Gauge { get; set; }
    public List<int> Provinces { get; set; } = new();
    public float ThroughputMultiplier { get; set; } = 1.0f;
}

public class RailwayConnection
{
    public int From { get; set; }
    public int To { get; set; }
    public float ThroughputMultiplier { get; set; }
}