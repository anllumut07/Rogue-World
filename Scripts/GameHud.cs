using Godot;

// Üst HUD: sol=ülke+bayrak, orta=tarih+hız göstergesi, sağ=PP/Stability/WarSupport.
// Alt çubuk: seçim bilgisi, oyun hızı metni.
// Tüm elemanlarda tooltip; tema UITheme üzerinden.
public partial class GameHud : CanvasLayer
{
    private Label _dateLabel;
    private Label _countryLabel;
    private Label _ppLabel;
    private Label _stabilityLabel;
    private Label _warSupportLabel;
    private Label _stateLabel;
    private Button _pauseButton;
    private Button[] _speedButtons;

    // Alt çubuk
    private Label _selectedLabel;
    private Label _speedTextLabel;

    private GameManager GM => GameManager.Instance;

    public override void _Ready()
    {
        BuildUi();

        GM.DateChanged += OnDateChanged;
        GM.SpeedChanged += OnSpeedChanged;
        GM.PlayerCountryChanged += OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged += OnPlayerCountryDataChanged;

        OnDateChanged(GM.GetDateText());
        OnSpeedChanged(GM.Speed);
        RefreshCountryLabel();
        RefreshDataLabels();
    }

    public override void _ExitTree()
    {
        if (!GameManager.IsAvailable) return;
        GM.DateChanged -= OnDateChanged;
        GM.SpeedChanged -= OnSpeedChanged;
        GM.PlayerCountryChanged -= OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged -= OnPlayerCountryDataChanged;
    }

    private void BuildUi()
    {
        Layer = 10;

        // === ÜST BAR ===
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;

        UITheme.ApplyTheme(margin);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        margin.AddChild(row);

        var panel = new PanelContainer();
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(panel);

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 16);
        panel.AddChild(bar);

        // --- SOL: Ülke bayrağı + isim ---
        var leftSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        leftSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftSection.AddThemeConstantOverride("separation", 8);
        bar.AddChild(leftSection);

        var flagRect = new ColorRect { CustomMinimumSize = new Vector2(28, 18) };
        flagRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        leftSection.AddChild(flagRect);

