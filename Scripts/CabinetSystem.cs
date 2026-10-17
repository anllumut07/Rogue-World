using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Kabine (bakanlık) sistemi (M21).
// - Data/cabinet_ministries.json'dan bakanlık tanımları yüklenir.
// - Her ülke için bakanlık Id -> atanan bakan Id eşleştirmesi tutulur (boş = atanmamış).
// - M21.2: kurgusal bakan havuzu; M21.3: atama/görevden alma + gösterge etkileri.
// - Save/Load: SaveSystem.SaveFile.Cabinet. Deterministik (yeni RNG yok).
public static class CabinetSystem
{
    public class Ministry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class Minister
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CountryId { get; set; }
        public string[] MinistryIds { get; set; } // yetkili olduğu bakanlıklar
        public int Skill { get; set; }      // yetenek 0-100
        public int Honesty { get; set; }    // dürüstlük 0-100
        public int Loyalty { get; set; }    // sadakat 0-100
        public int Popularity { get; set; } // popülerlik 0-100
        public string Party { get; set; }
        public string Ideology { get; set; }
        public List<string> Traits { get; set; } // reformcu/şahin/teknokrat...
    }

    public class CabinetSave
    {
        public CountryCabinetSave[] Countries { get; set; }
    }
    public class CountryCabinetSave
    {
        public string CountryId { get; set; }
        public Dictionary<string, string> Assignments { get; set; } // ministryId -> ministerId
        public CabinetScandalSave[] Scandals { get; set; } // M21.4: aktif skandal/uyarı kayıtları (ministerId -> ...)
    }
    public class CabinetScandalSave
    {
        public string MinisterId { get; set; }
        public string Type { get; set; }      // corruption / disloyalty / unpopularity / warning
        public int Severity { get; set; }     // 1-3
        public int DayStarted { get; set; }
    }

    private static readonly string MinistriesPath = "res://Data/cabinet_ministries.json";
    private static readonly string MinistersPath = "res://Data/cabinet_ministers.json";
    private static readonly List<Ministry> _ministries = new();
    private static readonly List<Minister> _ministers = new();

    // countryId -> ministryId -> ministerId
    private static readonly Dictionary<string, Dictionary<string, string>> _assignments = new();

    // M21.4: ministerId -> aktif skandal/uyarı kaydı
    public class ScandalRecord
    {
        public string MinisterId;
        public string Type;      // corruption / disloyalty / unpopularity / warning
        public int Severity;     // 1-3
        public int DayStarted;
        public bool IsActive;
    }
    private static readonly Dictionary<string, ScandalRecord> _scandals = new();

    public static IReadOnlyList<Ministry> Ministries => _ministries;
    public static IReadOnlyList<Minister> Ministers => _ministers;

    static CabinetSystem()
    {
        LoadData();
    }

    private static void LoadData()
    {
        _ministries.Clear();
        _ministers.Clear();
        try
        {
            if (!Godot.FileAccess.FileExists(MinistriesPath))
            {
                GD.PrintErr($"[CabinetSystem] Veri dosyası yok: {MinistriesPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(MinistriesPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Ministries", out var arr))
                foreach (var item in arr.EnumerateArray())
                    _ministries.Add(new Ministry
                    {
                        Id = item.GetProperty("Id").GetString(),
                        Name = item.TryGetProperty("Name", out var n) ? n.GetString() : item.GetProperty("Id").GetString(),
                        Description = item.TryGetProperty("Description", out var d) ? d.GetString() : ""
                    });
            GD.Print($"[CabinetSystem] {_ministries.Count} bakanlık tanımı yüklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[CabinetSystem] Bakanlık veri yükleme HATA: {e.Message}");
        }

        LoadMinisters();
    }

    private static void LoadMinisters()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(MinistersPath))
            {
                GD.PrintErr($"[CabinetSystem] Bakan havuzu dosyası yok: {MinistersPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(MinistersPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Ministers", out var arr)) return;
            foreach (var item in arr.EnumerateArray())
            {
                var m = new Minister
                {
                    Id = item.GetProperty("Id").GetString(),
                    Name = item.TryGetProperty("Name", out var n) ? n.GetString() : item.GetProperty("Id").GetString(),
                    CountryId = item.TryGetProperty("CountryId", out var c) ? c.GetString() : "",
                    Skill = item.TryGetProperty("Skill", out var s) ? s.GetInt32() : 50,
                    Honesty = item.TryGetProperty("Honesty", out var h) ? h.GetInt32() : 50,
                    Loyalty = item.TryGetProperty("Loyalty", out var l) ? l.GetInt32() : 50,
                    Popularity = item.TryGetProperty("Popularity", out var p) ? p.GetInt32() : 50,
                    Party = item.TryGetProperty("Party", out var pt) ? pt.GetString() : "",
                    Ideology = item.TryGetProperty("Ideology", out var id) ? id.GetString() : "",
                    Traits = new List<string>()
                };
                if (item.TryGetProperty("MinistryIds", out var midArr))
                {
                    var ministryIds = new List<string>();
                    foreach (var mid in midArr.EnumerateArray())
                        ministryIds.Add(mid.GetString());
                    m.MinistryIds = ministryIds.ToArray();
                }
                if (item.TryGetProperty("Traits", out var tArr))
                    foreach (var t in tArr.EnumerateArray())
                        m.Traits.Add(t.GetString());
                _ministers.Add(m);
            }
            GD.Print($"[CabinetSystem] {_ministers.Count} kurgusal bakan havuzu yüklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[CabinetSystem] Bakan havuzu yükleme HATA: {e.Message}");
        }
    }

    public static void InitializeAll()
    {
        LoadData();
        _assignments.Clear();
        _scandals.Clear();
        foreach (var c in CountryDatabase.All())
            _assignments[c.Id] = new Dictionary<string, string>();
        GD.Print($"[CabinetSystem] {_assignments.Count} ülke kabinesi başlatıldı (bakan atanmamış).");
    }

    public static void ResetForNewGame() => InitializeAll();

    public static string GetMinister(string countryId, string ministryId)
    {
        if (!_assignments.TryGetValue(countryId, out var a)) return null;
        return a.TryGetValue(ministryId, out var m) ? m : null;
    }

    public static bool IsAssigned(string countryId, string ministryId) => GetMinister(countryId, ministryId) != null;

    public static void AssignMinister(string countryId, string ministryId, string ministerId)
    {
        if (!_assignments.TryGetValue(countryId, out var a))
        {
            a = new Dictionary<string, string>();
            _assignments[countryId] = a;
        }
        if (string.IsNullOrEmpty(ministerId))
        {
            a.Remove(ministryId);
            return;
        }
        a[ministryId] = ministerId;
    }

    public static int AssignmentCount()
    {
        return _assignments.Sum(kv => kv.Value.Count);
    }

    // Tüm bakanlar (belirli bir ülkeye ait)
    public static IReadOnlyList<Minister> GetMinisters(string countryId)
    {
        return _ministers.Where(m => m.CountryId == countryId).ToList().AsReadOnly();
    }

    // Belirli bir bakanlık için uygun, henüz atanmamış bakan adayları (countryId + ministryId'ye göre)
    public static IReadOnlyList<Minister> GetAvailableMinisters(string countryId, string ministryId)
    {
        var assignedIds = new HashSet<string>();
        if (_assignments.TryGetValue(countryId, out var a))
            foreach (var mv in a.Values)
                if (!string.IsNullOrEmpty(mv)) assignedIds.Add(mv);

        return _ministers
            .Where(m => m.CountryId == countryId
                     && (m.MinistryIds ?? Array.Empty<string>()).Contains(ministryId)
                     && !assignedIds.Contains(m.Id))
            .OrderByDescending(m => m.Skill)
            .ToList()
            .AsReadOnly();
    }

    // M21.3: atanan bakanın nitelikleri ilgili göstergeleri etkiler (günlük, deterministik).
    // Etki büyüklüğü: (stat - 50) / 5000 → %50 istatistikte etkisiz, %100'de küçük pozitif/negatif değişim.
    // LowerWhenHigh = true ise stat arttıkça göstergenin değeri düşer (ör. dürüstlik → yolsuzluk↓).
    private static readonly (string MinistryId, string IndicatorId, string Stat, bool LowerWhenHigh)[]
        _ministerEffects =
    {
        ("interior", "corruption", "Honesty", true),
        ("interior", "crime_rate", "Honesty", true),
        ("interior", "crime_rate", "Skill", true),
        ("finance", "budget_deficit", "Skill", true),
        ("finance", "gdp_growth", "Skill", false),
        ("finance", "inflation", "Skill", true),
        ("justice", "judicial_independence", "Honesty", false),
        ("justice", "corruption", "Honesty", true),
        ("education", "education", "Skill", false),
        ("health", "health", "Skill", false),
        ("health", "life_expectancy", "Skill", false),
        ("economy", "gdp_growth", "Skill", false),
        ("economy", "inflation", "Skill", true),
        ("economy", "unemployment", "Skill", true),
        ("energy", "energy", "Skill", false),
        ("environment", "environment", "Skill", false),
    };

    // Atanan bakanların niteliklerini ilgili göstergelere yansıtır (yalnızca belirtilen ülke).
    public static void ApplyMinisterEffects(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return;
        if (!_assignments.TryGetValue(countryId, out var a)) return;

        int applied = 0;
        foreach (var kv in a)
        {
            string ministryId = kv.Key;
            string ministerId = kv.Value;
            if (string.IsNullOrEmpty(ministerId)) continue;
            var minister = _ministers.FirstOrDefault(m => m.Id == ministerId);
            if (minister == null) continue;

            foreach (var eff in _ministerEffects)
            {
                if (eff.MinistryId != ministryId) continue;
                double stat = eff.Stat switch
                {
                    "Skill" => minister.Skill,
                    "Honesty" => minister.Honesty,
                    "Loyalty" => minister.Loyalty,
                    "Popularity" => minister.Popularity,
                    _ => 50
                };
                double delta = (stat - 50.0) / 5000.0;
                if (eff.LowerWhenHigh) delta = -delta;

                string indicatorId = eff.IndicatorId;
                double current = StatisticsSystem.GetValue(countryId, indicatorId);
                StatisticsSystem.SetValue(countryId, indicatorId, current + delta);
                applied++;
            }
        }
        GD.Print($"[CabinetSystem] {countryId} için {applied} bakan-etkisi uygulandı.");
    }

    // M21.4: Günlük kabine denetimi — skandal, istifa, koalisyon baskısı, başarısızlık uyarısı.
    // Deterministik (DeterministicRNG). Etkiler: CountryData.StabilityPercent üzerinden.
    public static void CheckCabinetEvents(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return;
        if (!_assignments.TryGetValue(countryId, out var a)) return;

        // Koalisyon referansı: ilk atanan bakanın partisi = "lider partisi"
        string leadingParty = "";
        foreach (var kv in a)
        {
            if (string.IsNullOrEmpty(kv.Value)) continue;
            var m = _ministers.FirstOrDefault(x => x.Id == kv.Value);
            if (m != null) { leadingParty = m.Party; break; }
        }

        var data = CountryDatabase.Get(countryId);
        if (data == null) return;

        int warnings = 0, scandals = 0, pressures = 0, resignations = 0;

        // Atanmış bakanların anlık kopya (istifa sırasında sözlük değişir)
        var snapshot = a.ToList();
        foreach (var kv in snapshot)
        {
            string ministryId = kv.Key;
            string ministerId = kv.Value;
            if (string.IsNullOrEmpty(ministerId)) continue;
            var minister = _ministers.FirstOrDefault(m => m.Id == ministerId);
            if (minister == null) continue;

            // ---- 1) Başarısızlık uyarısı (Skill < 30) — her bakan için bir kez ----
            if (minister.Skill < 30)
            {
                if (!_scandals.TryGetValue(ministerId, out var w) || !w.IsActive || w.Type != "warning")
                {
                    data.StabilityPercent = Math.Clamp(data.StabilityPercent - 1.0, 0, 100);
                    _scandals[ministerId] = new ScandalRecord
                    {
                        MinisterId = ministerId, Type = "warning", Severity = 1,
                        DayStarted = GameManager.Instance.TotalDays, IsActive = true
                    };
                    GD.Print($"[CabinetSystem] UYARI: {minister.Name} ({minister.Party}) başarısızlık — Skill {minister.Skill} < 30, stabilite -1");
                    warnings++;
                }
            }

            // ---- 2) Skandal tetikleme (yalnızca zaten skandalı yoksa) ----
            bool inScandal = _scandals.TryGetValue(ministerId, out var ex) && ex.IsActive && ex.Type != "warning";
            if (!inScandal)
            {
                double chance = 0;
                string type = "";
                if (minister.Honesty < 25) { chance = 0.03; type = "corruption"; }
                else if (minister.Loyalty < 25) { chance = 0.03; type = "disloyalty"; }
                else if (minister.Popularity < 15) { chance = 0.02; type = "unpopularity"; }

                if (chance > 0 && DeterministicRNG.NextDouble() < chance)
                {
                    int severity = 1;
                    if (minister.Skill < 30) severity++;
                    if (chance >= 0.03) severity++;
                    severity = Math.Clamp(severity, 1, 3);

                    _scandals[ministerId] = new ScandalRecord
                    {
                        MinisterId = ministerId, Type = type, Severity = severity,
                        DayStarted = GameManager.Instance.TotalDays, IsActive = true
                    };
                    data.StabilityPercent = Math.Clamp(data.StabilityPercent - 3.0, 0, 100);
                    GD.Print($"[CabinetSystem] SKANDAL: {minister.Name} ({minister.Party}) — {type}, şiddet {severity}, stabilite -3");
                    scandals++;
                }
            }

            // ---- 3) Aktif skandal: koalisyon baskısı + istifa ----
            if (_scandals.TryGetValue(ministerId, out var rec) && rec.IsActive && rec.Type != "warning")
            {
                // Koalisyon baskısı: farklı partili bakan + skandal
                if (!string.IsNullOrEmpty(leadingParty) && minister.Party != leadingParty)
                {
                    if (DeterministicRNG.NextDouble() < 0.05)
                    {
                        data.StabilityPercent = Math.Clamp(data.StabilityPercent - 1.0, 0, 100);
                        GD.Print($"[CabinetSystem] KOALİSYON BASKISI: {minister.Party} partisi, {minister.Name} ({rec.Type}) skandalında istifa istiyor, stabilite -1");
                        pressures++;
                    }
                }

                // İstifa: şiddete bağlı günlük şans (sev1: %2, sev2: %5, sev3: %10)
                double resignChance = rec.Severity switch { 1 => 0.02, 2 => 0.05, _ => 0.10 };
                if (DeterministicRNG.NextDouble() < resignChance)
                {
                    AssignMinister(countryId, ministryId, ""); // görevden al / istifa
                    _scandals.Remove(ministerId);
                    data.StabilityPercent = Math.Clamp(data.StabilityPercent - 5.0, 0, 100);
                    GD.Print($"[CabinetSystem] İSTİFA: {minister.Name} ({minister.Party}) — {rec.Type} skandalından kaynaklanan istifa, stabilite -5");
                    resignations++;
                }
            }
        }

        GD.Print($"[CabinetSystem] {countryId} kabine denetimi: {warnings} uyarı, {scandals} skandal, {pressures} koalisyon baskısı, {resignations} istifa");
    }

    // Ministerin mevcut skandal/uyarı durumunu gösteren kısa etiket (UI için).
    public static string GetScandalStatus(string ministerId)
    {
        if (string.IsNullOrEmpty(ministerId)) return "";
        if (_scandals.TryGetValue(ministerId, out var rec) && rec.IsActive)
        {
            return rec.Type switch
            {
                "warning" => "[UYARI: yetmezlik]",
                "corruption" => $"[SKANDAL: yolsuzluk, şiddet {rec.Severity}]",
                "disloyalty" => $"[SKANDAL: hıyar ve isyansızlık, şiddet {rec.Severity}]",
                "unpopularity" => $"[SKANDAL: çeyrekçi / düşük popülerlik, şiddet {rec.Severity}]",
                _ => $"[SKANDAL: {rec.Type}, şiddet {rec.Severity}]"
            };
        }
        return "";
    }

    public static CabinetSave GetSaveData() => new CabinetSave
    {
        Countries = _assignments.Select(kv => new CountryCabinetSave
        {
            CountryId = kv.Key,
            Assignments = kv.Value.ToDictionary(p => p.Key, p => p.Value),
            Scandals = _scandals.Values
                .Where(s => s.IsActive)
                .Join(_ministers, s => s.MinisterId, m => m.Id, (s, m) => new { s, m })
                .Where(x => x.m.CountryId == kv.Key)
                .Select(x => new CabinetScandalSave
                {
                    MinisterId = x.s.MinisterId,
                    Type = x.s.Type,
                    Severity = x.s.Severity,
                    DayStarted = x.s.DayStarted
                })
                .ToArray()
        }).ToArray()
    };

    public static void LoadSaveData(CabinetSave data)
    {
        if (data == null) return;
        _assignments.Clear();
        _scandals.Clear();
        if (data.Countries != null)
        {
            foreach (var c in data.Countries)
            {
                if (string.IsNullOrEmpty(c.CountryId)) continue;
                _assignments[c.CountryId] = c.Assignments?.ToDictionary(p => p.Key, p => p.Value)
                    ?? new Dictionary<string, string>();
                if (c.Scandals != null)
                {
                    foreach (var s in c.Scandals)
                    {
                        if (string.IsNullOrEmpty(s.MinisterId)) continue;
                        _scandals[s.MinisterId] = new ScandalRecord
                        {
                            MinisterId = s.MinisterId,
                            Type = s.Type,
                            Severity = s.Severity,
                            DayStarted = s.DayStarted,
                            IsActive = true
                        };
                    }
                }
            }
        }
        GD.Print($"[CabinetSystem] {_assignments.Count} ülke kabinesi, {_scandals.Count(s => s.Value.IsActive)} aktif skandal/uyarı yüklendi.");
    }
}
