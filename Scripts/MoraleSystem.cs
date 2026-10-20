using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Ordu morali sistemi (M39.3).
/// Her ordunun 0-100 arası moral değerini takip eder. Düşük morale saldırı/savunma
/// cezası uygular; savaş sonucu morale artar/azalır, günlük tick'te varsayılan değere (50)
/// doğru yavaş toparlanır. Statik sınıf; GameManager günlük tick'inde çağrılır.
/// </summary>
public static class MoraleSystem
{
    public const int DefaultMorale = 50;
    public const int MinMorale = 0;
    public const int MaxMorale = 100;

    // Morale → savaş gücü çarpanı: %20 ve altı %60, %50 ve üstü %100 (ara değerler lineer)
    private const int LowMoraleThreshold = 20;
    private const int FullMoraleThreshold = 50;
    private const float LowMoraleMultiplier = 0.60f;
    private const float DailyRecovery = 1f; // günde morale toparlanma (varsayılana doğru)

    private static bool _initialized;
    // orduId → morale (0-100); kaydı olmayan ordular DefaultMorale sayılır
    private static readonly Dictionary<string, int> _armyMorale = new();

    public static bool IsInitialized => _initialized;
    public static IReadOnlyDictionary<string, int> ArmyMorale => _armyMorale;

    public static void ResetForNewGame()
    {
        _armyMorale.Clear();
        _initialized = true;
        GD.Print("[MoraleSystem] Ordu morali sistemi sıfırlandı (yeni oyun)");
    }

    public static void Initialize(CountryData data)
    {
        if (!_initialized) ResetForNewGame();
        GD.Print($"MoraleSystem: {(data?.Id ?? "—")} için hazır — takip edilen ordu={_armyMorale.Count}");
    }

    public static int GetMorale(string armyId)
    {
        if (string.IsNullOrEmpty(armyId)) return DefaultMorale;
        return _armyMorale.TryGetValue(armyId, out var m) ? m : DefaultMorale;
    }

    public static void SetMorale(string armyId, int value)
    {
        if (string.IsNullOrEmpty(armyId)) return;
        _armyMorale[armyId] = Mathf.Clamp(value, MinMorale, MaxMorale);
    }

    public static void ModifyMorale(string armyId, int delta)
    {
        if (string.IsNullOrEmpty(armyId) || delta == 0) return;
        SetMorale(armyId, GetMorale(armyId) + delta);
    }

    /// <summary>Morale'a dayalı savaş gücü çarpanı (düşük morale = ceza, varsayılan = 1.00).</summary>
    public static float GetCombatMoraleMultiplier(string armyId)
    {
        int m = GetMorale(armyId);
        if (m <= LowMoraleThreshold) return LowMoraleMultiplier;
        if (m >= FullMoraleThreshold) return 1f;
        // 21..49 → 0.60 .. 1.00 lineer
        return Mathf.Clamp(LowMoraleMultiplier + (m - LowMoraleThreshold) * (1f - LowMoraleMultiplier) /
                                     (FullMoraleThreshold - LowMoraleThreshold), LowMoraleMultiplier, 1f);
    }

    public static void DailyTick()
    {
        if (!_initialized) return;

        var keys = _armyMorale.Keys.ToList();
        foreach (var armyId in keys)
        {
            int cur = _armyMorale[armyId];
            if (cur < DefaultMorale) _armyMorale[armyId] = Math.Min(DefaultMorale, cur + (int)DailyRecovery);
            else if (cur > DefaultMorale) _armyMorale[armyId] = Math.Max(DefaultMorale, cur - (int)DailyRecovery);
        }
    }

    /// <summary>Savaş günü sonrası morale güncelleme (kazanıyorsa artar, kaybediyorsa düşer).</summary>
    public static void ApplyCombatResult(string armyId, bool won, int intensityBonus = 0)
    {
        if (string.IsNullOrEmpty(armyId)) return;
        int delta = won ? 3 : -5;
        delta += intensityBonus;
        ModifyMorale(armyId, delta);
        GD.Print($"[Morale] Ordu {armyId} morale {(won ? "arttı" : "düştü")} (delta={delta}) → {GetMorale(armyId)}");
    }

    // ===== M12: Save/Load =====
    public static SaveSystem.MoraleSave GetSaveData()
    {
        return new SaveSystem.MoraleSave
        {
            States = _armyMorale.Select(kv => new SaveSystem.MoraleEntry { ArmyId = kv.Key, Morale = kv.Value }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.MoraleSave data)
    {
        if (data == null) return;
        _armyMorale.Clear();
        if (data.States != null)
        {
            foreach (var e in data.States)
            {
                if (!string.IsNullOrEmpty(e.ArmyId))
                    _armyMorale[e.ArmyId] = Mathf.Clamp(e.Morale, MinMorale, MaxMorale);
            }
        }
        _initialized = true;
        GD.Print($"MoraleSystem: state yüklendi — takip edilen ordu={_armyMorale.Count}");
    }
}

