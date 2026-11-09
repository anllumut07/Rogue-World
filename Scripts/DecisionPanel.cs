using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// HOI4-style Decisions Panel (M10.2): category tabs, search filter, cost/requirements tooltip.
/// HUD DECISIONS butonundan açılır; 1152x648 sınırında; backdrop MouseFilter.Stop.
/// </summary>
public partial class DecisionPanel : Control
{
    public static bool IsOpen { get; private set; }

    // UI components
    private ColorRect _backdrop;
    private PanelContainer _panel;
    private VBoxContainer _rootVBox;

    // Category tabs
    private HBoxContainer _tabBar;
    private List<Button> _tabButtons = new();
    private string _activeCategory = "All";

    // Search
    private LineEdit _searchBox;
    private Label _resultsCountLabel;

    // Available decisions container
    private ScrollContainer _availableScroll;
    private VBoxContainer _availableContainer;
    private Label _noDecisionsLabel;

    // Active decisions container
    private ScrollContainer _activeScroll;
    private VBoxContainer _activeContainer;

    // Categories in order
    private static readonly string[] Categories = { "All", "Economy", "Military", "Political", "Diplomatic", "Technology", "General" };

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[DecisionPanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new DecisionPanel();
        ModalHost.AddUnder(parent, panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        UITheme.ApplyTheme(this);
        BuildUi();
        ConnectSignals();
        RefreshAll();
        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);
        GD.Print("DecisionPanel: hazır");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void BuildUi()
    {
        // Backdrop: full screen, blocks map input
        _backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.75f), MouseFilter = Control.MouseFilterEnum.Stop };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(1000, 560) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        _panel.AddChild(margin);

        _rootVBox = new VBoxContainer();
        _rootVBox.AddThemeConstantOverride("separation", 12);
        margin.AddChild(_rootVBox);

