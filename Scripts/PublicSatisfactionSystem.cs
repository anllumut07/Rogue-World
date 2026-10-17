using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Halk memnuniyeti sistemi (M20.3).
// - Data/public_satisfaction.json'dan toplum grupları (işçi, iş dünyası, genç, yaşlı,
//   ordu, dindar, laik, kırsal, kentsel) ve her grubun gösterge/harcama tepki ağırlıkları
//   yüklenir.
// - Her grubun memnuniyeti 0-100 aralığında tutulur. Günlük tick'te her grubun
//   hedef memnuniyeti mevcut göstergeler + bütçe harcamaları üzerinden ağırlıklı olarak
//   hesaplanır ve mevcut değer hedef doğru yavaşça doğruçar (+ küçük deterministik jitter).
// - Memnuniyet, seçimler, bakanlık kararları, istifa ve skandal mekaniklerinin (M21-M23)
//   girdisi olarak kullanılır.
// - Save/Load: SaveSystem.SaveFile.PublicSatisfaction + SaveLoadTest özeti.
// - Deterministik: yalnızca DeterministicRNG (new Random() yok).
public static class PublicSatisfactionSystem
{
    public class SatisfactionGroup
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double Base { get; set; } = 50;
        public string Description { get; set; }
    }

    public class SatisfactionSave
    {
        public CountrySatisfactionSave[] Countries { get; set; }
    }
    public class CountrySatisfactionSave
    {
        public string CountryId { get; set; }
        public Dictionary<string, double> Satisfaction { get; set; } // groupId -> değer (0-100)
    }

    private static readonly string DataPath = "res://Data/public_satisfaction.json";
    private static readonly List<SatisfactionGroup> _groups = new();
    private static readonly Dictionary<string, Dictionary<string, double>> _groupWeights = new(); // groupId -> inputId -> weight

    // countryId -> groupId -> satisfaction (0-100)
    private static readonly Dictionary<string, Dictionary<string, double>> _satisfaction = new();

    public static IReadOnlyList<SatisfactionGroup> Groups => _groups;
    public static double MoveSpeed = 0.15; // günde hedef memnuniyetin %15'i kadar yaklaş

    static PublicSatisfactionSystem()
    {
        LoadData();
    }

    private static void LoadData()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(DataPath))
            {
                GD.PrintErr($"[PublicSatisfactionSystem] Veri dosyası yok: {DataPath}");
                ApplyDefaults();
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(DataPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Groups", out var grpArr))
            {
                _groups.Clear();
                foreach (var g in grpArr.EnumerateArray())
                {
                    _groups.Add(new SatisfactionGroup
                    {
                        Id = g.GetProperty("Id").GetString(),
                        Name = g.TryGetProperty("Name", out var n) ? n.GetString() : g.GetProperty("Id").GetString(),
                        Base = g.TryGetProperty("Base", out var b) ? b.GetDouble() : 50,
                        Description = g.TryGetProperty("Description", out var d) ? d.GetString() : ""
                    });
                }
            }

            _groupWeights.Clear();
            if (root.TryGetProperty("GroupWeights", out var weights))
            {
                foreach (var gprop in weights.EnumerateObject())
                {
                    var dict = new Dictionary<string, double>();
                    foreach (var wprop in gprop.Value.EnumerateObject())
                        dict[wprop.Name] = wprop.Value.GetDouble();
                    _groupWeights[gprop.Name] = dict;
                }
            }

            GD.Print($"[PublicSatisfactionSystem] {_groups.Count} toplum grubu yüklendi, {_groupWeights.Count} ağırlık kümesi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[PublicSatisfactionSystem] Veri yükleme HATA: {e.Message}");
            ApplyDefaults();
        }
    }

    private static void ApplyDefaults()
    {
        _groups.Clear();
        _groups.AddRange(new[]
        {
            new SatisfactionGroup { Id = "workers", Name = "Workers", Base = 50, Description = "Industrial and service workforce" },
            new SatisfactionGroup { Id = "business", Name = "Business Community", Base = 50, Description = "Entrepreneurs, investors, company owners" },
            new SatisfactionGroup { Id = "youth", Name = "Youth", Base = 50, Description = "Young citizens, students" },
            new SatisfactionGroup { Id = "elderly", Name = "Elderly", Base = 50, Description = "Retired and senior citizens" },
            new SatisfactionGroup { Id = "military", Name = "Military", Base = 50, Description = "Armed forces personnel" },
            new SatisfactionGroup { Id = "religious", Name = "Religious", Base = 50, Description = "Faith-based citizens" },
            new SatisfactionGroup { Id = "secular", Name = "Secular", Base = 50, Description = "Non-religious citizens" },
            new SatisfactionGroup { Id = "rural", Name = "Rural", Base = 50, Description = "Countryside population" },
            new SatisfactionGroup { Id = "urban", Name = "Urban", Base = 50, Description = "City dwellers" }
        });
        _groupWeights.Clear();
    }

    public static double GetSatisfaction(string countryId, string groupId)
    {
        var state = GetState(countryId);
        return state.TryGetValue(groupId, out var v) ? v : GetGroupBase(groupId);
    }

    public static void SetSatisfaction(string countryId, string groupId, double value)
    {
        var state = GetState(countryId);
        state[groupId] = Math.Clamp(value, 0, 100);
    }

    private static double GetGroupBase(string groupId) =>
        _groups.FirstOrDefault(g => g.Id == groupId)?.Base ?? 50;

    // Bir ülkenin grup memnuniyetlerini yoksa base değerlerle oluştur (deterministik)
    private static Dictionary<string, double> GetState(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return new Dictionary<string, double>();
        if (!_satisfaction.TryGetValue(countryId, out var state))
        {
            state = new Dictionary<string, double>();
            foreach (var g in _groups)
            {
                double jitter = (DeterministicRNG.NextDouble() * 2.0 - 1.0) * 2.0; // ±2
                state[g.Id] = Math.Clamp(g.Base + jitter, 0, 100);
            }
            _satisfaction[countryId] = state;
        }
        return state;
    }

    private static void EnsureCountry(string countryId)
    {
        GetState(countryId); // lazy oluşturur
    }

    // ===== Başlatma =====
    public static void InitializeAll()
    {
        LoadData();
        _satisfaction.Clear();
        foreach (var c in CountryDatabase.All())
            EnsureCountry(c.Id);
        GD.Print($"[PublicSatisfactionSystem] {_satisfaction.Count} ülke halk memnuniyeti başlatıldı.");
    }

    public static void ResetForNewGame()
    {
        InitializeAll();
    }

    // Bir giriş değeri (göstergeye veya harcama kategorisine) çözümle
    private static double ResolveInput(string countryId, string inputId)
    {
        // Önce gösterge (StatisticsSystem)
        var def = StatisticsSystem.Definitions.FirstOrDefault(d => d.Id == inputId);
        if (def != null)
            return StatisticsSystem.GetValue(countryId, inputId);
        // Sonra harcama kategorisi (BudgetSystem)
        return BudgetSystem.GetSpendingPercent(countryId, inputId);
    }

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====
    public static void DailyTick()
    {
        LoadData();
        foreach (var c in CountryDatabase.All())
            EnsureCountry(c.Id);

        foreach (var kv in _satisfaction)
        {
            string countryId = kv.Key;
            var state = kv.Value;

            foreach (var group in _groups)
            {
                double target = group.Base;
                if (_groupWeights.TryGetValue(group.Id, out var weights))
                {
                    foreach (var w in weights)
                        target += w.Value * ResolveInput(countryId, w.Key);
                }
                target = Math.Clamp(target, 0, 100);

                double current = state.TryGetValue(group.Id, out var v) ? v : group.Base;
                double next = current + (target - current) * MoveSpeed;

                // Küçük deterministik jitter (günlük mikro değişim)
                double jitter = (DeterministicRNG.NextDouble() * 2.0 - 1.0) * 0.5;
                next = Math.Clamp(next + jitter, 0, 100);

                state[group.Id] = next;
            }
        }

        GD.Print("[PublicSatisfactionSystem] Günlük halk memnuniyeti tick'i tamamlandı.");
    }

    // ===== M12: Save/Load =====
    public static SatisfactionSave GetSaveData()
    {
        return new SatisfactionSave
        {
            Countries = _satisfaction.Select(kv => new CountrySatisfactionSave
            {
                CountryId = kv.Key,
                Satisfaction = kv.Value.ToDictionary(p => p.Key, p => p.Value)
            }).ToArray()
        };
    }

    public static void LoadSaveData(SatisfactionSave data)
    {
        if (data == null) return;
        _satisfaction.Clear();
        if (data.Countries != null)
        {
            foreach (var c in data.Countries)
            {
                if (string.IsNullOrEmpty(c.CountryId)) continue;
                _satisfaction[c.CountryId] = c.Satisfaction?.ToDictionary(p => p.Key, p => p.Value)
                    ?? new Dictionary<string, double>();
            }
        }
        GD.Print($"[PublicSatisfactionSystem] {_satisfaction.Count} ülke halk memnuniyeti yüklendi.");
    }
}
