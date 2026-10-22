using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// M45.2 — Bağımlı ülkelerde özerklik (autonomy).
///
/// İlhak edilen ya da kukla yapılan bir ülke durmak durumu bilmez: ya overlord
/// ona özerklik verir, ya halkının istemediği yabancı yönetim altında baskı
/// birikir. Bu sistem o baskıyı 0-100 arası bir seviyeye çevirir.
///
/// Seviye neyi değiştirir?
///   • Çıkarım: özerk bağımlıdan alınan kaynak azalır
///     (<see cref="ResourceExtractionFactor"/> — `EconomySystem` üretiminde okunur).
///   • Stabilite: özerk bağımlılar overlord'un stabilitesini her gün biraz daha
///     eroder (<see cref="StabilityPressure"/> — GameManager günlük tick'i yazar).
///
/// Günlük kayma deterministiktir (RNG yok): baskı yalnızca
///   (a) kayıtlı illerin ne kadarı overlord'un baskın kültüründen farklı,
///   (b) overlord'un stabilitesi ne kadar düşük
/// ile hesaplanır. Aynı dünya + aynı gün sayısı → aynı seviye.
///
/// Kaynak: `Data/autonomy.json`. Bağımlılık kaydı `ReleasableSystem`'te (M44.4);
/// bu sistem yalnızca seviyeyi ve etkilerini tutar.
/// </summary>
public static class AutonomySystem
{
    private const string DataPath = "res://Data/autonomy.json";

    // ===== Ayarlar (Data/autonomy.json — yoğunlaştırılmış sabitler) =====
    private sealed class AutonomySettings
    {
        public double AutonomyStep = 10.0;
        public int ChangeCooldownDays = 7;
        public double AnnexStartLevel = 0.0;
        public double PuppetStartLevel = 30.0;
        public double DriftBasePerDay = 0.05;
        public double CultureDriftWeight = 0.25;
        public double WeaknessDriftWeight = 0.2;
        public double MaxDailyDrift = 0.6;
        public double MaxExtractionLoss = 0.5;
        public double MaxStabilityPressurePerDay = 0.15;
    }

    private sealed class AutonomyRoot
    {
        public int Version { get; set; } = 1;
        public AutonomySettings Settings { get; set; } = new();
    }

    private static AutonomySettings S = new();
    private static bool _loaded;

    /// <summary>Bir bağımlının özerklik durumu.</summary>
    public class AutonomyState
    {
        public string CountryId = "";
        public string OverlordId = "";
        public double Level = 0;             // 0-100
        public long LastChangeDay = -999;    // son ver/geri al günü
        public int GrantCount = 0;
        public int RevokeCount = 0;
        public double LastDailyDrift = 0;    // son günkü kayma (UI/log için)
    }

