using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// RailwaySystem - Railway network with gauge types affecting supply throughput.
/// Based on HoI4's map/railways.txt (1,284 lines).
/// </summary>
public static class RailwaySystem
{
    private static bool _initialized = false;

    // Railway graph: provinceId -> List of connected provinces via rail
    private static Dictionary<int, List<RailConnection>> _railways = new();

    // All railway lines for reference
    private static List<RailwayLine> _allLines = new();

    public enum GaugeType
    {
        Standard = 1,      // 1.0x throughput (1,226 lines)
        Broad = 2,         // 0.8x throughput (50 lines, Russian)
        Narrow = 3         // 0.6x throughput (8 lines)
    }

    public sealed class RailConnection
    {
        public int ToProvince;
        public GaugeType Gauge;
        public float Efficiency = 1.0f; // 0-1, can be damaged
        public int LineId;
    }

    public sealed class RailwayLine
    {
        public int Id;
        public GaugeType Gauge;
        public List<int> Provinces = new(); // Connected province chain
    }

    // Gauge throughput multipliers
    private static readonly Dictionary<GaugeType, float> GaugeThroughput = new()
    {
        [GaugeType.Standard] = 1.0f,
        [GaugeType.Broad] = 0.8f,
        [GaugeType.Narrow] = 0.6f,
    };

    public static bool IsInitialized => _initialized;

    public static void Initialize()
    {
        if (_initialized) return;

        const string path = "res://Data/hoi4_imported/railways.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[RailwaySystem] {path} not found!");
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
            var lines = JsonSerializer.Deserialize<List<JsonLine>>(json, options);

            if (lines != null)
            {
                int lineId = 0;
                foreach (var jl in lines)
                {
                    GaugeType gauge = (GaugeType)jl.OwnerLevel;
                    if (!Enum.IsDefined(typeof(GaugeType), gauge))
                        gauge = GaugeType.Standard;

                    var railway = new RailwayLine
                    {
                        Id = lineId++,
                        Gauge = gauge,
                        Provinces = jl.Provinces ?? new List<int>()
                    };
                    _allLines.Add(railway);

                    // Build connections between consecutive provinces
                    for (int i = 0; i < railway.Provinces.Count - 1; i++)
                    {
                        int from = railway.Provinces[i];
                        int to = railway.Provinces[i + 1];

                        AddConnection(from, to, gauge, railway.Id);
                        AddConnection(to, from, gauge, railway.Id); // Bidirectional
                    }
                }
            }

            _initialized = true;
            GD.Print($"[RailwaySystem] Initialized: {_allLines.Count} lines, {_railways.Count} provinces connected");

            // Stats
            var gaugeStats = _allLines.GroupBy(l => l.Gauge)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            GD.Print($"[RailwaySystem] Gauge distribution: {string.Join(", ", gaugeStats)}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[RailwaySystem] Failed to load: {e.Message}");
        }
    }

    private static void AddConnection(int from, int to, GaugeType gauge, int lineId)
    {
        if (!_railways.TryGetValue(from, out var list))
        {
            list = new List<RailConnection>();
            _railways[from] = list;
        }

        // Avoid duplicates
        if (!list.Any(c => c.ToProvince == to && c.Gauge == gauge))
        {
            list.Add(new RailConnection
            {
                ToProvince = to,
                Gauge = gauge,
                LineId = lineId
            });
        }
    }

    public static void ResetForNewGame()
    {
        // Reset efficiency to 1.0 for all connections
        foreach (var list in _railways.Values)
        {
            foreach (var conn in list)
                conn.Efficiency = 1.0f;
        }
    }

    /// <summary>
    /// Get all rail connections from a province.
    /// </summary>
    public static IReadOnlyList<RailConnection> GetConnections(int provinceId)
    {
        return _railways.GetValueOrDefault(provinceId) ?? new List<RailConnection>();
    }

    /// <summary>
    /// Get throughput multiplier for a gauge type.
    /// </summary>
    public static float GetGaugeThroughput(GaugeType gauge)
    {
        return GaugeThroughput.GetValueOrDefault(gauge, 1.0f);
    }

    /// <summary>
    /// Get throughput multiplier for a connection.
    /// </summary>
    public static float GetConnectionThroughput(RailConnection conn)
    {
        return GaugeThroughput.GetValueOrDefault(conn.Gauge, 1.0f) * conn.Efficiency;
    }

    /// <summary>
    /// Damage a railway line (reduces efficiency).
    /// </summary>
    public static void DamageLine(int lineId, float damageAmount)
    {
        if (lineId >= 0 && lineId < _allLines.Count)
        {
            var line = _allLines[lineId];
            foreach (var conn in GetConnectionsForLine(lineId))
            {
                conn.Efficiency = Math.Max(0f, conn.Efficiency - damageAmount);
            }
        }
    }

    /// <summary>
    /// Repair a railway line.
    /// </summary>
    public static void RepairLine(int lineId, float repairAmount)
    {
        if (lineId >= 0 && lineId < _allLines.Count)
        {
            foreach (var conn in GetConnectionsForLine(lineId))
            {
                conn.Efficiency = Math.Min(1.0f, conn.Efficiency + repairAmount);
            }
        }
    }

    private static IEnumerable<RailConnection> GetConnectionsForLine(int lineId)
    {
        foreach (var list in _railways.Values)
        {
            foreach (var conn in list)
            {
                if (conn.LineId == lineId)
                    yield return conn;
            }
        }
    }

    /// <summary>
    /// Check if two provinces are connected by rail.
    /// </summary>
    public static bool AreConnectedByRail(int provinceA, int provinceB)
    {
        if (_railways.TryGetValue(provinceA, out var list))
        {
            return list.Any(c => c.ToProvince == provinceB);
        }
        return false;
    }

    /// <summary>
    /// Get the best gauge available between two provinces.
    /// </summary>
    public static GaugeType GetBestGauge(int provinceA, int provinceB)
    {
        if (_railways.TryGetValue(provinceA, out var list))
        {
            var conn = list.FirstOrDefault(c => c.ToProvince == provinceB);
            return conn?.Gauge ?? GaugeType.Standard;
        }
        return GaugeType.Standard;
    }

    /// <summary>
    /// Get all railway lines.
    /// </summary>
    public static IReadOnlyList<RailwayLine> GetAllLines()
    {
        return _allLines;
    }

    // JSON deserialization helper
    private class JsonLine
    {
        public int OwnerLevel;
        public int NumSegments;
        public List<int> Provinces;
    }
}