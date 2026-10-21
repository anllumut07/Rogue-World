using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Ulusal skor ve canlı sıralama (M41.3).
/// Fabrika / tümen / il / nüfus / teknoloji ağırlıklı tek skor; tüm ülkeler için sıralı tablo.
/// Saf hesap — DeterministicRNG kullanılmaz (determinizm korunur), süreklilik için state yok
/// (her an mevcut durumdan hesaplanır). Skor yalnızca `top_score` zafer koşulu ve oyun sonu
/// ekranı için kullanılır.
/// </summary>
public static class ScoreSystem
{
    public class Entry
    {
        public string CountryId;
        public string Name;
        public double Score;
        public int Rank;
    }

    private const double CivFactoryWeight = 8.0;
    private const double MilFactoryWeight = 12.0;
    private const double DockyardWeight = 15.0;
    private const double DivisionWeight = 1.5;
    private const double ProvinceWeight = 0.4;
    private const double PopulationWeight = 0.6;   // milyon başına
    private const double TechLevelWeight = 25.0;

    /// <summary>Bir ülkenin ulusal skoru (ilhak edilmişse 0).</summary>
    public static double ComputeScore(string countryId)
    {
        var c = CountryDatabase.Get(countryId);
        if (c == null || c.IsAnnexed) return 0;

        double score = 0;
        score += Math.Max(0, c.CivFactories) * CivFactoryWeight;
        score += Math.Max(0, c.MilFactories) * MilFactoryWeight;
        score += Math.Max(0, c.Dockyards) * DockyardWeight;
        score += Math.Max(0, CountryAI.GetDivisions(countryId)) * DivisionWeight;
        score += ProvinceOwnershipSystem.ProvinceCountFor(countryId) * ProvinceWeight;
        score += c.Population / 1_000_000.0 * PopulationWeight;
        score += Math.Max(0, CountryDatabase.GetTechLevel(countryId)) * TechLevelWeight;
        return score;
    }

    /// <summary>Bir ülkenin skor kırılımı (oyun sonu ekranı için).</summary>
    public static Dictionary<string, double> ScoreBreakdown(string countryId)
    {
        var parts = new Dictionary<string, double>();
        var c = CountryDatabase.Get(countryId);
        if (c == null || c.IsAnnexed) return parts;

        parts["CivilianFactories"] = Math.Max(0, c.CivFactories) * CivFactoryWeight;
        parts["MilitaryFactories"] = Math.Max(0, c.MilFactories) * MilFactoryWeight;
        parts["Dockyards"] = Math.Max(0, c.Dockyards) * DockyardWeight;
        parts["Divisions"] = Math.Max(0, CountryAI.GetDivisions(countryId)) * DivisionWeight;
        parts["Provinces"] = ProvinceOwnershipSystem.ProvinceCountFor(countryId) * ProvinceWeight;
        parts["Population"] = c.Population / 1_000_000.0 * PopulationWeight;
        parts["Technology"] = Math.Max(0, CountryDatabase.GetTechLevel(countryId)) * TechLevelWeight;
        return parts;
    }

    /// <summary>Skora göre azalan sıralı tablo (ilk = en yüksek skor).</summary>
    public static List<Entry> GetRanking()
    {
        var entries = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed)
            .Select(c => new Entry { CountryId = c.Id, Name = c.Name, Score = ComputeScore(c.Id) })
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.CountryId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < entries.Count; i++)
            entries[i].Rank = i + 1;
        return entries;
    }

    /// <summary>Bir ülkenin sıralaması (1 = en iyi). Bulunamazsa -1.</summary>
    public static int GetRank(string countryId)
    {
        var e = GetRanking().FirstOrDefault(x => string.Equals(x.CountryId, countryId, StringComparison.OrdinalIgnoreCase));
        return e?.Rank ?? -1;
    }

    /// <summary>Bir ülke en yüksek skora sahip mi? (top_score koşulu).</summary>
    public static bool IsTopCountry(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return false;
        double mine = ComputeScore(countryId);
        if (mine <= 0) return false;
        return !CountryDatabase.All().Any(c =>
            c != null && !c.IsAnnexed &&
            !string.Equals(c.Id, countryId, StringComparison.OrdinalIgnoreCase) &&
            ComputeScore(c.Id) > mine);
    }

    /// <summary>En yüksek skorlu ülkenin id'si.</summary>
    public static string GetLeaderId()
    {
        var top = GetRanking().FirstOrDefault();
        return top?.CountryId ?? "";
    }

    /// <summary>İlk N sırayı loglar (--score-test / oyun sonu).</summary>
    public static void LogTop(int count = 10)
    {
        var ranking = GetRanking();
        GD.Print($"[Skor] Sıralama ({ranking.Count} ülke) — ilk {Mathf.Min(count, ranking.Count)}:");
        foreach (var e in ranking.Take(count))
            GD.Print($"[Skor]   #{e.Rank} {e.Name} ({e.CountryId}) — {e.Score:F1}");
    }
}
