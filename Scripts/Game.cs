using Godot;
using System;

// Oyun sahnesinin kökü: harita çerçevesi, SubViewport → WorldMap, ülke bilgi paneli, HUD.
// M11: Harita TAM EKRAN (MapViewportFrame FullRect). HUD ve CountryInfoPanel overlay.
public partial class Game : Control
{
    private MapViewportFrame _mapFrame;
    private CountryInfoPanel _infoPanel;
    private CountryMiniCard _miniCard; // M15.1: kilitli oyundaki küçük ülke kartı
    private GameHud _hud;
    private MiniMapPanel _miniMap; // M32.4: sağ-alt köşede mini harita
    private WorldMap _worldMap; // X/ESC kapatma isteğinde ClearSelection() için
    private bool _isNewGame; // M24.1: yeni oyun mu yoksa yükleme mi?

    public const int HudHeight = 64; // HUD yüksekliği (görsel ölçüm) — info panel bundan sonra başlar

    public override void _Ready()
    {
        _mapFrame = new MapViewportFrame();
        _mapFrame.Ready += OnMapFrameReady;
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

    private void OnMapFrameReady()
    {
        // --- Merkez: SubViewport içinde WorldMap ---
        var subViewport = new SubViewport();
        // DÜZELTME (eski projeden öğrenildi): HandleInputLocally=true olmalı,
        // yoksa input SubViewport yerine parent'a gider.
        subViewport.HandleInputLocally = true;
        subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        // Stretch=true iken boyutu konteyner yönetir; başlangıçta geçerli bir
        // render hedefi ver (layout oturunca konteyner güncelleyecek).
        var winSize = GetViewportRect().Size;
        subViewport.Size = new Vector2I(System.Math.Max(64, (int)winSize.X), System.Math.Max(64, (int)winSize.Y));

        _worldMap = new WorldMap();
        subViewport.AddChild(_worldMap);
        _mapFrame.CenterViewport.AddChild(subViewport);

        // DÜZELTME (eski projeden öğrenildi): SubViewportContainer input'u
        // SADECE Stretch=true iken SubViewport'e iletir.
        var viewportContainer = _mapFrame.CenterViewport;
        // NOT: Stretch, MapViewportFrame._Ready'de true yapılıyor.

        // Layout'u scene oturunca bir kez daha uygula (pencere Game'i boyutlandıktan sonra)
        Callable.From(() =>
        {
            _mapFrame.EnforceLayout();

            var containerRect = viewportContainer.GetGlobalRect();
            GD.Print($"[Teşhis] SubViewport={subViewport.Size}, konteyner={containerRect}, " +
                     $"Stretch={viewportContainer.Stretch}, görünür={viewportContainer.IsVisibleInTree()}");
            _worldMap.EnsureCameraCurrent();
        }).CallDeferred();

        // --- Sol panel: ülke bilgi paneli (overlay — HUD altında, genişlik sınırlı) ---
        _infoPanel = new CountryInfoPanel();
        AddChild(_infoPanel); // Game köküne overlay olarak ekle (harita üstünde)

        // X / ESC: panel kapatma isteği → harita seçimini de kaldır (tek yol: Deselected)
        _infoPanel.CloseRequested += () =>
        {
            GD.Print("[Diag] Game: CloseRequested → WorldMap.ClearSelection()");
            _worldMap?.ClearSelection();
        };

        // --- M15.1: Küçük ülke kartı (kilitli oyunda tam panel yerine) ---
        _miniCard = new CountryMiniCard();
        AddChild(_miniCard);
        _miniCard.CloseRequested += () =>
        {
            GD.Print("[Diag] Game: MiniCard CloseRequested → WorldMap.ClearSelection()");
            _worldMap?.ClearSelection();
        };

        // --- Üst bar: HUD (CanvasLayer overlay) ---
        _hud = new GameHud();
        AddChild(_hud);

        // --- M32.4: Mini harita (her zaman görünür, sağ-alt köşe) ---
        _miniMap = new MiniMapPanel();
        AddChild(_miniMap);

        // --- Sinyaller ---
        _worldMap.CountrySelected += OnCountrySelected;
        _worldMap.CountryDeselected += OnCountryDeselected;

        // --- Render teşhisi: --mapshot=<yol> ile 30. karede PNG kaydet ve çık ---
        // Kullanım: godot --path . res://Scenes/game.tscn -- --mapshot=C:/g/shot.png
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--mapshot="))
                SetupMapshot(arg.Substring("--mapshot=".Length), subViewport, viewportContainer);

        GD.Print("Game: harita + paneller hazır");
    }

    private void OnCountrySelected(string countryId, string countryName)
    {
        var data = CountryDatabase.GetOrCreate(countryId, countryName);

        // M24.1: Yeni oyunda ilk ülke seçimi sonrası karakter oluşturma paneli açılır.
        // Yüklü oyunda karakter zaten kaytta mevcut → panel açılmaz.
        if (GameManager.IsAvailable && !GameManager.Instance.HasPlayer && _isNewGame)
        {
            GameManager.Instance.PlayerCountryChanged += OnPlayerCountryLockedForCharacter;
        }

        // M15.1: Oyuncu ülkesi kilitlendiyse tam panel YOK — yalnızca küçük kart
        // (ad, bayrak, ilişki, savaş durumu). Seçim aşamasında davranış aynı: tam panel.
        if (GameManager.IsAvailable && GameManager.Instance.IsPlayerCountryLocked)
        {
            _infoPanel.HidePanel();
            _miniCard?.ShowCountry(data, countryName);
        }
        else
        {
            _miniCard?.HideCard();
            _infoPanel.ShowCountry(data, countryName);
        }
        // Alt çubuğa da seçimi yaz
        _hud?.OnCountrySelected(countryName);
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
    }

    private void OnCountryDeselected()
    {
        GD.Print("[Diag] Game: CountryDeselected → HidePanel");
        _infoPanel.HidePanel();
        _miniCard?.HideCard();
        _hud?.OnCountryDeselected();
    }

    private void OnDayChangedForDebug(long totalDays)
    {
        if (totalDays <= 10)
            GD.Print($"[TICK] Gün {totalDays} — {GameManager.Instance.GetDateText()}");
        else if (GameManager.IsAvailable)
            GameManager.Instance.DayChanged -= OnDayChangedForDebug; // log spam'i kes
    }

    // 30. karede SubViewport + ana pencere görüntüsünü PNG olarak kaydet.
    // İkisini karşılaştırınca sorun nerede anlaşılır:
    //  - SubViewport'ta harita var, pencerede gri → konteyner compositing sorunu
    //  - İkisi de gri → SubViewport içi (kamera/ülke) sorunu
    private void SetupMapshot(string basePath, SubViewport subViewport, SubViewportContainer container)
    {
        int frame = 0;
        var tree = GetTree();
        tree.ProcessFrame += () =>
        {
            frame++;
            if (frame != 30) return;

            var containerRect = container.GetGlobalRect();
            GD.Print($"[Mapshot] Kare 30: SubViewport={subViewport.Size}, konteyner={containerRect}");

            try
            {
                var sub = subViewport.GetTexture().GetImage();
                sub.SavePng(basePath + "_sub.png");
                GD.Print($"[Mapshot] SubViewport: {basePath}_sub.png ({sub.GetWidth()}x{sub.GetHeight()})");

                var win = GetViewport().GetTexture().GetImage();
                win.SavePng(basePath + "_win.png");
                GD.Print($"[Mapshot] Pencere: {basePath}_win.png ({win.GetWidth()}x{win.GetHeight()})");
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"[Mapshot] HATA: {e.Message}");
            }
            tree.Quit();
        };
    }
}
