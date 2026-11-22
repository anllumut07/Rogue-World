using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FileAccess = Godot.FileAccess;

/// <summary>
/// Main runtime manager for HoI4-style province system.
/// Handles province state, ownership, adjacency, supply, buildings, weather.
/// </summary>
public partial class ProvinceManager : Node
{
    public static ProvinceManager Instance { get; private set; }

    // Core data
    private Dictionary<int, ProvinceRuntime> _provinces = new();
    private Dictionary<string, List<int>> _countryProvinces = new(); // country tag -> province IDs
    private AdjacencyGraph _adjacencyGraph;
    private StraitCanalManager _straitManager;
    private SupplyNetworkManager _supplyNetwork;
    private BuildingManager _buildingManager;

    // Map dimensions
    public int MapWidth => ProvinceBitmapLoader.MapWidth;
    public int MapHeight => ProvinceBitmapLoader.MapHeight;

    // Signals
    [Signal] public delegate void ProvinceOwnerChangedEventHandler(int provinceId, string oldOwner, string newOwner);
    [Signal] public delegate void ProvinceSupplyChangedEventHandler(int provinceId, float oldSupply, float newSupply);

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[ProvinceManager] Ready");
    }

    /// <summary>
    /// Initialize the complete province system
    /// </summary>
    public async Task InitializeAsync()
    {
        GD.Print("[ProvinceManager] Initializing province system...");

        // 1. Load province definitions and bitmap
        await ProvinceBitmapLoader.InitializeAsync();

        // 2. Load terrain system
        await TerrainBitmapLoader.InitializeAsync();

        // 3. Load heightmap
        await HeightmapLoader.InitializeAsync();

        // 4. Load rivers
        await RiversLoader.InitializeAsync();

        // 5. Create runtime province objects
        CreateProvinceRuntimes();

        // 6. Load adjacency graph (from imported JSON)
        LoadAdjacencyGraph();

        // 7. Load strait/canal rules
        LoadStraitCanalRules();

        // 8. Load supply network
        LoadSupplyNetwork();

        // 9. Load buildings
        LoadBuildings();

        // 10. Assign terrain to provinces
        AssignTerrainToProvinces();

        // 11. Assign strategic regions
        AssignStrategicRegions();

        GD.Print($"[ProvinceManager] Initialization complete: {_provinces.Count} provinces");
    }

    private void CreateProvinceRuntimes()
    {
        foreach (var kvp in ProvinceBitmapLoader.GetAllProvinces())
        {
            var def = kvp.Value;
            var runtime = new ProvinceRuntime
            {
                Definition = def,
                Owner = "", // Will be set from scenario
                Controller = "",
                Supply = 1.0f,
                Units = new List<ArmyRuntime>(),
                Buildings = new List<BuildingRuntime>(),
                WeatherState = new WeatherState()
            };
            _provinces[def.Id] = runtime;
        }
    }

    private void AssignTerrainToProvinces()
    {
        GD.Print("[ProvinceManager] Assigning terrain to provinces...");

        int assigned = 0;
        foreach (var prov in _provinces.Values)
        {
            var def = prov.Definition;
            if (def.Type == ProvinceType.Land)
            {
                // Sample terrain at province centroid
                var terrain = TerrainBitmapLoader.GetTerrainAt(def.Centroid);
                if (terrain != null)
                {
                    def.Terrain = terrain;
                    def.TerrainPaletteIndex = terrain.PaletteIndex;
                    assigned++;
                }
            }
            else if (def.Type == ProvinceType.Sea || def.Type == ProvinceType.Lake)
            {
                // Water provinces get water terrain
                var waterTerrain = TerrainBitmapLoader.GetTerrainByName(def.Type == ProvinceType.Lake ? "lakes" : "ocean");
                if (waterTerrain != null)
                {
                    def.Terrain = waterTerrain;
                    def.TerrainPaletteIndex = waterTerrain.PaletteIndex;
                    assigned++;
                }
            }
        }

        GD.Print($"[ProvinceManager] Assigned terrain to {assigned} provinces");
    }

    private void AssignStrategicRegions()
    {
        // Load from imported strategic_regions.json
        string path = "res://Data/hoi4_imported/strategic_regions.json";
        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr("[ProvinceManager] strategic_regions.json not found");
            return;
        }

        var json = FileAccess.GetFileAsString(path);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        int assigned = 0;

        foreach (var regionElem in doc.RootElement.EnumerateArray())
        {
            int regionId = regionElem.GetProperty("id").GetInt32();
            if (regionElem.TryGetProperty("provinces", out var provsElem))
            {
                foreach (var provElem in provsElem.EnumerateArray())
                {
                    int provId = provElem.GetInt32();
                    if (_provinces.TryGetValue(provId, out var prov))
                    {
                        prov.Definition.StrategicRegionId = regionId;
                        assigned++;
                    }
                }
            }
        }

        GD.Print($"[ProvinceManager] Assigned {assigned} provinces to strategic regions");
    }

    private void LoadAdjacencyGraph()
    {
        _adjacencyGraph = new AdjacencyGraph();
        string path = "res://Data/hoi4_imported/province_adjacencies.json";

        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr("[ProvinceManager] province_adjacencies.json not found");
            return;
        }

        var json = FileAccess.GetFileAsString(path);
        var doc = System.Text.Json.JsonDocument.Parse(json);

        foreach (var adjElem in doc.RootElement.EnumerateArray())
        {
            int from = adjElem.GetProperty("From").GetInt32();
            int to = adjElem.GetProperty("To").GetInt32();
            string type = adjElem.GetProperty("Type").GetString() ?? "";
            int through = adjElem.GetProperty("Through").GetInt32();
            string ruleName = adjElem.GetProperty("Comment").GetString() ?? ""; // Using Comment as rule name

            _adjacencyGraph.AddAdjacency(from, to, type, through, ruleName);

            // Also add to province runtime for quick access
            if (_provinces.TryGetValue(from, out var fromProv))
                fromProv.Definition.NeighborProvinces.Add(to);
            if (_provinces.TryGetValue(to, out var toProv))
                toProv.Definition.NeighborProvinces.Add(from);
        }

        GD.Print($"[ProvinceManager] Loaded adjacency graph: {_adjacencyGraph.AdjacencyCount} connections");
    }

    private void LoadStraitCanalRules()
    {
        _straitManager = new StraitCanalManager();
        string path = "res://Data/hoi4_imported/strait_canal_rules.json";

        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr("[ProvinceManager] strait_canal_rules.json not found");
            return;
        }

        var json = FileAccess.GetFileAsString(path);
        var doc = System.Text.Json.JsonDocument.Parse(json);

        foreach (var ruleElem in doc.RootElement.EnumerateArray())
        {
            string name = ruleElem.GetProperty("name").GetString() ?? "";
            var rule = new StraitCanalRule { Name = name };

            // Parse permissions
            foreach (var permType in new[] { "contested", "enemy", "friend", "neutral" })
            {
                if (ruleElem.TryGetProperty(permType, out var permElem))
                {
                    var perms = new StraitPermissions();
                    if (permElem.TryGetProperty("army", out var a)) perms.Army = a.GetBoolean();
                    if (permElem.TryGetProperty("navy", out var n)) perms.Navy = n.GetBoolean();
                    if (permElem.TryGetProperty("submarine", out var s)) perms.Submarine = s.GetBoolean();
                    if (permElem.TryGetProperty("trade", out var t)) perms.Trade = t.GetBoolean();

                    switch (permType)
                    {
                        case "contested": rule.Contested = perms; break;
                        case "enemy": rule.Enemy = perms; break;
                        case "friend": rule.Friend = perms; break;
                        case "neutral": rule.Neutral = perms; break;
                    }
                }
            }

            // Required provinces
            if (ruleElem.TryGetProperty("required_provinces", out var reqElem))
            {
                foreach (var p in reqElem.EnumerateArray())
                    rule.RequiredProvinces.Add(p.GetInt32());
            }

            _straitManager.AddRule(name, rule);
        }

        GD.Print($"[ProvinceManager] Loaded {_straitManager.RuleCount} strait/canal rules");
    }

    private void LoadSupplyNetwork()
    {
        _supplyNetwork = new SupplyNetworkManager();

        string nodesPath = "res://Data/hoi4_imported/supply_nodes.json";
        if (ResourceLoader.Exists(nodesPath))
        {
            var json = FileAccess.GetFileAsString(nodesPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var nodeElem in doc.RootElement.EnumerateArray())
            {
                int level = nodeElem.GetProperty("level").GetInt32();
                int provinceId = nodeElem.GetProperty("province").GetInt32();
                _supplyNetwork.AddSupplyNode(level, provinceId);
            }
        }

        string railwaysPath = "res://Data/hoi4_imported/railways.json";
        if (ResourceLoader.Exists(railwaysPath))
        {
            var json = FileAccess.GetFileAsString(railwaysPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var railElem in doc.RootElement.EnumerateArray())
            {
                int gauge = railElem.GetProperty("owner_level").GetInt32();
                if (railElem.TryGetProperty("provinces", out var provsElem))
                {
                    var provinces = new List<int>();
                    foreach (var p in provsElem.EnumerateArray())
                        provinces.Add(p.GetInt32());
                    _supplyNetwork.AddRailway(gauge, provinces);
                }
            }
        }

        GD.Print("[ProvinceManager] Supply network loaded");
    }

    private void LoadBuildings()
    {
        _buildingManager = new BuildingManager();
        string path = "res://Data/hoi4_imported/buildings.json";

        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr("[ProvinceManager] buildings.json not found");
            return;
        }

        var json = FileAccess.GetFileAsString(path);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        int loaded = 0;

        foreach (var bldElem in doc.RootElement.EnumerateArray())
        {
            int provinceId = bldElem.GetProperty("province").GetInt32();
            string type = bldElem.GetProperty("type").GetString() ?? "";
            float x = bldElem.GetProperty("x").GetSingle();
            float y = bldElem.GetProperty("y").GetSingle();
            float z = bldElem.GetProperty("z").GetSingle();
            float rotation = bldElem.GetProperty("rotation").GetSingle();
            int level = bldElem.GetProperty("level").GetInt32();

            if (_provinces.TryGetValue(provinceId, out var prov))
            {
                var building = new BuildingRuntime
                {
                    Type = type,
                    Position3D = new Vector3(x, y, z),
                    Rotation = rotation,
                    Level = level,
                    ProvinceId = provinceId
                };
                prov.Buildings.Add(building);
                _buildingManager.RegisterBuilding(building);
                loaded++;
            }
        }

        GD.Print($"[ProvinceManager] Loaded {loaded} buildings");
    }

    /// <summary>
    /// Set province owner (for scenario setup, peace conferences, occupation)
    /// </summary>
    public void SetProvinceOwner(int provinceId, string countryTag)
    {
        if (!_provinces.TryGetValue(provinceId, out var prov)) return;

        string oldOwner = prov.Owner;
        if (oldOwner == countryTag) return;

        prov.Owner = countryTag;
        prov.Controller = countryTag;

        // Update country-province mapping
        if (!string.IsNullOrEmpty(oldOwner) && _countryProvinces.TryGetValue(oldOwner, out var oldList))
            oldList.Remove(provinceId);

        if (!_countryProvinces.TryGetValue(countryTag, out var newList))
        {
            newList = new List<int>();
            _countryProvinces[countryTag] = newList;
        }
        newList.Add(provinceId);

        EmitSignal(SignalName.ProvinceOwnerChanged, provinceId, oldOwner, countryTag);
        GD.Print($"[ProvinceManager] Province {provinceId} owner: {oldOwner} -> {countryTag}");
    }

    /// <summary>
    /// Get province runtime data
    /// </summary>
    public ProvinceRuntime GetProvince(int provinceId)
    {
        return _provinces.GetValueOrDefault(provinceId);
    }

    /// <summary>
    /// Get province by world position (mouse click)
    /// </summary>
    public ProvinceRuntime GetProvinceAtWorldPos(Vector2 worldPos)
    {
        var def = ProvinceBitmapLoader.GetProvinceAtWorldPos(worldPos);
        if (def != null)
            return _provinces.GetValueOrDefault(def.Id);
        return null;
    }

    /// <summary>
    /// Get province by RGB color (from province map texture)
    /// </summary>
    public ProvinceRuntime GetProvinceByRgb(byte r, byte g, byte b)
    {
        var def = ProvinceBitmapLoader.GetProvinceByRgb(r, g, b);
        if (def != null)
            return _provinces.GetValueOrDefault(def.Id);
        return null;
    }

    /// <summary>
    /// Get all provinces owned by a country
    /// </summary>
    public IReadOnlyList<int> GetCountryProvinces(string countryTag)
    {
        return _countryProvinces.GetValueOrDefault(countryTag, new List<int>());
    }

    /// <summary>
    /// Get adjacency graph
    /// </summary>
    public AdjacencyGraph GetAdjacencyGraph()
    {
        return _adjacencyGraph;
    }

    /// <summary>
    /// Get strait/canal manager
    /// </summary>
    public StraitCanalManager GetStraitManager()
    {
        return _straitManager;
    }

    /// <summary>
    /// Get supply network
    /// </summary>
    public SupplyNetworkManager GetSupplyNetwork()
    {
        return _supplyNetwork;
    }

    /// <summary>
    /// Get building manager
    /// </summary>
    public BuildingManager GetBuildingManager()
    {
        return _buildingManager;
    }

    /// <summary>
    /// Get all provinces (for iteration)
    /// </summary>
    public IReadOnlyDictionary<int, ProvinceRuntime> GetAllProvinces()
    {
        return _provinces;
    }

    /// <summary>
    /// Daily tick - update weather, supply, etc.
    /// </summary>
    public void DailyTick(float dayProgress)
    {
        // Update weather per strategic region
        UpdateWeather(dayProgress);

        // Update supply flow
        _supplyNetwork?.DailyTick(this);

        // Update province supply values
        foreach (var prov in _provinces.Values)
        {
            float oldSupply = prov.Supply;
            prov.Supply = _supplyNetwork.GetProvinceSupply(prov.Definition.Id);
            if (Mathf.Abs(prov.Supply - oldSupply) > 0.01f)
                EmitSignal(SignalName.ProvinceSupplyChanged, prov.Definition.Id, oldSupply, prov.Supply);
        }
    }

    private void UpdateWeather(float dayProgress)
    {
        // Weather is handled by ClimateSystem/WeatherSystem per strategic region
        // This just updates province-level weather state from region weather
    }

    // ===== Save/Load =====

    public ProvinceManagerSaveData GetSaveData()
    {
        var data = new ProvinceManagerSaveData
        {
            Provinces = new List<ProvinceSaveData>(),
            CountryProvinces = new Dictionary<string, List<int>>(_countryProvinces)
        };

        foreach (var prov in _provinces.Values)
        {
            if (prov.Definition.Id == 0) continue; // Skip invalid
            data.Provinces.Add(new ProvinceSaveData
            {
                Id = prov.Definition.Id,
                Owner = prov.Owner,
                Controller = prov.Controller,
                Supply = prov.Supply,
                WeatherState = prov.WeatherState
            });
        }

        return data;
    }

    public void LoadSaveData(ProvinceManagerSaveData data)
    {
        _countryProvinces.Clear();
        foreach (var kvp in data.CountryProvinces)
            _countryProvinces[kvp.Key] = new List<int>(kvp.Value);

        foreach (var provData in data.Provinces)
        {
            if (_provinces.TryGetValue(provData.Id, out var prov))
            {
                prov.Owner = provData.Owner;
                prov.Controller = provData.Controller;
                prov.Supply = provData.Supply;
                prov.WeatherState = provData.WeatherState;
            }
        }
    }
}

