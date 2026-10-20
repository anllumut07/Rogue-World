using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Deniz invazyonu / amfibiy operasyon sistemi (M39.2).
/// Deniz filosu sayesinde kara bölüklerini kıyıdan kıyıya taşır ve hedef İL'de
/// devreye sokur. Statik sınıf; GameManager günlük tick'inde çağrılır.
/// </summary>
public static class NavalInvasionSystem
{
    public enum InvasionState { Planning, Loading, Transit, Landing, Completed, Failed, Aborted }

    public class NavalInvasion
    {
        public string Id { get; set; }
        public string NavyId { get; set; }          // Deniz filosu (Domain = Naval)
        public string OriginProvinceId { get; set; } // Kalkış limanı (kıyıda, sahip)
        public string TargetProvinceId { get; set; } // Hedef il (kıyıda, düşman/boş)
        public List<string> DivisionIds { get; set; } = new(); // Taşınan kara bölükler
        public int DaysRemaining { get; set; }       // Günlük ilerleme sayacı
        public int TotalDays { get; set; }           // Toplam planlanan süre
        public InvasionState State { get; set; }
    }

    // ===== Sabitler =====
    private const int LoadingDays = 1;
    private const int TransitDays = 2;
    private const int LandingDays = 1;
    private const int MaxDivisionsPerNavy = 4;
    private const float StormDelayChance = 0.10f; // Deterministik rüzgar gecikme ihtimali

    private static bool _initialized;
    private static readonly List<NavalInvasion> _invasions = new();
    private static int _invasionCounter;

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    public static bool IsInitialized => _initialized;
    public static IReadOnlyList<NavalInvasion> Invasions => _invasions;

    public static void ResetForNewGame()
    {
        _invasions.Clear();
        _invasionCounter = 0;
        _initialized = true;
        GD.Print("[NavalInvasion] Deniz invazyonu sistemi sıfırlandı (yeni oyun)");
    }

    public static void Initialize(CountryData data)
    {
        if (!_initialized) ResetForNewGame();
        GD.Print($"NavalInvasionSystem: {(data?.Id ?? "—")} için hazır — aktif invazyon={_invasions.Count}");
    }

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====

    public static void DailyTick()
    {
        if (!_initialized || Player == null) return;

        for (int i = _invasions.Count - 1; i >= 0; i--)
        {
            var inv = _invasions[i];
            if (inv.State == InvasionState.Completed || inv.State == InvasionState.Failed || inv.State == InvasionState.Aborted)
                continue;

            inv.DaysRemaining--;
            if (inv.DaysRemaining > 0) continue;

            // Aşama tamamlandı → bir sonraki aşamaya geç
            switch (inv.State)
            {
                case InvasionState.Planning:
                    inv.State = InvasionState.Loading;
                    inv.DaysRemaining = LoadingDays;
                    GD.Print($"[NavalInvasion] {inv.Id}: Filo {inv.NavyId} yükleme aşamasına girdi ({inv.DaysRemaining} gün)");
                    break;

                case InvasionState.Loading:
                    inv.State = InvasionState.Transit;
                    inv.DaysRemaining = TransitDays;
                    // Rüzgar gecikmesi (deterministik)
                    if (DeterministicRNG.NextFloat() < StormDelayChance)
                    {
                        inv.DaysRemaining += 1;
                        GD.Print($"[NavalInvasion] {inv.Id}: rüzgar gecikmesi, transit +1 gün ({inv.DaysRemaining})");
                    }
                    GD.Print($"[NavalInvasion] {inv.Id}: Yükleme tamam, denizden ayrılıyor → {inv.TargetProvinceId} ({inv.DaysRemaining} gün)");
                    break;

                case InvasionState.Transit:
                    inv.State = InvasionState.Landing;
                    inv.DaysRemaining = LandingDays;
                    GD.Print($"[NavalInvasion] {inv.Id}: Hedef kıyısına varıldı, indirme başlıyor ({inv.DaysRemaining} gün)");
                    break;

                case InvasionState.Landing:
                    CompleteInvasion(inv);
                    break;
            }
        }
    }

