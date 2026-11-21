using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// M32.4: HoI4-style terrain system.
/// Defines terrain categories with movement, combat, supply, and attrition modifiers.
/// Data-driven via JSON (res://Data/terrain_definitions.json).
/// </summary>
public static class TerrainSystem
{
    // ============================================================
    // TERRAIN DEFINITIONS (mirrors HoI4 00_terrain.txt categories)
    // ============================================================

    public enum TerrainCategory
    {
        Unknown,
        Plains,
        Forest,
        Hills,
        Mountain,
        Urban,
        Supercity,
        Jungle,
        Marsh,
        Desert,
        Lakes,      // Inland water
        Ocean,      // Deep ocean
        WaterFjords,
        WaterLittoral,
        WaterShallowSea,
        WaterDeepOcean
    }

    // Land terrain categories (used for province terrain assignment)
    public static readonly HashSet<TerrainCategory> LandTerrains = new()
    {
        TerrainCategory.Plains, TerrainCategory.Forest, TerrainCategory.Hills,
        TerrainCategory.Mountain, TerrainCategory.Urban, TerrainCategory.Supercity,
        TerrainCategory.Jungle, TerrainCategory.Marsh, TerrainCategory.Desert,
        TerrainCategory.Lakes
    };

    // Naval terrain categories
    public static readonly HashSet<TerrainCategory> NavalTerrains = new()
    {
        TerrainCategory.Ocean, TerrainCategory.WaterFjords,
        TerrainCategory.WaterLittoral, TerrainCategory.WaterShallowSea,
        TerrainCategory.WaterDeepOcean
    };

    [Flags]
    public enum TerrainFlags : uint
    {
        None = 0,
        IsWater = 1 << 0,
        IsImpassable = 1 << 1,
        HasAttrition = 1 << 2,
        HasSickness = 1 << 3,
        NavalTerrain = 1 << 4,
        SpawnCity = 1 << 5,
        PermSnow = 1 << 6
    }

    public sealed class TerrainDefinition
    {
        public TerrainCategory Category;
        public string Name;                    // Localization key
        public Color MapColor;                 // Visual color on terrain mode
        public int PaletteIndex;               // terrain.bmp palette index (0-31)
        public int TextureIndex;               // Texture atlas index
        public TerrainFlags Flags = TerrainFlags.None;

        // Movement & Combat
        public float MovementCost = 1.0f;      // Hours per province (base 1.0)
        public int CombatWidth = 144;          // Max frontage
        public int CombatSupportWidth = 72;    // Support frontage
        public float AttackModifier = 0.0f;    // Attack penalty/bonus
        public float DefenceModifier = 0.0f;   // Defence bonus
        public float MovementModifier = 0.0f;  // Movement speed modifier

        // Supply & Logistics
        public float SupplyFlowPenalty = 0.0f;     // Supply throughput reduction
        public float TruckAttritionFactor = 0.0f;  // Truck attrition multiplier

        // Attrition & Health
        public float Attrition = 0.0f;          // Daily non-combat attrition %
        public float SicknessChance = 0.0f;     // Daily sickness chance %

        // AI & Strategic
        public float AIImportanceFactor = 1.0f; // AI strategic weight
        public float MatchValue = 0.0f;         // Defensive value for AI

        // Audio
        public string SoundType = "plains";     // Footstep/vehicle sound set

        // Buildings
        public Dictionary<string, int> MaxBuildingLevel = new(); // building_type -> max_level

        // Naval specific
        public float MinSeazoneDominance = 0.0f; // Minimum naval dominance to operate
        public Dictionary<string, ShipTerrainModifier> ShipModifiers = new(); // ship_class -> modifier

        // Visual
        public bool PermSnow = false;           // Permanent snow (polar)
        public bool SpawnCity = false;          // City spawn point (urban)

