using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Judiciary System (Yargı Sistemi) — Anayasa Mahkemesi, yargı bağımsızlığı, atama sistemi,
/// dava süreci ve kararlara dayalı presedens (öncek) mekanizması.
///
/// Özellikler:
/// - Anayasa Mahkemesi: yasaların anayasa uygunluğunu denetler, iptal edebilir
/// - Yargı Bağımsızlığı: ülkelerin yargı bağımsızlık seviyesi (0-100), hükümet müdahalesine dayanıklılık
/// - Hakim Atama: yürütme organı atar, yasama onaylar; ideolojik/uygunluk skorları
/// - Dava Sistemi: yasalar anayasa mahkemesine itiraz edilebilir, süreç zaman alır
/// - Presedens (Öncek): Mahkeme kararları gelecekteki benzer davalara kılavuz olur
/// - Farklı rejim türleri için farklı yargı yapıları (tek parti, monarşi, demokrasi)
///
/// Veri: Data/judiciary.json (mahkeme yapılandırması), anayasa.json entegrasyonu
/// Save/Load destekli, DeterministicRNG kullanır.
/// </summary>
public static class JudiciarySystem
{
    // ===== Veri Modelleri =====

    /// <summary>Yargı bağımsızlık seviyesi.</summary>
    public enum JudicialIndependenceLevel
    {
        Controlled = 0,      // %0-20: Hükümetin tam kontrolünde
        Influenced = 1,      // %21-40: Hükümet baskısı altında
        SemiIndependent = 2, // %41-60: Kısmi bağımsızlık
        Independent = 3,     // %61-80: Çoğunlukla bağımsız
        FullyIndependent = 4 // %81-100: Tam bağımsız
    }

    /// <summary>Mahkeme türleri.</summary>
    public enum CourtType
    {
        Constitutional,      // Anayasa Mahkemesi
        Supreme,             // Yüce Mahkeme (Yargıtay/Danıştay)
        Administrative,      // İdare Mahkemeleri
        Civil,               // Hukuk Mahkemeleri
        Criminal,            // Ceza Mahkemeleri
        Military             // Askeri Mahkemeler
    }

    /// <summary>Dava türleri.</summary>
    public enum CaseType
    {
        ConstitutionalReview,    // Anayasa uygunluk denetimi (yasa iptali)
        IndividualApplication,   // Bireysel başvuru (temel hak ihlali)
        ElectionDispute,         // Seçim itirazı
        PartyClosure,            // Parti kapatma davası
        Impeachment,             // Yüksek makamlı yargılanma
        JurisdictionConflict,    // Yetki uyuşmazlığı
        EmergencyReview          // Olağanüstü hal denetimi
    }

    /// <summary>Dava durumu.</summary>
    public enum CaseStatus
    {
        Filed,           // Dava açıldı
        UnderReview,     // İnceleniyor
        HearingScheduled,// Durağa gelindi
        Decided,         // Karar verildi
        Enforced         // Karar uygulandı/İptal edildi
    }

    /// <summary>Dava sonucu.</summary>
    public enum CaseOutcome
    {
        Dismissed,              // Reddedildi
        Upheld,                 // Yasama kararı onaylandı (yasa kaldı)
        StruckDown,             // İptal edildi (yasa anayasaya aykırı)
        PartiallyStruckDown,    // Kısmen iptal edildi
        Deferred                // Ertelendi
    }

