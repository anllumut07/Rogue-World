using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Kararlar sistemi (M10.2): kararları yükler (aktif senaryonun decisions.json),
/// kullanılabilirlik koşullarını kontrol eder, maliyet (PP + gün) ve
/// cooldown yönetir, etkileri uygular. DecisionPanel UI'dan kullanılır.
/// Statik sınıf; GameManager günlük tick'inde çağrılmaz (UI tabanlı).
/// </summary>
public static class DecisionSystem
{
    // M17.1: yol aktif senaryodan; senaryo değişince ResetForScenario cache'i temizler
    private static string CurrentPath => ScenarioSystem.ActiveDecisionsPath;

    // ===== Veri modelleri =====

    public class Decision
    {
        public string Id = "?";
        public string Name = "?";
        public string Description = "";
        public string Country = "ALL";           // "ALL" veya ülke kodu (GER, USA, SOV...)
        public int CostPP = 0;
        public int CostDays = 0;                 // tamamlanma süresi (gün)
        public int CooldownDays = 0;             // tekrar kullanılabilme süresi
        public DecisionCondition Condition = new();
        public DecisionEffects Effects = new();
    }

    public class DecisionCondition
    {
        public int MinStability = 0;
        public int MaxStability = 100;
        public int MinWarSupport = 0;
        public int MaxWarSupport = 100;
        public int MinPP = 0;
        public float MaxWorldTension = 100f;
        public string RequiredFocus = "";         // tamamlanmış odak gerekirse
        public string RequiredNotFocus = "";      // tamamlanmamış odak gerekirse
        public string RequiredIdeology = "";      // ülke ideolojisi (Democracy/Fascism/Communism)
        public bool IsAtWar = false;              // savaşta olma zorunluluğu
    }

    public class DecisionEffects
    {
        public double PoliticalPower = 0;
        public double Stability = 0;
        public double WarSupport = 0;
        public int CivFactories = 0;
        public int MilFactories = 0;
        public int Dockyards = 0;
        public int ResearchSlots = 0;
        public long Manpower = 0;
        public int Oil = 0;
        public int Rubber = 0;
        public int Steel = 0;
        public int Aluminum = 0;
        public int Tungsten = 0;
        public int Chromium = 0;
        public double WorldTension = 0;
    }

    // Ülke bazlı karar durumu (ilerleme + cooldown)
    public class CountryDecisionState
    {
        public string CountryId = "";
        public Dictionary<string, ActiveDecision> Active = new();      // yürütülüyor: decisionId -> ActiveDecision
        public Dictionary<string, int> Cooldowns = new();              // decisionId -> kalan gün
        public HashSet<string> Completed = new();                      // en az bir kez tamamlanan
    }

    public class ActiveDecision
    {
        public string DecisionId = "";
        public int DaysElapsed = 0;
        public int TotalDays = 0;
    }

    // ===== Durum =====
    private static bool _loaded;
    private static readonly List<Decision> _allDecisions = new();
    private static readonly Dictionary<string, Decision> _decisionsById = new();
    private static readonly Dictionary<string, CountryDecisionState> _states = new();

    private static GameManager GM => GameManager.Instance;

