using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// WeatherSystem - Daily probabilistic weather simulation with terrain-modulated probabilities.
/// Based on HoI4's common/weather.txt mechanics.
/// Integrates with StrategicRegionSystem for regional weather.
/// </summary>
public static class WeatherSystem
{
    private static bool _initialized = false;
    private static bool _testMode = false;

    // Weather state per province (accumulated)
    private static Dictionary<int, ProvinceWeather> _provinceWeather = new();

    // Terrain modifiers on weather probabilities (from HoI4 common/weather.txt)
    private static readonly Dictionary<string, Dictionary<string, float>> TerrainWeatherMods = new()
    {
        ["mud"] = new Dictionary<string, float>
        {
            ["mountain"] = -0.3f, ["plains"] = +0.5f, ["marsh"] = +0.9f,
            ["desert"] = +0.1f, ["jungle"] = +0.2f, ["hills"] = +0.2f,
            ["forest"] = +0.1f, ["urban"] = 0f, ["supercity"] = 0f, ["lakes"] = +0.3f
        },
        ["snow"] = new Dictionary<string, float>
        {
            ["mountain"] = +0.8f, ["desert"] = -1.0f, ["urban"] = -0.4f,
            ["hills"] = +0.3f, ["plains"] = +0.1f, ["forest"] = +0.1f,
            ["marsh"] = +0.2f, ["jungle"] = +0.1f, ["supercity"] = -0.2f, ["lakes"] = +0.2f
        },
        ["sandstorm"] = new Dictionary<string, float>
        {
            ["desert"] = +0.8f, ["mountain"] = -1.0f, ["hills"] = -0.5f,
            ["plains"] = -0.2f, ["forest"] = -0.3f, ["urban"] = -0.5f
        },
        ["temperature"] = new Dictionary<string, float>
        {
            ["desert"] = +5f, ["mountain"] = -10f, ["hills"] = -3f,
            ["forest"] = -1f, ["urban"] = +3f, ["supercity"] = +3f,
            ["jungle"] = +5f, ["marsh"] = -1f, ["plains"] = +1f, ["lakes"] = -2f
        }
    };

    // Temperature thresholds (from HoI4)
    public const float ExtremeCold = -12f;
    public const float VeryCold = -4f;
    public const float TemperateMin = -4f;
    public const float TemperateMax = 20f;
    public const float VeryHot = 28f;
    public const float ExtremeHot = 28f;

    // Mud mechanics constants
    public const float MudGainRainLight = 0.5f;
    public const float MudGainRainHeavy = 1.8f;
    public const float MudMax = 200f;
    public const float MudMin = 5f;
    public const float MudDryoutTempMin = 2f;
    public const float MudDryoutTempMax = 38f;
    public const float MudDryoutMultMin = 0.0005f;
    public const float MudDryoutMultMax = 0.4f;
    public const float MudFreezeTemp = -1f;

    // Snow mechanics constants
    public const float SnowGainSnowing = 1.0f;
    public const float SnowGainBlizzard = 5.0f;
    public const float SnowMax = 80f;
    public const float SnowMeltTempMin = 0f;
    public const float SnowMeltTempMax = 12f;

    // Weather duration ranges (days, weighted toward lower)
    private static readonly Dictionary<string, (int min, int max)> WeatherDurations = new()
    {
        ["no_phenomenon"] = (5, 12),
        ["rain_light"] = (5, 20),
        ["rain_heavy"] = (2, 15),
        ["snow"] = (5, 20),
        ["blizzard"] = (2, 15),
        ["sandstorm"] = (2, 10),
    };

    public sealed class ProvinceWeather
    {
        public int ProvinceId;
        public float MudLevel = 0f;        // 0-200
        public float SnowLevel = 0f;       // 0-80
        public float Temperature = 15f;    // Current temperature (Celsius)
        public string CurrentPhenomenon = "no_phenomenon";
        public int PhenomenonDaysRemaining = 0;
        public float MudLevelVisual = 0f;  // For rendering (smoothed)
        public float SnowLevelVisual = 0f; // For rendering (smoothed)
    }

    public enum TemperatureCategory
    {
        ExtremeCold,
        VeryCold,
        Temperate,
        VeryHot,
        ExtremeHot
    }

    public static bool IsInitialized => _initialized;

    public static void Initialize()
    {
        if (_initialized) return;

        // Ensure StrategicRegionSystem is initialized first
        StrategicRegionSystem.Initialize();

        _initialized = true;
        GD.Print("[WeatherSystem] Initialized");
    }

