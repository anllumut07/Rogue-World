using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Danışman Sistemi (M53).
///
/// Oyuncuya 3 tür danışman sunar: Ekonomi (Maliye yardımcı), Güvenlik (Savunma yardımcı), Medya (Basın yardımcı).
/// Her danışman tavsiye üretir: öneri + gerekçe + güven skoru (0-100).
/// Tüm rastgelelik DeterministicRNG kullanır. State save/load desteklenir.
/// </summary>
public static class AdvisorSystem
{
    public enum AdvisorType
    {
        Economy,   // Ekonomi / Maliye
        Security,  // Güvenlik / Savunma
        Media      // Medya / Basın
    }

    /// <summary>Danışman profili (advisors.json'dan yüklenir).</summary>
    public class AdvisorProfile
    {
        public string Id = "";
        public string Name = "";
        public AdvisorType Type = AdvisorType.Economy;
        public string CultureGroup = ""; // ülke kültür grubu (ör. "western", "eastern", "middle_eastern" vb.)
        public int Loyalty = 50;         // 0-100
        public int Popularity = 50;      // 0-100
        public string Trait = "";        // "reformer" | "hawk" | "technocrat" | "populist" | "diplomat"
        public string[] ExpertiseTags = Array.Empty<string>(); // "tax", "debt", "trade", "army", "navy", "air", "propaganda", "censorship" vb.
    }

    /// <summary>Üretilen tavsiye.</summary>
    public class AdvisorAdvice
    {
        public AdvisorType Type;
        public string AdvisorId;
        public string Recommendation; // Kısa öneri metni (İngilizce UI)
        public string Rationale;      // Gerekçe
        public int Confidence;        // 0-100
        public long GeneratedDay;
        public bool IsUrgent;         // Acil uyarı mı
    }

    /// <summary>İşbaşındaki danışman (oyuncu tarafından atanan).</summary>
    public class ActiveAdvisor
    {
        public string ProfileId = "";
        public AdvisorType Type = AdvisorType.Economy;
        public long HiredDay = -1;
        public int AdviceCount = 0;
        public int LastAdviceDay = -1;
    }

    // --- State ---
    private static readonly Dictionary<string, AdvisorProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<AdvisorType, ActiveAdvisor> _activeAdvisors = new();
    private static readonly List<AdvisorAdvice> _adviceHistory = new();
    private static readonly Dictionary<AdvisorType, int> _cooldownDays = new();
    private static bool _loaded;

    private const string DataPath = "res://Data/advisors.json";

