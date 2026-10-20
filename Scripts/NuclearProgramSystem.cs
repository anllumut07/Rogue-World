using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Nükleer program sistemi (M31.2): uranyum zenginleştirme → reaktör → silah tasarımı →
/// deneme → taşıyıcı (bombardeıman, kıtalararası füze, denizaltı) → doktrin. Aşamalar
/// sırayla ilerler: her aşama öncekinin tamamlanmış, ilgili teknolojinin araştırılmış
/// ve PP/kaynak ödemesinin yapılmış olmasını gerektirir. Nükleer deneme başarısı
/// DeterministicRNG ile çözülür. Statik sınıf, oyuncuya özel; GameManager günlük
/// tick'inde çağrılır. Normal oyunda UI'sı yok (M31.3/M31.4 diplomi/askerî etkileri
/// tamamlar).
/// </summary>
public static class NuclearProgramSystem
{
    public enum NuclearStage
    {
        None = 0,
        UraniumEnrichment = 1, // 1: Uranyum zenginleştirme tesisi
        Reactor = 2,           // 2: Nükleer reaktör
        WeaponDesign = 3,      // 3: Nükleer silah tasarımı
        NuclearTest = 4,       // 4: Nükleer deneme patlaması
        BomberDelivery = 5,    // 5a: Stratejik bombardman taşıyıcısı
        IcbmDelivery = 6,      // 5b: Kıtalararası füze (ICBM)
        SlbmDelivery = 7,      // 5c: Denizaltı füzesi (SLBM)
        Doctrine = 8           // 6: Nükleer doktrin
    }

    public enum NuclearDoctrine { None = 0, Deterrence = 1, FirstUse = 2, NoFirstUse = 3 }

    /// <summary>
    /// M31.4: Bir ülke üzerinde aktif Radyasyon Bölgesi (fallout). Belirli bir gün sayısı
    /// boyunca target'ın refah/istiridyasını yagyar; GetRadiationIntensity ile sorgulanır.
    /// </summary>
    public class RadiationZone
    {
        public string TargetId { get; set; }
        public int UntilDay { get; set; }      // bu güne kadar radyoaktivite devam eder
        public float Intensity { get; set; }   // 0-1 (ör. %30-%70)
    }

    /// <summary>
    /// M31.4: Nükleer saldırının net etkileri (test ve raporlama için).
    /// </summary>
    public class StrikeResult
    {
        public string AttackerId { get; set; }
        public string TargetId { get; set; }
        public int Day { get; set; }
        public long PopulationLost { get; set; }
        public int FactoriesLost { get; set; }
        public float StabilityLoss { get; set; }
        public float WarSupportLoss { get; set; }
        public float TensionIncrease { get; set; }
        public int RelationDelta { get; set; }      // saldırgan-hedef ikisi için uygulanan düşüş
        public bool RetaliationTriggered { get; set; }
        public float RetaliationDamageToAttacker { get; set; } // hedefin karşı saldırısından saldırgana düşen nüfus/istiridya
        public RadiationZone Radiation { get; set; }
    }

    // Aşama → gereken teknoloji ID'si (technologies.json). TümEra >= 2020, bu yüzden
    // başlangıçta hiçbiri otomatik araştırılmaz (maks. startEra = 2010).
    private static readonly Dictionary<NuclearStage, string> StageTechId = new()
    {
        { NuclearStage.UraniumEnrichment, "uranium_enrichment" },
        { NuclearStage.Reactor, "nuclear_reactor" },
        { NuclearStage.WeaponDesign, "nuclear_weapon_design" },
        { NuclearStage.NuclearTest, "nuclear_testing" },
        { NuclearStage.BomberDelivery, "strategic_bomber" },
        { NuclearStage.IcbmDelivery, "icbm_technology" },
        { NuclearStage.SlbmDelivery, "slbm_technology" },
        { NuclearStage.Doctrine, "nuclear_doctrine" },
    };

