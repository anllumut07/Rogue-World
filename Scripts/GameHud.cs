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

    // === TOP RIGHT MENU (single ☰ MENU button with PopupMenu) ===
    private MenuButton _menuButton;
    private PopupMenu _menuPopup;
    private List<(string Text, string Tooltip, Action Open, string Shortcut)> _menuItems = new();

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

    // Layout containers for responsive design
    private PanelContainer _topBarPanel;
    private HBoxContainer _resourceBox;
    private HBoxContainer _topBarCenterSection;
    private HBoxContainer _topBarRightSection;

    private GameManager GM => GameManager.Instance;

    /// <summary>Actual top bar height (for camera vertical clamp calculation)</summary>
    public float TopBarHeight => _topBarPanel?.Size.Y ?? 50f;

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

        // Validate HUD elements visibility on first frame
        CallDeferred(nameof(ValidateAllElementsVisible));
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

        // --- Top bar background panel (semi-transparent dark, blocks input) ---
        _topBarPanel = new PanelContainer
        {
            ThemeTypeVariation = "InnerPanel", // No border, just bg
            MouseFilter = Control.MouseFilterEnum.Stop, // Block input from reaching map
            Name = "TopBarPanel"
        };
        _topBarPanel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _topBarPanel.AddThemeConstantOverride("margin_top", 4);
        _topBarPanel.AddThemeConstantOverride("margin_right", 8);
        _topBarPanel.AddThemeConstantOverride("margin_left", 8);
        // Style: dark semi-transparent bg, thin bottom border
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.11f, 0.92f),
            BorderColor = new Color(0.2f, 0.25f, 0.3f, 1f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        panelStyle.SetBorderWidthAll(0);
        panelStyle.SetBorderWidth(Side.Bottom, 1);
        _topBarPanel.AddThemeStyleboxOverride("panel", panelStyle);
        UITheme.ApplyTheme(_topBarPanel);

        var topBar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        topBar.AddThemeConstantOverride("separation", 8);
        _topBarPanel.AddChild(topBar);

        // ===== LEFT: Country Flag + Name =====
        var leftSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        leftSection.AddThemeConstantOverride("separation", 6);
        leftSection.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; // Don't expand
        topBar.AddChild(leftSection);

        _flagBox = new FlagBox { CustomMinimumSize = new Vector2(32, 20) };
        leftSection.AddChild(_flagBox);

        _countryLabel = new Label();
        _countryLabel.AddThemeFontSizeOverride("font_size", 18);
        _countryLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _countryLabel.TooltipText = "Player country";
        _countryLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _countryLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        leftSection.AddChild(_countryLabel);

        // ===== CENTER: Date + Speed Controls (HoI4 style) =====
        _topBarCenterSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _topBarCenterSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _topBarCenterSection.AddThemeConstantOverride("separation", 16);
        topBar.AddChild(_topBarCenterSection);

        // Date (large, prominent)
        _dateLabel = new Label();
        _dateLabel.AddThemeFontSizeOverride("font_size", 22);
        _dateLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        _dateLabel.TooltipText = "Game date";
        _topBarCenterSection.AddChild(_dateLabel);

        _stateLabel = new Label();
        _stateLabel.AddThemeFontSizeOverride("font_size", 20);
        _stateLabel.AddThemeColorOverride("font_color", Colors.OrangeRed);
        _stateLabel.TooltipText = "Game paused";
        _topBarCenterSection.AddChild(_stateLabel);

        // Speed buttons (HoI4 style: 1x 2x 3x 4x 5x + Pause)
        _speedButtons = new Button[5];
        var speedBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        speedBox.AddThemeConstantOverride("separation", 4);
        _topBarCenterSection.AddChild(speedBox);

        for (int i = 0; i < 5; i++)
        {
            int speed = i + 1;
            var b = new Button
            {
                Text = $"{speed}x",
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                ThemeTypeVariation = "ToolbarButton",
                TooltipText = $"Game speed: {speed}x",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
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
            CustomMinimumSize = new Vector2(100, 32),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
        };
        _pauseButton.Pressed += () => GM.TogglePause();
        _topBarCenterSection.AddChild(_pauseButton);
        UITheme.AddHoverScale(_pauseButton, 1.08f);

        // ===== RIGHT: Resources + Panel Buttons =====
        _topBarRightSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _topBarRightSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _topBarRightSection.AddThemeConstantOverride("separation", 12);
        topBar.AddChild(_topBarRightSection);

        // --- Resource icons with labels (HoI4 style) - responsive: can wrap to second row ---
        _resourceBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _resourceBox.AddThemeConstantOverride("separation", 8);
        _resourceBox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; // Don't steal space from buttons
        _topBarRightSection.AddChild(_resourceBox);

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
            container.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

            var iconLabel = new Label { Text = icon };
            iconLabel.AddThemeFontSizeOverride("font_size", 14);
            container.AddChild(iconLabel);

            var valueLabel = new Label { Text = $"{key}: 0" };
            valueLabel.AddThemeFontSizeOverride("font_size", 12);
            valueLabel.AddThemeColorOverride("font_color", new Color($"#{color}"));
            valueLabel.TooltipText = key;
            valueLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
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

            _resourceBox.AddChild(container);
        }

        // --- Single MENU Button (☰) anchored to far right ---
        // Define all menu items: (Text, Tooltip, Action, Shortcut)
        _menuItems = new List<(string Text, string Tooltip, Action Open, string Shortcut)>
        {
            ("Research", "Technology tree", () => ResearchPanel.OpenUnder(this), "R"),
            ("Production", "Production lines & equipment", () => ProductionPanel.OpenUnder(this), "P"),
            ("Army", "Army, divisions & templates", () => ArmyPanel.OpenUnder(this), "A"),
            ("Navy", "Navy, fleets & ship designer", () => {/* TODO: NavyPanel */}, "N"),
            ("Air Force", "Air force, wings & air bases", () => {/* TODO: AirPanel */}, ""),
            ("Diplomacy", "Diplomacy, relations & factions", () => DiplomacyPanel.OpenUnder(this), "D"),
            ("Focus Tree", "National focus tree", () => FocusPanel.OpenUnder(this), "F"),
            ("Decisions", "Decisions & political actions", () => DecisionPanel.OpenUnder(this), ""),
            ("Logistics", "Supply, railways & convoys", () => {/* TODO: LogisticsPanel */}, ""),
            ("Statistics", "Country statistics & budget", () => StatisticsPanel.OpenUnder(this), ""),
            ("Organizations", "International organizations (M37)", () => OrganizationPanel.OpenUnder(this), ""),
            ("Advisors", "Advisors & advice history (M53)", () => AdvisorPanel.OpenUnder(this), ""),
            ("Climate", "Climate, emissions & policies (M55)", () => ClimatePanel.OpenUnder(this), ""),
            ("Migration", "Migration & refugee crises (M56)", () => MigrationPanel.OpenUnder(this), ""),
        };

        _menuButton = new MenuButton
        {
            Text = "☰ MENU",
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "ToolbarButton",
            TooltipText = "Open main menu (all panels)",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            CustomMinimumSize = new Vector2(100, 32)
        };
        _menuPopup = _menuButton.GetPopup();
        _menuPopup.IdPressed += OnMenuItemPressed;

        // Populate menu
        for (int i = 0; i < _menuItems.Count; i++)
        {
            var item = _menuItems[i];
            string displayText = item.Text;
            if (!string.IsNullOrEmpty(item.Shortcut))
                displayText += $"  [{item.Shortcut}]";
            _menuPopup.AddItem(displayText, i);
            _menuPopup.SetItemTooltip(i, item.Tooltip);
        }

        // Ensure popup stays within viewport
        _menuPopup.AboutToPopup += OnMenuPopup;

        _topBarRightSection.AddChild(_menuButton);
        UITheme.AddHoverScale(_menuButton, 1.06f);

        // NEXT EVENT button
        _nextEventBtn = new Button
        {
            Text = "⚑ NEXT EVENT",
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "ToolbarButton",
            TooltipText = "Process next pending notification",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
        };
        _nextEventBtn.Pressed += () =>
        {
            TimeTempoSystem.ProcessNextNotification();
            _nextEventBtn.Hide();
        };
        _nextEventBtn.Hide();
        _topBarRightSection.AddChild(_nextEventBtn);
        UITheme.AddHoverScale(_nextEventBtn);

        // Window resize handling
        GetTree().Root.SizeChanged += ReflowPanels;

        AddChild(_topBarPanel);
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

    // Window resize - validate HUD elements stay within viewport
    private void ReflowPanels()
    {
        if (_resourceBox == null || _topBarRightSection == null || _menuButton == null) return;

        // Just validate that all HUD elements are within viewport bounds
        ValidateAllElementsVisible();
    }

    /// <summary>Validate all HUD elements are within viewport bounds</summary>
    private void ValidateAllElementsVisible()
    {
        if (!IsInsideTree()) return;

        var viewportRect = GetViewport().GetVisibleRect();
        var hudRect = _topBarPanel.GetGlobalRect();

        // Check top bar panel
        if (hudRect.End.X > viewportRect.Size.X || hudRect.End.Y > viewportRect.Size.Y)
        {
            GD.PrintErr($"[HUD] TopBarPanel exceeds viewport: rect={hudRect}, viewport={viewportRect.Size}");
        }

        // Check menu button
        if (_menuButton != null && _menuButton.Visible)
        {
            var menuRect = _menuButton.GetGlobalRect();
            if (menuRect.End.X > viewportRect.Size.X || menuRect.Position.X < 0)
            {
                GD.PrintErr($"[HUD] MenuButton off-screen: rect={menuRect}");
            }
        }

        // Check resource labels
        foreach (Node child in _resourceBox.GetChildren())
        {
            if (child is HBoxContainer container)
            {
                var globalRect = container.GetGlobalRect();
                if (globalRect.End.X > viewportRect.Size.X || globalRect.Position.X < 0)
                {
                    GD.PrintErr($"[HUD] Resource container off-screen: rect={globalRect}");
                }
            }
        }

        // Check NEXT EVENT button
        if (_nextEventBtn != null && _nextEventBtn.Visible)
        {
            var nextRect = _nextEventBtn.GetGlobalRect();
            if (nextRect.End.X > viewportRect.Size.X || nextRect.Position.X < 0)
            {
                GD.PrintErr($"[HUD] NextEventBtn off-screen: rect={nextRect}");
            }
        }

        GD.Print($"[HUD] Viewport validation complete. Viewport={viewportRect.Size}, TopBar={hudRect.Size}");
    }

    // MENU button popup handler - ensure popup stays within viewport
    // Note: PopupMenu automatically positions itself within viewport bounds
    // This handler can be used for custom positioning if needed
    private void OnMenuPopup()
    {
        // PopupMenu handles viewport clamping automatically in Godot 4.x
        // No manual intervention needed
        GD.Print("[HUD] Menu popup opened");
    }

    // MENU item handler
    private void OnMenuItemPressed(long id)
    {
        if (id < 0 || id >= _menuItems.Count) return;
        _menuItems[(int)id].Open();
        _menuPopup.Hide();

        // Block map input briefly while menu action executes
        // (Panels opened by menu items will handle their own input blocking)
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