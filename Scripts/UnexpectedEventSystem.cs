using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// M26.4 + M26.5: Beklenmedik olay (unexpected event) sistemi — finansal kriz, terör, siber saldırı,
// enerji krizi, sanayi kazası, cinayet girişi, sızdırma skandalı.
// - Data/unexpected_events.json'dan olay tanımlarını yükler (günlük tetikleme olasılığı, süresi,
//   göstergi etkileri, memnuniyet etkileri, istikrar ve savaş desteği etkisi).
// - Günlük tick'te her (annex değil) ülke için DeterministicRNG ile tetikleme zarı atar;
//   olasılıktan geçen olay strikes yapar ve göstergelere tek seferlik şok uygular,
//   CountryData.StabilityPercent / WarSupportPercent'i değiştirir ve memnuniyet gruplarını etkiler.
// - Aktif olaylar günlük olarak süresi azaltılır; süresi dolanı kaldırılır.
// M26.5 ekleri:
// - Bayrak (flag) takibi: olaylar ateşlendiğinde uzun vadeli bayrak ayarlayabilir; olay tanımları
//   bir bayrağı gerektirebilir (RequiresFlag) → önceki olayların sonuçları yeni olayları koşullu hale getirir.
// - Olay zincirleri: bir olay belirli bir gecikmeyle bir takip olayını planlar (Chain).
// - Haftalık karar yoğunluğu hedefi: oyuncu ülkesinde son 7 günde olay yoksa ve bu hafta henüz
//   önemli karar yoksa, haftaya en az bir önemli olay garanti edilir (deterministik, ağırlıklı seçimi).
// - Kararlar/hafta her hafta sınırda loglanır.
// - Save/Load: SaveSystem.SaveFile.UnexpectedEvents + SaveLoadTest özeti (bayraklar, bekleyen zincirler,
//   haftalık sayaçlar dahil).
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
        public string Flag = ""; // olay ateşlendiğinde ayarlanan uzun vadeli bayrak
        public string RequiresFlag = ""; // bu bayrak etkin olduğunda sadece ateşlenebilir
        public bool Significant = true; // haftalık karar hedefine sayılır
        public ChainEvent Chain = null; // takip olayı (zincir)
        public Dictionary<string, double> IndicatorEffects = new(); // göstergeId → delta (tek seferlik şok)
        public Dictionary<string, double> SatisfactionImpacts = new(); // grupId → delta
        public double StabilityImpact = 0; // CountryData.StabilityPercent'e eklenen delta
        public double WarSupportImpact = 0; // CountryData.WarSupportPercent'e eklenen delta
        public string Description = "";
    }

    public class ChainEvent
    {
        public string EventId = ""; // takip olarak ateşlenecek olay ID'si
        public int DelayDays = 0; // kaçıngün sonra ateşlensin
        public string Flag = ""; // zincir olay ateşlendiğinde ek bayrak
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

    // M26.5: planlanmış takip (zincir) olayı — çalışma zamanında
    public class PendingChain
    {
        public string EventId = "";
        public string CountryId = "";
        public int DueDay = 0;
        public string Flag = "";
    }

    // DTO (Save/Load)
    public class UnexpectedEventSave
    {
        public ActiveUnexpectedEventSave[] Active { get; set; }
        public UnexpectedEventRecordSave[] History { get; set; }
        public FlagSave[] Flags { get; set; } // uzun vadeli sonuç bayrakları
        public PendingChainSave[] PendingChains { get; set; } // planlanmış takip olayları
        public int EventsThisWeek { get; set; }
        public int WeekIndex { get; set; }
        public int DaysSinceLastPlayerEvent { get; set; }
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
    public class FlagSave
    {
        public string Id = "";
        public int Day = 0;
    }
    public class PendingChainSave
    {
        public string EventId = "";
        public string CountryId = "";
        public int DueDay = 0;
        public string Flag = "";
    }

    private static readonly string DataPath = "res://Data/unexpected_events.json";
    private static readonly List<UnexpectedEventDefinition> _definitions = new();
    private static bool _definitionsLoaded;

    private static readonly List<ActiveUnexpectedEvent> _active = new();
    private static readonly List<UnexpectedEventRecord> _history = new();
    private static bool _testMode; // --unexpected-events-test: olasılığı artırır

    // M26.5: baytaklar, zincirler, haftalık sayaç
    private static readonly Dictionary<string, int> _flags = new(); // flagId → gün
    private static readonly List<PendingChain> _pendingChains = new(); // planlanmış takip olayları
    private static int _eventsThisWeek; // bu hafta oyuncu ülkesinde çıkan önemli karar sayısı
    private static int _weekIndex = -1; // currentDay / 7; -1 = henüz ilk tick yok
    private static int _daysSinceLastPlayerEvent; // oyuncu ülkesinde son olaydan geçen gün

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
                    Flag = item.TryGetProperty("Flag", out var f) ? f.GetString() : "",
                    RequiresFlag = item.TryGetProperty("RequiresFlag", out var rf) ? rf.GetString() : "",
                    Significant = item.TryGetProperty("Significant", out var sig) && sig.ValueKind == JsonValueKind.True,
                    Description = item.TryGetProperty("Description", out var desc) ? desc.GetString() : ""
                };
                if (item.TryGetProperty("Chain", out var chain))
                {
                    def.Chain = new ChainEvent
                    {
                        EventId = chain.TryGetProperty("EventId", out var ceid) ? ceid.GetString() : "",
                        DelayDays = chain.TryGetProperty("DelayDays", out var ced) ? ced.GetInt32() : 0,
                        Flag = chain.TryGetProperty("ChainFlag", out var cef) ? cef.GetString() : ""
                    };
                }
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
        _flags.Clear();
        _pendingChains.Clear();
        _eventsThisWeek = 0;
        _weekIndex = -1;
        _daysSinceLastPlayerEvent = 0;
        _testMode = false;
    }

    public static void ResetForNewGame()
    {
        _active.Clear();
        _history.Clear();
        _flags.Clear();
        _pendingChains.Clear();
        _eventsThisWeek = 0;
        _weekIndex = -1;
        _daysSinceLastPlayerEvent = 0;
        _testMode = false;
        GD.Print("[UnexpectedEventSystem] Yeni oyun için beklenmedik olay durumu sıfırlandı.");
    }

    public static void EnableTestMode() => _testMode = true;

    // ===== Baytak API (M26.5) =====

    public static bool HasFlag(string flagId) => !string.IsNullOrEmpty(flagId) && _flags.TryGetValue(flagId, out _);

    public static int GetFlag(string flagId) => _flags.TryGetValue(flagId, out var day) ? day : -1;

    public static void SetFlag(string flagId, int day)
    {
        if (string.IsNullOrEmpty(flagId)) return;
        _flags[flagId] = day;
    }

    public static void ClearFlag(string flagId)
    {
        if (string.IsNullOrEmpty(flagId)) return;
        _flags.Remove(flagId);
    }

    public static IReadOnlyDictionary<string, int> Flags => _flags;

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====

    public static void DailyTick()
    {
        if (GM == null || !GM.HasPlayer) return;
        EnsureLoaded();

        int currentDay = GM.TotalDays;
        string playerId = GM.PlayerCountryId;

        // Hafta sınırı: önceki haftanın karar sayısını logla, sayacı sıfırla
        int weekIndex = currentDay / 7;
        if (weekIndex != _weekIndex)
        {
            GD.Print($"[UnexpectedEvent] Haftalık karar sayısı: {_eventsThisWeek} önemli karar (hafta {_weekIndex}, başlangıç gün {_weekIndex * 7})");
            _weekIndex = weekIndex;
            _eventsThisWeek = 0;
        }

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

        // 2) Zincir (takip) olayları: gecikmesi dolanları ateşle
        for (int i = _pendingChains.Count - 1; i >= 0; i--)
        {
            var c = _pendingChains[i];
            if (c.DueDay <= currentDay)
            {
                var def = _definitions.FirstOrDefault(d => d.Id == c.EventId);
                if (def != null)
                {
                    Strike(c.CountryId, def, currentDay);
                    if (!string.IsNullOrEmpty(c.Flag)) SetFlag(c.Flag, currentDay);
                }
                _pendingChains.RemoveAt(i);
            }
        }

        // 3) Yeni olay tetikleme — günlük DeterministicRNG zarı
        foreach (var c in CountryDatabase.All())
        {
            if (c == null || c.IsAnnexed) continue;
            bool isPlayer = c.Id == playerId;
            bool playerGotEvent = false;
            foreach (var def in _definitions)
            {
                if (!string.IsNullOrEmpty(def.RequiresFlag) && !HasFlag(def.RequiresFlag)) continue;
                double p = def.BaseProbabilityPerDay;
                if (_testMode) p = Math.Min(1.0, p * 100.0); // test modu: olasılığı artırır
                if (DeterministicRNG.NextDouble() >= p) continue;
                Strike(c.Id, def, currentDay);
                if (isPlayer && def.Significant)
                {
                    _daysSinceLastPlayerEvent = 0;
                    _eventsThisWeek++;
                    playerGotEvent = true;
                }
                break; // bir ülkede günde en fazla bir olay
            }
            if (isPlayer && !playerGotEvent)
                _daysSinceLastPlayerEvent++;
        }

        // 4) Haftalık garanti: oyuncu ülkesinde 7 gündür olay yoksa ve bu hafta önemli karar yoksa zorla
        if (playerId != null && _daysSinceLastPlayerEvent >= 7 && _eventsThisWeek == 0)
        {
            var candidates = _definitions.Where(d =>
                d.Significant &&
                (string.IsNullOrEmpty(d.RequiresFlag) || HasFlag(d.RequiresFlag))).ToList();
            if (candidates.Count > 0)
            {
                double total = candidates.Sum(d => Math.Max(d.BaseProbabilityPerDay, 1e-6));
                double r = DeterministicRNG.NextDouble() * total;
                double acc = 0;
                foreach (var def in candidates)
                {
                    acc += Math.Max(def.BaseProbabilityPerDay, 1e-6);
                    if (r <= acc)
                    {
                        Strike(playerId, def, currentDay);
                        _daysSinceLastPlayerEvent = 0;
                        _eventsThisWeek++;
                        GD.Print($"[UnexpectedEvent] Haftalık garanti tetiklendi: {def.Name} ({def.Id}) — ülke={playerId}");
                        break;
                    }
                }
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

        // Bayrak: uzun vadeli sonuç
        if (!string.IsNullOrEmpty(def.Flag)) SetFlag(def.Flag, day);

        // Zincir: belirli gecikmeden sonra takip olayı planla
        if (def.Chain != null && !string.IsNullOrEmpty(def.Chain.EventId))
            _pendingChains.Add(new PendingChain
            {
                EventId = def.Chain.EventId,
                CountryId = countryId,
                DueDay = day + def.Chain.DelayDays,
                Flag = def.Chain.Flag
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
            }).ToArray(),
            Flags = _flags.Select(kv => new FlagSave { Id = kv.Key, Day = kv.Value }).ToArray(),
            PendingChains = _pendingChains.Select(c => new PendingChainSave
            {
                EventId = c.EventId,
                CountryId = c.CountryId,
                DueDay = c.DueDay,
                Flag = c.Flag
            }).ToArray(),
            EventsThisWeek = _eventsThisWeek,
            WeekIndex = _weekIndex,
            DaysSinceLastPlayerEvent = _daysSinceLastPlayerEvent
        };
    }

    public static void LoadSaveData(UnexpectedEventSave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _active.Clear();
        _history.Clear();
        _flags.Clear();
        _pendingChains.Clear();
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
        if (data.Flags != null)
        {
            foreach (var f in data.Flags)
                if (!string.IsNullOrEmpty(f.Id)) _flags[f.Id] = f.Day;
        }
        if (data.PendingChains != null)
        {
            foreach (var c in data.PendingChains)
                _pendingChains.Add(new PendingChain
                {
                    EventId = c.EventId,
                    CountryId = c.CountryId,
                    DueDay = c.DueDay,
                    Flag = c.Flag
                });
        }
        _eventsThisWeek = data.EventsThisWeek;
        _weekIndex = data.WeekIndex;
        // Eski kayıtlarda alan yoktur; DTO varsayılanı 0 kabul edilir.
        _daysSinceLastPlayerEvent = data.DaysSinceLastPlayerEvent;
        GD.Print($"[UnexpectedEventSystem] {_active.Count} aktif + {_history.Count} kayıtlı olay + {_flags.Count} bayrak + {_pendingChains.Count} bekleyen zincir yüklendi.");
    }

    // ===== Public API (UI / Test) =====

    public static IReadOnlyList<UnexpectedEventDefinition> Definitions => _definitions;
    public static IReadOnlyList<ActiveUnexpectedEvent> ActiveUnexpectedEvents => _active;
    public static IReadOnlyList<UnexpectedEventRecord> UnexpectedEventHistory => _history;
}
