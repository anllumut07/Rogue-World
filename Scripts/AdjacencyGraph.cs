using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Adjacency graph for province connections (land/sea movement, straits, impassable borders)
/// </summary>
public class AdjacencyGraph
{
    private Dictionary<int, List<Adjacency>> _adjacencies = new();
    private Dictionary<string, List<Adjacency>> _byRuleName = new();

    public int AdjacencyCount => _adjacencies.Values.Sum(list => list.Count);

    public void AddAdjacency(int from, int to, string type, int through, string ruleName)
    {
        if (!_adjacencies.ContainsKey(from))
            _adjacencies[from] = new List<Adjacency>();

        var adj = new Adjacency
        {
            From = from,
            To = to,
            Type = type,
            ThroughProvince = through,
            RuleName = ruleName
        };

        _adjacencies[from].Add(adj);

        // Also add reverse for undirected connections (sea/land)
        if (type != "impassable")
        {
            if (!_adjacencies.ContainsKey(to))
                _adjacencies[to] = new List<Adjacency>();
            _adjacencies[to].Add(new Adjacency
            {
                From = to,
                To = from,
                Type = type,
                ThroughProvince = through,
                RuleName = ruleName
            });
        }

        // Index by rule name for strait/canal lookups
        if (!string.IsNullOrEmpty(ruleName))
        {
            if (!_byRuleName.ContainsKey(ruleName))
                _byRuleName[ruleName] = new List<Adjacency>();
            _byRuleName[ruleName].Add(adj);
        }
    }

    /// <summary>
    /// Get all adjacent provinces for a given province
    /// </summary>
    public List<Adjacency> GetAdjacent(int provinceId)
    {
        return _adjacencies.GetValueOrDefault(provinceId, new List<Adjacency>());
    }

    /// <summary>
    /// Get only land adjacencies (for army movement)
    /// </summary>
    public List<int> GetLandNeighbors(int provinceId)
    {
        var result = new List<int>();
        foreach (var adj in GetAdjacent(provinceId))
        {
            if (adj.Type == "" || adj.Type == "land") // Empty type = normal land connection
                result.Add(adj.To);
        }
        return result;
    }

    /// <summary>
    /// Get only sea adjacencies (for naval movement)
    /// </summary>
    public List<int> GetSeaNeighbors(int provinceId)
    {
        var result = new List<int>();
        foreach (var adj in GetAdjacent(provinceId))
        {
            if (adj.Type == "sea")
                result.Add(adj.To);
        }
        return result;
    }

    /// <summary>
    /// Check if two provinces are adjacent (any type)
    /// </summary>
    public bool AreAdjacent(int from, int to)
    {
        if (!_adjacencies.TryGetValue(from, out var list)) return false;
        return list.Any(a => a.To == to);
    }

    /// <summary>
    /// Get adjacency info between two provinces
    /// </summary>
    public Adjacency GetAdjacency(int from, int to)
    {
        if (!_adjacencies.TryGetValue(from, out var list)) return null;
        return list.FirstOrDefault(a => a.To == to);
    }

    /// <summary>
    /// Get all adjacencies for a strait/canal rule
    /// </summary>
    public List<Adjacency> GetByRuleName(string ruleName)
    {
        return _byRuleName.GetValueOrDefault(ruleName, new List<Adjacency>());
    }

    /// <summary>
    /// Check if movement is blocked by impassable border
    /// </summary>
    public bool IsImpassable(int from, int to)
    {
        var adj = GetAdjacency(from, to);
        return adj != null && adj.Type == "impassable";
    }

    /// <summary>
    /// Find path between provinces using A* (simplified - uses terrain costs from ProvinceManager)
    /// </summary>
    public List<int> FindPath(int from, int to, Func<int, int, float> costFunc, bool naval = false)
    {
        if (!_adjacencies.ContainsKey(from) || !_adjacencies.ContainsKey(to))
            return null;

        var openSet = new PriorityQueue<int, float>();
        var cameFrom = new Dictionary<int, int>();
        var gScore = new Dictionary<int, float> { [from] = 0 };
        var fScore = new Dictionary<int, float> { [from] = Heuristic(from, to) };

        openSet.Enqueue(from, fScore[from]);

        while (openSet.Count > 0)
        {
            int current = openSet.Dequeue();

            if (current == to)
                return ReconstructPath(cameFrom, current);

            foreach (var adj in GetAdjacent(current))
            {
                // Filter by movement type
                if (naval && adj.Type != "sea") continue;
                if (!naval && adj.Type == "sea") continue;
                if (adj.Type == "impassable") continue;

                float cost = costFunc(current, adj.To);
                float tentativeG = gScore[current] + cost;

                if (!gScore.ContainsKey(adj.To) || tentativeG < gScore[adj.To])
                {
                    cameFrom[adj.To] = current;
                    gScore[adj.To] = tentativeG;
                    fScore[adj.To] = tentativeG + Heuristic(adj.To, to);
                    openSet.Enqueue(adj.To, fScore[adj.To]);
                }
            }
        }

        return null; // No path found
    }

    private float Heuristic(int a, int b)
    {
        // Simple Euclidean distance heuristic (province centroids)
        // In practice, would use actual centroid positions
        return Math.Abs(a - b) * 0.1f;
    }

    private List<int> ReconstructPath(Dictionary<int, int> cameFrom, int current)
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
}

public class Adjacency
{
    public int From { get; set; }
    public int To { get; set; }
    public string Type { get; set; } // "land", "sea", "impassable", ""
    public int ThroughProvince { get; set; } = -1; // Province that must be controlled for strait/canal
    public string RuleName { get; set; } = ""; // Strait/canal rule name
}

/// <summary>
/// Simple priority queue for A*
/// </summary>
public class PriorityQueue<T, TPriority> where TPriority : IComparable<TPriority>
{
    private List<(T item, TPriority priority)> _items = new();

    public int Count => _items.Count;

    public void Enqueue(T item, TPriority priority)
    {
        _items.Add((item, priority));
        _items.Sort((a, b) => a.priority.CompareTo(b.priority));
    }

    public T Dequeue()
    {
        var item = _items[0].item;
        _items.RemoveAt(0);
        return item;
    }
}