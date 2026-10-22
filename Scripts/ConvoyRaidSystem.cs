using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Denizaltı baskını ve konvoy kaybı (M43.2).
///
/// HOI4'un denizaltı savaşının sadeleştirilmiş hali: bir ülkenin ticaret
/// anlaşmaları (konvoy) hangi deniz bölgesinden geçerse, o bölgedeki
/// HAKİMİYET kaybı oranını belirler. Oyuncunun hakimiyeti yüksekse konvoylar
/// rahat geçer; düşman bölgeyi kontrol ediyorsa konvoylar batmaya başlar ve
/// ithalat kesilir.
///
/// Model:
///   Kayıp oranı = temelBaskınOranı × düşmanHakimiyeti / 100
///   düşmanHakimiyeti = 100 − SeaZoneSystem.Supremacy(bölge, ülke)
///
/// Örnek: bölgede hakimiyetin 10 (düşman 90 hakim) ise kayıp oranı
/// temelOran × 0.9 olur. Hakimiyet 50 (çekişmeli) ise temelOran × 0.5.
/// Tam kontrolündeki bölgede (100) kayıp yaşanmaz.
///
/// Rastgelelik YOKTUR: kayıp, hakimiyetten türeyen beklenen değerdir. Bu bilinçli
/// bir tercihtir — günlük kaybı ayrı bir zar atışına bağlamak, olasılığı çift sayar
/// ve "daha çok konvoy = daha çok kayıp" iliskisini bozar. Sistem saf bir fonksiyon
/// olduğu için save/load'a ayrı veri yazmaz.
///
/// M43.4 — Müttefik konvoy koruması (lend-lease / eskort):
///   Bir müttefik filo güzergâha koruma atanır. Koruyucunun bölge ağırlığı
///   "bizim" tarafa eklenir ve düşman toplamından çıkarılır; etkin hakimiyet
///   yükseldiği için kayıp oranı düşer. Rastgelelik yine YOKTUR — korumanın
///   etkisi saf ağırlık hesabıdır. Tek state, "korunan → koruyan" tahsis
///   listesidir ve save/load'a yazılır.
/// </summary>
public static class ConvoyRaidSystem
{
    // Baskın kaybı çarpanı: temel oran (hakimiyet 0 = düşman tam kontrolündeyken).
    // %30 — HOI4'teki tipik konvoy kaybından mürekkep.
    private const float BaseRaidRate = 0.30f;

    // Günlük kayıp oranının üst sınırı (aşırı kesintiyi engellemek için).
    private const float MaxDailyLossFraction = 0.60f;

    // ===== M43.4: Müttefik koruması (eskort) sabitleri ve durumu =====

    /// <summary>
    /// Eskortun kayıp oranını azaltma üst sınırı. Güçlü bir müttefik donanması
    /// konvoyu rahatlatır ama yok etmez: pusu kuran denizaltı her zaman bir
    /// miktar risk taşır. %70 — zayıf koruma bile hissedilir, mükemmel koruma
    /// imkânsızdır.
    /// </summary>
    private const float MaxEscortEffect = 0.70f;

    // Koruma tahsisi: korunan ülke → koruyan ülke (müttefik).
    private static readonly Dictionary<string, string> _escorts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Koruma tahsisinin gerekçesi (UI mesajı ve test doğrulaması için).</summary>
    public enum EscortReason
    {
        Ok,
        SelfProtection,
        UnknownCountry,
        NotAllies,
        ProtectorHasNoFleet,
        Annexed
    }

    /// <summary>Koruma tahsisi sonucu — reddedilirse <see cref="Reason"/> nedenini taşır.</summary>
    public class EscortAssignment
    {
        public bool Accepted => Reason == EscortReason.Ok;
        public EscortReason Reason { get; set; } = EscortReason.Ok;
        public string ProtectorId { get; set; } = "";
        public string ProtectedId { get; set; } = "";
    }

