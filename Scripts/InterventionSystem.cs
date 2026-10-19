using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// M26.3: Müdahale (intervention) sistemi — acil yardım, uluslararası yardım, yeniden inşa, tahliye.
// - Data/interventions.json'dan müdahale tanımlarını yükler (PP maliyeti, süre, ilgili bakanlık,
//   yönetim kalitesi göstergesi, gösterge etkileri, memnuniyet artışları).
// - Uygulama: oyuncu ülkesinde PP'yi düşürür, ilgili bakanın yönetim kalitesi stat'ına göre
//   kalite bónusu hesaplar (yoksa 0), gösterge etkilerini (tek seferlik pozitif şok) ve
//   memnuniyet artışlarını uygular. Aktif müdahaleler günlük olarak süreleri azalır ve
//   gösterge etkileri DurationDays boyunca doğrusal olarak expires olur (afet iyileşmesinin tersi).
// - Kalite bónusu: (stat - 50) / 100 → etki çarpanı 0.5 ... 1.5. Yüksek kalite = daha etkili.
// - Memnuniyet artışları PublicSatisfactionSystem'e yansır → seçim sonuçlarını değiştirir (M23.3).
// - Pozitif gösterge etkileri afetlerin negatif şoklarını doğal olarak tela eder (iyileşmeyi hızlandırır).
// - Save/Load: SaveSystem.SaveFile.Interventions + SaveLoadTest özeti.
// - Deterministik: yalnızca DeterministicRNG. new Random() yok.
// - Günlük tick: GameManager.DailyTick'te DisasterSystem.DailyTick() sonra çağrılır.
public static class InterventionSystem
{
    public class InterventionDefinition
    {
        public string Id = "?";
        public string Name = "?";
        public string Type = "EmergencyAid"; // EmergencyAid, InternationalAid, Reconstruction, Evacuation
        public int CostPP = 20;
        public int DurationDays = 3;
        public string RelevantMinistryId = "interior";
        public string ManagementQualityStat = "Skill"; // Skill / Honesty / Loyalty / Popularity
        public string Description = "";
        public Dictionary<string, double> IndicatorEffects = new(); // göstergeId → delta (tek seferlik pozitif şok)
        public Dictionary<string, double> SatisfactionBoosts = new(); // grupId → delta
    }

    public class ActiveIntervention
    {
        public string InterventionId = "";
        public string CountryId = "";
        public int Day = 0;
        public int DaysLeft = 0;
        public double QualityBonus = 0; // (stat - 50) / 100
    }

    public class InterventionRecord
    {
        public string CountryId = "";
        public string InterventionId = "";
        public int Day = 0;
        public double QualityBonus = 0;
    }

    // DTO (Save/Load)
    public class InterventionSave
    {
        public ActiveInterventionSave[] Active { get; set; }
        public InterventionRecordSave[] History { get; set; }
    }
    public class ActiveInterventionSave
    {
        public string InterventionId = "";
        public string CountryId = "";
        public int Day = 0;
        public int DaysLeft = 0;
        public double QualityBonus = 0;
    }
    public class InterventionRecordSave
    {
        public string CountryId = "";
        public string InterventionId = "";
        public int Day = 0;
        public double QualityBonus = 0;
    }

    private static readonly string DataPath = "res://Data/interventions.json";
    private static readonly List<InterventionDefinition> _definitions = new();
    private static bool _definitionsLoaded;

