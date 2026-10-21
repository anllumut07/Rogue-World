using Godot;
using System;

/// <summary>
/// Savaş puanı birikimi (M42.1).
///
/// HOI4'un "war score" mekaniğinin sadeleştirilmiş hali: her savaş için iki tarafın
/// ayrı puanı ([0,100]) tutulur. Puan, savaşın başındaki düşman durumu (tümen, fabrika)
/// ile o anki durum arasındaki farktan, günlük çatışma kazanımlarından ve işgal
/// oranından (WarProgress) hesaplanır.
///
/// Saf hesap: DeterministicRNG KULLANILMAZ ve sistemde birikim state'i yok — her an
/// var olan veriden üretilir. Böylece save/load determinizmi korunur ve ayrı bir DTO
/// gerekmez (yalnızca sayaçlar War kaydında taşınır).
///
/// Beyaz barış eşiği: 50 (her iki taraf için de). 50 üzeri = saldırgan lehine.
/// </summary>
public static class WarScoreSystem
{
    /// <summary>Beyaz barış / pazarlık eşiği — iki taraf için de geçerli dengesizlik sınırı.</summary>
    public const float WhitePeaceThreshold = 50f;

    // Puan ağırlıkları (toplamı 100).
    private const float WeightOccupation = 55f;   // işgal oranı (WarProgress)
    private const float WeightManpower = 20f;     // düşman tümen kaybı
    private const float WeightIndustry = 15f;     // düşman fabrika kaybı
    private const float WeightBattles = 10f;      // kazanılan çatışma oranı

    /// <summary>Saldırgan tarafın puanı (0-100).</summary>
    public static float AttackerScore(WarSystem.War war)
    {
        if (war == null) return 0f;
        // Biten savaşta puan kesin: kaybeden 0, kazanan 100 (kısmi paylar toplamı 95'e
        // düşebileceğinden kapitülasyonda sabitlenir).
        if (war.IsEnded)
            return war.DefenderCapitulated ? 100f : 0f;

        return Math.Clamp(
            OccupationPart(war) + ManpowerPart(war) + IndustryPart(war) + BattlePart(war),
            0f, 100f);
    }

    /// <summary>Savunan tarafın puanı (0-100) — saldırganın eksiği.</summary>
    public static float DefenderScore(WarSystem.War war)
    {
        if (war == null) return 0f;
        return 100f - AttackerScore(war);
    }

    /// <summary>Bir ülkenün bu savaştaki puanı (taraf bağımsız erişim).</summary>
    public static float ScoreForSide(WarSystem.War war, string countryId)
    {
        if (war == null || string.IsNullOrEmpty(countryId)) return 0f;

        if (string.Equals(war.AttackerId, countryId, StringComparison.OrdinalIgnoreCase))
            return AttackerScore(war);
        if (string.Equals(war.DefenderId, countryId, StringComparison.OrdinalIgnoreCase))
            return DefenderScore(war);

        return 0f; // savaş dışı ülke
    }

    /// <summary>Bir ülke lehine sonuçlanabilir mi? (barış pazarlığı için eşik kontrolü)</summary>
    public static bool IsFavourable(WarSystem.War war, string countryId)
    {
        return ScoreForSide(war, countryId) >= WhitePeaceThreshold;
    }

    // ===== Puan kırılımı =====

    /// <summary>İşgal payı: WarProgress doğrudan kullanılır (0-100 → 0-55).</summary>
    private static float OccupationPart(WarSystem.War war)
    {
        if (war.IsEnded && war.DefenderCapitulated) return WeightOccupation;
        return Math.Clamp(war.WarProgress, 0f, 100f) / 100f * WeightOccupation;
    }

    /// <summary>Tümen kaybı payı: savaş başındaki düşman tümenine göre oran.</summary>
    private static float ManpowerPart(WarSystem.War war)
    {
        int start = Math.Max(1, war.StartEnemyDivisions);
        int now = GetCurrentEnemyDivisions(war);
        float lostRatio = Math.Clamp((start - now) / (float)start, 0f, 1f);
        if (war.IsEnded && war.AttackerCapitulated) return 0f;
        return lostRatio * WeightManpower;
    }

    /// <summary>Fabrika kaybı payı: kapitülasyonda %50 fabrika transferi mevcut modelde yansır.</summary>
    private static float IndustryPart(WarSystem.War war)
    {
        int start = Math.Max(1, war.StartEnemyFactories);
        int now = GetCurrentEnemyFactories(war);
        float lostRatio = Math.Clamp((start - now) / (float)start, 0f, 1f);
        if (war.IsEnded && war.AttackerCapitulated) return 0f;
        return lostRatio * WeightIndustry;
    }

    /// <summary>Çatışma kazanımı payı: iki tarafın kazandığı çatışma oranı.</summary>
    private static float BattlePart(WarSystem.War war)
    {
        int att = Math.Max(0, war.AttackerBattlesWon);
        int def = Math.Max(0, war.DefenderBattlesWon);
        int total = att + def;
        if (total <= 0) return WeightBattles / 2f; // veri yoksa nötr
        return att / (float)total * WeightBattles;
    }

    // ===== Yardımcılar =====

    /// <summary>Düşmanın (Una savunan taraf) o anki tümen sayısı.</summary>
    private static int GetCurrentEnemyDivisions(WarSystem.War war)
    {
        // Biten savaşta kayıp tamdır
        if (war.IsEnded && war.DefenderCapitulated) return 0;
        if (war.IsEnded && war.AttackerCapitulated) return Math.Max(0, war.StartEnemyDivisions);
        return Math.Max(0, war.EnemyDivisions);
    }

    /// <summary>Düşmanın o anki toplam fabrika sayısı (sivil + askeri + tersane).</summary>
    private static int GetCurrentEnemyFactories(WarSystem.War war)
    {
        var enemy = CountryDatabase.Get(war.DefenderId);
        if (enemy == null) return Math.Max(0, war.StartEnemyFactories);
        if (war.IsEnded && enemy.IsAnnexed) return 0;
        return Math.Max(0, enemy.CivFactories) + Math.Max(0, enemy.MilFactories) + Math.Max(0, enemy.Dockyards);
    }

    /// <summary>Puan dökümü (UI/log) — kısmi paylarla birlikte.</summary>
    public static string Breakdown(WarSystem.War war)
    {
        if (war == null) return "—";
        return $"İşgal {OccupationPart(war):F1}/{WeightOccupation:F0}, " +
               $"Tümen {ManpowerPart(war):F1}/{WeightManpower:F0}, " +
               $"Fabrika {IndustryPart(war):F1}/{WeightIndustry:F0}, " +
               $"Çatışma {BattlePart(war):F1}/{WeightBattles:F0}";
    }

    /// <summary>Savaş puanı özeti loglar (--war-score-test / barış ekranı).</summary>
    public static void LogScore(WarSystem.War war)
    {
        if (war == null) return;
        GD.Print($"[WarScore] {war.AttackerId} vs {war.DefenderId} — " +
                 $"saldıran {AttackerScore(war):F1} / savunan {DefenderScore(war):F1} " +
                 $"({Breakdown(war)})");
    }

    /// <summary>Aktif tüm savaşların puanlarını loglar.</summary>
    public static void LogAll()
    {
        foreach (var war in WarSystem.ActiveWars)
            LogScore(war);
    }
}