        _countryLabel = new Label();
        _countryLabel.AddThemeFontSizeOverride("font_size", 20);
        _countryLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.66f, 0.22f));
        _countryLabel.TooltipText = "Oyuncu ülke";
        leftSection.AddChild(_countryLabel);

        // --- ORTA: Tarih + Hız göstergesi (butonlar) ---
        var centerSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        centerSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        centerSection.AddThemeConstantOverride("separation", 12);
        bar.AddChild(centerSection);

        _dateLabel = new Label();
        _dateLabel.AddThemeFontSizeOverride("font_size", 20);
        _dateLabel.TooltipText = "Oyun tarihi";
        centerSection.AddChild(_dateLabel);

        _stateLabel = new Label();
        _stateLabel.AddThemeFontSizeOverride("font_size", 20);
        _stateLabel.AddThemeColorOverride("font_color", Colors.OrangeRed);
        centerSection.AddChild(_stateLabel);

        // Hız butonları (kompakt toolbar varyantı)
        _speedButtons = new Button[5];
        for (int i = 0; i < 5; i++)
        {
            int speed = i + 1;
            var b = new Button
            {
                Text = $"{speed}x",
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                ThemeTypeVariation = "ToolbarButton",
                TooltipText = $"Oyun hızı: {speed}x"
            };
            b.Pressed += () =>
            {
                GM.SetSpeed(speed);
                SyncSpeedButtons();
            };
            centerSection.AddChild(b);
            _speedButtons[i] = b;
            UITheme.AddHoverScale(b, 1.08f);
        }

        // --- SAĞ: PP | Stability | War Support | ECONOMY | Pause ---
        var rightSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        rightSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightSection.AddThemeConstantOverride("separation", 20);
        bar.AddChild(rightSection);

        _ppLabel = new Label();
        _ppLabel.AddThemeFontSizeOverride("font_size", 18);
        _ppLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
        _ppLabel.TooltipText = "Political Power (günlük +1.5)";
        rightSection.AddChild(_ppLabel);

        _stabilityLabel = new Label();
        _stabilityLabel.AddThemeFontSizeOverride("font_size", 18);
        _stabilityLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 1f));
        _stabilityLabel.TooltipText = "Stability % (düşükse direniş riski)";
        rightSection.AddChild(_stabilityLabel);

        _warSupportLabel = new Label();
        _warSupportLabel.AddThemeFontSizeOverride("font_size", 18);
        _warSupportLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.4f));
        _warSupportLabel.TooltipText = "War Support % (savaş performansını etkiler)";
        rightSection.AddChild(_warSupportLabel);

        // RESEARCH butonu
        var researchBtn = new Button { Text = "RESEARCH", FocusMode = Control.FocusModeEnum.None, TooltipText = "Technology tree (research slots, progress)" };
        researchBtn.Pressed += () => ResearchPanel.OpenUnder(this);
        rightSection.AddChild(researchBtn);
        UITheme.AddHoverScale(researchBtn);

        // ECONOMY butonu
        var economyBtn = new Button { Text = "ECONOMY", FocusMode = Control.FocusModeEnum.None, TooltipText = "Economy panel (build queue, factories, resources)" };
        economyBtn.Pressed += () => EconomyPanel.OpenUnder(this);
        rightSection.AddChild(economyBtn);
        UITheme.AddHoverScale(economyBtn);

        // Pause/Play butonu (en sağda)
        _pauseButton = new Button { FocusMode = Control.FocusModeEnum.None, TooltipText = "Pause/Resume (Space)" };
        _pauseButton.Pressed += () => GM.TogglePause();
        rightSection.AddChild(_pauseButton);
        UITheme.AddHoverScale(_pauseButton, 1.08f);

        // === ALT ÇUBUK ===
        var bottomMargin = new MarginContainer();
        bottomMargin.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        bottomMargin.AddThemeConstantOverride("margin_bottom", 8);
        bottomMargin.AddThemeConstantOverride("margin_left", 20);
        bottomMargin.AddThemeConstantOverride("margin_right", 20);
        bottomMargin.MouseFilter = Control.MouseFilterEnum.Ignore;
        UITheme.ApplyTheme(bottomMargin);

        var bottomBar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        bottomBar.AddThemeConstantOverride("separation", 24);
        bottomMargin.AddChild(bottomBar);

        var leftBottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        leftBottom.AddThemeConstantOverride("separation", 12);
        bottomBar.AddChild(leftBottom);

        _selectedLabel = new Label();
        _selectedLabel.AddThemeFontSizeOverride("font_size", 16);
        _selectedLabel.TooltipText = "Seçili ülke";
        leftBottom.AddChild(_selectedLabel);

        var rightBottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        rightBottom.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightBottom.AddThemeConstantOverride("separation", 12);
        bottomBar.AddChild(rightBottom);

        _speedTextLabel = new Label();
        _speedTextLabel.AddThemeFontSizeOverride("font_size", 16);
        _speedTextLabel.AddThemeColorOverride("font_color", Colors.Gray);
        _speedTextLabel.TooltipText = "Mevcut oyun hızı";
        rightBottom.AddChild(_speedTextLabel);

        // HUD'u sahneye ekle (üst ve alt margin)
        AddChild(margin);
        AddChild(bottomMargin);
    }

    private void OnDateChanged(string text) => _dateLabel.Text = text;

    private void OnPlayerCountryChanged(string id) => RefreshCountryLabel();

    private void OnPlayerCountryDataChanged() => RefreshDataLabels();

    private void RefreshCountryLabel()
    {
        if (GM.HasPlayer && GM.PlayerCountryData != null)
        {
            var d = GM.PlayerCountryData;
            _countryLabel.Text = d.Name;
            // Bayrak rengi (ideoloji rengi)
            var flagRect = (ColorRect)_countryLabel.GetParent().GetChild(0);
            if (flagRect is ColorRect cr) cr.Color = d.GetFlagColor();
        }
        else
        {
            _countryLabel.Text = "Rogue World";
            var flagRect = (ColorRect)_countryLabel.GetParent().GetChild(0);
            if (flagRect is ColorRect cr) cr.Color = new Color("#8E9AA5");
        }
    }

    private void RefreshDataLabels()
    {
        if (GM.HasPlayer && GM.PlayerCountryData != null)
        {
            var d = GM.PlayerCountryData;
            _ppLabel.Text = $"PP: {d.PoliticalPower:N1}";
            _stabilityLabel.Text = $"STA: {d.StabilityPercent:N0}%";
            _warSupportLabel.Text = $"WS: {d.WarSupportPercent:N0}%";
        }
        else
        {
            _ppLabel.Text = "";
            _stabilityLabel.Text = "";
            _warSupportLabel.Text = "";
        }
    }

    private void OnSpeedChanged(int speed)
    {
        bool paused = speed == 0;
        _pauseButton.Text = paused ? "PLAY" : "PAUSE";
        _stateLabel.Text = paused ? "PAUSED" : "";
        SyncSpeedButtons();
        _speedTextLabel.Text = paused ? "Hız: Pause" : $"Hız: {speed}x";
    }

    private void SyncSpeedButtons()
    {
        for (int i = 0; i < _speedButtons.Length; i++)
            _speedButtons[i].SetPressedNoSignal(i + 1 == GM.Speed);
    }

    // WorldMap'ten seçim sinyali için public hook
    public void OnCountrySelected(string name)
    {
        _selectedLabel.Text = $"Seçili: {name}";
    }

    public void OnCountryDeselected()
    {
        _selectedLabel.Text = "Seçili: —";
    }
}