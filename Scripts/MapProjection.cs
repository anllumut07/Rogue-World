using Godot;
using System;

/// <summary>
/// Single source of truth for all coordinate conversions.
/// Equirectangular projection: lon [-180,180] → x [0, WorldWidth], lat [90,-90] → y [0, WorldHeight]
/// Matches Natural Earth texture (2:1 aspect, full world -180..180, 90..-90)
/// NOTE: Uses top margin to match Natural Earth texture polar padding (consistent with existing country_borders.json and cities_runtime.json)
/// </summary>
public static class MapProjection
{
    // Natural Earth texture is 2:1 (e.g. 4096x2048 or 8192x4096), covers full world
    // Our MapSize in world space (pixels) - this is the coordinate space for all map data
    // NOTE: Height is 1280 (not 1080) to match existing country_borders.json and cities_runtime.json
    public static readonly Vector2 WorldSize = new Vector2(2160f, 1280f); // 2:1 aspect, 2160x1280

    // Latitude/longitude ranges
    private const float LatMax = 90f;
    private const float LatMin = -90f;
    private const float LonMax = 180f;
    private const float LonMin = -180f;

    // Top margin in world space pixels (matches country_borders.json and cities_runtime.json projection)
    // This accounts for polar padding in Natural Earth textures
    private const float TopMargin = 80f;
    private const float UsableHeight = 1280f - 80f * 2; // 1120px

    /// <summary>
    /// Convert longitude/latitude to WORLD SPACE coordinates (pixels in 2160x1080 world space).
    /// This is the ONLY function that should do lon/lat → world conversion.
    /// Used by: country borders, cities, sea labels, terrain, ocean.
    /// </summary>
    public static Vector2 LonLatToWorld(double lon, double lat)
    {
        // Clamp to valid ranges
        lon = Math.Clamp(lon, LonMin, LonMax);
        lat = Math.Clamp(lat, LatMin, LatMax);

        float x = (float)((lon - LonMin) / (LonMax - LonMin)) * WorldSize.X;  // 0..2160
        float y = TopMargin + (float)((LatMax - lat) / (LatMax - LatMin)) * UsableHeight;  // 80..1200 (lat 90 at y=80)
        return new Vector2(x, y);
    }

    /// <summary>
    /// Convert WORLD SPACE coordinates back to longitude/latitude.
    /// </summary>
    public static (double lon, double lat) WorldToLonLat(Vector2 worldPos)
    {
        double lon = LonMin + (worldPos.X / WorldSize.X) * (LonMax - LonMin);
        double lat = LatMax - ((worldPos.Y - TopMargin) / UsableHeight) * (LatMax - LatMin);
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
    /// Natural Earth textures cover full world (2:1, 360°x180°).
    /// </summary>
    public static Vector2 LonLatToUV(double lon, double lat)
    {
        float u = (float)((lon - LonMin) / (LonMax - LonMin));  // 0..1
        float v = (float)((LatMax - lat) / (LatMax - LatMin));  // 0..1
        return new Vector2(u, v);
    }

    /// <summary>
    /// Get the world space bounding box for the entire world.
    /// </summary>
    public static Rect2 WorldRect => new Rect2(0, 0, WorldSize.X, WorldSize.Y);
}