using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Army Morale & Supply Lines System (M58) — Gelişmiş ordu morali, ikmal hatları, özel kuvvetler, veteränlık.
/// Özellikler:
/// - Ordu morali (0-100): Deneyim, liderlik, ikmal, kayıplar, zaferler/mağlupiyetler
/// - İkmal Hatları (Supply Lines): Görsel/mantıksal hatlar, kesilebilir/interdiction edilebilir
/// - Özel Kuvvetler (Special Forces): Paraşüt, komando,marine, istihbarat özel yetenekler
/// - Veteränlık/Deneyim: Savaş deneyimi kazanma, veterän bonusları, yetenek ağacı
/// - İkmal Hattı Kesme/Interdiction: Hava/deniz/karada ikmal hatlarını kesme
/// - Şef Komuta Yeteneği: General yetenekleri morali/ikmal etkiler
/// - Psikolojik Savaş: Propaganda, deşarj, kaçarak gruplar
/// Tüm veri deterministiktir (DeterministicRNG), Save/Load desteklidir.
/// </summary>
public static class ArmyMoraleSystem
{
    // ===== Sabitler =====
    public const int DefaultMorale = 50;
    public const int MinMorale = 0;
    public const int MaxMorale = 100;
    public const int MaxExperience = 1000;  // Deneyim puanı (0-1000)
    public const int MaxVeterancyLevel = 5; // 0=Green, 1=Regular, 2=Veteran, 3=Elite, 4=Guard, 5=Legendary

    // Morale faktörleri
    private const int BaseMorale = 50;
    private const int LowMoraleThreshold = 30;
    private const int CriticalMoraleThreshold = 15;
    private const int HighMoraleThreshold = 70;
    private const float DailyMoraleRecovery = 1.5f; // Günde toparlanma (varsayılana doğru)
    private const float DailyMoraleDrift = 0.5f; // Günlük morale kayması (varsayılana doğru)

    // Deneyim kazanımı
    private const int ExpPerCombatDay = 5;
    private const int ExpPerVictory = 15;
    private const int ExpPerDefeat = 5;
    private const int ExpPerCasualtyTaken = 1;  // Kendi kayıpları da deneyim verir
    private const int ExpPerCasualtyInflicted = 2; // Düşmana kayıp vermek deneyim verir

    // Veteränlık seviyeleri (deneyim eşikleri)
    private static readonly int[] VeterancyThresholds = { 0, 50, 150, 300, 550, 1000 };

    // ===== Veri Modelleri =====

    /// <summary>Morale etkileyen faktörler.</summary>
    public enum MoraleFactor
    {
        Victory,            // Zafer (+morale)
        Defeat,             // Mağlubiyet (-morale)
        HighCasualties,     // Yüksek kayıp (-morale)
        LowCasualties,      // Düşük kayıp (+morale)
        SupplyShortage,     // İkmal eksikliği (-morale)
        GoodSupply,         // İyi ikmal (+morale)
        GoodLeadership,     // İyi komuta (+morale)
        PoorLeadership,     // Kötü komuta (-morale)
        HighExperience,     // Yüksek deneyim (+morale)
        LowExperience,      // Düşük deneyim (-morale)
        Entrenched,         // Kazık kazılmış (+morale)
        Flanked,            // Yandan vuruldu (-morale)
        Surrounded,         // Kuşatıldı (-büyük morale kaybı)
        Breakthrough,       // Sırttan vuruldu / krıldı (+morale)
        Propaganda,         // Propaganda etkisi (±morale)
        SpecialForcesBonus, // Özel kuvvetler bonusu (+morale)
        TerrorWeapon,       // Terör silahlı (gaz, nukleer vb.) -büyük morale kaybı
        ChemicalWeapon,     // Kimyasal silah -morale
        NuclearStrike       // Nükleer vurulma -büyük morale kaybı
    }

    /// <summary>Özel kuvvet türleri.</summary>
    public enum SpecialForceType
    {
        None = 0,
        Paratroopers,       // Paraşütçüler: Hava ile indirme, arkadan saldırı
        Commandos,          // Komandolar: Gizli operasyon, sabotaj, hedef vurma
        Marines,            // Piyadeler: Amfibi üstünlük, sahil işgali
        Rangers,            // Kayıkçılar: Keşif, arka çizgi operasyonları
        MountainTroops,     // Dağ piyadeleri: Dağ arazi avantajı
        DesertTroops,       // Çöl piyadeleri: Çöl arazi avantajı
        ArcticTroops,       // Kutup piyadeleri: Kar/buz avantajı
        UrbanWarfare,       // Şehir savaşçısı: Şehir avantajı
        SpecialOps,         // Özel operasyonlar: İstihbarat, hedef vurma
        PsychologicalOps    // Psikolojik operasyonlar: Propaganda, deşarj
    }

    /// <summary>Gençlik seviyeleri (Green → Legendary).</summary>
    public enum VeterancyLevel
    {
        Green = 0,      // 0-49 exp: %0 bonus
        Regular = 1,    // 50-149 exp: %5 bonus
        Veteran = 2,    // 150-299 exp: %15 bonus
        Elite = 3,      // 300-549 exp: %30 bonus
        Guard = 4,      // 550-999 exp: %50 bonus
        Legendary = 5   // 1000+ exp: %80 bonus
    }

    /// <summary>İkmal hattı durumu.</summary>
    public enum SupplyLineStatus
    {
        Intact,         // Sağlam
        Interdicted,    // Interdiction altındayken (parça parça)
        Severed,        // Kesilmiş
        InterdictedHeavy, // Ağır interdiction
        Rerouted        // Yeniden yönlendirilmiş (uzun, yavaş)
    }

    /// <summary>İkmal hattı interdictor türleri.</summary>
    public enum InterdictionType
    {
        Air,        // Hava interdiction (bombardıman)
        Naval,      // Deniz interdiction (blokaj)
        Ground,     // Karada interdiction (partizan, komando)
        Cyber,      // Siber interdiction (lojistik yazılımı hack)
        Electronic  // Elektronik savaş (radar, iletişim kesme)
    }

    /// <summary>Morale detaylı bileşenleri.</summary>
    public class MoraleBreakdown
    {
        public int BaseMorale = 50;
        public int LeadershipBonus = 0;
        public int ExperienceBonus = 0;
        public int SupplyBonus = 0;
        public int CasualtyPenalty = 0;
        public int VictoryBonus = 0;
        public int DefeatPenalty = 0;
        public int EntrenchmentBonus = 0;
        public int FlankPenalty = 0;
        public int SpecialForcesBonus = 0;
        public int PropagandaEffect = 0;
        public int PsychologicalPenalty = 0;

        public int Total => BaseMorale + LeadershipBonus + ExperienceBonus + SupplyBonus
            - CasualtyPenalty + VictoryBonus - DefeatPenalty
            + EntrenchmentBonus - FlankPenalty + SpecialForcesBonus
            + PropagandaEffect - PsychologicalPenalty;

        public int ClampedTotal => Math.Clamp(Total, MinMorale, MaxMorale);
    }

    /// <summary>Ordu morali ve deneyim durumu.</summary>
    public class ArmyMoraleState
    {
        public string ArmyId = "";
        public string CountryId = "";
        public int Morale = DefaultMorale;           // 0-100
        public int Experience = 0;                   // 0-1000
        public VeterancyLevel Veterancy = VeterancyLevel.Green;
        public int CombatDays = 0;                   // Savaş günü sayısı
        public int Victories = 0;                    // Kazanılan savaş sayısı
        public int Defeats = 0;                      // Kaybedilen savaş sayısı
        public long TotalCasualtiesTaken = 0;        // Alınan toplam kayıp (kişi)
        public long TotalCasualtiesInflicted = 0;    // Verilen toplam kayıp
        public int DaysSinceLastCombat = 0;          // Son savaşın üzerinden geçen gün
        public int DaysSinceLastSupply = 0;          // Son ikmalın üzerinden geçen gün
        public float CurrentSupplyLevel = 100f;      // 0-100
        public int ConsecutiveVictories = 0;         // Ardışık zafer sayısı
        public int ConsecutiveDefeats = 0;           // Ardışık mağlubiyet sayısı
        public bool IsEntrenched = false;            // Kazık kazılmış mı
        public bool IsFlanked = false;               // Yan vuruluyor mu
        public bool IsSurrounded = false;            // Kuşatılmış mı
        public int EntrenchmentLevel = 0;            // Kazı seviyesi (0-5)
        public int LastCombatDay = -999;             // Son savaş günü
        public int LastSupplyDay = -999;             // Son ikmal günü
        public Dictionary<MoraleFactor, int> ActiveFactors = new(); // Aktif morale faktörleri ve süreleri
        public SpecialForceType SpecialForceType = SpecialForceType.None;
        public int SpecialForceLevel = 0;            // Özel kuvvet seviyesi (0-5)
        public int SpecialForceExperience = 0;       // Özel kuvvet deneyimi

        // Psikolojik durum
        public bool IsDemoralized => Morale < CriticalMoraleThreshold;
        public bool IsShaken => Morale < LowMoraleThreshold;
        public bool IsSteady => Morale >= 50;
        public bool IsConfident => Morale >= 75;
        public bool IsFanatical => Morale >= 90;
    }

