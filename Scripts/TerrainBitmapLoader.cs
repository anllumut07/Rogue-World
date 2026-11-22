using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SysFileAccess = System.IO.FileAccess;
using SysFileMode = System.IO.FileMode;
using SysFileStream = System.IO.FileStream;
using SysBinaryReader = System.IO.BinaryReader;

/// <summary>
/// Loads HoI4 terrain bitmap (terrain.bmp) and maps color indices to terrain categories.
/// 5632×2048 8-bit BMP, color indices map to terrain types via 00_terrain.txt terrain{} block.
/// </summary>
public static class TerrainBitmapLoader
{
    private const string HoI4MapPath = @"C:\Proje\Hearts of Iron IV No Compromise No Surrender\map";
    private const string TerrainBmpPath = HoI4MapPath + @"\terrain.bmp";
    private const string TerrainTxtPath = @"C:\Proje\Hearts of Iron IV No Compromise No Surrender\common\terrain\00_terrain.txt";

    public static int MapWidth { get; private set; } = 5632;
    public static int MapHeight { get; private set; } = 2048;

    // Color index (0-255) → TerrainCategory
    private static Dictionary<byte, TerrainCategory> _indexToTerrain = new();
    private static Dictionary<string, TerrainCategory> _nameToTerrain = new();

    // Terrain palette index per pixel (for sampling)
    private static byte[] _terrainIndices; // One byte per pixel (width * height)

    /// <summary>
    /// Initialize terrain system - parses 00_terrain.txt and loads terrain.bmp
    /// </summary>
    public static async Task InitializeAsync()
    {
        GD.Print("[TerrainBitmapLoader] Starting initialization...");

        // Parse terrain definitions from 00_terrain.txt
        ParseTerrainDefinitions();

        // Load terrain.bmp
        await LoadTerrainBmpAsync();

        GD.Print($"[TerrainBitmapLoader] Initialization complete: {_indexToTerrain.Count} terrain mappings, Map={MapWidth}x{MapHeight}");
    }

    /// <summary>
    /// Parse 00_terrain.txt - both categories{} and terrain{} blocks
    /// </summary>
    private static void ParseTerrainDefinitions()
    {
        GD.Print("[TerrainBitmapLoader] Parsing 00_terrain.txt...");

        if (!File.Exists(TerrainTxtPath))
        {
            GD.PrintErr($"[TerrainBitmapLoader] 00_terrain.txt not found at {TerrainTxtPath}");
            return;
        }

        string content = File.ReadAllText(TerrainTxtPath);

        // Parse categories{} block - gameplay modifiers
        ParseCategoriesBlock(content);

        // Parse terrain{} block - graphical mappings (color index → terrain type)
        ParseTerrainBlock(content);

        GD.Print($"[TerrainBitmapLoader] Parsed {_nameToTerrain.Count} terrain categories, {_indexToTerrain.Count} color index mappings");
    }

    private static void ParseCategoriesBlock(string content)
    {
        // Find categories block
        int catStart = content.IndexOf("categories = {");
        if (catStart < 0) return;

        int braceCount = 0;
        int catEnd = catStart;
        for (int i = catStart; i < content.Length; i++)
        {
            if (content[i] == '{') braceCount++;
            else if (content[i] == '}') braceCount--;
            if (braceCount == 0) { catEnd = i; break; }
        }

        string catContent = content.Substring(catStart, catEnd - catStart + 1);

        // Parse each terrain category
        var terrainNames = new[] { "unknown", "lakes", "forest", "hills", "mountain", "plains",
            "urban", "supercity", "jungle", "marsh", "desert", "ocean",
            "water_fjords", "water_littoral", "water_shallow_sea", "water_deep_ocean" };

        foreach (string name in terrainNames)
        {
            var terrain = ParseTerrainCategory(catContent, name);
            if (terrain != null)
            {
                _nameToTerrain[name] = terrain;
            }
        }
    }

    private static TerrainCategory ParseTerrainCategory(string content, string name)
    {
        // Find terrain block: name = { ... }
        string pattern = $"{name} = {{";
        int start = content.IndexOf(pattern);
        if (start < 0) return null;

        start += pattern.Length;
        int braceCount = 1;
        int end = start;
        for (int i = start; i < content.Length; i++)
        {
            if (content[i] == '{') braceCount++;
            else if (content[i] == '}') { braceCount--; if (braceCount == 0) { end = i; break; } }
        }

        string block = content.Substring(start, end - start);

        var terrain = new TerrainCategory { Name = name };

        // Parse key=value pairs
        foreach (var line in block.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            int eqIdx = trimmed.IndexOf('=');
            if (eqIdx < 0) continue;

            string key = trimmed.Substring(0, eqIdx).Trim();
            string val = trimmed.Substring(eqIdx + 1).Trim().Trim('{', '}');

            // Remove inline comments
            int commentIdx = val.IndexOf('#');
            if (commentIdx >= 0) val = val.Substring(0, commentIdx).Trim();

            ParseTerrainProperty(terrain, key, val, block);
        }

        return terrain;
    }

