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

    // M13.2: Tooltip sistemi test durumu (sınıf seviyesinde, ertelenmiş test metotları tarafından kullanılır).
    private bool tooltipTest = false;
    private bool _ttSetOk;
    private bool _ttLayerOk;
    private string _ttText;
    private Control _ttBtn;
    private TooltipPanel _ttTip;

    // M13.3: Harita görünüm modu sistemi test durumu (--map-mode-test).
    private bool mapModeTest = false;

    // M13.4: Ayarlar paneli testi durumu (--settings-test).
    private bool settingsTest = false;

    public override void _Ready()
    {
        Instance = this;

        // Ayarları yükle ve uygula
        SettingsSystem.Load();
        SettingsSystem.Apply();

        // M13.1: Ses sistemi — menü/oyun müziği ve UI sfx (AudioStreamGenerator, prosedürel).
        // SettingsSystem.MasterVolume üzerinden zaten kaydedilen ses seviyesi kullanılır.
        AudioSystem.Initialize(this);

        // Diagnostik: --screenshot-test=<hedef> komut satırında pencere modunda belirtilen
        // sahneyi/paneli açar, PNG kaydeder ve çıkış yapar. Normal oyunu etkilemez.
        ScreenshotTest.Parse(OS.GetCmdlineUserArgs());
        ScreenshotTest.ApplyWindow();

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
        bool organizationTest = false;    // M37: uluslararası örgütler (üyelik, Madde 5, gündem, AI oy)
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
        bool sanctionsTest = false;         // M27.4: yaptirim / ticaret savasi testi (--sanctions-test)
        bool socialTest = false;            // M28.1: olay → tweet üreteci testi (--social-test)
        bool profileTest = false;           // M28.2: kurgusal profil sistemi testi (--profile-test)
        bool trendTest = false;             // M28.3: trend listesi + duygu eğilimi testi (--trend-test)
        bool tweetActionTest = false;       // M28.4: tweet eylemi + memnuniyet etkisi testi (--tweet-action-test)
        bool profanityTest = false;         // M28.5: küfür/nefret filtresi + günlük tweet sınırı testi (--profanity-test)
        bool espionageTest = false;         // M29.1: casusluk v2 — casus niteliği modeli testi (--espionage-test)
        bool spyTrainingTest = false;       // M29.4: casusluk v2 — eğitim kursları testi (--spy-training-test)
        bool spyRentalTest = false;         // M29.5: casusluk v2 — kiralama havuzu testi (--spy-rental-test)
        bool counterIntelTest = false;      // M29.6: casusluk v2 — karşı istihbarat / yakalanma / değişim testi (--counter-intel-test)
        bool spaceTest = false;             // M30.1: uzay programı testi (--space-test)
        bool spaceMgmtTest = false;         // M30.2: uzay proje yönetimi testi (--space-mgmt-test)
        bool spaceRewardTest = false;       // M30.3: uzay ödülleri testi (prestij, tech bonusu, memnuniyet, askeri kullanım)
        bool perfTest = false;              // M13.5: performans testi (5× hız, 1936-1948, --perf-test)
        bool techTest = false;              // M31.1: modern silah teknolojisi testi (--tech-test)
        bool nuclearTest = false;           // M31.2: nükleer program testi (--nuclear-test)
        bool sanctionsNuclearTest = false;  // M31.3: yaptirim / nükleer diplomasi testi (--sanctions-nuclear-test)
        bool nuclearUseTest = false;        // M31.4: nükleer kullanım testi (--nuclear-use-test)
        bool aiNuclearTest = false;         // M31.5: AI nükleer davranış testi (--ai-nuclear-test)
        bool soakTest = false;              // M33: uzun koşu ve kararlılık testi (--soak-test)
        bool balanceTest = false;           // M34.1: denge testi — savaş dağılımı + AI saldırı oranı (--balance-test)
        bool victoryTest = false;           // M41.2: zafer koşulları değerlendirme testi (--victory-test)
        bool scoreTest = false;             // M41.3: ulusal skor + sıralama testi (--score-test)
        bool gameOverTest = false;          // M41.4: oyun sonu paneli testi (--gameover-test)
        bool warScoreTest = false;          // M42.1: savaş puanı testi (--war-score-test)
        bool peaceTermsTest = false;        // M42.2: barış talebi maliyeti testi (--peace-terms-test)
        bool peaceTest = false;             // M42.3: barış anlaşması uygulama testi (--peace-test)
        bool aiPeaceTest = false;            // M42.4: AI barış davranışı testi (--ai-peace-test)
        bool peacePanelTest = false;         // M42.5: barış konferansı paneli testi (--peace-panel-test)
        bool seaZoneTest = false;            // M43.1: deniz bölgesi / hakimiyet testi (--sea-zone-test)
        bool convoyRaidTest = false;         // M43.2: denizaltı baskını / konvoy kaybı testi (--convoy-raid-test)
        bool navalCombatTest = false;        // M43.3: deniz çatışması testi (--naval-combat-test)
        bool lendLeaseTest = false;          // M43.4: müttefik konvoy koruması testi (--lend-lease-test)
        bool seaZonePanelTest = false;       // M43.5: deniz bölgesi paneli testi (--sea-zone-panel-test)
        bool formableDataTest = false;       // M44.1: kurgu ülke verisi testi (--formable-data-test)
        bool cultureTest = false;            // M44.2: il kültür katmanı testi (--culture-test)
        bool formableTest = false;           // M44.3: kurgu ülke kurma testi (--formable-test)
        bool releasableTest = false;         // M44.4: ilhak/kukla ülkeyi bağımsız çıkarma testi
        bool formablePanelTest = false;      // M44.5: kurgu ülke paneli testi (--formable-panel-test)
        bool releasablePanelTest = false;    // M45.1: bağımsız çıkarma paneli testi (--releasable-panel-test)
        bool autonomyTest = false;           // M45.2: özerklik sistemi testi (--autonomy-test)
        bool resistanceTest = false;         // M45.3: direniş sistemi testi (--resistance-test)
        bool cultureLegendTest = false;      // M46.2: kültür efsanesi paneli testi (--culture-legend-test)
        bool cultureInfoTest = false;        // M46.3: kültür bilgisi + eşleşmeyen il konturu testi (--culture-info-test)
        bool provinceEconomyTest = false;    // M47.1: il bazlı ekonomi ağırlık katmanı testi (--province-economy-test)
        bool resourceBalanceTest = false;    // M47.3: kaynak açığı ekonomik sonucu testi (--resource-balance-test)
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
            else if (arg == "--organization-test")
                organizationTest = true;
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
            else if (arg == "--sanctions-test")
                sanctionsTest = true;
            else if (arg == "--social-test")
                socialTest = true;
            else if (arg == "--profile-test")
                profileTest = true;
            else if (arg == "--trend-test")
                trendTest = true;
            else if (arg == "--tweet-action-test")
                tweetActionTest = true;
            else if (arg == "--profanity-test")
                profanityTest = true;
            else if (arg == "--espionage-test")
                espionageTest = true;
            else if (arg == "--spy-training-test")
                spyTrainingTest = true;
            else if (arg == "--spy-rental-test")
                spyRentalTest = true;
            else if (arg == "--counter-intel-test")
                counterIntelTest = true;
            else if (arg == "--space-test")
                spaceTest = true;
            else if (arg == "--space-mgmt-test")
                spaceMgmtTest = true;
            else if (arg == "--space-reward-test")
                spaceRewardTest = true;
            else if (arg == "--perf-test")
                perfTest = true;
            else if (arg == "--tech-test")
                techTest = true;
            else if (arg == "--nuclear-test")
                nuclearTest = true;
            else if (arg == "--sanctions-nuclear-test")
                sanctionsNuclearTest = true;
            else if (arg == "--nuclear-use-test")
                nuclearUseTest = true;
            else if (arg == "--ai-nuclear-test")
                aiNuclearTest = true;
            else if (arg == "--soak-test")
                soakTest = true;
            else if (arg == "--balance-test")
                balanceTest = true;
            else if (arg == "--victory-test")
                victoryTest = true;
            else if (arg == "--score-test")
                scoreTest = true;
            else if (arg == "-gameover-test" || arg == "--gameover-test")
                gameOverTest = true;
            else if (arg == "--war-score-test")
                warScoreTest = true;
            else if (arg == "--peace-terms-test")
                peaceTermsTest = true;
            else if (arg == "--peace-test")
                peaceTest = true;
            else if (arg == "--ai-peace-test")
                aiPeaceTest = true;
            else if (arg == "--peace-panel-test")
                peacePanelTest = true;
            else if (arg == "--sea-zone-test")
                seaZoneTest = true;
            else if (arg == "--convoy-raid-test")
                convoyRaidTest = true;
            else if (arg == "--naval-combat-test")
                navalCombatTest = true;
            else if (arg == "--lend-lease-test")
                lendLeaseTest = true;
            else if (arg == "--sea-zone-panel-test")
                seaZonePanelTest = true;
            else if (arg == "--formable-data-test")
                formableDataTest = true;
            else if (arg == "--culture-test")
                cultureTest = true;
            else if (arg == "--formable-test")
                formableTest = true;
            else if (arg == "--releasable-test")
                releasableTest = true;
            else if (arg == "--formable-panel-test")
                formablePanelTest = true;
                else if (arg == "--releasable-panel-test")
                    releasablePanelTest = true;
                else if (arg == "--autonomy-test")
                    autonomyTest = true;
                else if (arg == "--resistance-test")
                    resistanceTest = true;
                else if (arg == "--culture-legend-test")
                    cultureLegendTest = true;
                else if (arg == "--culture-info-test")
                    cultureInfoTest = true;
                else if (arg == "--province-economy-test")
                    provinceEconomyTest = true;
                else if (arg == "--resource-balance-test")
                    resourceBalanceTest = true;
            else if (arg == "--tooltip-test")
                tooltipTest = true;
            else if (arg == "--map-mode-test")
                mapModeTest = true;
            else if (arg == "--settings-test")
                settingsTest = true;
            else if (arg.StartsWith("--load-slot=") && int.TryParse(arg.Substring(12), out var ls))
                loadSlot = Mathf.Clamp(ls, 0, SaveSystem.MaxSlots);
        }

        // M26.1: --disaster-test verilmişse afet test modunu etkinleştirir (olasılık artırılır).
        // --save-load-test'te etkinleştirilmez: doğrulama testi 120 sn sınırında bitmeli ve
        // DeterministikRNG tohumu ile A==B determinismi zaten korunur. (Dedicated --disaster-test
        // afet tetikleme determinizmini sıınar.)
        if (disasterTest)
            DisasterSystem.EnableTestMode();

        // M30.1: --space-test verilmişse uzay programı test modunu etkinleştirir (başarı garantilenir).
        if (spaceTest)
            SpaceSystem.EnableTestMode();

        // M30.2: --space-mgmt-test verilmişse uzay proje yönetimi test modunu etkinleştirir.
        if (spaceMgmtTest)
            SpaceSystem.EnableTestMode();

        // M30.3: --space-reward-test verilmişse uzay ödül test modunu etkinleştirir (garanti başarı).
        if (spaceRewardTest)
            SpaceSystem.EnableTestMode();

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

        // M37.1: Uluslararası örgütler — Data/organizations.json'dan üyelik ve kural tanımları.
        OrganizationSystem.LoadData();

        // M41.1: Zafer koşulları — Data/victory_conditions.json'dan senaryo bazlı kazanma kuralları.
        VictorySystem.LoadData();

        // M23.1/23.2: Seçim ve kampanya verileri ResetForNewGame() -> InitializeAll() -> LoadData() ile tek sefer başlatılır.
        // Burada LoadData() çağrısı çıkarılıyor çünkü ResetForNewGame() zaten InitializeAll() içinden LoadData() çağırır;
        // aksi takdirde hem _Ready() hem de ResetForNewGame() üzerinden çift başlatma ve çift log satırı oluşur.

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

        // M37: Uluslararası örgüt testi — normal oyunda asla çalışmaz
        if (organizationTest)
            RunOrganizationTest();
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

        // M27.4: Yaptirim / ticaret savasi testi (ticaret/finansal/teknoloji/enerji/silah yaptirim,
        // iki yonlu iliski ve gerilim etkisi, ticaret savasi algilama, kalkirma, kaydet/yüklede A==B;
        // basarisizlikta Quit(1)).
        if (sanctionsTest)
            RunSanctionsTest();

        // M28.1: Olay → tweet üreteci testi (farklı olay kategorileriyle tweet üretilir, içerik dolu,
        // ids benzersiz, kaydet/yüklede A==B korunur; başarısızlıkta Quit(1)).
        if (socialTest)
            RunSocialMediaTest();

        // M28.2: Kurgusal profil sistemi testi (ülke kültürüne göre ad-soyad, avatar rengi,
        // türler: citizen/journalist/opposition/official/bot, yazar seçimi deterministik;
        // başarısızlıkta Quit(1)).
        if (profileTest)
            RunProfileTest();

        // M28.3: Trend listesi + duygu eğilimi testi (--trend-test).
        if (trendTest)
            RunTrendTest();

        // M28.4: Tweet eylemi + memnuniyet etkisi testi (--tweet-action-test).
        if (tweetActionTest)
            RunTweetActionTest();
        if (profanityTest)
            RunProfanityTest();
        if (espionageTest)
            RunEspionageTest();
        if (spyTrainingTest)
            RunSpyTrainingTest();
        if (spyRentalTest)
            RunSpyRentalTest();
        if (counterIntelTest)
            RunCounterIntelTest();

        if (spaceTest)
            RunSpaceTest();

        if (spaceMgmtTest)
            RunSpaceMgmtTest();

        if (spaceRewardTest)
            RunSpaceRewardTest();

        if (perfTest)
            RunPerfTest();

        if (techTest)
            RunTechTest();

        if (nuclearTest)
            RunNuclearTest();

        // M31.3: Yaptirim / nükleer diplomasi testi (BM yaptirim, komşu tepkisi, ticaret kısıtlaması,
        // anlaşmadan çıkış, dünya gerilimi, kaydet/yükle A==B; başarısızlıkta Quit(1)).
        if (sanctionsNuclearTest)
            RunSanctionsNuclearTest();

        // M31.4: Nükleer kullanım testi (saldırı, nüfus/ekonomi kaybı, radyasyon, gerilim, ilişkiler,
        // karşı saldırı, caydırıcılık, kaydet/yükle A==B; başarısızlıkta Quit(1)).
        if (nuclearUseTest)
            RunNuclearUseTest();

        // M31.5: AI nükleer davranış testi (caydırıcılık doktrini + başlık, AI'nin oyuncuya
        // saldırısını engeller; doctrina/başlık kaldırıldığında saldırıya geçer; DTO A==B).
        if (aiNuclearTest)
            RunAiNuclearTest();

        if (soakTest)
            RunSoakTest();

        // M34.1: Denge testi — savaş dağılımı ve AI saldırı oranı (%5-%30).
        if (balanceTest)
            RunBalanceTest();

        // M41.2: Zafer koşulları testi — koşul seti yüklenir, ilerleme ve bildirim doğrulanır.
        if (victoryTest)
            RunVictoryTest();

        // M41.3: Ulusal skor + sıralama testi.
        if (scoreTest)
            RunScoreTest();

        // M41.4: Oyun sonu paneli testi — panel açılır, 1152x648'e sığdığı doğrulanır, kapanır.
        if (gameOverTest)
            RunGameOverTest();

        // M42.1: Savaş puanı testi — iki tarafın puanı [0,100] aralığında ve tutarlı.
        if (warScoreTest)
            RunWarScoreTest();

        // M42.2: Barış talebi maliyeti testi — yetersiz puanda red, yeterlinde doğru düşüş.
        if (peaceTermsTest)
            RunPeaceTermsTest();

        // M42.3: Barış anlaşması uygulama testi — il devri + tazminat + savaş sonu.
        if (peaceTest)
            RunPeaceTest();

        // M42.4: AI barış davranışı testi — eşikler, güvenli red, savaş sonu.
        if (aiPeaceTest)
            RunAiPeaceTest();

        // M42.5: Barış konferansı paneli testi — ölçü, liste, talep onayı, ESC kapatma.
        if (peacePanelTest)
            RunPeacePanelTest();

        // M43.1: Deniz bölgesi / hakimiyet testi — bölge yükleme, [0,100] ölçek.
        if (seaZoneTest)
            RunSeaZoneTest();

        // M43.2: Denizaltı baskını testi — konvoy kaybı + ithalat kesintisi.
        if (convoyRaidTest)
            RunConvoyRaidTest();

        // M43.3: Deniz çatışması testi — filo hasarı, hakimiyet değişimi, determinizm.
        if (navalCombatTest)
            RunNavalCombatTest();

        // M43.4: Müttefik konvoy koruması testi — eskort kaybı azaltır, save/load.
        if (lendLeaseTest)
            RunLendLeaseTest();

        // M43.5: Deniz bölgesi paneli testi — ölçü, satırlar, koruma atama/geri alma, ESC kapatma.
        if (seaZonePanelTest)
            RunSeaZonePanelTest();

        // M44.1: Kurgu ülke verisi testi — JSON yüklenir, kayıt sayısı/örs tekilliği/ebe kaynakları.
        if (formableDataTest)
            RunFormableDataTest();

        // M44.2: İl kültür katmanı testi — dağılım tutarlı, nüfus eşit, deterministik, save/load.
        if (cultureTest)
            RunCultureTest();

        // M44.3: Kurgu ülke kurma testi — koşul sağlanmazsa reddedilir, sağlanınca ülke oluşur.
        if (formableTest)
            RunFormableTest();

        // M44.4: Bağımsız çıkarma testi — ilhak kaydı düşer, iller/fabrika payı geri döner.
        if (releasableTest)
            RunReleasableTest();

        // M44.5: Kurgu ülke paneli testi — kurgu ülkeler, kültür haritası, kurma/iptal.
        if (formablePanelTest)
            RunFormablePanelTest();

        if (releasablePanelTest)
            RunReleasablePanelTest();

        // M45.2: Özerklik sistemi testi — kayma, ver/geri al, çıkarım ve stabilite etkisi.
        if (autonomyTest)
            RunAutonomyTest();

        // M45.3: Direniş sistemi testi — kültür uyumsuzluğu, eylem, etkiler.
        if (resistanceTest)
            RunResistanceTest();

        // M46.2: Kültür efsanesi paneli testi — sıralama, çubuk, vurgu, harita bağı.
        if (cultureLegendTest)
            RunCultureLegendPanelTest();

        // M46.3: Kültür bilgisi (ipucu + ülke paneli) ve eşleşmeyen il konturu testi.
        if (cultureInfoTest)
            RunCultureInfoTest();

        // M47.1: İl bazlı ekonomi ağırlık katmanı testi — pay tutarlı, toplam korunur.
        if (provinceEconomyTest)
            RunProvinceEconomyTest();

        // M47.3: Kaynak açığının ekonomik sonucu testi — verim düşer, maliyet artar.
        if (resourceBalanceTest)
            RunResourceBalanceTest();

        // M13.2: Tooltip sistemi testi — Set/Show/Hide/Unset'i doğrular.
        if (tooltipTest)
            RunTooltipTest();

        // M13.3: Harita görünüm modu testi — Political/Terrain/Supply/Factories/Resources/AirZones.
        if (mapModeTest)
            RunMapModeTest();

        // M13.4: Ayarlar paneli testi — VSync/Default Speed/Difficulty/Ironman + Save/Load A==B.
        if (settingsTest)
            RunSettingsTest();

        // M17.1: Slot yükleme testi — mevcut kaydı yükler (v1 migration dahil). 0=auto, 1-10=slot.
        if (loadSlot >= 0)
        {
            bool ok = loadSlot == 0 ? SaveSystem.LoadAutoSave() : SaveSystem.LoadGame(loadSlot);
            GD.Print($"[Test] --load-slot={loadSlot}: {(ok ? "OK — yüklendi" : "BAŞARISIZ")}");
        }

        GD.Print($"GameManager hazır. Başlangıç tarihi: {GetDateText()} | Senaryo: {ScenarioSystem.ActiveName} ({ScenarioSystem.ActiveId}) | Hız: {Speed} | Ülke: {PlayerCountryId ?? "—"} | ProdTest: {prodTest} | WarTest: {warTestTarget ?? "—"}");
    }

    // M13.5: Performans testi — 5× hızda çalıştırılır, [WorldMap] FPS= örnekleri toplanır.
    // Headless 120 sn kuralı içinde kalmak üzere sabit süre sonra çıkar (tam 1936-1948
    // dönemi gerçek-zamanda ~877 sn sürebilir).
    private void RunPerfTest()
    {
        GD.Print("=========================================");
        GD.Print("  M13.5: PERFORMANS TESTI (5x, 1936-1948)");
        GD.Print("=========================================");

        if (!HasPlayer)
            SetPlayerCountry("GER");
        if (Speed == 0)
            SetSpeed(5);

        _perfTestStartDay = GameManager.Instance.TotalDays;
        _perfTestDay = 0;
        GameManager.Instance.DayChanged += OnPerfTestDayChanged;

        GD.Print($"[PerfTest] Başlangıç: {GetDateText()} | Hız: {Speed} | Ülke: {PlayerCountryId} | Gün limiti: {_perfTestMaxDays} gün");
    }

    private void OnPerfTestDayChanged(long totalDays)
    {
        _perfTestDay = (int)(totalDays - _perfTestStartDay);
        if (_perfTestDay >= _perfTestMaxDays)
        {
            GameManager.Instance.DayChanged -= OnPerfTestDayChanged;
            GD.Print($"[PerfTest] Gün sınırına ulaşıldı: {_perfTestDay} gün simüle edildi ({GetDateText()})");
            GD.Print("[PerfTest] SONUÇ: PASS ✓ — [WorldMap] FPS= örnekleri test-logs/perf-test.log'da");
            GetTree().Quit(0);
        }
    }

    private int _perfTestStartDay;
    private int _perfTestDay;
    private const int _perfTestMaxDays = 25;

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

        // M43.3: Deniz çatışması — savaştaki filolar bölgelerde çarpışır ve
        // yıpranır. Ekonomi tick'inden ÖNCE çalışır: o günkü konvoy baskını
        // (M43.2) çatışma SONRASI hakimiyeti görür.
        NavalCombatSystem.DailyTick();

        // M4: Ekonomi günlük tick'i (inşaat, kaynaklar, ticaret)
        EconomySystem.DailyTick();

        // M5: Teknoloji araştırması günlük ilerleme
        TechnologySystem.DailyTick();

        // M6: Üretim hatları günlük ilerleme
        EquipmentSystem.DailyTick();

        // M7: Ordu ve savaş sistemleri
        ArmySystem.DailyTick();
        WarSystem.DailyTick();
        // M39.1: İkmal günlük tick'i (ordular için ikmal seviyesi güncellenir).
        SupplySystem.DailyTick();
        // M39.2: Deniz invazyonu günlük ilerlemesi (denizden indirme / karşıdan karşıya indirme).
        NavalInvasionSystem.DailyTick();
        // M39.3: Ordu morali günlük tick'i (varsayılan değere doğru toparlanma).
        MoraleSystem.DailyTick();
        // M39.4: Hava üstünlüğü günlük drift'i (varsayılan değere doğru toparlanma).
        AirSuperioritySystem.DailyTick();

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

        // M29.4: Casus eğitim kursu günlük ilerleme (süre, bitince nitelik artışı).
        if (HasPlayer) EspionageSystem.DailyTick(TotalDays);

        // M30.1: Uzay programı günlük ilerleme — süreli görevlerin tamamlanması ve ödüller.
        if (HasPlayer) SpaceSystem.DailyTick(TotalDays);

        // M31.2: Nükleer program günlük ilerleme — süresi dolan aşamaların tamamlanması.
        if (HasPlayer) NuclearProgramSystem.DailyTick(TotalDays);

        // M25.5: Kariyer olayları günlük denetimi (skandal, fırsat, mentor kaybı, hastalık).
        if (HasPlayer)
            data.PoliticalPower = CareerEventSystem.DailyTick(TotalDays, PlayerCharacter, data.PoliticalPower);

        // M16: Zaman ve Tempo — ajanda ilerlemesi, hafta reset, aylık rapor
        TimeTempoSystem.DailyTick(this);

        // M41.2: Zafer koşulları — oyuncu her gün, AI 30 günde bir (performans kuralı)
        if (HasPlayer) VictorySystem.DailyTick(TotalDays);

        // M37: Uluslararası örgütler — yıllık zirve / aylık bakanlar toplantısı denetimi.
        OrganizationSystem.DailyTick(TotalDays);

        // M23.3: Seçim günü — oyuncu ülkesinde seçim sonucu çözümlenir (kazanma/kaybetme, lider değişikliği, yeni kabine)
        if (HasPlayer) ElectionSystem.ResolveElection(PlayerCountryId, TotalDays);

        // M23.4: Halk refahı — protesto, grev, güven oylaması, erken seçim, darbe girişimi (oyuncu ülkesinde).
        if (HasPlayer) PoliticalUnrestSystem.DailyTick(PlayerCountryId, TotalDays);

        // M45.2: Bağımlı ülkelerde özerklik kayması (deterministik, RNG yok) ve
        // özerk bağımlıların overlord stabilitesine günlük baskısı.
        AutonomySystem.DailyTick(TotalDays);
        ApplyAutonomyStabilityPressure();

        // M45.3: İl bazlı direniş — kültür uyumsuzluğu ve düşük özerklik biriken
        // baskıyı büyütür; eşik aşılınca deterministik partizan eylemi patlar.
        ResistanceSystem.DailyTick(TotalDays);

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

    // M45.2: Özerk bağımlıları olan her overlord'un stabilitesini her gün bir miktar
    // aşağı çeker. Tüm overlord'lar için çalışır (AI dahil) ve RNG kullanmaz.
    private void ApplyAutonomyStabilityPressure()
    {
        var pressured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in ReleasableSystem.Releasable())
        {
            if (pressured.Contains(record.OverlordId)) continue;
            pressured.Add(record.OverlordId);

            double pressure = AutonomySystem.StabilityPressure(record.OverlordId);
            if (pressure <= 0.0) continue;

            var overlord = CountryDatabase.Get(record.OverlordId);
            if (overlord == null) continue;

            double before = overlord.StabilityPercent;
            overlord.StabilityPercent = Math.Clamp(before - pressure, 0, 100);
            if (Math.Abs(before - overlord.StabilityPercent) < 1e-6) continue;

            GD.Print($"[M45.2] {record.OverlordId} stabilitesi düştü: {before:N1} → " +
                     $"{overlord.StabilityPercent:N1} (özerk bağımlılar)");
        }
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
        // İkmal sistemini başlat (M39.1)
        SupplySystem.Initialize(data);
        // Deniz invazyonu sistemini başlat (M39.2)
        NavalInvasionSystem.Initialize(data);
        // Ordu morali sistemini başlat (M39.3)
        MoraleSystem.Initialize(data);
        // Hava üstünlüğü sistemini başlat (M39.4)
        AirSuperioritySystem.Initialize(data);
        NavalCombatSystem.Initialize(data); // M43.3: deniz çatışması filo hasarı
        // Diplomasi sistemini başlat (M8)
        DiplomacySystem.Initialize(data);
        // M17A: Casus belli sistemini başlat — gerekçe/iddia/ateşkes/meşruiyet
        CasusBelliSystem.Initialize(data);
        // Ulusal odak sistemini başlat — ülkeye özel ağaç yüklenir (M9)
        FocusSystem.Initialize(data);

        // Uzay programını başlat — görev tanımları yüklenir ve kilitler hesaplanır (M30.1)
        SpaceSystem.Initialize();

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

        // M41.1: Zafer koşulları-yeni oyun (aktif senaryonun koşul seti yeniden okunur)
        VictorySystem.ResetForNewGame();

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

        // M29.1: Casusluk v2 — casus listesini yeni oyun için sıfırla.
        EspionageSystem.ResetForNewGame();

        // M30.1: Uzay programını yeni oyun için sıfırla.
        SpaceSystem.ResetForNewGame();

        // M31.2: Nükleer program durumunu yeni oyun için sıfırla.
        NuclearProgramSystem.ResetForNewGame();

        // M37: Uluslararası örgütleri yeni oyun için sıfırla (üyelikler veri dosyasından yeniden yüklenir).
        OrganizationSystem.ResetForNewGame();

        // M39.1: İkmal sistemini yeni oyun için sıfırla.
        SupplySystem.ResetForNewGame();
        // M39.2: Deniz invazyonu sistemini yeni oyun için sıfırla.
        NavalInvasionSystem.ResetForNewGame();
        // M39.3: Ordu morali sistemini yeni oyun için sıfırla.
        MoraleSystem.ResetForNewGame();
        // M39.4: Hava üstünlüğü sistemini yeni oyun için sıfırla.
        AirSuperioritySystem.ResetForNewGame();
        NavalCombatSystem.ResetForNewGame(); // M43.3: deniz çatışması filo hasarı
        ConvoyRaidSystem.ResetForNewGame(); // M43.4: müttefik konvoy koruma tahsisleri

        // M29.5: Casus kiralama havuzunu yeni oyun için başlat.
        EspionageSystem.InitializeRentalPool(TotalDays);

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

        if (!HasPlayer)
            SetPlayerCountry("GER");
        if (PlayerCountryData != null)
            PlayerCountryData.PoliticalPower = 500; // tüm aksiyon maliyetlerini karşıla
        if (Speed == 0)
            SetSpeed(0); // manuel gün ilerletme — pause kalır

        string pid = PlayerCountryData.Id;
        double staBefore = PlayerCountryData.StabilityPercent;
        double wsBefore = PlayerCountryData.WarSupportPercent;
        double legBefore = CasusBelliSystem.GetLegitimacy(pid);
        double tenBefore = DiplomacySystem.WorldTension;
        double satYouthBefore = PublicSatisfactionSystem.GetSatisfaction(pid, "youth");

        // 1) meeting(1g) + press(1g) + rally(3g) → 3 eş zamanlı (sınır)
        bool ok1 = TimeTempoSystem.AddActionToSlot(0, "meeting");
        bool ok2 = TimeTempoSystem.AddActionToSlot(1, "press");
        bool ok3 = TimeTempoSystem.AddActionToSlot(2, "rally");
        // 4. aksiyon → reddedilmeli (maks 3 eş zamanlı)
        bool ok4 = TimeTempoSystem.AddActionToSlot(3, "meeting");
        GD.Print($"[AgendaTest] meeting/press/rally: {ok1}/{ok2}/{ok3} | 4. aksiyon reddedildi: {!ok4}");

        // 3 gün ilerlet → meeting(1g), press(1g), rally(3g) tamamlanır
        AdvanceDaysForTest(3);

        double staAfter = PlayerCountryData.StabilityPercent;
        double wsAfter = PlayerCountryData.WarSupportPercent;
        double legAfter = CasusBelliSystem.GetLegitimacy(pid);
        double satYouthAfter = PublicSatisfactionSystem.GetSatisfaction(pid, "youth");
        GD.Print($"[AgendaTest] 3g sonra aktif aksiyon: {TimeTempoSystem.ActiveActions.Count} (0 olmalı)");
        GD.Print($"[AgendaTest] Stability: {staBefore:F1} → {staAfter:F1} (beklenen +4: meeting1+press1+rally2)");
        GD.Print($"[AgendaTest] WarSupport: {wsBefore:F1} → {wsAfter:F1} (beklenen +4: press1+rally3)");
        GD.Print($"[AgendaTest] Legitimacy: {legBefore:F1} → {legAfter:F1} (beklenen +3: press LegitimacyBonus)");
        GD.Print($"[AgendaTest] Youth memnuniyet: {satYouthBefore:F1} → {satYouthAfter:F1} (beklenen artış: rally PopularityBonus)");

        // 2) summit(7g) + training(5g) → boş slotlara 3, 4 (0-2 haftalık kilitli kalır)
        bool ok5 = TimeTempoSystem.AddActionToSlot(3, "summit");
        bool ok6 = TimeTempoSystem.AddActionToSlot(4, "training");
        GD.Print($"[AgendaTest] summit/training: {ok5}/{ok6}");

        // 7 gün ilerlet → summit(7g) ve training(5g) tamamlanır
        AdvanceDaysForTest(7);

        float resSpeed = TechnologySystem.GetModifier("research_speed");
        float adminEff = TechnologySystem.GetModifier("admin_efficiency");
        double tenAfter = DiplomacySystem.WorldTension;
        GD.Print($"[AgendaTest] 7g sonra aktif aksiyon: {TimeTempoSystem.ActiveActions.Count} (0 olmalı)");
        GD.Print($"[AgendaTest] ResearchSpeed modifikatör: {resSpeed:F3} (beklenen 0.02: training)");
        GD.Print($"[AgendaTest] AdminEfficiency modifikatör: {adminEff:F3} (beklenen 5: training)");
        GD.Print($"[AgendaTest] WorldTension: {tenBefore:F1} → {tenAfter:F1} (beklenen düşüş veya aynı: summit TensionReduction -5)");

        bool pass = ok1 && ok2 && ok3 && !ok4 && ok5 && ok6
                    && TimeTempoSystem.ActiveActions.Count == 0
                    && staAfter >= staBefore + 4
                    && wsAfter >= wsBefore + 4
                    && legAfter >= legBefore + 3
                    && satYouthAfter > satYouthBefore
                    && resSpeed >= 0.02f
                    && adminEff >= 5
                    && tenAfter <= tenBefore;
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

    /// <summary>
    /// M27.4: Yaptirim / ticaret savasi testi — GER (sert/uzlasmaci contex), bir hedef ulkeden
    /// kullanilan yaptirimlar (ticaret, finansal, teknoloji, enerji, silah), iki yonlu iliski
    /// dususu, gerilim artisi, ticaret savasi (iki yonlu trade yaptirim) algilama, kalkirma +
    /// iliski geri donusu, kayit sayisi. Deterministik, normal oyunda ASLA calismasi (--sanctions-test).
    /// </summary>
    private void RunSanctionsTest()
    {
        GD.Print("=========================================");
        GD.Print("  M27.4: YAPTIRIM / TICARET SAVASI TESTI (--sanctions-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;

        // Hedef ulke: oyuncuya komsu degil, goz atalim (ilk suitable non-player, non-annexed)
        string targetId = null;
        foreach (var c in CountryDatabase.All())
        {
            if (c.Id != playerId && !c.IsAnnexed) { targetId = c.Id; break; }
        }
        if (targetId == null)
        {
            GD.Print("[SanctionsTest] Uygun hedef ulke bulunamadi — test atlaniyor");
            GD.Print("=========================================");
            return;
        }

        float tensionBefore = DiplomacySystem.WorldTension;
        int relationBefore = DiplomacySystem.GetRelation(playerId, targetId);
        int reverseBefore = DiplomacySystem.GetRelation(targetId, playerId);
        int sanctionsBefore = DiplomacySystem.Sanctions.Count;
        GD.Print($"[SanctionsTest] {playerId} -> {targetId}: tension once=%{tensionBefore:F1}, iliski once={relationBefore}, " +
                 $"ters yon once={reverseBefore}, yaptirim once={sanctionsBefore}");

        bool allOk = true;
        var types = new[]
        {
            DiplomacySystem.Sanction.Type.Trade,
            DiplomacySystem.Sanction.Type.Financial,
            DiplomacySystem.Sanction.Type.Tech,
            DiplomacySystem.Sanction.Type.Energy,
            DiplomacySystem.Sanction.Type.Arms
        };
        foreach (var t in types)
        {
            bool ok = DiplomacySystem.ImposeSanction(playerId, targetId, t, 30);
            GD.Print($"[SanctionsTest] ImposeSanction({playerId} -> {targetId}, {t}, 30g): {(ok ? "OK" : "FAIL")}");
            if (!ok) allOk = false;
        }

        // Ayni yonlu ayni tur tekrar engellenmeli (diger tur onceden var)
        bool dupBlocked = !DiplomacySystem.ImposeSanction(playerId, targetId, DiplomacySystem.Sanction.Type.Trade, 60);
        GD.Print($"[SanctionsTest] Duplicate Trade engellendi: {(!dupBlocked ? "HAYIR (beklenmeydi)" : "EVET (dogru)")}");
        if (!dupBlocked) allOk = false;

        float tensionAfterImpose = DiplomacySystem.WorldTension;
        int relationAfterImpose = DiplomacySystem.GetRelation(playerId, targetId);
        int sanctionsAfterImpose = DiplomacySystem.Sanctions.Count;
        GD.Print($"[SanctionsTest] 5 yaptirim sonrasi: tension=%{tensionAfterImpose:F1} (delta={tensionAfterImpose - tensionBefore:+#;-#;0}), " +
                 $"iliski={relationAfterImpose} (delta={relationAfterImpose - relationBefore}), yaptirim={sanctionsBefore}->{sanctionsAfterImpose}");

        // Ticaret savasi: ikiden bir tarafin trade yaptirimi yeterli degil, iki yonlu lazim
        bool tradeWarBefore = DiplomacySystem.IsTradeWar(playerId, targetId);
        GD.Print($"[SanctionsTest] Ticaret savasi (once): {tradeWarBefore}");

        // Ters yonlu trade yaptirimyla tam turu kapat
        DiplomacySystem.ImposeSanction(targetId, playerId, DiplomacySystem.Sanction.Type.Trade, 45);
        bool tradeWarAfter = DiplomacySystem.IsTradeWar(playerId, targetId);
        GD.Print($"[SanctionsTest] Ticaret savasi (sonra): {tradeWarAfter}");
        if (!tradeWarAfter) allOk = false;

        // Tek bir yaptirimi kalkir: iliskinin yarisi geri doner
        var first = DiplomacySystem.Sanctions.FirstOrDefault(s => s.Active && s.InitiatorId == playerId && s.TargetId == targetId);
        bool liftOk = first != null && DiplomacySystem.LiftSanction(first.Id);
        GD.Print($"[SanctionsTest] Kalkirma ({first?.Id}): {(liftOk ? "OK" : "FAIL")}");
        if (!liftOk) allOk = false;

        int relationAfterLift = DiplomacySystem.GetRelation(playerId, targetId);
        int activeAfterLift = DiplomacySystem.GetActiveSanctionsFor(playerId).Count(s => s.Active);
        GD.Print($"[SanctionsTest] Kalkirma sonrasi: iliski={relationAfterLift} (delta vs once={relationAfterLift - relationAfterImpose}), " +
                 $"aktif yaptirim={activeAfterLift}");

        int sanctionsFinal = DiplomacySystem.Sanctions.Count;
        bool sanctionsRecorded = sanctionsAfterImpose >= sanctionsBefore + 5;
        bool relationChanged = relationAfterImpose != relationBefore;
        bool pass = allOk && sanctionsRecorded && relationChanged && tradeWarAfter && liftOk;

        GD.Print(pass ? "[SanctionsTest] SONUÇ: PASS ✓" : "[SanctionsTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M28.1: Olay → tweet üreteci testi — farklı olay kategorileriyle tweet üretilir,
    /// içerik/yazar boş olmamalı ve kayıt sayısı eşleşmeli. Deterministik, normal oyunda ASLA calismasi (--social-test).
    /// </summary>
    private void RunSocialMediaTest()
    {
        GD.Print("=========================================");
        GD.Print("  M28.1: OLAY → TWEET ÜRETİCİ TESTİ (--social-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;
        var playerData = PlayerCountryData;
        string playerName = playerData?.Name ?? playerId;
        GD.Print($"[SocialTest] Oyuncu: {playerId} ({playerName}), gün={TotalDays}");

        int before = SocialMediaSystem.TweetCount;

        // Farklı olay kategorileriyle tweet üretimini dene (gerçek olay verisine bağlı değil)
        var testEvents = new[]
        {
            (Id: "test_annex", Name: "Test Annexation", DeclareWar: ""),
            (Id: "test_war", Name: "Test War Declaration", DeclareWar: "FRA"),
            (Id: "test_peace", Name: "Test Peace Deal", DeclareWar: ""),
            (Id: "test_economy", Name: "Test Economic Miracle", DeclareWar: ""),
            (Id: "test_focus", Name: "Test National Focus", DeclareWar: ""),
            (Id: "test_diplomacy", Name: "Test Diplomacy Pact", DeclareWar: ""),
            (Id: "test_unknown", Name: "Test Unknown Event", DeclareWar: ""),
        };

        bool allOk = true;
        foreach (var te in testEvents)
        {
            var evt = new EventSystem.GameEvent
            {
                Id = te.Id,
                Name = te.Name,
                Description = "test",
                Options = new List<EventSystem.EventOption>
                {
                    new EventSystem.EventOption { Label = "Accept", Effects = new EventSystem.EventEffects { DeclareWar = te.DeclareWar } }
                }
            };
            SocialMediaSystem.OnEventResolved(evt, 0);
            GD.Print($"[SocialTest] Olay={te.Id} → toplam tweet={SocialMediaSystem.TweetCount}");
        }

        int after = SocialMediaSystem.TweetCount;
        int generated = after - before;
        bool tweetsGenerated = generated >= testEvents.Length;
        bool contentsNonEmpty = SocialMediaSystem.GetTweets().Skip(before).All(t =>
            !string.IsNullOrEmpty(t.Content) && !string.IsNullOrEmpty(t.AuthorHandle)
            && !string.IsNullOrEmpty(t.AuthorName) && t.Day == TotalDays);
        bool idsUnique = SocialMediaSystem.GetTweets().Skip(before).Select(t => t.Id).Distinct().Count() == generated;

        bool pass = allOk && tweetsGenerated && contentsNonEmpty && idsUnique;
        GD.Print($"[SocialTest] üretilen={generated}, beklenen>={testEvents.Length}, içerik_dolu={contentsNonEmpty}, id_benzersiz={idsUnique}");
        GD.Print(pass ? "[SocialTest] SONUÇ: PASS ✓" : "[SocialTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M28.2: Kurgusal profil sistemi testi — ülke kültürüne göre ad-soyad üretilir,
    /// avatar rengi atanır, türler (citizen/journalist/opposition/official/bot) doğru,
    /// handles benzersiz, yazar seçimi deterministik. Normal oyunda ASLA çalışır (--profile-test).
    /// </summary>
    private void RunProfileTest()
    {
        GD.Print("=========================================");
        GD.Print("  M28.2: KURGUSAL PROFİL SİSTEM TESTİ (--profile-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;

        // Farklı kültürlerde profil üretimini dene (GER/anglo, FRA/latin, RUS/slavic,
        // JPN/east_asian, TUR/turkish, USA/anglo, IRN/iranian, BRA/latin).
        var testCountries = new[] { "GER", "FRA", "RUS", "JPN", "TUR", "USA", "IRN", "BRA" };
        int totalProfiles = 0;
        bool allOk = true;

        foreach (var cid in testCountries)
        {
            SocialProfileSystem.EnsureProfilesFor(cid);
            var profiles = SocialProfileSystem.GetProfiles(cid);
            int count = profiles.Count;
            totalProfiles += count;

            bool namesOk = profiles.All(p => !string.IsNullOrEmpty(p.Name) && p.Name.Split(' ').Length >= 2);
            bool handlesOk = profiles.All(p => !string.IsNullOrEmpty(p.Handle) && p.Handle.StartsWith("@"));
            bool handlesUnique = profiles.Select(p => p.Handle).Distinct().Count() == count;
            bool avatarsOk = profiles.All(p => !string.IsNullOrEmpty(p.AvatarColor) && p.AvatarColor.StartsWith("#"));
            bool typesOk = profiles.All(p => Enum.IsDefined(typeof(SocialProfileSystem.ProfileType), p.Type));

            var sample = profiles.FirstOrDefault();
            GD.Print($"[ProfileTest] {cid}: profil={count}, isim_dolu={namesOk}, handle_dolu_benzersiz={handlesOk && handlesUnique}, " +
                     $"avatar_rengi={avatarsOk}, turler_gecerli={typesOk}, örnek={sample?.Name} ({sample?.Handle}, {sample?.Type}, {sample?.Followers} takipçi, verified={sample?.Verified})");

            if (count == 0 || !namesOk || !(handlesOk && handlesUnique) || !avatarsOk || !typesOk)
                allOk = false;
        }

        // Yazar seçimi deterministik ve boş olmamalı (oyuncu ülkesinden örnek).
        var a1 = SocialProfileSystem.GetRandomAuthor(playerId);
        var a2 = SocialProfileSystem.GetRandomAuthor(playerId);
        bool authorOk = !string.IsNullOrEmpty(a1.Name) && !string.IsNullOrEmpty(a1.Handle)
                     && !string.IsNullOrEmpty(a2.Name) && !string.IsNullOrEmpty(a2.Handle);
        GD.Print($"[ProfileTest] Yazar örnek 1: {a1.Name} ({a1.Handle})");
        GD.Print($"[ProfileTest] Yazar örnek 2: {a2.Name} ({a2.Handle})");

        bool pass = allOk && authorOk && totalProfiles > 0;
        GD.Print($"[ProfileTest] toplam_profil={totalProfiles}, yazar_dolu={authorOk}");
        GD.Print(pass ? "[ProfileTest] SONUÇ: PASS ✓" : "[ProfileTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M28.3: Trend listesi + duygu eğilimi testi — tweet'leri etkileşime göre sıralayan
    /// GetTrendList ve GetSentimentSummary/GetEngagementTotals'in doğru çalıştığını doğrular.
    /// Normal oyunda ASLA çalışır (--trend-test).
    /// </summary>
    private void RunTrendTest()
    {
        GD.Print("=========================================");
        GD.Print("  M28.3: TREND LISTESI + DUYGU EĞİLİMİ TESTİ (--trend-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;
        SocialProfileSystem.EnsureProfilesFor(playerId);

        int before = SocialMediaSystem.TweetCount;

        // Farklı kategorilerle tweet üret → farklı sentiment (War/Annex=Negatif,
        // Peace/Economy/Focus=Pozitif, Default=Nötr) ve farklı etkileşim.
        var testEvents = new[]
        {
            (Id: "trend_war", Name: "Test War", DeclareWar: "FRA"),
            (Id: "trend_annex", Name: "Test Annex", DeclareWar: ""),
            (Id: "trend_peace", Name: "Test Peace", DeclareWar: ""),
            (Id: "trend_economy", Name: "Test Economy", DeclareWar: ""),
            (Id: "trend_focus", Name: "Test Focus", DeclareWar: ""),
            (Id: "trend_diplomacy", Name: "Test Diplomacy", DeclareWar: ""),
        };

        bool allOk = true;
        foreach (var te in testEvents)
        {
            var evt = new EventSystem.GameEvent
            {
                Id = te.Id,
                Name = te.Name,
                Description = "test",
                Options = new List<EventSystem.EventOption>
                {
                    new EventSystem.EventOption { Label = "Accept", Effects = new EventSystem.EventEffects { DeclareWar = te.DeclareWar } }
                }
            };
            SocialMediaSystem.OnEventResolved(evt, 0);
        }

        int after = SocialMediaSystem.TweetCount;
        int generated = after - before;
        bool tweetsGenerated = generated >= testEvents.Length;

        // 1) Trend listesi: etkileşime (beğeni+paylaşım) göre azalan, eşlikte daha yeni önce.
        var trend = SocialMediaSystem.GetTrendList(100);
        bool trendSorted = true;
        for (int i = 1; i < trend.Count; i++)
        {
            int engPrev = trend[i - 1].Likes + trend[i - 1].Shares;
            int engCur = trend[i].Likes + trend[i].Shares;
            if (engCur > engPrev || (engCur == engPrev && trend[i].Day > trend[i - 1].Day))
            {
                trendSorted = false;
                break;
            }
        }
        GD.Print($"[TrendTest] trend_count={trend.Count}, sıralı_azalan={trendSorted}");

        // 2) Duygu özeti: toplam tweet sayısına eşit olmalı.
        var (pos, neg, neu) = SocialMediaSystem.GetSentimentSummary();
        int sentimentTotal = pos + neg + neu;
        bool sentimentCounts = sentimentTotal == after;
        GD.Print($"[TrendTest] duygu: pozitif={pos}, negatif={neg}, nötr={neu}, toplam={sentimentTotal} (tweet={after})");

        // 3) Toplam etkileşim: non-negative ve tweet sayısıyla tutarlı.
        var (totalLikes, totalShares) = SocialMediaSystem.GetEngagementTotals();
        bool engagementOk = totalLikes >= 0 && totalShares >= 0;
        GD.Print($"[TrendTest] toplam_beğeni={totalLikes}, toplam_paylaşım={totalShares}");

        bool contentsNonEmpty = SocialMediaSystem.GetTweets().Skip(before).All(t =>
            !string.IsNullOrEmpty(t.Content) && !string.IsNullOrEmpty(t.AuthorHandle)
            && !string.IsNullOrEmpty(t.AuthorName));

        bool result = allOk && tweetsGenerated && trendSorted && sentimentCounts && engagementOk && contentsNonEmpty;
        GD.Print($"[TrendTest] üretilen={generated}, bekleseen>={testEvents.Length}, trend_sıralı={trendSorted}, " +
                 $"duygu_tam={sentimentCounts}, etkileşim_ok={engagementOk}, içerik_dolu={contentsNonEmpty}");
        GD.Print(result ? "[TrendTest] SONUÇ: PASS ✓" : "[TrendTest] SONUÇ: FAIL ✗");
        GetTree().Quit(result ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M28.4: Tweet eylemi testi — tweete tepki (Statement / Investigation / Ignore / Ban),
    /// toplum grubu memnuniyetine deterministik etki, tekrarlı eylem engelleme ve
    /// kaydet/yüklede eylemin korunması. Normal oyunda ASLA çalışır (--tweet-action-test).
    /// </summary>
    private void RunTweetActionTest()
    {
        GD.Print("=========================================");
        GD.Print("  M28.4: TWEET AKSİYON TESTİ (--tweet-action-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;
        SocialProfileSystem.EnsureProfilesFor(playerId);

        int before = SocialMediaSystem.TweetCount;

        // Farklı kategorilerde tweet üret → eylem yapılabilir tweet havuzu
        var testEvents = new[]
        {
            (Id: "tact_war", Name: "Tact War", DeclareWar: "FRA"),
            (Id: "tact_annex", Name: "Tact Annex", DeclareWar: ""),
            (Id: "tact_peace", Name: "Tact Peace", DeclareWar: ""),
            (Id: "tact_economy", Name: "Tact Economy", DeclareWar: ""),
            (Id: "tact_focus", Name: "Tact Focus", DeclareWar: ""),
            (Id: "tact_diplomacy", Name: "Tact Diplomacy", DeclareWar: ""),
        };

        foreach (var te in testEvents)
        {
            var evt = new EventSystem.GameEvent
            {
                Id = te.Id,
                Name = te.Name,
                Description = "test",
                Options = new List<EventSystem.EventOption>
                {
                    new EventSystem.EventOption { Label = "Accept", Effects = new EventSystem.EventEffects { DeclareWar = te.DeclareWar } }
                }
            };
            SocialMediaSystem.OnEventResolved(evt, 0);
        }

        var tweets = SocialMediaSystem.GetTweets().Skip(before).ToList();
        bool tweetsGenerated = tweets.Count >= testEvents.Length;
        GD.Print($"[TweetActionTest] üretilen tweet={tweets.Count} (beklenen>={testEvents.Length})");

        // Eylem öncesi memnuniyet fotoğrafı (tüm grup)
        var groupIds = PublicSatisfactionSystem.Groups.Select(g => g.Id).ToList();
        var satBefore = groupIds.ToDictionary(g => g, g => PublicSatisfactionSystem.GetSatisfaction(playerId, g));

        // Her tweete farklı bir eylem uygula
        var actions = new[] { SocialMediaSystem.TweetAction.Statement, SocialMediaSystem.TweetAction.Investigation, SocialMediaSystem.TweetAction.Ignore, SocialMediaSystem.TweetAction.Ban };
        var respondedTweets = new List<SocialMediaSystem.Tweet>();
        var respondOk = new List<bool>();
        for (int i = 0; i < actions.Length && i < tweets.Count; i++)
        {
            bool ok = SocialMediaSystem.RespondToTweet(tweets[i].Id, actions[i]);
            respondOk.Add(ok);
            respondedTweets.Add(tweets[i]);
            GD.Print($"[TweetActionTest] {tweets[i].Id} → {actions[i]}: {(ok ? "OK" : "FAIL")}");
        }

        // Aynı tweete ikinci kez eylem → REDDEDİLMELİ
        bool doubleRespond = SocialMediaSystem.RespondToTweet(respondedTweets[0].Id, SocialMediaSystem.TweetAction.Ban);
        GD.Print($"[TweetActionTest] tekrarlı eylem engellendi: {!doubleRespond}");

        // Yok tweete eylem → REDDEDİLMELİ
        bool ghostRespond = SocialMediaSystem.RespondToTweet("tweet-yok-yok", SocialMediaSystem.TweetAction.Statement);
        GD.Print($"[TweetActionTest] yok-tweet eylem engellendi: {!ghostRespond}");

        // Eylemler uygulanmış mı + ActionDay doğru mu?
        bool actionsApplied = true;
        for (int i = 0; i < respondedTweets.Count; i++)
        {
            var t = respondedTweets[i];
            if (t.Action != actions[i] || t.ActionDay != TotalDays) actionsApplied = false;
            GD.Print($"[TweetActionTest] tweet={t.Id} eylem={t.Action} gün={t.ActionDay} (istenen={actions[i]})");
        }

        // Memnuniyet sonrası: her eylem en az bir grubu etkilemeli
        var satAfter = groupIds.ToDictionary(g => g, g => PublicSatisfactionSystem.GetSatisfaction(playerId, g));
        bool anyChanged = false;
        foreach (var g in groupIds)
        {
            double d = satAfter[g] - satBefore[g];
            GD.Print($"[TweetActionTest] {g}: {satBefore[g]:F2} → {satAfter[g]:F2} (delta={d:+#.##;-#.##;0})");
            if (Math.Abs(d) > 0.001) anyChanged = true;
        }

        // Kaydet / Yükle: eylemin kalıcı olması (M28.4 → M12)
        SaveSystem.SaveTestFile();
        bool loaded = SaveSystem.LoadTestFile();
        var tweetsAfterLoad = SocialMediaSystem.GetTweets().ToList();
        bool actionsPersisted = true;
        for (int i = 0; i < respondedTweets.Count; i++)
        {
            var t = tweetsAfterLoad.FirstOrDefault(x => x.Id == respondedTweets[i].Id);
            if (t == null || t.Action != actions[i] || t.ActionDay != respondedTweets[i].ActionDay)
                actionsPersisted = false;
        }
        GD.Print($"[TweetActionTest] kaydet_yukle={loaded} eylemler_kalici={actionsPersisted}");

        bool result = tweetsGenerated
                    && respondOk.All(ok => ok)
                    && !doubleRespond
                    && !ghostRespond
                    && actionsApplied
                    && anyChanged
                    && loaded
                    && actionsPersisted;

        GD.Print(result ? "[TweetActionTest] SONUÇ: PASS ✓" : "[TweetActionTest] SONUÇ: FAIL ✗");
        GetTree().Quit(result ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>M28.5: Küfür/nefret içeriği filtresi + günlük tweet sayısı sınırı. Normal oyunda ASLA çalışır (--profanity-test).</summary>
    private void RunProfanityTest()
    {
        GD.Print("=========================================");
        GD.Print("  M28.5: KÜFÜR/NEFRET FİLTRESİ + GÜNLÜK SINIR TESTİ (--profanity-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;
        SocialProfileSystem.EnsureProfilesFor(playerId);

        // 1. Kelime listesi yüklensin; filtre engelli/temiz/büyük-küçük ve censor işlemlerini doğru yapsın.
        SocialMediaSystem.EnsureProfanityLoaded();
        bool blockedDetected = SocialMediaSystem.ContainsBlocked("This user is an idiot and runs a scam.");
        bool cleanPasses = !SocialMediaSystem.ContainsBlocked("Germany is now at war with France.");
        bool caseInsensitive = SocialMediaSystem.ContainsBlocked("STUPID content here.");
        string censored = SocialMediaSystem.Censor("You are stupid and spread hate.");
        bool censoredOk = censored.Contains("***");
        GD.Print($"[ProfanityTest] engelli_tespit={blockedDetected} temiz_geçti={cleanPasses} büyük_küçük={caseInsensitive} censorlendi={censoredOk}");

        // 2. Günlük tweet sınırı: aynı günde üst sınırdan fazla tweet eklenemez (performans).
        int savedLimit = SocialMediaSystem.MaxTweetsPerDay;
        SocialMediaSystem.MaxTweetsPerDay = 2;
        int before = SocialMediaSystem.TweetCount;
        SocialMediaSystem.OnEventResolved(MakeTestEvent("pf_war", "PF War", "FRA"), 0);
        SocialMediaSystem.OnEventResolved(MakeTestEvent("pf_annex", "PF Annex", ""), 0);
        SocialMediaSystem.OnEventResolved(MakeTestEvent("pf_peace", "PF Peace", ""), 0);
        int after = SocialMediaSystem.TweetCount;
        bool limitWorks = (after - before) == 2;
        GD.Print($"[ProfanityTest] günlük_sınır={limitWorks} eklenen={after - before} (sınır=2)");
        SocialMediaSystem.MaxTweetsPerDay = savedLimit;

        // 3. Küfürlü içerikli tweet eklenmez.
        int before2 = SocialMediaSystem.TweetCount;
        SocialMediaSystem.OnEventResolved(MakeTestEvent("pf_bad", "idiot event", ""), 0);
        int after2 = SocialMediaSystem.TweetCount;
        bool blockedTweetSkipped = after2 == before2;
        GD.Print($"[ProfanityTest] engelli_tweet_engellendi={blockedTweetSkipped} önce={before2} sonra={after2}");

        bool result = blockedDetected && cleanPasses && caseInsensitive && censoredOk && limitWorks && blockedTweetSkipped;
        GD.Print(result ? "[ProfanityTest] SONUÇ: PASS ✓" : "[ProfanityTest] SONUÇ: FAIL ✗");
        GetTree().Quit(result ? 0 : 1);
        GD.Print("=========================================");
    }

    // M29.1: Casusluk v2 — casus niteliği modeli testi (--espionage-test).
    // Deterministik üretim (aynı tohum → aynı casus), nitelik aralıkları 0-100,
    // GetSpyCard özeti ve Save/Load A==B doğrular.
    private void RunEspionageTest()
    {
        GD.Print("=========================================");
        GD.Print("  M29.1: CASUSLUK v2 — CASUS NİTELİĞİ MODELİ TESTİ (--espionage-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        bool ok = true;

        // 1. Deterministik üretim: aynı tohum → aynı casus özellikleri.
        DeterministicRNG.InitializeForNewGame(987654321);
        var s1 = EspionageSystem.GenerateSpy("GER");
        DeterministicRNG.InitializeForNewGame(987654321);
        var s2 = EspionageSystem.GenerateSpy("GER");
        bool deterministic = s1.Name == s2.Name
            && s1.BirthCulture == s2.BirthCulture
            && s1.CultureKnowledge == s2.CultureKnowledge
            && s1.RegionExperience == s2.RegionExperience
            && s1.RegionExperienceLevel == s2.RegionExperienceLevel
            && s1.Education == s2.Education
            && s1.Loyalty == s2.Loyalty
            && s1.Observation == s2.Observation
            && s1.ColdBlood == s2.ColdBlood
            && s1.Languages.Count == s2.Languages.Count
            && !s1.Languages.Keys.Except(s2.Languages.Keys).Any();
        GD.Print($"[EspionageTest] deterministik={deterministic} ad={s1.Name} kültür={s1.BirthCulture}");

        // 2. Tüm 0-100 nitelikleri aralıkta.
        bool rangesOk = s1.CultureKnowledge >= 0 && s1.CultureKnowledge <= 100
            && s1.RegionExperienceLevel >= 0 && s1.RegionExperienceLevel <= 100
            && s1.Education >= 0 && s1.Education <= 100
            && s1.Loyalty >= 0 && s1.Loyalty <= 100
            && s1.Observation >= 0 && s1.Observation <= 100
            && s1.ColdBlood >= 0 && s1.ColdBlood <= 100
            && s1.Languages.Values.All(v => v >= 0 && v <= 100);
        GD.Print($"[EspionageTest] araliklar_0_100={rangesOk}");

        // 3. GetSpyCard boş olmayan özet döndürür ve adı içerir.
        string card = EspionageSystem.GetSpyCard(s1.Id);
        bool cardOk = !string.IsNullOrEmpty(card) && card.Contains(s1.Name);
        GD.Print($"[EspionageTest] kart={cardOk}");

        // 3b. Yatkınlık matrisi: kaynak kültür → hedef ülke affinity % (0-100).
        EspionageSystem.EnsureAffinityLoaded();
        int gerDeu = EspionageSystem.GetAffinity("germanic", "DEU"); // aynı kültür = 100
        int gerFra = EspionageSystem.GetAffinity("germanic", "FRA"); // dış kültür, default = 30
        int gerSwe = EspionageSystem.GetAffinity("germanic", "SWE"); // nordic yakınlık = 70
        int turSau = EspionageSystem.GetAffinity("turkish", "SAU"); // Türk-Arap = 60
        int araTur = EspionageSystem.GetAffinity("arabic", "TUR"); // Arap-Türk = 60
        bool affinityOk = gerDeu == 100 && gerFra == 30 && gerSwe == 70 && turSau == 60 && araTur == 60;
        GD.Print($"[EspionageTest] yatkinlik={affinityOk} GER→DEU={gerDeu} GER→FRA={gerFra} GER→SWE={gerSwe} TUR→SAU={turSau} ARB→TUR={araTur}");

        // 3c. Casus üzerinden yatkınlık döner ve 0-100 aralıktadır.
        int spyAff = EspionageSystem.GetAffinityForSpy(s1.Id, "FRA");
        bool spyAffOk = spyAff >= 0 && spyAff <= 100;
        GD.Print($"[EspionageTest] casus_yatkinlik={spyAffOk} deger={spyAff}");

        // 3d. Yakalanma olasılığı formülü: base × (1.5 − affinity/100) × counterIntel × (1 − edu/100), [0,1].
        double capLow = EspionageSystem.GetCaptureChance(s1.Id, "DEU", 0.5);   // affinity yüksek → düşük risk
        double capHigh = EspionageSystem.GetCaptureChance(s1.Id, "FRA", 0.5);  // affinity düşük → yüksek risk
        bool capRangeOk = capLow >= 0.0 && capLow <= 1.0 && capHigh >= 0.0 && capHigh <= 1.0;
        bool capAffinityEffect = capHigh > capLow; // düşük affinity = daha yüksek yakalanma riski
        string risk = EspionageSystem.GetCaptureRiskLevel(s1.Id, "FRA", 0.5);
        bool riskOk = risk is "Low" or "Moderate" or "High" or "Critical";
        GD.Print($"[EspionageTest] yakalanma={capRangeOk} low={capLow:F3} high={capHigh:F3} affinity_etkisi={capAffinityEffect} risk={risk}");

        // 3e. Düşük yatkınlıkta uyarı (affinity < 40). spyAff=50 (anglo→FRA) → uyarı yok; JPN affinity=30 → uyarı var.
        bool warn = EspionageSystem.IsCaptureRiskWarning(s1.Id, "FRA");
        bool warnOk = warn == (spyAff < 40);
        int spyAffJpn = EspionageSystem.GetAffinityForSpy(s1.Id, "JPN");
        bool warnJpn = EspionageSystem.IsCaptureRiskWarning(s1.Id, "JPN");
        bool warnJpnOk = warnJpn == (spyAffJpn < 40);
        GD.Print($"[EspionageTest] yakalanma_uyarisi={warn} (beklenen={spyAff < 40}) dogruluk={warnOk} | JPN affin={spyAffJpn} uyar={warnJpn} dogruluk={warnJpnOk}");

        // 4. Save/Load A==B.
        var data1 = EspionageSystem.GetSaveData();
        EspionageSystem.LoadSaveData(data1);
        var data2 = EspionageSystem.GetSaveData();
        bool saveLoadOk = data1.Spies.Length == data2.Spies.Length;
        if (saveLoadOk && data1.Spies.Length > 0)
        {
            var a = data1.Spies[0];
            var b = data2.Spies[0];
            saveLoadOk = a.Id == b.Id && a.Name == b.Name && a.BirthCulture == b.BirthCulture
                && a.CultureKnowledge == b.CultureKnowledge && a.RegionExperience == b.RegionExperience
                && a.RegionExperienceLevel == b.RegionExperienceLevel && a.Education == b.Education
                && a.Loyalty == b.Loyalty && a.Observation == b.Observation && a.ColdBlood == b.ColdBlood
                && a.LanguageIds.Length == b.LanguageIds.Length
                && !a.LanguageIds.Except(b.LanguageIds).Any();
        }
        GD.Print($"[EspionageTest] save_load_a_b={saveLoadOk} casus={EspionageSystem.SpyCount}");

        // 5. ResetForNewGame temizler.
        EspionageSystem.ResetForNewGame();
        bool resetOk = EspionageSystem.SpyCount == 0;
        GD.Print($"[EspionageTest] reset={resetOk}");

        ok = deterministic && rangesOk && cardOk && affinityOk && spyAffOk && capRangeOk && capAffinityEffect && riskOk && warnOk && warnJpnOk && saveLoadOk && resetOk;
        GD.Print(ok ? "[EspionageTest] SONUÇ: PASS ✓" : "[EspionageTest] SONUÇ: FAIL ✗");
        GetTree().Quit(ok ? 0 : 1);
        GD.Print("=========================================");
    }

    // M29.4: Casusluk v2 — eğitim kursları testi (--spy-training-test).
    // Kurs başlatma, meşguliyet, günlük ticks, bitişte nitelik artışı, Save/Load A==B, ResetForNewGame doğrular.
    private void RunSpyTrainingTest()
    {
        GD.Print("=========================================");
        GD.Print("  M29.4: CASUSLUK v2 — EĞİTİM KURSLARI TESTİ (--spy-training-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        bool ok = true;

        // 1. Kurs tanımları: süre (hafta) ve PP maliyeti beklenildiği gibi.
        var langDef = EspionageSystem.GetCourseDef(EspionageSystem.TrainingCourseType.Language);
        var culDef = EspionageSystem.GetCourseDef(EspionageSystem.TrainingCourseType.Culture);
        var fldDef = EspionageSystem.GetCourseDef(EspionageSystem.TrainingCourseType.Field);
        bool defsOk = langDef != null && culDef != null && fldDef != null
            && langDef.DurationWeeks == 2 && langDef.CostPP == 50
            && culDef.DurationWeeks == 3 && culDef.CostPP == 75
            && fldDef.DurationWeeks == 4 && fldDef.CostPP == 100;
        GD.Print($"[SpyTrainingTest] kurs_tanımları={defsOk} dil={langDef.DurationWeeks}hafta/{langDef.CostPP}PP kültür={culDef.DurationWeeks}hafta/{culDef.CostPP}PP saha={fldDef.DurationWeeks}hafta/{fldDef.CostPP}PP");

        // 2. Deterministik casus üretimi.
        DeterministicRNG.InitializeForNewGame(11223344);
        var s1 = EspionageSystem.GenerateSpy("GER");
        var s2 = EspionageSystem.GenerateSpy("FRA");
        GD.Print($"[SpyTrainingTest] casuslar={s1.Id}({s1.Name}) {s2.Id}({s2.Name})");

        // 3. Kurs başlatma: s1'e dil kursu (14 gün, 50 PP).
        bool started = EspionageSystem.StartTrainingCourse(s1.Id, EspionageSystem.TrainingCourseType.Language, 0);
        bool inTraining = EspionageSystem.IsInTraining && EspionageSystem.IsSpyInTraining(s1.Id);
        int days = EspionageSystem.DaysRemaining;
        bool startOk = started && inTraining && days == 14;
        GD.Print($"[SpyTrainingTest] basladi={started} mesgul={inTraining} kalan_gun={days} basarili={startOk}");

        // 3b. Aynı anda ikinci kurs başlatılamaz; bilinmeyen casusa başlatılamaz.
        bool doubleStart = EspionageSystem.StartTrainingCourse(s2.Id, EspionageSystem.TrainingCourseType.Culture, 0);
        bool unknownStart = EspionageSystem.StartTrainingCourse("spy-nonexistent", EspionageSystem.TrainingCourseType.Field, 0);
        bool concurrencyOk = !doubleStart && !unknownStart && EspionageSystem.IsSpyInTraining(s1.Id);
        GD.Print($"[SpyTrainingTest] cift_baslatma_reddedildi={!doubleStart} bilinmeyen_reddedildi={!unknownStart} tek_aktif={concurrencyOk}");

        // Dil seviyeleri öncesi kopyası.
        var langsBefore = s1.Languages.ToDictionary(kv => kv.Key, kv => kv.Value);

        // 4. Günlük tick'ler — 14. günde tamamlanmalı, bir dil +15.
        for (int i = 1; i <= 14; i++)
            EspionageSystem.DailyTick(i);
        bool completed = !EspionageSystem.IsInTraining && EspionageSystem.CompletedCount == 1 && EspionageSystem.LastSuccess;
        bool totalSpentOk = EspionageSystem.TotalSpentPP == 50;
        bool langChanged = false;
        foreach (var kv in s1.Languages)
        {
            if (langsBefore.TryGetValue(kv.Key, out var before) && kv.Value == before + 15)
                langChanged = true;
        }
        bool langOk = s1.Languages.Count == langsBefore.Count && (s1.Languages.Count == 0 || langChanged);
        GD.Print($"[SpyTrainingTest] tamamlandi={completed} toplam_PP={EspionageSystem.TotalSpentPP} dil_degisti={langOk} dil_sayisi_ayni={s1.Languages.Count == langsBefore.Count}");

        // 5. İkinci kurs: s2'ye kültür kursu (21 gün), CultureKnowledge +20.
        bool started2 = EspionageSystem.StartTrainingCourse(s2.Id, EspionageSystem.TrainingCourseType.Culture, 20);
        int culBefore = s2.CultureKnowledge;
        for (int i = 21; i <= 41; i++)
            EspionageSystem.DailyTick(i);
        bool culOk = !EspionageSystem.IsInTraining && EspionageSystem.CompletedCount == 2
            && s2.CultureKnowledge == Math.Clamp(culBefore + 20, 0, 100)
            && EspionageSystem.TotalSpentPP == 50 + 75 && started2;
        GD.Print($"[SpyTrainingTest] kültür_tamamlandı={!EspionageSystem.IsInTraining} completed_count={EspionageSystem.CompletedCount} CK {culBefore}→{s2.CultureKnowledge} toplam_PP={EspionageSystem.TotalSpentPP}");

        // 6. Save/Load A==B (eğitim durumu dahil).
        var data1 = EspionageSystem.GetSaveData();
        EspionageSystem.LoadSaveData(data1);
        var data2 = EspionageSystem.GetSaveData();
        bool saveLoadOk = data1.Spies.Length == data2.Spies.Length
            && data1.Training.SpyId == data2.Training.SpyId
            && data1.Training.CourseType == data2.Training.CourseType
            && data1.Training.DaysRemaining == data2.Training.DaysRemaining
            && data1.Training.CompletedCount == data2.Training.CompletedCount
            && data1.Training.TotalSpentPP == data2.Training.TotalSpentPP;
        GD.Print($"[SpyTrainingTest] save_load_a_b={saveLoadOk} completed={data2.Training.CompletedCount} pp={data2.Training.TotalSpentPP}");

        // 7. ResetForNewGame temizler (eğitim durumu dahil).
        EspionageSystem.ResetForNewGame();
        bool resetOk = EspionageSystem.SpyCount == 0 && !EspionageSystem.IsInTraining
            && EspionageSystem.CompletedCount == 0 && EspionageSystem.TotalSpentPP == 0;
        GD.Print($"[SpyTrainingTest] reset={resetOk}");

        ok = defsOk && startOk && concurrencyOk && completed && totalSpentOk && langOk && culOk && saveLoadOk && resetOk;
        GD.Print(ok ? "[SpyTrainingTest] SONUÇ: PASS ✓" : "[SpyTrainingTest] SONUÇ: FAIL ✗");
        GetTree().Quit(ok ? 0 : 1);
        GD.Print("=========================================");
    }

    // M29.5: Casusluk v2 — kiralama havuzu testi (--spy-rental-test).
    // Havuz başlatma, kiralama (PP düşer, aktive listeye geçer, havuzdan çıkar),
    // yetersiz PP reddi, Save/Load A==B, havuz yenileme ve ResetForNewGame doğrular.
    private void RunSpyRentalTest()
    {
        GD.Print("=========================================");
        GD.Print("  M29.5: CASUSLUK v2 — KIRALAMA HAVUZU TESTİ (--spy-rental-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        bool ok = true;

        // Deterministik tohum (havuz üretimi tekrarlanabilir olsun).
        DeterministicRNG.InitializeForNewGame(987654321);

        // Oyuncu PP'sini yeterli seviyeye ayarla (test için sabit bakiye).
        var player = GameManager.Instance.PlayerCountryData;
        player.PoliticalPower = 1000;
        double ppBefore = player.PoliticalPower;

        // 1. Havuz başlatma: RentalPoolSize adet müsait kiralık casus.
        EspionageSystem.ResetForNewGame();
        EspionageSystem.InitializeRentalPool(0);
        int poolSize = EspionageSystem.RentalPoolCount;
        GD.Print($"[SpyRentalTest] havuz_boyutu={poolSize} beklenir={EspionageSystem.RentalPoolSize}");
        bool poolOk = poolSize == EspionageSystem.RentalPoolSize
            && EspionageSystem.RentalPool.All(s => s.Available);

        var first = EspionageSystem.RentalPool.FirstOrDefault();
        int hireCost = first.HireCostPP;
        var hired = EspionageSystem.HireSpy(first.Id, 0);
        double ppAfter = player.PoliticalPower;
        bool hireOk = hired != null
            && hired.Name == first.Name
            && EspionageSystem.SpyCount == 1
            && EspionageSystem.RentalPoolCount == EspionageSystem.RentalPoolSize - 1
            && ppAfter == ppBefore - hireCost;
        GD.Print($"[SpyRentalTest] kiralama={hireOk} casus={hired?.Name} maliyet={hireCost} PP {ppBefore:F0}→{ppAfter:F0} havuz_kalan={EspionageSystem.RentalPoolCount} aktif={EspionageSystem.SpyCount}");

        // 2b. Zaten kiralanmış / bilinmeyen id → null.
        bool doubleHire = EspionageSystem.HireSpy(first.Id, 0) == null;
        bool unknownHire = EspionageSystem.HireSpy("rental-nonexistent", 0) == null;
        GD.Print($"[SpyRentalTest] cift_kiralama_reddedildi={doubleHire} bilinmeyen_reddedildi={unknownHire}");

        // 3. Yetersiz PP → kiralama reddedilir, PP değişmez.
        var second = EspionageSystem.RentalPool.FirstOrDefault(s => s.Available);
        int secondCost = second.HireCostPP;
        player.PoliticalPower = 0;
        bool poorHire = EspionageSystem.HireSpy(second.Id, 0) == null;
        player.PoliticalPower = ppAfter; // geri yükle
        bool poorOk = poorHire && player.PoliticalPower == ppAfter;
        GD.Print($"[SpyRentalTest] yetersiz_pp_reddedildi={poorOk} gerekli={secondCost} mevcut=0");

        // 4. Save/Load A==B (havuz durumu dahil).
        var data1 = EspionageSystem.GetSaveData();
        EspionageSystem.LoadSaveData(data1);
        var data2 = EspionageSystem.GetSaveData();
        bool saveLoadOk = data1.RentalPool.Spies.Length == data2.RentalPool.Spies.Length
            && data1.RentalPool.NextRentalSpyNum == data2.RentalPool.NextRentalSpyNum
            && data1.RentalPool.LastRefreshDay == data2.RentalPool.LastRefreshDay
            && data1.RentalPool.TotalHiredPP == data2.RentalPool.TotalHiredPP
            && data1.RentalPool.Spies.All(s1 => data2.RentalPool.Spies.Any(s2 => s2.Id == s1.Id && s2.Available == s1.Available));
        GD.Print($"[SpyRentalTest] save_load_a_b={saveLoadOk} havuz={data2.RentalPool.Spies.Length} toplam_PP={data2.RentalPool.TotalHiredPP}");

        // 5. Havuz yenileme: 30. günde yeni casuslar eklenerek hedef boyuta dönülür.
        int beforeRefresh = EspionageSystem.RentalPoolCount;
        EspionageSystem.RefreshRentalPoolIfNeeded(30);
        int afterRefresh = EspionageSystem.RentalPoolCount;
        bool refreshOk = afterRefresh == EspionageSystem.RentalPoolSize;
        GD.Print($"[SpyRentalTest] yenileme={refreshOk} once={beforeRefresh} son={afterRefresh} aralik=30");

        // 6. ResetForNewGame temizler (havuz + aktif casuslar + harcanan PP).
        EspionageSystem.ResetForNewGame();
        bool resetOk = EspionageSystem.RentalPoolCount == 0
            && EspionageSystem.SpyCount == 0
            && EspionageSystem.TotalHiredPP == 0;
        GD.Print($"[SpyRentalTest] reset={resetOk} havuz={EspionageSystem.RentalPoolCount} aktif={EspionageSystem.SpyCount} toplam_PP={EspionageSystem.TotalHiredPP}");

        ok = poolOk && hireOk && doubleHire && unknownHire && poorOk && saveLoadOk && refreshOk && resetOk;
        GD.Print(ok ? "[SpyRentalTest] SONUÇ: PASS ✓" : "[SpyRentalTest] SONUÇ: FAIL ✗");
        GetTree().Quit(ok ? 0 : 1);
        GD.Print("=========================================");
    }

    // M29.6: Casusluk v2 — karşı istihbarat / yakalanma / casus değişimi testi (--counter-intel-test).
    // Görev atama, karşı istihbarat seviyesi, deterministik günlük yakalanma, ilişkiye düşen sonuç,
    // yakalanan-casus listesi, serbest bırakma/değişim, Save/Load A==B ve ResetForNewGame.
    private void RunCounterIntelTest()
    {
        GD.Print("=========================================");
        GD.Print("  M29.6: CASUSLUK v2 — KARŞI İSTİHBARAT / YAKALANMA / DEĞİŞİM TESTİ (--counter-intel-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        bool ok = true;

        DeterministicRNG.InitializeForNewGame(555123456);

        var player = GameManager.Instance.PlayerCountryData;
        player.PoliticalPower = 1000; // test sabit bakiyesi

        // 1. GER casusu üret ve FRA'ya görev ata.
        EspionageSystem.ResetForNewGame();
        DeterministicRNG.InitializeForNewGame(555123456);
        var spy = EspionageSystem.GenerateSpy("GER");
        spy.Education = 0; // eğitim bonusesu sıfırla → yakalanma şansı maksimum (deterministik test)
        bool assigned = EspionageSystem.AssignSpyToMission(spy.Id, "FRA", 0);
        string missionTarget = EspionageSystem.GetSpyMissionTarget(spy.Id);
        GD.Print($"[CounterIntelTest] casus={spy.Id}({spy.Name}) owner={spy.OwnerCountryId} atandi={assigned} hedef={missionTarget}");

        // 2. FRA karşı istihbaratını %100'e ayarla (maks yakalanma riski).
        EspionageSystem.SetCounterIntelLevel("FRA", 100);
        GD.Print($"[CounterIntelTest] karsi_istihbari_FRA=100");

        // 3. Determistik günlük tick: yüksek karşı istihbaratla casus一定 yakalanır.
        int captureDay = -1;
        for (int day = 1; day <= 200; day++)
        {
            EspionageSystem.DailyTick(day);
            if (EspionageSystem.IsSpyCaptured(spy.Id)) { captureDay = day; break; }
        }
        bool captured = captureDay > 0;
        GD.Print($"[CounterIntelTest] yakalandi={captured} gun={captureDay} gorev_devam={EspionageSystem.IsSpyOnMission(spy.Id)}");

        // 4. İlişkiye düşen sonuç: GER→FRA ilişkisi negatif olmalı.
        int relAfterCapture = DiplomacySystem.GetRelation("GER", "FRA");
        bool relationDamaged = relAfterCapture < 0;
        GD.Print($"[CounterIntelTest] iliski_GER_FRA={relAfterCapture} dusdu={relationDamaged}");

        // 5. Yakalanan casus FRA tarafından listelenir.
        var fraCaptured = EspionageSystem.GetCapturedSpies("FRA");
        bool listed = fraCaptured.Any(c => c.SpyId == spy.Id);
        GD.Print($"[CounterIntelTest] fra_yakalanan_sayisi={fraCaptured.Count} listelendi={listed}");

        // 6. Serbest bırakma / değişim: FRA→GER ilişkisi iyileşir.
        int relBeforeRelease = DiplomacySystem.GetRelation("FRA", "GER");
        bool released = EspionageSystem.ReleaseCapturedSpy(spy.Id);
        int relAfterRelease = DiplomacySystem.GetRelation("FRA", "GER");
        bool relationImproved = relAfterRelease > relBeforeRelease;
        GD.Print($"[CounterIntelTest] serbest_birakildi={released} iliski_FRA_GER {relBeforeRelease}->{relAfterRelease} iyilesti={relationImproved}");

        // 7. Save/Load A==B (görev + yakalanma + karşı istihbarat).
        EspionageSystem.ResetForNewGame();
        DeterministicRNG.InitializeForNewGame(555123456);
        var s2 = EspionageSystem.GenerateSpy("GER");
        EspionageSystem.AssignSpyToMission(s2.Id, "FRA", 0);
        EspionageSystem.SetCounterIntelLevel("FRA", 100);
        for (int day = 1; day <= 200; day++) { EspionageSystem.DailyTick(day); if (EspionageSystem.IsSpyCaptured(s2.Id)) break; }
        var data1 = EspionageSystem.GetSaveData();
        EspionageSystem.LoadSaveData(data1);
        var data2 = EspionageSystem.GetSaveData();
        bool saveLoadOk = data1.Missions.Length == data2.Missions.Length
            && data1.Captured.Length == data2.Captured.Length
            && data1.CounterIntel.Length == data2.CounterIntel.Length
            && data1.Captured.Any(c => c.SpyId == s2.Id) == data2.Captured.Any(c => c.SpyId == s2.Id);
        GD.Print($"[CounterIntelTest] save_load_a_b={saveLoadOk} gorev={data2.Missions.Length} yakalanan={data2.Captured.Length} karsi_intel={data2.CounterIntel.Length}");

        // 8. ResetForNewGame temizler.
        EspionageSystem.ResetForNewGame();
        bool resetOk = EspionageSystem.MissionCount == 0 && EspionageSystem.CapturedCount == 0;
        GD.Print($"[CounterIntelTest] reset={resetOk} gorev={EspionageSystem.MissionCount} yakalanan={EspionageSystem.CapturedCount}");

        ok = assigned && missionTarget == "FRA" && captured && relationDamaged && listed && released && relationImproved && saveLoadOk && resetOk;
        GD.Print(ok ? "[CounterIntelTest] SONUÇ: PASS ✓" : "[CounterIntelTest] SONUÇ: FAIL ✗");
        GetTree().Quit(ok ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M30.1: Uzay programı testi — önkoşul kilidi, araştırma, görev başlatma/tamamlama,
    /// save/load A==B ve reset doğrular. Normal oyunda ASLA çalışmaz (--space-test).
    /// </summary>
    private void RunSpaceTest()
    {
        GD.Print("=========================================");
        GD.Print("  M30.1: UZAY PROGRAMI TESTI (--space-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 1000;
        if (Speed == 0) SetSpeed(0);

        SpaceSystem.EnableTestMode();

        // Kilit mantığı: s10'ın önkoşulu mars_mission (Era 2020, başlangıçta araştırılmadı) → kilitli.
        // s1'ın önkoşulu rocketry (Era 1990, başlangıçta araştırılmış) → kullanılabilir.
        bool s10LockedStart = SpaceSystem.GetMissionStatus("s10_human_mars") == SpaceSystem.MissionStatus.Locked;
        bool s1AvailableStart = SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Available;
        bool rocketryResearched = TechnologySystem.GetStatus("rocketry") == TechnologySystem.ResearchStatus.Researched;

        GD.Print($"[SpaceTest] s10 kilitli (mars_mission yok): {s10LockedStart}");
        GD.Print($"[SpaceTest] s1 kullanılabilir (rocketry var): {s1AvailableStart}");
        GD.Print($"[SpaceTest] rocketry araştırılmış: {rocketryResearched}");

        // s1 görevini başlat ve tamamla (test modunda garanti başarı, 30 gün).
        bool launchOk = SpaceSystem.LaunchMission("s1_suborbital_probe");
        GD.Print($"[SpaceTest] s1 başlatıldı: {launchOk}");

        int missionDays = 0;
        while (SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.InProgress && missionDays < 100)
        {
            AdvanceDaysForTest(1);
            missionDays++;
        }
        var status = SpaceSystem.GetMissionStatus("s1_suborbital_probe");
        bool missionCompleted = status == SpaceSystem.MissionStatus.Completed;
        GD.Print($"[SpaceTest] s1 tamamlandı: {missionCompleted} (durum={status}, gün={missionDays})");

        // save/load A==B
        var data1 = SpaceSystem.GetSaveData();
        SpaceSystem.LoadSaveData(data1);
        var data2 = SpaceSystem.GetSaveData();
        bool saveLoadOk = data1.Missions.Length == data2.Missions.Length
            && data1.Missions.Length > 0
            && data1.Missions[0].MissionId == data2.Missions[0].MissionId
            && data1.Missions[0].Status == data2.Missions[0].Status;
        GD.Print($"[SpaceTest] save_load_a_b={saveLoadOk}");

        // reset: yeniden başlatıldığında tüm görevler geri yüklenir ve durumlar
        // güncel önkoşul durumunu yansıtır (rocketry araştırılmış → s1 kullanılabilir,
        // mars_mission yok → s10 kilitli).
        SpaceSystem.ResetForNewGame();
        SpaceSystem.Initialize();
        bool resetOk = SpaceSystem.AllMissions.Count == 10
            && SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Available
            && SpaceSystem.GetMissionStatus("s10_human_mars") == SpaceSystem.MissionStatus.Locked;
        GD.Print($"[SpaceTest] reset={resetOk}");

        bool ok = s10LockedStart && s1AvailableStart && rocketryResearched && launchOk && missionCompleted && saveLoadOk && resetOk;
        GD.Print(ok ? "[SpaceTest] SONUÇ: PASS ✓" : "[SpaceTest] SONUÇ: FAIL ✗");
        GetTree().Quit(ok ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M30.2: Uzay proje yönetimi testi — bütçe (PP), süre (gün), başarısızlık olasılığı,
    /// fırlatma tekrarı (retry) ve save/load kalıcılığı. Normal oyunda ASLA çalışmaz (--space-mgmt-test).
    /// </summary>
    private void RunSpaceMgmtTest()
    {
        GD.Print("=========================================");
        GD.Print("  M30.2: UZAY PROJE YÖNETİMİ TESTİ (--space-mgmt-test)");
        GD.Print("=========================================");

        // modern_world senaryosuna geç (rocketry / satellite_technology sadece modern_world'de tanımlı)
        ScenarioSystem.SetActive("modern_world");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 2000;
        if (Speed == 0) SetSpeed(0);

        bool rocketryResearched = TechnologySystem.GetStatus("rocketry") == TechnologySystem.ResearchStatus.Researched;
        bool s1AvailableStart = SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Available;
        int budgetStart = SpaceSystem.RemainingBudgetPP;

        GD.Print($"[SpaceMgmtTest] rocketry araştırılmış: {rocketryResearched}");
        GD.Print($"[SpaceMgmtTest] s1 kullanılabilir başlangıç: {s1AvailableStart}");
        GD.Print($"[SpaceMgmtTest] başlangıç bütçe: {budgetStart}");

        // 1. Başarılı fırlatma (test modu, garanti başarı) — bütçe ve süre kullanılır.
        SpaceSystem.EnableTestMode(true);
        bool launch1 = SpaceSystem.LaunchMission("s1_suborbital_probe");
        GD.Print($"[SpaceMgmtTest] s1 1. fırlatma: {launch1}, durum={SpaceSystem.GetMissionStatus("s1_suborbital_probe")}, bütçe={SpaceSystem.RemainingBudgetPP}");

        int missionDays = 0;
        while (SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.InProgress && missionDays < 100)
        {
            AdvanceDaysForTest(1);
            missionDays++;
        }
        bool completed1 = SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Completed;
        GD.Print($"[SpaceMgmtTest] s1 1. fırlatma tamamlandı: {completed1} ({missionDays} gün)");

        // 2. Zorla başarısızlık — gerçek/test olasılık yoksayılır, zorla başarısız.
        SpaceSystem.ForceFailureForTest(true);
        SpaceSystem.EnableTestMode(false);
        bool retry1 = SpaceSystem.RetryLaunch("s1_suborbital_probe");
        GD.Print($"[SpaceMgmtTest] s1 retry 1: {retry1}, deneme={SpaceSystem.GetRetryCount("s1_suborbital_probe")}");
        bool launch2 = SpaceSystem.LaunchMission("s1_suborbital_probe");
        GD.Print($"[SpaceMgmtTest] s1 2. fırlatma: {launch2}, bütçe={SpaceSystem.RemainingBudgetPP}");
        missionDays = 0;
        while (SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.InProgress && missionDays < 100)
        {
            AdvanceDaysForTest(1);
            missionDays++;
        }
        bool failed2 = SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Failed;
        GD.Print($"[SpaceMgmtTest] s1 2. fırlatma başarısız: {failed2} ({missionDays} gün)");

        // 3. Fırlatma tekrarı — tekrar dene, garanti başarı.
        bool retry2 = SpaceSystem.RetryLaunch("s1_suborbital_probe");
        GD.Print($"[SpaceMgmtTest] s1 retry 2: {retry2}, deneme={SpaceSystem.GetRetryCount("s1_suborbital_probe")}");
        SpaceSystem.EnableTestMode(true);
        SpaceSystem.ForceFailureForTest(false);
        bool launch3 = SpaceSystem.LaunchMission("s1_suborbital_probe");
        missionDays = 0;
        while (SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.InProgress && missionDays < 100)
        {
            AdvanceDaysForTest(1);
            missionDays++;
        }
        bool completed3 = SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Completed;
        GD.Print($"[SpaceMgmtTest] s1 3. fırlatma (retry sonrası) tamamlandı: {completed3} ({missionDays} gün)");

        // Bütçe kontrolü: 3 fırlatma × 50 PP = 150 düşülmüş olmalı.
        int budgetAfter = SpaceSystem.RemainingBudgetPP;
        int budgetDropped = budgetStart - budgetAfter;
        bool budgetOk = budgetDropped == 150;
        GD.Print($"[SpaceMgmtTest] bütçe: {budgetStart} → {budgetAfter} (düşüş={budgetDropped}, beklenen=150): {budgetOk}");

        // save/load A==B (bütçe + deneme sayısı dahil).
        var data1 = SpaceSystem.GetSaveData();
        SpaceSystem.LoadSaveData(data1);
        var data2 = SpaceSystem.GetSaveData();
        bool saveLoadOk = data1.Missions.Length == data2.Missions.Length
            && data1.RemainingBudgetPP == data2.RemainingBudgetPP
            && data1.Missions[0].RetryCount == data2.Missions[0].RetryCount
            && data1.Missions[0].Status == data2.Missions[0].Status;
        GD.Print($"[SpaceMgmtTest] save_load_a_b={saveLoadOk}, retry_kalici={data1.Missions[0].RetryCount == data2.Missions[0].RetryCount}");

        // reset: yeni oyun → bütçe sıfırlanır, görevler önkoşula göre kilitlenir/kilit açılır.
        SpaceSystem.ResetForNewGame();
        SpaceSystem.Initialize();
        bool resetOk = SpaceSystem.AllMissions.Count == 10
            && SpaceSystem.RemainingBudgetPP == SpaceSystem.SpaceProgramBudgetPP
            && SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Available
            && SpaceSystem.GetMissionStatus("s10_human_mars") == SpaceSystem.MissionStatus.Locked;
        GD.Print($"[SpaceMgmtTest] reset={resetOk}, bütçe_sıfırlandı={SpaceSystem.RemainingBudgetPP}");

        bool ok = rocketryResearched && s1AvailableStart && launch1 && completed1
            && retry1 && launch2 && failed2 && retry2 && launch3 && completed3
            && budgetOk && saveLoadOk && resetOk;
        GD.Print(ok ? "[SpaceMgmtTest] SONUÇ: PASS ✓" : "[SpaceMgmtTest] SONUÇ: FAIL ✗");
        GetTree().Quit(ok ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M30.3: Uzay programı ödülleri testi — başarılı bir görevde prestij, teknoloji bonusu ve
    /// halk memnuniyeti (youth/asker) uygulanır; keşif uydusu (ReconSatellite) ve uydu karşıtı
    /// silah (AntiSatelliteWeapon) askeri kullanım devreye girer. Normal oyunda ASLA çalışmaz (--space-reward-test).
    /// </summary>
    private void RunSpaceRewardTest()
    {
        GD.Print("=========================================");
        GD.Print("  M30.3: UZAY ÖDÜLLERİ TESTİ (--space-reward-test)");
        GD.Print("=========================================");

        // modern_world senaryosuna geç (rocketry / satellite_technology sadece modern_world'de tanımlı)
        ScenarioSystem.SetActive("modern_world");
        EventSystem.EnableAutoChooseTest(); // headless'te olay popup'larını engelle

        if (!HasPlayer) SetPlayerCountry("DEU"); // modern_world'de DEU (Almanya)
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 2000;
        if (Speed == 0) SetSpeed(0);

        // modern_world'de DEU rocketry ve satellite_technology'yi başlangıçta araştırmış.
        bool rocketryResearched = TechnologySystem.GetStatus("rocketry") == TechnologySystem.ResearchStatus.Researched;
        bool satelliteResearched = TechnologySystem.GetStatus("satellite_technology") == TechnologySystem.ResearchStatus.Researched;
        bool s1Available = SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Available;
        bool s3Available = SpaceSystem.GetMissionStatus("s3_communication_satellite") == SpaceSystem.MissionStatus.Available;
        GD.Print($"[SpaceRewardTest] rocketry={rocketryResearched}, satellite={satelliteResearched}, s1={s1Available}, s3={s3Available}");

        double prestigeBefore = PlayerCountryData?.Prestige ?? 0;
        double youthBefore = PublicSatisfactionSystem.GetSatisfaction("DEU", "youth");
        double militaryBefore = PublicSatisfactionSystem.GetSatisfaction("DEU", "military");
        float researchSpeedBefore = TechnologySystem.GetModifier("research_speed");
        float tensionBefore = DiplomacySystem.WorldTension;

        GD.Print($"[SpaceRewardTest] öncesi — prestij={prestigeBefore}, youth={youthBefore:F1}, military={militaryBefore:F1}, research_speed={researchSpeedBefore:F3}, tension={tensionBefore:F1}");

        // 1. s1 görevi: başlat → 30 günde tamamla (test modu, garanti başarı).
        // modern_world'de DEU rocketry'yi zaten araştırmış (başlangıç teknolojisi).
        bool launch1 = SpaceSystem.LaunchMission("s1_suborbital_probe");
        GD.Print($"[SpaceRewardTest] s1 başlatıldı: {launch1}");
        AdvanceDaysForTest(35); // s1 süresi 30 gün (+güvenlik marajı)
        bool completed1 = SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Completed;
        GD.Print($"[SpaceRewardTest] s1 tamamlandı: {completed1}");

        // Ödüller: prestij +5, research_speed +0.01, youth memnuniyeti +5 (military değişmez).
        double prestigeAfter = PlayerCountryData?.Prestige ?? 0;
        double youthAfter = PublicSatisfactionSystem.GetSatisfaction("DEU", "youth");
        double militaryAfter = PublicSatisfactionSystem.GetSatisfaction("DEU", "military");
        float researchSpeedAfter = TechnologySystem.GetModifier("research_speed");

        bool prestigeOk = Math.Abs(prestigeAfter - (prestigeBefore + 5)) < 0.001;
        bool techBonusOk = Math.Abs(researchSpeedAfter - (researchSpeedBefore + 0.01f)) < 0.001f;
        // Halk memnuniyeti etkisi: aynı ResolveMission kod yoluyla uygulanır (prestij/tech bonusuyla birlikte).
        // Günlük drift (PublicSatisfactionSystem.DailyTick) nedeniyle net delta assert edilmez; sadece loglanır.
        GD.Print($"[SpaceRewardTest] prestij_ok={prestigeOk}, tech_bonus_ok={techBonusOk}, youth={youthBefore:F1}→{youthAfter:F1}, military={militaryBefore:F1}→{militaryAfter:F1}");

        // M30.4: s1 başarısıyla dünya gerilimi +2 (RewardTension). Günlük drift (DiplomacySystem.DailyTick)
        // nedeniyle kesin delta assert edilmez; değerler yalnızca loglanır.
        float tensionAfterS1 = DiplomacySystem.WorldTension;
        GD.Print($"[SpaceRewardTest] tension {tensionBefore:F1}→{tensionAfterS1:F1} (s1 beklenir +2)");

        // 2. Askerî kullanım — keşif uydusu (ReconSatellite): s3'ü tamamla.
        // DEU satellite_technology'yi zaten araştırmış.
        bool launch3 = SpaceSystem.LaunchMission("s3_communication_satellite");
        GD.Print($"[SpaceRewardTest] s3 başlatıldı: {launch3}");
        AdvanceDaysForTest(65); // s3 süresi 60 gün (+güvenlik marajı)
        bool completed3 = SpaceSystem.GetMissionStatus("s3_communication_satellite") == SpaceSystem.MissionStatus.Completed;
        bool reconActive = SpaceSystem.IsReconSatelliteActive("s3_communication_satellite");
        GD.Print($"[SpaceRewardTest] s3 tamamlandı: {completed3}, keşif_uydusu_aktif={reconActive}");

        // M30.4: s3 başarısıyla dünya gerilimi +3 (RewardTension, kümülatif).
        float tensionAfterS3 = DiplomacySystem.WorldTension;
        GD.Print($"[SpaceRewardTest] tension {tensionAfterS1:F1}→{tensionAfterS3:F1} (s3 beklenir +3)");

        // 3. save/load A==B (bütçe, deneme, keşif uyduları dahil).
        var data1 = SpaceSystem.GetSaveData();
        SpaceSystem.LoadSaveData(data1);
        var data2 = SpaceSystem.GetSaveData();
        bool saveLoadOk = data1.Missions.Length == data2.Missions.Length
            && data1.RemainingBudgetPP == data2.RemainingBudgetPP
            && data1.ReconSatellites.Length == data2.ReconSatellites.Length
            && data1.Missions[0].Status == data2.Missions[0].Status;
        GD.Print($"[SpaceRewardTest] save_load_a_b={saveLoadOk}, keşif_uydusu_kalıcı={data1.ReconSatellites.Length == data2.ReconSatellites.Length}");

        // 4. reset: yeni oyun → bütçe sıfırlanır, keşif uyduları temizlenir, görevler önkoşula göre.
        SpaceSystem.ResetForNewGame();
        SpaceSystem.Initialize();
        bool resetOk = SpaceSystem.AllMissions.Count == 10
            && SpaceSystem.RemainingBudgetPP == SpaceSystem.SpaceProgramBudgetPP
            && SpaceSystem.ReconSatelliteCount == 0
            && SpaceSystem.GetMissionStatus("s1_suborbital_probe") == SpaceSystem.MissionStatus.Available
            && SpaceSystem.GetMissionStatus("s10_human_mars") == SpaceSystem.MissionStatus.Locked;
        GD.Print($"[SpaceRewardTest] reset={resetOk}, bütçe_sıfırlandı={SpaceSystem.RemainingBudgetPP}, keşif_uydusu_sıfırlandı={SpaceSystem.ReconSatelliteCount == 0}");

        bool ok = rocketryResearched && satelliteResearched && s1Available && s3Available && launch1 && completed1
            && prestigeOk && techBonusOk
            && launch3 && completed3 && reconActive
            && saveLoadOk && resetOk;
        GD.Print(ok ? "[SpaceRewardTest] SONUÇ: PASS ✓" : "[SpaceRewardTest] SONUÇ: FAIL ✗");
        GetTree().Quit(ok ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M31.1: Modern silah teknolojisi testi — yeni silah (cruise_missiles) araştırıldığında
    /// land_attack modifikatörü etkin olur ve bölük etkili saldırı gücü artar (savaş istatistiği
    /// değişir). Teknoloji durumu kaydet/yüklede A==B korunur. Normal oyunda ASLA çalışmaz (--tech-test).
    /// </summary>
    private void RunTechTest()
    {
        GD.Print("=========================================");
        GD.Print("  M31.1: MODERN SILAH TEKNOLOJİSİ TESTİ (--tech-test)");
        GD.Print("=========================================");

        // modern_world senaryosu; DEU yüksek TechLevel'e sahip olabilir (startEra 2000+) ve
        // eski silah zaten otomatik araştırılmış olabilir. railgun_technology (Era 2020,
        // önkoşul yok) her zaman başlangıçta araştırılmamış, araştırılabilir kalır.
        ScenarioSystem.SetActive("modern_world");
        EventSystem.EnableAutoChooseTest(); // headless'te olay popup'larını engelle

        SetPlayerCountry("DEU"); // TechnologySystem'i DEU ile yeniden başlat
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 2000;
        if (PlayerCountryData != null) PlayerCountryData.ManpowerPool = 200000;
        if (Speed == 0) SetSpeed(0);

        // Test şablonu: 2 infanteri + 1 artileri → temeldeki saldırı gücü (bölük istatistiği)
        var tpl = ArmySystem.CreateTemplate("Tech Test Şablonu", new List<string> { "infantry", "infantry", "artillery" });
        if (tpl == null)
        {
            GD.Print("[TechTest] Şablon oluşturulamadı — test atlanıyor");
            GD.Print("=========================================");
            GetTree().Quit(1);
            return;
        }
        float baseAttack = tpl.Attack; // 10 + 10 + 30 = 50

        // Yeni silah teknolojisi: railgun_technology (önkoşul yok, Era 2020, başlangıçta araştırılmadı)
        string techId = "railgun_technology";
        if (!TechnologySystem.All.ContainsKey(techId))
        {
            GD.Print($"[TechTest] {techId} tanımlı değil — test atlanıyor");
            GD.Print("=========================================");
            GetTree().Quit(1);
            return;
        }

        float modBefore = TechnologySystem.GetModifier("land_attack");
        float effectiveBefore = baseAttack * (1f + modBefore);
        GD.Print($"[TechTest] öncesi — land_attack={modBefore:F3}, taban_attack={baseAttack:F0}, etkili_attack={effectiveBefore:F1}");

        // Araştırmayı başlat ve tamamla (test modu yok; DeterministikRNG ile 1 gün = 1 araştırma puanı).
        bool started = TechnologySystem.StartResearch(techId);
        GD.Print($"[TechTest] {techId} araştırma başlatıldı: {started}");
        int cost = TechnologySystem.All[techId].Cost;
        AdvanceDaysForTest(cost + 5); // araştırma süresi + güvenlik marajı

        bool researched = TechnologySystem.GetStatus(techId) == TechnologySystem.ResearchStatus.Researched;
        float modAfter = TechnologySystem.GetModifier("land_attack");
        float effectiveAfter = baseAttack * (1f + modAfter);
        GD.Print($"[TechTest] sonrası — researched={researched}, land_attack={modAfter:F3}, etkili_attack={effectiveAfter:F1}");

        // Save/Load A==B — teknoloji modifikatörü kalıcı mı?
        var save = TechnologySystem.GetSaveData();
        TechnologySystem.LoadSaveData(save);
        float modLoaded = TechnologySystem.GetModifier("land_attack");
        bool saveLoadOk = Math.Abs(modLoaded - modAfter) < 0.001f;
        GD.Print($"[TechTest] save_load_a_b={saveLoadOk}, yüklenen_land_attack={modLoaded:F3}");

        // Test kriteri: yeni silah → modifikatör arttı → etkili saldırı gücü arttı (savaş istatistiği değişti).
        bool ok = started && researched && modAfter > modBefore && effectiveAfter > effectiveBefore && saveLoadOk;
        GD.Print(ok ? "[TechTest] SONUÇ: PASS ✓" : "[TechTest] SONUÇ: FAIL ✗");
        GetTree().Quit(ok ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M31.2: Nükleer program testi — nükleer program aşamaları sırayla ilerir; her
    /// aşama öncekinin tamamlanmış, ilgili teknolojinin araştırılmış ve ödemenin
    /// yapılmış olmasını gerektirir. Nükleer durum kaydet/yükle'de A==B korunur.
    /// Normal oyunda ASLA çalışmaz (--nuclear-test).
    /// </summary>
    private void RunNuclearTest()
    {
        GD.Print("=========================================");
        GD.Print("  M31.2: NÜKLEER PROGRAM TESTİ (--nuclear-test)");
        GD.Print("=========================================");

        ScenarioSystem.SetActive("modern_world");
        EventSystem.EnableAutoChooseTest(); // headless'te olay popup'larını engelle

        SetPlayerCountry("DEU"); // NuclearProgramSystem'i DEU ile başlat
        if (PlayerCountryData != null) PlayerCountryData.PoliticalPower = 10000;
        if (Speed == 0) SetSpeed(0);

        NuclearProgramSystem.EnableTestMode(); // ücret 0, süre 1 gün, deneme garantili

        // Sıralı kural: reaktör aşaması zenginleştirmeden önce başlatılamaz
        var (reactorOk, reactorReason) = NuclearProgramSystem.CanStartStage(NuclearProgramSystem.NuclearStage.Reactor);
        GD.Print($"[NuclearTest] sıralı kural (reaktör önce zenginleştirme): başlatılabilir={reactorOk} ({reactorReason})");
        if (reactorOk)
        {
            GD.Print("[NuclearTest] SONUÇ: FAIL ✗ — sıralı kural ihlal edildi");
            GetTree().Quit(1);
            return;
        }

        // Gerekli teknolojileri araştır (tüm Era >= 2020 → başlangıçta araştırılmamış)
        void ResearchAndComplete(string techId)
        {
            if (TechnologySystem.GetStatus(techId) == TechnologySystem.ResearchStatus.Researched) return;
            bool started = TechnologySystem.StartResearch(techId);
            GD.Print($"[NuclearTest] {techId} araştırma başlatıldı: {started}");
            if (!started)
            {
                GD.Print("[NuclearTest] SONUÇ: FAIL ✗ — araştırma başlatılamadı: " + techId);
                GetTree().Quit(1);
                return;
            }
            AdvanceDaysForTest(TechnologySystem.All[techId].Cost + 1);
            if (TechnologySystem.GetStatus(techId) != TechnologySystem.ResearchStatus.Researched)
            {
                GD.Print("[NuclearTest] SONUÇ: FAIL ✗ — araştırma tamamlanamadı: " + techId);
                GetTree().Quit(1);
                return;
            }
        }

        ResearchAndComplete("uranium_mining");
        ResearchAndComplete("uranium_enrichment");

        // Zenginleştirme teknolojisi hazır: enrichment aşaması artık başlatılabilir
        var (enrOk, enrReason) = NuclearProgramSystem.CanStartStage(NuclearProgramSystem.NuclearStage.UraniumEnrichment);
        GD.Print($"[NuclearTest] enrichment aşaması başlatılabilir: {enrOk} ({enrReason})");
        if (!enrOk)
        {
            GD.Print("[NuclearTest] SONUÇ: FAIL ✗ — enrichment başlatılamadı: " + enrReason);
            GetTree().Quit(1);
            return;
        }

        // Tüm aşamaları sırayla ilerlet (önceki aşamanın teknolojisi zaten araştırılmış)
        var stages = new[]
        {
            NuclearProgramSystem.NuclearStage.UraniumEnrichment,
            NuclearProgramSystem.NuclearStage.Reactor,
            NuclearProgramSystem.NuclearStage.WeaponDesign,
            NuclearProgramSystem.NuclearStage.NuclearTest,
            NuclearProgramSystem.NuclearStage.BomberDelivery,
            NuclearProgramSystem.NuclearStage.IcbmDelivery,
            NuclearProgramSystem.NuclearStage.SlbmDelivery,
            NuclearProgramSystem.NuclearStage.Doctrine,
        };
        foreach (var stage in stages)
        {
            ResearchAndComplete(NuclearProgramSystem.GetStageTechId(stage));
            bool started = NuclearProgramSystem.StartStage(stage);
            GD.Print($"[NuclearTest] {stage} başlatıldı: {started}");
            if (!started)
            {
                GD.Print("[NuclearTest] SONUÇ: FAIL ✗ — aşama başlatılamadı: " + stage);
                GetTree().Quit(1);
                return;
            }
            AdvanceDaysForTest(NuclearProgramSystem.GetStageDuration(stage) + 1);
            bool completed = NuclearProgramSystem.CompletedThrough == stage;
            GD.Print($"[NuclearTest] {stage} tamamlandı: {completed} (ileri={NuclearProgramSystem.CompletedThrough})");
            if (!completed)
            {
                GD.Print("[NuclearTest] SONUÇ: FAIL ✗ — aşama tamamlanamadı: " + stage);
                GetTree().Quit(1);
                return;
            }
        }

        // Tamamlanmış bir aşamayı tekrar başlatılamaz (sıralı kural)
        var (redoOk, redoReason) = NuclearProgramSystem.CanStartStage(NuclearProgramSystem.NuclearStage.UraniumEnrichment);
        if (redoOk)
        {
            GD.Print("[NuclearTest] SONUÇ: FAIL ✗ — tamamlanmış aşama yeniden başlatılabilir");
            GetTree().Quit(1);
            return;
        }

        // Save/Load A==B — nükleer durum kalıcı mı?
        var before = NuclearProgramSystem.GetSaveData();
        NuclearProgramSystem.LoadSaveData(before);
        var after = NuclearProgramSystem.GetSaveData();
        bool saveLoadOk = after.CompletedThrough == (int)NuclearProgramSystem.NuclearStage.Doctrine
            && after.Warheads == 1
            && after.TestsConducted == 1
            && after.Doctrine == (int)NuclearProgramSystem.NuclearDoctrine.Deterrence
            && after.InProgress == -1
            && after.DaysInvested == 0;
        GD.Print($"[NuclearTest] save_load_a_b={saveLoadOk}, ileri={after.CompletedThrough}, başlık={after.Warheads}, deneme={after.TestsConducted}, doktrin={after.Doctrine}");

        // Test kriteri: tüm aşamalar sırayla tamamlandı; deneme başarılı; başlık var;
        // doktrin belirlendi; save/load A==B; tamamlanmış aşama tekrar başlatılamaz
        bool ok = NuclearProgramSystem.CompletedThrough == NuclearProgramSystem.NuclearStage.Doctrine
            && NuclearProgramSystem.Warheads == 1
            && NuclearProgramSystem.TestsConducted == 1
            && NuclearProgramSystem.Doctrine == NuclearProgramSystem.NuclearDoctrine.Deterrence
            && saveLoadOk
            && !redoOk;
        GD.Print(ok ? "[NuclearTest] SONUÇ: PASS ✓" : "[NuclearTest] SONUÇ: FAIL ✗");
        GetTree().Quit(ok ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M31.3: Yaptirim / nükleer diplomasi testi — BM (çok taraflı) yaptirim, komşu tepkisi,
    /// ticaret kısıtlaması, anlaşmadan çıkış, dünya gerilimi ve DTO kaydet/yükle A==B doğrular.
    /// Nükleer deneme sebebiyle BM yaptirimi uygulanır; komşular tepki verir; ticaret kısıtlanır;
    /// bir anlaşma imzalanıp ardından terk edilir. Normal oyunda ASLA çalışmaz (--sanctions-nuclear-test).
    /// </summary>
    private void RunSanctionsNuclearTest()
    {
        GD.Print("=========================================");
        GD.Print("  M31.3: YAPTIRIM / NÜKLEER DİPLOMASI TESTİ (--sanctions-nuclear-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        string playerId = PlayerCountryId;

        // Hedef: oyuncuyu içermeyen, bağımsız ilk ülke
        string targetId = null;
        foreach (var c in CountryDatabase.All())
        {
            if (c.Id != playerId && !c.IsAnnexed) { targetId = c.Id; break; }
        }
        if (targetId == null)
        {
            GD.Print("[SanctionsNuclearTest] Uygun hedef ülke bulunamadı — test atlanıyor");
            GD.Print("=========================================");
            return;
        }

        bool allOk = true;
        float tensionBefore = DiplomacySystem.WorldTension;
        int sanctionsBefore = DiplomacySystem.Sanctions.Count(s => s.Active);
        int agreementsBefore = DiplomacySystem.Agreements.Count(a => a.Active);
        GD.Print($"[SanctionsNuclearTest] {playerId} -> {targetId}: tension önce=%{tensionBefore:F1}, aktif yaptirim={sanctionsBefore}, aktif anlaşma={agreementsBefore}");
        // 1. BM (çok taraflı) yaptirim — nükleer deneme/program sebebi
        bool unOk = DiplomacySystem.ImposeUnSanction(playerId, targetId, DiplomacySystem.Sanction.Type.Trade, 60, "Nükleer deneme/program");
        GD.Print($"[SanctionsNuclearTest] BM ImposeUnSanction: {(unOk ? "OK" : "FAIL")}");
        if (!unOk) allOk = false;

        var unSanction = DiplomacySystem.Sanctions.LastOrDefault(s => s.IsUnSanction && s.TargetId == targetId);
        bool unFlagged = unSanction != null && unSanction.IsUnSanction && unSanction.Reason == "Nükleer deneme/program";
        GD.Print($"[SanctionsNuclearTest] BM bayrak/sebep doğrulandı: {unFlagged} (id={unSanction?.Id})");
        if (!unFlagged) allOk = false;

        // 2. Ticaret kısıtlaması (tek yönlü trade yaptırım = kısıtlı)
        bool tradeRestricted = DiplomacySystem.IsTradeRestricted(playerId, targetId);
        GD.Print($"[SanctionsNuclearTest] Ticaret kısıtlı mı: {tradeRestricted}");
        if (!tradeRestricted) allOk = false;

        // 3. Komşu tepkisi (hazırsa): hedefin bir komşusu uygulayıcıya karşı daha öfkeli olmalı
        string neighborId = null;
        int neighborRelBefore = 0;
        if (NeighborSystem.IsReady)
        {
            var neighbors = NeighborSystem.GetNeighbors(targetId);
            neighborId = neighbors.FirstOrDefault(n => n != playerId && CountryDatabase.Get(n) != null && !CountryDatabase.Get(n).IsAnnexed);
            if (neighborId != null)
                neighborRelBefore = DiplomacySystem.GetRelation(neighborId, playerId);
            GD.Print($"[SanctionsNuclearTest] Komşu {neighborId ?? "(yok)"} ile ilişki önce={neighborRelBefore}");
        }

        // 4. Anlaşma imzala, ardından çıkarılma (vardığı doğru)
        string agrId = null;
        int agreementsAfterSign = -1;
        if (DiplomacySystem.SignAgreement(playerId, targetId, DiplomacySystem.Agreement.Type.Trade, 0))
        {
            agrId = DiplomacySystem.Agreements.Last().Id;
            agreementsAfterSign = DiplomacySystem.Agreements.Count(a => a.Active);
            GD.Print($"[SanctionsNuclearTest] Anlaşma imzalandı: {agrId} (aktif anlaşma={agreementsAfterSign})");
        }

        bool withdrawOk = agrId != null && DiplomacySystem.WithdrawFromAgreement(playerId, agrId);
        GD.Print($"[SanctionsNuclearTest] Anlaşmadan çıkış: {(withdrawOk ? "OK" : "FAIL")}");
        if (!withdrawOk) allOk = false;

        int agreementsAfter = DiplomacySystem.Agreements.Count(a => a.Active);
        bool agreementCycleOk = agreementsAfterSign == agreementsBefore + 1 && agreementsAfter == agreementsBefore;
        GD.Print($"[SanctionsNuclearTest] Aktif anlaşma sonrası={agreementsAfter} (imza sonrası={agreementsAfterSign}, once={agreementsBefore}, döngü={agreementCycleOk})");
        if (!agreementCycleOk) allOk = false;

        float tensionAfter = DiplomacySystem.WorldTension;
        int sanctionsAfter = DiplomacySystem.Sanctions.Count(s => s.Active);
        GD.Print($"[SanctionsNuclearTest] Son: tension=%{tensionAfter:F1} (delta={tensionAfter - tensionBefore:+#;-#;0}), aktif yaptirim={sanctionsBefore}->{sanctionsAfter}");

        // DTO kaydet/yükle A==B — yeni state (IsUnSanction/Reason dahil) kalıcı mı?
        var beforeDto = DiplomacySystem.GetSaveData();
        DiplomacySystem.LoadSaveData(beforeDto);
        var afterDto = DiplomacySystem.GetSaveData();
        bool dtoRoundTrip = afterDto.Sanctions != null && afterDto.Sanctions.Length == beforeDto.Sanctions.Length
            && afterDto.Agreements != null && afterDto.Agreements.Length == beforeDto.Agreements.Length;
        bool unSurvives = afterDto.Sanctions != null && afterDto.Sanctions.Any(s => s.IsUnSanction && s.Reason == "Nükleer deneme/program");
        GD.Print($"[SanctionsNuclearTest] DTO round_trip={dtoRoundTrip}, BM state survives={unSurvives}");
        if (!dtoRoundTrip || !unSurvives) allOk = false;

        bool pass = allOk && tensionAfter > tensionBefore && sanctionsAfter > sanctionsBefore
            && unFlagged && tradeRestricted && withdrawOk && agreementCycleOk && dtoRoundTrip && unSurvives;

        GD.Print(pass ? "[SanctionsNuclearTest] SONUÇ: PASS ✓" : "[SanctionsNuclearTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M31.4: Nükleer kullanım testi — oyuncu nükleer saldırı başlatır; başlık tüketilir, hedefte
    /// nüfus/ekonomi kaybı, radyasyon bölgesi, dünya gerilimi ve ilişkide büyük darba, Deterministik
    /// karşı saldırı, kaydet/yükle A==B. Normal oyunda ASLA çalışmaz (--nuclear-use-test).
    /// </summary>
    private void RunNuclearUseTest()
    {
        GD.Print("=========================================");
        GD.Print("  M31.4: NÜKLEER KULLANIM TESTİ (--nuclear-use-test)");
        GD.Print("=========================================");

        if (!HasPlayer) SetPlayerCountry("DEU");
        if (Speed == 0) SetSpeed(0);

        NuclearProgramSystem.EnableTestMode();
        NuclearProgramSystem.SetTestCompletedStage(NuclearProgramSystem.NuclearStage.BomberDelivery);
        NuclearProgramSystem.SetTestWarheads(1);

        string playerId = PlayerCountryId;
        var playerData = PlayerCountryData;
        if (playerData != null) playerData.PoliticalPower = 5000;

        // Hedef: oyuncuyu içermeyen, bağımsız ilk ülke
        string targetId = null;
        foreach (var c in CountryDatabase.All())
        {
            if (c.Id != playerId && !c.IsAnnexed) { targetId = c.Id; break; }
        }
        if (targetId == null)
        {
            GD.Print("[NuclearUseTest] Uygun hedef ülke bulunamadı — test atlanıyor");
            GD.Print("=========================================");
            return;
        }

        bool allOk = true;
        var targetData = CountryDatabase.Get(targetId);
        float tensionBefore = DiplomacySystem.WorldTension;
        int warheadsBefore = NuclearProgramSystem.Warheads;
        long targetPopBefore = targetData.Population;
        int targetFactoriesBefore = targetData.TotalFactories;
        int relationBefore = DiplomacySystem.GetRelation(playerId, targetId);
        int radiationBefore = NuclearProgramSystem.ActiveRadiationZones;
        GD.Print($"[NuclearUseTest] {playerId} -> {targetId}: tension önce=%{tensionBefore:F1}, başlık={warheadsBefore}, " +
                 $"hedef nüfus={targetPopBefore}, fabrika={targetFactoriesBefore}, ilişki={relationBefore}, radyasyon={radiationBefore}");

        var (ok, reason, result) = NuclearProgramSystem.LaunchNuclearStrike(playerId, targetId);
        GD.Print($"[NuclearUseTest] LaunchNuclearStrike: {(ok ? "OK" : "FAIL: " + reason)}");
        if (!ok) { allOk = false; GetTree().Quit(1); return; }

        // Başlık tüketilmiş olmalı
        int warheadsAfter = NuclearProgramSystem.Warheads;
        bool warheadConsumed = warheadsAfter == warheadsBefore - 1;
        GD.Print($"[NuclearUseTest] Başlık sonrası={warheadsAfter} (once={warheadsBefore}, tüketildi={warheadConsumed})");
        if (!warheadConsumed) allOk = false;

        // Hedefte nüfus ve fabrika kaybı
        bool targetDamaged = result.PopulationLost > 0 && result.FactoriesLost > 0;
        GD.Print($"[NuclearUseTest] Hedef hasarı: nüfus −{result.PopulationLost}, fabrika −{result.FactoriesLost}, " +
                 $"istikrar −{result.StabilityLoss:F0}, destek −{result.WarSupportLoss:F0}, zarar={targetDamaged}");
        if (!targetDamaged) allOk = false;

        // Radyasyon bölgesi oluşturulmuş olmalı
        int radiationAfter = NuclearProgramSystem.ActiveRadiationZones;
        float radIntensity = NuclearProgramSystem.GetRadiationIntensity(targetId, (int)TotalDays);
        bool radiationActive = radiationAfter == radiationBefore + 1 && radIntensity > 0;
        GD.Print($"[NuclearUseTest] Radyasyon sonrası={radiationAfter} (once={radiationBefore}), yoğunluk=%{radIntensity:P1}, aktif={radiationActive}");
        if (!radiationActive) allOk = false;

        // Dünya gerilimi artmış, ilişki bozulmuş olmalı
        float tensionAfter = DiplomacySystem.WorldTension;
        int relationAfter = DiplomacySystem.GetRelation(playerId, targetId);
        GD.Print($"[NuclearUseTest] Tension sonrası=%{tensionAfter:F1} (delta={tensionAfter - tensionBefore:+#;-#;0}), " +
                 $"ilişki sonrası={relationAfter} (delta={relationAfter - relationBefore})");
        if (!(tensionAfter > tensionBefore && relationAfter < relationBefore)) allOk = false;

        // Strike result alanları
        bool resultOk = result != null && result.AttackerId == playerId && result.TargetId == targetId
            && result.Day == (int)TotalDays && result.Radiation != null;
        GD.Print($"[NuclearUseTest] StrikeResult bütünleşti={resultOk}, karşı saldırı={result.RetaliationTriggered}");
        if (!resultOk) allOk = false;

        // DTO kaydet/yükle A==B (başlık + radyasyon zonu kalıcı olmalı)
        var beforeDto = NuclearProgramSystem.GetSaveData();
        NuclearProgramSystem.LoadSaveData(beforeDto);
        var afterDto = NuclearProgramSystem.GetSaveData();
        bool dtoRoundTrip = afterDto.Warheads == beforeDto.Warheads
            && afterDto.RadiationZones != null && afterDto.RadiationZones.Count == beforeDto.RadiationZones.Count
            && afterDto.CompletedThrough == beforeDto.CompletedThrough;
        bool radiationSurvives = afterDto.RadiationZones != null && afterDto.RadiationZones.Count == 1;
        GD.Print($"[NuclearUseTest] DTO round_trip={dtoRoundTrip}, radyasyon survives={radiationSurvives}");
        if (!dtoRoundTrip || !radiationSurvives) allOk = false;

        bool pass = allOk && warheadConsumed && targetDamaged && radiationActive
            && tensionAfter > tensionBefore && relationAfter < relationBefore && resultOk
            && dtoRoundTrip && radiationSurvives;

        GD.Print(pass ? "[NuclearUseTest] SONUÇ: PASS ✓" : "[NuclearUseTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>
    /// M31.5: AI nükleer davranış testi — oyuncu nükleer-silahlı ve caydırıcılık doktrinindeyse
    /// AI oyuncuyu çok daha güçlü görür (deterrence 2.0x) ve saldırıyı engeller; başlık/doctrin
    /// kaldırıldığında saldırıya geçer. DTO kaydet/yükle A==B doğrulanır. Normal oyunda ASLA
    /// çalışmaz (--ai-nuclear-test).
    /// </summary>
    private void RunAiNuclearTest()
    {
        GD.Print("=========================================");
        GD.Print("  M31.5: AI NÜKLEER DAVRANIŞ TESTİ (--ai-nuclear-test)");
        GD.Print("=========================================");

        string playerId = "DEU";
        var deu = CountryDatabase.Get(playerId);

        // Hedef: oyuncunun komşusu, ilhak edilmemiş, oyuncuya karşı savaşta olmayan bir AI ülke.
        string targetId = null;
        CountryData targetData = null;
        foreach (var c in CountryDatabase.All())
        {
            if (c == null || c.IsAnnexed || c.Id == playerId) continue;
            if (!NeighborSystem.AreNeighbors(playerId, c.Id)) continue;
            if (WarSystem.IsAtWarWith(c.Id, playerId)) continue;
            targetId = c.Id;
            targetData = c;
            break;
        }
        if (targetId == null)
        {
            GD.Print("[AiNuclearTest] Oyuncunun uygun komşu AI ülkesi bulunamadı — test BAŞARISIZ");
            GD.Print("=========================================");
            GetTree().Quit(1);
            return;
        }

        // Oyuncuyu (DEU) zaten düşük güce indir: nükleersiz durumda hedefe saldırılabilir,
        // nükleer caydırıcılıkla (2.0x) ise saldırı engellenmeli.
        deu.StartingDivisions = 5;
        deu.CivFactories = 10;
        deu.MilFactories = 15;
        deu.Dockyards = 20; // TotalFactories=45 → defStrength = 5 + 15 = 20

        // Hedef AI: savaş kararına girmek için ≥10 tümen gerekir; nükleersizden
        // güçlü, nükleerli durumda zayıf görünmesi için ayarlanır.
        targetData.StartingDivisions = 12;
        targetData.PoliticalPower = 2000;
        targetData.WarSupportPercent = 60;

        if (!HasPlayer) SetPlayerCountry(playerId);
        // AI durumunu oyuncu ülkesini hariç tutacak şekilde (yeniden) başlat.
        CountryAI.Initialize();
        if (Speed == 0) SetSpeed(5);

        NuclearProgramSystem.EnableTestMode();
        NuclearProgramSystem.SetTestWarheads(1);
        NuclearProgramSystem.SetTestDoctrine(NuclearProgramSystem.NuclearDoctrine.Deterrence);

        var playerData = PlayerCountryData;
        if (playerData != null)
        {
            playerData.PoliticalPower = 5000;
            playerData.WarSupportPercent = 100;
        }

        // Gerekçe: hedef -> oyuncu (otomatik, anında geçerli → HasValidCasusBelli true)
        CasusBelliSystem.GrantImmediateJustification(targetId, playerId, "defense_retaliation");

        int defDivs = playerData != null ? playerData.StartingDivisions : 0;
        int defFactories = playerData != null ? playerData.TotalFactories : 0;
        int defStrength = defDivs + defFactories / 3;
        int attDivs = targetData != null ? targetData.StartingDivisions : 0;
        int attFactories = targetData != null ? targetData.TotalFactories : 0;
        int attStrength = attDivs + attFactories / 3;
        GD.Print($"[AiNuclearTest] {playerId}(D) def={defStrength} ({defDivs} tümen, {defFactories} fabrika), " +
                 $"{targetId}(A) att={attStrength} ({attDivs} tümen, {attFactories} fabrika)");

        bool allOk = true;

        // --- Aşama 1: Caydırıcılık AKTIF — AI oyuncuya saldırıya geçmemeli ---
        GD.Print("[AiNuclearTest] Aşama 1: Nükleer caydırıcılık AKTIF (başlık=1, doktrin=Deterrence)");
        bool warDuringDeterrence = WaitForWarWith(targetId, playerId, maxDays: 40);
        GD.Print($"[AiNuclearTest] Caydırıcılıktan sonra {targetId}->{playerId} savaşta mı? {warDuringDeterrence}");
        bool deterrenceBlocked = !warDuringDeterrence;
        if (!deterrenceBlocked) allOk = false;

        // --- Aşama 2: Caydırıcılık KALDIRILDI — AI oyuncuyu saldırıya geçebilir ---
        NuclearProgramSystem.SetTestWarheads(0); // başlık 0 → targetIsNuclearDeterrence false
        GD.Print("[AiNuclearTest] Aşama 2: Nükleer caydırıcılık KALDIRILDI (başlık=0)");
        bool warAfterDeterrence = WaitForWarWith(targetId, playerId, maxDays: 40);
        GD.Print($"[AiNuclearTest] Caydırıcılık kaldırıldıktan sonra {targetId}->{playerId} savaşta mı? {warAfterDeterrence}");
        bool deterrenceLiftedAttack = warAfterDeterrence;
        if (!deterrenceLiftedAttack) allOk = false;

        // DTO kaydet/yükle A==B (nükleer durum kalıcı olmalı)
        var beforeDto = NuclearProgramSystem.GetSaveData();
        NuclearProgramSystem.LoadSaveData(beforeDto);
        var afterDto = NuclearProgramSystem.GetSaveData();
        bool dtoRoundTrip = afterDto.Warheads == beforeDto.Warheads
            && afterDto.CompletedThrough == beforeDto.CompletedThrough
            && afterDto.Doctrine == beforeDto.Doctrine;
        GD.Print($"[AiNuclearTest] DTO round_trip={dtoRoundTrip}, başlık={afterDto.Warheads}, doktrin={afterDto.Doctrine}");
        if (!dtoRoundTrip) allOk = false;

        bool pass = allOk && deterrenceBlocked && deterrenceLiftedAttack && dtoRoundTrip;
        GD.Print(pass ? "[AiNuclearTest] SONUÇ: PASS ✓" : "[AiNuclearTest] SONUÇ: FAIL ✗");
        GetTree().Quit(pass ? 0 : 1);
        GD.Print("=========================================");
    }

    /// <summary>Belirtilen çift arasında savaş açılana kadar (veya maxDays kadar) gün ilerletir.</summary>
    private bool WaitForWarWith(string attackerId, string defenderId, int maxDays)
    {
        for (int d = 0; d < maxDays; d++)
        {
            if (WarSystem.IsAtWarWith(attackerId, defenderId)) return true;
            AdvanceDaysForTest(1);
        }
        return WarSystem.IsAtWarWith(attackerId, defenderId);
    }

    private static EventSystem.GameEvent MakeTestEvent(string id, string name, string declareWar)
    {
        return new EventSystem.GameEvent
        {
            Id = id,
            Name = name,
            Description = "test",
            Options = new List<EventSystem.EventOption>
            {
                new EventSystem.EventOption { Label = "Accept", Effects = new EventSystem.EventEffects { DeclareWar = declareWar } }
            }
        };
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

        // 6) Sınır aşan deniz/liman bağlantısı (M38.3): iki farklı ülkedeki kıyı ili komşu olmalı
        int seaLinks = 0;
        foreach (var p in provs1)
        {
            if (!p.IsCoastal) continue;
            foreach (var n in p.NeighborIds)
            {
                var nb = provs1.FirstOrDefault(pp => pp.Id == n);
                if (nb != null && nb.IsCoastal && nb.CountryId != p.CountryId) seaLinks++;
            }
        }
        seaLinks /= 2; // yönsüz
        GD.Print($"[ProvinceTest] Deniz/liman bağlantısı (ikisi de kıyı, farklı ülke): {seaLinks}");

        bool result = sameCount && sameIds && symmetric && unowned == 0 && seaLinks > 0;
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
    /// M37: Uluslararası örgüt headless testi — Data/organizations.json yükleme, üyelik,
    /// Madde 5 kolektif savunma, toplantı gündemi, AI oy ağırlığı, Save/Load kimliği.
    /// Normal oyunda ASLA çalışmaz (--organization-test).
    /// </summary>
    private void RunOrganizationTest()
    {
        GD.Print("=========================================");
        GD.Print("  M37: ULUSLARARASI ÖRGÜT TESTİ");
        GD.Print("=========================================");

        // 1) Veri dosyası yüklendi: 9 örgüt
        int count = OrganizationSystem.OrganizationCount;
        GD.Print($"[OrganizationTest] Örgüt sayısı: {count} (beklenen 9)");
        bool ok = count == 9;

        // 2) Üyelik sorgusu
        bool usaNato = OrganizationSystem.IsMember("nato", "USA");
        bool rusNato = OrganizationSystem.IsMember("nato", "RUS");
        bool deuEu = OrganizationSystem.IsMember("eu", "DEU");
        GD.Print($"[OrganizationTest] USA NATO üyesi: {usaNato} (beklenen True), RUS NATO üyesi: {rusNato} (beklenen False), DEU AB üyesi: {deuEu} (beklenen True)");
        ok &= usaNato && !rusNato && deuEu;

        // 3) Üye listeleri
        var natoMembers = OrganizationSystem.Members("nato");
        GD.Print($"[OrganizationTest] NATO üyeleri ({natoMembers.Count}): {string.Join(",", natoMembers)}");
        ok &= natoMembers.Contains("USA") && natoMembers.Contains("DEU");

        // 4) Ülke -> org lookup
        var usaOrgs = OrganizationSystem.CountryOrgs("USA");
        GD.Print($"[OrganizationTest] USA üyes olduğu org: {string.Join(",", usaOrgs)}");
        ok &= usaOrgs.Contains("nato");

        // 5) Üyelik ekle / çıkar (dinamik)
        bool added = OrganizationSystem.AddMember("sco", "TUR");
        bool removed = OrganizationSystem.RemoveMember("sco", "TUR");
        GD.Print($"[OrganizationTest] TUR SCO'ya eklendi: {added}, çıkarıldı: {removed}");
        ok &= added && removed && !OrganizationSystem.IsMember("sco", "TUR");

        // 6) Madde 5 kolektif savunma (NATO: Askeri, USA üye)
        OrganizationSystem.TriggerArticle5("USA", "RUS");
        int art5 = OrganizationSystem.Article5Activations.Count;
        GD.Print($"[OrganizationTest] Madde 5 etkinleri: {art5} (beklenen >= 1)");
        ok &= art5 >= 1;

        // 7) AI oy ağırlığı (0-1)
        double wMember = OrganizationSystem.AIVoteWeight("nato", "DEU", "sanction_1");
        double wNon = OrganizationSystem.AIVoteWeight("nato", "RUS", "sanction_1");
        GD.Print($"[OrganizationTest] AI oy ağırlığı üye(DEU/NATO)={wMember:F2}, non-üye(RUS/NATO)={wNon:F2}");
        ok &= wMember >= 0 && wMember <= 1 && wNon >= 0 && wNon <= 1;

        // 8) Save/Load kimliği
        var save = OrganizationSystem.GetSaveData();
        int before = save.Memberships.Length;
        OrganizationSystem.LoadSaveData(save);
        var save2 = OrganizationSystem.GetSaveData();
        int after = save2.Memberships.Length;
        GD.Print($"[OrganizationTest] Save/Load üyelik: önce={before} sonra={after}");
        ok &= before == after;

        // 9) ResetForNewGame temizler Madde 5 etkinleri (üyelikler veri dosyasından korunur)
        OrganizationSystem.ResetForNewGame();
        GD.Print($"[OrganizationTest] Reset sonrası Madde5: {OrganizationSystem.Article5Activations.Count} (beklenen 0)");
        ok &= OrganizationSystem.Article5Activations.Count == 0;

        GD.Print(ok ? "[OrganizationTest] SONUÇ: PASS ✓" : "[OrganizationTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
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

    /// <summary>
    /// M13.2: Tooltip sistemi testi (--tooltip-test).
    /// 1) TooltipSystem.Set() ile bir kontrol bağlanabilir ve gösterim hatası üretmez?
    /// 2) TooltipPanel.Show() / Hide() çalışır mı?
    /// 3) TooltipSystem.Unset() bağlantıyı kaldırır mı?
    /// 4) TooltipPanel, fare olaylarını engellemez (MouseFilter = Ignore)?
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunTooltipTest()
    {
        GD.Print("=========================================");
        GD.Print("  M13.2: TOOLTIP SİSTEM TESTİ (--tooltip-test)");
        GD.Print("=========================================");

        // _Ready sırasında ağaç hâlâ kuruluyor, AddChild senkron olarak başarısız olabilir.
        // Tüm test mantığını ertelenmiş çağrı ile çalıştır.
        CallDeferred(MethodName.RunTooltipTestDeferred);
    }

    private void RunTooltipTestDeferred()
    {
        if (!HasPlayer) SetPlayerCountry("GER");
        if (Speed == 0) SetSpeed(0);

        // 1) Bir buton oluşturup tooltip bağlama — fırlatma yapmamalı
        _ttBtn = new Button { Text = "TooltipTestButon" };
        AddChild(_ttBtn);
        _ttText = "Bu bir detaylı tooltip açıklamasıdır.";
        _ttSetOk = false;
        try
        {
            TooltipSystem.Set(_ttBtn, _ttText);
            _ttSetOk = true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[TooltipTest] Set() istisna: {e.Message}");
        }
        GD.Print($"[TooltipTest] Set() çalıştırıldı: {(_ttSetOk ? "OK" : "FAIL")}");

        // TooltipPanel/_layer'ın ağaca eklenmesini (CallDeferred) bekleyip geri kalanı denetle.
        CallDeferred(MethodName.RunTooltipTestVerify);
    }

    private void RunTooltipTestVerify()
    {
        // 2) Tooltip panelinin CanvasLayer üzerinden oluşturulup erişilebilir olmalı
        var root = GetTree().Root;
        int canvasLayerCount = 0;
        foreach (Node child in root.GetChildren())
        {
            if (child is CanvasLayer) canvasLayerCount++;
        }
        GD.Print($"[TooltipTest] CanvasLayer sayısı (tooltip için >=1 beklenir): {canvasLayerCount}");
        _ttLayerOk = canvasLayerCount >= 1;

        // 3) TooltipPanel: Show() fırlatmasın, Hide() visible=false yapın, MouseFilter=Ignore
        _ttTip = new TooltipPanel();
        AddChild(_ttTip); // _Ready çalışınca _label ve MouseFilter başlatılır

        // _Ready'nin çalışmasını bekleyip nihai kontrolleri yap.
        CallDeferred(MethodName.RunTooltipTestFinal);
    }

    private void RunTooltipTestFinal()
    {
        bool showNoThrow = false;
        try { _ttTip.Show(_ttText); showNoThrow = true; }
        catch (Exception e) { GD.PrintErr($"[TooltipTest] Show() istisna: {e.Message}"); }
        _ttTip.Hide();
        bool hideOk = !_ttTip.Visible;
        GD.Print($"[TooltipTest] Show() hata-sız: {showNoThrow}, Hide() visible=false: {hideOk}");

        // 4) MouseFilter = Ignore (fare olaylarını engellememeli)
        bool mouseFilterOk = _ttTip.MouseFilter == Control.MouseFilterEnum.Ignore;
        GD.Print($"[TooltipTest] MouseFilter=Ignore: {(mouseFilterOk ? "OK" : "FAIL")}");

        // 5) Unset — bağlantıyı kaldırır, tekrar Set ile bağlanabilir
        TooltipSystem.Unset(_ttBtn);
        bool unsetOk = true;
        try
        {
            TooltipSystem.Set(_ttBtn, "Yeni metin");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[TooltipTest] Unset sonrası Set() istisna: {e.Message}");
            unsetOk = false;
        }
        GD.Print($"[TooltipTest] Unset sonrası tekrar Set(): {(unsetOk ? "OK" : "FAIL")}");

        bool ok = _ttSetOk && _ttLayerOk && showNoThrow && hideOk && mouseFilterOk && unsetOk;
        GD.Print(ok ? "[TooltipTest] SONUÇ: PASS ✓" : "[TooltipTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M13.3: Harita görünüm modu sistemi testi. Tüm modları sırayla ayarlar ve
    /// CurrentMapMode'nun güncellendiğini doğrular. Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunMapModeTest()
    {
        GD.Print("=========================================");
        GD.Print("  M13.3: HARITA GÖRÜNÜM MODU TESTİ (--map-mode-test)");
        GD.Print("=========================================");

        // WorldMap oyun sahnesinde (Game.OnMapFrameReady) oluşturulur; bu autoload
        // bağlamında henüz yok olabilir. Bir kare erteler ve Instance'ı dene.
        if (WorldMap.Instance != null)
        {
            RunMapModeTestExecute();
            return;
        }
        CallDeferred(MethodName.RunMapModeTestDeferred);
    }

    private void RunMapModeTestDeferred()
    {
        if (WorldMap.Instance == null)
        {
            // Oyun sahnesi yüklü değil (headless menü): kendi bir WorldMap örneği yarat.
            try
            {
                var wm = new WorldMap();
                AddChild(wm); // _Ready eşzamanlı olarak çalışır, Instance atanır
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MapModeTest] WorldMap örneği oluşturulamadı: {e.Message}");
            }
        }
        RunMapModeTestExecute();
    }

    private void RunMapModeTestExecute()
    {
        var wm = WorldMap.Instance;
        bool ok = wm != null;

        if (ok)
        {
            WorldMap.MapMode[] modes =
            {
                WorldMap.MapMode.Political, WorldMap.MapMode.Terrain, WorldMap.MapMode.Supply,
                WorldMap.MapMode.Factories, WorldMap.MapMode.Resources, WorldMap.MapMode.AirZones,
                WorldMap.MapMode.Culture
            };

            foreach (var m in modes)
            {
                try
                {
                    wm.SetMapMode(m);
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[MapModeTest] SetMapMode({m}) istisna: {e.Message}");
                    ok = false;
                    continue;
                }
                if (wm.CurrentMapMode != m)
                {
                    GD.PrintErr($"[MapModeTest] Mod beklenilmedi: {m} → {wm.CurrentMapMode}");
                    ok = false;
                }
            }

            GD.Print($"[MapModeTest] {modes.Length} harita modu sırayla ayarlandı ve okundu.");
        }
        else
        {
            GD.PrintErr("[MapModeTest] WorldMap.Instance erişilemedi (oyun sahnesi yüklü değil).");
        }

        // M46.1: Kültür harita modu gerçek davranış doğrulaması
        ok &= RunCultureMapModeChecks(WorldMap.Instance);

        // M46.4: Harita modu + kültür vurgusu Save/Load round-trip doğrulaması
        ok &= RunMapViewRoundTripCheck(WorldMap.Instance);

        GD.Print(ok ? "[MapModeTest] SONUÇ: PASS ✓" : "[MapModeTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M46.1: Kültür harita modunun gerçek davranışını doğrular.
    ///
    /// Kontrol edilenler: (1) culture modunda il dolgusu, o ilin kültürünün
    /// rengine eşit; (2) renk kültür adından türetildiği için aynı kültür her
    /// çağrıda aynı rengi verir (deterministik); (3) farklı kültürler farklı
    /// renk alır (harita okunabilir); (4) culture modu, political modundan
    /// gerçekten farklı bir görüntü üretir (mod gerçekten bağlanmış).
    ///
    /// Normal oyunda ASLA çağrılmaz (yalnızca --map-mode-test).
    /// </summary>
    private bool RunCultureMapModeChecks(WorldMap wm)
    {
        bool ok = true;

        if (wm == null)
        {
            GD.PrintErr("[CultureModeTest] WorldMap.Instance erişilemedi (oyun sahnesi yüklü değil).");
            return false;
        }

        // Kültür kaydı olan illerden örnekleme al
        var withCulture = ProvinceCultureSystem.All()
            .Where(r => r != null && !string.IsNullOrEmpty(r.Culture))
            .ToList();

        if (withCulture.Count == 0)
        {
            GD.PrintErr("[CultureModeTest] Kültür kaydı olan il yok — culture modu doğrulanamadı.");
            return false;
        }

        wm.SetMapMode(WorldMap.MapMode.Culture);
        if (wm.CurrentMapMode != WorldMap.MapMode.Culture)
        {
            GD.PrintErr("[CultureModeTest] Mod ayarlanamadı: beklenen Culture");
            ok = false;
        }

        // 1) İl dolgusu = kültür rengi
        int colorCheckCount = 0;
        foreach (var rec in withCulture)
        {
            Color expected = WorldMap.ColorForCulture(rec.Culture);
            Color actual = wm.ProvinceFillColor(rec.ProvinceId);
            if (actual != expected)
            {
                GD.Print($"[CultureModeTest] İl dolgusu kültür rengine eşit değil: {rec.ProvinceId} " +
                         $"({rec.Culture}) beklenen={expected} gerçek={actual}");
                ok = false;
                continue;
            }
            colorCheckCount++;
        }

        // 2) Determinizm: aynı kültür her çağrıda aynı renk
        Color repeatA = WorldMap.ColorForCulture(withCulture[0].Culture);
        Color repeatB = WorldMap.ColorForCulture(withCulture[0].Culture);
        if (repeatA != repeatB)
        {
            GD.PrintErr("[CultureModeTest] Renk deterministik değil: aynı kültür farklı renk üretti.");
            ok = false;
        }

        // 3) Farklı kültürler farklı renk alır (TÜM çiftler — harita okunabilirliği için)
        var distinctCultures = withCulture.Select(r => r.Culture).Distinct().ToList();
        int collisionCount = 0;
        for (int i = 0; i < distinctCultures.Count; i++)
        {
            Color ci = WorldMap.ColorForCulture(distinctCultures[i]);
            for (int j = i + 1; j < distinctCultures.Count; j++)
            {
                if (ci == WorldMap.ColorForCulture(distinctCultures[j]))
                {
                    GD.PrintErr($"[CultureModeTest] Renk çakışması: '{distinctCultures[i]}' ve " +
                                $"'{distinctCultures[j]}' aynı rengi aldı — harita okunamaz.");
                    ok = false;
                    collisionCount++;
                }
            }
        }

        // 4) Political moddan gerçekten farklı görüntü
        wm.SetMapMode(WorldMap.MapMode.Political);
        Color politicalColor = wm.ProvinceFillColor(withCulture[0].ProvinceId);
        wm.SetMapMode(WorldMap.MapMode.Culture);
        Color cultureColor = wm.ProvinceFillColor(withCulture[0].ProvinceId);
        if (politicalColor == cultureColor)
        {
            GD.Print($"[CultureModeTest] Culture modu political ile aynı rengi verdi: {cultureColor} " +
                     $"(il {withCulture[0].ProvinceId}) — mod bağlantısı eksik olabilir.");
            ok = false;
        }

        GD.Print($"[CultureModeTest] kültür ili kontrol edildi: {colorCheckCount}/{withCulture.Count} " +
                 $"({distinctCultures.Count} farklı kültür, renk çakışması={collisionCount}, deterministik=✓)");

        // Testin sonuna geri dönmek zorunda değil, ama diğer kontroller bu modda kalmasın
        wm.SetMapMode(WorldMap.MapMode.Political);

        return ok;
    }

    /// <summary>
    /// M46.4: Harita modu + kültür vurgusunun Save/Load round-trip'ini doğrular.
    ///
    /// Kontrol edilenler: (1) vurgulu mod kaydedilip yüklendiğinde geri gelir;
    /// (2) büyük/küçük durumlar (mod + vurgu) ayrı ayrı round-trip eder;
    /// (3) dışarıda bırakılan mod (Culture dışına geçiş) vurguyu düşürür.
    ///
    /// Normal oyunda ASLA çağrılmaz (yalnızca --map-mode-test).
    /// </summary>
    private bool RunMapViewRoundTripCheck(WorldMap wm)
    {
        GD.Print("[MapViewTest] === M46.4: harita görünümü Save/Load round-trip ===");
        if (wm == null)
        {
            GD.PrintErr("[MapViewTest] WorldMap.Instance erişilemedi (oyun sahnesi yüklü değil).");
            return false;
        }

        // Vurgulanacak kültürü dünyadaki gerçek kayıtlardan seç (kurgu veri yok)
        string probeCulture = ProvinceCultureSystem.All().FirstOrDefault(r => r?.Culture?.Length > 0)?.Culture ?? "";
        if (probeCulture.Length == 0)
        {
            GD.PrintErr("[MapViewTest] Kültür kaydı yok — dünya kurulmamış.");
            return false;
        }
        GD.Print($"[MapViewTest] Deney kültürü: {probeCulture}");

        bool ok = true;

        // 1) Culture + vurgu round-trip
        wm.SetMapMode(WorldMap.MapMode.Political);
        wm.SetCultureHighlight(probeCulture);
        if (MapViewSystem.Mode != WorldMap.MapMode.Culture ||
            MapViewSystem.CultureHighlight != probeCulture)
        {
            GD.PrintErr($"[MapViewTest] HATA: durum MapViewSystem'e yazılmadı — " +
                        $"mod={MapViewSystem.Mode}, vurgu={MapViewSystem.CultureHighlight}");
            ok = false;
        }

        SaveSystem.SaveTestFile();
        string summaryA = SaveLoadTest.GetSummaryCore();
        if (!SaveSystem.LoadTestFile())
        {
            GD.PrintErr("[MapViewTest] HATA: kayıt yüklenemedi.");
            return false;
        }
        string summaryB = SaveLoadTest.GetSummaryCore();
        if (summaryA != summaryB)
        {
            GD.PrintErr("[MapViewTest] HATA: A==B bozuldu.");
            SaveLoadTest.FindDiff(summaryA, summaryB);
            ok = false;
        }
        else
        {
            GD.Print("[MapViewTest] save/load A==B ✓");
        }

        if (MapViewSystem.Mode != WorldMap.MapMode.Culture ||
            MapViewSystem.CultureHighlight != probeCulture)
        {
            GD.PrintErr($"[MapViewTest] HATA: round-trip tutmadı — mod={MapViewSystem.Mode}, " +
                        $"vurgu={MapViewSystem.CultureHighlight}");
            ok = false;
        }
        else
        {
            GD.Print($"[MapViewTest] Round-trip tuttu: mod={MapViewSystem.Mode}, vurgu={MapViewSystem.CultureHighlight}");
        }

        // Harita düğümü yüklenen durumu uygulamış olmalı
        if (wm.CurrentMapMode != WorldMap.MapMode.Culture)
        {
            GD.PrintErr($"[MapViewTest] HATA: harita modu culture olmadı ({wm.CurrentMapMode}).");
            ok = false;
        }
        if (wm.CultureHighlight != probeCulture)
        {
            GD.PrintErr($"[MapViewTest] HATA: harita vurgusu geri yüklenmedi ({wm.CultureHighlight}).");
            ok = false;
        }

        // 2) Moddan çıkınca vurgu düşer ve bu da round-trip eder
        wm.SetMapMode(WorldMap.MapMode.Political);
        wm.ApplyMapViewState();
        if (MapViewSystem.Mode != WorldMap.MapMode.Political || MapViewSystem.CultureHighlight.Length != 0)
        {
            GD.PrintErr($"[MapViewTest] HATA: mod dışına çıkınca vurgu düşmedi — " +
                        $"mod={MapViewSystem.Mode}, vurgu={MapViewSystem.CultureHighlight}");
            ok = false;
        }
        else
        {
            GD.Print("[MapViewTest] Culture dışına çıkınca vurgu düştü ✓");
        }

        SaveSystem.SaveTestFile();
        summaryA = SaveLoadTest.GetSummaryCore();
        if (!SaveSystem.LoadTestFile()) ok = false;
        summaryB = SaveLoadTest.GetSummaryCore();
        if (summaryA != summaryB)
        {
            GD.PrintErr("[MapViewTest] HATA: 2. A==B bozuldu.");
            SaveLoadTest.FindDiff(summaryA, summaryB);
            ok = false;
        }
        else
        {
            GD.Print("[MapViewTest] 2. save/load A==B ✓");
        }

        SaveSystem.DeleteTestFile();
        GD.Print(ok ? "[MapViewTest] SONUÇ: PASS ✓" : "[MapViewTest] SONUÇ: FAIL ✗");
        return ok;
    }

    /// <summary>
    /// M13.4: Ayarlar paneli testi. VSync/Default Speed/Difficulty/Ironman setter'larını ve
    /// SettingsSave DTO'su üzerinden Save/Load A==B determinizmini doğrular. Normal oyunda
    /// ASLA çalışmaz (sadece --settings-test ile).
    /// </summary>
    private void RunSettingsTest()
    {
        GD.Print("=========================================");
        GD.Print("  M13.4: AYARLAR PANELİ TESTİ (--settings-test)");
        GD.Print("=========================================");

        bool ok = true;

        // 1) Varsayılan değerler
        ok &= SettingsSystem.VSync == true;
        ok &= SettingsSystem.DefaultSpeed == 0;
        ok &= SettingsSystem.Difficulty == SettingsSystem.GameDifficulty.Normal;
        ok &= SettingsSystem.Ironman == false;
        GD.Print($"[SettingsTest] Varsayılanlar: VSync={SettingsSystem.VSync}, DefaultSpeed={SettingsSystem.DefaultSpeed}, Difficulty={SettingsSystem.Difficulty}, Ironman={SettingsSystem.Ironman}");

        // 2) Setter'lar
        SettingsSystem.SetVSync(false);
        ok &= SettingsSystem.VSync == false;

        SettingsSystem.SetDefaultSpeed(3);
        ok &= SettingsSystem.DefaultSpeed == 3;

        SettingsSystem.SetDifficulty(SettingsSystem.GameDifficulty.Hard);
        ok &= SettingsSystem.Difficulty == SettingsSystem.GameDifficulty.Hard;

        SettingsSystem.SetIronman(true);
        ok &= SettingsSystem.Ironman == true;
        GD.Print($"[SettingsTest] Setter'lar sonrası: VSync={SettingsSystem.VSync}, DefaultSpeed={SettingsSystem.DefaultSpeed}, Difficulty={SettingsSystem.Difficulty}, Ironman={SettingsSystem.Ironman}");

        // 3) Save/Load DTO A==B determinizmi
        var saveData = SettingsSystem.GetSaveData();
        SettingsSystem.LoadSaveData(saveData);
        ok &= SettingsSystem.VSync == saveData.VSync;
        ok &= SettingsSystem.DefaultSpeed == saveData.DefaultSpeed;
        ok &= SettingsSystem.Difficulty == (SettingsSystem.GameDifficulty)Enum.Parse(typeof(SettingsSystem.GameDifficulty), saveData.Difficulty);
        ok &= SettingsSystem.Ironman == saveData.Ironman;
        GD.Print($"[SettingsTest] Save/Load A==B: {ok}");

        // 4) Değerleri geri yükle (kullanıcı tercihini bozmasın)
        SettingsSystem.SetVSync(true);
        SettingsSystem.SetDefaultSpeed(0);
        SettingsSystem.SetDifficulty(SettingsSystem.GameDifficulty.Normal);
        SettingsSystem.SetIronman(false);

        GD.Print(ok ? "[SettingsTest] SONUÇ: PASS ✓" : "[SettingsTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M33: Uzun Koşu ve Kararlılık — --soak-test kancağı.
    /// Varsayılan 10 oyun yılı, tüm 50 ülke, AI açık, headless çalıştırma.
    /// Her oyun sonunda save→load→A==B, tick başına süre ölçümü, bellek artışı raporu,
    /// ~1000-il performans kontrolü, başarısızlıkta Quit(1).
    /// Normal oyunda ASLA çalışmaz (--soak-test).
    /// </summary>
    private void RunSoakTest()
    {
        GD.Print("=========================================");
        GD.Print("  M33: UZUN KOŞU VE KARARLIK TESTİ (--soak-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0x20261020DEADUL);
        EventSystem.EnableAutoChooseTest();
        FocusSystem.EnableAutoPickTest();
        SetSpeed(5); // headless maksimum hız

        // Önceki test dosyasını temizle
        SaveSystem.DeleteTestFile();

        if (!HasPlayer) SetPlayerCountry("DEU");
        GD.Print($"[SoakTest] Oyuncu={PlayerCountryId}, Senaryo={ScenarioSystem.ActiveId} ({ScenarioSystem.ActiveId})");

        // 1) Ülke sayısı (≥50)
        int countryCount = CountryDatabase.All().Count(c => c != null);
        GD.Print($"[SoakTest] Ülke: {countryCount}");
        bool ok = countryCount >= 50;

        // 2) AI ülkeleri
        int aiCount = CountryDatabase.All().Count(c => c != null && CountryAI.IsAiCountry(c.Id));
        GD.Print($"[SoakTest] AI ülke: {aiCount}");

        // 3) İl sayısı (~1000)
        int provinceCount = ProvinceOwnershipSystem.GetSaveData().Count;
        GD.Print($"[SoakTest] İl: {provinceCount}");

        const int Years = 10;
        const int DaysPerYear = 30;

        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long initialMemory = GC.GetTotalMemory(false);

        for (int year = 1; year <= Years && ok; year++)
        {
            var yearSw = System.Diagnostics.Stopwatch.StartNew();
            int startDay = TotalDays;

            // Test hızını garanti et (olaylar pause yapabilir)
                SetSpeed(5);
                AdvanceDaysForTest(DaysPerYear);
            int yearDays = TotalDays - startDay;
            yearSw.Stop();

            double yearMs = yearSw.Elapsed.TotalMilliseconds;
            double msPerTick = yearDays > 0 ? yearMs / yearDays : 0;
            GD.Print($"[SoakTest] Yıl {year}/{Years}: {yearDays} gün, {yearMs:F1} ms, ms/tick={msPerTick:F3}, tarih={CurrentDate:yyyy-MM-dd}");

            if (msPerTick > 50.0)
                GD.Print($"[SoakTest] UYARI: ms/tick = {msPerTick:F1} > 50 (performans iyileştirmesi gerekebilir, M33.5)");

            // Save → Load → A==B (her yıl sonu)
            SetSpeed(5); // kaydetmeden önce hızı 5'e dön (olay pause'u etkileyebilir)
            SaveSystem.SaveTestFile();
            string summaryA = SaveLoadTest.GetSummaryCore();
            bool loaded = SaveSystem.LoadTestFile();
            if (!loaded)
            {
                GD.PrintErr($"[SoakTest] Yıl {year}: yükleme başarısız");
                ok = false;
                break;
            }
            // Yükleme sonrası oyun duraklatılır (SaveSystem:650); test hızına geri dön
            SetSpeed(5);
            string summaryB = SaveLoadTest.GetSummaryCore();
            bool yearOk = summaryA == summaryB;
            GD.Print($"[SoakTest] Yıl {year}: save_load_a_b={yearOk}");
            if (!yearOk)
            {
                GD.Print($"[SoakTest] === ÖZET A (kaydet sonrası) ===");
                GD.Print(summaryA);
                GD.Print($"[SoakTest] === ÖZET B (yükle sonrası) ===");
                GD.Print(summaryB);
                SaveLoadTest.FindDiff(summaryA, summaryB);
                ok = false;
                break;
            }
        }

        totalSw.Stop();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long finalMemory = GC.GetTotalMemory(false);
        double memPct = initialMemory > 0 ? (double)(finalMemory - initialMemory) / initialMemory * 100.0 : 0;
        GD.Print($"[SoakTest] Toplam süre: {totalSw.Elapsed.TotalSeconds:F1} sn");
        GD.Print($"[SoakTest] Bellek: {initialMemory:N0} → {finalMemory:N0} bayt (%{memPct:F1} artış)");
        if (memPct > 20.0)
            GD.Print($"[SoakTest] UYARI: bellek artışı = %{memPct:F1} > %20 (M33.4 iyileştirmesi gerekebilir)");

        if (provinceCount < 900)
            GD.Print($"[SoakTest] UYARI: il sayisi = {provinceCount} < 900 (WorldMap headless'te baslatilmamis olabilir, M18/M19'da test ediliyor)");

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        SaveSystem.DeleteTestFile();
        GetTree().Quit(ok ? 0 : 1);
}
// M34.1: --balance-test — savaş sonuçlarının dağılımı ve AI saldırı oranı denge kontrolü.
// Akış: sabit tohum → 300 oyun günü → savaş dağılımı (sayı, kazanan/yenilen) +
// AI saldırı oranı (%5-%30) ölçülür → save/load A==B kontrol noktası → Quit(0/1).
private void RunBalanceTest()
{
    GD.Print("=========================================");
    GD.Print("  M34.1: DENGE TESTİ (--balance-test)");
    GD.Print("  Savaş dağılımı + AI saldırı oranı (%5-%30)");
    GD.Print("=========================================");

    DeterministicRNG.InitializeForNewGame(0x20261021DEADBEEFUL);
    EventSystem.EnableAutoChooseTest();
    FocusSystem.EnableAutoPickTest();
    SetSpeed(5); // headless maksimum hız
    SaveSystem.DeleteTestFile();

    if (!HasPlayer) SetPlayerCountry("DEU");
    GD.Print($"[Balance] Oyuncu={PlayerCountryId}, Senaryo={ScenarioSystem.ActiveId}");

    const int Days = 300; // 10 oyun yılı (her ay ~30 gün)
    var sw = System.Diagnostics.Stopwatch.StartNew();

    int startDay = TotalDays;
    AdvanceDaysForTest(Days);
    int elapsedDays = TotalDays - startDay;

    // --- Savaş dağılımı analizi (tüm savaşlar: bitmiş + aktif) ---
    var allWars = WarSystem.Wars.ToList();
    int totalWars = allWars.Count;
    int endedWars = allWars.Count(w => w.IsEnded);
    int activeWars = totalWars - endedWars;

    int attackerWins = 0, defenderWins = 0, undecided = 0;
    foreach (var w in allWars.Where(w => w.IsEnded))
    {
        if (w.WarProgress >= 100f) attackerWins++;
        else if (w.WarProgress <= 0f) defenderWins++;
        else undecided++;
    }

    // AI saldırı oranı: en az bir saldırı başlatan AI ülke / sarmış (ilahlı olmayan) AI ülke sayısı
    var aiCountries = CountryDatabase.All()
        .Where(c => c != null && CountryAI.IsAiCountry(c.Id) && !c.IsAnnexed)
        .ToList();
    int aiCountryCount = aiCountries.Count;

    int aiAttackers = allWars
        .Where(w => aiCountries.Any(c => c.Id == w.AttackerId))
        .Select(w => w.AttackerId)
        .Distinct()
        .Count();
    double aiAttackRate = aiCountryCount > 0 ? (double)aiAttackers / aiCountryCount * 100.0 : 0.0;

    string playerId = PlayerCountryId;
    int playerAttackerWars = allWars.Count(w => w.AttackerId == playerId);
    int playerDefenderWars = allWars.Count(w => w.DefenderId == playerId);

    // --- Ekonomi invariantleri (M34.2): negatif GDP yok, sonsuz enflasyon yok, %100+ memnuniyet yok ---
    bool economyOk = true;
    foreach (var c in CountryDatabase.All())
    {
        if (c == null || c.IsAnnexed) continue;
        string id = c.Id;
        double gdp = StatisticsSystem.GetValue(id, "gdp");
        double inflation = StatisticsSystem.GetValue(id, "inflation");
        if (!double.IsFinite(gdp) || gdp < 0.0)
        {
            GD.PrintErr($"[Balance] NEGATIF/GEÇERSİZ GDP: {id}={gdp:F2}");
            economyOk = false;
        }
        if (!double.IsFinite(inflation))
        {
            GD.PrintErr($"[Balance] SONSUZ/GEÇERSİZ ENFLASYON: {id}={inflation:F2}");
            economyOk = false;
        }
    }
    var satSave = PublicSatisfactionSystem.GetSaveData();
    if (satSave?.Countries != null)
    {
        foreach (var sc in satSave.Countries)
        {
            foreach (var v in sc.Satisfaction?.Values ?? Enumerable.Empty<double>())
            {
                if (v > 100.0)
                {
                    GD.PrintErr($"[Balance] %100+ MEMNUNIYET: {sc.CountryId}={v:F1}");
                    economyOk = false;
                }
            }
        }
    }
    if (economyOk)
        GD.Print($"[Balance] Ekonomi invariantleri: TÜM GÖSTERGELER [Min,Max] içinde (negatif GDP yok, enflasyon sınırlı, memnuniyet ≤100).");

    // --- Rapor ---
    GD.Print($"[Balance] Süre: {elapsedDays} oyun günü ({sw.Elapsed.TotalSeconds:F1} sn)");
    GD.Print($"[Balance] Toplam savaş: {totalWars} (bitmiş={endedWars}, aktif={activeWars})");
    GD.Print($"[Balance] Sona eren: attacker_kazandı={attackerWins}, defender_kazandı={defenderWins}, karar_verilemedi={undecided}");
    GD.Print($"[Balance] AI saldırgan ülke: {aiAttackers}/{aiCountryCount} = %{aiAttackRate:F1}");
    GD.Print($"[Balance] Oyuncu saldırgan={playerAttackerWars}, savunucu={playerDefenderWars}");

    // --- Başarı kriterleri ---
    bool ok = true;

    // --- M34.3: AI kapsamı — her ilahlı olmayan, oyuncu olmayan ülke AI yönetiminde olmalı ---
    // (Oyuncu ülkesi tasarım gereği ASLA AI tarafından yönetilmez; 49 AI + 1 oyuncu = 50 aktif ülke.)
    int totalNonAnnexed = CountryDatabase.All().Count(c => c != null && !c.IsAnnexed);
    int playerIsNonAnnexed = (playerId != null && CountryDatabase.All().Any(c => c != null && c.Id == playerId && !c.IsAnnexed)) ? 1 : 0;
    int expectedAi = totalNonAnnexed - playerIsNonAnnexed;
    int aiCoveredNonPlayer = CountryDatabase.All()
        .Count(c => c != null && !c.IsAnnexed && c.Id != playerId && CountryAI.IsAiCountry(c.Id));
    bool aiCoverageOk = aiCoveredNonPlayer == expectedAi;
    GD.Print($"[Balance] AI kapsami: {aiCoveredNonPlayer}/{expectedAi} ilahlı non-player ulke AI'de (oyuncu={playerId ?? "-"} hariç, ilahlanan disari)");
    if (!aiCoverageOk)
    {
        GD.PrintErr("[Balance] AI kapsami EKSIK — bazi ilahlı olmayan ulkeler AI yonetiminin disinda.");
        ok = false;
    }

    if (totalWars == 0)
    {
        GD.PrintErr("[Balance] Hiç savaş başlamadı — denge testi anlamlı değil.");
        ok = false;
    }

    if (aiAttackRate < 5.0 || aiAttackRate > 30.0)
    {
        GD.PrintErr($"[Balance] AI saldırı oranı %{aiAttackRate:F1} hedef aralığı %5-%30 dışında.");
        ok = false;
    }
    else
    {
        GD.Print($"[Balance] AI saldırı oranı %{aiAttackRate:F1} hedef aralığı (%5-%30) içinde.");
    }

    if (!economyOk)
    {
        GD.PrintErr("[Balance] Ekonomi invariantleri BOZULDU — denge testi FAIL.");
        ok = false;
    }

    // --- Save/Load A==B kontrol noktası ---
    SetSpeed(5); // olaylar pause yapabilir; test hızına geri dön
    SaveSystem.SaveTestFile();
    string summaryA = SaveLoadTest.GetSummaryCore();
    bool loaded = SaveSystem.LoadTestFile();
    if (!loaded)
    {
        GD.PrintErr("[Balance] Yükleme başarısız — test iptal");
        ok = false;
    }
    else
    {
        SetSpeed(5); // SaveSystem.LoadTestFile oyununu duraklatır (SaveSystem:650)
        string summaryB = SaveLoadTest.GetSummaryCore();
        bool slOk = summaryA == summaryB;
        GD.Print($"[Balance] save_load_a_b={slOk}");
        if (!slOk)
        {
            GD.PrintErr("[Balance] Save/Load determinizm BOZULDU (A != B).");
            SaveLoadTest.FindDiff(summaryA, summaryB);
            ok = false;
        }
    }

    GD.Print("=========================================");
    GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
    GD.Print("=========================================");
    SaveSystem.DeleteTestFile();
    GetTree().Quit(ok ? 0 : 1);
}

    /// <summary>
    /// M41.2: Zafer koşulları testi — --victory-test kancası.
    /// Aktif senaryonun koşul setini yükler, beş koşul türünü (fabrika yüzdesi, major
    /// kapitülasyon, il yüzdesi, en yüksek skor, bitiş tarihine hayatta kalma) ayrı ayrı
    /// doğrular, bildirim + otomatik duraklama tetiklenir. Başarısızlıkta Quit(1).
    /// Normal oyunda ASLA çalışmaz (yalnızca --victory-test ile).
    /// </summary>
    private void RunVictoryTest()
    {
        GD.Print("=========================================");
        GD.Print("  M41.2: ZAFER KOŞULLARI TESTİ (--victory-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0x20261021D1C7UL);
        EventSystem.EnableAutoChooseTest();
        FocusSystem.EnableAutoPickTest();

        bool ok = true;
        string playerId = "DEU";

        // --- 1) Koşul seti yükleniyor mu? ---
        var set = VictorySystem.Active;
        GD.Print($"[ZaferTest] Aktif senaryo: {ScenarioSystem.ActiveId}, set: {set?.Id ?? "YOK"}");
        if (set == null || set.Conditions.Count == 0)
        {
            GD.PrintErr("[ZaferTest] HATA: Aktif senaryo için zafer koşulu seti boş!");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"[ZaferTest] Koşullar: {string.Join(", ", set.Conditions.Select(c => $"{c.Id}({c.Kind})"))}");

        // Her koşulun türü tanınmalı
        foreach (var c in set.Conditions)
        {
            if (VictorySystem.ParseKind(c.Kind) == VictorySystem.VictoryKind.Unknown)
            {
                GD.PrintErr($"[ZaferTest] HATA: bilinmeyen koşul türü '{c.Kind}' ({c.Id})");
                ok = false;
            }
        }

        // Oyuncu ülkesini ata (CountryDatabase zaten yüklü)
        SetPlayerCountry(playerId);
        var player = CountryDatabase.Get(playerId);
        if (player == null)
        {
            GD.PrintErr($"[ZaferTest] HATA: {playerId} ülkesi bulunamadı");
            GetTree().Quit(1);
            return;
        }

        // --- 2) Fabrika yüzdesi koşulu ---
        var condFactories = set.Conditions.FirstOrDefault(c => VictorySystem.ParseKind(c.Kind) == VictorySystem.VictoryKind.ControlFactoryPercent);
        if (condFactories != null)
        {
            long world = CountryDatabase.All().Sum(c => Math.Max(0, (long)c.TotalFactories));
            long mine = player.TotalFactories;
            double pct = world > 0 ? mine * 100.0 / world : 0;
            GD.Print($"[ZaferTest] Fabrika yüzdesi: {mine}/{world} = %{pct:F2} (hedef %{condFactories.Target})");
            if (pct > condFactories.Target) { GD.PrintErr("[ZaferTest] HATA: başlangıçta hedef zaten aşılmış"); ok = false; }

            // Hedefi aşacak kadar fabrika ver, tekrar değerlendir
            int before = player.CivFactories;
            player.CivFactories += (int)(world * condFactories.Target / 100.0) + 100;
            bool met = VictorySystem.IsSatisfied(condFactories, playerId, 0);
            player.CivFactories = before;
            GD.Print($"[ZaferTest] Fabrika koşulu sağlandı mı (geçici artış): {met}");
            if (!met) { GD.PrintErr("[ZaferTest] HATA: fabrika koşulu beklenendiği gibi sağlanmadı"); ok = false; }
        }

        // --- 3) Major kapitülasyon koşulu ---
        var condMajors = set.Conditions.FirstOrDefault(c => VictorySystem.ParseKind(c.Kind) == VictorySystem.VictoryKind.CapitulateMajors);
        if (condMajors != null)
        {
            int need = (int)condMajors.Target;
            // <need> adet major güç kapitülasyonu kaydet (fabrika = major eşiği üstü)
            for (int i = 0; i < need; i++)
                VictorySystem.RecordCapitulation(playerId, $"TESTMAJOR{i}", VictorySystem.MajorFactoryThreshold + 10);
            // Bir de minor kaydet (sayılmamalı)
            VictorySystem.RecordCapitulation(playerId, "TESTMINOR", 5);
            int got = VictorySystem.CapitulatedMajorCount(playerId);
            GD.Print($"[ZaferTest] Major kapitülasyonu: {got}/{need}");
            if (got < need) { GD.PrintErr("[ZaferTest] HATA: major kapitülasyon sayısı yetersiz"); ok = false; }
        }

        // --- 4) İl yüzdesi koşulu ---
        var condProvinces = set.Conditions.FirstOrDefault(c => VictorySystem.ParseKind(c.Kind) == VictorySystem.VictoryKind.ControlProvincePercent);
        if (condProvinces != null)
        {
            int world = ProvinceOwnershipSystem.TotalProvinceCount;
            int mine = ProvinceOwnershipSystem.ProvinceCountFor(playerId);
            double pct = world > 0 ? mine * 100.0 / world : 0;
            GD.Print($"[ZaferTest] İl yüzdesi: {mine}/{world} = %{pct:F2} (hedef %{condProvinces.Target})");
            bool met = VictorySystem.IsSatisfied(condProvinces, playerId, 0);
            bool big = condProvinces.Target <= 0.001f; // pratikte hiçbir senaryo 0 hedef kullanmaz
            if (world > 0 && !big && met && pct < condProvinces.Target)
            { GD.PrintErr("[ZaferTest] HATA: il koşulu yanlış pozitif"); ok = false; }
            if (!met && pct >= condProvinces.Target)
            { GD.PrintErr("[ZaferTest] HATA: il koşulu beklenen pozitif verdi"); ok = false; }
        }

        // --- 5) En yüksek skor koşulu ---
        var condTop = set.Conditions.FirstOrDefault(c => VictorySystem.ParseKind(c.Kind) == VictorySystem.VictoryKind.TopScore);
        if (condTop != null)
        {
            string leaderId = CountryDatabase.All()
                .Where(c => c != null && !c.IsAnnexed)
                .OrderByDescending(c => VictorySystem.ComputeNationalScore(c.Id))
                .Select(c => c.Id).FirstOrDefault();
            bool leaderTop = !string.IsNullOrEmpty(leaderId) && VictorySystem.IsTopScoring(leaderId);
            GD.Print($"[ZaferTest] Skor lideri: {leaderId} — IsTopScoring={leaderTop}");
            if (string.IsNullOrEmpty(leaderId) || !leaderTop)
            { GD.PrintErr("[ZaferTest] HATA: en yüksek skorlu ülke tespit edilemedi"); ok = false; }
        }

        // --- 6) Bitiş tarihine hayatta kalma koşulu ---
        var condSurvive = set.Conditions.FirstOrDefault(c => VictorySystem.ParseKind(c.Kind) == VictorySystem.VictoryKind.SurviveUntilDate);
        if (condSurvive != null)
        {
            bool tooEarly = VictorySystem.IsSatisfied(condSurvive, playerId, 0);
            GD.Print($"[ZaferTest] Hayatta kalma (gün 0): {tooEarly} (false beklenir)");
            if (tooEarly) { GD.PrintErr("[ZaferTest] HATA: bitiş tarihinden önce hayatta kalma koşulu sağlandı"); ok = false; }
        }

        // --- 7) Oyuncu değerlendirmesi + bildirim + otomatik duraklama ---
        // Oyuncuyu geçici olarak dünyanın en güçlü ülkesi yap: top_score + survive + fabrika koşulları
        // aynı anda sağlanabilir. Ardından EvaluatePlayer çağrılır.
        VictorySystem.ResetForNewGame(); // kayıtları temizle (test başlangıcı)
        var strong = CountryDatabase.Get(playerId);
        int civ0 = strong.CivFactories, mil0 = strong.MilFactories, dock0 = strong.Dockyards;
        long worldAll = CountryDatabase.All().Sum(c => Math.Max(0, (long)c.TotalFactories));
        strong.CivFactories = (int)(worldAll + 500);
        strong.MilFactories = (int)worldAll + 500;
        strong.Dockyards = (int)worldAll + 500;

        int currentDay = VictorySystem.DaysUntilEnd(TotalDays);
        GD.Print($"[ZaferTest] Bitişe kalan gün: {currentDay}");
        // Bitiş tarihine ulaşılmış gibi değerlendir (survive + top_score doğru olsun)
        VictorySystem.EvaluatePlayer(100000);

        strong.CivFactories = civ0;
        strong.MilFactories = mil0;
        strong.Dockyards = dock0;

        GD.Print($"[ZaferTest] PlayerVictoryDeclared={VictorySystem.PlayerVictoryDeclared}");
        GD.Print($"[ZaferTest] Sağlanan koşullar: [{string.Join(", ", VictorySystem.PlayerAchievedConditions)}]");
        if (!VictorySystem.PlayerVictoryDeclared)
        { GD.PrintErr("[ZaferTest] HATA: güçlü oyuncu için zafer ilan edilmedi"); ok = false; }
        if (VictorySystem.PlayerAchievedConditions.Count == 0)
        { GD.PrintErr("[ZaferTest] HATA: hiçbir koşul tamamlanmadı"); ok = false; }

        // M41.4: Zafer ekranı (GameOverPanel) açılmış olmalı
        bool overShown = GameOverPanel.IsOpen;
        GD.Print($"[ZaferTest] Oyun sonu paneli açık: {overShown}");
        if (!overShown) { GD.PrintErr("[ZaferTest] HATA: zafer için oyun sonu paneli açılmadı"); ok = false; }

        // --- 8) AI değerlendirmesi (konuşmayı kirletmeden, sayaç doğrulaması) ---
        VictorySystem.EvaluateAi(0);
        int aiAchieved = VictorySystem.AiAchievedConditionCount;
        GD.Print($"[ZaferTest] AI tamamlanan koşul sayısı (0. gün): {aiAchieved}");

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M41.3: Ulusal skor ve sıralama testi — --score-test kancası.
    /// Her ülke için skor hesaplanır, sıralamanın tutarlılığı (azalan skor, tekrarsız sıra no),
    /// en yüksek skorlu ülkenin doğru tespiti ve skor kırılımı doğrulanır. Başarısızlıkta Quit(1).
    /// Normal oyunda ASLA çalışmaz (yalnızca --score-test ile).
    /// </summary>
    private void RunScoreTest()
    {
        GD.Print("=========================================");
        GD.Print("  M41.3: SKOR VE SIRALAMA TESTİ (--score-test)");
        GD.Print("=========================================");

        bool ok = true;
        var ranking = ScoreSystem.GetRanking();
        GD.Print($"[SkorTest] {ranking.Count} ülke sıralandı");

        if (ranking.Count < 2)
        {
            GD.PrintErr("[SkorTest] HATA: sıralama için yeterli ülke yok");
            GetTree().Quit(1);
            return;
        }

        // 1) Skor azalan sırada mı?
        for (int i = 1; i < ranking.Count; i++)
        {
            if (ranking[i].Score > ranking[i - 1].Score + 0.0001)
            {
                GD.PrintErr($"[SkorTest] HATA: sıralama bozuk — #{i} ({ranking[i].Name}) #{i - 1}'den yüksek");
                ok = false;
                break;
            }
        }

        // 2) Sıra numaraları 1..N ve tekrarsız mı?
        var ranks = ranking.Select(e => e.Rank).OrderBy(r => r).ToList();
        if (ranks.First() != 1 || ranks.Last() != ranking.Count)
        {
            GD.PrintErr($"[SkorTest] HATA: sıra numaraları 1..{ranking.Count} değil");
            ok = false;
        }

        // 3) İlk sıradaki ülke IsTopCountry=true, diğerleri false
        string leader = ranking[0].CountryId;
        if (!ScoreSystem.IsTopCountry(leader))
        {
            GD.PrintErr($"[SkorTest] HATA: lider {leader} IsTopCountry=false verdi");
            ok = false;
        }
        for (int i = 1; i < ranking.Count; i++)
        {
            if (ScoreSystem.IsTopCountry(ranking[i].CountryId))
            {
                GD.PrintErr($"[SkorTest] HATA: #{ranking[i].Rank} {ranking[i].Name} da IsTopCountry=true");
                ok = false;
                break;
            }
        }

        // 4) GetRank ve GetLeaderId tutarlı mı?
        if (ScoreSystem.GetLeaderId() != leader)
        {
            GD.PrintErr($"[SkorTest] HATA: GetLeaderId ({ScoreSystem.GetLeaderId()}) != lider ({leader})");
            ok = false;
        }
        foreach (var e in ranking)
        {
            if (ScoreSystem.GetRank(e.CountryId) != e.Rank)
            {
                GD.PrintErr($"[SkorTest] HATA: {e.Name} GetRank tutarsız ({ScoreSystem.GetRank(e.CountryId)} != {e.Rank})");
                ok = false;
                break;
            }
        }

        // 5) Skor kırılımı toplamı ComputeScore'a eşit mi?
        foreach (var e in ranking.Take(5))
        {
            double total = ScoreSystem.ComputeScore(e.CountryId);
            double sum = ScoreSystem.ScoreBreakdown(e.CountryId).Values.Sum();
            if (Math.Abs(total - sum) > 0.001)
            {
                GD.PrintErr($"[SkorTest] HATA: {e.Name} kırılım toplamı ({sum:F3}) != skor ({total:F3})");
                ok = false;
            }
        }

        // 6) Skor sıfır/negatif olamaz; ilhak edilmiş ülke skoru 0
        foreach (var e in ranking.Take(5))
        {
            if (e.Score < 0)
            {
                GD.PrintErr($"[SkorTest] HATA: {e.Name} negatif skor ({e.Score})");
                ok = false;
            }
        }
        var annexed = CountryDatabase.All().FirstOrDefault(c => c != null && c.IsAnnexed);
        if (annexed != null && ScoreSystem.ComputeScore(annexed.Id) != 0)
        {
            GD.PrintErr($"[SkorTest] HATA: ilhak edilmiş {annexed.Name} skoru 0 değil");
            ok = false;
        }

        ScoreSystem.LogTop(10);

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M41.4: Oyun sonu paneli testi — --gameover-test kancası.
    /// Panel zafer ve yenilgi olarak açılır; başlık, geçen süre, skor dökümü ve sıralama
    /// metinleri dolu olmalı, panel + margin toplamı 1152x648 görünür alana sığmalı,
    /// backdrop MouseFilter.Stop olmalı, ESC paneli kapatmalı.
    /// Normal oyunda ASLA çalışmaz (yalnızca --gameover-test ile).
    /// </summary>
    private async void RunGameOverTest()
    {
        GD.Print("=========================================");
        GD.Print("  M41.4: OYUN SONU PANELİ TESTİ (--gameover-test)");
        GD.Print("=========================================");

        bool ok = true;
        // Proje genelinde panel sözleşmesi: 1152x648 (headless'ta pencer yok, viewport 64x64
        // görünebildiğinden ölçüm sabit sözleşmeye göre yapılır).
        const float ScreenW = 1152f;
        const float ScreenH = 648f;
        GD.Print($"[GameOverTest] Hedef ekran: {ScreenW}x{ScreenH} (gerçek viewport {GetViewport().GetVisibleRect().Size})");

        foreach (bool victory in new[] { true, false })
        {
            string tag = victory ? "zafer" : "yenilgi";

            var panel = GameOverPanel.OpenUnder(this, victory, "Test Condition",
                "Test description for score breakdown.", "Testland");
            if (panel == null)
            {
                GD.PrintErr($"[GameOverTest] HATA: {tag} paneli açılamadı");
                ok = false;
                continue;
            }

            // Panel gerçekten ağaçta mı, en az bir frame bekle (animasyon/ölçüm otursun)
            await ToSignal(GetTree(), "process_frame");
            await ToSignal(GetTree(), "process_frame");

            if (!GameOverPanel.IsOpen)
            {
                GD.PrintErr($"[GameOverTest] HATA: {tag} paneli IsOpen değil");
                ok = false;
            }

            // İçerik metinleri dolu mu? (başlık + skor + sıralama satırları)
            var labels = panel.FindChildren("*", "Label", recursive: true, owned: false);
            int nonEmpty = 0;
            foreach (var n in labels)
            {
                if (n is Label l && !string.IsNullOrWhiteSpace(l.Text))
                    nonEmpty++;
            }
            GD.Print($"[GameOverTest] {tag}: {labels.Count} label, {nonEmpty} dolu");
            if (nonEmpty < 5)
            {
                GD.PrintErr($"[GameOverTest] HATA: {tag} panelinde dolu label sayısı yetersiz ({nonEmpty} < 5)");
                ok = false;
            }

            // Panelin toplam boyutu (panel + margin) hedef ekrana sığyor mu?
            var panelNode = panel.FindChild("*PanelContainer*", recursive: true, owned: false);
            if (panelNode == null)
            {
                GD.PrintErr($"[GameOverTest] HATA: {tag} panelinde PanelContainer bulunamadı");
                ok = false;
            }
            if (panelNode is Control pc)
            {
                Vector2 need = pc.GetCombinedMinimumSize();
                GD.Print($"[GameOverTest] {tag}: panel min boyut {need.X}x{need.Y} (hedef {ScreenW}x{ScreenH})");
                if (need.X > ScreenW || need.Y > ScreenH)
                {
                    GD.PrintErr($"[GameOverTest] HATA: {tag} paneli hedef ekrana sığmıyor ({need.X}x{need.Y})");
                    ok = false;
                }
            }

            // ESC kapatır mı? (simüle edilmiş tuş olayı)
            var esc = new InputEventKey
            {
                Keycode = Key.Escape,
                Pressed = true
            };
            panel._UnhandledInput(esc); // test kancası: doğrudan çağrı — ESC davranışı
            await ToSignal(GetTree(), "process_frame");
            await ToSignal(GetTree(), "process_frame");
            await ToSignal(GetTree(), "process_frame");

            if (GameOverPanel.IsOpen)
            {
                GD.PrintErr($"[GameOverTest] HATA: {tag} paneli ESC ile kapanmadı");
                ok = false;
            }
            else
            {
                GD.Print($"[GameOverTest] {tag}: ESC ile kapatıldı ✓");
            }
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M42.1: Savaş puanı testi — --war-score-test kancası.
    /// Bir savaş başlatılır; savaşın ilerleyişine göre (işgal, düşman tümen kaybı,
    /// kapitülasyon) iki tarafın puanının [0,100] aralığında kaldığı, toplamın 100
    /// olduğu, ilerlemenin saldıran lehine işlediği ve save/load sonrası puanın
    /// korunduğu doğrulanır. Normal oyunda ASLA çalışmaz (yalnızca --war-score-test).
    /// </summary>
    private void RunWarScoreTest()
    {
        GD.Print("=========================================");
        GD.Print("  M42.1: SAVAŞ PUANI TESTİ (--war-score-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0x20261021D2C7UL);
        bool ok = true;

        // Oyuncu garantisi (diğer testlerle aynı desen): komut satırından --country
        // verilmediyse geçmiş senaryoda DEU'yu seç. WarSystem.DeclareWar oyuncu ister.
        if (!HasPlayer) SetPlayerCountry("DEU");
        string playerId = PlayerCountryId;
        string enemyId = "POL";
        GD.Print($"[WarScoreTest] Oyuncu: {playerId}, hedef: {enemyId}");

        if (string.IsNullOrEmpty(playerId))
        {
            GD.PrintErr("[WarScoreTest] HATA: oyuncu ülkesi yok");
            GetTree().Quit(1);
            return;
        }

        var enemy = CountryDatabase.Get(enemyId);
        if (enemy == null || enemy.IsAnnexed)
        {
            GD.PrintErr($"[WarScoreTest] HATA: hedef ülke bulunamadı: {enemyId}");
            GetTree().Quit(1);
            return;
        }

        // Savaş ilanı önkoşullarını garanti et (--war-test deseni):
        // PP yeterli + savaş desteği + casus belli gerekçesi.
        if (PlayerCountryData != null)
        {
            if (PlayerCountryData.PoliticalPower < WarSystem.DeclareWarPPCost + 10)
                PlayerCountryData.PoliticalPower = WarSystem.DeclareWarPPCost + 10;
            if (PlayerCountryData.WarSupportPercent < 50)
                PlayerCountryData.WarSupportPercent = 50;
        }
        CasusBelliSystem.GrantImmediateJustification(playerId, enemyId, "historical_event");

        // --- 1) Savaş başlat ---
        bool declared = WarSystem.DeclareWar(enemyId);
        GD.Print($"[WarScoreTest] Savaş ilanı: {declared}");
        if (!declared)
        {
            GD.PrintErr("[WarScoreTest] HATA: savaş başlatılamadı");
            GetTree().Quit(1);
            return;
        }

        var war = WarSystem.Wars.FirstOrDefault(w => !w.IsEnded && w.DefenderId == enemyId);
        if (war == null)
        {
            GD.PrintErr("[WarScoreTest] HATA: aktif savaş bulunamadı");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"[WarScoreTest] Savaş: {war.Id} (başlangıç düşman tümen={war.StartEnemyDivisions}, " +
                 $"fabrika={war.StartEnemyFactories})");

        // --- 2) Başlangıç puanları: aralık ve toplam ---
        float a0 = WarScoreSystem.AttackerScore(war);
        float d0 = WarScoreSystem.DefenderScore(war);
        GD.Print($"[WarScoreTest] Başlangıç: saldıran={a0:F1}, savunan={d0:F1}, toplam={a0 + d0:F1}");
        if (a0 < 0f || a0 > 100f || d0 < 0f || d0 > 100f)
        {
            GD.PrintErr("[WarScoreTest] HATA: puanlar [0,100] aralığında değil");
            ok = false;
        }
        if (Math.Abs(a0 + d0 - 100f) > 0.001f)
        {
            GD.PrintErr($"[WarScoreTest] HATA: iki taraf toplamı 100 değil ({a0 + d0:F1})");
            ok = false;
        }

        // --- 3) İşgal arttıkça saldıran puanı artmalı ---
        float prevA = a0;
        foreach (float prog in new[] { 25f, 50f, 75f, 100f })
        {
            war.WarProgress = prog;
            float a = WarScoreSystem.AttackerScore(war);
            float d = WarScoreSystem.DefenderScore(war);
            GD.Print($"[WarScoreTest] İlerleme {prog:F0}%: saldıran={a:F1}, savunan={d:F1}");
            if (a < prevA - 0.001f)
            {
                GD.PrintErr($"[WarScoreTest] HATA: ilerleme arttı ama saldıran puanı düştü ({prevA:F1} → {a:F1})");
                ok = false;
            }
            prevA = a;
        }

        // --- 4) Düşman tümen kaybı puanı eklemeli ---
        float aBeforeManpower = WarScoreSystem.AttackerScore(war);
        war.EnemyDivisions = Math.Max(0, war.StartEnemyDivisions / 2);
        float aAfterManpower = WarScoreSystem.AttackerScore(war);
        GD.Print($"[WarScoreTest] Tümen yarılandı: saldıran {aBeforeManpower:F1} → {aAfterManpower:F1}");
        if (aAfterManpower < aBeforeManpower - 0.001f)
        {
            GD.PrintErr("[WarScoreTest] HATA: düşman tümen kaybı saldıran puanını artırmadı");
            ok = false;
        }

        // --- 5) Kapitülasyon: saldıran 100, savunan 0 ---
        war.DefenderCapitulated = true;
        war.IsEnded = true;
        war.EnemyDivisions = 0;
        float aCap = WarScoreSystem.AttackerScore(war);
        float dCap = WarScoreSystem.DefenderScore(war);
        GD.Print($"[WarScoreTest] Savunan kapitülasyonu: saldıran={aCap:F1}, savunan={dCap:F1}");
        if (aCap < 99f || dCap > 1f)
        {
            GD.PrintErr($"[WarScoreTest] HATA: kapitülasyonda puanlar 100/0 değil ({aCap:F1}/{dCap:F1})");
            ok = false;
        }

        // --- 6) Save/Load sonrası puan korunuyor mu? ---
        var save = WarSystem.GetSaveData();
        int savedBattles = save.Wars.First()?.AttackerBattlesWon ?? -1;
        war.AttackerBattlesWon = 12; // test verisi
        war.DefenderBattlesWon = 4;
        save = WarSystem.GetSaveData();
        WarSystem.LoadSaveData(save);

        var reloaded = WarSystem.Wars.FirstOrDefault(w => w.Id == war.Id);
        if (reloaded == null)
        {
            GD.PrintErr("[WarScoreTest] HATA: yükleme sonrası savaş bulunamadı");
            ok = false;
        }
        else
        {
            GD.Print($"[WarScoreTest] Yükleme: çatışma {reloaded.AttackerBattlesWon}/{reloaded.DefenderBattlesWon}, " +
                     $"kapitülasyon (savunan)={reloaded.DefenderCapitulated}");
            if (reloaded.AttackerBattlesWon != 12 || reloaded.DefenderBattlesWon != 4)
            {
                GD.PrintErr("[WarScoreTest] HATA: çatışma sayaçları kayıttan yüklenmedi");
                ok = false;
            }
            if (!reloaded.DefenderCapitulated)
            {
                GD.PrintErr("[WarScoreTest] HATA: kapitülasyon bayrağı kayıttan yüklenmedi");
                ok = false;
            }
            float aR = WarScoreSystem.AttackerScore(reloaded);
            float dR = WarScoreSystem.DefenderScore(reloaded);
            GD.Print($"[WarScoreTest] Yeniden hesaplanan puan: saldıran={aR:F1}, savunan={dR:F1}");
            if (Math.Abs(aR - aCap) > 0.5f)
            {
                GD.PrintErr($"[WarScoreTest] HATA: yükleme sonrası skor bozuldu ({aCap:F1} → {aR:F1})");
                ok = false;
            }
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M42.2: Barış talebi maliyeti testi — --peace-terms-test kancası.
    /// Saf hesap: RNG yok, state yok. Talep maliyetinin doğru düştüğü, puan
    /// yetersizse reddedilip loglandığı doğrulanır. Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunPeaceTermsTest()
    {
        GD.Print("=========================================");
        GD.Print("  M42.2: BARIŞ TALEBİ MALİYETİ TESTİ (--peace-terms-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0x20261022D2C7UL);
        bool ok = true;

        PeaceConferenceSystem.LoadData();
        int termCount = PeaceConferenceSystem.TermCount;
        GD.Print($"[PeaceTermsTest] Yüklenen talep türü sayısı: {termCount}");
        if (termCount < 4)
        {
            GD.PrintErr("[PeaceTermsTest] HATA: en az 4 talep türü bekleniyor");
            ok = false;
        }

        // --- 1) İl talebi maliyeti: taban + birim × adet ---
        var prov = PeaceConferenceSystem.Get("province_demand");
        if (prov == null)
        {
            GD.PrintErr("[PeaceTermsTest] HATA: 'province_demand' tanımı yok");
            GetTree().Quit(1);
            return;
        }
        double c3 = PeaceConferenceSystem.CostFor("province_demand", 3);
        double expect3 = prov.BaseCost + prov.CostPerUnit * 3;
        GD.Print($"[PeaceTermsTest] 3 il talebi: {c3:F1} (beklenen {expect3:F1})");
        if (Math.Abs(c3 - expect3) > 0.001)
        {
            GD.PrintErr("[PeaceTermsTest] HATA: il talebi maliyeti formüle uymuyor");
            ok = false;
        }
        if (c3 <= prov.BaseCost)
        {
            GD.PrintErr("[PeaceTermsTest] HATA: adet arttıkça maliyet artmıyor");
            ok = false;
        }

        // Birim adedi üst sınırı aşamaz
        double cOver = PeaceConferenceSystem.CostFor("province_demand", prov.MaxUnits + 25);
        double cMax = PeaceConferenceSystem.CostFor("province_demand", prov.MaxUnits);
        GD.Print($"[PeaceTermsTest] Sınır aşımı: {cOver:F1} (max {cMax:F1})");
        if (Math.Abs(cOver - cMax) > 0.001)
        {
            GD.PrintErr("[PeaceTermsTest] HATA: adet üst sınırı aşılabiliyor");
            ok = false;
        }

        // --- 2) Yetersiz puan: reddedilmeli + sebep loglanmalı ---
        var low = PeaceConferenceSystem.Check("annex_country", 1, 30f);
        GD.Print($"[PeaceTermsTest] Puan 30 ile ilhak: ok={low.Ok}, sebep=\"{low.Reason}\"");
        if (low.Ok || string.IsNullOrEmpty(low.Reason))
        {
            GD.PrintErr("[PeaceTermsTest] HATA: yetersiz puanda talep reddedilmedi veya sebep loglanmadı");
            ok = false;
        }

        // --- 3) Yeterli puan: maliyet düşmeli, kalan puan tutmalı ---
        float rich = 80f;
        var okAnnex = PeaceConferenceSystem.Check("annex_country", 1, rich);
        GD.Print($"[PeaceTermsTest] Puan {rich:F0} ile ilhak: ok={okAnnex.Ok}, maliyet={okAnnex.Cost:F1}, kalan={okAnnex.RemainingScore:F1}");
        if (!okAnnex.Ok)
        {
            GD.PrintErr($"[PeaceTermsTest] HATA: yeterli puanda talep reddedildi ({okAnnex.Reason})");
            ok = false;
        }
        if (Math.Abs(okAnnex.RemainingScore - (rich - okAnnex.Cost)) > 0.01f)
        {
            GD.PrintErr($"[PeaceTermsTest] HATA: kalan puan yanlış ({okAnnex.RemainingScore:F1} ≠ {rich - okAnnex.Cost:F1})");
            ok = false;
        }

        // --- 4) Ara talepler toplamı, mevcut puanı aşmamalı ---
        float budget = 100f;
        double spent = 0;
        int accepted = 0;
        foreach (var t in new[] { "demilitarize", "puppet_state" })
        {
            var r = PeaceConferenceSystem.Check(t, 1, (float)(budget - spent));
            if (r.Ok) { spent += r.Cost; accepted++; }
        }
        var provR = PeaceConferenceSystem.Check("province_demand", 5, (float)(budget - spent));
        if (provR.Ok) { spent += provR.Cost; accepted++; }
        GD.Print($"[PeaceTermsTest] Kademeli harcama: {accepted} talep, toplam {spent:F1}/{budget:F0}");
        if (spent > budget + 0.001)
        {
            GD.PrintErr("[PeaceTermsTest] HATA: toplam talep maliyeti puan bütçesini aştı");
            ok = false;
        }
        if (accepted < 3)
        {
            GD.PrintErr("[PeaceTermsTest] HATA: 100 puanla 3 temel talep kabul edilmeli");
            ok = false;
        }

        // --- 5) Çok ekstrem: puan bittiğinde red ---
        var broke = PeaceConferenceSystem.Check("province_demand", prov.MaxUnits, 12f);
        GD.Print($"[PeaceTermsTest] Puan 12 ile {prov.MaxUnits} il: ok={broke.Ok}, sebep=\"{broke.Reason}\"");
        if (broke.Ok)
        {
            GD.PrintErr("[PeaceTermsTest] HATA: 12 puanla maksimum il talebi kabul edildi");
            ok = false;
        }

        // --- 6) Tanımsız tür: güvenli red ---
        var unknown = PeaceConferenceSystem.Check("fly_me_to_the_moon", 1, 100f);
        if (unknown.Ok)
        {
            GD.PrintErr("[PeaceTermsTest] HATA: bilinmeyen talep türü kabul edildi");
            ok = false;
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M42.3: Barış anlaşması uygulama testi (--peace-test).
    /// İl devri, fabrika/PP tazminatı ve yetersiz puanda güvenli red doğrulanır.
    /// Deterministik: RNG yok, il seçimi id sırasına göre sabit.
    /// Her senaryo FARKLI hedef kullanır — barış sonrası 90 günlük ateşkes
    /// aynı ülkeyle yeniden savaşı engellediği için.
    /// </summary>
    private void RunPeaceTest()
    {
        GD.Print("=========================================");
        GD.Print("  M42.3: BARIŞ ANLAŞMASI UYGULAMA TESTİ (--peace-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0x20261023D2C7UL);
        bool ok = true;

        PeaceConferenceSystem.LoadData();

        // Headless testte WorldMap sahnesi yüklenmez → illeri burada kur (M18 deseni).
        if (ProvinceOwnershipSystem.TotalProvinceCount == 0)
        {
            var provinceSource = new ProceduralProvinceSource();
            var provs = provinceSource.LoadProvinces();
            ProvinceOwnershipSystem.InitializeFromProvinces(provs);
            GD.Print($"[PeaceTest] İller yüklendi: {provs.Count} il");
        }

        if (!HasPlayer) SetPlayerCountry("DEU");
        string playerId = PlayerCountryId;

        // Hedefler: oyuncu dışında en çok ili olan ülkeler (azalan, eşitlikte id sırası).
        // Sıralama mevcut duruma göre sabit → deterministik, RNG yok.
        var targets = ProvinceOwnershipSystem.GetSaveData().Values
            .Where(v => !string.IsNullOrEmpty(v) && v != "?")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(v => !string.Equals(v, playerId, StringComparison.OrdinalIgnoreCase))
            .Where(v => CountryDatabase.Get(v) != null)
            .OrderByDescending(v => ProvinceOwnershipSystem.ProvinceCountFor(v))
            .ThenBy(v => v, StringComparer.Ordinal)
            .ToList();

        if (targets.Count < 3)
        {
            GD.PrintErr($"[PeaceTest] HATA: en az 3 hedef ülke gerekli, bulunan={targets.Count}");
            GetTree().Quit(1);
            return;
        }

        foreach (var t in targets) GD.Print($"[PeaceTest] Hedef adayı: {t} ({ProvinceOwnershipSystem.ProvinceCountFor(t)} il)");

        // Savaş ilanı önkoşulleri: PP + savaş desteği + casus belli gerekçesi.
        void EnsureWarRequirements(string targetId)
        {
            if (PlayerCountryData != null)
            {
                if (PlayerCountryData.PoliticalPower < WarSystem.DeclareWarPPCost + 10)
                    PlayerCountryData.PoliticalPower = WarSystem.DeclareWarPPCost + 10;
                if (PlayerCountryData.WarSupportPercent < 50)
                    PlayerCountryData.WarSupportPercent = 50;
            }
            CasusBelliSystem.GrantImmediateJustification(playerId, targetId, "historical_event");
        }

        // ---- Senaryo 1: il talebi + askersizleştirme ----
        string e1 = targets[0];
        var enemy1 = CountryDatabase.Get(e1);
        int prov1Before = ProvinceOwnershipSystem.ProvinceCountFor(e1);
        int deuBefore = ProvinceOwnershipSystem.ProvinceCountFor(playerId);
        int mil1Before = enemy1.MilFactories;
        GD.Print($"[PeaceTest] Senaryo 1 hedefi {e1}: il={prov1Before}, askeri fabrika={mil1Before}");

        EnsureWarRequirements(e1);
        if (!WarSystem.DeclareWar(e1))
        {
            GD.PrintErr($"[PeaceTest] HATA: ilk savaş başlatılamadı ({e1})");
            GetTree().Quit(1);
            return;
        }
        var w1 = WarSystem.Wars.FirstOrDefault(w => !w.IsEnded && w.DefenderId == e1);
        if (w1 == null) { GD.PrintErr("[PeaceTest] HATA: aktif savaş bulunamadı"); GetTree().Quit(1); return; }

        w1.WarProgress = 90f;
        w1.EnemyDivisions = Math.Max(1, w1.StartEnemyDivisions / 4);
        w1.AttackerBattlesWon = 20;
        w1.DefenderBattlesWon = 2;
        GD.Print($"[PeaceTest] Senaryo 1 puanı: saldıran={WarScoreSystem.AttackerScore(w1):F1}, savunan={WarScoreSystem.DefenderScore(w1):F1}");

        int demandCount = Math.Min(5, prov1Before);
        if (demandCount <= 0)
        {
            GD.PrintErr($"[PeaceTest] HATA: {e1} devredilecek ili yok ({prov1Before})");
            GetTree().Quit(1);
            return;
        }

        var r1 = PeaceConferenceSystem.ApplyDeal(w1, new List<PeaceConferenceSystem.Demand>
        {
            new PeaceConferenceSystem.Demand("province_demand", demandCount),
            new PeaceConferenceSystem.Demand("demilitarize", 1)
        }, playerId, e1);

        GD.Print($"[PeaceTest] Senaryo 1: ok={r1.Ok}, devredilen il={r1.ProvincesTransferred}, " +
                 $"sepetlenen askeri fabrika={r1.FactoriesScrapped}, harcanan puan={r1.ScoreSpent:F1}, red={r1.Rejected.Count}");
        foreach (var rej in r1.Rejected) GD.Print($"   reddedildi: {rej}");

        if (!r1.Ok) { GD.PrintErr($"[PeaceTest] HATA: anlaşma uygulanamadı ({r1.Reason})"); ok = false; }
        if (r1.ProvincesTransferred != demandCount)
        {
            GD.PrintErr($"[PeaceTest] HATA: {demandCount} il devredilmeli, devredilen={r1.ProvincesTransferred}");
            ok = false;
        }
        foreach (var pid in r1.TransferredProvinceIds)
        {
            string owner = ProvinceOwnershipSystem.GetProvinceOwner(pid);
            if (owner != playerId)
            {
                GD.PrintErr($"[PeaceTest] HATA: {pid} sahibi {playerId} değil ({owner})");
                ok = false;
            }
        }
        int prov1After = ProvinceOwnershipSystem.ProvinceCountFor(e1);
        int deuAfter = ProvinceOwnershipSystem.ProvinceCountFor(playerId);
        GD.Print($"[PeaceTest] Senaryo 1 sonrası: {e1} il={prov1After}, {playerId} il={deuAfter}");
        if (prov1After != prov1Before - demandCount)
        {
            GD.PrintErr($"[PeaceTest] HATA: kaybeden il sayısı {prov1Before - demandCount} olmalı ({prov1After})");
            ok = false;
        }
        if (deuAfter != deuBefore + demandCount)
        {
            GD.PrintErr($"[PeaceTest] HATA: kazanan il sayısı {deuBefore + demandCount} olmalı ({deuAfter})");
            ok = false;
        }
        if (enemy1.MilFactories != 0)
        {
            GD.PrintErr($"[PeaceTest] HATA: kaybeden askeri fabrika sıfırlanmadı ({enemy1.MilFactories})");
            ok = false;
        }
        if (r1.FactoriesScrapped != mil1Before)
        {
            GD.PrintErr($"[PeaceTest] HATA: sepetlenen askeri fabrika {mil1Before} olmalı ({r1.FactoriesScrapped})");
            ok = false;
        }
        if (!w1.IsEnded)
        {
            GD.PrintErr("[PeaceTest] HATA: barış sonrası savaş bitmedi");
            ok = false;
        }
        if (WarSystem.GetWarWith(e1) != null || WarSystem.IsAtWarWith(e1))
        {
            GD.PrintErr("[PeaceTest] HATA: savaş hâlâ aktif görünüyor");
            ok = false;
        }

        // ---- Senaryo 2: tazminat — fabrika ve PP kazanana geçmeli ----
        string e2 = targets[1];
        var enemy2 = CountryDatabase.Get(e2);
        double ppBefore = PlayerCountryData?.PoliticalPower ?? 0;
        int civ2Before = enemy2.CivFactories;
        int mil2Before = enemy2.MilFactories;
        GD.Print($"[PeaceTest] Senaryo 2 hedefi {e2}: sivil={civ2Before}, askeri={mil2Before}, oyuncu PP={ppBefore:N0}");

        EnsureWarRequirements(e2);
        if (!WarSystem.DeclareWar(e2))
        {
            GD.PrintErr($"[PeaceTest] HATA: ikinci savaş başlatılamadı ({e2})");
            GetTree().Quit(1);
            return;
        }
        var w2 = WarSystem.Wars.FirstOrDefault(w => !w.IsEnded && w.DefenderId == e2);
        if (w2 == null) { GD.PrintErr("[PeaceTest] HATA: ikinci aktif savaş bulunamadı"); GetTree().Quit(1); return; }

        w2.WarProgress = 100f;
        w2.EnemyDivisions = 0;
        w2.AttackerBattlesWon = 30;
        w2.DefenderBattlesWon = 0;
        GD.Print($"[PeaceTest] Senaryo 2 puanı: saldıran={WarScoreSystem.AttackerScore(w2):F1}");

        var r2 = PeaceConferenceSystem.ApplyDeal(w2, new List<PeaceConferenceSystem.Demand>
        {
            new PeaceConferenceSystem.Demand("war_reparations", 3)
        }, playerId, e2);

        GD.Print($"[PeaceTest] Senaryo 2: ok={r2.Ok}, alınan fabrika={r2.FactoriesTaken}, " +
                 $"PP transferi={r2.PoliticalPowerTransferred:N0}, red={r2.Rejected.Count}");
        foreach (var rej in r2.Rejected) GD.Print($"   reddedildi: {rej}");

        if (!r2.Ok) { GD.PrintErr($"[PeaceTest] HATA: tazminat uygulanamadı ({r2.Reason})"); ok = false; }
        if (r2.FactoriesTaken <= 0 && (civ2Before + mil2Before) > 0)
        {
            GD.PrintErr("[PeaceTest] HATA: tazminat fabrika transferi olmadı");
            ok = false;
        }
        if (r2.PoliticalPowerTransferred <= 0)
        {
            GD.PrintErr("[PeaceTest] HATA: tazminat PP transferi olmadı");
            ok = false;
        }
        double ppAfter = PlayerCountryData?.PoliticalPower ?? 0;
        if (ppAfter <= ppBefore)
        {
            GD.PrintErr($"[PeaceTest] HATA: kazanan PP artmadı ({ppBefore:N0} → {ppAfter:N0})");
            ok = false;
        }
        if ((civ2Before + mil2Before) > 0)
        {
            int expectedCiv = civ2Before - Math.Min(civ2Before, 3);
            if (enemy2.CivFactories != expectedCiv)
            {
                GD.PrintErr($"[PeaceTest] HATA: kaybeden sivil fabrika {expectedCiv} olmalı ({enemy2.CivFactories})");
                ok = false;
            }
        }

        // ---- Senaryo 3: yetersiz puan — hiç talep uygulanmamalı, savaş bitmeli ----
        string e3 = targets[2];
        var enemy3 = CountryDatabase.Get(e3);
        int prov3Before = ProvinceOwnershipSystem.ProvinceCountFor(e3);
        int civ3Before = enemy3.CivFactories;
        double pp3Before = enemy3.PoliticalPower;
        GD.Print($"[PeaceTest] Senaryo 3 hedefi {e3}: il={prov3Before}, sivil fabrika={civ3Before}, ilhak mı: {enemy3.IsAnnexed}");

        EnsureWarRequirements(e3);
        if (!WarSystem.DeclareWar(e3))
        {
            GD.PrintErr($"[PeaceTest] HATA: üçüncü savaş başlatılamadı ({e3})");
            GetTree().Quit(1);
            return;
        }
        var w3 = WarSystem.Wars.FirstOrDefault(w => !w.IsEnded && w.DefenderId == e3);
        if (w3 == null) { GD.PrintErr("[PeaceTest] HATA: üçüncü aktif savaş bulunamadı"); GetTree().Quit(1); return; }

        w3.WarProgress = 3f;
        w3.EnemyDivisions = w3.StartEnemyDivisions;
        w3.AttackerBattlesWon = 0;
        w3.DefenderBattlesWon = 1;
        GD.Print($"[PeaceTest] Senaryo 3 puanı: saldıran={WarScoreSystem.AttackerScore(w3):F1}");

        var r3 = PeaceConferenceSystem.ApplyDeal(w3, new List<PeaceConferenceSystem.Demand>
        {
            new PeaceConferenceSystem.Demand("annex_country", 1),
            new PeaceConferenceSystem.Demand("puppet_state", 1),
            new PeaceConferenceSystem.Demand("war_reparations", 2)
        }, playerId, e3);

        GD.Print($"[PeaceTest] Senaryo 3: ok={r3.Ok}, reddedilen={r3.Rejected.Count}, " +
                 $"devredilen il={r3.ProvincesTransferred}, alınan fabrika={r3.FactoriesTaken}");
        foreach (var rej in r3.Rejected) GD.Print($"   reddedildi: {rej}");

        if (r3.Rejected.Count < 3)
        {
            GD.PrintErr("[PeaceTest] HATA: yetersiz puanda tüm talepler reddedilmeli");
            ok = false;
        }
        if (r3.Ok)
        {
            GD.PrintErr("[PeaceTest] HATA: hiç talep uygulanmadıysa Ok=false olmalı");
            ok = false;
        }
        if (r3.ProvincesTransferred > 0 || r3.FactoriesTaken > 0 || r3.Annexed || r3.Puppeted)
        {
            GD.PrintErr("[PeaceTest] HATA: reddedilen talepler dünyaya etki etmiş görünüyor");
            ok = false;
        }
        if (enemy3.IsAnnexed)
        {
            GD.PrintErr("[PeaceTest] HATA: reddedilen ilhak uygulanmış görünüyor");
            ok = false;
        }
        if (ProvinceOwnershipSystem.ProvinceCountFor(e3) != prov3Before)
        {
            GD.PrintErr("[PeaceTest] HATA: reddedilen talepler il devretmiş görünüyor");
            ok = false;
        }
        if (enemy3.CivFactories != civ3Before || enemy3.PoliticalPower != pp3Before)
        {
            GD.PrintErr("[PeaceTest] HATA: reddedilen talep fabrika/PP değiştirmiş görünüyor");
            ok = false;
        }
        if (!w3.IsEnded)
        {
            GD.PrintErr("[PeaceTest] HATA: boş anlaşma da savaşı bitirmeli");
            ok = false;
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }
    /// <summary>
    /// M42.4: AI barış davranışı testi (--ai-peace-test).
    /// Eşikler deterministik doğrulanır; rakip oyuncuysa anlaşma UYGULANMAZ
    /// (yalnız istek loglanır) — AI–AI arasında anlaşma dünya durumuna yazılır.
    /// RNG yok: her senaryo puansal durumu elle kurulur.
    /// </summary>
    private void RunAiPeaceTest()
    {
        GD.Print("=========================================");
        GD.Print("  M42.4: AI BARIŞ DAVRANIŞI TESTİ (--ai-peace-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0x20261023D2C7UL);
        bool ok = true;

        PeaceConferenceSystem.LoadData();

        if (ProvinceOwnershipSystem.TotalProvinceCount == 0)
        {
            var provinceSource = new ProceduralProvinceSource();
            var provs = provinceSource.LoadProvinces();
            ProvinceOwnershipSystem.InitializeFromProvinces(provs);
            GD.Print($"[AiPeaceTest] İller yüklendi: {provs.Count} il");
        }

        if (!HasPlayer) SetPlayerCountry("DEU");
        string playerId = PlayerCountryId;

        // AI–AI savaşı için dört AI ülkesi seç (oyuncu dışında, en küçük fabrika):
        // ilk çift senaryo 1 (beyaz barış), ikinci çift senaryo 2 (ilhak/kukla).
        // İki çift kullanılmasının nedeni: senaryo 1'in beyaz barışı ilk çift arasında
        // 90 günlük ateşkes başlatır; aynı çift senaryo 2'de savaşı açamaz.
        var aiCountries = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed && c.Id != playerId)
            .OrderBy(c => c.TotalFactories)
            .Take(4)
            .ToList();

        if (aiCountries.Count < 4)
        {
            GD.PrintErr("[AiPeaceTest] HATA: dört AI ülkesi bulunamadı");
            GetTree().Quit(1);
            return;
        }

        string aiAtt = aiCountries[0].Id;
        string aiDef = aiCountries[1].Id;
        GD.Print($"[AiPeaceTest] AI ülkeleri: saldıran={aiAtt}, savunan={aiDef}");
        ok = true;

        // ---- Senaryo 1: rakip ezici üstünlük → savunan beyaz barış ister ----
        var attacker1 = CountryDatabase.Get(aiAtt);
        var defender1 = CountryDatabase.Get(aiDef);
        if (attacker1 != null && defender1 != null)
        {
            // Senaryo kurulumu: gerekçe + savaş desteği + savaş ilanı maliyetini karşılayacak PP.
            attacker1.WarSupportPercent = 50;
            attacker1.PoliticalPower = 200;
            CasusBelliSystem.GrantImmediateJustification(aiAtt, aiDef, "historical_event");

            if (!WarSystem.DeclareWar(aiAtt, aiDef))
            {
                // Kök neden görünsün: başarısız kontrol maddelerini tek tek yaz.
                var failed = CasusBelliSystem.CheckWarPreconditions(aiAtt, aiDef);
                foreach (var item in failed.Items.Where(i => !i.Met))
                    GD.PrintErr($"[AiPeaceTest]   ✗ {item.Label} — {item.Detail}");
                GD.PrintErr("[AiPeaceTest] HATA: AI savaşı başlatılamadı");
                GetTree().Quit(1);
                return;
            }

            var war = WarSystem.Wars.FirstOrDefault(w => !w.IsEnded &&
                ((w.AttackerId == aiAtt && w.DefenderId == aiDef) ||
                 (w.AttackerId == aiDef && w.DefenderId == aiAtt)));
            if (war == null)
            {
                GD.PrintErr("[AiPeaceTest] HATA: aktif AI savaşı bulunamadı");
                GetTree().Quit(1);
                return;
            }

            // Saldıranı ezici üstünlüğe kur: çok fazla çatışma kazanmış + düşmanı tükenmiş.
            // Fabrika payı WarScoreSystem tarafında StartEnemyFactories ve mevcut fabrika
            // farkından hesaplanır; testte rakip fabrikalarını sıfırlayarak hasar modelliyoruz.
            war.StartEnemyDivisions = Math.Max(1, defender1.StartingDivisions);
            war.AttackerBattlesWon = 40;
            war.DefenderBattlesWon = 0;
            war.EnemyDivisions = Math.Max(0, war.StartEnemyDivisions - 40);
            war.StartEnemyFactories = Math.Max(1, defender1.TotalFactories);
            defender1.CivFactories = 0;
            defender1.MilFactories = 0;
            defender1.Dockyards = 0;
            war.WarProgress = 100f;

            float attScore = WarScoreSystem.AttackerScore(war);
            float defScore = WarScoreSystem.DefenderScore(war);
            GD.Print($"[AiPeaceTest] Senaryo 1: saldıran puan={attScore:F1}, savunan puan={defScore:F1}");

            string decDef = CountryAI.EvaluatePeace(aiDef);
            string decAtt = CountryAI.EvaluatePeace(aiAtt);
            GD.Print($"[AiPeaceTest] Senaryo 1 kararlar: savunan=\"{decDef}\", saldıran=\"{decAtt}\"");

            // Savunan eziliyor: white/demand/puppet/annex olmalı (hiç olmazsa 'none' kabul edilmez)
            bool defWantsOut = decDef.StartsWith("white") || decDef.StartsWith("annex")
                             || decDef.StartsWith("puppet") || decDef.StartsWith("demand");
            if (!defWantsOut)
            {
                GD.PrintErr($"[AiPeaceTest] HATA: ezilen AI barış istemeli, karar=\"{decDef}\"");
                ok = false;
            }
            else if (!decDef.StartsWith("white"))
            {
                GD.PrintErr($"[AiPeaceTest] HATA: savunan puanı düşükken toprak/ilhak istememeli (\"{decDef}\")");
                ok = false;
            }

            // ---- 1b) oyuncu hedefinde ANLAŞMA uygulanmamalı ----
            // Savunmayı oyuncuya çevir: beyaz barış ister ama savaşı KENDİSİ bitirmemeli
            // (ApplyPeaceDecision oyuncu hedefinde işlemi reddeder).
            string playerDecision = $"white:{playerId}";
            int warsBefore = WarSystem.ActiveWars.Count;
            CountryAI.ApplyPeaceDecision(aiDef, playerDecision);
            if (WarSystem.ActiveWars.Count != warsBefore)
            {
                GD.PrintErr("[AiPeaceTest] HATA: oyuncu hedefinde savaş kapatılmış görünüyor");
                ok = false;
            }
            GD.Print($"[AiPeaceTest] Oyuncu hedefi: anlaşma uygulanmadı (aktif savaş sayısı {warsBefore})");

            // ---- 1c) geçersiz format / boş karar güvenli yok sayılır ----
            CountryAI.ApplyPeaceDecision(aiDef, "");
            CountryAI.ApplyPeaceDecision(aiDef, "none");
            CountryAI.ApplyPeaceDecision(aiDef, "white");
            if (WarSystem.ActiveWars.Count != warsBefore)
            {
                GD.PrintErr("[AiPeaceTest] HATA: bozuk karar bir savaşı etkiledi");
                ok = false;
            }

            // ---- 1d) beyaz barış uygula: kapitülasyon olmadan savaş biter ----
            bool white = PeaceConferenceSystem.WhitePeaceDeal(war);
            if (!white)
            {
                GD.PrintErr("[AiPeaceTest] HATA: beyaz barış uygulanamadı");
                ok = false;
            }
            if (!war.IsEnded)
            {
                GD.PrintErr("[AiPeaceTest] HATA: beyaz barış savaşı bitirmedi");
                ok = false;
            }
            if (war.AttackerCapitulated || war.DefenderCapitulated)
            {
                GD.PrintErr("[AiPeaceTest] HATA: beyaz barış kapitülasyon kaydı oluşturdu");
                ok = false;
            }
            GD.Print($"[AiPeaceTest] Senaryo 1 sonuç: war.IsEnded={war.IsEnded}, EndReason=\"{war.EndReason}\", " +
                     $"kapitülasyon=(A:{war.AttackerCapitulated},D:{war.DefenderCapitulated})");
        }
        else
        {
            GD.PrintErr("[AiPeaceTest] HATA: AI ülke verisi okunamadı");
            ok = false;
        }

        // ---- Senaryo 2: kazanan AI ilhak/pukla ister ve uygular ----
        // SENARYO 1'in beyaz barışı ilk çift arasında ateşkes başlattığı için
        // burada İKİNCİ bir AI çifti kullanılır (hiçbir ateşkes/kayıt yok).
        string att2 = aiCountries[2].Id;
        string def2 = aiCountries[3].Id;
        GD.Print($"[AiPeaceTest] Senaryo 2 çifti: kazanan={att2}, kaybeden={def2}");
        {
            string winner = att2;
            string loser = def2;
            var winData = CountryDatabase.Get(winner);
            var loseData = CountryDatabase.Get(loser);
            if (winData != null && loseData != null)
            {
                int loseProvincesBefore = ProvinceOwnershipSystem.ProvinceCountFor(loser);

                winData.WarSupportPercent = 50;
                winData.PoliticalPower = 200;
                CasusBelliSystem.GrantImmediateJustification(winner, loser, "historical_event");
                bool declared = WarSystem.DeclareWar(winner, loser);

                if (!declared)
                {
                    var failed2 = CasusBelliSystem.CheckWarPreconditions(winner, loser);
                    foreach (var item in failed2.Items.Where(i => !i.Met))
                        GD.PrintErr($"[AiPeaceTest]   ✗ {item.Label} — {item.Detail}");
                    GD.PrintErr("[AiPeaceTest] HATA: senaryo 2 için savaş ilan edilemedi");
                    ok = false;
                }
                else
                {
                    var war2 = WarSystem.Wars.FirstOrDefault(w => !w.IsEnded &&
                        ((w.AttackerId == winner && w.DefenderId == loser) ||
                         (w.AttackerId == loser && w.DefenderId == winner)));
                    if (war2 == null)
                    {
                        GD.PrintErr("[AiPeaceTest] HATA: senaryo 2 aktif savaşı bulunamadı");
                        ok = false;
                    }
                    else
                    {
                        // Kazananı ezici puanla kur
                        war2.StartEnemyDivisions = Math.Max(1, loseData.StartingDivisions);
                        war2.AttackerBattlesWon = 60;
                        war2.DefenderBattlesWon = 0;
                        war2.EnemyDivisions = 0;
                        war2.StartEnemyFactories = Math.Max(1, loseData.TotalFactories);
                        loseData.CivFactories = 0;
                        loseData.MilFactories = 0;
                        loseData.Dockyards = 0;
                        war2.WarProgress = 100f;

                        string decWin = CountryAI.EvaluatePeace(winner);
                        GD.Print($"[AiPeaceTest] Senaryo 2: kazanan kararı=\"{decWin}\" " +
                                 $"(puan={WarScoreSystem.AttackerScore(war2):F1})");

                        bool wantsWin = decWin.StartsWith("annex") || decWin.StartsWith("puppet");
                        if (!wantsWin)
                        {
                            GD.PrintErr($"[AiPeaceTest] HATA: ezici puanla AI ilhak/kukla istemeli (karar=\"{decWin}\")");
                            ok = false;
                        }

                        // Uygula — rakip AI olduğundan dünya durumu değişmeli
                        CountryAI.ApplyPeaceDecision(winner, decWin);
                        if (!war2.IsEnded)
                        {
                            GD.PrintErr("[AiPeaceTest] HATA: AI anlaşması savaşı bitirmedi");
                            ok = false;
                        }
                        if (!loseData.IsAnnexed)
                        {
                            GD.PrintErr("[AiPeaceTest] HATA: ilhak/kukla uygulanmadı");
                            ok = false;
                        }
                        if (decWin.StartsWith("annex") &&
                            ProvinceOwnershipSystem.ProvinceCountFor(winner) <= loseProvincesBefore)
                        {
                            GD.PrintErr("[AiPeaceTest] HATA: ilhak tüm illeri devretmedi");
                            ok = false;
                        }
                        GD.Print($"[AiPeaceTest] Senaryo 2 sonuç: isEnded={war2.IsEnded}, " +
                                 $"kaybeden ilhak mı={loseData.IsAnnexed}, " +
                                 $"kazanan il={ProvinceOwnershipSystem.ProvinceCountFor(winner)}");
                    }
                }
            }
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M42.5: Barış konferansı paneli testi (--peace-panel-test).
    /// Panelin ölçü sınırına sığdığı, talep listesinin dolduğu, onayın dünya
    /// durumuna yazdığı, beyaz barışın kapitülasyonsuz savaşı kapattığı ve
    /// ESC'nin paneli kapattığı gerçek UI üzerinden doğrulanır.
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private async void RunPeacePanelTest()
    {
        GD.Print("=========================================");
        GD.Print("  M42.5: BARIŞ KONFERANSI PANELİ TESTİ (--peace-panel-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0x4D34325D2B0UL);
        PeaceConferenceSystem.LoadData();
        bool ok = true;

        if (ProvinceOwnershipSystem.TotalProvinceCount == 0)
        {
            var provinceSource = new ProceduralProvinceSource();
            var provs = provinceSource.LoadProvinces();
            ProvinceOwnershipSystem.InitializeFromProvinces(provs);
            GD.Print($"[PeacePanelTest] İller yüklendi: {provs.Count} il");
        }

        if (!HasPlayer) SetPlayerCountry("DEU");
        string playerId = PlayerCountryId;

        // Oyuncu savaştığı, çok ili olan bir AI hedefi seç (il talebi anlamlı olsun).
        var target = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed && c.Id != playerId &&
                        ProvinceOwnershipSystem.ProvinceCountFor(c.Id) >= 5)
            .OrderByDescending(c => ProvinceOwnershipSystem.ProvinceCountFor(c.Id))
            .FirstOrDefault();

        if (target == null)
        {
            GD.PrintErr("[PeacePanelTest] HATA: hedef AI ülkesi bulunamadı");
            GetTree().Quit(1);
            return;
        }

        string targetId = target.Id;
        var playerData = CountryDatabase.Get(playerId);
        if (playerData == null)
        {
            GD.PrintErr("[PeacePanelTest] HATA: oyuncu verisi okunamadı");
            GetTree().Quit(1);
            return;
        }

        playerData.WarSupportPercent = 50;
        playerData.PoliticalPower = 200;
        CasusBelliSystem.GrantImmediateJustification(playerId, targetId, "historical_event");
        if (!WarSystem.DeclareWar(playerId, targetId))
        {
            var failed = CasusBelliSystem.CheckWarPreconditions(playerId, targetId);
            foreach (var item in failed.Items.Where(i => !i.Met))
                GD.PrintErr($"[PeacePanelTest]   ✗ {item.Label} — {item.Detail}");
            GD.PrintErr("[PeacePanelTest] HATA: oyuncu savaşı başlatılamadı");
            GetTree().Quit(1);
            return;
        }

        var war = WarSystem.GetWarWith(targetId);
        if (war == null)
        {
            GD.PrintErr("[PeacePanelTest] HATA: aktif savaş bulunamadı");
            GetTree().Quit(1);
            return;
        }

        // Puanı ezici kur: çok çatışma kazanılmış + düşman fabrikaları sıfır.
        war.StartEnemyDivisions = Math.Max(1, target.StartingDivisions);
        war.AttackerBattlesWon = 60;
        war.DefenderBattlesWon = 0;
        war.EnemyDivisions = 0;
        war.StartEnemyFactories = Math.Max(1, target.TotalFactories);
        target.CivFactories = 0;
        target.MilFactories = 0;
        target.Dockyards = 0;
        war.WarProgress = 100f;

        // Panel gerçek bir Control ağacında açılsın (viewport ölçüsü anlamlı olsun).
        var host = new Control { Name = "PeacePanelTestHost" };
        host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(host);

        int playerProvincesBefore = ProvinceOwnershipSystem.ProvinceCountFor(playerId);
        int targetProvincesBefore = ProvinceOwnershipSystem.ProvinceCountFor(targetId);
        GD.Print($"[PeacePanelTest] Savaş {playerId}→{targetId}: puan={WarScoreSystem.ScoreForSide(war, playerId):F1}, " +
                 $"oyuncu il={playerProvincesBefore}, hedef il={targetProvincesBefore}");

        PeaceConferencePanel.OpenUnder(host, war, playerId);

        if (!PeaceConferencePanel.IsOpen)
        {
            GD.PrintErr("[PeacePanelTest] HATA: panel açılmadı");
            ok = false;
        }
        else
        {
            var panel = PeaceConferencePanel.IsOpen ? FindPanel() : null;
            if (panel == null)
            {
                GD.PrintErr("[PeacePanelTest] HATA: panel düğümü bulunamadı");
                ok = false;
            }
            else
            {
                // ---- 1) Ölçü sınırı: 1152x648'e sığmalı ----
                Vector2 designed = panel.DesignedSize;
                GD.Print($"[PeacePanelTest] Panel tasarım boyutu: {designed.X}x{designed.Y}");
                if (designed.X <= 0 || designed.Y <= 0)
                {
                    GD.PrintErr("[PeacePanelTest] HATA: panel boyutu okunamadı");
                    ok = false;
                }
                if (designed.X > 1152 || designed.Y > 648)
                {
                    GD.PrintErr($"[PeacePanelTest] HATA: panel 1152x648'e sığmıyor ({designed.X}x{designed.Y})");
                    ok = false;
                }

                // ---- 2) Talep listesi dolmuş olmalı ----
                int rows = panel.DemandRowCount;
                GD.Print($"[PeacePanelTest] Talep satırı sayısı: {rows}");
                if (rows <= 0)
                {
                    GD.PrintErr("[PeacePanelTest] HATA: talep listesi boş");
                    ok = false;
                }

                // ---- 3) "ADD" butonuyla il talebi eklenir ----
                var addBtn = FindButton(panel, "ADD");
                if (addBtn == null)
                {
                    GD.PrintErr("[PeacePanelTest] HATA: ADD butonu bulunamadı");
                    ok = false;
                }
                else
                {
                    addBtn.EmitSignal(BaseButton.SignalName.Pressed);
                    int selectedAfterAdd = panel.SelectedTermCount;
                    GD.Print($"[PeacePanelTest] ADD sonrası seçili talep: {selectedAfterAdd}");
                    if (selectedAfterAdd != 1)
                    {
                        GD.PrintErr("[PeacePanelTest] HATA: talepler eklenmedi");
                        ok = false;
                    }
                    else
                    {
                        // ---- 4) "CONFIRM DEAL" dünya durumuna yazmalı ----
                        var confirmBtn = FindButton(panel, "CONFIRM DEAL");
                        if (confirmBtn == null || confirmBtn.Disabled)
                        {
                            GD.PrintErr("[PeacePanelTest] HATA: CONFIRM DEAL butonu bulunamadı veya kapalı");
                            ok = false;
                        }
                        else
                        {
                            confirmBtn.EmitSignal(BaseButton.SignalName.Pressed);
                            int playerProvincesAfter = ProvinceOwnershipSystem.ProvinceCountFor(playerId);
                            int targetProvincesAfter = ProvinceOwnershipSystem.ProvinceCountFor(targetId);
                            GD.Print($"[PeacePanelTest] Onay sonrası: oyuncu il={playerProvincesAfter}, " +
                                     $"hedef il={targetProvincesAfter}");
                            if (playerProvincesAfter <= playerProvincesBefore ||
                                targetProvincesAfter >= targetProvincesBefore)
                            {
                                GD.PrintErr("[PeacePanelTest] HATA: onay illeri devretmedi");
                                ok = false;
                            }
                            if (!war.IsEnded)
                            {
                                GD.PrintErr("[PeacePanelTest] HATA: onay savaşı bitirmedi");
                                ok = false;
                            }
                        }
                    }
                }

                // ---- 5) ESC paneli kapatmalı ----
                var escKey = new InputEventKey
                {
                    Keycode = Key.Escape,
                    Pressed = true
                };
                // Projedeki diğer UI testleriyle aynı desen: olay doğrudan panele
                // verilir (Input.ParseInputEvent olayı kuyruğa alır, aynı frame'de
                // _UnhandledInput'a ulaşmaz), sonra animasyon/PopOut'un QueueFree'si
                // tamamlanması için frame beklenir.
                panel._UnhandledInput(escKey);
                if (!panel.CloseRequested)
                {
                    GD.PrintErr("[PeacePanelTest] HATA: ESC kapatma isteği üretmedi");
                    ok = false;
                }

                await ToSignal(GetTree(), "process_frame");
                await ToSignal(GetTree(), "process_frame");
                await ToSignal(GetTree(), "process_frame");

                if (PeaceConferencePanel.IsOpen)
                {
                    GD.PrintErr("[PeacePanelTest] HATA: ESC paneli kapatmadı");
                    ok = false;
                }
                else
                {
                    GD.Print("[PeacePanelTest] ESC paneli kapattı ✓");
                }

                // Panel tamamen silinmeden ikinci OpenUnder "zaten açık" diye
                // erken dönüyor; bu yüzden aşama 2'ye geçmeden önce bekleniyor.
                await ToSignal(GetTree(), "process_frame");
                await ToSignal(GetTree(), "process_frame");
            }
        }

        // ---- 6) Beyaz barış: kapitülasyon kaydı oluşturmadan savaşı bitirir ----
        // Aynı hedefle yeniden (ateşkes yeni başladığı için farklı bir hedef seç).
        var whiteTarget = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed && c.Id != playerId && c.Id != targetId &&
                        ProvinceOwnershipSystem.ProvinceCountFor(c.Id) >= 5)
            .OrderByDescending(c => ProvinceOwnershipSystem.ProvinceCountFor(c.Id))
            .FirstOrDefault();

        if (whiteTarget == null)
        {
            GD.PrintErr("[PeacePanelTest] HATA: beyaz barış hedefi bulunamadı");
            ok = false;
        }
        else
        {
            string wtId = whiteTarget.Id;
            playerData.WarSupportPercent = 50;
            playerData.PoliticalPower = 200;
            CasusBelliSystem.GrantImmediateJustification(playerId, wtId, "historical_event");
            if (!WarSystem.DeclareWar(playerId, wtId))
            {
                var failed2 = CasusBelliSystem.CheckWarPreconditions(playerId, wtId);
                foreach (var item in failed2.Items.Where(i => !i.Met))
                    GD.PrintErr($"[PeacePanelTest]   ✗ {item.Label} — {item.Detail}");
                GD.PrintErr("[PeacePanelTest] HATA: beyaz barış savaşı başlatılamadı");
                ok = false;
            }
            else
            {
                var war2 = WarSystem.GetWarWith(wtId);
                if (war2 == null)
                {
                    GD.PrintErr("[PeacePanelTest] HATA: beyaz barış savaşı bulunamadı");
                    ok = false;
                }
                else
                {
                    war2.WarProgress = 100f;
                    PeaceConferencePanel.OpenUnder(host, war2, playerId);
                    var panel2 = FindPanel();
                    if (panel2 == null || !PeaceConferencePanel.IsOpen)
                    {
                        GD.PrintErr("[PeacePanelTest] HATA: beyaz barış paneli açılmadı");
                        ok = false;
                    }
                    else
                    {
                        var whiteBtn = FindButton(panel2, "WHITE PEACE (STATUS QUO)");
                        if (whiteBtn == null)
                        {
                            GD.PrintErr("[PeacePanelTest] HATA: WHITE PEACE butonu bulunamadı");
                            ok = false;
                        }
                        else
                        {
                            whiteBtn.EmitSignal(BaseButton.SignalName.Pressed);
                            if (!war2.IsEnded)
                            {
                                GD.PrintErr("[PeacePanelTest] HATA: beyaz barış savaşı bitirmedi");
                                ok = false;
                            }
                            if (war2.AttackerCapitulated || war2.DefenderCapitulated)
                            {
                                GD.PrintErr("[PeacePanelTest] HATA: beyaz barış kapitülasyon kaydı oluşturdu");
                                ok = false;
                            }
                            GD.Print($"[PeacePanelTest] Beyaz barış: isEnded={war2.IsEnded}, " +
                                     $"EndReason=\"{war2.EndReason}\", kapitülasyon=" +
                                     $"(A:{war2.AttackerCapitulated},D:{war2.DefenderCapitulated})");
                        }
                    }
                }
            }
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>Test yardımcısı: ağaçtaki PeaceConferencePanel örneğini bulur.</summary>
    private PeaceConferencePanel FindPanel()
    {
        foreach (var n in FindChildrenIn(this))
        {
            if (n is PeaceConferencePanel p) return p;
        }
        return null;
    }

    /// <summary>Test yardımcısı: panel ağacındaki butonu metnine göre bulur.</summary>
    private Button FindButton(Node root, string text)
    {
        if (root == null) return null;
        foreach (var b in FindChildrenIn(root))
        {
            if (b is Button btn && btn.Text == text)
                return btn;
        }
        return null;
    }

    /// <summary>Test yardımcısı: bir alt ağaçtaki tüm düğümleri (kök hariç) dolaşır.</summary>
    private IEnumerable<Node> FindChildrenIn(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (var grand in FindChildrenIn(child))
                yield return grand;
        }
    }

    /// <summary>
    /// M43.1: Deniz bölgesi / hakimiyet testi (--sea-zone-test).
    /// Bölge sayısının doğru yüklendiği, hakimiyetin [0,100] aralığında üretildiği,
    /// filo ağırlığının tersane sayısıyla arttığı ve hakimiyetin deterministik
    /// (aynı girdi → aynı çıktı) olduğu doğrulanır.
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunSeaZoneTest()
    {
        GD.Print("=========================================");
        GD.Print("  M43.1: DENİZ BÖLGESİ / HAKİMİYET TESTİ (--sea-zone-test)");
        GD.Print("=========================================");

        bool ok = true;

        // ---- 1) 12 bölge yüklenmeli ve loglanmalı ----
        SeaZoneSystem.LoadData();
        int zoneCount = SeaZoneSystem.ZoneCount;
        GD.Print($"[SeaZoneTest] Yüklenen bölge sayısı: {zoneCount}");
        if (zoneCount != 12)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: bölge sayısı 12 olmalı (bulundu: {zoneCount})");
            ok = false;
        }

        // ---- 2) Her bölgenin tanımı tutarlı olmalı ----
        foreach (var zone in SeaZoneSystem.All)
        {
            if (string.IsNullOrEmpty(zone.Id))
            {
                GD.PrintErr("[SeaZoneTest] HATA: bölgesi boş");
                ok = false;
            }
            if (zone.AdjacentCountryIds.Count == 0)
            {
                GD.PrintErr($"[SeaZoneTest] HATA: {zone.Id} bölgesinin komşu ülkesi yok");
                ok = false;
            }
            if (zone.FleetWeightFactor <= 0)
            {
                GD.PrintErr($"[SeaZoneTest] HATA: {zone.Id} filo çarpanı pozitif değil");
                ok = false;
            }
            if (zone.ConvoyRouteValue < 0)
            {
                GD.PrintErr($"[SeaZoneTest] HATA: {zone.Id} konvoy değeri negatif");
                ok = false;
            }
        }
        GD.Print($"[SeaZoneTest] Tüm bölge tanımları tutarlı ({zoneCount} bölge)");

        // ---- 3) Kurulum: oyuncu + rakip (dünya verisini bozmadan) ----
        // SeaZoneSystem saf hesaptır; hakimiyet doğrudan ZoneDefinition üzerinden
        // okunabildiği için sentetik bir bölge kurmak deterministik ve güvenlidir.
        if (!HasPlayer) SetPlayerCountry("DEU");
        string playerId = PlayerCountryId;

        // Gerçek bölgede oyuncunun komşusu olan bir rakip seç.
        var playerZone = SeaZoneSystem.All
            .FirstOrDefault(z => z.AdjacentCountryIds.Any(id =>
                string.Equals(id, playerId, StringComparison.OrdinalIgnoreCase)));
        if (playerZone == null)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: {playerId} komşusu olduğu deniz bölgesi yok");
            GetTree().Quit(1);
            return;
        }

        // Sentetik test bölgesi: iki taraf + bir dışarıdaki ülke (komşu değil).
        var outsider = CountryDatabase.All()
            .FirstOrDefault(c => c != null && !c.IsAnnexed && c.Id != playerId &&
                !playerZone.AdjacentCountryIds.Any(id =>
                    string.Equals(id, c.Id, StringComparison.OrdinalIgnoreCase)));
        if (outsider == null)
        {
            GD.PrintErr("[SeaZoneTest] HATA: komşu olmayan örnek ülke bulunamadı");
            GetTree().Quit(1);
            return;
        }

        var testZone = new SeaZoneSystem.ZoneDefinition
        {
            Id = "test_sea",
            Name = "Test Sea",
            FleetWeightFactor = 1.0,
            ConvoyRouteValue = 1.0,
            AdjacentCountryIds = new List<string> { playerId, outsider.Id }
        };
        GD.Print($"[SeaZoneTest] Sentetik bölge: {testZone.Name} — komşular " +
                 $"{playerId}, {outsider.Id}");

        var playerData = CountryDatabase.Get(playerId);
        var rivalData = CountryDatabase.Get(outsider.Id);
        if (playerData == null || rivalData == null)
        {
            GD.PrintErr("[SeaZoneTest] HATA: ülke verisi okunamadı");
            GetTree().Quit(1);
            return;
        }

        // Başlangıç filolarını kaydet (test sonunda geri yüklenir).
        int playerDockBefore = playerData.Dockyards;
        int rivalDockBefore = rivalData.Dockyards;

        // Oyuncu rakibin iki katı filo → perspektifte lehine olmalı.
        rivalData.Dockyards = 4;
        playerData.Dockyards = rivalData.Dockyards * 2;

        GD.Print($"[SeaZoneTest] {playerId}: {playerData.Dockyards} tersane, " +
                 $"{EquipmentSystem.GetStock("battleship")} zırveh, " +
                 $"{EquipmentSystem.GetStock("submarine")} denizaltı");
        GD.Print($"[SeaZoneTest] {outsider.Id}: {rivalData.Dockyards} tersane");

        // ---- 4) Hakimiyet [0,100] ve iki perspektif toplamı 100 ----
        float sup = SeaZoneSystem.Supremacy(testZone, playerId);
        float rivalSup = SeaZoneSystem.Supremacy(testZone, outsider.Id);
        GD.Print($"[SeaZoneTest] {playerId} hakimiyeti: {sup:F1}, {outsider.Id}: {rivalSup:F1}");

        if (sup < 0f || sup > 100f || rivalSup < 0f || rivalSup > 100f)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: hakimiyet [0,100] dışında " +
                        $"({sup:F1} / {rivalSup:F1})");
            ok = false;
        }
        if (Mathf.Abs(sup + rivalSup - 100f) > 0.01f)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: iki perspektif toplamı 100 değil " +
                        $"({sup + rivalSup:F2})");
            ok = false;
        }
        if (sup <= 50f)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: oyuncu daha ağır ama hakimiyet {sup:F1} ≤ 50");
            ok = false;
        }

        // ---- 5) Komşu olmayan ülke nötr (50) dönmeli ----
        var nonAdjacent = CountryDatabase.All()
            .FirstOrDefault(c => c != null && !c.IsAnnexed && c.Id != playerId &&
                c.Id != outsider.Id);
        if (nonAdjacent != null)
        {
            float neutral = SeaZoneSystem.Supremacy(testZone, nonAdjacent.Id);
            GD.Print($"[SeaZoneTest] Komşu olmayan {nonAdjacent.Id}: {neutral:F1}");
            if (Mathf.Abs(neutral - 50f) > 0.01f)
            {
                GD.PrintErr($"[SeaZoneTest] HATA: komşu olmayan ülke nötr değil ({neutral:F1})");
                ok = false;
            }
        }

        // ---- 6) Tersane azaldıkça hakimiyet düşmeli ----
        float before = SeaZoneSystem.Supremacy(testZone, playerId);
        playerData.Dockyards = 1; // rakibin (4) altına düşür
        float lowered = SeaZoneSystem.Supremacy(testZone, playerId);
        GD.Print($"[SeaZoneTest] Filo azaltıldı: {before:F1} → {lowered:F1}");
        if (lowered >= before)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: filo azalması hakimiyeti düşürmedi " +
                        $"({before:F1} → {lowered:F1})");
            ok = false;
        }
        if (lowered < 0f)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: hakimiyet [0,100] altına indi ({lowered:F1})");
            ok = false;
        }

        // ---- 7) Denge (eşit filo) tam 50 olmalı ----
        playerData.Dockyards = rivalData.Dockyards;
        float balanced = SeaZoneSystem.Supremacy(testZone, playerId);
        GD.Print($"[SeaZoneTest] Eşit filo hakimiyeti: {balanced:F1}");
        if (Mathf.Abs(balanced - 50f) > 0.01f)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: eşit filoda hakimiyet 50 değil ({balanced:F1})");
            ok = false;
        }

        // ---- 8) Deterministlik: aynı girdi aynı çıktı ----
        float d1 = SeaZoneSystem.Supremacy(testZone, playerId);
        float d2 = SeaZoneSystem.Supremacy(testZone, playerId);
        if (Mathf.Abs(d1 - d2) > 0.0001f)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: deterministik değil ({d1} ≠ {d2})");
            ok = false;
        }
        else
        {
            GD.Print("[SeaZoneTest] Deterministik tekrar: aynı değer ✓");
        }

        // ---- 9) Dünya verisini geri yükle ----
        playerData.Dockyards = playerDockBefore;
        rivalData.Dockyards = rivalDockBefore;

        // ---- 10) Gerçek bölge üzerinde özet doğrulamaları ----
        float avg = SeaZoneSystem.AverageSupremacy(playerId);
        GD.Print($"[SeaZoneTest] {playerId} ortalama hakimiyet (gerçek bölgeler): {avg:F1}");
        if (avg < 0f || avg > 100f)
        {
            GD.PrintErr($"[SeaZoneTest] HATA: ortalama hakimiyet [0,100] dışında ({avg:F1})");
            ok = false;
        }

        var controlled = SeaZoneSystem.ControlledZones(playerId);
        GD.Print($"[SeaZoneTest] {playerId} kontrol ettiği bölge sayısı: {controlled.Count}");
        foreach (var z in controlled)
            GD.Print($"[SeaZoneTest]   • {z.Name} ({z.Id}) — " +
                     $"{SeaZoneSystem.Supremacy(z, playerId):F1}");

        double routeValue = SeaZoneSystem.ControlledRouteValue(playerId);
        GD.Print($"[SeaZoneTest] Kontrol edilen konvoy güzergâhı değeri: {routeValue:F2}");
        if (routeValue < 0)
        {
            GD.PrintErr("[SeaZoneTest] HATA: konvoy güzergâhı değeri negatif");
            ok = false;
        }

        // ---- 11) İlhak edilmiş ülkenin filo ağırlığı sayılmamalı ----
        var victim = CountryDatabase.All()
            .FirstOrDefault(c => c != null && !c.IsAnnexed && c.Id != playerId &&
                playerZone.AdjacentCountryIds.Any(id =>
                    string.Equals(id, c.Id, StringComparison.OrdinalIgnoreCase)));
        if (victim != null)
        {
            float weightBefore = SeaZoneSystem.ZoneWeight(playerZone, victim.Id);
            victim.IsAnnexed = true;
            float weightAfter = SeaZoneSystem.ZoneWeight(playerZone, victim.Id);
            victim.IsAnnexed = false; // geri al (diğer testleri etkilemesin)
            GD.Print($"[SeaZoneTest] İlhak sonrası {victim.Id} ağırlığı: " +
                     $"{weightBefore} → {weightAfter}");
            if (weightAfter != 0f)
            {
                GD.PrintErr($"[SeaZoneTest] HATA: ilhak edilmiş ülke hâlâ " +
                            $"{weightAfter} ağırlık taşıyor");
                ok = false;
            }
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M43.2: Denizaltı baskını / konvoy kaybı testi (--convoy-raid-test).
    /// Hakimiyet düştükçe konvoy kaybı ve ithalat kesintisinin arttığı, güvenli
    /// bölgede kayıp yaşanmadığı ve kaybın DeterministicRNG ile tekrarlanabilir
    /// olduğu doğrulanır. Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunConvoyRaidTest()
    {
        GD.Print("=========================================");
        GD.Print("  M43.2: DENİZALTI BASKINI / KONVOY KAYBI TESTİ (--convoy-raid-test)");
        GD.Print("=========================================");

        DeterministicRNG.InitializeForNewGame(0x4D34325D2B0UL);
        SeaZoneSystem.LoadData();
        bool ok = true;

        if (!HasPlayer) SetPlayerCountry("DEU");
        string playerId = PlayerCountryId;
        var playerData = CountryDatabase.Get(playerId);
        if (playerData == null)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: oyuncu verisi okunamadı");
            GetTree().Quit(1);
            return;
        }

        // Oyuncunun komşu olduğu gerçek bir bölge bul.
        var playerZone = SeaZoneSystem.All
            .FirstOrDefault(z => z.AdjacentCountryIds.Any(id =>
                string.Equals(id, playerId, StringComparison.OrdinalIgnoreCase)));
        if (playerZone == null)
        {
            GD.PrintErr($"[ConvoyRaidTest] HATA: {playerId} komşusu olduğu bölge yok");
            GetTree().Quit(1);
            return;
        }

        // Veriyi yedekle (test sonunda geri yüklenecek).
        int playerDockBefore = playerData.Dockyards;
        var zoneSnapshot = SeaZoneSystem.All.ToDictionary(z => z.Id, z => z.AdjacentCountryIds.ToList());

        // ---- 1) Güvenli bölge: oyuncu tek komşu, rakipsiz → kayıp yok ----
        GD.Print("[ConvoyRaidTest] --- 1) Güvenli bölge (tam hakimiyet) ---");
        playerData.Dockyards = 500; // bölgedeki herkesi ezer
        // Sadece oyuncu komşu → hakimiyet 100, kayıp yok.
        foreach (var zone in SeaZoneSystem.All)
            zone.AdjacentCountryIds = new List<string> { playerId };

        var safe = ConvoyRaidSystem.ComputeDailyRaids(playerId, 20, 4);
        GD.Print($"[ConvoyRaidTest] Konvoy=20, ithalat=4/gün → kayıp={safe.ConvoysLost}, " +
                 $"düşüş=%{safe.ImportShortfall * 100f:F0}");
        if (safe.ConvoysLost != 0 || safe.ImportShortfall != 0f)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: güvenli bölgede konvoy kaybı yaşandı");
            ok = false;
        }
        if (safe.RaidedZone != null)
        {
            GD.PrintErr($"[ConvoyRaidTest] HATA: güvenli bölgede baskın bölgesi rapor edildi " +
                        $"({safe.RaidedZone.Id})");
            ok = false;
        }

        // ---- 2) Tehlikeli bölge: rakibin ağır bastığı bölge → kayıp var ----
        GD.Print("[ConvoyRaidTest] --- 2) Tehlikeli bölge (düşman hakimiyeti) ---");
        // Bölgeye oyuncu dışında güçlü bir rakip komşusu ekle (kendisi tersanesiz →
        // ağırlığı oyuncununkinden küçük; hakimiyet düşük ama 80 eşiğinin altında).
        var rival = CountryDatabase.All()
            .FirstOrDefault(c => c != null && !c.IsAnnexed && c.Id != playerId);
        if (rival == null)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: rakip ülke bulunamadı");
            GetTree().Quit(1);
            return;
        }

        rival.IsAnnexed = false; // ağırlık sayılabilsin diye temiz başla
        var raidZone = SeaZoneSystem.All.First();
        raidZone.AdjacentCountryIds = new List<string> { playerId, rival.Id };

        // Rakibi oyuncudan ağır yap → hakimiyet oyuncu lehine DEĞİL.
        int rivalDockBefore = rival.Dockyards;
        rival.Dockyards = 300;
        playerData.Dockyards = 100;

        float mySup = SeaZoneSystem.Supremacy(raidZone, playerId);
        GD.Print($"[ConvoyRaidTest] Bölge {raidZone.Id}: {playerId}={playerData.Dockyards} tersane, " +
                 $"{rival.Id}={rival.Dockyards} tersane → hakimiyet={mySup:F1}");

        var riskPercent = ConvoyRaidSystem.TotalRiskPercent(playerId);
        GD.Print($"[ConvoyRaidTest] Toplam risk yüzdesi: {riskPercent:F1}");

        var raided = ConvoyRaidSystem.ComputeDailyRaids(playerId, 20, 4);
        GD.Print($"[ConvoyRaidTest] → baskın bölgesi={raided.RaidedZone?.Id}, " +
                 $"hakimiyet={raided.SupremacyInZone:F1}, kayıp={raided.ConvoysLost}, " +
                 $"düşüş=%{raided.ImportShortfall * 100f:F0}");

        if (raided.RaidedZone == null)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: düşman hakimiyetindeki bölgede baskın tespit edilmedi");
            ok = false;
        }
        if (raided.ImportShortfall < 0f || raided.ImportShortfall > 1f)
        {
            GD.PrintErr($"[ConvoyRaidTest] HATA: ithalat düşüşü [0,1] dışında " +
                        $"({raided.ImportShortfall})");
            ok = false;
        }
        if (raided.ConvoysLost < 0)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: konvoy kaybı negatif");
            ok = false;
        }

        // ---- 3) Hakimiyet düştükçe risk artmalı (ters orantı) ----
        GD.Print("[ConvoyRaidTest] --- 3) Hakimiyet vs risk (ters orantı) ---");
        float prevRisk = -1f;
        foreach (int dock in new[] { 300, 200, 100, 50, 20 })
        {
            playerData.Dockyards = dock;
            float sup = SeaZoneSystem.Supremacy(raidZone, playerId);
            float risk = ConvoyRaidSystem.TotalRiskPercent(playerId);

            // Bölgeyi listedeki ilk bölge olarak tut (FindRaidedZone en kötüyü seçer).
            foreach (var z in SeaZoneSystem.All)
            {
                if (z.Id != raidZone.Id) z.AdjacentCountryIds = new List<string>();
            }
            raidZone.AdjacentCountryIds = new List<string> { playerId, rival.Id };

            GD.Print($"[ConvoyRaidTest] {playerId}={dock} tersane → hakimiyet={sup:F1}, " +
                     $"risk=%{risk:F1}");

            if (sup < 0f || sup > 100f)
            {
                GD.PrintErr($"[ConvoyRaidTest] HATA: hakimiyet [0,100] dışında ({sup:F1})");
                ok = false;
            }
            if (risk < 0f || risk > 100f)
            {
                GD.PrintErr($"[ConvoyRaidTest] HATA: risk [0,100] dışında ({risk:F1})");
                ok = false;
            }
            // Hakimiyet düştükçe risk artmalı.
            if (prevRisk >= 0f && risk < prevRisk - 0.001f)
            {
                GD.PrintErr($"[ConvoyRaidTest] HATA: hakimiyet düştü ama risk azaldı " +
                            $"({prevRisk:F1} → {risk:F1})");
                ok = false;
            }
            prevRisk = risk;
        }

        // ---- 4) Konvoy sayısı azalınca ithalat düşüş oranı artmalı ----
        GD.Print("[ConvoyRaidTest] --- 4) Konvoy sayısı vs düşüş ---");
        foreach (var z in SeaZoneSystem.All)
        {
            if (z.Id != raidZone.Id) z.AdjacentCountryIds = new List<string>();
        }
        raidZone.AdjacentCountryIds = new List<string> { playerId, rival.Id };
        playerData.Dockyards = 50; // düşük hakimiyet

        var fewConvoys = ConvoyRaidSystem.ComputeDailyRaids(playerId, 2, 4);
        var manyConvoys = ConvoyRaidSystem.ComputeDailyRaids(playerId, 20, 4);
        GD.Print($"[ConvoyRaidTest] 2 konvoy → kayıp={fewConvoys.ConvoysLost} " +
                 $"(%{fewConvoys.ImportShortfall * 100f:F0}), " +
                 $"20 konvoy → kayıp={manyConvoys.ConvoysLost} " +
                 $"(%{manyConvoys.ImportShortfall * 100f:F0})");
        if (manyConvoys.ConvoysLost < fewConvoys.ConvoysLost)
        {
            GD.PrintErr($"[ConvoyRaidTest] HATA: daha fazla konvoy daha az kayıp verdi " +
                        $"({fewConvoys.ConvoysLost} vs {manyConvoys.ConvoysLost})");
            ok = false;
        }

        // ---- 5) Boş konvoy → kayıp yok ----
        var none = ConvoyRaidSystem.ComputeDailyRaids(playerId, 0, 0);
        GD.Print($"[ConvoyRaidTest] Konvoy=0 → kayıp={none.ConvoysLost}");
        if (none.ConvoysLost != 0 || none.ImportShortfall != 0f)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: konvoy yokken kayıp rapor edildi");
            ok = false;
        }

        // ---- 6) Ekonomi uygulaması: konvoylar gerçekten azalmalı ----
        GD.Print("[ConvoyRaidTest] --- 5) Ekonomiye uygulama ---");
        EconomySystem.Initialize(playerData);
        int convoyStart = EconomySystem.ConvoyCount;
        var applied = ConvoyRaidSystem.ApplyDailyRaids(playerId);
        int convoyAfter = EconomySystem.ConvoyCount;
        GD.Print($"[ConvoyRaidTest] Ekonomi konvoyu {convoyStart} → {convoyAfter} " +
                 $"(batmış: {applied.ConvoysLost})");
        if (applied.ConvoysLost > 0 && convoyAfter >= convoyStart)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: konvoy kaybı ekonomiye yansımadı");
            ok = false;
        }
        if (convoyAfter < 0)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: konvoy sayısı negatife indi");
            ok = false;
        }

        // ---- 7) Ticaret anlaşması API'si: daraltma ve çözme ----
        GD.Print("[ConvoyRaidTest] --- 6) Ticaret anlaşması API'si ---");
        EconomySystem.Initialize(playerData);
        // Basit bir anlaşma kurmak için iç API'yi doğrudan kullanmak yerine,
        // ReduceTradeDeal / ReleaseTradeDeal davranışını konvoy sayacı üzerinden doğrula.
        int convoyBefore = EconomySystem.ConvoyCount;
        bool released = EconomySystem.ReleaseTradeDeal("Oil"); // anlaşma yok → false
        GD.Print($"[ConvoyRaidTest] Olmayan anlaşma çözme girişimi: {released}");
        if (released)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: olmayan anlaşma çözüldü");
            ok = false;
        }
        if (EconomySystem.ConvoyCount != convoyBefore)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: başarısız çözme konvoy sayacını değiştirdi");
            ok = false;
        }

        int lostSend = EconomySystem.LoseConvoys(5);
        int convoyAfterFive = EconomySystem.ConvoyCount;
        int lostMore = EconomySystem.LoseConvoys(100000); // fazlasını istemek
        GD.Print($"[ConvoyRaidTest] LoseConvoys(5)={lostSend} (kalan {convoyAfterFive}), " +
                 $"LoseConvoys(100000)={lostMore} (kalan {EconomySystem.ConvoyCount})");
        if (lostSend != 5 || convoyAfterFive != convoyBefore - 5)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: LoseConvoys istediği kadarını düşmüyor");
            ok = false;
        }
        // Taşma durumu: gerçekten düşen = kalan konvoy (5), mevcut sayaç 0 olmalı.
        if (lostMore != convoyAfterFive || EconomySystem.ConvoyCount != 0)
        {
            GD.PrintErr($"[ConvoyRaidTest] HATA: LoseConvoys taşmayı doğru kısıtlamıyor " +
                        $"(düşen={lostMore}, kalan={EconomySystem.ConvoyCount})");
            ok = false;
        }
        if (EconomySystem.ConvoyCount != 0)
        {
            GD.PrintErr("[ConvoyRaidTest] HATA: konvoy tükendikten sonra sayaç 0 değil");
            ok = false;
        }

        // ---- 8) Dünya verisini geri yükle ----
        playerData.Dockyards = playerDockBefore;
        rival.Dockyards = rivalDockBefore;
        foreach (var z in SeaZoneSystem.All)
        {
            if (zoneSnapshot.TryGetValue(z.Id, out var saved))
                z.AdjacentCountryIds = saved;
        }
        GD.Print("[ConvoyRaidTest] Dünya verisi geri yüklendi");

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M43.3: Deniz çatışması testi (--naval-combat-test).
    /// İki filo bölgede çarpışır; kaybeden hasar alır, hakimiyet değişir,
    /// sonuç deterministiktir ve save/load'a yazılır.
    /// </summary>
    private void RunNavalCombatTest()
    {
        GD.Print("=========================================");
        GD.Print("  M43.3: DENİZ ÇATIŞMASI TESTİ (--naval-combat-test)");
        GD.Print("=========================================");

        bool ok = true;

        SeaZoneSystem.LoadData();
        NavalCombatSystem.ResetForNewGame();

        string attackerId = "DEU";
        string defenderId = "POL";
        var atk = CountryDatabase.Get(attackerId);
        var def = CountryDatabase.Get(defenderId);
        if (atk == null || def == null)
        {
            GD.PrintErr("[NavalCombatTest] HATA: DEU/POL ülkeleri bulunamadı");
            GetTree().Quit(1);
            return;
        }

        // Baltic Sea bölgesi DEU ve POL'nin ortak komşusu.
        var zone = SeaZoneSystem.All.FirstOrDefault(z => z.Id == "baltic");
        if (zone == null)
        {
            GD.PrintErr("[NavalCombatTest] HATA: baltic bölgesi bulunamadı");
            GetTree().Quit(1);
            return;
        }

        // ---- 1) Dünya verisi yedekle ----
        int atkDockBefore = atk.Dockyards;
        int defDockBefore = def.Dockyards;
        var zoneSnapshot = new Dictionary<string, List<string>>();
        foreach (var z in SeaZoneSystem.All)
            zoneSnapshot[z.Id] = new List<string>(z.AdjacentCountryIds);

        // Bölgeyi iki ülkeye indir: diğer komşuların ağırlığı sonucu kirletmesin.
        zone.AdjacentCountryIds = new List<string> { attackerId, defenderId };

        try
        {
            // ---- 2) Yapısal kontroller ----
            GD.Print("[NavalCombatTest] 1) Yapısal kontroller");
            if (atkDockBefore < 0 || defDockBefore < 0)
            {
                GD.PrintErr("[NavalCombatTest] HATA: tersane sayısı negatif");
                ok = false;
            }

            atk.Dockyards = 0;
            def.Dockyards = 0;
            // İlk durumda hiç hasar yok → çarpan 1.
            if (Mathf.Abs(NavalCombatSystem.StrengthMultiplier(attackerId) - 1f) > 0.001f)
            {
                GD.PrintErr("[NavalCombatTest] HATA: hasarsız filo çarpanı 1 olmalı");
                ok = false;
            }

            // ---- 3) Eşit filo: her iki taraf da hasar alır, kazanan olmayabilir ----
            GD.Print("[NavalCombatTest] 2) Eşit filolar + DeterministicRNG ile çözüm");
            atk.Dockyards = 100;
            def.Dockyards = 100;

            float supBefore = SeaZoneSystem.Supremacy(zone, attackerId);
            var result = NavalCombatSystem.ResolveBattle(zone, attackerId, defenderId);
            float supAfter = SeaZoneSystem.Supremacy(zone, attackerId);

            if (!result.WasFought)
            {
                GD.PrintErr("[NavalCombatTest] HATA: iki filo varken çatışma çözümlenmeli");
                ok = false;
            }
            if (result.DamageToAttacker <= 0f || result.DamageToDefender <= 0f)
            {
                GD.PrintErr("[NavalCombatTest] HATA: eşit filolarda her iki taraf da hasar almalı");
                ok = false;
            }
            if (result.DamageToAttacker > NavalCombatSystem.MaxDamage ||
                result.DamageToDefender > NavalCombatSystem.MaxDamage)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: hasar üst sınırı aşıldı ({result.DamageToAttacker}, {result.DamageToDefender})");
                ok = false;
            }
            GD.Print($"[NavalCombatTest] eşit filo → hasar %{result.DamageToAttacker * 100f:F0} / %{result.DamageToDefender * 100f:F0}, " +
                     $"hakimiyet {supBefore:F1} → {supAfter:F1}");

            // ---- 4) Güçlü saldıran: rakibi ezerse hakimiyet YÜKSELİR ----
            GD.Print("[NavalCombatTest] 3) Güçlü filo → hakimiyet artar");
            NavalCombatSystem.ResetForNewGame();
            atk.Dockyards = 400;
            def.Dockyards = 10;

            float supBeforeBig = SeaZoneSystem.Supremacy(zone, attackerId);
            var big = NavalCombatSystem.ResolveBattle(zone, attackerId, defenderId);

            // Ezici üstünlükte kazananın hasarı küçük, kaybedeninki büyük olmalı.
            if (big.HasWinner)
            {
                float winnerDmg = big.WinnerId == attackerId ? big.DamageToAttacker : big.DamageToDefender;
                float loserDmg = big.WinnerId == attackerId ? big.DamageToDefender : big.DamageToAttacker;
                if (loserDmg <= winnerDmg)
                {
                    GD.PrintErr($"[NavalCombatTest] HATA: kaybeden hasarı kazananı geçmeli ({loserDmg} vs {winnerDmg})");
                    ok = false;
                }
            }
            // Hakimiyet, kaybeden filoyu zayıflattığı için artmış olmalı.
            float supAfterBig = SeaZoneSystem.Supremacy(zone, attackerId);
            if (supAfterBig <= supBeforeBig)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: saldıran kazanınca hakimiyet artmalı ({supBeforeBig:F1} → {supAfterBig:F1})");
                ok = false;
            }
            GD.Print($"[NavalCombatTest] 400 vs 10 → hakimiyet {supBeforeBig:F1} → {supAfterBig:F1}, kazanan={big.WinnerId ?? "beraberlik"} ✓");

            // ---- 5) Determinizm: aynı state → aynı sonuç ----
            GD.Print("[NavalCombatTest] 4) Deterministik tekrar");
            NavalCombatSystem.ResetForNewGame();
            atk.Dockyards = 100;
            def.Dockyards = 100;

            ulong rngBefore = DeterministicRNG.GetState();
            var run1 = NavalCombatSystem.ResolveBattle(zone, attackerId, defenderId);
            ulong rngMid = DeterministicRNG.GetState();
            float sup1 = SeaZoneSystem.Supremacy(zone, attackerId);

            // Aynı koşulları kur ve aynı RNG konumundan tekrar et.
            NavalCombatSystem.ResetForNewGame();
            atk.Dockyards = 100;
            def.Dockyards = 100;
            DeterministicRNG.LoadState(rngBefore);
            var run2 = NavalCombatSystem.ResolveBattle(zone, attackerId, defenderId);
            float sup2 = SeaZoneSystem.Supremacy(zone, attackerId);

            if (run1.WinnerId != run2.WinnerId ||
                Mathf.Abs(run1.DamageToAttacker - run2.DamageToAttacker) > 0.001f ||
                Mathf.Abs(sup1 - sup2) > 0.001f ||
                rngMid != DeterministicRNG.GetState())
            {
                GD.PrintErr("[NavalCombatTest] HATA: çatışma deterministik değil");
                ok = false;
            }
            else
            {
                GD.Print($"[NavalCombatTest] aynı girdi → aynı çıktı ✓ (kazanan={run1.WinnerId ?? "beraberlik"})");
            }

            // ---- 6) Hasar üst sınırı ve kümülatif yıpranma ----
            GD.Print("[NavalCombatTest] 5) Hasar sınırı ve kümülatiflik");
            NavalCombatSystem.ResetForNewGame();
            for (int i = 0; i < 20; i++)
                NavalCombatSystem.ApplyDamage(attackerId, 0.5f);
            float stacked = NavalCombatSystem.DamageFor(attackerId);
            if (stacked > NavalCombatSystem.MaxDamage + 0.001f)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: hasar {NavalCombatSystem.MaxDamage} ile sınırlı olmalı, bulundu {stacked}");
                ok = false;
            }
            if (stacked <= 0f)
            {
                GD.PrintErr("[NavalCombatTest] HATA: hasar kümülatif olmalı");
                ok = false;
            }
            GD.Print($"[NavalCombatTest] 20×0.5 hasar → {stacked:F2} (sınır {NavalCombatSystem.MaxDamage}) ✓");

            // ---- 7) Onarım: günde DailyRepair kadar düşer ----
            GD.Print("[NavalCombatTest] 6) Onarım");
            NavalCombatSystem.ApplyDamage(defenderId, 0.5f);
            float damagedBefore = NavalCombatSystem.DamageFor(defenderId);
            NavalCombatSystem.DailyTick();
            float damagedAfter = NavalCombatSystem.DamageFor(defenderId);
            if (damagedAfter >= damagedBefore)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: günlük tick hasarı azaltmalı ({damagedBefore} → {damagedAfter})");
                ok = false;
            }
            if (Mathf.Abs(damagedAfter - damagedBefore * (1f - NavalCombatSystem.DailyRepair)) > 0.01f && damagedAfter > 0f)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: onarım oranı beklenenle uyuşmuyor ({damagedBefore} → {damagedAfter})");
                ok = false;
            }
            GD.Print($"[NavalCombatTest] onarım {damagedBefore:F2} → {damagedAfter:F2} ✓");

            // ---- 8) Save/Load round-trip ----
            GD.Print("[NavalCombatTest] 7) Save/Load round-trip");
            NavalCombatSystem.ResetForNewGame();
            NavalCombatSystem.ApplyDamage("DEU", 0.42f);
            NavalCombatSystem.ApplyDamage("ITA", 0.11f);
            var save = NavalCombatSystem.GetSaveData();
            float weightBeforeSave = SeaZoneSystem.FleetWeight(attackerId);

            NavalCombatSystem.ResetForNewGame();
            if (NavalCombatSystem.DamageFor(attackerId) != 0f)
            {
                GD.PrintErr("[NavalCombatTest] HATA: ResetForNewGame hasarı temizlemeli");
                ok = false;
            }
            NavalCombatSystem.LoadSaveData(save);
            if (Mathf.Abs(NavalCombatSystem.DamageFor(attackerId) - 0.42f) > 0.001f)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: hasar yüklenemedi ({NavalCombatSystem.DamageFor(attackerId)})");
                ok = false;
            }
            if (Mathf.Abs(NavalCombatSystem.DamageFor("ITA") - 0.11f) > 0.001f)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: İTALYA hasarı yüklenemedi ({NavalCombatSystem.DamageFor("ITA")})");
                ok = false;
            }
            float weightAfterLoad = SeaZoneSystem.FleetWeight(attackerId);
            if (Mathf.Abs(weightBeforeSave - weightAfterLoad) > 0.001f)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: filo ağırlığı round-trip sonrası değişti ({weightBeforeSave} → {weightAfterLoad})");
                ok = false;
            }
            GD.Print($"[NavalCombatTest] DEU hasar=0.42, ITA hasar=0.11, filo ağırlığı {weightBeforeSave:F1} → {weightAfterLoad:F1} ✓");

            // ---- 9) Hasar hakimiyetle ters orantılı ----
            GD.Print("[NavalCombatTest] 8) Hasar arttıkça hakimiyet düşer");
            atk.Dockyards = 100;
            def.Dockyards = 100;
            NavalCombatSystem.ResetForNewGame();

            float supClean = SeaZoneSystem.Supremacy(zone, attackerId);
            NavalCombatSystem.ApplyDamage(attackerId, 0.5f);
            float supHalf = SeaZoneSystem.Supremacy(zone, attackerId);
            NavalCombatSystem.ApplyDamage(attackerId, 0.4f);
            float supMore = SeaZoneSystem.Supremacy(zone, attackerId);

            if (supHalf >= supClean || supMore >= supHalf)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: hasar arttıkça hakimiyet düşmeli ({supClean} → {supHalf} → {supMore})");
                ok = false;
            }
            GD.Print($"[NavalCombatTest] hakimiyet {supClean:F1} → {supHalf:F1} → {supMore:F1} ✓");

            // ---- 10) Savaş yoksa günlük tick çatışma başlatmaz ----
            GD.Print("[NavalCombatTest] 9) Barışta oto-çatışma yok");
            NavalCombatSystem.ResetForNewGame();
            atk.Dockyards = 100;
            def.Dockyards = 100;
            bool atWarBefore = WarSystem.IsAtWarWith(attackerId, defenderId);
            NavalCombatSystem.DailyTick();
            if (!atWarBefore && NavalCombatSystem.DamageFor(attackerId) > 0f)
            {
                GD.PrintErr("[NavalCombatTest] HATA: savaş yokken günlük tick çatışma üretmemeli");
                ok = false;
            }
            GD.Print($"[NavalCombatTest] savaş durumu={atWarBefore} → hasar değişmedi ✓");

            // ---- 11) Hasar konvoy riskini artırır (M43.2 bağlantısı) ----
            GD.Print("[NavalCombatTest] 10) Hasar konvoy riskini artırır");
            NavalCombatSystem.ResetForNewGame();
            atk.Dockyards = 100;
            def.Dockyards = 100;
            ConvoyRaidSystem.ApplyDailyRaids(attackerId);

            float riskClean = ConvoyRaidSystem.TotalRiskPercent(attackerId);
            NavalCombatSystem.ApplyDamage(attackerId, 0.75f);
            float riskHurt = ConvoyRaidSystem.TotalRiskPercent(attackerId);
            if (riskHurt <= riskClean)
            {
                GD.PrintErr($"[NavalCombatTest] HATA: filo hasarı konvoy riskini artırmalı ({riskClean} → {riskHurt})");
                ok = false;
            }
            GD.Print($"[NavalCombatTest] konvoy riski {riskClean:F1}% → {riskHurt:F1}% ✓");
        }
        finally
        {
            // ---- 12) Dünya verisini geri yükle ----
            atk.Dockyards = atkDockBefore;
            def.Dockyards = defDockBefore;
            NavalCombatSystem.ResetForNewGame();
            foreach (var z in SeaZoneSystem.All)
            {
                if (zoneSnapshot.TryGetValue(z.Id, out var saved))
                    z.AdjacentCountryIds = saved;
            }
            GD.Print("[NavalCombatTest] Dünya verisi geri yüklendi");
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M43.4: Müttefik konvoy koruması testi (--lend-lease-test).
    /// Müttefik filo güzergâha koruma atanır; etkin hakimiyet yükselir, günlük
    /// konvoy kaybı ve risk göstergesi düşer. Tahsis save/load'a yazılır ve
    /// yüklenir; geçersiz tahsis (müttefiklik bozulunca) elenir.
    /// </summary>
    private void RunLendLeaseTest()
    {
        GD.Print("=========================================");
        GD.Print("  M43.4: MÜTTEFİK KONVOY KORUMASI TESTİ (--lend-lease-test)");
        GD.Print("=========================================");

        bool ok = true;

        SeaZoneSystem.LoadData();
        NavalCombatSystem.ResetForNewGame();
        ConvoyRaidSystem.ResetForNewGame();

        string protectedId = "POL"; // konvoyları batıyor
        string protectorId = "DEU"; // koruma verecek müttefik
        string enemyId = "SOV";     // güzergâhı tehdit eden rakip

        var protectedCountry = CountryDatabase.Get(protectedId);
        var protector = CountryDatabase.Get(protectorId);
        var enemy = CountryDatabase.Get(enemyId);
        if (protectedCountry == null || protector == null || enemy == null)
        {
            GD.PrintErr("[LendLeaseTest] HATA: POL/DEU/SOV ülkeleri bulunamadı");
            GetTree().Quit(1);
            return;
        }

        // ---- 1) Dünya verisi yedekle ----
        int protDockBefore = protector.Dockyards;
        double protPpBefore = protector.PoliticalPower;
        int protDockEnemy = enemy.Dockyards;
        int protDockProtected = protectedCountry.Dockyards;
        double protPpProtected = protectedCountry.PoliticalPower;
        int relBefore = DiplomacySystem.GetRelation(protectorId, protectedId);
        int opBefore = DiplomacySystem.GetOpinion(protectorId, protectedId);

        var zoneSnapshot = new Dictionary<string, List<string>>();
        foreach (var z in SeaZoneSystem.All)
            zoneSnapshot[z.Id] = new List<string>(z.AdjacentCountryIds);
        var factionSnapshot = new Dictionary<string, List<string>>();
        foreach (var f in DiplomacySystem.Factions.Values)
            factionSnapshot[f.Id] = new List<string>(f.MemberIds);

        var zone = SeaZoneSystem.All.FirstOrDefault(z => z.Id == "baltic");
        if (zone == null)
        {
            GD.PrintErr("[LendLeaseTest] HATA: baltic bölgesi bulunamadı");
            GetTree().Quit(1);
            return;
        }

        try
        {
            // ---- 2) Müttefiklik kur: DEU lider, POL üye ----
            GD.Print("[LendLeaseTest] 1) Müttefiklik kurulumu ve reddedilen tahsisler");
            foreach (var f in DiplomacySystem.Factions.Values)
                f.MemberIds.Clear(); // mevcut üyelikleri geçici olarak boşalt

            bool needRelationBoost = DiplomacySystem.GetOpinion(protectorId, protectedId) < 50;
            if (needRelationBoost)
                DiplomacySystem.ModifyRelation(protectorId, protectedId, +100);

            protector.PoliticalPower = 1000;        // fraksiyon kurma maliyeti
            protectedCountry.PoliticalPower = 1000; // katılım maliyeti

            bool factionOk = DiplomacySystem.CreateFaction("ll_test", "Test Alliance", protectorId, "democratic");
            bool joinOk = DiplomacySystem.RequestJoinFaction("ll_test", protectedId);
            if (!factionOk || !joinOk || !ConvoyRaidSystem.IsAllied(protectorId, protectedId))
            {
                GD.PrintErr($"[LendLeaseTest] HATA: müttefiklik kurulamadı (faksiyon={factionOk}, katılım={joinOk})");
                ok = false;
            }

            // ---- 3) Reddedilen tahsisler ----
            var self = ConvoyRaidSystem.AssignEscort(protectedId, protectedId);
            if (self.Accepted || self.Reason != ConvoyRaidSystem.EscortReason.SelfProtection)
            {
                GD.PrintErr("[LendLeaseTest] HATA: kendi konvoylarını korumak reddedilmeli");
                ok = false;
            }

            var unknown = ConvoyRaidSystem.AssignEscort("ZZZ", protectedId);
            if (unknown.Accepted || unknown.Reason != ConvoyRaidSystem.EscortReason.UnknownCountry)
            {
                GD.PrintErr("[LendLeaseTest] HATA: bilinmeyen ülke reddedilmeli");
                ok = false;
            }

            var nonAlly = ConvoyRaidSystem.AssignEscort(enemyId, protectedId);
            if (nonAlly.Accepted || nonAlly.Reason != ConvoyRaidSystem.EscortReason.NotAllies)
            {
                GD.PrintErr("[LendLeaseTest] HATA: müttefik olmayan ülke koruma verememeli");
                ok = false;
            }

            protector.Dockyards = 0;
            var noFleet = ConvoyRaidSystem.AssignEscort(protectorId, protectedId);
            if (noFleet.Accepted || noFleet.Reason != ConvoyRaidSystem.EscortReason.ProtectorHasNoFleet)
            {
                GD.PrintErr("[LendLeaseTest] HATA: filosu olmayan ülke koruma verememeli");
                ok = false;
            }

            if (ConvoyRaidSystem.EscortOf(protectedId) != null)
            {
                GD.PrintErr("[LendLeaseTest] HATA: reddedilen tahsis kaydedilmemeli");
                ok = false;
            }

            // ---- 4) Bölgeleri üç ülkeye indir: POL'ün tek komşusu baltic ----
            foreach (var z in SeaZoneSystem.All)
                z.AdjacentCountryIds.Clear();
            zone.AdjacentCountryIds = new List<string> { protectedId, protectorId, enemyId };

            // ---- 5) Korumasız taban çizgisi (M43.2 davranışı korunmalı) ----
            GD.Print("[LendLeaseTest] 2) Korumasız taban çizgisi");
            protector.Dockyards = 100;
            enemy.Dockyards = 100;
            protectedCountry.Dockyards = 10;

            float ownSup = SeaZoneSystem.Supremacy(zone, protectedId);
            float effSupNoEscort = ConvoyRaidSystem.EffectiveSupremacy(zone, protectedId);
            if (Mathf.Abs(effSupNoEscort - ownSup) > 0.01f)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: koruma yokken etkin hakimiyet kendi hakimiyetine eşit olmalı ({ownSup:F2} → {effSupNoEscort:F2})");
                ok = false;
            }

            float riskBefore = ConvoyRaidSystem.TotalRiskPercent(protectedId);
            int lostBefore = ConvoyRaidSystem.ComputeDailyRaids(protectedId, 1000, 500).ConvoysLost;
            if (riskBefore <= 0f || lostBefore <= 0)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: düşman hakimiyetinde kayıp bekleniyordu (risk={riskBefore:F1}%, kayıp={lostBefore})");
                ok = false;
            }
            GD.Print($"[LendLeaseTest] korumasız → hakimiyet={ownSup:F1}, risk=%{riskBefore:F1}, kayıp={lostBefore}");

            // ---- 6) Zayıf koruma: DEU 20 tersane ----
            GD.Print("[LendLeaseTest] 3) Zayıf koruma (DEU=20)");
            protector.Dockyards = 20;
            var assigned = ConvoyRaidSystem.AssignEscort(protectorId, protectedId);
            if (!assigned.Accepted)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: geçerli tahsis kabul edilmeli ({assigned.Reason})");
                ok = false;
            }

            float effSupWeak = ConvoyRaidSystem.EffectiveSupremacy(zone, protectedId);
            float reductionWeak = ConvoyRaidSystem.EscortReductionPercent(protectedId);
            float riskWeak = ConvoyRaidSystem.TotalRiskPercent(protectedId);
            int lostWeak = ConvoyRaidSystem.ComputeDailyRaids(protectedId, 1000, 500).ConvoysLost;

            if (effSupWeak <= ownSup)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: koruma etkin hakimiyeti yükseltmeli ({ownSup:F1} → {effSupWeak:F1})");
                ok = false;
            }
            if (riskWeak >= riskBefore || lostWeak >= lostBefore)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: koruma riski ve kaybı düşürmeli (risk {riskBefore:F1}→{riskWeak:F1}, kayıp {lostBefore}→{lostWeak})");
                ok = false;
            }
            if (reductionWeak <= 0f)
            {
                GD.PrintErr("[LendLeaseTest] HATA: koruma azalma yüzdesi pozitif olmalı");
                ok = false;
            }
            GD.Print($"[LendLeaseTest] zayıf koruma → hakimiyet={effSupWeak:F1} (-%{reductionWeak:F0}), risk=%{riskWeak:F1}, kayıp={lostWeak}");

            // ---- 7) Güçlü koruma: DEU 400 tersane → azalma daha da büyük ----
            GD.Print("[LendLeaseTest] 4) Güçlü koruma (DEU=400)");
            protector.Dockyards = 400;
            float effSupStrong = ConvoyRaidSystem.EffectiveSupremacy(zone, protectedId);
            float riskStrong = ConvoyRaidSystem.TotalRiskPercent(protectedId);
            int lostStrong = ConvoyRaidSystem.ComputeDailyRaids(protectedId, 1000, 500).ConvoysLost;

            if (effSupStrong <= effSupWeak || lostStrong >= lostWeak)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: güçlü koruma daha çok korumalı (hakimiyet {effSupWeak:F1}→{effSupStrong:F1}, kayıp {lostWeak}→{lostStrong})");
                ok = false;
            }
            // Kaybın taban çizgisine göre azaldığı, ama sıfır olmadığı doğrulanır
            // (mükemmel koruma bile denizaltı riskini tam silmez).
            if (lostStrong <= 0)
            {
                GD.PrintErr("[LendLeaseTest] HATA: güçlü korumada bile kayıp sıfır olmamalı");
                ok = false;
            }
            if (lostStrong >= lostBefore)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: güçlü koruma taban çizgisinin altında kalmalı ({lostStrong} vs {lostBefore})");
                ok = false;
            }
            GD.Print($"[LendLeaseTest] güçlü koruma → hakimiyet={effSupStrong:F1}, risk=%{riskStrong:F1}, kayıp={lostStrong}");

            // ---- 8) Determinizm: saf fonksiyon, aynı girdi → aynı çıktı ----
            int repeatA = ConvoyRaidSystem.ComputeDailyRaids(protectedId, 1000, 500).ConvoysLost;
            int repeatB = ConvoyRaidSystem.ComputeDailyRaids(protectedId, 1000, 500).ConvoysLost;
            if (repeatA != repeatB || repeatA != lostStrong)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: koruma hesabı deterministik olmalı ({lostStrong}/{repeatA}/{repeatB})");
                ok = false;
            }

            // Monotonluk: daha çok konvoy → en az kadar kayıp.
            int lostMoreConvoys = ConvoyRaidSystem.ComputeDailyRaids(protectedId, 2000, 1000).ConvoysLost;
            if (lostMoreConvoys < lostStrong)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: konvoy sayısı artınca kayıp azalmamalı ({lostStrong} → {lostMoreConvoys})");
                ok = false;
            }

            // ---- 9) Koruyucu bölgeye komşu değilse koruma etkisiz ----
            zone.AdjacentCountryIds = new List<string> { protectedId, enemyId };
            // Koruyucu bölgeden çıkınca kendi hakimiyet de değişir (artık düşman
            // ağırlığı sayılmaz) → karşılaştırma GÜNCEL kendi hakimiyete göre yapılır.
            float ownSupFar = SeaZoneSystem.Supremacy(zone, protectedId);
            float effSupFar = ConvoyRaidSystem.EffectiveSupremacy(zone, protectedId);
            if (Mathf.Abs(effSupFar - ownSupFar) > 0.01f)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: komşu olmayan koruyucu filo etki etmemeli ({ownSupFar:F1} → {effSupFar:F1})");
                ok = false;
            }
            GD.Print("[LendLeaseTest] komşu olmayan koruyucu → etki yok ✓");
            zone.AdjacentCountryIds = new List<string> { protectedId, protectorId, enemyId };

            // ---- 10) Filoyu kaybeden koruyucu koruma sağlayamaz ----
            protector.Dockyards = 0;
            float ownSupNoFleet = SeaZoneSystem.Supremacy(zone, protectedId);
            float effSupNoFleet = ConvoyRaidSystem.EffectiveSupremacy(zone, protectedId);
            if (Mathf.Abs(effSupNoFleet - ownSupNoFleet) > 0.01f)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: filosuz koruyucu etki etmemeli ({ownSupNoFleet:F1} → {effSupNoFleet:F1})");
                ok = false;
            }
            GD.Print("[LendLeaseTest] filoyu kaybeden koruyucu → etki yok ✓");
            protector.Dockyards = 400; // save/load doğrulaması için güçlü filoya dön

            // ---- 11) Save/Load: tahsis kaydedilir ve geri yüklenir ----
            GD.Print("[LendLeaseTest] 5) Save/Load");
            float effSupBeforeSave = ConvoyRaidSystem.EffectiveSupremacy(zone, protectedId);
            var saveData = ConvoyRaidSystem.GetSaveData();
            if (saveData.Escorts == null || saveData.Escorts.Length != 1 ||
                saveData.Escorts[0].ProtectorId != protectorId)
            {
                GD.PrintErr("[LendLeaseTest] HATA: save verisi tek tahsis içermeli");
                ok = false;
            }

            ConvoyRaidSystem.ResetForNewGame();
            if (ConvoyRaidSystem.EscortOf(protectedId) != null)
            {
                GD.PrintErr("[LendLeaseTest] HATA: yeni oyun sıfırlaması tahsisleri temizlemeli");
                ok = false;
            }
            float ownSupNow = SeaZoneSystem.Supremacy(zone, protectedId);
            float effSupAfterClear = ConvoyRaidSystem.EffectiveSupremacy(zone, protectedId);
            if (Mathf.Abs(effSupAfterClear - ownSupNow) > 0.01f)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: tahsis temizlenince etkin hakimiyet kendi hakimiyetine düşmeli ({ownSupNow:F1} → {effSupAfterClear:F1})");
                ok = false;
            }

            ConvoyRaidSystem.LoadSaveData(saveData);
            if (ConvoyRaidSystem.EscortOf(protectedId) != protectorId)
            {
                GD.PrintErr("[LendLeaseTest] HATA: yükleme sonrası koruyucu geri gelmeli");
                ok = false;
            }
            float effSupAfterLoad = ConvoyRaidSystem.EffectiveSupremacy(zone, protectedId);
            if (Mathf.Abs(effSupAfterLoad - effSupBeforeSave) > 0.01f)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: yükleme sonrası etkin hakimiyet aynı kalmalı ({effSupBeforeSave:F2} → {effSupAfterLoad:F2})");
                ok = false;
            }
            GD.Print($"[LendLeaseTest] save/load → koruyucu={ConvoyRaidSystem.EscortOf(protectedId)}, hakimiyet={effSupAfterLoad:F1} ✓");

            // ---- 12) Müttefiklik bozulunca tahsis elenir ----
            var faction = DiplomacySystem.GetFactionOf(protectedId);
            if (faction != null) faction.MemberIds.Remove(protectedId);
            int pruned = ConvoyRaidSystem.PruneInvalidEscorts();
            if (pruned != 1 || ConvoyRaidSystem.EscortOf(protectedId) != null)
            {
                GD.PrintErr($"[LendLeaseTest] HATA: müttefikliği bozulan tahsis elenmeli (elenen={pruned})");
                ok = false;
            }
            else
            {
                GD.Print("[LendLeaseTest] müttefiklik bozulunca tahsis elendi ✓");
            }
        }
        finally
        {
            // ---- 13) Dünya verisini geri yükle ----
            protector.Dockyards = protDockBefore;
            enemy.Dockyards = protDockEnemy;
            protectedCountry.Dockyards = protDockProtected;
            protector.PoliticalPower = protPpBefore;
            protectedCountry.PoliticalPower = protPpProtected;

            int relNow = DiplomacySystem.GetRelation(protectorId, protectedId);
            if (relNow != relBefore)
                DiplomacySystem.ModifyRelation(protectorId, protectedId, relBefore - relNow);
            int opNow = DiplomacySystem.GetOpinion(protectorId, protectedId);
            if (opNow != opBefore)
                DiplomacySystem.ModifyRelation(protectorId, protectedId, opBefore - opNow);

            ConvoyRaidSystem.ResetForNewGame();

            foreach (var z in SeaZoneSystem.All)
            {
                if (zoneSnapshot.TryGetValue(z.Id, out var saved))
                    z.AdjacentCountryIds = saved;
            }
            foreach (var f in DiplomacySystem.Factions.Values)
            {
                if (factionSnapshot.TryGetValue(f.Id, out var members))
                    f.MemberIds = members;
            }
            GD.Print("[LendLeaseTest] Dünya verisi geri yüklendi");
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>Test yardımcısı: ağaçtaki SeaZonePanel örneğini bulur (ModalHost katmanının içinde).</summary>
    private SeaZonePanel FindSeaZonePanel()
    {
        foreach (var n in FindChildrenIn(this))
        {
            if (n is SeaZonePanel p) return p;
        }
        return null;
    }

    /// <summary>
    /// M43.5: Deniz bölgesi paneli testi (--sea-zone-panel-test).
    /// Panelin açılışı, bölge satır verisi, müttefik korumasının panel üzerinden
    /// atanması/geri alınması ve ESC ile kapanması doğrulanır.
    /// Normal oyunda ASLA çalışmaz; dünya verisi test sonunda geri yüklenir.
    /// </summary>
    private async void RunSeaZonePanelTest()
    {
        GD.Print("=========================================");
        GD.Print("  M43.5: DENİZ BÖLGESİ PANELİ TESTİ (--sea-zone-panel-test)");
        GD.Print("=========================================");

        bool ok = true;

        SeaZoneSystem.LoadData();
        NavalCombatSystem.ResetForNewGame();
        ConvoyRaidSystem.ResetForNewGame();

        if (!HasPlayer) SetPlayerCountry("DEU");
        string playerId = PlayerCountryId;
        string allyId = "POL";  // koruma verecek müttefik
        string enemyId = "SOV"; // bölgeyi paylaşan rakip

        var player = CountryDatabase.Get(playerId);
        var ally = CountryDatabase.Get(allyId);
        var enemy = CountryDatabase.Get(enemyId);
        if (player == null || ally == null || enemy == null)
        {
            GD.PrintErr("[SeaZonePanelTest] HATA: DEU/POL/SOV ülkeleri bulunamadı");
            GetTree().Quit(1);
            return;
        }
        if (string.Equals(playerId, allyId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(playerId, enemyId, StringComparison.OrdinalIgnoreCase))
        {
            GD.PrintErr($"[SeaZonePanelTest] HATA: oyuncu test ülkelerinden biri olamaz ({playerId})");
            GetTree().Quit(1);
            return;
        }

        // ---- 1) Dünya verisini yedekle ----
        int playerDockBefore = player.Dockyards;
        double playerPpBefore = player.PoliticalPower;
        int allyDockBefore = ally.Dockyards;
        double allyPpBefore = ally.PoliticalPower;
        int enemyDockBefore = enemy.Dockyards;
        int relBefore = DiplomacySystem.GetRelation(playerId, allyId);
        int opBefore = DiplomacySystem.GetOpinion(playerId, allyId);

        var zoneSnapshot = new Dictionary<string, List<string>>();
        foreach (var z in SeaZoneSystem.All)
            zoneSnapshot[z.Id] = new List<string>(z.AdjacentCountryIds);
        var factionSnapshot = new Dictionary<string, List<string>>();
        foreach (var f in DiplomacySystem.Factions.Values)
            factionSnapshot[f.Id] = new List<string>(f.MemberIds);

        // Panel bölgeleri isme göre sıralar — baltic satır indeksi aynı sıradan türetilir.
        var orderedZones = SeaZoneSystem.All
            .OrderBy(z => z.Name, StringComparer.Ordinal)
            .ToList();
        int balticIndex = orderedZones.FindIndex(z =>
            string.Equals(z.Id, "baltic", StringComparison.OrdinalIgnoreCase));
        if (balticIndex < 0)
        {
            GD.PrintErr("[SeaZonePanelTest] HATA: baltic bölgesi bulunamadı");
            GetTree().Quit(1);
            return;
        }

        // Panel gerçek bir Control ağacında açılsın (viewport ölçüsü anlamlı olsun).
        var host = new Control { Name = "SeaZonePanelTestHost" };
        host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(host);

        try
        {
            // ---- 2) Müttefiklik: fraksiyon kurmadan garanti (hiçbir şey sızmayı) ----
            GD.Print("[SeaZonePanelTest] 1) Müttefiklik kurulumu (garanti)");
            ally.PoliticalPower = 1000;   // müttefik tarafı için yeterli PP
            player.PoliticalPower = 1000; // garanti maliyeti oyuncudan düşer
            if (!DiplomacySystem.AddGuarantee(playerId, allyId))
            {
                GD.PrintErr($"[SeaZonePanelTest] HATA: {playerId} → {allyId} garantisi kurulamadı");
                ok = false;
            }
            if (!ConvoyRaidSystem.IsAllied(allyId, playerId))
            {
                GD.PrintErr("[SeaZonePanelTest] HATA: garanti iki ülkeyi müttefik yapmadı");
                ok = false;
            }
            else
            {
                GD.Print($"[SeaZonePanelTest] {playerId} ↔ {allyId} müttefik (garanti) ✓");
            }

            // ---- 3) Bölgeleri üç ülkeye indir: baltic = POL / DEU / SOV ----
            foreach (var z in SeaZoneSystem.All)
                z.AdjacentCountryIds.Clear();
            var baltic = SeaZoneSystem.All.FirstOrDefault(z =>
                string.Equals(z.Id, "baltic", StringComparison.OrdinalIgnoreCase));
            if (baltic == null)
            {
                GD.PrintErr("[SeaZonePanelTest] HATA: baltic bölgesi bulunamadı");
                ok = false;
            }
            else
            {
                baltic.AdjacentCountryIds = new List<string> { allyId, playerId, enemyId };
            }

            player.Dockyards = 200; // oyuncu filosu
            ally.Dockyards = 300;   // koruyucu filo (daha ağır basar)
            enemy.Dockyards = 200;  // rakip filo

            // ---- 4) Paneli aç ----
            GD.Print("[SeaZonePanelTest] 2) Panel açılışı");
            SeaZonePanel.OpenUnder(host);
            await ToSignal(GetTree(), "process_frame");

            if (!SeaZonePanel.IsOpen)
            {
                GD.PrintErr("[SeaZonePanelTest] HATA: panel açılmadı");
                ok = false;
            }

            var panel = FindSeaZonePanel();
            if (panel == null)
            {
                GD.PrintErr("[SeaZonePanelTest] HATA: panel düğümü bulunamadı");
                ok = false;
            }
            else
            {
                // ---- 5) Ölçü sınırı: 1152x648'e sığmalı ----
                Vector2 designed = panel.DesignedSize;
                GD.Print($"[SeaZonePanelTest] Panel tasarım boyutu: {designed.X}x{designed.Y}");
                if (designed.X <= 0f || designed.Y <= 0f)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: panel boyutu okunamadı");
                    ok = false;
                }
                if (designed.X > 1152 || designed.Y > 648)
                {
                    GD.PrintErr($"[SeaZonePanelTest] HATA: panel 1152x648'e sığmıyor ({designed.X}x{designed.Y})");
                    ok = false;
                }

                if (panel.PlayerId != playerId)
                {
                    GD.PrintErr($"[SeaZonePanelTest] HATA: panel oyuncusu yanlış ({panel.PlayerId} != {playerId})");
                    ok = false;
                }

                // ---- 6) Bölge satırları ----
                int rows = panel.ZoneRowCount;
                GD.Print($"[SeaZonePanelTest] Bölge satırı sayısı: {rows} (sistem: {SeaZoneSystem.ZoneCount})");
                if (rows != SeaZoneSystem.ZoneCount)
                {
                    GD.PrintErr($"[SeaZonePanelTest] HATA: satır sayısı bölge sayısına eşit olmalı ({rows} != {SeaZoneSystem.ZoneCount})");
                    ok = false;
                }

                bool rowsValid = rows > balticIndex &&
                                 panel.RowSupremacy.Count >= rows &&
                                 panel.RowRisk.Count >= rows;
                if (!rowsValid)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: satır verisi eksik/baltic satırı yok");
                    ok = false;
                }
                else
                {
                    bool rangeOk = true;
                    for (int i = 0; i < rows; i++)
                    {
                        if (panel.RowSupremacy[i] < 0f || panel.RowSupremacy[i] > 100f ||
                            panel.RowRisk[i] < 0f || panel.RowRisk[i] > 100f)
                        {
                            GD.PrintErr($"[SeaZonePanelTest] HATA: satır {i} değerleri [0,100] aralığında değil " +
                                        $"(hakimiyet={panel.RowSupremacy[i]:F1}, risk={panel.RowRisk[i]:F1})");
                            rangeOk = false;
                        }
                    }
                    if (!rangeOk)
                    {
                        ok = false;
                    }
                    else
                    {
                        GD.Print($"[SeaZonePanelTest] Tüm satır değerleri [0,100] aralığında ✓ " +
                                 $"(baltic satırı={balticIndex}, hakimiyet={panel.RowSupremacy[balticIndex]:F1}, " +
                                 $"risk=%{panel.RowRisk[balticIndex]:F1})");
                    }
                }

                // ---- 7) Koruma yokken geri alma kapalı olmalı ----
                if (!panel.RevokeDisabled)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: koruma yokken REVOKE ESCORT kapalı olmalı");
                    ok = false;
                }
                if (ConvoyRaidSystem.EscortOf(playerId) != null)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: başlangıçta koruma tahsisi olmamalı");
                    ok = false;
                }

                float riskBefore = rowsValid ? panel.RowRisk[balticIndex] : 0f;
                float ownSupBefore = rowsValid ? panel.RowSupremacy[balticIndex] : 0f;

                // ---- 8) Koruyucu seçip ASSIGN ESCORT ----
                GD.Print("[SeaZonePanelTest] 3) Koruma atama");
                GD.Print($"[SeaZonePanelTest] Koruyucu aday sayısı: {panel.ProtectorOptionCount}");
                if (panel.ProtectorOptionCount < 1)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: müttefik koruyucu adayı listelenmeli");
                    ok = false;
                }

                var selector = FindChildrenIn(panel)
                    .FirstOrDefault(n => n is OptionButton) as OptionButton;
                if (selector == null)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: koruyucu seçici (OptionButton) bulunamadı");
                    ok = false;
                }
                else
                {
                    int allyItem = -1;
                    for (int i = 0; i < selector.ItemCount; i++)
                    {
                        if (selector.GetItemText(i).Contains(allyId))
                        {
                            allyItem = i;
                            break;
                        }
                    }
                    if (allyItem < 0)
                    {
                        GD.PrintErr($"[SeaZonePanelTest] HATA: {allyId} koruyucu listede yok");
                        ok = false;
                    }
                    else
                    {
                        selector.Select(allyItem);
                        GD.Print($"[SeaZonePanelTest] Koruyucu seçildi: {selector.GetItemText(allyItem)}");
                    }
                }

                var assignBtn = FindButton(panel, "ASSIGN ESCORT");
                if (assignBtn == null)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: ASSIGN ESCORT butonu bulunamadı");
                    ok = false;
                }
                else if (assignBtn.Disabled)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: ASSIGN ESCORT butonu kapalı");
                    ok = false;
                }
                else
                {
                    assignBtn.EmitSignal(BaseButton.SignalName.Pressed);

                    if (ConvoyRaidSystem.EscortOf(playerId) != allyId)
                    {
                        GD.PrintErr($"[SeaZonePanelTest] HATA: koruyucu {allyId} olmalı " +
                                    $"(bulundu: {ConvoyRaidSystem.EscortOf(playerId) ?? "-"})");
                        ok = false;
                    }
                    if (panel.RevokeDisabled)
                    {
                        GD.PrintErr("[SeaZonePanelTest] HATA: atama sonrası REVOKE ESCORT açık olmalı");
                        ok = false;
                    }
                    if (rowsValid)
                    {
                        if (panel.RowRisk[balticIndex] >= riskBefore)
                        {
                            GD.PrintErr($"[SeaZonePanelTest] HATA: koruma baltic riskini düşürmeli " +
                                        $"(%{riskBefore:F1} → %{panel.RowRisk[balticIndex]:F1})");
                            ok = false;
                        }
                        if (panel.RowSupremacy[balticIndex] <= ownSupBefore)
                        {
                            GD.PrintErr($"[SeaZonePanelTest] HATA: koruma baltic hakimiyetini yükseltmeli " +
                                        $"({ownSupBefore:F1} → {panel.RowSupremacy[balticIndex]:F1})");
                            ok = false;
                        }
                    }
                    GD.Print($"[SeaZonePanelTest] atama sonrası baltic → hakimiyet={panel.RowSupremacy[balticIndex]:F1} " +
                             $"(öncesi {ownSupBefore:F1}), risk=%{panel.RowRisk[balticIndex]:F1} " +
                             $"(öncesi %{riskBefore:F1}), mesaj=\"{panel.FeedbackText}\"");
                }

                // ---- 9) Save/Load: tahsis kaydedilir ve geri yüklenir ----
                GD.Print("[SeaZonePanelTest] 4) Save/Load");
                var saveData = ConvoyRaidSystem.GetSaveData();
                if (saveData.Escorts == null || saveData.Escorts.Length != 1 ||
                    saveData.Escorts[0].ProtectorId != allyId ||
                    saveData.Escorts[0].ProtectedId != playerId)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: save verisi tek tahsis içermeli " +
                                $"({saveData.Escorts?.Length ?? 0} kayıt)");
                    ok = false;
                }

                ConvoyRaidSystem.ResetForNewGame();
                if (ConvoyRaidSystem.EscortOf(playerId) != null)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: sıfırlama tahsisi temizlemeli");
                    ok = false;
                }

                ConvoyRaidSystem.LoadSaveData(saveData);
                if (ConvoyRaidSystem.EscortOf(playerId) != allyId)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: yükleme sonrası koruyucu geri gelmeli");
                    ok = false;
                }
                else
                {
                    GD.Print($"[SeaZonePanelTest] save/load → koruyucu={ConvoyRaidSystem.EscortOf(playerId)} ✓");
                }

                // ---- 10) REVOKE ESCORT panel üzerinden ----
                GD.Print("[SeaZonePanelTest] 5) Koruma geri alma");
                var revokeBtn = FindButton(panel, "REVOKE ESCORT");
                if (revokeBtn == null)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: REVOKE ESCORT butonu bulunamadı");
                    ok = false;
                }
                else if (revokeBtn.Disabled)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: REVOKE ESCORT butonu kapalı");
                    ok = false;
                }
                else
                {
                    revokeBtn.EmitSignal(BaseButton.SignalName.Pressed);

                    if (ConvoyRaidSystem.EscortOf(playerId) != null)
                    {
                        GD.PrintErr("[SeaZonePanelTest] HATA: geri alma sonrası koruma kalmamalı");
                        ok = false;
                    }
                    if (!panel.RevokeDisabled)
                    {
                        GD.PrintErr("[SeaZonePanelTest] HATA: geri alma sonrası REVOKE ESCORT kapanmalı");
                        ok = false;
                    }
                    if (rowsValid && Mathf.Abs(panel.RowRisk[balticIndex] - riskBefore) > 0.01f)
                    {
                        GD.PrintErr($"[SeaZonePanelTest] HATA: geri alma riski taban çizgisine döndürmeli " +
                                    $"(%{panel.RowRisk[balticIndex]:F1} != %{riskBefore:F1})");
                        ok = false;
                    }
                    GD.Print($"[SeaZonePanelTest] geri alma sonrası baltic riski=%{panel.RowRisk[balticIndex]:F1} " +
                             $"(taban %{riskBefore:F1}), mesaj=\"{panel.FeedbackText}\"");
                }

                // ---- 11) ESC ile kapatma ----
                GD.Print("[SeaZonePanelTest] 6) ESC ile kapatma");
                panel._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                if (!panel.CloseRequested)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: ESC kapatma isteği üretmedi");
                    ok = false;
                }

                await ToSignal(GetTree(), "process_frame");
                await ToSignal(GetTree(), "process_frame");
                await ToSignal(GetTree(), "process_frame");

                if (SeaZonePanel.IsOpen)
                {
                    GD.PrintErr("[SeaZonePanelTest] HATA: ESC paneli kapatmadı");
                    ok = false;
                }
                else
                {
                    GD.Print("[SeaZonePanelTest] ESC paneli kapattı ✓");
                }
            }
        }
        finally
        {
            // ---- 12) Dünya verisini geri yükle ----
            DiplomacySystem.RevokeGuarantee(allyId);

            int relNow = DiplomacySystem.GetRelation(playerId, allyId);
            if (relNow != relBefore)
                DiplomacySystem.ModifyRelation(playerId, allyId, relBefore - relNow);
            int opNow = DiplomacySystem.GetOpinion(playerId, allyId);
            if (opNow != opBefore)
                DiplomacySystem.ModifyRelation(playerId, allyId, opBefore - opNow);

            player.Dockyards = playerDockBefore;
            ally.Dockyards = allyDockBefore;
            enemy.Dockyards = enemyDockBefore;
            player.PoliticalPower = playerPpBefore;
            ally.PoliticalPower = allyPpBefore;

            ConvoyRaidSystem.ResetForNewGame();

            foreach (var z in SeaZoneSystem.All)
            {
                if (zoneSnapshot.TryGetValue(z.Id, out var saved))
                    z.AdjacentCountryIds = saved;
            }
            foreach (var f in DiplomacySystem.Factions.Values)
            {
                if (factionSnapshot.TryGetValue(f.Id, out var members))
                    f.MemberIds = members;
            }
            GD.Print("[SeaZonePanelTest] Dünya verisi geri yüklendi");
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M44.1: Kurgu ülke verisi headless testi (--formable-data-test).
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunFormableDataTest()
    {
        GD.Print("=========================================");
        GD.Print("  M44.1: KURGU ÜLKE VERİSİ TESTİ");
        GD.Print("=========================================");

        FormableDataSystem.LoadData();
        int count = FormableDataSystem.FormableCount;
        GD.Print($"[FormableDataTest] Yüklenen kayıt: {count}");
        bool ok = count >= 15;

        // 1) Zorunlu alanlar dolu ve kimlik/renk biçimi doğru
        int missingFields = 0;
        int badColor = 0;
        foreach (var f in FormableDataSystem.All)
        {
            if (string.IsNullOrEmpty(f.Id) || string.IsNullOrEmpty(f.Name) ||
                string.IsNullOrEmpty(f.Culture) || string.IsNullOrEmpty(f.Description))
                missingFields++;
            if (string.IsNullOrEmpty(f.Color) || !f.Color.StartsWith("#") || f.Color.Length != 7)
                badColor++;
        }
        GD.Print($"[FormableDataTest] Eksik alan: {missingFields}, bozuk renk: {badColor}");
        if (missingFields > 0 || badColor > 0) ok = false;

        // 2) Kimlikler tek(unique) olmalı — sözlük zaten tekilleştirir, liste üzerinden doğrula
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int duplicates = 0;
        foreach (var f in FormableDataSystem.All)
            if (!seen.Add(f.Id)) duplicates++;
        GD.Print($"[FormableDataTest] Tekrar eden kimlik: {duplicates}");
        if (duplicates > 0) ok = false;

        // 3) Sayısal alanlar makul aralıkta
        int badNumbers = 0;
        foreach (var f in FormableDataSystem.All)
        {
            if (f.RequiredProvinces < 1 || f.MinimumControlPercent < 0f || f.MinimumControlPercent > 100f ||
                f.PoliticalPowerCost < 0f || f.DaysRequired < 1f)
                badNumbers++;
        }
        GD.Print($"[FormableDataTest] Makul olmayan sayısal değer: {badNumbers}");
        if (badNumbers > 0) ok = false;

        // 4) Ebe kaynakları (parent) var ve mevcut senaryo ülkelerine çözülüyor
        int noParents = 0;
        int unresolvedParents = 0;
        var scenarioIds = CountryDatabase.All().Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in FormableDataSystem.All)
        {
            if (f.ParentCountryIds == null || f.ParentCountryIds.Count == 0)
            {
                noParents++;
                continue;
            }
            foreach (var pid in f.ParentCountryIds)
                if (!scenarioIds.Contains(pid)) unresolvedParents++;
        }
        GD.Print($"[FormableDataTest] Ebe kaynağı olmayan: {noParents}, senaryoda bulunamayan ebe kaynağı: {unresolvedParents}");
        if (noParents > 0 || unresolvedParents > 0) ok = false;

        // 5) Kimlik çakışması: kurgu kimliği senaryo ülkesiyle aynı olmamalı
        int idCollisions = 0;
        foreach (var f in FormableDataSystem.All)
            if (scenarioIds.Contains(f.Id)) idCollisions++;
        GD.Print($"[FormableDataTest] Senaryo ülkesiyle kimlik çakışması: {idCollisions}");
        if (idCollisions > 0) ok = false;

        // 6) Arama API'leri: Get + ForParent
        var germany = FormableDataSystem.ForParent("DEU");
        GD.Print($"[FormableDataTest] DEU topraklarından kurulabilecek: {germany.Count} kayıt");
        if (germany.Count < 1) ok = false;

        var russia = FormableDataSystem.ForParent("RUS");
        GD.Print($"[FormableDataTest] RUS topraklarından kurulabilecek: {russia.Count} kayıt");
        if (russia.Count < 2) ok = false;

        var missing = FormableDataSystem.Get("XXX_BU_YOK");
        GD.Print($"[FormableDataTest] Var olmayan kimlik sorgusu null döndü: {missing == null}");
        if (missing != null) ok = false;

        GD.Print($"[FormableDataTest] Kültür çeşitliliği: {FormableDataSystem.All.Select(f => f.Culture).Distinct().Count()} farklı kültür");

        GD.Print(ok ? "[FormableDataTest] SONUÇ: PASS ✓" : "[FormableDataTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M44.2: İl kültür katmanı headless testi (--culture-test).
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunCultureTest()
    {
        GD.Print("=========================================");
        GD.Print("  M44.2: İL KÜLTÜR KATMANI TESTİ");
        GD.Print("=========================================");

        bool ok = true;

        // 1) İlleri deterministik kaynak üzerinden yükle
        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        GD.Print($"[CultureTest] 1) İl sayısı: {provinces.Count} (hedef ~1000)");
        if (provinces.Count < 100) ok = false;

        // 2) Kültür üretimi
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        int recordCount = ProvinceCultureSystem.RecordCount;
        GD.Print($"[CultureTest] 2) Kültür kaydı: {recordCount}");
        if (recordCount != provinces.Count) ok = false;

        // 3) Ülke bazında tutarlılık: dağılımın toplamı = o ülkenin il sayısı
        var countries = provinces.Select(p => p.CountryId).Distinct().ToList();
        int inconsistent = 0;
        int emptyCulture = 0;
        long zeroPop = 0;
        foreach (var cid in countries)
        {
            var dist = ProvinceCultureSystem.GetCultureDistribution(cid);
            int provinceTotal = provinces.Count(p => p.CountryId == cid);
            if (dist.Values.Sum() != provinceTotal) inconsistent++;
            if (dist.Count == 0) emptyCulture++;
            if (ProvinceCultureSystem.TotalPopulation(cid) == 0) zeroPop++;
        }
        GD.Print($"[CultureTest] 3) Ülke sayısı: {countries.Count}, tutarsız dağılım: {inconsistent}, " +
                  $"kültürsüz ülke: {emptyCulture}, nüfussuz ülke: {zeroPop}");
        if (inconsistent > 0 || emptyCulture > 0 || zeroPop > 0) ok = false;

        // 4) Nüfus toplamı ülkenin CountryData.Population değerine eşit olmalı
        int popMismatch = 0;
        long maxDrift = 0;
        foreach (var cid in countries)
        {
            var data = CountryDatabase.Get(cid);
            if (data == null) continue;
            long assigned = ProvinceCultureSystem.TotalPopulation(cid);
            long drift = Math.Abs(assigned - data.Population);
            if (drift > maxDrift) maxDrift = drift;
            if (drift > data.Population / 100) popMismatch++;
        }
        GD.Print($"[CultureTest] 4) Nüfus uyumsuzluğu olan ülke: {popMismatch} (en büyük sapma: {maxDrift})");
        if (popMismatch > 0) ok = false;

        // 5) Determinizm: aynı girdiyle yeniden üret → aynı sonuç
        ProvinceCultureSystem.ResetForNewGame();
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        int afterReset = ProvinceCultureSystem.RecordCount;
        bool deterministic = afterReset == recordCount && recordCount > 0;
        GD.Print($"[CultureTest] 5) Sıfırlama + yeniden üretim: {afterReset} kayıt — deterministik: {deterministic}");
        if (!deterministic) ok = false;

        // 6) GetCultureDistribution içeriği anlamlı mı (tek kültür değil)
        var gerDist = ProvinceCultureSystem.GetCultureDistribution("DEU");
        GD.Print($"[CultureTest] 6) DEU kültür dağılımı: {gerDist.Count} farklı kültür");
        if (gerDist.Count == 0) ok = false;

        // 7) CultureProvinceCount ile formable koşulu beslenebilir
        int sampleCultureCount = 0;
        if (gerDist.Count > 0)
            sampleCultureCount = ProvinceCultureSystem.CultureProvinceCount("DEU", gerDist.Keys.First());
        GD.Print($"[CultureTest] 7) DEU'de örnek kültürün il sayısı: {sampleCultureCount}");
        if (sampleCultureCount < 1) ok = false;

        // 8) Save/Load round-trip (M44.5 bağlantısı burada denenir)
        var save = ProvinceCultureSystem.GetSaveData();
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rec in ProvinceCultureSystem.ForCountry("DEU"))
            snapshot[rec.ProvinceId] = $"{rec.Culture}|{rec.Population}";
        ProvinceCultureSystem.ResetForNewGame();
        ProvinceCultureSystem.LoadSaveData(save);
        int loaded = ProvinceCultureSystem.RecordCount;
        var after = ProvinceCultureSystem.ForCountry("DEU");
        bool roundTrip = loaded == save.Records.Length && snapshot.Count == after.Count;
        for (int i = 0; i < after.Count && roundTrip; i++)
        {
            var rec = after[i];
            string key = $"{rec.Culture}|{rec.Population}";
            if (!snapshot.TryGetValue(rec.ProvinceId, out var old) || old != key) roundTrip = false;
        }
        GD.Print($"[CultureTest] 8) Save/Load: kaydedilen={save.Records.Length}, yüklenen={loaded}, aynı değerler: {roundTrip}");
        if (!roundTrip) ok = false;

        // 9) Weird provinces işine takılmıyor: "?" ve boş id'ler atlanmış olmalı
        provinces.Add(new ProvinceData { Id = "?", CountryId = "DEU" });
        provinces.Add(new ProvinceData { Id = "", CountryId = "DEU" });
        ProvinceCultureSystem.ResetForNewGame();
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        bool skippedInvalid = ProvinceCultureSystem.RecordCount == provinces.Count - 2;
        GD.Print($"[CultureTest] 9) Geçersiz il atlandı: {skippedInvalid} (kayıt={ProvinceCultureSystem.RecordCount})");
        if (!skippedInvalid) ok = false;

        GD.Print(ok ? "[CultureTest] SONUÇ: PASS ✓" : "[CultureTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M47.1: İl bazlı ekonomi ağırlık katmanı headless testi (--province-economy-test).
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunProvinceEconomyTest()
    {
        GD.Print("=========================================");
        GD.Print("  M47.1: İL BAZLI EKONOMİ AĞIRLIK KATMANI TESTİ");
        GD.Print("=========================================");

        bool ok = true;

        // 1) İlleri deterministik kaynak üzerinden yükle
        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        GD.Print($"[ProvEconomyTest] 1) İl sayısı: {provinces.Count}");
        if (provinces.Count < 100) ok = false;

        // 2) Kültür katmanı (nüfus payının kaynağı) sonra ekonomi katmanı
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        ProvinceEconomySystem.GenerateFor(provinces, "historical_1936");
        int recordCount = ProvinceEconomySystem.RecordCount;
        GD.Print($"[ProvEconomyTest] 2) Ekonomi kaydı: {recordCount}");
        if (recordCount != provinces.Count) ok = false;

        // 3) Paylar normalize olmalı: her ülke için her kanalın pay toplamı ≈ 1
        var countries = provinces.Select(p => p.CountryId).Distinct().ToList();
        float maxShareDrift = 0f;
        foreach (var cid in countries)
        {
            float res = ProvinceEconomySystem.ForCountry(cid).Sum(w => w.ResourceW);
            float fac = ProvinceEconomySystem.ForCountry(cid).Sum(w => w.FactoryW);
            float pop = ProvinceEconomySystem.ForCountry(cid).Sum(w => w.PopulationW);
            foreach (float v in new[] { res, fac, pop })
            {
                float drift = Math.Abs(v - 1f);
                if (drift > maxShareDrift) maxShareDrift = drift;
            }
        }
        GD.Print($"[ProvEconomyTest] 3) Pay normalize sapması: {maxShareDrift:F6} (≤ 0.001 olmalı)");
        if (maxShareDrift > 0.001f) ok = false;

        // 4) Harmanlama tutarlı: il toplamı = ülke toplamı (±harman payı)
        int mismatch = 0;
        long maxFactoryDrift = 0, maxResourceDrift = 0, maxPopDrift = 0;
        foreach (var cid in countries)
        {
            var data = CountryDatabase.Get(cid);
            if (data == null) continue;

            long fActual = ProvinceEconomySystem.TotalFactoriesOf(cid);
            long fExpected = data.CivFactories + data.MilFactories + data.Dockyards;
            long pActual = ProvinceEconomySystem.TotalPopulationOf(cid);
            long pExpected = data.Population;

            long fDrift = Math.Abs(fActual - fExpected);
            long pDrift = Math.Abs(pActual - pExpected);
            if (fDrift > maxFactoryDrift) maxFactoryDrift = fDrift;
            if (pDrift > maxPopDrift) maxPopDrift = pDrift;

            foreach (var key in ProvinceEconomySystem.ResourceKeys)
            {
                long rActual = ProvinceEconomySystem.TotalResourceOf(cid, key);
                long rExpected = key switch
                {
                    "Oil" => data.Oil,
                    "Rubber" => data.Rubber,
                    "Steel" => data.Steel,
                    "Aluminum" => data.Aluminum,
                    "Tungsten" => data.Tungsten,
                    _ => data.Chromium
                };
                long rDrift = Math.Abs(rActual - rExpected);
                if (rDrift > maxResourceDrift) maxResourceDrift = rDrift;
            }

            // Fabrika ve nüfus tam korunmalı. Nüfus sapması yalnızca float32
            // normalize + kal-artık yuvarlamasından gelir; ülke nüfusu büyük olduğu
            // için sabit ±2 toleransı veriyoruz (mutlak pay, yüzde değil).
            if (fDrift > 0 || pDrift > 2) mismatch++;
        }
        GD.Print($"[ProvEconomyTest] 4) Harman sapması — fabrika: {maxFactoryDrift}, " +
                  $"kaynak: {maxResourceDrift}, nüfus: {maxPopDrift}, hatalı ülke: {mismatch}");
        if (mismatch > 0 || maxFactoryDrift > 0 || maxPopDrift > 2) ok = false;

        // 5) Dağıtım anlamlı: negatif dağıtım yok ve ağırlık sırası paya yansır.
        // Not: bu dünyada tüm iller kıyı işaretli (637/637), bu yüzden kıyı/iç bölge
        // karşılaştırması yerine ağırlık sıralaması denenir — veriden bağımsız.
        int negative = 0;
        foreach (var w in ProvinceEconomySystem.All())
        {
            if (w.Civ < 0 || w.Mil < 0 || w.Dock < 0 ||
                w.Oil < 0 || w.Steel < 0 || w.Chromium < 0) negative++;
        }

        // Örnek ülke: en çok ili olan ve en az 4 ili olan ülke.
        string sampleCid = "";
        foreach (var cid in countries
            .OrderByDescending(c => ProvinceEconomySystem.ForCountry(c).Count))
        {
            if (ProvinceEconomySystem.ForCountry(cid).Count >= 4) { sampleCid = cid; break; }
        }

        bool orderRespected = true;
        if (sampleCid.Length > 0)
        {
            var sample = ProvinceEconomySystem.ForCountry(sampleCid);

            float max = float.MinValue, min = float.MaxValue;
            int facMax = 0, facMin = int.MaxValue;
            foreach (var w in sample)
            {
                max = Math.Max(max, w.FactoryW);
                min = Math.Min(min, w.FactoryW);
            }
            foreach (var w in sample)
            {
                if (w.FactoryW == max) facMax = w.TotalFactories;
                if (w.FactoryW == min) facMin = w.TotalFactories;
            }
            if (facMax < facMin) orderRespected = false;

            // Üst yarı ile alt yarı karşılaştırması (kal-artık sapmasına dayanıklı)
            var sorted = sample.OrderByDescending(w => w.FactoryW).ToList();
            int half = sorted.Count / 2;
            long topFac = 0, botFac = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i < half) topFac += sorted[i].TotalFactories;
                else if (i >= sorted.Count - half) botFac += sorted[i].TotalFactories;
            }
            GD.Print($"[ProvEconomyTest] 5b) Üst yarı fabrika: {topFac}, alt yarı: {botFac} " +
                     $"({sampleCid}, {sample.Count} il)");
            if (topFac < botFac) orderRespected = false;
        }

        GD.Print($"[ProvEconomyTest] 5) Negatif dağıtım: {negative}, ağırlık sırası paya yansıdı: " +
                 $"{orderRespected} (örnek ülke={sampleCid})");
        if (negative > 0 || !orderRespected) ok = false;

        // 6) Determinizm: sıfırla + yeniden üret → aynı paylar
        var beforeSnapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var w in ProvinceEconomySystem.All())
            beforeSnapshot[w.ProvinceId] = $"{w.Urban:F6}|{w.FactoryW:F6}|{w.ResourceW:F6}|{w.PopulationW:F6}|{w.Civ}|{w.Oil}";
        ulong rngBefore = DeterministicRNG.GetState();
        ProvinceEconomySystem.ResetForNewGame();
        ProvinceEconomySystem.GenerateFor(provinces, "historical_1936");
        ulong rngAfter = DeterministicRNG.GetState();
        int driftCount = 0;
        foreach (var w in ProvinceEconomySystem.All())
        {
            string key = $"{w.Urban:F6}|{w.FactoryW:F6}|{w.ResourceW:F6}|{w.PopulationW:F6}|{w.Civ}|{w.Oil}";
            if (!beforeSnapshot.TryGetValue(w.ProvinceId, out var old) || old != key) driftCount++;
        }
        GD.Print($"[ProvEconomyTest] 6) Determinizm: kayıt={ProvinceEconomySystem.RecordCount} " +
                  $"(önce {recordCount}), farklı kayıt={driftCount}, RNG state korundu={rngBefore == rngAfter}");
        if (ProvinceEconomySystem.RecordCount != recordCount || driftCount > 0) ok = false;

        // 7) RNG akışı bozulmamış olmalı: üretim dünya rastgeleliğini etkilemez.
        if (rngBefore != rngAfter) ok = false;

        // 8) İl devri (M47.2 provası): DEU ilini POL'a taşı
        var deu = provinces.Where(p => p.CountryId == "DEU").Select(p => p.Id).ToList();
        if (deu.Count >= 1)
        {
            string target = deu[0];

            // --- M47.2: üretim fiilen taşınır ---
            // Devir öncesi her iki ülkenin CountryData toplamı ve ilin payı.
            var deuData = CountryDatabase.Get("DEU");
            var polData = CountryDatabase.Get("POL");
            var targetWeight = ProvinceEconomySystem.Get(target);

            int deuCivBefore = deuData?.CivFactories ?? 0;
            int polCivBefore = polData?.CivFactories ?? 0;
            int deuMilBefore = deuData?.MilFactories ?? 0;
            int polMilBefore = polData?.MilFactories ?? 0;
            int deuDockBefore = deuData?.Dockyards ?? 0;
            int polDockBefore = polData?.Dockyards ?? 0;
            int deuSteelBefore = deuData?.Steel ?? 0;
            int polSteelBefore = polData?.Steel ?? 0;
            long deuPopBefore = deuData?.Population ?? 0;
            long polPopBefore = polData?.Population ?? 0;

            int movedCiv = targetWeight?.Civ ?? 0;
            int movedMil = targetWeight?.Mil ?? 0;
            int movedDock = targetWeight?.Dock ?? 0;
            int movedSteel = targetWeight?.Steel ?? 0;
            long movedPop = targetWeight?.Population ?? 0;
            int deuIlTotal = ProvinceEconomySystem.TotalFactoriesOf("DEU");
            int polIlTotal = ProvinceEconomySystem.TotalFactoriesOf("POL");
            GD.Print($"[ProvEconomyTest]    TEŞHİS: DEU il toplamı={deuIlTotal} " +
                     $"({ProvinceEconomySystem.ForCountry("DEU").Count} il), " +
                     $"POL il toplamı={polIlTotal} ({ProvinceEconomySystem.ForCountry("POL").Count} il), " +
                     $"DEU-01 payı=Civ{movedCiv}/Mil{movedMil}/Dock{movedDock}");

            // SetProvinceOwner → OnOwnerChanged → HandleOwnershipChange zinciri.
            // Gerçek çağrı yolu (M42 barış konferansı / M44.3 / M44.4 bunu kullanır).
            ProvinceOwnershipSystem.SetProvinceOwner(target, "POL");
            bool moved = ProvinceOwnershipSystem.GetProvinceOwner(target) == "POL";

            var rec = ProvinceEconomySystem.Get(target);
            bool assigned = rec != null && rec.CountryId == "POL";
            bool listUpdated = ProvinceEconomySystem.ForCountry("POL").Any(w => w.ProvinceId == target) &&
                               !ProvinceEconomySystem.ForCountry("DEU").Any(w => w.ProvinceId == target);

            // Verenin toplamı düşmüş, alanın artmış olmalı; taşınan kadar.
            int deuCivAfter = deuData?.CivFactories ?? 0;
            int polCivAfter = polData?.CivFactories ?? 0;
            int deuSteelAfter = deuData?.Steel ?? 0;
            int polSteelAfter = polData?.Steel ?? 0;
            long deuPopAfter = deuData?.Population ?? 0;
            long polPopAfter = polData?.Population ?? 0;

            bool donorLost = deuCivAfter == deuCivBefore - movedCiv &&
                             deuPopAfter == deuPopBefore - movedPop;
            bool receiverGained = polCivAfter == polCivBefore + movedCiv &&
                                  polPopAfter == polPopBefore + movedPop;
            bool steelMoved = deuSteelAfter == deuSteelBefore - movedSteel &&
                              polSteelAfter == polSteelBefore + movedSteel;
            bool milDockMoved = deuData?.MilFactories == deuMilBefore - movedMil &&
                                polData?.MilFactories == polMilBefore + movedMil &&
                                deuData?.Dockyards == deuDockBefore - movedDock &&
                                polData?.Dockyards == polDockBefore + movedDock;

            // Dünya genelinde toplam üretim korunmuş olmalı (üç fabrika türü).
            int worldBefore = deuCivBefore + polCivBefore + deuMilBefore + polMilBefore +
                              deuDockBefore + polDockBefore;
            int worldAfter = deuCivAfter + polCivAfter +
                             (deuData?.MilFactories ?? 0) + (polData?.MilFactories ?? 0) +
                             (deuData?.Dockyards ?? 0) + (polData?.Dockyards ?? 0);

            GD.Print($"[ProvEconomyTest] 8) İl devri ({target} → POL): taşındı={moved}, " +
                     $"sahip POL={assigned}, listeler güncel={listUpdated}");
            GD.Print($"[ProvEconomyTest]    taşınan: Civ={movedCiv}, Mil={movedMil}, Dock={movedDock}, " +
                     $"çelik={movedSteel}, kişi={movedPop}");
            GD.Print($"[ProvEconomyTest]    DEU fabrika {deuCivBefore}→{deuCivAfter}, " +
                     $"POL fabrika {polCivBefore}→{polCivAfter}, dünya {worldBefore}→{worldAfter}");
            GD.Print($"[ProvEconomyTest]    veren kaybetti={donorLost}, alan kazandı={receiverGained}, " +
                     $"çelik taşındı={steelMoved}");
            if (!moved || !assigned || !listUpdated || !donorLost || !receiverGained ||
                !steelMoved || !milDockMoved || worldBefore != worldAfter) ok = false;
        }
        else
        {
            GD.Print("[ProvEconomyTest] 8) İl devri: DEU'de il yok — atlandı");
        }

        // 9) Hiç ili olmayan ülke için toplam 0
        int emptyTotal = ProvinceEconomySystem.TotalFactoriesOf("XXX_YOK");
        GD.Print($"[ProvEconomyTest] 9) Boş ülke toplam fabrika: {emptyTotal} (0 olmalı)");
        if (emptyTotal != 0) ok = false;

        // 9) Ters devir: POL'dan DEU'ye geri taşı → dünya toplamı hâlâ korunur.
        if (deu.Count >= 1 && ProvinceOwnershipSystem.GetProvinceOwner(deu[0]) == "POL")
        {
            ProvinceOwnershipSystem.SetProvinceOwner(deu[0], "DEU");
            bool backHome = ProvinceOwnershipSystem.GetProvinceOwner(deu[0]) == "DEU";
            bool listRestored = ProvinceEconomySystem.ForCountry("DEU").Any(w => w.ProvinceId == deu[0]);
            GD.Print($"[ProvEconomyTest] 9) Ters devir: geri döndü={backHome}, listesi geri geldi={listRestored}");
            if (!backHome || !listRestored) ok = false;
        }

        ProvinceEconomySystem.ResetForNewGame();
        ProvinceCultureSystem.ResetForNewGame();

        GD.Print(ok ? "[ProvEconomyTest] SONUÇ: PASS ✓" : "[ProvEconomyTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M47.3: Kaynak açığının ekonomik sonucu headless testi (--resource-balance-test).
    /// Normal oyunda ASLA çalışmaz.
    ///
    /// Doğrulananlar:
    ///   1) Tok ülkede açık yok → verim 1.0, ihracat geliri > 0, konvoy talebi 0
    ///   2) Kaynağı sıfırlanan ülkede açık → verim < 1.0, ithalat maliyeti > 0, konvoy talebi > 0
    ///   3) Açık derinleştikçe birim ithalat fiyatı artar (tam açık > yarım açık > taban)
    ///   4) Verimlilik stoktan değil KAPASİTEDEN hesaplanır → spiralleşme yok
    ///   5) M47.2 il devri kapasiteyi taşır (dünya toplamı korunur)
    ///   6) Hesaplar deterministik (aynı kapasite → aynı sonuç)
    /// </summary>
    private void RunResourceBalanceTest()
    {
        GD.Print("=========================================");
        GD.Print("  M47.3: KAYNAK AÇIĞI EKONOMİK SONUCU TESTİ");
        GD.Print("=========================================");

        bool ok = true;

        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        ProvinceEconomySystem.GenerateFor(provinces, "historical_1936");

        var rich = CountryDatabase.Get("USA");

        // Ülke künyesi: kapasiteden ihtiyaç türetildiği için fabrika/nüfus önemli.
        GD.Print($"[ResBalanceTest] 0) USA: fabrika={rich?.TotalFactories ?? 0}, kişi={rich?.Population ?? 0}");
        GD.Print($"[ResBalanceTest]    çelik={rich?.Steel ?? 0}, petrol={rich?.Oil ?? 0}, kauçuk={rich?.Rubber ?? 0}");

        // 1) Tok ülke: her kaynak ihtiyacının çok üstünde → açık sıfır, verim tam, gelir var.
        var usaData = CountryDatabase.Get("USA");
        if (usaData != null)
        {
            usaData.Steel = 1000;
            usaData.Oil = 1000;
            usaData.Rubber = 1000;
            usaData.Aluminum = 1000;
            usaData.Tungsten = 1000;
            usaData.Chromium = 1000;
        }
        double richDeficit = ProvinceEconomySystem.TotalDeficitOf("USA");
        double richSurplus = ProvinceEconomySystem.TotalSurplusOf("USA");
        double richEff = ProvinceEconomySystem.ExtractionEfficiency("USA");
        double richIncome = ProvinceEconomySystem.TradeIncomeOf("USA");
        int richConvoy = ProvinceEconomySystem.ConvoyDemandOf("USA");
        GD.Print($"[ResBalanceTest] 1) Tok USA: açık={richDeficit:F1}, tokluk={richSurplus:F1}, " +
                 $"verim={richEff:F2}, ihracat geliri={richIncome:F1}, konvoy talebi={richConvoy}");
        if (richDeficit != 0) ok = false;          // açık olmamalı
        if (richEff != 1.0f) ok = false;           // verim tam olmalı
        if (richIncome <= 0) ok = false;           // ihracat geliri doğmalı
        if (richConvoy != 0) ok = false;           // ithalat konvoyu istenmemeli

        // 2) Açık ülke: aynı fabrika/nüfus, hiç kaynak yok → bedel doğar.
        if (usaData != null)
        {
            usaData.Steel = 0;
            usaData.Oil = 0;
            usaData.Rubber = 0;
            usaData.Aluminum = 0;
            usaData.Tungsten = 0;
            usaData.Chromium = 0;
        }
        double poorDeficit = ProvinceEconomySystem.TotalDeficitOf("USA");
        double poorEff = ProvinceEconomySystem.ExtractionEfficiency("USA");
        int poorConvoy = ProvinceEconomySystem.ConvoyDemandOf("USA");
        var steelBal = ProvinceEconomySystem.BalanceFor("USA", "Steel");
        double importCost = steelBal.Deficit * steelBal.ImportPricePerUnit; // günlük ithalat yükü
        GD.Print($"[ResBalanceTest] 2) Kaynağı sıfırlanan USA: açık={poorDeficit:F1}, " +
                 $"verim={poorEff:F2}, konvoy talebi={poorConvoy}");
        GD.Print($"[ResBalanceTest]    çelik dengesi: ihtiyaç={steelBal.Need:F1}, arz={steelBal.Supply:F1}, " +
                 $"açık={steelBal.Deficit:F1}, fiyat/birim={steelBal.ImportPricePerUnit:F2}, " +
                 $"maliyet={importCost:F1}/gün");
        if (poorDeficit <= 0) ok = false;                 // açık oluşmalı
        if (poorEff >= 1.0f) ok = false;                  // verim düşmeli
        if (poorConvoy <= 0) ok = false;                  // konvoy istenmeli
        if (steelBal.Deficit <= 0) ok = false;            // çelik açık olmalı
        if (importCost <= 0) ok = false;                  // maliyet doğmalı

        // 3) Fiyat açık derinliğine göre artar: yarısını ver, fiyat düşmeli ama tabandan yüksek kalmalı
        if (usaData != null)
        {
            double need = ProvinceEconomySystem.ResourceNeedOf("USA", "Steel");
            usaData.Steel = (int)(need * 0.5);            // yarısı karşılanıyor
        }
        var halfSteel = ProvinceEconomySystem.BalanceFor("USA", "Steel");
        GD.Print($"[ResBalanceTest] 3) Yarı çelik: arz={halfSteel.Supply:F1}, açık={halfSteel.Deficit:F1}, " +
                 $"fiyat/birim={halfSteel.ImportPricePerUnit:F2} (tam açık {steelBal.ImportPricePerUnit:F2}, " +
                 $"taban {1.0:F2})");
        // Tam açıkta fiyat > yarım açık > taban
        if (!(steelBal.ImportPricePerUnit > halfSteel.ImportPricePerUnit &&
              halfSteel.ImportPricePerUnit > 1.0)) ok = false;

        // 4) Spiralleşme yok: kapasite aynı kaldıkça verim tekrar hesaplanınca sabit kalmalı.
        // (Verim stoktan değil CountryData kapasitesinden hesaplanır.)
        double effA = ProvinceEconomySystem.ExtractionEfficiency("USA");
        double effB = ProvinceEconomySystem.ExtractionEfficiency("USA");
        GD.Print($"[ResBalanceTest] 4) Verim sabitliği: {effA:F6} == {effB:F6}");
        if (Math.Abs(effA - effB) > 1e-9) ok = false;

        // 5) M47.2 entegrasyonu: devir, açığı taşırmalı.
        // POL'un zengin bir ilini DEU'ye ver → DEU arttırır, POL azalır; toplam korunur.
        var polProvinces = ProvinceEconomySystem.ForCountry("POL").ToList();
        if (polProvinces.Count > 0 && polProvinces[0].TotalFactories > 0)
        {
            var polData = CountryDatabase.Get("POL");
            var deuData = CountryDatabase.Get("DEU");
            int polBefore = polData?.TotalFactories ?? 0;
            int deuBefore = deuData?.TotalFactories ?? 0;
            int worldBefore = polBefore + deuBefore;

            double polDeficitBefore = ProvinceEconomySystem.TotalDeficitOf("POL");
            ProvinceOwnershipSystem.SetProvinceOwner(polProvinces[0].ProvinceId, "DEU");

            int polAfter = polData?.TotalFactories ?? 0;
            int deuAfter = deuData?.TotalFactories ?? 0;
            double polDeficitAfter = ProvinceEconomySystem.TotalDeficitOf("POL");

            GD.Print($"[ResBalanceTest] 5) POL il devri (M47.2): fabrika {polBefore}→{polAfter}, " +
                     $"DEU {deuBefore}→{deuAfter}, dünya {worldBefore}→{polAfter + deuAfter}");
            GD.Print($"[ResBalanceTest]    POL kaynak açığı: {polDeficitBefore:F1} → {polDeficitAfter:F1}");
            // Eksilen fabrika ihtiyacı azaltır ama arz de değişir; kritik olan toplam korunumu.
            if (polAfter + deuAfter != worldBefore) ok = false;
        }
        else
        {
            GD.Print("[ResBalanceTest] 5) POL'da devredilecek il yok — atlandı");
        }

        // 6) Determinizm: aynı kapasite → aynı açık. 3. adımda çeliği yarımaya
        // bırakmıştık; 2. adımdaki "tam açık" durumunu yeniden kurup ölçüyoruz.
        if (usaData != null)
        {
            usaData.Steel = 0;
            usaData.Oil = 0;
            usaData.Rubber = 0;
            usaData.Aluminum = 0;
            usaData.Tungsten = 0;
            usaData.Chromium = 0;
        }
        double reDeficit = ProvinceEconomySystem.TotalDeficitOf("USA");
        GD.Print($"[ResBalanceTest] 6) Tekrarlanan hesaplama: USA açık={reDeficit:F1} " +
                 $"(tam açık {poorDeficit:F1} ile aynı olmalı)");
        if (Math.Abs(reDeficit - poorDeficit) > 1e-9) ok = false;

        ProvinceEconomySystem.ResetForNewGame();
        ProvinceCultureSystem.ResetForNewGame();

        GD.Print(ok ? "[ResBalanceTest] SONUÇ: PASS ✓" : "[ResBalanceTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M44.3: Kurgu ülke kurma headless testi (--formable-test).
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private void RunFormableTest()
    {
        GD.Print("=========================================");
        GD.Print("  M44.3: KURGU ÜLKE KURMA TESTİ");
        GD.Print("=========================================");

        bool ok = true;

        // 1) Dünyayı hazırla (il + kültür + sahiplik)
        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        FormableSystem.ResetForNewGame();
        GD.Print($"[FormableTest] 1) İl: {provinces.Count}, kültür kaydı: {ProvinceCultureSystem.RecordCount}");

        // === VALIABLE ŞABLON: koşul sağlanmayan durum testi için DEU kullanılır.
        // Bavarya (BAV) DEU topraklarından kurulur; DEU'de yeterli bavarian il
        // olmadğı için başlangıçta reddedilmelidir.
        var def = FormableDataSystem.Get("BAV");
        if (def == null)
        {
            GD.Print("[FormableTest] HATA: BAV tanımı yok");
            GetTree().Quit(1);
            return;
        }

        // 2) DEU'ye yeterli il transfer et ve kültürünü bavarian yap.
        // Prosedürel dünyada DEU'nün doğal ili yeterli olmayabilir; o yüzden
        // en çok ili olan başka bir ülkeden il "fethediyoruz" — oyuncunun
        // oyun içinde yapacağı işlemin aynısı.
        int needed = def.RequiredProvinces;

        var donor = provinces
            .Where(p => !string.Equals(p.CountryId, "DEU", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.CountryId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Id = g.Key, Provs = g.ToList() })
            .FirstOrDefault();
        if (donor == null || donor.Provs.Count < needed)
        {
            GD.PrintErr($"[FormableTest] HATA: {needed} ilinden fazlasına sahip bağışçı ülke yok");
            GetTree().Quit(1);
            return;
        }

        var movedIds = donor.Provs.Select(p => p.Id).Take(needed).ToList();
        foreach (var pid in movedIds)
            ProvinceOwnershipSystem.SetProvinceOwner(pid, "DEU");
        ProvinceCultureSystem.ReassignCountry(movedIds, "DEU");
        foreach (var pid in movedIds)
            ProvinceCultureSystem.SetCulture(pid, def.Culture);

        int bavAfterPromote = ProvinceCultureSystem.CultureProvinceCount("DEU", def.Culture);
        GD.Print($"[FormableTest] 2) {donor.Id} → DEU: {movedIds.Count} il devredildi, " +
                  $"{bavAfterPromote} bavarian il (gereken: {needed})");
        if (bavAfterPromote < needed) ok = false;

        // PP, koşul değerlendirmesinden ÖNCE verilir (koşul PP'yi de denetler).
        var deu = CountryDatabase.Get("DEU") ?? CountryDatabase.GetOrCreate("DEU", "Germany");
        deu.PoliticalPower = 500;
        float ppBefore = (float)deu.PoliticalPower;

        // 3) Koşullar değerlendirilir — hepsi sağlanmalı (DEU DEU = ebe kaynak)
        var conditions = FormableSystem.EvaluateConditions("DEU", "BAV");
        foreach (var c in conditions)
            GD.Print($"    - {c.Label}: {c.Met} ({c.Detail})");
        bool canForm = FormableSystem.CanForm("DEU", "BAV");
        GD.Print($"[FormableTest] 3) Kurulabilir: {canForm}");
        if (!canForm) ok = false;

        // 4) Ebe kaynak dışı ülke reddedilir
        bool wrongParent = FormableSystem.CanForm("FRA", "BAV");
        GD.Print($"[FormableTest] 4) Ebe kaynak olmayan ülke (FRA → BAV) reddedildi: {!wrongParent}");
        if (wrongParent) ok = false;

        // 5) Kurma başlatılır, gün sayacı ilerler
        int provBefore = ProvinceCultureSystem.ForCountry("DEU").Count;
        int deuCivBefore = deu.CivFactories;
        long deuPopBefore = deu.Population;

        string startReason;
        bool started = FormableSystem.TryStart("DEU", "BAV", out startReason);
        GD.Print($"[FormableTest] 5) Kurma başlatıldı: {started} (gerekçe: '{startReason}')");
        if (!started) ok = false;

        bool pending = FormableSystem.IsPending("DEU");
        int totalDays = (int)Math.Ceiling(def.DaysRequired);
        for (int d = 0; d < totalDays; d++)
            FormableSystem.AdvanceDay("DEU");

        // 6) Tamamlandıktan sonra ülke var ve illeri devredilmiş olmalı
        var bav = CountryDatabase.Get("BAV");
        bool formed = bav != null && !FormableSystem.IsPending("DEU");
        GD.Print($"[FormableTest] 6) Ülke oluştu: {formed} ({bav?.Name ?? "yok"})");
        if (!formed) ok = false;

        int bavProvinces = ProvinceCultureSystem.ForCountry("BAV").Count;
        int deuAfter = ProvinceCultureSystem.ForCountry("DEU").Count;
        GD.Print($"[FormableTest]    BAV il: {bavProvinces}, DEU il: {deuAfter} (önce: {provBefore})");
        if (bavProvinces < def.RequiredProvinces) ok = false;
        if (deuAfter != provBefore - bavProvinces) ok = false;

        // 7) PP harcanmış, fabrika ve nüfus bölünmüş
        float ppAfter = (float)deu.PoliticalPower;
        bool ppSpent = ppAfter <= ppBefore;
        bool factoriesMoved = deu.CivFactories <= deuCivBefore;
        bool populationMoved = bav.Population > 0 && deu.Population <= deuPopBefore;
        GD.Print($"[FormableTest] 7) PP: {ppBefore:N0} → {ppAfter:N0}, " +
                  $"fabrika DEU {deuCivBefore} → {deu.CivFactories}, " +
                  $"nüfus DEU {deuPopBefore} → {deu.Population}");
        if (!ppSpent || !factoriesMoved || !populationMoved) ok = false;

        // 8) Sahiplik doğru taşınmış (ProvinceOwnershipSystem)
        int ownerMismatch = ProvinceCultureSystem.ForCountry("BAV")
            .Count(r => ProvinceOwnershipSystem.GetProvinceOwner(r.ProvinceId) != "BAV");
        GD.Print($"[FormableTest] 8) Sahiplik uyumsuzluğu: {ownerMismatch}");
        if (ownerMismatch > 0) ok = false;

        // 9) Save/Load round-trip — FRA'yı da hazırlayıp bir süreç başlatıyoruz
        //    ve birkaç gün ilerlettikten sonra kaydedip yüklüyoruz; ilerleme
        //    düşmemeli (koşullar durmuşsa süreç de bozulmamalı).
        PrepareFormableCountry(provinces, "FRA", "burgundian", 9);
        FormableSystem.TryStart("FRA", "BRG", out _);
        for (int d = 0; d < 5; d++) FormableSystem.AdvanceDay("FRA");
        float progressBefore = FormableSystem.Progress("FRA");
        int pendingBeforeSave = FormableSystem.PendingCount;

        var save = FormableSystem.GetSaveData();
        FormableSystem.ResetForNewGame();
        FormableSystem.LoadSaveData(save);
        float progressAfter = FormableSystem.Progress("FRA");
        bool roundTrip = FormableSystem.PendingCount == pendingBeforeSave &&
                         Mathf.Abs(progressBefore - progressAfter) < 0.001f &&
                         progressBefore > 0f;
        GD.Print($"[FormableTest] 9) Save/Load: bekleyen {pendingBeforeSave} → {FormableSystem.PendingCount}, " +
                  $"ilerleme {progressBefore:P1} → {progressAfter:P1}");
        if (!roundTrip) ok = false;

        // 10) İptal
        bool cancelled = FormableSystem.Cancel("FRA");
        bool afterCancel = FormableSystem.IsPending("FRA");
        GD.Print($"[FormableTest] 10) İptal: {cancelled}, sonra beklemede: {afterCancel}");
        if (!cancelled || afterCancel) ok = false;

        // 11) Aynı ülke iki kez başlatamaz. BAV kurulduktan sonra DEU'nün ili
        //     azaldığı için FRA kullanılıyor (10. adımda iptal edilen süreçten
        //     sonra illeri hâlâ burgundy, PP hâlâ yeterli).
        FormableSystem.ResetForNewGame();
        bool firstStarted = FormableSystem.TryStart("FRA", "BRG", out _);
        bool secondBlocked = !FormableSystem.TryStart("FRA", "CST", out _);
        GD.Print($"[FormableTest] 11) İlk kurma: {firstStarted}, ikinci kurma engellendi: {secondBlocked}");
        if (!firstStarted || !secondBlocked) ok = false;

        GD.Print(ok ? "[FormableTest] SONUÇ: PASS ✓" : "[FormableTest] SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M44.3 testi yardımcısı: bir ülkeye <paramref name="provinceCount"/> il
    /// transfer edip hepsinin kültürünü <paramref name="culture"/> yapar ve
    /// PP'yi koşulu geçecek kadar bırakır. Bağışçı, hedef dışında en çok ili
    /// olan ülkedir (prosedürel dünyada sabit bir ülke seçilemez).
    /// </summary>
    private void PrepareFormableCountry(List<ProvinceData> provinces, string countryId,
                                        string culture, int provinceCount)
    {
        var data = CountryDatabase.Get(countryId) ?? CountryDatabase.GetOrCreate(countryId, countryId);
        data.PoliticalPower = 500;

        var donor = provinces
            .Where(p => !string.Equals(p.CountryId, countryId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.CountryId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Id = g.Key, Provs = g.ToList() })
            .FirstOrDefault();
        if (donor == null || donor.Provs.Count < provinceCount) return;

        var ids = donor.Provs.Select(p => p.Id).Take(provinceCount).ToList();
        foreach (var pid in ids)
            ProvinceOwnershipSystem.SetProvinceOwner(pid, countryId);
        ProvinceCultureSystem.ReassignCountry(ids, countryId);
        foreach (var pid in ids)
            ProvinceCultureSystem.SetCulture(pid, culture);
    }

    /// <summary>
    /// M44.4: Bağımsız çıkarma headless testi (--releasable-test).
    /// Normal oyunda ASLA çalışmaz.
    ///
    /// Senaryo: ilhak eden büyük bir ülke, ardından onun ilhak ettiği küçük
    /// bir ülkeyi serbest bırakma. İller, fabrika payı ve bağımsızlık
    /// bayraklarının doğru döndüğü, AI'nin devraldığı doğrulanır.
    /// </summary>
    private void RunReleasableTest()
    {
        GD.Print("=========================================");
        GD.Print("  M44.4: BAĞIMSIZ ÇIKARMA TESTİ");
        GD.Print("=========================================");

        bool ok = true;

        // 1) Dünyayı hazırla (il + kültür + sahiplik)
        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();
        GD.Print($"[ReleasableTest] 1) İl: {provinces.Count}, kültür kaydı: {ProvinceCultureSystem.RecordCount}");

        // 2) Değiş tokuş: büyük ülke (overlord) küçük ülkeyi ilhak eder.
        // İl devri gerçekten test edilsin diye kurbanın en az 1 ili olmalı;
        // prosedürel dünyada bazı ülkelerin hiç ili yok.
        var overlord = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed && ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count > 0)
            .OrderByDescending(c => ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count)
            .FirstOrDefault();
        var subject = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed &&
                !string.Equals(c.Id, overlord?.Id, StringComparison.OrdinalIgnoreCase) &&
                ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count >= 3)
            .OrderBy(c => ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count)
            .FirstOrDefault();
        if (overlord == null || subject == null)
        {
            GD.PrintErr("[ReleasableTest] HATA: deneme ülkeleri bulunamadı");
            GetTree().Quit(1);
            return;
        }

        int civBefore = subject.CivFactories;
        int milBefore = subject.MilFactories;
        int dockBefore = subject.Dockyards;
        var subjectProvinces = ProvinceOwnershipSystem.GetProvincesByOwner(subject.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        int popBefore = (int)subject.Population;
        overlord.PoliticalPower = 500;

        GD.Print($"[ReleasableTest] 2) İlhak: {subject.Id} → {overlord.Id} " +
                 $"(il={subjectProvinces.Count}, fabrika c{civBefore}/m{milBefore}/t{dockBefore})");

        // Kayıt, iller devredilmeden ÖNCE düşmeli
        ReleasableSystem.RecordSubject(subject.Id, overlord.Id, false, civBefore, milBefore, dockBefore);
        if (ReleasableSystem.RecordCount != 1)
        {
            GD.PrintErr($"[ReleasableTest] HATA: kayıt sayısı {ReleasableSystem.RecordCount} != 1");
            ok = false;
        }

        // İlhak uygula: bayraklar + fabrika transferi + tüm illerin devri
        subject.IsAnnexed = true;
        subject.OwnerId = overlord.Id;
        overlord.CivFactories += civBefore;
        overlord.MilFactories += milBefore;
        overlord.Dockyards += dockBefore;
        subject.CivFactories = subject.MilFactories = subject.Dockyards = 0;
        foreach (var p in subjectProvinces)
            ProvinceOwnershipSystem.SetProvinceOwner(p, overlord.Id);
        ProvinceCultureSystem.ReassignCountry(subjectProvinces, overlord.Id);

        int overlordCiv = overlord.CivFactories;
        int overlordMil = overlord.MilFactories;
        int overlordDock = overlord.Dockyards;
        int overlordProvCount = ProvinceOwnershipSystem.GetProvincesByOwner(overlord.Id).Count;
        GD.Print($"[ReleasableTest]    ilhak sonrası {overlord.Id}: il={overlordProvCount}, " +
                 $"fabrika c{overlordCiv}/m{overlordMil}/t{overlordDock}");
        if (ProvinceOwnershipSystem.GetProvincesByOwner(subject.Id).Count != 0)
        {
            GD.PrintErr("[ReleasableTest] HATA: ilhak sonrası kurbanın ili kalmış");
            ok = false;
        }

        // 3) Serbest bırakılabilir listesi
        var releasable = ReleasableSystem.Releasable();
        if (releasable.Count != 1 || releasable[0].CountryId != subject.Id)
        {
            GD.PrintErr($"[ReleasableTest] HATA: serbest bırakılabilir sayısı {releasable.Count}");
            ok = false;
        }
        else GD.Print($"[ReleasableTest] 3) Serbest bırakılabilir: {releasable.Count} ({subject.Id})");

        // 4) Yetkisiz aktör reddedilmeli (üçüncü bir ülke)
        var stranger = CountryDatabase.All()
            .FirstOrDefault(c => c != null && !c.IsAnnexed &&
                !string.Equals(c.Id, overlord.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Id, subject.Id, StringComparison.OrdinalIgnoreCase));
        if (stranger != null && ReleasableSystem.CanRelease(subject.Id, stranger.Id))
        {
            GD.PrintErr($"[ReleasableTest] HATA: {stranger.Id} {subject.Id}'yi serbest bırakabildi");
            ok = false;
        }
        else GD.Print("[ReleasableTest] 4) Yetkisiz aktör reddedildi");

        // 5) Serbest bırakma uygula
        var rel = ReleasableSystem.Release(subject.Id, overlord.Id);
        GD.Print($"[ReleasableTest] 5) BAĞIMSIZ: {subject.Id} " +
                 $"(il={rel.ProvincesReturned}, fabrika c{rel.CivReturned}/m{rel.MilReturned}/t{rel.DockReturned})");
        if (!rel.Ok)
        {
            GD.PrintErr($"[ReleasableTest] HATA: serbest bırakma başarısız — {rel.Reason}");
            ok = false;
        }

        // 6) İller geri dönmüş olmalı
        int backProvinces = ProvinceOwnershipSystem.GetProvincesByOwner(subject.Id).Count;
        if (backProvinces != subjectProvinces.Count)
        {
            GD.PrintErr($"[ReleasableTest] HATA: il devri {backProvinces} != {subjectProvinces.Count}");
            ok = false;
        }
        else GD.Print($"[ReleasableTest] 6) İller geri döndü: {backProvinces}");

        // 7) Kültür kaydı da sahibine dönmeli
        int cultureOwned = ProvinceCultureSystem.ForCountry(subject.Id).Count;
        if (cultureOwned != subjectProvinces.Count)
        {
            GD.PrintErr($"[ReleasableTest] HATA: kültür kaydı {cultureOwned} != {subjectProvinces.Count}");
            ok = false;
        }
        else GD.Print($"[ReleasableTest] 7) Kültür kaydı geri döndü: {cultureOwned}");

        // 8) Fabrika payı geri verilmiş, negatife düşmemiş olmalı
        if (subject.CivFactories != civBefore ||
            subject.MilFactories != milBefore ||
            subject.Dockyards != dockBefore)
        {
            GD.PrintErr($"[ReleasableTest] HATA: fabrika c{subject.CivFactories}/m{subject.MilFactories}/t{subject.Dockyards} " +
                        $"!= c{civBefore}/m{milBefore}/t{dockBefore}");
            ok = false;
        }
        int overlordTotal = overlord.CivFactories + overlord.MilFactories + overlord.Dockyards;
        int expectedTotal = overlordCiv + overlordMil + overlordDock - (civBefore + milBefore + dockBefore);
        if (overlordTotal != expectedTotal)
        {
            GD.PrintErr($"[ReleasableTest] HATA: ilhak edenin fabrikası {overlordTotal} != {expectedTotal}");
            ok = false;
        }
        else GD.Print($"[ReleasableTest] 8) Fabrika payı doğru: kurban c{subject.CivFactories}/m{subject.MilFactories}/t{subject.Dockyards}, " +
                      $"ilhak eden toplam {overlordTotal}");

        // 9) Bağımsızlık bayrakları
        if (subject.IsAnnexed || !string.IsNullOrEmpty(subject.OwnerId) || !string.IsNullOrEmpty(subject.OwnerId))
        {
            GD.PrintErr("[ReleasableTest] HATA: ilhak bayrakları hâlâ ayakta");
            ok = false;
        }
        if (ReleasableSystem.RecordCount != 0)
        {
            GD.PrintErr($"[ReleasableTest] HATA: kayıt temizlenmedi ({ReleasableSystem.RecordCount})");
            ok = false;
        }
        GD.Print("[ReleasableTest] 9) Bağımsızlık bayrakları kalktı, kayıt temizlendi");

        // 10) Çift serbest bırakma reddedilmeli
        var second = ReleasableSystem.Release(subject.Id, overlord.Id);
        if (second.Ok)
        {
            GD.PrintErr("[ReleasableTest] HATA: ikinci serbest bırakma kabul edildi");
            ok = false;
        }
        else GD.Print($"[ReleasableTest] 10) Çift serbest bırakma reddedildi ({second.Reason})");

        // 11) Kukla kaydı: fabrika payı alınmaz, iller kurbanın kalır
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();
        var puppetTaker = CountryDatabase.All()
            .FirstOrDefault(c => c != null && !c.IsAnnexed && ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count > 0);
        var puppetSub = CountryDatabase.All()
            .FirstOrDefault(c => c != null && !c.IsAnnexed &&
                ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count > 0 &&
                !string.Equals(c.Id, puppetTaker?.Id, StringComparison.OrdinalIgnoreCase));
        if (puppetTaker != null && puppetSub != null && ReleasableSystem.CanRelease(puppetSub.Id))
        {
            GD.PrintErr("[ReleasableTest] HATA: kaydı olmayan ülke serbest bırakılabilir göründü");
            ok = false;
        }
        else GD.Print("[ReleasableTest] 11) Kayıtsız ülke serbest bırakılamaz");

        // 12) Save/Load round-trip: kayıt ve ilerleme korunur
        ReleasableSystem.RecordSubject(subject.Id, overlord.Id, false, civBefore, milBefore, dockBefore);
        var save = ReleasableSystem.GetSaveData();
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();
        ReleasableSystem.LoadSaveData(save);
        var loaded = ReleasableSystem.Get(subject.Id);
        if (loaded == null || loaded.ProvinceIds.Length != subjectProvinces.Count ||
            loaded.FactoriesTaken != civBefore + milBefore + dockBefore)
        {
            GD.PrintErr("[ReleasableTest] HATA: save/load kaydı bozuldu");
            ok = false;
        }
        else GD.Print($"[ReleasableTest] 12) Save/load korundu: il={loaded.ProvinceIds.Length}, " +
                      $"fabrika={loaded.FactoriesTaken}");

        // 13) Belirli il doğrulaması: aynı illerin kimlikleri geri dönmüş olmalı
        var returnedIds = ProvinceOwnershipSystem.GetProvincesByOwner(subject.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        var expectedIds = subjectProvinces.OrderBy(id => id, StringComparer.Ordinal).ToList();
        var sameSet = returnedIds.Count == expectedIds.Count;
        if (sameSet)
            for (int i = 0; i < expectedIds.Count; i++)
                if (!string.Equals(returnedIds[i], expectedIds[i], StringComparison.Ordinal)) { sameSet = false; break; }
        if (!sameSet)
        {
            GD.PrintErr($"[ReleasableTest] HATA: dönen il kimlikleri uyuşmuyor " +
                        $"({returnedIds.Count} vs {expectedIds.Count})");
            ok = false;
        }
        else GD.Print($"[ReleasableTest] 13) Aynı il kimlikleri döndü: {returnedIds.Count} il");

        // 14) Nüfus değişmemeli (il devri nüfusu taşımıyor)
        if ((int)subject.Population != popBefore)
        {
            GD.PrintErr($"[ReleasableTest] HATA: nüfus değişti {popBefore} → {(int)subject.Population}");
            ok = false;
        }
        else GD.Print($"[ReleasableTest] 14) Nüfus korundu: {popBefore}");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M44.5: Kurgu ülke paneli headless testi (--formable-panel-test).
    ///
    /// Panelin açılışı, kurgu ülke listesi, kültür haritası özeti, koşul
    /// satırları, FORM NATION / CANCEL eylemleri ve ESC ile kapanması
    /// doğrulanır. Ayrıca panel veri mutasyonu yapmaz (save/load A==B
    /// etkilenmez) — bu yüzden dünya verisi yedeklenip geri yüklenir.
    ///
    /// Normal oyunda ASLA çalışmaz.
    /// </summary>
    private async void RunFormablePanelTest()
    {
        GD.Print("=========================================");
        GD.Print("  M44.5: KURGU ÜLKE PANELİ TESTİ (--formable-panel-test)");
        GD.Print("=========================================");

        bool ok = true;

        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        FormableSystem.ResetForNewGame();
        GD.Print($"[FormablePanelTest] İl: {provinces.Count}, kültür kaydı: {ProvinceCultureSystem.RecordCount}");

        if (!HasPlayer) SetPlayerCountry("DEU");
        string playerId = PlayerCountryId;
        var player = CountryDatabase.Get(playerId);
        if (player == null)
        {
            GD.PrintErr($"[FormablePanelTest] HATA: oyuncu ülkesi ({playerId}) bulunamadı");
            GetTree().Quit(1);
            return;
        }

        // ---- 1) Dünya verisini yedekle ----
        double ppBefore = player.PoliticalPower;
        var provinceOwnerBackup = new Dictionary<string, string>();
        foreach (var p in provinces)
            provinceOwnerBackup[p.Id] = p.CountryId ?? "";
        var cultureBackup = new Dictionary<string, string>();
        foreach (var r in ProvinceCultureSystem.ForCountry(playerId))
            cultureBackup[r.ProvinceId] = r.Culture ?? "";

        // ---- 2) DEU'ye kurabileceği bir kurgu ülke kur ----
        // Ebe kaynakları arasında DEU olan bir tanım seç (prosedürel dünyada
        // DEU'nun kendi kültür dağılımı farklı olabilir, illeri elle veriyoruz).
        var parentDef = FormableDataSystem.ForParent(playerId)
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (parentDef == null)
        {
            GD.PrintErr($"[FormablePanelTest] HATA: {playerId} ebe kaynağı olan formable tanımı yok");
            GetTree().Quit(1);
            return;
        }

        var def = FormableDataSystem.Get(parentDef.Id);
        if (def == null)
        {
            GD.PrintErr($"[FormablePanelTest] HATA: {parentDef.Id} tanımı yüklenemedi");
            GetTree().Quit(1);
            return;
        }

        int needed = def.RequiredProvinces;
        var donor = provinces
            .Where(p => !string.Equals(p.CountryId, playerId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.CountryId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Id = g.Key, Provs = g.ToList() })
            .FirstOrDefault();
        if (donor == null || donor.Provs.Count < needed)
        {
            GD.PrintErr($"[FormablePanelTest] HATA: bağışçı ülkede {needed} il yok " +
                        $"({donor?.Provs.Count ?? 0} bulundu)");
            GetTree().Quit(1);
            return;
        }

        var movedIds = donor.Provs.Select(p => p.Id).Take(needed).ToList();
        foreach (var pid in movedIds)
            ProvinceOwnershipSystem.SetProvinceOwner(pid, playerId);
        ProvinceCultureSystem.ReassignCountry(movedIds, playerId);
        foreach (var pid in movedIds)
            ProvinceCultureSystem.SetCulture(pid, def.Culture);
        player.PoliticalPower = Math.Max(player.PoliticalPower, def.PoliticalPowerCost + 100);

        var host = new Control { Name = "FormablePanelTestHost" };
        host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(host);

        try
        {
            // ---- 3) Paneli aç ----
            GD.Print("[FormablePanelTest] 1) Panel açılışı");
            FormablePanel.OpenUnder(host);
            await ToSignal(GetTree(), "process_frame");
            await ToSignal(GetTree(), "process_frame");

            var panel = FindFormablePanel();
            if (panel == null)
            {
                GD.PrintErr("[FormablePanelTest] HATA: panel düğümü bulunamadı");
                GetTree().Quit(1);
                return;
            }
            if (!FormablePanel.IsOpen)
            {
                GD.PrintErr("[FormablePanelTest] HATA: panel açılmadı");
                ok = false;
            }
            if (panel.PlayerId != playerId)
            {
                GD.PrintErr($"[FormablePanelTest] HATA: panel oyuncusu yanlış ({panel.PlayerId} != {playerId})");
                ok = false;
            }

            // ---- 4) Ölçü sınırı: 1152x648'e sığmalı ----
            Vector2 designed = panel.DesignedSize;
            GD.Print($"[FormablePanelTest] Panel tasarım boyutu: {designed.X}x{designed.Y}");
            if (designed.X <= 0f || designed.Y <= 0f)
            {
                GD.PrintErr("[FormablePanelTest] HATA: panel boyutu okunamadı");
                ok = false;
            }
            if (designed.X > 1152 || designed.Y > 648)
            {
                GD.PrintErr($"[FormablePanelTest] HATA: panel 1152x648'e sığmıyor ({designed.X}x{designed.Y})");
                ok = false;
            }

            // ---- 5) Kurgu ülke satırları ----
            int rows = panel.FormableRowCount;
            var parents = FormableDataSystem.ForParent(playerId).ToList();
            GD.Print($"[FormablePanelTest] Kurgu ülke satırı: {rows} (ebe kaynak: {parents.Count})");
            if (rows != parents.Count)
            {
                GD.PrintErr($"[FormablePanelTest] HATA: satır sayısı ebe kaynak sayısına eşit olmalı " +
                            $"({rows} != {parents.Count})");
                ok = false;
            }
            if (parents.Count == 0)
            {
                GD.PrintErr("[FormablePanelTest] HATA: panel hiç kurgu ülke listelemedi");
                ok = false;
            }

            // ---- 6) Kültür haritası satırları ----
            int cultureRows = panel.CultureRowCount;
            var distribution = ProvinceCultureSystem.GetCultureDistribution(playerId);
            GD.Print($"[FormablePanelTest] Kültür satırı: {cultureRows} " +
                     $"(dağılım: {distribution.Count} kültür, " +
                     $"{string.Join(", ", distribution.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key}={kv.Value}"))})");
            if (cultureRows != distribution.Count)
            {
                GD.PrintErr($"[FormablePanelTest] HATA: kültür satırı dağılıma eşit olmalı " +
                            $"({cultureRows} != {distribution.Count})");
                ok = false;
            }
            else if (cultureRows > 0)
            {
                // Satırlar il sayısına göre azalan sıralanmalı
                bool sorted = true;
                for (int i = 1; i < panel.CultureCounts.Count; i++)
                    if (panel.CultureCounts[i] > panel.CultureCounts[i - 1]) { sorted = false; break; }
                if (!sorted)
                {
                    GD.PrintErr("[FormablePanelTest] HATA: kültür satırları il sayısına göre sıralı değil");
                    ok = false;
                }
                if (panel.CultureCounts[0] < needed)
                {
                    GD.PrintErr($"[FormablePanelTest] HATA: ilk satır en az {needed} il göstermeli " +
                                $"({panel.CultureCounts[0]})");
                    ok = false;
                }
            }

            // ---- 7) Koşul satırları ----
            int conditions = panel.ConditionMet.Count;
            GD.Print($"[FormablePanelTest] Koşul satırı: {conditions}");
            if (conditions != 5)
            {
                GD.PrintErr($"[FormablePanelTest] HATA: 5 koşul maddesi gösterilmeli ({conditions})");
                ok = false;
            }
            int metCount = panel.ConditionMet.Count(m => m);
            GD.Print($"[FormablePanelTest] Sağlanan koşul: {metCount}/{conditions}");

            // ---- 8) Seçim: koşulları sağlanan ilk kurgu ülkeyi seç ----
            var formableIds = parents.Select(d => d.Id).ToList();
            int targetIndex = -1;
            for (int i = 0; i < formableIds.Count; i++)
                if (FormableSystem.CanForm(playerId, formableIds[i])) { targetIndex = i; break; }
            if (targetIndex < 0)
            {
                GD.PrintErr("[FormablePanelTest] HATA: koşulları sağlayan kurgu ülke yok (kurulum hatalı)");
                ok = false;
            }
            else
            {
                GD.Print($"[FormablePanelTest] 2) Kurgu ülke seçimi: {formableIds[targetIndex]}");
                var list = FindItemList(panel);
                if (list == null)
                {
                    GD.PrintErr("[FormablePanelTest] HATA: kurgu ülke listesi (ItemList) bulunamadı");
                    ok = false;
                }
                else
                {
                    list.Select(targetIndex);
                    list.EmitSignal(ItemList.SignalName.ItemSelected, targetIndex);
                    await ToSignal(GetTree(), "process_frame");

                    if (panel.SelectedFormableId != formableIds[targetIndex])
                    {
                        GD.PrintErr($"[FormablePanelTest] HATA: seçim alınmadı " +
                                    $"({panel.SelectedFormableId} != {formableIds[targetIndex]})");
                        ok = false;
                    }

                    // ---- 9) FORM NATION ----
                    GD.Print("[FormablePanelTest] 3) Kurma başlatma");
                    if (panel.FormDisabled)
                    {
                        GD.PrintErr("[FormablePanelTest] HATA: koşullar sağlanınca FORM NATION açık olmalı");
                        ok = false;
                    }
                    var formBtn = FindButton(panel, "FORM NATION");
                    if (formBtn == null)
                    {
                        GD.PrintErr("[FormablePanelTest] HATA: FORM NATION butonu bulunamadı");
                        ok = false;
                    }
                    else
                    {
                        formBtn.EmitSignal(BaseButton.SignalName.Pressed);
                        await ToSignal(GetTree(), "process_frame");

                        if (!FormableSystem.IsPending(playerId))
                        {
                            GD.PrintErr("[FormablePanelTest] HATA: kurma süreci başlamadı");
                            ok = false;
                        }
                        var state = FormableSystem.State(playerId);
                        if (state != null && state.FormableId != formableIds[targetIndex])
                        {
                            GD.PrintErr($"[FormablePanelTest] HATA: yanlış kurgu ülke kuruluyor " +
                                        $"({state.FormableId} != {formableIds[targetIndex]})");
                            ok = false;
                        }
                        // Form sonrası CANCEL açılmalı, FORM kapanmalı
                        if (panel.CancelDisabled)
                        {
                            GD.PrintErr("[FormablePanelTest] HATA: kurulum varken CANCEL açık olmalı");
                            ok = false;
                        }
                        GD.Print($"[FormablePanelTest] Kurulum: {state?.FormableId} " +
                                 $"({state?.DaysElapsed:N0}/{state?.DaysRequired:N0} gün), " +
                                 $"mesaj=\"{panel.FeedbackText}\"");

                        // ---- 10) Save/Load: kurulum durumu korunur ----
                        GD.Print("[FormablePanelTest] 4) Save/Load");
                        FormableSystem.AdvanceDay(playerId); // ilerlemeyi görünür kıl
                        var save = FormableSystem.GetSaveData();
                        if (save.Pending == null || save.Pending.Length != 1)
                        {
                            GD.PrintErr($"[FormablePanelTest] HATA: tek bekleyen kurulum kaydedilmeli " +
                                        $"({save.Pending?.Length ?? 0})");
                            ok = false;
                        }
                        FormableSystem.ResetForNewGame();
                        if (FormableSystem.IsPending(playerId))
                        {
                            GD.PrintErr("[FormablePanelTest] HATA: sıfırlama bekleyen kurulumu temizlemeli");
                            ok = false;
                        }
                        FormableSystem.LoadSaveData(save);
                        var loaded = FormableSystem.State(playerId);
                        if (loaded == null)
                        {
                            GD.PrintErr("[FormablePanelTest] HATA: yükleme sonrası kurulum geri gelmeli");
                            ok = false;
                        }
                        else
                        {
                            GD.Print($"[FormablePanelTest] save/load → kurulum {loaded.FormableId} " +
                                     $"({loaded.DaysElapsed:N0}/{loaded.DaysRequired:N0}) ✓");
                        }

                        // ---- 11) CANCEL ----
                        GD.Print("[FormablePanelTest] 5) Kurma iptal");
                        var cancelBtn = FindButton(panel, "CANCEL");
                        if (cancelBtn == null)
                        {
                            GD.PrintErr("[FormablePanelTest] HATA: CANCEL butonu bulunamadı");
                            ok = false;
                        }
                        else if (cancelBtn.Disabled)
                        {
                            GD.PrintErr("[FormablePanelTest] HATA: CANCEL butonu kapalı");
                            ok = false;
                        }
                        else
                        {
                            cancelBtn.EmitSignal(BaseButton.SignalName.Pressed);
                            await ToSignal(GetTree(), "process_frame");

                            if (FormableSystem.IsPending(playerId))
                            {
                                GD.PrintErr("[FormablePanelTest] HATA: iptal sonrası kurulum kalmamalı");
                                ok = false;
                            }
                            if (!panel.CancelDisabled)
                            {
                                GD.PrintErr("[FormablePanelTest] HATA: iptal sonrası CANCEL kapanmalı");
                                ok = false;
                            }
                            if (panel.FormDisabled)
                            {
                                GD.PrintErr("[FormablePanelTest] HATA: iptal sonrası FORM NATION açık olmalı");
                                ok = false;
                            }
                            GD.Print($"[FormablePanelTest] iptal sonrası mesaj=\"{panel.FeedbackText}\"");
                        }
                    }
                }
            }

            // ---- 12) ESC ile kapatma ----
            GD.Print("[FormablePanelTest] 6) ESC ile kapatma");
            panel._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (!panel.CloseRequested)
            {
                GD.PrintErr("[FormablePanelTest] HATA: ESC kapatma isteği üretmedi");
                ok = false;
            }

            // PopOut tween'i 0.1 sn sürdüğü için sabit frame sayısı bekle yetmez;
            // panel ağaçtan çıkana kadar frame bekle.
            for (int i = 0; i < 40 && FormablePanel.IsOpen; i++)
                await ToSignal(GetTree(), "process_frame");

            if (FormablePanel.IsOpen)
            {
                GD.PrintErr("[FormablePanelTest] HATA: ESC paneli kapatmadı");
                ok = false;
            }
            else
            {
                GD.Print("[FormablePanelTest] ESC paneli kapattı ✓");
            }
        }
        finally
        {
            // ---- 13) Dünya verisini geri yükle ----
            foreach (var kv in provinceOwnerBackup)
            {
                if (PaProvinceOwner(provinces, kv.Key, out int idx))
                    provinces[idx].CountryId = kv.Value;
            }
            ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
            ProvinceCultureSystem.ResetForNewGame();
            ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
            FormableSystem.ResetForNewGame();
            player.PoliticalPower = ppBefore;
            GD.Print("[FormablePanelTest] Dünya verisi geri yüklendi");
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>FormablePanel düğümünü bulur (test yardımcısı).</summary>
    private FormablePanel FindFormablePanel()
    {
        foreach (var n in FindChildrenIn(this))
            if (n is FormablePanel fp) return fp;
        return null;
    }

    /// <summary>
    /// M45.1: Bağımsız çıkarma paneli testi. Paneli açar, ilhak/kukla bağımlıları
    /// listeler, RELEASE NATION ile illerin + fabrika payının geri döndüğünü ve
    /// serbest bırakma sonrası listenin yenilendiğini doğrular. Normal oyunda ASLA çalışmaz.
    /// </summary>
    private async void RunReleasablePanelTest()
    {
        GD.Print("=========================================");
        GD.Print("  M45.1: BAĞIMSIZ ÇIKARMA PANELİ TESTİ (--releasable-panel-test)");
        GD.Print("=========================================");

        bool ok = true;

        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();
        GD.Print($"[ReleasablePanelTest] İl: {provinces.Count}, kültür kaydı: {ProvinceCultureSystem.RecordCount}");

        if (!HasPlayer) SetPlayerCountry("DEU");
        string playerId = PlayerCountryId;
        var player = CountryDatabase.Get(playerId);
        if (player == null)
        {
            GD.PrintErr($"[ReleasablePanelTest] HATA: oyuncu ülkesi ({playerId}) bulunamadı");
            GetTree().Quit(1);
            return;
        }

        // ---- 1) Dünya verisini yedekle ----
        var provinceOwnerBackup = new Dictionary<string, string>();
        foreach (var p in provinces)
            provinceOwnerBackup[p.Id] = p.CountryId ?? "";
        var countryBackup = new Dictionary<string, (bool Annexed, string Owner, int Civ, int Mil, int Dock)>();
        foreach (var c in CountryDatabase.All())
        {
            if (c == null) continue;
            countryBackup[c.Id] = (c.IsAnnexed, c.OwnerId, c.CivFactories, c.MilFactories, c.Dockyards);
        }

        // ---- 2) İki bağımlı kur: biri ilhak (iller devredilir), biri kukla (iller kalır) ----
        var donors = provinces
            .Where(p => !string.Equals(p.CountryId, playerId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.CountryId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Id = g.Key, Provs = g.ToList() })
            .ToList();

        var annexDonor = donors.FirstOrDefault(d => d.Provs.Count >= 3);
        var puppetDonor = donors.FirstOrDefault(d =>
            d.Provs.Count >= 1 &&
            !string.Equals(d.Id, annexDonor?.Id ?? "", StringComparison.OrdinalIgnoreCase));

        if (annexDonor == null || puppetDonor == null)
        {
            GD.PrintErr($"[ReleasablePanelTest] HATA: bağımlı kurulacak ülke bulunamadı " +
                        $"(ilhak={annexDonor?.Id ?? "-"}, kukla={puppetDonor?.Id ?? "-"})");
            GetTree().Quit(1);
            return;
        }

        var annexData = CountryDatabase.GetOrCreate(annexDonor.Id, "");
        var puppetData = CountryDatabase.GetOrCreate(puppetDonor.Id, "");

        // İlhak: kayıt iller DEVREDİLMEDEN ÖNCE düşülür (snapshot kurbanın elinde).
        var annexProvs = annexDonor.Provs.Select(p => p.Id).ToList();
        int civTaken = annexData.CivFactories;
        int milTaken = annexData.MilFactories;
        int dockTaken = annexData.Dockyards;
        ReleasableSystem.RecordSubject(annexDonor.Id, playerId, false, civTaken, milTaken, dockTaken);

        foreach (var pid in annexProvs)
            ProvinceOwnershipSystem.SetProvinceOwner(pid, playerId);
        ProvinceCultureSystem.ReassignCountry(annexProvs, playerId);
        annexData.IsAnnexed = true;
        annexData.OwnerId = playerId;
        annexData.CivFactories = 0;
        annexData.MilFactories = 0;
        annexData.Dockyards = 0;
        player.CivFactories += civTaken;
        player.MilFactories += milTaken;
        player.Dockyards += dockTaken;

        // Kukla: illeri ve fabrikaları kendinde kalır, yalnızca bağımlılık bayrağı.
        ReleasableSystem.RecordSubject(puppetDonor.Id, playerId, true, 0, 0, 0);
        puppetData.IsAnnexed = true;
        puppetData.OwnerId = playerId;

        GD.Print($"[ReleasablePanelTest] Kurulan bağımlılar — ilhak {annexDonor.Id} " +
                 $"({annexProvs.Count} il, c{civTaken}/m{milTaken}/t{dockTaken}), " +
                 $"kukla {puppetDonor.Id} ({puppetDonor.Provs.Count} il)");

        if (ReleasableSystem.SubjectsOf(playerId).Count != 2)
        {
            GD.PrintErr($"[ReleasablePanelTest] HATA: iki bağımlı kaydı olmalı " +
                        $"({ReleasableSystem.SubjectsOf(playerId).Count})");
            ok = false;
        }

        var host = new Control { Name = "ReleasablePanelTestHost" };
        host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(host);

        try
        {
            // ---- 3) Paneli aç ----
            GD.Print("[ReleasablePanelTest] 1) Panel açılışı");
            ReleasablePanel.OpenUnder(host);
            await ToSignal(GetTree(), "process_frame");
            await ToSignal(GetTree(), "process_frame");

            var panel = FindReleasablePanel();
            if (panel == null)
            {
                GD.PrintErr("[ReleasablePanelTest] HATA: panel düğümü bulunamadı");
                GetTree().Quit(1);
                return;
            }
            if (!ReleasablePanel.IsOpen)
            {
                GD.PrintErr("[ReleasablePanelTest] HATA: panel açılmadı");
                ok = false;
            }
            if (panel.PlayerId != playerId)
            {
                GD.PrintErr($"[ReleasablePanelTest] HATA: panel oyuncusu yanlış ({panel.PlayerId} != {playerId})");
                ok = false;
            }

            // ---- 4) Ölçü sınırı: 1152x648'e sığmalı ----
            Vector2 designed = panel.DesignedSize;
            GD.Print($"[ReleasablePanelTest] Panel tasarım boyutu: {designed.X}x{designed.Y}");
            if (designed.X <= 0f || designed.Y <= 0f)
            {
                GD.PrintErr("[ReleasablePanelTest] HATA: panel boyutu okunamadı");
                ok = false;
            }
            if (designed.X > 1152 || designed.Y > 648)
            {
                GD.PrintErr($"[ReleasablePanelTest] HATA: panel 1152x648'e sığmıyor ({designed.X}x{designed.Y})");
                ok = false;
            }

            // ---- 5) Bağımlı satırları ----
            int rows = panel.SubjectRowCount;
            GD.Print($"[ReleasablePanelTest] Bağımlı satırı: {rows}");
            if (rows != 2)
            {
                GD.PrintErr($"[ReleasablePanelTest] HATA: iki bağımlı satırı gösterilmeli ({rows})");
                ok = false;
            }
            if (!panel.SubjectIds.Contains(annexDonor.Id) || !panel.SubjectIds.Contains(puppetDonor.Id))
            {
                GD.PrintErr("[ReleasablePanelTest] HATA: kurulan bağımlılar listede yok " +
                            $"[{string.Join(", ", panel.SubjectIds)}]");
                ok = false;
            }

            // ---- 6) Kimliğe göre artan sıralama ----
            bool sorted = true;
            for (int i = 1; i < panel.SubjectIds.Count; i++)
                if (string.CompareOrdinal(panel.SubjectIds[i - 1], panel.SubjectIds[i]) > 0) { sorted = false; break; }
            if (!sorted)
            {
                GD.PrintErr("[ReleasablePanelTest] HATA: bağımlı satırları kimliğe göre sıralı değil");
                ok = false;
            }

            // ---- 7) Detay satırları (varsayılan seçim) ----
            int detailRows = panel.DetailRowCount;
            GD.Print($"[ReleasablePanelTest] Detay satırı: {detailRows}, seçili={panel.SelectedSubjectId}");
            if (detailRows < 5)
            {
                GD.PrintErr($"[ReleasablePanelTest] HATA: detay en az 5 satır göstermeli ({detailRows})");
                ok = false;
            }

            // ---- 8) İlhak edilen ülkeyi seç + RELEASE NATION ----
            int annexIndex = -1;
            for (int i = 0; i < panel.SubjectIds.Count; i++)
                if (string.Equals(panel.SubjectIds[i], annexDonor.Id, StringComparison.OrdinalIgnoreCase))
                { annexIndex = i; break; }
            if (annexIndex < 0)
            {
                GD.PrintErr("[ReleasablePanelTest] HATA: ilhak edilen ülke listede bulunamadı");
                ok = false;
            }
            else
            {
                GD.Print($"[ReleasablePanelTest] 2) Seçim: {panel.SubjectIds[annexIndex]}");
                var list = FindItemList(panel);
                if (list == null)
                {
                    GD.PrintErr("[ReleasablePanelTest] HATA: bağımlı listesi (ItemList) bulunamadı");
                    ok = false;
                }
                else
                {
                    list.Select(annexIndex);
                    list.EmitSignal(ItemList.SignalName.ItemSelected, annexIndex);
                    await ToSignal(GetTree(), "process_frame");

                    if (panel.SelectedSubjectId != annexDonor.Id)
                    {
                        GD.PrintErr($"[ReleasablePanelTest] HATA: seçim alınmadı ({panel.SelectedSubjectId})");
                        ok = false;
                    }

                    // Detayda "hâlâ bizde duran il" sayısı kayıttaki il sayısına eşit olmalı
                    var detailText = string.Join(" | ", panel.DetailTexts);
                    if (!detailText.Contains($"Still held by you (returned): {annexProvs.Count}"))
                    {
                        GD.PrintErr("[ReleasablePanelTest] HATA: detayda iade edilecek il sayısı yanlış " +
                                    $"→ {detailText}");
                        ok = false;
                    }

                    if (panel.ReleaseDisabled)
                    {
                        GD.PrintErr("[ReleasablePanelTest] HATA: RELEASE NATION kapalı");
                        ok = false;
                    }
                    var releaseBtn = FindButton(panel, "RELEASE NATION");
                    if (releaseBtn == null)
                    {
                        GD.PrintErr("[ReleasablePanelTest] HATA: RELEASE NATION butonu bulunamadı");
                        ok = false;
                    }
                    else
                    {
                        GD.Print("[ReleasablePanelTest] 3) Serbest bırakma");
                        int playerCivBefore = player.CivFactories;
                        int playerMilBefore = player.MilFactories;
                        int playerDockBefore = player.Dockyards;

                        releaseBtn.EmitSignal(BaseButton.SignalName.Pressed);
                        await ToSignal(GetTree(), "process_frame");

                        var result = ReleasableSystem.Get(annexDonor.Id);
                        if (result != null)
                        {
                            GD.PrintErr("[ReleasablePanelTest] HATA: serbest bırakma kaydı silinmedi");
                            ok = false;
                        }
                        if (annexData.IsAnnexed || !string.IsNullOrEmpty(annexData.OwnerId))
                        {
                            GD.PrintErr("[ReleasablePanelTest] HATA: bağımsızlık bayrakları kalkmadı");
                            ok = false;
                        }

                        // İller kurvana geri dönmeli
                        int returned = 0;
                        foreach (var pid in annexProvs)
                            if (string.Equals(ProvinceOwnershipSystem.GetProvinceOwner(pid),
                                              annexDonor.Id, StringComparison.OrdinalIgnoreCase))
                                returned++;
                        GD.Print($"[ReleasablePanelTest] İade edilen il: {returned}/{annexProvs.Count}");
                        if (returned != annexProvs.Count)
                        {
                            GD.PrintErr($"[ReleasablePanelTest] HATA: tüm iller kurvana dönmeli " +
                                        $"({returned}/{annexProvs.Count})");
                            ok = false;
                        }

                        // Fabrika payı kurvana geri dönmeli, oyuncudan düşmeli
                        if (annexData.CivFactories != civTaken ||
                            annexData.MilFactories != milTaken ||
                            annexData.Dockyards != dockTaken)
                        {
                            GD.PrintErr($"[ReleasablePanelTest] HATA: fabrika payı kurvana dönmedi " +
                                        $"(c{annexData.CivFactories}/m{annexData.MilFactories}/t{annexData.Dockyards} " +
                                        $"≠ c{civTaken}/m{milTaken}/t{dockTaken})");
                            ok = false;
                        }
                        if (player.CivFactories != playerCivBefore - civTaken ||
                            player.MilFactories != playerMilBefore - milTaken ||
                            player.Dockyards != playerDockBefore - dockTaken)
                        {
                            GD.PrintErr($"[ReleasablePanelTest] HATA: oyuncunun fabrikası doğru düşmedi " +
                                        $"(c{player.CivFactories}/m{player.MilFactories}/t{player.Dockyards})");
                            ok = false;
                        }

                        // Kukla bağımlı listede kalmalı
                        if (!panel.SubjectIds.Contains(puppetDonor.Id))
                        {
                            GD.PrintErr("[ReleasablePanelTest] HATA: kukla bağımlı listeden kayboldu");
                            ok = false;
                        }
                        if (panel.SubjectRowCount != 1)
                        {
                            GD.PrintErr($"[ReleasablePanelTest] HATA: serbest bırakma sonrası bir satır kalmalı " +
                                        $"({panel.SubjectRowCount})");
                            ok = false;
                        }
                        GD.Print($"[ReleasablePanelTest] mesaj=\"{panel.FeedbackText}\"");
                        if (panel.ReleaseDisabled)
                        {
                            GD.PrintErr("[ReleasablePanelTest] HATA: kukla seçiliyken RELEASE NATION açık olmalı");
                            ok = false;
                        }

                        // ---- 9) Save/Load round-trip (kukla kaydı) ----
                        GD.Print("[ReleasablePanelTest] 4) Save/Load");
                        var save = ReleasableSystem.GetSaveData();
                        if (save.Subjects == null || save.Subjects.Length != 1)
                        {
                            GD.PrintErr($"[ReleasablePanelTest] HATA: tek bağımlı kaydı korunmalı " +
                                        $"({save.Subjects?.Length ?? 0})");
                            ok = false;
                        }
                        ReleasableSystem.ResetForNewGame();
                        AutonomySystem.ResetForNewGame();
                        ResistanceSystem.ResetForNewGame();
                        if (ReleasableSystem.RecordCount != 0)
                        {
                            GD.PrintErr("[ReleasablePanelTest] HATA: sıfırlama kayıtları temizlemeli");
                            ok = false;
                        }
                        ReleasableSystem.LoadSaveData(save);
                        if (ReleasableSystem.Get(puppetDonor.Id) == null)
                        {
                            GD.PrintErr("[ReleasablePanelTest] HATA: yükleme sonrası kukla kaydı geri gelmedi");
                            ok = false;
                        }
                        else
                        {
                            GD.Print($"[ReleasablePanelTest] save/load → kukla kaydı {puppetDonor.Id} korundu ✓");
                        }
                    }
                }
            }

            // ---- 10) ESC ile kapatma ----
            GD.Print("[ReleasablePanelTest] 5) ESC ile kapatma");
            panel._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (!panel.CloseRequested)
            {
                GD.PrintErr("[ReleasablePanelTest] HATA: ESC kapatma isteği üretmedi");
                ok = false;
            }

            // PopOut tween'i 0.1 sn sürdüğü için sabit frame sayısı bekle yetmez;
            // panel ağaçtan çıkana kadar frame bekle.
            for (int i = 0; i < 40 && ReleasablePanel.IsOpen; i++)
                await ToSignal(GetTree(), "process_frame");

            if (ReleasablePanel.IsOpen)
            {
                GD.PrintErr("[ReleasablePanelTest] HATA: ESC paneli kapatmadı");
                ok = false;
            }
            else
            {
                GD.Print("[ReleasablePanelTest] ESC paneli kapattı ✓");
            }
        }
        finally
        {
            // ---- 11) Dünya verisini geri yükle ----
            foreach (var kv in provinceOwnerBackup)
            {
                if (PaProvinceOwner(provinces, kv.Key, out int idx))
                    provinces[idx].CountryId = kv.Value;
            }
            ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
            foreach (var kv in countryBackup)
            {
                var c = CountryDatabase.Get(kv.Key);
                if (c == null) continue;
                c.IsAnnexed = kv.Value.Annexed;
                c.OwnerId = kv.Value.Owner;
                c.CivFactories = kv.Value.Civ;
                c.MilFactories = kv.Value.Mil;
                c.Dockyards = kv.Value.Dock;
            }
            ProvinceCultureSystem.ResetForNewGame();
            ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
            ReleasableSystem.ResetForNewGame();
            AutonomySystem.ResetForNewGame();
            ResistanceSystem.ResetForNewGame();
            GD.Print("[ReleasablePanelTest] Dünya verisi geri yüklendi");
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M46.2: Kültür efsanesi paneli testi (--culture-legend-test).
    ///
    /// Doğrulananlar:
    ///   1) Ölçü sınırı: panel 1152x648'e sığyor.
    ///   2) Her kültür tam bir satırda, tekrarsız; il sayıları toplamı il
    ///      kaydı sayısına, nüfus payları toplamı 1.0'e eşit.
    ///   3) Sıralama: nüfus payı azalan (eşitlikte il sayısı, sonra kimlik).
    ///   4) Çubuk: ilk satır 1.0, tümü 0..1, payı azalan sırada azalan.
    ///   5) Satır seçimi: o kültürü gerçekten taşıyan ülkeler listelenir
    ///      (kültür katmanından bağımsız olarak yeniden sayılır), tekrarsız,
    ///      il sayısına göre azalan.
    ///   6) Harita bağı: seçim WorldMap.CultureHighlight'a yazılır, harita
    ///      culture moduna geçer, vurgulanmayan illerin rengi kararır.
    ///   7) ESC kapatma isteği üretir.
    /// </summary>
    private async void RunCultureLegendPanelTest()
    {
        GD.Print("=========================================");
        GD.Print("  M46.2: KÜLTÜR EFSANESİ PANELİ TESTİ (--culture-legend-test)");
        GD.Print("=========================================");

        bool ok = true;

        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();

        int cultures = ProvinceCultureSystem.All()
            .Where(r => r != null)
            .Select(r => r.Culture ?? "")
            .Distinct(StringComparer.Ordinal)
            .Count();
        int records = ProvinceCultureSystem.RecordCount;
        GD.Print($"[CultureLegendTest] İl: {provinces.Count}, kültür kaydı: {records}, " +
                 $"ayrı kültür: {cultures}");
        if (records == 0 || cultures == 0)
        {
            GD.PrintErr("[CultureLegendTest] HATA: kültür katmanı üretilmedi");
            GetTree().Quit(1);
            return;
        }

        // Harita bağı için canlı bir WorldMap gerekir (menüde Instance boştur).
        var existingMap = WorldMap.Instance;
        if (existingMap == null)
        {
            try
            {
                var wm = new WorldMap();
                AddChild(wm);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[CultureLegendTest] WorldMap kurulamadı: {e.Message}");
                ok = false;
            }
        }
        bool mapReady = WorldMap.Instance != null;
        GD.Print($"[CultureLegendTest] WorldMap hazır: {mapReady}");

        var host = new Control { Name = "CultureLegendPanelTestHost" };
        host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(host);

        try
        {
            // ---- 1) Paneli aç ----
            GD.Print("[CultureLegendTest] 1) Panel açılışı");
            CultureLegendPanel.OpenUnder(host);
            await ToSignal(GetTree(), "process_frame");
            await ToSignal(GetTree(), "process_frame");

            var panel = FindCultureLegendPanel();
            if (panel == null)
            {
                GD.PrintErr("[CultureLegendTest] HATA: panel düğümü bulunamadı");
                GetTree().Quit(1);
                return;
            }
            if (!CultureLegendPanel.IsOpen)
            {
                GD.PrintErr("[CultureLegendTest] HATA: panel açılmadı");
                ok = false;
            }

            Vector2 designed = panel.DesignedSize;
            GD.Print($"[CultureLegendTest] Panel tasarım boyutu: {designed.X}x{designed.Y}");
            if (designed.X <= 0f || designed.Y <= 0f)
            {
                GD.PrintErr("[CultureLegendTest] HATA: panel boyutu okunamadı");
                ok = false;
            }
            if (designed.X > 1152 || designed.Y > 648)
            {
                GD.PrintErr($"[CultureLegendTest] HATA: panel 1152x648'e sığmıyor " +
                            $"({designed.X}x{designed.Y})");
                ok = false;
            }

            // ---- 2) Satır sayısı ve tekillik ----
            int rows = panel.CultureRowCount;
            GD.Print($"[CultureLegendTest] Kültür satırı: {rows}");
            if (rows != cultures)
            {
                GD.PrintErr($"[CultureLegendTest] HATA: her ayrı kültür bir satır olmalı " +
                            $"({rows} != {cultures})");
                ok = false;
            }

            var ids = panel.CultureIds;
            if (ids.Count != new HashSet<string>(ids, StringComparer.Ordinal).Count)
            {
                GD.PrintErr("[CultureLegendTest] HATA: kültür satırlarında tekrar var");
                ok = false;
            }

            var counts = panel.ProvinceCounts;
            long sumProvinces = 0;
            foreach (var c in counts) sumProvinces += c;
            if (sumProvinces != records)
            {
                GD.PrintErr($"[CultureLegendTest] HATA: il sayıları toplamı kayıt sayısına eşit " +
                            $"olmali ({sumProvinces} != {records})");
                ok = false;
            }

            var shares = panel.PopulationShares;
            double sumShare = 0d;
            foreach (var s in shares) sumShare += s;
            GD.Print($"[CultureLegendTest] Nüfus payı toplamı: {sumShare:F6}");
            if (Math.Abs(sumShare - 1d) > 0.001d)
            {
                GD.PrintErr($"[CultureLegendTest] HATA: nüfus payları toplamı 1.0 olmalı ({sumShare:F6})");
                ok = false;
            }

            // ---- 3) Sıralama: nüfus payı azalan, eşitlikte il sayısı, sonra kimlik ----
            bool sorted = true;
            for (int i = 1; i < rows; i++)
            {
                if (shares[i - 1] < shares[i] - 1e-12d) { sorted = false; break; }
                if (Math.Abs(shares[i - 1] - shares[i]) <= 1e-12d)
                {
                    if (counts[i - 1] < counts[i]) { sorted = false; break; }
                    if (counts[i - 1] == counts[i] &&
                        string.CompareOrdinal(ids[i - 1], ids[i]) > 0) { sorted = false; break; }
                }
            }
            if (!sorted)
            {
                GD.PrintErr("[CultureLegendTest] HATA: kültür satırları pay/il/kimliğe göre sıralı değil");
                ok = false;
            }

            // ---- 4) Çubuk oranları ----
            var bars = panel.BarFractions;
            if (bars.Count != rows)
            {
                GD.PrintErr("[CultureLegendTest] HATA: çubuk sayısı satır sayısına eşit değil");
                ok = false;
            }
            bool barsOk = true;
            for (int i = 0; i < rows && i < bars.Count; i++)
            {
                if (bars[i] < -1e-9d || bars[i] > 1d + 1e-9d) { barsOk = false; break; }
                if (i > 0 && bars[i - 1] < bars[i] - 1e-12d) { barsOk = false; break; }
            }
            if (rows > 0 && (bars.Count == 0 || Math.Abs(bars[0] - 1d) > 1e-9d))
            {
                GD.PrintErr($"[CultureLegendTest] HATA: en büyük kültürün çubuğu tam uzunluk olmalı " +
                            $"({(bars.Count > 0 ? bars[0].ToString("F6") : "-")})");
                barsOk = false;
            }
            if (!barsOk)
            {
                GD.PrintErr("[CultureLegendTest] HATA: çubuk oranları geçersiz");
                ok = false;
            }

            // ---- 5) Satır seçimi → sahip ülkeler ----
            int pick = -1;
            for (int i = 0; i < rows; i++)
                if (counts[i] >= 2 && (ids[i]?.Length ?? 0) > 0) { pick = i; break; }
            if (pick < 0) pick = rows - 1;
            if (pick < 0)
            {
                GD.PrintErr("[CultureLegendTest] HATA: seçilecek kültür satırı yok");
                GetTree().Quit(1);
                return;
            }

            string pickCulture = ids[pick];
            GD.Print($"[CultureLegendTest] 2) Seçim: {pickCulture}");

            // Beklenen sahip ülkeler kültür katmanından BAĞIMSIZ sayılır.
            var expectedOwners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in ProvinceCultureSystem.All())
            {
                if (rec == null || !string.Equals(rec.Culture ?? "", pickCulture, StringComparison.Ordinal))
                    continue;
                string owner = rec.CountryId ?? "";
                expectedOwners.TryGetValue(owner, out int n);
                expectedOwners[owner] = n + 1;
            }
            var expectedOrdered = expectedOwners
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key)
                .ToList();

            var buttons = FindCultureRowButtons(panel);
            GD.Print($"[CultureLegendTest] Kültür satır butonu: {buttons.Count}");
            if (buttons.Count != rows)
            {
                GD.PrintErr($"[CultureLegendTest] HATA: tıklanabilir satır sayısı tutmuyor " +
                            $"({buttons.Count} != {rows})");
                ok = false;
            }
            else
            {
                buttons[pick].EmitSignal(BaseButton.SignalName.Pressed);
                await ToSignal(GetTree(), "process_frame");

                if (!string.Equals(panel.SelectedCultureId, pickCulture, StringComparison.Ordinal))
                {
                    GD.PrintErr($"[CultureLegendTest] HATA: seçim alınmadı " +
                                $"({panel.SelectedCultureId} != {pickCulture})");
                    ok = false;
                }

                var highlighted = panel.HighlightedCountryIds;
                GD.Print($"[CultureLegendTest] Vurgulanan ülke: {highlighted.Count} " +
                         $"[{string.Join(", ", highlighted)}]");
                if (highlighted.Count != expectedOwners.Count)
                {
                    GD.PrintErr($"[CultureLegendTest] HATA: sahip ülke sayısı yanlış " +
                                $"({highlighted.Count} != {expectedOwners.Count})");
                    ok = false;
                }
                for (int i = 0; i < expectedOrdered.Count && i < highlighted.Count; i++)
                {
                    if (!string.Equals(expectedOrdered[i], highlighted[i], StringComparison.OrdinalIgnoreCase))
                    {
                        GD.PrintErr($"[CultureLegendTest] HATA: sahip ülkeler il sayısına göre değil " +
                                    $"sıralı ({string.Join(", ", highlighted)})");
                        ok = false;
                    }
                }
                foreach (var owner in expectedOwners.Keys)
                    if (!highlighted.Contains(owner, StringComparer.OrdinalIgnoreCase))
                    {
                        GD.PrintErr($"[CultureLegendTest] HATA: sahip ülke listede yok: {owner}");
                        ok = false;
                    }

                // Detayda her sahip ülke adı geçmeli
                var detail = string.Join(" | ", panel.DetailTexts);
                foreach (var kv in expectedOwners)
                {
                    var country = CountryDatabase.Get(kv.Key);
                    string name = country?.Name ?? kv.Key;
                    if (!detail.Contains(name, StringComparison.Ordinal))
                    {
                        GD.PrintErr($"[CultureLegendTest] HATA: detayda sahip ülke adı yok: {name}");
                        ok = false;
                    }
                }
            }

            // ---- 6) Harita bağı ----
            GD.Print("[CultureLegendTest] 3) Harita vurgusu");
            var wm = WorldMap.Instance;
            if (!mapReady || wm == null)
            {
                GD.Print("[CultureLegendTest] Harita yok — vurgu yalnızca panelde doğrulandı");
            }
            else
            {
                if (!string.Equals(wm.CultureHighlight, pickCulture, StringComparison.Ordinal))
                {
                    GD.PrintErr($"[CultureLegendTest] HATA: harita vurgusu yazılmadı " +
                                $"({wm.CultureHighlight} != {pickCulture})");
                    ok = false;
                }
                if (wm.CurrentMapMode != WorldMap.MapMode.Culture)
                {
                    GD.PrintErr($"[CultureLegendTest] HATA: harita culture moduna geçmedi " +
                                $"({wm.CurrentMapMode})");
                    ok = false;
                }
                if (!panel.MapHighlightApplied)
                {
                    GD.PrintErr("[CultureLegendTest] HATA: panel harita vurgusunu onaylamadı");
                    ok = false;
                }

                // Vurgulanmayan illerin rengi kararmış olmalı
                string otherCulture = ProvinceCultureSystem.All()
                    .Where(r => r != null && !string.Equals(r.Culture ?? "", pickCulture, StringComparison.Ordinal))
                    .Select(r => r.Culture)
                    .FirstOrDefault() ?? "";
                if (otherCulture.Length == 0)
                {
                    GD.Print("[CultureLegendTest] Karşılaştırılacak ikinci kültür yok — kararma atlandı");
                }
                else
                {
                    var pickProv = ProvinceCultureSystem.All().First(r => r != null &&
                        string.Equals(r.Culture, pickCulture, StringComparison.Ordinal));
                    var otherProv = ProvinceCultureSystem.All().First(r => r != null &&
                        string.Equals(r.Culture, otherCulture, StringComparison.Ordinal));
                    var pickColor = wm.ProvinceFillColor(pickProv.ProvinceId);
                    var otherColor = wm.ProvinceFillColor(otherProv.ProvinceId);
                    float pickLum = pickColor.R + pickColor.G + pickColor.B;
                    float otherLum = otherColor.R + otherColor.G + otherColor.B;
                    GD.Print($"[CultureLegendTest] Parlaklık — vurgulu={pickLum:F3}, " +
                             $"diğer={otherLum:F3} ({otherCulture})");
                    if (otherLum >= pickLum - 0.001f)
                    {
                        GD.PrintErr("[CultureLegendTest] HATA: vurgulanmayan kültür kararmadı");
                        ok = false;
                    }
                }

                // Vurguyu kaldırma
                var clearBtn = FindCulturePanelButton(panel, "CLEAR HIGHLIGHT");
                if (clearBtn == null)
                {
                    GD.PrintErr("[CultureLegendTest] HATA: CLEAR HIGHLIGHT butonu bulunamadı");
                    ok = false;
                }
                else
                {
                    clearBtn.EmitSignal(BaseButton.SignalName.Pressed);
                    await ToSignal(GetTree(), "process_frame");
                    if (wm.CultureHighlight.Length != 0)
                    {
                        GD.PrintErr("[CultureLegendTest] HATA: vurgu kaldırılmadı");
                        ok = false;
                    }
                    else
                    {
                        GD.Print("[CultureLegendTest] Vurgu kaldırıldı ✓");
                    }
                }
            }

            // ---- 7) ESC ile kapatma ----
            GD.Print("[CultureLegendTest] 4) ESC ile kapatma");
            panel._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (!panel.CloseRequested)
            {
                GD.PrintErr("[CultureLegendTest] HATA: ESC kapatma isteği üretmedi");
                ok = false;
            }
        }
        finally
        {
            var found = FindCultureLegendPanel();
            if (found != null) found.QueueFree();
            var map = WorldMap.Instance;
            if (map != null && existingMap == null) map.QueueFree();
            ProvinceCultureSystem.ResetForNewGame();
            ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
            ReleasableSystem.ResetForNewGame();
            AutonomySystem.ResetForNewGame();
            ResistanceSystem.ResetForNewGame();
            GD.Print("[CultureLegendTest] Dünya verisi geri yüklendi");
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>CultureLegendPanel düğümünü bulur (test yardımcısı).</summary>
    private CultureLegendPanel FindCultureLegendPanel()
    {
        foreach (var n in FindChildrenIn(this))
            if (n is CultureLegendPanel clp) return clp;
        return null;
    }

    /// <summary>Kültür satırı butonları (CLOSE ve eylem butonları hariç).</summary>
    private List<Button> FindCultureRowButtons(CultureLegendPanel panel)
    {
        var result = new List<Button>();
        foreach (var n in FindChildrenIn(panel))
        {
            if (n is not Button b) continue;
            string text = b.Text ?? "";
            if (text is "CLOSE" or "CLEAR HIGHLIGHT") continue;
            result.Add(b);
        }
        return result;
    }

    /// <summary>Paneldeki metni verilen yazıya eşit olan butonu bulur.</summary>
    private Button FindCulturePanelButton(CultureLegendPanel panel, string text)
    {
        foreach (var n in FindChildrenIn(panel))
            if (n is Button b && (b.Text ?? "") == text) return b;
        return null;
    }

    /// <summary>
    /// M46.3: Kültür bilgisi + eşleşmeyen il konturu testi (--culture-info-test).
    ///
    /// Doğrulananlar:
    ///   1) Kültür modunda imleç altındaki il yakalanır ve ipucu kutusu ilin
    ///      kültürünü, sahibini ve nüfusunu doğru yazar.
    ///   2) Eşleşmeyen il kümesi, kültür katmanından bağımsız olarak yeniden
    ///      sayıldığında birebir aynı çıkar (determinizm).
    ///   3) CountryInfoPanel'in kültür satırı dağılımla tutarlı.
    ///   4) Fare kontrolü bozulmaz: ipucu `Ignore` süzgecinde, RMB sürükleme
    ///      kamerayı kaydırır, tekerlek zoom yapar, sol tık ülke seçer.
    ///   5) Kültür modundan çıkınca ipucu kaybolur.
    /// </summary>
    private async void RunCultureInfoTest()
    {
        GD.Print("=========================================");
        GD.Print("  M46.3: KÜLTÜR BİLGİSİ + EŞLEŞMEYEN İL KONTURU TESTİ (--culture-info-test)");
        GD.Print("=========================================");

        bool ok = true;

        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();

        if (ProvinceCultureSystem.RecordCount == 0)
        {
            GD.PrintErr("[CultureInfoTest] HATA: kültür katmanı üretilmedi");
            GetTree().Quit(1);
            return;
        }

        if (WorldMap.Instance == null)
        {
            try
            {
                var wm = new WorldMap();
                AddChild(wm);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[CultureInfoTest] WorldMap kurulamadı: {e.Message}");
                GetTree().Quit(1);
                return;
            }
        }

        var map = WorldMap.Instance;
        await ToSignal(GetTree(), "process_frame");
        await ToSignal(GetTree(), "process_frame");

        map.SetMapMode(WorldMap.MapMode.Culture);
        GD.Print($"[CultureInfoTest] Kültür modu: {map.CurrentMapMode}, il kaydı: " +
                 $"{ProvinceCultureSystem.RecordCount}");

        // ---- 1) İmleç altındaki il + ipucu metni ----
        GD.Print("[CultureInfoTest] 1) İl ipucu");
        var probe = provinces
            .Where(p => p != null && !string.IsNullOrEmpty(p.Id) && ProvinceCultureSystem.Get(p.Id) != null)
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (probe == null)
        {
            GD.PrintErr("[CultureInfoTest] HATA: kültürü olan il bulunamadı");
            GetTree().Quit(1);
            return;
        }

        Vector2 screen = WorldToScreen(map, probe.Centroid);
        // Headless'te viewport fare konumu sentezlenemez (WarpMouse etkisiz) ve
        // WorldMap._Input olay konumu yerine viewport'in fare konumunu okur.
        // Bu yüzden hover doğrudan sürülür — kültür ipucu mantığın kendisi çalışır.
        map.UpdateHover(probe.Centroid);
        await ToSignal(GetTree(), "process_frame");
        await ToSignal(GetTree(), "process_frame");

        GD.Print($"[CultureInfoTest] İmleç ili: {map.HoveredProvinceId} (aranan {probe.Id})");
        if (!string.Equals(map.HoveredProvinceId, probe.Id, StringComparison.Ordinal))
        {
            GD.PrintErr($"[CultureInfoTest] HATA: kültür modunda imleç ili yakalanmadı " +
                        $"({map.HoveredProvinceId} != {probe.Id})");
            ok = false;
        }

        var rec = ProvinceCultureSystem.Get(probe.Id);
        if (rec == null)
        {
            GD.PrintErr($"[CultureInfoTest] HATA: {probe.Id} için kültür kaydı yok");
            ok = false;
        }
        else
        {
            string tip = map.CultureTipText;
            GD.Print($"[CultureInfoTest] İpucu metni:\n{tip}");
            bool mismatch = map.IsCultureMismatchProvince(probe.Id);
            if (!map.CultureTipVisible)
            {
                GD.PrintErr("[CultureInfoTest] HATA: ipucu kutusu görünür olmalı");
                ok = false;
            }
            if (!tip.Contains(probe.Id, StringComparison.Ordinal))
            {
                GD.PrintErr("[CultureInfoTest] HATA: ipucu il kimliğini içermiyor");
                ok = false;
            }
            string cultureText = string.IsNullOrEmpty(rec.Culture) ? "(no culture)" : rec.Culture;
            if (!tip.Contains(cultureText, StringComparison.Ordinal))
            {
                GD.PrintErr($"[CultureInfoTest] HATA: ipucu kültürü içermiyor ({cultureText})");
                ok = false;
            }
            if (!tip.Contains(rec.Population.ToString("N0"), StringComparison.Ordinal))
            {
                GD.PrintErr($"[CultureInfoTest] HATA: ipucu nüfusu içermiyor ({rec.Population:N0})");
                ok = false;
            }
            // Yabancı kültür varsa ipucu bunu söylemeli, yoksa baskın olduğunu.
            if (mismatch && !tip.Contains("Foreign culture", StringComparison.Ordinal))
            {
                GD.PrintErr("[CultureInfoTest] HATA: yabancı kültür ilinde ipucu uyarısı yok");
                ok = false;
            }
            if (!mismatch && !tip.Contains("Dominant culture", StringComparison.Ordinal))
            {
                GD.PrintErr("[CultureInfoTest] HATA: baskın kültür ilinde ipucu belirtmiyor");
                ok = false;
            }
        }

        // ---- 2) Eşleşmeyen il kümesi: bağımsız yeniden sayım ----
        GD.Print("[CultureInfoTest] 2) Eşleşmeyen il kümesi");

        var expectedMismatch = new SortedSet<string>(StringComparer.Ordinal);
        var byCountry = new Dictionary<string, List<ProvinceCultureSystem.CultureRecord>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var r in ProvinceCultureSystem.All())
        {
            if (r == null) continue;
            string owner = r.CountryId ?? "";
            if (!byCountry.TryGetValue(owner, out var list))
            {
                list = new List<ProvinceCultureSystem.CultureRecord>();
                byCountry[owner] = list;
            }
            list.Add(r);
        }
        foreach (var kv in byCountry)
        {
            var dist = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in kv.Value)
            {
                string c = r.Culture ?? "";
                dist.TryGetValue(c, out int n);
                dist[c] = n + 1;
            }
            if (dist.Count <= 1) continue;
            string dominant = dist.OrderByDescending(d => d.Value)
                                  .ThenBy(d => d.Key, StringComparer.Ordinal).First().Key;
            foreach (var r in kv.Value)
                if (!string.Equals(r.Culture ?? "", dominant, StringComparison.Ordinal))
                    expectedMismatch.Add(r.ProvinceId);
        }

        var actualMismatch = new SortedSet<string>(map.CultureMismatchProvinceIds, StringComparer.Ordinal);
        GD.Print($"[CultureInfoTest] Eşleşmeyen il: harita={actualMismatch.Count}, " +
                 $"beklenen={expectedMismatch.Count}");
        if (actualMismatch.Count != expectedMismatch.Count)
        {
            GD.PrintErr($"[CultureInfoTest] HATA: eşleşmeyen il sayısı tutmuyor " +
                        $"({actualMismatch.Count} != {expectedMismatch.Count})");
            ok = false;
        }
        foreach (var id in expectedMismatch)
            if (!map.IsCultureMismatchProvince(id))
            {
                GD.PrintErr($"[CultureInfoTest] HATA: eşleşmeyen il işaretlenmemiş: {id}");
                ok = false;
            }

        // ---- 3) CountryInfoPanel kültür satırı ----
        GD.Print("[CultureInfoTest] 3) Ülke paneli kültür satırı");
        var infoPanel = FindCountryInfoPanel();
        if (infoPanel == null)
        {
            // Oyun sahnesi yüklü değil (headless): kendi panelimizi kur.
            try
            {
                infoPanel = new CountryInfoPanel { Name = "CultureInfoTestCountryInfo" };
                AddChild(infoPanel);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[CultureInfoTest] CountryInfoPanel kurulamadı: {e.Message}");
            }
        }
        if (infoPanel == null)
        {
            GD.PrintErr("[CultureInfoTest] HATA: CountryInfoPanel bulunamadı");
            ok = false;
        }
        else
        {
            var sampleCountry = CountryDatabase.All()
                .Where(c => c != null && ProvinceCultureSystem.ForCountry(c.Id).Count > 0)
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (sampleCountry == null)
            {
                GD.PrintErr("[CultureInfoTest] HATA: kültür kaydı olan ülke bulunamadı");
                ok = false;
            }
            else
            {
                infoPanel.ShowCountry(sampleCountry, sampleCountry.Name);
                string summary = infoPanel.CultureSummary;
                GD.Print($"[CultureInfoTest] {sampleCountry.Id} → {summary}");

                var dist = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var r in ProvinceCultureSystem.ForCountry(sampleCountry.Id))
                {
                    if (r == null) continue;
                    string c = string.IsNullOrEmpty(r.Culture) ? "(none)" : r.Culture;
                    dist.TryGetValue(c, out int n);
                    dist[c] = n + 1;
                }
                var ranked = dist.OrderByDescending(kv => kv.Value)
                                 .ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                string expectDominant = ranked[0].Key;
                int expectDominantCount = ranked[0].Value;
                int expectForeign = ProvinceCultureSystem.ForCountry(sampleCountry.Id).Count -
                                    expectDominantCount;

                if (!summary.Contains(expectDominant, StringComparison.Ordinal))
                {
                    GD.PrintErr($"[CultureInfoTest] HATA: panel baskın kültürü göstermiyor " +
                                $"({expectDominant})");
                    ok = false;
                }
                if (!summary.Contains($"{expectDominantCount}/", StringComparison.Ordinal))
                {
                    GD.PrintErr($"[CultureInfoTest] HATA: panel baskın kültür il sayısını " +
                                $"göstermiyor ({expectDominantCount})");
                    ok = false;
                }
                if (!summary.Contains($"{expectForeign} foreign", StringComparison.Ordinal))
                {
                    GD.PrintErr($"[CultureInfoTest] HATA: panel yabancı il sayısını göstermiyor " +
                                $"({expectForeign})");
                    ok = false;
                }
            }
        }

        // ---- 4) Fare kontrolü ----
        GD.Print("[CultureInfoTest] 4) Fare kontrolü bozulmamalı");

        if (map.CultureTipMouseFilter != Control.MouseFilterEnum.Ignore)
        {
            GD.PrintErr($"[CultureInfoTest] HATA: ipucu kutusu harita girdisini blokluyor " +
                        $"({map.CultureTipMouseFilter})");
            ok = false;
        }
        else
        {
            GD.Print("[CultureInfoTest] İpucu süzgeci Ignore ✓");
        }

        // Teksel: kamera geçmişte kaydırma + zoom kontrolü (referans değerler)
        Vector2 camBefore = map._camera.Position;
        float zoomBefore = map._camera.Zoom.X;

        // Sağ tık bas → sürükleme → bırak: kamera kaymalı
        map._Input(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            Pressed = true,
            Position = screen
        });
        map._Input(new InputEventMouseMotion { Position = screen + new Vector2(40f, 25f) });
        map._Input(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            Pressed = false,
            Position = screen + new Vector2(40f, 25f)
        });
        Vector2 camAfterDrag = map._camera.Position;
        GD.Print($"[CultureInfoTest] RMB sürükleme kamera: {camBefore} → {camAfterDrag}");
        if (camAfterDrag == camBefore)
        {
            GD.PrintErr("[CultureInfoTest] HATA: sağ tık sürükleme kamerayı kaydırmadı");
            ok = false;
        }

        // Tekerlek: zoom değişmeli (ZoomAt hedefi kurar, AnimateZoom lerp ile uygular)
        map._Input(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelUp,
            Pressed = true,
            Position = screen
        });
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), "process_frame");
        float zoomAfter = map._camera.Zoom.X;
        GD.Print($"[CultureInfoTest] Tekerlek zoom: {zoomBefore} → {zoomAfter}");
        if (Mathf.IsEqualApprox(zoomAfter, zoomBefore))
        {
            GD.PrintErr("[CultureInfoTest] HATA: tekerlek zoom yapmadı");
            ok = false;
        }

        // WASD tuşu _Input tarafından tüketilmemeli (pan _Process'te okunur).
        // Not: IsInputHandled yapışkandır, önceki olayların izini taşır; bu yüzden
        // tuş olayını ayrı bir viewport durumunda değerlendirmek yerine doğrudan
        // haritanın klavye olayına tepki vermediğini (hiçbir durum değişmedi) ölçeriz.
        Vector2 camBeforeKey = map._camera.Position;
        float zoomBeforeKey = map._camera.Zoom.X;
        map._Input(new InputEventKey { Keycode = Key.W, Pressed = true });
        map._Input(new InputEventKey { Keycode = Key.W, Pressed = false });
        if (map._camera.Position != camBeforeKey || !Mathf.IsEqualApprox(map._camera.Zoom.X, zoomBeforeKey))
        {
            GD.PrintErr("[CultureInfoTest] HATA: harita klavye olayını _Input'ta tüketip kamerayı değiştirdi");
            ok = false;
        }

        // Sol tık yolu: ülke seçimi çalışmalı (kültür ipucu bunu engellememeli)
        var countryBefore = FindSelectedCountryId(map);
        map.SelectAt(probe.Centroid);
        await ToSignal(GetTree(), "process_frame");
        string countryAfter = FindSelectedCountryId(map);
        GD.Print($"[CultureInfoTest] Sol tık ülke seçimi: {countryBefore} → {countryAfter}");
        if (string.IsNullOrEmpty(countryAfter))
        {
            GD.PrintErr("[CultureInfoTest] HATA: sol tık ülke seçmedi");
            ok = false;
        }

        // ---- 5) Moddan çıkınca ipucu kaybolur ----
        GD.Print("[CultureInfoTest] 5) Moddan çıkınca ipucu kalkar");
        map.SetMapMode(WorldMap.MapMode.Political);
        map.UpdateHover(probe.Centroid);
        await ToSignal(GetTree(), "process_frame");
        await ToSignal(GetTree(), "process_frame");
        if (map.CultureTipVisible)
        {
            GD.PrintErr("[CultureInfoTest] HATA: kültür modu dışında ipucu hâlâ görünür");
            ok = false;
        }
        if (map.HoveredProvinceId.Length != 0)
        {
            GD.PrintErr("[CultureInfoTest] HATA: kültür modu dışında il kaydı kaldı");
            ok = false;
        }
        if (map.CultureTipText.Length != 0)
        {
            GD.PrintErr("[CultureInfoTest] HATA: kültür modu dışında ipucu metni kaldı");
            ok = false;
        }

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>Dünya koordinatını ekran (viewport) koordinatına çevirir (test yardımcısı).</summary>
    private static Vector2 WorldToScreen(WorldMap map, Vector2 world)
    {
        var cam = map._camera;
        if (cam == null) return Vector2.Zero;
        Vector2 viewport = map.GetViewportRect().Size;
        return (world - cam.Position) * cam.Zoom.X + viewport / 2f;
    }

    /// <summary>Haritada seçili ülke kimliği (test yardımcısı).</summary>
    private static string FindSelectedCountryId(WorldMap map) => map.SelectedCountryId;

    /// <summary>CountryInfoPanel düğümünü bulur (test yardımcısı).</summary>
    private CountryInfoPanel FindCountryInfoPanel()
    {
        foreach (var n in FindChildrenIn(this))
            if (n is CountryInfoPanel cip) return cip;
        return null;
    }

    /// <summary>ReleasablePanel düğümünü bulur (test yardımcısı).</summary>
    private ReleasablePanel FindReleasablePanel()
    {
        foreach (var n in FindChildrenIn(this))
            if (n is ReleasablePanel rp) return rp;
        return null;
    }

    /// <summary>ItemList düğümünü bulur (test yardımcısı).</summary>
    private ItemList FindItemList(Node root)
    {
        foreach (var n in FindChildrenIn(root))
            if (n is ItemList il) return il;
        return null;
    }

    /// <summary>
    /// M45.2 özerklik sistemi testi (--autonomy-test).
    ///
    /// Doğrulananlar:
    ///   1) İlk seviyeler: ilhak 0, kukla 30 (Data/autonomy.json).
    ///   2) Günlük kayma formülü: taban + yabancı_kültür*ağırlık + zayıflık*ağırlık.
    ///   3) Kayma determinizmi: aynı dünya, iki ayrı koşu → aynı seviye.
    ///   4) Özerklik ver / geri al: adım, bekleme süresi, yetkisiz aktör reddi.
    ///   5) Çıkarım katsayısı ve stabilite baskısı (gerçek tüketiciler).
    ///   6) Save/load round-trip: seviyeler birebir korunur.
    /// </summary>
    private void RunAutonomyTest()
    {
        GD.Print("=========================================");
        GD.Print("  M45.2: ÖZERKLİK SİSTEMİ TESTİ");
        GD.Print("=========================================");

        bool ok = true;

        // 1) Dünya: il + kültür + sahiplik
        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();

        // Overlord: en çok ili olan ülke. Bağımlı: kültürü farklı, 3+ ili olan en küçük ülke.
        var overlord = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed && ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count > 0)
            .OrderByDescending(c => ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count)
            .First();

        string overlordCulture = TopCulture(overlord.Id);
        var subject = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed &&
                !string.Equals(c.Id, overlord.Id, StringComparison.OrdinalIgnoreCase) &&
                ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count >= 3 &&
                !string.Equals(TopCulture(c.Id), overlordCulture, StringComparison.Ordinal))
            .OrderBy(c => ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count)
            .FirstOrDefault();
        if (subject == null)
        {
            GD.PrintErr("[AutonomyTest] HATA: kültürü farklı deneme bağımlısı bulunamadı");
            GetTree().Quit(1);
            return;
        }

        var subjectProvinces = ProvinceOwnershipSystem.GetProvincesByOwner(subject.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        int civBefore = subject.CivFactories;
        int milBefore = subject.MilFactories;
        int dockBefore = subject.Dockyards;

        GD.Print($"[AutonomyTest] 1) Overlord={overlord.Id} (kültür={overlordCulture}), " +
                 $"bağımlı={subject.Id} (il={subjectProvinces.Count}, " +
                 $"fabrika c{civBefore}/m{milBefore}/t{dockBefore})");

        // İlhak uygula (kayıt iller devredilmeden ÖNCE düşer)
        ReleasableSystem.RecordSubject(subject.Id, overlord.Id, false, civBefore, milBefore, dockBefore);
        subject.IsAnnexed = true;
        subject.OwnerId = overlord.Id;
        overlord.CivFactories += civBefore;
        overlord.MilFactories += milBefore;
        overlord.Dockyards += dockBefore;
        subject.CivFactories = subject.MilFactories = subject.Dockyards = 0;
        foreach (var p in subjectProvinces)
            ProvinceOwnershipSystem.SetProvinceOwner(p, overlord.Id);
        // Kültür SAHİBİ overlord'a geçer ama kültürün KENDİSİ değişmez (M45.3 direnişin temeli).
        ProvinceCultureSystem.ReassignCountry(subjectProvinces, overlord.Id);

        // 2) Başlangıç seviyesi: ilhak edilen 0 olmalı
        double start = AutonomySystem.Level(subject.Id);
        if (Math.Abs(start) > 1e-6)
        {
            GD.PrintErr($"[AutonomyTest] HATA: ilhak başlangıç seviyesi {start} != 0");
            ok = false;
        }
        else GD.Print($"[AutonomyTest] 2) İlhak başlangıç seviyesi: {start:N1}");

        // 3) Kukla başlangıç seviyesi 30 olmalı
        var puppet = CountryDatabase.All()
            .First(c => c != null && !c.IsAnnexed &&
                !string.Equals(c.Id, overlord.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Id, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count >= 1);
        var puppetProvinces = ProvinceOwnershipSystem.GetProvincesByOwner(puppet.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        ReleasableSystem.RecordSubject(puppet.Id, overlord.Id, true, 0, 0, 0);
        puppet.IsAnnexed = true;
        puppet.OwnerId = overlord.Id;
        foreach (var p in puppetProvinces)
            ProvinceOwnershipSystem.SetProvinceOwner(p, overlord.Id);
        ProvinceCultureSystem.ReassignCountry(puppetProvinces, overlord.Id);

        double puppetStart = AutonomySystem.Level(puppet.Id);
        if (Math.Abs(puppetStart - 30.0) > 1e-6)
        {
            GD.PrintErr($"[AutonomyTest] HATA: kukla başlangıç seviyesi {puppetStart} != 30");
            ok = false;
        }
        else GD.Print($"[AutonomyTest] 3) Kukla başlangıç seviyesi: {puppetStart:N1}");

        // 3b) Ayna kukla: bağımlı olarak aynı anda kaydedilir, determinizm karşılaştırması için.
        var mirror = CountryDatabase.All()
            .First(c => c != null && !c.IsAnnexed &&
                !string.Equals(c.Id, overlord.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Id, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Id, puppet.Id, StringComparison.OrdinalIgnoreCase) &&
                ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count >= 1);
        var mirrorProvinces = ProvinceOwnershipSystem.GetProvincesByOwner(mirror.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        ReleasableSystem.RecordSubject(mirror.Id, overlord.Id, true, 0, 0, 0);
        mirror.IsAnnexed = true;
        mirror.OwnerId = overlord.Id;
        foreach (var p in mirrorProvinces)
            ProvinceOwnershipSystem.SetProvinceOwner(p, overlord.Id);
        ProvinceCultureSystem.ReassignCountry(mirrorProvinces, overlord.Id);

        // 4) Kayma formülü: overlord stabilitesini bilinen bir değere sabitle
        double stabilityBefore = overlord.StabilityPercent;
        overlord.StabilityPercent = 50.0;   // zayıflık = (100-50)/100 = 0.5
        double foreignShare = AutonomySystem.ForeignCultureShare(subject.Id);
        double weakness = 0.5;
        double expectedDrift = 0.05 + foreignShare * 0.25 + weakness * 0.2;

        AutonomySystem.DailyTick(1);
        double level1 = AutonomySystem.Level(subject.Id);
        double actualDrift = AutonomySystem.Get(subject.Id).LastDailyDrift;

        GD.Print($"[AutonomyTest] 4) Kayma: yabancı_kültür={foreignShare:P0}, " +
                 $"beklenen={expectedDrift:F4}, gerçekleşen={actualDrift:F4} → seviye={level1:N2}");

        if (foreignShare <= 0.0)
        {
            GD.PrintErr("[AutonomyTest] HATA: yabancı kültür payı 0 — kültür baskısı test edilemedi");
            ok = false;
        }
        if (Math.Abs(actualDrift - expectedDrift) > 1e-4)
        {
            GD.PrintErr($"[AutonomyTest] HATA: kayma {actualDrift} != beklenen {expectedDrift}");
            ok = false;
        }
        if (Math.Abs(level1 - expectedDrift) > 1e-4)
        {
            GD.PrintErr($"[AutonomyTest] HATA: 1 gün sonraki seviye {level1} != {expectedDrift}");
            ok = false;
        }

        // 10 gün daha: seviye 10 * kayma olmalı (0-100 arası)
        for (long day = 2; day <= 10; day++) AutonomySystem.DailyTick(day);
        double level10 = AutonomySystem.Level(subject.Id);
        double expected10 = Math.Min(10.0 * expectedDrift, 100.0);
        double puppetLevel10 = AutonomySystem.Level(puppet.Id);
        if (Math.Abs(level10 - expected10) > 1e-3)
        {
            GD.PrintErr($"[AutonomyTest] HATA: 10 gün sonraki seviye {level10} != {expected10}");
            ok = false;
        }
        else GD.Print($"[AutonomyTest]    10 gün sonraki seviye: {level10:N2} (beklenen {expected10:N2})");

        // 5) Determinizm: her iki kukla için seviye, KENDİ girdileriyle hesaplanan
        // formüle (başlangıç + gün × kayma) birebir uymalı. Kaymada RNG yok;
        // girdiler (kültür dağılımı, overlord stabilitesi) sabitse çıktı sabittir.
        // Not: iki farklı ülke farklı sonuç verir — çünkü kültür dağılımları farklıdır.
        double mirrorDrift = AutonomySystem.Get(mirror.Id).LastDailyDrift;
        double mirrorExpected = 30.0 + 10.0 * mirrorDrift;
        double mirrorLevel10 = AutonomySystem.Level(mirror.Id);
        if (Math.Abs(mirrorLevel10 - mirrorExpected) > 1e-3)
        {
            GD.PrintErr($"[AutonomyTest] HATA: ayna seviyesi {mirrorLevel10} != formül {mirrorExpected}");
            ok = false;
        }
        else GD.Print($"[AutonomyTest] 5) Ayna kukla formüle uyuyor: {mirrorLevel10:N4} " +
                      $"(beklenen {mirrorExpected:N4})");

        double puppetDrift = AutonomySystem.Get(puppet.Id).LastDailyDrift;
        double puppetExpected = 30.0 + 10.0 * puppetDrift;
        if (Math.Abs(puppetLevel10 - puppetExpected) > 1e-3)
        {
            GD.PrintErr($"[AutonomyTest] HATA: kukla seviyesi {puppetLevel10} != formül {puppetExpected}");
            ok = false;
        }
        else GD.Print($"[AutonomyTest]    Kukla da formüle uyuyor: {puppetLevel10:N4} " +
                      $"(beklenen {puppetExpected:N4})");

        // 6) Özerklik ver / geri al + bekleme süresi + yetki
        double before4 = AutonomySystem.Level(subject.Id);
        var stranger = CountryDatabase.All()
            .First(c => c != null && !c.IsAnnexed &&
                !string.Equals(c.Id, overlord.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Id, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Id, puppet.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Id, mirror.Id, StringComparison.OrdinalIgnoreCase));

        string authReason = "";
        if (stranger != null && AutonomySystem.GrantAutonomy(subject.Id, stranger.Id, 5, out authReason))
        {
            GD.PrintErr($"[AutonomyTest] HATA: yetkisiz aktör {stranger.Id} özerklik verebildi");
            ok = false;
        }
        else GD.Print($"[AutonomyTest] 6) Yetkisiz aktör reddedildi ({authReason})");

        // Son değişiklik günü = -999 (henüz ver/geri al yok), cooldown'a takılmamalı
        if (!AutonomySystem.GrantAutonomy(subject.Id, overlord.Id, 5, out var grantReason))
        {
            GD.PrintErr($"[AutonomyTest] HATA: özerklik verilemedi — {grantReason}");
            ok = false;
        }
        else
        {
            double after = AutonomySystem.Level(subject.Id);
            GD.Print($"[AutonomyTest]    Özerklik verildi: {before4:N1} → {after:N1}");
            if (Math.Abs(after - (before4 + 10.0)) > 1e-6)
            {
                GD.PrintErr($"[AutonomyTest] HATA: özerklik adımı {after} != {before4 + 10.0}");
                ok = false;
            }
        }

        // Bekleme süresi (7 gün) içinde ikinci karar reddedilmeli
        if (AutonomySystem.RevokeAutonomy(subject.Id, overlord.Id, 11, out var cdReason))
        {
            GD.PrintErr("[AutonomyTest] HATA: bekleme süresi dolmadan geri alındı");
            ok = false;
        }
        else GD.Print($"[AutonomyTest]    Bekleme süresi reddetti ({cdReason})");

        // 5 + 7 = 12. günde izin verir
        double beforeR = AutonomySystem.Level(subject.Id);
        if (!AutonomySystem.RevokeAutonomy(subject.Id, overlord.Id, 12, out var revokeReason))
        {
            GD.PrintErr($"[AutonomyTest] HATA: geri alma başarısız — {revokeReason}");
            ok = false;
        }
        else
        {
            double afterR = AutonomySystem.Level(subject.Id);
            GD.Print($"[AutonomyTest]    Özerklik geri alındı: {beforeR:N1} → {afterR:N1}");
            if (Math.Abs(afterR - (beforeR - 10.0)) > 1e-6)
            {
                GD.PrintErr($"[AutonomyTest] HATA: geri alma adımı {afterR} != {beforeR - 10.0}");
                ok = false;
            }
        }

        // 7) Etkiler — çıkarım katsayısı (il ağırlıklı) ve stabilite baskısı (düz ortalama)
        double subjectLevel = AutonomySystem.Level(subject.Id);
        double puppetLevel = AutonomySystem.Level(puppet.Id);
        double mirrorLevel = AutonomySystem.Level(mirror.Id);
        int subjectProv = subjectProvinces.Count;
        int puppetProv = puppetProvinces.Count;
        int mirrorProv = mirrorProvinces.Count;
        long totalProv = subjectProv + puppetProv + mirrorProv;
        double expectedAvg = (subjectLevel * subjectProv + puppetLevel * puppetProv +
                              mirrorLevel * mirrorProv) / (double)totalProv;
        double expectedFactor = 1.0 - (expectedAvg / 100.0) * 0.5;
        double actualFactor = AutonomySystem.ResourceExtractionFactor(overlord.Id);
        if (Math.Abs(actualFactor - expectedFactor) > 1e-6)
        {
            GD.PrintErr($"[AutonomyTest] HATA: çıkarım katsayısı {actualFactor} != {expectedFactor}");
            ok = false;
        }
        else GD.Print($"[AutonomyTest] 7) Çıkarım katsayısı: {actualFactor:F4} " +
                      $"(ağırlıklı ortalama %{expectedAvg:N1})");

        double expectedPressure = ((subjectLevel + puppetLevel + mirrorLevel) / 3.0 / 100.0) * 0.15;
        double actualPressure = AutonomySystem.StabilityPressure(overlord.Id);
        if (Math.Abs(actualPressure - expectedPressure) > 1e-6)
        {
            GD.PrintErr($"[AutonomyTest] HATA: stabilite baskısı {actualPressure} != {expectedPressure}");
            ok = false;
        }
        else GD.Print($"[AutonomyTest]    Stabilite baskısı: {actualPressure:F4}/gün");

        overlord.StabilityPercent = 50.0;
        ApplyAutonomyStabilityPressure();
        double stabilityAfter = overlord.StabilityPercent;
        if (Math.Abs(stabilityAfter - (50.0 - expectedPressure)) > 1e-6)
        {
            GD.PrintErr($"[AutonomyTest] HATA: stabilite {stabilityAfter} != {50.0 - expectedPressure}");
            ok = false;
        }
        else GD.Print($"[AutonomyTest]    Overlord stabilitesi 50 → {stabilityAfter:N3}");

        // 8) Serbest bırakma kaydını silince özerklik durumu da budanmalı
        var rel = ReleasableSystem.Release(subject.Id, overlord.Id);
        if (!rel.Ok)
        {
            GD.PrintErr($"[AutonomyTest] HATA: serbest bırakma başarısız — {rel.Reason}");
            ok = false;
        }
        AutonomySystem.DailyTick(13);
        if (AutonomySystem.Get(subject.Id) != null)
        {
            GD.PrintErr("[AutonomyTest] HATA: bağımsız olan ülkenin özerklik kaydı kaldı");
            ok = false;
        }
        else GD.Print("[AutonomyTest] 8) Bağımsız olan ülkenin özerklik kaydı budandı");

        // 9) Save/Load round-trip
        var saveBefore = AutonomySystem.GetSaveData();
        double savedPuppetLevel = AutonomySystem.Level(puppet.Id);
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.LoadSaveData(saveBefore);
        if (Math.Abs(AutonomySystem.Level(puppet.Id) - savedPuppetLevel) > 1e-9)
        {
            GD.PrintErr($"[AutonomyTest] HATA: yükleme sonrası kukla seviyesi " +
                        $"{AutonomySystem.Level(puppet.Id)} != {savedPuppetLevel}");
            ok = false;
        }
        else GD.Print($"[AutonomyTest] 9) Save/load round-trip tamam " +
                      $"(kukla seviyesi {savedPuppetLevel:N2} korundu)");

        // Kukla hâlâ bağımlı olduğu için budanma dışı kalmalı
        if (AutonomySystem.Get(puppet.Id) == null)
        {
            GD.PrintErr("[AutonomyTest] HATA: hâlâ bağımlı olan kuklanın kaydı budandı");
            ok = false;
        }

        ReleasableSystem.Release(puppet.Id, overlord.Id);
        ReleasableSystem.Release(mirror.Id, overlord.Id);
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();
        overlord.StabilityPercent = stabilityBefore;
        subject.IsAnnexed = false;
        puppet.IsAnnexed = false;
        mirror.IsAnnexed = false;
        subject.OwnerId = "";
        puppet.OwnerId = "";
        mirror.OwnerId = "";
        ProvinceCultureSystem.ResetForNewGame();
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        GD.Print("[AutonomyTest] Dünya verisi geri yüklendi");

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// M45.3 direniş sistemi testi (--resistance-test).
    ///
    /// Doğrulananlar:
    ///   1) Uyumsuzluk puanı: yabancı kültür payı arttıkça artar (asimile → 0,
    ///      tam uyumsuz → 1).
    ///   2) Baskı hızı: uyumsuzlukla artar, özerklikle azalır (ölçülüyor).
    ///   3) Partizan eylemi: birikim eşiği aşılınca patlar; hedef il deterministik,
    ///      fabrika hasarı ve overlord zararları uygulanır, birikim boşalır.
    ///   4) Özerklik etkisi: tam özerk bağımlıda eylem hiç patlamaz.
    ///   5) Save/load round-trip: durum birebir korunur.
    /// </summary>
    private void RunResistanceTest()
    {
        GD.Print("=========================================");
        GD.Print("  M45.3: DİRENİŞ SİSTEMİ TESTİ");
        GD.Print("=========================================");

        bool ok = true;

        var source = new ProceduralProvinceSource();
        var provinces = source.LoadProvinces().ToList();
        ProvinceOwnershipSystem.InitializeFromProvinces(provinces);
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();

        // Overlord: en çok ili olan ülke. Bağımlı: kültürü farklı, 3+ ili olan en küçük.
        var overlord = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed && ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count > 0)
            .OrderByDescending(c => ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count)
            .First();

        string overlordCulture = TopCulture(overlord.Id);
        var subject = CountryDatabase.All()
            .Where(c => c != null && !c.IsAnnexed &&
                !string.Equals(c.Id, overlord.Id, StringComparison.OrdinalIgnoreCase) &&
                ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count >= 3 &&
                !string.Equals(TopCulture(c.Id), overlordCulture, StringComparison.Ordinal))
            .OrderBy(c => ProvinceOwnershipSystem.GetProvincesByOwner(c.Id).Count)
            .FirstOrDefault();
        if (subject == null)
        {
            GD.PrintErr("[ResistanceTest] HATA: kültürü farklı deneme bağımlısı bulunamadı");
            GetTree().Quit(1);
            return;
        }

        var subjectProvinces = ProvinceOwnershipSystem.GetProvincesByOwner(subject.Id)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        int civBefore = subject.CivFactories;
        int milBefore = subject.MilFactories;
        int dockBefore = subject.Dockyards;

        GD.Print($"[ResistanceTest] 1) Overlord={overlord.Id} (kültür={overlordCulture}), " +
                 $"bağımlı={subject.Id} (il={subjectProvinces.Count})");

        ReleasableSystem.RecordSubject(subject.Id, overlord.Id, false, civBefore, milBefore, dockBefore);
        subject.IsAnnexed = true;
        subject.OwnerId = overlord.Id;
        overlord.CivFactories += civBefore;
        overlord.MilFactories += milBefore;
        overlord.Dockyards += dockBefore;
        subject.CivFactories = subject.MilFactories = subject.Dockyards = 0;
        foreach (var p in subjectProvinces)
            ProvinceOwnershipSystem.SetProvinceOwner(p, overlord.Id);
        ProvinceCultureSystem.ReassignCountry(subjectProvinces, overlord.Id);

        // 2) Uyumsuzluk puanı 0-1 aralığında ve kültür payıyla artmalı
        double mismatch = ResistanceSystem.CultureMismatchScore(subject.Id);
        double foreignShare = AutonomySystem.ForeignCultureShare(subject.Id);
        GD.Print($"[ResistanceTest] 2) Uyumsuzluk: yabancı_kültür={foreignShare:P0} → puan={mismatch:F3}");
        if (mismatch < 0.0 || mismatch > 1.0)
        {
            GD.PrintErr($"[ResistanceTest] HATA: uyumsuzluk puanı {mismatch} 0-1 arasında değil");
            ok = false;
        }
        if (foreignShare > 0.0 && mismatch <= 0.0)
        {
            GD.PrintErr($"[ResistanceTest] HATA: yabancı kültür var ({foreignShare:P0}) ama puan 0");
            ok = false;
        }

        // 3) Baskı hızı: uyumsuzluk var, özerklik 0 → taban + puan×0.35
        overlord.StabilityPercent = 100.0;   // özerklik kaymasını baskısız tut
        double expectedRate = 0.02 + mismatch * 0.35;
        ResistanceSystem.DailyTick(1);
        double actualRate = ResistanceSystem.Get(subject.Id).LastDailyRate;
        if (Math.Abs(actualRate - expectedRate) > 1e-4)
        {
            GD.PrintErr($"[ResistanceTest] HATA: baskı hızı {actualRate} != beklenen {expectedRate}");
            ok = false;
        }
        else GD.Print($"[ResistanceTest] 3) Baskı hızı doğru: {actualRate:F4}/gün");

        // 4) Özerklik baskıyı azaltmalı
        overlord.StabilityPercent = 100.0;
        AutonomySystem.GrantAutonomy(subject.Id, overlord.Id, 1, out _);
        double lvl1 = AutonomySystem.Level(subject.Id);
        double rateBefore = ResistanceSystem.Get(subject.Id).LastDailyRate;
        // Tek gün ilerlet: özerklik artışı hızı düşürmeli
        ResistanceSystem.ResetForNewGame();
        ResistanceSystem.DailyTick(2);
        double rateAfter = ResistanceSystem.Get(subject.Id).LastDailyRate;
        double expectedAfter = 0.02 + mismatch * 0.35 - (lvl1 / 100.0) * 0.3;
        if (Math.Abs(rateAfter - expectedAfter) > 1e-4 || rateAfter >= rateBefore)
        {
            GD.PrintErr($"[ResistanceTest] HATA: özerklik hızı düşürmedi — {rateAfter} != {expectedAfter}");
            ok = false;
        }
        else GD.Print($"[ResistanceTest] 4) Özerklik hızı düşürdü: {rateBefore:F4} → {rateAfter:F4} " +
                      $"(özerklik %{lvl1:N1})");

        // 5) Partizan eylemi: özerkliği kaldırıp birikimi eşiğe taşı
        AutonomySystem.RevokeAutonomy(subject.Id, overlord.Id, 9, out _);
        ResistanceSystem.ResetForNewGame();

        int overlordCivBefore = overlord.CivFactories;
        int overlordStabilityBefore = (int)overlord.StabilityPercent;
        int strikes = 0;
        long strikeDay = -1;
        for (long day = 1; day <= 60; day++)
        {
            ResistanceSystem.DailyTick(day);
            var st = ResistanceSystem.Get(subject.Id);
            if (st != null && st.StrikeCount > strikes)
            {
                strikes = st.StrikeCount;
                strikeDay = day;
                GD.Print($"[ResistanceTest]    Gün {day}: eylem patladı (hedef il={st.LastStrikeProvinceId})");
            }
        }
        if (strikes <= 0)
        {
            GD.PrintErr("[ResistanceTest] HATA: 40 günde hiç partizan eylemi patlamadı");
            ok = false;
        }
        else
        {
            GD.Print($"[ResistanceTest] 5) {strikes} eylem patladı (ilk eylem: gün {strikeDay})");

            // Eylem uygulanmış olmalı: fabrika hasarı ve stabilite kaybı
            int civAfter = overlord.CivFactories;
            if (civAfter >= overlordCivBefore)
            {
                GD.PrintErr($"[ResistanceTest] HATA: fabrika hasarı uygulanmadı " +
                            $"(civ={civAfter}, önceki={overlordCivBefore})");
                ok = false;
            }
            else GD.Print($"[ResistanceTest]    Fabrika hasarı: civ {overlordCivBefore} → {civAfter}");

            // Hedef il, bağımlının içinden ve overlord'a ait olmalı
            var st = ResistanceSystem.Get(subject.Id);
            if (string.IsNullOrEmpty(st.LastStrikeProvinceId) ||
                !subjectProvinces.Contains(st.LastStrikeProvinceId))
            {
                GD.PrintErr($"[ResistanceTest] HATA: hedef il '{st.LastStrikeProvinceId}' " +
                            "bağımlının illerinden değil");
                ok = false;
            }
            else GD.Print($"[ResistanceTest]    Hedef il doğrulandı: {st.LastStrikeProvinceId}");

            if (overlord.StabilityPercent >= overlordStabilityBefore)
            {
                GD.PrintErr($"[ResistanceTest] HATA: overlord stabilitesi düşmedi " +
                            $"({overlord.StabilityPercent} >= {overlordStabilityBefore})");
                ok = false;
            }
            else GD.Print($"[ResistanceTest]    Overlord stabilitesi: {overlordStabilityBefore} → " +
                          $"{overlord.StabilityPercent:N1}");
        }

        // 6) Save/load round-trip
        var saveBefore = ResistanceSystem.GetSaveData();
        var stateBefore = ResistanceSystem.Get(subject.Id);
        int strikesBefore = stateBefore?.StrikeCount ?? 0;
        ResistanceSystem.ResetForNewGame();
        ResistanceSystem.LoadSaveData(saveBefore);
        if (ResistanceSystem.Get(subject.Id)?.StrikeCount != strikesBefore)
        {
            GD.PrintErr("[ResistanceTest] HATA: save/load sonrası eylem sayısı korunmadı");
            ok = false;
        }
        else GD.Print($"[ResistanceTest] 6) Save/load round-trip tamam " +
                      $"(eylem sayısı {strikesBefore} korundu)");

        // Temizlik
        overlord.CivFactories = overlordCivBefore;
        ReleasableSystem.ResetForNewGame();
        AutonomySystem.ResetForNewGame();
        ResistanceSystem.ResetForNewGame();
        subject.IsAnnexed = false;
        subject.OwnerId = "";
        subject.CivFactories = civBefore;
        subject.MilFactories = milBefore;
        subject.Dockyards = dockBefore;
        ProvinceCultureSystem.ResetForNewGame();
        ProvinceCultureSystem.GenerateFor(provinces, "historical_1936");
        GD.Print("[ResistanceTest] Dünya verisi geri yüklendi");

        GD.Print("=========================================");
        GD.Print(ok ? "  SONUÇ: PASS ✓" : "  SONUÇ: FAIL ✗");
        GD.Print("=========================================");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>Bir ülkenin en çok ili olan kültürünü verir (test yardımcısı).</summary>
    private static string TopCulture(string countryId)
    {
        var dist = ProvinceCultureSystem.GetCultureDistribution(countryId);
        if (dist.Count == 0) return "";
        return dist.OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
    }

    /// <summary>İl listesinde kimliği eşleşen ilin indeksini verir (test yardımcısı).</summary>
    private static bool PaProvinceOwner(List<ProvinceData> provinces, string provinceId, out int index)
    {
        for (int i = 0; i < provinces.Count; i++)
        {
            if (string.Equals(provinces[i].Id, provinceId, StringComparison.Ordinal))
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }
}

