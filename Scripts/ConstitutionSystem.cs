using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// M22.1: Anayasa — yönetim biçimi, dönem süresi/limit, seçim yaşı, olağanüstü hal kuralları.
// Veri: Data/constitution.json. Gerçek kişi adı kullanılmaz (kurgusal sistem).
public static class ConstitutionSystem
{
    public enum GovernmentType
    {
        Presidential,
        Parliamentary,
        SemiPresidential,
        Monarchy,
        OneParty
    }

    public class ConstitutionData
    {
        public string CountryId { get; set; } = "";
        public GovernmentType GovernmentType { get; set; } = GovernmentType.Parliamentary;
        public int TermLength { get; set; } = 4; // yıl
        public int TermLimit { get; set; } = 2;  // 0 = sınırsız
        public int VotingAge { get; set; } = 18;
    }

    public class ConstitutionSave
    {
        public CountryConstitutionSave[] Countries { get; set; } = Array.Empty<CountryConstitutionSave>();
    }

    public class CountryConstitutionSave
    {
        public string CountryId { get; set; } = "";
        public bool EmergencyDeclared { get; set; }
        public long EmergencyStartDay { get; set; }
    }

    private const string Path = "res://Data/constitution.json";

    private static readonly Dictionary<string, ConstitutionData> _constitutions = new();

    // countryId -> emergency state (olağanüstü hal, per-country)
    private static readonly Dictionary<string, (bool Declared, long StartDay)> _emergency = new();

    public static IReadOnlyDictionary<string, ConstitutionData> Constitutions => _constitutions;

    public static void LoadData()
    {
        _constitutions.Clear();
        try
        {
            if (!Godot.FileAccess.FileExists(Path))
            {
                GD.PrintErr($"[ConstitutionSystem] Veri dosyası yok: {Path}");
                GD.Print($"ConstitutionSystem hazır: 0 ülke tanımı yüklendi.");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(Path);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Constitutions", out var arr))
            {
                GD.PrintErr("[ConstitutionSystem] 'Constitutions' dizisi bulunamadı.");
                GD.Print($"ConstitutionSystem hazır: 0 ülke tanımı yüklendi.");
                return;
            }
            int count = 0;
            foreach (var item in arr.EnumerateArray())
            {
                var c = new ConstitutionData
                {
                    CountryId = item.TryGetProperty("CountryId", out var id) ? id.GetString() : "",
                    GovernmentType = item.TryGetProperty("GovernmentType", out var gt) ? ParseGovType(gt.GetString()) : GovernmentType.Parliamentary,
                    TermLength = item.TryGetProperty("TermLength", out var tl) ? tl.GetInt32() : 4,
                    TermLimit = item.TryGetProperty("TermLimit", out var tml) ? tml.GetInt32() : 2,
                    VotingAge = item.TryGetProperty("VotingAge", out var va) ? va.GetInt32() : 18
                };
                if (!string.IsNullOrEmpty(c.CountryId))
                {
                    _constitutions[c.CountryId] = c;
                    count++;
                }
            }
            GD.Print($"ConstitutionSystem hazır: {count} ülke tanımı yüklendi (constitution.json).");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ConstitutionSystem] Yükleme hatası: {e.Message}");
        }
    }

    public static void ResetForNewGame()
    {
        _emergency.Clear();
    }

    public static ConstitutionData GetConstitution(string countryId)
    {
        if (_constitutions.TryGetValue(countryId, out var c))
            return c;
        // Varsayılan: parlamentosal, 4 yıl, 2 dönem, 18 yaş
        return new ConstitutionData { CountryId = countryId, GovernmentType = GovernmentType.Parliamentary, TermLength = 4, TermLimit = 2, VotingAge = 18 };
    }

    // Olağanüstü hal ilan et / kaldır
    public static void SetEmergency(string countryId, bool declared, long currentDay)
    {
        if (string.IsNullOrEmpty(countryId)) return;
        if (declared)
            _emergency[countryId] = (true, currentDay);
        else
            _emergency.Remove(countryId);
    }

    public static bool IsEmergencyDeclared(string countryId)
    {
        return _emergency.TryGetValue(countryId, out var e) && e.Declared;
    }

    public static long EmergencyStartDay(string countryId)
    {
        return _emergency.TryGetValue(countryId, out var e) ? e.StartDay : 0;
    }

    // --- Save / Load ---
    public static ConstitutionSave GetSaveData()
    {
        var save = new ConstitutionSave
        {
            Countries = _emergency.Select(kv => new CountryConstitutionSave
            {
                CountryId = kv.Key,
                EmergencyDeclared = kv.Value.Declared,
                EmergencyStartDay = kv.Value.StartDay
            }).ToArray()
        };
        return save;
    }

    public static void LoadSaveData(ConstitutionSave save)
    {
        _emergency.Clear();
        if (save?.Countries == null) return;
        foreach (var c in save.Countries)
            if (!string.IsNullOrEmpty(c.CountryId))
                _emergency[c.CountryId] = (c.EmergencyDeclared, c.EmergencyStartDay);
    }

    private static GovernmentType ParseGovType(string s)
    {
        return s switch
        {
            "Presidential" => GovernmentType.Presidential,
            "Parliamentary" => GovernmentType.Parliamentary,
            "SemiPresidential" => GovernmentType.SemiPresidential,
            "Monarchy" => GovernmentType.Monarchy,
            "OneParty" => GovernmentType.OneParty,
            _ => GovernmentType.Parliamentary
        };
    }
}
