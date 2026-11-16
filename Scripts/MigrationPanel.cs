using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Migration Panel (M56): HUD MIGRATION button opens this.
/// Tabs: Overview (global stats, border policy), Crises (active crises), Flows (country-to-country), Policies (border policies, aid packages).
/// 1152x648 limit, MouseFilter.Stop backdrop, ESC closes. Style matches DecisionPanel/ClimatePanel.
/// </summary>
public partial class MigrationPanel : Control
{
    public static bool IsOpen { get; private set; }

    // UI components
    private ColorRect _backdrop;
    private PanelContainer _panel;
    private VBoxContainer _rootVBox;

    // Category tabs
    private HBoxContainer _tabBar;
    private List<Button> _tabButtons = new();
    private string _activeTab = "Overview";

    // Search
    private LineEdit _searchBox;
    private Label _resultsCountLabel;

    // Content containers
    private ScrollContainer _contentScroll;
    private VBoxContainer _contentContainer;

    // Tabs in order
    private static readonly string[] Tabs = { "Overview", "Crises", "Flows", "Policies" };

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[MigrationPanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new MigrationPanel();
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
        GD.Print("MigrationPanel: hazır");
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

        var title = new Label { Text = "MIGRATION & REFUGEES" };
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

        // --- Search row (for Crises and Flows tabs) ---
        var searchRow = new HBoxContainer();
        searchRow.AddThemeConstantOverride("separation", 12);
        _rootVBox.AddChild(searchRow);

        var searchLabel = new Label { Text = "Search:", CustomMinimumSize = new Vector2(60, 0) };
        searchLabel.AddThemeFontSizeOverride("font_size", 14);
        searchRow.AddChild(searchLabel);

        _searchBox = new LineEdit
        {
            PlaceholderText = "Filter...",
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

        // Initially hide search row for non-searchable tabs
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
        bool showSearch = _activeTab == "Crises" || _activeTab == "Flows";
        _searchBox.Visible = showSearch;
        _searchBox.Editable = showSearch;
        _resultsCountLabel.Visible = showSearch;
        var searchRow = _searchBox.GetParent<HBoxContainer>();
        if (searchRow != null && searchRow.GetChildCount() > 0)
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
            case "Overview":
                BuildOverviewTab();
                break;
            case "Crises":
                BuildCrisesTab();
                break;
            case "Flows":
                BuildFlowsTab();
                break;
            case "Policies":
                BuildPoliciesTab();
                break;
        }
    }

    // ===== OVERVIEW TAB =====
    private void BuildOverviewTab()
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
        var state = MigrationSystem.GetCountryState(playerId);
        var policy = MigrationSystem.GetBorderPolicy(state.BorderPolicyId);
        int totalRefugees = MigrationSystem.GetTotalGlobalRefugees();
        int totalCrises = MigrationSystem.GetAllCrisesCount();
        int playerRefugees = state.RefugeePopulation;
        int playerPending = state.AsylumSeekersPending;

        // Key metrics row
        var metricsRow = new HBoxContainer();
        metricsRow.AddThemeConstantOverride("separation", 24);
        _contentContainer.AddChild(metricsRow);

        AddMetricCard(metricsRow, "Global Refugees", totalRefugees.ToString("N0"), "Worldwide displaced population", Colors.White);
        AddMetricCard(metricsRow, "Active Crises", totalCrises.ToString(), "Ongoing displacement situations", new Color("#FF9800"));
        AddMetricCard(metricsRow, "Your Refugees", playerRefugees.ToString("N0"), "Refugees currently in your country", new Color("#4ECDC4"));
        AddMetricCard(metricsRow, "Pending Asylum", playerPending.ToString("N0"), "Awaiting decision", new Color("#FFB347"));

        _contentContainer.AddChild(new HSeparator());

        // Current border policy
        var policyTitle = new Label { Text = "CURRENT BORDER POLICY" };
        policyTitle.AddThemeFontSizeOverride("font_size", 16);
        policyTitle.AddThemeColorOverride("font_color", UITheme.Gold);
        _contentContainer.AddChild(policyTitle);

