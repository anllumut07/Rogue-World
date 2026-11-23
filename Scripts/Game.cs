using Godot;
using System;
using System.Threading.Tasks;

// Oyun sahnesinin kökü: harita çerçevesi, SubViewport → WorldMapHoI4 (Map v6), ülke bilgi paneli, HUD.
// M11: Harita TAM EKRAN (MapViewportFrame FullRect). HUD ve CountryInfoPanel overlay.
// WorldMapHoI4: HoI4 Province Bitmap Map v6 (16,290 provinces, 5632x2048)
public partial class Game : Control
{
    private MapViewportFrame _mapFrame;
    private CountryInfoPanel _infoPanel;
    private CountryMiniCard _miniCard; // M15.1: kilitli oyundaki küçük ülke kartı
    private GameHud _hud;
    private MiniMapPanel _miniMap; // M32.4: sağ-alt köşede mini harita
    private Node2D _worldMap; // WorldMapHoI4 (Map v6) - X/ESC kapatma isteğinde ClearSelection() için
    private bool _isNewGame; // M24.1: yeni oyun mu yoksa yükleme mi?

    public const int HudHeight = 64; // HUD yüksekliği (görsel ölçüm) — info panel bundan sonra başlar

    public override void _Ready()
        {
            _mapFrame = new MapViewportFrame();
            _mapFrame.MapFrameReady += OnMapFrameReady;
            AddChild(_mapFrame);

        // M12: Ana menüden LOAD seçildiyse — pending slot'u yükle.
        // PendingLoadSlot: -1=yok, 0=auto-save, 1-10=slot.
        _isNewGame = SaveSystem.PendingLoadSlot < 0; // M24.1: yeni oyun mu?
        if (SaveSystem.PendingLoadSlot >= 0)
        {
            int slot = SaveSystem.PendingLoadSlot;
            SaveSystem.PendingLoadSlot = -1; // temizle (tek seferlik)
            bool ok = slot == 0 ? SaveSystem.LoadAutoSave() : SaveSystem.LoadGame(slot);
            if (!ok)
            {
                GD.PrintErr($"[Game] Ana menüden yükleme başarısız (slot={slot}), yeni oyun gibi devam ediliyor");
            }
            else
            {
                GD.Print($"[Game] Ana menüden yükleme başarılı (slot={slot}) — oyun pause'lu başlıyor");
            }
        }

        // İlk 10 günün tick'ini logla (test/doğrulama amaçlı)
        GameManager.Instance.DayChanged += OnDayChangedForDebug;

        // M-Flags: bayrak raporu — PNG bulunan / yedek kullanan ülkeler (bir kez)
        FlagLibrary.LogReportOnce();

        GD.Print("Game: hazır");
    }

    public override void _ExitTree()
    {
        if (!GameManager.IsAvailable) return;
        GameManager.Instance.DayChanged -= OnDayChangedForDebug;
    }

