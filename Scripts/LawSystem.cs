using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// M22.2: Yasalar — önerme, çıkarma, kaldırma; kategori, PP maliyeti, etkiler (Data/laws.json).
// Kurgusal içerik, gerçek kişi adı kullanılmaz.
public static class LawSystem
{
    public class LawData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public int CostPP { get; set; }
        public string Description { get; set; } = "";
        public Dictionary<string, double> Effects { get; set; } = new(); // indicatorId -> delta
        public bool OneTime { get; set; }
    }

    public class LawSave
    {
        public CountryLawSave[] Countries { get; set; } = Array.Empty<CountryLawSave>();
    }

    public class CountryLawSave
    {
        public string CountryId { get; set; } = "";
        public string[] ActiveLawIds { get; set; } = Array.Empty<string>();
        public LawEffectSave[] Effects { get; set; } = Array.Empty<LawEffectSave>();
    }

    public class LawEffectSave
    {
        public string IndicatorId { get; set; } = "";
        public double Delta { get; set; }
    }

    private const string Path = "res://Data/laws.json";

    private static readonly Dictionary<string, LawData> _laws = new();

    // countryId -> lawId (etkili yasalar)
    private static readonly Dictionary<string, HashSet<string>> _activeLaws = new();
    // countryId -> indicatorId -> accumulated delta (tüm aktif yasaların etkileri)
    private static readonly Dictionary<string, Dictionary<string, double>> _lawEffects = new();

    public static IReadOnlyDictionary<string, LawData> Laws => _laws;

    public static void LoadData()
    {
        _laws.Clear();
        try
        {
            if (!Godot.FileAccess.FileExists(Path))
            {
                GD.PrintErr($"[LawSystem] Veri dosyası yok: {Path}");
                GD.Print($"LawSystem hazır: 0 yasa tanımı yüklendi.");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(Path);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Laws", out var arr))
            {
                GD.PrintErr("[LawSystem] 'Laws' dizisi bulunamadı.");
                GD.Print($"LawSystem hazır: 0 yasa tanımı yüklendi.");
                return;
            }
            int count = 0;
            foreach (var item in arr.EnumerateArray())
            {
                var l = new LawData
                {
                    Id = item.TryGetProperty("Id", out var id) ? id.GetString() : "",
                    Name = item.TryGetProperty("Name", out var n) ? n.GetString() : "",
                    Category = item.TryGetProperty("Category", out var cat) ? cat.GetString() : "General",
                    CostPP = item.TryGetProperty("CostPP", out var cp) ? cp.GetInt32() : 0,
                    Description = item.TryGetProperty("Description", out var d) ? d.GetString() : "",
                    OneTime = item.TryGetProperty("OneTime", out var ot) && ot.GetBoolean()
                };
                if (item.TryGetProperty("Effects", out var eff))
                    foreach (var e in eff.EnumerateObject())
                        l.Effects[e.Name] = e.Value.GetDouble();

                if (!string.IsNullOrEmpty(l.Id))
                {
                    _laws[l.Id] = l;
                    count++;
                }
            }
            GD.Print($"LawSystem hazır: {count} yasa tanımı yüklendi (laws.json).");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[LawSystem] Yükleme hatası: {e.Message}");
        }
    }

    public static void ResetForNewGame()
    {
        _activeLaws.Clear();
        _lawEffects.Clear();
    }

    public static LawData GetLaw(string lawId)
    {
        return _laws.TryGetValue(lawId, out var l) ? l : null;
    }

    public static IReadOnlyList<LawData> GetAllLaws() => _laws.Values.ToList().AsReadOnly();

    public static IReadOnlyList<LawData> GetActiveLaws(string countryId)
    {
        if (!_activeLaws.TryGetValue(countryId, out var ids)) return Array.Empty<LawData>();
        return ids.Where(id => _laws.TryGetValue(id, out _))
            .Select(id => _laws[id])
            .ToList().AsReadOnly();
    }

    private static LawData _lazyLaw(LawData l) => l;

    public static IReadOnlyList<LawData> GetAvailableLaws(string countryId)
    {
        var active = _activeLaws.TryGetValue(countryId, out var a) ? a : new HashSet<string>();
        double pp = GetPP(countryId);
        return _laws.Values
            .Where(l => !active.Contains(l.Id) && pp >= l.CostPP)
            .OrderBy(l => l.CostPP)
            .ToList()
            .AsReadOnly();
    }

    public static double GetLawEffect(string countryId, string indicatorId)
    {
        if (!_lawEffects.TryGetValue(countryId, out var eff)) return 0;
        return eff.TryGetValue(indicatorId, out var d) ? d : 0;
    }

    private static double GetPP(string countryId)
    {
        var c = CountryDatabase.Get(countryId); // HOI4 alias (GER) → ISO (DEU) normalize edilir
        return c != null ? c.PoliticalPower : 0;
    }

    // Yasa çıkar: PP maliyetini öder, etkileri uygular.
    public static bool EnactLaw(string countryId, string lawId)
    {
        if (string.IsNullOrEmpty(countryId) || string.IsNullOrEmpty(lawId)) return false;
        var law = GetLaw(lawId);
        if (law == null) return false;

        if (!_activeLaws.TryGetValue(countryId, out var active)) { active = new HashSet<string>(); _activeLaws[countryId] = active; }
        if (active.Contains(lawId)) return false; // zaten etkin

        var c = CountryDatabase.Get(countryId); // HOI4 alias (GER) → ISO (DEU) normalize edilir
        if (c == null || c.PoliticalPower < law.CostPP) return false;

        c.PoliticalPower -= law.CostPP;
        active.Add(lawId);

        if (!_lawEffects.TryGetValue(countryId, out var eff)) { eff = new Dictionary<string, double>(); _lawEffects[countryId] = eff; }
        foreach (var kv in law.Effects)
            eff[kv.Key] = eff.TryGetValue(kv.Key, out var cur) ? cur + kv.Value : kv.Value;

        GD.Print($"[Law] {countryId} yasa çıkardı: {law.Name} ({law.Category}, {law.CostPP} PP)");
        return true;
    }

    // Yasa kaldır: etkileri geri alır.
    public static bool RepealLaw(string countryId, string lawId)
    {
        if (string.IsNullOrEmpty(countryId) || string.IsNullOrEmpty(lawId)) return false;
        var law = GetLaw(lawId);
        if (law == null) return false;

        if (!_activeLaws.TryGetValue(countryId, out var active) || !active.Contains(lawId))
            return false;

        active.Remove(lawId);
        if (_lawEffects.TryGetValue(countryId, out var eff))
        {
            foreach (var kv in law.Effects)
                if (eff.TryGetValue(kv.Key, out var cur))
                    eff[kv.Key] = cur - kv.Value;
        }

        GD.Print($"[Law] {countryId} yasa kaldırdı: {law.Name}");
        return true;
    }

    // --- Save / Load ---
    public static LawSave GetSaveData()
    {
        var save = new LawSave
        {
            Countries = _activeLaws.Select(kv => new CountryLawSave
            {
                CountryId = kv.Key,
                ActiveLawIds = kv.Value.ToArray(),
                Effects = _lawEffects.TryGetValue(kv.Key, out var eff)
                    ? eff.Select(e => new LawEffectSave { IndicatorId = e.Key, Delta = e.Value }).ToArray()
                    : Array.Empty<LawEffectSave>()
            }).ToArray()
        };
        return save;
    }

    public static void LoadSaveData(LawSave save)
    {
        _activeLaws.Clear();
        _lawEffects.Clear();
        if (save?.Countries == null) return;
        foreach (var c in save.Countries)
        {
            if (string.IsNullOrEmpty(c.CountryId)) continue;
            var ids = new HashSet<string>(c.ActiveLawIds ?? Array.Empty<string>());
            _activeLaws[c.CountryId] = ids;
            var eff = new Dictionary<string, double>();
            if (c.Effects != null)
                foreach (var e in c.Effects)
                    eff[e.IndicatorId] = e.Delta;
            _lawEffects[c.CountryId] = eff;
        }
    }
}
