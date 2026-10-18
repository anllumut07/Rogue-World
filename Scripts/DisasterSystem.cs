using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Doğal afet ve beklenmedik olay sistemi (M26.1).
// - Data/disasters.json'dan afet tanımlarını yükler (tür, şiddet, bölgeye özel olasılık,
//   bölgeye özel şiddet çarpanı, gösterge etkileri, süre).
// - Ülke→bölge eşleştirmesi aktif senaryonun library/countries.json'dan gelir (Region alanı).
// - Günlük tick'te her ülke için DeterministicRNG ile tetikleme zarı atar; bölgeye özel
//   olasılıktan geçen afet strikes yapar ve göstergelere tek seferlik şok uygular.
// - Aktif afetler günlük olarak azaltılır; süresi dolunca kaldırılır.
// - Save/Load: SaveSystem.SaveFile.Disasters + SaveLoadTest özeti.
// Statik sınıf; GameManager günlük tick'inde DailyTick() çağrılır.
// Test kancası: --disaster-test → EnableTestMode() (olasılığı artırır).
public static class DisasterSystem
{
    public class DisasterDefinition
    {
        public string Id = "?";
        public string Name = "?";
        public string Type = "Earthquake"; // Earthquake, Flood, Drought, Wildfire, Storm, Volcano, Epidemic
        public int Severity = 1;           // 1-5
        public int DurationDays = 7;       // aktif sürüm (gün)
        public double BaseProbabilityPerDay = 0.001; // 0-1
        public string[] Regions = Array.Empty<string>(); // boş = tüm bölgeler
        public Dictionary<string, float> RegionSeverityMultiplier = new(); // bölge → şiddet çarpanı
        public Dictionary<string, double> Impacts = new(); // göstergeId → delta (tek seferlik şok)
        public string Description = "";
    }

    public class ActiveDisaster
    {
        public string DisasterId = "";
        public string CountryId = "";
        public int Day = 0;
        public int DaysLeft = 0;
        public int Severity = 1;
        public string Region = "";
    }

    public class DisasterRecord
    {
        public string CountryId = "";
        public string DisasterId = "";
        public int Day = 0;
        public int Severity = 1;
        public string Region = "";
    }

    // DTO (Save/Load)
    public class DisasterSave
    {
        public ActiveDisasterSave[] Active { get; set; }
        public DisasterRecordSave[] History { get; set; }
    }
    public class ActiveDisasterSave
    {
        public string DisasterId = "";
        public string CountryId = "";
        public int Day = 0;
        public int DaysLeft = 0;
        public int Severity = 1;
        public string Region = "";
    }
    public class DisasterRecordSave
    {
        public string CountryId = "";
        public string DisasterId = "";
        public int Day = 0;
        public int Severity = 1;
        public string Region = "";
    }

    private static readonly string DataPath = "res://Data/disasters.json";
    private static readonly List<DisasterDefinition> _definitions = new();
    private static bool _definitionsLoaded;
    // ülkeId -> bölge adı (aktif senaryonun library/countries.json'dan)
    private static readonly Dictionary<string, string> _countryRegion = new();
    private static bool _regionMapLoaded;

    private static readonly List<ActiveDisaster> _active = new();     // devam eden afetler
    private static readonly List<DisasterRecord> _history = new();    // geçmiş afet kayıtları
    private static bool _testMode; // --disaster-test: olasılığı artırır

    private static GameManager GM => GameManager.Instance;

    // ===== Yükleme =====

