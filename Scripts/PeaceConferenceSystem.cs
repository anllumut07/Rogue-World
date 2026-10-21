using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Barış konferansı tercihleri ve puan harcama kuralları (M42.2).
///
/// `Data/peace_terms.json` taleplerin savaş puanı maliyetini tanımlar. Sistem
/// saf hesaptır: rastgelelik YOKTUR ve talep listesi (state) tutulmaz —
/// maliyet her çağrıda aynı veriden üretilir. Böylece ayrı bir DTO gerekmez
/// (M42.5'te savaş sonu anlık görüntüsü WarSystem kaydında zaten duruyor).
///
/// Uygulama (il devri, fabrika tazminatı) M42.3'te `PeaceConferenceSystem`
/// üzerine kurulacak; burada yalnızca maliyet/doğrulama mantığı var.
/// </summary>
public static class PeaceConferenceSystem
{
    private const string DataPath = "res://Data/peace_terms.json";

    /// <summary>Talep türleri — JSON "Kind" alanından eşlenir.</summary>
    public enum TermKind
    {
        Unknown,
        Province,        // il talebi (birim = il sayısı)
        Annex,           // ülke ilhakı
        Puppet,          // kukla devlet
        Liberate,        // bağımsız devlet çıkarma
        Reparations,     // fabrika/PP tazminatı (birim = fabrika paketi)
        Demilitarize     // askeri fabrika sepeti
    }

    /// <summary>Bir barış talebi türünün tanımı (JSON tek kaydı).</summary>
    public class TermDefinition
    {
        public string Id = "";
        public string Label = "";
        public string Description = "";
        public string Kind = "";
        public double BaseCost = 0;
        public double CostPerUnit = 0;
        public string UnitLabel = "";
        public int MaxUnits = 1;
        public double RequiresScoreAtLeast = 0;
        public double TensionCost = 0;

        public TermKind KindEnum => ParseKind(Kind);
        public static TermKind ParseKind(string kind) => (kind ?? "").ToLowerInvariant() switch
        {
            "province" => TermKind.Province,
            "annex" => TermKind.Annex,
            "puppet" => TermKind.Puppet,
            "liberate" => TermKind.Liberate,
            "reparations" => TermKind.Reparations,
            "demilitarize" => TermKind.Demilitarize,
            _ => TermKind.Unknown
        };
    }

    /// <summary>Deal seviyesinde sınırlar (JSON "Limits" bloğu).</summary>
    public class LimitsDefinition
    {
        public double MinScoreForAnyDemand = 10;
        public int MaxTermsPerDeal = 4;
        public bool LiberateRemovesOverflow = true;
    }

    private class Root
    {
        public int Version = 1;
        public List<TermDefinition> Terms = new();
        public LimitsDefinition Limits = new();
    }

    // Talep türü id → tanım
    private static readonly Dictionary<string, TermDefinition> _terms = new(StringComparer.OrdinalIgnoreCase);
    private static LimitsDefinition _limits = new();
    private static bool _loaded;

    /// <summary>Toplam talep türü sayısı (log/yükleme doğrulaması için).</summary>
    public static int TermCount
    {
        get { EnsureLoaded(); return _terms.Count; }
    }

    /// <summary>Deal sınırları (erken kullanım güvenli olsun diye lazy yüklenir).</summary>
    public static LimitsDefinition Limits
    {
        get { EnsureLoaded(); return _limits; }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        LoadData();
    }

    /// <summary>JSON'u bir kez okur. `IncludeFields=true` olmadan alanlar doldurulmaz.</summary>
    public static void LoadData()
    {
        _terms.Clear();
        _limits = new LimitsDefinition();

        if (!Godot.FileAccess.FileExists(DataPath))
        {
            GD.PrintErr($"[PeaceTerms] HATA: {DataPath} bulunamadı");
            _loaded = true;
            return;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = true
            };
            var json = Godot.FileAccess.GetFileAsString(DataPath);
            var root = JsonSerializer.Deserialize<Root>(json, options);
            if (root?.Terms != null)
            {
                foreach (var t in root.Terms)
                    if (!string.IsNullOrEmpty(t.Id))
                        _terms[t.Id] = t;
                if (root.Limits != null) _limits = root.Limits;
            }
            GD.Print($"[PeaceTerms] {_terms.Count} barış talebi türü yüklendi ({DataPath}, IncludeFields=true)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PeaceTerms] HATA: JSON okunamadı — {ex.Message}");
        }

