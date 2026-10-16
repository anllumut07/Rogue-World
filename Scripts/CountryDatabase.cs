using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

// Ülke veritabanı: aktif senaryonun veri dosyasından yükler.
// Modern senaryo: countries.json (ham girdi: nüfus, GSYİH, ordu, kaynak sınıfları) + formulas.json → oyun değerleri türetilir.
// Klasik senaryo: countries_data.json (hazır oyun değerleri) doğrudan yüklenir.
public static class CountryDatabase
{
    // M17.1/17.2: yollar aktif senaryodan alınır; senaryo değişince ResetForScenario ile cache yenilenir
    private static string CurrentPath => ScenarioSystem.ActiveCountriesPath;
    private static string FormulasPath => ScenarioSystem.ActiveFormulasPath;

    // HOI4 kodu ↔ GeoJSON ISO A3 takma adları (ör. SOV=RUS).
    // NOT: HOI4'te AUS = Avusturya, ISO'da AUS = Avustralya olduğu için
    // "AUS" kasıtlı olarak eşlenmedi (belirsiz; ISO anlamı korunur).
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["SOV"] = "RUS",   // Sovyetler Birliği
        ["ENG"] = "GBR",   // Birleşik Krallık
        ["GER"] = "DEU",   // Almanya
        ["JAP"] = "JPN",   // Japonya
        ["CHI"] = "CHN",   // Çin
        ["SPA"] = "ESP",   // İspanya
        ["GRE"] = "GRC",   // Yunanistan
        ["YUG"] = "SRB",   // Yugoslavya
        ["MAN"] = "MNG",   // Mançurya
        ["MON"] = "MNG",   // Moğolistan
        // ["PER"] = "IRN" KALDIRILDI (M12): JSON'da PER = Peru, IRN = Iran.
        // HOI4'te PER = Persia/Iran ama bu oyun ISO A3 kullanıyor; bu alias
        // Peru'yu Iran'a yönlendirip save/load determinizmini bozuyordu.
        ["SIA"] = "THA",   // Siyam/Tayland
        ["SAF"] = "ZAF",   // Güney Afrika
        ["ROM"] = "ROU",   // Romanya
        ["BUL"] = "BGR",   // Bulgaristan
        ["RAJ"] = "IND",   // Britanya Rajı
        ["HOL"] = "NLD",   // Hollanda
        ["DEN"] = "DNK",   // Danimarka
        ["SWI"] = "CHE",   // İsviçre
        ["AST"] = "AUS",   // Avustralya
        ["PHI"] = "PHL",   // Filipinler
        ["INS"] = "IDN",   // Hollanda Doğu Hint Adaları
    };

    private static Dictionary<string, CountryData> _byId = new();
    private static bool _loaded = false;

    // GeoJSON'dan Id → (Name, Capital) cache'i (senaryo bağımsız, bir kez yüklenir)
    private static Dictionary<string, (string Name, string Capital)> _geoNames = new();
    private static bool _geoNamesLoaded = false;

    public static CountryData Get(string id)
    {
        EnsureLoaded();
        return id != null && _byId.TryGetValue(Normalize(id), out var data) ? data : null;
    }

    public static CountryData GetOrCreate(string id, string name)
    {
        EnsureLoaded();
        if (id == null) return null;
        id = Normalize(id);
        if (_byId.TryGetValue(id, out var data)) return data;

        data = GenerateFallback(id, string.IsNullOrEmpty(name) ? id : name);
        _byId[id] = data;
        return data;
    }

    public static IEnumerable<CountryData> All()
    {
        EnsureLoaded();
        return _byId.Values.OrderBy(c => c.Id, StringComparer.Ordinal);
    }

    // HOI4 takma adını (SOV vb.) veri dosyasındaki ISO A3'e çevirir.
    private static string Normalize(string id)
    {
        return Aliases.TryGetValue(id, out var canonical) ? canonical : id;
    }

    // Dış sistemler (Event/Decision/Focus) için public normalizasyon:
    // HOI4 kodu (GER, SOV...) → ISO A3 (DEU, RUS...).
    public static string NormalizeId(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        return Aliases.TryGetValue(id, out var canonical) ? canonical : id;
    }

    // M12: Save/Load için normalizasyonsuz exact lookup.
    // PER/IRN gibi takma ad çiftleri için kritik — ID birebir eşleşsin.
    public static CountryData GetExact(string id)
    {
        EnsureLoaded();
        return id != null && _byId.TryGetValue(id, out var data) ? data : null;
    }

    // Verisi olmayan ülkeler için id bazlı istikrarlı (deterministik) değerler.
    private static CountryData GenerateFallback(string id, string name)
    {
        unchecked
        {
            int h = 17;
            foreach (char ch in id) h = h * 31 + ch;
            h = (int)Mathf.Abs(h);

            long pop = 500_000 + (long)(h % 15_000_000);
            int civ = 1 + h % 4;
            int mil = h % 3;

            return new CountryData
            {
                Id = id,
                Name = name,
                Capital = "—",
                Ideology = "Neutral",
                Population = pop,
                ManpowerPool = pop / 50,
                CivFactories = civ,
                MilFactories = mil,
                Dockyards = h % 2,
                Oil = h % 3,
                Rubber = (h >> 3) % 3,
                Steel = 1 + (h >> 5) % 3,
                Aluminum = (h >> 7) % 2,
                Tungsten = (h >> 9) % 2,
                Chromium = (h >> 11) % 2,
                PoliticalPower = 30,
                StabilityPercent = 50 + (h >> 13) % 20,
                WarSupportPercent = 10 + (h >> 15) % 10,
                ResearchSlots = 2,
                StartingDivisions = 2 + h % 6
            };
        }
    }

    // GeoJSON (Data/countries.geo.json) içinde Id'ye göre Name ve Capital bulur.
    // İlk çağrıda dosyayı parse eder ve cache'ler.
    private static (string Name, string Capital) GetGeoNameCapital(string id)
    {
        if (!_geoNamesLoaded)
        {
            _geoNamesLoaded = true;
            string geoPath = "res://Data/countries.geo.json";
            if (Godot.FileAccess.FileExists(geoPath))
            {
                try
                {
                    string json = Godot.FileAccess.GetFileAsString(geoPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("features", out var features))
                    {
                        foreach (var f in features.EnumerateArray())
                        {
                            if (f.TryGetProperty("properties", out var props))
                            {
                                string iso = props.TryGetProperty("iso_a3", out var isoProp) ? isoProp.GetString() : null;
                                string name = props.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                                string capital = props.TryGetProperty("capital", out var capProp) ? capProp.GetString() : null;
                                if (!string.IsNullOrEmpty(iso))
                                    _geoNames[iso] = (name ?? iso, capital ?? "—");
                            }
                        }
                    }
                    GD.Print($"CountryDatabase: GeoJSON'dan {_geoNames.Count} ülke adı/başkent yüklendi.");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"CountryDatabase: GeoJSON isim okunamadı: {e.Message}");
                }
            }
        }
        return _geoNames.GetValueOrDefault(id, (id, "—"));
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (!Godot.FileAccess.FileExists(CurrentPath))
        {
            GD.PrintErr($"HATA: {CurrentPath} bulunamadı!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
            string json = Godot.FileAccess.GetFileAsString(CurrentPath);

            // Modern ham girdi formatı mı? ("PopulationMillions" alanı imzası)
            bool isModernInput;
            using (var doc = JsonDocument.Parse(json))
            {
                isModernInput = doc.RootElement.ValueKind == JsonValueKind.Array
                                && doc.RootElement.GetArrayLength() > 0
                                && doc.RootElement[0].TryGetProperty("PopulationMillions", out _);
            }

            int count = 0;
            if (isModernInput)
            {
                // ===== MODERN SENARYO: ham girdi + formüller → CountryData =====
                var inputs = JsonSerializer.Deserialize<List<CountryInput>>(json, options);
                if (inputs != null)
                {
                    // Formülleri yükle (modern senaryoda FormulasPath doludur)
                    var formulas = LoadFormulas();

                    foreach (var c in inputs)
                    {
                        if (string.IsNullOrEmpty(c.Id)) continue;

                        // Name/Capital: önce countries.json'dan; yoksa GeoJSON'dan
                        var (geoName, geoCapital) = GetGeoNameCapital(c.Id);
                        string name = string.IsNullOrEmpty(c.Name) ? geoName : c.Name;
                        string capital = string.IsNullOrEmpty(c.Capital) ? geoCapital : c.Capital;

                        var data = DeriveFromCountryInput(c, formulas, name, capital);
                        data.IsDetailed = true;
                        _byId[data.Id] = data;
                        count++;
                    }
                }
                GD.Print($"CountryDatabase hazır: {count} ülke (modern ham girdi + formüller, senaryo: {ScenarioSystem.ActiveId}).");
            }
            else
            {
                // ===== KLASİK SENARYO: countries_data.json doğrudan =====
                var list = JsonSerializer.Deserialize<List<CountryData>>(json, options);
                foreach (var c in list)
                    if (c?.Id != null)
                    {
                        c.IsDetailed = true;
                        _byId[c.Id] = c;
                        count++;
                    }
                GD.Print($"CountryDatabase hazır: {count} ülke verisi yüklendi (senaryo: {ScenarioSystem.ActiveId}).");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"HATA: {CurrentPath} okunamadı: {e.Message}");
        }
    }

    // countries.json ham girdi kaydı
    private class CountryInput
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Capital { get; set; } = "";
        public double PopulationMillions { get; set; }
        public double GdpBillionUsd { get; set; }
        public int? ActiveMilitaryThousands { get; set; }
        public double? DefenseBudgetPercentGdp { get; set; }
        public string Oil { get; set; } = "";
        public string Steel { get; set; } = "";
        public string Aluminum { get; set; } = "";
        public string Chromium { get; set; } = "";
        public string Tungsten { get; set; } = "";
        public string Rubber { get; set; } = "";
        public string RegimeType { get; set; } = "";
        public string Ideology { get; set; } = "";
        public bool NuclearPower { get; set; }
        public bool Approx { get; set; }
    }

    // formulas.json yapısı
    private class Formulas
    {
        public CivFactoriesFormula CivFactories { get; set; } = new();
        public MilFactoriesFormula MilFactories { get; set; } = new();
        public DockyardsFormula Dockyards { get; set; } = new();
        public ManpowerPoolFormula ManpowerPool { get; set; } = new();
        public StartingDivisionsFormula StartingDivisions { get; set; } = new();
        public Dictionary<string, int> ResourceUnits { get; set; } = new();
        public PoliticsFormula Politics { get; set; } = new();
        public ResearchSlotsFormula ResearchSlots { get; set; } = new();
        public DefaultsFormula Defaults { get; set; } = new();
        public TechnologyLevelFormula TechnologyLevel { get; set; } = new(); // M17.4
        public EquipmentStartFormula EquipmentStart { get; set; } = new();   // M17.4
    }
    private class CivFactoriesFormula { public double GdpPerCivFactory { get; set; } = 12; public int MinCiv { get; set; } = 2; public int MaxCiv { get; set; } = 150; }
    private class MilFactoriesFormula { public double GdpPerMilFactory { get; set; } = 6; public int MinMil { get; set; } = 1; public int MaxMil { get; set; } = 80; }
    private class DockyardsFormula { public double GdpPerDockyard { get; set; } = 60; public int MinDock { get; set; } = 0; public int MaxDock { get; set; } = 30; }
    private class ManpowerPoolFormula { public double ManpowerPercent { get; set; } = 1.0; }
    private class StartingDivisionsFormula { public int TroopsPerDivision { get; set; } = 15000; public int MinDivisions { get; set; } = 1; public int MaxDivisions { get; set; } = 120; }
    private class PoliticsFormula
    {
        public int StartingPP { get; set; } = 30;
        public Dictionary<string, int> StabilityByRegime { get; set; } = new();
        public int WarSupportBase { get; set; } = 15;
        public int WarSupportNuclearBonus { get; set; } = 5;
        public Dictionary<string, int> WarSupportByRegime { get; set; } = new();
    }
    private class ResearchSlotsFormula
    {
        public int HighGdpThreshold { get; set; } = 2000;
        public int MediumGdpThreshold { get; set; } = 200;
        public int HighSlots { get; set; } = 4;
        public int MediumSlots { get; set; } = 3;
        public int LowSlots { get; set; } = 2;
    }
    private class DefaultsFormula
    {
        public int ActiveMilitaryThousands { get; set; } = 50;
        public double DefenseBudgetPercentGdp { get; set; } = 2.0;
        public string OilClass { get; set; } = "none";
        public string SteelClass { get; set; } = "low";
        public string AluminumClass { get; set; } = "none";
        public string ChromiumClass { get; set; } = "none";
        public string TungstenClass { get; set; } = "none";
        public string RubberClass { get; set; } = "none";
    }

    // M17.4: Teknoloji seviyesi (1-4) — kişi başı GSYİH ve savunma bütçesinden
    private class TechnologyLevelFormula
    {
        public double GdpPerCapitaWeight { get; set; } = 1.0;     // GSYİH/kişi (bin $) çarpanı
        public double DefenseBudgetWeight { get; set; } = 2.0;      // Savunma bütçesi % çarpanı
        public int Level4Threshold { get; set; } = 35;
        public int Level3Threshold { get; set; } = 15;
        public int Level2Threshold { get; set; } = 5;
        // Araştırma slotları seviyeye göre (boşsa GDP eşikleri kullanılır)
        public Dictionary<int, int> ResearchSlotsByTechLevel { get; set; } = new();
        // M17.4: başlangıç teknolojileri — seviyenin araştırmış sayıldığı Era eşiği
        // (0 = hiçbir teknoloji araştırılmamış başla; boşsa başlangıç teknolojisi yok)
        public Dictionary<int, int> StartingTechEraByTechLevel { get; set; } = new();
    }

    // M17.4: Modern ekipman başlangıcı — tümen sayısı × teknoloji seviyesi çarpanı
    // StockPerDivisionByTechLevel boşsa formül devre dışıdır (eski davranış korunur)
    private class EquipmentStartFormula
    {
        public double BaseStockPerDivision { get; set; } = 1.0;    // temel çarpan (1.0 = tam donanım)
        public Dictionary<int, double> StockPerDivisionByTechLevel { get; set; } = new();
    }

    private static Formulas LoadFormulas()
    {
        // M17.4: formüller senaryo başına bir kez yüklenir (cache)
        if (_formulasCached != null) return _formulasCached;
        var def = new Formulas();
        if (string.IsNullOrEmpty(FormulasPath) || !Godot.FileAccess.FileExists(FormulasPath))
        {
            _formulasCached = def; // default formüller
            return _formulasCached;
        }

        try
        {
            string json = Godot.FileAccess.GetFileAsString(FormulasPath);
            var loaded = JsonSerializer.Deserialize<Formulas>(json, new JsonSerializerOptions { IncludeFields = true });
            _formulasCached = loaded ?? def;
            return _formulasCached;
        }
        catch (Exception e)
        {
            GD.PrintErr($"CountryDatabase: formulas.json okunamadı, varsayılan kullanılıyor: {e.Message}");
            _formulasCached = def;
            return _formulasCached;
        }
    }

    private static Formulas _formulasCached;

    // M17.4: Ülkenin teknoloji seviyesine göre ekipman stok çarpanı.
    // EquipmentStart formülü yoksa 0 döner → EquipmentSystem eski davranışı kullanır.
    public static double GetEquipmentStockMultiplier(CountryData data)
    {
        if (data == null) return 0;
        var f = LoadFormulas();
        if (f.EquipmentStart.StockPerDivisionByTechLevel.Count == 0) return 0;
        return f.EquipmentStart.BaseStockPerDivision
             * f.EquipmentStart.StockPerDivisionByTechLevel.GetValueOrDefault(data.TechLevel, 0);
    }

    // M17.4: Ülkenin teknoloji seviyesi (1-4); klasik senaryoda 1 döner
    public static int GetTechLevel(string countryId)
    {
        var c = Get(countryId);
        return c?.TechLevel ?? 1;
    }

    // M17.4: Ülkenin başlangıç teknolojisi Era eşiği — Era <= eşik teknolojiler
    // başlangıçta araştırılmış sayılır. 0 veya formül yoksa başlangıç teknolojisi yok
    // (1936 senaryosu etkilenmez).
    public static int GetStartingTechEra(CountryData data)
    {
        if (data == null) return 0;
        var f = LoadFormulas();
        if (f.TechnologyLevel.StartingTechEraByTechLevel.Count == 0) return 0;
        return f.TechnologyLevel.StartingTechEraByTechLevel.GetValueOrDefault(data.TechLevel, 0);
    }

    private static CountryData DeriveFromCountryInput(CountryInput c, Formulas f, string name, string capital)
    {
        // Varsayılanları uygula
        double gdp = c.GdpBillionUsd;
        int activeMil = c.ActiveMilitaryThousands ?? f.Defaults.ActiveMilitaryThousands;
        double defenseBudget = c.DefenseBudgetPercentGdp ?? f.Defaults.DefenseBudgetPercentGdp;
        string oilClass = string.IsNullOrEmpty(c.Oil) ? f.Defaults.OilClass : c.Oil;
        string steelClass = string.IsNullOrEmpty(c.Steel) ? f.Defaults.SteelClass : c.Steel;
        string aluminumClass = string.IsNullOrEmpty(c.Aluminum) ? f.Defaults.AluminumClass : c.Aluminum;
        string chromiumClass = string.IsNullOrEmpty(c.Chromium) ? f.Defaults.ChromiumClass : c.Chromium;
        string tungstenClass = string.IsNullOrEmpty(c.Tungsten) ? f.Defaults.TungstenClass : c.Tungsten;
        string rubberClass = string.IsNullOrEmpty(c.Rubber) ? f.Defaults.RubberClass : c.Rubber;
        string regime = string.IsNullOrEmpty(c.RegimeType) ? "democracy" : c.RegimeType;
        string ideology = string.IsNullOrEmpty(c.Ideology) ? "Neutral" : c.Ideology;

        // Fabrikalar
        int civ = (int)Math.Clamp(gdp / f.CivFactories.GdpPerCivFactory, f.CivFactories.MinCiv, f.CivFactories.MaxCiv);
        int mil = (int)Math.Clamp((gdp * defenseBudget / 100.0) / f.MilFactories.GdpPerMilFactory, f.MilFactories.MinMil, f.MilFactories.MaxMil);
        int dock = (int)Math.Clamp(gdp / f.Dockyards.GdpPerDockyard, f.Dockyards.MinDock, f.Dockyards.MaxDock);

        // Nüfus & Manpower
        long pop = (long)(c.PopulationMillions * 1_000_000);
        long manpower = (long)(pop * f.ManpowerPool.ManpowerPercent / 100.0);

        // Başlangıç tümenleri
        int divs = (int)Math.Clamp(activeMil * 1000.0 / f.StartingDivisions.TroopsPerDivision, f.StartingDivisions.MinDivisions, f.StartingDivisions.MaxDivisions);

        // Kaynaklar
        int oil = f.ResourceUnits.GetValueOrDefault(oilClass, 0);
        int steel = f.ResourceUnits.GetValueOrDefault(steelClass, 0);
        int aluminum = f.ResourceUnits.GetValueOrDefault(aluminumClass, 0);
        int chromium = f.ResourceUnits.GetValueOrDefault(chromiumClass, 0);
        int tungsten = f.ResourceUnits.GetValueOrDefault(tungstenClass, 0);
        int rubber = f.ResourceUnits.GetValueOrDefault(rubberClass, 0);

        // Politika
        int pp = f.Politics.StartingPP;
        int stability = f.Politics.StabilityByRegime.GetValueOrDefault(regime, 60);
        int warSupport = f.Politics.WarSupportBase
                       + (c.NuclearPower ? f.Politics.WarSupportNuclearBonus : 0)
                       + f.Politics.WarSupportByRegime.GetValueOrDefault(regime, 0);

        // M17.4: Teknoloji seviyesi (1-4) — kişi başı GSYİH + savunma bütçesi puanı
        double gdpPerCapitaThousands = c.PopulationMillions > 0 ? gdp / c.PopulationMillions : 0;
        double techScore = gdpPerCapitaThousands * f.TechnologyLevel.GdpPerCapitaWeight
                         + defenseBudget * f.TechnologyLevel.DefenseBudgetWeight;
        int techLevel;
        if (techScore >= f.TechnologyLevel.Level4Threshold) techLevel = 4;
        else if (techScore >= f.TechnologyLevel.Level3Threshold) techLevel = 3;
        else if (techScore >= f.TechnologyLevel.Level2Threshold) techLevel = 2;
        else techLevel = 1;

        // Araştırma slotları — M17.4: teknoloji seviyesine göre (boşsa GDP eşikleri)
        int rslots;
        if (f.TechnologyLevel.ResearchSlotsByTechLevel.Count > 0)
            rslots = f.TechnologyLevel.ResearchSlotsByTechLevel.GetValueOrDefault(techLevel, f.ResearchSlots.LowSlots);
        else if (gdp > f.ResearchSlots.HighGdpThreshold) rslots = f.ResearchSlots.HighSlots;
        else if (gdp > f.ResearchSlots.MediumGdpThreshold) rslots = f.ResearchSlots.MediumSlots;
        else rslots = f.ResearchSlots.LowSlots;

        return new CountryData
        {
            Id = c.Id,
            Name = string.IsNullOrEmpty(name) ? c.Id : name,
            Capital = capital,
            Ideology = ideology,
            Population = pop,
            ManpowerPool = manpower,
            CivFactories = civ,
            MilFactories = mil,
            Dockyards = dock,
            Oil = oil,
            Rubber = rubber,
            Steel = steel,
            Aluminum = aluminum,
            Tungsten = tungsten,
            Chromium = chromium,
            PoliticalPower = pp,
            StabilityPercent = stability,
            WarSupportPercent = warSupport,
            ResearchSlots = rslots,
            StartingDivisions = divs,
            TechLevel = techLevel
        };
    }

    // M17.1/17.2: Senaryo değişince cache'i temizle — yeni senaryodan yeniden yüklensin
    public static void ResetForScenario()
    {
        _byId.Clear();
        _loaded = false;
        _formulasCached = null; // M17.4: formül cache'i de senaryoya bağlı
    }

    // M17.1: Senaryo verisi yükle (ülke gerekmez) — LogLoadCountsForAll için
    public static void LoadScenarioData()
    {
        EnsureLoaded();
    }

    // M12: SaveSystem tarafından çağrılır — ülke verilerini kayıttan yükle
    public static void LoadSaveData(CountryData[] countries)
    {
        _byId.Clear();
        if (countries == null) return;
        foreach (var c in countries)
            if (c?.Id != null) _byId[c.Id] = c;
        _loaded = true;
        GD.Print($"CountryDatabase: {_byId.Count} ülke state'i yüklendi (save/load)");
    }
}