    public static void ResetForNewGame()
    {
        _provinceWeather.Clear();
    }

    public static void EnableTestMode()
    {
        _testMode = true;
        GD.Print("[WeatherSystem] Test mode enabled");
    }

    /// <summary>
    /// Get or create weather state for a province.
    /// </summary>
    public static ProvinceWeather GetProvinceWeather(int provinceId)
    {
        if (!_provinceWeather.TryGetValue(provinceId, out var pw))
        {
            pw = new ProvinceWeather { ProvinceId = provinceId };
            _provinceWeather[provinceId] = pw;
        }
        return pw;
    }

    /// <summary>
    /// Daily weather tick for all provinces.
    /// Called from GameManager daily tick.
    /// </summary>
    public static void DailyTick()
    {
        // We iterate over provinces that have weather data or are in strategic regions
        // In practice, this would be all land provinces
        var allProvinces = GetAllProvincesWithWeather();

        foreach (var provinceId in allProvinces)
        {
            TickProvinceWeather(provinceId);
        }
    }

    private static List<int> GetAllProvincesWithWeather()
    {
        // Return all provinces from strategic regions
        var provinces = new List<int>();
        foreach (var region in StrategicRegionSystem.Regions.Values)
        {
            provinces.AddRange(region.Provinces);
        }
        return provinces.Distinct().ToList();
    }

    /// <summary>
    /// Process daily weather for a single province.
    /// </summary>
    private static void TickProvinceWeather(int provinceId)
    {
        var pw = GetProvinceWeather(provinceId);
        var region = StrategicRegionSystem.GetRegionForProvince(provinceId);

        if (region == null) return;

        // Get current weather period for this region
        var dayOfYear = GameManager.Instance?.TotalDays % 365 ?? 0;
        var period = StrategicRegionSystem.GetCurrentWeatherPeriod(region.Id, dayOfYear);

        if (period == null) return;

        // Get terrain for this province
        var terrain = GetProvinceTerrain(provinceId);

        // --- Temperature ---
        float baseTemp = (float)DeterministicRNG.NextDouble() * (period.Temperature.max - period.Temperature.min) + period.Temperature.min;
        float terrainTempMod = GetTerrainTemperatureModifier(terrain);
        pw.Temperature = baseTemp + terrainTempMod;

        // --- Phenomenon Roll ---
        if (pw.PhenomenonDaysRemaining <= 0)
        {
            // Roll new phenomenon
            pw.CurrentPhenomenon = RollPhenomenon(period, terrain);
            var dur = WeatherDurations.GetValueOrDefault(pw.CurrentPhenomenon, (1, 5));
            pw.PhenomenonDaysRemaining = DeterministicRNG.NextInt(dur.Item1, dur.Item2);
        }
        else
        {
            pw.PhenomenonDaysRemaining--;
        }

        // --- Mud Accumulation ---
        UpdateMud(pw, period, terrain);

        // --- Snow Accumulation ---
        UpdateSnow(pw, period, terrain);
    }

    private static TerrainSystem.TerrainCategory GetProvinceTerrain(int provinceId)
    {
        // Get terrain from ProvinceData cache
        var prov = ProvinceDataCache.GetById(provinceId.ToString());
        if (prov != null && prov.Terrain != TerrainSystem.TerrainCategory.Unknown)
            return prov.Terrain;

        // Fallback: try to get from ProceduralProvinceSource
        return TerrainSystem.TerrainCategory.Plains;
    }

    private static string RollPhenomenon(StrategicRegionSystem.WeatherPeriod period, TerrainSystem.TerrainCategory terrain)
    {
        // Build weighted probabilities with terrain modifiers
        var weights = new Dictionary<string, float>();

        foreach (var kvp in period.Probabilities)
        {
            float weight = kvp.Value;
            string phenomenon = kvp.Key;

            // Apply terrain modifier
            if (TerrainWeatherMods.TryGetValue(phenomenon, out var terrainMods))
            {
                string terrainKey = terrain.ToString().ToLower();
                if (terrainMods.TryGetValue(terrainKey, out float mod))
                    weight += mod;
            }

            // Clamp to minimum
            weight = Math.Max(0.001f, weight);
            weights[phenomenon] = weight;
        }

        // Normalize
        float total = 0f;
        foreach (var w in weights.Values) total += w;

        // Weighted random selection
        float roll = (float)DeterministicRNG.NextDouble() * total;
        float accum = 0f;

        foreach (var kvp in weights)
        {
            accum += kvp.Value;
            if (roll <= accum)
                return kvp.Key;
        }

        return "no_phenomenon"; // fallback
    }

