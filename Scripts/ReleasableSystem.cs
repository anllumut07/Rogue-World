using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// İlhak edilmiş / kukla ülkeyi bağımsız çıkarma (M44.4).
///
/// Oyunun birçok yolu ülkeyi ilhak ediyor (barış konferansı, kapitülasyon,
/// olaylar). Bu sisteme her ilhak "kayıt" düşer ve o ülke sonradan
/// serbest bırakılabilir:
///
///   1. İller ilhak edene geri devredilir (ilhak anında hangi illerin
///      devredildiği kaydedildiği için tam tersi yön yapılabilir).
///   2. İlak anında alınan fabrika payı, sahibinde ne kadarsa o kadar geri
///      verilir (negatife düşmez).
///   3. `IsAnnexed` / `OwnerId` kalkar, ülke yeniden AI devralır.
///
/// Kukla ülkede fabrika ilhak edene geçmediği için pay sıfırdır; illeri de
/// zaten kendisinde kaldığı için devir no-op olur.
///
/// Tüm adımlar deterministiktir — RNG kullanılmaz.
/// </summary>
public static class ReleasableSystem
{
    private static GameManager GM => GameManager.Instance;

    /// <summary>İlak edilmiş bir ülkenin kaydı (release anında ters yön için).</summary>
    public class SubjectRecord
    {
        public string CountryId = "";     // ilhak edilen / kukla ülke
        public string OverlordId = "";    // ilhak eden ülke
        public bool IsPuppet = false;     // kukla ise fabrika payı alınmamıştır
        public string[] ProvinceIds = Array.Empty<string>(); // ilhak anındaki iller
        public int FactoriesTaken = 0;    // ilhak anında devredilen fabrika sayısı
        public int CivTaken = 0;          // ayrıntılı kayıt (geri verme sırası için)
        public int MilTaken = 0;
        public int DockTaken = 0;
    }

    // Ülke kimliği → ilhak kaydı
    private static readonly Dictionary<string, SubjectRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// M48.2: Bir ilhak edilmiş ülkenin serbest bırakma TALEBİ.
    ///
    /// Talep iki taraflıdır ve tamamen deterministik (RNG yok):
    /// - **Bağımlı taraf:** özerklik düşük + direniş yüksekse adına talep açar.
    ///   Oyuncu değil, dünya durumu konuşur.
    /// - **Overlord AI:** açık talebi kendi gücüne / zayıflığına göre
    ///   değerlendirir; özerklik verir, görmezden gelir ya da ülkeyi bırakır.
    ///   Overlord OYUNCUYSA AI karar vermez — talep açık kalır ve oyuncu
    ///   M45.1 panelinden kendisi karar verir.
    /// </summary>
    public class DemandRecord
    {
        public string CountryId = "";        // talep eden (ilhak edilmiş ülke)
        public string OverlordId = "";       // talep edilen
        public long FirstRaisedDay = -1;     // ilk kez açıldığı gün
        public long LastRaisedDay = -1;      // son baskı günü (cooldown)
        public int RaiseCount = 0;           // kaç kez talepte bulundu
        public long LastResponseDay = -1;    // overlord'un son yanıtı
        public int ResponseCount = 0;
        public string LastResponse = "";     // "grant" | "ignore" | "release" | "war" | "independence" | "blocked"
        public bool Open = true;             // talep hâlâ açık mı
        public bool SelfResolved = false;    // koşullar düzelince talebi geri çekti
    }

    // Ülke kimliği → talep kaydı
    private static readonly Dictionary<string, DemandRecord> _demands =
        new(StringComparer.OrdinalIgnoreCase);

    private static DemandSettings D = new();
    private static bool _demandLoaded;
    private const string DemandDataPath = "res://Data/subject_demands.json";

    private sealed class DemandSettings
    {
        public double LowAutonomyLevel = 25.0;
        public double HighResistance = 35.0;
        public double ResignResistance = 15.0;
        public int RaiseCooldownDays = 7;
        public int ResponseCooldownDays = 14;
        public double AutonomyWeight = 0.45;
        public double ResistanceWeight = 0.55;
        public double WeaknessWeight = 0.15;
        public double GrantThreshold = 0.45;
        public double ReleaseThreshold = 0.80;
        public double WarPenalty = 0.35;
        public double MinOverlordStabilityForRelease = 30.0;
        public int MinDemandsBeforeWar = 2;
        public double DemandWarSupportFactor = 0.25;
    }