        // Helper properties
        public bool IsWater
        {
            get => Flags.HasFlag(TerrainFlags.IsWater);
            set
            {
                if (value) Flags |= TerrainFlags.IsWater;
                else Flags &= ~TerrainFlags.IsWater;
            }
        }

        public bool IsNaval
        {
            get => Flags.HasFlag(TerrainFlags.NavalTerrain);
            set
            {
                if (value) Flags |= TerrainFlags.NavalTerrain;
                else Flags &= ~TerrainFlags.NavalTerrain;
            }
        }

        public bool HasAttrition => Flags.HasFlag(TerrainFlags.HasAttrition);
        public bool HasSickness => Flags.HasFlag(TerrainFlags.HasSickness);
    }

    public sealed class ShipTerrainModifier
    {
        public float AttackModifier = 0.0f;
        public float MovementModifier = 0.0f;
        public float VisibilityModifier = 0.0f;
        public float PositioningModifier = 0.0f;
    }

    // ============================================================
    // RUNTIME STATE
    // ============================================================

    private static Dictionary<TerrainCategory, TerrainDefinition> _definitions = new();
    private static Dictionary<int, TerrainCategory> _paletteToCategory = new(); // palette index -> category
    private static bool _initialized = false;

    // ============================================================
    // INITIALIZATION
    // ============================================================

    public static void Initialize()
    {
        if (_initialized) return;
        LoadDefinitions();
        _initialized = true;
        GD.Print($"[TerrainSystem] Initialized with {_definitions.Count} terrain categories");
    }

    public static void ResetForNewGame()
    {
        // Terrain definitions are static data, no per-game state to reset
    }

    private static void LoadDefinitions()
    {
        const string path = "res://Data/terrain_definitions.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[TerrainSystem] {path} not found! Creating defaults.");
            CreateDefaultDefinitions();
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
            var list = JsonSerializer.Deserialize<List<TerrainDefinition>>(json, options);

            if (list != null)
            {
                foreach (var def in list)
                {
                    _definitions[def.Category] = def;
                    if (def.PaletteIndex >= 0)
                        _paletteToCategory[def.PaletteIndex] = def.Category;
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[TerrainSystem] Failed to load terrain definitions: {e.Message}");
            CreateDefaultDefinitions();
        }
    }