    // (PP maliyeti, gün, demir, alüminyum) — test modunda hepsi 0/1
    private static readonly Dictionary<NuclearStage, (int pp, int days, int steel, int aluminum)> StageCost = new()
    {
        { NuclearStage.UraniumEnrichment, (500, 10, 200, 100) },
        { NuclearStage.Reactor, (1000, 15, 400, 200) },
        { NuclearStage.WeaponDesign, (1500, 20, 600, 300) },
        { NuclearStage.NuclearTest, (2500, 30, 1000, 500) },
        { NuclearStage.BomberDelivery, (1500, 15, 400, 300) },
        { NuclearStage.IcbmDelivery, (3000, 35, 800, 400) },
        { NuclearStage.SlbmDelivery, (4000, 40, 1000, 500) },
        { NuclearStage.Doctrine, (1000, 10, 200, 100) },
    };

    // ===== Durum (oyuncuya özel) =====
    private static NuclearStage _completedThrough = NuclearStage.None;
    private static NuclearStage _inProgress = NuclearStage.None;
    private static int _daysInvested;
    private static int _testsConducted;
    private static int _warheads;
    private static NuclearDoctrine _doctrine = NuclearDoctrine.None;
    private static bool _testMode;

    // M31.4: aktif radyasyon bölgeleri (hedef ülke → temizleniş günü + yoğunluk)
    private static readonly List<RadiationZone> _radiationZones = new();

    public static NuclearStage CompletedThrough => _completedThrough;
    public static NuclearStage InProgressStage => _inProgress;
    public static int DaysInvested => _daysInvested;
    public static int TestsConducted => _testsConducted;
    public static int Warheads => _warheads;
    public static NuclearDoctrine Doctrine => _doctrine;

    /// <summary>M31.4: Belirli bir ülkedeki aktif radyasyon yoğunluğu (0 = temiz).</summary>
    public static float GetRadiationIntensity(string countryId, int currentDay)
    {
        foreach (var z in _radiationZones)
            if (z.TargetId == countryId && currentDay < z.UntilDay) return z.Intensity;
        return 0f;
    }

    /// <summary>M31.4: Aktif radyasyon bölgesi sayısı (test/raporlama için).</summary>
    public static int ActiveRadiationZones => _radiationZones.Count(z => z.UntilDay > (GM?.TotalDays ?? 0));    public static bool IsTestMode => _testMode;

    public static void EnableTestMode()
    {
        _testMode = true;
        GD.Print("[Nükleer] Test modu etkin: ücret/günek=0, süre=1 gün, deneme başarısı garantili");
    }

    /// <summary>Yalnızca test modunda geçerli: saldırı testi için başlık sayısını direkt ayarlar.</summary>
    public static void SetTestWarheads(int count)
    {
        if (!_testMode) return;
        _warheads = Math.Max(0, count);
        GD.Print($"[Nükleer] Test başlık sayısı={_warheads}");
    }

    /// <summary>Yalnızca test modunda geçerli: taşıyıcı yeteneği için tamamlanmış aşamayı direkt ayarlar.</summary>
    public static void SetTestCompletedStage(NuclearStage stage)
    {
        if (!_testMode) return;
        _completedThrough = stage;
        GD.Print($"[Nükleer] Test tamamlanmış aşama={stage}");
    }

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    // Aşama maliyeti (test modunda ücretsiz, 1 gün)
    private static (int pp, int days, int steel, int aluminum) GetStageCost(NuclearStage stage)
    {
        if (_testMode) return (0, 1, 0, 0);
        return StageCost[stage];
    }

    public static int GetStageDuration(NuclearStage stage) => GetStageCost(stage).days;

    public static string GetStageTechId(NuclearStage stage) => StageTechId.TryGetValue(stage, out var id) ? id : null;

    // Sıralı + kilit koşulları: aşama N, aşama N-1'in tamamlanmış olmasını ister
    public static (bool ok, string reason) CanStartStage(NuclearStage stage)
    {
        if (stage == NuclearStage.None) return (false, "Geçersiz aşama");
        if ((int)stage != (int)_completedThrough + 1)
            return (false, $"Sıralı kural: önce '{_completedThrough}' tamamlanmalı");
        if (!StageTechId.TryGetValue(stage, out var techId))
            return (false, $"Aşama tanımı yok: {stage}");
        if (TechnologySystem.GetStatus(techId) != TechnologySystem.ResearchStatus.Researched)
            return (false, $"Teknoloji araştırılmamış: {techId}");

        var (pp, _, steel, aluminum) = GetStageCost(stage);
        if (Player == null) return (false, "Oyuncu ülkesi yok");
        if (Player.PoliticalPower < pp) return (false, $"Yetersiz PP ({Player.PoliticalPower:F0} < {pp})");
        if (Player.Steel < steel) return (false, $"Yetersiz demir ({Player.Steel} < {steel})");
        if (Player.Aluminum < aluminum) return (false, $"Yetersiz alüminyum ({Player.Aluminum} < {aluminum})");
        return (true, null);
    }

