using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Senaryo yönetim sistemi (M17.1):
/// - Data/scenarios/scenarios.json'dan senaryo listesi yükler
/// - Aktif senaryo: veri yükleyicilerin (CountryDatabase, FocusSystem, EventSystem vb.)
///   dosya yollarını alacağı merkezi kaynak
/// - SetActive(id) → tüm loader cache'lerini temizler (_loaded=false), yeni yollardan yeniden yüklenir
/// - Varsayılan: historical_1936 (headless test uyumu için; menü modern_world'u öne çıkarır)
/// </summary>
public static class ScenarioSystem
{
    private const string ScenariosPath = "res://Data/scenarios/scenarios.json";

    // ===== Veri modelleri =====
    public class Scenario
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public string StartDate = "1936-01-01";
        public bool Placeholder = false;
        public string CountriesPath = "";
        public string FocusTreesDir = "";
        public string EventsPath = "";
        public string DecisionsPath = "";
        public string TechnologiesPath = "";
        public string LibraryDir = "";
        public string CasusBelliPath = "";
        public string TerritorialClaimsPath = "";
        // M17.2: Senaryoya bağlı başlangıç verileri
        public string DiplomacyStartPath = "";
        public string ArmyStartPath = "";
        public string EquipmentStartPath = "";
        public string LeadersPath = "";
        public string FormulasPath = ""; // M17.2: ham girdi → oyun değeri türetme formülleri
    }

    private static readonly List<Scenario> _all = new();
    private static readonly Dictionary<string, Scenario> _byId = new();
    private static Scenario _active;
    private static bool _loaded;

    // Varsayılan senaryo: headless testlerde historical_1936 çalışır (içerikli).
    // Menüde modern_world öne çıkar (TODO: "Modern Dünya (varsayılan)").
    private const string DefaultScenarioId = "historical_1936";

    // ===== Public API =====

    public static IReadOnlyList<Scenario> All
    {
        get
        {
            EnsureLoaded();
            return _all;
        }
    }

    public static Scenario Active
    {
        get
        {
            EnsureLoaded();
            if (_active == null) _active = GetById(DefaultScenarioId);
            return _active;
        }
    }

    public static string ActiveId => Active.Id;
    public static string ActiveName => Active.Name;
    public static string ActiveDescription => Active.Description;
    public static DateTime ActiveStartDate
    {
        get
        {
            var a = Active;
            if (DateTime.TryParse(a.StartDate, out var dt)) return dt;
            return new DateTime(1936, 1, 1);
        }
    }
    public static bool ActiveIsPlaceholder => Active.Placeholder;

    public static string ActiveCountriesPath => Active.CountriesPath;
    public static string ActiveFocusTreesDir => Active.FocusTreesDir;
    public static string ActiveEventsPath => Active.EventsPath;
    public static string ActiveDecisionsPath => Active.DecisionsPath;
    public static string ActiveTechnologiesPath => Active.TechnologiesPath;
    public static string ActiveLibraryDir => Active.LibraryDir;
    public static string ActiveCasusBelliPath => Active.CasusBelliPath;
    public static string ActiveTerritorialClaimsPath => Active.TerritorialClaimsPath;
    // M17.2: Senaryo başlangıç verisi yolları
    public static string ActiveDiplomacyStartPath => Active.DiplomacyStartPath;
    public static string ActiveArmyStartPath => Active.ArmyStartPath;
    public static string ActiveEquipmentStartPath => Active.EquipmentStartPath;
    public static string ActiveLeadersPath => Active.LeadersPath;
    public static string ActiveFormulasPath => Active.FormulasPath;

    /// <summary>Aktif senaryoyu değiştirir. Aynı id tekrar çağrılırsa no-op.</summary>
    /// <returns>Başarılıysa true, senaryo bulunamazsa false.</returns>
    public static bool SetActive(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id)) return false;
        if (!_byId.TryGetValue(id, out var s)) return false;

        if (_active != null && _active.Id == id) return true; // no-op

        _active = s;
        GD.Print($"[ScenarioSystem] Aktif senaryo: {_active.Id} — {_active.Name} (Start: {_active.StartDate}, Placeholder: {_active.Placeholder})");

        // Tüm loader cache'lerini temizle → yeni yolları EnsureLoaded'ta okuyacaklar
        CountryDatabase.ResetForScenario();
        FocusSystem.ResetForScenario();
        EventSystem.ResetForScenario();
        DecisionSystem.ResetForScenario();
        TechnologySystem.ResetForScenario();
        LibrarySystem.ResetForScenario();
        CasusBelliSystem.ResetForScenario();
        DisasterSystem.ResetForScenario();
        UnexpectedEventSystem.ResetForScenario();
        // M17.2: senaryoya bağlı başlangıç verisi olan sistemler
        DiplomacySystem.ResetForScenario();
        ArmySystem.ResetForScenario();
        EquipmentSystem.ResetForScenario();
        LeaderSystem.ResetForScenario();

        return true;
    }

    public static Scenario GetById(string id)
    {
        EnsureLoaded();
        return _byId.GetValueOrDefault(id);
    }

    // ===== Yükleme =====

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (!FileAccess.FileExists(ScenariosPath))
        {
            GD.PrintErr($"[ScenarioSystem] HATA: {ScenariosPath} bulunamadı!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var list = JsonSerializer.Deserialize<List<Scenario>>(FileAccess.GetFileAsString(ScenariosPath), options);
            if (list == null || list.Count == 0)
            {
                GD.PrintErr("[ScenarioSystem] HATA: Senaryo listesi boş!");
                return;
            }

            foreach (var s in list)
            {
                if (string.IsNullOrEmpty(s.Id)) continue;
                _all.Add(s);
                _byId[s.Id] = s;
            }

            GD.Print($"[ScenarioSystem] {_all.Count} senaryo yüklendi: {string.Join(", ", _all.Select(s => $"{s.Id}({s.StartDate})"))}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ScenarioSystem] HATA: {ScenariosPath} okunamadı: {e.Message}");
        }
    }

    // ===== Yardımcılar (test/debug) =====

    /// <summary>Her iki senaryo için veri yükleme sayılarını loglar (--scenario-test).</summary>
    public static void LogLoadCountsForAll()
    {
        EnsureLoaded();
        foreach (var s in _all)
        {
            GD.Print($"=== {s.Id} ({s.Name}) ===");
            var prev = _active;
            _active = s;

            CountryDatabase.ResetForScenario();
            FocusSystem.ResetForScenario();
            EventSystem.ResetForScenario();
            DecisionSystem.ResetForScenario();
            TechnologySystem.ResetForScenario();
            LibrarySystem.ResetForScenario();
            CasusBelliSystem.ResetForScenario();
            DiplomacySystem.ResetForScenario();
            ArmySystem.ResetForScenario();
            EquipmentSystem.ResetForScenario();
            LeaderSystem.ResetForScenario();

            // Senaryo verilerini yükle (ülke gerekmez)
            CountryDatabase.LoadScenarioData();
            FocusSystem.LoadScenarioData();
            EventSystem.LoadScenarioData();
            DecisionSystem.LoadScenarioData();
            TechnologySystem.LoadScenarioData();
            LibrarySystem.LoadScenarioData();
            CasusBelliSystem.LoadScenarioData();
            DiplomacySystem.LoadScenarioData();
            ArmySystem.LoadScenarioData();
            EquipmentSystem.LoadScenarioData();
            LeaderSystem.LoadScenarioData();

            int c = CountryDatabase.All().Count();
            int fTrees = FocusSystem.Trees.Count;
            int fFocuses = FocusSystem.Trees.Values.Sum(t => t.Focuses.Count);
            int e = EventSystem.AllEvents.Count;
            int d = DecisionSystem.AllDecisions.Count;
            int t = TechnologySystem.All.Count;
            int lib = LibrarySystem.TextEntryCount;
            int cbTypes = CasusBelliSystem.TypesCount;
            int cbClaims = CasusBelliSystem.ClaimsCount;
            int dipFactions = DiplomacySystem.Factions.Count;
            int dipRelations = DiplomacySystem.Relations.Count;
            int dipGuarantees = DiplomacySystem.Guarantees.Count;
            int leaders = LeaderSystem.LoadedCount;

            GD.Print($"  Ülke: {c}, Odak Ağacı: {fTrees} ({fFocuses} odak), Olay: {e}, Karar: {d}, Teknoloji: {t}, Kütüphane: {lib}, Gerekçe Türü: {cbTypes}, Toprak İddiası: {cbClaims}, Faksiyon: {dipFactions}, İlişki: {dipRelations}, Garanti: {dipGuarantees}, Lider: {leaders}");
        }
    }
}