    private static readonly Dictionary<string, AutonomyState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            using var file = Godot.FileAccess.Open(DataPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PrintErr("[Autonomy] autonomy.json açılamadı — varsayılan ayarlar kullanılıyor.");
                return;
            }
            var text = file.GetAsText();
            var root = JsonSerializer.Deserialize<AutonomyRoot>(text, new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true
            });
            if (root?.Settings != null)
            {
                S = root.Settings;
                GD.Print($"[Autonomy] Ayarlar yüklendi: adım={S.AutonomyStep}, " +
                         $"bekleme={S.ChangeCooldownDays}gün, maks_kayma={S.MaxDailyDrift}/gün, " +
                         $"maks_çıkarım_kaybı=%{S.MaxExtractionLoss * 100:N0}, " +
                         $"maks_stabilite_baskısı={S.MaxStabilityPressurePerDay}/gün");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Autonomy] autonomy.json okunamadı ({e.Message}) — varsayılan ayarlar.");
        }
    }

    /// <summary>Kayıtlı özerklik durumu sayısı (log/test).</summary>
    public static int StateCount => _states.Count;

    /// <summary>Yeni oyunda sıfırlar.</summary>
    public static void ResetForNewGame()
    {
        _states.Clear();
        GD.Print("[Autonomy] Yeni oyun için sıfırlandı.");
    }

    private static AutonomyState EnsureState(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return null;
        if (!_states.TryGetValue(countryId, out var s))
        {
            s = new AutonomyState
            {
                CountryId = countryId,
                OverlordId = ReleasableSystem.Get(countryId)?.OverlordId ?? "",
                Level = ReleasableSystem.Get(countryId)?.IsPuppet == true
                    ? S.PuppetStartLevel
                    : S.AnnexStartLevel
            };
            _states[countryId] = s;
        }
        return s;
    }

    /// <summary>
    /// Bir bağımlının özerklik durumu. Hâlâ bağımlı olan bir ülke için durum
    /// yoksa o anda ilklendirilir (başlangıç seviyesi: ilhak 0, kukla 30) —
    /// çağıran, tick beklemeden de doğru seviyeyi görür.
    /// </summary>
    public static AutonomyState Get(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return null;
        if (_states.TryGetValue(countryId, out var existing)) return existing;

        // Kaydı olmayan ülkeye boşuna durum açma.
        if (ReleasableSystem.Get(countryId) == null) return null;
        return EnsureState(countryId);
    }

    /// <summary>Özerklik seviyesi (kayıt yoksa -1).</summary>
    public static double Level(string countryId) => Get(countryId)?.Level ?? -1.0;

    // ===== Kültür baskısı =====

    /// <summary>
    /// Bağımlının kayıtlı illerinin ne kadarı overlord'un baskın kültüründen
    /// farklı (0-1). Yabancı halk ne kadar çoksa, özerklik istemi o kadar hızlı büyür.
    /// </summary>
    public static double ForeignCultureShare(string countryId)
    {
        var record = ReleasableSystem.Get(countryId);
        if (record == null) return 0.0;

        var provinces = record.ProvinceIds;
        if (provinces == null || provinces.Length == 0) return 0.0;

        // Overlord'un baskın kültürü: en çok ile sahip olduğu kültür.
        var distribution = ProvinceCultureSystem.GetCultureDistribution(record.OverlordId);
        string dominant = distribution.Count > 0
            ? distribution
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .First().Key
            : "";

        int foreign = 0;
        foreach (var p in provinces)
            if (!string.Equals(ProvinceCultureSystem.CultureOf(p), dominant, StringComparison.Ordinal))
                foreign++;

        return (double)foreign / provinces.Length;
    }

    // ===== Günlük tick =====

    /// <summary>
    /// Tüm bağımlılıkları bir gün ilerletir: kayma uygulanır, bağımsız olmuş
    /// kayıtlar budanır. Deterministiktir (RNG yok).
    /// </summary>
    public static void DailyTick(long currentDay)
    {
        EnsureLoaded();
        if (currentDay == 0) return;

        // Bağımsızlığını kazanmış / kaydı silinmiş durumları buda.
        var live = new HashSet<string>(
            ReleasableSystem.Releasable().Select(r => r.CountryId), StringComparer.OrdinalIgnoreCase);
        if (_states.Count > 0)
        {
            var stale = _states.Keys.Where(id => !live.Contains(id)).ToList();
            foreach (var id in stale)
                _states.Remove(id);
        }

        foreach (var record in ReleasableSystem.Releasable())
        {
            var state = EnsureState(record.CountryId);
            if (state == null) continue;

            var overlord = CountryDatabase.Get(record.OverlordId);
            double foreignShare = ForeignCultureShare(record.CountryId);
            double weakness = overlord != null ? Math.Clamp((100.0 - overlord.StabilityPercent) / 100.0, 0.0, 1.0) : 0.5;

            double drift = S.DriftBasePerDay
                         + foreignShare * S.CultureDriftWeight
                         + weakness * S.WeaknessDriftWeight;
            drift = Math.Min(drift, S.MaxDailyDrift);

            state.LastDailyDrift = drift;
            state.Level = Math.Clamp(state.Level + drift, 0.0, 100.0);
        }
    }

    // ===== Overlord eylemleri =====

    /// <summary>
    /// Bağımlıya özerklik verir (+adım). Yalnızca ilhak eden ülke yapabilir ve
    /// bekleme süresi geçmiş olmalı.
    /// </summary>
    public static bool GrantAutonomy(string countryId, string actorId, long currentDay, out string reason)
    {
        EnsureLoaded();
        reason = "";
        if (!CanAct(countryId, actorId, currentDay, out reason)) return false;

        var state = EnsureState(countryId);
        state.Level = Math.Clamp(state.Level + S.AutonomyStep, 0.0, 100.0);
        state.LastChangeDay = currentDay;
        state.GrantCount++;

        GD.Print($"[Autonomy] {countryId}: özerklik verildi → %{state.Level:N0} " +
                 $"(gün {currentDay}, toplam {state.GrantCount})");
        return true;
    }

    /// <summary>
    /// Özerkliği geri alır (-adım). Yalnızca ilhak eden ülke yapabilir ve
    /// bekleme süresi geçmiş olmalı.
    /// </summary>
    public static bool RevokeAutonomy(string countryId, string actorId, long currentDay, out string reason)
    {
        EnsureLoaded();
        reason = "";
        if (!CanAct(countryId, actorId, currentDay, out reason)) return false;

        var state = EnsureState(countryId);
        state.Level = Math.Clamp(state.Level - S.AutonomyStep, 0.0, 100.0);
        state.LastChangeDay = currentDay;
        state.RevokeCount++;

        GD.Print($"[Autonomy] {countryId}: özerklik geri alındı → %{state.Level:N0} " +
                 $"(gün {currentDay}, toplam {state.RevokeCount})");
        return true;
    }

    private static bool CanAct(string countryId, string actorId, long currentDay, out string reason)
    {
        reason = "";
        if (string.IsNullOrEmpty(countryId))
        {
            reason = "eksik bilgi";
            return false;
        }
        var record = ReleasableSystem.Get(countryId);
        if (record == null)
        {
            reason = "bu ülkenin bağımlılık kaydı yok";
            return false;
        }
        if (CountryDatabase.Get(countryId)?.IsAnnexed != true)
        {
            reason = "ülke artık bağımlı değil";
            return false;
        }
        if (!string.IsNullOrEmpty(actorId) &&
            !string.Equals(actorId, record.OverlordId, StringComparison.OrdinalIgnoreCase))
        {
            reason = "yalnızca ilhak eden ülke özerklik verip geri alabilir";
            return false;
        }

        var state = Get(countryId);
        if (state != null && currentDay >= 0 && state.LastChangeDay >= 0 &&
            currentDay - state.LastChangeDay < S.ChangeCooldownDays)
        {
            reason = $"özerklik kararları arasında {S.ChangeCooldownDays} gün beklenmeli";
            return false;
        }
        return true;
    }

    // ===== Etkiler =====

    /// <summary>
    /// Bir overlord'un bağımlılarından kesintiye uğrayan kaynak çıkarım katsayısı
    /// (1.0 = kesinti yok). İl sayısına göre ağırlıklı ortalama özerklik kullanılır.
    /// </summary>
    public static double ResourceExtractionFactor(string overlordId)
    {
        EnsureLoaded();
        var subjects = ReleasableSystem.SubjectsOf(overlordId);
        if (subjects.Count == 0) return 1.0;

        long totalProvinces = 0;
        double weighted = 0;
        foreach (var r in subjects)
        {
            int provinces = r.ProvinceIds?.Length ?? 0;
            double level = Math.Max(0.0, Level(r.CountryId));
            totalProvinces += provinces;
            weighted += level * provinces;
        }
        if (totalProvinces == 0) return 1.0;

        double avgLevel = weighted / totalProvinces;
        return 1.0 - (avgLevel / 100.0) * S.MaxExtractionLoss;
    }

    /// <summary>
    /// Özerk bağımlıların overlord stabilitesine günlük baskısı (pozitif değer,
    /// düşülecek miktar). Bağımlı yoksa 0.
    /// </summary>
    public static double StabilityPressure(string overlordId)
    {
        EnsureLoaded();
        var subjects = ReleasableSystem.SubjectsOf(overlordId);
        if (subjects.Count == 0) return 0.0;

        double sum = 0;
        foreach (var r in subjects)
            sum += Math.Max(0.0, Level(r.CountryId));
        if (sum <= 0) return 0.0;

        double avgLevel = sum / subjects.Count;
        return (avgLevel / 100.0) * S.MaxStabilityPressurePerDay;
    }

    // ===== Save/Load =====

    public static SaveSystem.AutonomySave GetSaveData()
    {
        var list = new List<SaveSystem.AutonomySubjectEntry>(_states.Count);
        foreach (var s in _states.Values)
        {
            list.Add(new SaveSystem.AutonomySubjectEntry
            {
                CountryId = s.CountryId,
                OverlordId = s.OverlordId,
                Level = s.Level,
                LastChangeDay = s.LastChangeDay,
                GrantCount = s.GrantCount,
                RevokeCount = s.RevokeCount,
                LastDailyDrift = s.LastDailyDrift
            });
        }
        return new SaveSystem.AutonomySave { Subjects = list.ToArray() };
    }

    public static void LoadSaveData(SaveSystem.AutonomySave data)
    {
        _states.Clear();
        if (data?.Subjects == null) return;

        foreach (var e in data.Subjects)
        {
            if (e == null || string.IsNullOrEmpty(e.CountryId)) continue;
            _states[e.CountryId] = new AutonomyState
            {
                CountryId = e.CountryId,
                OverlordId = e.OverlordId ?? "",
                Level = e.Level,
                LastChangeDay = e.LastChangeDay,
                GrantCount = e.GrantCount,
                RevokeCount = e.RevokeCount,
                LastDailyDrift = e.LastDailyDrift
            };
        }
        GD.Print($"[Autonomy] Özerklik durumları yüklendi: {_states.Count}");
    }
}