    public static bool StartStage(NuclearStage stage)
    {
        var (ok, reason) = CanStartStage(stage);
        if (!ok)
        {
            GD.Print($"[Nükleer] Aşama başlatılamadı: {stage} — {reason}");
            return false;
        }
        var (pp, _, steel, aluminum) = GetStageCost(stage);
        Player.PoliticalPower -= pp;
        Player.Steel -= steel;
        Player.Aluminum -= aluminum;
        _inProgress = stage;
        _daysInvested = 0;
        GD.Print($"[Nükleer] Aşama başladı: {stage} (süre {GetStageDuration(stage)} gün, PP −{pp}, demir −{steel}, alüminyum −{aluminum})");
        return true;
    }

    // Günlük tick: süresi dolan aşamayı tamamlar
    public static void DailyTick(int currentDay)
    {
        if (_inProgress != NuclearStage.None)
        {
            _daysInvested++;
            if (_daysInvested >= GetStageDuration(_inProgress))
                CompleteStage();
        }

        // M31.4: süresi dolan radyasyon bölgelerini temizle
        _radiationZones.RemoveAll(z => z.UntilDay <= currentDay);
    }

    private static void CompleteStage()
    {
        var done = _inProgress;

        // Nükleer deneme: DeterministicRNG ile başarı/başarısızlık (test modunda garantili)
        if (done == NuclearStage.NuclearTest && !_testMode && !DeterministicRNG.NextBool(0.80))
        {
            GD.Print("[Nükleer] Deneme patlaması başarısız — aşama tekrar edilmeli");
            _inProgress = NuclearStage.None;
            _daysInvested = 0;
            // _completedThrough önceki aşamada kalır (WeaponDesign)
            return;
        }

        _inProgress = NuclearStage.None;
        _daysInvested = 0;
        _completedThrough = done;

        if (done == NuclearStage.NuclearTest)
        {
            _testsConducted++;
            _warheads = 1;
            GD.Print("[Nükleer] Nükleer deneme başarılı — ilk savaş başlığı elde edildi");
        }
        else if (done == NuclearStage.Doctrine)
        {
            _doctrine = NuclearDoctrine.Deterrence;
            GD.Print("[Nükleer] Nükleer doktrin belirlendi: caydırıcılık (deterrence)");
        }
        GD.Print($"[Nükleer] Aşama tamamlandı: {done} — en yüksek tamamlanan: {_completedThrough}");
    }

    // ===== M31.4: Nükleer saldırı (kullanım) =====

    /// <summary>
    /// Bir nükleer saldırı başlatılabilir mi? Saldırganın savaş başlığı, taşıyıcı kapasitesi,
    /// PP'si ve hedefin geçerliliğini denetler.
    /// </summary>
    public static (bool ok, string reason) CanLaunchNuclearStrike(string attackerId, string targetId)
    {
        if (string.IsNullOrEmpty(attackerId) || string.IsNullOrEmpty(targetId) || attackerId == targetId)
            return (false, "Geçersiz saldırgan/hedef");

        var attacker = CountryDatabase.Get(attackerId);
        var target = CountryDatabase.Get(targetId);
        if (attacker == null || target == null || attacker.IsAnnexed || target.IsAnnexed)
            return (false, "Ülke yok veya ilhak edilmiş");

        // Taşıyıcı kapasitesi: en az bombardman/ICBM/SLBM aşaması tamamlanmış olmalı
        if (_completedThrough < NuclearStage.BomberDelivery)
            return (false, "Nükleer taşıyıcı teknolojisi yok (bombardman/ICBM/SLBM gerekli)");

        if (_warheads <= 0)
            return (false, "Savaş başlığı yok");

        const int launchPPCost = 1000;
        if (attacker.PoliticalPower < launchPPCost)
            return (false, $"Yetersiz PP (saldırı maliyeti {launchPPCost})");

        return (true, null);
    }

