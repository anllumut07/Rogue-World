using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Hava üstünlüğü sistemi (M39.4).
/// Her ordunun hava üstünlüğünü 0-100 arası takip eder. Hava üstünlüğü, ordunun
/// savaş gücüne çarpan bonus/ceza uygular (yüksek = bonus, düşük = ceza).
/// Statik sınıf; GameManager günlük tick'inde çağrılır.
/// </summary>
public static class AirSuperioritySystem
{
    public const int DefaultAirSuperiority = 50;
    public const int MinAirSuperiority = 0;
    public const int MaxAirSuperiority = 100;

    // Hava üstünlüğü → savaş gücü çarpanı: 0 ve altı %0.90, 100 ve üstü %1.10
    // (ara değerler lineer; 50 = %1.00 nötr)
    private const int LowAirSuperiorityThreshold = 0;
    private const int FullAirSuperiorityThreshold = 100;
    private const float LowAirSuperiorityMultiplier = 0.90f;
    private const float FullAirSuperiorityMultiplier = 1.10f;
    private const float DailyDrift = 1f; // günde varsayılana doğru drift

    private static bool _initialized;
    // armyId → hava üstünlüğü (0-100); kayıt olmayan ordular DefaultAirSuperiority sayılır
    private static readonly Dictionary<string, int> _armyAirSuperiority = new();

    public static bool IsInitialized => _initialized;
    public static IReadOnlyDictionary<string, int> ArmyAirSuperiority => _armyAirSuperiority;

    public static void ResetForNewGame()
    {
        _armyAirSuperiority.Clear();
        _initialized = true;
        GD.Print("[AirSuperioritySystem] Hava üstünlüğü sistemi sıfırlandı (yeni oyun)");
    }

    public static void Initialize(CountryData data)
    {
        if (!_initialized) ResetForNewGame();
        GD.Print($"[AirSuperioritySystem] Hava üstünlüğü sistemi hazır — takip edilen ordu={_armyAirSuperiority.Count}");
    }

    public static int GetAirSuperiority(string armyId)
    {
        if (string.IsNullOrEmpty(armyId)) return DefaultAirSuperiority;
        return _armyAirSuperiority.TryGetValue(armyId, out var a) ? a : DefaultAirSuperiority;
    }

    public static void SetAirSuperiority(string armyId, int value)
    {
        if (string.IsNullOrEmpty(armyId)) return;
        _armyAirSuperiority[armyId] = Mathf.Clamp(value, MinAirSuperiority, MaxAirSuperiority);
    }

    public static void ModifyAirSuperiority(string armyId, int delta)
    {
        if (string.IsNullOrEmpty(armyId) || delta == 0) return;
        SetAirSuperiority(armyId, GetAirSuperiority(armyId) + delta);
    }

    /// <summary>Hava üstünlüğüne dayalı savaş gücü çarpanı (varsayılan = 1.00).</summary>
    public static float GetAirSuperiorityMultiplier(string armyId)
    {
        int a = GetAirSuperiority(armyId);
        if (a <= LowAirSuperiorityThreshold) return LowAirSuperiorityMultiplier;
        if (a >= FullAirSuperiorityThreshold) return FullAirSuperiorityMultiplier;
        // 1..99 → 0.90 .. 1.10 lineer (50 = 1.00)
        return Mathf.Clamp(LowAirSuperiorityMultiplier + (a - LowAirSuperiorityThreshold) * (FullAirSuperiorityMultiplier - LowAirSuperiorityMultiplier) /
                                     (FullAirSuperiorityThreshold - LowAirSuperiorityThreshold), LowAirSuperiorityMultiplier, FullAirSuperiorityMultiplier);
    }

    public static void DailyTick()
    {
        if (!_initialized) return;

        var keys = _armyAirSuperiority.Keys.ToList();
        foreach (var armyId in keys)
        {
            int cur = _armyAirSuperiority[armyId];
            if (cur < DefaultAirSuperiority) _armyAirSuperiority[armyId] = Math.Min(DefaultAirSuperiority, cur + (int)DailyDrift);
            else if (cur > DefaultAirSuperiority) _armyAirSuperiority[armyId] = Math.Max(DefaultAirSuperiority, cur - (int)DailyDrift);
        }
    }

    // ===== M12: Save/Load =====
    public static SaveSystem.AirSuperioritySave GetSaveData()
    {
        return new SaveSystem.AirSuperioritySave
        {
            States = _armyAirSuperiority.Select(kv => new SaveSystem.AirSuperiorityEntry { ArmyId = kv.Key, AirSuperiority = kv.Value }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.AirSuperioritySave data)
    {
        if (data == null) return;
        _armyAirSuperiority.Clear();
        if (data.States != null)
        {
            foreach (var e in data.States)
            {
                if (!string.IsNullOrEmpty(e.ArmyId))
                    _armyAirSuperiority[e.ArmyId] = Mathf.Clamp(e.AirSuperiority, MinAirSuperiority, MaxAirSuperiority);
            }
        }
        _initialized = true;
        GD.Print($"[AirSuperioritySystem] state yüklendi — takip edilen ordu={_armyAirSuperiority.Count}");
    }
}
