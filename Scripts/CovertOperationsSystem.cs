using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Covert Operations & Leaks System (Gizli Operasyonlar ve Sızıntılar).
/// Özellikler:
/// - Gelişmiş gizli operasyonlar (devlet başkanlığı değiştirme, siber saldırı, suikast, rejim değişikliği)
/// - WikiLeaks tarzı toplu belge sızıntıları (mass leaks)
/// - İçerden sızdıranlar (whistleblowers)
/// - Sızıntı etkileri: diplomatik kriz, istikrar kaybı, savaş riski, meşruiyet kaybı
/// - Sızdırılan belgelerin kategorileri: askerî, diplomatik, ekonomik, istihbarat, nukleer
/// - Belge hassasiyeti: düşük/orta/yüksek/ayrıcalıklı
/// Tüm veri deterministiktir (DeterministicRNG), Save/Load desteklidir.
/// </summary>
public static class CovertOperationsSystem
{
    // ===== Veri Modelleri =====

    /// <summary>Gizli operasyon türleri.</summary>
    public enum CovertOpType
    {
        None = 0,
        CoupAttempt,          // Darbe girişimi (rejim değişikliği)
        Assassination,        // Suikast (lider/bakan hedefi)
        CyberAttack,          // Siber saldırı (altyapı, finans, askeri)
        RegimeChange,         // Rejim değişikliği (desteklenen muhalefet)
        ElectionInterference, // Seçim müdahalesi
        FalseFlag,            // Kibrisiz bayrak operasyonu (saldırıyı başkasına yıkama)
        EconomicSabotage,     // Ekonomik sabotaj (para birimi, borsa, enerji)
        DisinformationCampaign, // Kapsamlı dezinformasyon kampanyası
        NuclearSabotage,      // Nükleer tesis sabotajı
        BioweaponRelease,     // Biyolojik silah salınımı (riskli)
    }

    /// <summary>Sızıntı (leak) türleri.</summary>
    public enum LeakType
    {
        None = 0,
        DiplomaticCables,     // Diplomatik telgraflar (WikiLeaks Cablegate tarzı)
        MilitaryDocuments,    // Askeri belgeler (operasyon planları, kıyafetler)
        IntelligenceReports,  // İstihbarat raporları (casus listeleri, kaynaklar)
        EconomicData,         // Ekonomik veriler (bütçe, gizli anlaşmalar, rüşvetler)
        NuclearSecrets,       // Nükleer sırlar (zenginleştirme, tasarım)
        SurveillancePrograms, // Gözetim programları (vatandaş/leadership takibi)
        CorporateCorruption,  // Şirket yolsuzlukları (askeri-sanayi kompleksi)
        HumanRightsAbuses,    // İnsan hakları ihlalleri (gizli tutuklamalar, işkence)
        ElectionFraud,        // Seçim hilesi kanıtları
        CovertOperationsExposed, // Gizli operasyonların açığa çıkması
    }

    /// <summary>Belge hassasiyeti seviyeleri.</summary>
    public enum ClassificationLevel
    {
        Unclassified = 0,     // Kamu malı
        Confidential = 1,     // Gizli (düşük)
        Secret = 2,           // Çok gizli (orta)
        TopSecret = 3,        // En gizli (yüksek)
        SCI = 4,              // Sensitive Compartmented Information (ayrıcalıklı)
    }

    /// <summary>Sızdıran (whistleblower/leaker) profili.</summary>
    public class Leaker
    {
        public string Id { get; set; }
        public string Name { get; set; }               // Kod adı (örn. "SNOWDEN-7")
        public string RealCountryId { get; set; }      // Gerçek ülke (gizli)
        public string CoverIdentity { get; set; }      // Kapak kimliği (diplomat, asker, teknisyen)
        public string Motivation { get; set; }         // Motivasyon: ideology, conscience, money, revenge, coercion
        public int AccessLevel { get; set; }           // Erişim seviyesi (1-5, SCI'ye kadar)
        public int Credibility { get; set; }           // Güvenilirlik (0-100)
        public bool IsActive { get; set; }             // Hala aktif mi
        public int DocumentsLeaked { get; set; }       // Sızdırılan belge sayısı
        public int RecruitmentDay { get; set; }        // Tutunma günü
        public string HandlerCountryId { get; set; }   // Yöneten ülke (null = bağımsız)
    }

    /// <summary>Sızıntı (leak) olayı.</summary>
    public class LeakEvent
    {
        public string Id { get; set; }
        public LeakType Type { get; set; }
        public ClassificationLevel Classification { get; set; }
        public string SourceCountryId { get; set; }    // Sızdırılan ülke
        public string LeakerId { get; set; }           // Sızdıran (bilinmiyorsa "UNKNOWN")
        public string PublisherId { get; set; }        // Yayınlayan (gazete, WikiLeaks benzeri platform)
        public int Day { get; set; }
        public int DocumentCount { get; set; }
        public int Severity { get; set; }              // 1-100 (etki şiddeti)
        public bool IsPublished { get; set; }
        public string Summary { get; set; }
        public Dictionary<string, int> ImpactByCountry = new(); // ülke -> etki puanı
    }

    /// <summary>Gizli operasyon.</summary>
    public class CovertOperation
    {
        public string Id { get; set; }
        public CovertOpType Type { get; set; }
        public string SponsorCountryId { get; set; }   // Operasyonu finanse eden ülke
        public string TargetCountryId { get; set; }    // Hedef ülke
        public string SpyId { get; set; }              // Atanan casus (opsiyonel)
        public int StartDay { get; set; }
        public int RequiredDays { get; set; }
        public int ProgressDays { get; set; }
        public double SuccessChance { get; set; }      // 0-1
        public bool IsCompleted { get; set; }
        public bool IsExposed { get; set; }            // Açığa çıktı mı
        public bool IsCancelled { get; set; }
        public string EffectSummary { get; set; }
        public List<string> GeneratedLeaks = new();    // Oluşturulan leak ID'leri
    }