    private static readonly List<ActiveIntervention> _active = new();
    private static readonly List<InterventionRecord> _history = new();

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
                GD.PrintErr($"[InterventionSystem] Veri dosyası yok: {DataPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(DataPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Interventions", out var arr)) return;
            foreach (var item in arr.EnumerateArray())
            {
                var def = new InterventionDefinition
                {
                    Id = item.TryGetProperty("Id", out var id) ? id.GetString() : "?",
                    Name = item.TryGetProperty("Name", out var name) ? name.GetString() : "?",
                    Type = item.TryGetProperty("Type", out var type) ? type.GetString() : "EmergencyAid",
                    CostPP = item.TryGetProperty("CostPP", out var c) ? c.GetInt32() : 20,
                    DurationDays = item.TryGetProperty("DurationDays", out var d) ? d.GetInt32() : 3,
                    RelevantMinistryId = item.TryGetProperty("RelevantMinistryId", out var m) ? m.GetString() : "interior",
                    ManagementQualityStat = item.TryGetProperty("ManagementQualityStat", out var s) ? s.GetString() : "Skill",
                    Description = item.TryGetProperty("Description", out var desc) ? desc.GetString() : ""
                };
                if (item.TryGetProperty("IndicatorEffects", out var impacts))
                    foreach (var kv in impacts.EnumerateObject())
                        def.IndicatorEffects[kv.Name] = kv.Value.GetDouble();
                if (item.TryGetProperty("SatisfactionBoosts", out var boosts))
                    foreach (var kv in boosts.EnumerateObject())
                        def.SatisfactionBoosts[kv.Name] = kv.Value.GetDouble();
                if (string.IsNullOrEmpty(def.Id)) continue;
                _definitions.Add(def);
            }
            GD.Print($"[InterventionSystem] {_definitions.Count} müdahale tanımı yüklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[InterventionSystem] Tanım yükleme HATA: {e.Message}");
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
    }

    public static void ResetForNewGame()
    {
        _active.Clear();
        _history.Clear();
        GD.Print("[InterventionSystem] Yeni oyun için müdahale durumu sıfırlandı.");
    }

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====

    public static void DailyTick()
    {
        if (GM == null || !GM.HasPlayer) return;
        EnsureLoaded();

        int currentDay = GM.TotalDays;

        // 1) Aktif müdahalelerin süresini azalt, süresi dolanı kaldır
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var iv = _active[i];
            iv.DaysLeft--;
            if (iv.DaysLeft <= 0)
            {
                GD.Print($"[Intervention] Müdahale bitti: {iv.InterventionId} ({iv.CountryId}, başlangıç gün {iv.Day})");
                _active.RemoveAt(i);
            }
        }

        // 2) Aktif müdahalelerin gösterge etkilerinin günlük olarak expires olması (fade).
        // Her aktif müdahale, sunduğu pozitif shock'ı DurationDays boyunca doğrusal olarak geri alır;
        // müdahale süresi dolunca etki tamamen geçer (afet iyileşmesinin tam tersi).
        foreach (var iv in _active)
        {
            var def = _definitions.FirstOrDefault(d => d.Id == iv.InterventionId);
            if (def == null || def.IndicatorEffects == null || def.IndicatorEffects.Count == 0) continue;
            int duration = def.DurationDays;
            if (duration <= 0) continue;
            double rate = 1.0 / duration;
            foreach (var kv in def.IndicatorEffects)
            {
                string indicator = kv.Key;
                double shock = kv.Value * (1.0 + iv.QualityBonus); // uygulanmış pozitif etki
                double cur = StatisticsSystem.GetValue(iv.CountryId, indicator);
                double next = cur - (shock * rate); // etkiyi yavaşça geri al
                StatisticsSystem.SetValue(iv.CountryId, indicator, next);
                GD.Print($"[Intervention] Etki azalır: {iv.InterventionId} ({iv.CountryId}) {indicator} {cur:+#;-#} → {next:+#;-#} (kalite={iv.QualityBonus:+0.00;-0.00}, kalan gün {iv.DaysLeft}/{duration})");
            }
        }
    }

    // ===== Public API (UI / Test) =====

    /// Müdahale uygulanabilir mi? (ülke geçerli, annex değil, PP yeterli)
    public static bool CanApply(string countryId, string interventionId)
    {
        EnsureLoaded();
        var def = _definitions.FirstOrDefault(d => d.Id == interventionId);
        if (def == null) return false;
        var country = CountryDatabase.Get(countryId);
        if (country == null || country.IsAnnexed) return false;
        return country.PoliticalPower >= def.CostPP;
    }

    /// Müdahaleyi uygula: PP düşür, kalite bónusu hesapla, gösterge + memnuniyet etkilerini uygula,
    /// aktif müdahale listesine ekle. Başarılıysa true döner.
    public static bool ApplyIntervention(string countryId, string interventionId, int day)
    {
        EnsureLoaded();
        var def = _definitions.FirstOrDefault(d => d.Id == interventionId);
        if (def == null)
        {
            GD.PrintErr($"[InterventionSystem] Müdahale tanımı yok: {interventionId}");
            return false;
        }
        var country = CountryDatabase.Get(countryId);
        if (country == null || country.IsAnnexed)
        {
            GD.PrintErr($"[InterventionSystem] Geçersiz ülke: {countryId}");
            return false;
        }
        if (country.PoliticalPower < def.CostPP)
        {
            GD.PrintErr($"[InterventionSystem] PP yetersiz: {countryId} mevcut={country.PoliticalPower:F1} gerekli={def.CostPP}");
            return false;
        }

        // PP maliyeti
        country.PoliticalPower -= def.CostPP;

        // Yönetim kalitesi bónusu: ilgili bakanın ManagementQualityStat'ına göre (yoksa 0)
        string ministerId = CabinetSystem.GetMinister(countryId, def.RelevantMinistryId);
        double qualityBonus = _qualityBonus(ministerId, def.ManagementQualityStat);
        double effectMult = 1.0 + qualityBonus; // 0.5 ... 1.5

        // Göstergelere tek seferlik pozitif şok uygula (kalite bónulu)
        foreach (var kv in def.IndicatorEffects)
        {
            string indicator = kv.Key;
            double cur = StatisticsSystem.GetValue(countryId, indicator);
            StatisticsSystem.SetValue(countryId, indicator, cur + kv.Value * effectMult);
        }

        // Memnuniyet artışlarını uygula (kalite bónulu)
        foreach (var kv in def.SatisfactionBoosts)
        {
            string groupId = kv.Key;
            double cur = PublicSatisfactionSystem.GetSatisfaction(countryId, groupId);
            PublicSatisfactionSystem.SetSatisfaction(countryId, groupId, cur + kv.Value * effectMult);
        }

        // Aktif müdahaleye ekle
        _active.Add(new ActiveIntervention
        {
            InterventionId = def.Id,
            CountryId = countryId,
            Day = day,
            DaysLeft = def.DurationDays,
            QualityBonus = qualityBonus
        });
        _history.Add(new InterventionRecord
        {
            CountryId = countryId,
            InterventionId = def.Id,
            Day = day,
            QualityBonus = qualityBonus
        });

        string impacts = string.Join(",", def.IndicatorEffects.Select(kv => $"{kv.Key}={kv.Value * effectMult:+#;-#}"));
        string boosts = string.Join(",", def.SatisfactionBoosts.Select(kv => $"{kv.Key}={kv.Value * effectMult:+#;-#}"));
        double statVal = _statValue(ministerId, def.ManagementQualityStat);
        GD.Print($"[Intervention] MÜDHALE: {def.Name} ({def.Type}) — ülke={countryId} bakanlık={def.RelevantMinistryId} stat={def.ManagementQualityStat}={statVal:F0} bónus={qualityBonus:+0.00;-0.00} çarpan={effectMult:F2} PP={def.CostPP} süre={def.DurationDays}g gösterge=[{impacts}] memnuniyet=[{boosts}]");
        return true;
    }

    private static double _qualityBonus(string ministerId, string stat)
    {
        if (string.IsNullOrEmpty(ministerId)) return 0;
        var minister = CabinetSystem.Ministers.FirstOrDefault(m => m.Id == ministerId);
        if (minister == null) return 0;
        double value = stat switch
        {
            "Skill" => minister.Skill,
            "Honesty" => minister.Honesty,
            "Loyalty" => minister.Loyalty,
            "Popularity" => minister.Popularity,
            _ => 50
        };
        return (value - 50.0) / 100.0; // -0.5 ... +0.5
    }

    private static double _statValue(string ministerId, string stat)
    {
        if (string.IsNullOrEmpty(ministerId)) return 50;
        var minister = CabinetSystem.Ministers.FirstOrDefault(m => m.Id == ministerId);
        if (minister == null) return 50;
        return stat switch
        {
            "Skill" => minister.Skill,
            "Honesty" => minister.Honesty,
            "Loyalty" => minister.Loyalty,
            "Popularity" => minister.Popularity,
            _ => 50
        };
    }

    // ===== M12: Save/Load =====

    public static InterventionSave GetSaveData()
    {
        EnsureLoaded();
        return new InterventionSave
        {
            Active = _active.Select(a => new ActiveInterventionSave
            {
                InterventionId = a.InterventionId,
                CountryId = a.CountryId,
                Day = a.Day,
                DaysLeft = a.DaysLeft,
                QualityBonus = a.QualityBonus
            }).ToArray(),
            History = _history.Select(h => new InterventionRecordSave
            {
                CountryId = h.CountryId,
                InterventionId = h.InterventionId,
                Day = h.Day,
                QualityBonus = h.QualityBonus
            }).ToArray()
        };
    }

    public static void LoadSaveData(InterventionSave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _active.Clear();
        _history.Clear();
        if (data.Active != null)
        {
            foreach (var a in data.Active)
                _active.Add(new ActiveIntervention
                {
                    InterventionId = a.InterventionId,
                    CountryId = a.CountryId,
                    Day = a.Day,
                    DaysLeft = a.DaysLeft,
                    QualityBonus = a.QualityBonus
                });
        }
        if (data.History != null)
        {
            foreach (var h in data.History)
                _history.Add(new InterventionRecord
                {
                    CountryId = h.CountryId,
                    InterventionId = h.InterventionId,
                    Day = h.Day,
                    QualityBonus = h.QualityBonus
                });
        }
        GD.Print($"[InterventionSystem] {_active.Count} aktif + {_history.Count} kayıtlı müdahale yüklendi.");
    }

    // ===== Public API (UI / Test) =====

    public static IReadOnlyList<InterventionDefinition> Definitions => _definitions;
    public static IReadOnlyList<ActiveIntervention> ActiveInterventions => _active;
    public static IReadOnlyList<InterventionRecord> InterventionHistory => _history;
}