    private static void CreateDefaultDefinitions()
    {
        // ========================================================
        // LAND TERRAINS (matching HoI4 00_terrain.txt values)
        // ========================================================

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Unknown,
            Name = "terrain_unknown",
            MapColor = new Color(1.0f, 0.0f, 0.0f, 1.0f), // Bright red for debugging
            PaletteIndex = 0,
            MovementCost = 1.0f,
            CombatWidth = 144,
            CombatSupportWidth = 72,
            Flags = TerrainFlags.None
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Plains,
            Name = "terrain_plains",
            MapColor = new Color(1.0f, 0.51f, 0.26f, 1.0f), // #FF8242
            PaletteIndex = 0,
            TextureIndex = 0,
            MovementCost = 1.0f,
            CombatWidth = 144,
            CombatSupportWidth = 72,
            AIImportanceFactor = 0.1f,
            MatchValue = 0.5f,
            SoundType = "plains",
            Flags = TerrainFlags.None,
            MaxBuildingLevel = new() { { "bunker", 2 }, { "coastal_bunker", 2 } }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Forest,
            Name = "terrain_forest",
            MapColor = new Color(0.35f, 0.78f, 0.33f, 1.0f), // #59C755
            PaletteIndex = 1,
            TextureIndex = 4,
            MovementCost = 1.5f,
            CombatWidth = 134,
            CombatSupportWidth = 67,
            AttackModifier = -0.2f,
            DefenceModifier = 0.1f,
            MovementModifier = -0.15f,
            SupplyFlowPenalty = 0.08f,
            TruckAttritionFactor = 0.2f,
            AIImportanceFactor = 5.0f,
            MatchValue = 5.0f,
            SoundType = "forest",
            Flags = TerrainFlags.None,
            MaxBuildingLevel = new() { { "bunker", 3 }, { "coastal_bunker", 3 } }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Hills,
            Name = "terrain_hills",
            MapColor = new Color(0.97f, 1.0f, 0.6f, 1.0f), // #F8FF99
            PaletteIndex = 2,
            TextureIndex = 2,
            MovementCost = 1.5f,
            CombatWidth = 127,
            CombatSupportWidth = 63,
            AttackModifier = -0.1f,
            DefenceModifier = 0.1f,
            MovementModifier = -0.1f,
            SupplyFlowPenalty = 0.10f,
            TruckAttritionFactor = 0.2f,
            AIImportanceFactor = 3.0f,
            MatchValue = 2.0f,
            SoundType = "desert",
            Flags = TerrainFlags.None,
            MaxBuildingLevel = new() { { "bunker", 3 }, { "coastal_bunker", 3 } }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Mountain,
            Name = "terrain_mountain",
            MapColor = new Color(0.62f, 0.75f, 0.82f, 1.0f), // #9DC0D0
            PaletteIndex = 6,
            TextureIndex = 11,
            MovementCost = 2.0f,
            CombatWidth = 120,
            CombatSupportWidth = 60,
            AttackModifier = -0.2f,
            DefenceModifier = 0.2f,
            MovementModifier = -0.3f,
            SupplyFlowPenalty = 0.12f,
            TruckAttritionFactor = 1.0f,
            Attrition = 0.3f,
            AIImportanceFactor = 10.0f,
            MatchValue = 10.0f,
            SoundType = "desert",
            Flags = TerrainFlags.HasAttrition,
            MaxBuildingLevel = new() { { "bunker", 1 }, { "coastal_bunker", 1 } }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Urban,
            Name = "terrain_urban",
            MapColor = new Color(0.47f, 0.47f, 0.47f, 1.0f), // #787878
            PaletteIndex = 13,
            TextureIndex = 10,
            MovementCost = 1.2f,
            CombatWidth = 200,
            CombatSupportWidth = 66,
            AttackModifier = -0.2f,
            DefenceModifier = 0.3f,
            MovementModifier = -0.5f,
            SupplyFlowPenalty = -0.20f, // Supply bonus!
            AIImportanceFactor = 4.0f,
            MatchValue = 4.0f,
            SoundType = "plains",
            Flags = TerrainFlags.SpawnCity,
            MaxBuildingLevel = new() { { "bunker", 3 }, { "coastal_bunker", 3 } }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Supercity,
            Name = "terrain_supercity",
            MapColor = new Color(1.0f, 0.42f, 0.0f, 1.0f), // #FF6A00
            PaletteIndex = 13, // Same as urban visually, but different stats
            TextureIndex = 10,
            MovementCost = 1.5f,
            CombatWidth = 240,
            CombatSupportWidth = 80,
            AttackModifier = -0.35f,
            DefenceModifier = 0.5f,
            MovementModifier = -1.0f,
            SupplyFlowPenalty = -0.10f,
            AIImportanceFactor = 6.0f,
            MatchValue = 3.0f,
            SoundType = "plains",
            Flags = TerrainFlags.SpawnCity,
            MaxBuildingLevel = new() { { "bunker", 3 }, { "coastal_bunker", 3 } }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Jungle,
            Name = "terrain_jungle",
            MapColor = new Color(0.5f, 0.75f, 0.0f, 1.0f), // #80BF00
            PaletteIndex = 21,
            TextureIndex = 4,
            MovementCost = 1.5f,
            CombatWidth = 134,
            CombatSupportWidth = 67,
            AttackModifier = -0.3f,
            MovementModifier = -0.3f,
            SupplyFlowPenalty = 0.16f,
            TruckAttritionFactor = 3.0f,
            Attrition = 0.2f,
            SicknessChance = 1.0f,
            AIImportanceFactor = 6.0f,
            MatchValue = 6.0f,
            SoundType = "forest",
            Flags = TerrainFlags.HasAttrition | TerrainFlags.HasSickness,
            MaxBuildingLevel = new() { { "bunker", 2 }, { "coastal_bunker", 2 } }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Marsh,
            Name = "terrain_marsh",
            MapColor = new Color(0.3f, 0.38f, 0.14f, 1.0f), // #4C6023
            PaletteIndex = 9,
            TextureIndex = 6,
            MovementCost = 2.0f,
            CombatWidth = 124,
            CombatSupportWidth = 62,
            AttackModifier = -0.3f,
            MovementModifier = -0.3f,
            SupplyFlowPenalty = 0.16f,
            TruckAttritionFactor = 4.0f,
            Attrition = 0.35f,
            SicknessChance = 1.0f,
            AIImportanceFactor = 8.0f,
            MatchValue = 8.0f,
            SoundType = "forest",
            Flags = TerrainFlags.HasAttrition | TerrainFlags.HasSickness,
            MaxBuildingLevel = new() { { "bunker", 1 }, { "coastal_bunker", 1 } }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Desert,
            Name = "terrain_desert",
            MapColor = new Color(1.0f, 0.5f, 0.0f, 1.0f), // #FF8000
            PaletteIndex = 3,
            TextureIndex = 9,
            MovementCost = 1.05f,
            CombatWidth = 144,
            CombatSupportWidth = 72,
            SupplyFlowPenalty = 0.14f,
            TruckAttritionFactor = 0.5f,
            Attrition = 0.15f,
            SicknessChance = 1.0f,
            AIImportanceFactor = 8.0f,
            MatchValue = 8.0f,
            SoundType = "desert",
            Flags = TerrainFlags.HasAttrition | TerrainFlags.HasSickness,
            MaxBuildingLevel = new() { { "bunker", 1 }, { "coastal_bunker", 1 } }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Lakes,
            Name = "terrain_lakes",
            MapColor = new Color(0.23f, 0.36f, 1.0f, 1.0f), // #3A5BFF
            PaletteIndex = 14,
            MovementCost = 1.0f,
            Flags = TerrainFlags.IsWater,
            SoundType = "sea",
            MaxBuildingLevel = new() { { "bunker", 3 }, { "coastal_bunker", 3 } }
        });

        // ========================================================
        // NAVAL TERRAINS
        // ========================================================

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.Ocean,
            Name = "terrain_ocean",
            MapColor = new Color(0.23f, 0.36f, 1.0f, 1.0f),
            PaletteIndex = 15,
            TextureIndex = 9,
            MovementCost = 1.0f,
            Flags = TerrainFlags.IsWater | TerrainFlags.NavalTerrain,
            SoundType = "sea",
            MinSeazoneDominance = 386.36f,
            ShipModifiers = new()
            {
                { "corvette", new ShipTerrainModifier { AttackModifier = -0.3f, MovementModifier = -0.3f, VisibilityModifier = 0.3f } },
                { "stealth_corvette", new ShipTerrainModifier { AttackModifier = -0.3f, MovementModifier = -0.3f, VisibilityModifier = 0.3f } },
                { "patrol_boat", new ShipTerrainModifier { AttackModifier = -0.5f, MovementModifier = -0.5f, VisibilityModifier = 0.5f } }
            }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.WaterFjords,
            Name = "terrain_fjords",
            MapColor = new Color(0.29f, 0.64f, 0.78f, 1.0f),
            PaletteIndex = 15,
            TextureIndex = 9,
            MovementCost = 1.0f,
            Flags = TerrainFlags.IsWater | TerrainFlags.NavalTerrain,
            SoundType = "sea",
            MinSeazoneDominance = 128.79f,
            ShipModifiers = new()
            {
                { "destroyer", new ShipTerrainModifier { AttackModifier = -0.2f, MovementModifier = -0.2f, VisibilityModifier = 0.2f } },
                { "stealth_destroyer", new ShipTerrainModifier { AttackModifier = -0.2f, MovementModifier = -0.2f, VisibilityModifier = 0.2f } },
                { "cruiser", new ShipTerrainModifier { AttackModifier = -0.3f, MovementModifier = -0.3f, VisibilityModifier = 0.2f } },
                { "helicopter_operator", new ShipTerrainModifier { AttackModifier = -0.3f, MovementModifier = -0.3f, VisibilityModifier = 0.3f } },
                { "battle_cruiser", new ShipTerrainModifier { AttackModifier = -0.3f, MovementModifier = -0.3f, VisibilityModifier = 0.3f } },
                { "battleship", new ShipTerrainModifier { AttackModifier = -0.4f, MovementModifier = -0.4f, VisibilityModifier = 0.4f } },
                { "carrier", new ShipTerrainModifier { AttackModifier = -0.4f, MovementModifier = -0.4f, VisibilityModifier = 0.4f } }
            }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.WaterLittoral,
            Name = "terrain_littoral",
            MapColor = new Color(0.48f, 0.64f, 0.73f, 1.0f),
            PaletteIndex = 15,
            TextureIndex = 9,
            MovementCost = 1.0f,
            Flags = TerrainFlags.IsWater | TerrainFlags.NavalTerrain,
            SoundType = "sea",
            MinSeazoneDominance = 198.6f,
            ShipModifiers = new()
            {
                { "destroyer", new ShipTerrainModifier { AttackModifier = -0.1f, MovementModifier = -0.1f, VisibilityModifier = 0.1f } },
                { "stealth_destroyer", new ShipTerrainModifier { AttackModifier = -0.1f, MovementModifier = -0.1f, VisibilityModifier = 0.1f } },
                { "cruiser", new ShipTerrainModifier { AttackModifier = -0.15f, MovementModifier = -0.15f, VisibilityModifier = 0.15f } },
                { "helicopter_operator", new ShipTerrainModifier { AttackModifier = -0.15f, MovementModifier = -0.15f, VisibilityModifier = 0.15f } },
                { "battle_cruiser", new ShipTerrainModifier { AttackModifier = -0.15f, MovementModifier = -0.15f, VisibilityModifier = 0.15f } },
                { "battleship", new ShipTerrainModifier { AttackModifier = -0.2f, MovementModifier = -0.2f, VisibilityModifier = 0.2f } },
                { "carrier", new ShipTerrainModifier { AttackModifier = -0.2f, MovementModifier = -0.2f, VisibilityModifier = 0.2f } }
            }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.WaterShallowSea,
            Name = "terrain_shallow_sea",
            MapColor = new Color(0.59f, 0.8f, 0.91f, 1.0f),
            PaletteIndex = 15,
            TextureIndex = 9,
            MovementCost = 1.0f,
            Flags = TerrainFlags.IsWater | TerrainFlags.NavalTerrain,
            SoundType = "sea",
            MinSeazoneDominance = 257.58f,
            ShipModifiers = new()
            {
                { "destroyer", new ShipTerrainModifier { AttackModifier = -0.05f, MovementModifier = -0.05f, VisibilityModifier = 0.05f } },
                { "stealth_destroyer", new ShipTerrainModifier { AttackModifier = -0.05f, MovementModifier = -0.05f, VisibilityModifier = 0.05f } },
                { "cruiser", new ShipTerrainModifier { AttackModifier = -0.075f, MovementModifier = -0.075f, VisibilityModifier = 0.075f } },
                { "helicopter_operator", new ShipTerrainModifier { AttackModifier = -0.075f, MovementModifier = -0.075f, VisibilityModifier = 0.075f } },
                { "battle_cruiser", new ShipTerrainModifier { AttackModifier = -0.075f, MovementModifier = -0.075f, VisibilityModifier = 0.075f } },
                { "battleship", new ShipTerrainModifier { AttackModifier = -0.1f, MovementModifier = -0.1f, VisibilityModifier = 0.1f } },
                { "carrier", new ShipTerrainModifier { AttackModifier = -0.1f, MovementModifier = -0.1f, VisibilityModifier = 0.1f } },
                { "attack_submarine", new ShipTerrainModifier { MovementModifier = -0.25f, VisibilityModifier = 0.5f } },
                { "missile_submarine", new ShipTerrainModifier { MovementModifier = -0.25f, VisibilityModifier = 0.5f } }
            }
        });

        AddDef(new TerrainDefinition
        {
            Category = TerrainCategory.WaterDeepOcean,
            Name = "terrain_deep_ocean",
            MapColor = new Color(0.01f, 0.15f, 0.59f, 1.0f),
            PaletteIndex = 15,
            TextureIndex = 9,
            MovementCost = 1.0f,
            Flags = TerrainFlags.IsWater | TerrainFlags.NavalTerrain,
            SoundType = "sea",
            MinSeazoneDominance = 515.15f,
            ShipModifiers = new()
            {
                { "corvette", new ShipTerrainModifier { AttackModifier = -0.5f, MovementModifier = -0.5f, VisibilityModifier = 0.5f } },
                { "stealth_corvette", new ShipTerrainModifier { AttackModifier = -0.5f, MovementModifier = -0.5f, VisibilityModifier = 0.5f } },
                { "frigate", new ShipTerrainModifier { AttackModifier = -0.075f, MovementModifier = -0.15f, VisibilityModifier = 0.15f } },
                { "heavy_frigate", new ShipTerrainModifier { AttackModifier = -0.075f, MovementModifier = -0.15f, VisibilityModifier = 0.15f } },
                { "stealth_frigate", new ShipTerrainModifier { AttackModifier = -0.075f, MovementModifier = -0.15f, VisibilityModifier = 0.15f } },
                { "patrol_boat", new ShipTerrainModifier { AttackModifier = -0.75f, MovementModifier = -0.75f, VisibilityModifier = 0.75f } },
                { "missile_submarine", new ShipTerrainModifier { VisibilityModifier = -0.25f } },
                { "attack_submarine", new ShipTerrainModifier { VisibilityModifier = -0.25f } }
            }
        });
    }

    private static void AddDef(TerrainDefinition def)
    {
        _definitions[def.Category] = def;
        if (def.PaletteIndex >= 0)
            _paletteToCategory[def.PaletteIndex] = def.Category;
    }

    // ============================================================
    // PUBLIC API
    // ============================================================

    public static TerrainDefinition Get(TerrainCategory category)
    {
        return _definitions.TryGetValue(category, out var def) ? def : _definitions[TerrainCategory.Unknown];
    }

    public static TerrainDefinition GetByPaletteIndex(int paletteIndex)
    {
        if (_paletteToCategory.TryGetValue(paletteIndex, out var cat))
            return Get(cat);
        return Get(TerrainCategory.Unknown);
    }

    public static TerrainCategory ParseCategory(string name)
    {
        if (Enum.TryParse<TerrainCategory>(name, true, out var cat))
            return cat;
        return TerrainCategory.Unknown;
    }

    public static bool IsLand(TerrainCategory cat) => LandTerrains.Contains(cat);
    public static bool IsNaval(TerrainCategory cat) => NavalTerrains.Contains(cat);
    public static bool IsWater(TerrainCategory cat) => Get(cat).IsWater;

    // ============================================================
    // GAMEPLAY FORMULAS (matching HoI4 runtime behavior)
    // ============================================================

    /// <summary>
    /// Calculate movement time in hours for a unit crossing this terrain.
    /// Base formula: hours = distance * movement_cost / unit_speed
    /// </summary>
    public static float CalculateMoveHours(float distance, float unitSpeed, TerrainCategory terrain)
    {
        var def = Get(terrain);
        return distance * def.MovementCost / Math.Max(unitSpeed, 0.1f);
    }

    /// <summary>
    /// Effective combat width for this terrain.
    /// </summary>
    public static int GetEffectiveCombatWidth(int unitWidth, TerrainCategory terrain)
    {
        var def = Get(terrain);
        return Math.Min(unitWidth, def.CombatWidth);
    }

    /// <summary>
    /// Effective support width for this terrain.
    /// </summary>
    public static int GetEffectiveSupportWidth(int unitSupportWidth, TerrainCategory terrain)
    {
        var def = Get(terrain);
        return Math.Min(unitSupportWidth, def.CombatSupportWidth);
    }

    /// <summary>
    /// Final attack value after terrain modifiers.
    /// </summary>
    public static float ApplyAttackModifier(float baseAttack, TerrainCategory terrain, float weatherAttackMod = 0f)
    {
        var def = Get(terrain);
        return baseAttack * (1.0f + def.AttackModifier + weatherAttackMod);
    }

    /// <summary>
    /// Final defence value after terrain modifiers.
    /// </summary>
    public static float ApplyDefenceModifier(float baseDefence, TerrainCategory terrain, float weatherDefenceMod = 0f)
    {
        var def = Get(terrain);
        return baseDefence * (1.0f + def.DefenceModifier + weatherDefenceMod);
    }

    /// <summary>
    /// Supply throughput multiplier for this terrain.
    /// </summary>
    public static float GetSupplyThroughputMultiplier(TerrainCategory terrain)
    {
        var def = Get(terrain);
        return 1.0f - def.SupplyFlowPenalty;
    }

    /// <summary>
    /// Truck attrition multiplier for this terrain.
    /// </summary>
    public static float GetTruckAttritionMultiplier(TerrainCategory terrain)
    {
        var def = Get(terrain);
        return def.TruckAttritionFactor;
    }

    /// <summary>
    /// Daily attrition percentage for units in this terrain (non-combat).
    /// </summary>
    public static float GetDailyAttrition(TerrainCategory terrain, float weatherAttritionMod = 0f)
    {
        var def = Get(terrain);
        return def.Attrition * (1.0f + weatherAttritionMod);
    }

    /// <summary>
    /// Daily sickness chance for units in this terrain.
    /// </summary>
    public static float GetSicknessChance(TerrainCategory terrain)
    {
        var def = Get(terrain);
        return def.SicknessChance;
    }

    /// <summary>
    /// Maximum building level allowed on this terrain.
    /// </summary>
    public static int GetMaxBuildingLevel(string buildingType, TerrainCategory terrain)
    {
        var def = Get(terrain);
        return def.MaxBuildingLevel.TryGetValue(buildingType, out var level) ? level : 5; // Default 5
    }

    /// <summary>
    /// Naval ship modifier for this terrain.
    /// </summary>
    public static ShipTerrainModifier GetShipModifier(TerrainCategory terrain, string shipClass)
    {
        var def = Get(terrain);
        return def.ShipModifiers.TryGetValue(shipClass, out var mod) ? mod : new ShipTerrainModifier();
    }

    /// <summary>
    /// Minimum seazone dominance required for naval operations.
    /// </summary>
    public static float GetMinSeazoneDominance(TerrainCategory terrain)
    {
        var def = Get(terrain);
        return def.MinSeazoneDominance;
    }

    /// <summary>
    /// AI strategic importance of this terrain (higher = more important to defend/attack).
    /// </summary>
    public static float GetAIImportance(TerrainCategory terrain)
    {
        var def = Get(terrain);
        return def.AIImportanceFactor;
    }

    /// <summary>
    /// Defensive match value for AI evaluation.
    /// </summary>
    public static float GetMatchValue(TerrainCategory terrain)
    {
        var def = Get(terrain);
        return def.MatchValue;
    }

    /// <summary>
    /// Get terrain sound type for audio system.
    /// </summary>
    public static string GetSoundType(TerrainCategory terrain)
    {
        var def = Get(terrain);
        return def.SoundType;
    }
}