    private static void ParseTerrainProperty(TerrainCategory terrain, string key, string val, string fullBlock)
    {
        switch (key)
        {
            case "color":
                var rgb = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (rgb.Length >= 3)
                    terrain.Color = new Color(int.Parse(rgb[0]) / 255f, int.Parse(rgb[1]) / 255f, int.Parse(rgb[2]) / 255f);
                break;
            case "movement_cost":
                terrain.MovementCost = float.Parse(val); break;
            case "combat_width":
                terrain.CombatWidth = int.Parse(val); break;
            case "combat_support_width":
                terrain.CombatSupportWidth = int.Parse(val); break;
            case "attack":
                terrain.AttackModifier = float.Parse(val); break;
            case "defence":
                terrain.DefenceModifier = float.Parse(val); break;
            case "movement":
                terrain.MovementModifier = float.Parse(val); break;
            case "supply_flow_penalty_factor":
                terrain.SupplyFlowPenalty = float.Parse(val); break;
            case "truck_attrition_factor":
                terrain.TruckAttritionFactor = float.Parse(val); break;
            case "attrition":
                terrain.Attrition = float.Parse(val); break;
            case "sickness_chance":
                terrain.SicknessChance = float.Parse(val); break;
            case "ai_terrain_importance_factor":
                terrain.AiImportanceFactor = float.Parse(val); break;
            case "match_value":
                terrain.MatchValue = float.Parse(val); break;
            case "sound_type":
                terrain.SoundType = val.Trim('"'); break;
            case "naval_terrain":
                terrain.NavalTerrain = val == "yes"; break;
            case "is_water":
                terrain.IsWater = val == "yes"; break;
            case "minimum_seazone_dominance":
                terrain.MinimumSeazoneDominance = float.Parse(val); break;
            case "buildings_max_level":
                // Parse nested block: building = level
                ParseBuildingsMaxLevel(terrain, val); break;
        }

        // Also check for units{} block
        if (fullBlock.Contains("units = {"))
        {
            int unitsStart = fullBlock.IndexOf("units = {");
            if (unitsStart >= 0)
            {
                int braceCount = 0;
                int unitsEnd = unitsStart;
                for (int i = unitsStart; i < fullBlock.Length; i++)
                {
                    if (fullBlock[i] == '{') braceCount++;
                    else if (fullBlock[i] == '}') { braceCount--; if (braceCount == 0) { unitsEnd = i; break; } }
                }
                string unitsBlock = fullBlock.Substring(unitsStart, unitsEnd - unitsStart + 1);
                ParseUnitsBlock(terrain, unitsBlock);
            }
        }
    }

