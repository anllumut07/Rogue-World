using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// BuildingSystem - Map buildings with 3D placement, terrain limits, and levels.
/// Based on HoI4's map/buildings.txt (75,676 buildings).
/// </summary>
public static class BuildingSystem
{
    private static bool _initialized = false;

    // Buildings by province
    private static Dictionary<int, List<Building>> _provinceBuildings = new();

    // All buildings flat list
    private static List<Building> _allBuildings = new();

    // Building type definitions (from HoI4)
    private static Dictionary<string, BuildingType> _buildingTypes = new();

    public sealed class Building
    {
        public int Id;
        public string Type;           // e.g., "arms_factory", "air_base", "bunker"
        public int ProvinceId;
        public Vector3 Position;      // 3D coordinates (x, y, z)
        public float Rotation;        // Y-axis rotation in radians
        public int Level = 0;         // Current level (0 = base)
        public int MaxLevel = 5;      // Max level (varies by type/terrain)
        public float Health = 100f;   // 0-100, damaged by bombing
        public bool IsActive = true;
        public int OwnerCountryId = 0; // Country that owns/controls this building
    }

    public sealed class BuildingType
    {
        public string Name;                    // Internal name
        public string DisplayName;             // Localization key
        public int BaseMaxLevel = 5;
        public Dictionary<string, int> TerrainMaxLevel = new(); // terrain -> max level
        public float ConstructionCost = 100f;  // Base cost
        public float ConstructionTime = 100f;  // Days to build level 1
        public Dictionary<string, float> Effects = new(); // building effects
        public string ModelPath = "";          // 3D model path
        public bool IsMilitary = false;
        public bool IsNaval = false;
        public bool IsAir = false;
        public bool RequiresCoastal = false;
    }

    public static bool IsInitialized => _initialized;

    public static void Initialize()
    {
        if (_initialized) return;

        // Initialize building type definitions
        InitializeBuildingTypes();

        const string path = "res://Data/hoi4_imported/buildings.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[BuildingSystem] {path} not found!");
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
            var buildingsList = JsonSerializer.Deserialize<List<JsonBuilding>>(json, options);

            if (buildingsList != null)
            {
                int id = 0;
                foreach (var jb in buildingsList)
                {
                    // Skip if building type is null/empty or unknown
                    if (string.IsNullOrEmpty(jb.BuildingType) || !_buildingTypes.ContainsKey(jb.BuildingType))
                        continue;

                    var btype = _buildingTypes[jb.BuildingType];

                    var building = new Building
                    {
                        Id = jb.ProvinceId * 10000 + id++, // Unique ID
                        Type = jb.BuildingType,
                        ProvinceId = jb.ProvinceId,
                        Position = new Vector3(jb.X, jb.Y, jb.Z),
                        Rotation = jb.Rotation,
                        Level = jb.Level,
                        MaxLevel = GetMaxLevelForTerrain(jb.BuildingType, jb.ProvinceId),
                        OwnerCountryId = GetProvinceOwner(jb.ProvinceId)
                    };

                    _allBuildings.Add(building);

                    if (!_provinceBuildings.TryGetValue(jb.ProvinceId, out var list))
                    {
                        list = new List<Building>();
                        _provinceBuildings[jb.ProvinceId] = list;
                    }
                    list.Add(building);
                    id++;
                }
            }

            _initialized = true;
            GD.Print($"[BuildingSystem] Initialized: {_allBuildings.Count} buildings across {_provinceBuildings.Count} provinces");

