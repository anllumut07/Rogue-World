using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Climate Panel (M55.3 + Stage 2 UI Upgrade): HUD CLIMATE button opens this.
/// Tabs: Global (warming chart), Emissions (country table), Policies (policy cards), Agreements, Tipping Points.
/// Built with reusable UI components from Scripts/UI/
/// - Panel frame via GamePanel
/// - Category tabs via TabBar
/// - Search filter via SearchBox with result count
/// - Country rows via ListRow
/// - Category badges via Chip
/// - Progress bars via StatBar
/// - Rich tooltips via TooltipBuilder
/// - Empty states via EmptyState
/// </summary>
public partial class ClimatePanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    // === UI Components (using reusable components) ===
    private TabBar _tabBar;
    private SearchBox _searchBox;
    private VBoxContainer _contentContainer;
    private ScrollContainer _contentScroll;
    private EmptyState _emptyState;

    // Tabs in order
    private static readonly string[] TabIds = { "Global", "Emissions", "Policies", "Agreements", "TippingPoints" };
    private static readonly string[] TabLabels = { "GLOBAL", "EMISSIONS", "POLICIES", "AGREEMENTS", "TIPPING POINTS" };

    private string _activeTab = "Global";
    private string _searchText = "";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[ClimatePanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new ClimatePanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "CLIMATE & ENVIRONMENT";
        MinSize = new Vector2(1000, 560);
        MaxSize = new Vector2(1400, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        ConnectSignals();
        RefreshContent();
        IsOpen = true;

        GD.Print("ClimatePanel: hazır (Stage 2 UI)");
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
        // Content goes into GamePanel's ContentContainer
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // === Category Tabs (using TabBar component) ===
        _tabBar = new TabBar
        {
            TabIds = TabIds,
            TabLabels = TabLabels,
            SelectedIndex = 0,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _tabBar.TabChanged += OnTabChanged;
        content.AddChild(_tabBar);

        // === Search Box (using SearchBox component) - for Emissions and Policies tabs ===
        _searchBox = new SearchBox
        {
            PlaceholderText = "Filter...",
            ShowResultCount = true,
            ShowClearButton = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Visible = false // Only show for Emissions and Policies tabs
        };
        _searchBox.TextChanged += OnSearchTextChanged;
        _searchBox.SearchSubmitted += OnSearchSubmitted;
        _searchBox.Cleared += OnSearchCleared;
        content.AddChild(_searchBox);

        content.AddChild(new HSeparator());

        // === Content Area ===
        _contentScroll = new ScrollContainer();
        _contentScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _contentScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(_contentScroll);

        _contentContainer = new VBoxContainer();
        _contentContainer.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _contentContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _contentScroll.AddChild(_contentContainer);

        _emptyState = EmptyState.CreateGeneric("No Data", "No climate data available.");
        _emptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _emptyState.Visible = false;
        _contentContainer.AddChild(_emptyState);
    }

    private void ConnectSignals()
    {
        // Could listen for GameManager.PlayerCountryDataChanged if needed
    }

    private void OnTabChanged(string tabId, int tabIndex)
    {
        _activeTab = tabId;
        UpdateSearchVisibility();
        RefreshContent();
    }

    private void UpdateSearchVisibility()
    {
        bool showSearch = _activeTab == "Emissions" || _activeTab == "Policies";
        _searchBox.Visible = showSearch;
        if (showSearch) _searchBox.Clear();
    }

    private void OnSearchTextChanged(string text)
    {
        _searchText = text.Trim().ToLower();
        RefreshContent();
    }

    private void OnSearchSubmitted(string text) { }

    private void OnSearchCleared()
    {
        _searchText = "";
        RefreshContent();
    }

    private void RefreshContent()
    {
        foreach (var child in _contentContainer.GetChildren())
        {
            if (child == _emptyState) continue;
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
            case "TippingPoints":
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
        metricsRow.AddThemeConstantOverride("separation", GameTheme.SpaceL);
        _contentContainer.AddChild(metricsRow);

        AddMetricCard(metricsRow, "CO₂ Concentration", $"{co2:F1} ppm", "Current atmospheric CO₂ level", GameTheme.TextPrimary);
        AddMetricCard(metricsRow, "Global Warming", $"+{warming:F2}°C", "Above pre-industrial (1850-1900)", GameTheme.Red);
        AddMetricCard(metricsRow, "Annual Emissions", $"{annualEmissions:F2} GtCO₂/yr", "Current yearly global emissions", GameTheme.Orange);
        AddMetricCard(metricsRow, "Cumulative Emissions", $"{cumulativeEmissions:F1} GtCO₂", "Since pre-industrial", GameTheme.Blue);
        AddMetricCard(metricsRow, "Triggered Tipping Points", $"{tpCount} / {tippingPoints.Count}", "Irreversible thresholds crossed", tpCount > 0 ? GameTheme.Red : GameTheme.Green);

        _contentContainer.AddChild(new HSeparator());

        // Warming trend chart (real chart using Godot drawing)
        var chartTitle = new Label { Text = "WARMING TREND (Historical)" };
        chartTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        chartTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        _contentContainer.AddChild(chartTitle);

        var chartControl = new WarmingChartControl();
        chartControl.CustomMinimumSize = new Vector2(0, 280);
        chartControl.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        chartControl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _contentContainer.AddChild(chartControl);

        _contentContainer.AddChild(new HSeparator());

        // Tipping points summary
        var tpTitle = new Label { Text = "TIPPING POINTS SUMMARY" };
        tpTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        tpTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
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
        card.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        card.AddThemeStyleboxOverride("normal", GameTheme.CardBox());
        parent.AddChild(card);

        var titleLabel = new Label { Text = title };
        titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        titleLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(titleLabel);

        var valueLabel = new Label { Text = value };
        valueLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeLarge);
        valueLabel.AddThemeColorOverride("font_color", valueColor);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(valueLabel);

        var descLabel = new Label { Text = desc };
        descLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
        descLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
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
        if (!string.IsNullOrEmpty(_searchText))
        {
            states = states.Where(s => s.CountryName.ToLower().Contains(_searchText) || s.CountryId.ToLower().Contains(_searchText)).ToList();
        }

        int totalCountries = CountryDatabase.All().Count();
        _searchBox.UpdateResultCount(states.Count, totalCountries);

        if (states.Count == 0)
        {
            _emptyState.SetContent(
                "No Results Found",
                $"No countries match \"{_searchText}\". Try a different search term or clear filters.",
                "🔍"
            );
            _emptyState.Visible = true;
            return;
        }

        _emptyState.Visible = false;

        // Header row
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _contentContainer.AddChild(headerRow);

        AddTableHeader(headerRow, "Country", 160);
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
            row.AddThemeConstantOverride("separation", GameTheme.SpaceS);
            _contentContainer.AddChild(row);

            AddTableCell(row, countryName, 160);
            AddTableCell(row, $"{emissions:F1}", 160, GetEmissionsColor(emissions));
            AddTableCell(row, $"{(state.RenewableShare * 100):F0}%", 100, GameTheme.Green);
            AddTableCell(row, $"{(state.NuclearShare * 100):F0}%", 80, GameTheme.Orange);
            AddTableCell(row, $"{(state.CoalShare * 100):F0}%", 80, new Color("#795548"));
            AddTableCell(row, $"{(state.OilShare * 100):F0}%", 80, new Color("#212121"));
            AddTableCell(row, $"{(state.GasShare * 100):F0}%", 80, GameTheme.Blue);
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
        if (emissionsMt > 5000) return GameTheme.Red;
        if (emissionsMt > 1000) return GameTheme.Orange;
        if (emissionsMt > 100) return GameTheme.Yellow;
        return GameTheme.Green;
    }

    private void AddTableHeader(HBoxContainer parent, string text, float width)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0) };
        label.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
        label.AddThemeColorOverride("font_color", GameTheme.Gold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        parent.AddChild(label);
    }

    private void AddTableCell(HBoxContainer parent, string text, float width, Color? color = null)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0) };
        label.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
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
            _emptyState.SetContent("No Player Country", "Select a country to play first.", "🌍");
            _emptyState.Visible = true;
            return;
        }

        string playerId = GameManager.Instance.PlayerCountryId;
        var available = ClimateSystem.GetAvailablePolicies(playerId);
        var active = ClimateSystem.GetActivePolicies(playerId);

        // Filter by search
        if (!string.IsNullOrEmpty(_searchText))
        {
            available = available.Where(p => p.Name.ToLower().Contains(_searchText) || p.Category.ToLower().Contains(_searchText) || p.Description.ToLower().Contains(_searchText)).ToList();
        }

        _searchBox.UpdateResultCount(available.Count, ClimateSystem.GetAllPolicies().Count);

        // Active policies section
        var activeTitle = new Label { Text = "ACTIVE POLICIES" };
        activeTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        activeTitle.AddThemeColorOverride("font_color", GameTheme.Orange);
        _contentContainer.AddChild(activeTitle);

        if (active.Count == 0)
        {
            var none = new Label { Text = "No active policies.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            none.AddThemeColorOverride("font_color", GameTheme.TextMuted);
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
        availTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        availTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        _contentContainer.AddChild(availTitle);

        if (available.Count == 0)
        {
            _emptyState.SetContent("No Policies Available", "Check warming threshold, PP, and cooldown.", "📋");
            _emptyState.Visible = true;
        }
        else
        {
            _emptyState.Visible = false;
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
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(isActive ? GameTheme.Green : GameTheme.Blue, true));

        // Header: name + category
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        var catBadge = Chip.CreateCategory(policy.Category, GetCategoryColor(policy.Category));
        catBadge.FontSize = GameTheme.FontSizeTiny;
        header.AddChild(catBadge);

        var nameLabel = new Label { Text = policy.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        // Cost info
        var costLabel = new Label();
        costLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        costLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        costLabel.Text = $"Cost: {policy.CostPP} PP  |  Budget: {(policy.CostBudgetPercent * 100):F0}%  |  Min Warming: +{policy.MinWarming:F1}°C";
        container.AddChild(costLabel);

        // Description
        var descLabel = new Label { Text = policy.Description };
        descLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        descLabel.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
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
            effectsLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
            effectsLabel.AddThemeColorOverride("font_color", GameTheme.GreenBright);
            effectsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            container.AddChild(effectsLabel);
        }

        // Action button
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(actionRow);

        actionRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        if (isActive)
        {
            var revokeBtn = new Button { Text = "REVOKE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 32) };
            revokeBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            revokeBtn.Pressed += () => OnRevokePolicy(policy.Id);
            GameTheme.StyleButton(revokeBtn);
            actionRow.AddChild(revokeBtn);
        }
        else
        {
            var enactBtn = new Button { Text = "ENACT", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 32) };
            enactBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            var canEnact = ClimateSystem.CanEnactPolicy(GameManager.Instance.PlayerCountryId, policy.Id);
            enactBtn.Disabled = !canEnact;
            enactBtn.Pressed += () => OnEnactPolicy(policy.Id);
            GameTheme.StyleButton(enactBtn);
            actionRow.AddChild(enactBtn);

            if (!canEnact)
            {
                var reason = new Label { Text = "Requirements not met", CustomMinimumSize = new Vector2(140, 0) };
                reason.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
                reason.AddThemeColorOverride("font_color", GameTheme.Red);
                actionRow.AddChild(reason);
            }
        }

        // Tooltip
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
            "Pricing" => GameTheme.Orange,
            "Incentives" => GameTheme.Green,
            "Regulation" => GameTheme.Red,
            "Nature" => new Color("#8BC34A"),
            "Energy" => GameTheme.Blue,
            "Transport" => GameTheme.Purple,
            "Adaptation" => new Color("#00BCD4"),
            _ => GameTheme.TextMuted
        };
    }

    private string BuildPolicyTooltip(ClimateSystem.Policy p)
    {
        var data = new TooltipBuilder.TooltipData
        {
            Title = p.Name,
            Description = p.Description,
            Category = p.Category,
            CategoryColor = GetCategoryColor(p.Category),
        };

        data.Costs["Political Power"] = p.CostPP.ToString();
        data.Costs["Budget"] = $"{(p.CostBudgetPercent * 100):F0}%";
        data.Requirements["Min Warming"] = $"+{p.MinWarming:F1}°C";

        if (p.EmissionReduction > 0) data.Effects["Emission Reduction"] = $"{(p.EmissionReduction * 100):F0}%";
        if (p.RenewableBoost > 0) data.Effects["Renewable Share"] = $"+{(p.RenewableBoost * 100):F0}%";
        if (p.NuclearBoost > 0) data.Effects["Nuclear Share"] = $"+{(p.NuclearBoost * 100):F0}%";
        if (p.AdaptationBoost > 0) data.Effects["Adaptation Level"] = $"+{(p.AdaptationBoost * 100):F0}%";
        if (p.LandUseImprovement > 0) data.Effects["Land Use Improvement"] = $"+{(p.LandUseImprovement * 100):F0}%";
        if (p.EnergyCostIncrease != 0) data.Effects["Energy Cost"] = $"{(p.EnergyCostIncrease * 100):F0}%";
        if (p.GdpGrowthImpact != 0) data.Effects["GDP Growth"] = $"{(p.GdpGrowthImpact * 100):F0}%";
        if (p.PopularityImpact != 0) data.Effects["Popularity"] = $"{(p.PopularityImpact >= 0 ? "+" : "")}{p.PopularityImpact}";
        if (!string.IsNullOrEmpty(p.UnlockTech)) data.Effects["Unlocks Tech"] = p.UnlockTech;

        return TooltipBuilder.BuildText(data);
    }

    // ===== AGREEMENTS TAB =====
    private void BuildAgreementsTab()
    {
        if (!GameManager.IsAvailable || !GameManager.Instance.HasPlayer)
        {
            _emptyState.SetContent("No Player Country", "Select a country to play first.", "🌍");
            _emptyState.Visible = true;
            return;
        }

        string playerId = GameManager.Instance.PlayerCountryId;
        var state = ClimateSystem.GetCountryState(playerId);
        var allAgreements = ClimateSystem.GetAllAgreements();

        // Signed agreements
        var signedTitle = new Label { Text = "SIGNED AGREEMENTS" };
        signedTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        signedTitle.AddThemeColorOverride("font_color", GameTheme.Green);
        _contentContainer.AddChild(signedTitle);

        var signed = allAgreements.Where(a => state.SignedAgreements.Contains(a.Id)).ToList();
        if (signed.Count == 0)
        {
            var none = new Label { Text = "No agreements signed.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            none.AddThemeColorOverride("font_color", GameTheme.TextMuted);
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
        availTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        availTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        _contentContainer.AddChild(availTitle);

        var available = allAgreements.Where(a => !state.SignedAgreements.Contains(a.Id)).ToList();
        if (available.Count == 0)
        {
            var none = new Label { Text = "All agreements signed.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            none.AddThemeColorOverride("font_color", GameTheme.TextMuted);
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
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(isSigned ? GameTheme.Green : GameTheme.Blue, true));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        var nameLabel = new Label { Text = agr.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var targetLabel = new Label { Text = $"Target: ≤{agr.TargetWarmingLimit:F1}°C", CustomMinimumSize = new Vector2(140, 0) };
        targetLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        targetLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        targetLabel.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(targetLabel);

        var descLabel = new Label { Text = agr.Description };
        descLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(descLabel);

        var details = new Label();
        details.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
        details.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        details.Text = $"Review Cycle: {agr.ReviewCycleYears} year(s)  |  Min Participants: {agr.MinParticipants}  |  Prestige Bonus: +{agr.PrestigeBonus}  |  Compliance Bonus: {(agr.ComplianceBonus * 100):F0}%  |  Transparency: {(agr.TransparencyRequirement ? "Required" : "Optional")}";
        container.AddChild(details);

        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(actionRow);

        actionRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        if (isSigned)
        {
            var withdrawBtn = new Button { Text = "WITHDRAW", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 32) };
            withdrawBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            withdrawBtn.Pressed += () => OnWithdrawAgreement(agr.Id);
            GameTheme.StyleButton(withdrawBtn);
            actionRow.AddChild(withdrawBtn);
        }
        else
        {
            var signBtn = new Button { Text = "SIGN", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 32) };
            signBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            var canSign = ClimateSystem.CanSignAgreement(GameManager.Instance.PlayerCountryId, agr.Id);
            signBtn.Disabled = !canSign;
            signBtn.Pressed += () => OnSignAgreement(agr.Id);
            GameTheme.StyleButton(signBtn);
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
        titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        titleLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
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
        container.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(tp.Triggered ? GameTheme.Red : GameTheme.Orange, true));

        // Status indicator
        var status = new Label { Text = tp.Triggered ? "● TRIGGERED" : "○ PENDING", CustomMinimumSize = new Vector2(120, 0) };
        status.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        status.AddThemeColorOverride("font_color", tp.Triggered ? GameTheme.Red : GameTheme.Green);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(status);

        // Name
        var name = new Label { Text = tp.Name };
        name.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        container.AddChild(name);

        // Thresholds
        var thresholds = new Label { Text = $"CO₂: {tp.ThresholdCO2:F0} ppm  |  Warming: +{tp.ThresholdWarming:F1}°C  |  Extra: +{tp.ExtraWarming:F2}°C", CustomMinimumSize = new Vector2(320, 0) };
        thresholds.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        thresholds.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        thresholds.HorizontalAlignment = HorizontalAlignment.Right;
        container.AddChild(thresholds);

        // Progress bar
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
        bar.AddThemeColorOverride("fill_color", tp.Triggered ? GameTheme.Red : GameTheme.Orange);
        container.AddChild(bar);

        return container;
    }

    private Control CreateTippingPointCard(ClimateSystem.TippingPoint tp)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(tp.Triggered ? GameTheme.Red : GameTheme.Orange, true));

        // Header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        var status = new Label { Text = tp.Triggered ? "■ TRIGGERED" : "□ PENDING", CustomMinimumSize = new Vector2(120, 0) };
        status.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        status.AddThemeColorOverride("font_color", tp.Triggered ? GameTheme.Red : GameTheme.Green);
        header.AddChild(status);

        var name = new Label { Text = tp.Name };
        name.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(name);

        var extraWarming = new Label { Text = $"+{tp.ExtraWarming:F2}°C extra warming", CustomMinimumSize = new Vector2(160, 0) };
        extraWarming.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        extraWarming.AddThemeColorOverride("font_color", tp.Triggered ? GameTheme.Red : GameTheme.Orange);
        extraWarming.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(extraWarming);

        // Thresholds
        var thresholds = new Label { Text = $"Trigger Thresholds: CO₂ ≥ {tp.ThresholdCO2:F0} ppm  OR  Warming ≥ +{tp.ThresholdWarming:F1}°C" };
        thresholds.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        thresholds.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        container.AddChild(thresholds);

        // Current status
        double co2 = ClimateSystem.GetGlobalCO2();
        double warming = ClimateSystem.GetGlobalWarming();
        var current = new Label { Text = $"Current: {co2:F1} ppm CO₂  |  +{warming:F2}°C warming" };
        current.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        current.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
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
        bar.AddThemeColorOverride("fill_color", tp.Triggered ? GameTheme.Red : GameTheme.Orange);
        container.AddChild(bar);

        var progressLabel = new Label { Text = $"Progress to threshold: {progress * 100:F0}%" };
        progressLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
        progressLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        progressLabel.HorizontalAlignment = HorizontalAlignment.Right;
        container.AddChild(progressLabel);

        if (tp.Triggered)
        {
            var warning = new Label { Text = "⚠ THIS TIPPING POINT HAS BEEN TRIGGERED — Extra warming is now permanently applied!" };
            warning.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            warning.AddThemeColorOverride("font_color", GameTheme.Red);
            warning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            container.AddChild(warning);
        }

        return container;
    }

    public override void Close()
    {
        GD.Print("ClimatePanel: kapatılıyor");
        base.Close();
    }
}

