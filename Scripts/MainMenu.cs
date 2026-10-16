using Godot;
using System;

// Ana menü: START (oyun sahnesine geç), SETTINGS (ayarlar paneli), QUIT (çıkış).
// Milestone 2 Görsel #6: yavaş gradyan arka plan, stratejik ızgara drifti, vinyet,
// stilize altın başlık + nabız animasyonları, alt köşe sürüm/senaryo.
public partial class MainMenu : Control
{
    private ColorRect _background;
    private MapGridNode _grid;

    public override void _Ready()
    {
        // Ortak koyu tema
        UITheme.ApplyTheme(this);

        // Sahne değişiminde pause kilitli kalabilir -> ana menü her zaman canlı açılır.
        GetTree().Paused = false;

        BuildBackground();
        StyleTitle();
        BuildFooter();

        // Butonları bağla (unique_name_in_owner sahnede işaretli)
        var startBtn = GetNode<Button>("%StartButton");
        var loadBtn = GetNode<Button>("%LoadButton");
        var libraryBtn = GetNode<Button>("%LibraryButton");
        var settingsBtn = GetNode<Button>("%SettingsButton");
        var quitBtn = GetNode<Button>("%QuitButton");

        startBtn.Pressed += OnStartPressed;
        loadBtn.Pressed += OnLoadPressed;
        libraryBtn.Pressed += OnLibraryPressed;
        settingsBtn.Pressed += OnSettingsPressed;
        quitBtn.Pressed += OnQuitPressed;

        // Hover scale efekti
        UITheme.AddHoverScale(startBtn);
        UITheme.AddHoverScale(loadBtn);
        UITheme.AddHoverScale(libraryBtn);
        UITheme.AddHoverScale(settingsBtn);
        UITheme.AddHoverScale(quitBtn);

        GD.Print("MainMenu: hazır (görsel #6 aktif)");

        // Test kancası: --library-test → Kütüphane panelini aç ve seçim simüle et.
        // Normal oyunda asla çalışmaz (yalnızca headless test argümanıyla).
        var args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "--library-test") >= 0)
        {
            GD.Print("[Test] --library-test: Kütüphane paneli otomatik açılıyor");
            CallDeferred(nameof(OpenLibraryForTest));
        }
    }

    // Headless test: paneli aç + ilk ülkeyi seçim simülasyonu (buton sinyali atlanmaz,
    // SelectCountry doğrudan çağrılır — sinyal, veri ve boyut zincirini loglar).
    private void OpenLibraryForTest()
    {
        var panel = LibraryPanel.OpenUnder(this);

        // _Ready'in çalışması için bir frame bekle, sonra 3. butona tıklama simüle et ve çık
        var tree = GetTree();
        var timer = new Godot.Timer { WaitTime = 0.2, OneShot = true, Autostart = true };
        AddChild(timer);
        timer.Timeout += () =>
        {
            if (panel != null)
            {
                panel.SimulateClickForTest(2); // 3. ülke (AFG, ARG, AUS -> AUS)
            }
            GD.Print("[Test] --library-test: 0.2s sonra çıkış");
            tree.Quit();
        };
    }

    private void BuildBackground()
    {
        _background = GetNode<ColorRect>("Background");

        // 1) Stratejik harita ızgarası (Node2D): altın/mavi ince çizgiler, yavaş yatay drift.
        // _Draw ile bir kez çizilir; drift sadece Position.X Tween'i → performans çok düşük.
        _grid = new MapGridNode();
        AddChild(_grid);
        MoveChild(_grid, 1); // Background'tan hemen sonra: her şeyin arkasında

        var drift = CreateTween().SetLoops(-1);
        drift.TweenProperty(_grid, "position:x", -MapGridNode.Cell, 40f); // lineer (ease yok) = sabit hız

        // 2) Vinyet: kenarlar karartılır, menü merkezde öne çıkar.
        var grad = new Gradient();
        grad.SetColor(0, new Color(0, 0, 0, 0f));
        grad.SetColor(1, new Color(0, 0, 0, 0.55f));
        var tex = new GradientTexture2D
        {
            Gradient = grad,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0f),
            Width = 64,
            Height = 64
        };
        var vignette = new TextureRect
        {
            Texture = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(vignette);
        MoveChild(vignette, 2); // ızgaranın üstünde, butonların altında

        // 3) Arka plan rengi çok yavaş iki lacivert tonu arasında nefes alır.
        var breathe = CreateTween().SetLoops(-1);
        breathe.TweenProperty(_background, "color", new Color("#13233B"), 14f)
              .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        breathe.TweenProperty(_background, "color", new Color("#0B1018"), 14f)
              .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
    }

    private void StyleTitle()
    {
        var title = GetNode<Label>("CenterContainer/VBoxContainer/TitleLabel");

        // Altın nefes efekti: parlaklık yavaşça artıp azalır.
        var pulse = CreateTween().SetLoops(-1);
        pulse.TweenProperty(title, "modulate", new Color(1.08f, 1.04f, 0.9f), 2.6f)
             .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        pulse.TweenProperty(title, "modulate", Colors.White, 2.6f)
             .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);

        // Ayraç çizgisi altın renkte hafif nabız atar.
        var sep = GetNode<Control>("CenterContainer/VBoxContainer/HSeparator");
        var goldLine = new StyleBoxFlat { BgColor = UITheme.Gold };
        goldLine.ContentMarginTop = 1;
        goldLine.ContentMarginBottom = 1;
        sep.AddThemeStyleboxOverride("separator", goldLine);

        var sepPulse = CreateTween().SetLoops(-1);
        sepPulse.TweenProperty(sep, "self_modulate", new Color(1, 1, 1, 0.35f), 3.2f)
                .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        sepPulse.TweenProperty(sep, "self_modulate", new Color(1, 1, 1, 1f), 3.2f)
                .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
    }

    private void BuildFooter()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(margin);

        var row = new HBoxContainer();
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        margin.AddChild(row);

        var version = new Label { Text = "Rogue World v0.2.0 — World Map Build" };
        version.AddThemeFontSizeOverride("font_size", 13);
        version.AddThemeColorOverride("font_color", UITheme.TextMuted);
        version.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        version.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(version);

        var scenario = new Label { Text = "Scenario: 1936" };
        scenario.AddThemeFontSizeOverride("font_size", 13);
        scenario.AddThemeColorOverride("font_color", UITheme.TextMuted);
        scenario.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(scenario);
    }

    private void OnStartPressed()
    {
        GD.Print("MainMenu: Senaryo seçim paneli açılıyor");
        // M17.1: START artık senaryo seçim panelini açar; senaryo seçilince
        // panel ResetForNewGame + game.tscn akışını kendisi yönetir.
        ScenarioSelectPanel.OpenUnder(this);
    }

    // M12: Kayıt yükleme — panel overlay olarak açılır; LOAD seçilince
    // PendingLoadSlot set edilir ve sahne game.tscn'e değişir.
    private void OnLoadPressed()
    {
        GD.Print("MainMenu: Kayıt yükleme paneli açılıyor");
        LoadGamePanel.OpenUnder(this, fromMainMenu: true);
    }

    // M15.2: Kütüphane paneli — ana menüden overlay olarak açılır
    private void OnLibraryPressed()
    {
        GD.Print("MainMenu: Kütüphane açılıyor");
        LibraryPanel.OpenUnder(this);
    }

    private void OnSettingsPressed()
    {
        GD.Print("MainMenu: Ayarlar açılıyor");
        // Panel overlay olarak açılır; BACK ile kapanınca menü yerinde kalır.
        SettingsPanel.OpenUnder(this);
    }

    private void OnQuitPressed()
    {
        GD.Print("MainMenu: Çıkış");
        GetTree().Quit();
    }
}