    private async void OnMapFrameReady()
    {
        // Show loading screen while initializing map systems
        var loadingScreen = new LoadingScreen();
        AddChild(loadingScreen);
        loadingScreen.Show(4);

        GD.Print("Game: Initializing MapView (New map system with pixel-perfect hit-test)...");
        loadingScreen.UpdateProgress("Loading map data...");

        // Create MapView (new map system with pixel-perfect hit-test via ID map)
        var mapView = new MapView();
        _worldMap = mapView;
        _mapFrame.CenterViewport.AddChild(mapView);
        loadingScreen.UpdateProgress("Map data loaded", 1);

        // Initialize MapView (loads ID map, country data, map texture)
        await mapView.InitializeAsync();
        loadingScreen.UpdateProgress("MapView ready", 2);

        // Create WorldMap compatibility layer (data provider, not visual)
        var worldMapCompat = new WorldMap();
        AddChild(worldMapCompat);

        // Layout'u scene oturunca bir kez daha uygula (pencere Game'i boyutlandıktan sonra)
        Callable.From(() =>
        {
            _mapFrame.EnforceLayout();

            var viewportContainer = _mapFrame.CenterViewport;
            var containerRect = viewportContainer.GetGlobalRect();
            GD.Print($"[Teşhis] MapView={mapView.GetMapSize()}, konteyner={containerRect}, " +
                     $"Stretch={viewportContainer.Stretch}, görünür={viewportContainer.IsVisibleInTree()}");

            // Provide camera to WorldMap compatibility layer
            if (mapView.GetCamera() != null)
            {
                var wmCompat = WorldMap.Instance;
                if (wmCompat != null)
                    wmCompat._camera = mapView.GetCamera();
            }
        }).CallDeferred();

        loadingScreen.UpdateProgress("Setting up UI panels...", 3);

        // --- Sol panel: ülke bilgi paneli (overlay — HUD altında, genişlik sınırlı) ---
        _infoPanel = new CountryInfoPanel();
        AddChild(_infoPanel); // Game köküne overlay olarak ekle (harita üstünde)

        // X / ESC: panel kapatma isteği → harita seçimini de kaldır
        _infoPanel.CloseRequested += () =>
        {
            GD.Print("[Diag] Game: CloseRequested → MapView.ClearSelection()");
            _worldMap?.Call("ClearSelection");
        };

        // --- M15.1: Küçük ülke kartı (kilitli oyunda tam panel yerine) ---
        _miniCard = new CountryMiniCard();
        AddChild(_miniCard);
        _miniCard.CloseRequested += () =>
        {
            GD.Print("[Diag] Game: MiniCard CloseRequested → MapView.ClearSelection()");
            _worldMap?.Call("ClearSelection");
        };

        // --- Üst bar: HUD (CanvasLayer overlay) ---
        _hud = new GameHud();
        AddChild(_hud);

        // Update MapView with actual HUD height for camera vertical clamp
        Callable.From(() =>
        {
            float hudHeight = _hud.TopBarHeight;
            mapView.SetHudTopHeight(hudHeight);
        }).CallDeferred();

        // --- M32.4: Mini harita (her zaman görünür, sağ-alt köşe) ---
        _miniMap = new MiniMapPanel();
        AddChild(_miniMap);

        // --- Sinyaller (MapView has CountrySelected/CountryHovered/CountryDeselected signals) ---
        _worldMap.Connect("CountrySelected", new Callable(this, nameof(OnCountrySelected)));
        _worldMap.Connect("CountryHovered", new Callable(this, nameof(OnCountryHovered)));
        _worldMap.Connect("CountryDeselected", new Callable(this, nameof(OnCountryDeselected)));

        // --- Diagnostik: --screenshot-test (yalnızca komut satırı) ---
        if (ScreenshotTest.Enabled) SetupScreenshotShot();

        // M13.1: Oyun müziği (prosedürel, döngü).
        AudioSystem.PlayGameMusic();

        // --- Render teşhisi: --mapshot=<yol> ile 30. karede PNG kaydet ve çık ---
        // Kullanım: godot --path . res://Scenes/game.tscn -- --mapshot=C:/g/shot.png
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--mapshot="))
                SetupMapshot(arg.Substring("--mapshot=".Length), null, _mapFrame.CenterViewport);

        loadingScreen.UpdateProgress("All systems ready", 4);
        await Task.Delay(500);
        loadingScreen.HideScreen();
        loadingScreen.QueueFree();

        GD.Print("Game: harita + paneller hazır");
    }

        private void SetupCountrySelectTest(WorldMapV5 mapV5Instance)
    {
        GD.Print("=== Country Selection Test Started (Sync Mode) ===");

        var testCases = new[]
        {
            ("Turkey", 35.0, 39.0),
            ("Indonesia", 120.0, -5.0),
            ("Germany", 10.0, 51.0),
            ("Brazil", -50.0, -15.0),
            ("Egypt", 31.0, 26.0),
            ("Japan", 139.0, 35.0),
            ("Ocean", -100.0, 0.0),
        };

        // Store test state for sync execution in _Process
        mapV5Instance.TestMode = true;
        mapV5Instance.TestCases = testCases;
        mapV5Instance.TestIndex = 0;
        mapV5Instance.TestPhase = 0; // 0=send click, 1=check result, 2=deselect, 3=delay
        mapV5Instance.TestTimer = 0f;

        GD.Print("[Test] Test mode enabled, will run in _Process");
    }