    private static void EnsureLoaded()
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
                GD.PrintErr($"[DisasterSystem] Veri dosyası yok: {DataPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(DataPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Disasters", out var arr)) return;
            foreach (var item in arr.EnumerateArray())
            {
                var def = new DisasterDefinition
                {
                    Id = item.TryGetProperty("Id", out var id) ? id.GetString() : "?",
                    Name = item.TryGetProperty("Name", out var name) ? name.GetString() : "?",
                    Type = item.TryGetProperty("Type", out var type) ? type.GetString() : "Earthquake",
                    Severity = item.TryGetProperty("Severity", out var sev) ? sev.GetInt32() : 1,
                    DurationDays = item.TryGetProperty("DurationDays", out var dur) ? dur.GetInt32() : 7,
                    BaseProbabilityPerDay = item.TryGetProperty("BaseProbabilityPerDay", out var p) ? p.GetDouble() : 0.001,
                    Description = item.TryGetProperty("Description", out var desc) ? desc.GetString() : ""
                };
                if (item.TryGetProperty("Regions", out var regions))
                    def.Regions = regions.EnumerateArray().Select(r => r.GetString()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                if (item.TryGetProperty("RegionSeverityMultiplier", out var mult))
                    foreach (var kv in mult.EnumerateObject())
                        def.RegionSeverityMultiplier[kv.Name] = (float)kv.Value.GetDouble();
                if (item.TryGetProperty("Impacts", out var impacts))
                    foreach (var kv in impacts.EnumerateObject())
                        def.Impacts[kv.Name] = kv.Value.GetDouble();
                if (string.IsNullOrEmpty(def.Id)) continue;
                _definitions.Add(def);
            }
            GD.Print($"[DisasterSystem] {_definitions.Count} afet tanımı yüklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[DisasterSystem] Tanım yükleme HATA: {e.Message}");
        }
    }

    // Ülke→bölge haritası: aktif senaryonun library/countries.json'dan (Region alanı).
    private static void EnsureRegionMap()
    {
        if (_regionMapLoaded) return;
        _regionMapLoaded = true;
        string path = System.IO.Path.Combine(ScenarioSystem.ActiveLibraryDir, "countries.json").Replace("\\", "/");
        try
        {
            if (!Godot.FileAccess.FileExists(path))
            {
                GD.PrintErr($"[DisasterSystem] Ülke kütüphanesi yok: {path}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(path);
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : "";
                string region = item.TryGetProperty("Region", out var regProp) ? regProp.GetString() : "";
                if (!string.IsNullOrEmpty(id)) _countryRegion[id] = region;
            }
            GD.Print($"[DisasterSystem] {_countryRegion.Count} ulke-bolge eslestirmesi yuklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[DisasterSystem] Bölge haritası yükleme HATA: {e.Message}");
        }
    }

    // M17.1: Senaryo değişimi/yükleme — tanımlar + bölge haritasını (yeniden) yükle.
    public static void LoadScenarioData()
    {
        EnsureLoaded();
        EnsureRegionMap();
    }

    // M17.1: Senaryo değişince cache'i temizle.
    public static void ResetForScenario()
    {
        _definitions.Clear();
        _definitionsLoaded = false;
        _countryRegion.Clear();
        _regionMapLoaded = false;
        _active.Clear();
        _history.Clear();
        _testMode = false;
    }

    // ===== Başlatma / Sıfırlama (yeni oyun) =====

    public static void ResetForNewGame()
    {
        _active.Clear();
        _history.Clear();
        _testMode = false;
        GD.Print("[DisasterSystem] Yeni oyun için afet durumu sıfırlandı.");
    }

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====

    public static void DailyTick()
    {
        if (GM == null || !GM.HasPlayer) return;
        EnsureLoaded();
        EnsureRegionMap();

        int currentDay = GM.TotalDays;

        // 1) Aktif afetlerin süresini azalt, süresi dolanı kaldır
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var d = _active[i];
            d.DaysLeft--;
            if (d.DaysLeft <= 0)
            {
                GD.Print($"[Disaster] Afet bitti: {d.DisasterId} ({d.CountryId}, başlangıç gün {d.Day})");
                _active.RemoveAt(i);
            }
        }

        // 2) Yeni afet tetikleme — günlük DeterministicRNG zarı
        foreach (var c in CountryDatabase.All())
        {
            if (c == null || c.IsAnnexed) continue;
            string region = _countryRegion.TryGetValue(c.Id, out var r) ? r : "";
            foreach (var def in _definitions)
            {
                if (!IsRegionEligible(def, region)) continue;
                double p = def.BaseProbabilityPerDay;
                if (_testMode) p = Math.Min(1.0, p * 100.0); // test modu: garantili tetikleme
                if (DeterministicRNG.NextDouble() >= p) continue;
                Strike(c.Id, def, region, currentDay);
                break; // bir ülkede günde en fazla bir afet
            }
        }

        // M26.2: İyileşme — aktif her afetin gösterge etkileri DurationDays boyunca
        // doğru oranda toparlanır (günlük 1/DurationDays oranında doğrusal geri dönüş).
        RunRecovery();
    }

    // M26.2: Aktif afetlerin gösterge etkilerini günlük olarak toparla.
    // Her aktif afet, sunduğu shock'ı DurationDays boyunca doğrusal olarak geri alır;
    // afet süresi dolunca etki tamamen toplanmış sayılır.
    private static void RunRecovery()
    {
        foreach (var d in _active)
        {
            var def = _definitions.FirstOrDefault(defx => defx.Id == d.DisasterId);
            if (def == null || def.Impacts == null || def.Impacts.Count == 0) continue;
            int recoveryDays = def.DurationDays;
            if (recoveryDays <= 0) continue;
            double rate = 1.0 / recoveryDays;
            foreach (var kv in def.Impacts)
            {
                string indicator = kv.Key;
                double shock = kv.Value; // orijinal delta (negatif = düşüş)
                double cur = StatisticsSystem.GetValue(d.CountryId, indicator);
                // shock'ın tersi yönde oransal geri dönüş
                double next = cur + (-shock * rate);
                StatisticsSystem.SetValue(d.CountryId, indicator, next);
                GD.Print($"[Disaster] İyileşme: {d.DisasterId} ({d.CountryId}) {indicator} {cur:+#;-#} → {next:+#;-#} (kalan gün {d.DaysLeft}/{recoveryDays})");
            }
        }
    }

    private static bool IsRegionEligible(DisasterDefinition def, string region)
    {
        if (def.Regions == null || def.Regions.Length == 0) return true;
        return def.Regions.Contains(region);
    }

    private static void Strike(string countryId, DisasterDefinition def, string region, int day)
    {
        float mult = def.RegionSeverityMultiplier.TryGetValue(region, out var m) ? m : 1.0f;
        int severity = Mathf.Clamp(Mathf.CeilToInt(def.Severity * mult), 1, 5);

        var d = new ActiveDisaster
        {
            DisasterId = def.Id,
            CountryId = countryId,
            Day = day,
            DaysLeft = def.DurationDays,
            Severity = severity,
            Region = region
        };
        _active.Add(d);
        _history.Add(new DisasterRecord
        {
            CountryId = countryId,
            DisasterId = def.Id,
            Day = day,
            Severity = severity,
            Region = region
        });

        // Göstergelere tek seferlik şok uygula (M26.2'de iyileşme gelecek)
        foreach (var kv in def.Impacts)
        {
            double cur = StatisticsSystem.GetValue(countryId, kv.Key);
            StatisticsSystem.SetValue(countryId, kv.Key, cur + kv.Value);
        }

        string impacts = string.Join(",", def.Impacts.Select(kv => $"{kv.Key}={kv.Value:+#;-#}"));
        GD.Print($"[Disaster] AFET: {def.Name} ({def.Type}) — ülke={countryId} bölge={region} şiddet={severity} " +
                 $"süre={def.DurationDays}g etkiler=[{impacts}]");
    }

    // ===== M12: Save/Load =====

    public static DisasterSave GetSaveData()
    {
        EnsureLoaded();
        return new DisasterSave
        {
            Active = _active.Select(d => new ActiveDisasterSave
            {
                DisasterId = d.DisasterId,
                CountryId = d.CountryId,
                Day = d.Day,
                DaysLeft = d.DaysLeft,
                Severity = d.Severity,
                Region = d.Region
            }).ToArray(),
            History = _history.Select(h => new DisasterRecordSave
            {
                CountryId = h.CountryId,
                DisasterId = h.DisasterId,
                Day = h.Day,
                Severity = h.Severity,
                Region = h.Region
            }).ToArray()
        };
    }

    public static void LoadSaveData(DisasterSave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _active.Clear();
        _history.Clear();
        if (data.Active != null)
        {
            foreach (var d in data.Active)
                _active.Add(new ActiveDisaster
                {
                    DisasterId = d.DisasterId,
                    CountryId = d.CountryId,
                    Day = d.Day,
                    DaysLeft = d.DaysLeft,
                    Severity = d.Severity,
                    Region = d.Region
                });
        }
        if (data.History != null)
        {
            foreach (var h in data.History)
                _history.Add(new DisasterRecord
                {
                    CountryId = h.CountryId,
                    DisasterId = h.DisasterId,
                    Day = h.Day,
                    Severity = h.Severity,
                    Region = h.Region
                });
        }
        GD.Print($"[DisasterSystem] {_active.Count} aktif + {_history.Count} kayıtlı afet yüklendi.");
    }

    // ===== Public API (UI / Test) =====

    public static IReadOnlyList<DisasterDefinition> Definitions => _definitions;
    public static IReadOnlyList<ActiveDisaster> ActiveDisasters => _active;
    public static IReadOnlyList<DisasterRecord> DisasterHistory => _history;

    public static void EnableTestMode() => _testMode = true;
}