    // ===== Deniz invazyonu başlatma (UI / komuttan çağrılır) =====

    public static bool StartNavalInvasion(string navyId, string originProvinceId, string targetProvinceId, List<string> divisionIds)
    {
        if (!_initialized || Player == null) return false;

        // Aynı hedefte zaten aktif (bitmemiş) bir invazyon var mı?
        if (_invasions.Any(inv => inv.TargetProvinceId == targetProvinceId &&
            inv.State != InvasionState.Completed &&
            inv.State != InvasionState.Failed &&
            inv.State != InvasionState.Aborted))
        {
            GD.Print($"[NavalInvasion] {targetProvinceId} için zaten aktif bir deniz invazyonu devam ediyor.");
            return false;
        }

        var navy = ArmySystem.Armies.FirstOrDefault(a => a.Id == navyId);
        if (navy == null || navy.Domain != ArmySystem.UnitDomain.Naval)
        {
            GD.Print($"[NavalInvasion] Filo bulunamadı veya deniz filosu değil: {navyId}");
            return false;
        }

        // Filonun bulunduğu il kalkış ile aynı olmalı
        if (string.IsNullOrEmpty(navy.ProvinceId) || navy.ProvinceId != originProvinceId)
        {
            GD.Print($"[NavalInvasion] Filo {navyId} şu an {navy.ProvinceId ?? "?"} ilinde, kalkış {originProvinceId} isteniyor.");
            return false;
        }

        // Kalkış ve hedef kıyıda olmalı
        if (!IsCoastal(originProvinceId) || !IsCoastal(targetProvinceId))
        {
            GD.Print("[NavalInvasion] Kalkış veya hedef il kıyıda değil — deniz invazyonu sadece kıyı İL'ler arasında.");
            return false;
        }

        // Hedef kendi toprak değil (düşman veya boş)
        string targetOwner = ProvinceOwnershipSystem.GetProvinceOwner(targetProvinceId);
        if (targetOwner == Player.Id)
        {
            GD.Print("[NavalInvasion] Hedef il zaten sizin toprağınız — invazyon gerekli değil.");
            return false;
        }

        // Düşman toprağıysa savaşta olunmalı (veya deniz erişimi yoksa denize girilemez)
        if (!string.IsNullOrEmpty(targetOwner) && targetOwner != "?")
        {
            if (!WarSystem.IsAtWarWith(targetOwner) && !DiplomacySystem.HasNavalAccess(Player.Id, targetOwner))
            {
                GD.Print($"[NavalInvasion] {targetOwner} ile savaş yok ve deniz erişimi yok — {targetProvinceId}'e denizden girilemez.");
                return false;
            }
        }

        // Division'lar filoda olmalı (kara bölükleri, filo tarafından taşınacak)
        if (divisionIds == null || divisionIds.Count == 0 || divisionIds.Count > MaxDivisionsPerNavy)
        {
            GD.Print($"[NavalInvasion] Taşınacak bölük sayısı geçersiz (1-{MaxDivisionsPerNavy} arası olmalı).");
            return false;
        }

        foreach (var divId in divisionIds)
        {
            var div = ArmySystem.GetDivision(divId);
            if (div == null || !navy.DivisionIds.Contains(divId))
            {
                GD.Print($"[NavalInvasion] Bölük {divId} filoda bulunamadı.");
                return false;
            }
        }

        _invasionCounter++;
        string id = $"inv_{_invasionCounter}";
        int totalDays = LoadingDays + TransitDays + LandingDays;

        var inv = new NavalInvasion
        {
            Id = id,
            NavyId = navyId,
            OriginProvinceId = originProvinceId,
            TargetProvinceId = targetProvinceId,
            DivisionIds = new List<string>(divisionIds),
            DaysRemaining = totalDays,
            TotalDays = totalDays,
            State = InvasionState.Planning
        };
        _invasions.Add(inv);

        GD.Print($"[NavalInvasion] Deniz invazyonu başlatıldı: {id} — Filo={navy.Name}, {originProvinceId} → {targetProvinceId}, " +
                 $"Bölük={divisionIds.Count}, Süre={totalDays} gün");
        return true;
    }

