using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Climate Panel (M55.3): HUD CLIMATE button opens this.
/// Tabs: Global (warming chart), Emissions (country table), Policies (policy cards), Agreements, Tipping Points.
/// 1152x648 limit, MouseFilter.Stop backdrop, ESC closes. Style matches DecisionPanel.
/// </summary>
public partial class ClimatePanel : Control
{
    public static bool IsOpen { get; private set; }

    // UI components
    private ColorRect _backdrop;
    private PanelContainer _panel;
    private VBoxContainer _rootVBox;

    // Category tabs
    private HBoxContainer _tabBar;
    private List<Button> _tabButtons = new();
    private string _activeTab = "Global";

    // Search
    private LineEdit _searchBox;
    private Label _resultsCountLabel;

    // Content containers
    private ScrollContainer _contentScroll;
    private VBoxContainer _contentContainer;

    // Tabs in order
    private static readonly string[] Tabs = { "Global", "Emissions", "Policies", "Agreements", "Tipping Points" };

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[ClimatePanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new ClimatePanel();
        ModalHost.AddUnder(parent, panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        UITheme.ApplyTheme(this);
        BuildUi();
        ConnectSignals();
        RefreshContent();
        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);
        GD.Print("ClimatePanel: hazır");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
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

        var title = new Label { Text = "CLIMATE & ENVIRONMENT" };
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

        foreach (var tab in Tabs)
        {
            var tabBtn = new Button
            {
                Text = tab,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(120, 32)
            };
            tabBtn.AddThemeFontSizeOverride("font_size", 13);
            var tabCopy = tab;
            tabBtn.Pressed += () => OnTabPressed(tabCopy);
            UITheme.AddHoverScale(tabBtn);
            _tabBar.AddChild(tabBtn);
            _tabButtons.Add(tabBtn);
        }
        UpdateTabVisuals();

        // --- Search row (only for Emissions tab) ---
        var searchRow = new HBoxContainer();
        searchRow.AddThemeConstantOverride("separation", 12);
        _rootVBox.AddChild(searchRow);

        var searchLabel = new Label { Text = "Search:", CustomMinimumSize = new Vector2(60, 0) };
        searchLabel.AddThemeFontSizeOverride("font_size", 14);
        searchRow.AddChild(searchLabel);

        _searchBox = new LineEdit
        {
            PlaceholderText = "Filter countries...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 30)
        };
        _searchBox.TextChanged += OnSearchTextChanged;
        searchRow.AddChild(_searchBox);

        _resultsCountLabel = new Label { Text = "", CustomMinimumSize = new Vector2(200, 0) };
        _resultsCountLabel.AddThemeFontSizeOverride("font_size", 13);
        _resultsCountLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _resultsCountLabel.HorizontalAlignment = HorizontalAlignment.Right;
        searchRow.AddChild(_resultsCountLabel);

        // Initially hide search row for non-Emissions tabs
        UpdateSearchVisibility();

        _rootVBox.AddChild(new HSeparator());

        // --- Content area ---
        _contentScroll = new ScrollContainer();
        _contentScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _contentScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _rootVBox.AddChild(_contentScroll);

        _contentContainer = new VBoxContainer();
        _contentContainer.AddThemeConstantOverride("separation", 8);
        _contentContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _contentScroll.AddChild(_contentContainer);
    }

    private void ConnectSignals()
    {
        // Could listen for GameManager.PlayerCountryDataChanged if needed
    }

    private void OnTabPressed(string tab)
    {
        _activeTab = tab;
        UpdateTabVisuals();
        UpdateSearchVisibility();
        RefreshContent();
    }

    private void OnSearchTextChanged(string text)
    {
        RefreshContent();
    }

