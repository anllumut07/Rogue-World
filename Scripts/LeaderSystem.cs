using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Lider Sistemi (M17.2): Her ülke için kurgusal bir lider üretir.
/// - Gerçek kişi adı KULLANILMAZ (Faz 2 kuralı).
/// - Rastgelelik YALNIZCA deterministiktir: senaryo+ülke ID'sinden hash'lenen
///   lokal xorshift64* (DeterministicRNG ile aynı algoritma; global state'i kirletmez,
///   böylece lider üretimi oyun RNG akışını ve A==B testini bozmaz).
/// - İsim havuzları: Data/names/*.json (ülke kültürüne göre seçilir).
/// - Lider: ad, yaş, parti, ideoloji, nitelikler, özellikler.
/// - Senaryo değişince yeniden üretilir; Save/Load destekli.
/// </summary>
public static class LeaderSystem
{
    // ===== Veri Modelleri =====

    public class Leader
    {
        public string CountryId { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public int Age { get; set; }
        public string Party { get; set; } = "";
        public string Ideology { get; set; } = "";
        public string RegimeType { get; set; } = "";

        // Nitelikler (1-100 arası)
        public int Diplomacy { get; set; }
        public int Administration { get; set; }
        public int Charisma { get; set; }
        public int Military { get; set; }
        public int Intelligence { get; set; }
        public int Economy { get; set; }

        // Özellikler (trait): "Reformist", "Hawk", "Technocrat", "Populist"...
        public List<string> Traits { get; set; } = new();

        public string FullName => $"{FirstName} {LastName}";
    }

    // Kültür havuzu (Data/names/*.json ile aynı yapı)
    private class NamePool
    {
        public List<string> FirstNames { get; set; } = new();
        public List<string> LastNames { get; set; } = new();
    }

    // countries.json (modern ham girdi) kaydı — olmayan alanlar null/default kalır
    private class CountryInput
    {
        public string Id { get; set; } = "";
        public string Ideology { get; set; } = "";
        public string RegimeType { get; set; } = "";
        public bool NuclearPower { get; set; }
        public double GdpBillionUsd { get; set; }
    }

    // Lokal deterministik RNG (xorshift64*, DeterministicRNG ile aynı çekirdek)
    private struct LocalRng
    {
        private ulong _state;
        public LocalRng(ulong seed) { _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }
        public ulong NextUlong()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }
        public int NextInt(int min, int max)
        {
            if (min >= max) return min;
            return min + (int)((NextUlong() >> 33) % (ulong)(max - min));
        }
        public double NextDouble() => (NextUlong() >> 11) * (1.0 / 9007199254740992.0);
    }

    // ===== Durum =====
    private static readonly Dictionary<string, NamePool> _namePools = new();
    private static readonly Dictionary<string, Leader> _leaders = new();
    private static bool _loaded;
    private static bool _loadLogged;

    // ISO A3 ülke kodu → isim havuzu (kültür) eşleştirmesi.
    // Eşleşmeyen ülkeler Anglo havuzuna düşer.
    private static readonly Dictionary<string, string> _cultureMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Türk
        ["TUR"] = "turkish",
        ["AZE"] = "turkish",
        ["KAZ"] = "turkish",
        ["KGZ"] = "turkish",
        ["UZB"] = "turkish",
        ["TKM"] = "turkish",

        // Germanic
        ["DEU"] = "germanic",
        ["AUT"] = "germanic",
        ["CHE"] = "germanic",
        ["BEL"] = "germanic",
        ["NLD"] = "germanic",
        ["LUX"] = "germanic",

        // Anglo
        ["USA"] = "anglo",
        ["GBR"] = "anglo",
        ["CAN"] = "anglo",
        ["AUS"] = "anglo",
        ["NZL"] = "anglo",
        ["IRL"] = "anglo",

        // Latin
        ["FRA"] = "latin",
        ["ITA"] = "latin",
        ["ESP"] = "latin",
        ["PRT"] = "latin",
        ["ROU"] = "latin",
        ["MDA"] = "latin",
        ["ARG"] = "latin",
        ["BRA"] = "latin",
        ["CHL"] = "latin",
        ["COL"] = "latin",
        ["PER"] = "latin",
        ["VEN"] = "latin",
        ["ECU"] = "latin",
        ["BOL"] = "latin",
        ["PRY"] = "latin",
        ["URY"] = "latin",
        ["MEX"] = "latin",
        ["CUB"] = "latin",
        ["DOM"] = "latin",
        ["GTM"] = "latin",
        ["CRI"] = "latin",
        ["PAN"] = "latin",

