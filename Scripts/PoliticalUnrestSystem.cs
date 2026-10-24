using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// M23.4: Protesto, grev, güven oylaması, erken seçim ve darbe girişimi.
// Düşük halk memnuniyeti / stabilitede olaylar tetiklenir (deterministik).
//
// Tetikleyiciler (oyuncu ülkesinde, günlük):
//   • Protesto: 9 grubun ortalama memnuniyeti < %40 veya StabilityPercent < %40 → %15/gün
//   • Grev: İşçi grubu memnuniyeti < %30 → %10/gün
//   • Güven oylaması: ParliamentSystem.Vote (iktidarı vs muhalefet), başarısızsa erken seçim planlar
//   • Erken seçim: ElectionSystem.CallEarlyElection (planlanan günde gerçekleşir)
//   • Darbe: Tek parti (kongre) rejimlerinde AutoritarianSystem.TryTriggerCoup
//
// Etkiler: Stabilite, savaş desteği, PP, fabrika, memnuniyet (ağaçla loglanır).
// Save/Load: PoliticalUnrestSave DTO (GetSaveData/LoadSaveData).
// Deterministik: yalnızca DeterministicRNG (new Random() yok).

public static class PoliticalUnrestSystem
{
    // --- Eşikler ---
    private const double ProtestAvgSatisfactionThreshold = 40.0;
    private const double StrikeWorkersThreshold = 30.0;
    private const double StabilityThreshold = 40.0;

    // --- Günlük tetikleme olasılıkları (eşik altındayken) ---
    private const double ProtestChancePerDay = 0.15;
    private const double StrikeChancePerDay = 0.10;
    private const double VoteOfConfidenceChancePerDay = 0.05;

    // --- Bekleme (gün) ---
    private const int ProtestCooldownDays = 7;
    private const int StrikeCooldownDays = 7;
    private const int VoteOfConfidenceCooldownDays = 30;

    // --- Etkiler ---
    private const double ProtestStabilityHit = -5.0;
    private const double ProtestWarSupportHit = -3.0;
    private const double ProtestPPHit = -10.0;
    private const double ProtestSatisfactionHit = -3.0;

    private const double StrikeStabilityHit = -3.0;
    private const double StrikePPHit = -5.0;
    private const double StrikeWorkersHit = -8.0;

    // --- Per-ülke durum ---
    public class UnrestState
    {
        public string CountryId = "";
        public long LastProtestDay = -999;
        public long LastStrikeDay = -999;
        public long LastVoteOfConfidenceDay = -999;
        public int ProtestCount;
        public int StrikeCount;
        public int VoteOfConfidenceCount;
        public int CoupAttemptCount;
        public long EarlyElectionDay = -1;
        public string EarlyElectionReason = "";
    }

    private static readonly Dictionary<string, UnrestState> _states = new();

    public static IReadOnlyDictionary<string, UnrestState> States => _states;

    // --- Save/Load DTO ---
    public class PoliticalUnrestSave
    {
        public CountryUnrestSave[] Countries { get; set; } = Array.Empty<CountryUnrestSave>();
    }

    public class CountryUnrestSave
    {
        public string CountryId { get; set; } = "";
        public long LastProtestDay { get; set; } = -999;
        public long LastStrikeDay { get; set; } = -999;
        public long LastVoteOfConfidenceDay { get; set; } = -999;
        public int ProtestCount { get; set; }
        public int StrikeCount { get; set; }
        public int VoteOfConfidenceCount { get; set; }
        public int CoupAttemptCount { get; set; }
        public long EarlyElectionDay { get; set; } = -1;
        public string EarlyElectionReason { get; set; } = "";
    }

    public static void InitializeAll()
    {
        _states.Clear();
        foreach (var c in CountryDatabase.All())
            if (c != null) EnsureState(c.Id);
        GD.Print($"[PoliticalUnrest] {_states.Count} ülke durumu başlatıldı.");
    }

    public static void ResetForNewGame()
    {
        _states.Clear();
        GD.Print("[PoliticalUnrest] Yeni oyun için sıfırlandı.");
    }

    private static UnrestState GetState(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return null;
        if (!_states.TryGetValue(countryId, out var s))
        {
            s = new UnrestState { CountryId = countryId };
            _states[countryId] = s;
        }
        return s;
    }

