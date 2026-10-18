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
            else if (arg.StartsWith("--load-slot=") && int.TryParse(arg.Substring(12), out var ls))
                loadSlot = Mathf.Clamp(ls, 0, SaveSystem.MaxSlots);
        }

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

        // M20.2: Bütçe günlük tick'i (gelir/harcama/açık/borç/faiz + göstergelere yansıma)
        BudgetSystem.DailyTick();

        // M20.4: Göstergeler arası bağlantılar (enflasyon↔işsizlik↔memnuniyet vb.)
        FormulaSystem.ApplyLinks();

        // M20.3: Halk memnuniyeti günlük tick'i (grubun göstergelere/harcamalara tepkisi)
        PublicSatisfactionSystem.DailyTick();

        // M21.3: atanan bakanların nitelikleri ilgili göstergeleri etkiler (son söz, deterministik)
        if (HasPlayer) CabinetSystem.ApplyMinisterEffects(PlayerCountryId);

        // M21.4: skandal, istifa, koalisyon baskısı, başarısızlık uyarısı denetimi
        if (HasPlayer) CabinetSystem.CheckCabinetEvents(PlayerCountryId);

        // M22.4: Otoriter sistem günlük darbe riski değerlendirmesi (tek parti rejimler için).
        if (HasPlayer) AutoritarianSystem.DailyTick(PlayerCountryId);

        // M16: Zaman ve Tempo — ajanda ilerlemesi, hafta reset, aylık rapor
        TimeTempoSystem.DailyTick(this);

        // M23.3: Seçim günü — oyuncu ülkesinde seçim sonucu çözümlenir (kazanma/kaybetme, lider değişikliği, yeni kabine)
        if (HasPlayer) ElectionSystem.ResolveElection(PlayerCountryId, TotalDays);

        // M12: Ocak 1'de otomatik kayıt (yeni yılın ilk günü)
        if (CurrentDate.Month == 1 && CurrentDate.Day == 1)
            SaveSystem.AutoSave();

        EmitSignal(SignalName.PlayerCountryDataChanged);
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

    // Yeni oyun: oturum durumunu sıfırla — oyuncu ülkesi + kilidi dahil.
    // MainMenu START çağırır; ana menü → yeni oyun akışında eski ülke kalmaz.
    public void ResetForNewGame()
    {
        PlayerCountryId = "";
        _playerCountryData = null;
        IsPlayerCountryLocked = false;
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

        // M-Flags: bayrak önbelleği sıfırlanır (rapor yeni oyunda tekrar loglanır)
        FlagLibrary.ResetForNewGame();

        // M16: Ajanda/aylık rapor durumu sıfırlanır
        TimeTempoSystem.ResetForNewGame();

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
        if (maxAtWarPercent <= _aiWarSimTargetPercent)
            GD.Print("[WarSim] HEDEF BAŞARILI ✓");
        else
            GD.Print("[WarSim] HEDEF AŞILDI ✗ — gerekçe şansı daha da düşürülmeli");
        GD.Print("=========================================");
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
        GD.Print("=========================================");
    }

    /// <summary>
    /// M17.1: Senaryo yükleme testi — her iki senaryo için veri yükleyici sayılarını loglar.
    /// Normal oyunda asla çalışmaz (--scenario-test).
    /// </summary>
    private void RunScenarioTest()
    {
        GD.Print("=========================================");
        GD.Print("  M17.1: SENARYO YÜKLEME TESTİ");
        GD.Print("=========================================");

        // ScenarioSystem'in iç metodunu çağır (her senaryo için loader sayılarını loglar)
        ScenarioSystem.LogLoadCountsForAll();

        GD.Print("=========================================");
        GD.Print("[ScenarioTest] Tamamlandı — logları inceleyin");
        GD.Print("=========================================");
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
    }
}