    /// <summary>
    /// Nükleer saldırıyı gerçekleştirir: başlık tüketilir, hedefte nüfus/ekonomi kaybı,
    /// radyasyon bölgesi oluşturur, dünya gerilimi ve ilişkilerde büyük değişim olur.
    /// Karmaşık tepki (karşı saldırı) DeterministikRNG ile çözülür.
    /// </summary>
    public static (bool ok, string reason, StrikeResult result) LaunchNuclearStrike(string attackerId, string targetId)
    {
        var (can, reason) = CanLaunchNuclearStrike(attackerId, targetId);
        if (!can) return (false, reason, null);

        var attacker = CountryDatabase.Get(attackerId);
        var target = CountryDatabase.Get(targetId);
        int day = GM?.TotalDays ?? 0;

        const int launchPPCost = 1000;
        attacker.PoliticalPower -= launchPPCost;
        _warheads--;

        var result = new StrikeResult
        {
            AttackerId = attackerId,
            TargetId = targetId,
            Day = day,
            Radiation = new RadiationZone { TargetId = targetId }
        };

        // --- Nüfus ve ekonomi kaybı (hedef) ---
        int popLossPercent = _testMode ? 10 : DeterministicRNG.NextInt(5, 16); // %5-%15
        long populationLost = (long)(target.Population * popLossPercent / 100.0);
        target.Population = Math.Max(0, target.Population - populationLost);

        int civFactoryLost = _testMode ? 1 : DeterministicRNG.NextInt(1, 4); // 1-3
        int milFactoryLost = _testMode ? 1 : DeterministicRNG.NextInt(1, 3);  // 1-2
        target.CivFactories = Math.Max(0, target.CivFactories - civFactoryLost);
        target.MilFactories = Math.Max(0, target.MilFactories - milFactoryLost);
        int factoriesLost = civFactoryLost + milFactoryLost;

        target.StabilityPercent = Math.Clamp(target.StabilityPercent - 15, 0, 100);
        target.WarSupportPercent = Math.Clamp(target.WarSupportPercent - 10, 0, 100);

        result.PopulationLost = populationLost;
        result.FactoriesLost = factoriesLost;
        result.StabilityLoss = 15f;
        result.WarSupportLoss = 10f;

        // --- Radyasyon bölgesi (hedef) ---
        int radiationDuration = _testMode ? 5 : DeterministicRNG.NextInt(30, 91);
        float radiationIntensity = _testMode ? 0.5f : (float)DeterministicRNG.NextDouble(0.30, 0.70);
        result.Radiation.UntilDay = day + radiationDuration;
        result.Radiation.Intensity = radiationIntensity;
        _radiationZones.Add(result.Radiation);

        // --- Uluslararası tepki: dünya gerilimi (büyük artış) ---
        float tensionIncrease = _testMode ? 5f : 25f;
        DiplomacySystem.IncreaseTension(tensionIncrease);
        result.TensionIncrease = tensionIncrease;

        // --- İlişki etkisi: saldırgan-hedef ikisi büyük ölçüde bozulur ---
        int relationDelta = -50;
        DiplomacySystem.ModifyRelation(attackerId, targetId, relationDelta);
        DiplomacySystem.ModifyRelation(targetId, attackerId, relationDelta);
        result.RelationDelta = relationDelta;

        // Nükel tabosu: tüm bağımsız ülkelerin saldırgana karşı hafif bir yıtışı
        foreach (var c in CountryDatabase.All())
        {
            if (c.IsAnnexed || c.Id == attackerId || c.Id == targetId) continue;
            DiplomacySystem.ModifyRelation(c.Id, attackerId, -5);
        }

        // Hedefin faksiyon müttefikleri saldırgana karşı daha sert tepki verir
        foreach (var f in DiplomacySystem.Factions.Values)
        {
            bool targetIsMember = f.LeaderId == targetId || f.MemberIds.Contains(targetId);
            if (targetIsMember)
            {
                foreach (var m in f.MemberIds)
                {
                    if (m != attackerId && m != targetId)
                        DiplomacySystem.ModifyRelation(m, attackerId, -15);
                }
                break;
            }
        }

        // --- Karşı saldırı (retaliation): büyük askeri güce sahip hedefDeterministik olarak yanıt verir ---
        bool retaliation = target.MilFactories >= 8 && DeterministicRNG.NextBool(0.5);
        if (retaliation)
        {
            result.RetaliationTriggered = true;
            // Hedef, saldırgan üzerinde hafif bir karşı darbe yapar (konvansiyonel)
            int retaliatePopLossPercent = _testMode ? 3 : DeterministicRNG.NextInt(1, 5);
            long retaliatePopLost = (long)(attacker.Population * retaliatePopLossPercent / 100.0);
            attacker.Population = Math.Max(0, attacker.Population - retaliatePopLost);
            attacker.StabilityPercent = Math.Clamp(attacker.StabilityPercent - 5, 0, 100);
            result.RetaliationDamageToAttacker = retaliatePopLost;
            DiplomacySystem.IncreaseTension(_testMode ? 3f : 10f);
            GD.Print($"[Nükleer] KARŞI SALDIRI: {targetId} -> {attackerId} (nüfus kaybı {retaliatePopLost})");
        }

        // --- Caydırıcılık doktrin etkisi: saldırgan caydırıcılık doktrinindeyse prestij kaybı ---
        if (_doctrine == NuclearDoctrine.Deterrence)
        {
            attacker.Prestige = Math.Max(0, attacker.Prestige - 5);
            GD.Print("[Nükleer] Caydırıcılık doktriniyle saldırı — prestij −5");
        }

        GD.Print($"[Nükleer] SALDIRI: {attackerId} -> {targetId} (gün {day}) — başlık −1, " +
                 $"nüfus −{populationLost}, fabrika −{factoriesLost}, istikrar −15, gerilim +{tensionIncrease:F0}, " +
                 $"radyasyon {radiationIntensity:P0} {radiationDuration}g, karşı saldırı={retaliation}");

        return (true, null, result);
    }

