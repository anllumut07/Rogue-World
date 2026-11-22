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
using SysSeekOrigin = System.IO.SeekOrigin;

/// <summary>
/// Loads HoI4 province bitmap (provinces.bmp) and builds RGB→ProvinceID lookup table.
/// Handles 5632×2048 24-bit BMP with 16,290 provinces.
/// </summary>
public static class ProvinceBitmapLoader
{
    // BMP file path in the HoI4 mod
    private const string HoI4MapPath = @"C:\Proje\Hearts of Iron IV No Compromise No Surrender\map";
    private const string ProvincesBmpPath = HoI4MapPath + @"\provinces.bmp";
    private const string DefinitionCsvPath = HoI4MapPath + @"\definition.csv";

    // Cache file for processed data (avoid re-parsing BMP every startup)
    private const string CachePath = "user://hoi4_province_cache.json";

    public static int MapWidth { get; private set; } = 5632;
    public static int MapHeight { get; private set; } = 2048;
    public static int ProvinceCount { get; private set; }

    // RGB → ProvinceDefinition lookup (primary runtime structure)
    private static Dictionary<uint, ProvinceDefinition> _rgbToProvince = new();
    private static Dictionary<int, ProvinceDefinition> _idToProvince = new();

    // Province centroids for rendering/click detection
    private static Dictionary<int, Vector2> _provinceCentroids = new();

    // BMP pixel data (for terrain sampling if needed)
    private static byte[] _provinceBmpPixels; // R,G,B per pixel (width * height * 3)

    /// <summary>
    /// Initialize the province system - loads definition.csv and provinces.bmp
    /// </summary>
    public static async Task InitializeAsync()
    {
        GD.Print("[ProvinceBitmapLoader] Starting initialization...");

        // Try load from cache first
        if (TryLoadFromCache())
        {
            GD.Print($"[ProvinceBitmapLoader] Loaded from cache: {ProvinceCount} provinces");
            await ComputeCentroidsAsync();
            return;
        }

        // Load definition.csv first (builds RGB→ID mapping)
        LoadDefinitionCsv();

        // Load provinces.bmp and build province data
        await LoadProvincesBmpAsync();

        // Compute centroids for each province
        await ComputeCentroidsAsync();

        // Save to cache for next startup
        SaveToCache();

        GD.Print($"[ProvinceBitmapLoader] Initialization complete: {ProvinceCount} provinces, Map={MapWidth}x{MapHeight}");
    }