    /// <summary>İkmal hattı durumu.</summary>
    public class SupplyLine
    {
        public string Id = "";
        public string ArmyId = "";
        public string SourceProvinceId = "";    // Kaynak (ikmal deposu/liman/havaalanı)
        public string TargetProvinceId = "";    // Hedef (ordu konumu)
        public List<string> PathProvinceIds = new(); // Rotadaki iller
        public SupplyLineStatus Status = SupplyLineStatus.Intact;
        public float Integrity = 100f;          // 0-100 (hassasiyet/hasar)
        public float Capacity = 100f;           // Kapasite (0-100)
        public float CurrentFlow = 100f;        // Mevcut akış (0-100)
        public int Length = 0;                  // Mesafe (il sayısı)
        public bool IsMaritime = false;         // Deniz hattı mı
        public bool IsAirBridge = false;        // Hava köprüsü mü
        public int InterdictionLevel = 0;       // Interdiction seviyesi (0-100)
        public Dictionary<InterdictionType, int> ActiveInterdictions = new(); // Aktif interdiction'lar
        public int DaysSinceLastAttack = 0;
        public float DailyCapacityLoss = 0f;    // Günlük kapasite kaybı
        public string InterdictorCountryId = ""; // Interdiction yapan ülke
        public long LastAttackDay = -999;
        public int ConsecutiveAttackDays = 0;
        public float RepairRate = 5f;           // Günde onarım hızı
        public bool IsAutomatedRepair = true;   // Otomatik onarım
        public float ConvoyEfficiency = 1f;     // Konvoy verimliliği (0-1)
    }

    /// <summary>İkmal deposu/merkezi.</summary>
    public class SupplyDepot
    {
        public string Id = "";
        public string ProvinceId = "";
        public string CountryId = "";
        public string Name = "";
        public float Capacity = 1000f;          // Toplam kapasite (ton/gün)
        public float CurrentStock = 1000f;      // Mevcut stok
        public float DailyProduction = 100f;    // Günlük üretim
        public float MaxOutput = 500f;          // Günlük maksimum çıkış
        public bool IsPort = false;             // Liman mı
        public bool IsAirfield = false;         // Havaalanı mı
        public bool IsRailHub = false;          // Demir yolu merkezi mi
        public float FortificationLevel = 0f;   // Savunma seviyesi (0-100)
        public int GarrisonDivisions = 0;       // Garnizon tümen sayısı
        public List<string> ConnectedArmyIds = new(); // Bağlı ordular
        public float DailyDistribution = 0f;    // Günlük dağıtım
        public bool IsStrategic = false;        // Stratejik depo mu
        public float Vulnerability = 0f;        // Saldırıya karşı savunmasızlık (0-100)
    }

    /// <summary>Özel kuvvet birliği.</summary>
    public class SpecialForceUnit
    {
        public string Id = "";
        public string ArmyId = "";              // Bağlı ordu (opsiyonel)
        public string CountryId = "";
        public string Name = "";
        public SpecialForceType Type = SpecialForceType.None;
        public int Level = 1;                   // 1-5
        public int Experience = 0;              // Deneyim
        public int Manpower = 100;              // Personel sayısı
        public int MaxManpower = 100;
        public int EquipmentLevel = 1;          // Ekipman seviyesi (1-5)
        public int TrainingLevel = 1;           // Eğitim seviyesi (1-5)
        public int Morale = 75;                 // Başlangıç morali (özel kuvvetler daha yüksek başlar)
        public int Readiness = 100;             // Hazırbulunuşluk (0-100)
        public bool IsDeployed = false;         // Görevde mi
        public string CurrentMission = "";      // Mevcut görev
        public string TargetProvinceId = "";    // Hedef il
        public int MissionDaysRemaining = 0;    // Görev tamamlanma kalan gün
        public Dictionary<string, int> Capabilities = new(); // Yetenekler (sabotaj, keşif, vs.)
        public int SuccessfulMissions = 0;
        public int FailedMissions = 0;
        public long LastMissionDay = -999;
        public bool IsAvailable => !IsDeployed && Readiness >= 70 && Manpower >= MaxManpower / 2;
    }

    /// <summary>İkmal hattı interdiction (kesme) kaydı.</summary>
    public class InterdictionRecord
    {
        public string SupplyLineId = "";
        public InterdictionType Type = InterdictionType.Air;
        public string AttackerCountryId = "";
        public int Intensity = 0;               // 1-100
        public long StartDay = 0;
        public int DurationDays = 0;
        public float DamagePerDay = 0f;
        public bool IsActive = true;
        public string Description = "";
    }

    /// <summary>Psikolojik savaş etkileri.</summary>
    public class PsyOpsEffect
    {
        public string Id = "";
        public string SourceCountryId = "";
        public string TargetCountryId = "";
        public string TargetArmyId = "";
        public PsyOpsType Type = PsyOpsType.Propaganda;
        public int Intensity = 0;               // 1-100
        public long StartDay = 0;
        public int DurationDays = 0;
        public float MoraleImpactPerDay = 0f;   // Günlük morale etkisi
        public float StabilityImpactPerDay = 0f; // Günlük istikrar etkisi
        public bool IsActive = true;
        public string Description = "";
    }

    public enum PsyOpsType
    {
        Propaganda,       // Genel propaganda
        LeafletDrop,      // Broşür atma
        RadioBroadcast,   // Radyo yayını
        CyberPsyOps,      // Siber psikolojik operasyon
        Deception,        // Aldatmaca/Deception
        ThreatBroadcast,  // Tehdit yayını
        SurrenderAppeal   // Teslim olma çağrısı
    }
    // ===== State =====
    private static bool _initialized;
    private static readonly Dictionary<string, ArmyMoraleState> _armyStates = new();
    private static readonly Dictionary<string, SupplyLine> _supplyLines = new();
    private static readonly Dictionary<string, SupplyDepot> _supplyDepots = new();
    private static readonly Dictionary<string, SpecialForceUnit> _specialForces = new();
    private static readonly List<InterdictionRecord> _activeInterdictions = new();
    private static readonly List<PsyOpsEffect> _activePsyOps = new();
    private static int _nextSupplyLineId = 1;
    private static int _nextDepotId = 1;
    private static int _nextSpecialForceId = 1;
    private static int _nextInterdictionId = 1;
    private static int _nextPsyOpsId = 1;

    // ===== Public API =====

    public static bool IsInitialized => _initialized;
    public static IReadOnlyDictionary<string, ArmyMoraleState> ArmyStates => _armyStates;
    public static IReadOnlyDictionary<string, SupplyLine> SupplyLines => _supplyLines;
    public static IReadOnlyDictionary<string, SupplyDepot> SupplyDepots => _supplyDepots;
    public static IReadOnlyDictionary<string, SpecialForceUnit> SpecialForces => _specialForces;
    public static IReadOnlyList<InterdictionRecord> ActiveInterdictions => _activeInterdictions;
    public static IReadOnlyList<PsyOpsEffect> ActivePsyOps => _activePsyOps;

