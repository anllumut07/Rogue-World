using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
        public TrainingCourseSave Training = new(); // M29.4: aktif eğitim kursu durumu
        public RentalPoolSave RentalPool = new(); // M29.5: kiralama havuzu durumu
    }

    public class TrainingCourseSave
    {
        public string SpyId = "";
        public int CourseType;          // enum değeri, 0 = yok
        public int DaysRemaining;       // gün cinsinden kalan süre (hafta*7)
        public int StartedDay;
        public int CompletedCount;
        public int TotalSpentPP;
        public bool LastSuccess;
    }

    // ===== M29.5: Casus kiralama havuzu — farklı kültür/diaspora kökenli kiralık casuslar =====
    // Havuzdaki her casusun ayrı bir kiralama maliyeti (PP) vardır. Kiralandığında casus
    // aktif casus listesine (_spies) geçer ve havuzdan çıkarılarak yenilenir.
    public class RentalSpy
    {
        public string Id = "";
        public string Name = "";
        public string BirthCulture = "";        // diaspora kılığı (LeaderSystem.GetCultureKey anahtarı)
        public Dictionary<string, int> Languages = new(); // dil → seviye 0-100
        public int CultureKnowledge;
        public string RegionExperience = "";
        public int RegionExperienceLevel;
        public int Education;
        public int Loyalty;
        public int Observation;
        public int ColdBlood;
        public int HireCostPP;
        public bool Available = true;
    }

    public class RentalSpySave
    {
        public string Id = "";
        public string Name = "";
        public string BirthCulture = "";
        public string[] LanguageIds = Array.Empty<string>();
        public int[] LanguageLevels = Array.Empty<int>();
        public int CultureKnowledge;
        public string RegionExperience = "";
        public int RegionExperienceLevel;
        public int Education;
        public int Loyalty;
        public int Observation;
        public int ColdBlood;
        public int HireCostPP;
        public bool Available;
    }

    public class RentalPoolSave
    {
        public RentalSpySave[] Spies = Array.Empty<RentalSpySave>();
        public int NextRentalSpyNum = 1;
        public int LastRefreshDay = 0;
        public int TotalHiredPP = 0;
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

    // ===== M29.2: Yatkınlık matrisi (kültür grubu arası benzerlik %) =====
    private const string AffinityPath = "res://Data/espionage_affinity.json";
    private static readonly Dictionary<string, Dictionary<string, int>> _affinity = new(StringComparer.OrdinalIgnoreCase);
    private static bool _affinityLoaded;

    public static void EnsureAffinityLoaded()
    {
        if (_affinityLoaded) return;
        _affinityLoaded = true;
        try
        {
            if (!Godot.FileAccess.FileExists(AffinityPath))
            {
                GD.PrintErr($"[EspionageSystem] Yatkınlık matrisi yok: {AffinityPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(AffinityPath);
            using var doc = JsonDocument.Parse(json);
            foreach (var src in doc.RootElement.EnumerateObject())
            {
                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var tgt in src.Value.EnumerateObject())
                {
                    if (tgt.Value.ValueKind == JsonValueKind.Number)
                        map[tgt.Name] = tgt.Value.GetInt32();
                }
                _affinity[src.Name] = map;
            }
            GD.Print($"[EspionageSystem] Yatkınlık matrisi yüklendi: {_affinity.Count} kaynak kültür.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[EspionageSystem] Yatkınlık matrisi yükleme HATA: {e.Message}");
        }
    }

    /// <summary>Kaynak kültürden hedef ülkeye yatkınlık % (0-100). Hedef ülkenin kültürü LeaderSystem'den alınır.</summary>
    public static int GetAffinity(string sourceCulture, string targetCountryId)
    {
        EnsureAffinityLoaded();
        string targetCulture = LeaderSystem.GetCultureKey(targetCountryId);
        if (_affinity.TryGetValue(sourceCulture, out var map))
        {
            if (map.TryGetValue(targetCulture, out var v)) return v;
            if (map.TryGetValue("default", out var d)) return d;
        }
        if (_affinity.TryGetValue("default", out var g))
        {
            if (g.TryGetValue("default", out var gv)) return gv;
        }
        return 30;
    }

    /// <summary>Belirli bir casusun hedef ülkeye yatkınlığı % (0-100).</summary>
    public static int GetAffinityForSpy(string spyId, string targetCountryId)
    {
        var s = _spies.FirstOrDefault(s => s.Id == spyId);
        if (s == null) return 30;
        return GetAffinity(s.BirthCulture, targetCountryId);
    }

    // ===== M29.3: Yakalanma olasılığı =====
    // Formül: base × (1.5 − yatkınlık/100) × karşıİstihbarat × (1 − eğitimBonusu)
    // eğitimBonusu = Education / 100 (0-1). Sonuç [0, 1] clamp'lenir.
    // Düşük yatkınlık (< 40) → yakalanma riski uyarısı.
    private const double CaptureBaseChance = 0.10;  // %10 taban
    private const double CaptureAffinityWarningThreshold = 40;

    /// <summary>Casusun hedef ülkede yakalanma olasılığı (0-1, %100 tabanlı olasılık değil oran).</summary>
    public static double GetCaptureChance(string spyId, string targetCountryId, double counterIntel)
    {
        var s = _spies.FirstOrDefault(s => s.Id == spyId);
        if (s == null) return 1.0; // bilinmeyen casus = maksimum risk
        if (counterIntel < 0) counterIntel = 0;
        if (counterIntel > 1) counterIntel = 1;

        int affinity = GetAffinity(s.BirthCulture, targetCountryId);
        double educationBonus = Math.Clamp(s.Education / 100.0, 0.0, 1.0);

        double chance = CaptureBaseChance
            * (1.5 - affinity / 100.0)
            * counterIntel
            * (1.0 - educationBonus);

        return Math.Clamp(chance, 0.0, 1.0);
    }

    /// <summary>Düşük yatkınlık nedeniyle yakalanma riski uyarısı gösterilsin mi?</summary>
    public static bool IsCaptureRiskWarning(string spyId, string targetCountryId)
    {
        var s = _spies.FirstOrDefault(s => s.Id == spyId);
        if (s == null) return true;
        return GetAffinity(s.BirthCulture, targetCountryId) < CaptureAffinityWarningThreshold;
    }

    /// <summary>Risk seviyesi: Low / Moderate / High / Critical.</summary>
    public static string GetCaptureRiskLevel(string spyId, string targetCountryId, double counterIntel)
    {
        double p = GetCaptureChance(spyId, targetCountryId, counterIntel) * 100.0;
        if (p >= 40) return "Critical";
        if (p >= 20) return "High";
        if (p >= 8) return "Moderate";
        return "Low";
    }

    // ===== M29.4: Eğitim kursları — casus eğitim kursları (dil, kültür, saha) =====
    // Süre hafta cinsinden (gün = hafta*7), PP maliyeti var, casus kurs sırasında meşguldür,
    // tamamlama da deterministik nitelik artışı sağlar.
    public enum TrainingCourseType
    {
        None = 0,
        Language,   // Dil kursu — rastgele bilinen bir dili +15 artırır
        Culture,    // Kültür eğitimi — CultureKnowledge +20
        Field,      // Saha eğitimi — Observation +10, ColdBlood +5
    }

    public class TrainingCourseDef
    {
        public TrainingCourseType Type;
        public string Name;
        public int DurationWeeks;
        public int DurationDays => DurationWeeks * 7;
        public int CostPP;
    }

    // Tanımlı kurslar: (dil 2 hafta/50 PP, kültür 3 hafta/75 PP, saha 4 hafta/100 PP)
    private static readonly TrainingCourseDef[] _courses = new[]
    {
        new TrainingCourseDef { Type = TrainingCourseType.Language, Name = "Language Course",   DurationWeeks = 2, CostPP = 50 },
        new TrainingCourseDef { Type = TrainingCourseType.Culture,  Name = "Culture Training",  DurationWeeks = 3, CostPP = 75 },
        new TrainingCourseDef { Type = TrainingCourseType.Field,    Name = "Field Training",    DurationWeeks = 4, CostPP = 100 },
    };

    private static TrainingCourseType _courseType = TrainingCourseType.None;
    private static string _courseSpyId = "";
    private static int _daysRemaining;
    private static int _startedDay;
    private static int _completedCount;
    private static int _totalSpentPP;
    private static bool _lastSuccess;

    // ===== M29.5: Kiralama havuzu durumu =====
    public const int RentalPoolSize = 4;             // aynı anda havuzdaki kiralık casus sayısı
    public const int RentalRefreshIntervalDays = 30; // havuz yenileme aralığı (oyun günü)
    private const int RentalBaseCostPP = 50;          // taban kiralama maliyeti (PP)

    private static readonly List<RentalSpy> _rentalPool = new();
    private static int _nextRentalSpyNum = 1;
    private static int _lastRefreshDay = 0;
    private static int _totalHiredPP = 0;

    public static int RentalPoolCount => _rentalPool.Count(s => s.Available);
    public static IReadOnlyList<RentalSpy> RentalPool => _rentalPool;
    public static int TotalHiredPP => _totalHiredPP;

    public static TrainingCourseType CourseType => _courseType;
    public static string CourseSpyId => _courseSpyId;
    public static int DaysRemaining => _daysRemaining;
    public static int StartedDay => _startedDay;
    public static int CompletedCount => _completedCount;
    public static int TotalSpentPP => _totalSpentPP;
    public static bool LastSuccess => _lastSuccess;
    public static bool IsInTraining => _courseType != TrainingCourseType.None;

    /// <summary>Belirli bir casus meşgul mü?</summary>
    public static bool IsSpyInTraining(string spyId) => IsInTraining && _courseSpyId == spyId;

    /// <summary>Kurs tanımını döner (geçersiz türse null).</summary>
    public static TrainingCourseDef GetCourseDef(TrainingCourseType type)
    {
        if (type == TrainingCourseType.None) return null;
        int idx = (int)type - 1; // None=0 hariç, kurslar dizinin 0. indeksinden başlar
        if (idx < 0 || idx >= _courses.Length) return null;
        return _courses[idx];
    }

    /// <summary>Kurs başlatır: spy meşgul değilse, tür geçerliyse başlar; PP düşülmez (çağırıcı tarafından yönetilir).</summary>
    public static bool StartTrainingCourse(string spyId, TrainingCourseType type, int currentDay)
    {
        if (string.IsNullOrEmpty(spyId)) return false;
        if (IsInTraining) return false; // aynı anda tek kurs
        if (type == TrainingCourseType.None) return false;
        if (_spies.All(s => s.Id != spyId)) return false; // bilinmeyen casus

        var def = GetCourseDef(type);
        if (def == null) return false;

        _courseType = type;
        _courseSpyId = spyId;
        _daysRemaining = def.DurationDays;
        _startedDay = currentDay;
        _lastSuccess = false;
        GD.Print($"[EspionageSystem] Kurs başlatıldı: {def.Name} casus={spyId} ({def.DurationDays} gün, {def.CostPP} PP, başlangıç gün {currentDay})");
        return true;
    }

    /// <summary>Aktif kursu iptal eder: ilerleme kaybolur, PP iade edilmez.</summary>
    public static void CancelTrainingCourse()
    {
        if (!IsInTraining) return;
        GD.Print($"[EspionageSystem] Kurs iptal edildi: {GetCourseDef(_courseType).Name} casus={_courseSpyId}");
        _courseType = TrainingCourseType.None;
        _courseSpyId = "";
        _daysRemaining = 0;
    }

    /// <summary>Günlük tick — aktif kursun süresini azaltır, bitince deterministik nitelik artışı uygular.</summary>
    public static void DailyTick(int currentDay)
    {
        if (!IsInTraining) return;

        _daysRemaining--;
        if (_daysRemaining > 0) return;

        var def = GetCourseDef(_courseType);
        var spy = _spies.FirstOrDefault(s => s.Id == _courseSpyId);

        _completedCount++;
        _totalSpentPP += def.CostPP;
        _lastSuccess = true;

        if (spy != null)
        {
            ApplyTrainingStatChange(spy, def.Type);
            GD.Print($"[EspionageSystem] KURS TAMAMLANDI: {def.Name} casus={spy.Id} ({spy.Name}) → nitelikler güncellendi (gün {currentDay})");
        }
        else
        {
            GD.PrintErr($"[EspionageSystem] KURS TAMAMLANDI ama casus bulunamadı: {_courseSpyId} (gün {currentDay})");
        }

        _courseType = TrainingCourseType.None;
        _courseSpyId = "";
        _daysRemaining = 0;

        // M29.5: kiralama havuzu yenileme kontrolü (her gün ticks'te değerlendirilir, aralık bazlı gerçekleşir).
        RefreshRentalPoolIfNeeded(currentDay);
    }

    private static void ApplyTrainingStatChange(Spy spy, TrainingCourseType type)
    {
        switch (type)
        {
            case TrainingCourseType.Language:
                // Rastgele bilinen bir dili +15 artırır; dil yoksa keine işlem.
                var langs = spy.Languages.Keys.ToArray();
                if (langs.Length > 0)
                {
                    string lang = DeterministicRNG.NextChoice(langs);
                    spy.Languages[lang] = Math.Clamp(spy.Languages[lang] + 15, 1, 100);
                    GD.Print($"[EspionageSystem] Dil kursu: {spy.Id} dil={lang} yeni seviye={spy.Languages[lang]}");
                }
                break;
            case TrainingCourseType.Culture:
                spy.CultureKnowledge = Math.Clamp(spy.CultureKnowledge + 20, 0, 100);
                GD.Print($"[EspionageSystem] Kültür eğitimi: {spy.Id} CultureKnowledge={spy.CultureKnowledge}");
                break;
            case TrainingCourseType.Field:
                spy.Observation = Math.Clamp(spy.Observation + 10, 0, 100);
                spy.ColdBlood = Math.Clamp(spy.ColdBlood + 5, 0, 100);
                GD.Print($"[EspionageSystem] Saha eğitimi: {spy.Id} Observation={spy.Observation} ColdBlood={spy.ColdBlood}");
                break;
        }
    }

    // ===== M29.5: Kiralama havuzu mantığı =====

    /// <summary>Havuzu başlatır: hedef boyuta kadar kiralık casus üretir (yeni oyun / senaryo başlangıcı).</summary>
    public static void InitializeRentalPool(int currentDay)
    {
        _rentalPool.Clear();
        for (int i = 0; i < RentalPoolSize; i++)
            _rentalPool.Add(GenerateRentalSpy());
        _lastRefreshDay = currentDay;
        GD.Print($"[EspionageSystem] Kiralama havuzu başlatıldı: {RentalPoolCount} casus (gün {currentDay})");
    }

    /// <summary>Yeterli süre geçtiyse havuzu yeniler: kiralanmayan casusları çıkarır, yeniilleri doldurur.</summary>
    public static void RefreshRentalPoolIfNeeded(int currentDay)
    {
        if (currentDay - _lastRefreshDay < RentalRefreshIntervalDays) return;

        // Kiralanmayan (zaten havuzda kalan) casusları temizle, kiralanmışları zaten çıkarıldı.
        _rentalPool.RemoveAll(s => !s.Available);
        while (_rentalPool.Count < RentalPoolSize)
            _rentalPool.Add(GenerateRentalSpy());
        _lastRefreshDay = currentDay;
        GD.Print($"[EspionageSystem] Kiralama havuzu yenilendi: {RentalPoolCount} casus (gün {currentDay})");
    }

    private static RentalSpy GenerateRentalSpy()
    {
        // Diaspora: casusun doğum kültürü rastgele bir havuzdan (oyuncu ülkesine bağlı değil).
        var cultures = _namePools.Keys.ToList();
        string culture = DeterministicRNG.NextChoice(cultures);
        var pool = _namePools[culture];

        var spy = new RentalSpy
        {
            Id = $"rental-{_nextRentalSpyNum++}",
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
        // Maliyet: istatistik ortalamasına dayalı deterministik PP (50-150 arası).
        int avgStats = (spy.Education + spy.Observation + spy.ColdBlood + spy.CultureKnowledge) / 4;
        spy.HireCostPP = RentalBaseCostPP + avgStats;
        spy.Available = true;
        GD.Print($"[EspionageSystem] Kiralık casus üretildi: {spy.Name} ({spy.Id}, kültür={culture}, maliyet={spy.HireCostPP} PP)");
        return spy;
    }

    /// <summary>Kiralık casusu kiralar: PP düşülür, casus aktif listeye eklenir, havuzdan çıkarılır.</summary>
    /// <returns>Kiralanan casus (aktif listedeki Spy) veya null (geçersiz id / yetersiz PP / zaten kiralanmış).</returns>
    public static Spy HireSpy(string rentalSpyId, int currentDay)
    {
        if (string.IsNullOrEmpty(rentalSpyId)) return null;
        var rental = _rentalPool.FirstOrDefault(s => s.Id == rentalSpyId);
        if (rental == null || !rental.Available) return null;

        var player = GameManager.Instance?.PlayerCountryData;
        if (player == null) return null;
        if (player.PoliticalPower < rental.HireCostPP)
        {
            GD.PrintErr($"[EspionageSystem] Kiralama reddedildi: {rental.Id} yetersiz PP (gerekli={rental.HireCostPP}, mevcut={player.PoliticalPower:F0})");
            return null;
        }

        player.PoliticalPower -= rental.HireCostPP;
        rental.Available = false;
        _rentalPool.Remove(rental);
        _totalHiredPP += rental.HireCostPP;

        var spy = new Spy
        {
            Id = $"spy-{_nextSpyNum++}",
            Name = rental.Name,
            BirthCulture = rental.BirthCulture,
            Languages = new Dictionary<string, int>(rental.Languages),
            CultureKnowledge = rental.CultureKnowledge,
            RegionExperience = rental.RegionExperience,
            RegionExperienceLevel = rental.RegionExperienceLevel,
            Education = rental.Education,
            Loyalty = rental.Loyalty,
            Observation = rental.Observation,
            ColdBlood = rental.ColdBlood,
        };
        _spies.Add(spy);
        GD.Print($"[EspionageSystem] Casus kiralandı: {spy.Name} ({spy.Id}) ← {rental.Id} ({rental.BirthCulture}, {rental.HireCostPP} PP, gün {currentDay})");
        return spy;
    }

    /// <summary>Kiralık casus kartı görünümü (metin özeti).</summary>
    public static string GetRentalSpyCard(string rentalSpyId)
    {
        var s = _rentalPool.FirstOrDefault(s => s.Id == rentalSpyId);
        if (s == null) return "Kiralık casus bulunamadı.";
        var langs = string.Join(", ", s.Languages.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"[RentalSpyCard] {s.Name} ({s.Id}) | BirthCulture={s.BirthCulture} | Languages={langs} | CultureKnowledge={s.CultureKnowledge} | Region={s.RegionExperience} ({s.RegionExperienceLevel}) | Education={s.Education} | Loyalty={s.Loyalty} | Observation={s.Observation} | ColdBlood={s.ColdBlood} | HireCost={s.HireCostPP} PP";
    }

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
            }).ToArray(),
            Training = new TrainingCourseSave
            {
                SpyId = _courseSpyId,
                CourseType = (int)_courseType,
                DaysRemaining = _daysRemaining,
                StartedDay = _startedDay,
                CompletedCount = _completedCount,
                TotalSpentPP = _totalSpentPP,
                LastSuccess = _lastSuccess,
            },
            RentalPool = new RentalPoolSave
            {
                Spies = _rentalPool.Select(s => new RentalSpySave
                {
                    Id = s.Id, Name = s.Name, BirthCulture = s.BirthCulture,
                    LanguageIds = s.Languages.Keys.ToArray(), LanguageLevels = s.Languages.Values.ToArray(),
                    CultureKnowledge = s.CultureKnowledge, RegionExperience = s.RegionExperience,
                    RegionExperienceLevel = s.RegionExperienceLevel, Education = s.Education,
                    Loyalty = s.Loyalty, Observation = s.Observation, ColdBlood = s.ColdBlood,
                    HireCostPP = s.HireCostPP, Available = s.Available,
                }).ToArray(),
                NextRentalSpyNum = _nextRentalSpyNum,
                LastRefreshDay = _lastRefreshDay,
                TotalHiredPP = _totalHiredPP,
            },
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

        if (data?.Training != null)
        {
            _courseType = (TrainingCourseType)Math.Clamp(data.Training.CourseType, (int)TrainingCourseType.None, _courses.Length);
            _courseSpyId = data.Training.SpyId ?? "";
            _daysRemaining = data.Training.DaysRemaining;
            _startedDay = data.Training.StartedDay;
            _completedCount = data.Training.CompletedCount;
            _totalSpentPP = data.Training.TotalSpentPP;
            _lastSuccess = data.Training.LastSuccess;
        }

        if (data?.RentalPool != null)
        {
            _rentalPool.Clear();
            if (data.RentalPool.Spies != null)
            {
                foreach (var t in data.RentalPool.Spies)
                {
                    _rentalPool.Add(new RentalSpy
                    {
                        Id = t.Id, Name = t.Name, BirthCulture = t.BirthCulture,
                        Languages = t.LanguageIds.Zip(t.LanguageLevels, (k, v) => new { k, v }).ToDictionary(x => x.k, x => x.v),
                        CultureKnowledge = t.CultureKnowledge, RegionExperience = t.RegionExperience,
                        RegionExperienceLevel = t.RegionExperienceLevel, Education = t.Education,
                        Loyalty = t.Loyalty, Observation = t.Observation, ColdBlood = t.ColdBlood,
                        HireCostPP = t.HireCostPP, Available = t.Available,
                    });
                }
            }
            _nextRentalSpyNum = data.RentalPool.NextRentalSpyNum;
            _lastRefreshDay = data.RentalPool.LastRefreshDay;
            _totalHiredPP = data.RentalPool.TotalHiredPP;
        }

        GD.Print($"Espionage: state yüklendi — casus={_spies.Count} kurs={_courseType} kalan_gun={_daysRemaining} kiralik_havuz={_rentalPool.Count}");
    }

    public static void ResetForNewGame()
    {
        _spies.Clear();
        _nextSpyNum = 1;
        _courseType = TrainingCourseType.None;
        _courseSpyId = "";
        _daysRemaining = 0;
        _startedDay = 0;
        _completedCount = 0;
        _totalSpentPP = 0;
        _lastSuccess = false;

        // M29.5: kiralama havuzunu sıfırla.
        _rentalPool.Clear();
        _nextRentalSpyNum = 1;
        _lastRefreshDay = 0;
        _totalHiredPP = 0;

        GD.Print("Espionage: yeni oyun için sıfırlandı.");
    }
}
