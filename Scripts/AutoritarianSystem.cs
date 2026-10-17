using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// M22.4: Otoriter sistemler — parti kongresi (tek parti rejimleri için parlamento yerine)
// ve darbe riski (günlük deterministik değerlendirme). Kurgusal içerik, gerçek kişi adı yok.
public static class AutoritarianSystem
{
    // Kongre yapılandırması (tek parti rejimleri, sabit ve deterministik).
    public const int DelegateCount = 500;
    public const int ApprovalThreshold = DelegateCount / 2 + 1; // 251

    // Darbe riski sabitleri (günlük, % cinsinden).
    private const double BaseCoupRiskPercent = 0.3; // %0.3/gün temel risk
    private const double MaxCoupRiskPercent = 3.0;  // %3/gün üst sınır
    private const double StabilityRiskPerPoint = 0.05; // stabilite 40'ın altındaki her puan için +%0.05
    private const double WarSupportRiskPerPoint = 0.03; // savaş desteği 30'un altındaki her puan için +%0.03

    public class CoupAssessment
    {
        public string CountryId { get; set; } = "";
        public double RiskPercent { get; set; }
        public bool WillCoup { get; set; }
    }

    public class CongressResult
    {
        public string ProposalId { get; set; } = "";
        public int YesDelegates { get; set; }
        public int NoDelegates { get; set; }
        public int TotalDelegates { get; set; }
        public int ApprovalThreshold { get; set; }
        public bool Passed => YesDelegates >= ApprovalThreshold;
    }

    // proposalId -> onaylanmış (kongre tarafından)
    private static readonly HashSet<string> _approvedProposals = new();
    private static int _coupAttempts;
    private static long _lastCoupDay;
    private static double _lastCoupRiskPercent;

    public static int CoupAttempts => _coupAttempts;
    public static long LastCoupDay => _lastCoupDay;
    public static double LastCoupRiskPercent => _lastCoupRiskPercent;
    public static int ApprovedProposalCount => _approvedProposals.Count;

    /// <summary>Verilen ülke kongre rejimi mi? (Tek parti / monarşide parlamento yerine kongre.)</summary>
    public static bool IsCongressRegime(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return false;
        var type = ConstitutionSystem.GetConstitution(countryId).GovernmentType;
        return type == ConstitutionSystem.GovernmentType.OneParty;
    }

    /// <summary>Parti kongre oylaması. Evet delegesi >= onay eşiği geçer.</summary>
    public static CongressResult ApproveProposal(string proposalId, int yesDelegates, int noDelegates)
    {
        var result = new CongressResult
        {
            ProposalId = proposalId ?? "",
            YesDelegates = yesDelegates,
            NoDelegates = noDelegates,
            TotalDelegates = DelegateCount,
            ApprovalThreshold = ApprovalThreshold
        };
        if (result.Passed && !_approvedProposals.Contains(proposalId))
            _approvedProposals.Add(proposalId);
        GD.Print($"[Autoritarian] Kongre '{proposalId}': Evet={yesDelegates}, Hayır={noDelegates}, Eşik={ApprovalThreshold} → {(result.Passed ? "ONAYLANDI" : "REDDEDİLDİ")}");
        return result;
    }

    public static bool IsCongressApproved(string proposalId) => !string.IsNullOrEmpty(proposalId) && _approvedProposals.Contains(proposalId);

    /// <summary>Günlük darbe riskini hesaplar (deterministik, RNG atmazı yok). 0-100 arası.</summary>
    public static double ComputeCoupRisk(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return 0;
        var data = CountryDatabase.Get(countryId);
        if (data == null) return 0;

        double risk = BaseCoupRiskPercent;

        // Düşük stabilite riski artırır.
        if (data.StabilityPercent < 40)
            risk += (40 - data.StabilityPercent) * StabilityRiskPerPoint;

        // Düşük savaş desteği riski artırır.
        if (data.WarSupportPercent < 30)
            risk += (30 - data.WarSupportPercent) * WarSupportRiskPerPoint;

        risk = Math.Clamp(risk, 0, MaxCoupRiskPercent);
        _lastCoupRiskPercent = risk;
        return risk;
    }

    /// <summary>Darbe atar (deterministik RNG ile). Gerçekleşirse true döner ve etkileri uygular.</summary>
    public static bool TryTriggerCoup(string countryId, double riskPercent)
    {
        if (string.IsNullOrEmpty(countryId)) return false;
        double risk = riskPercent / 100.0;
        if (DeterministicRNG.NextDouble() >= risk) return false;

        _coupAttempts++;
        _lastCoupDay = GameManager.Instance?.TotalDays ?? 0;

        var data = CountryDatabase.Get(countryId);
        if (data != null)
        {
            data.StabilityPercent = Math.Clamp(data.StabilityPercent - 20, 0, 100);
            data.WarSupportPercent = Math.Clamp(data.WarSupportPercent - 15, 0, 100);
        }

        GD.Print($"[Autoritarian] DARBE TETİKLENDİ: {countryId}, risk=%{riskPercent:F1}, stabilite -20, savaş desteği -15");
        return true;
    }

    /// <summary>Günlük darbe riski değerlendirmesi ve olası darbe tetikleme (sadece kongre rejimleri).</summary>
    public static void DailyTick(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return;
        if (!IsCongressRegime(countryId)) return;

        double risk = ComputeCoupRisk(countryId);
        GD.Print($"[Autoritarian] {countryId} günlük darbe riski: % {risk:F1}");
        TryTriggerCoup(countryId, risk);
    }

    public static void ResetForNewGame()
    {
        _approvedProposals.Clear();
        _coupAttempts = 0;
        _lastCoupDay = 0;
        _lastCoupRiskPercent = 0;
        GD.Print("[Autoritarian] Yeni oyun için sıfırlandı.");
    }

    public static void LoadData()
    {
        // Kongre yapılandırması sabittir; dış veri dosyası yok. Yalnızca durumu raporla.
        GD.Print($"[Autoritarian] Hazır — kongre delegesi={DelegateCount}, onay eşiği={ApprovalThreshold}, tek parti rejimler için geçerli.");
    }

    // --- Save / Load ---
    public class AutoritarianSave
    {
        public string[] ApprovedProposals { get; set; } = Array.Empty<string>();
        public int CoupAttempts { get; set; }
        public long LastCoupDay { get; set; }
        public double LastCoupRiskPercent { get; set; }
    }

    public static AutoritarianSave GetSaveData() => new AutoritarianSave
    {
        ApprovedProposals = _approvedProposals.ToArray(),
        CoupAttempts = _coupAttempts,
        LastCoupDay = _lastCoupDay,
        LastCoupRiskPercent = _lastCoupRiskPercent
    };

    public static void LoadSaveData(AutoritarianSave save)
    {
        _approvedProposals.Clear();
        if (save == null) return;
        if (save.ApprovedProposals != null)
            foreach (var p in save.ApprovedProposals)
                if (!string.IsNullOrEmpty(p)) _approvedProposals.Add(p);
        _coupAttempts = save.CoupAttempts;
        _lastCoupDay = save.LastCoupDay;
        _lastCoupRiskPercent = save.LastCoupRiskPercent;
        GD.Print($"[Autoritarian] State yüklendi — Onaylı={_approvedProposals.Count}, Darbe={_coupAttempts}, SonRisk=%{_lastCoupRiskPercent:F1}");
    }
}
