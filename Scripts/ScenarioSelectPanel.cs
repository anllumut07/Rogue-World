using Godot;
using System;

/// <summary>
/// Senaryo seçim paneli (M17.1).
/// - MainMenu START basılınca açılır: oyun sahnesine geçmeden önce senaryo seçilir.
/// - Senaryo kartı: ad, açıklama, başlangıç tarihi, placeholder uyarısı.
/// - Seçince: ScenarioSystem.SetActive(id) + GameManager.ResetForNewGame() + game.tscn.
/// - 1152x648'e sığar (~600x520), backdrop MouseFilter.Stop, ESC/BACK kapatır.
/// - ULKE SEÇİMİ aynı sistemle devam eder (game.tscn'de haritadan ülke seçilir).
/// </summary>
public partial class ScenarioSelectPanel : CanvasLayer
{
    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private VBoxContainer _scenarioList;
    private Button _startBtn;
    private Button _backBtn;

    private string _selectedId = "";
    private System.Collections.Generic.Dictionary<string, Button> _buttonsById = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 100;

        BuildUi();

        IsOpen = true;

        // Açılış animasyonu: backdrop fade + panel pop-in
        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        RefreshScenarios();

        GD.Print("[ScenarioSelectPanel] hazır — senaryo seçimi bekleniyor");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    // ESC: paneli kapat (ana menüye dön)
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            ClosePanel();
        }
    }

    // Overlay olarak açar (SettingsPanel/LoadGamePanel.OpenUnder ile aynı desen)
    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[ScenarioSelectPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");
        var panel = new ScenarioSelectPanel();
        parent.AddChild(panel);
    }

    private void BuildUi()
    {
        // Backdrop: ekranı kaplar, fare girdilerini yakalar
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.75f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop; // KRİTİK: arka plan girdisi bloklansın
        AddChild(_backdrop);

        UITheme.ApplyTheme(_backdrop);

        // CenterContainer -> Panel
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer();
        _panel.CustomMinimumSize = new Vector2(600, 520); // 1152x648'e sığar
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 30);
        margin.AddThemeConstantOverride("margin_right", 30);
        margin.AddThemeConstantOverride("margin_top", 25);
        margin.AddThemeConstantOverride("margin_bottom", 25);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        margin.AddChild(vbox);

        // Başlık
        var title = new Label();
        title.Text = "SELECT SCENARIO";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 34);
        vbox.AddChild(title);

        // Açıklama
        var hint = new Label();
        hint.Text = "Each scenario has its own start date, country data, focus trees, events and decisions. After starting, pick your country on the map as usual.";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeColorOverride("font_color", UITheme.TextMuted);
        hint.AddThemeFontSizeOverride("font_size", 14);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hint);

        // Senaryo listesi
        _scenarioList = new VBoxContainer();
        _scenarioList.AddThemeConstantOverride("separation", 10);
        _scenarioList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(_scenarioList);

        // Buton satırı: START / BACK
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(btnRow);

        _startBtn = new Button
        {
            Text = "START GAME",
            FocusMode = Control.FocusModeEnum.None,
            Disabled = true // senaryo seçilene kadar devre dışı
        };
        _startBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _startBtn.Pressed += OnStartPressed;
        btnRow.AddChild(_startBtn);
        UITheme.AddHoverScale(_startBtn);

        _backBtn = new Button { Text = "BACK", FocusMode = Control.FocusModeEnum.None };
        _backBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _backBtn.Pressed += OnBackPressed;
        btnRow.AddChild(_backBtn);
        UITheme.AddHoverScale(_backBtn);
    }

    private void RefreshScenarios()
    {
        // Eski kartları temizle
        foreach (var child in _scenarioList.GetChildren())
            child.QueueFree();
        _buttonsById.Clear();

        var scenarios = ScenarioSystem.All;
        if (scenarios.Count == 0)
        {
            var err = new Label
            {
                Text = "No scenarios found!\n(Data/scenarios/scenarios.json missing or empty)",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            err.AddThemeFontSizeOverride("font_size", 16);
            err.AddThemeColorOverride("font_color", new Color(0.95f, 0.35f, 0.3f));
            _scenarioList.AddChild(err);
            return;
        }

        foreach (var s in scenarios)
            AddScenarioCard(s);
    }

    private void AddScenarioCard(ScenarioSystem.Scenario s)
    {
        var btn = new Button { FocusMode = Control.FocusModeEnum.None };
        btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btn.CustomMinimumSize = new Vector2(0, 100);
        btn.Alignment = HorizontalAlignment.Left;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 3);
        btn.AddChild(vbox);

        var nameRow = new HBoxContainer();
        vbox.AddChild(nameRow);

        var nameLabel = new Label { Text = s.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", 20);
        nameRow.AddChild(nameLabel);

        // Placeholder uyarısı (turuncu — içerik henüz hazır değil)
        if (s.Placeholder)
        {
            var phLabel = new Label { Text = "PLACEHOLDER" };
            phLabel.AddThemeFontSizeOverride("font_size", 13);
            phLabel.AddThemeColorOverride("font_color", Colors.Orange);
            nameRow.AddChild(phLabel);
        }

        var dateLabel = new Label { Text = $"Start Date: {s.StartDate}" };
        dateLabel.AddThemeFontSizeOverride("font_size", 13);
        dateLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        vbox.AddChild(dateLabel);

        var descLabel = new Label { Text = s.Description };
        descLabel.AddThemeFontSizeOverride("font_size", 13);
        descLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        descLabel.CustomMinimumSize = new Vector2(520, 0);
        vbox.AddChild(descLabel);

        // İlk senaryo (listede modern_world) önseçili
        btn.Pressed += () => SelectScenario(s.Id, btn);
        _buttonsById[s.Id] = btn;
        _scenarioList.AddChild(btn);

        if (string.IsNullOrEmpty(_selectedId))
            SelectScenario(s.Id, btn);
    }

    private void SelectScenario(string id, Button sender)
    {
        // Önceki seçimi resetle
        foreach (var b in _buttonsById.Values)
            b.SelfModulate = Colors.White;

        _selectedId = id;
        if (sender != null)
            sender.SelfModulate = UITheme.Gold;

        _startBtn.Disabled = false;
        GD.Print($"[ScenarioSelectPanel] Seçili senaryo: {id}");
    }

    private void OnStartPressed()
    {
        if (string.IsNullOrEmpty(_selectedId)) return;

        GD.Print($"[ScenarioSelectPanel] START GAME — senaryo: {_selectedId}");

        // 1) Senaryoyu aktifleştir (tüm veri yükleyiciler yeni yolları kullanacak)
        if (!ScenarioSystem.SetActive(_selectedId))
        {
            GD.PrintErr($"[ScenarioSelectPanel] Senaryo aktifleştirilemedi: {_selectedId}");
            return;
        }

        // 2) Yeni oyun: oturum durumunu sıfırla (tarih senaryo başlangıcına ayarlanır)
        if (GameManager.IsAvailable)
            GameManager.Instance.ResetForNewGame();

        // 3) Oyun sahnesine geç — ülke seçimi haritada aynı akışla devam eder
        ClosePanel();
        GetTree().ChangeSceneToFile("res://Scenes/game.tscn");
    }

    private void OnBackPressed()
    {
        GD.Print("[ScenarioSelectPanel] BACK — ana menüye dönülüyor");
        ClosePanel();
    }

    private void ClosePanel()
    {
        // Kapanış animasyonu
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () =>
        {
            _backdrop.Hide();
            _panel.Hide();
            IsOpen = false;
            QueueFree();
        });
    }
}