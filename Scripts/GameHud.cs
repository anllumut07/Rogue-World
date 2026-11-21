using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// HoI4-Style HUD — Top bar (country, date, speed, resources),
/// Top-right panel buttons, Bottom bar (selection info).
/// Fully integrated with map, no blocking panels.
/// </summary>
public partial class GameHud : CanvasLayer
{
    // === TOP BAR ===
    private Label _dateLabel;
    private Label _countryLabel;
    private FlagBox _flagBox;
    private Label _ppLabel;
    private Label _stabilityLabel;
    private Label _warSupportLabel;
    private Label _stateLabel;
    private Button _pauseButton;
    private Button[] _speedButtons;
    private Button _nextEventBtn;

    // === TOP RIGHT PANEL BUTTONS ===
    private HBoxContainer _panelButtonRow;
    private MenuButton _moreBtn;
    private PopupMenu _morePopup;
    private readonly List<(string Text, string Tooltip, Action Open)> _panelSections = new();
    private readonly List<Button> _panelButtons = new();
    private readonly List<Action> _moreActions = new();

    // === BOTTOM BAR ===
    private Label _selectedLabel;
    private Label _speedTextLabel;

    // === RESOURCE ICONS (HoI4 style) ===
    private Label _manpowerLabel;
    private Label _civFactoryLabel;
    private Label _milFactoryLabel;
    private Label _dockyardLabel;
    private Label _oilLabel;
    private Label _rubberLabel;
    private Label _steelLabel;
    private Label _aluminumLabel;
    private Label _tungstenLabel;
    private Label _chromiumLabel;

    private GameManager GM => GameManager.Instance;

    public override void _Ready()
    {
        BuildTopBar();
        BuildBottomBar();

        GM.DateChanged += OnDateChanged;
        GM.SpeedChanged += OnSpeedChanged;
        GM.PlayerCountryChanged += OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged += OnPlayerCountryDataChanged;

        OnDateChanged(GM.GetDateText());
        OnSpeedChanged(GM.Speed);
        RefreshCountryLabel();
        RefreshDataLabels();
        RefreshResourceLabels();
    }

    public override void _ExitTree()
    {
        if (!GameManager.IsAvailable) return;
        GM.DateChanged -= OnDateChanged;
        GM.SpeedChanged -= OnSpeedChanged;
        GM.PlayerCountryChanged -= OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged -= OnPlayerCountryDataChanged;
    }

    public override void _Process(double delta)
    {
        if (!GameManager.IsAvailable || !GameManager.Instance.HasPlayer) return;

        // M16.3: Monthly report auto-open
        if (TimeTempoSystem.IsMonthlyReportPending && !MonthlyReportPanel.IsOpen)
        {
            GD.Print("[GameHud] Monthly report pending — opening MonthlyReportPanel");
            MonthlyReportPanel.OpenUnder(this);
        }

        // M16.4: NEXT EVENT button
        if (_nextEventBtn != null)
        {
            bool hasNotifications = TimeTempoSystem.HasPendingNotifications;
            if (hasNotifications && !_nextEventBtn.Visible) _nextEventBtn.Show();
            else if (!hasNotifications && _nextEventBtn.Visible) _nextEventBtn.Hide();
        }

        // Update resource labels every frame (they change with trade/production)
        RefreshResourceLabels();
    }