    private static UnrestState EnsureState(string countryId) => GetState(countryId);

    // 9 grubun ortalama memnuniyeti (ElectionSystem ile aynı formül)
    private static double AverageSatisfaction(string countryId)
    {
        double sum = 0;
        int count = 0;
        foreach (var g in PublicSatisfactionSystem.Groups)
        {
            sum += PublicSatisfactionSystem.GetSatisfaction(countryId, g.Id);
            count++;
        }
        return count > 0 ? sum / count : 50.0;
    }

    private static bool CanTrigger(long lastDay, long currentDay, int cooldownDays) =>
        currentDay - lastDay >= cooldownDays;

    private static CountryData GetCountryData(string countryId) => CountryDatabase.Get(countryId);

    private static void ApplyStability(string countryId, double delta)
    {
        var data = GetCountryData(countryId);
        if (data != null)
            data.StabilityPercent = Math.Clamp(data.StabilityPercent + delta, 0, 100);
    }

    private static void ApplyWarSupport(string countryId, double delta)
    {
        var data = GetCountryData(countryId);
        if (data != null)
            data.WarSupportPercent = Math.Clamp(data.WarSupportPercent + delta, 0, 100);
    }

    private static void ApplyPP(string countryId, double delta)
    {
        var data = GetCountryData(countryId);
        if (data != null)
            data.PoliticalPower = Math.Clamp(data.PoliticalPower + delta, 0, 1000);
    }

    private static void HitAllSatisfaction(string countryId, double delta)
    {
        foreach (var g in PublicSatisfactionSystem.Groups)
        {
            var cur = PublicSatisfactionSystem.GetSatisfaction(countryId, g.Id);
            PublicSatisfactionSystem.SetSatisfaction(countryId, g.Id, Math.Clamp(cur + delta, 0, 100));
        }
    }

    // --- Günlük tick (GameManager.DailyTick çağırır, oyuncu ülkesinde) ---
    public static void DailyTick(string countryId, long currentDay)
    {
        if (string.IsNullOrEmpty(countryId)) return;

        var state = GetState(countryId);
        if (state == null) return;

        var data = GetCountryData(countryId);
        if (data == null) return;

        double avgSat = AverageSatisfaction(countryId);
        double stability = data.StabilityPercent;
        double workersSat = PublicSatisfactionSystem.GetSatisfaction(countryId, "workers");

        bool lowUnrest = avgSat < ProtestAvgSatisfactionThreshold || stability < StabilityThreshold;

        // 1) Protesto
        if (lowUnrest && CanTrigger(state.LastProtestDay, currentDay, ProtestCooldownDays))
        {
            if (DeterministicRNG.NextDouble() < ProtestChancePerDay)
                TriggerProtest(countryId, currentDay, state, avgSat, stability);
        }

        // 2) Grev (işçi memnuniyeti düşük)
        if (workersSat < StrikeWorkersThreshold && CanTrigger(state.LastStrikeDay, currentDay, StrikeCooldownDays))
        {
            if (DeterministicRNG.NextDouble() < StrikeChancePerDay)
                TriggerStrike(countryId, currentDay, state, workersSat);
        }

        // 3) Güven oylaması (parlamento olan rejimlerde, düşük memnuniyet/stabilitede)
        if (lowUnrest && ParliamentSystem.TotalSeats > 0 &&
            CanTrigger(state.LastVoteOfConfidenceDay, currentDay, VoteOfConfidenceCooldownDays))
        {
            if (DeterministicRNG.NextDouble() < VoteOfConfidenceChancePerDay)
                TriggerVoteOfConfidence(countryId, currentDay, state);
        }

        // 4) Erken seçim (planlanmışsa ve gün geldi)
        if (state.EarlyElectionDay >= 0 && currentDay >= state.EarlyElectionDay)
            TriggerEarlyElection(countryId, currentDay, state);

        // 5) Darbe (tek parti / kongre rejimlerinde, düşük stabilitede)
        if (AutoritarianSystem.IsCongressRegime(countryId) && stability < StabilityThreshold)
        {
            double risk = AutoritarianSystem.ComputeCoupRisk(countryId);
            if (risk > 0 && AutoritarianSystem.TryTriggerCoup(countryId, risk))
            {
                state.CoupAttemptCount++;
                GD.Print($"[PoliticalUnrest] {countryId}: darbe girişimi kaydedildi (toplam={state.CoupAttemptCount}).");
            }
        }
    }