    private void CaptureTestScreenshot(WorldMapV5 mapV5Instance, string testName)
    {
        // Capture screenshot for this test
        var viewport = mapV5Instance.GetViewport();
        var tex = viewport.GetTexture();
        var img = tex.GetImage();
        if (img != null && img.GetWidth() > 100 && img.GetHeight() > 100)
        {
            var dir = DirAccess.Open("user://");
            if (dir != null)
            {
                dir.MakeDirRecursive("test-logs/v5");
            }
            var path = $"user://test-logs/v5/sel_{testName}.png";
            img.SavePng(path);
            GD.Print($"[Test] Screenshot saved: {path}");
        }
    }

    private void OnCountrySelected(string countryId, string countryName)
    {
        if (string.IsNullOrEmpty(countryId))
        {
            GD.Print($"[Game] No country selected");
            OnCountryDeselected();
            return;
        }

        var data = CountryDatabase.GetOrCreate(countryId, "");
        if (data == null)
        {
            GD.PrintErr($"[Game] Country data not found for {countryId}");
            return;
        }

        // M24.1: Yeni oyunda ilk ülke seçimi sonrası karakter oluşturma paneli açılır.
        // Yüklü oyunda karakter zaten kayıtta mevcut → panel açılmaz.
        if (GameManager.IsAvailable && !GameManager.Instance.HasPlayer && _isNewGame)
        {
            GameManager.Instance.PlayerCountryChanged += OnPlayerCountryLockedForCharacter;
        }

        // M15.1: Oyuncu ülkesi kilitlendiyse tam panel YOK — yalnızca küçük kart
        // (ad, bayrak, ilişki, savaş durumu). Seçim aşamasında davranış aynı: tam panel.
        if (GameManager.IsAvailable && GameManager.Instance.IsPlayerCountryLocked)
        {
            _infoPanel.HidePanel();
            _miniCard?.ShowCountry(data, data.Name);
        }
        else
        {
            _miniCard?.HideCard();
            _infoPanel.ShowCountry(data, data.Name);
        }
        // Alt çubuğa da seçimi yaz
        _hud?.OnCountrySelected(data.Name);
    }

    private void OnCountryHovered(string countryId, string countryName)
    {
        // Hover handling - could show tooltip in HUD
        // _hud?.OnCountryHovered(countryId, countryName);
    }

    private void OnCountryDeselected()
    {
        GD.Print("[Diag] Game: CountryDeselected → HidePanel");
        _infoPanel.HidePanel();
        _miniCard?.HideCard();
        _hud?.OnCountryDeselected();
    }

    // Legacy handler - remove after all references updated
    private void OnProvinceHovered(int provinceId, string countryTag)
    {
        // Legacy - MapView uses CountryHovered
    }

    private void OnProvinceDeselected()
    {
        // Legacy - MapView uses CountryDeselected
        OnCountryDeselected();
    }

    // M24.1: SetPlayerCountry kilidi açıklandıktan sonra karakter panelsini aç (yeni oyun).
    private void OnPlayerCountryLockedForCharacter(string countryId)
    {
        if (GameManager.IsAvailable && GameManager.Instance.IsPlayerCountryLocked && !GameManager.Instance.HasCharacter)
        {
            GameManager.Instance.PlayerCountryChanged -= OnPlayerCountryLockedForCharacter;
            // M24.3: Başlangıç yolu seçimi (Sandbox / Career) önce karakter paneline gelmeden önce.
            ShowStartPathChoice();
        }
    }

