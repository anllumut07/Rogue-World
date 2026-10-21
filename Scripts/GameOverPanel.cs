using Godot;
using System;
using System.Linq;

/// <summary>
/// Oyun Sonu Paneli (M41.4).
/// Zafer koşulu tamamlandığında açılır: zafer tipi, geçen süre, skor dökümü,
/// ilk 10 sıralama ve MAIN MENU / CONTINUE butonları.
/// - 1152x648'e sığar (640x480), backdrop MouseFilter.Stop (harita girdisi bloklanır).
/// - ESC kapatır (CONTINUE ile aynı etki — oyuncu zafer sonrası oynamaya devam edebilir).
/// - Panel sahneden bağımsız yok edilir; durum (IsOpen) yalnızca oturum ömrü boyunca tutulur.
/// </summary>
public partial class GameOverPanel : CanvasLayer
{
    private const string MainMenuScenePath = "res://Scenes/main_menu.tscn";

    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private Label _titleLabel;
    private Label _subtitleLabel;
    private Label _conditionLabel;
    private Label _durationLabel;
    private Label _scoreLabel;
    private Label _rankingLabel;
    private Button _mainMenuButton;

    private const float FadeDuration = 0.10f;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 110; // PauseMenu(100) ve rapor panellerinin üstünde — oyun sonu her şeyi kapatır
        BuildUi();
        IsOpen = true;

        UITheme.FadeIn(_backdrop, FadeDuration);
        UITheme.PopIn(_panel, FadeDuration + 0.02f);
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Oyun sonu panelini açar. Zaten açıksa null döner.
    /// </summary>
    public static GameOverPanel OpenUnder(Node parent, bool victory, string conditionLabel,
        string conditionDescription, string countryName)
    {
        if (IsOpen) return null;

        GD.Print($"[GameOverPanel] Açılıyor — victory={victory}, ülke={countryName}, koşul={conditionLabel}");
        var panel = new GameOverPanel();
        panel._pending = (victory, conditionLabel, conditionDescription, countryName);
        ModalHost.AddUnder(parent, panel);
        return panel;
    }

