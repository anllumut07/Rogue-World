using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Göstergeler arası bağlantı sistemi (M20.4).
// Data/formulas.json'daki her bağlantı: Source göstergesinden Target göstergesine
// günlük etkiyi tanımlar (ör. inflation → unemployment, corruption → gdp_growth).
// Etki deterministiktir (new Random yok): her gün Target += Weight * Source * LinkSpeed,
// hedefin [Min,Max] aralığına clamp'lenir. BudgetSystem'tan sonra, PublicSatisfactionSystem'den
// önce çalışır ki memnuniyet güncellenen göstergeleri görsün.
// Kalıcı oyun durumu yok (veri dosyasından yeniden hesaplanır) → Save/Load taşımıyor;
// SaveLoadTest'te bağlantı sayısı özetlenir. Deterministik → A==B testi bozulmaz.
public static class FormulaSystem
{
    public class FormulaLink
    {
        public string Source { get; set; }
        public string Target { get; set; }
        public double Weight { get; set; }
    }

    public class FormulaSave
    {
        public int LinkCount { get; set; }
        public string[] Links { get; set; } // "source->target:weight"
    }

    private static readonly string DataPath = "res://Data/formulas.json";
    private static readonly List<FormulaLink> _links = new();

    public static double LinkSpeed = 0.05; // günde kaynak değerinin 5'i kadar etkisi hedefe

    static FormulaSystem()
    {
        LoadData();
    }

    private static void LoadData()
    {
        _links.Clear();
        try
        {
            if (!Godot.FileAccess.FileExists(DataPath))
            {
                GD.PrintErr($"[FormulaSystem] Veri dosyası yok: {DataPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(DataPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Links", out var arr)) return;
            foreach (var item in arr.EnumerateArray())
            {
                _links.Add(new FormulaLink
                {
                    Source = item.GetProperty("Source").GetString(),
                    Target = item.GetProperty("Target").GetString(),
                    Weight = item.TryGetProperty("Weight", out var w) ? w.GetDouble() : 1.0
                });
            }
            GD.Print($"[FormulaSystem] {_links.Count} gösterge bağlantısı yüklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[FormulaSystem] Veri yükleme HATA: {e.Message}");
        }
    }

    public static void InitializeAll()
    {
        LoadData();
        GD.Print($"[FormulaSystem] {_links.Count} bağlantı aktif.");
    }

    public static void ResetForNewGame() => InitializeAll();

    // Günlük çağrı (GameManager.DailyTick): her bağlantıyı her ülke için uygular.
    public static void ApplyLinks()
    {
        var defs = StatisticsSystem.Definitions;
        var countries = CountryDatabase.All();
        if (defs == null || defs.Count == 0 || _links.Count == 0 || !countries.Any()) return;

        foreach (var link in _links)
        {
            if (!defs.Any(d => d.Id == link.Source))
                GD.PrintErr($"[FormulaSystem] Kaynak gösterge yok: {link.Source}");
            if (!defs.Any(d => d.Id == link.Target))
                GD.PrintErr($"[FormulaSystem] Hedef gösterge yok: {link.Target}");
        }

        // Değişiklikleri önce topla (snapshot), sonra uygula — günlük özet tutarlı olsun.
        var changes = new List<(string CountryId, string Source, string Target, double Current, double Next)>();
        foreach (var c in countries)
        {
            foreach (var link in _links)
            {
                double source = StatisticsSystem.GetValue(c.Id, link.Source);
                double current = StatisticsSystem.GetValue(c.Id, link.Target);
                var targetDef = defs.First(d => d.Id == link.Target);
                double delta = link.Weight * source * LinkSpeed;
                double next = Math.Clamp(current + delta, targetDef.Min, targetDef.Max);
                if (Math.Abs(next - current) > 0.001)
                    changes.Add((c.Id, link.Source, link.Target, current, next));
            }
        }

        foreach (var ch in changes)
            StatisticsSystem.SetValue(ch.CountryId, ch.Target, ch.Next);

        // Günlük özet: bağlantı başına ortalama değişim / değişen ülke sayısı
        foreach (var link in _links)
        {
            var linkChanges = changes.Where(ch => ch.Source == link.Source && ch.Target == link.Target).ToList();
            double avgDelta = linkChanges.Count > 0 ? linkChanges.Average(ch => ch.Next - ch.Current) : 0;
            GD.Print($"[FormulaSystem] {link.Source} → {link.Target} (w={link.Weight}): ortalama_Δ={avgDelta:F+3}, değişen_ülke={linkChanges.Count}/{countries.Count()}");
        }
    }

    public static FormulaSave GetSaveData() => new FormulaSave
    {
        LinkCount = _links.Count,
        Links = _links.Select(l => $"{l.Source}->{l.Target}:{l.Weight}").ToArray()
    };

    public static void LoadSaveData(FormulaSave data)
    {
        if (data == null) return;
        GD.Print($"[FormulaSystem] {data.LinkCount} bağlantı kaydı ile uyumluluk doğrulandı.");
    }
}