    /// <summary>Günlük konvoy kaybı sonucu (test ve UI denetimi için).</summary>
    public class RaidResult
    {
        /// <summary>Bu gün batmış konvoy sayısı.</summary>
        public int ConvoysLost;

        /// <summary>İthalat hacmindeki düşüş yüzdesi [0,1].</summary>
        public float ImportShortfall;

        /// <summary>Baskın yapılan bölge (yoksa null — güvenli ticaret).</summary>
        public SeaZoneSystem.ZoneDefinition RaidedZone;

        /// <summary>Bölgedeki oyuncu hakimiyeti [0,100].</summary>
        public float SupremacyInZone;

        /// <summary>
        /// M43.4: Müttefik koruması DAHİL etkin hakimiyet [0,100].
        /// Koruma atanmamışsa <see cref="SupremacyInZone"/> ile aynıdır.
        /// </summary>
        public float EffectiveSupremacy;

        /// <summary>Konvoyları koruyan müttefik (koruma yoksa null).</summary>
        public string EscortId;

        /// <summary>Korumanın kayıp oranını azaltma oranı [0,1] (UI göstergesi).</summary>
        public float EscortReduction;

        /// <summary>Denizaltı saldırısı yaşandı mı?</summary>
        public bool WasRaided => ConvoysLost > 0;
    }

    /// <summary>
    /// Bir ülkenin ticaret güzergâhında baskına uğrayan bölgeyi bulur.
    /// Oyuncunun kontrol ETTİĞİ bölgeler güvenlidir; düşman hakimiyetindeki
    /// ilk bölge döner. Hiçbiri tehdit altında değilse null.
    /// </summary>
    public static SeaZoneSystem.ZoneDefinition FindRaidedZone(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return null;

        // Komşu bölgelerden, oyuncunun hakimiyeti en düşük olanı seçilir.
        // Hakimiyetin 50 altı = düşman ağırlıklı; orada konvoy riski doğar.
        SeaZoneSystem.ZoneDefinition worst = null;
        float worstSup = float.MaxValue;

        foreach (var zone in SeaZoneSystem.All)
        {
            if (!zone.AdjacentCountryIds.Any(id =>
                    string.Equals(id, countryId, StringComparison.OrdinalIgnoreCase)))
                continue;

            float sup = SeaZoneSystem.Supremacy(zone, countryId);
            if (sup < worstSup)
            {
                worstSup = sup;
                worst = zone;
            }
        }

        // 100 = tam kontrolümüz → tehdit yok. 50 altı = rakip ağırlıklı.
        if (worst == null || worstSup >= SeaZoneSystem.SafeSupremacyThreshold)
            return null;

        return worst;
    }

    // ===== M43.4: Koruma tahsisi =====

    /// <summary>
    /// Bir müttefik, diğerinin konvoylarına filo koruması tahsis eder.
    /// Koruyucu ile korunan aynı fraksiyonda olmalı (ya da koruyucu, korunanın
    /// bağımsızlığını garanti etmiş olmalı) ve koruyucunun filosu olmalı.
    /// Kendini korumak reddedilir.
    /// </summary>
    public static EscortAssignment AssignEscort(string protectorId, string protectedId)
    {
        var result = new EscortAssignment { ProtectorId = protectorId, ProtectedId = protectedId };

        if (string.IsNullOrEmpty(protectorId) || string.IsNullOrEmpty(protectedId))
        {
            result.Reason = EscortReason.UnknownCountry;
            GD.PrintErr($"[LendLease] HATA: geçersiz koruma tahsisi ({protectorId ?? "-"} → {protectedId ?? "-"})");
            return result;
        }

        if (string.Equals(protectorId, protectedId, StringComparison.OrdinalIgnoreCase))
        {
            result.Reason = EscortReason.SelfProtection;
            GD.PrintErr("[LendLease] HATA: bir ülke kendi konvoylarıını koruyamaz");
            return result;
        }

        var protector = CountryDatabase.Get(protectorId);
        var protectedCountry = CountryDatabase.Get(protectedId);
        if (protector == null || protectedCountry == null)
        {
            result.Reason = EscortReason.UnknownCountry;
            GD.PrintErr($"[LendLease] HATA: ülke bulunamadı ({protectorId} → {protectedId})");
            return result;
        }

        if (protector.IsAnnexed || protectedCountry.IsAnnexed)
        {
            result.Reason = EscortReason.Annexed;
            GD.PrintErr("[LendLease] HATA: ilhak edilmiş ülke koruma veremez/alıamaz");
            return result;
        }

        if (!IsAllied(protectorId, protectedId))
        {
            result.Reason = EscortReason.NotAllies;
            GD.PrintErr($"[LendLease] HATA: {protectorId} ile {protectedId} müttefik değil");
            return result;
        }

        if (SeaZoneSystem.FleetWeight(protectorId) <= 0f)
        {
            result.Reason = EscortReason.ProtectorHasNoFleet;
            GD.PrintErr($"[LendLease] HATA: {protectorId} koruma için filoya sahip değil");
            return result;
        }

        _escorts[protectedId] = protectorId;
        result.Reason = EscortReason.Ok;
        GD.Print($"[LendLease] Koruma tahsis edildi: {protectorId} → {protectedId} konvoyları");
        return result;
    }