    /// <summary>Hakim profili.</summary>
    public class Judge
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string Ideology { get; set; } = "";           // Liberal, Conservative, Socialist, Nationalist, Centrist
        public int Competence { get; set; } = 50;            // 0-100 yargılama yeteneği
        public int Integrity { get; set; } = 50;             // 0-100 ahlaki dayanım, rüşvete dayanıklılık
        public int Independence { get; set; } = 50;          // 0-100 hükümet baskısına dayanıklılık
        public int Experience { get; set; } = 0;             // Yıl cinsinden deneyim
        public CourtType Court { get; set; } = CourtType.Constitutional;
        public string AppointedBy { get; set; } = "";        // Kimin atadığı (lider/parti)
        public long AppointmentDay { get; set; }
        public bool IsChiefJustice { get; set; } = false;    // Başhakim mi
        public int CasesDecided { get; set; } = 0;
        public int PrecedentsAuthored { get; set; } = 0;
    }

    /// <summary>Anayasa Mahkemesi yapılandırması.</summary>
    public class ConstitutionalCourtConfig
    {
        public string CountryId { get; set; } = "";
        public int JudgeCount { get; set; } = 11;             // Hakim sayısı (tek sayı tercih edilir)
        public int TermLengthYears { get; set; } = 9;         // Görev süresi (yıl)
        public bool LifeTenure { get; set; } = false;         // Yaşam boyu görev mi
        public int RetirementAge { get; set; } = 70;          // Emeklilik yaşı
        public string AppointmentMethod { get; set; } = "ExecutiveLegislative"; // ExecutiveLegislative, LegislativeOnly, ExecutiveOnly
        public int LegislativeConfirmThreshold { get; set; } = 60; // Yasama onayı % (örn. %60 oy)
        public JudicialIndependenceLevel IndependenceLevel { get; set; } = JudicialIndependenceLevel.Independent;
    }

    /// <summary>Dava kaydı.</summary>
    public class Case
    {
        public string Id { get; set; } = "";
        public CaseType Type { get; set; }
        public string CountryId { get; set; } = "";
        public string ChallengerId { get; set; } = "";        // Dava açan (parti, birey, kurum)
        public string TargetLawId { get; set; } = "";         // Hedef yasa (varsa)
        public string TargetActionId { get; set; } = "";      // Hedef işlem/karar (varsa)
        public long FilingDay { get; set; }
        public CaseStatus Status { get; set; } = CaseStatus.Filed;
        public CaseOutcome Outcome { get; set; } = CaseOutcome.Dismissed;
        public long DecisionDay { get; set; }
        public string Reasoning { get; set; } = "";           // Gerekçe
        public string PrecedentId { get; set; } = "";         // Oluşturulan presedens ID
        public List<string> JudgeIds = new();                 // Karara katılan hakimler
        public int VoteFor { get; set; } = 0;                 // İptal/Red oyu
        public int VoteAgainst { get; set; } = 0;             // Koruma/İptal karşı oyu
        public bool CreatedPrecedent { get; set; } = false;
    }

    /// <summary>Presedens (Öncek) kaydı.</summary>
    public class Precedent
    {
        public string Id { get; set; } = "";
        public string CountryId { get; set; } = "";
        public CaseType CaseType { get; set; }
        public string Principle { get; set; } = "";           // Hukuki ilke/kural
        public string Summary { get; set; } = "";             // Özet
        public long EstablishedDay { get; set; }
        public int TimesCited { get; set; } = 0;              // Kaç kez atıf yapıldı
        public int BindingStrength { get; set; } = 100;       // Bağlayıcılık gücü (0-100, zamanla azalabilir)
        public List<string> RelatedLawIds = new();            // Etkilenen yasa alanları
    }

    /// <summary>Ülke yargı durumu.</summary>
    public class CountryJudiciaryState
    {
        public string CountryId { get; set; } = "";
        public ConstitutionalCourtConfig CourtConfig { get; set; } = new();
        public JudicialIndependenceLevel IndependenceLevel { get; set; } = JudicialIndependenceLevel.SemiIndependent;
        public List<Judge> Judges = new();                    // Tüm hakimler (Anayasa + Yüce + diğer)
        public List<Case> Cases = new();                      // Aktif/geçmiş davalar
        public List<Precedent> Precedents = new();            // Presedensler
        public int PendingCases => Cases.Count(c => c.Status != CaseStatus.Decided && c.Status != CaseStatus.Enforced);
        public int ConstitutionalCourtJudges => Judges.Count(j => j.Court == CourtType.Constitutional);
        public Judge ChiefJustice => Judges.FirstOrDefault(j => j.IsChiefJustice && j.Court == CourtType.Constitutional);
    }

    // ===== Sabitler =====
    private const string ConfigPath = "res://Data/judiciary.json";
    private const string DataPath = "res://Data/constitution.json"; // Anayasa verisi için

    // ===== Durum =====
    private static readonly Dictionary<string, CountryJudiciaryState> _states = new();
    private static int _nextCaseNum = 1;
    private static int _nextPrecedentNum = 1;
    private static int _nextJudgeNum = 1;
    private static bool _initialized;

    // ===== Public API =====

    public static int StateCount => _states.Count;
    public static IReadOnlyDictionary<string, CountryJudiciaryState> States => _states;

    /// <summary>Sistemi başlatır (yapılandırma dosyasını yükler, ülke durumu oluşturur).</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        LoadConfig();
        InitializeStates();

        GD.Print($"[JudiciarySystem] Initialized — {_states.Count} ülke yargı durumu hazır.");
    }

    /// <summary>Yeni oyun için yargı durumlarını sıfırlar ve yeniden oluşturur.</summary>
    public static void ResetForNewGame()
    {
        _states.Clear();
        _nextCaseNum = 1;
        _nextPrecedentNum = 1;
        _nextJudgeNum = 1;
        InitializeStates();
        GD.Print("[JudiciarySystem] Yeni oyun için sıfırlandı.");
    }

    /// <summary>Senaryo değişince yargı durumlarını sıfırlar.</summary>
    public static void ResetForScenario()
    {
        ResetForNewGame();
    }

    /// <summary>Ülke yargı durumunu getirir (yoksa oluşturur).</summary>
    public static CountryJudiciaryState GetState(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return null;
        if (!_states.TryGetValue(countryId, out var state))
        {
            state = CreateStateForCountry(countryId);
            _states[countryId] = state;
        }
        return state;
    }

    /// <summary>Yasa anayasa mahkemesine itiraz edilir (dava açılır).</summary>
    public static Case FileConstitutionalChallenge(string countryId, string lawId, string challengerId, long currentDay, string reason = "")
    {
        var state = GetState(countryId);
        if (state == null) return null;

        var law = LawSystem.GetLaw(lawId);
        if (law == null) return null;

        // Anayasa mahkemesi hakim sayısı kontrolü
        if (state.ConstitutionalCourtJudges < 3)
        {
            GD.PrintErr($"[Judiciary] {countryId}: Anayasa Mahkemesi hakim sayısı yetersiz ({state.ConstitutionalCourtJudges})");
            return null;
        }

        var caze = new Case
        {
            Id = $"case_{_nextCaseNum++}",
            Type = CaseType.ConstitutionalReview,
            CountryId = countryId,
            ChallengerId = challengerId,
            TargetLawId = lawId,
            FilingDay = currentDay,
            Status = CaseStatus.Filed,
            Reasoning = reason
        };

        state.Cases.Add(caze);
        GD.Print($"[Judiciary] {countryId}: Anayasa itirazı açıldı — {law.Name} ({lawId}) davacı={challengerId} (gün {currentDay})");
        return caze;
    }

    /// <summary>Bireysel başvuru açılır (temel hak ihlali).</summary>
    public static Case FileIndividualApplication(string countryId, string applicantId, string reason, long currentDay)
    {
        var state = GetState(countryId);
        if (state == null) return null;

        var caze = new Case
        {
            Id = $"case_{_nextCaseNum++}",
            Type = CaseType.IndividualApplication,
            CountryId = countryId,
            ChallengerId = applicantId,
            FilingDay = currentDay,
            Status = CaseStatus.Filed,
            Reasoning = reason
        };

        state.Cases.Add(caze);
        GD.Print($"[Judiciary] {countryId}: Bireysel başvuru açıldı — {applicantId} (gün {currentDay})");
        return caze;
    }

    /// <summary>Seçim itirazı davası açılır.</summary>
    public static Case FileElectionDispute(string countryId, string challengerId, long currentDay, string reason = "")
    {
        var state = GetState(countryId);
        if (state == null) return null;

        var caze = new Case
        {
            Id = $"case_{_nextCaseNum++}",
            Type = CaseType.ElectionDispute,
            CountryId = countryId,
            ChallengerId = challengerId,
            FilingDay = currentDay,
            Status = CaseStatus.Filed,
            Reasoning = reason
        };

        state.Cases.Add(caze);
        GD.Print($"[Judiciary] {countryId}: Seçim itirazı açıldı — {challengerId} (gün {currentDay})");
        return caze;
    }

    /// <summary>Günlük tick — davaları ilerlet, hakim emekliliği/atamaları yönet.</    /// </summary>
    public static void DailyTick(long currentDay)
    {
        foreach (var kv in _states)
        {
            var state = kv.Value;
            if (state == null) continue;

            // 1. Hakim emekliliği kontrolü
            ProcessJudgeRetirements(state, currentDay);

            // 2. Boş hakim koltuklarını doldur (atama süreci)
            ProcessVacancies(state, currentDay);

            // 3. Davaları ilerlet
            ProcessCases(state, currentDay);

            // 2. Presedens bağlayıcılık gücünü azalt (yılbaşı)
            if (currentDay > 0 && currentDay % 365 == 0)
            {
                DecayPrecedents(state);
            }
        }
    }

    /// <summary>Yargı bağımsızlık seviyesini getirir (0-100).</summary>
    public static int GetIndependenceScore(string countryId)
    {
        var state = GetState(countryId);
        if (state == null) return 50;
        return (int)state.IndependenceLevel * 20; // 0, 20, 40, 60, 80
    }

    /// <summary>Yargı bağımsızlık seviyesini ayarlar (test/senaryo için).</summary>
    public static void SetIndependenceLevel(string countryId, JudicialIndependenceLevel level)
    {
        var state = GetState(countryId);
        if (state != null) state.IndependenceLevel = level;
    }

    /// <summary>Yargı bağımsızlığına göre dava iptal olasılığını etkiler.</    /// Yüksek bağımsızlık = yasa iptali olasılığı artar, düşük = yasa korunur.
    /// </summary>
    public static double GetJudicialBiasFactor(string countryId, bool favorGovernment)
    {
        var state = GetState(countryId);
        if (state == null) return 1.0;

        int score = (int)state.IndependenceLevel; // 0-4
        double baseBias = 1.0 - (score * 0.15); // 1.0, 0.85, 0.70, 0.55, 0.40

        if (favorGovernment)
        {
            // Hükümet lehine bias: düşük bağımsızlıkta hükümet kazanıyor
            return baseBias;
        }
        else
        {
            // Hükümet aleyhine bias: yüksek bağımsızlıkta yasa iptal ediliyor
            return 2.0 - baseBias; // 1.0, 1.15, 1.30, 1.45, 1.60
        }
    }

    // ===== İç Metotlar =====

    private static void InitializeStates()
    {
        foreach (var country in CountryDatabase.All())
        {
            if (country == null || string.IsNullOrEmpty(country.Id)) continue;
            CreateStateForCountry(country.Id);
        }
    }

    private static CountryJudiciaryState CreateStateForCountry(string countryId)
    {
        var constitution = ConstitutionSystem.GetConstitution(countryId);
        if (constitution == null) return null;

        var state = new CountryJudiciaryState
        {
            CountryId = countryId,
            CourtConfig = CreateCourtConfigForCountry(countryId, constitution),
            IndependenceLevel = DetermineInitialIndependence(countryId, constitution)
        };

        // Hakimleri oluştur
        GenerateJudges(state);

        _states[countryId] = state;
        return state;
    }

    private static ConstitutionalCourtConfig CreateCourtConfigForCountry(string countryId, ConstitutionSystem.ConstitutionData constitution)
    {
        var config = new ConstitutionalCourtConfig
        {
            CountryId = countryId,
            JudgeCount = GetJudgeCountForGovType(constitution.GovernmentType),
            TermLengthYears = GetTermLengthForGovType(constitution.GovernmentType),
            LifeTenure = constitution.GovernmentType == ConstitutionSystem.GovernmentType.Parliamentary ||
                        constitution.GovernmentType == ConstitutionSystem.GovernmentType.Presidential,
            RetirementAge = 70,
            AppointmentMethod = GetAppointmentMethodForGovType(constitution.GovernmentType),
            LegislativeConfirmThreshold = GetConfirmThresholdForGovType(constitution.GovernmentType),
            IndependenceLevel = JudicialIndependenceLevel.SemiIndependent // Başlangıç varsayılanı
        };

        return config;
    }

    private static JudicialIndependenceLevel DetermineInitialIndependence(string countryId, ConstitutionSystem.ConstitutionData constitution)
    {
        var govType = constitution.GovernmentType;
        return govType switch
        {
            ConstitutionSystem.GovernmentType.Parliamentary => JudicialIndependenceLevel.Independent,
            ConstitutionSystem.GovernmentType.Presidential => JudicialIndependenceLevel.SemiIndependent,
            ConstitutionSystem.GovernmentType.SemiPresidential => JudicialIndependenceLevel.SemiIndependent,
            ConstitutionSystem.GovernmentType.Monarchy => JudicialIndependenceLevel.Influenced,
            ConstitutionSystem.GovernmentType.OneParty => JudicialIndependenceLevel.Controlled,
            _ => JudicialIndependenceLevel.SemiIndependent
        };
    }

    private static int GetJudgeCountForGovType(ConstitutionSystem.GovernmentType type)
    {
        return type switch
        {
            ConstitutionSystem.GovernmentType.Parliamentary => 11,
            ConstitutionSystem.GovernmentType.Presidential => 9,
            ConstitutionSystem.GovernmentType.SemiPresidential => 11,
            ConstitutionSystem.GovernmentType.Monarchy => 7,
            ConstitutionSystem.GovernmentType.OneParty => 5,
            _ => 9
        };
    }

    private static int GetTermLengthForGovType(ConstitutionSystem.GovernmentType type)
    {
        return type switch
        {
            ConstitutionSystem.GovernmentType.Parliamentary => 9,
            ConstitutionSystem.GovernmentType.Presidential => 12,
            ConstitutionSystem.GovernmentType.SemiPresidential => 9,
            ConstitutionSystem.GovernmentType.Monarchy => 0, // Yaşam boyu
            ConstitutionSystem.GovernmentType.OneParty => 5,
            _ => 9
        };
    }

    private static string GetAppointmentMethodForGovType(ConstitutionSystem.GovernmentType type)
    {
        return type switch
        {
            ConstitutionSystem.GovernmentType.Parliamentary => "ExecutiveLegislative",
            ConstitutionSystem.GovernmentType.Presidential => "ExecutiveLegislative",
            ConstitutionSystem.GovernmentType.SemiPresidential => "ExecutiveLegislative",
            ConstitutionSystem.GovernmentType.Monarchy => "ExecutiveOnly",
            ConstitutionSystem.GovernmentType.OneParty => "ExecutiveOnly",
            _ => "ExecutiveLegislative"
        };
    }

    private static int GetConfirmThresholdForGovType(ConstitutionSystem.GovernmentType type)
    {
        return type switch
        {
            ConstitutionSystem.GovernmentType.Parliamentary => 60,
            ConstitutionSystem.GovernmentType.Presidential => 51,
            ConstitutionSystem.GovernmentType.SemiPresidential => 55,
            ConstitutionSystem.GovernmentType.Monarchy => 0,
            ConstitutionSystem.GovernmentType.OneParty => 0,
            _ => 51
        };
    }

    private static void GenerateJudges(CountryJudiciaryState state)
    {
        var config = state.CourtConfig;
        var countryId = state.CountryId;
        var culture = LeaderSystem.GetCultureKey(countryId);
        var pool = LeaderSystem.GetNamePool(culture);

        // Anayasa Mahkemesi hakimleri
        for (int i = 0; i < config.JudgeCount; i++)
        {
            var judge = new Judge
            {
                Id = $"judge_{_nextJudgeNum++}",
                Name = GenerateJudgeName(pool),
                Age = DeterministicRNG.NextInt(45, 70),
                Ideology = GenerateIdeologyForGovType(ConstitutionSystem.GetConstitution(countryId).GovernmentType),
                Competence = DeterministicRNG.NextInt(40, 95),
                Integrity = DeterministicRNG.NextInt(30, 95),
                Independence = DetermineJudgeIndependence(state.IndependenceLevel),
                Experience = DeterministicRNG.NextInt(5, 25),
                Court = CourtType.Constitutional,
                AppointedBy = "Executive", // Başlangıçta atanmış sayılır
                AppointmentDay = -DeterministicRNG.NextInt(0, 365 * 10),
                IsChiefJustice = (i == 0)
            };
            state.Judges.Add(judge);
        }

        // Diğer mahkeme hakimleri (basitçe)
        var otherCourts = new[] { CourtType.Supreme, CourtType.Administrative, CourtType.Civil, CourtType.Criminal };
        foreach (var courtType in otherCourts)
        {
            int count = courtType == CourtType.Supreme ? 7 : 5;
            for (int i = 0; i < count; i++)
            {
                state.Judges.Add(new Judge
                {
                    Id = $"judge_{_nextJudgeNum++}",
                    Name = GenerateJudgeName(pool),
                    Age = DeterministicRNG.NextInt(40, 65),
                    Ideology = GenerateIdeologyForGovType(ConstitutionSystem.GetConstitution(countryId).GovernmentType),
                    Competence = DeterministicRNG.NextInt(30, 90),
                    Integrity = DeterministicRNG.NextInt(30, 90),
                    Independence = DetermineJudgeIndependence(state.IndependenceLevel),
                    Experience = DeterministicRNG.NextInt(3, 20),
                    Court = courtType,
                    AppointedBy = "Executive",
                    AppointmentDay = -DeterministicRNG.NextInt(0, 365 * 5)
                });
            }
        }

        GD.Print($"[Judiciary] {countryId}: {state.Judges.Count} hakim oluşturuldu (Anayasa: {config.JudgeCount}, Diğer: {state.Judges.Count - config.JudgeCount})");
    }

    private static int DetermineJudgeIndependence(JudicialIndependenceLevel countryLevel)
    {
        // Ülke seviyesine göre hakim bağımsızlığı dağılımı
        int baseIndep = (int)countryLevel * 20; // 0, 20, 40, 60, 80
        int variance = DeterministicRNG.NextInt(-15, 16);
        return Math.Clamp(baseIndep + variance, 0, 100);
    }

    private static string GenerateIdeologyForGovType(ConstitutionSystem.GovernmentType govType)
    {
        return govType switch
        {
            ConstitutionSystem.GovernmentType.Parliamentary => "Centrist",
            ConstitutionSystem.GovernmentType.Presidential => "Centrist",
            ConstitutionSystem.GovernmentType.SemiPresidential => "Centrist",
            ConstitutionSystem.GovernmentType.Monarchy => "Conservative",
            ConstitutionSystem.GovernmentType.OneParty => "Authoritarian",
            _ => "Centrist"
        };
    }

    private static string GenerateJudgeName(LeaderSystem.NamePool pool)
    {
        if (pool == null || pool.FirstNames.Count == 0 || pool.LastNames.Count == 0)
            return "Judge " + _nextJudgeNum;

        return $"{DeterministicRNG.NextChoice(pool.FirstNames)} {DeterministicRNG.NextChoice(pool.LastNames)}";
    }

    private static void ProcessJudgeRetirements(CountryJudiciaryState state, long currentDay)
    {
        var config = state.CourtConfig;
        var retired = new List<Judge>();

        foreach (var judge in state.Judges)
        {
            if (judge.Age >= config.RetirementAge)
            {
                retired.Add(judge);
            }
        }

        foreach (var judge in retired)
        {
            state.Judges.Remove(judge);
            GD.Print($"[Judiciary] {state.CountryId}: Hakim emekli oldu — {judge.Name} (yaş {judge.Age}, {judge.Experience} yıl deneyim)");
        }
    }

    private static void ProcessVacancies(CountryJudiciaryState state, long currentDay)
    {
        var config = state.CourtConfig;
        int currentConstitutional = state.ConstitutionalCourtJudges;

        if (currentConstitutional < config.JudgeCount)
        {
            int needed = config.JudgeCount - currentConstitutional;
            var pool = LeaderSystem.GetNamePool(LeaderSystem.GetCultureKey(state.CountryId));
            var govType = ConstitutionSystem.GetConstitution(state.CountryId).GovernmentType;

            for (int i = 0; i < needed; i++)
            {
                var judge = new Judge
                {
                    Id = $"judge_{_nextJudgeNum++}",
                    Name = GenerateJudgeName(pool),
                    Age = DeterministicRNG.NextInt(45, 65),
                    Ideology = GenerateIdeologyForGovType(ConstitutionSystem.GetConstitution(state.CountryId).GovernmentType),
                    Competence = DeterministicRNG.NextInt(40, 95),
                    Integrity = DeterministicRNG.NextInt(30, 95),
                    Independence = DetermineJudgeIndependence(state.IndependenceLevel),
                    Experience = DeterministicRNG.NextInt(5, 15),
                    Court = CourtType.Constitutional,
                    AppointedBy = "Executive",
                    AppointmentDay = currentDay,
                    IsChiefJustice = false
                };

                // Atama onayı simülasyonu
                bool confirmed = SimulateConfirmation(state, judge);
                if (confirmed)
                {
                    state.Judges.Add(judge);
                    GD.Print($"[Judiciary] {state.CountryId}: Yeni hakim atandı — {judge.Name} (bağımsızlık: {judge.Independence})");
                }
                else
                {
                    GD.Print($"[Judiciary] {state.CountryId}: Hakim ataması reddedildi — {judge.Name}");
                }
            }
        }
    }

    private static bool SimulateConfirmation(CountryJudiciaryState state, Judge judge)
    {
        var config = state.CourtConfig;

        if (config.AppointmentMethod == "ExecutiveOnly")
            return true; // Yürütme organı tek başına atar

        // Yasama onayı simülasyonu
        double confirmationChance = 0.7; // Temel onay şansı

        // Hakim yeterliliği ve bütünlüğü etkiler
        confirmationChance += (judge.Competence - 50) * 0.004; // ±0.2
        confirmationChance += (judge.Integrity - 50) * 0.003;   // ±0.15

        // Yargı bağımsızlığı etkisi
        int indepLevel = (int)state.IndependenceLevel;
        if (indepLevel >= 3) confirmationChance += 0.1; // Bağımsız yargıda onay kolaylaşır
        else if (indepLevel <= 1) confirmationChance -= 0.1; // Kontrollü yargıda onay zorlaşır

        // Ideolojik uyum (basit)
        // Gerçekte partili hakim ataması partiliyasama onayını etkiler

        return DeterministicRNG.NextDouble() < Math.Clamp(confirmationChance, 0.1, 0.95);
    }

    private static void ProcessCases(CountryJudiciaryState state, long currentDay)
    {
        foreach (var caze in state.Cases.Where(c => c.Status != CaseStatus.Decided && c.Status != CaseStatus.Enforced).ToList())
        {
            int daysSinceFiling = (int)(currentDay - caze.FilingDay);

            // Dava aşamaları
            switch (caze.Status)
            {
                case CaseStatus.Filed:
                    // İnceleme süresi (30-90 gün)
                    if (daysSinceFiling >= 30 + DeterministicRNG.NextInt(0, 60))
                    {
                        caze.Status = CaseStatus.UnderReview;
                        // Hakimler atanır
                        AssignJudgesToCase(state, caze);
                    }
                    break;

                case CaseStatus.UnderReview:
                    // İnceleme ve durağa gitme (60-180 gün)
                    if (daysSinceFiling >= 90 + DeterministicRNG.NextInt(0, 180))
                    {
                        caze.Status = CaseStatus.HearingScheduled;
                    }
                    break;

                case CaseStatus.HearingScheduled:
                    // Durağa gelme ve karar (30-60 gün sonra)
                    if (daysSinceFiling >= 120 + DeterministicRNG.NextInt(0, 120))
                    {
                        DecideCase(state, caze, currentDay);
                    }
                    break;
            }
        }
    }

    private static void AssignJudgesToCase(CountryJudiciaryState state, Case caze)
    {
        var constJudges = state.Judges.Where(j => j.Court == CourtType.Constitutional).ToList();
        if (constJudges.Count < 3) return;

        // Rastgele 3-5 hakim seç (DeterministicRNG ile)
        var selected = constJudges.OrderBy(j => DeterministicRNG.NextInt(0, int.MaxValue)).Take(DeterministicRNG.NextInt(3, 6)).ToList();
        caze.JudgeIds = selected.Select(j => j.Id).ToList();

        // Hakimlerin dava sayısını artır
        foreach (var judge in selected)
            judge.CasesDecided++;
    }

    private static void DecideCase(CountryJudiciaryState state, Case caze, long currentDay)
    {
        caze.Status = CaseStatus.Decided;
        caze.DecisionDay = currentDay;

        // Karar mantığı
        double biasFactor = GetJudicialBiasFactor(state.CountryId, favorGovernment: true);

        // Dava türüne göre temel karar olasılığı
        double baseStrikeDownChance = caze.Type switch
        {
            CaseType.ConstitutionalReview => 0.3,      // Yasa iptali zor
            CaseType.IndividualApplication => 0.4,     // Bireysel başvuru orta
            CaseType.ElectionDispute => 0.2,           // Seçim itirazı zor
            CaseType.PartyClosure => 0.1,              // Parti kapatma çok zor
            CaseType.Impeachment => 0.15,              // Yargılama zor
            _ => 0.3
        };

        // Yargı bağımsızlığı etkisi
        double independenceMultiplier = GetJudicialBiasFactor(state.CountryId, favorGovernment: false);
        double finalChance = baseStrikeDownChance * independenceMultiplier;

        // Hakim kompozisyonu etkisi
        var judges = state.Judges.Where(j => caze.JudgeIds.Contains(j.Id)).ToList();
        if (judges.Count > 0)
        {
            double avgIndependence = judges.Average(j => j.Independence) / 100.0;
            double avgIntegrity = judges.Average(j => j.Integrity) / 100.0;
            double avgCompetence = judges.Average(j => j.Competence) / 100.0;

            finalChance *= (0.7 + avgIndependence * 0.3) * (0.8 + avgIntegrity * 0.2) * (0.9 + avgCompetence * 0.1);
        }

        // Yargıtay önceleri (presedens) etkisi
        var relevantPrecedents = state.Precedents.Where(p => p.CaseType == caze.Type && p.BindingStrength > 50).ToList();
        if (relevantPrecedents.Count > 0)
        {
            double precedentFactor = relevantPrecedents.Average(p => p.BindingStrength / 100.0);
            finalChance *= (0.8 + precedentFactor * 0.4);
        }

        // Rastgele karar
        double roll = DeterministicRNG.NextDouble();
        CaseOutcome outcome;

        if (roll < finalChance * 0.2)
            outcome = CaseOutcome.StruckDown;           // Tam iptal
        else if (roll < finalChance * 0.5)
            outcome = CaseOutcome.PartiallyStruckDown;  // Kısmi iptal
        else if (roll < finalChance * 0.8)
            outcome = CaseOutcome.Upheld;               // Onaylandı
        else
            outcome = CaseOutcome.Dismissed;            // Reddedildi

        caze.Outcome = outcome;

        // Oy sayısı simülasyonu (hakim sayısına göre)
        int judgeCount = caze.JudgeIds.Count;
        if (judgeCount == 0) judgeCount = 5;

        if (outcome == CaseOutcome.StruckDown || outcome == CaseOutcome.PartiallyStruckDown)
        {
            caze.VoteFor = (int)Math.Ceiling(judgeCount * (0.5 + DeterministicRNG.NextDouble() * 0.3));
            caze.VoteAgainst = judgeCount - caze.VoteFor;
        }
        else
        {
            caze.VoteAgainst = (int)Math.Ceiling(judgeCount * (0.5 + DeterministicRNG.NextDouble() * 0.3));
            caze.VoteFor = judgeCount - caze.VoteAgainst;
        }

        // Gerekçe oluştur
        caze.Reasoning = GenerateReasoning(caze, state);

        // Sonuçları uygula
        ApplyCaseOutcome(state, caze, currentDay);

        GD.Print($"[Judiciary] {state.CountryId}: Dava kararlandı — {caze.Id} [{caze.Type}] -> {caze.Outcome} ({caze.VoteFor}-{caze.VoteAgainst}) (gün {currentDay})");
    }

    private static string GenerateReasoning(Case caze, CountryJudiciaryState state)
    {
        var templates = new Dictionary<CaseOutcome, string[]>
        {
            [CaseOutcome.StruckDown] = new[]
            {
                "Mahkeme, yasanın anayasanın {0} maddesine aykırı olduğunu buldu. Temel haklar ihlali ortadadır.",
                "Yasa, meşruiyet ilkesi ve hukuk devleti garantisi çerçevesinde anayasaya aykırı olarak değerlendirildi.",
                "Yasama organı yetki alanını aşarak anayasanın temel ilkelerine aykırı düzenleme yapmıştır."
            },
            [CaseOutcome.PartiallyStruckDown] = new[]
            {
                "Mahkeme, yasanın belli hükümlerinin anayasaya aykırı, diğerlerinin ise anayasaya uygun olduğunu buldu.",
                "Yasanın belli kısımları orantılık ilkesine aykırı olarak iptal edilmiştir.",
                "Bazı maddeler anayasal garanti kapsamında kalarak diğerleri iptal edilmiştir."
            },
            [CaseOutcome.Upheld] = new[]
            {
                "Mahkeme, yasanın anayasa ile uyumlu, gerekli ve orantılı bir düzenleme olduğunu buldu.",
                "Yasa, yasama organının takdir yetkisi içinde kalmakta ve anayasaya aykırı değildir.",
                "İddia edilen anayasa aykırılığı delillendirilememiş, yasa anayasaya uygun bulunmuştur."
            },
            [CaseOutcome.Dismissed] = new[]
            {
                "Dava, şekil şartları eksikliği veya menfaat sahipliği olmaması nedeniyle reddedilmiştir.",
                "İddialar delillendirilememiş, dava konusu anayasa mahkemesinin denetim yetkisi dışında kalmıştır.",
                "Başvurunun esası yok, anayasa mahkemesi yetkisi bulunmamıştır."
            }
        };

        if (!templates.TryGetValue(caze.Outcome, out var ts) || ts.Length == 0)
            return "Karar gerekçesi hazırlanıyor.";

        string template = DeterministicRNG.NextChoice(ts);
        return string.Format(template, "temel haklar");
    }

    private static void ApplyCaseOutcome(CountryJudiciaryState state, Case caze, long currentDay)
    {
        switch (caze.Outcome)
        {
            case CaseOutcome.StruckDown:
                // Yasa tamamen iptal
                if (!string.IsNullOrEmpty(caze.TargetLawId))
                {
                    LawSystem.RepealLaw(state.CountryId, caze.TargetLawId);
                    GD.Print($"[Judiciary] {state.CountryId}: Yasa iptal edildi — {caze.TargetLawId}");

                    // Presedens oluştur
                    CreatePrecedent(state, caze, "Yasa anayasaya aykırı olarak tamamen iptal edildi.");
                }
                break;

            case CaseOutcome.PartiallyStruckDown:
                // Yasa kısmen iptal (etkileri azalt)
                if (!string.IsNullOrEmpty(caze.TargetLawId))
                {
                    var law = LawSystem.GetLaw(caze.TargetLawId);
                    if (law != null)
                    {
                        // Etkileri %50 azalt
                        var active = LawSystem.GetActiveLaws(state.CountryId);
                        var targetLaw = active.FirstOrDefault(l => l.Id == caze.TargetLawId);
                        if (targetLaw != null)
                        {
                            // Etkileri yarıya indir (basitçe tekrar uygula/azalt)
                            LawSystem.RepealLaw(state.CountryId, caze.TargetLawId);
                            // Not: Gerçekte kısmi iptal daha karmaşık, şimdilik tam iptal gibi davranıyoruz
                        }
                    }

                    CreatePrecedent(state, caze, "Yasa kısmen anayasaya aykırı olarak iptal edildi.");
                }
                break;

            case CaseOutcome.Upheld:
                // Yasa geçerli kaldi, presedens oluştur
                CreatePrecedent(state, caze, "Yasa anayasaya uygun olarak onaylandı.");
                break;

            case CaseOutcome.Dismissed:
                // Dava reddedildi, presedens oluşturulmaz (veya zayıf presedens)
                if (DeterministicRNG.NextDouble() < 0.2)
                {
                    CreatePrecedent(state, caze, "Dava şekil şartları eksikliği nedeniyle reddedildi.", 30);
                }
                break;
        }

        caze.Status = CaseStatus.Enforced;
    }

    private static void CreatePrecedent(CountryJudiciaryState state, Case caze, string principle, int strength = 100)
    {
        var precedent = new Precedent
        {
            Id = $"prec_{_nextPrecedentNum++}",
            CountryId = state.CountryId,
            CaseType = caze.Type,
            Principle = principle,
            Summary = $"[{caze.Type}] {caze.Outcome}: {principle}",
            EstablishedDay = caze.DecisionDay,
            TimesCited = 0,
            BindingStrength = strength,
            RelatedLawIds = string.IsNullOrEmpty(caze.TargetLawId) ? new List<string>() : new List<string> { caze.TargetLawId }
        };

        state.Precedents.Add(precedent);
        caze.CreatedPrecedent = true;
        caze.PrecedentId = precedent.Id;

        GD.Print($"[Judiciary] {state.CountryId}: Presedens oluşturuldu — {precedent.Id} (güç: {strength})");
    }

    private static void DecayPrecedents(CountryJudiciaryState state)
    {
        foreach (var prec in state.Precedents)
        {
            // Yılın her başında bağlayıcılık %5 azalır (min 20)
            prec.BindingStrength = Math.Max(20, prec.BindingStrength - 5);
        }

        // Çok zayıf presedensleri temizle
        state.Precedents.RemoveAll(p => p.BindingStrength <= 20 && p.TimesCited == 0);
    }

    private static void LoadConfig()
    {
        // judiciary.json dosyası varsa yükle, yoksa varsayılanları kullan
        if (Godot.FileAccess.FileExists(ConfigPath))
        {
            try
            {
                string json = Godot.FileAccess.GetFileAsString(ConfigPath);
                // Config parse edilebilir, şimdilik varsayılanlar kullanılıyor
                GD.Print($"[JudiciarySystem] Yapılandırma dosyası bulundu: {ConfigPath}");
            }
            catch (Exception e)
            {
                GD.PrintErr($"[JudiciarySystem] Config yükleme hatası: {e.Message}");
            }
        }
    }

    // ===== Save / Load =====

    public class JudgeSave
    {
        public string Id = "";
        public string Name = "";
        public int Age = 0;
        public string Ideology = "";
        public int Competence = 0;
        public int Integrity = 0;
        public int Independence = 0;
        public int Experience = 0;
        public int Court = 0;
        public string AppointedBy = "";
        public long AppointmentDay = 0;
        public bool IsChiefJustice = false;
        public int CasesDecided = 0;
        public int PrecedentsAuthored = 0;
    }

    public class CaseSave
    {
        public string Id = "";
        public int Type = 0;
        public string CountryId = "";
        public string ChallengerId = "";
        public string TargetLawId = "";
        public string TargetActionId = "";
        public long FilingDay = 0;
        public int Status = 0;
        public int Outcome = 0;
        public long DecisionDay = 0;
        public string Reasoning = "";
        public string PrecedentId = "";
        public string[] JudgeIds = Array.Empty<string>();
        public int VoteFor = 0;
        public int VoteAgainst = 0;
        public bool CreatedPrecedent = false;
    }

    public class PrecedentSave
    {
        public string Id = "";
        public string CountryId = "";
        public int CaseType = 0;
        public string Principle = "";
        public string Summary = "";
        public long EstablishedDay = 0;
        public int TimesCited = 0;
        public int BindingStrength = 0;
        public string[] RelatedLawIds = Array.Empty<string>();
    }

    public class CourtConfigSave
    {
        public string CountryId = "";
        public int JudgeCount = 0;
        public int TermLengthYears = 0;
        public bool LifeTenure = false;
        public int RetirementAge = 0;
        public string AppointmentMethod = "";
        public int LegislativeConfirmThreshold = 0;
        public int IndependenceLevel = 0;
    }

    public class CountryJudiciarySave
    {
        public string CountryId = "";
        public CourtConfigSave CourtConfig = new();
        public int IndependenceLevel = 0;
        public JudgeSave[] Judges = Array.Empty<JudgeSave>();
        public CaseSave[] Cases = Array.Empty<CaseSave>();
        public PrecedentSave[] Precedents = Array.Empty<PrecedentSave>();
    }

    public class JudiciarySave
    {
        public CountryJudiciarySave[] States = Array.Empty<CountryJudiciarySave>();
        public int NextCaseNum = 1;
        public int NextPrecedentNum = 1;
        public int NextJudgeNum = 1;
    }

    public static JudiciarySave GetSaveData()
    {
        return new JudiciarySave
        {
            States = _states.Select(kv => new CountryJudiciarySave
            {
                CountryId = kv.Key,
                CourtConfig = new CourtConfigSave
                {
                    CountryId = kv.Value.CourtConfig.CountryId,
                    JudgeCount = kv.Value.CourtConfig.JudgeCount,
                    TermLengthYears = kv.Value.CourtConfig.TermLengthYears,
                    LifeTenure = kv.Value.CourtConfig.LifeTenure,
                    RetirementAge = kv.Value.CourtConfig.RetirementAge,
                    AppointmentMethod = kv.Value.CourtConfig.AppointmentMethod,
                    LegislativeConfirmThreshold = kv.Value.CourtConfig.LegislativeConfirmThreshold,
                    IndependenceLevel = (int)kv.Value.CourtConfig.IndependenceLevel
                },
                IndependenceLevel = (int)kv.Value.IndependenceLevel,
                Judges = kv.Value.Judges.Select(j => new JudgeSave
                {
                    Id = j.Id,
                    Name = j.Name,
                    Age = j.Age,
                    Ideology = j.Ideology,
                    Competence = j.Competence,
                    Integrity = j.Integrity,
                    Independence = j.Independence,
                    Experience = j.Experience,
                    Court = (int)j.Court,
                    AppointedBy = j.AppointedBy,
                    AppointmentDay = j.AppointmentDay,
                    IsChiefJustice = j.IsChiefJustice,
                    CasesDecided = j.CasesDecided,
                    PrecedentsAuthored = j.PrecedentsAuthored
                }).ToArray(),
                Cases = kv.Value.Cases.Select(c => new CaseSave
                {
                    Id = c.Id,
                    Type = (int)c.Type,
                    CountryId = c.CountryId,
                    ChallengerId = c.ChallengerId,
                    TargetLawId = c.TargetLawId,
                    TargetActionId = c.TargetActionId,
                    FilingDay = c.FilingDay,
                    Status = (int)c.Status,
                    Outcome = (int)c.Outcome,
                    DecisionDay = c.DecisionDay,
                    Reasoning = c.Reasoning,
                    PrecedentId = c.PrecedentId,
                    JudgeIds = c.JudgeIds.ToArray(),
                    VoteFor = c.VoteFor,
                    VoteAgainst = c.VoteAgainst,
                    CreatedPrecedent = c.CreatedPrecedent
                }).ToArray(),
                Precedents = kv.Value.Precedents.Select(p => new PrecedentSave
                {
                    Id = p.Id,
                    CountryId = p.CountryId,
                    CaseType = (int)p.CaseType,
                    Principle = p.Principle,
                    Summary = p.Summary,
                    EstablishedDay = p.EstablishedDay,
                    TimesCited = p.TimesCited,
                    BindingStrength = p.BindingStrength,
                    RelatedLawIds = p.RelatedLawIds.ToArray()
                }).ToArray()
            }).ToArray(),
            NextCaseNum = _nextCaseNum,
            NextPrecedentNum = _nextPrecedentNum,
            NextJudgeNum = _nextJudgeNum
        };
    }

    public static void LoadSaveData(JudiciarySave data)
    {
        _states.Clear();
        _nextCaseNum = data.NextCaseNum > 0 ? data.NextCaseNum : 1;
        _nextPrecedentNum = data.NextPrecedentNum > 0 ? data.NextPrecedentNum : 1;
        _nextJudgeNum = data.NextJudgeNum > 0 ? data.NextJudgeNum : 1;

        if (data?.States == null)
        {
            InitializeStates();
            return;
        }

        foreach (var s in data.States)
        {
            var state = new CountryJudiciaryState
            {
                CountryId = s.CountryId,
                CourtConfig = new ConstitutionalCourtConfig
                {
                    CountryId = s.CourtConfig.CountryId,
                    JudgeCount = s.CourtConfig.JudgeCount,
                    TermLengthYears = s.CourtConfig.TermLengthYears,
                    LifeTenure = s.CourtConfig.LifeTenure,
                    RetirementAge = s.CourtConfig.RetirementAge,
                    AppointmentMethod = s.CourtConfig.AppointmentMethod,
                    LegislativeConfirmThreshold = s.CourtConfig.LegislativeConfirmThreshold,
                    IndependenceLevel = (JudicialIndependenceLevel)s.CourtConfig.IndependenceLevel
                },
                IndependenceLevel = (JudicialIndependenceLevel)s.IndependenceLevel
            };

            if (s.Judges != null)
            {
                state.Judges = s.Judges.Select(j => new Judge
                {
                    Id = j.Id,
                    Name = j.Name,
                    Age = j.Age,
                    Ideology = j.Ideology,
                    Competence = j.Competence,
                    Integrity = j.Integrity,
                    Independence = j.Independence,
                    Experience = j.Experience,
                    Court = (CourtType)j.Court,
                    AppointedBy = j.AppointedBy,
                    AppointmentDay = j.AppointmentDay,
                    IsChiefJustice = j.IsChiefJustice,
                    CasesDecided = j.CasesDecided,
                    PrecedentsAuthored = j.PrecedentsAuthored
                }).ToList();
            }

            if (s.Cases != null)
            {
                state.Cases = s.Cases.Select(c => new Case
                {
                    Id = c.Id,
                    Type = (CaseType)c.Type,
                    CountryId = c.CountryId,
                    ChallengerId = c.ChallengerId,
                    TargetLawId = c.TargetLawId,
                    TargetActionId = c.TargetActionId,
                    FilingDay = c.FilingDay,
                    Status = (CaseStatus)c.Status,
                    Outcome = (CaseOutcome)c.Outcome,
                    DecisionDay = c.DecisionDay,
                    Reasoning = c.Reasoning,
                    PrecedentId = c.PrecedentId,
                    JudgeIds = c.JudgeIds?.ToList() ?? new List<string>(),
                    VoteFor = c.VoteFor,
                    VoteAgainst = c.VoteAgainst,
                    CreatedPrecedent = c.CreatedPrecedent
                }).ToList();
            }

            if (s.Precedents != null)
            {
                state.Precedents = s.Precedents.Select(p => new Precedent
                {
                    Id = p.Id,
                    CountryId = p.CountryId,
                    CaseType = (CaseType)p.CaseType,
                    Principle = p.Principle,
                    Summary = p.Summary,
                    EstablishedDay = p.EstablishedDay,
                    TimesCited = p.TimesCited,
                    BindingStrength = p.BindingStrength,
                    RelatedLawIds = p.RelatedLawIds?.ToList() ?? new List<string>()
                }).ToList();
            }

            _states[s.CountryId] = state;
        }

        _initialized = true;
        GD.Print($"[JudiciarySystem] State yüklendi — {_states.Count} ülke, dava={_states.Sum(s => s.Value.Cases.Count)}, presedens={_states.Sum(s => s.Value.Precedents.Count)}");
    }
}