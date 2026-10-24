using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// M45.3 — İl bazlı direniş.
///
/// İlhak edilmiş / kukla yapılmış bir halk, yabancı yönetime baş kaldırır.
/// Önceki adım (M45.2) direnişin yavaş yolunu modelliyordu: özerklik talebi.
/// Bu adım ise aynı baskının şiddetli yolunu: partizan eylemi.
///
/// Nasıl çalışır?
///   • Her bağımlı ülke için günlük "baskı" birikir: yabancı kültür payı arttıkça
///     artar (kültür ağırlığı), özerklik arttıkça azalır (rahatlama ağırlığı).
///   • Birikmiş baskı eşiği aşınca bir partizan eylemi patlak verir: bir il
///     seçilir, o ilin sahibi olan overlord'un bir fabrikası hasar görür ve
///     overlord'un stabilitesi ile halk memnuniyeti düşer.
///
/// Determinizm: eylem patlak verince hedef ilin seçimi
/// <see cref="DeterministicRNG"/> ile yapılır (GlobalRandom yasak) ve RNG
/// durumu kayda girer → save/load sonrası aynı eylem, aynı il.
/// </summary>
public static class ResistanceSystem
{
    private const string DataPath = "res://Data/resistance.json";

    private sealed class ResistanceSettings
    {
        public double BasePerDay = 0.02;
        public double CultureWeight = 0.35;
        public double AutonomyReliefWeight = 0.3;
        public double MaxPerDay = 0.5;
        public double MaxAccumulated = 100.0;
        public double StrikeChanceThreshold = 0.75;
        public double CultureScoreLow = 0.2;
        public double CultureScoreHigh = 0.8;
        public double CultureScoreStep = 0.08;
        public int FactoryDamagePerStrike = 1;
        public double OverlordStabilityLoss = 0.25;
        public double OverlordSatisfactionLoss = 2.0;
    }

    private sealed class ResistanceRoot
    {
        public int Version { get; set; } = 1;
        public ResistanceSettings Settings { get; set; } = new();
    }

    private static ResistanceSettings S = new();
    private static bool _loaded;

    /// <summary>Bir bağımlının direniş durumu.</summary>
    public class ResistanceState
    {
        public string CountryId = "";
        public string OverlordId = "";
        public double Accumulated = 0;      // 0-100 birikmiş baskı
        public double LastDailyRate = 0;    // son günkü artış hızı (UI/log)
        public int StrikeCount = 0;         // bugüne kadar patlak veren eylem
        public string LastStrikeProvinceId = ""; // son eylemin ili

        // M49.3: İşgal direnci. Bağımlılık zincirinden (ReleasableSystem)
        // bağımsız bir kaynak — toprağı işgal edilmiş halk da ayaklanır.
        public bool IsOccupied = false;     // toprakları şu an işgal altında
        public int OccupiedDays = 0;        // üst üste kaç gündür işgal altında
        public double OccupationRate = 0;   // son günkü işgal kaynaklı artış
    }

