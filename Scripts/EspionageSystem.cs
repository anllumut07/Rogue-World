using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// M29: Casusluk v2 — casus niteliği modeli.
/// Casus: doğum kültürü, diller (0-100), kültür bilgisi, bölge deneyimi, eğitim,
/// sadakat, gözlem, soğukkanlılık. Deterministik üretim (DeterministicRNG, new Random yok),
/// kaydedilebilir (GetSaveData/LoadSaveData/ResetForNewGame).
/// UI metinleri İngilizce, yorumlar Türkçe.
/// </summary>
public static class EspionageSystem
{
    public class Spy
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string BirthCulture { get; set; }         // ülke kültürüne göre (LeaderSystem.GetCultureKey)
        public Dictionary<string, int> Languages { get; set; } = new(); // dil → seviye 0-100
        public int CultureKnowledge { get; set; }         // 0-100
        public string RegionExperience { get; set; }      // primary region
        public int RegionExperienceLevel { get; set; }    // 0-100
        public int Education { get; set; }                // 0-100
        public int Loyalty { get; set; }                  // 0-100
        public int Observation { get; set; }              // 0-100
        public int ColdBlood { get; set; }                // soğukkanlılık 0-100
    }

    public class SpySave
    {
        public string Id = "";
        public string Name = "";
        public string BirthCulture = "";
        public string[] LanguageIds = Array.Empty<string>();
        public int[] LanguageLevels = Array.Empty<int>();
        public int CultureKnowledge = 0;
        public string RegionExperience = "";
        public int RegionExperienceLevel = 0;
        public int Education = 0;
        public int Loyalty = 0;
        public int Observation = 0;
        public int ColdBlood = 0;
    }

    public class EspionageSave
    {
        public SpySave[] Spies = Array.Empty<SpySave>();
    }

    private static readonly List<Spy> _spies = new();
    private static int _nextSpyNum = 1;

    private static readonly Dictionary<string, (string[] First, string[] Last)> _namePools = new(StringComparer.OrdinalIgnoreCase)
    {
        { "germanic", (new[] { "Hans", "Klaus", "Lena", "Greta", "Dieter" }, new[] { "Müller", "Schmidt", "Weber", "Wagner", "Becker" }) },
        { "slavic",   (new[] { "Ivan", "Olga", "Dmitri", "Natasha", "Alexei" }, new[] { "Volkov", "Romanov", "Sokolov", "Novak", "Kovalev" }) },
        { "latin",    (new[] { "Carlos", "Maria", "Diego", "Lucia", "Mateo" }, new[] { "García", "Rodríguez", "Fernández", "López", "Martínez" }) },
        { "anglo",    (new[] { "John", "Mary", "Robert", "Elizabeth", "James" }, new[] { "Smith", "Jones", "Wilson", "Taylor", "Brown" }) },
        { "turkish",  (new[] { "Mehmet", "Ayşe", "Ali", "Emine", "Mustafa" }, new[] { "Yılmaz", "Kaya", "Demir", "Şahin", "Çelik" }) },
        { "arabic",   (new[] { "Omar", "Layla", "Karim", "Nadia", "Farid" }, new[] { "Al-Farsi", "Nasser", "Mansour", "Rashid", "Salem" }) },
    };

    private static readonly string[] _regions = {
        "Western Europe", "Eastern Europe", "Southern Europe", "Northern Europe",
        "Middle East", "North Africa", "East Asia", "South Asia", "Southeast Asia",
        "North America", "South America"
    };

    public static int SpyCount => _spies.Count;
    public static IReadOnlyList<Spy> Spies => _spies;

    /// <summary>Deterministik casus üretimi. Aynı oyun tohumunda tekrar edilebilir.</summary>
    public static Spy GenerateSpy(string countryId)
    {
        string culture = LeaderSystem.GetCultureKey(countryId);
        var pool = _namePools.TryGetValue(culture, out var p) ? p : _namePools["anglo"];

        var spy = new Spy
        {
            Id = $"spy-{_nextSpyNum++}",
            Name = $"{DeterministicRNG.NextChoice(pool.First)} {DeterministicRNG.NextChoice(pool.Last)}",
            BirthCulture = culture,
            Languages = GenerateLanguages(),
            CultureKnowledge = DeterministicRNG.NextInt(0, 101),
            RegionExperience = DeterministicRNG.NextChoice(_regions),
            RegionExperienceLevel = DeterministicRNG.NextInt(0, 101),
            Education = DeterministicRNG.NextInt(0, 101),
            Loyalty = DeterministicRNG.NextInt(0, 101),
            Observation = DeterministicRNG.NextInt(0, 101),
            ColdBlood = DeterministicRNG.NextInt(0, 101),
        };
        _spies.Add(spy);
        GD.Print($"[EspionageSystem] Casus üretildi: {spy.Name} ({spy.Id}, kültür={culture})");
        return spy;
    }

    private static Dictionary<string, int> GenerateLanguages()
    {
        var langs = new Dictionary<string, int>();
        int count = DeterministicRNG.NextInt(1, 4); // 1-3 dil
        var all = new[] { "English", "German", "French", "Russian", "Spanish", "Turkish", "Arabic", "Chinese" };
        DeterministicRNG.Shuffle(all);
        for (int i = 0; i < count; i++)
            langs[all[i]] = DeterministicRNG.NextInt(0, 101);
        return langs;
    }

    /// <summary>Casus kartı görünümü (metin özeti).</summary>
    public static string GetSpyCard(string spyId)
    {
        var s = _spies.FirstOrDefault(s => s.Id == spyId);
        if (s == null) return "Casus bulunamadı.";
        var langs = string.Join(", ", s.Languages.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"[SpyCard] {s.Name} ({s.Id}) | BirthCulture={s.BirthCulture} | Languages={langs} | CultureKnowledge={s.CultureKnowledge} | Region={s.RegionExperience} ({s.RegionExperienceLevel}) | Education={s.Education} | Loyalty={s.Loyalty} | Observation={s.Observation} | ColdBlood={s.ColdBlood}";
    }

    public static EspionageSave GetSaveData()
    {
        return new EspionageSave
        {
            Spies = _spies.Select(s => new SpySave
            {
                Id = s.Id, Name = s.Name, BirthCulture = s.BirthCulture,
                LanguageIds = s.Languages.Keys.ToArray(), LanguageLevels = s.Languages.Values.ToArray(),
                CultureKnowledge = s.CultureKnowledge, RegionExperience = s.RegionExperience,
                RegionExperienceLevel = s.RegionExperienceLevel, Education = s.Education,
                Loyalty = s.Loyalty, Observation = s.Observation, ColdBlood = s.ColdBlood,
            }).ToArray()
        };
    }

    public static void LoadSaveData(EspionageSave data)
    {
        _spies.Clear();
        if (data?.Spies != null)
        {
            foreach (var t in data.Spies)
            {
                _spies.Add(new Spy
                {
                    Id = t.Id, Name = t.Name, BirthCulture = t.BirthCulture,
                    Languages = t.LanguageIds.Zip(t.LanguageLevels, (k, v) => new { k, v }).ToDictionary(x => x.k, x => x.v),
                    CultureKnowledge = t.CultureKnowledge, RegionExperience = t.RegionExperience,
                    RegionExperienceLevel = t.RegionExperienceLevel, Education = t.Education,
                    Loyalty = t.Loyalty, Observation = t.Observation, ColdBlood = t.ColdBlood,
                });
            }
            _nextSpyNum = _spies.Count + 1;
        }
        GD.Print($"Espionage: state yüklendi — casus={_spies.Count}");
    }

    public static void ResetForNewGame()
    {
        _spies.Clear();
        _nextSpyNum = 1;
        GD.Print("Espionage: yeni oyun için sıfırlandı.");
    }
}