        // Slavic
        ["RUS"] = "slavic",
        ["UKR"] = "slavic",
        ["BLR"] = "slavic",
        ["POL"] = "slavic",
        ["CZE"] = "slavic",
        ["SVK"] = "slavic",
        ["SRB"] = "slavic",
        ["HRV"] = "slavic",
        ["SVN"] = "slavic",
        ["BGR"] = "slavic",
        ["MKD"] = "slavic",
        ["MNG"] = "slavic",

        // Nordic
        ["SWE"] = "nordic",
        ["NOR"] = "nordic",
        ["DNK"] = "nordic",
        ["FIN"] = "nordic",
        ["ISL"] = "nordic",

        // Greek
        ["GRC"] = "greek",
        ["CYP"] = "greek",

        // Hungarian
        ["HUN"] = "hungarian",

        // Arab
        ["SAU"] = "arabic",
        ["EGY"] = "arabic",
        ["IRQ"] = "arabic",
        ["JOR"] = "arabic",
        ["LBN"] = "arabic",
        ["SYR"] = "arabic",
        ["YEM"] = "arabic",
        ["OMN"] = "arabic",
        ["KWT"] = "arabic",
        ["QAT"] = "arabic",
        ["BHR"] = "arabic",
        ["ARE"] = "arabic",
        ["DZA"] = "arabic",
        ["MAR"] = "arabic",
        ["TUN"] = "arabic",
        ["LBY"] = "arabic",
        ["SDN"] = "arabic",
        ["SOM"] = "arabic",

        // Iranian
        ["IRN"] = "iranian",
        ["AFG"] = "iranian",
        ["TJK"] = "iranian",

        // South Asian
        ["IND"] = "south_asian",
        ["PAK"] = "south_asian",
        ["BGD"] = "south_asian",
        ["LKA"] = "south_asian",
        ["NPL"] = "south_asian",
        ["BTN"] = "south_asian",

        // East Asian
        ["CHN"] = "east_asian",
        ["JPN"] = "east_asian",
        ["KOR"] = "east_asian",
        ["PRK"] = "east_asian",
        ["TWN"] = "east_asian",

        // Southeast Asian
        ["IDN"] = "southeast_asian",
        ["MYS"] = "southeast_asian",
        ["THA"] = "southeast_asian",
        ["VNM"] = "southeast_asian",
        ["PHL"] = "southeast_asian",
        ["SGP"] = "southeast_asian",
        ["MMR"] = "southeast_asian",
        ["KHM"] = "southeast_asian",
        ["LAO"] = "southeast_asian",

