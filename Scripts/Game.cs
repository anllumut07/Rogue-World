using Godot;

// Oyun sahnesinin kökü: harita çerçevesi, SubViewport → WorldMap, ülke bilgi paneli, HUD.
public partial class Game : Control
{
    private MapViewportFrame _mapFrame;
    private CountryInfoPanel _infoPanel;
    private GameHud _hud;

    public override void _Ready()
    {
        _mapFrame = new MapViewportFrame();
        _mapFrame.Ready += OnMapFrameReady;
        AddChild(_mapFrame);

        // İlk 10 günün tick'ini logla (test/doğrulama amaçlı)
        GameManager.Instance.DayChanged += OnDayChangedForDebug;

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

        var worldMap = new WorldMap();
        subViewport.AddChild(worldMap);
        _mapFrame.CenterViewport.AddChild(subViewport);

        // DÜZELTME (eski projeden öğrenildi): SubViewportContainer input'u
        // SADECE Stretch=true iken SubViewport'e iletir.
        // NOT: Stretch=true iken SubViewport boyutunu konteyner otomatik yönetir —
        // manuel Size setleme engellenir (uyarı üretir).
        var viewportContainer = _mapFrame.CenterViewport;
        viewportContainer.Stretch = true;

        // Layout'u scene oturunca bir kez daha uygula (pencere Game'i boyutlandıktan sonra)
        Callable.From(() =>
        {
            _mapFrame.EnforceLayout();

            var containerRect = viewportContainer.GetGlobalRect();
            GD.Print($"[Teşhis] SubViewport={subViewport.Size}, konteyner={containerRect}, " +
                     $"Stretch={viewportContainer.Stretch}, görünür={viewportContainer.IsVisibleInTree()}");
            worldMap.EnsureCameraCurrent();
        }).CallDeferred();

        // --- Sol panel: ülke bilgi paneli ---
        _infoPanel = new CountryInfoPanel();
        _mapFrame.LeftStack.AddChild(_infoPanel);

        // --- Üst bar: HUD ---
        _hud = new GameHud();
        AddChild(_hud);

        // --- Sinyaller ---
        worldMap.CountrySelected += OnCountrySelected;
        worldMap.CountryDeselected += OnCountryDeselected;

        // --- Render teşhisi: --mapshot=<yol> ile 30. karede PNG kaydet ve çık ---
        // Kullanım: godot --path . res://Scenes/game.tscn -- --mapshot=C:/g/shot.png
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--mapshot="))
                SetupMapshot(arg.Substring("--mapshot=".Length), subViewport, viewportContainer);

        GD.Print("Game: harita + paneller hazır");
    }

    private void OnCountrySelected(string countryId, string countryName)
    {
        _infoPanel.ShowCountry(CountryDatabase.GetOrCreate(countryId, countryName), countryName);
    }

    private void OnCountryDeselected()
    {
        _infoPanel.HidePanel();
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