    private static readonly Dictionary<string, ResistanceState> _states =
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
                GD.PrintErr("[Resistance] resistance.json açılamadı — varsayılan ayarlar kullanılıyor.");
                return;
            }
            var root = JsonSerializer.Deserialize<ResistanceRoot>(file.GetAsText(),
                new JsonSerializerOptions
                {
                    IncludeFields = true,
                    PropertyNameCaseInsensitive = true
                });
            if (root?.Settings != null)
            {
                S = root.Settings;
                GD.Print($"[Resistance] Ayarlar yüklendi: taban={S.BasePerDay}, " +
                         $"kültür_ağırlık={S.CultureWeight}, rahatlama={S.AutonomyReliefWeight}, " +
                         $"eşik={S.StrikeChanceThreshold}, maks_birikim={S.MaxAccumulated}");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Resistance] resistance.json okunamadı ({e.Message}) — varsayılan ayarlar.");
        }
    }

    /// <summary>Kayıtlı durum sayısı (log/test).</summary>
    public static int StateCount => _states.Count;

    public static void ResetForNewGame()
    {
        _states.Clear();
        GD.Print("[Resistance] Yeni oyun için sıfırlandı.");
    }

    private static ResistanceState EnsureState(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return null;
        if (!_states.TryGetValue(countryId, out var s))
        {
            s = new ResistanceState
            {
                CountryId = countryId,
                OverlordId = ReleasableSystem.Get(countryId)?.OverlordId ?? ""
            };
            _states[countryId] = s;
        }
        return s;
    }

    public static ResistanceState Get(string countryId)
        => !string.IsNullOrEmpty(countryId) && _states.TryGetValue(countryId, out var s) ? s : null;

    /// <summary>Birikmiş baskı (kayıt yoksa 0).</summary>
    public static double Accumulated(string countryId) => Get(countryId)?.Accumulated ?? 0.0;

    // ===== M49.3: İşgal direnci =====

    /// <summary>
    /// İşgal altındaki halka günlük direniş baskısı ekler.
    ///
    /// Bu yol bağımlılık zincirinden (ReleasableSystem / AutonomySystem)
    /// TAMAMEN bağımsızdır: bir ülkenin toprakları savaşta işgal edilmişse,
    /// ilhak kaydı ya da özerklik düzeyi olmasa da halkı ayaklanır. Budama
    /// işaretinden (<see cref="ResistanceState.IsOccupied"/>) dolayı kayıt
    /// bir sonraki <see cref="DailyTick"/>'te de yaşamaya devam eder.
    ///
    /// Determinizm: büyüklük tam olarak verilir (RNG yok); eylemin hedef il
    /// seçimi ise sıralı taramayla yapılır — RNG'e gerek yoktur.
    /// </summary>
    public static void AddOccupationPressure(string countryId, double amount, int occupiedProvinces)
    {
        if (string.IsNullOrEmpty(countryId) || amount <= 0) return;
        EnsureLoaded();

        var state = Get(countryId) ?? EnsureState(countryId);
        if (state == null) return;

        state.IsOccupied = occupiedProvinces > 0;
        state.OccupiedDays = occupiedProvinces > 0 ? state.OccupiedDays + 1 : 0;
        state.OccupationRate = amount;
        state.Accumulated = Math.Clamp(state.Accumulated + amount, 0.0, S.MaxAccumulated);

        // Bağımlılık kaydı olmayan halk için eylem özel yolla çözülür.
        if (state.Accumulated >= S.MaxAccumulated * S.StrikeChanceThreshold)
            ResolveOccupationStrike(countryId, state);
    }

    /// <summary>Toprağı işgal edilmiş halkın eylemi — kurban İŞGALCİ olur.</summary>
    private static void ResolveOccupationStrike(string countryId, ResistanceState state)
    {
        if (!OccupationSystem.IsReady) return;

        // Hedef il: işgalci elindeki, bu halka ait topraklar (sıralı — deterministik).
        var occupied = OccupationSystem.LostProvincesOf(countryId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (occupied.Count == 0) return;

        string target = occupied[0];
        string occupierId = OccupationSystem.Occupier(target);
        if (string.IsNullOrEmpty(occupierId)) return;

        state.LastStrikeProvinceId = target;
        state.StrikeCount++;
        state.Accumulated = 0;

        var occupier = CountryDatabase.Get(occupierId);
        int damaged = 0;
        if (occupier != null && S.FactoryDamagePerStrike > 0)
        {
            if (occupier.CivFactories >= S.FactoryDamagePerStrike)
            {
                occupier.CivFactories -= S.FactoryDamagePerStrike;
                damaged = S.FactoryDamagePerStrike;
            }
            else if (occupier.MilFactories >= S.FactoryDamagePerStrike)
            {
                occupier.MilFactories -= S.FactoryDamagePerStrike;
                damaged = S.FactoryDamagePerStrike;
            }
            else if (occupier.Dockyards >= S.FactoryDamagePerStrike)
            {
                occupier.Dockyards -= S.FactoryDamagePerStrike;
                damaged = S.FactoryDamagePerStrike;
            }

            if (S.OverlordStabilityLoss > 0)
                occupier.StabilityPercent = Math.Clamp(
                    occupier.StabilityPercent - S.OverlordStabilityLoss, 0, 100);
        }

        GD.Print($"[Resistance] {countryId}: İŞGAL DİRENCİ EYLEMİ — hedef il={target}, " +
                 $"işgalci={occupierId} fabrika hasarı={damaged}, " +
                 $"stabilite -{S.OverlordStabilityLoss} (eylem no={state.StrikeCount})");
    }

    // ===== Kültür uyumsuzluğu puanı =====

    /// <summary>
    /// Bağımlının yabancı kültür payını 0-1 arası direniş uyumsuzluğuna çevirir.
    /// Pay 0.2'nin altında direniş yoktur (asimile olmuş), 0.8'in üzerinde tam
    /// uyumsuzluktur; arası lineer.
    /// </summary>
    public static double CultureMismatchScore(string countryId)
    {
        var record = ReleasableSystem.Get(countryId);
        if (record == null) return 0.0;

        var provinces = record.ProvinceIds;
        if (provinces == null || provinces.Length == 0) return 0.0;

        var distribution = ProvinceCultureSystem.GetCultureDistribution(record.OverlordId);
        string dominant = distribution.Count > 0
            ? distribution.OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key
            : "";

        int foreign = 0;
        foreach (var p in provinces)
            if (!string.Equals(ProvinceCultureSystem.CultureOf(p), dominant, StringComparison.Ordinal))
                foreign++;

        double share = (double)foreign / provinces.Length;
        if (share <= S.CultureScoreLow) return 0.0;
        if (share >= S.CultureScoreHigh) return 1.0;

        // Lineer: 0.2 → 0, 0.8 → 1
        double span = S.CultureScoreHigh - S.CultureScoreLow;
        return span > 0 ? (share - S.CultureScoreLow) / span : 0.0;
    }

    // ===== Günlük tick =====

    /// <summary>
    /// Tüm bağımlılıkları bir gün ilerletir. Deterministik:
    ///   • Baskı artışı = taban + uyumsuzluk × kültür_ağırlık − rahatlama ×
    ///     (özerklik / 100). Yüksek özerklik baskıyı azaltır.
    ///   • Birikim eşiği (max × 0.75) aştıysa eylem patlak verir; hedef il
    ///     DeterministicRNG ile seçilir, sonra üretim hasarı ve overlord'un
    ///     stabilite/memnuniyet kaybı uygulanır.
    /// Bağımsız olmuş kayıtlar budanır.
    /// </summary>
    public static void DailyTick(long currentDay)
    {
        EnsureLoaded();
        if (currentDay == 0) return;

        var live = new HashSet<string>(
            ReleasableSystem.Releasable().Select(r => r.CountryId), StringComparer.OrdinalIgnoreCase);
        if (_states.Count > 0)
        {
            // M49.3: işgal direnci bağımsızdır — toprağı işgal edilmiş bir halk
            // bağımlılık kaydı olmadan da direnir. Bu kayıtlar budanmaz.
            var stale = _states
                .Where(kv => !live.Contains(kv.Key) && !kv.Value.IsOccupied)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var id in stale)
                _states.Remove(id);
        }

        foreach (var record in ReleasableSystem.Releasable())
        {
            var state = EnsureState(record.CountryId);
            if (state == null) continue;

            double mismatch = CultureMismatchScore(record.CountryId);
            double autonomy = AutonomySystem.Level(record.CountryId);
            if (autonomy < 0) autonomy = 0;

            double rate = S.BasePerDay
                        + mismatch * S.CultureWeight
                        - (autonomy / 100.0) * S.AutonomyReliefWeight;
            rate = Math.Clamp(rate, 0.0, S.MaxPerDay);

            state.LastDailyRate = rate;
            state.Accumulated = Math.Clamp(state.Accumulated + rate, 0.0, S.MaxAccumulated);

            if (state.Accumulated >= S.MaxAccumulated * S.StrikeChanceThreshold)
                ResolveStrike(record, state);
        }
    }

    private static void ResolveStrike(ReleasableSystem.SubjectRecord record, ResistanceState state)
    {
        var provinces = record.ProvinceIds;
        if (provinces == null || provinces.Length == 0) return;

        // Hedef il: deterministik seçim (RNG)
        var heldByOverlord = provinces
            .Where(p => string.Equals(ProvinceOwnershipSystem.GetProvinceOwner(p), record.OverlordId,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (heldByOverlord.Count == 0) return;

        int pick = DeterministicRNG.NextInt(0, heldByOverlord.Count);
        string target = heldByOverlord[pick];
        state.LastStrikeProvinceId = target;
        state.StrikeCount++;
        state.Accumulated = 0; // eylem sonrası baskı boşalır

        var overlord = CountryDatabase.Get(record.OverlordId);
        int damaged = 0;
        if (overlord != null && S.FactoryDamagePerStrike > 0)
        {
            // Sivil fabrika tercihen, yoksa askeri/tersane
            if (overlord.CivFactories >= S.FactoryDamagePerStrike)
            {
                overlord.CivFactories -= S.FactoryDamagePerStrike;
                damaged = S.FactoryDamagePerStrike;
            }
            else if (overlord.MilFactories >= S.FactoryDamagePerStrike)
            {
                overlord.MilFactories -= S.FactoryDamagePerStrike;
                damaged = S.FactoryDamagePerStrike;
            }
            else if (overlord.Dockyards >= S.FactoryDamagePerStrike)
            {
                overlord.Dockyards -= S.FactoryDamagePerStrike;
                damaged = S.FactoryDamagePerStrike;
            }

            if (S.OverlordStabilityLoss > 0)
                overlord.StabilityPercent = Math.Clamp(
                    overlord.StabilityPercent - S.OverlordStabilityLoss, 0, 100);
        }

        if (overlord != null && S.OverlordSatisfactionLoss > 0)
            ApplySatisfactionLoss(overlord.Id);

        GD.Print($"[Resistance] {record.CountryId}: PARTİZAN EYLEMİ — hedef il={target} " +
                 $"(eylem no={state.StrikeCount}), {overlord?.Id ?? "?"} için " +
                 $"fabrika hasarı={damaged}, stabilite -{S.OverlordStabilityLoss}, " +
                 $"memnuniyet -{S.OverlordSatisfactionLoss}");
    }

    /// <summary>Overlord'un halk gruplarının memnuniyetini düşürür (toplum grubu başına).</summary>
    private static void ApplySatisfactionLoss(string countryId)
    {
        foreach (var group in PublicSatisfactionSystem.Groups)
        {
            if (group == null || string.IsNullOrEmpty(group.Id)) continue;
            double current = PublicSatisfactionSystem.GetSatisfaction(countryId, group.Id);
            PublicSatisfactionSystem.SetSatisfaction(countryId, group.Id,
                Math.Clamp(current - S.OverlordSatisfactionLoss, 0, 100));
        }
    }

    // ===== Save/Load =====

    public static SaveSystem.ResistanceSave GetSaveData()
    {
        var list = new List<SaveSystem.ResistanceEntry>(_states.Count);
        foreach (var s in _states.Values)
        {
            list.Add(new SaveSystem.ResistanceEntry
            {
                CountryId = s.CountryId,
                OverlordId = s.OverlordId,
                Accumulated = s.Accumulated,
                LastDailyRate = s.LastDailyRate,
                StrikeCount = s.StrikeCount,
                LastStrikeProvinceId = s.LastStrikeProvinceId,
                IsOccupied = s.IsOccupied,
                OccupiedDays = s.OccupiedDays,
                OccupationRate = s.OccupationRate
            });
        }
        return new SaveSystem.ResistanceSave { Entries = list.ToArray() };
    }

    public static void LoadSaveData(SaveSystem.ResistanceSave data)
    {
        _states.Clear();
        if (data?.Entries == null) return;

        foreach (var e in data.Entries)
        {
            if (e == null || string.IsNullOrEmpty(e.CountryId)) continue;
            _states[e.CountryId] = new ResistanceState
            {
                CountryId = e.CountryId,
                OverlordId = e.OverlordId ?? "",
                Accumulated = e.Accumulated,
                LastDailyRate = e.LastDailyRate,
                StrikeCount = e.StrikeCount,
                LastStrikeProvinceId = e.LastStrikeProvinceId ?? "",
                IsOccupied = e.IsOccupied,
                OccupiedDays = e.OccupiedDays,
                OccupationRate = e.OccupationRate
            };
        }
        GD.Print($"[Resistance] Direniş durumları yüklendi: {_states.Count}");
    }
}