    // --- Init ---
    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            using var file = Godot.FileAccess.Open(DataPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PrintErr($"[AdvisorSystem] {DataPath} açılamadı — boş havuz.");
                return;
            }
            var json = file.GetAsText();
            var root = System.Text.Json.JsonSerializer.Deserialize<AdvisorDataRoot>(json, new System.Text.Json.JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
            if (root?.Advisors != null)
            {
                foreach (var p in root.Advisors)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    _profiles[p.Id] = p;
                }
                GD.Print($"[AdvisorSystem] {_profiles.Count} danışman profili yüklendi");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[AdvisorSystem] advisors.json okunamadı ({e.Message})");
        }
    }

    private sealed class AdvisorDataRoot
    {
        public AdvisorProfile[] Advisors { get; set; }
    }

    // --- Public API ---

    /// <summary>Tüm profiller (sadece okuma).</summary>
    public static IReadOnlyList<AdvisorProfile> GetAllProfiles()
    {
        EnsureLoaded();
        return _profiles.Values.ToList().AsReadOnly();
    }

    /// <summary>Belirli türe uygun profiller (kültür filtresi opsiyonel).</summary>
    public static List<AdvisorProfile> GetProfilesForType(AdvisorType type, string cultureGroup = "")
    {
        EnsureLoaded();
        var list = _profiles.Values.Where(p => p.Type == type).ToList();
        if (!string.IsNullOrEmpty(cultureGroup))
        {
            var cultured = list.Where(p => string.Equals(p.CultureGroup, cultureGroup, StringComparison.OrdinalIgnoreCase)).ToList();
            if (cultured.Count > 0) return cultured;
        }
        return list;
    }

    /// <summary>Danışman işe al (oyuncu seçimi).</summary>
    public static bool HireAdvisor(string profileId, AdvisorType type, long currentDay)
    {
        EnsureLoaded();
        if (!_profiles.TryGetValue(profileId, out var profile)) return false;
        if (profile.Type != type) return false;
        if (_activeAdvisors.ContainsKey(type)) return false; // Bu tipte zaten biri var

        _activeAdvisors[type] = new ActiveAdvisor
        {
            ProfileId = profileId,
            Type = type,
            HiredDay = currentDay,
            AdviceCount = 0,
            LastAdviceDay = -1
        };
        _cooldownDays[type] = 0;
        GD.Print($"[AdvisorSystem] Danışman işe alındı: {profile.Name} ({type}) gün={currentDay}");
        return true;
    }

    /// <summary>Danışman görevden al.</summary>
    public static void FireAdvisor(AdvisorType type, long currentDay)
    {
        if (_activeAdvisors.Remove(type, out var advisor))
        {
            GD.Print($"[AdvisorSystem] Danışman görevden alındı: {advisor.ProfileId} ({type}) gün={currentDay}");
        }
        _cooldownDays.Remove(type);
    }

    /// <summary>Aktif danışman (varsa).</summary>
    public static ActiveAdvisor GetActiveAdvisor(AdvisorType type)
    {
        _activeAdvisors.TryGetValue(type, out var a);
        return a;
    }

    /// <summary>Tüm aktif danışmanlar.</summary>
    public static IReadOnlyDictionary<AdvisorType, ActiveAdvisor> GetAllActiveAdvisors() => _activeAdvisors;

    /// <summary>Tavsiye iste (ajanda slotu harcanır, PP maliyeti GameManager'da).</summary>
    public static AdvisorAdvice RequestAdvice(AdvisorType type, long currentDay, string countryId)
    {
        EnsureLoaded();
        if (!_activeAdvisors.TryGetValue(type, out var active)) return null;

        var profile = _profiles[active.ProfileId];

        // Cooldown kontrolü (haftada 1 tavsiye = 7 gün)
        if (_cooldownDays.TryGetValue(type, out int cd) && cd > 0)
        {
            return null;
        }

        // Cooldown değerini ayarla (int olarak)
        _cooldownDays[type] = 7;

        // Tavsiye üret (deterministik)
        var advice = GenerateAdvice(profile, countryId, currentDay);

        // State güncelle
        active.AdviceCount++;
        active.LastAdviceDay = (int)currentDay;
        _adviceHistory.Add(advice);

        // Geçmişi sınırla (son 200)
        if (_adviceHistory.Count > 200) _adviceHistory.RemoveRange(0, _adviceHistory.Count - 200);

        GD.Print($"[AdvisorSystem] Tavsiye üretildi: {type} oleh {profile.Name} (conf={advice.Confidence}) gün={currentDay}");
        return advice;
    }

    /// <summary>Günlük tick: cooldown azalt.</summary>
    public static void DailyTick(long currentDay)
    {
        var keys = _cooldownDays.Keys.ToList();
        foreach (var k in keys)
        {
            if (_cooldownDays[k] > 0) _cooldownDays[k]--;
            else _cooldownDays.Remove(k);
        }
    }

    /// <summary>Tavsiye geçmişi (son N).</summary>
    public static List<AdvisorAdvice> GetAdviceHistory(int count = 20)
    {
        return _adviceHistory.TakeLast(count).ToList();
    }

    // --- Advice Generation (Deterministic) ---

    private static AdvisorAdvice GenerateAdvice(AdvisorProfile profile, string countryId, long currentDay)
    {
        var country = CountryDatabase.Get(countryId);
        if (country == null)
        {
            return new AdvisorAdvice
            {
                Type = profile.Type,
                AdvisorId = profile.Id,
                Recommendation = "No country data available",
                Rationale = "Country not found in database",
                Confidence = 10,
                GeneratedDay = currentDay,
                IsUrgent = false
            };
        }

        // Profil trait'ine göre bias
        double traitBias = profile.Trait switch
        {
            "reformer" => 0.1,
            "hawk" => 0.15,
            "technocrat" => 0.05,
            "populist" => 0.1,
            "diplomat" => -0.05,
            _ => 0.0
        };

        // Sadakat/popülerlik etkisi
        double loyaltyFactor = Math.Clamp(profile.Loyalty / 100.0, 0.3, 1.0);
        double popularityFactor = Math.Clamp(profile.Popularity / 100.0, 0.5, 1.2);

        var advice = profile.Type switch
        {
            AdvisorType.Economy => GenerateEconomyAdvice(country, profile, traitBias, loyaltyFactor, popularityFactor, currentDay),
            AdvisorType.Security => GenerateSecurityAdvice(country, profile, traitBias, loyaltyFactor, popularityFactor, currentDay),
            AdvisorType.Media => GenerateMediaAdvice(country, profile, traitBias, loyaltyFactor, popularityFactor, currentDay),
            _ => new AdvisorAdvice { Type = profile.Type, AdvisorId = profile.Id, Recommendation = "Unknown type", Rationale = "", Confidence = 0, GeneratedDay = currentDay }
        };

        advice.AdvisorId = profile.Id;
        advice.GeneratedDay = currentDay;
        return advice;
    }

    private static AdvisorAdvice GenerateEconomyAdvice(CountryData country, AdvisorProfile profile, double traitBias, double loyalty, double pop, long day)
    {
        // Ekonomik göstergeleri al
        double gdpGrowth = StatisticsSystem.GetValue(country.Id, "gdp_growth");
        double unemployment = StatisticsSystem.GetValue(country.Id, "unemployment");
        double inflation = StatisticsSystem.GetValue(country.Id, "inflation");
        double pubDebt = StatisticsSystem.GetValue(country.Id, "public_debt");
        int civFactories = country.CivFactories;
        int freeCiv = Math.Max(0, civFactories - EconomySystem.ConstructionQueue.Count);

        string rec = "";
        string rat = "";
        int conf = 50;
        bool urgent = false;

        // Öncelik sırası: borç krizi > enflasyon > işsizlik > büyüme > boş fabrika
        if (pubDebt > 120)
        {
            rec = "Austerity measures: cut spending, raise taxes";
            rat = $"Public debt at {pubDebt:F0}% GDP — risk of default";
            conf = 85 + (int)(DeterministicRNG.NextDouble() * 10);
            urgent = true;
        }
        else if (inflation > 15)
        {
            rec = "Tighten monetary policy, reduce money supply";
            rat = $"Inflation at {inflation:F0}% — eroding purchasing power";
            conf = 80 + (int)(DeterministicRNG.NextDouble() * 10);
            urgent = true;
        }
        else if (unemployment > 15)
        {
            rec = "Launch public works program, subsidize hiring";
            rat = $"Unemployment at {unemployment:F0}% — social unrest risk";
            conf = 75 + (int)(DeterministicRNG.NextDouble() * 10);
        }
        else if (gdpGrowth < -2)
        {
            rec = "Stimulus package: infrastructure + tax cuts";
            rat = $"GDP growth {gdpGrowth:F1}% — recession signals";
            conf = 70 + (int)(DeterministicRNG.NextDouble() * 10);
        }
        else if (freeCiv > 5)
        {
            rec = "Expand civilian industry: new factories";
            rat = $"{freeCiv} free civilian factories — unused capacity";
            conf = 65 + (int)(DeterministicRNG.NextDouble() * 10);
        }
        else
        {
            rec = "Maintain current economic course";
            rat = "Indicators within acceptable bounds";
            conf = 50 + (int)(DeterministicRNG.NextDouble() * 20);
        }

        // Trait bias uygula
        conf = (int)Math.Clamp(conf * (1.0 + traitBias) * loyalty * pop, 10, 100);

        return new AdvisorAdvice
        {
            Type = AdvisorType.Economy,
            Recommendation = rec,
            Rationale = rat,
            Confidence = conf,
            GeneratedDay = day,
            IsUrgent = urgent
        };
    }

    private static AdvisorAdvice GenerateSecurityAdvice(CountryData country, AdvisorProfile profile, double traitBias, double loyalty, double pop, long day)
    {
        int milFactories = country.MilFactories;
        int dockyards = country.Dockyards;
        int divs = CountryAI.GetDivisions(country.Id);
        // Division cap: approximate from starting divisions + factories (no GetDivisionCap method)
        int divCap = Math.Max(50, country.StartingDivisions + country.MilFactories * 2);
        double tension = DiplomacySystem.WorldTension;
        // Check if country is at war (any war involving this country)
        bool atWar = WarSystem.ActiveWars.Any(w => !w.IsEnded && (w.AttackerId == country.Id || w.DefenderId == country.Id));

        string rec = "";
        string rat = "";
        int conf = 50;
        bool urgent = false;

        if (atWar && divs < divCap * 0.5)
        {
            rec = "Emergency mobilization: raise new divisions";
            rat = $"At war with only {divs}/{divCap} divisions — manpower crisis";
            conf = 90;
            urgent = true;
        }
        else if (tension > 70 && divs < divCap * 0.7)
        {
            rec = "Accelerate military production, expand army";
            rat = $"World tension {tension:F0}% — preparedness gap";
            conf = 80 + (int)(DeterministicRNG.NextDouble() * 10);
        }
        else if (milFactories < 5 && country.CivFactories > 10)
        {
            rec = "Convert civilian factories to military";
            rat = $"Only {milFactories} military factories — production bottleneck";
            conf = 70 + (int)(DeterministicRNG.NextDouble() * 10);
        }
        else if (dockyards == 0 && country.Dockyards > 0)
        {
            rec = "Build naval dockyards for power projection";
            rat = "Coastal nation with zero naval capacity";
            conf = 60 + (int)(DeterministicRNG.NextDouble() * 15);
        }
        else
        {
            rec = "Maintain current defense posture";
            rat = "Military capacity adequate for current threats";
            conf = 50 + (int)(DeterministicRNG.NextDouble() * 20);
        }

        conf = (int)Math.Clamp(conf * (1.0 + traitBias) * loyalty * pop, 10, 100);

        return new AdvisorAdvice
        {
            Type = AdvisorType.Security,
            Recommendation = rec,
            Rationale = rat,
            Confidence = conf,
            GeneratedDay = day,
            IsUrgent = urgent
        };
    }

    private static AdvisorAdvice GenerateMediaAdvice(CountryData country, AdvisorProfile profile, double traitBias, double loyalty, double pop, long day)
    {
        // Halk memnuniyeti grupları
        double avgSat = 0;
        int cnt = 0;
        foreach (var g in PublicSatisfactionSystem.Groups)
        {
            avgSat += PublicSatisfactionSystem.GetSatisfaction(country.Id, g.Id);
            cnt++;
        }
        avgSat = cnt > 0 ? avgSat / cnt : 50;

        double stability = country.StabilityPercent;
        double warSupport = country.WarSupportPercent;
        // Count tweets in last 7 days for this country
        int recentTweets = SocialMediaSystem.GetTweets().Count(t => t.CountryId == country.Id && t.Day >= day - 7);

        string rec = "";
        string rat = "";
        int conf = 50;
        bool urgent = false;

        if (avgSat < 25 || stability < 20)
        {
            rec = "Address the nation: unity speech, promise reforms";
            rat = $"Satisfaction {avgSat:F0}%, stability {stability:F0}% — crisis of legitimacy";
            conf = 85;
            urgent = true;
        }
        else if (recentTweets < 3)
        {
            rec = "Increase media presence: scheduled press events";
            rat = $"Only {recentTweets} tweets this week — low visibility";
            conf = 60 + (int)(DeterministicRNG.NextDouble() * 15);
        }
        else if (warSupport < 30 && WarSystem.ActiveWars.Any(w => !w.IsEnded && (w.AttackerId == country.Id || w.DefenderId == country.Id)))
        {
            rec = "Patriotic messaging campaign to boost war support";
            rat = $"War support {warSupport:F0}% during conflict";
            conf = 75 + (int)(DeterministicRNG.NextDouble() * 10);
        }
        else
        {
            rec = "Continue current communication strategy";
            rat = "Public sentiment stable, media engagement adequate";
            conf = 50 + (int)(DeterministicRNG.NextDouble() * 20);
        }

        conf = (int)Math.Clamp(conf * (1.0 + traitBias) * loyalty * pop, 10, 100);

        return new AdvisorAdvice
        {
            Type = AdvisorType.Media,
            Recommendation = rec,
            Rationale = rat,
            Confidence = conf,
            GeneratedDay = day,
            IsUrgent = urgent
        };
    }

    // --- Save/Load ---

    public static AdvisorsSave GetSaveData()
    {
        var active = new List<ActiveAdvisorEntry>(_activeAdvisors.Count);
        foreach (var kv in _activeAdvisors)
        {
            active.Add(new ActiveAdvisorEntry
            {
                Type = kv.Key.ToString(),
                ProfileId = kv.Value.ProfileId,
                HiredDay = kv.Value.HiredDay,
                AdviceCount = kv.Value.AdviceCount,
                LastAdviceDay = kv.Value.LastAdviceDay
            });
        }

        var cooldowns = new List<AdvisorCooldownEntry>(_cooldownDays.Count);
        foreach (var kv in _cooldownDays)
        {
            cooldowns.Add(new AdvisorCooldownEntry
            {
                Type = kv.Key.ToString(),
                DaysRemaining = kv.Value
            });
        }

        var history = _adviceHistory
            .OrderBy(a => a.GeneratedDay)
            .Select(a => new AdvisorAdviceEntry
            {
                Type = a.Type.ToString(),
                AdvisorId = a.AdvisorId,
                Recommendation = a.Recommendation,
                Rationale = a.Rationale,
                Confidence = a.Confidence,
                GeneratedDay = a.GeneratedDay,
                IsUrgent = a.IsUrgent
            })
            .ToArray();

        return new AdvisorsSave
        {
            ActiveAdvisors = active.ToArray(),
            Cooldowns = cooldowns.ToArray(),
            History = history
        };
    }

    public static void LoadSaveData(AdvisorsSave data)
    {
        _activeAdvisors.Clear();
        _cooldownDays.Clear();
        _adviceHistory.Clear();

        if (data == null) return;

        if (data.ActiveAdvisors != null)
        {
            foreach (var a in data.ActiveAdvisors)
            {
                if (a == null || string.IsNullOrEmpty(a.ProfileId)) continue;
                if (Enum.TryParse<AdvisorType>(a.Type, out var type))
                {
                    _activeAdvisors[type] = new ActiveAdvisor
                    {
                        ProfileId = a.ProfileId,
                        Type = type,
                        HiredDay = a.HiredDay,
                        AdviceCount = a.AdviceCount,
                        LastAdviceDay = a.LastAdviceDay
                    };
                }
            }
        }

        if (data.Cooldowns != null)
        {
            foreach (var c in data.Cooldowns)
            {
                if (c == null) continue;
                if (Enum.TryParse<AdvisorType>(c.Type, out var type))
                {
                    _cooldownDays[type] = c.DaysRemaining;
                }
            }
        }

        if (data.History != null)
        {
            foreach (var h in data.History)
            {
                if (h == null) continue;
                if (Enum.TryParse<AdvisorType>(h.Type, out var type))
                {
                    _adviceHistory.Add(new AdvisorAdvice
                    {
                        Type = type,
                        AdvisorId = h.AdvisorId,
                        Recommendation = h.Recommendation ?? "",
                        Rationale = h.Rationale ?? "",
                        Confidence = h.Confidence,
                        GeneratedDay = h.GeneratedDay,
                        IsUrgent = h.IsUrgent
                    });
                }
            }
        }

        GD.Print($"[AdvisorSystem] state yüklendi — aktif={_activeAdvisors.Count}, cooldown={_cooldownDays.Count}, geçmiş={_adviceHistory.Count}");
    }

    public static void ResetForNewGame()
    {
        _profiles.Clear();
        _activeAdvisors.Clear();
        _cooldownDays.Clear();
        _adviceHistory.Clear();
        _loaded = false;
    }

    // ===== Save/Load DTOs (M53) =====
    public class AdvisorsSave
    {
        public ActiveAdvisorEntry[] ActiveAdvisors = Array.Empty<ActiveAdvisorEntry>();
        public AdvisorCooldownEntry[] Cooldowns = Array.Empty<AdvisorCooldownEntry>();
        public AdvisorAdviceEntry[] History = Array.Empty<AdvisorAdviceEntry>();
    }
    public class ActiveAdvisorEntry
    {
        public string Type { get; set; }
        public string ProfileId { get; set; }
        public long HiredDay { get; set; }
        public int AdviceCount { get; set; }
        public int LastAdviceDay { get; set; }
    }
    public class AdvisorCooldownEntry
    {
        public string Type { get; set; }
        public int DaysRemaining { get; set; }
    }
    public class AdvisorAdviceEntry
    {
        public string Type { get; set; }
        public string AdvisorId { get; set; }
        public string Recommendation { get; set; }
        public string Rationale { get; set; }
        public int Confidence { get; set; }
        public long GeneratedDay { get; set; }
        public bool IsUrgent { get; set; }
    }
}