    // M24.3: Başlangıç yolu seçimi — Sandbox (doğrudan lider) veya Career (kariyer yolu, M25).
    // Her iki yol da karakter oluşturma panele kadar gider; sadece GameManager.PlayerStartPath farklıdır.
    private void ShowStartPathChoice()
    {
        var choice = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        choice.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.5f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        choice.AddChild(backdrop);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(600, 320), MouseFilter = Control.MouseFilterEnum.Stop };
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        UITheme.ApplyTheme(panel);
        choice.AddChild(panel);

        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 16);
        vbox.AddThemeConstantOverride("margin_top", 24);
        vbox.AddThemeConstantOverride("margin_bottom", 24);
        vbox.AddThemeConstantOverride("margin_left", 32);
        vbox.AddThemeConstantOverride("margin_right", 32);
        panel.AddChild(vbox);

        var title = new Label { Text = "STARTING PATH", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        vbox.AddChild(title);

        var btnSandbox = new Button { Text = "Start as Leader (Sandbox)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        btnSandbox.Pressed += () =>
        {
            GameManager.Instance.SetStartPath(GameManager.StartPath.Sandbox);
            choice.QueueFree();
            CharacterCreationPanel.OpenUnder(this, OnCharacterConfirmed);
        };
        UITheme.AddHoverScale(btnSandbox);
        vbox.AddChild(btnSandbox);

        var btnCareer = new Button { Text = "Start Career Path", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        btnCareer.Pressed += () =>
        {
            GameManager.Instance.SetStartPath(GameManager.StartPath.Career);
            choice.QueueFree();
            CharacterCreationPanel.OpenUnder(this, OnCharacterConfirmed);
        };
        UITheme.AddHoverScale(btnCareer);
        vbox.AddChild(btnCareer);

        AddChild(choice);
    }

    private void OnCharacterConfirmed(Character character)
    {
        GameManager.Instance.SetCharacter(character);
        GD.Print($"[Game] Karakter onaylandı ve oyuncu olarak ayarlandı: {character.FullName}");

        // M36.2: İlk açılış rehberini göster (daha önce görülmediyse).
        if (!SettingsSystem.TutorialSeen)
            TutorialPanel.OpenUnder(this);
    }

    private void OnDayChangedForDebug(long totalDays)
    {
        if (totalDays <= 10)
            GD.Print($"[TICK] Gün {totalDays} — {GameManager.Instance.GetDateText()}");
        else if (GameManager.IsAvailable)
            GameManager.Instance.DayChanged -= OnDayChangedForDebug; // log spam'i kes
    }

    // Diagnostik (yalnızca --screenshot-test): hedef sahne/panel açılır, kareler
    // işlendikten sonra PNG kaydedilir ve süreç kapanır. Normal oyun bu yolu kullanmaz.
    private void SetupScreenshotShot()
    {
        switch (ScreenshotTest.Target)
        {
            case ScreenshotTest.ShotTarget.CharacterCreation:
                CharacterCreationPanel.OpenUnder(this, OnCharacterConfirmed);
                break;
            case ScreenshotTest.ShotTarget.StartPath:
                ShowStartPathChoice();
                break;
            case ScreenshotTest.ShotTarget.Cabinet:
                CabinetPanel.OpenUnder(this);
                break;
            case ScreenshotTest.ShotTarget.Statistics:
                StatisticsPanel.OpenUnder(this);
                break;
            case ScreenshotTest.ShotTarget.Election:
                ElectionPanel.OpenUnder(this);
                break;
            case ScreenshotTest.ShotTarget.Organizations:
                OrganizationPanel.OpenUnder(this);
                break;
            case ScreenshotTest.ShotTarget.DecisionPanel:
                DecisionPanel.OpenUnder(this);
                break;
        }

        int frame = 0;
        GetTree().ProcessFrame += () =>
        {
            frame++;
            if (frame < 60) return; // harita + panel açılış animasyonu kadar bekle
            try
            {
                var img = GetViewport().GetTexture().GetImage();
                var rel = ScreenshotTest.OutputPath();
                var abs = System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://test-logs/screens"),
                                                 System.IO.Path.GetFileName(rel));
                var dir = System.IO.Path.GetDirectoryName(abs);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                img.SavePng(abs);
                GD.Print($"[ScreenshotTest] KAYDEDİLDİ: {rel} ({img.GetWidth()}x{img.GetHeight()})");
                GD.Print($"[ScreenshotTest] SONUÇ: PASS ✓");
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"[ScreenshotTest] HATA: {e.Message}");
                GD.Print($"[ScreenshotTest] SONUÇ: FAIL ✗");
            }
            GetTree().Quit(0);
        };
    }

    // 30. karede SubViewport + ana pencere görüntüsünü PNG olarak kaydet.
    // İkisini karşılaştırınca sorun nerede anlaşılır:
    //  - SubViewport'ta harita var, pencerede gri → konteyner compositing sorunu
    //  - İkisi de gri → SubViewport içi (kamera/ülke) sorunu
    private void SetupMapshot(string basePath, SubViewport subViewport, SubViewportContainer container)
    {
        // Set explicit size for headless mode
        subViewport.Size = new Vector2I(1152, 648);

        int frame = 0;
        var tree = GetTree();
        tree.ProcessFrame += () =>
        {
            frame++;
            // Wait longer for layout to settle in headless mode
            if (frame < 60) return;

            var containerRect = container.GetGlobalRect();
            GD.Print($"[Mapshot] Kare {frame}: SubViewport={subViewport.Size}, konteyner={containerRect}");

            try
            {
                var sub = subViewport.GetTexture().GetImage();
                if (sub != null && sub.GetWidth() > 64 && sub.GetHeight() > 64)
                {
                    sub.SavePng(basePath + "_sub.png");
                    GD.Print($"[Mapshot] SubViewport: {basePath}_sub.png ({sub.GetWidth()}x{sub.GetHeight()})");

                    var win = GetViewport().GetTexture().GetImage();
                    win.SavePng(basePath + "_win.png");
                    GD.Print($"[Mapshot] Pencere: {basePath}_win.png ({win.GetWidth()}x{win.GetHeight()})");
                }
                else
                {
                    GD.PrintErr($"[Mapshot] SubViewport still too small: {sub?.GetWidth()}x{sub?.GetHeight()}, waiting...");
                    return; // Keep waiting
                }
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"[Mapshot] HATA: {e.Message}");
            }
            tree.Quit();
        };
    }

    // WorldMapV5 test modu: --v5-shot=<name> ile ekran görüntüsü al ve çık
    // Pencere modunda çalışır (headless'ta SubViewport 64x64 dummy verir)
    // Pencere boyutunu kodla ayarlar, FramePostDraw bekler, test-logs/v5/ altına kaydeder
    private void SetupV5Shot(string shotName, SubViewport subViewport, SubViewportContainer container)
    {
        // Ensure test-logs/v5 directory exists
        var dir = DirAccess.Open("user://");
        if (dir != null)
        {
            dir.MakeDirRecursive("test-logs/v5");
        }

        int frame = 0;
        int captureFrame = -1;
        var tree = GetTree();
        tree.ProcessFrame += () =>
        {
            frame++;
            // Wait for layout and initial render (at least 15 frames)
            if (frame < 15) return;

            // Schedule capture for next frame (after FramePostDraw)
            if (captureFrame == -1)
            {
                captureFrame = frame + 1;
                return;
            }

            // Capture on the scheduled frame
            if (frame == captureFrame)
            {
                try
                {
                    var viewport = GetViewport();
                    var tex = viewport.GetTexture();
                    var img = tex.GetImage();

                    if (img != null && img.GetWidth() > 100 && img.GetHeight() > 100)
                    {
                        var path = $"user://test-logs/v5/{shotName}.png";
                        var absPath = ProjectSettings.GlobalizePath(path);
                        img.SavePng(path);
                        GD.Print($"[V5Shot] Saved: {path} -> {absPath} ({img.GetWidth()}x{img.GetHeight()})");
                        GD.Print($"[V5Shot] SONUÇ: PASS ✓");

                        // Also log camera state if WorldMapV5
                        if (_worldMap is WorldMapV5 wmv5)
                        {
                            wmv5.LogCameraState(shotName);
                        }
                    }
                    else
                    {
                        GD.PrintErr($"[V5Shot] Image too small: {img?.GetWidth()}x{img?.GetHeight()}");
                        GD.Print($"[V5Shot] SONUÇ: FAIL ✗");
                    }
                }
                catch (System.Exception e)
                {
                    GD.PrintErr($"[V5Shot] ERROR: {e.Message}");
                    GD.Print($"[V5Shot] SONUÇ: FAIL ✗");
                }

                tree.Quit();
            }
        };
    }
}