        _loaded = true;
    }

    /// <summary>Talep türü tanımı; yoksa null.</summary>
    public static TermDefinition Get(string termId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(termId)) return null;
        return _terms.TryGetValue(termId, out var def) ? def : null;
    }

    /// <summary>Tüm talep türleri (UI listesi).</summary>
    public static IReadOnlyList<TermDefinition> All =>
        (IReadOnlyList<TermDefinition>)EnsureAll();

    private static List<TermDefinition> EnsureAll()
    {
        EnsureLoaded();
        return _terms.Values
            .OrderByDescending(t => t.RequiresScoreAtLeast)
            .ToList();
    }

    // ===== Maliyet hesabı =====

    /// <summary>
    /// Bir talebin savaş puanı maliyeti: taban + birim × adet.
    /// Adet 1'in altına düşürülmez, türün üst sınırı aşılamaz.
    /// </summary>
    public static double CostFor(string termId, int units)
    {
        var def = Get(termId);
        if (def == null) return 0;
        return CostFor(def, units);
    }

    public static double CostFor(TermDefinition def, int units)
    {
        if (def == null) return 0;
        int u = Math.Clamp(units, 1, Math.Max(1, def.MaxUnits));
        return def.BaseCost + def.CostPerUnit * u;
    }

    /// <summary>Bir talep türünün bu savaş puanında talep edilebilir olup olmadığı.</summary>
    public static bool IsAvailable(string termId, float warScore)
    {
        var def = Get(termId);
        if (def == null) return false;
        if (warScore + 0.001f < def.RequiresScoreAtLeast) return false;
        if (warScore + 0.001f < Limits.MinScoreForAnyDemand) return false;
        return true;
    }

    /// <summary>Doğrulama sonucu — neden reddedildiği loglanabilir olsun.</summary>
    public class CheckResult
    {
        public bool Ok { get; set; }
        public string Reason { get; set; } = "";
        public double Cost { get; set; }
        public double RemainingScore { get; set; }
        public int Units { get; set; }
        public TermDefinition Definition { get; set; }
    }

    /// <summary>
    /// Talebi savaş puanına karşı doğrular: tür biliniyor mu, adet geçerli mi,
    /// puan eşiği ve toplam maliyet karşılanıyor mu. Sonuç tek nesnede döner.
    /// </summary>
    public static CheckResult Check(string termId, int units, float warScore)
    {
        var res = new CheckResult { Units = units };

        var def = Get(termId);
        if (def == null)
        {
            res.Reason = $"unknown term '{termId}'";
            return res;
        }
        res.Definition = def;

        if (warScore + 0.001f < Limits.MinScoreForAnyDemand)
        {
            res.Reason = $"score {warScore:F1} below minimum for any demand ({Limits.MinScoreForAnyDemand:F1})";
            return res;
        }

        if (warScore + 0.001f < def.RequiresScoreAtLeast)
        {
            res.Reason = $"score {warScore:F1} below requirement for '{def.Id}' ({def.RequiresScoreAtLeast:F1})";
            return res;
        }

        // Adet [1, MaxUnits] arasına kırpılır — 0 adet talep anlamsızdır.
        int clamped = Math.Clamp(units, 1, Math.Max(1, def.MaxUnits));
        res.Units = clamped;

        double cost = CostFor(def, clamped);

        if (cost > warScore + 0.001)
        {
            res.Cost = cost;
            res.RemainingScore = 0;
            res.Reason = $"score {warScore:F1} insufficient for '{def.Id}' (needs {cost:F1})";
            return res;
        }

        res.Ok = true;
        res.Cost = cost;
        res.RemainingScore = Math.Max(0, warScore - cost);
        return res;
    }

    /// <summary>Deal'de izin verilen en fazla talep sayısı.</summary>
    public static int MaxTermsPerDeal => Limits.MaxTermsPerDeal;
}
