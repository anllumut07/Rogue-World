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

    public class CabinetSave
    {
        public CountryCabinetSave[] Countries { get; set; }
    }
    public class CountryCabinetSave
    {
        public string CountryId { get; set; }
        public Dictionary<string, string> Assignments { get; set; } // ministryId -> ministerId
    }

    private static readonly string DataPath = "res://Data/cabinet_ministries.json";
    private static readonly List<Ministry> _ministries = new();

    // countryId -> ministryId -> ministerId
    private static readonly Dictionary<string, Dictionary<string, string>> _assignments = new();

    public static IReadOnlyList<Ministry> Ministries => _ministries;

    static CabinetSystem()
    {
        LoadData();
    }

    private static void LoadData()
    {
        _ministries.Clear();
        try
        {
            if (!Godot.FileAccess.FileExists(DataPath))
            {
                GD.PrintErr($"[CabinetSystem] Veri dosyası yok: {DataPath}");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(DataPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Ministries", out var arr)) return;
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
            GD.PrintErr($"[CabinetSystem] Veri yükleme HATA: {e.Message}");
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