        // --- Header ---
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 16);
        _rootVBox.AddChild(headerRow);

        var title = new Label { Text = "DECISIONS" };
        title.AddThemeFontSizeOverride("font_size", 28);
        headerRow.AddChild(title);

        headerRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        headerRow.AddChild(closeBtn);

        _rootVBox.AddChild(new HSeparator());

        // --- Category Tabs ---
        _tabBar = new HBoxContainer();
        _tabBar.AddThemeConstantOverride("separation", 4);
        _rootVBox.AddChild(_tabBar);

        foreach (var cat in Categories)
        {
            var tabBtn = new Button
            {
                Text = cat,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(110, 32)
            };
            tabBtn.AddThemeFontSizeOverride("font_size", 13);
            var catCopy = cat;
            tabBtn.Pressed += () => OnCategoryTabPressed(catCopy);
            UITheme.AddHoverScale(tabBtn);
            _tabBar.AddChild(tabBtn);
            _tabButtons.Add(tabBtn);
        }
        UpdateTabVisuals();

        // --- Search row ---
        var searchRow = new HBoxContainer();
        searchRow.AddThemeConstantOverride("separation", 12);
        _rootVBox.AddChild(searchRow);

        var searchLabel = new Label { Text = "Search:", CustomMinimumSize = new Vector2(60, 0) };
        searchLabel.AddThemeFontSizeOverride("font_size", 14);
        searchRow.AddChild(searchLabel);

        _searchBox = new LineEdit
        {
            PlaceholderText = "Type to filter decisions...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 30)
        };
        _searchBox.TextChanged += OnSearchTextChanged;
        searchRow.AddChild(_searchBox);

        _resultsCountLabel = new Label { Text = "", CustomMinimumSize = new Vector2(140, 0) };
        _resultsCountLabel.AddThemeFontSizeOverride("font_size", 13);
        _resultsCountLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _resultsCountLabel.HorizontalAlignment = HorizontalAlignment.Right;
        searchRow.AddChild(_resultsCountLabel);

        _rootVBox.AddChild(new HSeparator());

        // --- Two columns: Available | Active ---
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        columns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _rootVBox.AddChild(columns);

        // LEFT: Available decisions
        var leftCol = new VBoxContainer();
        leftCol.AddThemeConstantOverride("separation", 8);
        leftCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(leftCol);

        var leftHeader = new HBoxContainer();
        leftHeader.AddThemeConstantOverride("separation", 8);
        leftCol.AddChild(leftHeader);

        var leftTitle = new Label { Text = "AVAILABLE DECISIONS" };
        leftTitle.AddThemeFontSizeOverride("font_size", 16);
        leftTitle.AddThemeColorOverride("font_color", UITheme.Gold);
        leftHeader.AddChild(leftTitle);

        leftHeader.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var clearFilterBtn = new Button { Text = "CLEAR FILTER", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(110, 28) };
        clearFilterBtn.AddThemeFontSizeOverride("font_size", 11);
        clearFilterBtn.Pressed += () => { _searchBox.Text = ""; _activeCategory = "All"; UpdateTabVisuals(); RefreshAvailable(); };
        UITheme.AddHoverScale(clearFilterBtn);
        leftHeader.AddChild(clearFilterBtn);

        _availableScroll = new ScrollContainer();
        _availableScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _availableScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftCol.AddChild(_availableScroll);

        _availableContainer = new VBoxContainer();
        _availableContainer.AddThemeConstantOverride("separation", 6);
        _availableContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _availableScroll.AddChild(_availableContainer);

        _noDecisionsLabel = new Label
        {
            Text = "No decisions available.\nRequirements not met, on cooldown, or filtered out.",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _noDecisionsLabel.AddThemeFontSizeOverride("font_size", 14);
        _noDecisionsLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _noDecisionsLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _noDecisionsLabel.Visible = false;
        _availableContainer.AddChild(_noDecisionsLabel);

        // RIGHT: Active decisions
        var rightCol = new VBoxContainer();
        rightCol.AddThemeConstantOverride("separation", 8);
        rightCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightCol.CustomMinimumSize = new Vector2(320, 0);
        columns.AddChild(rightCol);

        var rightHeader = new Label { Text = "ACTIVE DECISIONS" };
        rightHeader.AddThemeFontSizeOverride("font_size", 16);
        rightHeader.AddThemeColorOverride("font_color", Colors.Orange);
        rightCol.AddChild(rightHeader);

        _activeScroll = new ScrollContainer();
        _activeScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _activeScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightCol.AddChild(_activeScroll);

        _activeContainer = new VBoxContainer();
        _activeContainer.AddThemeConstantOverride("separation", 8);
        _activeContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _activeScroll.AddChild(_activeContainer);
    }

    private void ConnectSignals()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged += OnDataChanged;
    }

    private void OnDataChanged() => RefreshAll();

    private void RefreshAll()
    {
        RefreshAvailable();
        RefreshActive();
    }

    private void OnCategoryTabPressed(string category)
    {
        _activeCategory = category;
        UpdateTabVisuals();
        RefreshAvailable();
    }

    private void OnSearchTextChanged(string text)
    {
        RefreshAvailable();
    }

    private void UpdateTabVisuals()
    {
        for (int i = 0; i < Categories.Length && i < _tabButtons.Count; i++)
        {
            var btn = _tabButtons[i];
            bool active = Categories[i] == _activeCategory;
            if (active)
            {
                btn.AddThemeColorOverride("font_color", Colors.White);
                btn.AddThemeStyleboxOverride("normal", MakeTabStyle(new Color("#2A2A4A"), new Color("#4A4A8A")));
            }
            else
            {
                btn.AddThemeColorOverride("font_color", UITheme.TextMuted);
                btn.AddThemeStyleboxOverride("normal", MakeTabStyle(new Color("#1A1A2E"), new Color("#3A3A5A")));
            }
        }
    }

    private StyleBoxFlat MakeTabStyle(Color bg, Color border)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        b.SetBorderWidthAll(1);
        b.SetCornerRadiusAll(4);
        b.ContentMarginLeft = 12;
        b.ContentMarginRight = 12;
        b.ContentMarginTop = 4;
        b.ContentMarginBottom = 4;
        return b;
    }

    private void RefreshAvailable()
    {
        // Clear old buttons (keep _noDecisionsLabel)
        foreach (var child in _availableContainer.GetChildren())
        {
            if (child == _noDecisionsLabel) continue;
            _availableContainer.RemoveChild(child);
            child.QueueFree();
        }

        var decisions = DecisionSystem.GetAvailableDecisions();

        // Filter by category
        if (_activeCategory != "All")
        {
            decisions = decisions.Where(d => string.Equals(d.Category, _activeCategory, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Filter by search text
        string search = _searchBox?.Text?.Trim().ToLower() ?? "";
        if (!string.IsNullOrEmpty(search))
        {
            decisions = decisions.Where(d =>
                d.Name.ToLower().Contains(search) ||
                d.Description.ToLower().Contains(search) ||
                d.Category.ToLower().Contains(search)
            ).ToList();
        }

        int totalCount = DecisionSystem.GetAvailableDecisions().Count;
        int filteredCount = decisions.Count;
        _resultsCountLabel.Text = $"Showing {filteredCount} / {totalCount}";

        if (filteredCount == 0)
        {
            _noDecisionsLabel.Visible = true;
            return;
        }

        _noDecisionsLabel.Visible = false;

        foreach (var d in decisions)
        {
            var btn = CreateDecisionButton(d);
            _availableContainer.AddChild(btn);
        }
    }

    private Button CreateDecisionButton(DecisionSystem.Decision d)
    {
        var btn = new Button
        {
            CustomMinimumSize = new Vector2(0, 68),
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = BuildTooltip(d)
        };

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 12);
        btn.AddChild(hbox);

        // Category badge
        var catBadge = new Label { Text = $"[{d.Category}]", CustomMinimumSize = new Vector2(100, 0) };
        catBadge.AddThemeFontSizeOverride("font_size", 11);
        catBadge.AddThemeColorOverride("font_color", GetCategoryColor(d.Category));
        catBadge.HorizontalAlignment = HorizontalAlignment.Center;
        catBadge.VerticalAlignment = VerticalAlignment.Center;
        hbox.AddChild(catBadge);

        // Left: name + details
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 2);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(left);

        var nameLabel = new Label { Text = d.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.HorizontalAlignment = HorizontalAlignment.Left;
        left.AddChild(nameLabel);

        var details = new Label();
        var parts = new List<string>();
        if (d.CostPP > 0) parts.Add($"{d.CostPP} PP");
        if (d.CostDays > 0) parts.Add($"{d.CostDays} days");
        if (d.CooldownDays > 0) parts.Add($"Cooldown: {d.CooldownDays} days");
        details.Text = string.Join("  |  ", parts);
        details.AddThemeFontSizeOverride("font_size", 12);
        details.AddThemeColorOverride("font_color", UITheme.TextMuted);
        details.HorizontalAlignment = HorizontalAlignment.Left;
        left.AddChild(details);

        hbox.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        // Right: START button
        var startBtn = new Button
        {
            Text = "START",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(80, 36)
        };
        startBtn.AddThemeFontSizeOverride("font_size", 12);
        startBtn.Pressed += () => OnStartDecision(d.Id);
        UITheme.AddHoverScale(startBtn);
        hbox.AddChild(startBtn);

        // Category-based subtle background
        btn.AddThemeStyleboxOverride("normal", MakeDecisionBox(d.Category));
        UITheme.AddHoverScale(btn, 1.02f);
        return btn;
    }

    private Color GetCategoryColor(string category)
    {
        return category switch
        {
            "Economy" => new Color("#4CAF50"),      // Green
            "Military" => new Color("#F44336"),     // Red
            "Political" => new Color("#9C27B0"),    // Purple
            "Diplomatic" => new Color("#2196F3"),   // Blue
            "Technology" => new Color("#FF9800"),   // Orange
            _ => new Color("#757575")               // Grey
        };
    }

    private StyleBoxFlat MakeDecisionBox(string category)
    {
        Color accent = GetCategoryColor(category);
        var b = new StyleBoxFlat
        {
            BgColor = new Color("#1E1E2E"),
            BorderColor = new Color(accent.R * 0.3f, accent.G * 0.3f, accent.B * 0.3f, 1f)
        };
        b.SetBorderWidthAll(1);
        b.SetCornerRadiusAll(6);
        b.ContentMarginLeft = 12;
        b.ContentMarginRight = 12;
        b.ContentMarginTop = 8;
        b.ContentMarginBottom = 8;
        return b;
    }

    private void RefreshActive()
    {
        foreach (var child in _activeContainer.GetChildren())
        {
            _activeContainer.RemoveChild(child);
            child.QueueFree();
        }

        var active = DecisionSystem.GetActiveDecisions();

        if (active.Count == 0)
        {
            var noneLabel = new Label
            {
                Text = "No active decisions",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            noneLabel.AddThemeFontSizeOverride("font_size", 14);
            noneLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            noneLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _activeContainer.AddChild(noneLabel);
            return;
        }

        foreach (var info in active)
        {
            var item = CreateActiveItem(info);
            _activeContainer.AddChild(item);
        }
    }

    private Control CreateActiveItem(DecisionSystem.ActiveDecisionInfo info)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 4);
        container.AddThemeStyleboxOverride("normal", MakeBox(new Color("#1A1A2E"), new Color("#3A3A5A")));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        container.AddChild(header);

        // Category badge
        var catBadge = new Label { Text = $"[{info.Decision.Category}]", CustomMinimumSize = new Vector2(100, 0) };
        catBadge.AddThemeFontSizeOverride("font_size", 11);
        catBadge.AddThemeColorOverride("font_color", GetCategoryColor(info.Decision.Category));
        catBadge.HorizontalAlignment = HorizontalAlignment.Center;
        catBadge.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(catBadge);

        var nameLabel = new Label { Text = info.Decision.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", 14);
        nameLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var cancelBtn = new Button
        {
            Text = "CANCEL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(70, 28)
        };
        cancelBtn.AddThemeFontSizeOverride("font_size", 11);
        cancelBtn.Pressed += () => OnCancelDecision(info.Decision.Id);
        UITheme.AddHoverScale(cancelBtn, 1.05f);
        header.AddChild(cancelBtn);

        // Progress bar
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100,
            Value = info.Progress * 100f,
            CustomMinimumSize = new Vector2(0, 16)
        };
        bar.AddThemeColorOverride("fill_color", Colors.Orange);
        container.AddChild(bar);

        var progressLabel = new Label
        {
            Text = $"Day {info.DaysElapsed} / {info.TotalDays}",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        progressLabel.AddThemeFontSizeOverride("font_size", 12);
        progressLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        container.AddChild(progressLabel);

        return container;
    }

    private static StyleBoxFlat MakeBox(Color bg, Color border)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        b.SetBorderWidthAll(1);
        b.SetCornerRadiusAll(4);
        b.ContentMarginLeft = 8;
        b.ContentMarginRight = 8;
        b.ContentMarginTop = 6;
        b.ContentMarginBottom = 6;
        return b;
    }

    private void OnStartDecision(string decisionId)
    {
        if (DecisionSystem.StartDecision(decisionId))
        {
            RefreshAll();
        }
    }

    private void OnCancelDecision(string decisionId)
    {
        DecisionSystem.CancelDecision(decisionId);
        RefreshAll();
    }

    private void Close()
    {
        GD.Print("DecisionPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;

        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }

    private static string BuildTooltip(DecisionSystem.Decision d)
    {
        var lines = new List<string>();
        lines.Add(d.Description);
        lines.Add("");
        lines.Add($"Category: {d.Category}");
        lines.Add("");
        lines.Add("Cost:");
        if (d.CostPP > 0) lines.Add($"  {d.CostPP} Political Power");
        if (d.CostDays > 0) lines.Add($"  {d.CostDays} days to complete");
        if (d.CooldownDays > 0) lines.Add($"  Cooldown: {d.CooldownDays} days");

        var c = d.Condition;
        bool hasReq = c.MinStability > 0 || c.MaxStability < 100 ||
            c.MinWarSupport > 0 || c.MaxWarSupport < 100 ||
            c.MinPP > 0 || c.MaxWorldTension < 100 ||
            !string.IsNullOrEmpty(c.RequiredFocus) ||
            !string.IsNullOrEmpty(c.RequiredNotFocus) ||
            !string.IsNullOrEmpty(c.RequiredIdeology) ||
            c.IsAtWar;

        if (hasReq)
        {
            lines.Add("");
            lines.Add("Requirements:");
            if (c.MinStability > 0) lines.Add($"  Stability ≥ {c.MinStability}%");
            if (c.MaxStability < 100) lines.Add($"  Stability ≤ {c.MaxStability}%");
            if (c.MinWarSupport > 0) lines.Add($"  War Support ≥ {c.MinWarSupport}%");
            if (c.MinPP > 0) lines.Add($"  PP ≥ {c.MinPP}");
            if (c.MaxWorldTension < 100) lines.Add($"  World Tension ≤ {c.MaxWorldTension}%");
            if (!string.IsNullOrEmpty(c.RequiredFocus)) lines.Add($"  Focus completed: {c.RequiredFocus}");
            if (!string.IsNullOrEmpty(c.RequiredNotFocus)) lines.Add($"  Focus NOT completed: {c.RequiredNotFocus}");
            if (!string.IsNullOrEmpty(c.RequiredIdeology)) lines.Add($"  Ideology: {c.RequiredIdeology}");
            if (c.IsAtWar) lines.Add("  Must be at war");
        }

        var e = d.Effects;
        lines.Add("");
        lines.Add("Effects:");
        if (e.PoliticalPower != 0) lines.Add($"  PP: {e.PoliticalPower:+#;-#}");
        if (e.Stability != 0) lines.Add($"  Stability: {e.Stability:+#;-#}%");
        if (e.WarSupport != 0) lines.Add($"  War Support: {e.WarSupport:+#;-#}%");
        if (e.CivFactories != 0) lines.Add($"  Civilian Factories: {e.CivFactories:+#;-#}");
        if (e.MilFactories != 0) lines.Add($"  Military Factories: {e.MilFactories:+#;-#}");
        if (e.Dockyards != 0) lines.Add($"  Dockyards: {e.Dockyards:+#;-#}");
        if (e.ResearchSlots != 0) lines.Add($"  Research Slots: {e.ResearchSlots:+#;-#}");
        if (e.Manpower != 0) lines.Add($"  Manpower: {FormatSigned(e.Manpower)}");
        if (e.Oil != 0) lines.Add($"  Oil: {e.Oil:+#;-#}");
        if (e.Rubber != 0) lines.Add($"  Rubber: {e.Rubber:+#;-#}");
        if (e.Steel != 0) lines.Add($"  Steel: {e.Steel:+#;-#}");
        if (e.Aluminum != 0) lines.Add($"  Aluminum: {e.Aluminum:+#;-#}");
        if (e.Tungsten != 0) lines.Add($"  Tungsten: {e.Tungsten:+#;-#}");
        if (e.Chromium != 0) lines.Add($"  Chromium: {e.Chromium:+#;-#}");
        if (e.WorldTension != 0) lines.Add($"  World Tension: {e.WorldTension:+#;-#}%");

        return string.Join("\n", lines);
    }

    private static string FormatSigned(long value)
    {
        string sign = value >= 0 ? "+" : "-";
        long abs = Math.Abs(value);
        if (abs >= 1000) return $"{sign}{abs / 1000}k";
        return $"{sign}{abs}";
    }
}