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
/// </summary>
public static class ConvoyRaidSystem
{
    // Baskın kaybı çarpanı: temel oran (hakimiyet 0 = düşman tam kontrolündeyken).
    // %30 — HOI4'teki tipik konvoy kaybından mürekkep.
    private const float BaseRaidRate = 0.30f;

    // Günlük kayıp oranının üst sınırı (aşırı kesintiyi engellemek için).
    private const float MaxDailyLossFraction = 0.60f;

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
            return result;
        }

        float mySup = SeaZoneSystem.Supremacy(zone, countryId);
        result.RaidedZone = zone;
        result.SupremacyInZone = mySup;

        // Düşman hakimiyeti: 100 − benim hakimiyetim.
        float enemyDominance = Mathf.Clamp(100f - mySup, 0f, 100f);

        // Kayıp oranı: düşman hakimiyetinken orantılı.
        float lossFraction = BaseRaidRate * (enemyDominance / 100f);
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

        int convoys = EconomySystem.ConvoyCount;
        int importVolume = EconomySystem.TradeDeals.Sum(d => d.AmountPerDay);

        result = ComputeDailyRaids(countryId, convoys, importVolume);

        if (result.WasRaided)
        {
            // Konvoyları düş (ekonomi sisteminin sayacını aşağı çek).
            EconomySystem.LoseConvoys(result.ConvoysLost);

            // İthalat hacmini kıs: anlaşmaların miktarını oranla düşür.
            ApplyImportShortfall(result.ImportShortfall);

            GD.Print($"[ConvoyRaid] {countryId} konvoylarına saldırıldı — bölge " +
                     $"{result.RaidedZone?.Name} ({result.RaidedZone?.Id}), " +
                     $"hakimiyet={result.SupremacyInZone:F1}, " +
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
    /// Bir ülkenin tüm aktif konvoy güzergâhlarındaki toplam günlük risk yüzdesi.
    /// UI'da "konvoy riski" göstergesi için kullanılır.
    /// </summary>
    public static float TotalRiskPercent(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return 0f;

        var zone = FindRaidedZone(countryId);
        if (zone == null) return 0f;

        float mySup = SeaZoneSystem.Supremacy(zone, countryId);
        float enemyDominance = Mathf.Clamp(100f - mySup, 0f, 100f);
        float loss = BaseRaidRate * (enemyDominance / 100f);
        loss = Mathf.Clamp(loss, 0f, MaxDailyLossFraction);
        return loss * 100f;
    }
}
