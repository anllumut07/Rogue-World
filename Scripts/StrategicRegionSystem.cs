using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// StrategicRegionSystem - Manages strategic regions for weather simulation and AI planning.
/// Based on HoI4's map/strategicregions/*.txt (298 regions).
/// Each region groups provinces and defines seasonal weather periods.
/// </summary>
public static class StrategicRegionSystem
{
    private static bool _initialized = false;
    private static Dictionary<int, StrategicRegion> _regions = new();
    private static Dictionary<int, int> _provinceToRegion = new(); // provinceId -> regionId
    private static int _totalProvinces = 0;

    public static bool IsInitialized => _initialized;
    public static IReadOnlyDictionary<int, StrategicRegion> Regions => _regions;
    public static int RegionCount => _regions.Count;
    public static int TotalProvinces => _totalProvinces;

    public sealed class StrategicRegion
    {
        public int Id;
        public string Name;           // e.g., "STRATEGICREGION_1"
        public List<int> Provinces = new();  // Province IDs in this region
        public List<WeatherPeriod> WeatherPeriods = new(); // 12+ periods per year
    }

    public sealed class WeatherPeriod
    {
        public BetweenPeriod Between;   // Day of year range (0-365)
        public (float min, float max) Temperature; // Celsius range
        public Dictionary<string, float> Probabilities = new(); // phenomenon -> weight

        // Parsed fields
        public float NoPhenomenon;
        public float RainLight;
        public float RainHeavy;
        public float Snow;
        public float Blizzard;
        public float ArcticWater;
        public float Mud;
        public float Sandstorm;
        public float MinSnowLevel;
    }

    public struct BetweenPeriod
    {
        public float Start;  // Day of year (0-365)
        public float End;    // Day of year (0-365), can wrap
    }

    public static void Initialize()
    {
        if (_initialized) return;

        const string path = "res://Data/hoi4_imported/strategic_regions.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[StrategicRegionSystem] {path} not found!");
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
            var regionsList = JsonSerializer.Deserialize<List<JsonRegion>>(json, options);

            if (regionsList != null)
            {
                foreach (var jr in regionsList)
                {
                    var region = new StrategicRegion
                    {
                        Id = jr.Id,
                        Name = jr.Name?.Trim('"') ?? $"REGION_{jr.Id}",
                        Provinces = jr.Provinces ?? new List<int>(),
                    };

                    // Parse weather periods
                    if (jr.Weather?.Period != null)
                    {
                        foreach (var jp in jr.Weather.Period)
                        {
                            var period = new WeatherPeriod
                            {
                                Between = jp.Between,
                                Temperature = (jp.Temperature != null && jp.Temperature.Count == 2)
                                    ? (jp.Temperature[0], jp.Temperature[1])
                                    : (0f, 20f),
                                NoPhenomenon = jp.NoPhenomenon,
                                RainLight = jp.RainLight,
                                RainHeavy = jp.RainHeavy,
                                Snow = jp.Snow,
                                Blizzard = jp.Blizzard,
                                ArcticWater = jp.ArcticWater,
                                Mud = jp.Mud,
                                Sandstorm = jp.Sandstorm,
                                MinSnowLevel = jp.MinSnowLevel,
                            };

                            // Build probabilities dictionary (only non-zero)
                            if (period.NoPhenomenon > 0) period.Probabilities["no_phenomenon"] = period.NoPhenomenon;
                            if (period.RainLight > 0) period.Probabilities["rain_light"] = period.RainLight;
                            if (period.RainHeavy > 0) period.Probabilities["rain_heavy"] = period.RainHeavy;
                            if (period.Snow > 0) period.Probabilities["snow"] = period.Snow;
                            if (period.Blizzard > 0) period.Probabilities["blizzard"] = period.Blizzard;
                            if (period.ArcticWater > 0) period.Probabilities["arctic_water"] = period.ArcticWater;
                            if (period.Mud > 0) period.Probabilities["mud"] = period.Mud;
                            if (period.Sandstorm > 0) period.Probabilities["sandstorm"] = period.Sandstorm;

                            region.WeatherPeriods.Add(period);
                        }
                    }

                    _regions[region.Id] = region;
                    _totalProvinces += region.Provinces.Count;

                    // Build reverse lookup
                    foreach (var pid in region.Provinces)
                        _provinceToRegion[pid] = region.Id;
                }
            }

            _initialized = true;
            GD.Print($"[StrategicRegionSystem] Initialized: {_regions.Count} regions, {_totalProvinces} province references, {_provinceToRegion.Count} unique provinces mapped");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[StrategicRegionSystem] Failed to load: {e.Message}");
        }
    }

    public static void ResetForNewGame()
    {
        // Static data, no per-game state to reset
    }

    /// <summary>
    /// Get the strategic region ID for a province.
    /// </summary>
    public static int GetRegionIdForProvince(int provinceId)
    {
        return _provinceToRegion.GetValueOrDefault(provinceId, -1);
    }

    /// <summary>
    /// Get the strategic region by ID.
    /// </summary>
    public static StrategicRegion GetRegion(int regionId)
    {
        return _regions.GetValueOrDefault(regionId);
    }

    /// <summary>
    /// Get the strategic region for a province.
    /// </summary>
    public static StrategicRegion GetRegionForProvince(int provinceId)
    {
        if (_provinceToRegion.TryGetValue(provinceId, out var rid))
            return _regions.GetValueOrDefault(rid);
        return null;
    }

    /// <summary>
    /// Get all provinces in a region.
    /// </summary>
    public static IReadOnlyList<int> GetProvincesInRegion(int regionId)
    {
        return _regions.GetValueOrDefault(regionId)?.Provinces ?? new List<int>();
    }

    /// <summary>
    /// Get the current weather period for a region based on day of year.
    /// </summary>
    public static WeatherPeriod GetCurrentWeatherPeriod(int regionId, float dayOfYear)
    {
        if (!_regions.TryGetValue(regionId, out var region))
            return null;

        // Normalize day of year to 0-365
        dayOfYear = dayOfYear % 365f;
        if (dayOfYear < 0) dayOfYear += 365f;

        foreach (var period in region.WeatherPeriods)
        {
            if (IsDayInPeriod(dayOfYear, period.Between.Start, period.Between.End))
                return period;
        }

        // Fallback to first period
        return region.WeatherPeriods.Count > 0 ? region.WeatherPeriods[0] : null;
    }

    private static bool IsDayInPeriod(float day, float start, float end)
    {
        if (start <= end)
            return day >= start && day < end;
        // Wraps around year end (e.g., 350 to 30)
        return day >= start || day < end;
    }

    // JSON deserialization helpers
    private class JsonRegion
    {
        public int Id;
        public string Name;
        public List<int> Provinces;
        public JsonWeather Weather;
    }

    private class JsonWeather
    {
        public List<JsonPeriod> Period;
    }

    private class JsonPeriod
    {
        public BetweenPeriod Between;
        public List<float> Temperature;
        public float NoPhenomenon;
        public float RainLight;
        public float RainHeavy;
        public float Snow;
        public float Blizzard;
        public float ArcticWater;
        public float Mud;
        public float Sandstorm;
        public float MinSnowLevel;
    }
}