    /// <summary>Sistemi başlatır.</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        GD.Print("[ArmyMoraleSystem] Initialized — Enhanced morale, supply lines, special forces ready.");
    }

    /// <summary>Yeni oyun için sistemleri sıfırlar.</summary>
    public static void ResetForNewGame()
    {
        _armyStates.Clear();
        _supplyLines.Clear();
        _supplyDepots.Clear();
        _specialForces.Clear();
        _activeInterdictions.Clear();
        _activePsyOps.Clear();
        _nextSupplyLineId = 1;
        _nextDepotId = 1;
        _nextSpecialForceId = 1;
        _nextInterdictionId = 1;
        _nextPsyOpsId = 1;
        _initialized = true;
        GD.Print("[ArmyMoraleSystem] Yeni oyun için sıfırlandı.");
    }

    /// <summary>Senaryo değişince sistemleri sıfırlar.</summary>
    public static void ResetForScenario()
    {
        ResetForNewGame();
    }

    /// <summary>Ordu morali durumunu getirir (yoksa oluşturur).</summary>
    public static ArmyMoraleState GetOrCreateState(string armyId, string countryId = "")
    {
        if (string.IsNullOrEmpty(armyId)) return null;

        if (!_armyStates.TryGetValue(armyId, out var state))
        {
            state = new ArmyMoraleState
            {
                ArmyId = armyId,
                CountryId = countryId,
                Morale = DefaultMorale,
                Experience = 0,
                Veterancy = VeterancyLevel.Green
            };
            _armyStates[armyId] = state;
        }
        if (string.IsNullOrEmpty(state.CountryId) && !string.IsNullOrEmpty(countryId))
            state.CountryId = countryId;
        return state;
    }

    /// <summary>Ordu morali detaylı hesaplaması (MoraleBreakdown).</summary>
    public static MoraleBreakdown CalculateMoraleBreakdown(string armyId)
    {
        var state = GetOrCreateState(armyId);
        if (state == null) return new MoraleBreakdown { BaseMorale = DefaultMorale };

        var breakdown = new MoraleBreakdown { BaseMorale = BaseMorale };

        // Liderlik bonusu (general yeteneği)
        var army = ArmySystem.Armies.FirstOrDefault(a => a.Id == armyId);
        if (army != null)
        {
            breakdown.LeadershipBonus = (army.GeneralSkill - 3) * 3; // Skill 1-5, baz 3 = 0
            breakdown.LeadershipBonus = Math.Clamp(breakdown.LeadershipBonus, -10, 10);
        }

        // Deneyim bonusu
        var vLevel = (int)state.Veterancy;
        breakdown.ExperienceBonus = vLevel * 4; // Green=0, Regular=4, Veteran=8, Elite=12, Guard=16, Legendary=20

        // İkmal bonusu/cezası
        float supplyLevel = SupplySystem.GetSupplyLevel(state.ArmyId);
        if (supplyLevel >= 80) breakdown.SupplyBonus = 5;
        else if (supplyLevel >= 60) breakdown.SupplyBonus = 2;
        else if (supplyLevel >= 40) breakdown.SupplyBonus = 0;
        else if (supplyLevel >= 20) breakdown.SupplyBonus = -5;
        else breakdown.SupplyBonus = -15;

        // Kayıp cezası
        if (state.TotalCasualtiesTaken > 0)
        {
            float casualtyRate = (float)state.TotalCasualtiesTaken / Math.Max(1, (army?.DivisionIds.Count ?? 1) * 1000);
            breakdown.CasualtyPenalty = -(int)Math.Clamp(casualtyRate * 100, 0, 30);
        }

        // Zafer/mağlubiyet bonusu/cezası
        breakdown.VictoryBonus = Math.Min(state.ConsecutiveVictories * 3, 15);
        breakdown.DefeatPenalty = -Math.Min(state.ConsecutiveDefeats * 5, 25);

        // Kazı bonusu
        breakdown.EntrenchmentBonus = state.EntrenchmentLevel * 2; // Seviye başına +2

        // Yan vurma cezası
        if (state.IsFlanked) breakdown.FlankPenalty = -15;
        if (state.IsSurrounded) breakdown.FlankPenalty -= 20;

        // Özel kuvvet bonusu
        if (state.SpecialForceType != SpecialForceType.None)
        {
            breakdown.SpecialForcesBonus = 5 + state.SpecialForceLevel * 2; // +5 to +15
        }

        // Aktif faktörlerden etki
        foreach (var kv in state.ActiveFactors)
        {
            switch (kv.Key)
            {
                case MoraleFactor.Propaganda: breakdown.PropagandaEffect += kv.Value; break;
                case MoraleFactor.TerrorWeapon:
                case MoraleFactor.ChemicalWeapon:
                case MoraleFactor.NuclearStrike:
                    breakdown.PsychologicalPenalty -= Math.Min(kv.Value, 30); break;
            }
        }

        // Psikolojik cezalar
        if (state.TotalCasualtiesTaken > 10000) breakdown.PsychologicalPenalty -= 10;

        // Toplam ve clamp
        return breakdown;
    }

    /// <summary>Ordu morali güncelle (DailyTick'te çağrılır).</summary>
    public static void UpdateMorale(string armyId, long currentDay)
    {
        var state = GetOrCreateState(armyId);
        if (state == null) return;

        var breakdown = CalculateMoraleBreakdown(armyId);
        int targetMorale = breakdown.ClampedTotal;

        // Morale yavaşça hedefe doğru hareket eder
        int currentMorale = state.Morale;
        int diff = targetMorale - currentMorale;

        if (diff > 0)
            state.Morale = Math.Min(currentMorale + (int)Math.Ceiling(Math.Abs(diff) * 0.3), targetMorale);
        else if (diff < 0)
            state.Morale = Math.Max(currentMorale + (int)Math.Floor(diff * 0.4), targetMorale);

        // Clamp
        state.Morale = Math.Clamp(state.Morale, MinMorale, MaxMorale);

        // Deneyim kazanımı (günlük pasif)
        if (state.CombatDays > 0)
            state.Experience = Math.Min(MaxExperience, state.Experience + 1);

        // Veteranlık güncelle
        UpdateVeterancy(state);

        // Aktif faktör sürelerini azalt
        var expiredFactors = state.ActiveFactors.Where(kv => kv.Value <= 1).Select(kv => kv.Key).ToList();
        foreach (var key in expiredFactors)
            state.ActiveFactors.Remove(key);

        foreach (var key in state.ActiveFactors.Keys.ToList())
            state.ActiveFactors[key]--;

        // Ardışık zafer/mağlubiyet sıfırlama (uzun süre savaş yoksa)
        if (state.DaysSinceLastCombat > 30)
        {
            state.ConsecutiveVictories = 0;
            state.ConsecutiveDefeats = 0;
        }

        state.DaysSinceLastCombat++;
        state.DaysSinceLastSupply++;
    }

    private static void UpdateVeterancy(ArmyMoraleState state)
    {
        int newLevel = 0;
        for (int i = VeterancyThresholds.Length - 1; i >= 0; i--)
        {
            if (state.Experience >= VeterancyThresholds[i])
            {
                newLevel = i;
                break;
            }
        }
        if (newLevel != (int)state.Veterancy)
        {
            var oldLevel = state.Veterancy;
            state.Veterancy = (VeterancyLevel)newLevel;
            GD.Print($"[ArmyMorale] Ordu veteranlık seviyesi değişti: {oldLevel} -> {state.Veterancy} (Exp: {state.Experience})");
        }
    }

    /// <summary>Savaş sonucu morale/experience güncellemesi.</summary>
    public static void ApplyCombatResult(string armyId, bool won, int casualtiesTaken, int casualtiesInflicted, int intensityBonus = 0, long currentDay = 0)
    {
        var state = GetOrCreateState(armyId);
        if (state == null) return;

        state.CombatDays++;
        state.LastCombatDay = (int)currentDay;
        state.DaysSinceLastCombat = 0;

        state.TotalCasualtiesTaken += casualtiesTaken;
        state.TotalCasualtiesInflicted += casualtiesInflicted;

        // Deneyim kazanımı
        long expGain = ExpPerCombatDay;
        if (won)
        {
            expGain += ExpPerVictory;
            state.Victories++;
            state.ConsecutiveVictories++;
            state.ConsecutiveDefeats = 0;
        }
        else
        {
            expGain += ExpPerDefeat;
            state.Defeats++;
            state.ConsecutiveDefeats++;
            state.ConsecutiveVictories = 0;
        }
        expGain += casualtiesInflicted * ExpPerCasualtyInflicted / 100;
        expGain += casualtiesTaken * ExpPerCasualtyTaken / 100;
        expGain += intensityBonus;

        state.Experience = Math.Min(MaxExperience, state.Experience + (int)expGain);
        UpdateVeterancy(state);

        // Morale etkisi
        int moraleDelta = won ? (3 + intensityBonus) : (-5 - intensityBonus);
        moraleDelta += (int)(casualtiesInflicted - casualtiesTaken) / 100; // Net kayıp farkı
        ApplyMoraleFactor(state.ArmyId, won ? MoraleFactor.Victory : MoraleFactor.Defeat, Math.Abs(moraleDelta), 5);

        // Kayıp cezası
        if (casualtiesTaken > 100)
            ApplyMoraleFactor(state.ArmyId, MoraleFactor.HighCasualties, casualtiesTaken / 500, 10);

        GD.Print($"[ArmyMorale] {armyId}: Combat {(won ? "WON" : "LOST")} moraleΔ={moraleDelta} exp+{expGain} (total={state.Experience}, vet={state.Veterancy})");
    }

    /// <summary>Morale faktörü uygula (süreli etki).</summary>
    public static void ApplyMoraleFactor(string armyId, MoraleFactor factor, int intensity, int durationDays)
    {
        var state = GetOrCreateState(armyId);
        if (state == null) return;

        if (!state.ActiveFactors.ContainsKey(factor))
            state.ActiveFactors[factor] = 0;
        state.ActiveFactors[factor] = Math.Max(state.ActiveFactors[factor], durationDays);

        // Anlık morale etkisi
        int immediateEffect = intensity * (factor switch
        {
            MoraleFactor.Victory => 1,
            MoraleFactor.Defeat => -1,
            MoraleFactor.HighCasualties => -1,
            MoraleFactor.LowCasualties => 1,
            MoraleFactor.SupplyShortage => -1,
            MoraleFactor.GoodSupply => 1,
            MoraleFactor.GoodLeadership => 1,
            MoraleFactor.PoorLeadership => -1,
            MoraleFactor.HighExperience => 1,
            MoraleFactor.LowExperience => -1,
            MoraleFactor.Entrenched => 1,
            MoraleFactor.Flanked => -1,
            MoraleFactor.Surrounded => -2,
            MoraleFactor.Breakthrough => 2,
            MoraleFactor.Propaganda => 1,
            MoraleFactor.SpecialForcesBonus => 1,
            MoraleFactor.TerrorWeapon => -2,
            MoraleFactor.ChemicalWeapon => -2,
            MoraleFactor.NuclearStrike => -3,
            _ => 0
        });

        if (immediateEffect != 0)
        {
            int delta = intensity * immediateEffect;
            var moraleState = GetOrCreateState(armyId);
            moraleState.Morale = Math.Clamp(moraleState.Morale + delta, MinMorale, MaxMorale);
        }

        GD.Print($"[ArmyMorale] Faktör uygulandı: {factor} şiddet={intensity} süre={durationDays}gün -> moraleΔ={intensity * (factor switch { MoraleFactor.Victory => 1, MoraleFactor.Defeat => -1, _ => 0 })}");
    }

    /// <summary>Ordu moralini getir (0-100).</summary>
    public static int GetMorale(string armyId)
    {
        return GetOrCreateState(armyId)?.Morale ?? DefaultMorale;
    }

    /// <summary>Morale detaylı bileşenleri getir.</summary>
    public static MoraleBreakdown GetMoraleBreakdown(string armyId)
    {
        return CalculateMoraleBreakdown(armyId);
    }

    /// <summary>Veteranlık seviyesini getir.</summary>
    public static VeterancyLevel GetVeterancy(string armyId)
    {
        return GetOrCreateState(armyId)?.Veterancy ?? VeterancyLevel.Green;
    }

    /// <summary>Deneyim puanını getir.</summary>
    public static int GetExperience(string armyId)
    {
        return GetOrCreateState(armyId)?.Experience ?? 0;
    }

    /// <summary>Savaş verimlilik çarpanı (morale + veteränlık + ikmal + özel kuvvet).</summary>
    public static float GetCombatEffectivenessMultiplier(string armyId)
    {
        var state = GetOrCreateState(armyId);
        if (state == null) return 1f;

        // Morale çarpanı
        float moraleMult = state.Morale switch
        {
            >= 90 => 1.3f,
            >= 75 => 1.15f,
            >= 50 => 1.0f,
            >= 30 => 0.85f,
            >= 15 => 0.7f,
            _ => 0.5f
        };

        // Veteranlık çarpanı
        float vetMult = state.Veterancy switch
        {
            VeterancyLevel.Green => 1.0f,
            VeterancyLevel.Regular => 1.05f,
            VeterancyLevel.Veteran => 1.15f,
            VeterancyLevel.Elite => 1.3f,
            VeterancyLevel.Guard => 1.5f,
            VeterancyLevel.Legendary => 1.8f,
            _ => 1.0f
        };

        // İkmal çarpanı
        float supplyLevel = SupplySystem.GetSupplyLevel(state.ArmyId);
        float supplyMult = Math.Clamp(supplyLevel / 100f, 0.5f, 1.2f);

        // Özel kuvvet bonusu
        float sfMult = 1f;
        if (state.SpecialForceType != SpecialForceType.None)
            sfMult = 1f + 0.05f * state.SpecialForceLevel; // Seviye başına %5

        // Psikolojik durum
        float psyMult = 1f;
        if (state.Morale <= 10) psyMult = 0.4f;
        else if (state.Morale <= 20) psyMult = 0.6f;
        else if (state.Morale <= 35) psyMult = 0.8f;

        return moraleMult * vetMult * supplyMult * sfMult * psyMult;
    }

    // ===== Supply Lines (İkmal Hatları) =====

    /// <summary>Yeni ikmal hattı oluştur.</summary>
    public static SupplyLine CreateSupplyLine(string armyId, string sourceProvinceId, string targetProvinceId, List<string> path, bool isMaritime = false, bool isAirBridge = false)
    {
        var line = new SupplyLine
        {
            Id = $"sl_{_nextSupplyLineId++}",
            ArmyId = armyId,
            SourceProvinceId = sourceProvinceId,
            TargetProvinceId = targetProvinceId,
            PathProvinceIds = path,
            Status = SupplyLineStatus.Intact,
            Integrity = 100f,
            Capacity = 100f,
            CurrentFlow = 100f,
            Length = path.Count,
            IsMaritime = isMaritime,
            IsAirBridge = isAirBridge,
            RepairRate = isMaritime ? 3f : 5f,
            ConvoyEfficiency = 1f
        };

        _supplyLines[line.Id] = line;
        GD.Print($"[SupplyLine] Created: {line.Id} ({sourceProvinceId} -> {targetProvinceId}) len={path.Count} maritime={isMaritime} airBridge={isAirBridge}");
        return line;
    }

    /// <summary>Otomatik ikmal hattı oluştur (en kısa yol).</summary>
    public static SupplyLine AutoCreateSupplyLine(string armyId, string sourceProvinceId, string targetProvinceId, bool preferMaritime = false)
    {
        // Basit BFS ile en kısa yol bul (gerçekte A* kullanılır)
        var path = FindShortestPath(sourceProvinceId, targetProvinceId, preferMaritime);
        bool isMaritime = path.Any(p => IsCoastalProvince(p)) && preferMaritime;
        bool isAirBridge = path.Count > 10; // Uzun mesafe -> hava köprüsü

        return CreateSupplyLine(armyId, sourceProvinceId, targetProvinceId, path, isMaritime, isAirBridge);
    }

    private static bool IsCoastalProvince(string provinceId)
    {
        // Basit kontrol: Kıyı illeri (IsCoastal flag kullanılır)
        var prov = WorldMap.Instance?.GetProvinceById(provinceId);
        return prov != null && prov.IsCoastal;
    }

    private static List<string> FindShortestPath(string source, string target, bool preferMaritime)
    {
        // Basit BFS (gerçekte A* + terrain cost)
        var visited = new HashSet<string>();
        var queue = new Queue<(string province, List<string> path)>();
        queue.Enqueue((source, new List<string> { source }));
        visited.Add(source);

        while (queue.Count > 0)
        {
            var (current, path) = queue.Dequeue();
            if (current == target) return path;

            var prov = WorldMap.Instance?.GetProvinceById(current);
            if (prov == null) continue;

            foreach (var neighbor in prov.NeighborIds ?? new List<string>())
            {
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    var newPath = new List<string>(path) { neighbor };
                    queue.Enqueue((neighbor, newPath));
                }
            }
        }
        return new List<string> { source, target }; // Fallback
    }

    /// <summary>İkmal hattı durumunu getir.</summary>
    public static SupplyLine GetSupplyLine(string lineId)
    {
        _supplyLines.TryGetValue(lineId, out var line);
        return line;
    }

    /// <summary>Ordunun ikmal hatlarını getir.</summary>
    public static List<SupplyLine> GetArmySupplyLines(string armyId)
    {
        return _supplyLines.Values.Where(l => l.ArmyId == armyId).ToList();
    }

    /// <summary>İkmal hattı günlük güncelleme (DailyTick).</summary>
    public static void DailyTickSupplyLines(long currentDay)
    {
        foreach (var line in _supplyLines.Values.ToList())
        {
            // Onarım
            if (line.Status != SupplyLineStatus.Intact && line.IsAutomatedRepair)
            {
                line.Integrity = Math.Min(100f, line.Integrity + line.RepairRate);
                if (line.Integrity >= 95f)
                {
                    line.Status = SupplyLineStatus.Intact;
                    line.InterdictionLevel = 0;
                    line.ActiveInterdictions.Clear();
                    line.InterdictorCountryId = "";
                }
            }

            // Interdiction etkisi
            float totalInterdiction = 0f;
            foreach (var kv in line.ActiveInterdictions)
            {
                totalInterdiction += kv.Value;
            }
            line.InterdictionLevel = (int)Math.Clamp(totalInterdiction, 0, 100);

            // Kapasite kaybı
            float interdictionDamage = line.InterdictionLevel * 0.5f; // %0.5 per level
            line.Capacity = Math.Max(0f, line.Capacity - interdictionDamage);
            line.DailyCapacityLoss = interdictionDamage;

            // Akış hesapla
            line.CurrentFlow = line.Capacity * line.ConvoyEfficiency * (line.Integrity / 100f);

            // Status güncelle
            if (line.Capacity <= 0)
                line.Status = SupplyLineStatus.Severed;
            else if (line.InterdictionLevel >= 70)
                line.Status = SupplyLineStatus.InterdictedHeavy;
            else if (line.InterdictionLevel >= 30)
                line.Status = SupplyLineStatus.Interdicted;
            else if (line.Status == SupplyLineStatus.Rerouted && line.Integrity >= 90)
                line.Status = SupplyLineStatus.Intact;

            // Saldırı gün sayaçları
            if (line.ConsecutiveAttackDays > 0)
            {
                if (currentDay - line.LastAttackDay > 2)
                    line.ConsecutiveAttackDays = 0;
            }
            line.DaysSinceLastAttack = (int)(currentDay - line.LastAttackDay);
        }
    }

    /// <summary>İkmal hattına interdiction (kesme/saldırı) uygula.</summary>
    public static InterdictionRecord InterdictSupplyLine(string lineId, InterdictionType type, string attackerCountryId, int intensity, int durationDays, float damagePerDay, string description = "")
    {
        if (!_supplyLines.TryGetValue(lineId, out var line)) return null;

        var record = new InterdictionRecord
        {
            SupplyLineId = lineId,
            Type = type,
            AttackerCountryId = attackerCountryId,
            Intensity = Math.Clamp(intensity, 1, 100),
            StartDay = GameManager.Instance?.TotalDays ?? 0,
            DurationDays = durationDays,
            DamagePerDay = damagePerDay,
            IsActive = true,
            Description = description
        };

        line.ActiveInterdictions[type] = intensity;
        line.LastAttackDay = GameManager.Instance?.TotalDays ?? 0;
        line.ConsecutiveAttackDays++;
        line.InterdictorCountryId = attackerCountryId;
        line.LastAttackDay = GameManager.Instance?.TotalDays ?? 0;

        _activeInterdictions.Add(record);

        // İkmal hattı hasarı
        line.Integrity = Math.Max(0f, line.Integrity - intensity * 0.5f);
        line.Capacity = Math.Max(0f, line.Capacity - intensity * 0.3f);

        GD.Print($"[SupplyLine] Interdiction: {lineId} by {attackerCountryId} type={type} intensity={intensity} -> integrity={line.Integrity:F1} capacity={line.Capacity:F1}");
        return record;
    }

    /// <summary>İkmal hattını manuel onar/tamir et.</summary>
    public static bool RepairSupplyLine(string lineId, float repairAmount = 20f)
    {
        if (!_supplyLines.TryGetValue(lineId, out var line)) return false;
        if (line.Status == SupplyLineStatus.Intact) return true;

        line.Integrity = Math.Min(100f, line.Integrity + repairAmount);
        line.Capacity = Math.Min(100f, line.Capacity + repairAmount * 0.5f);
        if (line.Integrity >= 90f && line.Capacity >= 80f)
        {
            line.Status = SupplyLineStatus.Intact;
            line.InterdictionLevel = 0;
            line.ActiveInterdictions.Clear();
            line.InterdictorCountryId = "";
        }
        else if (line.Status == SupplyLineStatus.Severed)
        {
            line.Status = SupplyLineStatus.Rerouted;
        }

        GD.Print($"[SupplyLine] Repaired: {lineId} integrity={line.Integrity:F1} capacity={line.Capacity:F1} status={line.Status}");
        return true;
    }

    /// <summary>İkmal hattını yeniden yönlendir (alternatif rota).</summary>
    public static SupplyLine RerouteSupplyLine(string oldLineId, string newSourceId, string newTargetId, List<string> newPath)
    {
        if (!_supplyLines.TryGetValue(oldLineId, out var oldLine)) return null;

        oldLine.Status = SupplyLineStatus.Rerouted;

        var newLine = CreateSupplyLine(oldLine.ArmyId, newSourceId, newTargetId, newPath);
        newLine.Status = SupplyLineStatus.Rerouted;
        newLine.Integrity = 50f; // Yeniden yönlendirme hasarlı başlar
        newLine.Capacity = 60f;

        GD.Print($"[SupplyLine] Rerouted: {oldLineId} -> {newLine.Id}");
        return newLine;
    }

    // ===== Supply Depots (İkmal Depoları) =====

    public static SupplyDepot CreateSupplyDepot(string provinceId, string countryId, string name, bool isPort = false, bool isAirfield = false, bool isRailHub = false, float capacity = 1000f)
    {
        var depot = new SupplyDepot
        {
            Id = $"depot_{_nextDepotId++}",
            ProvinceId = provinceId,
            CountryId = countryId,
            Name = name,
            Capacity = capacity,
            CurrentStock = capacity,
            IsPort = isPort,
            IsAirfield = isAirfield,
            IsRailHub = isRailHub,
            DailyProduction = isPort ? 200f : (isAirfield ? 150f : (isRailHub ? 300f : 100f)),
            MaxOutput = isPort ? 800f : (isAirfield ? 400f : (isRailHub ? 600f : 300f))
        };

        _supplyDepots[depot.Id] = depot;
        GD.Print($"[SupplyDepot] Created: {depot.Id} ({name}) at {provinceId} capacity={capacity} port={isPort} airfield={isAirfield} rail={isRailHub}");
        return depot;
    }

    public static SupplyDepot GetSupplyDepot(string depotId)
    {
        _supplyDepots.TryGetValue(depotId, out var depot);
        return depot;
    }

    public static List<SupplyDepot> GetCountryDepots(string countryId)
    {
        return _supplyDepots.Values.Where(d => d.CountryId == countryId).ToList();
    }

    // ===== Special Forces (Özel Kuvvetler) =====

    public static SpecialForceUnit CreateSpecialForce(string countryId, SpecialForceType type, string name = "", int level = 1)
    {
        var unit = new SpecialForceUnit
        {
            Id = $"sf_{_nextSpecialForceId++}",
            CountryId = countryId,
            Name = string.IsNullOrEmpty(name) ? GenerateSpecialForceName(type) : name,
            Type = type,
            Level = Math.Clamp(level, 1, 5),
            Manpower = 100,
            MaxManpower = 100,
            EquipmentLevel = 1,
            TrainingLevel = 1,
            Morale = 75,
            Readiness = 100,
            Capabilities = GetDefaultCapabilities(type)
        };

        _specialForces[unit.Id] = unit;
        GD.Print($"[SpecialForces] Created: {unit.Id} ({unit.Name}) type={type} level={level}");
        return unit;
    }

    private static Dictionary<string, int> GetDefaultCapabilities(SpecialForceType type)
    {
        return type switch
        {
            SpecialForceType.Paratroopers => new Dictionary<string, int> { { "airborne_assault", 3 }, { "behind_lines", 2 }, { "rapid_deployment", 3 } },
            SpecialForceType.Commandos => new Dictionary<string, int> { { "sabotage", 3 }, { "stealth", 3 }, { "targeted_strike", 2 }, { "intelligence", 2 } },
            SpecialForceType.Marines => new Dictionary<string, int> { { "amphibious_assault", 3 }, { "coastal_raid", 2 }, { "ship_boarding", 2 } },
            SpecialForceType.Rangers => new Dictionary<string, int> { { "reconnaissance", 3 }, { "long_range_patrol", 3 }, { "stealth", 2 } },
            SpecialForceType.MountainTroops => new Dictionary<string, int> { { "mountain_warfare", 3 }, { "cold_weather", 2 }, { "high_altitude", 2 } },
            SpecialForceType.DesertTroops => new Dictionary<string, int> { { "desert_warfare", 3 }, { "heat_endurance", 2 }, { "navigation", 2 } },
            SpecialForceType.ArcticTroops => new Dictionary<string, int> { { "arctic_warfare", 3 }, { "cold_endurance", 3 }, { "ski_mobility", 2 } },
            SpecialForceType.UrbanWarfare => new Dictionary<string, int> { { "urban_combat", 3 }, { "cqb", 3 }, { "building_clear", 2 } },
            SpecialForceType.SpecialOps => new Dictionary<string, int> { { "direct_action", 3 }, { "counter_terrorism", 2 }, { "hostage_rescue", 2 }, { "intelligence", 2 } },
            SpecialForceType.PsychologicalOps => new Dictionary<string, int> { { "propaganda", 3 }, { "deception", 2 }, { "leaflet_drop", 2 }, { "radio_broadcast", 2 } },
            _ => new Dictionary<string, int> { { "general", 1 } }
        };
    }

    private static string GenerateSpecialForceName(SpecialForceType type)
    {
        string[] prefixes = { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Ghost", "Shadow", "Phantom", "Viper", "Cobra", "Tiger", "Eagle", "Falcon", "Wolf" };
        string[] suffixes = { "Team", "Squad", "Unit", "Detachment", "Group", "Force", "Command", "Brigade" };

        string typeName = type switch
        {
            SpecialForceType.Paratroopers => "Airborne",
            SpecialForceType.Commandos => "Commando",
            SpecialForceType.Marines => "Marine",
            SpecialForceType.Rangers => "Ranger",
            SpecialForceType.MountainTroops => "Mountain",
            SpecialForceType.DesertTroops => "Desert",
            SpecialForceType.ArcticTroops => "Arctic",
            SpecialForceType.UrbanWarfare => "Urban",
            SpecialForceType.SpecialOps => "SpecialOps",
            SpecialForceType.PsychologicalOps => "PsyOps",
            _ => "Special"
        };

        return $"{DeterministicRNG.NextChoice(prefixes)} {typeName} {DeterministicRNG.NextChoice(suffixes)}";
    }

    public static SpecialForceUnit GetSpecialForce(string unitId)
    {
        _specialForces.TryGetValue(unitId, out var unit);
        return unit;
    }

    public static List<SpecialForceUnit> GetCountrySpecialForces(string countryId)
    {
        return _specialForces.Values.Where(u => u.CountryId == countryId).ToList();
    }

    public static bool DeploySpecialForce(string unitId, string armyId, string missionType, string targetProvinceId, int durationDays)
    {
        if (!_specialForces.TryGetValue(unitId, out var unit)) return false;
        if (!unit.IsAvailable) return false;

        var army = ArmySystem.Armies.FirstOrDefault(a => a.Id == armyId);
        if (army == null) return false;

        unit.IsDeployed = true;
        unit.ArmyId = armyId;
        unit.CurrentMission = missionType;
        unit.TargetProvinceId = targetProvinceId;
        unit.MissionDaysRemaining = durationDays;
        unit.Readiness = Math.Max(0, unit.Readiness - 20); // Görev hazırbulunuşluğu düşer

        GD.Print($"[SpecialForces] Deployed: {unit.Name} ({unit.Id}) to army {armyId} mission={missionType} target={targetProvinceId} duration={durationDays}d");
        return true;
    }

    public static void RecallSpecialForce(string unitId)
    {
        if (!_specialForces.TryGetValue(unitId, out var unit)) return;
        unit.IsDeployed = false;
        unit.ArmyId = "";
        unit.CurrentMission = "";
        unit.TargetProvinceId = "";
        unit.MissionDaysRemaining = 0;
        unit.Readiness = Math.Min(100, unit.Readiness + 10); // Dinlenme

        GD.Print($"[SpecialForces] Recalled: {unit.Name} ({unit.Id})");
    }

    public static void DailyTickSpecialForces(long currentDay)
    {
        foreach (var unit in _specialForces.Values.Where(u => u.IsDeployed).ToList())
        {
            unit.MissionDaysRemaining--;
            if (unit.MissionDaysRemaining <= 0)
            {
                // Görev tamamlandı
                bool success = DetermineMissionSuccess(unit);
                if (success)
                {
                    unit.SuccessfulMissions++;
                    unit.Experience += 10;
                    unit.Morale = Math.Min(100, unit.Morale + 5);
                }
                else
                {
                    unit.FailedMissions++;
                    unit.Experience += 2;
                    unit.Morale = Math.Max(0, unit.Morale - 10);
                }

                // Kayıp olasılığı
                if (DeterministicRNG.NextDouble() < 0.05) // %5 kayıp riski
                {
                    int loss = DeterministicRNG.NextInt(1, 10);
                    unit.Manpower = Math.Max(0, unit.Manpower - loss);
                    GD.Print($"[SpecialForces] {unit.Name} suffered {loss} casualties during mission");
                }

                unit.IsDeployed = false;
                unit.ArmyId = "";
                unit.CurrentMission = "";
                unit.TargetProvinceId = "";
                unit.LastMissionDay = currentDay;

                GD.Print($"[SpecialForces] Mission complete: {unit.Name} success={unit.SuccessfulMissions > unit.FailedMissions} exp={unit.Experience}");
            }
            else
            {
                // Görev devam ediyor - hazırbulunuşluk azalır
                unit.Readiness = Math.Max(0, unit.Readiness - 2);
            }
        }

        // Görevde olmayan birimler: hazırbulunuşluk artar
        foreach (var unit in _specialForces.Values.Where(u => !u.IsDeployed))
        {
            unit.Readiness = Math.Min(100, unit.Readiness + 5);
            if (unit.Manpower < unit.MaxManpower)
                unit.Manpower = Math.Min(unit.MaxManpower, unit.Manpower + 1); // Yavaş yeniden doldurma
        }
    }

    private static bool DetermineMissionSuccess(SpecialForceUnit unit)
    {
        double baseChance = 0.7;
        baseChance += (unit.Level - 1) * 0.05; // Level bonus
        baseChance += (unit.TrainingLevel - 1) * 0.03;
        baseChance += (unit.EquipmentLevel - 1) * 0.02;
        baseChance += unit.Morale / 100.0 * 0.2;
        baseChance += unit.Readiness / 100.0 * 0.1;
        baseChance -= (unit.FailedMissions / Math.Max(1, unit.SuccessfulMissions + unit.FailedMissions)) * 0.1;

        // Mission türüne göre bonus/ceza
        switch (unit.CurrentMission)
        {
            case "sabotage": baseChance += unit.Capabilities.GetValueOrDefault("sabotage", 0) * 0.05; break;
            case "reconnaissance": baseChance += unit.Capabilities.GetValueOrDefault("reconnaissance", 0) * 0.05; break;
            case "direct_action": baseChance += unit.Capabilities.GetValueOrDefault("direct_action", 0) * 0.05; break;
            case "intelligence": baseChance += unit.Capabilities.GetValueOrDefault("intelligence", 0) * 0.05; break;
        }

        return DeterministicRNG.NextDouble() < Math.Clamp(baseChance, 0.1, 0.95);
    }

    // ===== Interdiction (İkmal Hattı Kesme) =====

    public static void DailyTickInterdictions(long currentDay)
    {
        // Süresi dolan interdiction'ları temizle
        var expired = _activeInterdictions.Where(i => currentDay - i.StartDay > i.DurationDays).ToList();
        foreach (var record in expired)
        {
            record.IsActive = false;
            if (_supplyLines.TryGetValue(record.SupplyLineId, out var line))
            {
                line.ActiveInterdictions.Remove(record.Type);
                GD.Print($"[Interdiction] Expired: {record.SupplyLineId} type={record.Type}");
            }
        }
        _activeInterdictions.RemoveAll(r => !r.IsActive);
    }

    // ===== PsyOps (Psikolojik Savaş) =====

    public static PsyOpsEffect LaunchPsyOps(string sourceCountryId, string targetCountryId, PsyOpsType type, int intensity, int durationDays, string targetArmyId = "")
    {
        var effect = new PsyOpsEffect
        {
            Id = $"psyops_{_nextPsyOpsId++}",
            SourceCountryId = sourceCountryId,
            TargetCountryId = targetCountryId,
            TargetArmyId = targetArmyId,
            Type = type,
            Intensity = Math.Clamp(intensity, 1, 100),
            StartDay = GameManager.Instance?.TotalDays ?? 0,
            DurationDays = durationDays,
            MoraleImpactPerDay = CalculatePsyOpsMoraleImpact(type, intensity),
            StabilityImpactPerDay = CalculatePsyOpsStabilityImpact(type, intensity),
            IsActive = true,
            Description = GeneratePsyOpsDescription(type, sourceCountryId, targetCountryId)
        };

        _activePsyOps.Add(effect);
        GD.Print($"[PsyOps] Launched: {effect.Id} type={type} {sourceCountryId}->{targetCountryId} intensity={intensity} duration={durationDays}d");
        return effect;
    }

    private static float CalculatePsyOpsMoraleImpact(PsyOpsType type, int intensity)
    {
        float baseImpact = intensity * 0.1f; // intensity 100 = -10 morale/day
        return type switch
        {
            PsyOpsType.Propaganda => -baseImpact * 0.5f,
            PsyOpsType.LeafletDrop => -baseImpact * 0.3f,
            PsyOpsType.RadioBroadcast => -baseImpact * 0.7f,
            PsyOpsType.CyberPsyOps => -baseImpact * 1.0f,
            PsyOpsType.Deception => -baseImpact * 0.8f,
            PsyOpsType.ThreatBroadcast => -baseImpact * 1.2f,
            PsyOpsType.SurrenderAppeal => -baseImpact * 1.5f,
            _ => -baseImpact * 0.5f
        };
    }

    private static float CalculatePsyOpsStabilityImpact(PsyOpsType type, int intensity)
    {
        return CalculatePsyOpsMoraleImpact(type, intensity) * 0.5f; // Stability impact is half of morale
    }

    private static string GeneratePsyOpsDescription(PsyOpsType type, string source, string target)
    {
        return type switch
        {
            PsyOpsType.Propaganda => $"{source} propaganda kampanyası başlattı: {target} hedefi",
            PsyOpsType.LeafletDrop => $"{source} {target} üzerine broşür atıyor",
            PsyOpsType.RadioBroadcast => $"{source} {target} için radyo yayını başlattı",
            PsyOpsType.CyberPsyOps => $"{source} {target}'a karşı siber psikolojik operasyon başlattı",
            PsyOpsType.Deception => $"{source} {target} yanlış istihbarat yayınıyor",
            PsyOpsType.ThreatBroadcast => $"{source} {target}'ı tehdit ediyor",
            PsyOpsType.SurrenderAppeal => $"{source} {target} ordusuna teslim olma çağrısı yapıyor",
            _ => $"{source} {target} psikolojik operasyonu"
        };
    }

    public static void DailyTickPsyOps(long currentDay)
    {
        foreach (var effect in _activePsyOps.Where(p => p.IsActive).ToList())
        {
            int daysElapsed = (int)(currentDay - effect.StartDay);
            if (daysElapsed >= effect.DurationDays)
            {
                effect.IsActive = false;
                GD.Print($"[PsyOps] Expired: {effect.Id}");
                continue;
            }

            // Hedef ordu/ülkeye etki uygula
            if (!string.IsNullOrEmpty(effect.TargetArmyId))
            {
                ApplyMoraleFactor(effect.TargetArmyId, MoraleFactor.Propaganda, (int)(effect.MoraleImpactPerDay * 10), 1);
            }
            else if (!string.IsNullOrEmpty(effect.TargetCountryId))
            {
                // Ülke genelindeki orduları etkile
                var armies = ArmySystem.Armies.Where(a => a.CountryId == effect.TargetCountryId).ToList();
                foreach (var army in armies)
                {
                    ApplyMoraleFactor(army.Id, MoraleFactor.Propaganda, (int)(effect.MoraleImpactPerDay * 10), 1);
                }
            }

            // Stability etkisi (DiplomacySystem/PoliticalUnrestSystem ile entegrasyon)
            // TODO: PoliticalUnrestSystem entegrasyonu
        }

        _activePsyOps.RemoveAll(p => !p.IsActive);
    }

    // ===== Daily Tick =====

    public static void DailyTick(long currentDay)
    {
        if (!_initialized) Initialize();

        // Ordu morali güncelle
        foreach (var state in _armyStates.Values)
            UpdateMorale(state.ArmyId, currentDay);

        // İkmal hatları
        DailyTickSupplyLines(currentDay);

        // Özel kuvvetler
        DailyTickSpecialForces(currentDay);

        // Interdiction'lar
        DailyTickInterdictions(currentDay);

        // PsyOps
        DailyTickPsyOps(currentDay);
    }

    // ===== Yardımcı Metotlar =====

    public static int GetVeterancyThreshold(VeterancyLevel level)
    {
        return VeterancyThresholds[(int)level];
    }

    public static string GetVeterancyName(VeterancyLevel level)
    {
        return level switch
        {
            VeterancyLevel.Green => "Green (Yeni)",
            VeterancyLevel.Regular => "Regular (Deneyimli)",
            VeterancyLevel.Veteran => "Veteran",
            VeterancyLevel.Elite => "Elite",
            VeterancyLevel.Guard => "Guard (Garda)",
            VeterancyLevel.Legendary => "Legendary (Efsanevi)",
            _ => "Unknown"
        };
    }

    public static string GetSpecialForceTypeName(SpecialForceType type)
    {
        return type switch
        {
            SpecialForceType.Paratroopers => "Paraşütçüler",
            SpecialForceType.Commandos => "Komandolar",
            SpecialForceType.Marines => "Piyadeler (Marines)",
            SpecialForceType.Rangers => "Kayıkçılar/Rangers",
            SpecialForceType.MountainTroops => "Dağ Piyadeleri",
            SpecialForceType.DesertTroops => "Çöl Piyadeleri",
            SpecialForceType.ArcticTroops => "Kutup Piyadeleri",
            SpecialForceType.UrbanWarfare => "Şehir Savaşçıları",
            SpecialForceType.SpecialOps => "Özel Operasyonlar",
            SpecialForceType.PsychologicalOps => "Psikolojik Operasyonlar",
            _ => "Bilinmeyen"
        };
    }

    // ===== Save / Load =====

    public class ArmyMoraleStateSave
    {
        public string ArmyId = "";
        public string CountryId = "";
        public int Morale = DefaultMorale;
        public int Experience = 0;
        public int Veterancy = 0;
        public int CombatDays = 0;
        public int Victories = 0;
        public int Defeats = 0;
        public long TotalCasualtiesTaken = 0;
        public long TotalCasualtiesInflicted = 0;
        public int DaysSinceLastCombat = 0;
        public int DaysSinceLastSupply = 0;
        public float CurrentSupplyLevel = 100f;
        public int ConsecutiveVictories = 0;
        public int ConsecutiveDefeats = 0;
        public bool IsEntrenched = false;
        public bool IsFlanked = false;
        public bool IsSurrounded = false;
        public int EntrenchmentLevel = 0;
        public int LastCombatDay = -999;
        public int LastSupplyDay = -999;
        public int SpecialForceType = 0;
        public int SpecialForceLevel = 0;
        public int SpecialForceExperience = 0;
        public Dictionary<string, int> ActiveFactors = new();
    }

    public class SupplyLineSave
    {
        public string Id = "";
        public string ArmyId = "";
        public string SourceProvinceId = "";
        public string TargetProvinceId = "";
        public string[] PathProvinceIds = Array.Empty<string>();
        public int Status = 0;
        public float Integrity = 100f;
        public float Capacity = 100f;
        public float CurrentFlow = 100f;
        public int Length = 0;
        public bool IsMaritime = false;
        public bool IsAirBridge = false;
        public int InterdictionLevel = 0;
        public string[] InterdictionTypes = Array.Empty<string>();
        public int[] InterdictionIntensities = Array.Empty<int>();
        public int DaysSinceLastAttack = 0;
        public float DailyCapacityLoss = 0f;
        public string InterdictorCountryId = "";
        public long LastAttackDay = -999;
        public int ConsecutiveAttackDays = 0;
        public float RepairRate = 5f;
        public bool IsAutomatedRepair = true;
        public float ConvoyEfficiency = 1f;
    }

    public class SupplyDepotSave
    {
        public string Id = "";
        public string ProvinceId = "";
        public string CountryId = "";
        public string Name = "";
        public float Capacity = 1000f;
        public float CurrentStock = 1000f;
        public float DailyProduction = 100f;
        public float MaxOutput = 500f;
        public bool IsPort = false;
        public bool IsAirfield = false;
        public bool IsRailHub = false;
        public float FortificationLevel = 0f;
        public int GarrisonDivisions = 0;
        public string[] ConnectedArmyIds = Array.Empty<string>();
        public float DailyDistribution = 0f;
        public bool IsStrategic = false;
        public float Vulnerability = 0f;
    }

    public class SpecialForceUnitSave
    {
        public string Id = "";
        public string ArmyId = "";
        public string CountryId = "";
        public string Name = "";
        public int Type = 0;
        public int Level = 1;
        public int Experience = 0;
        public int Manpower = 100;
        public int MaxManpower = 100;
        public int EquipmentLevel = 1;
        public int TrainingLevel = 1;
        public int Morale = 75;
        public int Readiness = 100;
        public bool IsDeployed = false;
        public string CurrentMission = "";
        public string TargetProvinceId = "";
        public int MissionDaysRemaining = 0;
        public string[] CapabilityKeys = Array.Empty<string>();
        public int[] CapabilityValues = Array.Empty<int>();
        public int SuccessfulMissions = 0;
        public int FailedMissions = 0;
        public long LastMissionDay = -999;
    }

    public class InterdictionRecordSave
    {
        public string SupplyLineId = "";
        public int Type = 0;
        public string AttackerCountryId = "";
        public int Intensity = 0;
        public long StartDay = 0;
        public int DurationDays = 0;
        public float DamagePerDay = 0f;
        public bool IsActive = true;
        public string Description = "";
    }

    public class PsyOpsEffectSave
    {
        public string Id = "";
        public string SourceCountryId = "";
        public string TargetCountryId = "";
        public string TargetArmyId = "";
        public int Type = 0;
        public int Intensity = 0;
        public long StartDay = 0;
        public int DurationDays = 0;
        public float MoraleImpactPerDay = 0f;
        public float StabilityImpactPerDay = 0f;
        public bool IsActive = true;
        public string Description = "";
    }

    public class ArmyMoraleSystemSave
    {
        public ArmyMoraleStateSave[] ArmyStates = Array.Empty<ArmyMoraleStateSave>();
        public SupplyLineSave[] SupplyLines = Array.Empty<SupplyLineSave>();
        public SupplyDepotSave[] SupplyDepots = Array.Empty<SupplyDepotSave>();
        public SpecialForceUnitSave[] SpecialForces = Array.Empty<SpecialForceUnitSave>();
        public InterdictionRecordSave[] Interdictions = Array.Empty<InterdictionRecordSave>();
        public PsyOpsEffectSave[] PsyOps = Array.Empty<PsyOpsEffectSave>();
        public int NextSupplyLineId = 1;
        public int NextDepotId = 1;
        public int NextSpecialForceId = 1;
        public int NextInterdictionId = 1;
        public int NextPsyOpsId = 1;
    }

    public static ArmyMoraleSystemSave GetSaveData()
    {
        return new ArmyMoraleSystemSave
        {
            ArmyStates = _armyStates.Select(kv => new ArmyMoraleStateSave
            {
                ArmyId = kv.Value.ArmyId,
                CountryId = kv.Value.CountryId,
                Morale = kv.Value.Morale,
                Experience = kv.Value.Experience,
                Veterancy = (int)kv.Value.Veterancy,
                CombatDays = kv.Value.CombatDays,
                Victories = kv.Value.Victories,
                Defeats = kv.Value.Defeats,
                TotalCasualtiesTaken = kv.Value.TotalCasualtiesTaken,
                TotalCasualtiesInflicted = kv.Value.TotalCasualtiesInflicted,
                DaysSinceLastCombat = kv.Value.DaysSinceLastCombat,
                DaysSinceLastSupply = kv.Value.DaysSinceLastSupply,
                CurrentSupplyLevel = kv.Value.CurrentSupplyLevel,
                ConsecutiveVictories = kv.Value.ConsecutiveVictories,
                ConsecutiveDefeats = kv.Value.ConsecutiveDefeats,
                IsEntrenched = kv.Value.IsEntrenched,
                IsFlanked = kv.Value.IsFlanked,
                IsSurrounded = kv.Value.IsSurrounded,
                EntrenchmentLevel = kv.Value.EntrenchmentLevel,
                LastCombatDay = kv.Value.LastCombatDay,
                LastSupplyDay = kv.Value.LastSupplyDay,
                SpecialForceType = (int)kv.Value.SpecialForceType,
                SpecialForceLevel = kv.Value.SpecialForceLevel,
                SpecialForceExperience = kv.Value.SpecialForceExperience,
                ActiveFactors = kv.Value.ActiveFactors.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
            }).ToArray(),
            SupplyLines = _supplyLines.Values.Select(l => new SupplyLineSave
            {
                Id = l.Id,
                ArmyId = l.ArmyId,
                SourceProvinceId = l.SourceProvinceId,
                TargetProvinceId = l.TargetProvinceId,
                PathProvinceIds = l.PathProvinceIds.ToArray(),
                Status = (int)l.Status,
                Integrity = l.Integrity,
                Capacity = l.Capacity,
                CurrentFlow = l.CurrentFlow,
                Length = l.Length,
                IsMaritime = l.IsMaritime,
                IsAirBridge = l.IsAirBridge,
                InterdictionLevel = l.InterdictionLevel,
                InterdictionTypes = l.ActiveInterdictions.Keys.Select(k => k.ToString()).ToArray(),
                InterdictionIntensities = l.ActiveInterdictions.Values.ToArray(),
                DaysSinceLastAttack = l.DaysSinceLastAttack,
                DailyCapacityLoss = l.DailyCapacityLoss,
                InterdictorCountryId = l.InterdictorCountryId,
                LastAttackDay = l.LastAttackDay,
                ConsecutiveAttackDays = l.ConsecutiveAttackDays,
                RepairRate = l.RepairRate,
                IsAutomatedRepair = l.IsAutomatedRepair,
                ConvoyEfficiency = l.ConvoyEfficiency
            }).ToArray(),
            SupplyDepots = _supplyDepots.Values.Select(d => new SupplyDepotSave
            {
                Id = d.Id,
                ProvinceId = d.ProvinceId,
                CountryId = d.CountryId,
                Name = d.Name,
                Capacity = d.Capacity,
                CurrentStock = d.CurrentStock,
                DailyProduction = d.DailyProduction,
                MaxOutput = d.MaxOutput,
                IsPort = d.IsPort,
                IsAirfield = d.IsAirfield,
                IsRailHub = d.IsRailHub,
                FortificationLevel = d.FortificationLevel,
                GarrisonDivisions = d.GarrisonDivisions,
                ConnectedArmyIds = d.ConnectedArmyIds.ToArray(),
                DailyDistribution = d.DailyDistribution,
                IsStrategic = d.IsStrategic,
                Vulnerability = d.Vulnerability
            }).ToArray(),
            SpecialForces = _specialForces.Values.Select(u => new SpecialForceUnitSave
            {
                Id = u.Id,
                ArmyId = u.ArmyId,
                CountryId = u.CountryId,
                Name = u.Name,
                Type = (int)u.Type,
                Level = u.Level,
                Experience = u.Experience,
                Manpower = u.Manpower,
                MaxManpower = u.MaxManpower,
                EquipmentLevel = u.EquipmentLevel,
                TrainingLevel = u.TrainingLevel,
                Morale = u.Morale,
                Readiness = u.Readiness,
                IsDeployed = u.IsDeployed,
                CurrentMission = u.CurrentMission,
                TargetProvinceId = u.TargetProvinceId,
                MissionDaysRemaining = u.MissionDaysRemaining,
                CapabilityKeys = u.Capabilities.Keys.ToArray(),
                CapabilityValues = u.Capabilities.Values.ToArray(),
                SuccessfulMissions = u.SuccessfulMissions,
                FailedMissions = u.FailedMissions,
                LastMissionDay = u.LastMissionDay
            }).ToArray(),
            Interdictions = _activeInterdictions.Where(r => r.IsActive).Select(r => new InterdictionRecordSave
            {
                SupplyLineId = r.SupplyLineId,
                Type = (int)r.Type,
                AttackerCountryId = r.AttackerCountryId,
                Intensity = r.Intensity,
                StartDay = r.StartDay,
                DurationDays = r.DurationDays,
                DamagePerDay = r.DamagePerDay,
                IsActive = r.IsActive,
                Description = r.Description
            }).ToArray(),
            PsyOps = _activePsyOps.Where(p => p.IsActive).Select(p => new PsyOpsEffectSave
            {
                Id = p.Id,
                SourceCountryId = p.SourceCountryId,
                TargetCountryId = p.TargetCountryId,
                TargetArmyId = p.TargetArmyId,
                Type = (int)p.Type,
                Intensity = p.Intensity,
                StartDay = p.StartDay,
                DurationDays = p.DurationDays,
                MoraleImpactPerDay = p.MoraleImpactPerDay,
                StabilityImpactPerDay = p.StabilityImpactPerDay,
                IsActive = p.IsActive,
                Description = p.Description
            }).ToArray(),
            NextSupplyLineId = _nextSupplyLineId,
            NextDepotId = _nextDepotId,
            NextSpecialForceId = _nextSpecialForceId,
            NextInterdictionId = _nextInterdictionId,
            NextPsyOpsId = _nextPsyOpsId
        };
    }

    public static void LoadSaveData(ArmyMoraleSystemSave data)
    {
        if (data == null)
        {
            ResetForNewGame();
            return;
        }

        _armyStates.Clear();
        _supplyLines.Clear();
        _supplyDepots.Clear();
        _specialForces.Clear();
        _activeInterdictions.Clear();
        _activePsyOps.Clear();

        if (data.ArmyStates != null)
        {
            foreach (var s in data.ArmyStates)
            {
                _armyStates[s.ArmyId] = new ArmyMoraleState
                {
                    ArmyId = s.ArmyId,
                    CountryId = s.CountryId,
                    Morale = s.Morale,
                    Experience = s.Experience,
                    Veterancy = (VeterancyLevel)s.Veterancy,
                    CombatDays = s.CombatDays,
                    Victories = s.Victories,
                    Defeats = s.Defeats,
                    TotalCasualtiesTaken = s.TotalCasualtiesTaken,
                    TotalCasualtiesInflicted = s.TotalCasualtiesInflicted,
                    DaysSinceLastCombat = s.DaysSinceLastCombat,
                    DaysSinceLastSupply = s.DaysSinceLastSupply,
                    CurrentSupplyLevel = s.CurrentSupplyLevel,
                    ConsecutiveVictories = s.ConsecutiveVictories,
                    ConsecutiveDefeats = s.ConsecutiveDefeats,
                    IsEntrenched = s.IsEntrenched,
                    IsFlanked = s.IsFlanked,
                    IsSurrounded = s.IsSurrounded,
                    EntrenchmentLevel = s.EntrenchmentLevel,
                    LastCombatDay = s.LastCombatDay,
                    LastSupplyDay = s.LastSupplyDay,
                    SpecialForceType = (SpecialForceType)s.SpecialForceType,
                    SpecialForceLevel = s.SpecialForceLevel,
                    SpecialForceExperience = s.SpecialForceExperience,
                    ActiveFactors = s.ActiveFactors?.ToDictionary(kv => (MoraleFactor)Enum.Parse(typeof(MoraleFactor), kv.Key), kv => kv.Value) ?? new Dictionary<MoraleFactor, int>()
                };
            }
        }

        if (data.SupplyLines != null)
        {
            foreach (var l in data.SupplyLines)
            {
                _supplyLines[l.Id] = new SupplyLine
                {
                    Id = l.Id,
                    ArmyId = l.ArmyId,
                    SourceProvinceId = l.SourceProvinceId,
                    TargetProvinceId = l.TargetProvinceId,
                    PathProvinceIds = l.PathProvinceIds?.ToList() ?? new List<string>(),
                    Status = (SupplyLineStatus)l.Status,
                    Integrity = l.Integrity,
                    Capacity = l.Capacity,
                    CurrentFlow = l.CurrentFlow,
                    Length = l.Length,
                    IsMaritime = l.IsMaritime,
                    IsAirBridge = l.IsAirBridge,
                    InterdictionLevel = l.InterdictionLevel,
                    ActiveInterdictions = l.InterdictionTypes?.Zip(l.InterdictionIntensities ?? new int[0],
                        (k, v) => new KeyValuePair<InterdictionType, int>(
                            (InterdictionType)Enum.Parse(typeof(InterdictionType), k), v))
                        .ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<InterdictionType, int>(),
                    DaysSinceLastAttack = l.DaysSinceLastAttack,
                    DailyCapacityLoss = l.DailyCapacityLoss,
                    InterdictorCountryId = l.InterdictorCountryId,
                    LastAttackDay = l.LastAttackDay,
                    ConsecutiveAttackDays = l.ConsecutiveAttackDays,
                    RepairRate = l.RepairRate,
                    IsAutomatedRepair = l.IsAutomatedRepair,
                    ConvoyEfficiency = l.ConvoyEfficiency
                };
            }
        }

        if (data.SupplyDepots != null)
        {
            foreach (var d in data.SupplyDepots)
            {
                _supplyDepots[d.Id] = new SupplyDepot
                {
                    Id = d.Id,
                    ProvinceId = d.ProvinceId,
                    CountryId = d.CountryId,
                    Name = d.Name,
                    Capacity = d.Capacity,
                    CurrentStock = d.CurrentStock,
                    DailyProduction = d.DailyProduction,
                    MaxOutput = d.MaxOutput,
                    IsPort = d.IsPort,
                    IsAirfield = d.IsAirfield,
                    IsRailHub = d.IsRailHub,
                    FortificationLevel = d.FortificationLevel,
                    GarrisonDivisions = d.GarrisonDivisions,
                    ConnectedArmyIds = d.ConnectedArmyIds?.ToList() ?? new List<string>(),
                    DailyDistribution = d.DailyDistribution,
                    IsStrategic = d.IsStrategic,
                    Vulnerability = d.Vulnerability
                };
            }
        }

        if (data.SpecialForces != null)
        {
            foreach (var u in data.SpecialForces)
            {
                _specialForces[u.Id] = new SpecialForceUnit
                {
                    Id = u.Id,
                    ArmyId = u.ArmyId,
                    CountryId = u.CountryId,
                    Name = u.Name,
                    Type = (SpecialForceType)u.Type,
                    Level = u.Level,
                    Experience = u.Experience,
                    Manpower = u.Manpower,
                    MaxManpower = u.MaxManpower,
                    EquipmentLevel = u.EquipmentLevel,
                    TrainingLevel = u.TrainingLevel,
                    Morale = u.Morale,
                    Readiness = u.Readiness,
                    IsDeployed = u.IsDeployed,
                    CurrentMission = u.CurrentMission,
                    TargetProvinceId = u.TargetProvinceId,
                    MissionDaysRemaining = u.MissionDaysRemaining,
                    Capabilities = u.CapabilityKeys?.Zip(u.CapabilityValues ?? new int[0],
                        (k, v) => new KeyValuePair<string, int>(k, v))
                        .ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, int>(),
                    SuccessfulMissions = u.SuccessfulMissions,
                    FailedMissions = u.FailedMissions,
                    LastMissionDay = u.LastMissionDay
                };
            }
        }

        if (data.Interdictions != null)
        {
            foreach (var r in data.Interdictions)
            {
                _activeInterdictions.Add(new InterdictionRecord
                {
                    SupplyLineId = r.SupplyLineId,
                    Type = (InterdictionType)r.Type,
                    AttackerCountryId = r.AttackerCountryId,
                    Intensity = r.Intensity,
                    StartDay = r.StartDay,
                    DurationDays = r.DurationDays,
                    DamagePerDay = r.DamagePerDay,
                    IsActive = r.IsActive,
                    Description = r.Description
                });
            }
        }

        if (data.PsyOps != null)
        {
            foreach (var p in data.PsyOps)
            {
                _activePsyOps.Add(new PsyOpsEffect
                {
                    Id = p.Id,
                    SourceCountryId = p.SourceCountryId,
                    TargetCountryId = p.TargetCountryId,
                    TargetArmyId = p.TargetArmyId,
                    Type = (PsyOpsType)p.Type,
                    Intensity = p.Intensity,
                    StartDay = p.StartDay,
                    DurationDays = p.DurationDays,
                    MoraleImpactPerDay = p.MoraleImpactPerDay,
                    StabilityImpactPerDay = p.StabilityImpactPerDay,
                    IsActive = p.IsActive,
                    Description = p.Description
                });
            }
        }

        _nextSupplyLineId = data.NextSupplyLineId > 0 ? data.NextSupplyLineId : 1;
        _nextDepotId = data.NextDepotId > 0 ? data.NextDepotId : 1;
        _nextSpecialForceId = data.NextSpecialForceId > 0 ? data.NextSpecialForceId : 1;
        _nextInterdictionId = data.NextInterdictionId > 0 ? data.NextInterdictionId : 1;
        _nextPsyOpsId = data.NextPsyOpsId > 0 ? data.NextPsyOpsId : 1;

        _initialized = true;
        GD.Print($"[ArmyMoraleSystem] State yüklendi — ordu={_armyStates.Count}, ikmal_hatları={_supplyLines.Count}, depolar={_supplyDepots.Count}, özel_kuvvetler={_specialForces.Count}, interdiction={_activeInterdictions.Count}, psyops={_activePsyOps.Count}");
    }
}