using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Yeni doğan ülkelerin diplomatik tanınması (M48.3).
///
/// Oyun içinde üretilen her ülke (kurgu ulus kurulumu ya da ilhak edilmiş
/// bir halkın serbest bırakılması) dünyada hiçbir yerde "bilinmiyor"du:
/// kurulduğu gün her ülke onunla normal diplomatik ilişki kuruyordu. Bu
/// sistem bu boşluğu doldurur — yeni devletler tanınma kazanır.
///
/// Kurallar (tamamen deterministik, RNG YOK):
///
///   1. Ülke doğduğu an yalnızca onu doğuran taraf (ebe kaynak ya da eski
///      overlord) tanır; kendiliğinden bir taban ilişkisi de işlenir.
///   2. Her gün diğer ülkeler tanıma puanı hesaplar: kültür benzerliği +
///      ideoloji yakınlığı + komşuluk + yeniliğin oturmuşluğu + güç.
///   3. Puan eşiği aşarsa o ülke tanınır; iki yönde ilişki iyileşir ve
///      durum değişikliği loglanır.
///
/// Doğuştan önce var olan ülkeler başlangıçta tanınmış sayılır — tanınma
/// yalnızca oyun içinde doğan ülkeleri ilgilendirir, bu yüzden mevcut
/// diplomasi ve ittifak akışlarına dokunulmaz.
/// </summary>
public static class RecognitionSystem
{
    /// <summary>Oyunda doğan bir ülkenin kaydı.</summary>
    public class BirthRecord
    {
        public string CountryId = "";   // yeni ülke
        public string ParentId = "";    // onu doğuran (ebe kaynak / eski overlord)
        public long BirthDay = -1;      // GM.TotalDays
        public string Reason = "";      // "formable" | "release"
    }

    // Ülke kimliği → doğuş kaydı
    private static readonly Dictionary<string, BirthRecord> _births =
        new(StringComparer.OrdinalIgnoreCase);

    // "tanıyan|tanınan" ikilileri (büyük/küçük harf duyarsız eşleşme)
    private static readonly HashSet<string> _recognitions = new(StringComparer.OrdinalIgnoreCase);

    private static RecognitionSettings D = new();
    private static bool _loaded;
    private const string DataPath = "res://Data/recognition.json";

    private sealed class RecognitionSettings
    {
        public double RecognitionThreshold = 0.40;
        public double FullRecognitionDays = 90.0;
        public double SameCultureWeight = 0.30;
        public double SameIdeologyWeight = 0.25;
        public double NeighborWeight = 0.20;
        public double StrengthWeight = 0.10;
        public double StrengthReference = 100.0;
        public bool FounderAutomatic = true;
        public int FounderRelation = 20;
        public int ReleasedRelation = -10;
        public int RelationOnRecognition = 5;
    }