    private void UpdateTabVisuals()
    {
        for (int i = 0; i < Tabs.Length && i < _tabButtons.Count; i++)
        {
            var btn = _tabButtons[i];
            bool active = Tabs[i] == _activeTab;
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

    private void UpdateSearchVisibility()
    {
        bool showSearch = _activeTab == "Emissions" || _activeTab == "Policies";
        _searchBox.Visible = showSearch;
        _searchBox.Editable = showSearch;
        _resultsCountLabel.Visible = showSearch;
        var searchRow = _searchBox.GetParent<HBoxContainer>();
        if (searchRow.GetChildCount() > 0)
        {
            var searchLabel = searchRow.GetChild(0) as Control;
            if (searchLabel != null) searchLabel.Visible = showSearch;
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

    private void RefreshContent()
    {
        foreach (var child in _contentContainer.GetChildren())
        {
            _contentContainer.RemoveChild(child);
            child.QueueFree();
        }

        switch (_activeTab)
        {
            case "Global":
                BuildGlobalTab();
                break;
            case "Emissions":
                BuildEmissionsTab();
                break;
            case "Policies":
                BuildPoliciesTab();
                break;
            case "Agreements":
                BuildAgreementsTab();
                break;
            case "Tipping Points":
                BuildTippingPointsTab();
                break;
        }
    }

    // ===== GLOBAL TAB =====
    private void BuildGlobalTab()
    {
        double co2 = ClimateSystem.GetGlobalCO2();
        double warming = ClimateSystem.GetGlobalWarming();
        double annualEmissions = ClimateSystem.GetAnnualEmissionsGt();
        double cumulativeEmissions = ClimateSystem.GetCumulativeEmissionsGt();
        int tpCount = ClimateSystem.GetTriggeredTippingPointsCount();
        var tippingPoints = ClimateSystem.GetTippingPoints();

        // Key metrics row
        var metricsRow = new HBoxContainer();
        metricsRow.AddThemeConstantOverride("separation", 24);
        _contentContainer.AddChild(metricsRow);

        AddMetricCard(metricsRow, "CO₂ Concentration", $"{co2:F1} ppm", "Current atmospheric CO₂ level", Colors.White);
        AddMetricCard(metricsRow, "Global Warming", $"+{warming:F2}°C", "Above pre-industrial (1850-1900)", new Color("#FF6B6B"));
        AddMetricCard(metricsRow, "Annual Emissions", $"{annualEmissions:F2} GtCO₂/yr", "Current yearly global emissions", new Color("#FFB347"));
        AddMetricCard(metricsRow, "Cumulative Emissions", $"{cumulativeEmissions:F1} GtCO₂", "Since pre-industrial", new Color("#87CEEB"));
        AddMetricCard(metricsRow, "Triggered Tipping Points", $"{tpCount} / {tippingPoints.Count}", "Irreversible thresholds crossed", tpCount > 0 ? Colors.Red : new Color("#4CAF50"));

        _contentContainer.AddChild(new HSeparator());

        // Warming chart placeholder (simple text-based for now)
        var chartLabel = new Label
        {
            Text = "[Warming Trend Chart]\n\nA line chart showing CO₂ ppm and temperature anomaly over time would appear here.\nRequires Chart/Graph library integration for full visualization.\n\nCurrent: " + warming.ToString("F2") + "°C warming | " + co2.ToString("F1") + " ppm CO₂",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        chartLabel.AddThemeFontSizeOverride("font_size", 14);
        chartLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        chartLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _contentContainer.AddChild(chartLabel);

        _contentContainer.AddChild(new HSeparator());

        // Tipping points summary
        var tpTitle = new Label { Text = "TIPPING POINTS SUMMARY" };
        tpTitle.AddThemeFontSizeOverride("font_size", 16);
        tpTitle.AddThemeColorOverride("font_color", UITheme.Gold);
        _contentContainer.AddChild(tpTitle);

        foreach (var tp in tippingPoints)
        {
            var tpItem = CreateTippingPointItem(tp);
            _contentContainer.AddChild(tpItem);
        }
    }

    private void AddMetricCard(HBoxContainer parent, string title, string value, string desc, Color valueColor)
    {
        var card = new VBoxContainer();
        card.AddThemeConstantOverride("separation", 4);
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        card.AddThemeStyleboxOverride("normal", MakeBox(new Color("#1A1A2E"), new Color("#3A3A5A")));
        parent.AddChild(card);

        var titleLabel = new Label { Text = title };
        titleLabel.AddThemeFontSizeOverride("font_size", 12);
        titleLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(titleLabel);

        var valueLabel = new Label { Text = value };
        valueLabel.AddThemeFontSizeOverride("font_size", 22);
        valueLabel.AddThemeColorOverride("font_color", valueColor);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(valueLabel);

        var descLabel = new Label { Text = desc };
        descLabel.AddThemeFontSizeOverride("font_size", 11);
        descLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        descLabel.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(descLabel);
    }

    // ===== EMISSIONS TAB =====
    private void BuildEmissionsTab()
    {
        var countries = CountryDatabase.All();
        var states = new List<(string CountryId, string CountryName, ClimateSystem.CountryClimateState State, double EmissionsMt)>();

        foreach (var c in countries)
        {
            var state = ClimateSystem.GetCountryState(c.Id);
            states.Add((c.Id, c.Name, state, state.AnnualEmissionsMt));
        }

        // Sort by emissions descending
        states.Sort((a, b) => b.EmissionsMt.CompareTo(a.EmissionsMt));

        // Filter by search
        string search = _searchBox?.Text?.Trim().ToLower() ?? "";
        if (!string.IsNullOrEmpty(search))
        {
            states = states.Where(s => s.CountryName.ToLower().Contains(search) || s.CountryId.ToLower().Contains(search)).ToList();
        }

        int totalCountries = CountryDatabase.All().Count();
        _resultsCountLabel.Text = $"Showing {states.Count} / {totalCountries} countries";

        if (states.Count == 0)
        {
            var none = new Label { Text = "No countries match filter.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", 14);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            none.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _contentContainer.AddChild(none);
            return;
        }

        // Header row
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 8);
        _contentContainer.AddChild(headerRow);

        AddTableHeader(headerRow, "Country", 140);
        AddTableHeader(headerRow, "Emissions (MtCO₂/yr)", 160);
        AddTableHeader(headerRow, "Renewable %", 100);
        AddTableHeader(headerRow, "Nuclear %", 80);
        AddTableHeader(headerRow, "Coal %", 80);
        AddTableHeader(headerRow, "Oil %", 80);
        AddTableHeader(headerRow, "Gas %", 80);
        AddTableHeader(headerRow, "Adaptation", 100);
        AddTableHeader(headerRow, "Active Policies", 200);

        foreach (var (countryId, countryName, state, emissions) in states)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _contentContainer.AddChild(row);

            AddTableCell(row, countryName, 140);
            AddTableCell(row, $"{emissions:F1}", 160, GetEmissionsColor(emissions));
            AddTableCell(row, $"{(state.RenewableShare * 100):F0}%", 100, new Color("#4CAF50"));
            AddTableCell(row, $"{(state.NuclearShare * 100):F0}%", 80, new Color("#FF9800"));
            AddTableCell(row, $"{(state.CoalShare * 100):F0}%", 80, new Color("#795548"));
            AddTableCell(row, $"{(state.OilShare * 100):F0}%", 80, new Color("#212121"));
            AddTableCell(row, $"{(state.GasShare * 100):F0}%", 80, new Color("#00BCD4"));
            AddTableCell(row, $"{(state.AdaptationLevel * 100):F0}%", 100, new Color("#2196F3"));
            AddTableCell(row, string.Join(", ", state.ActivePolicies.Select(p => GetPolicyShortName(p))), 200);
        }
    }

    private string GetPolicyShortName(string policyId)
    {
        var policy = ClimateSystem.GetAllPolicies().FirstOrDefault(p => p.Id == policyId);
        return policy?.Name ?? policyId;
    }

    private Color GetEmissionsColor(double emissionsMt)
    {
        if (emissionsMt > 5000) return new Color("#F44336");      // Red
        if (emissionsMt > 1000) return new Color("#FF9800");      // Orange
        if (emissionsMt > 100) return new Color("#FFEB3B");       // Yellow
        return new Color("#4CAF50");                               // Green
    }

    private void AddTableHeader(HBoxContainer parent, string text, float width)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0) };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", UITheme.Gold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        parent.AddChild(label);
    }