/// <summary>
/// Runtime province state (mutable game state)
/// </summary>
public class ProvinceRuntime
{
    public ProvinceDefinition Definition { get; set; }
    public string Owner { get; set; } = "";
    public string Controller { get; set; } = "";
    public float Supply { get; set; } = 1.0f;
    public List<ArmyRuntime> Units { get; set; } = new();
    public List<BuildingRuntime> Buildings { get; set; } = new();
    public WeatherState WeatherState { get; set; } = new();
    public float FortificationLevel { get; set; } = 0.0f;
    public int VictoryPoints { get; set; } = 0;
    public bool IsOccupied => !string.IsNullOrEmpty(Controller) && Controller != Owner;
}

public class ArmyRuntime
{
    public string Id { get; set; }
    public string CountryTag { get; set; }
    public int ProvinceId { get; set; }
    public float Strength { get; set; }
    public float Organization { get; set; }
    public List<DivisionTemplate> Divisions { get; set; } = new();
}

public class DivisionTemplate
{
    public string Name { get; set; }
    public Dictionary<string, int> Battalions { get; set; } = new();
    public float Org { get; set; }
    public float Hp { get; set; }
}

public class BuildingRuntime
{
    public string Type { get; set; }
    public Vector3 Position3D { get; set; }
    public float Rotation { get; set; }
    public int Level { get; set; }
    public int ProvinceId { get; set; }
}

public class WeatherState
{
    public string Phenomenon { get; set; } = "none";
    public float Temperature { get; set; } = 15.0f;
    public float MudLevel { get; set; } = 0.0f;
    public float SnowLevel { get; set; } = 0.0f;
}

public class ProvinceManagerSaveData
{
    public List<ProvinceSaveData> Provinces { get; set; } = new();
    public Dictionary<string, List<int>> CountryProvinces { get; set; } = new();
}

public class ProvinceSaveData
{
    public int Id { get; set; }
    public string Owner { get; set; }
    public string Controller { get; set; }
    public float Supply { get; set; }
    public WeatherState WeatherState { get; set; }
}