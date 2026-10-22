using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
    public static void ResetForNewGame() => _records.Clear();

    public static SubjectRecord Get(string countryId)
        => !string.IsNullOrEmpty(countryId) && _records.TryGetValue(countryId, out var r) ? r : null;

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

        if (GameManager.IsAvailable)
            GameManager.Instance.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);

        res.Ok = true;
        return res;
    }

    /// <summary>
    /// Bir gün ilerletir. Şu an için otomatik bir mekanizası yok; ileride
    /// ilhak edenin stabilitesi düştükçe serbest bırakma talebi gelecek.
    /// </summary>
    public static void AdvanceDay(string overlordId) { }

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
        return new SaveSystem.ReleasableSave { Subjects = list.ToArray() };
    }

    public static void LoadSaveData(SaveSystem.ReleasableSave data)
    {
        _records.Clear();
        if (data?.Subjects == null) return;

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
    }
}
