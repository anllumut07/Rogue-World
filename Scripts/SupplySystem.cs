using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// İkmal (supply) sistemi (M39.1): ordular için günlük ikmal seviyesi takibi.
/// Dost toprakta ikmal regeneration, düşman/topraksız yerde azalır. Düşük ikmal
/// savaş gücünü cezalandırır (WarSystem entegrasyonu). Statik sınıf;
/// GameManager günlük tick'inde çağrılır.
/// </summary>
public static class SupplySystem
{
    public class ArmySupplyState
    {
        public string ArmyId = "";
        public string CountryId = "";
        public string ProvinceId = "";
        public float SupplyLevel = 100f;   // 0-100
        public float SupplyUse = 0f;        // ordunun toplam günlük tüketimi
        public int DaysOutOfSupply = 0;
    }

    private const float MaxSupply = 100f;
    private const float FriendlyRegenPerDay = 8f;   // dost toprakta günde +8
    private const float HostileDrainPerDay = 12f;   // düşman/topraksız yerde günde -12
    private const float OutOfSupplyThreshold = 20f; // altı bu = "ikmalsiz"
    private const float MinCombatMultiplier = 0.6f; // ikminsizde min %60 gücü kalır

    private static bool _initialized;
    private static readonly Dictionary<string, ArmySupplyState> _states = new();

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    public static bool IsInitialized => _initialized;

    public static void ResetForNewGame()
    {
        _states.Clear();
        _initialized = false;
    }

    public static void Initialize(CountryData data)
    {
        _states.Clear();
        _initialized = data != null;
        if (data == null) return;

        foreach (var army in ArmySystem.Armies)
            RegisterArmy(army, initialLevel: 100f);

        GD.Print($"SupplySystem: {data.Id} için hazır — ordular={_states.Count}");
    }

    // Yeni/yeniden oluşturulan ordular için state garanti et (DailyTick'te çağrılır)
    public static void EnsureArmyState(ArmySystem.Army army)
    {
        if (!_states.ContainsKey(army.Id))
            RegisterArmy(army, initialLevel: 100f);
    }

    private static void RegisterArmy(ArmySystem.Army army, float initialLevel)
    {
        _states[army.Id] = new ArmySupplyState
        {
            ArmyId = army.Id,
            CountryId = army.CountryId,
            ProvinceId = army.ProvinceId,
            SupplyLevel = initialLevel,
            SupplyUse = ArmySupplyUse(army),
            DaysOutOfSupply = 0
        };
    }

    // Ordunun total ikmal tüketimini (tümen şablonlarından) hesapla
    private static float ArmySupplyUse(ArmySystem.Army army)
    {
        float total = 0f;
        foreach (var divId in army.DivisionIds)
        {
            var div = ArmySystem.GetDivision(divId);
            var tpl = ArmySystem.GetTemplate(div?.TemplateId);
            if (tpl != null) total += tpl.SupplyUse;
        }
        return total;
    }

    public static void DailyTick()
    {
        if (!_initialized || Player == null) return;

        // Yeni oluşturulan ordular için state ekle
        foreach (var army in ArmySystem.Armies)
            EnsureArmyState(army);

        foreach (var kv in _states.ToList())
        {
            var state = kv.Value;
            var army = ArmySystem.Armies.FirstOrDefault(a => a.Id == state.ArmyId);
            if (army == null)
            {
                // Ordunun silinmesi → state'i de temizle
                _states.Remove(state.ArmyId);
                continue;
            }

            // Province sahibi ile ordunun ülkesini karşılaştır
            string owner = ProvinceOwnershipSystem.GetProvinceOwner(state.ProvinceId);
            bool friendly = owner == state.CountryId;

            float delta = friendly ? FriendlyRegenPerDay : -HostileDrainPerDay;
            state.SupplyLevel = Math.Clamp(state.SupplyLevel + delta, 0f, MaxSupply);
            state.DaysOutOfSupply = state.SupplyLevel < OutOfSupplyThreshold
                ? state.DaysOutOfSupply + 1 : 0;

            state.ProvinceId = army.ProvinceId;
            state.CountryId = army.CountryId;
            state.SupplyUse = ArmySupplyUse(army);
        }
    }

    // 0-100 arası ikmal seviyesi (UI için)
    public static float GetSupplyLevel(string armyId) =>
        _states.TryGetValue(armyId, out var s) ? s.SupplyLevel : 100f;

    // Ikmal cezalı çarpanı: 0.6 (ikminsiz) … 1.0 (tam ikmal)
    public static float GetCombatMultiplier(string armyId)
    {
        if (!_states.TryGetValue(armyId, out var s)) return 1f;
        return Math.Clamp(s.SupplyLevel / 100f, MinCombatMultiplier, 1f);
    }

    public static bool IsOutOfSupply(string armyId) =>
        _states.TryGetValue(armyId, out var s) && s.SupplyLevel < OutOfSupplyThreshold;

    // ===== M39.5 Save/Load =====

    public static SaveSystem.SupplySave GetSaveData()
    {
        return new SaveSystem.SupplySave
        {
            States = _states.Values.Select(s => new SaveSystem.SupplyEntry
            {
                ArmyId = s.ArmyId,
                CountryId = s.CountryId,
                ProvinceId = s.ProvinceId,
                SupplyLevel = s.SupplyLevel,
                DaysOutOfSupply = s.DaysOutOfSupply
            }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.SupplySave data)
    {
        if (data == null) return;
        _states.Clear();
        if (data.States != null)
            foreach (var s in data.States)
                _states[s.ArmyId] = new ArmySupplyState
                {
                    ArmyId = s.ArmyId,
                    CountryId = s.CountryId,
                    ProvinceId = s.ProvinceId,
                    SupplyLevel = s.SupplyLevel,
                    DaysOutOfSupply = s.DaysOutOfSupply,
                    SupplyUse = 0 // DailyTick'te yeniden hesaplanır
                };
        _initialized = true;
        GD.Print($"SupplySystem: state yüklendi — Ordular={_states.Count}");
    }
}