    private static float GetTerrainTemperatureModifier(TerrainSystem.TerrainCategory terrain)
    {
        string terrainKey = terrain.ToString().ToLower();
        if (TerrainWeatherMods["temperature"].TryGetValue(terrainKey, out float mod))
            return mod;
        return 0f;
    }

    private static void UpdateMud(ProvinceWeather pw, StrategicRegionSystem.WeatherPeriod period, TerrainSystem.TerrainCategory terrain)
    {
        float gain = 0f;

        // Rain adds mud
        if (pw.CurrentPhenomenon == "rain_light")
            gain = MudGainRainLight;
        else if (pw.CurrentPhenomenon == "rain_heavy")
            gain = MudGainRainHeavy;

        // Apply terrain modifier
        string terrainKey = GetProvinceTerrain(pw.ProvinceId).ToString().ToLower();
        if (TerrainWeatherMods["mud"].TryGetValue(terrainKey, out float mudMod))
            gain *= (1f + mudMod);

        // Temperature affects dryout
        float dryout = 0f;
        if (pw.Temperature >= MudDryoutTempMin && pw.Temperature <= MudDryoutTempMax)
        {
            float t = (pw.Temperature - MudDryoutTempMin) / (MudDryoutTempMax - MudDryoutTempMin);
            dryout = MudDryoutMultMin + t * (MudDryoutMultMax - MudDryoutMultMin);
        }

        // Frozen mud disappears
        if (pw.Temperature <= MudFreezeTemp)
        {
            pw.MudLevel = Math.Max(0f, pw.MudLevel - 5f); // Rapid freeze-thaw removal
        }
        else
        {
            pw.MudLevel = Math.Clamp(pw.MudLevel + gain - dryout, MudMin, MudMax);
        }

        // Smooth visual
        pw.MudLevelVisual = Mathf.Lerp(pw.MudLevelVisual, pw.MudLevel, 0.1f);
    }

    private static void UpdateSnow(ProvinceWeather pw, StrategicRegionSystem.WeatherPeriod period, TerrainSystem.TerrainCategory terrain)
    {
        float gain = 0f;

        // Snow/blizzard adds snow
        if (pw.CurrentPhenomenon == "snow")
            gain = SnowGainSnowing;
        else if (pw.CurrentPhenomenon == "blizzard")
            gain = SnowGainBlizzard;

        // Apply terrain modifier
        string terrainKey = GetProvinceTerrain(pw.ProvinceId).ToString().ToLower();
        if (TerrainWeatherMods["snow"].TryGetValue(terrainKey, out float snowMod))
            gain *= (1f + snowMod);

        // Melting
        float melt = 0f;
        if (pw.Temperature >= SnowMeltTempMin && pw.Temperature <= SnowMeltTempMax)
        {
            float t = (pw.Temperature - SnowMeltTempMin) / (SnowMeltTempMax - SnowMeltTempMin);
            melt = t * 2f; // Melt rate
        }

        pw.SnowLevel = Math.Clamp(pw.SnowLevel + gain - melt, 0f, SnowMax);

        // Smooth visual
        pw.SnowLevelVisual = Mathf.Lerp(pw.SnowLevelVisual, pw.SnowLevel, 0.05f);
    }

    /// <summary>
    /// Get temperature category for a province.
    /// </summary>
    public static TemperatureCategory GetTemperatureCategory(float temperature)
    {
        if (temperature <= ExtremeCold) return TemperatureCategory.ExtremeCold;
        if (temperature <= VeryCold) return TemperatureCategory.VeryCold;
        if (temperature >= ExtremeHot) return TemperatureCategory.ExtremeHot;
        if (temperature >= VeryHot) return TemperatureCategory.VeryHot;
        return TemperatureCategory.Temperate;
    }

