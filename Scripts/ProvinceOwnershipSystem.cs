using Godot;
using System;
using System.Collections.Generic;

// M18.3: İl sahipliği yönetimi. İl -> sahip ülke haritası; ilhak (savaş sonrası)
// el değişimini tetikler ve WorldMap'e doğru yeniden renklenmesi için signal fırlatır.
public static class ProvinceOwnershipSystem
{
    private static readonly Dictionary<string, string> _ownerByProvince = new();
    // M41.2: sahip → il id listesi ters indeks (O(1) sayaç, zafer koşulu değerlendirmesi için)
    private static readonly Dictionary<string, List<string>> _byOwner = new(StringComparer.OrdinalIgnoreCase);
    public static event Action<string, string, string> OnOwnerChanged; // provinceId, oldCountryId, newCountryId

    public static string GetProvinceOwner(string provinceId)
    {
        _ownerByProvince.TryGetValue(provinceId, out var owner);
        return owner ?? "?";
    }

    // M19.8: AI için — bir ülkenin sahip olduğu illerin id listesi (start-province seçimi).
    public static List<string> GetProvincesByOwner(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return new List<string>();
        return _byOwner.TryGetValue(countryId, out var list) ? list : new List<string>();
    }

    /// <summary>M41.2: Bir ülkenin il sayısı (O(1)).</summary>
    public static int ProvinceCountFor(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return 0;
        return _byOwner.TryGetValue(countryId, out var list) ? list.Count : 0;
    }

    /// <summary>M41.2: Dünyadaki toplam il sayısı (O(1)).</summary>
    public static int TotalProvinceCount => _ownerByProvince.Count;

    private static void Reindex(string provinceId, string oldCountryId, string newCountryId)
    {
        if (!string.IsNullOrEmpty(oldCountryId) && _byOwner.TryGetValue(oldCountryId, out var oldList))
            oldList.Remove(provinceId);
        if (!string.IsNullOrEmpty(newCountryId))
        {
            if (!_byOwner.TryGetValue(newCountryId, out var list))
            {
                list = new List<string>();
                _byOwner[newCountryId] = list;
            }
            list.Add(provinceId);
        }
    }

    public static void SetProvinceOwner(string provinceId, string countryId)
    {
        string old = GetProvinceOwner(provinceId);
        if (old == countryId) return;
        _ownerByProvince[provinceId] = countryId;
        Reindex(provinceId, old, countryId);
        OnOwnerChanged?.Invoke(provinceId, old, countryId);
        // M47.2: il devri ekonomiyi de taşır — veren ülkenin kaynağı/fabrikası
        // azalır, alanın artar. Tüm devir noktaları (M42 barış konferansı,
        // M44.3 kurgu kurma, M44.4 serbest bırakma, ilhak) buradan geçer.
        ProvinceEconomySystem.HandleOwnershipChange(provinceId, old, countryId);
        GD.Print($"[ProvinceOwnership] {provinceId}: {old} → {countryId}");
    }

    public static void InitializeFromProvinces(IReadOnlyList<ProvinceData> provinces)
    {
        _ownerByProvince.Clear();
        _byOwner.Clear();
        foreach (var p in provinces)
        {
            _ownerByProvince[p.Id] = p.CountryId;
            Reindex(p.Id, "", p.CountryId);
        }
    }

    // M18.5: Save/Load
    public static Dictionary<string, string> GetSaveData()
    {
        var copy = new Dictionary<string, string>();
        foreach (var kvp in _ownerByProvince) copy[kvp.Key] = kvp.Value;
        return copy;
    }

    public static void LoadSaveData(Dictionary<string, string> data)
    {
        _ownerByProvince.Clear();
        _byOwner.Clear();
        if (data != null)
            foreach (var kvp in data)
            {
                _ownerByProvince[kvp.Key] = kvp.Value;
                Reindex(kvp.Key, "", kvp.Value);
            }
    }
}