/// <summary>
/// Custom control for drawing the warming trend chart (CO₂ and temperature anomaly over time).
/// Uses deterministic data from ClimateSystem.GetHistory().
/// </summary>
public partial class WarmingChartControl : Control
{
    public override void _Draw()
    {
        var history = ClimateSystem.GetHistory();
        if (history == null || history.Count < 2)
        {
            // No history yet - draw placeholder message
            DrawString(GetThemeFont("font", "Label"), new Vector2(Size.X / 2 - 100, Size.Y / 2),
                "No historical data yet. Chart appears after first year.", HorizontalAlignment.Center, -1, 14,
                GameTheme.TextMuted);
            return;
        }

        DrawChart(history);
    }

    private void DrawChart(List<ClimateSystem.YearlyClimateSnapshot> history)
    {
        // Chart area with margins
        float marginLeft = 70;
        float marginRight = 20;
        float marginTop = 30;
        float marginBottom = 50;
        float chartWidth = Size.X - marginLeft - marginRight;
        float chartHeight = Size.Y - marginTop - marginBottom;

        if (chartWidth <= 0 || chartHeight <= 0) return;

        // Find min/max for CO2 and Warming
        double minCO2 = history.Min(h => h.CO2);
        double maxCO2 = history.Max(h => h.CO2);
        double minWarming = history.Min(h => h.Warming);
        double maxWarming = history.Max(h => h.Warming);

        // Add small padding
        double co2Range = maxCO2 - minCO2;
        double warmingRange = maxWarming - minWarming;
        if (co2Range < 1) { minCO2 -= 0.5; maxCO2 += 0.5; co2Range = maxCO2 - minCO2; }
        if (warmingRange < 0.1) { minWarming -= 0.05; maxWarming += 0.05; warmingRange = maxWarming - minWarming; }

        // Colors
        Color co2Color = new Color("#FF6B6B");      // Red for CO2
        Color warmingColor = new Color("#4ECDC4");  // Teal for warming
        Color gridColor = new Color("#3A3A5A");
        Color axisColor = new Color("#5A5A7A");
        Color textColor = GameTheme.TextMuted;

        // Draw background
        DrawRect(new Rect2(marginLeft, marginTop, chartWidth, chartHeight), new Color("#1A1A2E"), false);
        DrawRect(new Rect2(marginLeft, marginTop, chartWidth, chartHeight), new Color("#3A3A5A"), true); // Border

        // Draw grid lines and labels (Y-axis for CO2 on left, Warming on right)
        int gridLines = 5;
        for (int i = 0; i <= gridLines; i++)
        {
            float y = marginTop + (i / (float)gridLines) * chartHeight;
            // Horizontal grid line
            DrawLine(new Vector2(marginLeft, y), new Vector2(marginLeft + chartWidth, y), gridColor, 1);

            // CO2 labels (left)
            double co2Val = maxCO2 - (i / (float)gridLines) * co2Range;
            DrawString(GetThemeFont("font", "Label"), new Vector2(5, y + 5), $"{co2Val:F0} ppm", HorizontalAlignment.Right, -1, 11, textColor);

            // Warming labels (right)
            double warmVal = maxWarming - (i / (float)gridLines) * warmingRange;
            DrawString(GetThemeFont("font", "Label"), new Vector2(Size.X - 60, y + 5), $"+{warmVal:F2}°C", HorizontalAlignment.Left, -1, 11, textColor);
        }

        // Draw X-axis labels (years)
        int yearCount = history.Count;
        int labelStep = Math.Max(1, yearCount / 8); // Show ~8 year labels
        for (int i = 0; i < yearCount; i += labelStep)
        {
            float x = marginLeft + (i / (float)(yearCount - 1)) * chartWidth;
            int year = history[i].Year;
            DrawString(GetThemeFont("font", "Label"), new Vector2(x - 15, Size.Y - 20), $"Y{year}", HorizontalAlignment.Center, -1, 11, textColor);
        }

        // Draw CO2 line (left Y-axis)
        DrawDataLine(history, marginLeft, marginTop, chartWidth, chartHeight,
            h => h.CO2, minCO2, maxCO2, co2Color, "CO₂");

        // Draw Warming line (right Y-axis)
        DrawDataLine(history, marginLeft, marginTop, chartWidth, chartHeight,
            h => h.Warming, minWarming, maxWarming, warmingColor, "Warming");

        // Draw legend
        float legendX = marginLeft + 10;
        float legendY = marginTop + 10;
        DrawRect(new Rect2(legendX, legendY, 15, 15), co2Color);
        DrawString(GetThemeFont("font", "Label"), new Vector2(legendX + 20, legendY + 12), "CO₂ (ppm)", HorizontalAlignment.Left, -1, 12, co2Color);
        DrawRect(new Rect2(legendX + 100, legendY, 15, 15), warmingColor);
        DrawString(GetThemeFont("font", "Label"), new Vector2(legendX + 120, legendY + 12), "Warming (°C)", HorizontalAlignment.Left, -1, 12, warmingColor);

        // Current values at the end
        var last = history[^1];
        string currentInfo = $"Current: {last.CO2:F1} ppm CO₂ | +{last.Warming:F2}°C warming";
        DrawString(GetThemeFont("font", "Label"), new Vector2(Size.X - 5, Size.Y - 30), currentInfo, HorizontalAlignment.Right, -1, 12, GameTheme.Gold);
    }

    private void DrawDataLine(List<ClimateSystem.YearlyClimateSnapshot> history, float marginLeft, float marginTop,
        float chartWidth, float chartHeight, Func<ClimateSystem.YearlyClimateSnapshot, double> valueSelector,
        double minVal, double maxVal, Color lineColor, string label)
    {
        if (history.Count < 2) return;

        double range = maxVal - minVal;
        if (range <= 0) return;

        Vector2 prevPoint = Vector2.Zero;
        bool first = true;

        for (int i = 0; i < history.Count; i++)
        {
            double val = valueSelector(history[i]);
            float x = marginLeft + (i / (float)(history.Count - 1)) * chartWidth;
            float y = marginTop + chartHeight - (float)((val - minVal) / range) * chartHeight;

            if (first)
            {
                prevPoint = new Vector2(x, y);
                first = false;
            }
            else
            {
                DrawLine(prevPoint, new Vector2(x, y), lineColor, 2);
                prevPoint = new Vector2(x, y);
            }
        }
    }
}