        if (policy != null)
        {
            var policyCard = CreateBorderPolicyCard(policy, true);
            _contentContainer.AddChild(policyCard);
        }

        _contentContainer.AddChild(new HSeparator());

        // Quick policy change buttons
        var changeTitle = new Label { Text = "CHANGE BORDER POLICY" };
        changeTitle.AddThemeFontSizeOverride("font_size", 16);
        changeTitle.AddThemeColorOverride("font_color", UITheme.Gold);
        _contentContainer.AddChild(changeTitle);

        var policyGrid = new GridContainer();
        policyGrid.Columns = 2;
        policyGrid.AddThemeConstantOverride("h_separation", 12);
        policyGrid.AddThemeConstantOverride("v_separation", 12);
        _contentContainer.AddChild(policyGrid);

        foreach (var p in MigrationSystem.GetAllBorderPolicies())
        {
            var btn = new Button
            {
                Text = p.Name,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 80)
            };
            btn.AddThemeFontSizeOverride("font_size", 12);
            btn.TooltipText = p.Description + $"\n\nAcceptance: {(p.RefugeeAcceptanceMultiplier * 100):F0}%\nStability: {(p.StabilityImpact >= 0 ? "+" : "")}{p.StabilityImpact:F1}\nGDP Growth: {(p.GdpGrowthImpact >= 0 ? "+" : "")}{p.GdpGrowthImpact:F1}\nPP Cost: {p.CostPP}";
            var canChange = MigrationSystem.CanSetBorderPolicy(playerId, p.Id);
            btn.Disabled = !canChange || p.Id == state.BorderPolicyId;
            var pid = p.Id;
            btn.Pressed += () => OnSetBorderPolicy(pid);
            UITheme.AddHoverScale(btn);
            policyGrid.AddChild(btn);