    // ============================================================
    // TOP BAR BUILD
    // ============================================================
    private void BuildTopBar()
    {
        Layer = 10;

        var topMargin = new MarginContainer();
        topMargin.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        topMargin.AddThemeConstantOverride("margin_top", 4);
        topMargin.AddThemeConstantOverride("margin_right", 8);
        topMargin.AddThemeConstantOverride("margin_left", 8);
        topMargin.MouseFilter = Control.MouseFilterEnum.Ignore;
        UITheme.ApplyTheme(topMargin);

        var topBar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        topBar.AddThemeConstantOverride("separation", 8);
        topMargin.AddChild(topBar);

        // ===== LEFT: Country Flag + Name =====
        var leftSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        leftSection.AddThemeConstantOverride("separation", 6);
        topBar.AddChild(leftSection);

        _flagBox = new FlagBox { CustomMinimumSize = new Vector2(32, 20) };
        leftSection.AddChild(_flagBox);

        _countryLabel = new Label();
        _countryLabel.AddThemeFontSizeOverride("font_size", 18);
        _countryLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _countryLabel.TooltipText = "Player country";
        _countryLabel.HorizontalAlignment = HorizontalAlignment.Left;
        leftSection.AddChild(_countryLabel);

        // ===== CENTER: Date + Speed Controls (HoI4 style) =====
        var centerSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        centerSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        centerSection.AddThemeConstantOverride("separation", 16);
        topBar.AddChild(centerSection);

        // Date (large, prominent)
        _dateLabel = new Label();
        _dateLabel.AddThemeFontSizeOverride("font_size", 22);
        _dateLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        _dateLabel.TooltipText = "Game date";
        centerSection.AddChild(_dateLabel);

        _stateLabel = new Label();
        _stateLabel.AddThemeFontSizeOverride("font_size", 20);
        _stateLabel.AddThemeColorOverride("font_color", Colors.OrangeRed);
        _stateLabel.TooltipText = "Game paused";
        centerSection.AddChild(_stateLabel);

        // Speed buttons (HoI4 style: 1x 2x 3x 4x 5x + Pause)
        _speedButtons = new Button[5];
        var speedBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        speedBox.AddThemeConstantOverride("separation", 4);
        centerSection.AddChild(speedBox);

        for (int i = 0; i < 5; i++)
        {
            int speed = i + 1;
            var b = new Button
            {
                Text = $"{speed}x",
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                ThemeTypeVariation = "ToolbarButton",
                TooltipText = $"Game speed: {speed}x"
            };
            b.Pressed += () =>
            {
                GM.SetSpeed(speed);
                SyncSpeedButtons();
            };
            speedBox.AddChild(b);
            _speedButtons[i] = b;
            UITheme.AddHoverScale(b, 1.08f);
        }

        // Pause button (large, prominent)
        _pauseButton = new Button
        {
            Text = "⏸ PAUSE",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "ToolbarButton",
            TooltipText = "Pause/Resume (Space)",
            CustomMinimumSize = new Vector2(100, 32)
        };
        _pauseButton.Pressed += () => GM.TogglePause();
        centerSection.AddChild(_pauseButton);
        UITheme.AddHoverScale(_pauseButton, 1.08f);

        // ===== RIGHT: Resources + Panel Buttons =====
        var rightSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        rightSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightSection.AddThemeConstantOverride("separation", 12);
        topBar.AddChild(rightSection);

        // --- Resource icons with labels (HoI4 style) ---
        var resourceBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        resourceBox.AddThemeConstantOverride("separation", 10);
        rightSection.AddChild(resourceBox);

        // Create resource labels with icons
        var resources = new (string key, string icon, string color)[]
        {
            ("PP", "💰", "C9A227"),           // Political Power - Gold
            ("MAN", "👥", "8FD400"),          // Manpower - Green
            ("CIV", "🏭", "FFB800"),          // Civilian Factory - Amber
            ("MIL", "🏭", "FF6B00"),          // Military Factory - Orange
            ("DOCK", "⚓", "4DB8FF"),         // Dockyard - Blue
            ("OIL", "🛢️", "8B4513"),         // Oil - Brown
            ("RUB", "🟢", "00AA00"),          // Rubber - Green
            ("STL", "⚙️", "AAAAAA"),          // Steel - Gray
            ("ALU", "⬜", "DDDDDD"),          // Aluminum - Light Gray
            ("TUN", "🟤", "8B7355"),         // Tungsten - Brown
            ("CHR", "🔷", "00FFFF")           // Chromium - Cyan
        };

        foreach (var (key, icon, color) in resources)
        {
            var container = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            container.AddThemeConstantOverride("separation", 2);

            var iconLabel = new Label { Text = icon };
            iconLabel.AddThemeFontSizeOverride("font_size", 14);
            container.AddChild(iconLabel);

            var valueLabel = new Label { Text = $"{key}: 0" };
            valueLabel.AddThemeFontSizeOverride("font_size", 12);
            valueLabel.AddThemeColorOverride("font_color", new Color($"#{color}"));
            valueLabel.TooltipText = key;
            container.AddChild(valueLabel);

            // Store references for updating
            switch (key)
            {
                case "PP": _ppLabel = valueLabel; break;
                case "MAN": _manpowerLabel = valueLabel; break;
                case "CIV": _civFactoryLabel = valueLabel; break;
                case "MIL": _milFactoryLabel = valueLabel; break;
                case "DOCK": _dockyardLabel = valueLabel; break;
                case "OIL": _oilLabel = valueLabel; break;
                case "RUB": _rubberLabel = valueLabel; break;
                case "STL": _steelLabel = valueLabel; break;
                case "ALU": _aluminumLabel = valueLabel; break;
                case "TUN": _tungstenLabel = valueLabel; break;
                case "CHR": _chromiumLabel = valueLabel; break;
            }

            resourceBox.AddChild(container);
        }

        // --- Panel Buttons Row (HoI4 style: Research, Production, Army, Navy, Air, Diplomacy, Focus, Decisions, Logistics) ---
        _panelButtonRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _panelButtonRow.AddThemeConstantOverride("separation", 4);
        rightSection.AddChild(_panelButtonRow);

        var panelButtons = new (string Text, string Tooltip, Action Action)[]
        {
            ("RESEARCH", "Technology tree (R)", () => ResearchPanel.OpenUnder(this)),
            ("PRODUCTION", "Production lines & equipment (P)", () => ProductionPanel.OpenUnder(this)),
            ("ARMY", "Army, divisions & templates (A)", () => ArmyPanel.OpenUnder(this)),
            ("NAVY", "Navy, fleets & ship designer (N)", () => {/* TODO: NavyPanel */}),
            ("AIR", "Air force, wings & air bases", () => {/* TODO: AirPanel */}),
            ("DIPLOMACY", "Diplomacy, relations & factions (D)", () => DiplomacyPanel.OpenUnder(this)),
            ("FOCUS", "National focus tree (F)", () => FocusPanel.OpenUnder(this)),
            ("DECISIONS", "Decisions & political actions", () => DecisionPanel.OpenUnder(this)),
            ("LOGISTICS", "Supply, railways & convoys", () => {/* TODO: LogisticsPanel */}),
            ("STATISTICS", "Country statistics & budget", () => StatisticsPanel.OpenUnder(this)),
            ("ORGANIZATIONS", "International organizations (M37)", () => OrganizationPanel.OpenUnder(this)),
            ("ADVISORS", "Advisors & advice history (M53)", () => AdvisorPanel.OpenUnder(this)),
            ("CLIMATE", "Climate, emissions & policies (M55)", () => ClimatePanel.OpenUnder(this)),
            ("MIGRATION", "Migration & refugee crises (M56)", () => MigrationPanel.OpenUnder(this)),
        };

        foreach (var (text, tooltip, action) in panelButtons)
        {
            var btn = new Button
            {
                Text = text,
                FocusMode = Control.FocusModeEnum.None,
                ThemeTypeVariation = "ToolbarButton",
                TooltipText = tooltip
            };
            btn.Pressed += action;
            _panelButtonRow.AddChild(btn);
            UITheme.AddHoverScale(btn, 1.06f);
        }

        // MORE button for overflow (not needed now but kept for future)
        _moreBtn = new MenuButton
        {
            Text = "MORE ▾",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "More panels",
            Visible = false
        };
        _morePopup = _moreBtn.GetPopup();
        _morePopup.IdPressed += OnMoreItemPressed;
        rightSection.AddChild(_moreBtn);

        // NEXT EVENT button
        _nextEventBtn = new Button
        {
            Text = "⚑ NEXT EVENT",
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "ToolbarButton",
            TooltipText = "Process next pending notification"
        };
        _nextEventBtn.Pressed += () =>
        {
            TimeTempoSystem.ProcessNextNotification();
            _nextEventBtn.Hide();
        };
        _nextEventBtn.Hide();
        rightSection.AddChild(_nextEventBtn);
        UITheme.AddHoverScale(_nextEventBtn);

        // Window resize handling
        GetTree().Root.SizeChanged += ReflowPanels;

        AddChild(topMargin);
    }

