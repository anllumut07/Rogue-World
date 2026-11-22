using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages buildings on the map (factories, bases, bunkers, etc.)
/// </summary>
public class BuildingManager
{
    private Dictionary<int, List<BuildingRuntime>> _buildingsByProvince = new();
    private Dictionary<string, List<BuildingRuntime>> _buildingsByType = new();
    private Dictionary<string, BuildingTypeDefinition> _buildingDefinitions = new();

    public BuildingManager()
    {
        InitializeBuildingDefinitions();
    }

    private void InitializeBuildingDefinitions()
    {
        // Define all building types with their properties
        _buildingDefinitions["arms_factory"] = new BuildingTypeDefinition
        {
            Name = "Arms Factory",
            Category = BuildingCategory.Military,
            ConstructionCost = 70, // days with 15 factories
            ConstructionCostIC = 0.5f, // IC days per level
            MaxLevel = 5,
            Provides = new Dictionary<string, float> { { "military_factory", 1 } }
        };

        _buildingDefinitions["industrial_complex"] = new BuildingTypeDefinition
        {
            Name = "Industrial Complex",
            Category = BuildingCategory.Civilian,
            ConstructionCost = 70,
            ConstructionCostIC = 0.5f,
            MaxLevel = 5,
            Provides = new Dictionary<string, float> { { "civilian_factory", 1 } }
        };

        _buildingDefinitions["dockyard"] = new BuildingTypeDefinition
        {
            Name = "Dockyard",
            Category = BuildingCategory.Naval,
            ConstructionCost = 105,
            ConstructionCostIC = 0.5f,
            MaxLevel = 5,
            RequiresNavalBase = true,
            Provides = new Dictionary<string, float> { { "naval_factory", 1 } }
        };

        _buildingDefinitions["air_base"] = new BuildingTypeDefinition
        {
            Name = "Air Base",
            Category = BuildingCategory.Military,
            ConstructionCost = 35,
            ConstructionCostIC = 0.5f,
            MaxLevel = 10,
            Provides = new Dictionary<string, float> { { "air_base_capacity", 200 } }
        };

        _buildingDefinitions["anti_air_building"] = new BuildingTypeDefinition
        {
            Name = "Anti-Air Battery",
            Category = BuildingCategory.Military,
            ConstructionCost = 35,
            ConstructionCostIC = 0.5f,
            MaxLevel = 5,
            Provides = new Dictionary<string, float> { { "air_attack", 1.0f } }
        };

        _buildingDefinitions["radar_station"] = new BuildingTypeDefinition
        {
            Name = "Radar Station",
            Category = BuildingCategory.Military,
            ConstructionCost = 35,
            ConstructionCostIC = 0.5f,
            MaxLevel = 5,
            Provides = new Dictionary<string, float> { { "detection_range", 150 } }
        };

        _buildingDefinitions["bunker"] = new BuildingTypeDefinition
        {
            Name = "Land Fort",
            Category = BuildingCategory.Military,
            ConstructionCost = 35,
            ConstructionCostIC = 0.5f,
            MaxLevel = 10,
            Provides = new Dictionary<string, float> { { "defense", 0.1f }, { "attack_penalty", 0.05f } }
        };

        _buildingDefinitions["coastal_bunker"] = new BuildingTypeDefinition
        {
            Name = "Coastal Fort",
            Category = BuildingCategory.Military,
            ConstructionCost = 35,
            ConstructionCostIC = 0.5f,
            MaxLevel = 10,
            Provides = new Dictionary<string, float> { { "naval_defense", 0.1f } }
        };

        _buildingDefinitions["naval_base_spawn"] = new BuildingTypeDefinition
        {
            Name = "Naval Base",
            Category = BuildingCategory.Naval,
            ConstructionCost = 70,
            ConstructionCostIC = 0.5f,
            MaxLevel = 10,
            Provides = new Dictionary<string, float> { { "naval_capacity", 10 } }
        };

        _buildingDefinitions["supply_node"] = new BuildingTypeDefinition
        {
            Name = "Supply Node",
            Category = BuildingCategory.Infrastructure,
            ConstructionCost = 0,
            ConstructionCostIC = 0,
            MaxLevel = 3,
            IsSupplyNode = true
        };

        _buildingDefinitions["floating_harbor"] = new BuildingTypeDefinition
        {
            Name = "Floating Harbor",
            Category = BuildingCategory.Naval,
            ConstructionCost = 70,
            ConstructionCostIC = 0.5f,
            MaxLevel = 5,
            Provides = new Dictionary<string, float> { { "supply_throughput", 2.0f } }
        };

        _buildingDefinitions["synthetic_refinery"] = new BuildingTypeDefinition
        {
            Name = "Synthetic Refinery",
            Category = BuildingCategory.Industrial,
            ConstructionCost = 70,
            ConstructionCostIC = 0.5f,
            MaxLevel = 5,
            Provides = new Dictionary<string, float> { { "synthetic_oil", 1000 } }
        };

        _buildingDefinitions["fuel_silo"] = new BuildingTypeDefinition
        {
            Name = "Fuel Silo",
            Category = BuildingCategory.Infrastructure,
            ConstructionCost = 35,
            ConstructionCostIC = 0.5f,
            MaxLevel = 3,
            Provides = new Dictionary<string, float> { { "fuel_storage", 50000 } }
        };

        _buildingDefinitions["nuclear_reactor"] = new BuildingTypeDefinition
        {
            Name = "Nuclear Reactor",
            Category = BuildingCategory.Industrial,
            ConstructionCost = 210,
            ConstructionCostIC = 0.5f,
            MaxLevel = 3,
            Provides = new Dictionary<string, float> { { "nuclear_research", 1 } }
        };

        _buildingDefinitions["stronghold_network"] = new BuildingTypeDefinition
        {
            Name = "Stronghold Network",
            Category = BuildingCategory.Military,
            ConstructionCost = 140,
            ConstructionCostIC = 0.5f,
            MaxLevel = 1,
            Provides = new Dictionary<string, float> { { "division_defense", 0.2f } }
        };

        _buildingDefinitions["special_project_facility_spawn"] = new BuildingTypeDefinition
        {
            Name = "Special Project Facility",
            Category = BuildingCategory.Special,
            ConstructionCost = 0,
            ConstructionCostIC = 0,
            MaxLevel = 1,
            IsSpecial = true
        };
    }