            // Stats by type
            var typeStats = _allBuildings.GroupBy(b => b.Type)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            GD.Print($"[BuildingSystem] Types: {string.Join(", ", typeStats)}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BuildingSystem] Failed to load: {e.Message}");
        }
    }

    private static void InitializeBuildingTypes()
    {
        // Military buildings
        AddType(new BuildingType
        {
            Name = "arms_factory",
            DisplayName = "Arms Factory",
            BaseMaxLevel = 5,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 5, ["forest"] = 4, ["hills"] = 4, ["mountain"] = 2,
                ["urban"] = 5, ["desert"] = 3, ["jungle"] = 2, ["marsh"] = 2
            },
            ConstructionCost = 150f,
            ConstructionTime = 120f,
            Effects = new Dictionary<string, float> { { "military_factory_output", 5f } },
            IsMilitary = true
        });

        AddType(new BuildingType
        {
            Name = "industrial_complex",
            DisplayName = "Industrial Complex",
            BaseMaxLevel = 5,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 5, ["forest"] = 4, ["hills"] = 4, ["mountain"] = 2,
                ["urban"] = 5, ["desert"] = 3, ["jungle"] = 2, ["marsh"] = 2
            },
            ConstructionCost = 200f,
            ConstructionTime = 150f,
            Effects = new Dictionary<string, float> { { "civilian_factory_output", 5f }, { "construction_speed", 0.1f } }
        });

        AddType(new BuildingType
        {
            Name = "air_base",
            DisplayName = "Air Base",
            BaseMaxLevel = 3,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 3, ["forest"] = 2, ["hills"] = 2, ["mountain"] = 1,
                ["urban"] = 3, ["desert"] = 3, ["jungle"] = 1, ["marsh"] = 1
            },
            ConstructionCost = 100f,
            ConstructionTime = 90f,
            Effects = new Dictionary<string, float> { { "air_capacity", 200f }, { "air_defense", 10f } },
            IsAir = true
        });

        AddType(new BuildingType
        {
            Name = "bunker",
            DisplayName = "Land Fort",
            BaseMaxLevel = 3,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 3, ["forest"] = 3, ["hills"] = 3, ["mountain"] = 2,
                ["urban"] = 3, ["desert"] = 2, ["jungle"] = 2, ["marsh"] = 1
            },
            ConstructionCost = 80f,
            ConstructionTime = 60f,
            Effects = new Dictionary<string, float> { { "defense_bonus", 0.15f }, { "max_entrenchment", 10f } },
            IsMilitary = true
        });

        AddType(new BuildingType
        {
            Name = "coastal_bunker",
            DisplayName = "Coastal Fort",
            BaseMaxLevel = 3,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 3, ["forest"] = 2, ["hills"] = 2, ["mountain"] = 1,
                ["urban"] = 3, ["desert"] = 2, ["jungle"] = 1, ["marsh"] = 1
            },
            ConstructionCost = 100f,
            ConstructionTime = 90f,
            Effects = new Dictionary<string, float> { { "naval_defense_bonus", 0.2f }, { "shore_bombardment_defense", 0.3f } },
            IsMilitary = true,
            RequiresCoastal = true
        });

        AddType(new BuildingType
        {
            Name = "dockyard",
            DisplayName = "Dockyard",
            BaseMaxLevel = 5,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 5, ["forest"] = 4, ["hills"] = 3, ["mountain"] = 1,
                ["urban"] = 5, ["desert"] = 2, ["jungle"] = 2, ["marsh"] = 1
            },
            ConstructionCost = 250f,
            ConstructionTime = 200f,
            Effects = new Dictionary<string, float> { { "naval_factory_output", 5f }, { "ship_repair_speed", 0.1f } },
            IsNaval = true,
            RequiresCoastal = true
        });

        AddType(new BuildingType
        {
            Name = "anti_air_building",
            DisplayName = "Anti-Air Battery",
            BaseMaxLevel = 3,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 3, ["forest"] = 2, ["hills"] = 2, ["mountain"] = 2,
                ["urban"] = 3, ["desert"] = 3, ["jungle"] = 2, ["marsh"] = 2
            },
            ConstructionCost = 60f,
            ConstructionTime = 45f,
            Effects = new Dictionary<string, float> { { "air_attack", 10f }, { "air_defense", 15f } },
            IsMilitary = true
        });

        AddType(new BuildingType
        {
            Name = "radar_station",
            DisplayName = "Radar Station",
            BaseMaxLevel = 3,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 3, ["forest"] = 2, ["hills"] = 3, ["mountain"] = 3,
                ["urban"] = 3, ["desert"] = 3, ["jungle"] = 2, ["marsh"] = 2
            },
            ConstructionCost = 80f,
            ConstructionTime = 60f,
            Effects = new Dictionary<string, float> { { "detection_range", 150f }, { "early_warning", 1f } },
            IsMilitary = true
        });

        AddType(new BuildingType
        {
            Name = "nuclear_reactor",
            DisplayName = "Nuclear Reactor",
            BaseMaxLevel = 1,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 1, ["forest"] = 1, ["hills"] = 1, ["mountain"] = 1,
                ["urban"] = 1, ["desert"] = 1, ["jungle"] = 1, ["marsh"] = 1
            },
            ConstructionCost = 500f,
            ConstructionTime = 365f,
            Effects = new Dictionary<string, float> { { "nuclear_capability", 1f } },
            IsMilitary = true
        });

        AddType(new BuildingType
        {
            Name = "synthetic_refinery",
            DisplayName = "Synthetic Refinery",
            BaseMaxLevel = 3,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 3, ["forest"] = 2, ["hills"] = 2, ["mountain"] = 1,
                ["urban"] = 3, ["desert"] = 2, ["jungle"] = 1, ["marsh"] = 1
            },
            ConstructionCost = 200f,
            ConstructionTime = 150f,
            Effects = new Dictionary<string, float> { { "synthetic_oil", 10f }, { "synthetic_rubber", 5f } }
        });

        AddType(new BuildingType
        {
            Name = "rocket_site",
            DisplayName = "Rocket Site",
            BaseMaxLevel = 3,
            TerrainMaxLevel = new Dictionary<string, int>
            {
                ["plains"] = 3, ["forest"] = 2, ["hills"] = 2, ["mountain"] = 2,
                ["urban"] = 3, ["desert"] = 2, ["jungle"] = 2, ["marsh"] = 1
            },
            ConstructionCost = 150f,
            ConstructionTime = 120f,
            Effects = new Dictionary<string, float> { { "rocket_capacity", 10f }, { "strategic_attack", 1f } },
            IsMilitary = true
        });
    }

    private static void AddType(BuildingType type)
    {
        _buildingTypes[type.Name] = type;
    }

    private static int GetMaxLevelForTerrain(string buildingType, int provinceId)
    {
        if (!_buildingTypes.TryGetValue(buildingType, out var btype))
            return 5;

        // Get terrain for province
        var prov = ProvinceDataCache.GetById(provinceId.ToString());
        if (prov != null)
        {
            string terrain = prov.Terrain.ToString().ToLower();
            if (btype.TerrainMaxLevel.TryGetValue(terrain, out int maxLevel))
                return maxLevel;
        }
        return btype.BaseMaxLevel;
    }

    private static int GetProvinceOwner(int provinceId)
    {
        // Get current owner from ProvinceDataCache
        var prov = ProvinceDataCache.GetById(provinceId.ToString());
        if (prov != null && !string.IsNullOrEmpty(prov.CountryId))
        {
            // Convert country ISO code to ID (simplified)
            return prov.CountryId.GetHashCode();
        }
        return 0;
    }

    public static void ResetForNewGame()
    {
        foreach (var b in _allBuildings)
        {
            b.Health = 100f;
            b.IsActive = true;
        }
    }

    /// <summary>
    /// Get all buildings in a province.
    /// </summary>
    public static IReadOnlyList<Building> GetBuildingsInProvince(int provinceId)
    {
        return _provinceBuildings.GetValueOrDefault(provinceId) ?? new List<Building>();
    }

    /// <summary>
    /// Get buildings of a specific type in a province.
    /// </summary>
    public static IEnumerable<Building> GetBuildingsOfType(int provinceId, string type)
    {
        if (_provinceBuildings.TryGetValue(provinceId, out var list))
        {
            foreach (var b in list)
                if (b.Type == type) yield return b;
        }
    }

    /// <summary>
    /// Get max level for a building type in a province (terrain-limited).
    /// </summary>
    public static int GetMaxLevel(string buildingType, int provinceId)
    {
        return GetMaxLevelForTerrain(buildingType, provinceId);
    }

    /// <summary>
    /// Check if a building can be upgraded.
    /// </summary>
    public static bool CanUpgrade(Building building)
    {
        return building.IsActive &&
               building.Level < building.MaxLevel &&
               building.Health > 20f;
    }

    /// <summary>
    /// Upgrade a building.
    /// </summary>
    public static bool UpgradeBuilding(Building building)
    {
        if (!CanUpgrade(building)) return false;

        building.Level++;
        building.MaxLevel = GetMaxLevelForTerrain(building.Type, building.ProvinceId);

        // Apply level-up effects
        ApplyLevelEffects(building);
        return true;
    }

    private static void ApplyLevelEffects(Building building)
    {
        if (!_buildingTypes.TryGetValue(building.Type, out var btype))
            return;

        // Effects scale with level
        float levelMult = building.Level;
        foreach (var kvp in btype.Effects)
        {
            // Effects would be applied to province/country systems
            // This is a placeholder for integration
        }
    }

    /// <summary>
    /// Damage a building (e.g., from bombing).
    /// </summary>
    public static void DamageBuilding(Building building, float damage)
    {
        building.Health = Math.Max(0f, building.Health - damage);
        if (building.Health <= 0)
        {
            building.IsActive = false;
            building.Level = Math.Max(0, building.Level - 1); // Damage reduces level
        }
    }

    /// <summary>
    /// Repair a building.
    /// </summary>
    public static void RepairBuilding(Building building, float repair)
    {
        building.Health = Math.Min(100f, building.Health + repair);
        if (building.Health > 20f)
            building.IsActive = true;
    }

    /// <summary>
    /// Get all buildings of a type across all provinces.
    /// </summary>
    public static IEnumerable<Building> GetAllBuildingsOfType(string type)
    {
        foreach (var b in _allBuildings)
            if (b.Type == type) yield return b;
    }

    /// <summary>
    /// Get total building count by type for a country.
    /// </summary>
    public static Dictionary<string, int> GetCountryBuildingCounts(int countryId)
    {
        var counts = new Dictionary<string, int>();
        foreach (var b in _allBuildings)
        {
            if (b.OwnerCountryId == countryId)
            {
                counts[b.Type] = counts.GetValueOrDefault(b.Type, 0) + 1;
            }
        }
        return counts;
    }

    // JSON deserialization helper
    private class JsonBuilding
    {
        public int ProvinceId;
        public string BuildingType;
        public float X, Y, Z;
        public float Rotation;
        public int Level;
    }
}