    // ============================================================
    // BOTTOM BAR BUILD
    // ============================================================
    private void BuildBottomBar()
    {
        var bottomMargin = new MarginContainer();
        bottomMargin.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        bottomMargin.AddThemeConstantOverride("margin_bottom", 4);
        bottomMargin.AddThemeConstantOverride("margin_left", 8);
        bottomMargin.AddThemeConstantOverride("margin_right", 8);
        bottomMargin.MouseFilter = Control.MouseFilterEnum.Ignore;
        UITheme.ApplyTheme(bottomMargin);

        var bottomBar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        bottomBar.AddThemeConstantOverride("separation", 20);
        bottomMargin.AddChild(bottomBar);

        // Left: Selected country/unit info
        var leftBottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        leftBottom.AddThemeConstantOverride("separation", 12);
        bottomBar.AddChild(leftBottom);

        _selectedLabel = new Label();
        _selectedLabel.AddThemeFontSizeOverride("font_size", 14);
        _selectedLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _selectedLabel.TooltipText = "Selected country or unit";
        leftBottom.AddChild(_selectedLabel);

        // Center: Construction queue summary (HoI4 style)
        var centerBottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        centerBottom.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bottomBar.AddChild(centerBottom);

        // Right: Speed indicator
        var rightBottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        rightBottom.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightBottom.AddThemeConstantOverride("separation", 12);
        bottomBar.AddChild(rightBottom);

        _speedTextLabel = new Label();
        _speedTextLabel.AddThemeFontSizeOverride("font_size", 14);
        _speedTextLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _speedTextLabel.TooltipText = "Current game speed";
        rightBottom.AddChild(_speedTextLabel);

        AddChild(bottomMargin);
    }