    public void RegisterBuilding(BuildingRuntime building)
    {
        if (!_buildingsByProvince.ContainsKey(building.ProvinceId))
            _buildingsByProvince[building.ProvinceId] = new List<BuildingRuntime>();

        _buildingsByProvince[building.ProvinceId].Add(building);

        if (!_buildingsByType.ContainsKey(building.Type))
            _buildingsByType[building.Type] = new List<BuildingRuntime>();

        _buildingsByType[building.Type].Add(building);
    }

    public void UnregisterBuilding(BuildingRuntime building)
    {
        if (_buildingsByProvince.TryGetValue(building.ProvinceId, out var list))
            list.Remove(building);

        if (_buildingsByType.TryGetValue(building.Type, out var typeList))
            typeList.Remove(building);
    }

    public List<BuildingRuntime> GetBuildingsInProvince(int provinceId)
    {
        return _buildingsByProvince.GetValueOrDefault(provinceId, new List<BuildingRuntime>());
    }

    public List<BuildingRuntime> GetBuildingsByType(string type)
    {
        return _buildingsByType.GetValueOrDefault(type, new List<BuildingRuntime>());
    }

    public BuildingTypeDefinition GetBuildingDefinition(string type)
    {
        return _buildingDefinitions.GetValueOrDefault(type);
    }

    public int GetBuildingCount(int provinceId, string type)
    {
        if (!_buildingsByProvince.TryGetValue(provinceId, out var list))
            return 0;
        return list.Count(b => b.Type == type);
    }

    public int GetTotalBuildingLevel(int provinceId, string type)
    {
        if (!_buildingsByProvince.TryGetValue(provinceId, out var list))
            return 0;
        return list.Where(b => b.Type == type).Sum(b => b.Level);
    }

    public float GetBuildingBonus(int provinceId, string bonusType)
    {
        float total = 0f;
        if (!_buildingsByProvince.TryGetValue(provinceId, out var list))
            return 0f;

        foreach (var building in list)
        {
            if (_buildingDefinitions.TryGetValue(building.Type, out var def))
            {
                if (def.Provides.TryGetValue(bonusType, out var value))
                    total += value * building.Level;
            }
        }
        return total;
    }

    public bool CanBuildInProvince(int provinceId, string buildingType, TerrainCategory terrain)
    {
        if (!_buildingDefinitions.TryGetValue(buildingType, out var def))
            return false;

        // Check terrain building limits
        if (terrain != null && terrain.BuildingsMaxLevel.TryGetValue(buildingType, out int maxLevel))
        {
            int currentLevel = GetTotalBuildingLevel(provinceId, buildingType);
            if (currentLevel >= maxLevel)
                return false;
        }

        // Special requirements
        if (def.RequiresNavalBase)
        {
            bool hasNavalBase = GetBuildingCount(provinceId, "naval_base_spawn") > 0;
            if (!hasNavalBase) return false;
        }

        return true;
    }

    public IReadOnlyDictionary<string, BuildingTypeDefinition> GetAllDefinitions()
    {
        return _buildingDefinitions;
    }
}

public class BuildingTypeDefinition
{
    public string Name { get; set; }
    public BuildingCategory Category { get; set; }
    public int ConstructionCost { get; set; } // Days with 15 factories
    public float ConstructionCostIC { get; set; } // IC days per level
    public int MaxLevel { get; set; }
    public bool RequiresNavalBase { get; set; } = false;
    public bool IsSupplyNode { get; set; } = false;
    public bool IsSpecial { get; set; } = false;
    public Dictionary<string, float> Provides { get; set; } = new();
}

public enum BuildingCategory
{
    Civilian,
    Military,
    Naval,
    Infrastructure,
    Industrial,
    Special
}