    private void AddTableCell(HBoxContainer parent, string text, float width, Color? color = null)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0) };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        if (color.HasValue)
            label.AddThemeColorOverride("font_color", color.Value);
        parent.AddChild(label);
    }

    // ===== POLICIES TAB =====
    private void BuildPoliciesTab()
    {
        if (!GameManager.IsAvailable || !GameManager.Instance.HasPlayer)
        {
            var label = new Label { Text = "No player country selected.", HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 14);
            label.AddThemeColorOverride("font_color", UITheme.TextMuted);
            label.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _contentContainer.AddChild(label);
            return;
        }

        string playerId = GameManager.Instance.PlayerCountryId;
        var available = ClimateSystem.GetAvailablePolicies(playerId);
        var active = ClimateSystem.GetActivePolicies(playerId);

        // Filter by search
        string search = _searchBox?.Text?.Trim().ToLower() ?? "";
        if (!string.IsNullOrEmpty(search))
        {
            available = available.Where(p => p.Name.ToLower().Contains(search) || p.Category.ToLower().Contains(search) || p.Description.ToLower().Contains(search)).ToList();
        }

        _resultsCountLabel.Text = $"Available: {available.Count} | Active: {active.Count}";

        // Active policies section
        var activeTitle = new Label { Text = "ACTIVE POLICIES" };
        activeTitle.AddThemeFontSizeOverride("font_size", 16);
        activeTitle.AddThemeColorOverride("font_color", Colors.Orange);
        _contentContainer.AddChild(activeTitle);

        if (active.Count == 0)
        {
            var none = new Label { Text = "No active policies.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", 13);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _contentContainer.AddChild(none);
        }
        else
        {
            foreach (var policy in active)
            {
                var card = CreatePolicyCard(policy, true);
                _contentContainer.AddChild(card);
            }
        }

        _contentContainer.AddChild(new HSeparator());

        // Available policies section
        var availTitle = new Label { Text = "AVAILABLE POLICIES" };
        availTitle.AddThemeFontSizeOverride("font_size", 16);
        availTitle.AddThemeColorOverride("font_color", UITheme.Gold);
        _contentContainer.AddChild(availTitle);

        if (available.Count == 0)
        {
            var none = new Label { Text = "No policies available. Check warming threshold, PP, and cooldown.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", 13);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _contentContainer.AddChild(none);
        }
        else
        {
            foreach (var policy in available)
            {
                var card = CreatePolicyCard(policy, false);
                _contentContainer.AddChild(card);
            }
        }
    }

    private Control CreatePolicyCard(ClimateSystem.Policy policy, bool isActive)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 6);
        container.AddThemeStyleboxOverride("normal", MakeBox(
            isActive ? new Color("#1A2A1A") : new Color("#1E1E2E"),
            isActive ? new Color("#4CAF50") : new Color("#3A3A5A")
        ));

        // Header: name + category
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        container.AddChild(header);

        var catBadge = new Label { Text = $"[{policy.Category}]", CustomMinimumSize = new Vector2(100, 0) };
        catBadge.AddThemeFontSizeOverride("font_size", 11);
        catBadge.AddThemeColorOverride("font_color", GetCategoryColor(policy.Category));
        catBadge.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddChild(catBadge);

        var nameLabel = new Label { Text = policy.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        // Cost info
        var costLabel = new Label();
        costLabel.AddThemeFontSizeOverride("font_size", 12);
        costLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        costLabel.Text = $"Cost: {policy.CostPP} PP  |  Budget: {(policy.CostBudgetPercent * 100):F0}%  |  Min Warming: +{policy.MinWarming:F1}°C";
        container.AddChild(costLabel);

        // Description
        var descLabel = new Label { Text = policy.Description };
        descLabel.AddThemeFontSizeOverride("font_size", 12);
        descLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(descLabel);

        // Effects
        var effects = new List<string>();
        if (policy.EmissionReduction > 0) effects.Add($"Emission Reduction: {(policy.EmissionReduction * 100):F0}%");
        if (policy.RenewableBoost > 0) effects.Add($"Renewable Boost: +{(policy.RenewableBoost * 100):F0}%");
        if (policy.NuclearBoost > 0) effects.Add($"Nuclear Boost: +{(policy.NuclearBoost * 100):F0}%");
        if (policy.AdaptationBoost > 0) effects.Add($"Adaptation Boost: +{(policy.AdaptationBoost * 100):F0}%");
        if (policy.LandUseImprovement > 0) effects.Add($"Land Use Improvement: +{(policy.LandUseImprovement * 100):F0}%");
        if (policy.EnergyCostIncrease != 0) effects.Add($"Energy Cost: {(policy.EnergyCostIncrease * 100):F0}%");
        if (policy.GdpGrowthImpact != 0) effects.Add($"GDP Growth: {(policy.GdpGrowthImpact * 100):F0}%");
        if (policy.PopularityImpact != 0) effects.Add($"Popularity: {(policy.PopularityImpact >= 0 ? "+" : "")}{policy.PopularityImpact}");

        if (effects.Count > 0)
        {
            var effectsLabel = new Label { Text = "Effects: " + string.Join("  |  ", effects) };
            effectsLabel.AddThemeFontSizeOverride("font_size", 11);
            effectsLabel.AddThemeColorOverride("font_color", new Color("#81C784"));
            effectsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            container.AddChild(effectsLabel);
        }

        // Action button
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 8);
        container.AddChild(actionRow);

        actionRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        if (isActive)
        {
            var revokeBtn = new Button { Text = "REVOKE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 32) };
            revokeBtn.AddThemeFontSizeOverride("font_size", 12);
            revokeBtn.Pressed += () => OnRevokePolicy(policy.Id);
            UITheme.AddHoverScale(revokeBtn);
            actionRow.AddChild(revokeBtn);
        }
        else
        {
            var enactBtn = new Button { Text = "ENACT", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 32) };
            enactBtn.AddThemeFontSizeOverride("font_size", 12);
            var canEnact = ClimateSystem.CanEnactPolicy(GameManager.Instance.PlayerCountryId, policy.Id);
            enactBtn.Disabled = !canEnact;
            enactBtn.Pressed += () => OnEnactPolicy(policy.Id);
            UITheme.AddHoverScale(enactBtn);
            actionRow.AddChild(enactBtn);

            if (!canEnact)
            {
                var reason = new Label { Text = "Requirements not met", CustomMinimumSize = new Vector2(140, 0) };
                reason.AddThemeFontSizeOverride("font_size", 11);
                reason.AddThemeColorOverride("font_color", Colors.Red);
                actionRow.AddChild(reason);
            }
        }

        // Tooltip with full details
        container.TooltipText = BuildPolicyTooltip(policy);

        return container;
    }

    private void OnEnactPolicy(string policyId)
    {
        if (GameManager.Instance.HasPlayer)
        {
            ClimateSystem.EnactPolicy(GameManager.Instance.PlayerCountryId, policyId);
            RefreshContent();
        }
    }

    private void OnRevokePolicy(string policyId)
    {
        if (GameManager.Instance.HasPlayer)
        {
            ClimateSystem.RevokePolicy(GameManager.Instance.PlayerCountryId, policyId);
            RefreshContent();
        }
    }

    private Color GetCategoryColor(string category)
    {
        return category switch
        {
            "Pricing" => new Color("#FF9800"),
            "Incentives" => new Color("#4CAF50"),
            "Regulation" => new Color("#F44336"),
            "Nature" => new Color("#8BC34A"),
            "Energy" => new Color("#2196F3"),
            "Transport" => new Color("#9C27B0"),
            "Adaptation" => new Color("#00BCD4"),
            _ => new Color("#757575")
        };
    }

    private string BuildPolicyTooltip(ClimateSystem.Policy p)
    {
        var lines = new List<string>();
        lines.Add(p.Description);
        lines.Add("");
        lines.Add($"Category: {p.Category}");
        lines.Add($"Min Warming: +{p.MinWarming:F1}°C");
        lines.Add("");
        lines.Add("Cost:");
        lines.Add($"  {p.CostPP} Political Power");
        lines.Add($"  {(p.CostBudgetPercent * 100):F0}% of budget");
        lines.Add("");
        lines.Add("Effects:");
        if (p.EmissionReduction > 0) lines.Add($"  Emission Reduction: {(p.EmissionReduction * 100):F0}%");
        if (p.RenewableBoost > 0) lines.Add($"  Renewable Share: +{(p.RenewableBoost * 100):F0}%");
        if (p.NuclearBoost > 0) lines.Add($"  Nuclear Share: +{(p.NuclearBoost * 100):F0}%");
        if (p.AdaptationBoost > 0) lines.Add($"  Adaptation Level: +{(p.AdaptationBoost * 100):F0}%");
        if (p.LandUseImprovement > 0) lines.Add($"  Land Use Improvement: +{(p.LandUseImprovement * 100):F0}%");
        if (p.EnergyCostIncrease != 0) lines.Add($"  Energy Cost: {(p.EnergyCostIncrease * 100):F0}%");
        if (p.GdpGrowthImpact != 0) lines.Add($"  GDP Growth: {(p.GdpGrowthImpact * 100):F0}%");
        if (p.PopularityImpact != 0) lines.Add($"  Popularity: {(p.PopularityImpact >= 0 ? "+" : "")}{p.PopularityImpact}");
        if (!string.IsNullOrEmpty(p.UnlockTech)) lines.Add($"  Unlocks Tech: {p.UnlockTech}");
        return string.Join("\n", lines);
    }

    // ===== AGREEMENTS TAB =====
    private void BuildAgreementsTab()
    {
        if (!GameManager.IsAvailable || !GameManager.Instance.HasPlayer)
        {
            var label = new Label { Text = "No player country selected.", HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 14);
            label.AddThemeColorOverride("font_color", UITheme.TextMuted);
            label.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _contentContainer.AddChild(label);
            return;
        }

        string playerId = GameManager.Instance.PlayerCountryId;
        var state = ClimateSystem.GetCountryState(playerId);
        var allAgreements = ClimateSystem.GetAllAgreements();

        // Signed agreements
        var signedTitle = new Label { Text = "SIGNED AGREEMENTS" };
        signedTitle.AddThemeFontSizeOverride("font_size", 16);
        signedTitle.AddThemeColorOverride("font_color", new Color("#4CAF50"));
        _contentContainer.AddChild(signedTitle);

        var signed = allAgreements.Where(a => state.SignedAgreements.Contains(a.Id)).ToList();
        if (signed.Count == 0)
        {
            var none = new Label { Text = "No agreements signed.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", 13);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _contentContainer.AddChild(none);
        }
        else
        {
            foreach (var agr in signed)
            {
                var card = CreateAgreementCard(agr, true);
                _contentContainer.AddChild(card);
            }
        }

        _contentContainer.AddChild(new HSeparator());

        // Available agreements
        var availTitle = new Label { Text = "AVAILABLE AGREEMENTS" };
        availTitle.AddThemeFontSizeOverride("font_size", 16);
        availTitle.AddThemeColorOverride("font_color", UITheme.Gold);
        _contentContainer.AddChild(availTitle);

        var available = allAgreements.Where(a => !state.SignedAgreements.Contains(a.Id)).ToList();
        if (available.Count == 0)
        {
            var none = new Label { Text = "All agreements signed.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", 13);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _contentContainer.AddChild(none);
        }
        else
        {
            foreach (var agr in available)
            {
                var card = CreateAgreementCard(agr, false);
                _contentContainer.AddChild(card);
            }
        }
    }

    private Control CreateAgreementCard(ClimateSystem.Agreement agr, bool isSigned)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 6);
        container.AddThemeStyleboxOverride("normal", MakeBox(
            isSigned ? new Color("#1A2A1A") : new Color("#1E1E2E"),
            isSigned ? new Color("#4CAF50") : new Color("#3A3A5A")
        ));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        container.AddChild(header);

        var nameLabel = new Label { Text = agr.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var targetLabel = new Label { Text = $"Target: ≤{agr.TargetWarmingLimit:F1}°C", CustomMinimumSize = new Vector2(120, 0) };
        targetLabel.AddThemeFontSizeOverride("font_size", 12);
        targetLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        targetLabel.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(targetLabel);

        var descLabel = new Label { Text = agr.Description };
        descLabel.AddThemeFontSizeOverride("font_size", 12);
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(descLabel);

        var details = new Label();
        details.AddThemeFontSizeOverride("font_size", 11);
        details.AddThemeColorOverride("font_color", UITheme.TextMuted);
        details.Text = $"Review Cycle: {agr.ReviewCycleYears} year(s)  |  Min Participants: {agr.MinParticipants}  |  Prestige Bonus: +{agr.PrestigeBonus}  |  Compliance Bonus: {(agr.ComplianceBonus * 100):F0}%  |  Transparency: {(agr.TransparencyRequirement ? "Required" : "Optional")}";
        container.AddChild(details);

        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 8);
        container.AddChild(actionRow);

        actionRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        if (isSigned)
        {
            var withdrawBtn = new Button { Text = "WITHDRAW", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 32) };
            withdrawBtn.AddThemeFontSizeOverride("font_size", 12);
            withdrawBtn.Pressed += () => OnWithdrawAgreement(agr.Id);
            UITheme.AddHoverScale(withdrawBtn);
            actionRow.AddChild(withdrawBtn);
        }
        else
        {
            var signBtn = new Button { Text = "SIGN", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 32) };
            signBtn.AddThemeFontSizeOverride("font_size", 12);
            var canSign = ClimateSystem.CanSignAgreement(GameManager.Instance.PlayerCountryId, agr.Id);
            signBtn.Disabled = !canSign;
            signBtn.Pressed += () => OnSignAgreement(agr.Id);
            UITheme.AddHoverScale(signBtn);
            actionRow.AddChild(signBtn);
        }

        return container;
    }

    private void OnSignAgreement(string agreementId)
    {
        if (GameManager.Instance.HasPlayer)
        {
            ClimateSystem.SignAgreement(GameManager.Instance.PlayerCountryId, agreementId);
            RefreshContent();
        }
    }

    private void OnWithdrawAgreement(string agreementId)
    {
        if (GameManager.Instance.HasPlayer)
        {
            ClimateSystem.WithdrawAgreement(GameManager.Instance.PlayerCountryId, agreementId);
            RefreshContent();
        }
    }

    // ===== TIPPING POINTS TAB =====
    private void BuildTippingPointsTab()
    {
        var tippingPoints = ClimateSystem.GetTippingPoints();

        var titleLabel = new Label { Text = "CLIMATE TIPPING POINTS — Irreversible thresholds that accelerate warming" };
        titleLabel.AddThemeFontSizeOverride("font_size", 14);
        titleLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        titleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _contentContainer.AddChild(titleLabel);

        _contentContainer.AddChild(new HSeparator());

        foreach (var tp in tippingPoints)
        {
            var card = CreateTippingPointCard(tp);
            _contentContainer.AddChild(card);
        }
    }

    private Control CreateTippingPointItem(ClimateSystem.TippingPoint tp)
    {
        var container = new HBoxContainer();
        container.AddThemeConstantOverride("separation", 12);
        container.AddThemeStyleboxOverride("normal", MakeBox(
            tp.Triggered ? new Color("#2A1A1A") : new Color("#1E1E2E"),
            tp.Triggered ? Colors.Red : new Color("#3A3A5A")
        ));

        // Status indicator
        var status = new Label { Text = tp.Triggered ? "● TRIGGERED" : "○ PENDING", CustomMinimumSize = new Vector2(120, 0) };
        status.AddThemeFontSizeOverride("font_size", 13);
        status.AddThemeColorOverride("font_color", tp.Triggered ? Colors.Red : new Color("#4CAF50"));
        status.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(status);

        // Name
        var name = new Label { Text = tp.Name };
        name.AddThemeFontSizeOverride("font_size", 14);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        container.AddChild(name);

        // Thresholds
        var thresholds = new Label { Text = $"CO₂: {tp.ThresholdCO2:F0} ppm  |  Warming: +{tp.ThresholdWarming:F1}°C  |  Extra: +{tp.ExtraWarming:F2}°C", CustomMinimumSize = new Vector2(300, 0) };
        thresholds.AddThemeFontSizeOverride("font_size", 12);
        thresholds.AddThemeColorOverride("font_color", UITheme.TextMuted);
        thresholds.HorizontalAlignment = HorizontalAlignment.Right;
        container.AddChild(thresholds);

        // Progress bar (approximate)
        double co2 = ClimateSystem.GetGlobalCO2();
        double warming = ClimateSystem.GetGlobalWarming();
        double progress = Math.Max(co2 / tp.ThresholdCO2, warming / tp.ThresholdWarming);
        progress = Math.Min(progress, 1.0);

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = (float)(progress * 100),
            CustomMinimumSize = new Vector2(200, 16)
        };
        bar.AddThemeColorOverride("fill_color", tp.Triggered ? Colors.Red : new Color("#FF9800"));
        container.AddChild(bar);

        return container;
    }

    private Control CreateTippingPointCard(ClimateSystem.TippingPoint tp)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 6);
        container.AddThemeStyleboxOverride("normal", MakeBox(
            tp.Triggered ? new Color("#2A1A1A") : new Color("#1E1E2E"),
            tp.Triggered ? Colors.Red : new Color("#3A3A5A")
        ));

        // Header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        container.AddChild(header);

        var status = new Label { Text = tp.Triggered ? "■ TRIGGERED" : "□ PENDING", CustomMinimumSize = new Vector2(120, 0) };
        status.AddThemeFontSizeOverride("font_size", 14);
        status.AddThemeColorOverride("font_color", tp.Triggered ? Colors.Red : new Color("#4CAF50"));
        header.AddChild(status);

        var name = new Label { Text = tp.Name };
        name.AddThemeFontSizeOverride("font_size", 16);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(name);

        var extraWarming = new Label { Text = $"+{tp.ExtraWarming:F2}°C extra warming", CustomMinimumSize = new Vector2(150, 0) };
        extraWarming.AddThemeFontSizeOverride("font_size", 13);
        extraWarming.AddThemeColorOverride("font_color", tp.Triggered ? Colors.Red : new Color("#FF9800"));
        extraWarming.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(extraWarming);

        // Thresholds
        var thresholds = new Label { Text = $"Trigger Thresholds: CO₂ ≥ {tp.ThresholdCO2:F0} ppm  OR  Warming ≥ +{tp.ThresholdWarming:F1}°C" };
        thresholds.AddThemeFontSizeOverride("font_size", 12);
        thresholds.AddThemeColorOverride("font_color", UITheme.TextMuted);
        container.AddChild(thresholds);

        // Current status
        double co2 = ClimateSystem.GetGlobalCO2();
        double warming = ClimateSystem.GetGlobalWarming();
        var current = new Label { Text = $"Current: {co2:F1} ppm CO₂  |  +{warming:F2}°C warming" };
        current.AddThemeFontSizeOverride("font_size", 12);
        current.AddThemeColorOverride("font_color", UITheme.TextMain);
        container.AddChild(current);

        // Progress bar
        double progress = Math.Max(co2 / tp.ThresholdCO2, warming / tp.ThresholdWarming);
        progress = Math.Min(progress, 1.0);

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = (float)(progress * 100),
            CustomMinimumSize = new Vector2(0, 16)
        };
        bar.AddThemeColorOverride("fill_color", tp.Triggered ? Colors.Red : new Color("#FF9800"));
        container.AddChild(bar);

        var progressLabel = new Label { Text = $"Progress to threshold: {progress * 100:F0}%" };
        progressLabel.AddThemeFontSizeOverride("font_size", 11);
        progressLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        progressLabel.HorizontalAlignment = HorizontalAlignment.Right;
        container.AddChild(progressLabel);

        if (tp.Triggered)
        {
            var warning = new Label { Text = "⚠ THIS TIPPING POINT HAS BEEN TRIGGERED — Extra warming is now permanently applied!" };
            warning.AddThemeFontSizeOverride("font_size", 12);
            warning.AddThemeColorOverride("font_color", Colors.Red);
            warning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            container.AddChild(warning);
        }

        return container;
    }

    private StyleBoxFlat MakeBox(Color bg, Color border)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        b.SetBorderWidthAll(1);
        b.SetCornerRadiusAll(4);
        b.ContentMarginLeft = 12;
        b.ContentMarginRight = 12;
        b.ContentMarginTop = 10;
        b.ContentMarginBottom = 10;
        return b;
    }

    private void Close()
    {
        GD.Print("ClimatePanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}