// Ana menü arka planı: stratejik harita ızgarası (enlem/boylam hissi).
// Node2D; çizgiler _Draw ile bir kez çizilir, drift sadece position değişimi.
public partial class MapGridNode : Node2D
{
    public const float Cell = 120f;      // küçük hücre (px)
    public const float MajorEvery = 4f;  // her 4. çizgi majör (480px)
    private const float Extent = 4200f;  // çizim yarıçapı: her pencere boyutunu örter

    private static readonly Color MinorColor = new Color(0.25f, 0.35f, 0.48f, 0.10f);
    private static readonly Color MajorColor = new Color(0.79f, 0.64f, 0.15f, 0.09f);

    public override void _Draw()
    {
        float major = Cell * MajorEvery;
        for (float x = -Extent; x <= Extent; x += Cell)
        {
            bool isMajor = Mathf.IsZeroApprox(Mathf.PosMod(x, major));
            DrawLine(new Vector2(x, -Extent), new Vector2(x, Extent),
                isMajor ? MajorColor : MinorColor, isMajor ? 1.5f : 1f);
        }
        for (float y = -Extent; y <= Extent; y += Cell)
        {
            bool isMajor = Mathf.IsZeroApprox(Mathf.PosMod(y, major));
            DrawLine(new Vector2(-Extent, y), new Vector2(Extent, y),
                isMajor ? MajorColor : MinorColor, isMajor ? 1.5f : 1f);
        }
    }
}