    /// <summary>Yayıncı/platform (WikiLeaks benzeri).</summary>
    public class Publisher
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string BaseCountryId { get; set; }      // Merkez ülke
        public int Reputation { get; set; }            // İtibar (0-100)
        public int Reach { get; set; }                 // Erişim (0-100, küresel yayın gücü)
        public bool IsStateAffiliated { get; set; }    // Devlet bağlantılı mı
        public List<string> PublishedLeakIds = new();  // Yayınladığı leak'ler
    }

    // ===== Sabitler =====
    private const string DataPath = "res://Data/covert_operations.json";
    private const string LeaksDataPath = "res://Data/leaks.json";

    // ===== State =====
    private static readonly List<CovertOperation> _operations = new();
    private static readonly List<LeakEvent> _leaks = new();
    private static readonly List<Leaker> _leakers = new();
    private static readonly List<Publisher> _publishers = new();
    private static int _nextOpNum = 1;
    private static int _nextLeakNum = 1;
    private static int _nextLeakerNum = 1;
    private static bool _initialized;

    // ===== Public API =====

    public static int OperationCount => _operations.Count;
    public static int LeakCount => _leaks.Count;
    public static int LeakerCount => _leakers.Count;
    public static int PublisherCount => _publishers.Count;
    public static IReadOnlyList<CovertOperation> Operations => _operations.AsReadOnly();
    public static IReadOnlyList<LeakEvent> Leaks => _leaks.AsReadOnly();
    public static IReadOnlyList<Leaker> Leakers => _leakers.AsReadOnly();
    public static IReadOnlyList<Publisher> Publishers => _publishers.AsReadOnly();

    /// <summary>Sistemi başlatır (veri dosyalarını yükler, yayıncıları oluşturur).</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        LoadPublishers();
        LoadLeakDefinitions();

        GD.Print($"[CovertOperationsSystem] Initialized — publishers={_publishers.Count}, leak defs loaded");
    }

    /// <summary>Yeni gizli operasyon başlatır.</summary>
    public static CovertOperation StartOperation(CovertOpType type, string sponsorId, string targetId, string spyId = null, int currentDay = 0)
    {
        if (type == CovertOpType.None || string.IsNullOrEmpty(sponsorId) || string.IsNullOrEmpty(targetId))
            return null;

        if (sponsorId == targetId)
        {
            GD.PrintErr($"[CovertOps] Kendine operasyon başlatılamaz: {sponsorId}");
            return null;
        }

        var sponsor = CountryDatabase.Get(sponsorId);
        var target = CountryDatabase.Get(targetId);
        if (sponsor == null || target == null) return null;

        // PP maliyeti kontrolü
        int cost = GetOperationCost(type);
        if (sponsor.PoliticalPower < cost)
        {
            GD.PrintErr($"[CovertOps] PP yetersiz: {sponsorId} ({sponsor.PoliticalPower:F0}/{cost})");
            return null;
        }

        sponsor.PoliticalPower -= cost;

        // Başarı şansı hesapla
        double successChance = CalculateSuccessChance(type, sponsorId, targetId, spyId);

        // Süre hesapla
        int duration = GetOperationDuration(type, sponsorId, targetId);

        var op = new CovertOperation
        {
            Id = $"cop_{_nextOpNum++}",
            Type = type,
            SponsorCountryId = sponsorId,
            TargetCountryId = targetId,
            SpyId = spyId,
            StartDay = currentDay,
            RequiredDays = duration,
            ProgressDays = 0,
            SuccessChance = successChance,
            IsCompleted = false,
            IsExposed = false,
            IsCancelled = false,
            EffectSummary = ""
        };

        _operations.Add(op);

        // Eğer casus atandıysa, casusu meşgul et
        if (!string.IsNullOrEmpty(spyId))
        {
            EspionageSystem.AssignSpyToMission(spyId, targetId, EspionageSystem.MissionType.Intelligence, currentDay);
            // Not: Aslında özel bir mission type olmalı ama mevcut sistemde Intelligence kullanıyoruz
        }

        GD.Print($"[CovertOps] OPERASYON BAŞLATILDI: {op.Id} [{type}] {sponsorId} -> {targetId} " +
                 $"süre={duration}gün başarı={successChance:P0} maliyet={cost} PP (gün {currentDay})");

        return op;
    }

    /// <summary>Operasyonu günlük ilerletir.</summary>
    public static void AdvanceOperation(string opId, int currentDay)
    {
        var op = _operations.FirstOrDefault(o => o.Id == opId);
        if (op == null || op.IsCompleted || op.IsCancelled) return;

        op.ProgressDays++;

        // Her gün açığa çıkma riski (düşük)
        if (!op.IsExposed)
        {
            double exposureRisk = GetDailyExposureRisk(op);
            if (DeterministicRNG.NextDouble() < exposureRisk)
            {
                op.IsExposed = true;
                GD.Print($"[CovertOps] OPERASYON AÇIĞA ÇIKTI: {op.Id} [{op.Type}] {op.SponsorCountryId} -> {op.TargetCountryId} (gün {currentDay})");

                // Açığa çıkma sonucu leak oluştur
                CreateExposureLeak(op, currentDay);

                // Diplomat sonuçları
                DiplomacySystem.ModifyRelation(op.SponsorCountryId, op.TargetCountryId, -25);
                DiplomacySystem.IncreaseTension(10);

                // Hedef ülke casus yakalama şansını artır
                if (!string.IsNullOrEmpty(op.SpyId))
                {
                    EspionageSystem.SetCounterIntelLevel(op.TargetCountryId,
                        Math.Min(100, EspionageSystem.GetCounterIntelLevel(op.TargetCountryId) + 10));
                }
            }
        }

        // Tamamlandı mı?
        if (op.ProgressDays >= op.RequiredDays)
        {
            CompleteOperation(op, currentDay);
        }
    }

    /// <summary>Operasyonu tamamlar ve etkiyi uygular.</summary>
    private static void CompleteOperation(CovertOperation op, int currentDay)
    {
        op.IsCompleted = true;
        string effect = "";

        try
        {
            switch (op.Type)
            {
                case CovertOpType.CoupAttempt:
                    effect = ApplyCoupEffect(op);
                    break;
                case CovertOpType.Assassination:
                    effect = ApplyAssassinationEffect(op);
                    break;
                case CovertOpType.CyberAttack:
                    effect = ApplyCyberAttackEffect(op);
                    break;
                case CovertOpType.RegimeChange:
                    effect = ApplyRegimeChangeEffect(op);
                    break;
                case CovertOpType.ElectionInterference:
                    effect = ApplyElectionInterferenceEffect(op);
                    break;
                case CovertOpType.FalseFlag:
                    effect = ApplyFalseFlagEffect(op);
                    break;
                case CovertOpType.EconomicSabotage:
                    effect = ApplyEconomicSabotageEffect(op);
                    break;
                case CovertOpType.DisinformationCampaign:
                    effect = ApplyDisinformationEffect(op);
                    break;
                case CovertOpType.NuclearSabotage:
                    effect = ApplyNuclearSabotageEffect(op);
                    break;
                case CovertOpType.BioweaponRelease:
                    effect = ApplyBioweaponEffect(op);
                    break;
            }
        }
        catch (Exception e)
        {
            effect = $"Hata: {e.Message}";
            GD.PrintErr($"[CovertOps] Operasyon etki hatası: {e.Message}");
        }

        op.EffectSummary = effect;

        GD.Print($"[CovertOps] OPERASYON TAMAMLANDI: {op.Id} [{op.Type}] {op.SponsorCountryId} -> {op.TargetCountryId} — {effect} (gün {currentDay})");
    }

    /// <summary>İptal edilen operasyon.</summary>
    public static void CancelOperation(string opId)
    {
        var op = _operations.FirstOrDefault(o => o.Id == opId);
        if (op != null && !op.IsCompleted)
        {
            op.IsCancelled = true;
            GD.Print($"[CovertOps] Operasyon iptal edildi: {opId}");
        }
    }

    // ===== Leak (Sızıntı) Sistemi =====

    /// <summary>Yeni bir sızıntı oluşturur (manuel veya operasyon sonucu).</summary>
    public static LeakEvent CreateLeak(LeakType type, ClassificationLevel classification, string sourceCountryId, string leakerId = null, int currentDay = 0)
    {
        if (type == LeakType.None || string.IsNullOrEmpty(sourceCountryId)) return null;

        var leak = new LeakEvent
        {
            Id = $"leak_{_nextLeakNum++}",
            Type = type,
            Classification = classification,
            SourceCountryId = sourceCountryId,
            LeakerId = leakerId ?? "UNKNOWN",
            PublisherId = null, // Henüz yayınlanmadı
            Day = currentDay,
            DocumentCount = DetermineDocumentCount(type, classification),
            Severity = DetermineSeverity(type, classification),
            IsPublished = false,
            Summary = GenerateLeakSummary(type, sourceCountryId)
        };

        _leaks.Add(leak);
        GD.Print($"[CovertOps] SIZINTI OLUŞTURULDU: {leak.Id} [{type}] {sourceCountryId} " +
                 $"hassasiyet={classification} belge={leak.DocumentCount} şiddet={leak.Severity} (gün {currentDay})");

        return leak;
    }

    /// <summary>Operasyon açığa çıkınca otomatik leak oluşturur.</summary>
    private static LeakEvent CreateExposureLeak(CovertOperation op, int currentDay)
    {
        // Operasyon türüne göre leak türü belirle
        LeakType leakType = op.Type switch
        {
            CovertOpType.CoupAttempt => LeakType.DiplomaticCables,
            CovertOpType.Assassination => LeakType.IntelligenceReports,
            CovertOpType.CyberAttack => LeakType.MilitaryDocuments,
            CovertOpType.RegimeChange => LeakType.DiplomaticCables,
            CovertOpType.ElectionInterference => LeakType.ElectionFraud,
            CovertOpType.FalseFlag => LeakType.MilitaryDocuments,
            CovertOpType.EconomicSabotage => LeakType.EconomicData,
            CovertOpType.DisinformationCampaign => LeakType.IntelligenceReports,
            CovertOpType.NuclearSabotage => LeakType.NuclearSecrets,
            CovertOpType.BioweaponRelease => LeakType.MilitaryDocuments,
            _ => LeakType.DiplomaticCables
        };

        ClassificationLevel classification = op.Type switch
        {
            CovertOpType.NuclearSabotage => ClassificationLevel.SCI,
            CovertOpType.CoupAttempt => ClassificationLevel.TopSecret,
            CovertOpType.Assassination => ClassificationLevel.TopSecret,
            CovertOpType.BioweaponRelease => ClassificationLevel.SCI,
            _ => ClassificationLevel.Secret
        };

        var leak = CreateLeak(leakType, classification, op.TargetCountryId, op.SpyId ?? "OPERATIVE", currentDay);
        if (leak != null)
        {
            op.GeneratedLeaks.Add(leak.Id);
            leak.ImpactByCountry[op.SponsorCountryId] = leak.Severity;
            leak.ImpactByCountry[op.TargetCountryId] = leak.Severity * 2; // Hedef daha çok etkilenir
        }
        return leak;
    }

    /// <summary>Sızıntıyı bir yayıncıya satar/verir ve yayınlar.</summary>
    public static bool PublishLeak(string leakId, string publisherId)
    {
        var leak = _leaks.FirstOrDefault(l => l.Id == leakId);
        var publisher = _publishers.FirstOrDefault(p => p.Id == publisherId);
        if (leak == null || publisher == null || leak.IsPublished) return false;

        leak.IsPublished = true;
        leak.PublisherId = publisherId;
        publisher.PublishedLeakIds.Add(leakId);

        // Yayın etkisi: tüm etkilenen ülkelere uygula
        ApplyLeakImpact(leak, publisher);

        GD.Print($"[CovertOps] SIZINTI YAYINLANDI: {leak.Id} [{leak.Type}] -> {publisher.Name} " +
                 $"belge={leak.DocumentCount} şiddet={leak.Severity}");

        return true;
    }

    /// <summary>Sızıntı etkisini uygular (diplomatik, istikrar, meşruiyet).</summary>
    private static void ApplyLeakImpact(LeakEvent leak, Publisher publisher)
    {
        string source = leak.SourceCountryId;
        int classLevel = (int)leak.Classification; // Cast enum to int for calculations

        // Kaynak ülke etkileri
        var sourceData = CountryDatabase.Get(source);
        if (sourceData != null)
        {
            // İstikrar kaybı
            double stabilityLoss = leak.Severity * 0.15 * (classLevel / 4.0 + 0.5);
            sourceData.StabilityPercent = Math.Max(0, sourceData.StabilityPercent - stabilityLoss);

            // Savaş desteği kaybı
            double wsLoss = leak.Severity * 0.1 * (classLevel / 4.0 + 0.5);
            sourceData.WarSupportPercent = Math.Max(0, sourceData.WarSupportPercent - wsLoss);

            // Meşruiyet/itibar kaybı (political unrest)
            PoliticalUnrestSystem.AddLeakPressure(source, (int)(leak.Severity * 0.5));
        }

        // Etkilenen diğer ülkelere yayılma
        foreach (var kv in leak.ImpactByCountry)
        {
            if (kv.Key == source) continue;
            var targetData = CountryDatabase.Get(kv.Key);
            if (targetData != null)
            {
                double impact = kv.Value * 0.1;
                targetData.StabilityPercent = Math.Max(0, targetData.StabilityPercent - impact);
                DiplomacySystem.ModifyRelation(source, kv.Key, (int)(-impact * 0.5));
            }
        }

        // Yayıncının itibarı artar (gazetecilik başarması)
        publisher.Reputation = Math.Min(100, publisher.Reputation + leak.Severity / 10);

        // Dünya gerilimi artar
        DiplomacySystem.IncreaseTension(leak.Severity / 5);

        // Eğer nuclear secrets leak edildiyse nükleer program etkilenir (PoliticalUnrestSystem ile)
        if (leak.Type == LeakType.NuclearSecrets)
        {
            PoliticalUnrestSystem.AddLeakPressure(source, leak.Severity);
        }
    }

    /// <summary>Yeni bir sızdıran (whistleblower) recrute eder.</summary>
    public static Leaker RecruitLeaker(string handlerCountryId, string targetCountryId, int currentDay, string motivation = "conscience")
    {
        var target = CountryDatabase.Get(targetCountryId);
        if (target == null) return null;

        // Maliyet: PP
        int cost = 100 + DeterministicRNG.NextInt(0, 200);
        var handler = CountryDatabase.Get(handlerCountryId);
        if (handler == null || handler.PoliticalPower < cost) return null;

        handler.PoliticalPower -= cost;

        var leaker = new Leaker
        {
            Id = $"leaker_{_nextLeakerNum++}",
            Name = GenerateLeakerCodename(),
            RealCountryId = targetCountryId,
            CoverIdentity = GenerateCoverIdentity(targetCountryId),
            Motivation = motivation,
            AccessLevel = DeterministicRNG.NextInt(1, 6), // 1-5
            Credibility = DeterministicRNG.NextInt(30, 101),
            IsActive = true,
            DocumentsLeaked = 0,
            RecruitmentDay = currentDay,
            HandlerCountryId = handlerCountryId
        };

        _leakers.Add(leaker);
        GD.Print($"[CovertOps] SIZDIRAN RECRUTE EDİLDİ: {leaker.Id} ({leaker.Name}) -> {targetCountryId} " +
                 $"motivasyon={motivation} erişim={leaker.AccessLevel} güvenilirlik={leaker.Credibility} (gün {currentDay})");

        return leaker;
    }

    /// <summary>Sızdırandan belge sızdırır (leak oluşturur).</summary>
    public static LeakEvent LeakerLeakDocuments(string leakerId, LeakType type, ClassificationLevel classification, int currentDay)
    {
        var leaker = _leakers.FirstOrDefault(l => l.Id == leakerId && l.IsActive);
        if (leaker == null) return null;

        // Erişim seviyesi classification'a uygun mu?
        int minAccessForClass = (int)classification; // SCI=4 -> access 4+
        if (leaker.AccessLevel < minAccessForClass)
        {
            GD.PrintErr($"[CovertOps] Sızdıran erişim yetkisiz: {leaker.AccessLevel} < {minAccessForClass}");
            return null;
        }

        var leak = CreateLeak(type, classification, leaker.RealCountryId, leakerId, currentDay);
        if (leak != null)
        {
            leaker.DocumentsLeaked += leak.DocumentCount;
            leaker.Credibility = Math.Max(10, leaker.Credibility - 5); // Her leak güvenilirliği biraz düşürür

            // Yakalanma riski
            double captureRisk = 0.05 * leak.DocumentCount * ((int)leak.Classification / 4.0 + 0.5);
            if (DeterministicRNG.NextDouble() < captureRisk)
            {
                leaker.IsActive = false;
                GD.Print($"[CovertOps] SIZDIRAN YAKALANDI: {leaker.Id} ({leaker.Name}) leak sonrası (gün {currentDay})");

                // Diplomat kriz
                DiplomacySystem.ModifyRelation(leaker.HandlerCountryId, leaker.RealCountryId, -20);
                EspionageSystem.SetCounterIntelLevel(leaker.RealCountryId,
                    Math.Min(100, EspionageSystem.GetCounterIntelLevel(leaker.RealCountryId) + 15));
            }
        }

        return leak;
    }

    /// <summary>Günlük tick - operasyonları ilerlet, leak risklerini değerlendir.</summary>
    public static void DailyTick(int currentDay)
    {
        // Aktif operasyonları ilerlet
        foreach (var op in _operations.Where(o => !o.IsCompleted && !o.IsCancelled).ToList())
        {
            AdvanceOperation(op.Id, currentDay);
        }

        // Leak riski: pasif sızdırma olasılığı
        EvaluatePassiveLeaks(currentDay);

        // Sızdıranların aktivitesini azalt (zamanla şüphe çekerler)
        foreach (var leaker in _leakers.Where(l => l.IsActive).ToList())
        {
            int daysActive = currentDay - leaker.RecruitmentDay;
            if (daysActive > 365 && DeterministicRNG.NextDouble() < 0.01)
            {
                leaker.IsActive = false;
                GD.Print($"[CovertOps] Sızdıran pasif oldu (güvenlik): {leaker.Id} (gün {currentDay})");
            }
        }
    }

    private static void EvaluatePassiveLeaks(int currentDay)
    {
        // Her ülke için düşük ihtimalle otomatik leak (içerden sızdıran olmadan)
        foreach (var country in CountryDatabase.All())
        {
            if (country.IsAnnexed) continue;

            // Düşük istikrar, yüksek yolsuzluk -> leak riski artar
            double leakChance = 0.001 * (100 - country.StabilityPercent) / 100.0 * (country.CorruptionLevel / 100.0 + 0.5);
            if (DeterministicRNG.NextDouble() < leakChance)
            {
                LeakType type = (LeakType)DeterministicRNG.NextInt(1, Enum.GetValues(typeof(LeakType)).Length);
                ClassificationLevel classif = (ClassificationLevel)DeterministicRNG.NextInt(1, 4);
                CreateLeak(type, classif, country.Id, "INTERNAL", currentDay);
            }
        }
    }

    // ===== Helper Metotları =====

    private static int GetOperationCost(CovertOpType type)
    {
        return type switch
        {
            CovertOpType.CoupAttempt => 500,
            CovertOpType.Assassination => 300,
            CovertOpType.CyberAttack => 150,
            CovertOpType.RegimeChange => 400,
            CovertOpType.ElectionInterference => 200,
            CovertOpType.FalseFlag => 250,
            CovertOpType.EconomicSabotage => 150,
            CovertOpType.DisinformationCampaign => 100,
            CovertOpType.NuclearSabotage => 600,
            CovertOpType.BioweaponRelease => 800,
            _ => 100
        };
    }

    private static int GetOperationDuration(CovertOpType type, string sponsorId, string targetId)
    {
        int baseDays = type switch
        {
            CovertOpType.CoupAttempt => 180,
            CovertOpType.Assassination => 90,
            CovertOpType.CyberAttack => 60,
            CovertOpType.RegimeChange => 365,
            CovertOpType.ElectionInterference => 120,
            CovertOpType.FalseFlag => 45,
            CovertOpType.EconomicSabotage => 90,
            CovertOpType.DisinformationCampaign => 60,
            CovertOpType.NuclearSabotage => 240,
            CovertOpType.BioweaponRelease => 180,
            _ => 60
        };

        // Sponsor yeteneği ve hedef savunması süreyi etkiler
        var sponsor = CountryDatabase.Get(sponsorId);
        var target = CountryDatabase.Get(targetId);
        if (sponsor != null && target != null)
        {
            double sponsorCap = (sponsor.IntelligenceLevel + sponsor.CyberLevel) / 200.0;
            double targetDef = (target.CounterIntelLevel + target.CyberDefense) / 200.0;
            double mult = 1.0 + targetDef - sponsorCap;
            baseDays = (int)Math.Round(baseDays * Math.Clamp(mult, 0.5, 2.0));
        }

        return Math.Max(30, baseDays);
    }

    private static double CalculateSuccessChance(CovertOpType type, string sponsorId, string targetId, string spyId)
    {
        double baseChance = type switch
        {
            CovertOpType.CoupAttempt => 0.15,
            CovertOpType.Assassination => 0.25,
            CovertOpType.CyberAttack => 0.40,
            CovertOpType.RegimeChange => 0.10,
            CovertOpType.ElectionInterference => 0.30,
            CovertOpType.FalseFlag => 0.20,
            CovertOpType.EconomicSabotage => 0.35,
            CovertOpType.DisinformationCampaign => 0.50,
            CovertOpType.NuclearSabotage => 0.05,
            CovertOpType.BioweaponRelease => 0.02,
            _ => 0.30
        };

        // Casus varsa yeteneklerini ekle
        if (!string.IsNullOrEmpty(spyId))
        {
            var spy = EspionageSystem.Spies.FirstOrDefault(s => s.Id == spyId);
            if (spy != null)
            {
                double spyBonus = (spy.Observation + spy.ColdBlood + spy.Education) / 300.0 * 0.3;
                baseChance += spyBonus;
            }
        }

        // Hedef ülke savunması
        var target = CountryDatabase.Get(targetId);
        if (target != null)
        {
            double defense = (target.CounterIntelLevel + target.IntelligenceLevel) / 200.0 * 0.3;
            baseChance -= defense;
        }

        return Math.Clamp(baseChance, 0.01, 0.95);
    }

    private static double GetDailyExposureRisk(CovertOperation op)
    {
        double baseRisk = 0.002; // Günde %0.2

        // Operasyon türüne göre
        double typeMult = op.Type switch
        {
            CovertOpType.CoupAttempt => 3.0,
            CovertOpType.Assassination => 2.0,
            CovertOpType.CyberAttack => 1.5,
            CovertOpType.NuclearSabotage => 4.0,
            CovertOpType.BioweaponRelease => 5.0,
            _ => 1.0
        };

        // Hedef ülke istihbarat yeteneği
        var target = CountryDatabase.Get(op.TargetCountryId);
        double intelFactor = target != null ? target.IntelligenceLevel / 100.0 : 0.5;

        // Süre arttıkça risk artar
        double timeFactor = 1.0 + (op.ProgressDays / 365.0);

        return Math.Clamp(baseRisk * typeMult * (1.0 + intelFactor) * timeFactor, 0.001, 0.1);
    }

    private static int DetermineDocumentCount(LeakType type, ClassificationLevel classification)
    {
        int baseCount = type switch
        {
            LeakType.DiplomaticCables => 250000,
            LeakType.MilitaryDocuments => 50000,
            LeakType.IntelligenceReports => 10000,
            LeakType.EconomicData => 15000,
            LeakType.NuclearSecrets => 500,
            LeakType.SurveillancePrograms => 8000,
            LeakType.CorporateCorruption => 20000,
            LeakType.HumanRightsAbuses => 3000,
            LeakType.ElectionFraud => 5000,
            LeakType.CovertOperationsExposed => 1000,
            _ => 1000
        };

        double classMult = 1.0 + (int)classification * 0.5;
        return (int)Math.Round(baseCount * classMult);
    }

    private static int DetermineSeverity(LeakType type, ClassificationLevel classification)
    {
        int baseSeverity = type switch
        {
            LeakType.DiplomaticCables => 40,
            LeakType.MilitaryDocuments => 60,
            LeakType.IntelligenceReports => 50,
            LeakType.EconomicData => 45,
            LeakType.NuclearSecrets => 90,
            LeakType.SurveillancePrograms => 55,
            LeakType.CorporateCorruption => 35,
            LeakType.HumanRightsAbuses => 50,
            LeakType.ElectionFraud => 60,
            LeakType.CovertOperationsExposed => 70,
            _ => 30
        };

        double classMult = 1.0 + (int)classification * 0.3;
        return (int)Math.Round(baseSeverity * classMult);
    }

    private static string GenerateLeakSummary(LeakType type, string sourceCountryId)
    {
        var templates = new Dictionary<LeakType, string[]>
        {
            [LeakType.DiplomaticCables] = new[] {
                $"Diplomatic cables from {sourceCountryId} reveal secret negotiations and assessments of foreign leaders.",
                $"Massive leak of {sourceCountryId} diplomatic communications exposes covert diplomatic pressure campaigns."
            },
            [LeakType.MilitaryDocuments] = new[] {
                $"Classified military documents from {sourceCountryId} detail operational plans and force dispositions.",
                $"Leaked {sourceCountryId} military files reveal secret bases, weapon systems, and contingency plans."
            },
            [LeakType.IntelligenceReports] = new[] {
                $"Intelligence reports from {sourceCountryId} expose spy networks, sources, and collection priorities.",
                $"Top-secret {sourceCountryId} intelligence assessments leaked, compromising ongoing operations."
            },
            [LeakType.EconomicData] = new[] {
                $"Secret economic data from {sourceCountryId} reveals hidden debt, off-budget spending, and corruption.",
                $"Leaked {sourceCountryId} financial records show clandestine funding of foreign proxies and bribes."
            },
            [LeakType.NuclearSecrets] = new[] {
                $"Nuclear secrets from {sourceCountryId} exposed: enrichment levels, warhead designs, delivery systems.",
                $"Classified {sourceCountryId} nuclear program documents leaked, violating non-proliferation treaties."
            },
            [LeakType.SurveillancePrograms] = new[] {
                $"Mass surveillance programs of {sourceCountryId} revealed: bulk collection, backdoors, targeting criteria.",
                $"Leaked documents show {sourceCountryId} conducts warrantless surveillance on allies and citizens alike."
            },
            [LeakType.CorporateCorruption] = new[] {
                $"Corporate corruption in {sourceCountryId} defense sector exposed: bid-rigging, kickbacks, revolving doors.",
                $"Leaked contracts show {sourceCountryId} military-industrial complex inflated costs by 300%+."
            },
            [LeakType.HumanRightsAbuses] = new[] {
                $"Human rights abuses by {sourceCountryId} security forces documented: secret prisons, torture, disappearances.",
                $"Classified reports confirm {sourceCountryId} operates black sites and extrajudicial killings."
            },
            [LeakType.ElectionFraud] = new[] {
                $"Evidence of election fraud in {sourceCountryId} leaked: ballot stuffing, vote switching, suppression.",
                $"Leaked {sourceCountryId} election commission data shows systematic manipulation of results."
            },
            [LeakType.CovertOperationsExposed] = new[] {
                $"Covert operations by {sourceCountryId} exposed: regime change, assassinations, false flags, cyber attacks.",
                $"Massive leak reveals {sourceCountryId} global covert action program spanning decades."
            }
        };

        if (templates.TryGetValue(type, out var ts) && ts.Length > 0)
        {
            return DeterministicRNG.NextChoice(ts);
        }
        return $"Classified leak from {sourceCountryId} [{type}]";
    }

    private static string GenerateLeakerCodename()
    {
        string[] prefixes = { "SHADOW", "GHOST", "PHANTOM", "SPECTER", "WHISPER", "SILENT", "DARK", "VOID", "NIGHT", "ECHO" };
        string[] suffixes = { "WALKER", "WATCHER", "LISTENER", "KEEPER", "HUNTER", "SEEKER", "WALKER", "RUNNER", "DRIFTER", "STRIDER" };
        return $"{DeterministicRNG.NextChoice(prefixes)}-{DeterministicRNG.NextInt(100, 999)}";
    }

    private static string GenerateCoverIdentity(string countryId)
    {
        string[] roles = { "Diplomat", "Military Attaché", "Technical Specialist", "Intelligence Analyst",
                          "Cultural Attaché", "Trade Representative", "Scientific Advisor", "Press Officer" };
        return $"{DeterministicRNG.NextChoice(roles)} at {countryId} Embassy";
    }

    private static void LoadPublishers()
    {
        // Varsayılan yayıncılar (WikiLeaks benzeri)
        _publishers.Add(new Publisher
        {
            Id = "wikileaks_org",
            Name = "WikiLeaks",
            BaseCountryId = "SWE", // Şu an İsveç'te barınıyor
            Reputation = 85,
            Reach = 95,
            IsStateAffiliated = false
        });

        _publishers.Add(new Publisher
        {
            Id = "guardian_uk",
            Name = "The Guardian",
            BaseCountryId = "GBR",
            Reputation = 80,
            Reach = 85,
            IsStateAffiliated = false
        });

        _publishers.Add(new Publisher
        {
            Id = "nyt_us",
            Name = "The New York Times",
            BaseCountryId = "USA",
            Reputation = 78,
            Reach = 90,
            IsStateAffiliated = false
        });

        _publishers.Add(new Publisher
        {
            Id = "der_spiegel_de",
            Name = "Der Spiegel",
            BaseCountryId = "DEU",
            Reputation = 75,
            Reach = 70,
            IsStateAffiliated = false
        });

        _publishers.Add(new Publisher
        {
            Id = "le_monde_fr",
            Name = "Le Monde",
            BaseCountryId = "FRA",
            Reputation = 72,
            Reach = 65,
            IsStateAffiliated = false
        });

        // Devlet bağlantılı yayıncılar
        _publishers.Add(new Publisher
        {
            Id = "rt_ru",
            Name = "RT (Russia Today)",
            BaseCountryId = "RUS",
            Reputation = 45,
            Reach = 80,
            IsStateAffiliated = true
        });

        _publishers.Add(new Publisher
        {
            Id = "cgtn_cn",
            Name = "CGTN",
            BaseCountryId = "CHN",
            Reputation = 40,
            Reach = 75,
            IsStateAffiliated = true
        });

        GD.Print($"[CovertOps] Loaded {_publishers.Count} publishers");
    }

    private static void LoadLeakDefinitions()
    {
        // Leak türleri ve tanımları zaten enum'da var, ek veri gerekirse JSON'dan yüklenebilir
        // Şimdilik hardcoded
    }

    // ===== Operasyon Etki Uygulama Metotları =====

    private static string ApplyCoupEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        if (target == null) return "Hedef ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Darbe başarılı: rejim değişir, lider değişir, istikrar çökür
            target.RegimeType = DetermineNewRegime(target.RegimeType);
            target.StabilityPercent = Math.Max(0, target.StabilityPercent - 40);
            target.WarSupportPercent = Math.Max(0, target.WarSupportPercent - 30);
            LeaderSystem.GenerateNewLeaderForCovertOp(op.TargetCountryId, op.StartDay + op.ProgressDays);

            // Sponsor ülke ilişkisi artar (geçici)
            DiplomacySystem.ModifyRelation(op.SponsorCountryId, op.TargetCountryId, +15);

            // Uluslararası tepki
            DiplomacySystem.IncreaseTension(15);

            return $"DARBE BAŞARILI: {op.TargetCountryId} rejimi değişti ({target.RegimeType}), istikrar -40%, WS -30%, yeni lider atandı";
        }
        else
        {
            // Darbe başarısız: darbeciler temizlenir, sponsor gizli kalır (açığa çıkmadıysa)
            target.StabilityPercent = Math.Min(100, target.StabilityPercent + 5); // Rejim güçlenir
            target.WarSupportPercent = Math.Min(100, target.WarSupportPercent + 10);

            return $"DARBE BAŞARISIZ: {op.TargetCountryId} rejimi dayandı, istikrar +5%, WS +10%";
        }
    }

    private static string ApplyAssassinationEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        if (target == null) return "Hedef ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Lider öldürüldü
            string oldLeader = target.LeaderName ?? "Bilinmeyen";
            LeaderSystem.GenerateNewLeaderForCovertOp(op.TargetCountryId, op.StartDay + op.ProgressDays);
            string newLeader = target.LeaderName ?? "Yeni Lider";

            target.StabilityPercent = Math.Max(0, target.StabilityPercent - 20);
            target.WarSupportPercent = Math.Max(0, target.WarSupportPercent - 15);
            PoliticalUnrestSystem.AddAssassinationShock(op.TargetCountryId, 30);

            return $"SUIKAST BAŞARILI: {oldLeader} öldürüldü, yeni lider {newLeader}, istikrar -20%, WS -15%, şok etkisi";
        }
        else
        {
            // Suikast başarısız
            target.StabilityPercent = Math.Min(100, target.StabilityPercent + 3);
            return $"SUIKAST BAŞARISIZ: Hedef hayatta, istikrar +3%";
        }
    }

    private static string ApplyCyberAttackEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        if (target == null) return "Hedef ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Altyapı hasarı
            int infraDamage = DeterministicRNG.NextInt(10, 30);
            target.InfrastructureLevel = Math.Max(0, target.InfrastructureLevel - infraDamage);

            // Finansal kayıp
            double gdpLoss = target.EstimatedGDP * 0.02;

            // Askeri sistemler etkilenir
            target.CyberDefense = Math.Max(0, target.CyberDefense - 15);

            return $"SİBER SALDIRI BAŞARILI: {op.TargetCountryId} altyapı -{infraDamage}%, GDP -%2, siber savunma -15";
        }
        else
        {
            return $"SİBER SALDIRI BAŞARISIZ: Hedef siber savunması dayandı";
        }
    }

    private static string ApplyRegimeChangeEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        if (target == null) return "Hedef ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Rejim değişikliği (darbeye benzer ama daha yumuşak, mücadele yoluya)
            target.RegimeType = DetermineNewRegime(target.RegimeType);
            target.StabilityPercent = Math.Max(0, target.StabilityPercent - 25);
            target.WarSupportPercent = Math.Max(0, target.WarSupportPercent - 20);
            LeaderSystem.GenerateNewLeaderForCovertOp(op.TargetCountryId, op.StartDay + op.ProgressDays);

            DiplomacySystem.ModifyRelation(op.SponsorCountryId, op.TargetCountryId, +20);

            return $"REJİM DEĞİŞİKLİĞİ BAŞARILI: {op.TargetCountryId} yeni rejim ({target.RegimeType}), istikrar -25%, WS -20%, sponsor ilişkisi +20";
        }
        else
        {
            target.StabilityPercent = Math.Min(100, target.StabilityPercent + 10);
            return $"REJİM DEĞİŞİKLİĞİ BAŞARISIZ: Muhalefet bastırıldı, istikrar +10%";
        }
    }

    private static string ApplyElectionInterferenceEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        if (target == null) return "Hedef ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Seçim sonucu değiştirildi
            target.ElectionInterferenceLevel = Math.Min(100, target.ElectionInterferenceLevel + 50);
            target.StabilityPercent = Math.Max(0, target.StabilityPercent - 15);
            target.Legitimacy = Math.Max(0, target.Legitimacy - 20);

            return $"SEÇİM MÜDAHALESİ BAŞARILI: {op.TargetCountryId} seçim sonucu değiştirildi, meşruiyet -20%, istikrar -15%";
        }
        else
        {
            // Müdahale açığa çıktı (açık leak riski artar)
            CreateLeak(LeakType.ElectionFraud, ClassificationLevel.Secret, op.TargetCountryId, "OPERATIVE", op.StartDay + op.ProgressDays);
            return $"SEÇİM MÜDAHALESİ BAŞARISIZ: Müdahale tespit edildi, sızıntı riski oluştu";
        }
    }

    private static string ApplyFalseFlagEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        var frame = CountryDatabase.Get(DeterministicRNG.NextChoice(CountryDatabase.All().Select(c => c.Id).Where(c => c != op.TargetCountryId && c != op.SponsorCountryId).ToArray()));

        if (target == null || frame == null) return "Hedef/çerçeve ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Saldırı frame ülkesine yıkandı
            DiplomacySystem.ModifyRelation(frame.Id, target.Id, -30);
            DiplomacySystem.ModifyRelation(op.SponsorCountryId, target.Id, -10); // Sponsor da şüphe çeker
            DiplomacySystem.IncreaseTension(20);

            return $"KIBRISIZ BAYRAK BAŞARILI: {target.Id} saldırısı {frame.Id}'e yıklandı, ilişki -30, gerilim +20";
        }
        else
        {
            // Operasyon açığa çıktı
            CreateLeak(LeakType.MilitaryDocuments, ClassificationLevel.TopSecret, op.SponsorCountryId, op.SpyId, op.StartDay + op.ProgressDays);
            return $"KIBRISIZ BAYRAK BAŞARISIZ: Operasyon açığa çıktı, sızıntı oluşturuldu";
        }
    }

    private static string ApplyEconomicSabotageEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        if (target == null) return "Hedef ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Ekonomik hasar - sivil fabrikaları azaltarak GDP'yi düşür
            int factoryLoss = Math.Max(1, target.CivFactories / 20); // ~%5 fabrika kaybı
            target.CivFactories = Math.Max(0, target.CivFactories - factoryLoss);
            target.InflationRate = Math.Min(100, target.InflationRate + 10);
            target.UnemploymentRate = Math.Min(30, target.UnemploymentRate + 5);

            return $"EKONOMİK SABOTAJ BAŞARILI: {op.TargetCountryId} sivil fabrika -{factoryLoss}, enflasyon +10, işsizlik +5";
        }
        else
        {
            return $"EKONOMİK SABOTAJ BAŞARISIZ: Ekonomik dayanıklılık sağlandı";
        }
    }

    private static string ApplyDisinformationEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        if (target == null) return "Hedef ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Toplum kutuplaşması
            target.Polarization = Math.Min(100, target.Polarization + 25);
            target.StabilityPercent = Math.Max(0, target.StabilityPercent - 10);
            target.TrustInGovernment = Math.Max(0, target.TrustInGovernment - 20);

            return $"DEZİNFORMASYON BAŞARILI: {op.TargetCountryId} kutuplaşma +25, istikrar -10, güven -20";
        }
        else
        {
            return $"DEZİNFORMASYON BAŞARISIZ: Kamu opinion dayandı";
        }
    }

    private static string ApplyNuclearSabotageEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        if (target == null) return "Hedef ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Nükleer program hasarı
            int programDamage = DeterministicRNG.NextInt(20, 50);
            target.NuclearProgramProgress = Math.Max(0, target.NuclearProgramProgress - programDamage);
            target.NuclearSafetyIncidents++;

            // Nükleer güvenlik konseyi toplanır
            DiplomacySystem.IncreaseTension(25);

            return $"NÜKLEER SABOTAJ BAŞARILI: {op.TargetCountryId} nükleer program -{programDamage}%, güvenlik olayı, gerilim +25";
        }
        else
        {
            return $"NÜKLEER SABOTAJ BAŞARISIZ: Nükleer tesisler güvenli";
        }
    }

    private static string ApplyBioweaponEffect(CovertOperation op)
    {
        var target = CountryDatabase.Get(op.TargetCountryId);
        if (target == null) return "Hedef ülke bulunamadı";

        bool success = DeterministicRNG.NextDouble() < op.SuccessChance;

        if (success)
        {
            // Biyolojik saldırı - çok ağır sonuçlar
            int deaths = DeterministicRNG.NextInt(10000, 100000);
            target.Population -= deaths;
            target.StabilityPercent = Math.Max(0, target.StabilityPercent - 50);
            target.HealthcareCapacity = Math.Max(0, target.HealthcareCapacity - 40);

            // Dünya çapında ceza
            DiplomacySystem.IncreaseTension(50);
            foreach (var country in CountryDatabase.All().Where(c => !c.IsAnnexed && c.Id != op.SponsorCountryId))
            {
                DiplomacySystem.ModifyRelation(op.SponsorCountryId, country.Id, -30);
            }

            return $"BİYOLOJİK SALDIRı BAŞARILI: {op.TargetCountryId} {deaths:N0} ölü, istikrar -50%, sağlık -40%, küresel öfke, sponsor izole edildi";
        }
        else
        {
            // Biyolojik silah kontrolü kaybedildi, kendi ülkeye bulaştı
            var sponsor = CountryDatabase.Get(op.SponsorCountryId);
            if (sponsor != null)
            {
                int blowbackDeaths = DeterministicRNG.NextInt(1000, 10000);
                sponsor.Population -= blowbackDeaths;
                sponsor.StabilityPercent = Math.Max(0, sponsor.StabilityPercent - 30);
                return $"BİYOLOJİK SALDIRı BAŞARISIZ ve GERİ DÖNÜŞ: Sponsor ülkesine bulaştı, {blowbackDeaths:N0} ölü";
            }
            return $"BİYOLOJİK SALDIRı BAŞARISIZ";
        }
    }

    private static string DetermineNewRegime(string currentRegime)
    {
        string[] regimes = { "Democracy", "Authoritarian", "Monarchy", "Theocracy", "Military Junta", "Single Party", "Federal Republic" };
        return DeterministicRNG.NextChoice(regimes.Where(r => r != currentRegime).ToArray());
    }

    // ===== Save / Load =====

    public class CovertOperationSave
    {
        public string Id = "";
        public int Type = 0;
        public string SponsorCountryId = "";
        public string TargetCountryId = "";
        public string SpyId = "";
        public int StartDay = 0;
        public int RequiredDays = 0;
        public int ProgressDays = 0;
        public double SuccessChance = 0;
        public bool IsCompleted = false;
        public bool IsExposed = false;
        public bool IsCancelled = false;
        public string EffectSummary = "";
        public string[] GeneratedLeaks = Array.Empty<string>();
    }

    public class LeakEventSave
    {
        public string Id = "";
        public int Type = 0;
        public int Classification = 0;
        public string SourceCountryId = "";
        public string LeakerId = "";
        public string PublisherId = "";
        public int Day = 0;
        public int DocumentCount = 0;
        public int Severity = 0;
        public bool IsPublished = false;
        public string Summary = "";
        public string[] ImpactCountries = Array.Empty<string>();
        public int[] ImpactValues = Array.Empty<int>();
    }

    public class LeakerSave
    {
        public string Id = "";
        public string Name = "";
        public string RealCountryId = "";
        public string CoverIdentity = "";
        public string Motivation = "";
        public int AccessLevel = 0;
        public int Credibility = 0;
        public bool IsActive = false;
        public int DocumentsLeaked = 0;
        public int RecruitmentDay = 0;
        public string HandlerCountryId = "";
    }

    public class PublisherSave
    {
        public string Id = "";
        public string Name = "";
        public string BaseCountryId = "";
        public int Reputation = 0;
        public int Reach = 0;
        public bool IsStateAffiliated = false;
        public string[] PublishedLeakIds = Array.Empty<string>();
    }

    public class CovertOperationsSave
    {
        public CovertOperationSave[] Operations = Array.Empty<CovertOperationSave>();
        public LeakEventSave[] Leaks = Array.Empty<LeakEventSave>();
        public LeakerSave[] Leakers = Array.Empty<LeakerSave>();
        public PublisherSave[] Publishers = Array.Empty<PublisherSave>();
        public int NextOpNum = 1;
        public int NextLeakNum = 1;
        public int NextLeakerNum = 1;
    }

    public static CovertOperationsSave GetSaveData()
    {
        return new CovertOperationsSave
        {
            Operations = _operations.Select(o => new CovertOperationSave
            {
                Id = o.Id,
                Type = (int)o.Type,
                SponsorCountryId = o.SponsorCountryId,
                TargetCountryId = o.TargetCountryId,
                SpyId = o.SpyId ?? "",
                StartDay = o.StartDay,
                RequiredDays = o.RequiredDays,
                ProgressDays = o.ProgressDays,
                SuccessChance = o.SuccessChance,
                IsCompleted = o.IsCompleted,
                IsExposed = o.IsExposed,
                IsCancelled = o.IsCancelled,
                EffectSummary = o.EffectSummary ?? "",
                GeneratedLeaks = o.GeneratedLeaks.ToArray()
            }).ToArray(),
            Leaks = _leaks.Select(l => new LeakEventSave
            {
                Id = l.Id,
                Type = (int)l.Type,
                Classification = (int)l.Classification,
                SourceCountryId = l.SourceCountryId,
                LeakerId = l.LeakerId,
                PublisherId = l.PublisherId ?? "",
                Day = l.Day,
                DocumentCount = l.DocumentCount,
                Severity = l.Severity,
                IsPublished = l.IsPublished,
                Summary = l.Summary ?? "",
                ImpactCountries = l.ImpactByCountry.Keys.ToArray(),
                ImpactValues = l.ImpactByCountry.Values.ToArray()
            }).ToArray(),
            Leakers = _leakers.Select(l => new LeakerSave
            {
                Id = l.Id,
                Name = l.Name,
                RealCountryId = l.RealCountryId,
                CoverIdentity = l.CoverIdentity,
                Motivation = l.Motivation,
                AccessLevel = l.AccessLevel,
                Credibility = l.Credibility,
                IsActive = l.IsActive,
                DocumentsLeaked = l.DocumentsLeaked,
                RecruitmentDay = l.RecruitmentDay,
                HandlerCountryId = l.HandlerCountryId ?? ""
            }).ToArray(),
            Publishers = _publishers.Select(p => new PublisherSave
            {
                Id = p.Id,
                Name = p.Name,
                BaseCountryId = p.BaseCountryId,
                Reputation = p.Reputation,
                Reach = p.Reach,
                IsStateAffiliated = p.IsStateAffiliated,
                PublishedLeakIds = p.PublishedLeakIds.ToArray()
            }).ToArray(),
            NextOpNum = _nextOpNum,
            NextLeakNum = _nextLeakNum,
            NextLeakerNum = _nextLeakerNum
        };
    }

    public static void LoadSaveData(CovertOperationsSave data)
    {
        _operations.Clear();
        _leaks.Clear();
        _leakers.Clear();
        _publishers.Clear();

        if (data == null)
        {
            LoadPublishers();
            _initialized = true;
            return;
        }

        if (data.Operations != null)
        {
            foreach (var o in data.Operations)
            {
                _operations.Add(new CovertOperation
                {
                    Id = o.Id,
                    Type = (CovertOpType)o.Type,
                    SponsorCountryId = o.SponsorCountryId,
                    TargetCountryId = o.TargetCountryId,
                    SpyId = o.SpyId,
                    StartDay = o.StartDay,
                    RequiredDays = o.RequiredDays,
                    ProgressDays = o.ProgressDays,
                    SuccessChance = o.SuccessChance,
                    IsCompleted = o.IsCompleted,
                    IsExposed = o.IsExposed,
                    IsCancelled = o.IsCancelled,
                    EffectSummary = o.EffectSummary,
                    GeneratedLeaks = o.GeneratedLeaks?.ToList() ?? new List<string>()
                });
            }
        }

        if (data.Leaks != null)
        {
            foreach (var l in data.Leaks)
            {
                var leak = new LeakEvent
                {
                    Id = l.Id,
                    Type = (LeakType)l.Type,
                    Classification = (ClassificationLevel)l.Classification,
                    SourceCountryId = l.SourceCountryId,
                    LeakerId = l.LeakerId,
                    PublisherId = l.PublisherId,
                    Day = l.Day,
                    DocumentCount = l.DocumentCount,
                    Severity = l.Severity,
                    IsPublished = l.IsPublished,
                    Summary = l.Summary,
                    ImpactByCountry = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                };
                if (l.ImpactCountries != null && l.ImpactValues != null)
                {
                    for (int i = 0; i < Math.Min(l.ImpactCountries.Length, l.ImpactValues.Length); i++)
                    {
                        leak.ImpactByCountry[l.ImpactCountries[i]] = l.ImpactValues[i];
                    }
                }
                _leaks.Add(leak);
            }
        }

        if (data.Leakers != null)
        {
            foreach (var l in data.Leakers)
            {
                _leakers.Add(new Leaker
                {
                    Id = l.Id,
                    Name = l.Name,
                    RealCountryId = l.RealCountryId,
                    CoverIdentity = l.CoverIdentity,
                    Motivation = l.Motivation,
                    AccessLevel = l.AccessLevel,
                    Credibility = l.Credibility,
                    IsActive = l.IsActive,
                    DocumentsLeaked = l.DocumentsLeaked,
                    RecruitmentDay = l.RecruitmentDay,
                    HandlerCountryId = l.HandlerCountryId
                });
            }
        }

        if (data.Publishers != null)
        {
            foreach (var p in data.Publishers)
            {
                _publishers.Add(new Publisher
                {
                    Id = p.Id,
                    Name = p.Name,
                    BaseCountryId = p.BaseCountryId,
                    Reputation = p.Reputation,
                    Reach = p.Reach,
                    IsStateAffiliated = p.IsStateAffiliated,
                    PublishedLeakIds = p.PublishedLeakIds?.ToList() ?? new List<string>()
                });
            }
        }

        _nextOpNum = data.NextOpNum > 0 ? data.NextOpNum : _operations.Count + 1;
        _nextLeakNum = data.NextLeakNum > 0 ? data.NextLeakNum : _leaks.Count + 1;
        _nextLeakerNum = data.NextLeakerNum > 0 ? data.NextLeakerNum : _leakers.Count + 1;

        _initialized = true;
        GD.Print($"[CovertOps] State yüklendi — operasyon={_operations.Count}, leak={_leaks.Count}, sızdıran={_leakers.Count}, yayıncı={_publishers.Count}");
    }

    public static void ResetForNewGame()
    {
        _operations.Clear();
        _leaks.Clear();
        _leakers.Clear();
        _publishers.Clear();
        _nextOpNum = 1;
        _nextLeakNum = 1;
        _nextLeakerNum = 1;
        _initialized = false;
        LoadPublishers();
        GD.Print("[CovertOps] Yeni oyun için sıfırlandı.");
    }
}