    /// <summary>
    /// Load definition.csv - maps RGB colors to province IDs and metadata
    /// Format: ID;R;G;B;Type;IsWater;TerrainCategory;Unknown
    /// </summary>
    private static void LoadDefinitionCsv()
    {
        GD.Print("[ProvinceBitmapLoader] Loading definition.csv...");

        if (!File.Exists(DefinitionCsvPath))
        {
            GD.PrintErr($"[ProvinceBitmapLoader] definition.csv not found at {DefinitionCsvPath}");
            return;
        }

        int loaded = 0;
        using (var reader = new StreamReader(DefinitionCsvPath))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split(';');
                if (parts.Length < 7)
                    continue;

                try
                {
                    var prov = new ProvinceDefinition
                    {
                        Id = int.Parse(parts[0]),
                        R = byte.Parse(parts[1]),
                        G = byte.Parse(parts[2]),
                        B = byte.Parse(parts[3]),
                        Type = ParseProvinceType(parts[4]),
                        IsWater = parts[5].ToLower() == "true",
                        TerrainCategory = parts[6],
                        Unknown = parts.Length > 7 ? int.Parse(parts[7]) : 0
                    };

                    prov.RgbColor = new Color(prov.R / 255f, prov.G / 255f, prov.B / 255f);

                    _rgbToProvince[prov.RgbKey] = prov;
                    _idToProvince[prov.Id] = prov;
                    loaded++;
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[ProvinceBitmapLoader] Error parsing line: {line} - {e.Message}");
                }
            }
        }

        ProvinceCount = loaded;
        GD.Print($"[ProvinceBitmapLoader] Loaded {loaded} province definitions from CSV");
    }

    private static ProvinceType ParseProvinceType(string typeStr)
    {
        return typeStr.ToLower() switch
        {
            "land" => ProvinceType.Land,
            "sea" => ProvinceType.Sea,
            "lake" => ProvinceType.Lake,
            _ => ProvinceType.Land
        };
    }

    /// <summary>
    /// Load provinces.bmp (5632×2048, 24-bit) and associate pixels with province definitions
    /// </summary>
    private static async Task LoadProvincesBmpAsync()
    {
        GD.Print("[ProvinceBitmapLoader] Loading provinces.bmp...");

        if (!File.Exists(ProvincesBmpPath))
        {
            GD.PrintErr($"[ProvinceBitmapLoader] provinces.bmp not found at {ProvincesBmpPath}");
            return;
        }

        // Read BMP header
        using (var fs = new SysFileStream(ProvincesBmpPath, SysFileMode.Open, SysFileAccess.Read))
        using (var br = new SysBinaryReader(fs))
        {
            // BMP Header
            var signature = br.ReadBytes(2);
            if (signature[0] != 'B' || signature[1] != 'M')
            {
                GD.PrintErr("[ProvinceBitmapLoader] Not a valid BMP file");
                return;
            }

            br.ReadBytes(4); // file size
            br.ReadBytes(4); // reserved
            int dataOffset = br.ReadInt32();

            // DIB Header
            int headerSize = br.ReadInt32();
            MapWidth = br.ReadInt32();
            MapHeight = br.ReadInt32();
            br.ReadInt16(); // planes
            short bitsPerPixel = br.ReadInt16();
            br.ReadBytes(4); // compression
            br.ReadBytes(4); // image size
            br.ReadBytes(4); // x ppm
            br.ReadBytes(4); // y ppm
            br.ReadBytes(4); // colors used
            br.ReadBytes(4); // colors important

            // Handle negative height (top-down BMP)
            bool topDown = MapHeight < 0;
            MapHeight = Math.Abs(MapHeight);

            GD.Print($"[ProvinceBitmapLoader] BMP: {MapWidth}x{MapHeight}, {bitsPerPixel}-bit, topDown={topDown}");

            if (bitsPerPixel != 24)
            {
                GD.PrintErr($"[ProvinceBitmapLoader] Expected 24-bit BMP, got {bitsPerPixel}-bit");
                return;
            }

            // Calculate row size (padded to 4 bytes)
            int rowSize = ((MapWidth * 3 + 3) / 4) * 4;
            _provinceBmpPixels = new byte[MapWidth * MapHeight * 3];

            // Seek to pixel data
            fs.Seek(dataOffset, SeekOrigin.Begin);

            // Read pixel rows
            byte[] rowBuffer = new byte[rowSize];
            for (int y = 0; y < MapHeight; y++)
            {
                int bytesRead = fs.Read(rowBuffer, 0, rowSize);
                if (bytesRead < rowSize)
                {
                    GD.PrintErr($"[ProvinceBitmapLoader] Unexpected end of file at row {y}");
                    break;
                }

                int targetY = topDown ? y : (MapHeight - 1 - y);
                int targetOffset = targetY * MapWidth * 3;

                for (int x = 0; x < MapWidth; x++)
                {
                    // BMP is BGR order
                    int srcOffset = x * 3;
                    _provinceBmpPixels[targetOffset + x * 3 + 0] = rowBuffer[srcOffset + 2]; // R
                    _provinceBmpPixels[targetOffset + x * 3 + 1] = rowBuffer[srcOffset + 1]; // G
                    _provinceBmpPixels[targetOffset + x * 3 + 2] = rowBuffer[srcOffset + 0]; // B
                }

                // Yield every 256 rows to avoid blocking
                if (y % 256 == 0)
                    await Task.Yield();
            }
        }

        GD.Print($"[ProvinceBitmapLoader] BMP pixel data loaded: {_provinceBmpPixels.Length / 3} pixels");

        // Validate: sample some pixels and check they map to known provinces
        ValidateBmpMapping();
    }

    private static void ValidateBmpMapping()
    {
        int matched = 0, total = 0;
        // Sample every 100th pixel
        for (int i = 0; i < _provinceBmpPixels.Length; i += 300)
        {
            if (i + 2 >= _provinceBmpPixels.Length) break;
            uint key = ((uint)_provinceBmpPixels[i] << 16) | ((uint)_provinceBmpPixels[i + 1] << 8) | (uint)_provinceBmpPixels[i + 2];
            if (_rgbToProvince.ContainsKey(key))
                matched++;
            total++;
        }
        GD.Print($"[ProvinceBitmapLoader] BMP validation: {matched}/{total} sampled pixels map to known provinces");
    }

    /// <summary>
    /// Compute centroid (center point) for each province by averaging its pixels
    /// </summary>
    private static async Task ComputeCentroidsAsync()
    {
        GD.Print("[ProvinceBitmapLoader] Computing province centroids...");

        // Accumulator for each province
        var sumX = new Dictionary<int, long>();
        var sumY = new Dictionary<int, long>();
        var count = new Dictionary<int, int>();

        // Scan all pixels and accumulate positions per province
        for (int y = 0; y < MapHeight; y++)
        {
            int rowOffset = y * MapWidth * 3;
            for (int x = 0; x < MapWidth; x++)
            {
                int pixelOffset = rowOffset + x * 3;
                uint key = ((uint)_provinceBmpPixels[pixelOffset] << 16) |
                          ((uint)_provinceBmpPixels[pixelOffset + 1] << 8) |
                          (uint)_provinceBmpPixels[pixelOffset + 2];

                if (_rgbToProvince.TryGetValue(key, out var prov))
                {
                    int id = prov.Id;
                    sumX[id] = sumX.GetValueOrDefault(id) + x;
                    sumY[id] = sumY.GetValueOrDefault(id) + y;
                    count[id] = count.GetValueOrDefault(id) + 1;
                }
            }

            // Yield every 128 rows
            if (y % 128 == 0)
                await Task.Yield();
        }

        // Calculate centroids
        int computed = 0;
        foreach (var kvp in count)
        {
            int id = kvp.Key;
            int c = kvp.Value;
            if (c > 0 && _idToProvince.TryGetValue(id, out var prov))
            {
                float cx = (float)sumX[id] / c;
                float cy = (float)sumY[id] / c;
                prov.Centroid = new Vector2(cx, cy);
                _provinceCentroids[id] = prov.Centroid;
                computed++;
            }
        }

        GD.Print($"[ProvinceBitmapLoader] Computed centroids for {computed} provinces");
    }

    /// <summary>
    /// Get province definition by RGB color (from mouse click on province map)
    /// </summary>
    public static ProvinceDefinition GetProvinceByRgb(byte r, byte g, byte b)
    {
        uint key = ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        return _rgbToProvince.GetValueOrDefault(key);
    }

    /// <summary>
    /// Get province definition by ID
    /// </summary>
    public static ProvinceDefinition GetProvinceById(int id)
    {
        return _idToProvince.GetValueOrDefault(id);
    }

    /// <summary>
    /// Get province at world position (pixels)
    /// </summary>
    public static ProvinceDefinition GetProvinceAtWorldPos(Vector2 worldPos)
    {
        int x = Mathf.Clamp((int)worldPos.X, 0, MapWidth - 1);
        int y = Mathf.Clamp((int)worldPos.Y, 0, MapHeight - 1);

        int offset = (y * MapWidth + x) * 3;
        if (offset + 2 >= _provinceBmpPixels.Length)
            return null;

        uint key = ((uint)_provinceBmpPixels[offset] << 16) |
                  ((uint)_provinceBmpPixels[offset + 1] << 8) |
                  (uint)_provinceBmpPixels[offset + 2];

        return _rgbToProvince.GetValueOrDefault(key);
    }

    /// <summary>
    /// Get province centroid position
    /// </summary>
    public static Vector2 GetProvinceCentroid(int provinceId)
    {
        return _provinceCentroids.GetValueOrDefault(provinceId, Vector2.Zero);
    }

    /// <summary>
    /// Convert world position (pixels) to longitude/latitude
    /// Map is equirectangular: 5632px = 360°, 2048px = 180°
    /// </summary>
    public static Vector2 WorldToLonLat(Vector2 worldPos)
    {
        double lon = -180.0 + (worldPos.X / MapWidth) * 360.0;
        double lat = 90.0 - (worldPos.Y / MapHeight) * 180.0;
        return new Vector2((float)lon, (float)lat);
    }

    /// <summary>
    /// Convert longitude/latitude to world position (pixels)
    /// </summary>
    public static Vector2 LonLatToWorld(double lon, double lat)
    {
        float x = (float)((lon + 180.0) / 360.0 * MapWidth);
        float y = (float)((90.0 - lat) / 180.0 * MapHeight);
        return new Vector2(Mathf.Clamp(x, 0, MapWidth - 1), Mathf.Clamp(y, 0, MapHeight - 1));
    }

    /// <summary>
    /// Get all loaded provinces
    /// </summary>
    public static IReadOnlyDictionary<int, ProvinceDefinition> GetAllProvinces()
    {
        return _idToProvince;
    }

    /// <summary>
    /// Get all land provinces
    /// </summary>
    public static IEnumerable<ProvinceDefinition> GetLandProvinces()
    {
        return _idToProvince.Values.Where(p => p.Type == ProvinceType.Land);
    }

    /// <summary>
    /// Get all sea provinces
    /// </summary>
    public static IEnumerable<ProvinceDefinition> GetSeaProvinces()
    {
        return _idToProvince.Values.Where(p => p.Type == ProvinceType.Sea);
    }

    // ===== Cache System =====

    private static bool TryLoadFromCache()
    {
        try
        {
            if (!DirAccess.Open("user://").FileExists("hoi4_province_cache.json"))
                return false;

            var json = Godot.FileAccess.GetFileAsString("user://hoi4_province_cache.json");
            if (string.IsNullOrEmpty(json))
                return false;

            // Parse cache (simplified - in production use JSON deserialization)
            // For now, just return false to force reload
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveToCache()
    {
        try
        {
            // Save minimal cache: province IDs, centroids, RGB keys
            // Full implementation would serialize _idToProvince and _provinceCentroids
            GD.Print("[ProvinceBitmapLoader] Cache save not fully implemented yet");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProvinceBitmapLoader] Cache save failed: {e.Message}");
        }
    }

    /// <summary>
    /// Create a Texture2D from the province bitmap for GPU-based lookup
    /// </summary>
    public static Texture2D CreateProvinceLookupTexture()
    {
        var image = Image.CreateEmpty(MapWidth, MapHeight, false, Image.Format.Rgb8);
        if (image == null || _provinceBmpPixels == null)
            return null;

        // Copy pixel data
        byte[] data = new byte[MapWidth * MapHeight * 3];
        Array.Copy(_provinceBmpPixels, data, data.Length);
        image.SetData(MapWidth, MapHeight, false, Image.Format.Rgb8, data);

        var texture = ImageTexture.CreateFromImage(image);
        GD.Print($"[ProvinceBitmapLoader] Created province lookup texture: {MapWidth}x{MapHeight}");
        return texture;
    }
}