using Godot;
using System.Collections.Generic;
using System.Text.Json;

// Ülke veritabanı: Data/countries_data.json'dan yükler,
// dosyada olmayan ülkeler için id-bazlı deterministik fallback üretir.
public static class CountryDatabase
{
    private const string Path = "res://Data/countries_data.json";

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
        ["PER"] = "IRN",   // Pers İran
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

        data = GenerateFallback(id, name ?? id);
        _byId[id] = data;
        return data;
    }

    public static IEnumerable<CountryData> All()
    {
        EnsureLoaded();
        return _byId.Values;
    }

    // HOI4 takma adını (SOV vb.) veri dosyasındaki ISO A3'e çevirir.
    private static string Normalize(string id)
    {
        return Aliases.TryGetValue(id, out var canonical) ? canonical : id;
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

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (!FileAccess.FileExists(Path))
        {
            GD.PrintErr($"HATA: {Path} bulunamadı!");
            return;
        }

        List<CountryData> list;
        try
        {
            list = JsonSerializer.Deserialize<List<CountryData>>(
                FileAccess.GetFileAsString(Path));
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"HATA: {Path} okunamadı: {e.Message}");
            return;
        }

        foreach (var c in list)
            if (c?.Id != null)
                _byId[c.Id] = c;

        GD.Print($"CountryDatabase hazır: {_byId.Count} ülke verisi yüklendi.");
    }
}