    private sealed class RecognitionRoot
    {
        public int Version { get; set; } = 1;
        public RecognitionSettings Settings { get; set; } = new();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            using var file = Godot.FileAccess.Open(DataPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PrintErr("[Recognition] recognition.json açılamadı — varsayılan ağırlıklar kullanılıyor.");
                return;
            }
            var root = JsonSerializer.Deserialize<RecognitionRoot>(file.GetAsText(), new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true
            });
            if (root?.Settings != null)
            {
                D = root.Settings;
                GD.Print($"[Recognition] Tanınma ayarları yüklendi: eşik={D.RecognitionThreshold:N2}, " +
                         $"tam_tanınma_gün={D.FullRecognitionDays:N0}");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Recognition] recognition.json okunamadı ({e.Message}) — varsayılan ağırlıklar.");
        }
    }

    // ===== Sorgular =====

    /// <summary>Oyun içinde doğan ülke sayısı.</summary>
    public static int BirthCount => _births.Count;

    /// <summary>Bu ülke oyun içinde doğdu mu?</summary>
    public static bool WasBorn(string countryId) =>
        !string.IsNullOrEmpty(countryId) && _births.ContainsKey(countryId);

    /// <summary>Doğuş günü (bilinmiyorsa -1).</summary>
    public static long BirthDay(string countryId) =>
        _births.TryGetValue(countryId ?? "", out var b) ? b.BirthDay : -1;

    /// <summary>Doğuş kaydı (bilinmiyorsa null).</summary>
    public static BirthRecord Birth(string countryId) =>
        _births.TryGetValue(countryId ?? "", out var b) ? b : null;

    /// <summary>Tanınma durumu.</summary>
    public static bool IsRecognized(string recognizerId, string targetId) =>
        _recognitions.Contains(Key(recognizerId, targetId));

    /// <summary>Hedefi tanıyan ülke sayısı.</summary>
    public static int RecognizerCount(string targetId)
    {
        int n = 0;
        foreach (var k in _recognitions)
            if (k.EndsWith("|" + targetId, StringComparison.OrdinalIgnoreCase)) n++;
        return n;
    }

    /// <summary>Tanınma kaydı sayısı.</summary>
    public static int RecognitionCount => _recognitions.Count;

    /// <summary>Tüm kurulu ülkeler bu ülkeyi tanıyor mu?</summary>
    public static bool IsFullyRecognized(string targetId) =>
        RecognizerCount(targetId) >= EstablishedCount(targetId);

    // ===== Ülke doğumu =====

    /// <summary>
    /// Oyunda yeni bir ülke doğdu. Doğuran taraf tanır ve taban ilişki işlenir.
    /// </summary>
    /// <param name="reason">"formable" (ebe kaynak böldü) ya da "release" (ilhak eden bıraktı).</param>
    public static void OnCountryBorn(string countryId, string parentId, long day, string reason)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(countryId)) return;
        if (CountryDatabase.Get(countryId) == null) return;

        if (_births.TryGetValue(countryId, out _))
        {
            GD.PrintErr($"[Recognition] {countryId} zaten doğmuş — kayıt güncellenmiyor");
            return;
        }

        _births[countryId] = new BirthRecord
        {
            CountryId = countryId,
            ParentId = parentId ?? "",
            BirthDay = day,
            Reason = reason ?? ""
        };

        var newCountry = CountryDatabase.Get(countryId);
        GD.Print($"[Recognition] YENİ ÜLKE DOĞDU: {newCountry.Name} ({countryId}) — " +
                 $"neden={reason}, doğuran={parentId ?? "—"}, gün={day}, tanınan=0");

        if (D.FounderAutomatic && !string.IsNullOrEmpty(parentId) && parentId != countryId)
        {
            Recognize(parentId, countryId, day, "doğuran");

            // Taban ilişki: iyi niyetle bölünen ile baskıyla bırakılan ayrıdır.
            int baseRelation = string.Equals(reason, "release", StringComparison.OrdinalIgnoreCase)
                ? D.ReleasedRelation
                : D.FounderRelation;
            DiplomacySystem.ModifyRelation(parentId, countryId, baseRelation);
            DiplomacySystem.ModifyRelation(countryId, parentId, baseRelation);
            GD.Print($"[Recognition] TABAN İLİŞKİ: {parentId} ↔ {countryId} = {baseRelation} " +
                     $"({reason}) — iki yön");
        }
    }

    // ===== Günlük tanınma yayılımı =====

    /// <summary>
    /// Tüm tanınmamış ülkeler için bir günlük değerlendirme.
    /// Yeni eklenen tanınma sayısını döndürür.
    /// </summary>
    public static int AdvanceAllDays(long day)
    {
        EnsureLoaded();
        if (_births.Count == 0) return 0;

        // İterasyon sırasında _recognitions değişir, bu yüzden kimlikler önce kopyalanır.
        var ids = _births.Keys.ToList();
        int added = 0;

        foreach (var targetId in ids)
        {
            if (string.IsNullOrEmpty(targetId)) continue;
            var target = CountryDatabase.Get(targetId);
            if (target == null) continue;

            var recognizers = CountryDatabase.All()
                .Where(c => c != null && !string.Equals(c.Id, targetId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var recognizer in recognizers)
            {
                if (IsRecognized(recognizer.Id, targetId)) continue;
                if (!TryScore(recognizer, target, day, out var score)) continue;

                if (score >= D.RecognitionThreshold)
                {
                    Recognize(recognizer.Id, targetId, day, $"skor={score:N2}");
                    added++;
                }
            }
        }

        return added;
    }

    /// <summary>
    /// Bir tanıma kaydı ekler ve iki yönlü ilişkiyi iyileştirir.
    /// </summary>
    private static void Recognize(string recognizerId, string targetId, long day, string why)
    {
        if (!_recognitions.Add(Key(recognizerId, targetId))) return;

        DiplomacySystem.ModifyRelation(recognizerId, targetId, D.RelationOnRecognition);
        DiplomacySystem.ModifyRelation(targetId, recognizerId, D.RelationOnRecognition);

        var r = CountryDatabase.Get(recognizerId);
        var t = CountryDatabase.Get(targetId);
        GD.Print($"[Recognition] TANINDI: {r?.Name ?? recognizerId} ({recognizerId}) → " +
                 $"{t?.Name ?? targetId} ({targetId}) — {why}, gün={day}, " +
                 $"tanıyan={RecognizerCount(targetId)}/{EstablishedCount(targetId)}, " +
                 $"ilişki +{D.RelationOnRecognition}");
    }

    /// <summary>
    /// Tanınma puanı. Doğuştan önce kurulu ülkeler tanımaz (false döner).
    /// </summary>
    private static bool TryScore(CountryData recognizer, CountryData target, long day, out double score)
    {
        score = 0.0;
        if (!_births.TryGetValue(target.Id, out var birth)) return false;

        long age = day - birth.BirthDay;
        if (age < 0) age = 0;

        // 1) Kültür benzerliği
        if (SharesCulture(recognizer.Id, target.Id)) score += D.SameCultureWeight;

        // 2) İdeoloji yakınlığı
        if (string.Equals(recognizer.Ideology, target.Ideology, StringComparison.OrdinalIgnoreCase))
            score += D.SameIdeologyWeight;

        // 3) Komşuluk
        if (NeighborSystem.IsReady && NeighborSystem.AreNeighbors(recognizer.Id, target.Id))
            score += D.NeighborWeight;

        // 4) Yeniliğin oturmuşluğu
        double maturity = D.FullRecognitionDays > 0
            ? Math.Clamp(age / D.FullRecognitionDays, 0.0, 1.0)
            : 1.0;
        score += D.StrengthWeight * maturity;

        // 5) Güç
        double strength = D.StrengthReference > 0
            ? Math.Clamp(target.StartingDivisions / D.StrengthReference, 0.0, 1.0)
            : 0.0;
        score += D.StrengthWeight * strength;

        return true;
    }

    /// <summary>İki ülke en az bir kültürü paylaşıyor mu?</summary>
    private static bool SharesCulture(string countryA, string countryB)
    {
        var a = ProvinceCultureSystem.GetCultureDistribution(countryA);
        if (a == null || a.Count == 0) return false;
        var b = ProvinceCultureSystem.GetCultureDistribution(countryB);
        if (b == null || b.Count == 0) return false;

        foreach (var kv in a)
            if (b.ContainsKey(kv.Key)) return true;
        return false;
    }

    /// <summary>Hedef için aday sayısı: kurulu ülkeler eksi kendisi.</summary>
    private static int EstablishedCount(string targetId) =>
        CountryDatabase.All().Count(c => c != null &&
            !string.Equals(c.Id, targetId, StringComparison.OrdinalIgnoreCase));

    private static string Key(string recognizerId, string targetId) => $"{recognizerId}|{targetId}";

    // ===== Yeni oyun / Save-Load =====

    public static void ResetForNewGame()
    {
        _births.Clear();
        _recognitions.Clear();
    }

    public static SaveSystem.RecognitionSave GetSaveData()
    {
        var births = new List<SaveSystem.RecognitionBirthEntry>(_births.Count);
        foreach (var b in _births.Values)
        {
            births.Add(new SaveSystem.RecognitionBirthEntry
            {
                CountryId = b.CountryId,
                ParentId = b.ParentId,
                BirthDay = b.BirthDay,
                Reason = b.Reason ?? ""
            });
        }

        // Sıralı yaz: kayıt dosyasının kararlı olması için.
        var recognitions = _recognitions
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        return new SaveSystem.RecognitionSave
        {
            Births = births.ToArray(),
            Recognitions = recognitions
        };
    }

    public static void LoadSaveData(SaveSystem.RecognitionSave data)
    {
        _births.Clear();
        _recognitions.Clear();
        if (data == null) return;

        if (data.Births != null)
        {
            foreach (var b in data.Births)
            {
                if (b == null || string.IsNullOrEmpty(b.CountryId)) continue;
                _births[b.CountryId] = new BirthRecord
                {
                    CountryId = b.CountryId,
                    ParentId = b.ParentId ?? "",
                    BirthDay = b.BirthDay,
                    Reason = b.Reason ?? ""
                };
            }
        }

        if (data.Recognitions != null)
        {
            foreach (var k in data.Recognitions)
                if (!string.IsNullOrEmpty(k)) _recognitions.Add(k);
        }

        GD.Print($"[Recognition] state yüklendi — doğan ülke={_births.Count}, tanınma={_recognitions.Count}");
    }
}
