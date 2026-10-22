using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Kurgu kurulabilir ülke verisi (M44.1).
///
/// Age of History tarzı "formable nation" kavramı: var olan bir ülkenin
/// topraklarında (veya birkaç ülkenin topraklarında) yeterli il kontrol
/// edildiğinde, yeterli siyasi güç ve gün harcanarak YENİ bir ülke kurulabilir.
///
/// Bu sınıf yalnızca veri katmanıdır: JSON'u okur ve kayıtları tutar. Kurma
/// kararını veren mantık <c>FormableSystem</c>'te (M44.3) yazılır; o yüzden
/// burada dünya durumu okunmaz, dolayısıyla save/load'a veri yazılmaz.
///
/// Ölçüt bir ülkenin sahip olduğu il sayısı + o illerdeki kültür dağılımı
/// (M44.2 <c>ProvinceCultureSystem</c>) ile karşılaştırılır; bu yüzden
/// <see cref="FormableDefinition.Culture"/> alanı kurucu kültürü tanımlar.
/// </summary>
public static class FormableDataSystem
{
    private const string DataPath = "res://Data/formable_nations.json";

    /// <summary>Bir kurgu ülkenin tanımı (JSON tek kaydı).</summary>
    public class FormableDefinition
    {
        public string Id = "";
        public string Name = "";
        public string Culture = "";
        public string Color = "#808080";
        public int RequiredProvinces = 1;
        public float MinimumControlPercent = 50f;
        public float PoliticalPowerCost = 50f;
        public float DaysRequired = 90f;
        public List<string> ParentCountryIds = new();
        public string Description = "";
    }

    private class Root
    {
        public int Version = 1;
        public List<FormableDefinition> Formables = new();
    }

    // Kurgu ülke id → tanım
    private static readonly Dictionary<string, FormableDefinition> _formables =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    /// <summary>Yüklenen kurgu ülke sayısı (log/yükleme doğrulaması için).</summary>
    public static int FormableCount
    {
        get { EnsureLoaded(); return _formables.Count; }
    }

    public static IReadOnlyCollection<FormableDefinition> All
    {
        get { EnsureLoaded(); return _formables.Values; }
    }

    public static FormableDefinition Get(string formableId)
    {
        EnsureLoaded();
        return formableId != null ? _formables.GetValueOrDefault(formableId) : null;
    }

    /// <summary>Belirli bir ülkenin topraklarından kurulabilecek kurgu ülkeler.</summary>
    public static List<FormableDefinition> ForParent(string countryId)
    {
        var result = new List<FormableDefinition>();
        if (string.IsNullOrEmpty(countryId)) return result;

        EnsureLoaded();

        foreach (var def in _formables.Values)
        {
            if (def.ParentCountryIds == null) continue;
            if (def.ParentCountryIds.Any(id =>
                    string.Equals(id, countryId, StringComparison.OrdinalIgnoreCase)))
                result.Add(def);
        }

        return result;
    }

    /// <summary>
    /// JSON'u bir kez okur. `IncludeFields=true` olmadan alanlar doldurulmaz —
    /// deniz bölgesi yükleyicisiyle (M43.1) aynı desen.
    /// </summary>
    public static void LoadData()
    {
        _formables.Clear();

        if (!Godot.FileAccess.FileExists(DataPath))
        {
            GD.PrintErr($"[Formable] HATA: {DataPath} bulunamadı");
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

            if (root?.Formables == null)
            {
                GD.PrintErr($"[Formable] HATA: {DataPath} okundu ama kurgu ülke listesi boş");
                _loaded = true;
                return;
            }

            foreach (var f in root.Formables)
            {
                if (f == null || string.IsNullOrEmpty(f.Id)) continue;
                f.ParentCountryIds ??= new List<string>();
                _formables[f.Id] = f;
            }

            GD.Print($"[Formable] Yüklendi: {_formables.Count} kurgu ülke tanımı ({DataPath})");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Formable] HATA: {DataPath} çözümlenemedi — {e.Message}");
        }

        _loaded = true;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        LoadData();
    }
}