    private sealed class DemandRoot
    {
        public int Version { get; set; } = 1;
        public DemandSettings Settings { get; set; } = new();
    }

    private static void EnsureDemandLoaded()
    {
        if (_demandLoaded) return;
        _demandLoaded = true;

        try
        {
            using var file = Godot.FileAccess.Open(DemandDataPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PrintErr("[Releasable] subject_demands.json açılamadı — varsayılan eşikler kullanılıyor.");
                return;
            }
            var root = JsonSerializer.Deserialize<DemandRoot>(file.GetAsText(), new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true
            });
            if (root?.Settings != null)
            {
                D = root.Settings;
                GD.Print($"[Releasable] Talep eşikleri yüklendi: özerklik<%{D.LowAutonomyLevel:N0}, " +
                         $"direniş>={D.HighResistance:N0}, serbest_bırakma_eşiği={D.ReleaseThreshold:N2}");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Releasable] subject_demands.json okunamadı ({e.Message}) — varsayılan eşikler.");
        }
    }

    /// <summary>Kayıtlı ilhak/kukla sayısı.</summary>
    public static int RecordCount => _records.Count;

    /// <summary>Bir ilhak/kukla durumunu kaydeder. İller DEVRİ YAPILMADAN ÖNCE çağrılmalı.</summary>
    public static void RecordSubject(string countryId, string overlordId,
        bool isPuppet, int civTaken, int milTaken, int dockTaken)
    {
        if (string.IsNullOrEmpty(countryId)) return;

        // İller hâlâ ilhak edenin değil, kurbanın elindeyse snapshot alınır.
        var provinces = ProvinceOwnershipSystem.GetProvincesByOwner(countryId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        _records[countryId] = new SubjectRecord
        {
            CountryId = countryId,
            OverlordId = overlordId ?? "",
            IsPuppet = isPuppet,
            ProvinceIds = provinces,
            FactoriesTaken = civTaken + milTaken + dockTaken,
            CivTaken = civTaken,
            MilTaken = milTaken,
            DockTaken = dockTaken
        };
        GD.Print($"[Releasable] Kayıt: {countryId} → {overlordId} " +
                 $"({(isPuppet ? "kukla" : "ilhak")}, {provinces.Length} il, " +
                 $"{civTaken + milTaken + dockTaken} fabrika)");
    }

    /// <summary>Bir ülkenin ilhak kaydını siler (yeni oyunda sıfırlama gibi).</summary>
    public static void Forget(string countryId)
    {
        if (!string.IsNullOrEmpty(countryId)) _records.Remove(countryId);
    }

    /// <summary>Yeni oyunda sıfırlar.</summary>
    public static void ResetForNewGame()
    {
        _records.Clear();
        _demands.Clear();
    }

    /// <summary>Yalnızca talep kayıtlarını temizler (ilhak kayıtları kalır).</summary>
    public static void ResetDemands() => _demands.Clear();

    /// <summary>Kayıtlı talep sayısı (açık + kapalı).</summary>
    public static int DemandCount => _demands.Count;

    /// <summary>Bir ülkenin talebi (yoksa null).</summary>
    public static DemandRecord DemandFor(string countryId)
        => !string.IsNullOrEmpty(countryId) && _demands.TryGetValue(countryId, out var d) ? d : null;

    /// <summary>Bir overlord'a bağlı AÇIK talepler (kimliğe göre artan).</summary>
    public static List<DemandRecord> DemandsOf(string overlordId)
    {
        var result = new List<DemandRecord>();
        if (string.IsNullOrEmpty(overlordId)) return result;

        foreach (var d in _demands.Values)
        {
            if (!d.Open) continue;
            if (!string.Equals(d.OverlordId, overlordId, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(d);
        }
        result.Sort((a, b) => string.CompareOrdinal(a.CountryId, b.CountryId));
        return result;
    }

    public static SubjectRecord Get(string countryId)
        => !string.IsNullOrEmpty(countryId) && _records.TryGetValue(countryId, out var r) ? r : null;

    /// <summary>
    /// Bir overlord'un serbest bırakılabilecek bağımlıları (M45.1 paneli,
    /// M45.2/M48.2 özerklik ve AI talepleri de bu listeyi kullanır).
    /// Kimliğe göre artan sıralıdır.
    /// </summary>
    public static List<SubjectRecord> SubjectsOf(string overlordId)
    {
        var result = new List<SubjectRecord>();
        if (string.IsNullOrEmpty(overlordId)) return result;

        foreach (var r in _records.Values)
        {
            if (!string.Equals(r.OverlordId, overlordId, StringComparison.OrdinalIgnoreCase))
                continue;
            var country = CountryDatabase.Get(r.CountryId);
            if (country != null && country.IsAnnexed)
                result.Add(r);
        }
        result.Sort((a, b) => string.CompareOrdinal(a.CountryId, b.CountryId));
        return result;
    }

    /// <summary>
    /// Serbest bırakılabilecek ülkeler: hâlâ ilhak edilmiş ve kaydı olanlar.
    /// </summary>
    public static List<SubjectRecord> Releasable()
    {
        var result = new List<SubjectRecord>();
        foreach (var r in _records.Values)
        {
            var country = CountryDatabase.Get(r.CountryId);
            if (country != null && country.IsAnnexed)
                result.Add(r);
        }
        result.Sort((a, b) => string.CompareOrdinal(a.CountryId, b.CountryId));
        return result;
    }

    /// <summary>
    /// Bir ülkeyi serbest bırakmanın koşullarını değerlendirir.
    /// `actorId` boşsa yalnızca dünya durumu kontrol edilir.
    /// </summary>
    public static bool CanRelease(string countryId, string actorId = "")
    {
        if (string.IsNullOrEmpty(countryId)) return false;
        var r = Get(countryId);
        if (r == null) return false;

        var country = CountryDatabase.Get(countryId);
        if (country == null || !country.IsAnnexed) return false;

        // Aktör ya ilhak eden ya da kendisi olmalı (kendi halkı bağımsızlık istiyor)
        if (!string.IsNullOrEmpty(actorId))
        {
            bool isOverlord = string.Equals(actorId, r.OverlordId, StringComparison.OrdinalIgnoreCase);
            bool isSelf = string.Equals(actorId, countryId, StringComparison.OrdinalIgnoreCase);
            if (!isOverlord && !isSelf) return false;
        }
        return true;
    }

    /// <summary>Serbest bırakma sonucu.</summary>
    public class ReleaseResult
    {
        public bool Ok { get; set; }
        public string Reason { get; set; } = "";
        public string CountryId { get; set; } = "";
        public string CountryName { get; set; } = "";
        public string OverlordId { get; set; } = "";
        public int ProvincesReturned { get; set; }
        public int CivReturned { get; set; }
        public int MilReturned { get; set; }
        public int DockReturned { get; set; }
    }

    /// <summary>
    /// Ülkeyi bağımsız çıkarır: iller ve fabrika payı geri verilir,
    /// ilhak bayrakları kalkar, AI yeniden devralır.
    /// </summary>
    public static ReleaseResult Release(string countryId, string actorId = "")
    {
        var res = new ReleaseResult { Ok = false, CountryId = countryId ?? "" };

        if (string.IsNullOrEmpty(countryId))
        {
            res.Reason = "eksik bilgi";
            return res;
        }
        var record = Get(countryId);
        if (record == null)
        {
            res.Reason = "bu ülkenin ilhak kaydı yok";
            return res;
        }
        var country = CountryDatabase.Get(countryId);
        if (country == null)
        {
            res.Reason = "ülke bulunamadı";
            return res;
        }
        if (!country.IsAnnexed)
        {
            res.Reason = "ülke zaten bağımsız";
            return res;
        }
        if (!CanRelease(countryId, actorId))
        {
            res.Reason = "yalnızca ilhak eden ülke serbest bırakabilir";
            return res;
        }

        var overlord = CountryDatabase.Get(record.OverlordId);
        res.CountryName = country.Name;
        res.OverlordId = record.OverlordId;

        // 1) İlleri geri ver — yalnızca hâlâ ilhak edende olanlar.
        if (overlord != null && record.ProvinceIds != null)
        {
            var returning = record.ProvinceIds
                .Where(p => string.Equals(ProvinceOwnershipSystem.GetProvinceOwner(p),
                                          overlord.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var p in returning)
                ProvinceOwnershipSystem.SetProvinceOwner(p, country.Id);
            if (returning.Count > 0)
                ProvinceCultureSystem.ReassignCountry(returning, country.Id);
            res.ProvincesReturned = returning.Count;
        }

        // 2) Fabrika payını geri ver — sahibinde ne kadarsa o kadar.
        if (overlord != null)
        {
            int civ = Math.Min(record.CivTaken, overlord.CivFactories);
            int mil = Math.Min(record.MilTaken, overlord.MilFactories);
            int dock = Math.Min(record.DockTaken, overlord.Dockyards);

            // Kayıtlı tür bazında yeterli yoksa, toplam payını sırayla doldur.
            // (İlhak sonrası fabrika inşa/x yıkım payı kayıttan sapmış olabilir.)
            int totalMin = Math.Min(record.FactoriesTaken, overlord.TotalFactories);
            int assigned = civ + mil + dock;
            if (assigned < totalMin)
            {
                int missing = totalMin - assigned;
                int extraDock = Math.Min(missing, overlord.Dockyards - dock);
                dock += extraDock;
                assigned += extraDock;
                missing -= extraDock;
                if (missing > 0)
                {
                    int extraMil = Math.Min(missing, overlord.MilFactories - mil);
                    mil += extraMil;
                    assigned += extraMil;
                    missing -= extraMil;
                }
                if (missing > 0)
                {
                    int extraCiv = Math.Min(missing, overlord.CivFactories - civ);
                    civ += extraCiv;
                }
            }

            overlord.CivFactories -= civ;
            overlord.MilFactories -= mil;
            overlord.Dockyards -= dock;
            country.CivFactories += civ;
            country.MilFactories += mil;
            country.Dockyards += dock;

            res.CivReturned = civ;
            res.MilReturned = mil;
            res.DockReturned = dock;
        }

        // 3) Bağımsızlık: ilhak bayraklarını kaldır
        country.IsAnnexed = false;
        country.OwnerId = "";
        _records.Remove(countryId);

        GD.Print($"[Releasable] BAĞIMSIZ: {country.Name} ({country.Id}) " +
                 $"serbest bırakıldı — il={res.ProvincesReturned}, " +
                 $"fabrika c{res.CivReturned}/m{res.MilReturned}/t{res.DockReturned}");

        // 4) Yeni ülke AI devralır (CountryAI Initialize/Reset, CountryDatabase üzerinden kurulur)
        CountryAI.Reset();

        // M48.3: Diplomatik tanınma — ülke bağımsız oldu ama dünya onu
        // resmen tanımıyor. Eski overlord tanır (kötü niyetli taban ilişki),
        // diğer ülkeler gün gün değerlendirir.
        RecognitionSystem.OnCountryBorn(country.Id, record.OverlordId,
            GM != null ? GM.TotalDays : 0, "release");

        if (GameManager.IsAvailable)
            GameManager.Instance.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);

        res.Ok = true;
        return res;
    }

    /// <summary>
    /// Bir gün ilerletir. Şu an için otomatik bir mekanizası yok; ileride
    /// ilhak edenin stabilitesi düştükçe serbest bırakma talebi gelecek.
    /// </summary>
    public static int AdvanceDay(string overlordId) => AdvanceDay(overlordId, -1);

    /// <summary>
    /// M48.2 tam sürüm: <paramref name="currentDay"/> ile cooldown'lar
    /// yönetilir. Negatif gün "gün yok" demektir (cooldown devre dışı).
    /// </summary>
    public static int AdvanceDay(string overlordId, long currentDay)
    {
        EnsureDemandLoaded();
        if (string.IsNullOrEmpty(overlordId)) return 0;

        var subjects = SubjectsOf(overlordId);
        if (subjects.Count == 0) return 0;

        // Overlord oyuncunun bağımlılarına AI karar vermez (M11 kuralı):
        // talep doğar ama karar oyuncuya kalır.
        bool aiOverlord = CountryAI.IsAiCountry(overlordId);

        foreach (var record in subjects)
        {
            RaiseDemandFor(record, currentDay);

            var demand = _demands.GetValueOrDefault(record.CountryId);
            if (demand == null || !demand.Open || !aiOverlord) continue;

            RespondToDemand(record, demand, currentDay);
        }
        return subjects.Count;
    }

    /// <summary>
    /// Bütün overlord'lar için tek gün ilerletir (GameManager günlük tick).
    /// </summary>
    public static int AdvanceAllDays(long currentDay)
    {
        var overlords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _records.Values)
        {
            var country = CountryDatabase.Get(r.CountryId);
            if (country == null || !country.IsAnnexed) continue;
            if (!string.IsNullOrEmpty(r.OverlordId)) overlords.Add(r.OverlordId);
        }

        int total = 0;
        foreach (var overlordId in overlords) total += AdvanceDay(overlordId, currentDay);
        return total;
    }

    /// <summary>
    /// Talep doğma / kendi kendine kapanma koşulu. Deterministik: yalnızca
    /// özerklik ve direniş düzeyine bakar, RNG yok.
    /// </summary>
    private static void RaiseDemandFor(SubjectRecord record, long currentDay)
    {
        EnsureDemandLoaded();
        string id = record.CountryId;

        double autonomy = AutonomySystem.Level(id);
        double resistance = ResistanceSystem.Accumulated(id);

        var demand = _demands.GetValueOrDefault(id);

        // Koşullar düzelmişse talep kendiliğinden kapanır. Overlord'un bir şey
        // yapmasına gerek yok; kayıt geçmiş olarak durur.
        if (demand is { Open: true } &&
            (autonomy < 0 || autonomy >= D.LowAutonomyLevel ||
             resistance <= D.ResignResistance))
        {
            demand.Open = false;
            demand.SelfResolved = true;
            GD.Print($"[M48.2] {id} talebini geri çekti: özerklik %{autonomy:N0}, " +
                     $"direniş {resistance:N0} ({demand.RaiseCount} kez açılmıştı)");
            return;
        }

        // Koşul sağlanmıyorsa hiçbir şey yapma
        if (autonomy < 0 || autonomy >= D.LowAutonomyLevel) return;
        if (resistance < D.HighResistance) return;

        // Cooldown: aynı bağımlı en fazla N günde bir baskı yapar
        if (demand != null && currentDay >= 0 &&
            currentDay - demand.LastRaisedDay < D.RaiseCooldownDays) return;

        if (demand == null)
        {
            demand = new DemandRecord { CountryId = id, OverlordId = record.OverlordId };
            _demands[id] = demand;
        }

        demand.Open = true;
        demand.SelfResolved = false;
        demand.LastRaisedDay = currentDay;
        if (currentDay >= 0 && demand.FirstRaisedDay < 0) demand.FirstRaisedDay = currentDay;
        demand.RaiseCount++;

        // M48.3: Baskı altındaki halk savaş desteği kazanır — işgal altında
        // bir halkın bağımsızlık için savaşması doğal sonuçtur. Bu adım
        // olmadan bağımsızlık savaşı yalnızca testte, üretimde hiç çıkmazdı.
        var subject = CountryDatabase.Get(id);
        if (subject != null)
        {
            double wsBefore = subject.WarSupportPercent;
            double wsGain = resistance * D.DemandWarSupportFactor;
            subject.WarSupportPercent = (float)Math.Clamp(wsBefore + wsGain, 0.0, 100.0);
            GD.Print($"[M48.3] {id} savaş desteği arttı: %{wsBefore:N0} → " +
                     $"%{subject.WarSupportPercent:N0} (direniş {resistance:N0})");
        }

        GD.Print($"[M48.2] {id} serbest bırakma TALEBİ açtı → {record.OverlordId} " +
                 $"(özerklik %{autonomy:N0} < %{D.LowAutonomyLevel:N0}, " +
                 $"direniş {resistance:N0} >= {D.HighResistance:N0}, " +
                 $"{demand.RaiseCount}. talep)");
    }

    /// <summary>
    /// Overlord AI'nin bir talebe yanıtı. Deterministik puanlama:
    ///
    ///   baskı = özerklik eksikliği x AutonomyWeight + direniş x ResistanceWeight
    ///   skor  = baskı + overlord zayıflığı x WeaknessWeight - savas cezası
    ///
    /// - skor >= ReleaseThreshold ve overlord yeterince sağlam → ülkeyi bırakır
    /// - skor >= ReleaseThreshold ama talep tekrarlanıyor        → "war" (bağımsızlık savaşı)
    ///   ya da "independence" (koparır ama savaş açamaz)
    /// - skor >= GrantThreshold                            → özerklik verir
    /// - skor >= GrantThreshold ama bekleme süresinde       → "deferred" (ertelendi)
    /// - aksi halde                                        → görmezden gelir
    ///
    /// Overlord savaştaysa bırakmak daha zordur (WarPenalty). Puanlama yalnızca
    /// kalıcı dünya durumundan beslenir; RNG yok.
    /// </summary>
    private static void RespondToDemand(SubjectRecord record, DemandRecord demand, long currentDay)
    {
        if (currentDay >= 0 && demand.ResponseCount > 0 &&
            currentDay - demand.LastResponseDay < D.ResponseCooldownDays) return;

        demand.LastResponseDay = currentDay;
        demand.ResponseCount++;

        string subjectId = record.CountryId;
        string overlordId = record.OverlordId;

        double autonomy = Math.Clamp(AutonomySystem.Level(subjectId), 0, 100);
        double resistance = Math.Clamp(ResistanceSystem.Accumulated(subjectId), 0, 100);

        double pressure = (1.0 - autonomy / 100.0) * D.AutonomyWeight
                        + (resistance / 100.0) * D.ResistanceWeight;

        var overlord = CountryDatabase.Get(overlordId);
        double stability = overlord?.StabilityPercent ?? 50.0;
        double weakness = Math.Clamp((100.0 - stability) / 100.0, 0.0, 1.0);

        bool atWar = WarSystem.Wars.Any(w => w != null && !w.IsEnded &&
            (string.Equals(w.AttackerId, overlordId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(w.DefenderId, overlordId, StringComparison.OrdinalIgnoreCase)));

        double score = pressure + weakness * D.WeaknessWeight - (atWar ? D.WarPenalty : 0.0);

        if (score >= D.ReleaseThreshold && stability >= D.MinOverlordStabilityForRelease)
        {
            var released = Release(subjectId, overlordId);
            if (released.Ok)
            {
                demand.Open = false;
                demand.LastResponse = "release";
                GD.Print($"[M48.2] {overlordId} TALEBİ KABUL ETTİ: {subjectId} bağımsız " +
                         $"(skor={score:N2}, {released.ProvincesReturned} il, " +
                         $"c{released.CivReturned}/m{released.MilReturned}/t{released.DockReturned} fabrika)");
                return;
            }
            GD.Print($"[M48.2] {overlordId} bırakmak istedi ama olamadı: {released.Reason}");
        }

        // M48.3: Bağımsızlık savaşı. Baskı serbest bırakma eşiğinde ama
        // overlord ya çok zayıf ya bırakmaya hazır değil — ve talep üst üste
        // geliyorsa halk konuşmayı bırakır. WarSystem ilhak edilmiş bir ülkeye
        // savaş ilanına izin vermediği için bağımsızlık önce ilhak olduğu gibi
        // güçle koparılır (Release), ardından savaş ilan edilir.
        bool wantsIndependence = score >= D.ReleaseThreshold;
        if (wantsIndependence && demand.RaiseCount >= D.MinDemandsBeforeWar && !atWar)
        {
            var revolt = Release(subjectId, overlordId);
            if (!revolt.Ok)
            {
                demand.LastResponse = "blocked";
                GD.Print($"[M48.3] {subjectId} bağımsızlık için ayaklandı ama " +
                         $"ayrılamadı: {revolt.Reason} (skor={score:N2})");
                return;
            }

            CasusBelliSystem.GrantImmediateJustification(subjectId, overlordId, "independence_war");
            bool hasCb = CasusBelliSystem.HasValidCasusBelli(subjectId, overlordId);
            bool declared = WarSystem.DeclareWar(subjectId, overlordId);
            GD.Print($"[M48.3] savaş ilanı denemesi: gerekçe var mı={hasCb}, " +
                     $"ilan edildi mi={declared} (hedef {overlordId})");
            if (declared)
            {
                demand.Open = false;
                demand.LastResponse = "war";
                GD.Print($"[M48.3] BAĞIMSIZLIK SAVAŞI: {subjectId} koptu ve şimdi " +
                         $"{overlordId} üzerine savaşıyor (skor={score:N2}, " +
                         $"talep sayısı={demand.RaiseCount})");
                return;
            }

            demand.Open = false;
            demand.LastResponse = "independence";
            GD.Print($"[M48.3] BAŞLATILAN BAĞIMSIZLIK: {subjectId} güçle ayrıldı ama " +
                     $"savaş açamadı (skor={score:N2}, talep sayısı={demand.RaiseCount})");
            return;
        }

        if (score >= D.GrantThreshold)
        {
            // Skor konuşturuyor ama özerklik bekleme süresinde (AutonomySystem
            // cooldown'u) — bu bir reddetme DEĞİL, ertelenmiş taleptir.
            if (AutonomySystem.GrantAutonomy(subjectId, overlordId, currentDay, out var grantReason))
            {
                demand.LastResponse = "grant";
                GD.Print($"[M48.2] {overlordId} pazarlık yaptı: {subjectId} özerklik verildi " +
                         $"(skor={score:N2}, direniş {resistance:N0})");
                return;
            }

            demand.LastResponse = "deferred";
            GD.Print($"[M48.2] {overlordId} talebi erteleyemedi: {subjectId} — {grantReason} " +
                     $"(skor={score:N2}, konuştu ama eylem bekletildi)");
            return;
        }

        demand.LastResponse = "ignore";
        GD.Print($"[M48.2] {overlordId} TALEBİ REDDETTİ: {subjectId} " +
                 $"(skor={score:N2}, özerklik %{autonomy:N0}, direniş {resistance:N0})");
    }

    /// <summary>Save/Load verisi.</summary>
    public static SaveSystem.ReleasableSave GetSaveData()
    {
        var list = new List<SaveSystem.ReleasableSubjectEntry>(_records.Count);
        foreach (var r in _records.Values)
        {
            list.Add(new SaveSystem.ReleasableSubjectEntry
            {
                CountryId = r.CountryId,
                OverlordId = r.OverlordId,
                IsPuppet = r.IsPuppet,
                ProvinceIds = r.ProvinceIds ?? Array.Empty<string>(),
                FactoriesTaken = r.FactoriesTaken,
                CivTaken = r.CivTaken,
                MilTaken = r.MilTaken,
                DockTaken = r.DockTaken
            });
        }
        var demands = new List<SaveSystem.ReleasableDemandEntry>(_demands.Count);
        foreach (var d in _demands.Values)
        {
            demands.Add(new SaveSystem.ReleasableDemandEntry
            {
                CountryId = d.CountryId,
                OverlordId = d.OverlordId,
                FirstRaisedDay = d.FirstRaisedDay,
                LastRaisedDay = d.LastRaisedDay,
                RaiseCount = d.RaiseCount,
                LastResponseDay = d.LastResponseDay,
                ResponseCount = d.ResponseCount,
                LastResponse = d.LastResponse ?? "",
                Open = d.Open,
                SelfResolved = d.SelfResolved
            });
        }

        return new SaveSystem.ReleasableSave { Subjects = list.ToArray(), Demands = demands.ToArray() };
    }

    public static void LoadSaveData(SaveSystem.ReleasableSave data)
    {
        _records.Clear();
        _demands.Clear();
        if (data == null) return;

        foreach (var e in data.Subjects)
        {
            if (e == null || string.IsNullOrEmpty(e.CountryId)) continue;
            _records[e.CountryId] = new SubjectRecord
            {
                CountryId = e.CountryId,
                OverlordId = e.OverlordId ?? "",
                IsPuppet = e.IsPuppet,
                ProvinceIds = e.ProvinceIds ?? Array.Empty<string>(),
                FactoriesTaken = e.FactoriesTaken,
                CivTaken = e.CivTaken,
                MilTaken = e.MilTaken,
                DockTaken = e.DockTaken
            };
        }
        GD.Print($"[Releasable] İlak kayıtları yüklendi: {_records.Count}");

        // M48.2: talepler ayrı yüklenir — kaydı olmayan başlangıç kaydında boş gelir.
        if (data.Demands != null)
        {
            foreach (var e in data.Demands)
            {
                if (e == null || string.IsNullOrEmpty(e.CountryId)) continue;
                _demands[e.CountryId] = new DemandRecord
                {
                    CountryId = e.CountryId,
                    OverlordId = e.OverlordId ?? "",
                    FirstRaisedDay = e.FirstRaisedDay,
                    LastRaisedDay = e.LastRaisedDay,
                    RaiseCount = e.RaiseCount,
                    LastResponseDay = e.LastResponseDay,
                    ResponseCount = e.ResponseCount,
                    LastResponse = e.LastResponse ?? "",
                    Open = e.Open,
                    SelfResolved = e.SelfResolved
                };
            }
            GD.Print($"[M48.2] Talepler yüklendi: {_demands.Count}");
        }
    }
}