    private static void ParseBuildingsMaxLevel(TerrainCategory terrain, string val)
    {
        // Format: { bunker = 3 coastal_bunker = 3 }
        var parts = val.Split(new[] { ' ', '\n', '\t', '{', '}' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i += 2)
        {
            if (int.TryParse(parts[i + 1], out int level))
                terrain.BuildingsMaxLevel[parts[i]] = level;
        }
    }

    private static void ParseUnitsBlock(TerrainCategory terrain, string block)
    {
        // Parse units = { attack = -0.2 defence = 0.1 movement = -0.15 }
        foreach (var line in block.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            int eqIdx = trimmed.IndexOf('=');
            if (eqIdx < 0) continue;

            string key = trimmed.Substring(0, eqIdx).Trim();
            string val = trimmed.Substring(eqIdx + 1).Trim().Trim('{', '}');

            int commentIdx = val.IndexOf('#');
            if (commentIdx >= 0) val = val.Substring(0, commentIdx).Trim();

            switch (key)
            {
                case "attack": terrain.AttackModifier = float.Parse(val); break;
                case "defence": terrain.DefenceModifier = float.Parse(val); break;
                case "movement": terrain.MovementModifier = float.Parse(val); break;
            }
        }
    }

    private static void ParseTerrainBlock(string content)
    {
        // Find terrain block (graphical mappings)
        int terrainStart = content.IndexOf("terrain = {");
        if (terrainStart < 0) return;

        int braceCount = 0;
        int terrainEnd = terrainStart;
        for (int i = terrainStart; i < content.Length; i++)
        {
            if (content[i] == '{') braceCount++;
            else if (content[i] == '}') { braceCount--; if (braceCount == 0) { terrainEnd = i; break; } }
        }

        string terrainContent = content.Substring(terrainStart, terrainEnd - terrainStart + 1);

        // Parse each terrain_X = { type = "..." color = { X } texture = Y ... }
        var lines = terrainContent.Split('\n');
        string currentName = null;
        string currentType = null;
        byte? currentColor = null;
        int currentTexture = 0;
        bool currentPermSnow = false;
        bool currentSpawnCity = false;

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            // New terrain entry: terrain_0 = { type = "plains" color = { 0 } texture = 1 }
            if (trimmed.EndsWith("= {") || trimmed.Contains("= {"))
            {
                // Save previous
                if (currentName != null && currentType != null && currentColor.HasValue)
                {
                    RegisterTerrainMapping(currentName, currentType, currentColor.Value, currentTexture, currentPermSnow, currentSpawnCity);
                }

                // Start new
                currentName = trimmed.Split('=')[0].Trim();
                currentType = null;
                currentColor = null;
                currentTexture = 0;
                currentPermSnow = false;
                currentSpawnCity = false;
            }
            else if (trimmed.StartsWith("type ="))
            {
                currentType = trimmed.Split('=')[1].Trim().Trim('"', ' ');
            }
            else if (trimmed.StartsWith("color ="))
            {
                // color = { 0 } or color = { 0 5 19 }
                string colorStr = trimmed.Substring(trimmed.IndexOf('{') + 1).Trim('}', ' ');
                var indices = colorStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (indices.Length > 0 && byte.TryParse(indices[0], out byte idx))
                    currentColor = idx;
            }
            else if (trimmed.StartsWith("texture ="))
            {
                currentTexture = int.Parse(trimmed.Split('=')[1].Trim());
            }
            else if (trimmed.StartsWith("perm_snow ="))
            {
                currentPermSnow = trimmed.Split('=')[1].Trim() == "yes";
            }
            else if (trimmed.StartsWith("spawn_city ="))
            {
                currentSpawnCity = trimmed.Split('=')[1].Trim() == "yes";
            }
        }

        // Don't forget last entry
        if (currentName != null && currentType != null && currentColor.HasValue)
        {
            RegisterTerrainMapping(currentName, currentType, currentColor.Value, currentTexture, currentPermSnow, currentSpawnCity);
        }
    }

    private static void RegisterTerrainMapping(string name, string type, byte colorIndex, int texture, bool permSnow, bool spawnCity)
    {
        if (!_nameToTerrain.TryGetValue(type, out var terrain))
        {
            GD.PrintErr($"[TerrainBitmapLoader] Unknown terrain type: {type} for {name}");
            return;
        }

        // Clone or reference the terrain category
        var mapping = new TerrainCategory
        {
            Name = name,
            Color = terrain.Color,
            PaletteIndex = colorIndex,
            TextureIndex = texture,
            PermSnow = permSnow,
            SpawnCity = spawnCity,
            NavalTerrain = terrain.NavalTerrain,
            IsWater = terrain.IsWater,
            MovementCost = terrain.MovementCost,
            CombatWidth = terrain.CombatWidth,
            CombatSupportWidth = terrain.CombatSupportWidth,
            AttackModifier = terrain.AttackModifier,
            DefenceModifier = terrain.DefenceModifier,
            MovementModifier = terrain.MovementModifier,
            SupplyFlowPenalty = terrain.SupplyFlowPenalty,
            TruckAttritionFactor = terrain.TruckAttritionFactor,
            Attrition = terrain.Attrition,
            SicknessChance = terrain.SicknessChance,
            AiImportanceFactor = terrain.AiImportanceFactor,
            MatchValue = terrain.MatchValue,
            SoundType = terrain.SoundType,
            BuildingsMaxLevel = new Dictionary<string, int>(terrain.BuildingsMaxLevel),
            MinimumSeazoneDominance = terrain.MinimumSeazoneDominance,
            NavalUnitModifiers = new Dictionary<string, NavalUnitModifiers>(terrain.NavalUnitModifiers)
        };

        _indexToTerrain[colorIndex] = mapping;
    }