    /// <summary>Koruma tahsisini geri alır (koruyucu filoyu başkaye çeker).</summary>
    public static bool RevokeEscort(string protectedId)
    {
        if (string.IsNullOrEmpty(protectedId)) return false;

        bool removed = _escorts.Remove(protectedId);
        if (removed)
            GD.Print($"[LendLease] {protectedId} konvoy koruması geri alındı");
        return removed;
    }

    /// <summary>Korunan ülkeyi koruyan müttefik (tahsis yoksa null).</summary>
    public static string EscortOf(string protectedId)
    {
        if (string.IsNullOrEmpty(protectedId)) return null;
        return _escorts.TryGetValue(protectedId, out var protectorId) ? protectorId : null;
    }

    /// <summary>Aktif tüm koruma tahsisleri (UI listesi ve save/load için).</summary>
    public static IReadOnlyCollection<KeyValuePair<string, string>> AllEscorts => _escorts;

    /// <summary>
    /// İki ülke koruma bağı kurabilir mi? Fraksiyon ortaklığı yeterlidir;
    /// fraksiyon dışı durumda koruyucunun bağımsızlık garantisi de kabul edilir
    /// (garantisiz ülkeler birbirini koruyamaz).
    /// </summary>
    public static bool IsAllied(string aId, string bId)
    {
        if (string.IsNullOrEmpty(aId) || string.IsNullOrEmpty(bId)) return false;
        if (string.Equals(aId, bId, StringComparison.OrdinalIgnoreCase)) return false;

        var fa = DiplomacySystem.GetFactionOf(aId);
        var fb = DiplomacySystem.GetFactionOf(bId);
        if (fa != null && fb != null &&
            string.Equals(fa.Id, fb.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        return DiplomacySystem.HasGuarantee(aId, bId) || DiplomacySystem.HasGuarantee(bId, aId);
    }

    /// <summary>
    /// Geçersizleşmiş tahsisleri temizler: müttefik fraksiyondan ayrıldı,
    /// bağımsızlık garanti iptal oldu ya da ülke ilhak edildi. Save verisini
    /// şişirmeden tutar; etkisiz tahsis okuma anında zaten yok sayılır.
    /// </summary>
    public static int PruneInvalidEscorts()
    {
        var stale = new List<string>();
        foreach (var pair in _escorts)
        {
            if (!IsAllied(pair.Value, pair.Key))
                stale.Add(pair.Key);
        }

        foreach (var id in stale) _escorts.Remove(id);
        return stale.Count;
    }

    /// <summary>
    /// Koruma DAHİL etkin hakimiyet. Koruyucunun bölge ağırlığı "bizim" tarafa
    /// eklenir ve düşman toplamından çıkarılır. Koruyucu bölgeye komşu değilse
    /// filo oraya ulaşamaz → etki yok.
    /// </summary>
    public static float EffectiveSupremacy(SeaZoneSystem.ZoneDefinition zone, string countryId)
    {
        if (zone == null || string.IsNullOrEmpty(countryId)) return 50f;

        float mine = SeaZoneSystem.ZoneWeight(zone, countryId);
        float theirs = 0f;
        foreach (var id in zone.AdjacentCountryIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (string.Equals(id, countryId, StringComparison.OrdinalIgnoreCase)) continue;
            theirs += SeaZoneSystem.ZoneWeight(zone, id);
        }

        string protectorId = EscortOf(countryId);
        if (protectorId != null)
        {
            var protector = CountryDatabase.Get(protectorId);
            bool adjacent = zone.AdjacentCountryIds.Any(id =>
                string.Equals(id, protectorId, StringComparison.OrdinalIgnoreCase));

            if (protector != null && !protector.IsAnnexed &&
                IsAllied(protectorId, countryId) && adjacent)
            {
                float protectorWeight = SeaZoneSystem.ZoneWeight(zone, protectorId);
                mine += protectorWeight;
                theirs -= Mathf.Min(theirs, protectorWeight);
            }
        }

        float sum = mine + theirs;
        if (sum <= 0f) return 50f;

        float norm = (mine - theirs) / sum;
        return Mathf.Clamp(50f + 50f * norm, 0f, 100f);
    }

    /// <summary>
    /// Korumanın günlük kayıp riskindeki azalma yüzdesi [0,100].
    /// UI'da "koruma verildiğinde risk %X düşer" göstergesi için kullanılır.
    /// </summary>
    public static float EscortReductionPercent(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return 0f;

        var zone = FindRaidedZone(countryId);
        if (zone == null) return 0f;

        float ownSup = SeaZoneSystem.Supremacy(zone, countryId);
        float effSup = EffectiveSupremacy(zone, countryId);
        return LossReduction(ownSup, effSup) * 100f;
    }

    /// <summary>
    /// Etkin hakimiyetin kayıp oranını ne kadar düşürdüğü [0,1].
    /// Koruma yoksa 0 döner (etkin = kendi hakimiyet).
    /// </summary>
    public static float LossReduction(float ownSup, float effectiveSup)
    {
        float ownLoss = LossFraction(100f - Mathf.Clamp(ownSup, 0f, 100f));
        float effLoss = LossFraction(100f - Mathf.Clamp(effectiveSup, 0f, 100f));
        if (ownLoss <= 0f) return 0f;
        return Mathf.Clamp((ownLoss - effLoss) / ownLoss, 0f, 1f);
    }

    private static float LossFraction(float enemyDominance)
    {
        float loss = BaseRaidRate * (Mathf.Clamp(enemyDominance, 0f, 100f) / 100f);
        return Mathf.Clamp(loss, 0f, MaxDailyLossFraction);
    }

    /// <summary>
    /// O günkü konvoy kaybını hesaplar. Ekonomi ve dünya durumunu DEĞİŞTİRMEZ;
    /// yalnızca sonucu üretir (saf hesap + DeterministicRNG).
    /// </summary>
    public static RaidResult ComputeDailyRaids(
        string countryId,
        int activeConvoys,
        int dailyImportVolume)
    {
        var result = new RaidResult();

        // Boş güzergâh → kayıp yok (ne konvoy ne de ithalat varsa saldırı manzarası değil).
        if (activeConvoys <= 0)
        {
            result.ConvoysLost = 0;
            result.ImportShortfall = 0f;
            return result;
        }

        var zone = FindRaidedZone(countryId);
        if (zone == null)
        {
            // Güvenli bölge: kayıp yok, ithalat tam.
            result.ConvoysLost = 0;
            result.ImportShortfall = 0f;
            result.SupremacyInZone = SeaZoneSystem.AverageSupremacy(countryId);
            result.EffectiveSupremacy = result.SupremacyInZone;
            return result;
        }

        float mySup = SeaZoneSystem.Supremacy(zone, countryId);
        result.RaidedZone = zone;
        result.SupremacyInZone = mySup;

        // M43.4: Müttefik eskortu bölgenin etkin hakimiyetini yükseltir.
        float effSup = EffectiveSupremacy(zone, countryId);
        result.EffectiveSupremacy = effSup;
        result.EscortId = EscortOf(countryId);
        result.EscortReduction = LossReduction(mySup, effSup);

        // Kayıp oranı: düşman hakimiyetinken orantılı.
        float enemyDominance = Mathf.Clamp(100f - mySup, 0f, 100f);
        float lossFraction = LossFraction(enemyDominance);

        // Korumasız temel kayıp, korumanın azalttığı oran kadar düşürülür.
        // Azalma üst sınırı MaxEscortEffect: mükemmel koruma bile kaybı
        // sıfıra indirmez — pusu kuran denizaltı her zaman risk taşır.
        lossFraction *= 1f - MaxEscortEffect * result.EscortReduction;
        lossFraction = Mathf.Clamp(lossFraction, 0f, MaxDailyLossFraction);

        // Konvoy kaybı: aktif konvoyların bir kısmı (yuvarlanmış, en fazla konvoy sayısı).
        // Beklenen kayıp = aktif konvoy × kayıp oranı.
        // Üzerine ikinci bir rastgele kapı UYGULANMAZ: beklenen değer zaten olasılığı
        // taşıdığı için ayrı bir zar atmak rastgeleliği çift sayar ve günlük kaybı
        // konvoy sayısıyla monoton olmaktan çıkarır. Sistem böylece saf bir fonksiyon
        // olur ve DeterministicRNG'ye ihtiyaç duymaz.
        int lost = (int)Math.Round(activeConvoys * lossFraction);
        lost = Math.Max(0, Math.Min(lost, activeConvoys));

        result.ConvoysLost = lost;

        // İthalat düşüşü: batmış konvoy oranı (kaybedilen konvoy taşıdığı kaynağı de taşır).
        result.ImportShortfall = activeConvoys > 0 ? lost / (float)activeConvoys : 0f;

        return result;
    }

    /// <summary>
    /// Günlük kaybı ekonomiye uygular: konvoyları düşer, ithalat hacmini kısar,
    /// loglanır. Pure sonucu <see cref="ComputeDailyRaids"/> üzerinden alır.
    /// </summary>
    public static RaidResult ApplyDailyRaids(string countryId)
    {
        var result = new RaidResult();

        if (string.IsNullOrEmpty(countryId)) return result;

        // M43.4: Müttefiklik bozulmuş tahsisleri save/load şişmesin diye temizle.
        PruneInvalidEscorts();

        int convoys = EconomySystem.ConvoyCount;
        int importVolume = EconomySystem.TradeDeals.Sum(d => d.AmountPerDay);

        result = ComputeDailyRaids(countryId, convoys, importVolume);

        if (result.WasRaided)
        {
            // Konvoyları düş (ekonomi sisteminin sayacını aşağı çek).
            EconomySystem.LoseConvoys(result.ConvoysLost);

            // İthalat hacmini kıs: anlaşmaların miktarını oranla düşür.
            ApplyImportShortfall(result.ImportShortfall);

            string escort = result.EscortId != null
                ? $", koruma={result.EscortId} (-%{result.EscortReduction * 100f:F0})"
                : ", koruma yok";

            GD.Print($"[ConvoyRaid] {countryId} konvoylarına saldırıldı — bölge " +
                     $"{result.RaidedZone?.Name} ({result.RaidedZone?.Id}), " +
                     $"hakimiyet={result.SupremacyInZone:F1}{escort}, " +
                     $"etkin hakimiyet={result.EffectiveSupremacy:F1}, " +
                     $"batan konvoy={result.ConvoysLost}, " +
                     $"ithalat düşüşü=%{result.ImportShortfall * 100f:F0}");
        }

        return result;
    }

    /// <summary>
    /// İthalat anlaşmalarını kısalma oranına göre daraltır. Bir kaynak için
    /// anlaşma miktarı 1 birim/günün altına düşerse anlaşma düşer (ihracatçı
    /// konuyu bırakır).
    /// </summary>
    private static void ApplyImportShortfall(float shortfall)
    {
        if (shortfall <= 0f) return;

        var deals = EconomySystem.TradeDeals.ToList();
        foreach (var deal in deals)
        {
            int reduced = (int)Math.Ceiling(deal.AmountPerDay * (1f - shortfall));
            reduced = Math.Max(0, reduced);

            if (reduced <= 0)
            {
                // Anlaşma çöktü: konvoyları tamamen geri ver ve anlaşmayı kaldır.
                EconomySystem.ReleaseTradeDeal(deal.Resource);
                GD.Print($"[ConvoyRaid] Anlaşma çöktü: {deal.Resource} " +
                         $"(ihracatçı: {deal.ExporterName})");
            }
            else if (reduced < deal.AmountPerDay)
            {
                EconomySystem.ReduceTradeDeal(deal.Resource, reduced);
            }
        }
    }

    /// <summary>
    /// Belirli bir bölgedeki günlük konvoy kaybı riski [0,100] — M43.5 panelinde
    /// bölge satırlarının "risk" göstergesi olarak kullanılır.
    ///
    /// Etkin hakimiyet (müttefik koruması dahil) üzerinden hesaplanır; böylece
    /// panel, günlük uygulanan kayıpla aynı sayıyı gösterir.
    /// </summary>
    public static float ZoneRiskPercent(SeaZoneSystem.ZoneDefinition zone, string countryId)
    {
        if (zone == null || string.IsNullOrEmpty(countryId)) return 0f;

        float mySup = SeaZoneSystem.Supremacy(zone, countryId);
        float effSup = EffectiveSupremacy(zone, countryId);
        float enemyDominance = Mathf.Clamp(100f - effSup, 0f, 100f);
        float loss = LossFraction(enemyDominance);
        loss *= 1f - MaxEscortEffect * LossReduction(mySup, effSup);
        return Mathf.Clamp(loss, 0f, MaxDailyLossFraction) * 100f;
    }

    /// <summary>
    /// Bir ülkenin tüm aktif konvoy güzergâhlarındaki toplam günlük risk yüzdesi.
    /// UI'da "konvoy riski" göstergesi için kullanılır.
    /// </summary>
    public static float TotalRiskPercent(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return 0f;

        var zone = FindRaidedZone(countryId);
        if (zone == null) return 0f;

        return ZoneRiskPercent(zone, countryId);
    }

    // ===== Save/Load + yeni oyun (M43.4) =====

    public static SaveSystem.ConvoyRaidSave GetSaveData()
    {
        var escorts = new List<SaveSystem.ConvoyEscortEntry>(_escorts.Count);
        foreach (var pair in _escorts)
        {
            escorts.Add(new SaveSystem.ConvoyEscortEntry
            {
                ProtectedId = pair.Key,
                ProtectorId = pair.Value
            });
        }

        return new SaveSystem.ConvoyRaidSave { Escorts = escorts.ToArray() };
    }

    public static void LoadSaveData(SaveSystem.ConvoyRaidSave data)
    {
        _escorts.Clear();
        if (data?.Escorts == null)
        {
            GD.Print("[LendLease] Koruma tahsisi yok (kayıt boş)");
            return;
        }

        foreach (var entry in data.Escorts)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ProtectedId) ||
                string.IsNullOrEmpty(entry.ProtectorId))
                continue;

            _escorts[entry.ProtectedId] = entry.ProtectorId;
        }

        GD.Print($"[LendLease] Koruma tahsisleri yüklendi: {_escorts.Count}");
    }

    public static void ResetForNewGame() => _escorts.Clear();
}
