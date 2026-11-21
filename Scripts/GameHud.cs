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
    private readonly Dictionary<Button, Action> _buttonActions = new(); // Button -> its action

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

        // Validate button visibility on first frame
        CallDeferred(nameof(ValidateAllButtonsVisible));
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

        // --- Panel Buttons Row (HoI4 style) ---
        _panelButtonRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _panelButtonRow.AddThemeConstantOverride("separation", 4);
        _panelButtonRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; // Takes remaining space
        _topBarRightSection.AddChild(_panelButtonRow);

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
                TooltipText = tooltip,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
            };
            btn.Pressed += action;
            _panelButtonRow.AddChild(btn);
            _buttonActions[btn] = action; // Store action for MORE menu
            UITheme.AddHoverScale(btn, 1.06f);
        }

        // MORE button for overflow (not needed now but kept for future)
        _moreBtn = new MenuButton
        {
            Text = "MORE ▾",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "More panels",
            Visible = false,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
        };
        _morePopup = _moreBtn.GetPopup();
        _morePopup.IdPressed += OnMoreItemPressed;
        _topBarRightSection.AddChild(_moreBtn);

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

    // Window resize - reflow panel buttons and resources responsively
    private void ReflowPanels()
    {
        if (_panelButtonRow == null || _resourceBox == null || _topBarRightSection == null) return;

        float viewportW = GetViewport().GetVisibleRect().Size.X;
        // Account for left + center sections (approximate)
        const float leftCenterWidth = 450f; // flag + name + date + speed controls + pause (reduced)
        float rightSectionStart = leftCenterWidth;
        float availableForRight = viewportW - rightSectionStart - 40f; // margins

        if (availableForRight <= 0) return;

        // Calculate resource box width
        float resourceWidth = 0f;
        foreach (Node child in _resourceBox.GetChildren())
        {
            if (child is HBoxContainer container)
                resourceWidth += container.GetMinimumSize().X + 8f; // separation
        }

        // Calculate button widths
        float buttonWidth = 0f;
        int buttonCount = 0;
        List<Button> allButtons = new();
        foreach (Node child in _panelButtonRow.GetChildren())
        {
            if (child is Button btn)
            {
                buttonWidth += btn.GetMinimumSize().X + 4f; // separation
                buttonCount++;
                allButtons.Add(btn);
            }
        }

        float totalNeeded = resourceWidth + buttonWidth + 24f; // extra padding
        float available = availableForRight;

        // Clear MORE popup
        _morePopup.Clear();
        _moreActions.Clear();

        if (totalNeeded <= available)
        {
            // Everything fits - show all
            foreach (var btn in allButtons) btn.Visible = true;
            foreach (Node child in _resourceBox.GetChildren())
                if (child is Control ctrl) ctrl.Visible = true;
            _moreBtn.Visible = false;
        }
        else
        {
            // Not enough space - determine how many buttons fit
            float buttonsAvailable = available - resourceWidth - 12f;
            if (buttonsAvailable < 80f) buttonsAvailable = 80f; // At least 1 button

            float accWidth = 0f;
            int visibleButtons = 0;

            foreach (var btn in allButtons)
            {
                float btnWidth = btn.GetMinimumSize().X + 4f;
                if (accWidth + btnWidth <= buttonsAvailable || visibleButtons == 0)
                {
                    btn.Visible = true;
                    accWidth += btnWidth;
                    visibleButtons++;
                }
                else
                {
                    btn.Visible = false;
                    // Add to MORE menu using stored action
                    if (_buttonActions.TryGetValue(btn, out var action))
                    {
                        _moreActions.Add(action);
                        _morePopup.AddItem(btn.Text, _moreActions.Count - 1);
                        _morePopup.SetItemTooltip(_moreActions.Count - 1, btn.TooltipText);
                    }
                }
            }

            // Ensure at least 1 button visible
            if (visibleButtons == 0 && allButtons.Count > 0)
            {
                allButtons[0].Visible = true;
                visibleButtons = 1;
            }

            _moreBtn.Visible = visibleButtons < allButtons.Count;
        }

        // Compact resources if still too tight
        float usedWidth = resourceWidth;
        foreach (var btn in allButtons) if (btn.Visible) usedWidth += btn.GetMinimumSize().X + 4f;
        if (usedWidth > available)
        {
            foreach (Node child in _resourceBox.GetChildren())
            {
                if (child is HBoxContainer container && container.GetChildCount() == 2)
                {
                    if (container.GetChild(1) is Label valueLabel)
                        valueLabel.Visible = false;
                }
            }
            CallDeferred(nameof(ReflowPanels));
        }

        // Log validation
        ValidateAllButtonsVisible();
    }

    /// <summary>Validate all HUD buttons are within viewport bounds</summary>
    private void ValidateAllButtonsVisible()
    {
        if (!IsInsideTree()) return;

        var viewportRect = GetViewport().GetVisibleRect();
        var hudRect = _topBarPanel.GetGlobalRect();

        // Check top bar panel
        if (hudRect.End.X > viewportRect.Size.X || hudRect.End.Y > viewportRect.Size.Y)
        {
            GD.PrintErr($"[HUD] TopBarPanel exceeds viewport: rect={hudRect}, viewport={viewportRect.Size}");
        }

        // Check panel buttons
        int offScreenCount = 0;
        foreach (Node child in _panelButtonRow.GetChildren())
        {
            if (child is Button btn && btn.Visible)
            {
                var globalRect = btn.GetGlobalRect();
                if (globalRect.End.X > viewportRect.Size.X || globalRect.Position.X < 0)
                {
                    offScreenCount++;
                    GD.PrintErr($"[HUD] Button off-screen: {btn.Text} rect={globalRect}");
                }
            }
        }
        if (offScreenCount > 0)
            GD.PrintErr($"[HUD] {offScreenCount} button(s) off-screen!");

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