    /// <summary>
    /// Load terrain.bmp (8-bit palette) - one byte per pixel = terrain color index
    /// </summary>
    private static async Task LoadTerrainBmpAsync()
    {
        GD.Print("[TerrainBitmapLoader] Loading terrain.bmp...");

        if (!File.Exists(TerrainBmpPath))
        {
            GD.PrintErr($"[TerrainBitmapLoader] terrain.bmp not found at {TerrainBmpPath}");
            return;
        }

        using (var fs = new SysFileStream(TerrainBmpPath, SysFileMode.Open, SysFileAccess.Read))
        using (var br = new SysBinaryReader(fs))
        {
            var signature = br.ReadBytes(2);
            if (signature[0] != 'B' || signature[1] != 'M')
            {
                GD.PrintErr("[TerrainBitmapLoader] Not a valid BMP file");
                return;
            }

            br.ReadBytes(4); br.ReadBytes(4); // file size, reserved
            int dataOffset = br.ReadInt32();
            int headerSize = br.ReadInt32();
            MapWidth = br.ReadInt32();
            MapHeight = br.ReadInt32();
            br.ReadInt16(); // planes
            short bitsPerPixel = br.ReadInt16();
            br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4);

            bool topDown = MapHeight < 0;
            MapHeight = Math.Abs(MapHeight);

            GD.Print($"[TerrainBitmapLoader] BMP: {MapWidth}x{MapHeight}, {bitsPerPixel}-bit, topDown={topDown}");

            if (bitsPerPixel != 8)
            {
                GD.PrintErr($"[TerrainBitmapLoader] Expected 8-bit BMP, got {bitsPerPixel}-bit");
                return;
            }

            int rowSize = ((MapWidth + 3) / 4) * 4;
            _terrainIndices = new byte[MapWidth * MapHeight];

            fs.Seek(dataOffset, SeekOrigin.Begin);
            byte[] rowBuffer = new byte[rowSize];

            for (int y = 0; y < MapHeight; y++)
            {
                int bytesRead = fs.Read(rowBuffer, 0, rowSize);
                int targetY = topDown ? y : (MapHeight - 1 - y);
                int targetOffset = targetY * MapWidth;

                for (int x = 0; x < MapWidth; x++)
                {
                    _terrainIndices[targetOffset + x] = rowBuffer[x];
                }

                if (y % 256 == 0)
                    await Task.Yield();
            }
        }

        GD.Print($"[TerrainBitmapLoader] Terrain indices loaded: {_terrainIndices.Length} pixels");

        // Validate: check color index distribution
        ValidateTerrainDistribution();
    }

    private static void ValidateTerrainDistribution()
    {
        var counts = new Dictionary<byte, int>();
        foreach (byte idx in _terrainIndices)
            counts[idx] = counts.GetValueOrDefault(idx) + 1;

        GD.Print("[TerrainBitmapLoader] Top terrain color indices:");
        foreach (var kvp in counts.OrderByDescending(k => k.Value).Take(15))
        {
            string terrainName = _indexToTerrain.TryGetValue(kvp.Key, out var t) ? t.Name : "unknown";
            GD.Print($"  Index {kvp.Key}: {terrainName} ({kvp.Value} pixels, {kvp.Value * 100.0 / _terrainIndices.Length:F1}%)");
        }
    }

    /// <summary>
    /// Get terrain category at world position
    /// </summary>
    public static TerrainCategory GetTerrainAt(Vector2 worldPos)
    {
        int x = Mathf.Clamp((int)worldPos.X, 0, MapWidth - 1);
        int y = Mathf.Clamp((int)worldPos.Y, 0, MapHeight - 1);
        byte idx = _terrainIndices[y * MapWidth + x];
        return _indexToTerrain.GetValueOrDefault(idx);
    }

    /// <summary>
    /// Get terrain category by color index
    /// </summary>
    public static TerrainCategory GetTerrainByIndex(byte index)
    {
        return _indexToTerrain.GetValueOrDefault(index);
    }

    /// <summary>
    /// Get terrain category by name
    /// </summary>
    public static TerrainCategory GetTerrainByName(string name)
    {
        return _nameToTerrain.GetValueOrDefault(name);
    }

    /// <summary>
    /// Get all terrain mappings
    /// </summary>
    public static IReadOnlyDictionary<byte, TerrainCategory> GetAllMappings()
    {
        return _indexToTerrain;
    }

    /// <summary>
    /// Sample terrain at multiple points for a province (for dominant terrain)
    /// </summary>
    public static TerrainCategory GetDominantTerrainForProvince(int provinceId, IEnumerable<Vector2> provincePixels)
    {
        var terrainCounts = new Dictionary<string, int>();

        foreach (var pixel in provincePixels)
        {
            var terrain = GetTerrainAt(pixel);
            if (terrain != null)
                terrainCounts[terrain.Name] = terrainCounts.GetValueOrDefault(terrain.Name) + 1;
        }

        if (terrainCounts.Count == 0) return GetTerrainByName("plains");

        var dominant = terrainCounts.OrderByDescending(k => k.Value).First();
        return GetTerrainByName(dominant.Key);
    }
}