    /// <summary>
    /// Get movement penalty for current weather/terrain.
    /// </summary>
    public static float GetMovementPenalty(int provinceId)
    {
        var pw = GetProvinceWeather(provinceId);
        var terrain = GetProvinceTerrain(provinceId);
        var terrainDef = TerrainSystem.Get(terrain);

        float penalty = 0f;

        // Mud penalty
        if (pw.MudLevel > MudMin)
            penalty += 0.1f * (pw.MudLevel / MudMax);

        // Snow penalty
        if (pw.SnowLevel > 0)
            penalty += 0.2f * (pw.SnowLevel / SnowMax);

        // Extreme temperature penalty
        var tempCat = GetTemperatureCategory(pw.Temperature);
        if (tempCat == TemperatureCategory.ExtremeCold || tempCat == TemperatureCategory.ExtremeHot)
            penalty += 0.3f;
        else if (tempCat == TemperatureCategory.VeryCold || tempCat == TemperatureCategory.VeryHot)
            penalty += 0.15f;

        // Phenomenon-specific penalties
        switch (pw.CurrentPhenomenon)
        {
            case "rain_heavy": penalty += 0.1f; break;
            case "snow": penalty += 0.15f; break;
            case "blizzard": penalty += 0.3f; break;
            case "sandstorm": penalty += 0.2f; break;
        }

        return Math.Clamp(penalty, 0f, 1f);
    }

    /// <summary>
    /// Get combat penalty for current weather/terrain.
    /// </summary>
    public static (float attackMod, float defenseMod) GetCombatModifiers(int provinceId)
    {
        var pw = GetProvinceWeather(provinceId);
        var terrain = GetProvinceTerrain(provinceId);

        float attackMod = 0f;
        float defenseMod = 0f;

        // Mud reduces both attack and defense
        if (pw.MudLevel > MudMin)
        {
            float mudFactor = pw.MudLevel / MudMax;
            attackMod -= 0.1f * mudFactor;
            defenseMod -= 0.05f * mudFactor;
        }

        // Snow affects visibility and movement
        if (pw.SnowLevel > 0)
        {
            float snowFactor = pw.SnowLevel / SnowMax;
            attackMod -= 0.15f * snowFactor;
            defenseMod += 0.05f * snowFactor; // Defender advantage in snow
        }

        // Extreme temperatures
        var tempCat = GetTemperatureCategory(pw.Temperature);
        if (tempCat == TemperatureCategory.ExtremeCold)
        {
            attackMod -= 0.2f;
            defenseMod -= 0.1f;
        }
        else if (tempCat == TemperatureCategory.ExtremeHot)
        {
            attackMod -= 0.15f;
            defenseMod -= 0.15f;
        }

        // Phenomenon-specific combat mods
        switch (pw.CurrentPhenomenon)
        {
            case "rain_heavy":
                attackMod -= 0.1f;
                defenseMod -= 0.05f;
                break;
            case "snow":
                attackMod -= 0.15f;
                defenseMod += 0.05f;
                break;
            case "blizzard":
                attackMod -= 0.3f;
                defenseMod += 0.1f;
                break;
            case "sandstorm":
                attackMod -= 0.2f;
                defenseMod -= 0.1f;
                break;
        }

        return (Math.Clamp(attackMod, -0.5f, 0.5f), Math.Clamp(defenseMod, -0.5f, 0.5f));
    }

    /// <summary>
    /// Get supply flow penalty for current weather/terrain.
    /// </summary>
    public static float GetSupplyPenalty(int provinceId)
    {
        var pw = GetProvinceWeather(provinceId);

        float penalty = 0f;

        // Mud reduces supply flow
        if (pw.MudLevel > MudMin)
            penalty += 0.15f * (pw.MudLevel / MudMax);

        // Snow reduces supply flow
        if (pw.SnowLevel > 0)
            penalty += 0.2f * (pw.SnowLevel / SnowMax);

        // Extreme cold freezes supply
        if (pw.Temperature <= ExtremeCold)
            penalty += 0.3f;
        else if (pw.Temperature <= VeryCold)
            penalty += 0.15f;

        // Blizzard severely disrupts supply
        if (pw.CurrentPhenomenon == "blizzard")
            penalty += 0.4f;

        return Math.Clamp(penalty, 0f, 0.9f);
    }

    /// <summary>
    /// Get attrition multiplier for current weather/terrain.
    /// </summary>
    public static float GetAttritionMultiplier(int provinceId)
    {
        var pw = GetProvinceWeather(provinceId);

        float mult = 1f;

        // Extreme temperatures
        if (pw.Temperature <= ExtremeCold)
            mult += 0.5f;
        else if (pw.Temperature <= VeryCold)
            mult += 0.2f;
        else if (pw.Temperature >= ExtremeHot)
            mult += 0.3f;
        else if (pw.Temperature >= VeryHot)
            mult += 0.15f;

        // Phenomena
        switch (pw.CurrentPhenomenon)
        {
            case "blizzard": mult += 0.4f; break;
            case "snow": mult += 0.15f; break;
            case "rain_heavy": mult += 0.1f; break;
            case "sandstorm": mult += 0.2f; break;
        }

        return mult;
    }
}