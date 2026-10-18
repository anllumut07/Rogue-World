using System;
using System.Collections.Generic;

// M24.1: Oyuncu karakteri modeli — gerçek kişi adı yok, tüm veriler kurgusal/deterministiktir.
// Nitelikler ve köken bonusları Save/Load'a dahil edilir (M24.4).
public partial class Character
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int Age { get; set; } = 35;
    public string Gender { get; set; } = "Male";
    public string BirthCountryId { get; set; } = "";
    public string CultureKey { get; set; } = "anglo";
    public string Background { get; set; } = "military"; // military, law, business, academic, activist

    // Nitelikler (1-100)
    public int Diplomacy { get; set; }
    public int Administration { get; set; }
    public int Intelligence { get; set; }
    public int Charisma { get; set; }
    public int Military { get; set; }
    public int Economy { get; set; }
    public int Law { get; set; }

    public List<string> Traits { get; set; } = new();

    // Portre verisi (prosedürel çizim için, deterministik)
    public string SkinTone { get; set; } = "";
    public string HairColor { get; set; } = "";
    public string ClothingStyle { get; set; } = "";

    public int Health { get; set; } = 100;

    public string FullName => $"{FirstName} {LastName}".Trim();

    // M24.4: Temel aile / halefiyet (basit)
    public string SpouseId { get; set; } = "";
    public bool IsMarried { get; set; }

    public static readonly string[] Backgrounds = { "military", "law", "business", "academic", "activist" };
    public static readonly string[] Genders = { "Male", "Female", "Other" };

    public static string BackgroundLabel(string background) => background switch
    {
        "military" => "Military Officer",
        "law" => "Lawyer",
        "business" => "Businessperson",
        "academic" => "Academic",
        "activist" => "Activist",
        _ => background
    };

    public static string BackgroundDescription(string background) => background switch
    {
        "military" => "Military background: Military +15, Administration and Intelligence +5.",
        "law" => "Lawyer background: Law +15, Administration +10, Diplomacy +5.",
        "business" => "Businessperson background: Economy +15, Administration +10, Charisma +5.",
        "academic" => "Academic background: Intelligence +15, Diplomacy and Administration +5.",
        "activist" => "Activist background: Charisma +15, Diplomacy and Intelligence +5.",
        _ => ""
    };

    // Köken → başlangıç nitelik bonusu (her köken bir ana nitelikte +15, iki Yardımcında +5)
    public static void ApplyBackgroundBonus(string background, ref int diplomacy, ref int administration,
        ref int intelligence, ref int charisma, ref int military, ref int economy, ref int law)
    {
        switch (background)
        {
            case "military": military += 15; administration += 5; intelligence += 5; break;
            case "law": law += 15; administration += 10; diplomacy += 5; break;
            case "business": economy += 15; administration += 10; charisma += 5; break;
            case "academic": intelligence += 15; diplomacy += 5; administration += 5; break;
            case "activist": charisma += 15; diplomacy += 10; intelligence += 5; break;
        }
    }

    // Deterministik karakter üretimi: isim, yaş, cinsiyet, portre verisi (yeni oyun başlangıcı için)
    public static Character GenerateRandom(string birthCountryId, string cultureKey, ulong seed)
    {
        var rng = new LocalRng(seed == 0 ? 0x9E3779B97F4A7C15UL : seed);

        var namePool = LeaderSystemGetNamePool(cultureKey);
        string firstName = "Alex", lastName = "Smith";
        if (namePool != null && namePool.FirstNames.Count > 0 && namePool.LastNames.Count > 0)
        {
            firstName = namePool.FirstNames[rng.NextInt(0, namePool.FirstNames.Count)];
            lastName = namePool.LastNames[rng.NextInt(0, namePool.LastNames.Count)];
        }

        string gender = Genders[rng.NextInt(0, Genders.Length)];
        int age = rng.NextInt(18, 70);
        string background = Backgrounds[rng.NextInt(0, Backgrounds.Length)];

        // Baz nitelikler 30-70 arası
        int dip = 30 + rng.NextInt(0, 41);
        int adm = 30 + rng.NextInt(0, 41);
        int intel = 30 + rng.NextInt(0, 41);
        int cha = 30 + rng.NextInt(0, 41);
        int mil = 30 + rng.NextInt(0, 41);
        int eco = 30 + rng.NextInt(0, 41);
        int law = 30 + rng.NextInt(0, 41);

        ApplyBackgroundBonus(background, ref dip, ref adm, ref intel, ref cha, ref mil, ref eco, ref law);

        // Portre renkleri (deterministik)
        string[] skinTones = { "#E0AC69", "#C18F63", "#9A6B4A", "#7A563A", "#5A3E2A", "#3A2A1A" };
        string[] hairColors = { "#2B221A", "#4A3A2A", "#8B6B4A", "#C9A227", "#FFD700", "#1A1A1A", "#5A4A3A" };
        string[] clothingStyles = { "Formal", "Casual", "Uniform", "Academic", "Activist" };

        var c = new Character
        {
            BirthCountryId = birthCountryId,
            CultureKey = cultureKey,
            FirstName = firstName,
            LastName = lastName,
            Age = age,
            Gender = gender,
            Background = background,
            Diplomacy = Math.Clamp(dip, 1, 100),
            Administration = Math.Clamp(adm, 1, 100),
            Intelligence = Math.Clamp(intel, 1, 100),
            Charisma = Math.Clamp(cha, 1, 100),
            Military = Math.Clamp(mil, 1, 100),
            Economy = Math.Clamp(eco, 1, 100),
            Law = Math.Clamp(law, 1, 100),
            SkinTone = skinTones[rng.NextInt(0, skinTones.Length)],
            HairColor = hairColors[rng.NextInt(0, hairColors.Length)],
            ClothingStyle = clothingStyles[rng.NextInt(0, clothingStyles.Length)],
            Health = 100
        };

        // Özellikler (1-3 trait, arka plan ile uyumlu)
        var traitPool = BackgroundTraits(background);
        int traitCount = 1 + rng.NextInt(0, 3);
        var shuffled = new List<string>(traitPool);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        for (int i = 0; i < Math.Min(traitCount, shuffled.Count); i++)
            c.Traits.Add(shuffled[i]);

        return c;
    }

    private static string[] BackgroundTraits(string background) => background switch
    {
        "military" => new[] { "Strategist", "Disciplined", "Hawk", "Loyal", "Authoritative" },
        "law" => new[] { "Diplomat", "Technocrat", "Reformist", "Idealist", "Pragmatist" },
        "business" => new[] { "Entrepreneur", "Pragmatist", "Charismatic", "Ambitious", "Technocrat" },
        "academic" => new[] { "Technocrat", "Idealist", "Diplomat", "Reformist", "Analytical" },
        "activist" => new[] { "Populist", "Charismatic", "Idealist", "Ambitious", "Opportunist" },
        _ => new[] { "Pragmatist" }
    };

    // Yerel isim havuzu erişimi (LeaderSystem'deki _namePools özel olduğundan burada kopyalanır)
    private class NamePool
    {
        public List<string> FirstNames { get; set; } = new();
        public List<string> LastNames { get; set; } = new();
    }

    private static readonly Dictionary<string, NamePool> _pools = new();
    private static bool _poolsLoaded;

    private static NamePool LeaderSystemGetNamePool(string cultureKey)
    {
        if (!_poolsLoaded)
        {
            _poolsLoaded = true;
            const string baseDir = "Data/names/";
            if (Godot.DirAccess.DirExistsAbsolute(baseDir))
            {
                var dir = Godot.DirAccess.Open(baseDir);
                if (dir != null)
                {
                    dir.ListDirBegin();
                    string file = dir.GetNext();
                    while (file != "")
                    {
                        if (!dir.CurrentIsDir() && file.EndsWith(".json"))
                        {
                            try
                            {
                                string key = System.IO.Path.GetFileNameWithoutExtension(file);
                                string json = System.IO.File.ReadAllText(System.IO.Path.Combine(baseDir, file));
                                var pool = System.Text.Json.JsonSerializer.Deserialize<NamePool>(json,
                                    new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
                                if (pool?.FirstNames?.Count > 0 && pool.LastNames?.Count > 0)
                                    _pools[key] = pool;
                            }
                            catch { /* atla */ }
                        }
                        file = dir.GetNext();
                    }
                    dir.ListDirEnd();
                }
            }
        }
        return _pools.GetValueOrDefault(cultureKey) ?? _pools.GetValueOrDefault("anglo");
    }

    // Lokal deterministik RNG (xorshift64*, global DeterministicRNG state'ini kirletmez)
    internal struct LocalRng
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
    }
}