    private static void TriggerProtest(string countryId, long currentDay, UnrestState state, double avgSat, double stability)
    {
        state.LastProtestDay = currentDay;
        state.ProtestCount++;

        ApplyStability(countryId, ProtestStabilityHit);
        ApplyWarSupport(countryId, ProtestWarSupportHit);
        ApplyPP(countryId, ProtestPPHit);
        HitAllSatisfaction(countryId, ProtestSatisfactionHit);

        GD.Print($"[PoliticalUnrest] {countryId}: PROTESTO tetiklendi (gün {currentDay}, ortalama_memnuniyet=%{avgSat:F1}, stabilite=%{stability:F1}) → stabilite {ProtestStabilityHit}, WS {ProtestWarSupportHit}, PP {ProtestPPHit}, tüm gruplar {ProtestSatisfactionHit}.");
    }

    private static void TriggerStrike(string countryId, long currentDay, UnrestState state, double workersSat)
    {
        state.LastStrikeDay = currentDay;
        state.StrikeCount++;

        ApplyStability(countryId, StrikeStabilityHit);
        ApplyPP(countryId, StrikePPHit);
        var cur = PublicSatisfactionSystem.GetSatisfaction(countryId, "workers");
        PublicSatisfactionSystem.SetSatisfaction(countryId, "workers", Math.Clamp(cur + StrikeWorkersHit, 0, 100));

        GD.Print($"[PoliticalUnrest] {countryId}: GREV tetiklendi (gün {currentDay}, işçi_memnuniyet=%{workersSat:F1}) → stabilite {StrikeStabilityHit}, PP {StrikePPHit}, işçi {StrikeWorkersHit}.");
    }

    private static void TriggerVoteOfConfidence(string countryId, long currentDay, UnrestState state)
    {
        state.LastVoteOfConfidenceDay = currentDay;
        state.VoteOfConfidenceCount++;

        var ruling = ParliamentSystem.Parties.Where(p => p.IsRuling).Select(p => p.Id).ToList();
        var opposition = ParliamentSystem.Parties.Where(p => !p.IsRuling).Select(p => p.Id).ToList();

        var result = ParliamentSystem.Vote($"confidence_{currentDay}", ruling, opposition, Array.Empty<string>());

        if (!result.Passed)
        {
            if (state.EarlyElectionDay < 0)
            {
                int daysUntil = DeterministicRNG.NextInt(1, 4);
                state.EarlyElectionDay = currentDay + daysUntil;
                state.EarlyElectionReason = "güven_oylama_basarisiz";
                GD.Print($"[PoliticalUnrest] {countryId}: güven oylaması BAŞARISIZ (Evet={result.YesSeats}/{result.MajorityThreshold}) — erken seçim {daysUntil} gün sonra (gün {state.EarlyElectionDay}).");
            }
        }
        else
        {
            GD.Print($"[PoliticalUnrest] {countryId}: güven oylaması BAŞARILI (Evet={result.YesSeats}/{result.MajorityThreshold}) — iktidar devam ediyor.");
        }
    }

    private static void TriggerEarlyElection(string countryId, long currentDay, UnrestState state)
    {
        state.EarlyElectionDay = -1;
        string reason = state.EarlyElectionReason;

        var result = ElectionSystem.CallEarlyElection(countryId, currentDay);
        if (result != null)
        {
            GD.Print($"[PoliticalUnrest] {countryId}: ERKEK SEÇİM gerçekleşti (gün {currentDay}, neden={reason}) → {(result.Won ? "KAZANDI — iktidar korundu" : "KAYBEDİLDİ — lider değişti: " + result.NewLeader)}, oy=%{result.PredictedVote:F1}");
        }
        else
        {
            GD.PrintErr($"[PoliticalUnrest] {countryId}: erken seçim çözümlenemedi (gün {currentDay}, neden={reason}) — seçim henüz due değil.");
        }
    }

