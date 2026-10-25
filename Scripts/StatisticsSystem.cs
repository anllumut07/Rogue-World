using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// İstatistik & Bütçe göstergeleri sistemi (M20.1).
// - Data/statistics_indicators.json'dan gösterge tanımlarını yükler.
// - Her ülke için gösterge değerini tutar (günlük deterministik dalgalanma) ve son 60 günün
//   değerini geçmiş olarak saklar (M20.5 grafik için).
// - StatisticsPanel oyuncu ülkesinin göstergelerini gösterir.
// - Save/Load: SaveSystem.SaveFile.Statistics (+ değer geçmişi) + SaveLoadTest özeti.
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
        public Dictionary<string, List<double>> History { get; set; } // indicatorId -> son 60 günün değeri
    }

    private static readonly string DataPath = "res://Data/statistics_indicators.json";
    private static readonly List<IndicatorDefinition> _definitions = new();
    private static bool _definitionsLoaded;
    private static readonly Dictionary<string, Dictionary<string, double>> _values = new(); // countryId -> indicatorId -> değer
    private const int HISTORY_DAYS = 60; // grafik için tutulan geçmiş gün sayısı
    private static readonly Dictionary<string, Dictionary<string, List<double>>> _history = new(); // countryId -> indicatorId -> değer geçişi

    public static IReadOnlyList<IndicatorDefinition> Definitions
    {
        get { EnsureDefinitions(); return _definitions; }
    }

    public static IReadOnlyList<double> GetHistory(string countryId, string indicatorId)
    {
        if (_history.TryGetValue(countryId, out var h) && h.TryGetValue(indicatorId, out var list))
            return list;
        return new List<double>();
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
        // Ülke bu gösterge için ilk kez yazılıyorsa önce TÜM gösterge setiyle
        // kaydını kur. Aksi hâlde SetValue tek göergeli bir sözlük bırakır ve
        // EnsureCountry artık "bu ülke var" deyip atlar: ülke ömür boyu eksik
        // gösterge setiyle kalır (M50.4 — A==B testinde böyle bir ülke
        // yükleyicinin hayalet geçmiş listeleriyle çatıştı).
        EnsureCountry(countryId);
        value = Math.Clamp(value, def.Min, def.Max);
        _values.TryGetValue(countryId, out var dict);
        dict ??= new Dictionary<string, double>();
        if (!_values.ContainsKey(countryId)) _values[countryId] = dict;
        dict[indicatorId] = value;

        // Günlük geçmişi kaydet (grafik için, M20.5)
        if (!_history.TryGetValue(countryId, out var h))
        {
            h = new Dictionary<string, List<double>>();
            _history[countryId] = h;
        }
        if (!h.TryGetValue(indicatorId, out var list))
        {
            list = new List<double>();
            h[indicatorId] = list;
        }
        list.Add(value);
        if (list.Count > HISTORY_DAYS) list.RemoveAt(0);
    }

    private static double GetBase(string indicatorId) =>
        _definitions.FirstOrDefault(d => d.Id == indicatorId)?.BaseValue ?? 0;

    // Bir ülkenin gösterge değerleri yoksa base değerlerle oluştur (deterministik)
    private static void EnsureCountry(string countryId)
    {
        if (string.IsNullOrEmpty(countryId) || _values.ContainsKey(countryId)) return;
        var dict = new Dictionary<string, double>();
        var h = new Dictionary<string, List<double>>();
        foreach (var def in _definitions)
        {
            double jitter = (DeterministicRNG.NextDouble() * 2.0 - 1.0) * 0.05; // ±%5
            double v = Math.Clamp(def.BaseValue * (1.0 + jitter), def.Min, def.Max);
            dict[def.Id] = v;
            h[def.Id] = new List<double> { v }; // gün 0 = başlangıç değeri
        }
        _values[countryId] = dict;
        _history[countryId] = h;
    }

    // ===== Başlatma =====
    public static void InitializeAll()
    {
        EnsureDefinitions();
        _values.Clear();
        _history.Clear();
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
            string countryId = kv.Key;
            var dict = kv.Value;
            foreach (var def in _definitions)
            {
                if (!dict.TryGetValue(def.Id, out double v)) continue;
                // Küçük deterministik randôm yürüyüş: günlük değişim ±0.3 puan
                double delta = (DeterministicRNG.NextDouble() * 2.0 - 1.0) * 0.3;
                SetValue(countryId, def.Id, Math.Clamp(v + delta, def.Min, def.Max));
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
                Values = kv.Value.ToDictionary(p => p.Key, p => p.Value),
                History = _history.TryGetValue(kv.Key, out var h)
                    ? h.ToDictionary(p => p.Key, p => new List<double>(p.Value))
                    : null
            }).ToArray()
        };
    }

    public static void LoadSaveData(IndicatorsSave data)
    {
        if (data == null) return;
        _values.Clear();
        _history.Clear();
        if (data.Countries != null)
        {
            foreach (var c in data.Countries)
            {
                if (string.IsNullOrEmpty(c.CountryId)) continue;
                _values[c.CountryId] = c.Values?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, double>();
                if (!_history.TryGetValue(c.CountryId, out var hh))
                {
                    hh = new Dictionary<string, List<double>>();
                    _history[c.CountryId] = hh;
                }
                foreach (var def in _definitions)
                {
                    // Yalnızca bu ülkenin GERÇEKTEN sahip olduğu gösterge için geçmiş
                    // kurulur. Tüm tanımlar gezilirse _values'ta olmayan gösterge için
                    // hayalet liste doğar: kayıtta hiç olmayan bu listeler yükleme
                    // sonrası belirir ve A==B özetinde geçmiş sayısı şaşar (M50.4).
                    if (!_values[c.CountryId].ContainsKey(def.Id)) continue;
                    hh[def.Id] = c.History != null && c.History.TryGetValue(def.Id, out var arr)
                        ? new List<double>(arr)
                        : new List<double> { _values[c.CountryId][def.Id] };
                }
            }
        }
        GD.Print($"[StatisticsSystem] {_values.Count} ülke göstergesi yüklendi.");
    }
}