    // ============================================================
    // EVENT HANDLERS
    // ============================================================
    private void OnDateChanged(string text) => _dateLabel.Text = text;

    private void OnPlayerCountryChanged(string id) => RefreshCountryLabel();

    private void OnPlayerCountryDataChanged()
    {
        RefreshDataLabels();
        RefreshResourceLabels();
    }

    private void RefreshCountryLabel()
    {
        if (GM.HasPlayer && GM.PlayerCountryData != null)
        {
            var d = GM.PlayerCountryData;
            _countryLabel.Text = d.Name;
            _flagBox?.SetCountry(d.Id);
        }
        else
        {
            _countryLabel.Text = "ROGUE WORLD";
            _flagBox?.SetCountry("XXX");
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
            _ppLabel.Text = "PP: 0";
            _stabilityLabel.Text = "STA: 0%";
            _warSupportLabel.Text = "WS: 0%";
        }
    }

    private void RefreshResourceLabels()
    {
        if (!GM.HasPlayer || GM.PlayerCountryData == null) return;

        var d = GM.PlayerCountryData;
        var stockpiles = EconomySystem.Stockpiles;
        int freeCiv = EconomySystem.FreeCivFactories;

        if (_manpowerLabel != null) _manpowerLabel.Text = $"MAN: {d.ManpowerPool:N0}";
        if (_civFactoryLabel != null) _civFactoryLabel.Text = $"CIV: {d.CivFactories} ({freeCiv} free)";
        if (_milFactoryLabel != null) _milFactoryLabel.Text = $"MIL: {d.MilFactories}";
        if (_dockyardLabel != null) _dockyardLabel.Text = $"DOCK: {d.Dockyards}";
        if (_oilLabel != null) _oilLabel.Text = $"OIL: {stockpiles.GetValueOrDefault("Oil", 0):N0}";
        if (_rubberLabel != null) _rubberLabel.Text = $"RUB: {stockpiles.GetValueOrDefault("Rubber", 0):N0}";
        if (_steelLabel != null) _steelLabel.Text = $"STL: {stockpiles.GetValueOrDefault("Steel", 0):N0}";
        if (_aluminumLabel != null) _aluminumLabel.Text = $"ALU: {stockpiles.GetValueOrDefault("Aluminum", 0):N0}";
        if (_tungstenLabel != null) _tungstenLabel.Text = $"TUN: {stockpiles.GetValueOrDefault("Tungsten", 0):N0}";
        if (_chromiumLabel != null) _chromiumLabel.Text = $"CHR: {stockpiles.GetValueOrDefault("Chromium", 0):N0}";
    }

    private void OnSpeedChanged(int speed)
    {
        bool paused = speed == 0;
        _pauseButton.Text = paused ? "▶ PLAY" : "⏸ PAUSE";
        _pauseButton.ButtonPressed = paused;
        _stateLabel.Text = paused ? "PAUSED" : "";
        SyncSpeedButtons();
        _speedTextLabel.Text = paused ? "Speed: Pause" : $"Speed: {speed}x";
    }

    private void SyncSpeedButtons()
    {
        for (int i = 0; i < _speedButtons.Length; i++)
            _speedButtons[i].SetPressedNoSignal(i + 1 == GM.Speed);
    }

    // Window resize - reflow panel buttons
    private void ReflowPanels()
    {
        if (_panelButtonRow == null) return;

        float viewportW = _panelButtonRow.GetViewportRect().Size.X;
        // Estimate available space after resources
        const float resourceWidth = 600f;
        const float buttonWidth = 80f;
        const float padding = 100f;
        float available = Mathf.Max(0f, viewportW - resourceWidth - padding);
        int maxButtons = Mathf.Max(1, (int)(available / buttonWidth));

        for (int i = 0; i < _panelButtonRow.GetChildCount(); i++)
        {
            if (_panelButtonRow.GetChild(i) is Button btn)
                btn.Visible = i < maxButtons;
        }
    }

    // MORE menu handler
    private void OnMoreItemPressed(long id)
    {
        if (id < 0 || id >= _moreActions.Count) return;
        _moreActions[(int)id]();
        _morePopup.Hide();
    }

    // WorldMap selection signals
    public void OnCountrySelected(string name)
    {
        _selectedLabel.Text = $"Selected: {name}";
    }

    public void OnCountryDeselected()
    {
        _selectedLabel.Text = "Selected: —";
    }
}