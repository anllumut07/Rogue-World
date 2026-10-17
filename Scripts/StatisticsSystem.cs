using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// İstatistik & Bütçe göstergeleri sistemi (M20.1).
// - Data/statistics_indicators.json'dan gösterge tanımlarını yükler.
// - Her ülke için gösterge değerini tutar (günlük deterministik dalgalanma).
// - StatisticsPanel oyuncu ülkesinin göstergelerini gösterir.
// - Save/Load: SaveSystem.SaveFile.Statistics + SaveLoadTest özerk özeti.
public static class StatisticsSystem
{
    public class IndicatorDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public double BaseValue { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public string Description { get; set; }
    }

    public class IndicatorsSave
    {
        public CountryIndicatorsSave[] Countries { get; set; }
    }
    public class CountryIndicatorsSave
    {
        public string CountryId { get; set; }
        public Dictionary<string, double> Values { get; set; }
    }

    private static readonly string DataPath = "res://Data/statistics_indicators.json";
    private static readonly List<IndicatorDefinition> _definitions = new();
    private static bool _definitionsLoaded;
    private static readonly Dictionary<string, Dictionary<string, double>> _values = new(); // countryId -> indicatorId -> value

    public static IReadOnlyList<IndicatorDefinition> Definitions
    {
        get { EnsureDefinitions(); return _definitions; }
    }

    private static void EnsureDefinitions()
    {
        if (_definitionsLoaded) return;
        _definitionsLoaded = true;
        LoadDefinitions();
    }

    private static void LoadDefinitions()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(DataPath))
            {
                GD.PrintErr($"[StatisticsSystem] Veri dosyası yok: {DataPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(DataPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Indicators", out var arr)) return;
            foreach (var item in arr.EnumerateArray())
            {
                _definitions.Add(new IndicatorDefinition
                {
                    Id = item.GetProperty("Id").GetString(),
                    Name = item.GetProperty("Name").GetString(),
                    Category = item.GetProperty("Category").GetString(),
                    BaseValue = item.GetProperty("BaseValue").GetDouble(),
                    Min = item.GetProperty("Min").GetDouble(),
                    Max = item.GetProperty("Max").GetDouble(),
                    Description = item.TryGetProperty("Description", out var desc) ? desc.GetString() : ""
                });
            }
            GD.Print($"[StatisticsSystem] {_definitions.Count} gösterge tanımı yüklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[StatisticsSystem] Tanım yükleme HATA: {e.Message}");
        }
    }

    // ===== Değer erişimi =====
    public static double GetValue(string countryId, string indicatorId)
    {
        EnsureDefinitions();
        EnsureCountry(countryId);
        if (_values.TryGetValue(countryId, out var dict) && dict.TryGetValue(indicatorId, out var v))
            return v;
        return GetBase(indicatorId);
    }

    public static void SetValue(string countryId, string indicatorId, double value)
    {
        EnsureDefinitions();
        var def = _definitions.FirstOrDefault(d => d.Id == indicatorId);
        if (def == null) return;
        value = Math.Clamp(value, def.Min, def.Max);
        if (!_values.TryGetValue(countryId, out var dict))
        {
            dict = new Dictionary<string, double>();
            _values[countryId] = dict;
        }
        dict[indicatorId] = value;
    }

    private static double GetBase(string indicatorId) =>
        _definitions.FirstOrDefault(d => d.Id == indicatorId)?.BaseValue ?? 0;

    // Bir ülkenin gösterge değerleri yoksa base değerlerle oluştur (deterministik)
    private static void EnsureCountry(string countryId)
    {
        if (string.IsNullOrEmpty(countryId) || _values.ContainsKey(countryId)) return;
        var dict = new Dictionary<string, double>();
        foreach (var def in _definitions)
        {
            double jitter = (DeterministicRNG.NextDouble() * 2.0 - 1.0) * 0.05; // ±%5
            double v = Math.Clamp(def.BaseValue * (1.0 + jitter), def.Min, def.Max);
            dict[def.Id] = v;
        }
        _values[countryId] = dict;
    }

    // ===== Başlatma =====
    public static void InitializeAll()
    {
        EnsureDefinitions();
        _values.Clear();
        foreach (var c in CountryDatabase.All())
            EnsureCountry(c.Id);
        GD.Print($"[StatisticsSystem] {_values.Count} ülke göstergesi başlatıldı.");
    }

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====
    public static void DailyTick()
    {
        EnsureDefinitions();
        foreach (var c in CountryDatabase.All())
            EnsureCountry(c.Id);

        foreach (var kv in _values)
        {
            var dict = kv.Value;
            foreach (var def in _definitions)
            {
                if (!dict.TryGetValue(def.Id, out double v)) continue;
                // Küçük deterministik randôm yürüyüş: günlük değişim ±0.3 puan
                double delta = (DeterministicRNG.NextDouble() * 2.0 - 1.0) * 0.3;
                dict[def.Id] = Math.Clamp(v + delta, def.Min, def.Max);
            }
        }
    }

    // ===== M12: Save/Load =====
    public static IndicatorsSave GetSaveData()
    {
        EnsureDefinitions();
        return new IndicatorsSave
        {
            Countries = _values.Select(kv => new CountryIndicatorsSave
            {
                CountryId = kv.Key,
                Values = kv.Value.ToDictionary(p => p.Key, p => p.Value)
            }).ToArray()
        };
    }

    public static void LoadSaveData(IndicatorsSave data)
    {
        if (data == null) return;
        _values.Clear();
        if (data.Countries != null)
        {
            foreach (var c in data.Countries)
            {
                if (string.IsNullOrEmpty(c.CountryId)) continue;
                _values[c.CountryId] = c.Values?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, double>();
            }
        }
        GD.Print($"[StatisticsSystem] {_values.Count} ülke göstergesi yüklendi.");
    }
}
