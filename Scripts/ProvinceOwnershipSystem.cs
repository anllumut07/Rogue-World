using Godot;
using System;
using System.Collections.Generic;

// M18.3: İl sahipliği yönetimi. İl -> sahip ülke haritası; ilhak (savaş sonrası)
// el değişimini tetikler ve WorldMap'e doğru yeniden renklenmesi için signal fırlatır.
public static class ProvinceOwnershipSystem
{
    private static readonly Dictionary<string, string> _ownerByProvince = new();
    public static event Action<string, string, string> OnOwnerChanged; // provinceId, oldCountryId, newCountryId

    public static string GetProvinceOwner(string provinceId)
    {
        _ownerByProvince.TryGetValue(provinceId, out var owner);
        return owner ?? "?";
    }

    public static void SetProvinceOwner(string provinceId, string countryId)
    {
        string old = GetProvinceOwner(provinceId);
        if (old == countryId) return;
        _ownerByProvince[provinceId] = countryId;
        OnOwnerChanged?.Invoke(provinceId, old, countryId);
        GD.Print($"[ProvinceOwnership] {provinceId}: {old} → {countryId}");
    }

    public static void InitializeFromProvinces(IReadOnlyList<ProvinceData> provinces)
    {
        _ownerByProvince.Clear();
        foreach (var p in provinces)
            _ownerByProvince[p.Id] = p.CountryId;
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
        if (data != null)
            foreach (var kvp in data) _ownerByProvince[kvp.Key] = kvp.Value;
    }
}
