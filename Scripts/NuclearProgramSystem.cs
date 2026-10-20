using Godot;
using System;
using System.Collections.Generic;

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

    public static NuclearStage CompletedThrough => _completedThrough;
    public static NuclearStage InProgressStage => _inProgress;
    public static int DaysInvested => _daysInvested;
    public static int TestsConducted => _testsConducted;
    public static int Warheads => _warheads;
    public static NuclearDoctrine Doctrine => _doctrine;
    public static bool IsTestMode => _testMode;

    public static void EnableTestMode()
    {
        _testMode = true;
        GD.Print("[Nükleer] Test modu etkin: ücret/günek=0, süre=1 gün, deneme başarısı garantili");
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
        if (_inProgress == NuclearStage.None) return;
        _daysInvested++;
        if (_daysInvested >= GetStageDuration(_inProgress))
            CompleteStage();
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

    // ===== M12: Save/Load =====

    public class NuclearSave
    {
        public int CompletedThrough { get; set; } // (int)NuclearStage
        public int InProgress { get; set; }       // -1 = yok
        public int DaysInvested { get; set; }
        public int TestsConducted { get; set; }
        public int Warheads { get; set; }
        public int Doctrine { get; set; } // (int)NuclearDoctrine
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
            Doctrine = (int)_doctrine
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
        GD.Print($"[Nükleer] State yüklendi — ileri={_completedThrough}, devam={_inProgress}, başlık={_warheads}, doktrin={_doctrine}");
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
        GD.Print("[Nükleer] Yeni oyun için sıfırlandı");
    }
}