    // _Ready'den önce taşınan gösterim verisi (BuildUi/RefreshContent bunu okur).
    private (bool Victory, string Label, string Description, string Country)?
        _pending;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            ClosePanel();
        }
    }

    private void ClosePanel()
    {
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
        IsOpen = false;
    }

    private void OnContinuePressed()
    {
        ClosePanel();
    }

    private void OnMainMenuPressed()
    {
        IsOpen = false;
        GetTree().ChangeSceneToFile(MainMenuScenePath);
    }

    private void BuildUi()
    {
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.82f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop; // harita girdisi bloklansın
        AddChild(_backdrop);
        UITheme.ApplyTheme(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        // Panel 1152x648 ekrana sığar
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(640, 480) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        _panel.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        // Başlık: VICTORY / DEFEAT
        _titleLabel = new Label();
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AddThemeFontSizeOverride("font_size", 40);
        root.AddChild(_titleLabel);

        _subtitleLabel = new Label();
        _subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _subtitleLabel.AddThemeFontSizeOverride("font_size", 16);
        _subtitleLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        root.AddChild(_subtitleLabel);

        root.AddChild(new HSeparator());

        // Zafer koşulu
        var condHeader = new Label { Text = "CONDITION MET" };
        condHeader.AddThemeFontSizeOverride("font_size", 13);
        condHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        root.AddChild(condHeader);

        _conditionLabel = new Label();
        _conditionLabel.AddThemeFontSizeOverride("font_size", 15);
        _conditionLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        root.AddChild(_conditionLabel);

        // Geçen süre
        var durHeader = new Label { Text = "TIME ELAPSED" };
        durHeader.AddThemeFontSizeOverride("font_size", 13);
        durHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        root.AddChild(durHeader);

        _durationLabel = new Label();
        _durationLabel.AddThemeFontSizeOverride("font_size", 15);
        _durationLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        root.AddChild(_durationLabel);

        // Skor dökümü + sıralama yan yana
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 18);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        root.AddChild(columns);

        var leftCol = new VBoxContainer();
        leftCol.AddThemeConstantOverride("separation", 4);
        leftCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(leftCol);

        var scoreHeader = new Label { Text = "SCORE BREAKDOWN" };
        scoreHeader.AddThemeFontSizeOverride("font_size", 13);
        scoreHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        leftCol.AddChild(scoreHeader);

        _scoreLabel = new Label();
        _scoreLabel.AddThemeFontSizeOverride("font_size", 13);
        _scoreLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        leftCol.AddChild(_scoreLabel);

        var rightCol = new VBoxContainer();
        rightCol.AddThemeConstantOverride("separation", 4);
        rightCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(rightCol);

        var rankHeader = new Label { Text = "TOP NATIONS" };
        rankHeader.AddThemeFontSizeOverride("font_size", 13);
        rankHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        rightCol.AddChild(rankHeader);

        _rankingLabel = new Label();
        _rankingLabel.AddThemeFontSizeOverride("font_size", 13);
        _rankingLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        rightCol.AddChild(_rankingLabel);

        // Butonlar
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 14);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(buttons);

        var continueButton = new Button { Text = "CONTINUE", FocusMode = Control.FocusModeEnum.None };
        continueButton.CustomMinimumSize = new Vector2(180, 36);
        continueButton.Pressed += OnContinuePressed;
        buttons.AddChild(continueButton);
        UITheme.AddHoverScale(continueButton);

        _mainMenuButton = new Button { Text = "MAIN MENU", FocusMode = Control.FocusModeEnum.None };
        _mainMenuButton.CustomMinimumSize = new Vector2(180, 36);
        _mainMenuButton.Pressed += OnMainMenuPressed;
        buttons.AddChild(_mainMenuButton);
        UITheme.AddHoverScale(_mainMenuButton);

        RefreshContent();
    }

    private void RefreshContent()
    {
        if (_pending == null) return;
        var (victory, label, description, countryName) = _pending.Value;

        _titleLabel.Text = victory ? "VICTORY" : "DEFEAT";
        _titleLabel.AddThemeColorOverride("font_color",
            victory ? UITheme.GoldBright : new Color(0.85f, 0.25f, 0.22f));
        _subtitleLabel.Text = countryName ?? "—";
        _conditionLabel.Text = string.IsNullOrEmpty(description) ? label : $"{label} — {description}";
        _conditionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        _durationLabel.Text = BuildDurationText();
        _scoreLabel.Text = BuildScoreText();
        _rankingLabel.Text = BuildRankingText();
    }

    private string BuildDurationText()
    {
        if (!GameManager.IsAvailable) return "—";
        var gm = GameManager.Instance;
        int days = gm.TotalDays;
        int years = days / 365;
        int rest = days % 365;
        string span = years > 0 ? $"{years}y {rest}d" : $"{days}d";
        return $"{gm.StartDate:dd MMM yyyy} → {gm.CurrentDate:dd MMM yyyy}  ({span})";
    }

    private string BuildScoreText()
    {
        if (!GameManager.IsAvailable) return "—";
        string playerId = GameManager.Instance.PlayerCountryId;
        if (string.IsNullOrEmpty(playerId)) return "—";

        var parts = ScoreSystem.ScoreBreakdown(playerId);
        if (parts.Count == 0) return "No score yet.";
        double total = parts.Values.Sum();

        var lines = parts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{Pretty(kv.Key)}: {kv.Value:F0}");
        return $"TOTAL {total:F0}\n" + string.Join("\n", lines);
    }

    private static string Pretty(string key) => key switch
    {
        "CivilianFactories" => "Civilian Factories",
        "MilitaryFactories" => "Military Factories",
        "Dockyards" => "Dockyards",
        "Divisions" => "Divisions",
        "Provinces" => "Provinces",
        "Population" => "Population",
        "Technology" => "Technology",
        _ => key
    };

    private string BuildRankingText()
    {
        var ranking = ScoreSystem.GetRanking();
        if (ranking.Count == 0) return "—";

        int shown = Mathf.Min(10, ranking.Count);
        var lines = new string[shown];
        for (int i = 0; i < shown; i++)
            lines[i] = $"{i + 1}. {ranking[i].Name} ({ranking[i].Score:F0})";
        return string.Join("\n", lines);
    }
}
