using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// M26.4: Beklenmedik olay (unexpected event) sistemi — finansal kriz, terör, siber saldırı,
// enerji krizi, sanayi kazası, cinayet girişi, sızdırma skandalı.
// - Data/unexpected_events.json'dan olay tanımlarını yükler (günlük tetikleme olasılığı, süresi,
//   göstergi etkileri, memnuniyet etkileri, istikrar ve savaş desteği etkisi).
// - Günlük tick'te her (annex değil) ülke için DeterministicRNG ile tetikleme zarı atar;
//   olasılıktan geçen olay strikes yapar ve göstergelere tek seferlik şok uygular,
//   CountryData.StabilityPercent / WarSupportPercent'i değiştirir ve memnuniyet gruplarını etkiler.
// - Aktif olaylar günlük olarak süresi azaltılır; süresi dolanı kaldırılır. (Tek seferlik şok,
//   geri kazanım yok — olayın etkisi gösterge geçmişinde kalır.)
// - Save/Load: SaveSystem.SaveFile.UnexpectedEvents + SaveLoadTest özeti.
// - Deterministik: yalnızca DeterministicRNG. new Random() yok.
// - Günlük tick: GameManager.DailyTick'te InterventionSystem.DailyTick() sonra çağrılır.
public static class UnexpectedEventSystem
{
    public class UnexpectedEventDefinition
    {
        public string Id = "?";
        public string Name = "?";
        public string Type = "FinancialCrisis"; // FinancialCrisis, TerrorAttack, CyberAttack, EnergyCrisis, IndustrialAccident, AssassinationAttempt, LeakScandal
        public double BaseProbabilityPerDay = 0.001; // 0-1
        public int DurationDays = 7;
        public Dictionary<string, double> IndicatorEffects = new(); // göstergeId → delta (tek seferlik şok)
        public Dictionary<string, double> SatisfactionImpacts = new(); // grupId → delta
        public double StabilityImpact = 0; // CountryData.StabilityPercent'e eklenen delta
        public double WarSupportImpact = 0; // CountryData.WarSupportPercent'e eklenen delta
        public string Description = "";
    }

    public class ActiveUnexpectedEvent
    {
        public string EventId = "";
        public string CountryId = "";
        public int Day = 0;
        public int DaysLeft = 0;
    }

    public class UnexpectedEventRecord
    {
        public string CountryId = "";
        public string EventId = "";
        public int Day = 0;
    }

    // DTO (Save/Load)
    public class UnexpectedEventSave
    {
        public ActiveUnexpectedEventSave[] Active { get; set; }
        public UnexpectedEventRecordSave[] History { get; set; }
    }
    public class ActiveUnexpectedEventSave
    {
        public string EventId = "";
        public string CountryId = "";
        public int Day = 0;
        public int DaysLeft = 0;
    }
    public class UnexpectedEventRecordSave
    {
        public string CountryId = "";
        public string EventId = "";
        public int Day = 0;
    }

    private static readonly string DataPath = "res://Data/unexpected_events.json";
    private static readonly List<UnexpectedEventDefinition> _definitions = new();
    private static bool _definitionsLoaded;

