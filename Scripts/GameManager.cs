using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// Merkezi oyun döngüsü: tarih, hız, pause, sinyaller.
// Autoload olarak eklenir (project.godot -> [autoload] GameManager).
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    // Stale singleton koruması: sahne değişiminde Instance ölü node olabilir.
    public static bool IsAvailable =>
        Instance != null && GodotObject.IsInstanceValid(Instance);

    // --- Sinyaller ---
    [Signal] public delegate void DateChangedEventHandler(string dateText);
    [Signal] public delegate void SpeedChangedEventHandler(int newSpeed);
    [Signal] public delegate void DayChangedEventHandler(long totalDays);
    [Signal] public delegate void PlayerCountryChangedEventHandler(string countryId);
    [Signal] public delegate void PlayerCountryDataChangedEventHandler();
    [Signal] public delegate void PlayerCharacterChangedEventHandler();
    [Signal] public delegate void CountryOwnershipChangedEventHandler(); // M7: ilhak sonrası harita yenile

    // --- Yapılandırma ---
    [Export] public double SecondsPerDay { get; set; } = 1.0; // 1x hız = 1 saniyede 1 gün

    // --- Durum ---
    // M17.1: başlangıç tarihi aktif senaryodan (historical_1936 → 1936-01-01, modern_world → 2026-01-01)
    public DateTime CurrentDate { get; private set; } = new DateTime(1936, 1, 1); // HOI4 başlangıcı
    public DateTime StartDate => ScenarioSystem.ActiveStartDate; // aktif senaryonun başlangıç tarihi
    public int TotalDays { get; private set; } = 0;
    [Export] public double BasePPDailyGain { get; set; } = 2.0; // HOI4 temel: 2 PP/gün

    public int Speed { get; private set; } = 0; // 0=pause, 1-5 hız
    public bool IsPaused => Speed == 0;

    public string PlayerCountryId { get; private set; } = "";
    public bool HasPlayer => PlayerCountryId != "";

    // M24.1: Oyuncu karakteri (yeni oyun başlangıcında oluşturulur, Save/Load'ta kalıcıdır)
    public Character PlayerCharacter { get; private set; }
    public bool HasCharacter => PlayerCharacter != null;

    // M24.3: Başlangıç yolu — Sandbox (doğrudan lider olarak başla) veya Career (kariyer/educasyon, M25).
    public enum StartPath { Sandbox, Career }
    public StartPath PlayerStartPath { get; private set; } = StartPath.Sandbox;
    public void SetStartPath(StartPath path) => PlayerStartPath = path;

    // Oyuncu ülkesi kilidi: bir kez seçilince oyun sürerken değiştirilemez.
    // Yalnızca yeni oyun başlatınca ResetForNewGame() temizler.
    // Kilit tek bu property'de durur → ileride Save/Load buradan okur/yazar.
    public bool IsPlayerCountryLocked { get; private set; } = false;

    // M10: EventPopup auto-pause için hız kaydı (olay kapanınca geri yüklenir)
    public int SpeedBeforeEventPause { get; private set; } = 1;

    // Oyuncu ülkesinin verisi (CountryDatabase üzerinden)
    private CountryData _playerCountryData;
    public CountryData PlayerCountryData
    {
        get
        {
            if (_playerCountryData == null && HasPlayer)
                _playerCountryData = CountryDatabase.GetOrCreate(PlayerCountryId, "");
            return _playerCountryData;
        }
    }

    private double _accumulator;
    private int _speedBeforePause = 1;

    // M12: --rng-seed test tohumu (yoksa null → her yeni oyunda tarih bazlı tohum)
    private ulong? _forcedRngSeed;

    public override void _Ready()
    {
        Instance = this;

        // Ayarları yükle ve uygula
        SettingsSystem.Load();
        SettingsSystem.Apply();

        // Geliştirici/test argümanları (headless test için):
        // --speed=N (0-5)
        // --country=XXX (oyuncu ülkesini seç, örn. --country=GER)
        // --prod-test (örnek üretim hatları ekle: infantry/support/light_tank/destroyer + variant switch)
        // --war-test=XXX (hedef ülkeye savaş ilanı, örn. --war-test=POL)
        // --focus-test=XXX (M9: ülkeyi seç + AI odak seçim mantığı otomatik odak seçsin)
        string testCountry = null;
        bool prodTest = false;
        string warTestTarget = null;
        string focusTestCountry = null;
        string eventTestId = null;      // M10: olayı ID ile doğrudan tetikle (--event-test=ID)
        bool eventAuto = false;         // M10: --event-auto — auto-choose test modu
        string dateJump = null;         // M10: --date-jump=YYYY-MM-DD — Date trigger testi
        bool decisionsTest = false;     // M10: --decisions-test — karar zinciri testi
        ulong? rngSeed = null;          // M12: --rng-seed=N — deterministik test tohumu
        bool saveLoadTest = false;      // M12: --save-load-test — kaydet/yükle doğrulama
        bool warSimTest = false;        // JOB1: 5 yıllık savaş simülasyonu
        bool agendaTest = false;        // M16: ajanda sistem testi (aksiyon ekle + gün ilerlet + efekt kontrol)
        bool cabinetMeetingTest = false; // M21.5: kabine toplantısı testi (ajanda slotu + bakan sadakati)
        string scenarioId = null;       // M17.1: --scenario=ID — aktif senaryoyu seç (modern_world / historical_1936)
        bool scenarioTest = false;      // M17.1: --scenario-test — her iki senaryonun yükleme sayılarını doğrula
        int loadSlot = -1;              // M17.1: --load-slot=N — mevcut kaydı yükle (v1 migration testi)
        bool constitutionTest = false;  // M22.1: anayasa yönetim-biçimi tanımlarını doğrula
        bool lawsTest = false;            // M22.2: yasa çıkarma/kaldırma + etki testi
        bool provinceTest = false;        // M18: il sayısı, sahiplik, komşuluk, deterministik üretim
        bool armyTest = false;            // M19: ordu hareketi, düşman teması durdurma, save/load kimlik
        bool parliamentTest = false;      // M22.3: partiler, sandalye dağılımı, oylama, veto, koalisyon, referandum
        bool autoritarianTest = false;    // M22.4: parti kongresi + darbe riski
        bool electionTest = false;        // M23.1: seçim takvimi + oy tahmini
        bool characterTest = false;       // M24.1: karakter oluşturma + Save/Load determinizm
        bool careerTest = false;          // M25.1: siyasetçi kariyer yolu (eğitim → parti gençlik → yerel yönetim → milletvekili → bakanlık)
        bool militaryCareerTest = false;  // M25.2: asker kariyer yolu (harp okulu → rütbeler → görev yerleri → savaş deneyimi → darbe)
        bool educationTest = false;       // M25.3: eğitim modülleri (ders, staj, mentor, sınav, tez/proje, ağ kurma)
        bool candidacyTest = false;       // M25.4: aday olma kriterleri (yaş, eğitim, parti, tanınırlık, itibar, sicil)
        bool careerEventsTest = false;    // M25.5: kariyer olayları (skandal, fırsat, mentor kaybı, hastalık)
        bool characterTakeoverTest = false; // M25.6: seçim kazanıp ülkeyi devralma testi
        bool disasterTest = false;          // M26.1: afet sistemi testi (--disaster-test)
        bool interventionTest = false;      // M26.3: müdahale sistemi testi (--intervention-test)
        bool unexpectedEventTest = false;   // M26.4: beklenmedik olay sistemi testi (--unexpected-events-test)
        bool diplomacyTest = false;         // M27: lider diploması testi (--diplomacy-test)
        bool agreementsTest = false;        // M27.2: anlaşma testi (--agreements-test)
        bool pressTest = false;             // M27.3: basın etkinliği testi (--press-test)

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--speed=") && int.TryParse(arg.Substring(8), out var s))
                SetSpeed(Mathf.Clamp(s, 0, 5));
            else if (arg.StartsWith("--country="))
                testCountry = arg.Substring(10).ToUpper();
            else if (arg == "--prod-test")
                prodTest = true;
            else if (arg.StartsWith("--war-test="))
                warTestTarget = arg.Substring(11).ToUpper();
            else if (arg.StartsWith("--focus-test="))
            {
                focusTestCountry = arg.Substring(13).ToUpper();
                testCountry = focusTestCountry; // oyuncu ülkesi olarak da seç
            }
            else if (arg.StartsWith("--event-test="))
                eventTestId = arg.Substring(13);
            else if (arg == "--event-auto")
                eventAuto = true;
            else if (arg.StartsWith("--date-jump="))
                dateJump = arg.Substring(12);
            else if (arg == "--decisions-test")
                decisionsTest = true;
            else if (arg.StartsWith("--rng-seed=") && ulong.TryParse(arg.Substring(11), out var seed))
            {
                rngSeed = seed;
                _forcedRngSeed = seed;
            }
            else if (arg == "--save-load-test")
                saveLoadTest = true;
            else if (arg == "--war-sim")
                warSimTest = true;
            else if (arg == "--agenda-test")
                agendaTest = true;
            else if (arg == "--cabinet-meeting-test")
                cabinetMeetingTest = true;
            else if (arg.StartsWith("--scenario="))
                scenarioId = arg.Substring(11);
            else if (arg == "--scenario-test")
                scenarioTest = true;
            else if (arg == "--constitution-test")
                constitutionTest = true;
            else if (arg == "--laws-test")
                lawsTest = true;
            else if (arg == "--province-test")
                provinceTest = true;
            else if (arg == "--army-test")
                armyTest = true;
            else if (arg == "--parliament-test")
                parliamentTest = true;
            else if (arg == "--autoritarian-test")
                autoritarianTest = true;
            else if (arg == "--election-test")
                electionTest = true;
            else if (arg == "--character-test")
                characterTest = true;
            else if (arg == "--career-test")
                careerTest = true;
            else if (arg == "--military-career-test")
                militaryCareerTest = true;
            else if (arg == "--education-test")
                educationTest = true;
            else if (arg == "--candidacy-test")
                candidacyTest = true;
            else if (arg == "--career-events-test")
                careerEventsTest = true;
            else if (arg == "--character-takeover-test")
                characterTakeoverTest = true;
            else if (arg == "--disaster-test")
                disasterTest = true;
            else if (arg == "--intervention-test")
                interventionTest = true;
            else if (arg == "--unexpected-events-test")
                unexpectedEventTest = true;
            else if (arg == "--diplomacy-test")
                diplomacyTest = true;
            else if (arg == "--agreements-test")
                agreementsTest = true;
            else if (arg == "--press-test")
                pressTest = true;
            else if (arg.StartsWith("--load-slot=") && int.TryParse(arg.Substring(12), out var ls))
                loadSlot = Mathf.Clamp(ls, 0, SaveSystem.MaxSlots);
        }

        // M26.1: --disaster-test verilmişse afet test modunu etkinleştirir (olasılık artırılır).
        // --save-load-test'te etkinleştirilmez: doğrulama testi 120 sn sınırında bitmeli ve
        // DeterministikRNG tohumu ile A==B determinismi zaten korunur. (Dedicated --disaster-test
        // afet tetikleme determinizmini sıınar.)
        if (disasterTest)
            DisasterSystem.EnableTestMode();

        // M26.3: --intervention-test (ve --save-load-test) verilmişse müdahale test modunu
        // etkinleştir.
        if (interventionTest || saveLoadTest)
            InterventionSystem.EnableTestMode();

        // M26.4: --unexpected-events-test verilmişse beklenmedik olay test modunu etkinleştirir
        // (olasılık artırılır). --save-load-test'te etkinleştirilmez: doğal (düşük) olasılıkla
        // çalışması gereken doğrulama testi 120 sn sınırında bitmeli.
        if (unexpectedEventTest)
            UnexpectedEventSystem.EnableTestMode();

        // M17.1: Aktif senaryoyu ayarla — veri yükleyicilerin yolları buradan gelir.
        // --scenario verilmezse ScenarioSystem varsayılanı (historical_1936) kullanılır.
        // Headless testlerde: --scenario=modern_world / --scenario=historical_1936
        if (scenarioId != null)
        {
            if (!ScenarioSystem.SetActive(scenarioId))
                GD.PrintErr($"[Test] --scenario: Senaryo bulunamadı: {scenarioId} — varsayılan kullanılıyor");
        }

        // M17.1: aktif senaryoyu başta logla
        GD.Print($"Senaryo: {ScenarioSystem.ActiveId} — {ScenarioSystem.ActiveName} | Başlangıç: {ScenarioSystem.ActiveStartDate:yyyy-MM-dd} | Placeholder: {ScenarioSystem.ActiveIsPlaceholder}");

        // M17.1: henüz oyun başlamadıysa tarihi senaryo başlangıcına senkronize et.
        // Menü akışında ResetForNewGame yapar; headless --country akışında garanti burada.
        // Kayıt yükleme akışında tarih kayıttan gelir ve bunu ezer.
        if (TotalDays == 0 && !HasPlayer)
            CurrentDate = StartDate;

        // M12: deterministik RNG — test tohumu varsa hemen uygula (headless iki çalıştırma
        // aynı sonucu versin diye); yoksa yeni oyun başında (ResetForNewGame) tarih bazlı atılır.
        if (rngSeed.HasValue)
            DeterministicRNG.InitializeForNewGame(rngSeed);

        // Test ülkesini seç (diğer sistemler Initialize içinde çağrılır)
        if (!string.IsNullOrEmpty(testCountry))
            SetPlayerCountry(testCountry);

        // M-Flags: bayrak raporu — PNG bulunan / yedek kullanan ülkeler (bir kez)
        FlagLibrary.LogReportOnce();

        // M16: Zaman ve Tempo sistemi — hız tablosu + ajanda + aylık rapor
        TimeTempoSystem.Initialize();

        // M17.2: Lider sistemi — kurgusal liderler (senaryo bazlı)
        LeaderSystem.LoadScenarioData();

        // M22.1: Anayasa — yönetim biçimi, dönem, seçim yaşı, olağanüstü hal kuralları.
        ConstitutionSystem.LoadData();

        // M22.2: Yasalar — tanım dosyasını yükle (Data/laws.json).
        LawSystem.LoadData();

        // M22.3: Parlamento — parti/sandalye verisini yükle (Data/parties.json).
        ParliamentSystem.LoadData();

        // M22.4: Otoriter sistemler — parti kongresi ve darbe riski.
        AutoritarianSystem.LoadData();

        // M23.1: Seçim takvimi ve oy tahmini.
        ElectionSystem.LoadData();

        // M23.2: Kampanya aksiyonları (miting, reklam, TV, ittifak, vaat).
        ElectionCampaignSystem.LoadData();

        // M25.1: Siyasetçi kariyer yolu — üniversite → parti gençlik → yerel yönetim → milletvekili → bakanlık.
        CareerSystem.Initialize();

        // M25.2: Asker kariyer yolu — harp okulu → rütbeler → görev yerleri → savaş deneyimi → genelkurmay.
        MilitaryCareerSystem.Initialize();

        // M25.3: Eğitim modülleri — ders, staj, mentor, sınav, tez/proje, ağ kurma.
        EducationSystem.Initialize();

        // M25.4: Aday olma kriterleri — yaş, eğitim seviyesi, parti üyeliği, tanınırlık, itibar, sicil.
        CandidacySystem.Initialize();

        // M25.5: Kariyer olayları — skandal, fırsat, mentor kaybı, hastalık.
        CareerEventSystem.Initialize();

        // M23.4: Halk refahı — protesto, grev, güven oylaması, erken seçim, darbe girişimi.
        PoliticalUnrestSystem.InitializeAll();

        // M26.1: Afet sistemini yükle (tanımlar + ülke→bölge haritası, aktif senaryodan).
        DisasterSystem.LoadScenarioData();

        // Prod test: örnek hatlar ekle
        if (prodTest && HasPlayer)
            SetupProductionTest();

        // War test: hedefe savaş ilanı (PP top-up — başlangıç PP'si maliyeti karşılamayabilir)
        if (warTestTarget != null && HasPlayer)
        {
            if (PlayerCountryData != null && PlayerCountryData.PoliticalPower < WarSystem.DeclareWarPPCost + 10)
                PlayerCountryData.PoliticalPower = WarSystem.DeclareWarPPCost + 10;
            // M17.2: savaş desteği önkoşulunu da garanti et (demokrasilerde %40 gerekir)
            if (PlayerCountryData != null && PlayerCountryData.WarSupportPercent < 50)
                PlayerCountryData.WarSupportPercent = 50;
            // M17A: savaş ilanı artık casus belli gerektirir — teste otomatik gerekçe ver
            CasusBelliSystem.GrantImmediateJustification(PlayerCountryId, warTestTarget, "historical_event");
            CasusBelliSystem.LogWarDeclarationCheck(PlayerCountryId, warTestTarget);
            // M17.2: UI ile aynı tam yolu test et (faksiyon çağrısı + garanti tetiklemesi dahil)
            DiplomacySystem.DeclareWar(warTestTarget);
            // Ofansif emri: savaşın ilerlemesi için (pasif çatışma ilerlemez)
            WarSystem.SetOffensive(warTestTarget, true);
        }

        // Focus test (M9): AI odak seçim mantığı — normal oyunda asla etkinleşmez
        if (focusTestCountry != null && HasPlayer)
        {
            FocusSystem.EnableAutoPickTest();
            if (Speed == 0) SetSpeed(5); // --speed verilmediyse hız 5
        }

        // === M10 test kancaları — normal oyunda asla etkinleşmez ===

        // Date jump: oyunu verilen tarihe atlat (Date trigger'ın gerçekten çalıştığını test eder)
        if (dateJump != null && System.DateTime.TryParse(dateJump, out var jumpDate))
            JumpToDateForTest(jumpDate);

        // Event auto-choose: Date trigger testinde popup yerine ilk seçenek uygulanır
        if (eventAuto)
            EventSystem.EnableAutoChooseTest();

        // Event test: olayı ID ile doğrudan tetikle; ülke yoksa olayın hedef ülkesini seç
        if (eventTestId != null)
        {
            EventSystem.EnableAutoChooseTest();
            var evt = EventSystem.GetEvent(eventTestId);
            if (evt == null)
            {
                GD.PrintErr($"[Test] Olay bulunamadı: {eventTestId}");
            }
            else
            {
                if (!HasPlayer && !string.IsNullOrEmpty(evt.Trigger?.Country))
                    SetPlayerCountry(evt.Trigger.Country);
                if (Speed == 0) SetSpeed(5);
                bool ok = EventSystem.TriggerEventNow(eventTestId);
                GD.Print($"[Test] Olay tetikleme ({eventTestId}): {(ok ? "OK" : "BAŞARISIZ — yok veya zaten ateşlenmiş")}");
            }
        }

        // Decisions test: kullanılabilir kararları listele + ilkini başlat
        if (decisionsTest)
        {
            if (!HasPlayer) SetPlayerCountry("GER"); // GER: hem ALL hem GER kararları görünür
            if (Speed == 0) SetSpeed(5);

            var decisions = DecisionSystem.GetAvailableDecisions();
            GD.Print($"[Test] Kullanılabilir kararlar: {decisions.Count}");
            foreach (var d in decisions)
                GD.Print($"[Test]   - {d.Name} ({d.CostPP} PP, {d.CostDays} gün, cooldown {d.CooldownDays})");

            if (decisions.Count > 0)
            {
                // PP top-up — başlangıç PP'si maliyeti karşılamayabilir
                if (PlayerCountryData != null && PlayerCountryData.PoliticalPower < decisions[0].CostPP + 10)
                    PlayerCountryData.PoliticalPower = decisions[0].CostPP + 10;
                DecisionSystem.StartDecision(decisions[0].Id);
                GD.Print($"[Test] İlk karar başlatıldı: {decisions[0].Name} — tamamlanma logunu bekle");
            }
        }

        // M12: Kaydet/Yükle doğrulama testi — normal oyunda asla etkinleşmez.
        // Test tüm akışı senkron çalıştırır (100+50 gün, kaydet, yükle, karşılaştır);
        // sonrasında oyun pause kalır (--speed verilmediyse hiç ilerlemez).
        if (saveLoadTest)
            SaveLoadTest.Run();

        // JOB1: 5 yıllık AI savaş simülasyonu (headless)
        if (warSimTest)
            RunWarSimulationTest();

        // M16: Ajanda testi — normal oyunda asla çalışmaz
        if (agendaTest)
            RunAgendaTest();

        // M21.5: Kabine toplantısı testi — normal oyunda asla çalışmaz
        if (cabinetMeetingTest)
            RunCabinetMeetingTest();

        // M17.1: Senaryo testi — her iki senaryonun yüklediği kayıt sayılarını logla
        if (scenarioTest)
            RunScenarioTest();
        if (constitutionTest)
            RunConstitutionTest();
        if (lawsTest)
            RunLawsTest();
        if (provinceTest)
            RunProvinceTest();
        if (armyTest)
            RunArmyTest();
        if (parliamentTest)
            RunParliamentTest();
        if (autoritarianTest)
            RunAutoritarianTest();
        if (electionTest)
            RunElectionTest();
        if (characterTest)
            RunCharacterTest();
        if (careerTest)
            RunCareerTest();
        if (militaryCareerTest)
            RunMilitaryCareerTest();
        if (educationTest)
            RunEducationTest();
        if (candidacyTest)
            RunCandidacyTest();
        if (careerEventsTest)
            RunCareerEventsTest();
        if (characterTakeoverTest)
            RunCharacterTakeoverTest();
        if (disasterTest)
            RunDisasterTest();

        // M26.3: Müdahale testi (müdahale uygulanır mı, süre sonunda etki doğrusal söner,
        // bakan kalitesi çarpanı doğru mu; başarısızlıkta Quit(1)).
        if (interventionTest)
            RunInterventionTest();

        // M26.4: Beklenmedik olay testi (7 tanım yüklü, test modunda olay ateşlenir,
        // kaydet/yükle dönüşümünde aktif olay sayısı korunur; başarısızlıkta Quit(1)).
        if (unexpectedEventTest)
            RunUnexpectedEventTest();

        // M27: Lider diploması testi (toplantı, anlaşmanın ilişkiyi etkiliyor mu,
        // gerilim değişiyor mu, kaydet/yüklede A==B korunur; başarısızlıkta Quit(1)).
        if (diplomacyTest)
            RunDiplomacyTest();

        // M27.2: Anlaşma testi (ticaret/savunma/vize/enerji/iklim imzalanır, etki görülür,
        // iptal edilebilir, kaydet/yüklede A==B korunur; başarısızlıkta Quit(1)).
        if (agreementsTest)
            RunAgreementsTest();

        // M27.3: Basın etkinliği testi (toplantı/TV/miting, ton seçimi, gösterge etkileri,
        // deterministik geri-etki, kaydet/yüklede A==B korunur; başarısızlıkta Quit(1)).
        if (pressTest)
            RunPressTest();

        // M17.1: Slot yükleme testi — mevcut kaydı yükler (v1 migration dahil). 0=auto, 1-10=slot.
        if (loadSlot >= 0)
        {
            bool ok = loadSlot == 0 ? SaveSystem.LoadAutoSave() : SaveSystem.LoadGame(loadSlot);
            GD.Print($"[Test] --load-slot={loadSlot}: {(ok ? "OK — yüklendi" : "BAŞARISIZ")}");
        }

        GD.Print($"GameManager hazır. Başlangıç tarihi: {GetDateText()} | Senaryo: {ScenarioSystem.ActiveName} ({ScenarioSystem.ActiveId}) | Hız: {Speed} | Ülke: {PlayerCountryId ?? "—"} | ProdTest: {prodTest} | WarTest: {warTestTarget ?? "—"}");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Process(double delta)
    {
        if (IsPaused) return;

        // M16: Hız tablosu game_settings.json'dan (1x=0.5 gün/sn ... 5x=1 gün/sn)
        double daysPerSec = TimeTempoSystem.GetDaysPerSecond(Speed);
        if (daysPerSec < 0) daysPerSec = Speed / SecondsPerDay; // fallback: eski formül
        _accumulator += delta * daysPerSec;
        while (_accumulator >= 1.0)
        {
            _accumulator -= 1.0;
            AdvanceDay();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        switch (key.Keycode)
        {
            case Key.Space:
                TogglePause();
                break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key3: SetSpeed(3); break;
            case Key.Key4: SetSpeed(4); break;
            case Key.Key5: SetSpeed(5); break;
        }
    }

    private void AdvanceDay()
    {
        CurrentDate = CurrentDate.AddDays(1);
        TotalDays++;

        // Gunluk tick: oyuncu ulkesinin PP'si, manpower'i artsin
        DailyTick();

        EmitSignal(SignalName.DayChanged, (long)TotalDays);
        EmitSignal(SignalName.DateChanged, GetDateText());
    }

    private void DailyTick()
    {
        // M25.6: Karakter henüz ülke yönetmiyorsa, doğum ülkesindeki seçimi denetle;
        // kazanınca ülkeyi devral (lider olur, kabine devralınır). Bu blok HasPlayer
        // olmadan da çalışmalı, çünkü kendi içinde SetPlayerCountry çağırır ve
        // ilk kez bir ülkeyi seçer.
        if (HasCharacter && !HasPlayer && PlayerCharacter != null)
        {
            var birthCountry = PlayerCharacter.BirthCountryId;
            if (!string.IsNullOrEmpty(birthCountry)
                && CandidacySystem.IsEligible(PlayerCharacter, birthCountry)
                && ElectionSystem.IsElectionDue(birthCountry, TotalDays))
            {
                var result = ElectionSystem.ResolveElection(birthCountry, TotalDays);
                if (result != null && result.Won)
                {
                    SetPlayerCountry(birthCountry);
                    // Kabine devralınır: atanmamış bakanlıklar için ülke içinden en yetenekli bakan atanır
                    foreach (var m in CabinetSystem.Ministries)
                    {
                        if (!CabinetSystem.IsAssigned(birthCountry, m.Id))
                        {
                            var available = CabinetSystem.GetAvailableMinisters(birthCountry, m.Id);
                            if (available.Count > 0)
                                CabinetSystem.AssignMinister(birthCountry, m.Id, available[0].Id);
                        }
                    }
                    GD.Print($"[M25.6] {PlayerCharacter.FullName} seçimi kazandı, {birthCountry} ülkesinin lideri oldu.");
                }
            }
        }

        if (!HasPlayer) return;
        var data = PlayerCountryData;
        if (data == null) return;

        data.PoliticalPower += BasePPDailyGain;
        data.ManpowerPool += data.ManpowerPerDay;

        // M4: Ekonomi günlük tick'i (inşaat, kaynaklar, ticaret)
        EconomySystem.DailyTick();

        // M5: Teknoloji araştırması günlük ilerleme
        TechnologySystem.DailyTick();

        // M6: Üretim hatları günlük ilerleme
        EquipmentSystem.DailyTick();

        // M7: Ordu ve savaş sistemleri
        ArmySystem.DailyTick();
        WarSystem.DailyTick();

        // M8: Diplomasi sistemi
        DiplomacySystem.DailyTick();

        // M17A: Casus belli — gerekçe inşaatı / ültimatom / ateşkes sayaçları
        CasusBelliSystem.DailyTick();

        // M9: Ulusal odak ilerlemesi (tamamlanınca efektler uygulanır + loglanır)
        FocusSystem.DailyTick();

        // M10: Olay ve karar sistemleri
        EventSystem.DailyTick();
        DecisionSystem.DailyTick();

        // M11: AI ülkeleri (oyuncu DIŞI) — hafif güncellemeler + karar dilimi
        CountryAI.DailyTick();

        // M20.1: İstatistik göstergelerinin günlük dalgalanması
        StatisticsSystem.DailyTick();

        // M26.1: Afet günlük tick'i — bölgeye özel olasılık/şiddet, gösterge şokları.
        DisasterSystem.DailyTick();

        // M26.3: Müdahale günlük tick'i — acil yardım / yeniden inşa / tahliye / uluslararası yardım;
        // gösterge etkileri expires olur, memnuniyet artışları seçime akar.
        InterventionSystem.DailyTick();

        // M26.4: Beklenmedik olay günlük tick'i — finansal kriz, terör, siber saldırı, enerji krizi,
        // sanayi kazası, cinayet girişi, sızdırma skandalı; gösterge şokları, istikrar ve savaş
        // desteği etkileri uygulanır, aktif olaylar süresi azaltılır.
        UnexpectedEventSystem.DailyTick();

        // M20.2: Bütçe günlük tick'i (gelir/harcama/açık/borç/faiz + göstergelere yansıma)
        BudgetSystem.DailyTick();

        // M20.4: Göstergeler arası bağlantılar (enflasyon↔işsizlik↔memnuniyet vb.)
        FormulaSystem.ApplyLinks();

        // M20.3: Halk memnuniyeti günlük tick'i (grubun göstergelere/harcamalara tepkisi)
        PublicSatisfactionSystem.DailyTick();

        // M21.3: atanan bakanların nitelikleri ilgili göstergeleri etkiler (son söz, deterministik)
        if (HasPlayer) CabinetSystem.ApplyMinisterEffects(PlayerCountryId);

        // M24.2: oyuncu karakterinin 7 niteliği ilgili göstergeleri etkiler (düşük, deterministik)
        if (HasPlayer) ApplyCharacterEffects();

        // M21.4: skandal, istifa, koalisyon baskısı, başarısızlık uyarısı denetimi
        if (HasPlayer) CabinetSystem.CheckCabinetEvents(PlayerCountryId);

        // M22.4: Otoriter sistem günlük darbe riski değerlendirmesi (tek parti rejimler için).
        if (HasPlayer) AutoritarianSystem.DailyTick(PlayerCountryId);

        // M25.1: Kariyer sistemi günlük ilerleme (eğitim/kariyer aşaması).
        if (HasPlayer) CareerSystem.DailyTick(TotalDays, PlayerCharacter);

        // M25.2: Asker kariyeri günlük ilerleme (rütbe, deneyim, darbe riski).
        if (HasPlayer) MilitaryCareerSystem.DailyTick(TotalDays, PlayerCharacter);

        // M25.3: Eğitim modülü günlük ilerleme (süre, başarı/başarısızlık çözümü).
        if (HasPlayer) EducationSystem.DailyTick(TotalDays, PlayerCharacter);

        // M25.5: Kariyer olayları günlük denetimi (skandal, fırsat, mentor kaybı, hastalık).
        if (HasPlayer)
            data.PoliticalPower = CareerEventSystem.DailyTick(TotalDays, PlayerCharacter, data.PoliticalPower);

        // M16: Zaman ve Tempo — ajanda ilerlemesi, hafta reset, aylık rapor
        TimeTempoSystem.DailyTick(this);

        // M23.3: Seçim günü — oyuncu ülkesinde seçim sonucu çözümlenir (kazanma/kaybetme, lider değişikliği, yeni kabine)
        if (HasPlayer) ElectionSystem.ResolveElection(PlayerCountryId, TotalDays);

        // M23.4: Halk refahı — protesto, grev, güven oylaması, erken seçim, darbe girişimi (oyuncu ülkesinde).
        if (HasPlayer) PoliticalUnrestSystem.DailyTick(PlayerCountryId, TotalDays);

        // M12: Ocak 1'de otomatik kayıt (yeni yılın ilk günü)
        if (CurrentDate.Month == 1 && CurrentDate.Day == 1)
        {
            SaveSystem.AutoSave();
            // M24.4: Karakter her oyun yılında 1 yaşlanır (deterministik).
            if (HasCharacter)
            {
                PlayerCharacter.Age += 1;
                GD.Print($"[M24.4] {PlayerCharacter.FullName} yaşlandı: {PlayerCharacter.Age}");
            }
        }

        EmitSignal(SignalName.PlayerCountryDataChanged);
    }

    // M24.2: Oyuncu karakterinin 7 niteliği ilgili istatistik göstergelerini düşük,
    // deterministik olarak etkiler (günlük). CabinetSystem.ApplyMinisterEffects ile aynı pattern.
    private void ApplyCharacterEffects()
    {
        if (PlayerCharacter == null) return;

        var c = PlayerCharacter;
        int applied = 0;

        // Yönetim → yolsuzluk (daha iyi yönetim = daha az yolsuzluk)
        ApplyCharStat("corruption", -(c.Administration - 50.0) / 5000.0, ref applied);
        // İstihbarat → eğitim
        ApplyCharStat("education", (c.Intelligence - 50.0) / 5000.0, ref applied);
        // Ekonomi → GSYİH büyümesi + bütçe açığı (daha iyi ekonomi = daha fazla büyüme, daha az açık)
        ApplyCharStat("gdp_growth", (c.Economy - 50.0) / 5000.0, ref applied);
        ApplyCharStat("budget_deficit", -(c.Economy - 50.0) / 5000.0, ref applied);
        // Hukuk → yargı bağımsızlığı + suç oranı (daha iyi hukuk = daha fazla bağımsızlık, daha az suç)
        ApplyCharStat("judicial_independence", (c.Law - 50.0) / 5000.0, ref applied);
        ApplyCharStat("crime_rate", -(c.Law - 50.0) / 5000.0, ref applied);

        GD.Print($"[CharacterEffects] {c.FullName} nitelik-etkisi: {applied} gösterge güncellendi.");
    }

    private void ApplyCharStat(string indicatorId, double delta, ref int applied)
    {
        if (string.IsNullOrEmpty(indicatorId)) return;
        double current = StatisticsSystem.GetValue(PlayerCountryId, indicatorId);
        StatisticsSystem.SetValue(PlayerCountryId, indicatorId, current + delta);
        applied++;
    }

    public string GetDateText()
    {
        // HOI4 stili: "1 Jan 1936"
        return CurrentDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    public void TogglePause()
    {
        if (IsPaused) SetSpeed(_speedBeforePause);
        else SetSpeed(0);
    }

    // M10: Olay popup'ı için otomatik duraklatma — eski hız kaydedilir.
    // (Zaten pause'daysa hiçbir şey yapma: kullanıcının pause'u bozulmasın.)
    public void PauseForEvent()
    {
        if (!IsPaused)
        {
            SpeedBeforeEventPause = Speed;
            SetSpeed(0);
        }
    }

    // M10: Olay seçimi bitince kaydedilmiş hıza dön.
    // (Kullanıcı popup açıkken manuel PLAY'e bastıysa dokunma.)
    public void ResumeFromEvent()
    {
        if (IsPaused && SpeedBeforeEventPause > 0)
            SetSpeed(SpeedBeforeEventPause);
    }

    public void SetSpeed(int speed)
    {
        speed = Mathf.Clamp(speed, 0, 5);
        if (Speed == speed) return;
        Speed = speed;
        if (speed > 0) _speedBeforePause = speed;
        EmitSignal(SignalName.SpeedChanged, Speed);
    }

    public void SetPlayerCountry(string id)
    {
        // Kilit: oyuncu ülkesi bir kez seçilir, oyun sürerken değiştirilemez.
        // Yalnızca yeni oyun başlatınca ResetForNewGame() temizler.
        if (IsPlayerCountryLocked)
        {
            GD.Print($"GameManager: Oyuncu ülkesi kilitli — {id} reddedildi (yalnızca yeni oyunda değişir)");
            return;
        }

        if (PlayerCountryId == id) return;
        PlayerCountryId = id ?? "";
        _playerCountryData = null; // cache temizle
        var data = PlayerCountryData;
        GD.Print($"GameManager: Oyuncu ülkesi = {PlayerCountryId} ({data?.Name ?? "—"})");

        // M12: headless/test akışında ResetForNewGame atlanabilir — RNG'yi
        // burada da garantiye al (zaten başlatılmışsa dokunma: aynı tohum korunur).
        if (!DeterministicRNG.IsInitialized)
            DeterministicRNG.InitializeForNewGame(_forcedRngSeed);

        // Seçim başarılı → kilitlenir
        IsPlayerCountryLocked = true;

        // Ekonomi sistemini yeni ülkeyle başlat (M4)
        EconomySystem.Initialize(data);
        // Teknoloji sistemini sıfırla (M5)
        TechnologySystem.Initialize();
        // Üretim sistemini başlat (M6)
        EquipmentSystem.Initialize(data);
        // Ordu sistemini başlat (M7)
        ArmySystem.Initialize(data);
        // Savaş sistemini sıfırla (M7)
        WarSystem.Initialize(data);
        // Diplomasi sistemini başlat (M8)
        DiplomacySystem.Initialize(data);
        // M17A: Casus belli sistemini başlat — gerekçe/iddia/ateşkes/meşruiyet
        CasusBelliSystem.Initialize(data);
        // Ulusal odak sistemini başlat — ülkeye özel ağaç yüklenir (M9)
        FocusSystem.Initialize(data);

        // Olay sistemini başlat (M10.1) — tetikleyiciler bu ülkede çalışır
        EventSystem.Initialize(data);

        // Kararlar sistemini başlat (M10.2)
        DecisionSystem.Initialize(data);

        // AI sistemini başlat (M11) — oyuncu DIŞI ülkeleri yönetir
        CountryAI.Initialize();

        EmitSignal(SignalName.PlayerCountryChanged, PlayerCountryId);
        EmitSignal(SignalName.PlayerCountryDataChanged);
    }

    // M24.1: Oyuncu karakterini ayarlar (karakter oluşturma panelsinden çağrılır).
    public void SetCharacter(Character character)
    {
        PlayerCharacter = character ?? throw new ArgumentNullException(nameof(character));
        EmitSignal(SignalName.PlayerCharacterChanged);
        GD.Print($"GameManager: Oyuncu karakteri ayarlandı: {character.FullName} (köken={character.Background}, yaş={character.Age})");
    }

    // Yeni oyun: oturum durumunu sıfırla — oyuncu ülkesi + kilidi dahil.
    // MainMenu START çağırır; ana menü → yeni oyun akışında eski ülke kalmaz.
    public void ResetForNewGame()
    {
        PlayerCountryId = "";
        _playerCountryData = null;
        IsPlayerCountryLocked = false;
        PlayerCharacter = null; // M24.1: yeni oyun → karakter yok
        CurrentDate = StartDate; // M17.1: aktif senaryonun başlangıç tarihi
        TotalDays = 0;
        Speed = 0; // pause — oyuncu hız seçince başlar
        _speedBeforePause = 1;
        _accumulator = 0;

        // M12: yeni oyun → yeni RNG tohumu (test tohumu verilmişse onu koru —
        // --rng-seed ile başlatılan deterministik test aynı tohumla devam etsin)
        DeterministicRNG.InitializeForNewGame(_forcedRngSeed);

        // AI sistemi de sıfırlanır (yeni oyun — oyuncu yok, AI boş kalır)
        CountryAI.Reset();

        // M20.1: İstatistik göstergelerini yeni oyuna başlat
        StatisticsSystem.InitializeAll();

        // M20.2: Bütçe (vergi, harcama, borç, faiz) sistemini yeni oyuna başlat
        BudgetSystem.InitializeAll();

        // M20.4: Göstergeler arası bağlantıları yükle
        FormulaSystem.InitializeAll();

        // M20.3: Halk memnuniyeti gruplarını yeni oyuna başlat
        PublicSatisfactionSystem.InitializeAll();

        // M21.1: Kabine/bakanlık atama sistemini yeni oyuna başlat (hiçbiri atanmamış)
        CabinetSystem.InitializeAll();

        // M22.2: Yasa sistemini yeni oyuna başlat (etkin yasa/etki state'i sıfırlanır)
        LawSystem.ResetForNewGame();

        // M22.3: Parlamento-yeni oyun (parti/sandalye/veto state'i sıfırlanır)
        ParliamentSystem.ResetForNewGame();

        // M22.4: Otoriter sistem-yeni oyun (kongre onayları ve darbe state'i sıfırlanır)
        AutoritarianSystem.ResetForNewGame();

        // M23.1: Seçim takvimi-yeni oyun (son seçim günleri sıfırlanır)
        ElectionSystem.ResetForNewGame();

        // M23.2: Kampanya geçmişi-yeni oyun (kampanya aksiyonları ve vaatler sıfırlanır)
        ElectionCampaignSystem.ResetForNewGame();

        // M23.4: Halk refahı durumu-yeni oyun (protesto/grev/darbe state'i sıfırlanır)
        PoliticalUnrestSystem.ResetForNewGame();

        // M-Flags: bayrak önbelleği sıfırlanır (rapor yeni oyunda tekrar loglanır)
        FlagLibrary.ResetForNewGame();

        // M16: Ajanda/aylık rapor durumu sıfırlanır
        TimeTempoSystem.ResetForNewGame();

        // M25.2: Asker kariyer durumu sıfırlanır
        MilitaryCareerSystem.ResetForNewGame();

        // M25.3: Eğitim modül durumu sıfırlanır
        EducationSystem.ResetForNewGame();

        // M25.4: Aday olma kriter durumu sıfırlanır
        CandidacySystem.ResetForNewGame();

        // M25.5: Kariyer olayı durumu sıfırlanır
        CareerEventSystem.ResetForNewGame();

        // M26.1: Afet durumu sıfırlanır (yeni oyun → eski aktif/geçmiş afetler silinir).
        DisasterSystem.ResetForNewGame();

        // M26.4: Beklenmedik olay durumu sıfırlanır (yeni oyun → eski aktif/geçmiş olaylar silinir).
        UnexpectedEventSystem.ResetForNewGame();

        GD.Print($"GameManager: Yeni oyun — senaryo: {ScenarioSystem.ActiveName}, başlangıç: {StartDate:yyyy-MM-dd}, oyuncu ülkesi ve kilit sıfırlandı");

        EmitSignal(SignalName.PlayerCountryChanged, PlayerCountryId);
        EmitSignal(SignalName.DateChanged, GetDateText());
        EmitSignal(SignalName.DayChanged, (long)TotalDays);
        EmitSignal(SignalName.PlayerCountryDataChanged);
    }

    // Test kancası (--date-jump): oyun tarihini atlatır (Date trigger testleri için)
    // M17.1: TotalDays hesaplaması aktif senaryonun başlangıç tarihine göre yapılır.
    public void JumpToDateForTest(DateTime date)
    {
        CurrentDate = date;
        TotalDays = (int)(date - StartDate).TotalDays;
        EmitSignal(SignalName.DateChanged, GetDateText());
        EmitSignal(SignalName.DayChanged, (long)TotalDays);
        GD.Print($"[Test] Tarih atlatıldı: {GetDateText()} (gün {TotalDays})");
    }

    // Save/Load desteği
    public void ApplyLoadedState(DateTime date, int totalDays, string playerCountryId)
    {
        CurrentDate = date;
        TotalDays = totalDays;
        PlayerCountryId = playerCountryId ?? "";
        _playerCountryData = null;
        // Kayıttan gelen oyuncu ülkesi de kilitlidir (oyun ortasında seçilmiş durumda)
        IsPlayerCountryLocked = PlayerCountryId != "";

        EmitSignal(SignalName.DateChanged, GetDateText());
        EmitSignal(SignalName.PlayerCountryChanged, PlayerCountryId);
        EmitSignal(SignalName.DayChanged, (long)TotalDays);
        EmitSignal(SignalName.PlayerCountryDataChanged);
    }

    // SaveSystem'den LoadGameInternal çağırır — Speed ve _speedBeforePause restore eder
    public void ApplyLoadedRuntime(int speed, bool isPlayerCountryLocked)
    {
        Speed = speed;
        IsPlayerCountryLocked = isPlayerCountryLocked;
        if (speed > 0) _speedBeforePause = speed;
    }

    // Headless test kancası (--save-load-test): günleri manuel ilerletir
    // (_Process/SecondsPerDay beklemez; SaveLoadTest doğrulaması için)
    public void AdvanceDaysForTest(int days)
    {
        for (int i = 0; i < days; i++)
            AdvanceDay();
    }

    // Headless prod test: örnek üretim hatları ekle
    private void SetupProductionTest()
    {
        // Başlangıç ekipman stoku (savaş/egitim testi için — HOI4'te ülkeler stokla başlar)
        EquipmentSystem.AddToStock("infantry_equipment", 30000);
        EquipmentSystem.AddToStock("support_equipment", 5000);

        // Tümenleri yeniden oluştur (Initialize stok=0 iken çalıştığı için 0 tümen vardı)
        ArmySystem.Initialize(PlayerCountryData);

        // Üretim hatları: GER gerçek veri (Mil=20, Dock=4) ile
        EquipmentSystem.AddLine("infantry_equipment", 3);
        EquipmentSystem.AddLine("support_equipment", 2);
        EquipmentSystem.AddLine("light_tank", 2);
        // destroyer (naval): tersane havuzundan
        EquipmentSystem.AddLine("destroyer", 1);

        // Variant tasarla ve light_tank hattına uygula (ceza %50 testi)
        var gunMod = EquipmentSystem.GetModuleOptions("Gun").Find(m => m.Name == "Improved Gun");
        var armorMod = EquipmentSystem.GetModuleOptions("Armor").Find(m => m.Name == "Extra Armor");
        if (gunMod != null && armorMod != null)
        {
            var variant = EquipmentSystem.CreateVariant("light_tank", new List<EquipmentSystem.Module> { gunMod, armorMod });
            if (variant != null)
            {
                // light_tank hattı index 2 (0=inf, 1=sup, 2=lt) — başarısızsa logla
                int ltIndex = -1;
                for (int i = 0; i < EquipmentSystem.Lines.Count; i++)
                    if (EquipmentSystem.Lines[i].EquipmentId == "light_tank") { ltIndex = i; break; }
                if (ltIndex >= 0 && EquipmentSystem.SwitchLineEquipment(ltIndex, variant.Id))
                    GD.Print($"[Test] light_tank hattına variant uygulandı: {variant.Id} (etkinlik cezası %50)");
                else
                    GD.Print("[Test] UYARI: light_tank hattı yok — variant switch atlandı");
            }
        }

        GD.Print("[Test] Prod test kurulumu tamam — 4 hat (7 mil + 1 naval), 1 variant switch");
    }

    /// <summary>
    /// JOB1: 5 oyun yılı (1825 gün) headless simüle eder, her ay aynı anda
    /// savaşta olan AI ülke sayısını loglar. Hedef: en fazla ~%10 AI ülkesi
    /// aynı anda savaşta olmalı. normal oyunda asla çalışmaz (--war-sim).
    /// </summary>
    private void RunWarSimulationTest()
    {
        GD.Print("=========================================");
        GD.Print("  JOB1: 5 YILLIK AI SAVAŞ SİMÜLASYONU");
        GD.Print("=========================================");

        // Deterministik koşullar: sabit tohum + tarafsız gözlemci ülkesi seçimi.
        // Oyuncu ülkesi seçmek zorunlu (DailyTick HasPlayer kontrolü) — küçük bir
        // ülke seçip AI savaşlarını izleyelim (TUR: savaş çıkarmayan gözlemci).
        DeterministicRNG.InitializeForNewGame(0x51D14BADC0DEUL);
        if (!HasPlayer)
            SetPlayerCountry("TUR");

        int totalDays = 1825; // ~5 yıl
        int aiCountryCount = 0;
        foreach (var c in CountryDatabase.All())
            if (c != null && CountryAI.IsAiCountry(c.Id)) aiCountryCount++;

        GD.Print($"[WarSim] AI ülke sayısı: {aiCountryCount} (oyuncu: {PlayerCountryId} yönetilmez)");
        GD.Print($"[WarSim] {totalDays} gün simüle ediliyor (5 yıl)...");

        int maxAtWar = 0;
        int maxAtWarDay = 0;
        double maxAtWarPercent = 0;
        int maxWars = 0;
        var atWarSet = new System.Collections.Generic.HashSet<string>();
        var atWarHistory = new System.Collections.Generic.List<string>();

        for (int day = 1; day <= totalDays; day++)
        {
            AdvanceDaysForTest(1);

            // Her ayın 1'inde savaş sayımını logla
            if (CurrentDate.Day == 1)
            {
                atWarSet.Clear();
                int warCount = 0;
                foreach (var w in WarSystem.ActiveWars)
                {
                    if (w.IsEnded) continue;
                    warCount++;
                    atWarSet.Add(w.AttackerId);
                    atWarSet.Add(w.DefenderId);
                }

                int aiAtWar = 0;
                foreach (var id in atWarSet)
                    if (CountryAI.IsAiCountry(id)) aiAtWar++;

                double pct = aiCountryCount > 0 ? 100.0 * aiAtWar / aiCountryCount : 0;
                string line = $"[WarSim] {CurrentDate:yyyy-MM-dd}: savaş={warCount}, savaşta AI ülke={aiAtWar}/{aiCountryCount} (%{pct:F1})";
                atWarHistory.Add(line);
                GD.Print(line);

                if (aiAtWar > maxAtWar)
                {
                    maxAtWar = aiAtWar;
                    maxAtWarDay = TotalDays;
                    maxAtWarPercent = pct;
                }
                if (warCount > maxWars) maxWars = warCount;
            }
        }

        GD.Print("=========================================");
        GD.Print($"[WarSim] SONUÇ: en yüksek eş zamanlı savaşta AI = {maxAtWar}/{aiCountryCount} (%{maxAtWarPercent:F1}) — gün {maxAtWarDay}");
        GD.Print($"[WarSim] En yüksek eş zamanlı savaş sayısı = {maxWars}");
        GD.Print($"[WarSim] Hedef: %{_aiWarSimTargetPercent} altı");
        bool warSimPass = maxAtWarPercent <= _aiWarSimTargetPercent;
        if (warSimPass)
            GD.Print("[WarSim] HEDEF BAŞARILI ✓");
        else
            GD.Print("[WarSim] HEDEF AŞILDI ✗ — gerekçe şansı daha da düşürülmeli");
        GD.Print("=========================================");
        GetTree().Quit(warSimPass ? 0 : 1);
    }

    // JOB1 hedefi: AI ülkelerinin ~%10'undan fazlası aynı anda savaşta olmamalı
    private const double _aiWarSimTargetPercent = 10.0;

    /// <summary>
    /// M16: Ajanda sistem testi — aksiyon ekle + gün ilerlet + efekt kontrol.
    /// Normal oyunda asla çalışmaz (--agenda-test).
    /// </summary>
    private void RunAgendaTest()
    {
        GD.Print("=========================================");
        GD.Print("  M16: AJANDA SİSTEM TESTİ");
        GD.Print("=========================================");

        // Oyuncu ülke + PP top-up (aksiyon maliyetleri için)
        if (!HasPlayer)
            SetPlayerCountry("GER");
        if (PlayerCountryData != null)
            PlayerCountryData.PoliticalPower = 200;
        if (Speed == 0) SetSpeed(0); // manuel gün ilerletme — pause kalır

        double staBefore = PlayerCountryData.StabilityPercent;
        double wsBefore = PlayerCountryData.WarSupportPercent;

        // 1) Toplantı (1 gün, 5 PP) → slot 0
        bool ok1 = TimeTempoSystem.AddActionToSlot(0, "meeting");
        GD.Print($"[AgendaTest] meeting → slot 0: {(ok1 ? "OK" : "FAIL")}");

        // 2) Basın konuşması (1 gün, 10 PP) → slot 1
        bool ok2 = TimeTempoSystem.AddActionToSlot(1, "press");
        GD.Print($"[AgendaTest] press → slot 1: {(ok2 ? "OK" : "FAIL")}");

        // 3) Aynı slota ikinci aksiyon → REDDEDİLMELİ
        bool ok3 = TimeTempoSystem.AddActionToSlot(0, "rally");
        GD.Print($"[AgendaTest] rally → dolu slot 0: {(ok3 ? "FAIL — kabul edildi!" : "OK — reddedildi")}");

        // 4) Max eş zamanlı aksiyon (3) aşımı → REDDEDİLMELİ
        bool ok4a = TimeTempoSystem.AddActionToSlot(2, "meeting");
        bool ok4b = TimeTempoSystem.AddActionToSlot(3, "meeting");
        GD.Print($"[AgendaTest] 3. ve 4. eş zamanlı aksiyon: {ok4a}/{ok4b} (4. reddedilmeli)");

        GD.Print($"[AgendaTest] Aktif aksiyon: {TimeTempoSystem.ActiveActions.Count}, kullanılan slot: {TimeTempoSystem.SlotsUsedThisWeek}/{TimeTempoSystem.MaxSlotsPerWeekConst}");

        // 5) 3 gün ilerlet — meeting (1g) ve press (1g) tamamlanmalı
        AdvanceDaysForTest(3);

        GD.Print($"[AgendaTest] 3 gün sonra aktif aksiyon: {TimeTempoSystem.ActiveActions.Count} (0 olmalı)");
        GD.Print($"[AgendaTest] Stability: {staBefore:F1} → {PlayerCountryData.StabilityPercent:F1} (+2 beklenir)");
        GD.Print($"[AgendaTest] WarSupport: {wsBefore:F1} → {PlayerCountryData.WarSupportPercent:F1} (+1 beklenir)");

        bool pass = TimeTempoSystem.ActiveActions.Count == 0
                    && PlayerCountryData.StabilityPercent >= staBefore + 2
                    && PlayerCountryData.WarSupportPercent >= wsBefore + 1;
        GD.Print(pass ? "[AgendaTest] SONUÇ: PASS ✓" : "[AgendaTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M17.1: Senaryo yükleme testi — her iki senaryo için veri yükleyici sayılarını
    /// gerçek assertlerle doğrular. Normal oyunda asla çalışmaz (--scenario-test).
    /// </summary>
    private void RunScenarioTest()
    {
        GD.Print("=========================================");
        GD.Print("  M17.1: SENARYO YÜKLEME TESTİ");
        GD.Print("=========================================");

        var all = ScenarioSystem.All;
        bool result = all.Count >= 2;

        foreach (var s in all)
        {
            ScenarioSystem.SetActive(s.Id);

            int c = CountryDatabase.All().Count();
            int fTrees = FocusSystem.Trees.Count;
            int fFocuses = FocusSystem.Trees.Values.Sum(t => t.Focuses.Count);
            int e = EventSystem.AllEvents.Count;
            int d = DecisionSystem.AllDecisions.Count;
            int t = TechnologySystem.All.Count;
            int lib = LibrarySystem.TextEntryCount;
            int cbTypes = CasusBelliSystem.TypesCount;
            int cbClaims = CasusBelliSystem.ClaimsCount;
            int leaders = LeaderSystem.LoadedCount;

            GD.Print($"  [{s.Id}] Ülke:{c}, Odak:{fTrees}/{fFocuses}, Olay:{e}, Karar:{d}, Tech:{t}, Kütüphane:{lib}, CB:{cbTypes}/{cbClaims}, Lider:{leaders}");

            bool ok;
            if (s.Id == "modern_world")
            {
                // Modern dünya: GENERIC odak ağacı (1 ağaç / 10 fokus), modern teknik taban (54 tech),
                // 50 ülke + 50 kütüphane girişi, olay/karar/gerekçe/lider yüklemesi talebe göre (lazy) olur.
                ok = c == 50 && fTrees >= 1 && fFocuses >= 10 && e == 0 && d == 0
                     && t > 0 && lib == 50 && cbTypes == 0 && cbClaims == 0;
                GD.Print($"     modern_world assert: {ok} (50 ülke / >=1 odak agaci / >=10 fokus / 0 olay / 0 karar / >0 tech / 50 kutuphane / 0 CB / 0 iddiâ)");
            }
            else if (s.Id == "historical_1936")
            {
                // Tarihi 1936: 8 odak agaci / 73 fokus, 73 tech, 50 ülke + 50 kütüphane girişi.
                ok = c == 50 && fTrees == 8 && fFocuses == 73 && e == 0 && d == 0
                     && t == 73 && lib == 50 && cbTypes == 0 && cbClaims == 0;
                GD.Print($"     historical_1936 assert: {ok} (50 ulke / 8 odak-73 fokus / 0 olay / 0 karar / 73 tech / 50 kutuphane / 0 CB / 0 iddiâ)");
            }
            else
            {
                ok = c > 0; // bilinmeyen senaryo: yalnızca ülke yüklü olsun
                GD.Print($"     [{s.Id}] bilinmeyen senaryo — yalnızca ülke>0 kontrolü: {ok}");
            }

            result = result && ok;
        }

        GD.Print(result ? "[ScenarioTest] SONUÇ: PASS ✓" : "[ScenarioTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M21.5: Kabine toplantısı testi — "meeting" aksiyonunu ajanda slota ekler,
    /// 1 gün ilerler, bakan sadakasının arttığını doğrular. Normal oyunda asla çalışmaz (--cabinet-meeting-test).
    /// </summary>
    private void RunCabinetMeetingTest()
    {
        GD.Print("=========================================");
        GD.Print("  M21.5: KABINE TOPLANTISI TESTI");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 200;
        if (Speed == 0) SetSpeed(0);

        // Oyuncu ülkesinde İçişleri bakanı atanmış olmalı; yoksa test için uygun bir aday atanır (test kanca kodu, normal oyunda çalışmaz)
        var playerId = PlayerCountryId;
        // Minister verileri ISO kodla anahtarlanır (DEU), PlayerCountryId ise HOI4 kodu olabilir (GER) — lookup'ta normalize et.
        var lookupId = CountryDatabase.NormalizeId(playerId);
        string ministerId = CabinetSystem.GetMinister(playerId, "interior");
        if (ministerId == null)
        {
            var available = CabinetSystem.GetAvailableMinisters(lookupId, "interior");
            if (available.Count == 0)
            {
                GD.Print("[CabinetMeetingTest] İçişleri bakanı atanmamış ve uygun aday yok — test atlanıyor");
                GD.Print("=========================================");
                return;
            }
            ministerId = available[0].Id;
            CabinetSystem.AssignMinister(playerId, "interior", ministerId);
            GD.Print($"[CabinetMeetingTest] Test için İçişleri bakanı atandı: {ministerId}");
        }

        var minister = CabinetSystem.Ministers.FirstOrDefault(m => m.Id == ministerId);
        if (minister == null)
        {
            GD.Print("[CabinetMeetingTest] İçişleri bakanı bulunamadı — test atlanıyor");
            GD.Print("=========================================");
            return;
        }

        int ministerBefore = minister.Loyalty;
        GD.Print($"[CabinetMeetingTest] {minister.Name} sadakati toplantı öncesi: {ministerBefore}");

        // Meeting aksiyonu: 1 gün, 5 PP, MinisterLoyaltyBonus = 2
        bool ok = TimeTempoSystem.AddActionToSlot(0, "meeting");
        GD.Print($"[CabinetMeetingTest] meeting → slot 0: {(ok ? "OK" : "FAIL")}");

        // 1 gün ilerle (meeting süresi 1 gün) → etkiler uygulanır
        AdvanceDaysForTest(1);

        int ministerAfter = minister.Loyalty;
        GD.Print($"[CabinetMeetingTest] {minister.Name} sadakati toplantı sonrası: {ministerAfter} (delta={ministerAfter - ministerBefore})");

        bool pass = ok && ministerAfter == Math.Clamp(ministerBefore + 2, 0, 100);
        GD.Print(pass ? "[CabinetMeetingTest] SONUÇ: PASS ✓" : "[CabinetMeetingTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M27: Lider diploması testi — görüşme (HoldMeeting) iki yönlü ilişkiye etki eder,
    /// zirve/devlet ziyareti dünya gerilimini azaltır, görüşme kaydı oluşturulur.
    /// Normal oyunda ASLA çalışmaz (--diplomacy-test).
    /// </summary>
    private void RunDiplomacyTest()
    {
        GD.Print("=========================================");
        GD.Print("  M27: LIDER DİPLOMASI TESTİ (--diplomacy-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;

        // Hedef ülke: oyuncu dışı, işgal edilmemiş ilk ülke
        string targetId = null;
        foreach (var c in CountryDatabase.All())
        {
            if (c.Id != playerId && !c.IsAnnexed) { targetId = c.Id; break; }
        }
        if (targetId == null)
        {
            GD.Print("[DiplomacyTest] Uygun hedef ülke bulunamadı — test atlanıyor");
            GD.Print("=========================================");
            return;
        }

        int relationBefore = DiplomacySystem.GetRelation(playerId, targetId);
        float tensionBefore = DiplomacySystem.WorldTension;
        int meetingsBefore = DiplomacySystem.Meetings.Count;
        GD.Print($"[DiplomacyTest] {playerId} → {targetId}: ilişki öncesi={relationBefore}, tension öncesi=%{tensionBefore:F1}");

        // Tüm görüşme türlerini dene
        var types = new[]
        {
            DiplomacySystem.DiplomacyMeeting.Type.PhoneCall,
            DiplomacySystem.DiplomacyMeeting.Type.BilateralMeeting,
            DiplomacySystem.DiplomacyMeeting.Type.StateVisit,
            DiplomacySystem.DiplomacyMeeting.Type.Summit
        };
        bool allOk = true;
        foreach (var type in types)
        {
            bool ok = DiplomacySystem.HoldMeeting(playerId, targetId, type);
            GD.Print($"[DiplomacyTest] HoldMeeting({type}): {(ok ? "OK" : "FAIL")}");
            if (!ok) allOk = false;
        }

        int relationAfter = DiplomacySystem.GetRelation(playerId, targetId);
        float tensionAfter = DiplomacySystem.WorldTension;
        int meetingsAfter = DiplomacySystem.Meetings.Count;
        GD.Print($"[DiplomacyTest] ilişki sonrası={relationAfter} (delta={relationAfter - relationBefore}), tension sonrası=%{tensionAfter:F1} (delta={tensionAfter - tensionBefore:+#;-#;0}), görüşme={meetingsBefore}→{meetingsAfter}");

        // İlişki artmış, en az 4 görüşme kaydı olmuş, zirve/devlet ziyareti tension azaltmış olmalı
        bool relationImproved = relationAfter > relationBefore;
        bool meetingsRecorded = meetingsAfter >= meetingsBefore + 4;
        bool tensionReduced = tensionAfter <= tensionBefore; // StateVisit(-1) + Summit(-2) azaltır
        bool pass = allOk && relationImproved && meetingsRecorded && tensionReduced;

        GD.Print(pass ? "[DiplomacyTest] SONUÇ: PASS ✓" : "[DiplomacyTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M27.2: Anlaşma testi — ticaret/savunma/vize/enerji/iklim anlaşması imzalanır,
    /// iki yönlü ilişkiye etki देखilir, savunma karşılıklı garanti ekler, iklim tension
    /// azaltır, iptal edilebilir. Normal oyunda ASLA çalışmaz (--agreements-test).
    /// </summary>
    private void RunAgreementsTest()
    {
        GD.Print("=========================================");
        GD.Print("  M27.2: ANALAŞMA TESTİ (--agreements-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;

        // Hedef ülke: oyuncu dışı, işgal edilmemiş ilk ülke
        string targetId = null;
        foreach (var c in CountryDatabase.All())
        {
            if (c.Id != playerId && !c.IsAnnexed) { targetId = c.Id; break; }
        }
        if (targetId == null)
        {
            GD.Print("[AgreementsTest] Uygun hedef ülke bulunamadı — test atlanıyor");
            GD.Print("=========================================");
            return;
        }

        int relationBefore = DiplomacySystem.GetRelation(playerId, targetId);
        float tensionBefore = DiplomacySystem.WorldTension;
        int agreementsBefore = DiplomacySystem.Agreements.Count;
        GD.Print($"[AgreementsTest] {playerId} ↔ {targetId}: ilişki öncesi={relationBefore}, tension öncesi=%{tensionBefore:F1}, anlaşma={agreementsBefore}");

        // Tüm anlaşma türlerini imzala
        var types = new[]
        {
            DiplomacySystem.Agreement.Type.Trade,
            DiplomacySystem.Agreement.Type.Defense,
            DiplomacySystem.Agreement.Type.Visa,
            DiplomacySystem.Agreement.Type.Energy,
            DiplomacySystem.Agreement.Type.Climate
        };
        bool allOk = true;
        string lastId = null;
        foreach (var type in types)
        {
            bool ok = DiplomacySystem.SignAgreement(playerId, targetId, type, 0); // süresiz
            GD.Print($"[AgreementsTest] SignAgreement({type}): {(ok ? "OK" : "FAIL")}");
            if (ok) lastId = DiplomacySystem.Agreements.Last().Id;
            else allOk = false;
        }

        // Bir anlaşmayı iptal et
        bool cancelled = false;
        if (lastId != null)
        {
            cancelled = DiplomacySystem.CancelAgreement(lastId);
            GD.Print($"[AgreementsTest] CancelAgreement({lastId}): {(cancelled ? "OK" : "FAIL")}");
        }

        int relationAfter = DiplomacySystem.GetRelation(playerId, targetId);
        float tensionAfter = DiplomacySystem.WorldTension;
        int agreementsAfter = DiplomacySystem.Agreements.Count;
        int activeForPlayer = DiplomacySystem.GetActiveAgreementsFor(playerId).Count;
        GD.Print($"[AgreementsTest] ilişki sonrası={relationAfter} (delta={relationAfter - relationBefore}), tension sonrası=%{tensionAfter:F1} (delta={tensionAfter - tensionBefore:+#;-#;0}), anlaşma={agreementsBefore}→{agreementsAfter}, aktif({playerId})={activeForPlayer}");

        bool relationImproved = relationAfter > relationBefore;
        bool agreementsRecorded = agreementsAfter >= agreementsBefore + 5;
        bool tensionReduced = tensionAfter <= tensionBefore; // iklim -1
        bool pass = allOk && cancelled && relationImproved && agreementsRecorded && tensionReduced;

        GD.Print(pass ? "[AgreementsTest] SONUÇ: PASS ✓" : "[AgreementsTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M27.3: Basın etkinliği testi — basın toplantısı / TV konuşması / miting, ton seçimi
    /// (sert/uzlaşmacı/popülist), gösterge etkileri (gerilim/istikrar/savaş desteği/ilişki),
    /// deterministik geri-etki. Normal oyunda ASLA çalışmaz (--press-test).
    /// </summary>
    private void RunPressTest()
    {
        GD.Print("=========================================");
        GD.Print("  M27.3: BASIN ETKINLIGI TESTI (--press-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;

        // Hedef iliski olcusu icin diger bir ulke (olcekleme kontrolu)
        string otherId = null;
        foreach (var c in CountryDatabase.All())
        {
            if (c.Id != playerId && !c.IsAnnexed) { otherId = c.Id; break; }
        }
        if (otherId == null)
        {
            GD.Print("[PressTest] Uygun diger ulke bulunamadi — test atlaniyor");
            GD.Print("=========================================");
            return;
        }

        float tensionBefore = DiplomacySystem.WorldTension;
        double stabilityBefore = GameManager.Instance.PlayerCountryData.StabilityPercent;
        double warSupportBefore = GameManager.Instance.PlayerCountryData.WarSupportPercent;
        int relationBefore = DiplomacySystem.GetRelation(playerId, otherId);
        int eventsBefore = DiplomacySystem.PressEvents.Count;
        GD.Print($"[PressTest] {playerId}: tension once=%{tensionBefore:F1}, istikrar once={stabilityBefore:F0}, " +
                 $"savus desteği once={warSupportBefore:F0}, iliski({playerId}->{otherId}) once={relationBefore}, basin once={eventsBefore}");

        // Farkli (tur, ton) kombinasyonlarini dene
        var combos = new[]
        {
            (DiplomacySystem.PressEvent.Type.PressConference, DiplomacySystem.PressEvent.Tone.Conciliatory),
            (DiplomacySystem.PressEvent.Type.TVSpeech, DiplomacySystem.PressEvent.Tone.Hardline),
            (DiplomacySystem.PressEvent.Type.Rally, DiplomacySystem.PressEvent.Tone.Populist)
        };
        bool allOk = true;
        foreach (var (type, tone) in combos)
        {
            bool ok = DiplomacySystem.HoldPressEvent(playerId, type, tone);
            GD.Print($"[PressTest] HoldPressEvent({type}, {tone}): {(ok ? "OK" : "FAIL")}");
            if (!ok) allOk = false;
        }

        float tensionAfter = DiplomacySystem.WorldTension;
        double stabilityAfter = GameManager.Instance.PlayerCountryData.StabilityPercent;
        double warSupportAfter = GameManager.Instance.PlayerCountryData.WarSupportPercent;
        int relationAfter = DiplomacySystem.GetRelation(playerId, otherId);
        int eventsAfter = DiplomacySystem.PressEvents.Count;
        GD.Print($"[PressTest] tension sonra=%{tensionAfter:F1} (delta={tensionAfter - tensionBefore:+#;-#;0}), " +
                 $"istikrar sonra={stabilityAfter:F0} (delta={stabilityAfter - stabilityBefore:+#;-#;0}), " +
                 $"savus desteği sonra={warSupportAfter:F0} (delta={warSupportAfter - warSupportBefore:+#;-#;0}), " +
                 $"iliski sonra={relationAfter} (delta={relationAfter - relationBefore}), basin={eventsBefore}->{eventsAfter}");

        bool anyIndicatorChanged = tensionAfter != tensionBefore || stabilityAfter != stabilityBefore ||
                                   warSupportAfter != warSupportBefore || relationAfter != relationBefore;
        bool eventsRecorded = eventsAfter >= eventsBefore + 3;
        bool pass = allOk && eventsRecorded && anyIndicatorChanged;

        GD.Print(pass ? "[PressTest] SONUÇ: PASS ✓" : "[PressTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    private void RunConstitutionTest()
    {
        GD.Print("=========================================");
        GD.Print("  M22.1: ANAYASA TESTİ");
        GD.Print("=========================================");

        int count = ConstitutionSystem.Constitutions.Count;
        int pass = 0;
        foreach (var kv in ConstitutionSystem.Constitutions.OrderBy(k => k.Key))
        {
            var c = kv.Value;
            bool ok = true;
            string reason = "";
            if (c.GovernmentType == ConstitutionSystem.GovernmentType.Monarchy)
            {
                if (c.VotingAge != 0 || c.TermLength != 0) { ok = false; reason = "monarşi için dönem/yaş 0 olmalı"; }
            }
            else
            {
                if (c.VotingAge <= 0) { ok = false; reason = "geçersiz seçim yaşı"; }
                if (c.TermLength <= 0) { ok = false; reason = "geçersiz dönem süresi"; }
            }
            GD.Print($"[ConstitutionTest] {c.CountryId}: {c.GovernmentType}, dönem={c.TermLength}yıl, limit={c.TermLimit}, oy yaşı={c.VotingAge} → {(ok ? "OK" : "FAIL: " + reason)}");
            if (ok) pass++;
        }
        bool result = pass == count;
        GD.Print(result ? "[ConstitutionTest] SONUÇ: PASS ✓" : $"[ConstitutionTest] SONUÇ: FAIL ✗ ({pass}/{count})");
        GetTree().Quit(result ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M22.2: Yasa sistemi testi — yasa çıkar (PP ödeme + etki), etkin yasaları listeler,
    /// yasa kaldırır (etki geri al), sonra yasa kalmadığını doğrular. Normal oyunda ASLA çalışmaz (--laws-test).
    /// </summary>
    private void RunLawsTest()
    {
        GD.Print("=========================================");
        GD.Print("  M22.2: YASA SİSTEM TESTİ");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 500;
        if (Speed == 0) SetSpeed(0);

        var playerId = PlayerCountryId;

        // 1) Tanımlı bir yasa var mı? (law_econ_tax_cut: Economy, 80 PP, gdp_growth +0.20)
        var law = LawSystem.GetLaw("law_econ_tax_cut");
        if (law == null)
        {
            GD.Print("[LawsTest] law_econ_tax_cut bulunamadı — test atlanıyor");
            GD.Print("=========================================");
            return;
        }
        GD.Print($"[LawsTest] Yasa: {law.Name} ({law.Category}, {law.CostPP} PP), efekt: gdp_growth={law.Effects.GetValueOrDefault("gdp_growth")}");

        double ppBefore = PlayerCountryData.PoliticalPower;
        GD.Print($"[LawsTest] PP öncesi: {ppBefore:F1}");

        // 2) Yasa çıkar — etki uygulanmalı, PP düşmeli
        bool enacted = LawSystem.EnactLaw(playerId, law.Id);
        GD.Print($"[LawsTest] Yasa çıkarıldı: {(enacted ? "OK" : "FAIL")}");
        GD.Print($"[LawsTest] PP sonrası: {PlayerCountryData.PoliticalPower:F1} (beklenen {ppBefore - law.CostPP:F1})");

        double gdpAfterEnact = LawSystem.GetLawEffect(playerId, "gdp_growth");
        int activeCount = LawSystem.GetActiveLaws(playerId).Count;
        int availableCount = LawSystem.GetAvailableLaws(playerId).Count;
        GD.Print($"[LawsTest] gdp_growth etki: {gdpAfterEnact:F2} (beklenen 0.20), etkin yasa: {activeCount} (beklenen 1), mevcut yasa: {availableCount}");

        // 3) Aynı yasa tekrar çıkarılamamalı
        bool dup = LawSystem.EnactLaw(playerId, law.Id);
        GD.Print($"[LawsTest] Aynı yasa ikinci kez: {(dup ? "FAIL — kabul edildi!" : "OK — reddedildi")}");

        // 4) Yasa kaldır — etki geri alınmalı
        bool repealed = LawSystem.RepealLaw(playerId, law.Id);
        GD.Print($"[LawsTest] Yasa kaldırıldı: {(repealed ? "OK" : "FAIL")}");
        double gdpAfterRepeal = LawSystem.GetLawEffect(playerId, "gdp_growth");
        int activeAfter = LawSystem.GetActiveLaws(playerId).Count;
        GD.Print($"[LawsTest] gdp_growth etki kaldırılınca: {gdpAfterRepeal:F2} (beklenen 0), etkin yasa: {activeAfter} (beklenen 0)");

        // 5) Kaldırıldıktan sonra tekrar çıkarılabilmeli
        bool reEnacted = LawSystem.EnactLaw(playerId, law.Id);
        GD.Print($"[LawsTest] Tekrar çıkarıldı: {(reEnacted ? "OK" : "FAIL")}");
        LawSystem.RepealLaw(playerId, law.Id); // temizle

        bool pass = enacted && !dup && repealed
                    && Math.Abs(gdpAfterEnact - 0.20) < 0.001
                    && Math.Abs(gdpAfterRepeal) < 0.001
                    && activeCount == 1 && activeAfter == 0;
        GD.Print(pass ? "[LawsTest] SONUÇ: PASS ✓" : "[LawsTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M18: İl sistemi headless testi — il sayısı, sahiplik, komşuluk simetrisi, deterministik üretim.
    /// Normal oyunda ASLA çalışmaz (--province-test).
    /// </summary>
    private void RunProvinceTest()
    {
        GD.Print("=========================================");
        GD.Print("  M18: İL SİSTEM TESTİ");
        GD.Print("=========================================");

        // 1) İlleri yükle (sabit seed → deterministik)
        var source = new ProceduralProvinceSource();
        var provs1 = source.LoadProvinces();
        int count1 = provs1.Count;
        GD.Print($"[ProvinceTest] 1. yükleme: {count1} il (hedef ~1000)");

        // 2) Aynı seed ile yeniden üret → aynı sayı ve ID'ler gelmeli
        source.ResetForNewGame();
        var provs2 = source.LoadProvinces();
        int count2 = provs2.Count;
        bool sameCount = count1 == count2;
        bool sameIds = true;
        for (int i = 0; i < Math.Min(count1, count2); i++)
            if (provs1[i].Id != provs2[i].Id) { sameIds = false; break; }
        GD.Print($"[ProvinceTest] 2. yükleme: {count2} il — aynı sayı: {sameCount}, aynı ID'ler: {sameIds}");

        // 3) Komşuluk simetrisi: A'nın komşusu B ise B'nin komşusu A olmalı
        bool symmetric = true;
        foreach (var p in provs1)
        {
            foreach (var n in p.NeighborIds)
            {
                var neighbor = provs1.FirstOrDefault(pp => pp.Id == n);
                if (neighbor == null || !neighbor.NeighborIds.Contains(p.Id))
                { symmetric = false; break; }
            }
            if (!symmetric) break;
        }
        GD.Print($"[ProvinceTest] Komşuluk simetrik: {symmetric}");

        // 4) Sahiplik: InitializeFromProvinces → her ilin sahibi ülke ID'si
        ProvinceOwnershipSystem.InitializeFromProvinces(provs1);
        int owned = 0;
        int unowned = 0;
        foreach (var p in provs1)
        {
            string owner = ProvinceOwnershipSystem.GetProvinceOwner(p.Id);
            if (owner == "?") unowned++; else owned++;
        }
        GD.Print($"[ProvinceTest] Sahiplik: sahipli={owned}, sahipsiz={unowned}");

        // 5) GetProvincesByOwner reverse lookup
        var germanProvinces = ProvinceOwnershipSystem.GetProvincesByOwner("DEU");
        GD.Print($"[ProvinceTest] DEU illeri: {germanProvinces.Count}");

        bool result = sameCount && sameIds && symmetric && unowned == 0;
        GD.Print(result ? "[ProvinceTest] SONUÇ: PASS ✓" : "[ProvinceTest] SONUÇ: FAIL ✗");
        GetTree().Quit(result ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M19: Birlik hareketi headless testi — oluşturma, hareket, düşman teması durdurma, save/load kimlik.
    /// Normal oyunda ASLA çalışmaz (--army-test).
    /// </summary>
    private void RunArmyTest()
    {
        GD.Print("=========================================");
        GD.Print("  M19: BİRLİK HAREKETİ TESTİ");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (PlayerCountryData != null)
        {
            PlayerCountryData.PoliticalPower = 500;
            PlayerCountryData.ManpowerPool = 100000;
        }
        if (Speed == 0) SetSpeed(0);

        // 1) Ordular sistemi zaten Initialize edilmiş (SetPlayerCountry'de). Ordu oluştur.
        var army = ArmySystem.CreateArmy("Test Ordusu");
        GD.Print($"[ArmyTest] Ordu oluşturuldu: {army.Name} (ID={army.Id})");

        // 2) Şablon ve tümen oluştur, ordula bağla
        var tpl = ArmySystem.CreateTemplate("Test Şablonu", new List<string> { "infantry", "infantry", "artillery" });
        if (tpl == null)
        {
            GD.Print("[ArmyTest] Şablon oluşturulamadı — test atlanıyor");
            GD.Print("=========================================");
            return;
        }
        var div = ArmySystem.CreateDivision(tpl.Id, immediateDeploy: true);
        ArmySystem.AssignDivisionToArmy(div.Id, army.Id);
        GD.Print($"[ArmyTest] Tümen oluşturuldu ve ordula bağlandı: {div.DisplayName} → {army.Name}");

        // 3) İl konumu ata ve hareket komutu ver
        var source = new ProceduralProvinceSource();
        var allProvs = source.LoadProvinces();
        string startProv = allProvs.FirstOrDefault(p => p.CountryId == "DEU")?.Id ?? "DEU-01";
        string targetProv = allProvs.FirstOrDefault(p => p.CountryId == "FRA")?.Id ?? "FRA-01";
        army.ProvinceId = startProv;
        army.TargetProvinceId = targetProv;
        army.WaypointIds = new List<string> { targetProv };
        army.DaysRemainingInHop = army.DaysPerProvince;
        GD.Print($"[ArmyTest] Konum: {startProv} → hedef: {targetProv}, IsMoving={army.IsMoving}");

        // 4) Günlük tick ile hareket ettir (2 gün = DaysPerProvince)
        AdvanceDaysForTest(2);
        GD.Print($"[ArmyTest] 2 gün sonra ProvinceId: {army.ProvinceId} (beklenen {targetProv}), IsMoving={army.IsMoving}");

        // 5) Save/Load kimlik — ordunun state'ini kaydet ve yükle
        var saveData = ArmySystem.GetSaveData();
        int armyCountBefore = saveData.Armies?.Length ?? 0;
        int divCountBefore = saveData.Divisions?.Length ?? 0;
        GD.Print($"[ArmyTest] Save: ordular={armyCountBefore}, tümenler={divCountBefore}");

        // State'i sıfırla ve yeniden yükle
        ArmySystem.ResetForScenario();
        ArmySystem.LoadSaveData(saveData);
        var loadedArmies = ArmySystem.Armies;
        var loadedDivs = ArmySystem.Divisions;
        GD.Print($"[ArmyTest] Load: ordular={loadedArmies.Count}, tümenler={loadedDivs.Count}");

        var loadedArmy = loadedArmies.FirstOrDefault(a => a.Id == army.Id);
        bool armyIdentity = loadedArmy != null
            && loadedArmy.Name == army.Name
            && loadedArmy.ProvinceId == army.ProvinceId
            && loadedArmy.IsMoving == army.IsMoving;
        GD.Print($"[ArmyTest] Ordu kimliği korundu: {armyIdentity} (ProvinceId={loadedArmy?.ProvinceId ?? "null"})");

        bool result = army != null && div != null
            && army.ProvinceId == targetProv // hareket tamamlandı: hedef ilde duruyor
            && armyCountBefore > 0 && divCountBefore > 0
            && armyIdentity;
        GD.Print(result ? "[ArmyTest] SONUÇ: PASS ✓" : "[ArmyTest] SONUÇ: FAIL ✗");
        GetTree().Quit(result ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M22.3: Parlamento headless testi — parti/sandalye yükleme, çoğunluk eşiği,
    /// sandalye ağırlıklı oylama, veto, koalisyon, referendum. Normal oyunda ASLA çalışmaz (--parliament-test).
    /// </summary>
    private void RunParliamentTest()
    {
        GD.Print("=========================================");
        GD.Print("  M22.3: PARLAMENTO SİSTEM TESTİ");
        GD.Print("=========================================");

        // 1) Partiler yüklendi mi? (7 parti, toplam 460 sandalye, çoğunluk eşiği 231)
        int partyCount = ParliamentSystem.Parties.Count;
        int totalSeats = ParliamentSystem.TotalSeats;
        int threshold = ParliamentSystem.MajorityThreshold;
        GD.Print($"[ParliamentTest] Parti sayısı: {partyCount} (beklenen 7), toplam sandalye: {totalSeats} (beklenen 460), çoğunluk eşiği: {threshold} (beklenen 231)");

        // 2) Çoğunluk denetimi: iki büyük parti (120+110=230) eşiğe ulaşamaz; üç parti (120+110+80=310) ulaşır.
        bool twoParties = ParliamentSystem.HasMajority(new[] { "p_conservative", "p_social_democrat" }); // 230 < 231
        bool threeParties = ParliamentSystem.HasMajority(new[] { "p_conservative", "p_social_democrat", "p_liberal" }); // 310 >= 231
        GD.Print($"[ParliamentTest] Conservative+SocialDemocrat=230 çoğunluk mu: {twoParties} (beklenen False)");
        GD.Print($"[ParliamentTest] +Liberal=310 çoğunluk mu: {threeParties} (beklenen True)");

        // 3) CanPass (oylama geçebilme) — destek sandalyesi >= eşik
        bool canPass = ParliamentSystem.CanPass(new[] { "p_conservative", "p_nationalist", "p_centrist" }); // 120+70+10=200 < 231 -> False
        GD.Print($"[ParliamentTest] CanPass Conservative+Nationalist+Centrist=200: {canPass} (beklenen False)");

        // 4) Oylama: Evet sandalyesi >= eşik geçer, aksi halde kalır
        var vote = ParliamentSystem.Vote("law_test_1",
            new[] { "p_conservative", "p_social_democrat", "p_liberal" }, // 310 evet
            new[] { "p_nationalist", "p_green", "p_communist" },           // 140 hayır
            new[] { "p_centrist" });                                       // 10 tarafsız
        GD.Print($"[ParliamentTest] Oylama geçti mi: {vote.Passed} (beklenen True), YesSeats={vote.YesSeats}");

        // 5) Veto: yürütme organı veto koyar → teklif engellenir (IsVetoed true)
        ParliamentSystem.Veto("law_test_2");
        bool vetoed = ParliamentSystem.IsVetoed("law_test_2");
        ParliamentSystem.RemoveVeto("law_test_2");
        bool vetoRemoved = !ParliamentSystem.IsVetoed("law_test_2");
        GD.Print($"[ParliamentTest] Veto uygulandı: {vetoed} (beklenen True), veto kaldırıldı: {vetoRemoved} (beklenen True)");

        // 6) Koalisyon: birleşik sandalye çoğunluk oluşturuyor mu?
        var coalition = ParliamentSystem.FormCoalition(new[] { "p_social_democrat", "p_liberal", "p_green" }); // 110+80+40=230 < 231
        bool coalitionMajority = coalition.HasMajority; // False
        GD.Print($"[ParliamentTest] Koalisyon SD+Lib+Green={coalition.CombinedSeats} sandalye, çoğunluk mu: {coalitionMajority} (beklenen False)");

        // 7) Referandum: direkt oy (sandalye ağırlığı yok)
        var refr = ParliamentSystem.Referendum("issue_test_1", 55, 45);
        GD.Print($"[ParliamentTest] Referandum kabul edildi mi: {refr.Passed} (beklenen True)");

        bool result = partyCount == 7
                      && totalSeats == 460
                      && threshold == 231
                      && !twoParties
                      && threeParties
                      && !canPass
                      && vote.Passed
                      && vetoed
                      && vetoRemoved
                      && !coalitionMajority
                      && refr.Passed;
        GD.Print(result ? "[ParliamentTest] SONUÇ: PASS ✓" : "[ParliamentTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>Parti kongre oylaması ve darbe riski testi (tek parti rejimleri). Normal oyunda ASLA çalışmaz (--autoritarian-test).</summary>
    private void RunAutoritarianTest()
    {
        GD.Print("=========================================");
        GD.Print("  M22.4: OTORITER SİSTEMLER TESTİ");
        GD.Print("=========================================");

        // 1) Kongre rejimi tespiti: CHN = OneParty (kongre), DEU = Parliamentary (değil).
        bool chnCongress = AutoritarianSystem.IsCongressRegime("CHN");
        bool deuCongress = AutoritarianSystem.IsCongressRegime("DEU");
        GD.Print($"[AutoritarianTest] CHN kongre rejimi: {chnCongress} (beklenen True)");
        GD.Print($"[AutoritarianTest] DEU kongre rejimi: {deuCongress} (beklenen False)");

        // 2) Kongre oylaması: 300 evet >= 251 eşik → geçer; 200 evet → kalır.
        var passVote = AutoritarianSystem.ApproveProposal("pol_1", 300, 200);
        var failVote = AutoritarianSystem.ApproveProposal("pol_2", 200, 300);
        GD.Print($"[AutoritarianTest] Kongre 300/200 geçti mi: {passVote.Passed} (beklenen True), threshold={passVote.ApprovalThreshold}");
        GD.Print($"[AutoritarianTest] Kongre 200/300 geçti mi: {failVote.Passed} (beklenen False)");

        // 3) Aynı teklif tekrar onaylanamaz (zaten onaylı).
        bool dupApproved = AutoritarianSystem.IsCongressApproved("pol_1");
        GD.Print($"[AutoritarianTest] pol_1 zaten onaylı mı: {dupApproved} (beklenen True)");

        // 4) Darbe riski: %0-100 arası, sıfır riskte darbe olmaz.
        double chnRisk = AutoritarianSystem.ComputeCoupRisk("CHN");
        bool noCoupAtZero = !AutoritarianSystem.TryTriggerCoup("DEU", 0);
        GD.Print($"[AutoritarianTest] CHN darbe riski: % {chnRisk:F1} (0-100 arası)");
        GD.Print($"[AutoritarianTest] DEU %0 riskte darbe yok: {noCoupAtZero} (beklenen True)");

        bool result = chnCongress && !deuCongress
                      && passVote.Passed && !failVote.Passed
                      && dupApproved
                      && chnRisk >= 0 && chnRisk <= 100
                      && noCoupAtZero;
        GD.Print(result ? "[AutoritarianTest] SONUÇ: PASS ✓" : "[AutoritarianTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M23.3: Seçim sistemi headless testi — takvim, oy tahmini, seçim çözümü (kazanma/kaybetme, lider değişikliği, yeni kabine) ve Save/Load kimliği.
    /// Normal oyunda ASLA çalışmaz (--election-test).
    /// </summary>
    private void RunElectionTest()
    {
        GD.Print("=========================================");
        GD.Print("  M23.3: SEÇİM SİSTEMİ TESTİ (kazanma/kaybetme, lider değişikliği)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        // 1) Seçim takvimi: DEU (Parliamentary, dönem 4y) → ilk seçim gün 1460
        long deuNext = ElectionSystem.GetNextElectionDay("DEU");
        GD.Print($"[ElectionTest] DEU bir sonraki seçim: gün {deuNext} (beklenen 1460 = 4*365)");

        // 2) CHN (OneParty) ve SAU (Monarchy) için seçim yok → 0
        long chnNext = ElectionSystem.GetNextElectionDay("CHN");
        long sauNext = ElectionSystem.GetNextElectionDay("SAU");
        GD.Print($"[ElectionTest] CHN (tek parti) seçim: {chnNext} (beklenen 0)");
        GD.Print($"[ElectionTest] SAU (monarşi) seçim: {sauNext} (beklenen 0)");

        // 3) Seçim durumu: gün 0'da henüz seçim yok; gün 1460'da GEÇER
        bool notDueAt0 = !ElectionSystem.IsElectionDue("DEU", 0);
        bool dueAt1460 = ElectionSystem.IsElectionDue("DEU", 1460);
        GD.Print($"[ElectionTest] DEU gün 0'da seçim var mı: {notDueAt0} (beklenen False)");
        GD.Print($"[ElectionTest] DEU gün 1460'da seçim var mı: {dueAt1460} (beklenen True)");

        // 4) (M23.3) Seçim çözümlemesi: 1940-01-02 (gün ~1462)'ta DEU için seçim geçer.
        //    Son seçim günü henüz 0 (ilk dönem doldu) → seçim due. Kazanınca iktidar korunur,
        //    kaybetse lider değişir ve yeni kabine dağıtılır.
        GameManager.Instance.JumpToDateForTest(new DateTime(1940, 1, 2));
        long resolveDay = GameManager.Instance.TotalDays;
        var electionResult = ElectionSystem.ResolveElection("DEU", resolveDay);
        GD.Print($"[ElectionTest] DEU seçim çözümü (gün {resolveDay}): {(electionResult == null ? "yok/geçmedi" : electionResult.Won ? "KAZANDI — iktidar korundu" : "KAYBEDİLDİ — lider değişti: " + electionResult.NewLeader)}");
        bool resolveOk = electionResult != null
                         && electionResult.CountryId == "DEU"
                         && electionResult.Day == resolveDay
                         && electionResult.PredictedVote >= 0 && electionResult.PredictedVote <= 100
                         && (electionResult.Won || (electionResult.LeaderChanged && !string.IsNullOrEmpty(electionResult.NewLeader)));
        GD.Print($"[ElectionTest] Seçim çözümü geçerli: {resolveOk}");

        // Seçimden sonraki dönem ileri taşınmış mı?
        long afterResolve = ElectionSystem.GetNextElectionDay("DEU");
        GD.Print($"[ElectionTest] DEU çözüm sonrası sonraki seçim: {afterResolve} (beklenen > {resolveDay})");
        bool nextAdvanced = afterResolve > resolveDay;

        // 5) Oy tahmini: oyuncu ülkesinde hesaplanmalı, 0-100 arası
        var pred = ElectionSystem.GetVotePrediction(GameManager.Instance.PlayerCountryId, GameManager.Instance.TotalDays);
        GD.Print($"[ElectionTest] {pred.CountryName} oy tahmini: %{pred.PredictedVote:F1} (0-100 aralıklı olmalı)");
        GD.Print($"[ElectionTest]   Taban memnuniyet: %{pred.BaseSatisfaction:F1}");
        GD.Print($"[ElectionTest]   Gün sonra: {pred.DaysUntilElection}, seçim var mı: {pred.ElectionDue}");
        GD.Print($"[ElectionTest]   Hükümet tipi: {pred.GovernmentType}, dönem: {pred.TermLengthYears}yıl");

        // 6) Tüm ülkeler için tahmin
        int allCount = ElectionSystem.GetAllVotePredictions(GameManager.Instance.TotalDays).Count();
        GD.Print($"[ElectionTest] Tüm ülke tahmin sayısı: {allCount}");

        // 7) Kampanya aksiyonu: rally kaydı ve süresi dolmadan önce oy bonusu
        ElectionCampaignSystem.RecordCampaignAction("DEU", "rally", "Miting", GameManager.Instance.TotalDays, 7, 5.0, false);
        double campaignBonus = ElectionCampaignSystem.GetCampaignBonus("DEU", GameManager.Instance.TotalDays);
        GD.Print($"[ElectionTest] DEU kampanya bonusu: %{campaignBonus:F1} (beklenen > 0)");

        // 8) Vaat kaydı ve yerine getirme
        ElectionCampaignSystem.RecordCampaignAction("DEU", "promise", "Vaat", GameManager.Instance.TotalDays, 30, 2.0, true);
        var activePromises = ElectionCampaignSystem.GetActivePromises("DEU");
        GD.Print($"[ElectionTest] DEU aktif vaat sayısı: {activePromises.Count} (beklenen 1)");
        ElectionCampaignSystem.FulfillPromise("DEU", 0);
        var remainingPromises = ElectionCampaignSystem.GetActivePromises("DEU");
        GD.Print($"[ElectionTest] DEU vaat yerine getirme sonrası aktif vaat: {remainingPromises.Count} (beklenen 0)");

        // 9) Save/Load kimliği (seçim tarihi + seçim sonuçları)
        var save = ElectionSystem.GetSaveData();
        int saveCountries = save.Countries?.Length ?? 0;
        int saveResults = save.Results?.Length ?? 0;
        GD.Print($"[ElectionTest] Save kayıt sayısı: {saveCountries} ülke, {saveResults} seçim sonucu");
        ElectionSystem.LoadSaveData(save);
        long reloadNext = ElectionSystem.GetNextElectionDay("DEU");
        bool saveLoadId = reloadNext == afterResolve;
        bool resultsLoaded = ElectionSystem.ElectionResults.TryGetValue("DEU", out var loadedResults) && loadedResults.Count > 0;
        GD.Print($"[ElectionTest] Save/Load sonrası DEU bir sonraki seçim: {reloadNext} (eşleşiyor: {saveLoadId}), sonuç yüklendi: {resultsLoaded}");

        bool result = deuNext == 1460
                      && chnNext == 0 && sauNext == 0
                      && notDueAt0 && dueAt1460
                      && resolveOk && nextAdvanced
                      && pred.PredictedVote >= 0 && pred.PredictedVote <= 100
                      && allCount == CountryDatabase.All().Count(c => c != null)
                      && campaignBonus > 0
                      && activePromises.Count == 1
                      && remainingPromises.Count == 0
                      && saveLoadId && resultsLoaded;
        GD.Print(result ? "[ElectionTest] SONUÇ: PASS ✓" : "[ElectionTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M24.1: Karakter oluşturma + Save/Load determinizm testi (--character-test).
    /// Aynı tohum → aynı karakter; Save/Load dönüşümü karakteri korur.
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunCharacterTest()
    {
        GD.Print("=========================================");
        GD.Print("  M24.1: KARAKTER OLUŞTURMA TESTİ");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        ulong seed = 0xC0FFEE12345678UL;

        // 1) Aynı tohumla iki kez üret → aynı karakter çıkmalı (determinizm)
        var c1 = Character.GenerateRandom("DEU", "germanic", seed);
        var c2 = Character.GenerateRandom("DEU", "germanic", seed);
        bool sameSeedSameChar =
            c1.FirstName == c2.FirstName && c1.LastName == c2.LastName &&
            c1.Age == c2.Age && c1.Gender == c2.Gender &&
            c1.Diplomacy == c2.Diplomacy && c1.Administration == c2.Administration &&
            c1.Intelligence == c2.Intelligence && c1.Charisma == c2.Charisma &&
            c1.Military == c2.Military && c1.Economy == c2.Economy && c1.Law == c2.Law &&
            c1.Background == c2.Background &&
            c1.SkinTone == c2.SkinTone && c1.HairColor == c2.HairColor && c1.ClothingStyle == c2.ClothingStyle;
        GD.Print($"[CharacterTest] Aynı tohum → aynı isim: {c1.FullName} == {c2.FullName}: {sameSeedSameChar}");

        // 2) Farklı tohum → farklı karakter (genellikle)
        var c3 = Character.GenerateRandom("DEU", "germanic", seed + 1);
        bool diffSeedDiffChar = c1.Id != c3.Id || c1.FirstName != c3.FirstName || c1.Age != c3.Age;
        GD.Print($"[CharacterTest] Farklı tohum → farklı karakter: {diffSeedDiffChar} ({c1.FullName} vs {c3.FullName})");

        // 3) Save/Load dönüşüm karakterini korumalıdır (CharacterSaveFrom → CharacterSaveTo)
        var save = SaveSystem.CharacterSaveFrom(c1);
        var loaded = SaveSystem.CharacterSaveTo(save);
        bool saveLoadSame =
            loaded != null &&
            loaded.Id == c1.Id && loaded.FirstName == c1.FirstName && loaded.LastName == c1.LastName &&
            loaded.Age == c1.Age && loaded.Diplomacy == c1.Diplomacy &&
            loaded.Traits != null && loaded.Traits.SequenceEqual(c1.Traits);
        GD.Print($"[CharacterTest] Save/Load dönüşüm: {loaded?.FullName ?? "null"} == {c1.FullName}: {saveLoadSame}");

        // 4) GameManager.SetCharacter ile karakter atanabilir
        GameManager.Instance.SetCharacter(c1);
        bool hasChar = GameManager.Instance.HasCharacter && GameManager.Instance.PlayerCharacter.Id == c1.Id;
        GD.Print($"[CharacterTest] SetCharacter/HasCharacter: {hasChar} ({GameManager.Instance.PlayerCharacter?.FullName ?? "null"})");

        bool result = sameSeedSameChar && diffSeedDiffChar && saveLoadSame && hasChar;
        GD.Print(result ? "[CharacterTest] SONUÇ: PASS ✓" : "[CharacterTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M25.1: Siyasetçi kariyer yolu testi — eğitim başlatma, nitelik bonusu, aşama ilerleme, Save/Load.
    /// Normal oyunda asla çalışmaz (--career-test).
    /// </summary>
    private void RunCareerTest()
    {
        GD.Print("=========================================");
        GD.Print("  M25.1: SİYASETCİ KARİYER YOLU TESTİ");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        CareerSystem.Initialize();

        bool result = true;

        // 1) Eğitim başlatma → nitelik bonusu
        var c = GameManager.Instance.PlayerCharacter;
        if (c == null)
        {
            c = Character.GenerateRandom("DEU", "germanic", 0x1234UL);
            GameManager.Instance.SetCharacter(c);
        }
        int lawBefore = c.Law;
        CareerSystem.StartEducation(CareerSystem.EducationField.Law, c, 0);
        bool eduBonus = c.Law > lawBefore; // Law +10
        GD.Print($"[CareerTest] Eğitim başlatıldı (Law bonus): law {lawBefore} → {c.Law}: {eduBonus}");
        result = result && eduBonus;

        // 2) Eğitim tamamlandı → Parti Gençlik aşamasına ilerler
        CareerSystem.CompleteEducation(0);
        bool stagePartyYouth = CareerSystem.Stage == CareerSystem.CareerStage.PartyYouth;
        GD.Print($"[CareerTest] Eğitim tamamlandı → aşama={CareerSystem.Stage}: {stagePartyYouth}");
        result = result && stagePartyYouth;

        // 3) Günlük tick ile 30 günde bir sonraki aşama (LocalGovernment)
        CareerSystem.DailyTick(30, c);
        bool stageLocalGov = CareerSystem.Stage == CareerSystem.CareerStage.LocalGovernment;
        GD.Print($"[CareerTest] 30 gün sonra aşama={CareerSystem.Stage}: {stageLocalGov}");
        result = result && stageLocalGov;

        // 4) Save/Load dönüşüm
        var save = CareerSystem.GetSaveData();
        CareerSystem.LoadSaveData(save);
        bool saveLoadSame =
            CareerSystem.Stage == (CareerSystem.CareerStage)save.Stage &&
            CareerSystem.Education == (CareerSystem.EducationField)save.EducationField &&
            CareerSystem.EducationCompleted == save.EducationCompleted;
        GD.Print($"[CareerTest] Save/Load dönüşüm: stage={CareerSystem.Stage} edu={CareerSystem.Education} completed={CareerSystem.EducationCompleted}: {saveLoadSame}");
        result = result && saveLoadSame;

        GD.Print(result ? "[CareerTest] SONUÇ: PASS ✓" : "[CareerTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M25.2: Asker kariyer yolu testi — harp okulu, rütbe terfisi, görev yeri, savaş deneyimi,
    /// Save/Load, darbe engeli (Albay altı reddedilir). Normal oyunda asla çalışmaz (--military-career-test).
    /// </summary>
    private void RunMilitaryCareerTest()
    {
        GD.Print("=========================================");
        GD.Print("  M25.2: ASKER KARİYER YOLU TESTİ");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        MilitaryCareerSystem.Initialize();

        bool result = true;

        // 1) Karakter (asker kökenli) + kariyer başlat → Private
        var c = GameManager.Instance.PlayerCharacter;
        if (c == null)
        {
            c = Character.GenerateRandom("DEU", "germanic", 0x55AAUL);
            c.Background = "military"; // asker kökeni: başlangıç deneyim avantajı
            GameManager.Instance.SetCharacter(c);
        }
        MilitaryCareerSystem.StartMilitaryCareer(MilitaryCareerSystem.MilitaryBranch.Infantry, c, 0);
        bool startedPrivate = MilitaryCareerSystem.Rank == MilitaryCareerSystem.MilitaryRank.Private
                              && MilitaryCareerSystem.CareerActive;
        GD.Print($"[MilitaryCareerTest] Kariyer başlatıldı: rütbe={MilitaryCareerSystem.Rank}, aktif={MilitaryCareerSystem.CareerActive}: {startedPrivate}");
        result = result && startedPrivate;

        // 2) Harp okulu → Corporal (özel terfi), görev=Garrison
        MilitaryCareerSystem.CompleteAcademy(0);
        bool academyDone = MilitaryCareerSystem.AcademyCompleted
                           && MilitaryCareerSystem.Rank == MilitaryCareerSystem.MilitaryRank.Corporal
                           && MilitaryCareerSystem.Duty == MilitaryCareerSystem.DutyStation.Garrison;
        GD.Print($"[MilitaryCareerTest] Harp okulu: akademi={MilitaryCareerSystem.AcademyCompleted}, rütbe={MilitaryCareerSystem.Rank}, görev={MilitaryCareerSystem.Duty}: {academyDone}");
        result = result && academyDone;

        // 3) FrontLine'ye geç → deneyim kazanmaya başla
        MilitaryCareerSystem.ChangeDutyStation(MilitaryCareerSystem.DutyStation.FrontLine, 0);

        // 4) 61 gün ilerle → Corporal(60 gün) → Sergeant terfisi, deneyim > 0
        MilitaryCareerSystem.DailyTick(61, c);
        for (int i = 0; i < 60; i++) MilitaryCareerSystem.DailyTick(61 + i, c);
        bool promotedSergeant = MilitaryCareerSystem.Rank == MilitaryCareerSystem.MilitaryRank.Sergeant;
        bool hasCombatExp = MilitaryCareerSystem.CombatExperience > 0;
        GD.Print($"[MilitaryCareerTest] 61 gün sonra: rütbe={MilitaryCareerSystem.Rank}, deneyim={MilitaryCareerSystem.CombatExperience}: terfi={promotedSergeant}, deneyim={hasCombatExp}");
        result = result && promotedSergeant && hasCombatExp;

        // 5) Save/Load dönüşüm durumu korunur
        var save = MilitaryCareerSystem.GetSaveData();
        MilitaryCareerSystem.LoadSaveData(save);
        bool saveLoadSame =
            MilitaryCareerSystem.Rank == (MilitaryCareerSystem.MilitaryRank)save.Rank
            && MilitaryCareerSystem.AcademyCompleted == save.AcademyCompleted
            && MilitaryCareerSystem.Duty == (MilitaryCareerSystem.DutyStation)save.DutyStation
            && MilitaryCareerSystem.CombatExperience == save.CombatExperience;
        GD.Print($"[MilitaryCareerTest] Save/Load dönüşüm: rütbe={MilitaryCareerSystem.Rank}, deneyim={MilitaryCareerSystem.CombatExperience}: {saveLoadSame}");
        result = result && saveLoadSame;

        // 6) Darbe engeli: Sergeant < Albay → reddedilir, kariyer sona ermez (discharge olur ama testte sadece engel doğrulanır)
        var (coupOk, coupMsg) = MilitaryCareerSystem.AttemptCoup();
        bool coupBlocked = !coupOk && coupMsg.Contains("yetersiz");
        GD.Print($"[MilitaryCareerTest] Darbe girişimi (Şeriş): başarı={coupOk}, mesaj=\"{coupMsg}\": engel={coupBlocked}");
        result = result && coupBlocked;

        GD.Print(result ? "[MilitaryCareerTest] SONUÇ: PASS ✓" : "[MilitaryCareerTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M25.3: Eğitim modülü testi — ders/staj/mentor/sınav/tez/ağ-kurma; süre + PP + nitelik değişimi,
    /// başarısızlık ve tekrar, Save/Load. Normal oyunda asla çalışmaz (--education-test).
    /// </summary>
    private void RunEducationTest()
    {
        GD.Print("=========================================");
        GD.Print("  M25.3: EĞİTİM MODÜLLERİ TESTİ");
        GD.Print("=========================================");

        // Deterministik sonuç: sabit tohum
        DeterministicRNG.InitializeForNewGame(0xE00D1EEDUL);

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 500;

        EducationSystem.Initialize();

        bool result = true;

        var c = GameManager.Instance.PlayerCharacter;
        if (c == null)
        {
            c = Character.GenerateRandom("DEU", "germanic", 0xBEEFUL);
            GameManager.Instance.SetCharacter(c);
        }

        // 1) Sınav modülünü başlat (5 gün, 25 PP; başarı→Law+6, hata→Intelligence-2)
        int lawBefore = c.Law;
        int intelBefore = c.Intelligence;
        bool started = EducationSystem.StartModule(EducationSystem.EducationModuleType.Exam, c, 0);
        GD.Print($"[EducationTest] Sınav başlatıldı: aktif={EducationSystem.IsActive}, süre={EducationSystem.DaysRemaining}: {started}");
        result = result && started && EducationSystem.IsActive && EducationSystem.DaysRemaining == 5;

        // 2) 5 gün ilerle → modül çözülür (başarılı veya başarısız; her iki sonuçta nitelik değişir)
        for (int i = 0; i < 5; i++)
            EducationSystem.DailyTick(0 + i, c);
        bool moduleResolved = !EducationSystem.IsActive; // modül çözüldü (başarı veya başarısızlık)
        GD.Print($"[EducationTest] 5 gün sonra: aktif={EducationSystem.IsActive}, tamamlanan={EducationSystem.CompletedCount}, son={EducationSystem.LastSuccess}: {moduleResolved}");
        result = result && moduleResolved;

        // 3) Nitelik değişimi doğrulanır (sınav: Law +6 başarılı / Intelligence -2 başarısız)
        bool statChanged = c.Law == lawBefore + 6 || c.Intelligence == intelBefore - 2;
        GD.Print($"[EducationTest] Nitelik değişimi: Law {lawBefore}→{c.Law}, Intelligence {intelBefore}→{c.Intelligence}: {statChanged}");
        result = result && statChanged;

        // 4) Tekrar deneyebilirlik: aynı modülü tekrar başlatılabilir
        bool restarted = EducationSystem.StartModule(EducationSystem.EducationModuleType.Course, c, 0);
        GD.Print($"[EducationTest] Tekrar başlatma (Course): {restarted}, aktif={EducationSystem.IsActive}");
        result = result && restarted;

        // 5) Save/Load dönüşüm durumu korunur
        var save = EducationSystem.GetSaveData();
        EducationSystem.LoadSaveData(save);
        bool saveLoadSame =
            EducationSystem.CurrentModule == save.CurrentModule
            && EducationSystem.DaysRemaining == save.DaysRemaining
            && EducationSystem.CompletedCount == save.CompletedCount;
        GD.Print($"[EducationTest] Save/Load dönüşüm: modül={EducationSystem.CurrentModule}, süre={EducationSystem.DaysRemaining}, tamamlanan={EducationSystem.CompletedCount}: {saveLoadSame}");
        result = result && saveLoadSame;

        GD.Print(result ? "[EducationTest] SONUÇ: PASS ✓" : "[EducationTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M25.4: Aday olma kriterleri testi — yaş, eğitim seviyesi, parti üyeliği,
    /// tanınırlık, itibar, sicil. Eksik kriter listesi gösterilir. Normal oyunda
    /// asla çalışmaz (--candidacy-test).
    /// </summary>
    private void RunCandidacyTest()
    {
        GD.Print("=========================================");
        GD.Print("  M25.4: ADAY OLMA KRİTERLERİ TESTİ");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0xCAFE1234UL);

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 500;

        CandidacySystem.Initialize();

        bool result = true;

        var c = GameManager.Instance.PlayerCharacter;
        if (c == null)
        {
            c = Character.GenerateRandom("DEU", "germanic", 0xBEEFUL);
            GameManager.Instance.SetCharacter(c);
        }

        // Varsayılan kriterler: yaş≥18, eğitim≥1, parti üyesi, tanınırlık≥30, itibar≥40, temiz sicil
        var defaultCriteria = CandidacySystem.GetCriteria();
        GD.Print($"[CandidacyTest] Varsayılan kriterler: yaş≥{defaultCriteria.MinAge}, eğitim≥{defaultCriteria.MinEducationCompleted}, parti={defaultCriteria.RequiresPartyMembership}, tanınırlık≥{defaultCriteria.MinRecognition}, itibar≥{defaultCriteria.MinReputation}, temiz_sicil={defaultCriteria.RequiresCleanRecord}");

        // Karakteri kriterleri sağlamayan durumda ayarla (genç, eğitim yok, partisiz, düşük tanınırlık/itibar, sicil var)
        c.Age = 16;
        c.PartyId = "";
        c.Recognition = 10;
        c.Reputation = 20;
        c.HasCriminalRecord = true;
        EducationSystem.ResetForNewGame(); // tamamlanmış modül sayısı 0

        var res1 = CandidacySystem.CheckEligibility(c, "DEU");
        GD.Print($"[CandidacyTest] Eğilimsiz karakter: uygun={res1.IsEligible}, eksikler={string.Join("; ", res1.MissingCriteria)}");
        result = result && !res1.IsEligible && res1.MissingCriteria.Count > 0;

        // Karakteri kriterleri sağlayan durumda ayarla
        c.Age = 35;
        c.PartyId = "DEMOKRATIK_PARTI";
        c.Recognition = 60;
        c.Reputation = 70;
        c.HasCriminalRecord = false;
        EducationSystem.StartModule(EducationSystem.EducationModuleType.Course, c, 0);
        for (int i = 0; i < 7; i++)
            EducationSystem.DailyTick(i, c); // 7 gün → ders tamamlanır, CompletedCount=1

        var res2 = CandidacySystem.CheckEligibility(c, "DEU");
        GD.Print($"[CandidacyTest] Eğilimli karakter: uygun={res2.IsEligible}, eksikler={string.Join("; ", res2.MissingCriteria)}");
        result = result && res2.IsEligible && res2.MissingCriteria.Count == 0;

        // Save/Load dönüşüm kriter korundu mu
        var save = CandidacySystem.GetSaveData();
        CandidacySystem.LoadSaveData(save);
        var crit2 = CandidacySystem.GetCriteria();
        bool saveLoadSame =
            crit2.MinAge == defaultCriteria.MinAge
            && crit2.MinEducationCompleted == defaultCriteria.MinEducationCompleted
            && crit2.RequiresPartyMembership == defaultCriteria.RequiresPartyMembership
            && crit2.MinRecognition == defaultCriteria.MinRecognition
            && crit2.MinReputation == defaultCriteria.MinReputation
            && crit2.RequiresCleanRecord == defaultCriteria.RequiresCleanRecord;
        GD.Print($"[CandidacyTest] Save/Load dönüşüm kriterler: {saveLoadSame}");
        result = result && saveLoadSame;

        GD.Print(result ? "[CandidacyTest] SONUÇ: PASS ✓" : "[CandidacyTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M25.5: Kariyer olayları testi — skandal, fırsat, mentor kaybı, hastalık.
    /// Her olayın karakter niteliklerini ve KarriereSystem durumunu etkilediğini doğrular.
    /// Normal oyunda asla çalışmaz (--career-events-test).
    /// </summary>
    private void RunCareerEventsTest()
    {
        GD.Print("=========================================");
        GD.Print("  M25.5: KARİYER OLAYLARI TESTİ");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0xDEADBEEFUL);

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 500;

        CareerSystem.Initialize();
        CareerEventSystem.Initialize();

        bool result = true;

        var c = GameManager.Instance.PlayerCharacter;
        if (c == null)
        {
            c = Character.GenerateRandom("DEU", "germanic", 0xBEEFUL);
            GameManager.Instance.SetCharacter(c);
        }

        // Kariyeri bir aşamaya ilerlet (olaların etkisini görebilmek için)
        CareerSystem.StartEducation(CareerSystem.EducationField.Law, c, 0);
        int repBefore = c.Reputation;
        int recBefore = c.Recognition;
        double ppBefore = PlayerCountryData.PoliticalPower;
        int stageBefore = (int)CareerSystem.Stage;

        // 1) Skandal: itibar -15, tanınırlık -5, PP -50, aşama geri
        bool triggered = CareerEventSystem.TriggerEvent(CareerEventSystem.CareerEventType.Scandal, c, 10, ref ppBefore);
        GD.Print($"[CareerEventTest] Skandal tetiklendi: {triggered}");
        result = result && triggered;
        GD.Print($"[CareerEventTest] Skandal sonrası: Rep {repBefore}→{c.Reputation}, Rec {recBefore}→{c.Recognition}, PP {ppBefore}, Stage {stageBefore}→{(int)CareerSystem.Stage}");
        result = result && c.Reputation < repBefore && c.Recognition < recBefore && ppBefore < 500;

        // 2) Fırsat: itibar +10, tanınırlık +5, PP +100
        int repBefore2 = c.Reputation;
        int recBefore2 = c.Recognition;
        double ppBefore2 = ppBefore;
        triggered = CareerEventSystem.TriggerEvent(CareerEventSystem.CareerEventType.Opportunity, c, 11, ref ppBefore2);
        GD.Print($"[CareerEventTest] Fırsat tetiklendi: {triggered}");
        result = result && triggered;
        GD.Print($"[CareerEventTest] Fırsat sonrası: Rep {repBefore2}→{c.Reputation}, Rec {recBefore2}→{c.Recognition}, PP {ppBefore2}");
        result = result && c.Reputation > repBefore2 && c.Recognition > recBefore2 && ppBefore2 > ppBefore;

        // 3) Mentor kaybı: engelleme 30 gün
        int blockedBefore = CareerSystem.BlockedDaysRemaining;
        triggered = CareerEventSystem.TriggerEvent(CareerEventSystem.CareerEventType.MentorLoss, c, 12, ref ppBefore2);
        GD.Print($"[CareerEventTest] Mentor kaybı tetiklendi: {triggered}, engellenme {blockedBefore}→{CareerSystem.BlockedDaysRemaining}");
        result = result && triggered && CareerSystem.BlockedDaysRemaining == 30;

        // 4) Hastalık: engelleme 14 gün
        triggered = CareerEventSystem.TriggerEvent(CareerEventSystem.CareerEventType.Illness, c, 13, ref ppBefore2);
        GD.Print($"[CareerEventTest] Hastalık tetiklendi: {triggered}, engellenme {CareerSystem.BlockedDaysRemaining}");
        result = result && triggered && CareerSystem.BlockedDaysRemaining == 30; // max(30, 14) = 30

        // 5) DailyTick engelleme azaltma (30 günden az ki bir 사건 tetiklenmesin)
        int daysLeft = CareerSystem.BlockedDaysRemaining;
        for (int i = 0; i < 5; i++)
            CareerEventSystem.DailyTick(20 + i, c, ppBefore2);
        GD.Print($"[CareerEventTest] 5 gün sonra engellenme: {daysLeft}→{CareerSystem.BlockedDaysRemaining}");
        result = result && CareerSystem.BlockedDaysRemaining < daysLeft;

        // 6) Save/Load dönüşüm
        var save = CareerEventSystem.GetSaveData();
        CareerEventSystem.LoadSaveData(save);
        bool saveLoadSame = CareerSystem.BlockedDaysRemaining == save.DaysBlockedRemaining;
        GD.Print($"[CareerEventTest] Save/Load engellenme: {saveLoadSame}");
        result = result && saveLoadSame;

        GD.Print(result ? "[CareerEventTest] SONUÇ: PASS ✓" : "[CareerEventTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M25.6: Seçimi kazanınca karakterin doğum ülkesini devralma testi.
    /// Eğilimli bir karakterin doğum ülkesindeki (DEU) seçimin debido olduğu,
    /// kazandığı durumda SetPlayerCountry + kabine devralınmasını doğrular.
    /// Normal oyunda asla çalışmaz (--character-takeover-test).
    /// </summary>
    private void RunCharacterTakeoverTest()
    {
        GD.Print("=========================================");
        GD.Print("  M25.6: KARAKTER ÜLKE DEVRALMA TESTİ");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0xFEEDFACEUL);

        if (Speed == 0) SetSpeed(0);

        CandidacySystem.Initialize();
        EducationSystem.ResetForNewGame();

        bool result = true;

        // 1) Eğilimli bir karakter oluştur: DEU doğumlu, yaş 35, parti üyesi,
        // yüksek tanınırlık/itibar, temiz sicil, bir eğitim modülü tamamlanmış.
        var c = GameManager.Instance.PlayerCharacter;
        if (c == null)
        {
            c = Character.GenerateRandom("DEU", "germanic", 0xBEEFUL);
            GameManager.Instance.SetCharacter(c);
        }
        c.BirthCountryId = "DEU";
        c.Age = 35;
        c.PartyId = "DEMOKRATIK_PARTI";
        c.Recognition = 60;
        c.Reputation = 70;
        c.HasCriminalRecord = false;

        EducationSystem.StartModule(EducationSystem.EducationModuleType.Course, c, 0);
        for (int i = 0; i < 7; i++)
            EducationSystem.DailyTick(i, c);
        GD.Print($"[TakeoverTest] Karakter: {c.FullName}, yaş={c.Age}, parti={c.PartyId}, tanınırlık={c.Recognition}, itibar={c.Reputation}, eğitim={EducationSystem.CompletedCount}");

        var elig = CandidacySystem.CheckEligibility(c, "DEU");
        GD.Print($"[TakeoverTest] Adaylık: uygun={elig.IsEligible}, eksikler={string.Join("; ", elig.MissingCriteria)}");
        result = result && elig.IsEligible && elig.MissingCriteria.Count == 0;

        // 2) DEU halk memnuniyetini yüksek tut → seçimde kazanma garantisi (oy ≥ 50).
        foreach (var g in PublicSatisfactionSystem.Groups)
            PublicSatisfactionSystem.SetSatisfaction("DEU", g.Id, 80.0);

        // 3) Tarihi DEU'nın ilk seçiminin ardındanına atla (4*365 = 1460 → gün 1462).
        GameManager.Instance.JumpToDateForTest(new DateTime(1940, 1, 2));
        long resolveDay = GameManager.Instance.TotalDays;
        GD.Print($"[TakeoverTest] Tarih atlatıldı: gün {resolveDay} (DEU ilk seçimi 1460)");

        // 4) Günlük tick → M25.6 bloğu çalışır: karakter seçimi kazanır ve ülkeyi devralır.
        DailyTick();

        GD.Print($"[TakeoverTest] Sonra: HasPlayer={HasPlayer}, PlayerCountryId={PlayerCountryId}, kilitli={IsPlayerCountryLocked}");
        bool takenOver = HasPlayer && PlayerCountryId == "DEU" && IsPlayerCountryLocked;
        GD.Print($"[TakeoverTest] Ülke devredildi: {takenOver}");
        result = result && takenOver;

        // 5) Kabine devralınmış olmalı: en az bir bakanlık atanmış.
        int assigned = 0;
        foreach (var m in CabinetSystem.Ministries)
            if (CabinetSystem.IsAssigned("DEU", m.Id)) assigned++;
        GD.Print($"[TakeoverTest] DEU atanmış bakanlık sayısı: {assigned}");
        result = result && assigned > 0;

        GD.Print(result ? "[TakeoverTest] SONUÇ: PASS ✓" : "[TakeoverTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(result ? 0 : 1);
    }

    /// <summary>
    /// M26.1: Afet sistemi testi (--disaster-test). Deterministik tohumla N gün ilerletir,
    /// test modunda afet tetiklenmesini garanti eder ve sonucu raporlar.
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunDisasterTest()
    {
        GD.Print("=========================================");
        GD.Print("  M26.1: AFET SİSTEMİ TESTİ (--disaster-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0xDEADCAFEUL);
        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);
        DisasterSystem.EnableTestMode();

        int days = 60;
        AdvanceDaysForTest(days);
        int active = DisasterSystem.ActiveDisasters.Count;
        int history = DisasterSystem.DisasterHistory.Count;
        GD.Print($"[DisasterTest] {days} gün → aktif={active}, geçmiş_kayıt={history}, RNG={DeterministicRNG.GetState():X}");
        foreach (var d in DisasterSystem.DisasterHistory)
            GD.Print($"[DisasterTest]   {d.Day:D3}.gün {d.DisasterId} → {d.CountryId} ({d.Region}) şiddet={d.Severity}");

        bool ok = history > 0; // test modunda en az bir afet ateşlenmeli
        GD.Print(ok ? "[DisasterTest] SONUÇ: PASS ✓" : "[DisasterTest] SONUÇ: FAIL ✗ (hiç afet ateşlenmedi)");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M26.3: Müdahale sistemi testi (--intervention-test).
    /// 1) Müdahale uygulanır mı (ApplyIntervention true, PP düşer)?
    /// 2) İlgili bakanın yönetim kalitesi çarpanı doğru mu? (DEU içişleri → Clara Neumann Honesty 90 → ×1.4)
    /// 3) Gösterge etkisi DurationDays boyunca doğrusal olarak söner mi?
    /// 4) Başarısızlıkta (geçersiz ID) Quit(1)?
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunInterventionTest()
    {
        GD.Print("=========================================");
        GD.Print("  M26.3: MÜDHALE SİSTEMİ TESTİ (--intervention-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0xDEADCAFEUL);
        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);
        InterventionSystem.EnableTestMode();
        InterventionSystem.LoadScenarioData(); // tanımlar yüklensin

        const string countryId = "GER";
        const string interventionId = "emergency_aid";
        var def = InterventionSystem.Definitions.FirstOrDefault(d => d.Id == interventionId);
        bool defOk = def != null && def.CostPP > 0 && def.DurationDays > 0;
        GD.Print($"[InterventionTest] Tanım: {(defOk ? "OK" : "YOK")} ({interventionId})");

        // İçişleri bakanı atanacak: Clara Neumann (Honesty 90) → çarpan (90-50)/100+1 = 1.4
        CabinetSystem.AssignMinister(countryId, "interior", "min_deu_justice_1");
        var minister = CabinetSystem.Ministers.FirstOrDefault(m => m.Id == "min_deu_justice_1");
        double expectedMult = 1.0 + ((minister?.Honesty ?? 50) - 50.0) / 100.0;
        GD.Print($"[InterventionTest] Bakan={minister?.Name ?? "-"} Honesty={minister?.Honesty ?? 50} beklenenÇarpan={expectedMult:F2}");

        double healthBefore = StatisticsSystem.GetValue(countryId, "health");
        double ppBefore = CountryDatabase.Get(countryId).PoliticalPower;

        bool applied = InterventionSystem.ApplyIntervention(countryId, interventionId, TotalDays);
        bool ppDeducted = Math.Abs(CountryDatabase.Get(countryId).PoliticalPower - (ppBefore - def.CostPP)) < 0.001;
        int activeAfter = InterventionSystem.ActiveInterventions.Count;
        int historyAfter = InterventionSystem.InterventionHistory.Count;
        double healthAfter = StatisticsSystem.GetValue(countryId, "health");
        double expectedDelta = def.IndicatorEffects["health"] * expectedMult;
        GD.Print($"[InterventionTest] Uygulandı={applied} PP düştü={ppDeducted} ({(ppBefore - def.CostPP):F1}) " +
                 $"aktif={activeAfter} geçmiş={historyAfter} health={healthBefore:F2}→{healthAfter:F2} (beklenen delta={expectedDelta:F2})");

        // Süre boyunca etkinin doğrusal olarak sönmesi. Yalnızca InterventionSystem.DailyTick()
        // çalıştırılır (AdvanceDaysForTest değil), böylece Statistics/Formula vb. sistemlerin
        // günlük dalgalanması müdahale etkisini karıştırmaz.
        for (int i = 0; i < def.DurationDays; i++)
            InterventionSystem.DailyTick();
        int activeAfterFade = InterventionSystem.ActiveInterventions.Count;
        double healthAfterFade = StatisticsSystem.GetValue(countryId, "health");
        bool faded = Math.Abs((healthAfter - healthAfterFade) - expectedDelta) < 0.001;
        GD.Print($"[InterventionTest] {def.DurationDays} gün sonrası aktif={activeAfterFade} health={healthAfterFade:F2} (sonra={healthAfter:F2}) sönme={faded}");

        // Başarısızlık: geçersiz ID → false
        bool invalidOk = !InterventionSystem.ApplyIntervention(countryId, "bölmeyici_id", TotalDays);
        GD.Print($"[InterventionTest] Geçersiz ID reddedildi={invalidOk}");

        bool ok = defOk && applied && ppDeducted && activeAfter == 1 && historyAfter == 1 &&
                  Math.Abs((healthAfter - healthBefore) - expectedDelta) < 0.001 &&
                  faded && activeAfterFade == 0 && invalidOk;
        GD.Print(ok ? "[InterventionTest] SONUÇ: PASS ✓" : "[InterventionTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M26.4: Beklenmedik olay sistemi testi (--unexpected-events-test).
    /// 1) 7 olay tanımı yüklü mü?
    /// 2) Test modunda 60 gün içinde en az bir olay ateşlenir mi? (Deterministik RNG, sabit tohum)
    /// 3) GetSaveData / LoadSaveData dönüşümünde aktif olay sayısı korunuyor mu?
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunUnexpectedEventTest()
    {
        GD.Print("=========================================");
        GD.Print("  M26.4: BEKLENMEDİK OLAY SİSTEMİ TESTİ (--unexpected-events-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0xDEADCAFEUL);
        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);
        UnexpectedEventSystem.EnableTestMode();
        UnexpectedEventSystem.LoadScenarioData(); // tanımlar yüklensin

        int defCount = UnexpectedEventSystem.Definitions.Count;
        bool defsOk = defCount == 7;
        GD.Print($"[UnexpectedEventTest] Tanım sayısı: {defCount} (beklenen 7)");

        // Tetikleme testi: test modunda olasılık ×100, 60 gün
        int days = 60;
        AdvanceDaysForTest(days);
        int active = UnexpectedEventSystem.ActiveUnexpectedEvents.Count;
        int history = UnexpectedEventSystem.UnexpectedEventHistory.Count;
        GD.Print($"[UnexpectedEventTest] {days} gün → aktif={active}, geçmiş_kayıt={history}, RNG={DeterministicRNG.GetState():X}");
        foreach (var ev in UnexpectedEventSystem.UnexpectedEventHistory)
            GD.Print($"[UnexpectedEventTest]   {ev.Day:D3}.gün {ev.EventId} → {ev.CountryId}");

        // Kaydet/yükle dönüşümü: aktif olay sayısı korunmalı
        var save = UnexpectedEventSystem.GetSaveData();
        UnexpectedEventSystem.LoadSaveData(save);
        int activeAfterRoundtrip = UnexpectedEventSystem.ActiveUnexpectedEvents.Count;
        bool roundtripOk = activeAfterRoundtrip == active;
        GD.Print($"[UnexpectedEventTest] Kaydet/yükle dönüşümü aktif={active}→{activeAfterRoundtrip} korundu={roundtripOk}");

        bool ok = defsOk && history > 0 && roundtripOk;
        GD.Print(ok ? "[UnexpectedEventTest] SONUÇ: PASS ✓" : "[UnexpectedEventTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }
}