    // ===== Yükleme =====

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (!FileAccess.FileExists(CurrentPath))
        {
            GD.PrintErr($"HATA: {CurrentPath} bulunamadı!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var list = JsonSerializer.Deserialize<List<Decision>>(FileAccess.GetFileAsString(CurrentPath), options);
            if (list == null)
            {
                GD.PrintErr("HATA: decisions.json boş!");
                return;
            }

            foreach (var d in list)
            {
                if (string.IsNullOrEmpty(d.Id)) continue;
                _allDecisions.Add(d);
                _decisionsById[d.Id] = d;
            }

            GD.Print($"DecisionSystem: {_allDecisions.Count} karar yüklendi (senaryo: {ScenarioSystem.ActiveId})");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"HATA: {CurrentPath} okunamadı: {ex.Message}");
        }
    }

    // M17.1: Senaryo değişince cache'i temizle
    public static void ResetForScenario()
    {
        _allDecisions.Clear();
        _decisionsById.Clear();
        _states.Clear();
        _loaded = false;
    }

    // M17.1: Senaryo verisi yükle (ülke gerekmez) — LogLoadCountsForAll için
    public static void LoadScenarioData()
    {
        EnsureLoaded();
    }

    // ===== Başlatma =====

    public static void Initialize(CountryData playerData)
    {
        EnsureLoaded();
        _states.Clear();

        if (playerData == null) return;

        var st = GetOrCreateState(playerData.Id);
        GD.Print($"DecisionSystem: {playerData.Name} ({playerData.Id}) için hazır");
    }

    public static CountryDecisionState GetOrCreateState(string countryId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(countryId)) return null;
        if (_states.TryGetValue(countryId, out var st)) return st;

        st = new CountryDecisionState { CountryId = countryId };
        _states[countryId] = st;
        return st;
    }

    // ===== Sorgular (UI için) =====

    // Oyuncu ülkesinin kullanılabilir kararları
    public static List<Decision> GetAvailableDecisions()
    {
        if (!GM.HasPlayer) return new List<Decision>();

        var st = GetOrCreateState(GM.PlayerCountryId);
        if (st == null) return new List<Decision>();

        var data = GM.PlayerCountryData;
        if (data == null) return new List<Decision>();

        var result = new List<Decision>();

        foreach (var d in _allDecisions)
        {
            // Ülke filtresi
            if (d.Country != "ALL" && !IsCountryMatch(d.Country, GM.PlayerCountryId))
                continue;

            // Zaten aktif mi?
            if (st.Active.ContainsKey(d.Id))
                continue;

            // Cooldown var mı?
            if (st.CoolsDownsContains(d.Id))
                continue;

            // Koşullar
            if (!CheckConditions(d, data))
                continue;

            result.Add(d);
        }

        return result;
    }

    // Aktif (yürütülen) kararlar
    public static List<ActiveDecisionInfo> GetActiveDecisions()
    {
        if (!GM.HasPlayer) return new List<ActiveDecisionInfo>();

        var st = GetOrCreateState(GM.PlayerCountryId);
        if (st == null) return new List<ActiveDecisionInfo>();

        var result = new List<ActiveDecisionInfo>();
        foreach (var kv in st.Active)
        {
            if (_decisionsById.TryGetValue(kv.Key, out var d))
            {
                result.Add(new ActiveDecisionInfo
                {
                    Decision = d,
                    DaysElapsed = kv.Value.DaysElapsed,
                    TotalDays = kv.Value.TotalDays,
                    Progress = kv.Value.TotalDays > 0 ? (float)kv.Value.DaysElapsed / kv.Value.TotalDays : 0f
                });
            }
        }
        return result;
    }

    // Cooldown kalan günler
    public static int GetCooldownDays(string decisionId)
    {
        if (!GM.HasPlayer) return 0;
        var st = GetOrCreateState(GM.PlayerCountryId);
        return st?.Cooldowns.GetValueOrDefault(decisionId, 0) ?? 0;
    }

    public static Decision GetDecision(string id) => _decisionsById.GetValueOrDefault(id);

    public static IReadOnlyList<Decision> AllDecisions => _allDecisions;

    // ===== Aksiyonlar (UI'dan) =====

    // Kararı başlat (PP öde, aktif listeye ekle)
    public static bool StartDecision(string decisionId)
    {
        if (!GM.HasPlayer) return false;

        var st = GetOrCreateState(GM.PlayerCountryId);
        if (st == null) return false;

        if (!_decisionsById.TryGetValue(decisionId, out var d))
        {
            GD.Print($"[Decision] Bilinmeyen karar: {decisionId}");
            return false;
        }

        // Zaten aktif?
        if (st.Active.ContainsKey(decisionId))
        {
            GD.Print($"[Decision] Zaten aktif: {d.Name}");
            return false;
        }

        // Cooldown?
        if (st.CoolsDownsContains(decisionId))
        {
            GD.Print($"[Decision] Cooldown devam ediyor: {d.Name}");
            return false;
        }

        // Koşullar
        var data = GM.PlayerCountryData;
        if (data == null || !CheckConditions(d, data))
        {
            GD.Print($"[Decision] Koşullar sağlanmıyor: {d.Name}");
            return false;
        }

        // PP maliyeti
        if (data.PoliticalPower < d.CostPP)
        {
            GD.Print($"[Decision] PP yetersiz: {data.PoliticalPower:N1}/{d.CostPP}");
            return false;
        }

        data.PoliticalPower -= d.CostPP;

        // Aktif listeye ekle
        st.Active[decisionId] = new ActiveDecision
        {
            DecisionId = decisionId,
            DaysElapsed = 0,
            TotalDays = d.CostDays
        };

        GD.Print($"[Decision] BAŞLATILDI: {d.Name} ({d.CostDays} gün, maliyet {d.CostPP} PP)");
        return true;
    }

    // Kararı iptal et (PP iade edilmez, ilerleme kaybolur — HOI4 kuralı)
    public static void CancelDecision(string decisionId)
    {
        if (!GM.HasPlayer) return;
        var st = GetOrCreateState(GM.PlayerCountryId);
        if (st?.Active.Remove(decisionId) == true)
        {
            GD.Print($"[Decision] İPTAL: {decisionId} (ilerleme kayboldu)");
        }
    }

    // Günlük tick: aktif kararları ilerlet (DecisionPanel açılıyken çağrılmaz, GameManager'dan çağrılır)
    public static void DailyTick()
    {
        if (!GM.HasPlayer) return;
        var st = GetOrCreateState(GM.PlayerCountryId);
        if (st == null) return;

        // 1) Cooldownları azalt
        var toRemove = new List<string>();
        foreach (var kv in st.Cooldowns)
        {
            int remaining = kv.Value - 1;
            if (remaining <= 0)
                toRemove.Add(kv.Key);
            else
                st.Cooldowns[kv.Key] = remaining;
        }
        foreach (var key in toRemove) st.Cooldowns.Remove(key);

        // 2) Aktif kararları ilerlet
        var completed = new List<string>();
        foreach (var kv in st.Active)
        {
            kv.Value.DaysElapsed++;
            if (kv.Value.DaysElapsed >= kv.Value.TotalDays)
                completed.Add(kv.Key);
        }

        // 3) Tamamlananları işle
        foreach (var id in completed)
        {
            CompleteDecision(st, id);
        }

        // 4) TEŞHİS: kullanılabilir karar listesi boş mu? (spam önleme: yalnızca sayı değişince logla)
        int availableCount = GetAvailableDecisions().Count;
        if (availableCount != _lastAvailableCount)
        {
            if (availableCount == 0)
                GD.Print($"[Decision] UYARI: {GM.PlayerCountryId} için kullanılabilir karar YOK (toplam {_allDecisions.Count} karar yüklü)");
            else
                GD.Print($"[Decision] Kullanılabilir karar sayısı: {availableCount} ({GM.PlayerCountryId})");
            _lastAvailableCount = availableCount;
        }
    }

    private static int _lastAvailableCount = -1;

    private static void CompleteDecision(CountryDecisionState st, string decisionId)
    {
        st.Active.Remove(decisionId);

        if (!_decisionsById.TryGetValue(decisionId, out var d)) return;

        st.Completed.Add(decisionId);
        if (d.CooldownDays > 0)
            st.Cooldowns[decisionId] = d.CooldownDays;

        ApplyEffects(d.Effects);
        GD.Print($"[Decision] TAMAMLANDI: {d.Name} — efektler uygulandı");

        if (GameManager.IsAvailable)
            GM.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);
    }

    // Koşulları kontrol et
    private static bool CheckConditions(Decision d, CountryData data)
    {
        var c = d.Condition;

        if (data.StabilityPercent < c.MinStability || data.StabilityPercent > c.MaxStability) return false;
        if (data.WarSupportPercent < c.MinWarSupport || data.WarSupportPercent > c.MaxWarSupport) return false;
        if (data.PoliticalPower < c.MinPP) return false;
        if (DiplomacySystem.WorldTension > c.MaxWorldTension) return false;

        // Savaşta olma zorunluluğu
        if (c.IsAtWar && WarSystem.ActiveWars.Count == 0) return false;

        // İdeoloji
        if (!string.IsNullOrEmpty(c.RequiredIdeology) && data.Ideology != c.RequiredIdeology) return false;

        // Gerekli odak tamamlanmış mı?
        if (!string.IsNullOrEmpty(c.RequiredFocus))
        {
            var focusSt = FocusSystem.GetOrCreateState(data.Id);
            if (focusSt == null || !focusSt.Completed.Contains(c.RequiredFocus))
                return false;
        }

        // Yasaklı odak tamamlanmamış mı?
        if (!string.IsNullOrEmpty(c.RequiredNotFocus))
        {
            var focusSt = FocusSystem.GetOrCreateState(data.Id);
            if (focusSt != null && focusSt.Completed.Contains(c.RequiredNotFocus))
                return false;
        }

        return true;
    }

    // Etikleri uygula (EventSystem.ApplyEffects ile benzer)
    private static void ApplyEffects(DecisionEffects e)
    {
        var data = GM?.PlayerCountryData;
        if (data == null) return;

        if (e.PoliticalPower != 0) data.PoliticalPower += e.PoliticalPower;
        if (e.Stability != 0) data.StabilityPercent = Math.Clamp(data.StabilityPercent + e.Stability, 0, 100);
        if (e.WarSupport != 0) data.WarSupportPercent = Math.Clamp(data.WarSupportPercent + e.WarSupport, 0, 100);
        if (e.CivFactories != 0) data.CivFactories += e.CivFactories;
        if (e.MilFactories != 0) data.MilFactories += e.MilFactories;
        if (e.Dockyards != 0) data.Dockyards += e.Dockyards;
        if (e.ResearchSlots != 0) data.ResearchSlots = Math.Min(5, data.ResearchSlots + e.ResearchSlots);
        if (e.Manpower != 0) data.ManpowerPool += e.Manpower;

        if (e.Oil != 0) data.Oil += e.Oil;
        if (e.Rubber != 0) data.Rubber += e.Rubber;
        if (e.Steel != 0) data.Steel += e.Steel;
        if (e.Aluminum != 0) data.Aluminum += e.Aluminum;
        if (e.Tungsten != 0) data.Tungsten += e.Tungsten;
        if (e.Chromium != 0) data.Chromium += e.Chromium;

        if (e.WorldTension != 0) DiplomacySystem.IncreaseTension((float)e.WorldTension);
    }

    // Ülke eşleşmesi (HOI4 ↔ ISO)
    private static bool IsCountryMatch(string decisionCountry, string playerCountry)
    {
        string dc = CountryDatabase.NormalizeId(decisionCountry);
        string pc = CountryDatabase.NormalizeId(playerCountry);
        return dc == pc;
    }

    // ===== M12: Save/Load =====

    public static SaveSystem.DecisionSave GetSaveData()
    {
        return new SaveSystem.DecisionSave
        {
            States = _states.Values.Select(s => new SaveSystem.CountryDecisionStateSave
            {
                CountryId = s.CountryId,
                Active = s.Active.Select(a => new SaveSystem.ActiveDecisionSave
                {
                    DecisionId = a.Key,
                    DaysElapsed = a.Value.DaysElapsed,
                    TotalDays = a.Value.TotalDays
                }).ToArray(),
                Cooldowns = s.Cooldowns.ToDictionary(kv => kv.Key, kv => kv.Value),
                Completed = s.Completed.ToArray()
            }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.DecisionSave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _states.Clear();
        if (data.States != null)
        {
            foreach (var s in data.States)
            {
                var state = new CountryDecisionState
                {
                    CountryId = s.CountryId,
                    Active = new Dictionary<string, ActiveDecision>(),
                    Cooldowns = new Dictionary<string, int>(),
                    Completed = new HashSet<string>()
                };
                if (s.Active != null)
                    foreach (var a in s.Active)
                        state.Active[a.DecisionId] = new ActiveDecision
                        {
                            DecisionId = a.DecisionId,
                            DaysElapsed = a.DaysElapsed,
                            TotalDays = a.TotalDays
                        };
                if (s.Cooldowns != null)
                    foreach (var kv in s.Cooldowns)
                        state.Cooldowns[kv.Key] = kv.Value;
                if (s.Completed != null)
                    foreach (var c in s.Completed)
                        state.Completed.Add(c);
                _states[state.CountryId] = state;
            }
        }
        GD.Print($"DecisionSystem: state yüklendi — Ülke state={_states.Count}");
    }

    // ===== UI yardımcı sınıflar =====

    public class ActiveDecisionInfo
    {
        public Decision Decision;
        public int DaysElapsed;
        public int TotalDays;
        public float Progress; // 0-1
    }
}

// CountryDecisionState extension (CooldownsContains metodunu eklemek için)
public static class DecisionSystemExtensions
{
    public static bool CoolsDownsContains(this DecisionSystem.CountryDecisionState st, string decisionId)
    {
        return st?.Cooldowns.ContainsKey(decisionId) == true && st.Cooldowns[decisionId] > 0;
    }
}