    /// <summary>
    /// M50.3: Huzursuzluk kanalına dışarıdan bir olay ekler (yabancı güç
    /// ajitasyonu, dış müdahale). Çöküş puanı formülü değişmez; girdisi değişir.
    /// </summary>
    public static void AddUnrestEvent(string countryId, string kind, long currentDay)
    {
        if (string.IsNullOrEmpty(countryId)) return;
        var state = GetState(countryId);
        if (state == null) return;

        switch ((kind ?? "").ToLowerInvariant())
        {
            case "strike":
                state.LastStrikeDay = currentDay;
                state.StrikeCount++;
                break;
            case "vote":
                state.LastVoteOfConfidenceDay = currentDay;
                state.VoteOfConfidenceCount++;
                break;
            default:
                state.LastProtestDay = currentDay;
                state.ProtestCount++;
                break;
        }

        GD.Print($"[PoliticalUnrest] {countryId}: dış kaynaklı olay ({kind}) kaydedildi " +
                 $"(gün {currentDay}) — huzursuzluk kanalı yükseldi.");
    }

    /// <summary>
    /// M50.3: Huzursuzluk kanalını yumuşatır — sayaçları orana göre azaltır.
    /// Yabancı yardım/hukumetin yatıştırıcı adımları buradan çöküş puanına
    /// iner. Oran 0 kanalı tamamen sıfırlar, 1 dokunmaz.
    /// </summary>
    public static void CalmUnrest(string countryId, double factor)
    {
        if (string.IsNullOrEmpty(countryId) || factor <= 0.0) return;
        var state = GetState(countryId);
        if (state == null) return;

        double keep = Math.Clamp(1.0 - factor, 0.0, 1.0);
        int before = state.ProtestCount + state.StrikeCount +
                     state.VoteOfConfidenceCount + state.CoupAttemptCount;
        if (before == 0) return;

        state.ProtestCount = (int)Math.Floor(state.ProtestCount * keep);
        state.StrikeCount = (int)Math.Floor(state.StrikeCount * keep);
        state.VoteOfConfidenceCount = (int)Math.Floor(state.VoteOfConfidenceCount * keep);
        state.CoupAttemptCount = (int)Math.Floor(state.CoupAttemptCount * keep);

        GD.Print($"[PoliticalUnrest] {countryId}: huzursuzluk kanalı yatıştırıldı " +
                 $"(oran=%{factor * 100:F0}) — {before} → " +
                 $"{state.ProtestCount + state.StrikeCount + state.VoteOfConfidenceCount + state.CoupAttemptCount}");
    }

    // ===== M12: Save/Load =====
    public static PoliticalUnrestSave GetSaveData() => new PoliticalUnrestSave
    {
        Countries = _states.Select(kv => new CountryUnrestSave
        {
            CountryId = kv.Key,
            LastProtestDay = kv.Value.LastProtestDay,
            LastStrikeDay = kv.Value.LastStrikeDay,
            LastVoteOfConfidenceDay = kv.Value.LastVoteOfConfidenceDay,
            ProtestCount = kv.Value.ProtestCount,
            StrikeCount = kv.Value.StrikeCount,
            VoteOfConfidenceCount = kv.Value.VoteOfConfidenceCount,
            CoupAttemptCount = kv.Value.CoupAttemptCount,
            EarlyElectionDay = kv.Value.EarlyElectionDay,
            EarlyElectionReason = kv.Value.EarlyElectionReason
        }).ToArray()
    };

    public static void LoadSaveData(PoliticalUnrestSave data)
    {
        if (data == null) return;
        _states.Clear();
        if (data.Countries != null)
        {
            foreach (var c in data.Countries)
            {
                if (string.IsNullOrEmpty(c.CountryId)) continue;
                _states[c.CountryId] = new UnrestState
                {
                    CountryId = c.CountryId,
                    LastProtestDay = c.LastProtestDay,
                    LastStrikeDay = c.LastStrikeDay,
                    LastVoteOfConfidenceDay = c.LastVoteOfConfidenceDay,
                    ProtestCount = c.ProtestCount,
                    StrikeCount = c.StrikeCount,
                    VoteOfConfidenceCount = c.VoteOfConfidenceCount,
                    CoupAttemptCount = c.CoupAttemptCount,
                    EarlyElectionDay = c.EarlyElectionDay,
                    EarlyElectionReason = c.EarlyElectionReason
                };
            }
        }
        GD.Print($"[PoliticalUnrest] {_states.Count} ülke halk-refahı durumu yüklendi.");
    }
}