    private static void CompleteInvasion(NavalInvasion inv)
    {
        var navy = ArmySystem.Armies.FirstOrDefault(a => a.Id == inv.NavyId);
        if (navy == null)
        {
            FailInvasion(inv, "Filo bulunamadı");
            return;
        }

        // Bölükler filo ordusunda hedef İL'de kalır (denizden indikten sonra kara birim olarak devreye girer;
        // WarSystem'in bir sonraki günlük çözümlemesi onları normal olarak görür).
        foreach (var divId in inv.DivisionIds)
        {
            var div = ArmySystem.GetDivision(divId);
            if (div == null) continue;
            div.ArmyId = navy.Id;
        }

        // Filo da hedef il'e geçer
        navy.ProvinceId = inv.TargetProvinceId;

        inv.State = InvasionState.Completed;
        GD.Print($"[NavalInvasion] INVAZYON TAMAMLANDI: {inv.Id} — Filo {navy.Name} {navy.ProvinceId} limanına ulaştı, {inv.DivisionIds.Count} bölük indirildi.");

        // Hedef ilde düşman ordu varsa WarSystem bir sonraki günde normal çatışma çözümleyicisiyle ilgilenir.
    }

    private static void FailInvasion(NavalInvasion inv, string reason)
    {
        inv.State = InvasionState.Failed;
        GD.PrintErr($"[NavalInvasion] INVAZYON BAŞARISIZ: {inv.Id} — {reason}");
    }

    public static void AbortInvasion(string invasionId)
    {
        var inv = _invasions.Find(i => i.Id == invasionId);
        if (inv == null) return;
        inv.State = InvasionState.Aborted;
        GD.Print($"[NavalInvasion] Invazyon iptal edildi: {invasionId}");
    }

    private static bool IsCoastal(string provinceId)
    {
        var prov = WorldMap.Instance?.GetProvinceById(provinceId);
        return prov != null && prov.IsCoastal;
    }

    // ===== M12: Save/Load =====

    public static SaveSystem.NavalInvasionSave GetSaveData()
    {
        return new SaveSystem.NavalInvasionSave
        {
            Invasions = _invasions.Select(inv => new SaveSystem.NavalInvasionEntry
            {
                Id = inv.Id,
                NavyId = inv.NavyId,
                OriginProvinceId = inv.OriginProvinceId,
                TargetProvinceId = inv.TargetProvinceId,
                DivisionIds = inv.DivisionIds.ToArray(),
                DaysRemaining = inv.DaysRemaining,
                TotalDays = inv.TotalDays,
                State = (int)inv.State
            }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.NavalInvasionSave data)
    {
        if (data == null) return;
        _invasions.Clear();
        _invasionCounter = 0;
        if (data.Invasions != null)
        {
            foreach (var e in data.Invasions)
            {
                _invasions.Add(new NavalInvasion
                {
                    Id = e.Id,
                    NavyId = e.NavyId,
                    OriginProvinceId = e.OriginProvinceId,
                    TargetProvinceId = e.TargetProvinceId,
                    DivisionIds = e.DivisionIds != null ? new List<string>(e.DivisionIds) : new List<string>(),
                    DaysRemaining = e.DaysRemaining,
                    TotalDays = e.TotalDays,
                    State = (InvasionState)e.State
                });
                if (!string.IsNullOrEmpty(e.Id) && e.Id.StartsWith("inv_"))
                {
                    int num;
                    if (int.TryParse(e.Id.AsSpan(4), out num)) _invasionCounter = Math.Max(_invasionCounter, num);
                }
            }
        }
        _initialized = true;
        GD.Print($"NavalInvasionSystem: state yüklendi — Aktif invazyon={_invasions.Count}");
    }
}
