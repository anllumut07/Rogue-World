using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Province definition from definition.csv - maps RGB color to province data
/// </summary>
public partial class ProvinceDefinition
{
    public int Id { get; set; }
    public Color RgbColor { get; set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public ProvinceType Type { get; set; }
    public bool IsWater { get; set; }
    public string TerrainCategory { get; set; }
    public int Unknown { get; set; }

    // Runtime data (not from CSV)
    public TerrainCategory Terrain { get; set; }
    public byte TerrainPaletteIndex { get; set; }
    public Vector2 Centroid { get; set; } // World position (pixels)
    public Vector2 LonLat { get; set; }  // Longitude, Latitude
    public bool IsCoastal { get; set; }
    public List<int> NeighborProvinces { get; set; } = new();
    public int StrategicRegionId { get; set; } = -1;
    public int StateId { get; set; } = -1; // HoI4 state system (optional)
    public int VictoryPoints { get; set; } = 0;

    public uint RgbKey => ((uint)R << 16) | ((uint)G << 8) | (uint)B;

    public override string ToString()
    {
        return $"Province {Id}: RGB=({R},{G},{B}) Type={Type} Terrain={TerrainCategory}";
    }
}

public enum ProvinceType
{
    Land,
    Sea,
    Lake
}

/// <summary>
/// Terrain categories with gameplay modifiers (from 00_terrain.txt)
/// </summary>
public partial class TerrainCategory
{
    public string Name { get; set; }
    public Color Color { get; set; }
    public byte PaletteIndex { get; set; }
    public int TextureIndex { get; set; }
    public bool PermSnow { get; set; }
    public bool SpawnCity { get; set; }
    public bool NavalTerrain { get; set; }
    public bool IsWater { get; set; }

    // Movement & Combat
    public float MovementCost { get; set; } = 1.0f;
    public int CombatWidth { get; set; } = 144;
    public int CombatSupportWidth { get; set; } = 72;
    public float AttackModifier { get; set; } = 0.0f;
    public float DefenceModifier { get; set; } = 0.0f;
    public float MovementModifier { get; set; } = 0.0f;

    // Supply & Logistics
    public float SupplyFlowPenalty { get; set; } = 0.0f;
    public float TruckAttritionFactor { get; set; } = 0.0f;

    // Attrition & Environment
    public float Attrition { get; set; } = 0.0f;
    public float SicknessChance { get; set; } = 0.0f;

    // AI
    public float AiImportanceFactor { get; set; } = 1.0f;
    public float MatchValue { get; set; } = 0.0f;
    public string SoundType { get; set; } = "plains";

    // Buildings
    public Dictionary<string, int> BuildingsMaxLevel { get; set; } = new();

    // Naval
    public float MinimumSeazoneDominance { get; set; } = 0.0f;
    public Dictionary<string, NavalUnitModifiers> NavalUnitModifiers { get; set; } = new();

    public override string ToString()
    {
        return $"{Name} (idx={PaletteIndex}) move={MovementCost} combatW={CombatWidth} atk={AttackModifier} def={DefenceModifier}";
    }
}

public partial class NavalUnitModifiers
{
    public float Attack { get; set; } = 0.0f;
    public float Movement { get; set; } = 0.0f;
    public float NavyVisibility { get; set; } = 0.0f;
    public float Positioning { get; set; } = 0.0f;
    public float NavalMineHitChance { get; set; } = 0.0f;
}