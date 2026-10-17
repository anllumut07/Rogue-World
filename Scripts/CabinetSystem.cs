using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Kabine (bakanlık) sistemi (M21).
// - Data/cabinet_ministries.json'dan bakanlık tanımları yüklenir.
// - Her ülke için bakanlık Id -> atanan bakan Id eşleştirmesi tutulur (boş = atanmamış).
// - M21.2: kurgusal bakan havuzu; M21.3: atama/görevden alma + gösterge etkileri.
// - Save/Load: SaveSystem.SaveFile.Cabinet. Deterministik (yeni RNG yok).
public static class CabinetSystem
{
    public class Ministry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class Minister
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CountryId { get; set; }
        public string[] MinistryIds { get; set; } // yetkili olduğu bakanlıklar
        public int Skill { get; set; }      // yetenek 0-100
        public int Honesty { get; set; }    // dürüstlük 0-100
        public int Loyalty { get; set; }    // sadakat 0-100
        public int Popularity { get; set; } // popülerlik 0-100
        public string Party { get; set; }
        public string Ideology { get; set; }
        public List<string> Traits { get; set; } // reformcu/şahin/teknokrat...
    }

    public class CabinetSave
    {
        public CountryCabinetSave[] Countries { get; set; }
    }
    public class CountryCabinetSave
    {
        public string CountryId { get; set; }
        public Dictionary<string, string> Assignments { get; set; } // ministryId -> ministerId
    }

    private static readonly string MinistriesPath = "res://Data/cabinet_ministries.json";
    private static readonly string MinistersPath = "res://Data/cabinet_ministers.json";
    private static readonly List<Ministry> _ministries = new();
    private static readonly List<Minister> _ministers = new();

    // countryId -> ministryId -> ministerId
    private static readonly Dictionary<string, Dictionary<string, string>> _assignments = new();

    public static IReadOnlyList<Ministry> Ministries => _ministries;
    public static IReadOnlyList<Minister> Ministers => _ministers;

    static CabinetSystem()
    {
        LoadData();
    }

    private static void LoadData()
    {
        _ministries.Clear();
        _ministers.Clear();
        try
        {
            if (!Godot.FileAccess.FileExists(MinistriesPath))
            {
                GD.PrintErr($"[CabinetSystem] Veri dosyası yok: {MinistriesPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(MinistriesPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Ministries", out var arr))
                foreach (var item in arr.EnumerateArray())
                    _ministries.Add(new Ministry
                    {
                        Id = item.GetProperty("Id").GetString(),
                        Name = item.TryGetProperty("Name", out var n) ? n.GetString() : item.GetProperty("Id").GetString(),
                        Description = item.TryGetProperty("Description", out var d) ? d.GetString() : ""
                    });
            GD.Print($"[CabinetSystem] {_ministries.Count} bakanlık tanımı yüklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[CabinetSystem] Bakanlık veri yükleme HATA: {e.Message}");
        }

        LoadMinisters();
    }

    private static void LoadMinisters()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(MinistersPath))
            {
                GD.PrintErr($"[CabinetSystem] Bakan havuzu dosyası yok: {MinistersPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(MinistersPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Ministers", out var arr)) return;
            foreach (var item in arr.EnumerateArray())
            {
                var m = new Minister
                {
                    Id = item.GetProperty("Id").GetString(),
                    Name = item.TryGetProperty("Name", out var n) ? n.GetString() : item.GetProperty("Id").GetString(),
                    CountryId = item.TryGetProperty("CountryId", out var c) ? c.GetString() : "",
                    Skill = item.TryGetProperty("Skill", out var s) ? s.GetInt32() : 50,
                    Honesty = item.TryGetProperty("Honesty", out var h) ? h.GetInt32() : 50,
                    Loyalty = item.TryGetProperty("Loyalty", out var l) ? l.GetInt32() : 50,
                    Popularity = item.TryGetProperty("Popularity", out var p) ? p.GetInt32() : 50,
                    Party = item.TryGetProperty("Party", out var pt) ? pt.GetString() : "",
                    Ideology = item.TryGetProperty("Ideology", out var id) ? id.GetString() : "",
                    Traits = new List<string>()
                };
                if (item.TryGetProperty("MinistryIds", out var midArr))
                {
                    var ministryIds = new List<string>();
                    foreach (var mid in midArr.EnumerateArray())
                        ministryIds.Add(mid.GetString());
                    m.MinistryIds = ministryIds.ToArray();
                }
                if (item.TryGetProperty("Traits", out var tArr))
                    foreach (var t in tArr.EnumerateArray())
                        m.Traits.Add(t.GetString());
                _ministers.Add(m);
            }
            GD.Print($"[CabinetSystem] {_ministers.Count} kurgusal bakan havuzu yüklendi.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[CabinetSystem] Bakan havuzu yükleme HATA: {e.Message}");
        }
    }

    public static void InitializeAll()
    {
        LoadData();
        _assignments.Clear();
        foreach (var c in CountryDatabase.All())
            _assignments[c.Id] = new Dictionary<string, string>();
        GD.Print($"[CabinetSystem] {_assignments.Count} ülke kabinesi başlatıldı (bakan atanmamış).");
    }

    public static void ResetForNewGame() => InitializeAll();

    public static string GetMinister(string countryId, string ministryId)
    {
        if (!_assignments.TryGetValue(countryId, out var a)) return null;
        return a.TryGetValue(ministryId, out var m) ? m : null;
    }

    public static bool IsAssigned(string countryId, string ministryId) => GetMinister(countryId, ministryId) != null;

    public static void AssignMinister(string countryId, string ministryId, string ministerId)
    {
        if (!_assignments.TryGetValue(countryId, out var a))
        {
            a = new Dictionary<string, string>();
            _assignments[countryId] = a;
        }
        if (string.IsNullOrEmpty(ministerId))
        {
            a.Remove(ministryId);
            return;
        }
        a[ministryId] = ministerId;
    }

    public static int AssignmentCount()
    {
        return _assignments.Sum(kv => kv.Value.Count);
    }

    // Tüm bakanlar (belirli bir ülkeye ait)
    public static IReadOnlyList<Minister> GetMinisters(string countryId)
    {
        return _ministers.Where(m => m.CountryId == countryId).ToList().AsReadOnly();
    }

    // Belirli bir bakanlık için uygun, henüz atanmamış bakan adayları (countryId + ministryId'ye göre)
    public static IReadOnlyList<Minister> GetAvailableMinisters(string countryId, string ministryId)
    {
        var assignedIds = new HashSet<string>();
        if (_assignments.TryGetValue(countryId, out var a))
            foreach (var mv in a.Values)
                if (!string.IsNullOrEmpty(mv)) assignedIds.Add(mv);

        return _ministers
            .Where(m => m.CountryId == countryId
                     && (m.MinistryIds ?? Array.Empty<string>()).Contains(ministryId)
                     && !assignedIds.Contains(m.Id))
            .OrderByDescending(m => m.Skill)
            .ToList()
            .AsReadOnly();
    }

    public static CabinetSave GetSaveData() => new CabinetSave
    {
        Countries = _assignments.Select(kv => new CountryCabinetSave
        {
            CountryId = kv.Key,
            Assignments = kv.Value.ToDictionary(p => p.Key, p => p.Value)
        }).ToArray()
    };

    public static void LoadSaveData(CabinetSave data)
    {
        if (data == null) return;
        _assignments.Clear();
        if (data.Countries != null)
        {
            foreach (var c in data.Countries)
            {
                if (string.IsNullOrEmpty(c.CountryId)) continue;
                _assignments[c.CountryId] = c.Assignments?.ToDictionary(p => p.Key, p => p.Value)
                    ?? new Dictionary<string, string>();
            }
        }
        GD.Print($"[CabinetSystem] {_assignments.Count} ülke kabinesi yüklendi.");
    }
}
