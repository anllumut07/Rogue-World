using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SysFileAccess = System.IO.FileAccess;
using SysFileMode = System.IO.FileMode;
using SysFileStream = System.IO.FileStream;
using SysBinaryReader = System.IO.BinaryReader;

/// <summary>
/// Loads HoI4 heightmap (heightmap.bmp) for elevation data.
/// 5632×2048 8-bit BMP, values 0-255 represent elevation.
/// </summary>
public static class HeightmapLoader
{
    private const string HoI4MapPath = @"C:\Proje\Hearts of Iron IV No Compromise No Surrender\map";
    private const string HeightmapBmpPath = HoI4MapPath + @"\heightmap.bmp";

    public static int MapWidth { get; private set; } = 5632;
    public static int MapHeight { get; private set; } = 2048;

    // Elevation data: 0-255 per pixel (normalized to 0.0-1.0)
    private static byte[] _heightData;
    private static float[] _normalizedHeightData;

    /// <summary>
    /// Initialize heightmap system
    /// </summary>
    public static async Task InitializeAsync()
    {
        GD.Print("[HeightmapLoader] Loading heightmap.bmp...");

        if (!File.Exists(HeightmapBmpPath))
        {
            GD.PrintErr($"[HeightmapLoader] heightmap.bmp not found at {HeightmapBmpPath}");
            return;
        }

        await LoadHeightmapAsync();

        GD.Print($"[HeightmapLoader] Loaded: {MapWidth}x{MapHeight}, {_heightData.Length} samples");
    }

    private static async Task LoadHeightmapAsync()
    {
        using (var fs = new SysFileStream(HeightmapBmpPath, SysFileMode.Open, SysFileAccess.Read))
        using (var br = new SysBinaryReader(fs))
        {
            var signature = br.ReadBytes(2);
            if (signature[0] != 'B' || signature[1] != 'M')
            {
                GD.PrintErr("[HeightmapLoader] Not a valid BMP file");
                return;
            }

            br.ReadBytes(4); br.ReadBytes(4);
            int dataOffset = br.ReadInt32();
            int headerSize = br.ReadInt32();
            MapWidth = br.ReadInt32();
            MapHeight = br.ReadInt32();
            br.ReadInt16();
            short bitsPerPixel = br.ReadInt16();
            br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4);

            bool topDown = MapHeight < 0;
            MapHeight = Math.Abs(MapHeight);

            if (bitsPerPixel != 8)
            {
                GD.PrintErr($"[HeightmapLoader] Expected 8-bit BMP, got {bitsPerPixel}-bit");
                return;
            }

            int rowSize = ((MapWidth + 3) / 4) * 4;
            _heightData = new byte[MapWidth * MapHeight];
            _normalizedHeightData = new float[MapWidth * MapHeight];

            fs.Seek(dataOffset, SeekOrigin.Begin);
            byte[] rowBuffer = new byte[rowSize];

            for (int y = 0; y < MapHeight; y++)
            {
                int bytesRead = fs.Read(rowBuffer, 0, rowSize);
                int targetY = topDown ? y : (MapHeight - 1 - y);
                int targetOffset = targetY * MapWidth;

                for (int x = 0; x < MapWidth; x++)
                {
                    byte h = rowBuffer[x];
                    _heightData[targetOffset + x] = h;
                    _normalizedHeightData[targetOffset + x] = h / 255.0f;
                }

                if (y % 256 == 0)
                    await Task.Yield();
            }
        }
    }

    /// <summary>
    /// Get raw height value (0-255) at world position
    /// </summary>
    public static byte GetHeight(Vector2 worldPos)
    {
        int x = Mathf.Clamp((int)worldPos.X, 0, MapWidth - 1);
        int y = Mathf.Clamp((int)worldPos.Y, 0, MapHeight - 1);
        return _heightData[y * MapWidth + x];
    }

    /// <summary>
    /// Get normalized height (0.0-1.0) at world position
    /// </summary>
    public static float GetNormalizedHeight(Vector2 worldPos)
    {
        int x = Mathf.Clamp((int)worldPos.X, 0, MapWidth - 1);
        int y = Mathf.Clamp((int)worldPos.Y, 0, MapHeight - 1);
        return _normalizedHeightData[y * MapWidth + x];
    }

    /// <summary>
    /// Get height in meters (approximate: 0-255 maps to 0-8000m)
    /// </summary>
    public static float GetHeightMeters(Vector2 worldPos)
    {
        return GetNormalizedHeight(worldPos) * 8000.0f;
    }

    /// <summary>
    /// Get slope/steepness at position (for movement penalties)
    /// </summary>
    public static float GetSlope(Vector2 worldPos)
    {
        int x = Mathf.Clamp((int)worldPos.X, 1, MapWidth - 2);
        int y = Mathf.Clamp((int)worldPos.Y, 1, MapHeight - 2);

        int idx = y * MapWidth + x;
        float h = _normalizedHeightData[idx];
        float hLeft = _normalizedHeightData[idx - 1];
        float hRight = _normalizedHeightData[idx + 1];
        float hUp = _normalizedHeightData[idx - MapWidth];
        float hDown = _normalizedHeightData[idx + MapWidth];

        float dx = (hRight - hLeft) * 0.5f;
        float dy = (hDown - hUp) * 0.5f;

        return Mathf.Sqrt(dx * dx + dy * dy); // Approximate slope magnitude
    }

    /// <summary>
    /// Create normal map texture for shader-based lighting
    /// </summary>
    public static Texture2D CreateNormalMapTexture()
    {
        if (_heightData == null) return null;

        var image = Image.CreateEmpty(MapWidth, MapHeight, false, Image.Format.Rgba8);
        var normalData = new byte[MapWidth * MapHeight * 4];

        for (int y = 1; y < MapHeight - 1; y++)
        {
            for (int x = 1; x < MapWidth - 1; x++)
            {
                int idx = y * MapWidth + x;
                float h = _normalizedHeightData[idx];
                float hLeft = _normalizedHeightData[idx - 1];
                float hRight = _normalizedHeightData[idx + 1];
                float hUp = _normalizedHeightData[idx - MapWidth];
                float hDown = _normalizedHeightData[idx + MapWidth];

                // Calculate normal (Sobel-like)
                float nx = (hLeft - hRight) * 128.0f + 128.0f;
                float ny = (hUp - hDown) * 128.0f + 128.0f;
                float nz = 255.0f;

                // Normalize
                float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                nx = (nx / len) * 127.5f + 127.5f;
                ny = (ny / len) * 127.5f + 127.5f;
                nz = (nz / len) * 127.5f + 127.5f;

                int outIdx = idx * 4;
                normalData[outIdx] = (byte)Mathf.Clamp(nx, 0, 255);
                normalData[outIdx + 1] = (byte)Mathf.Clamp(ny, 0, 255);
                normalData[outIdx + 2] = (byte)Mathf.Clamp(nz, 0, 255);
                normalData[outIdx + 3] = 255;
            }
        }

        image.SetData(MapWidth, MapHeight, false, Image.Format.Rgba8, normalData);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Get average height for a province (for strategic display)
    /// </summary>
    public static float GetAverageHeightForProvince(IEnumerable<Vector2> provincePixels)
    {
        float sum = 0;
        int count = 0;
        foreach (var pixel in provincePixels)
        {
            sum += GetNormalizedHeight(pixel);
            count++;
        }
        return count > 0 ? sum / count : 0;
    }
}