        // African
        ["ZAF"] = "african",
        ["NGA"] = "african",
        ["KEN"] = "african",
        ["ETH"] = "african",
        ["TZA"] = "african",
        ["UGA"] = "african",
        ["GHA"] = "african",
        ["CIV"] = "african",
        ["SEN"] = "african",
        ["CMR"] = "african",
        ["COD"] = "african",
        ["RWA"] = "african",
        ["MLI"] = "african",
        ["BFA"] = "african",
        ["NER"] = "african",
        ["ZMB"] = "african",
        ["ZWE"] = "african",
        ["BWA"] = "african",
        ["NAM"] = "african",
        ["MDG"] = "african",
    };

    // İdeoloji → parti isim havuzları
    private static readonly Dictionary<string, string[]> _partyTemplates = new()
    {
        ["Democracy"] = new[] { "People's Party", "Democratic Party", "Liberal Party", "Progressive Alliance", "Social Democrats", "Civic Platform", "Freedom Party", "National Coalition", "Green Alliance", "Center Party" },
        ["Fascism"] = new[] { "National Unity Party", "Fatherland Front", "National Revival Movement", "Patriotic Alliance", "New Order Movement", "National Salvation Front", "Greater Nation Party", "Sovereignty Party" },
        ["Communism"] = new[] { "Communist Party", "Workers' Party", "People's Revolutionary Party", "Socialist Unity Party", "Progressive Labor Party", "Revolutionary Front", "Red Banner Party", "Proletarian Party" },
        ["Neutral"] = new[] { "National Front", "Unity Party", "People's Movement", "Civic Alliance", "Independent Bloc", "National Concord", "Stability Party", "Centrist Union" },
    };

    // Rejim tipi → olası özellik (trait) havuzu
    private static readonly Dictionary<string, string[]> _regimeTraits = new()
    {
        ["democracy"] = new[] { "Reformist", "Technocrat", "Diplomat", "Populist", "Idealist" },
        ["republic"] = new[] { "Technocrat", "Administrator", "Pragmatist", "Diplomat" },
        ["monarchy"] = new[] { "Traditionalist", "Loyal", "Diplomat", "Conservative" },
        ["authoritarian"] = new[] { "Hawk", "Hardliner", "Nationalist", "Strategist" },
        ["hybrid"] = new[] { "Pragmatist", "Opportunist", "Survivor", "Ambitious" },
        ["one_party"] = new[] { "Ideologue", "Loyal", "Hardliner", "Architect" },
    };

    // ===== Public API =====

    public static IReadOnlyDictionary<string, Leader> All => _leaders;
    public static int LoadedCount => _leaders.Count;

    public static Leader GetLeader(string countryId)
    {
        EnsureLoaded();
        return _leaders.GetValueOrDefault(countryId);
    }

    // M23.3: Seçim kaybı sonrası yeni (farklı, deterministik) lider üretir.
    // Seed'e seçim günü dahil edilir → aynı ülke ve aynı günde tekrar çağrıldığında aynı lider çıkar.
    public static Leader RegenerateLeader(string countryId, long electionDay)
    {
        EnsureLoaded();
        var current = _leaders.GetValueOrDefault(countryId);
        if (current == null) return GetLeader(countryId);

        ulong seed = HashSeed($"{ScenarioSystem.ActiveId}:{countryId}:{electionDay}");
        var leader = GenerateLeader(countryId, current.Ideology, current.RegimeType, false, 0, seed);
        _leaders[countryId] = leader;
        GD.Print($"[LeaderSystem] {countryId}: yeni lider seçildi (gün {electionDay}): {leader.FullName}");
        return leader;
    }

    // ===== M17.1: Senaryo değişikliği =====

    public static void ResetForScenario()
    {
        _leaders.Clear();
        _loaded = false;
        _loadLogged = false;
    }

    // Test/debug: senaryo verisini yükle ve logla (ülke gerekmez)
    public static void LoadScenarioData()
    {
        EnsureLoaded();
        if (!_loadLogged)
        {
            _loadLogged = true;
            GD.Print($"LeaderSystem: {_leaders.Count} kurgusal lider hazır (senaryo: {ScenarioSystem.ActiveId}, havuz: {_namePools.Count})");
        }
    }

    // ===== M12/M17.2: Save/Load =====

    public static SaveSystem.LeaderSave GetSaveData()
    {
        return new SaveSystem.LeaderSave
        {
            Leaders = _leaders.Values.Select(l => new Leader
            {
                CountryId = l.CountryId,
                FirstName = l.FirstName,
                LastName = l.LastName,
                Age = l.Age,
                Party = l.Party,
                Ideology = l.Ideology,
                RegimeType = l.RegimeType,
                Diplomacy = l.Diplomacy,
                Administration = l.Administration,
                Charisma = l.Charisma,
                Military = l.Military,
                Intelligence = l.Intelligence,
                Economy = l.Economy,
                Traits = new List<string>(l.Traits)
            }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.LeaderSave data)
    {
        _namePools.Clear(); // yeniden yüklenir ama log birikmesin
        LoadNamePools();
        _leaders.Clear();
        if (data?.Leaders != null)
        {
            foreach (var l in data.Leaders)
                if (!string.IsNullOrEmpty(l.CountryId))
                    _leaders[l.CountryId] = l;
        }
        _loaded = true;
        _loadLogged = true;
        GD.Print($"LeaderSystem: {_leaders.Count} lider state'i yüklendi (save/load)");
    }

    // ===== Yükleme =====

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        // 1) İsim havuzları
        LoadNamePools();

        // 2) Senaryonun ülke dosyası (modern: ham girdi; klasik: countries_data.json)
        string countriesPath = ScenarioSystem.ActiveCountriesPath;
        if (!Godot.FileAccess.FileExists(countriesPath))
        {
            GD.PrintErr($"LeaderSystem: {countriesPath} bulunamadı — lider üretilemedi!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true
            };
            var json = Godot.FileAccess.GetFileAsString(countriesPath);

            // Modern ham girdi dosyası mı? ("RegimeType" alanı imzası)
            bool isModernInput;
            using (var doc = JsonDocument.Parse(json))
            {
                isModernInput = doc.RootElement.ValueKind == JsonValueKind.Array
                                && doc.RootElement.GetArrayLength() > 0
                                && doc.RootElement[0].TryGetProperty("RegimeType", out _);
            }

            int count = 0;
            if (isModernInput)
            {
                var inputs = JsonSerializer.Deserialize<List<CountryInput>>(json, options);
                foreach (var c in inputs)
                {
                    if (string.IsNullOrEmpty(c.Id)) continue;
                    _leaders[c.Id] = GenerateLeader(c.Id, c.Ideology, c.RegimeType, c.NuclearPower, c.GdpBillionUsd);
                    count++;
                }
            }
            else
            {
                // Klasik format (historical_1936): RegimeType yok → Ideology'den türet
                var classics = JsonSerializer.Deserialize<List<CountryData>>(json, options);
                foreach (var c in classics)
                {
                    if (c?.Id == null || string.IsNullOrEmpty(c.Id)) continue;
                    string regime = IdeologyToRegime(c.Ideology);
                    bool nuclear = c.Ideology is "Communism" or "Fascism"; // 1936 majörleri için kaba proxy
                    _leaders[c.Id] = GenerateLeader(c.Id, c.Ideology, regime, nuclear, 0);
                    count++;
                }
            }

            GD.Print($"LeaderSystem: {count} kurgusal lider üretildi (senaryo: {ScenarioSystem.ActiveId})");
        }
        catch (Exception e)
        {
            GD.PrintErr($"LeaderSystem: ülke dosyası okunamadı: {e.Message}");
        }
    }

    // İdeology → rejim tipi eşlemesi (klasik senaryo dosyaları için)
    private static string IdeologyToRegime(string ideology)
    {
        return ideology switch
        {
            "Democracy" => "democracy",
            "Fascism" => "authoritarian",
            "Communism" => "one_party",
            _ => "republic"
        };
    }

    private static void LoadNamePools()
    {
        if (_namePools.Count > 0) return;

        const string baseDir = "Data/names/";
        if (!DirAccess.DirExistsAbsolute(baseDir))
        {
            GD.PrintErr($"LeaderSystem: {baseDir} bulunamadı!");
            return;
        }

        var dir = DirAccess.Open(baseDir);
        if (dir == null) return;

        dir.ListDirBegin();
        string file = dir.GetNext();
        while (file != "")
        {
            if (!dir.CurrentIsDir() && file.EndsWith(".json"))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                string fullPath = Path.Combine(baseDir, file);
                try
                {
                    var json = File.ReadAllText(fullPath);
                    var pool = JsonSerializer.Deserialize<NamePool>(json,
                        new JsonSerializerOptions { IncludeFields = true });
                    if (pool?.FirstNames?.Count > 0 && pool.LastNames?.Count > 0)
                        _namePools[key] = pool;
                }
                catch (Exception e)
                {
                    GD.PrintErr($"LeaderSystem: {file} okunamadı: {e.Message}");
                }
            }
            file = dir.GetNext();
        }
        dir.ListDirEnd();

        GD.Print($"LeaderSystem: {_namePools.Count} isim havuzu yüklendi ({string.Join(", ", _namePools.Keys.OrderBy(k => k))})");
    }

    // Senaryo+ülke bazlı deterministik seed → aynı senaryoda lider her zaman aynı
    private static ulong HashSeed(string text)
    {
        unchecked
        {
            ulong h = 0x9E3779B97F4A7C15UL;
            foreach (char c in text) h = (h ^ c) * 0x2545F4914F6CDD1DUL;
            return h;
        }
    }

    private static Leader GenerateLeader(string countryId, string ideology, string regimeType, bool nuclearPower, double gdpBillion, ulong? seedOverride = null)
    {
        var rng = new LocalRng(seedOverride ?? HashSeed($"{ScenarioSystem.ActiveId}:{countryId}"));

        ideology = string.IsNullOrEmpty(ideology) ? "Neutral" : ideology;
        regimeType = string.IsNullOrEmpty(regimeType) ? IdeologyToRegime(ideology) : regimeType;

        // Kültür havuzu: ülke → kültür, yoksa Anglo
        string cultureKey = _cultureMap.GetValueOrDefault(countryId, "anglo");
        var pool = _namePools.GetValueOrDefault(cultureKey)
                   ?? _namePools.GetValueOrDefault("anglo");

        string firstName, lastName;
        if (pool != null && pool.FirstNames.Count > 0 && pool.LastNames.Count > 0)
        {
            firstName = pool.FirstNames[rng.NextInt(0, pool.FirstNames.Count)];
            lastName = pool.LastNames[rng.NextInt(0, pool.LastNames.Count)];
        }
        else
        {
            firstName = "Alex";
            lastName = countryId; // son çare
        }

        // Yaş: rejim tipine göre baz ±10
        int baseAge = regimeType switch
        {
            "democracy" => 52,
            "republic" => 55,
            "monarchy" => 60,
            "authoritarian" => 62,
            "hybrid" => 58,
            "one_party" => 65,
            _ => 55
        };
        int age = Math.Clamp(baseAge + rng.NextInt(-10, 11), 35, 85);

        // Parti
        string party = _partyTemplates.TryGetValue(ideology, out var templates) && templates.Length > 0
            ? templates[rng.NextInt(0, templates.Length)]
            : "National Party";

        // Nitelikler: baz 30-70 + rejim/ideoloji/nükleer bonusları
        int dip = 30 + rng.NextInt(0, 41);
        int adm = 30 + rng.NextInt(0, 41);
        int cha = 30 + rng.NextInt(0, 41);
        int mil = 30 + rng.NextInt(0, 41);
        int intel = 30 + rng.NextInt(0, 41);
        int eco = 30 + rng.NextInt(0, 41);

        switch (regimeType)
        {
            case "democracy": dip += 10; adm += 5; cha += 5; break;
            case "republic": adm += 10; dip += 5; break;
            case "monarchy": dip += 10; cha += 10; break;
            case "authoritarian": adm += 10; mil += 10; intel += 5; break;
            case "hybrid": adm += 5; mil += 5; eco += 5; break;
            case "one_party": adm += 10; intel += 10; mil += 5; break;
        }
        switch (ideology)
        {
            case "Democracy": dip += 5; cha += 5; eco += 5; break;
            case "Fascism": mil += 10; cha += 5; adm += 5; break;
            case "Communism": adm += 10; eco += 10; intel += 5; break;
            case "Neutral": dip += 5; adm += 5; break;
        }
        if (nuclearPower) { dip += 10; mil += 10; intel += 5; }
        if (gdpBillion > 2000) { adm += 5; eco += 5; } // büyük ekonomi deneyimi

        // Özellikler (1-3 trait, rejim havuzundan)
        var traits = new List<string>();
        if (_regimeTraits.TryGetValue(regimeType, out var traitPool) && traitPool.Length > 0)
        {
            int traitCount = 1 + rng.NextInt(0, 3);
            var shuffled = traitPool.OrderBy(t => rng.NextInt(0, int.MaxValue)).ToList();
            for (int i = 0; i < Math.Min(traitCount, shuffled.Count); i++)
                traits.Add(shuffled[i]);
        }
        if (nuclearPower && traits.Count < 4)
            traits.Add("NuclearAuthority");

        return new Leader
        {
            CountryId = countryId,
            FirstName = firstName,
            LastName = lastName,
            Age = age,
            Party = party,
            Ideology = ideology,
            RegimeType = regimeType,
            Diplomacy = Math.Clamp(dip, 1, 100),
            Administration = Math.Clamp(adm, 1, 100),
            Charisma = Math.Clamp(cha, 1, 100),
            Military = Math.Clamp(mil, 1, 100),
            Intelligence = Math.Clamp(intel, 1, 100),
            Economy = Math.Clamp(eco, 1, 100),
            Traits = traits
        };
    }
}