    private static readonly List<ActiveUnexpectedEvent> _active = new();
    private static readonly List<UnexpectedEventRecord> _history = new();
    private static bool _testMode; // --unexpected-events-test: olasılığı artırır

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
                GD.PrintErr($"[UnexpectedEventSystem] Veri dosyası yok: {DataPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(DataPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("UnexpectedEvents", out var arr)) return;
            foreach (var item in arr.EnumerateArray())
            {
                var def = new UnexpectedEventDefinition
                {
                    Id = item.TryGetProperty("Id", out var id) ? id.GetString() : "?",
                    Name = item.TryGetProperty("Name", out var name) ? name.GetString() : "?",
                    Type = item.TryGetProperty("Type", out var type) ? type.GetString() : "FinancialCrisis",
                    BaseProbabilityPerDay = item.TryGetProperty("BaseProbabilityPerDay", out var p) ? p.GetDouble() : 0.001,
                    DurationDays = item.TryGetProperty("DurationDays", out var d) ? d.GetInt32() : 7,
                    Description = item.TryGetProperty("Description", out var desc) ? desc.GetString() : ""
                };
                if (item.TryGetProperty("IndicatorEffects", out var impacts))
                    foreach (var kv in impacts.EnumerateObject())
                        def.IndicatorEffects[kv.Name] = kv.Value.GetDouble();
                if (item.TryGetProperty("SatisfactionImpacts", out var impacts2))
                    foreach (var kv in impacts2.EnumerateObject())
                        def.SatisfactionImpacts[kv.Name] = kv.Value.GetDouble();
                def.StabilityImpact = item.TryGetProperty("StabilityImpact", out var si) ? si.GetDouble() : 0;
                def.WarSupportImpact = item.TryGetProperty("WarSupportImpact", out var wsi) ? wsi.GetDouble() : 0;
                if (string.IsNullOrEmpty(def.Id)) continue;
                _definitions.Add(def);
            }
            GD.Print($"[UnexpectedEventSystem] {_definitions.Count} beklenmedik olay tanımı yüklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[UnexpectedEventSystem] Tanım yükleme HATA: {e.Message}");
        }
    }

    // ===== Başlatma / Sıfırlama =====

    public static void LoadScenarioData() => EnsureLoaded();

    public static void ResetForScenario()
    {
        _definitions.Clear();
        _definitionsLoaded = false;
        _active.Clear();
        _history.Clear();
        _testMode = false;
    }

    public static void ResetForNewGame()
    {
        _active.Clear();
        _history.Clear();
        _testMode = false;
        GD.Print("[UnexpectedEventSystem] Yeni oyun için beklenmedik olay durumu sıfırlandı.");
    }

    public static void EnableTestMode() => _testMode = true;

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====

    public static void DailyTick()
    {
        if (GM == null || !GM.HasPlayer) return;
        EnsureLoaded();

        int currentDay = GM.TotalDays;

        // 1) Aktif olayların süresini azalt, süresi dolanı kaldır
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var e = _active[i];
            e.DaysLeft--;
            if (e.DaysLeft <= 0)
            {
                GD.Print($"[UnexpectedEvent] Olay bitti: {e.EventId} ({e.CountryId}, başlangıç gün {e.Day})");
                _active.RemoveAt(i);
            }
        }

        // 2) Yeni olay tetikleme — günlük DeterministicRNG zarı
        foreach (var c in CountryDatabase.All())
        {
            if (c == null || c.IsAnnexed) continue;
            foreach (var def in _definitions)
            {
                double p = def.BaseProbabilityPerDay;
                if (_testMode) p = Math.Min(1.0, p * 100.0); // test modu: olasılığı artırır
                if (DeterministicRNG.NextDouble() >= p) continue;
                Strike(c.Id, def, currentDay);
                break; // bir ülkede günde en fazla bir olay
            }
        }
    }

    private static void Strike(string countryId, UnexpectedEventDefinition def, int day)
    {
        var e = new ActiveUnexpectedEvent
        {
            EventId = def.Id,
            CountryId = countryId,
            Day = day,
            DaysLeft = def.DurationDays
        };
        _active.Add(e);
        _history.Add(new UnexpectedEventRecord
        {
            CountryId = countryId,
            EventId = def.Id,
            Day = day
        });

        // Göstergelere tek seferlik şok uygula
        foreach (var kv in def.IndicatorEffects)
        {
            string indicator = kv.Key;
            double cur = StatisticsSystem.GetValue(countryId, indicator);
            StatisticsSystem.SetValue(countryId, indicator, cur + kv.Value);
        }

        // Memnuniyet etkilerini uygula
        foreach (var kv in def.SatisfactionImpacts)
        {
            string groupId = kv.Key;
            double cur = PublicSatisfactionSystem.GetSatisfaction(countryId, groupId);
            PublicSatisfactionSystem.SetSatisfaction(countryId, groupId, cur + kv.Value);
        }

        // Ülke istikrarı ve savaş desteğini güncelle (0-100 arasında tut)
        var country = CountryDatabase.Get(countryId);
        if (country != null)
        {
            country.StabilityPercent = Math.Clamp(country.StabilityPercent + def.StabilityImpact, 0, 100);
            country.WarSupportPercent = Math.Clamp(country.WarSupportPercent + def.WarSupportImpact, 0, 100);
        }

        string impacts = string.Join(",", def.IndicatorEffects.Select(kv => $"{kv.Key}={kv.Value:+#;-#}"));
        string boosts = string.Join(",", def.SatisfactionImpacts.Select(kv => $"{kv.Key}={kv.Value:+#;-#}"));
        GD.Print($"[UnexpectedEvent] OLAY: {def.Name} ({def.Type}) — ülke={countryId} süre={def.DurationDays}g " +
                 $"göstergeler=[{impacts}] memnuniyet=[{boosts}] istikrar={def.StabilityImpact:+#;-#} savaş_desteği={def.WarSupportImpact:+#;-#}");
    }

    // ===== M12: Save/Load =====

    public static UnexpectedEventSave GetSaveData()
    {
        EnsureLoaded();
        return new UnexpectedEventSave
        {
            Active = _active.Select(a => new ActiveUnexpectedEventSave
            {
                EventId = a.EventId,
                CountryId = a.CountryId,
                Day = a.Day,
                DaysLeft = a.DaysLeft
            }).ToArray(),
            History = _history.Select(h => new UnexpectedEventRecordSave
            {
                CountryId = h.CountryId,
                EventId = h.EventId,
                Day = h.Day
            }).ToArray()
        };
    }

    public static void LoadSaveData(UnexpectedEventSave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _active.Clear();
        _history.Clear();
        if (data.Active != null)
        {
            foreach (var a in data.Active)
                _active.Add(new ActiveUnexpectedEvent
                {
                    EventId = a.EventId,
                    CountryId = a.CountryId,
                    Day = a.Day,
                    DaysLeft = a.DaysLeft
                });
        }
        if (data.History != null)
        {
            foreach (var h in data.History)
                _history.Add(new UnexpectedEventRecord
                {
                    CountryId = h.CountryId,
                    EventId = h.EventId,
                    Day = h.Day
                });
        }
        GD.Print($"[UnexpectedEventSystem] {_active.Count} aktif + {_history.Count} kayıtlı olay yüklendi.");
    }

    // ===== Public API (UI / Test) =====

    public static IReadOnlyList<UnexpectedEventDefinition> Definitions => _definitions;
    public static IReadOnlyList<ActiveUnexpectedEvent> ActiveUnexpectedEvents => _active;
    public static IReadOnlyList<UnexpectedEventRecord> UnexpectedEventHistory => _history;
}