    // ===== M12: Save/Load =====

    public class NuclearSave
    {
        public int CompletedThrough { get; set; } // (int)NuclearStage
        public int InProgress { get; set; }       // -1 = yok
        public int DaysInvested { get; set; }
        public int TestsConducted { get; set; }
        public int Warheads { get; set; }
        public int Doctrine { get; set; } // (int)NuclearDoctrine
        public List<RadiationZone> RadiationZones { get; set; } // M31.4
    }

    public static NuclearSave GetSaveData()
    {
        return new NuclearSave
        {
            CompletedThrough = (int)_completedThrough,
            InProgress = _inProgress == NuclearStage.None ? -1 : (int)_inProgress,
            DaysInvested = _daysInvested,
            TestsConducted = _testsConducted,
            Warheads = _warheads,
            Doctrine = (int)_doctrine,
            RadiationZones = _radiationZones.Select(z => new RadiationZone
            {
                TargetId = z.TargetId,
                UntilDay = z.UntilDay,
                Intensity = z.Intensity
            }).ToList()
        };
    }

    public static void LoadSaveData(NuclearSave data)
    {
        if (data == null) return;
        _completedThrough = (NuclearStage)data.CompletedThrough;
        _inProgress = data.InProgress < 0 ? NuclearStage.None : (NuclearStage)data.InProgress;
        _daysInvested = data.DaysInvested;
        _testsConducted = data.TestsConducted;
        _warheads = data.Warheads;
        _doctrine = (NuclearDoctrine)data.Doctrine;

        _radiationZones.Clear();
        if (data.RadiationZones != null)
            foreach (var z in data.RadiationZones) _radiationZones.Add(z);

        GD.Print($"[Nükleer] State yüklendi — ileri={_completedThrough}, devam={_inProgress}, başlık={_warheads}, doktrin={_doctrine}, radyasyon={_radiationZones.Count}");
    }

    public static void ResetForNewGame()
    {
        _completedThrough = NuclearStage.None;
        _inProgress = NuclearStage.None;
        _daysInvested = 0;
        _testsConducted = 0;
        _warheads = 0;
        _doctrine = NuclearDoctrine.None;
        _testMode = false;
        _radiationZones.Clear();
        GD.Print("[Nükleer] Yeni oyun için sıfırlandı");
    }
}