            if (!canChange && p.Id != state.BorderPolicyId)
            {
                var reason = new Label { Text = "Requirements not met", CustomMinimumSize = new Vector2(0, 20) };
                reason.AddThemeFontSizeOverride("font_size", 10);
                reason.AddThemeColorOverride("font_color", Colors.Red);
                policyGrid.AddChild(reason);
            }
        }

        _contentContainer.AddChild(new HSeparator());

        // Top crisis summary
        var crisisTitle = new Label { Text = "MAJOR ACTIVE CRISES" };
        crisisTitle.AddThemeFontSizeOverride("font_size", 16);
        crisisTitle.AddThemeColorOverride("font_color", UITheme.Gold);
        _contentContainer.AddChild(crisisTitle);

        var crises = MigrationSystem.GetAllActiveCrises().OrderByDescending(c => c.RefugeeCount).Take(5).ToList();
        if (crises.Count == 0)
        {
            var none = new Label { Text = "No active crises.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", 13);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _contentContainer.AddChild(none);
        }
        else
        {
            foreach (var crisis in crises)
            {
                var item = CreateCrisisSummaryItem(crisis);
                _contentContainer.AddChild(item);
            }
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

    private Control CreateBorderPolicyCard(MigrationSystem.BorderPolicy policy, bool isCurrent)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 6);
        container.AddThemeStyleboxOverride("normal", MakeBox(
            isCurrent ? new Color("#1A2A1A") : new Color("#1E1E2E"),
            isCurrent ? new Color("#4CAF50") : new Color("#3A3A5A")
        ));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        container.AddChild(header);

        var catBadge = new Label { Text = $"[{policy.Category}]", CustomMinimumSize = new Vector2(120, 0) };
        catBadge.AddThemeFontSizeOverride("font_size", 11);
        catBadge.AddThemeColorOverride("font_color", GetCategoryColor(policy.Category));
        catBadge.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddChild(catBadge);

        var nameLabel = new Label { Text = policy.Name + (isCurrent ? " (ACTIVE)" : "") };
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var descLabel = new Label { Text = policy.Description };
        descLabel.AddThemeFontSizeOverride("font_size", 12);
        descLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(descLabel);

        // Effects
        var effects = new List<string>();
        effects.Add($"Refugee Acceptance: {(policy.RefugeeAcceptanceMultiplier * 100):F0}%");
        effects.Add($"Stability: {(policy.StabilityImpact >= 0 ? "+" : "")}{policy.StabilityImpact:F1}/year");
        effects.Add($"GDP Growth: {(policy.GdpGrowthImpact >= 0 ? "+" : "")}{policy.GdpGrowthImpact:F1}/year");
        effects.Add($"Diplomacy: {(policy.DiplomacyBonus >= 0 ? "+" : "")}{policy.DiplomacyBonus} with all");

        var effectsLabel = new Label { Text = "Effects: " + string.Join("  |  ", effects) };
        effectsLabel.AddThemeFontSizeOverride("font_size", 11);
        effectsLabel.AddThemeColorOverride("font_color", new Color("#81C784"));
        effectsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(effectsLabel);

        if (!isCurrent)
        {
            var costLabel = new Label { Text = $"Cost: {policy.CostPP} PP" };
            costLabel.AddThemeFontSizeOverride("font_size", 12);
            costLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            container.AddChild(costLabel);
        }

        return container;
    }

    private Color GetCategoryColor(string category)
    {
        return category switch
        {
            "Humanitarian" => new Color("#4CAF50"),
            "Balanced" => new Color("#2196F3"),
            "Security" => new Color("#FF9800"),
            "Isolationist" => new Color("#F44336"),
            _ => new Color("#757575")
        };
    }

    private Control CreateCrisisSummaryItem(MigrationSystem.ActiveCrisis crisis)
    {
        var type = MigrationSystem.GetAllCrisisTypes().FirstOrDefault(t => t.Id == crisis.TypeId);
        string typeName = type?.Name ?? crisis.TypeId;

        var container = new HBoxContainer();
        container.AddThemeConstantOverride("separation", 12);
        container.AddThemeStyleboxOverride("normal", MakeBox(new Color("#1E1E2E"), new Color("#3A3A5A")));

        // Crisis type badge
        var typeBadge = new Label { Text = typeName, CustomMinimumSize = new Vector2(160, 0) };
        typeBadge.AddThemeFontSizeOverride("font_size", 12);
        typeBadge.AddThemeColorOverride("font_color", GetCrisisColor(crisis.TypeId));
        typeBadge.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(typeBadge);

        // Source country
        var sourceName = CountryDatabase.GetOrCreate(crisis.SourceCountryId, "").Name;
        var sourceLabel = new Label { Text = $"From: {sourceName}", CustomMinimumSize = new Vector2(150, 0) };
        sourceLabel.AddThemeFontSizeOverride("font_size", 12);
        sourceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(sourceLabel);

        // Refugee count
        var countLabel = new Label { Text = $"{crisis.RefugeeCount:N0} refugees", CustomMinimumSize = new Vector2(140, 0) };
        countLabel.AddThemeFontSizeOverride("font_size", 12);
        countLabel.AddThemeColorOverride("font_color", new Color("#FFB347"));
        countLabel.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(countLabel);

        // Duration
        int currentYear = (int)((GameManager.Instance?.TotalDays ?? 0) / 365);
        int remaining = Math.Max(0, crisis.DurationYears - (currentYear - crisis.YearStarted));
        var durLabel = new Label { Text = $"{remaining} yr left", CustomMinimumSize = new Vector2(100, 0) };
        durLabel.AddThemeFontSizeOverride("font_size", 12);
        durLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        durLabel.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(durLabel);

        // Intensity bar
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = (float)(crisis.Intensity * 100),
            CustomMinimumSize = new Vector2(150, 16)
        };
        bar.AddThemeColorOverride("fill_color", new Color("#FF6B6B"));
        container.AddChild(bar);

        return container;
    }

    private Color GetCrisisColor(string typeId)
    {
        return typeId switch
        {
            "war" => new Color("#F44336"),
            "civil_war" => new Color("#E91E63"),
            "climate" => new Color("#2196F3"),
            "persecution" => new Color("#9C27B0"),
            _ => new Color("#757575")
        };
    }

    // ===== CRISES TAB =====
    private void BuildCrisesTab()
    {
        var allCrises = MigrationSystem.GetAllActiveCrises();
        string search = _searchBox?.Text?.Trim().ToLower() ?? "";

        if (!string.IsNullOrEmpty(search))
        {
            allCrises = allCrises.Where(c =>
            {
                var sourceName = CountryDatabase.GetOrCreate(c.SourceCountryId, "").Name.ToLower();
                var type = MigrationSystem.GetAllCrisisTypes().FirstOrDefault(t => t.Id == c.TypeId);
                var typeName = type?.Name.ToLower() ?? "";
                return sourceName.Contains(search) || typeName.Contains(search) || c.TypeId.Contains(search);
            }).ToList();
        }

        _resultsCountLabel.Text = $"Showing {allCrises.Count} / {MigrationSystem.GetAllActiveCrises().Count} crises";

        if (allCrises.Count == 0)
        {
            var none = new Label { Text = "No active crises.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", 14);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            none.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _contentContainer.AddChild(none);
            return;
        }

        foreach (var crisis in allCrises.OrderByDescending(c => c.RefugeeCount))
        {
            var card = CreateCrisisCard(crisis);
            _contentContainer.AddChild(card);
        }
    }

    private Control CreateCrisisCard(MigrationSystem.ActiveCrisis crisis)
    {
        var type = MigrationSystem.GetAllCrisisTypes().FirstOrDefault(t => t.Id == crisis.TypeId);
        string typeName = type?.Name ?? crisis.TypeId;
        string sourceName = CountryDatabase.GetOrCreate(crisis.SourceCountryId, "").Name;
        int currentYear = (int)((GameManager.Instance?.TotalDays ?? 0) / 365);
        int remaining = Math.Max(0, crisis.DurationYears - (currentYear - crisis.YearStarted));

        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 6);
        container.AddThemeStyleboxOverride("normal", MakeBox(
            new Color("#1E1E2E"),
            GetCrisisColor(crisis.TypeId)
        ));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        container.AddChild(header);

        var typeBadge = new Label { Text = $"[{typeName}]", CustomMinimumSize = new Vector2(160, 0) };
        typeBadge.AddThemeFontSizeOverride("font_size", 12);
        typeBadge.AddThemeColorOverride("font_color", GetCrisisColor(crisis.TypeId));
        typeBadge.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddChild(typeBadge);

        var nameLabel = new Label { Text = $"Crisis from {sourceName}" };
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        // Details
        var details = new HBoxContainer();
        details.AddThemeConstantOverride("separation", 24);
        container.AddChild(details);

        AddDetailItem(details, "Refugees", crisis.RefugeeCount.ToString("N0"));
        AddDetailItem(details, "Duration Left", $"{remaining} years");
        AddDetailItem(details, "Intensity", $"{crisis.Intensity * 100:F0}%");
        AddDetailItem(details, "Started", $"Year {crisis.YearStarted}");

        // Destinations
        var flows = MigrationSystem.GetFlowsForCountry(crisis.SourceCountryId, true);
        if (flows.Count > 0)
        {
            var destTitle = new Label { Text = "Main Destinations:" };
            destTitle.AddThemeFontSizeOverride("font_size", 12);
            destTitle.AddThemeColorOverride("font_color", UITheme.Gold);
            container.AddChild(destTitle);

            foreach (var flow in flows.Take(3))
            {
                var destName = CountryDatabase.GetOrCreate(flow.DestinationCountryId, "").Name;
                var destItem = new Label { Text = $"  → {destName}: {flow.AnnualFlow:N0}/yr" };
                destItem.AddThemeFontSizeOverride("font_size", 11);
                destItem.AddThemeColorOverride("font_color", UITheme.TextMain);
                container.AddChild(destItem);
            }
        }

        return container;
    }

    private void AddDetailItem(HBoxContainer parent, string label, string value)
    {
        var item = new VBoxContainer();
        item.AddThemeConstantOverride("separation", 2);
        parent.AddChild(item);

        var lbl = new Label { Text = label };
        lbl.AddThemeFontSizeOverride("font_size", 10);
        lbl.AddThemeColorOverride("font_color", UITheme.TextMuted);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        item.AddChild(lbl);

        var val = new Label { Text = value };
        val.AddThemeFontSizeOverride("font_size", 13);
        val.AddThemeColorOverride("font_color", Colors.White);
        val.HorizontalAlignment = HorizontalAlignment.Center;
        item.AddChild(val);
    }

    // ===== FLOWS TAB =====
    private void BuildFlowsTab()
    {
        var allFlows = MigrationSystem.GetAllFlows();
        string search = _searchBox?.Text?.Trim().ToLower() ?? "";

        if (!string.IsNullOrEmpty(search))
        {
            allFlows = allFlows.Where(f =>
            {
                var srcName = CountryDatabase.GetOrCreate(f.SourceCountryId, "").Name.ToLower();
                var dstName = CountryDatabase.GetOrCreate(f.DestinationCountryId, "").Name.ToLower();
                return srcName.Contains(search) || dstName.Contains(search) || f.PrimaryCause.Contains(search);
            }).ToList();
        }

        _resultsCountLabel.Text = $"Showing {allFlows.Count} / {MigrationSystem.GetAllFlows().Count} flows";

        if (allFlows.Count == 0)
        {
            var none = new Label { Text = "No active migration flows.", HorizontalAlignment = HorizontalAlignment.Center };
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

        AddTableHeader(headerRow, "Source", 150);
        AddTableHeader(headerRow, "Destination", 150);
        AddTableHeader(headerRow, "Annual Flow", 120);
        AddTableHeader(headerRow, "Cause", 100);
        AddTableHeader(headerRow, "Pressure", 100);
        AddTableHeader(headerRow, "Since", 80);

        foreach (var flow in allFlows)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _contentContainer.AddChild(row);

            var srcName = CountryDatabase.GetOrCreate(flow.SourceCountryId, "").Name;
            var dstName = CountryDatabase.GetOrCreate(flow.DestinationCountryId, "").Name;

            AddTableCell(row, srcName, 150);
            AddTableCell(row, dstName, 150);
            AddTableCell(row, $"{flow.AnnualFlow:N0}/yr", 120, new Color("#FFB347"));
            AddTableCell(row, GetCauseDisplayName(flow.PrimaryCause), 100, GetCauseColor(flow.PrimaryCause));
            AddTableCell(row, $"{(flow.Pressure * 100):F0}%", 100, flow.Pressure > 0.7 ? Colors.Red : (flow.Pressure > 0.4 ? new Color("#FF9800") : new Color("#4CAF50")));
            AddTableCell(row, $"Y{flow.YearStarted}", 80);
        }
    }

    private string GetCauseDisplayName(string causeId)
    {
        return causeId switch
        {
            "war" => "War",
            "civil_war" => "Civil War",
            "climate" => "Climate",
            "persecution" => "Persecution",
            _ => causeId
        };
    }

    private Color GetCauseColor(string causeId)
    {
        return causeId switch
        {
            "war" => new Color("#F44336"),
            "civil_war" => new Color("#E91E63"),
            "climate" => new Color("#2196F3"),
            "persecution" => new Color("#9C27B0"),
            _ => new Color("#757575")
        };
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
        var state = MigrationSystem.GetCountryState(playerId);
        var currentPolicy = MigrationSystem.GetBorderPolicy(state.BorderPolicyId);

        // Current policy
        var currentTitle = new Label { Text = "CURRENT BORDER POLICY" };
        currentTitle.AddThemeFontSizeOverride("font_size", 16);
        currentTitle.AddThemeColorOverride("font_color", new Color("#4CAF50"));
        _contentContainer.AddChild(currentTitle);

        if (currentPolicy != null)
        {
            var card = CreateBorderPolicyCard(currentPolicy, true);
            _contentContainer.AddChild(card);
        }

        _contentContainer.AddChild(new HSeparator());

        // Available policies
        var availTitle = new Label { Text = "AVAILABLE BORDER POLICIES" };
        availTitle.AddThemeFontSizeOverride("font_size", 16);
        availTitle.AddThemeColorOverride("font_color", UITheme.Gold);
        _contentContainer.AddChild(availTitle);

        foreach (var policy in MigrationSystem.GetAllBorderPolicies().Where(p => p.Id != state.BorderPolicyId))
        {
            var card = CreateBorderPolicyCard(policy, false);
            // Add SET button
            var actionRow = new HBoxContainer();
            actionRow.AddThemeConstantOverride("separation", 8);
            card.AddChild(actionRow);

            actionRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

            var canSet = MigrationSystem.CanSetBorderPolicy(playerId, policy.Id);
            var setBtn = new Button { Text = "SET POLICY", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 32) };
            setBtn.AddThemeFontSizeOverride("font_size", 12);
            setBtn.Disabled = !canSet;
            var pid = policy.Id;
            setBtn.Pressed += () => OnSetBorderPolicy(pid);
            UITheme.AddHoverScale(setBtn);
            actionRow.AddChild(setBtn);

            if (!canSet)
            {
                var reason = new Label { Text = "Requirements not met", CustomMinimumSize = new Vector2(140, 0) };
                reason.AddThemeFontSizeOverride("font_size", 11);
                reason.AddThemeColorOverride("font_color", Colors.Red);
                actionRow.AddChild(reason);
            }

            _contentContainer.AddChild(card);
        }

        _contentContainer.AddChild(new HSeparator());

        // Aid packages
        var aidTitle = new Label { Text = "HUMANITARIAN AID PACKAGES" };
        aidTitle.AddThemeFontSizeOverride("font_size", 16);
        aidTitle.AddThemeColorOverride("font_color", new Color("#2196F3"));
        _contentContainer.AddChild(aidTitle);

        var activeAids = state.ActiveAidPackages.Select(a => a.PackageId).ToHashSet();

        // Active aid packages
        if (state.ActiveAidPackages.Count > 0)
        {
            var activeSection = new Label { Text = "ACTIVE AID PACKAGES" };
            activeSection.AddThemeFontSizeOverride("font_size", 13);
            activeSection.AddThemeColorOverride("font_color", new Color("#4CAF50"));
            _contentContainer.AddChild(activeSection);

            foreach (var aid in state.ActiveAidPackages)
            {
                var pkg = MigrationSystem.GetAllAidPackages().FirstOrDefault(p => p.Id == aid.PackageId);
                if (pkg != null)
                {
                    var card = CreateAidPackageCard(pkg, true, aid.YearStarted);
                    _contentContainer.AddChild(card);
                }
            }

            _contentContainer.AddChild(new HSeparator());
        }

        // Available aid packages
        var availAidTitle = new Label { Text = "AVAILABLE AID PACKAGES" };
        availAidTitle.AddThemeFontSizeOverride("font_size", 13);
        availAidTitle.AddThemeColorOverride("font_color", UITheme.Gold);
        _contentContainer.AddChild(availAidTitle);

        foreach (var pkg in MigrationSystem.GetAllAidPackages())
        {
            if (activeAids.Contains(pkg.Id)) continue;

            var card = CreateAidPackageCard(pkg, false, 0);
            var actionRow = new HBoxContainer();
            actionRow.AddThemeConstantOverride("separation", 8);
            card.AddChild(actionRow);

            actionRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

            var canActivate = MigrationSystem.CanActivateAidPackage(playerId, pkg.Id);
            var activateBtn = new Button { Text = "ACTIVATE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 32) };
            activateBtn.AddThemeFontSizeOverride("font_size", 12);
            activateBtn.Disabled = !canActivate;
            var pid = pkg.Id;
            activateBtn.Pressed += () => OnActivateAidPackage(pid);
            UITheme.AddHoverScale(activateBtn);
            actionRow.AddChild(activateBtn);

            if (!canActivate)
            {
                var reason = new Label { Text = "Requirements not met", CustomMinimumSize = new Vector2(140, 0) };
                reason.AddThemeFontSizeOverride("font_size", 11);
                reason.AddThemeColorOverride("font_color", Colors.Red);
                actionRow.AddChild(reason);
            }

            _contentContainer.AddChild(card);
        }
    }

    private Control CreateAidPackageCard(MigrationSystem.AidPackage pkg, bool isActive, int yearStarted)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 6);
        container.AddThemeStyleboxOverride("normal", MakeBox(
            isActive ? new Color("#1A2A3A") : new Color("#1E1E2E"),
            isActive ? new Color("#2196F3") : new Color("#3A3A5A")
        ));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        container.AddChild(header);

        var nameLabel = new Label { Text = pkg.Name + (isActive ? " (ACTIVE)" : "") };
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var descLabel = new Label { Text = pkg.Description };
        descLabel.AddThemeFontSizeOverride("font_size", 12);
        descLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(descLabel);

        // Effects
        var effects = new List<string>();
        effects.Add($"Supports: {pkg.RefugeesSupported:N0} refugees");
        effects.Add($"Cost: {pkg.CostPP} PP + {(pkg.CostBudgetPercent * 100):F0}% budget");
        if (pkg.StabilityBonus > 0) effects.Add($"Stability: +{pkg.StabilityBonus:F1}");
        if (pkg.AssimilationBoost > 0) effects.Add($"Assimilation: +{(pkg.AssimilationBoost * 100):F0}%");
        if (pkg.GdpGrowthBonus > 0) effects.Add($"GDP Growth: +{(pkg.GdpGrowthBonus * 100):F0}%");
        if (pkg.ReturnRateBoost > 0) effects.Add($"Return Rate: +{(pkg.ReturnRateBoost * 100):F0}%");
        effects.Add($"Reputation: +{pkg.ReputationBonus}");

        var effectsLabel = new Label { Text = "Effects: " + string.Join("  |  ", effects) };
        effectsLabel.AddThemeFontSizeOverride("font_size", 11);
        effectsLabel.AddThemeColorOverride("font_color", new Color("#81C784"));
        effectsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(effectsLabel);

        if (isActive)
        {
            int currentYear = (int)((GameManager.Instance?.TotalDays ?? 0) / 365);
            int remaining = Math.Max(0, 3 - (currentYear - yearStarted));
            var durLabel = new Label { Text = $"Expires in: {remaining} year(s)" };
            durLabel.AddThemeFontSizeOverride("font_size", 11);
            durLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            container.AddChild(durLabel);
        }

        // Tooltip
        container.TooltipText = BuildAidPackageTooltip(pkg);

        return container;
    }

    private string BuildAidPackageTooltip(MigrationSystem.AidPackage p)
    {
        var lines = new List<string>();
        lines.Add(p.Description);
        lines.Add("");
        lines.Add($"Cost: {p.CostPP} PP + {(p.CostBudgetPercent * 100):F0}% budget");
        lines.Add($"Refugees Supported: {p.RefugeesSupported:N0}");
        lines.Add("");
        lines.Add("Effects:");
        if (p.StabilityBonus > 0) lines.Add($"  Stability: +{p.StabilityBonus:F1}");
        if (p.AssimilationBoost > 0) lines.Add($"  Assimilation Boost: +{(p.AssimilationBoost * 100):F0}%");
        if (p.GdpGrowthBonus > 0) lines.Add($"  GDP Growth: +{(p.GdpGrowthBonus * 100):F0}%");
        if (p.ReturnRateBoost > 0) lines.Add($"  Return Rate Boost: +{(p.ReturnRateBoost * 100):F0}%");
        lines.Add($"  Reputation Bonus: +{p.ReputationBonus}");
        return string.Join("\n", lines);
    }

    private void OnSetBorderPolicy(string policyId)
    {
        if (GameManager.Instance.HasPlayer)
        {
            MigrationSystem.SetBorderPolicy(GameManager.Instance.PlayerCountryId, policyId);
            RefreshContent();
        }
    }

    private void OnActivateAidPackage(string packageId)
    {
        if (GameManager.Instance.HasPlayer)
        {
            MigrationSystem.ActivateAidPackage(GameManager.Instance.PlayerCountryId, packageId);
            RefreshContent();
        }
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
        GD.Print("MigrationPanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}