using Godot;
using System;

/// <summary>
/// MapProjectionV5 — Single source of truth for all coordinate conversions in WorldMapV5.
/// Equirectangular projection: lon [-180,180] → x [0, imgW], lat [90,-90] → y [0, imgH]
///
/// CRITICAL: Texture dimensions are the ONLY coordinate source. Projection derives from texture,
/// NOT texture fitted to projection. LatTop/LatBottom/LonLeft/LonRight are MEASURED from
/// texture alignment (IoU with NE land polygons), not assumed.
/// </summary>
public static class MapProjectionV5
{
    // Background texture pixel dimensions (set at runtime from loaded texture)
    public static int ImgW { get; private set; } = 10800;
    public static int ImgH { get; private set; } = 5400;

    // Latitude/longitude bounds of the texture coverage
    // These are MEASURED via IoU alignment (tools/v5_align.py), not assumed
    // DEFAULT: full world -180..180, 90..-90
    // After measurement, update these constants to match actual texture coverage
    public static double LatTop { get; private set; } = 90.0;
    public static double LatBottom { get; private set; } = -90.0;
    public static double LonLeft { get; private set; } = -180.0;
    public static double LonRight { get; private set; } = 180.0;

    // Precomputed ranges
    private static double _lonRange = 360.0;
    private static double _latRange = 180.0;

    /// <summary>
    /// Initialize projection with actual texture dimensions and measured bounds.
    /// Call once at startup from WorldMapV5._Ready after texture loads.
    /// </summary>
    public static void Initialize(int imgW, int imgH, double latTop = 90.0, double latBottom = -90.0, double lonLeft = -180.0, double lonRight = 180.0)
    {
        ImgW = imgW;
        ImgH = imgH;
        LatTop = latTop;
        LatBottom = latBottom;
        LonLeft = lonLeft;
        LonRight = lonRight;
        _lonRange = LonRight - LonLeft;
        _latRange = LatTop - LatBottom;

        GD.Print($"[MapProjectionV5] Initialized: {ImgW}x{ImgH}, Lat={LatTop:F4}..{LatBottom:F4}, Lon={LonLeft:F4}..{LonRight:F4}");
    }

    /// <summary>
    /// Convert longitude/latitude to WORLD SPACE coordinates (pixels in texture space).
    /// This is the ONLY function that should do lon/lat → world conversion.
    /// Used by: country borders, cities, sea labels, terrain, ocean.
    /// </summary>
    public static Vector2 LonLatToWorld(double lon, double lat)
    {
        // Clamp to valid ranges
        lon = Math.Clamp(lon, LonLeft, LonRight);
        lat = Math.Clamp(lat, LatBottom, LatTop);

        float x = (float)((lon - LonLeft) / _lonRange) * ImgW;
        float y = (float)((LatTop - lat) / _latRange) * ImgH;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Convert WORLD SPACE coordinates back to longitude/latitude.
    /// </summary>
    public static (double lon, double lat) WorldToLonLat(Vector2 worldPos)
    {
        double lon = LonLeft + (worldPos.X / ImgW) * _lonRange;
        double lat = LatTop - (worldPos.Y / ImgH) * _latRange;
        return (lon, lat);
    }

    /// <summary>
    /// Convert WORLD SPACE to SCREEN SPACE using current camera.
    /// </summary>
    public static Vector2 WorldToScreen(Vector2 worldPos, Camera2D camera, Vector2 viewportSize)
    {
        if (camera == null) return worldPos;
        return (worldPos - camera.Position) * camera.Zoom.X + viewportSize / 2f;
    }

    /// <summary>
    /// Convert SCREEN SPACE to WORLD SPACE using current camera.
    /// </summary>
    public static Vector2 ScreenToWorld(Vector2 screenPos, Camera2D camera, Vector2 viewportSize)
    {
        if (camera == null) return screenPos;
        return camera.Position + (screenPos - viewportSize / 2f) / camera.Zoom.X;
    }

    /// <summary>
    /// Get the UV coordinates for terrain/ocean textures.
    /// Texture covers full world (LonLeft..LonRight, LatTop..LatBottom).
    /// </summary>
    public static Vector2 LonLatToUV(double lon, double lat)
    {
        float u = (float)((lon - LonLeft) / _lonRange);  // 0..1
        float v = (float)((LatTop - lat) / _latRange);  // 0..1
        return new Vector2(u, v);
    }

    /// <summary>
    /// Get the world space bounding box for the entire texture.
    /// </summary>
    public static Rect2 WorldRect => new Rect2(0, 0, ImgW, ImgH);

    /// <summary>
    /// Check if a world position is within the texture bounds.
    /// </summary>
    public static bool IsInBounds(Vector2 worldPos)
    {
        return worldPos.X >= 0 && worldPos.X <= ImgW && worldPos.Y >= 0 && worldPos.Y <= ImgH;
    }
}