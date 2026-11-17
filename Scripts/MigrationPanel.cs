using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Migration Panel (M56 + Stage 2 UI Upgrade): HUD MIGRATION button opens this.
/// Tabs: Overview, Crises, Flows, Policies.
/// Built with reusable UI components from Scripts/UI/
/// - Panel frame via GamePanel
/// - Category tabs via TabBar
/// - Search filter via SearchBox with result count
/// - Country/crisis/flow rows via ListRow
/// - Category badges via Chip
/// - Progress bars via StatBar
/// - Rich tooltips via TooltipBuilder
/// - Empty states via EmptyState
/// </summary>
public partial class MigrationPanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    // === UI Components (using reusable components) ===
    private TabBar _tabBar;
    private SearchBox _searchBox;
    private VBoxContainer _contentContainer;
    private ScrollContainer _contentScroll;
    private EmptyState _emptyState;

    // Tabs in order
    private static readonly string[] TabIds = { "Overview", "Crises", "Flows", "Policies" };
    private static readonly string[] TabLabels = { "OVERVIEW", "CRISES", "FLOWS", "POLICIES" };

    private string _activeTab = "Overview";
    private string _searchText = "";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[MigrationPanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new MigrationPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "MIGRATION & REFUGEES";
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

        GD.Print("MigrationPanel: hazır (Stage 2 UI)");
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

        // === Search Box (using SearchBox component) - for Crises and Flows tabs ===
        _searchBox = new SearchBox
        {
            PlaceholderText = "Filter...",
            ShowResultCount = true,
            ShowClearButton = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Visible = false // Only show for Crises and Flows tabs
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

        _emptyState = EmptyState.CreateGeneric("No Data", "No migration data available.");
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
        bool showSearch = _activeTab == "Crises" || _activeTab == "Flows";
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
            _emptyState.SetContent(
                "No Player Country",
                "Select a country to play first.",
                "🌍"
            );
            _emptyState.Visible = true;
            return;
        }

        _emptyState.Visible = false;

        string playerId = GameManager.Instance.PlayerCountryId;
        var state = MigrationSystem.GetCountryState(playerId);
        var policy = MigrationSystem.GetBorderPolicy(state.BorderPolicyId);
        int totalRefugees = MigrationSystem.GetTotalGlobalRefugees();
        int totalCrises = MigrationSystem.GetAllCrisesCount();
        int playerRefugees = state.RefugeePopulation;
        int playerPending = state.AsylumSeekersPending;

        // Key metrics row using StatBar components
        var metricsRow = new HBoxContainer();
        metricsRow.AddThemeConstantOverride("separation", GameTheme.SpaceL);
        _contentContainer.AddChild(metricsRow);

        AddMetricCard(metricsRow, "Global Refugees", totalRefugees.ToString("N0"), "Worldwide displaced population", GameTheme.TextPrimary);
        AddMetricCard(metricsRow, "Active Crises", totalCrises.ToString(), "Ongoing displacement situations", GameTheme.Orange);
        AddMetricCard(metricsRow, "Your Refugees", playerRefugees.ToString("N0"), "Refugees currently in your country", GameTheme.Blue);
        AddMetricCard(metricsRow, "Pending Asylum", playerPending.ToString("N0"), "Awaiting decision", GameTheme.Yellow);

        _contentContainer.AddChild(new HSeparator());

        // Current border policy
        var policyTitle = new Label { Text = "CURRENT BORDER POLICY" };
        policyTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        policyTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        _contentContainer.AddChild(policyTitle);

        if (policy != null)
        {
            var policyCard = CreateBorderPolicyCard(policy, true);
            _contentContainer.AddChild(policyCard);
        }

        _contentContainer.AddChild(new HSeparator());

        // Quick policy change buttons
        var changeTitle = new Label { Text = "CHANGE BORDER POLICY" };
        changeTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        changeTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        _contentContainer.AddChild(changeTitle);

        var policyGrid = new GridContainer();
        policyGrid.Columns = 2;
        policyGrid.AddThemeConstantOverride("h_separation", GameTheme.SpaceM);
        policyGrid.AddThemeConstantOverride("v_separation", GameTheme.SpaceM);
        _contentContainer.AddChild(policyGrid);

        foreach (var p in MigrationSystem.GetAllBorderPolicies())
        {
            var btn = new Button
            {
                Text = p.Name,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 80)
            };
            btn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            btn.TooltipText = BuildBorderPolicyTooltip(p);
            var canChange = MigrationSystem.CanSetBorderPolicy(playerId, p.Id);
            btn.Disabled = !canChange || p.Id == state.BorderPolicyId;
            var pid = p.Id;
            btn.Pressed += () => OnSetBorderPolicy(pid);
            GameTheme.StyleButton(btn);
            policyGrid.AddChild(btn);

            if (!canChange && p.Id != state.BorderPolicyId)
            {
                var reason = new Label { Text = "Requirements not met", CustomMinimumSize = new Vector2(0, 20) };
                reason.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
                reason.AddThemeColorOverride("font_color", GameTheme.Red);
                policyGrid.AddChild(reason);
            }
        }

        _contentContainer.AddChild(new HSeparator());

        // Top crisis summary
        var crisisTitle = new Label { Text = "MAJOR ACTIVE CRISES" };
        crisisTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        crisisTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        _contentContainer.AddChild(crisisTitle);

        var crises = MigrationSystem.GetAllActiveCrises().OrderByDescending(c => c.RefugeeCount).Take(5).ToList();
        if (crises.Count == 0)
        {
            var none = new Label { Text = "No active crises.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            none.AddThemeColorOverride("font_color", GameTheme.TextMuted);
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

    private Control CreateBorderPolicyCard(MigrationSystem.BorderPolicy policy, bool isCurrent)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(isCurrent ? GameTheme.Green : GameTheme.Blue, true));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        var catBadge = Chip.CreateCategory(policy.Category, GetCategoryColor(policy.Category));
        catBadge.FontSize = GameTheme.FontSizeTiny;
        header.AddChild(catBadge);

        var nameLabel = new Label { Text = policy.Name + (isCurrent ? " (ACTIVE)" : "") };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var descLabel = new Label { Text = policy.Description };
        descLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        descLabel.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(descLabel);

        // Effects
        var effects = new List<string>();
        effects.Add($"Refugee Acceptance: {(policy.RefugeeAcceptanceMultiplier * 100):F0}%");
        effects.Add($"Stability: {(policy.StabilityImpact >= 0 ? "+" : "")}{policy.StabilityImpact:F1}/year");
        effects.Add($"GDP Growth: {(policy.GdpGrowthImpact >= 0 ? "+" : "")}{policy.GdpGrowthImpact:F1}/year");
        effects.Add($"Diplomacy: {(policy.DiplomacyBonus >= 0 ? "+" : "")}{policy.DiplomacyBonus} with all");

        var effectsLabel = new Label { Text = "Effects: " + string.Join("  |  ", effects) };
        effectsLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
        effectsLabel.AddThemeColorOverride("font_color", GameTheme.GreenBright);
        effectsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(effectsLabel);

        if (!isCurrent)
        {
            var costLabel = new Label { Text = $"Cost: {policy.CostPP} PP" };
            costLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            costLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            container.AddChild(costLabel);
        }

        // Tooltip
        container.TooltipText = BuildBorderPolicyTooltip(policy);

        return container;
    }

    private Color GetCategoryColor(string category)
    {
        return category switch
        {
            "Humanitarian" => GameTheme.Green,
            "Balanced" => GameTheme.Blue,
            "Security" => GameTheme.Orange,
            "Isolationist" => GameTheme.Red,
            _ => GameTheme.TextMuted
        };
    }

    private string BuildBorderPolicyTooltip(MigrationSystem.BorderPolicy p)
    {
        var data = new TooltipBuilder.TooltipData
        {
            Title = p.Name,
            Description = p.Description,
            Category = p.Category,
            CategoryColor = GetCategoryColor(p.Category),
        };

        data.Costs["Political Power"] = p.CostPP.ToString();
        data.Effects["Refugee Acceptance"] = $"{(p.RefugeeAcceptanceMultiplier * 100):F0}%";
        data.Effects["Stability/year"] = $"{(p.StabilityImpact >= 0 ? "+" : "")}{p.StabilityImpact:F1}";
        data.Effects["GDP Growth/year"] = $"{(p.GdpGrowthImpact >= 0 ? "+" : "")}{p.GdpGrowthImpact:F1}";
        data.Effects["Diplomacy Bonus"] = $"{(p.DiplomacyBonus >= 0 ? "+" : "")}{p.DiplomacyBonus} with all";

        return TooltipBuilder.BuildText(data);
    }

    private Control CreateCrisisSummaryItem(MigrationSystem.ActiveCrisis crisis)
    {
        var type = MigrationSystem.GetAllCrisisTypes().FirstOrDefault(t => t.Id == crisis.TypeId);
        string typeName = type?.Name ?? crisis.TypeId;
        string sourceName = CountryDatabase.GetOrCreate(crisis.SourceCountryId, "").Name;
        int currentYear = (int)((GameManager.Instance?.TotalDays ?? 0) / 365);
        int remaining = Math.Max(0, crisis.DurationYears - (currentYear - crisis.YearStarted));

        var container = new HBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(GetCrisisColor(crisis.TypeId), true));

        // Crisis type badge
        var typeBadge = Chip.CreateCategory(typeName, GetCrisisColor(crisis.TypeId));
        typeBadge.FontSize = GameTheme.FontSizeTiny;
        typeBadge.CustomMinimumSize = new Vector2(120, 0);
        container.AddChild(typeBadge);

        // Source country
        var sourceLabel = new Label { Text = $"From: {sourceName}", CustomMinimumSize = new Vector2(150, 0) };
        sourceLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        sourceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(sourceLabel);

        // Refugee count
        var countLabel = new Label { Text = $"{crisis.RefugeeCount:N0} refugees", CustomMinimumSize = new Vector2(140, 0) };
        countLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        countLabel.AddThemeColorOverride("font_color", GameTheme.Yellow);
        countLabel.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(countLabel);

        // Duration
        var durLabel = new Label { Text = $"{remaining} yr left", CustomMinimumSize = new Vector2(100, 0) };
        durLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        durLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        durLabel.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(durLabel);

        // Intensity bar using StatBar
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = (float)(crisis.Intensity * 100),
            CustomMinimumSize = new Vector2(150, 16)
        };
        bar.AddThemeColorOverride("fill_color", GameTheme.Red);
        container.AddChild(bar);

        return container;
    }

    private Color GetCrisisColor(string typeId)
    {
        return typeId switch
        {
            "war" => GameTheme.Red,
            "civil_war" => GameTheme.Purple,
            "climate" => GameTheme.Blue,
            "persecution" => new Color("#9C27B0"),
            _ => GameTheme.TextMuted
        };
    }

    // ===== CRISES TAB =====
    private void BuildCrisesTab()
    {
        var allCrises = MigrationSystem.GetAllActiveCrises();

        if (!string.IsNullOrEmpty(_searchText))
        {
            allCrises = allCrises.Where(c =>
            {
                var sourceName = CountryDatabase.GetOrCreate(c.SourceCountryId, "").Name.ToLower();
                var type = MigrationSystem.GetAllCrisisTypes().FirstOrDefault(t => t.Id == c.TypeId);
                var typeName = type?.Name.ToLower() ?? "";
                return sourceName.Contains(_searchText) || typeName.Contains(_searchText) || c.TypeId.Contains(_searchText);
            }).ToList();
        }

        int totalCrises = MigrationSystem.GetAllActiveCrises().Count;
        _searchBox.UpdateResultCount(allCrises.Count, totalCrises);

        if (allCrises.Count == 0)
        {
            _emptyState.SetContent(
                "No Active Crises",
                string.IsNullOrEmpty(_searchText)
                    ? "There are currently no displacement crises worldwide."
                    : $"No crises match \"{_searchText}\". Try a different search term.",
                "⚠"
            );
            _emptyState.Visible = true;
            return;
        }

        _emptyState.Visible = false;

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
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(GetCrisisColor(crisis.TypeId), true));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        var typeBadge = Chip.CreateCategory(typeName, GetCrisisColor(crisis.TypeId));
        typeBadge.FontSize = GameTheme.FontSizeTiny;
        typeBadge.CustomMinimumSize = new Vector2(160, 0);
        header.AddChild(typeBadge);

        var nameLabel = new Label { Text = $"Crisis from {sourceName}" };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        // Details
        var details = new HBoxContainer();
        details.AddThemeConstantOverride("separation", GameTheme.SpaceL);
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
            destTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            destTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
            container.AddChild(destTitle);

            foreach (var flow in flows.Take(3))
            {
                var destName = CountryDatabase.GetOrCreate(flow.DestinationCountryId, "").Name;
                var destItem = new Label { Text = $"  → {destName}: {flow.AnnualFlow:N0}/yr" };
                destItem.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
                destItem.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
                container.AddChild(destItem);
            }
        }

        // Tooltip
        container.TooltipText = BuildCrisisTooltip(crisis, type);

        return container;
    }

    private void AddDetailItem(HBoxContainer parent, string label, string value)
    {
        var item = new VBoxContainer();
        item.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        parent.AddChild(item);

        var lbl = new Label { Text = label };
        lbl.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
        lbl.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        item.AddChild(lbl);

        var val = new Label { Text = value };
        val.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        val.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        val.HorizontalAlignment = HorizontalAlignment.Center;
        item.AddChild(val);
    }

    private string BuildCrisisTooltip(MigrationSystem.ActiveCrisis crisis, MigrationSystem.CrisisType type)
    {
        var data = new TooltipBuilder.TooltipData
        {
            Title = type?.Name ?? crisis.TypeId,
            Description = type?.Description ?? "Displacement crisis",
            Category = "Crisis",
            CategoryColor = GetCrisisColor(crisis.TypeId),
        };

        data.Effects["Refugees"] = crisis.RefugeeCount.ToString("N0");
        data.Effects["Duration Left"] = $"{Math.Max(0, crisis.DurationYears - ((GameManager.Instance?.TotalDays ?? 0) / 365 - crisis.YearStarted))} years";
        data.Effects["Intensity"] = $"{crisis.Intensity * 100:F0}%";
        data.Effects["Started"] = $"Year {crisis.YearStarted}";
        data.Effects["Source Country"] = CountryDatabase.GetOrCreate(crisis.SourceCountryId, "").Name;

        return TooltipBuilder.BuildText(data);
    }

    // ===== FLOWS TAB =====
    private void BuildFlowsTab()
    {
        var allFlows = MigrationSystem.GetAllFlows();

        if (!string.IsNullOrEmpty(_searchText))
        {
            allFlows = allFlows.Where(f =>
            {
                var srcName = CountryDatabase.GetOrCreate(f.SourceCountryId, "").Name.ToLower();
                var dstName = CountryDatabase.GetOrCreate(f.DestinationCountryId, "").Name.ToLower();
                return srcName.Contains(_searchText) || dstName.Contains(_searchText) || f.PrimaryCause.Contains(_searchText);
            }).ToList();
        }

        int totalFlows = MigrationSystem.GetAllFlows().Count;
        _searchBox.UpdateResultCount(allFlows.Count, totalFlows);

        if (allFlows.Count == 0)
        {
            _emptyState.SetContent(
                "No Migration Flows",
                string.IsNullOrEmpty(_searchText)
                    ? "No active migration flows at this time."
                    : $"No flows match \"{_searchText}\". Try a different search term.",
                "🌊"
            );
            _emptyState.Visible = true;
            return;
        }

        _emptyState.Visible = false;

        // Header row
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
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
            row.AddThemeConstantOverride("separation", GameTheme.SpaceS);
            _contentContainer.AddChild(row);

            var srcName = CountryDatabase.GetOrCreate(flow.SourceCountryId, "").Name;
            var dstName = CountryDatabase.GetOrCreate(flow.DestinationCountryId, "").Name;

            AddTableCell(row, srcName, 150);
            AddTableCell(row, dstName, 150);
            AddTableCell(row, $"{flow.AnnualFlow:N0}/yr", 120, GameTheme.Yellow);
            AddTableCell(row, GetCauseDisplayName(flow.PrimaryCause), 100, GetCauseColor(flow.PrimaryCause));
            AddTableCell(row, $"{(flow.Pressure * 100):F0}%", 100, flow.Pressure > 0.7 ? GameTheme.Red : (flow.Pressure > 0.4 ? GameTheme.Orange : GameTheme.Green));
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
            "war" => GameTheme.Red,
            "civil_war" => GameTheme.Purple,
            "climate" => GameTheme.Blue,
            "persecution" => new Color("#9C27B0"),
            _ => GameTheme.TextMuted
        };
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
            _emptyState.SetContent(
                "No Player Country",
                "Select a country to play first.",
                "🌍"
            );
            _emptyState.Visible = true;
            return;
        }

        _emptyState.Visible = false;

        string playerId = GameManager.Instance.PlayerCountryId;
        var state = MigrationSystem.GetCountryState(playerId);
        var currentPolicy = MigrationSystem.GetBorderPolicy(state.BorderPolicyId);

        // Current policy
        var currentTitle = new Label { Text = "CURRENT BORDER POLICY" };
        currentTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        currentTitle.AddThemeColorOverride("font_color", GameTheme.Green);
        _contentContainer.AddChild(currentTitle);

        if (currentPolicy != null)
        {
            var card = CreateBorderPolicyCard(currentPolicy, true);
            _contentContainer.AddChild(card);
        }

        _contentContainer.AddChild(new HSeparator());

        // Available policies
        var availTitle = new Label { Text = "AVAILABLE BORDER POLICIES" };
        availTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        availTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        _contentContainer.AddChild(availTitle);

        foreach (var policy in MigrationSystem.GetAllBorderPolicies().Where(p => p.Id != state.BorderPolicyId))
        {
            var card = CreateBorderPolicyCard(policy, false);
            // Add SET button
            var actionRow = new HBoxContainer();
            actionRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
            card.AddChild(actionRow);

            actionRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

            var canSet = MigrationSystem.CanSetBorderPolicy(playerId, policy.Id);
            var setBtn = new Button { Text = "SET POLICY", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 32) };
            setBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            setBtn.Disabled = !canSet;
            var pid = policy.Id;
            setBtn.Pressed += () => OnSetBorderPolicy(pid);
            GameTheme.StyleButton(setBtn);
            actionRow.AddChild(setBtn);

            if (!canSet)
            {
                var reason = new Label { Text = "Requirements not met", CustomMinimumSize = new Vector2(140, 0) };
                reason.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
                reason.AddThemeColorOverride("font_color", GameTheme.Red);
                actionRow.AddChild(reason);
            }

            _contentContainer.AddChild(card);
        }

        _contentContainer.AddChild(new HSeparator());

        // Aid packages
        var aidTitle = new Label { Text = "HUMANITARIAN AID PACKAGES" };
        aidTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        aidTitle.AddThemeColorOverride("font_color", GameTheme.Blue);
        _contentContainer.AddChild(aidTitle);

        var activeAids = state.ActiveAidPackages.Select(a => a.PackageId).ToHashSet();

        // Active aid packages
        if (state.ActiveAidPackages.Count > 0)
        {
            var activeSection = new Label { Text = "ACTIVE AID PACKAGES" };
            activeSection.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            activeSection.AddThemeColorOverride("font_color", GameTheme.Green);
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
        availAidTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        availAidTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        _contentContainer.AddChild(availAidTitle);

        foreach (var pkg in MigrationSystem.GetAllAidPackages())
        {
            if (activeAids.Contains(pkg.Id)) continue;

            var card = CreateAidPackageCard(pkg, false, 0);
            var actionRow = new HBoxContainer();
            actionRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
            card.AddChild(actionRow);

            actionRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

            var canActivate = MigrationSystem.CanActivateAidPackage(playerId, pkg.Id);
            var activateBtn = new Button { Text = "ACTIVATE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 32) };
            activateBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            activateBtn.Disabled = !canActivate;
            var pid = pkg.Id;
            activateBtn.Pressed += () => OnActivateAidPackage(pid);
            GameTheme.StyleButton(activateBtn);
            actionRow.AddChild(activateBtn);

            if (!canActivate)
            {
                var reason = new Label { Text = "Requirements not met", CustomMinimumSize = new Vector2(140, 0) };
                reason.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
                reason.AddThemeColorOverride("font_color", GameTheme.Red);
                actionRow.AddChild(reason);
            }

            _contentContainer.AddChild(card);
        }
    }

    private Control CreateAidPackageCard(MigrationSystem.AidPackage pkg, bool isActive, int yearStarted)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(isActive ? GameTheme.Blue : GameTheme.BgMedium, true));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        var nameLabel = new Label { Text = pkg.Name + (isActive ? " (ACTIVE)" : "") };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var descLabel = new Label { Text = pkg.Description };
        descLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        descLabel.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
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
        effectsLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
        effectsLabel.AddThemeColorOverride("font_color", GameTheme.GreenBright);
        effectsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(effectsLabel);

        if (isActive)
        {
            int currentYear = (int)((GameManager.Instance?.TotalDays ?? 0) / 365);
            int remaining = Math.Max(0, 3 - (currentYear - yearStarted));
            var durLabel = new Label { Text = $"Expires in: {remaining} year(s)" };
            durLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
            durLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            container.AddChild(durLabel);
        }

        // Tooltip
        container.TooltipText = BuildAidPackageTooltip(pkg);

        return container;
    }

    private string BuildAidPackageTooltip(MigrationSystem.AidPackage p)
    {
        var data = new TooltipBuilder.TooltipData
        {
            Title = p.Name,
            Description = p.Description,
            Category = "Aid Package",
            CategoryColor = GameTheme.Blue,
        };

        data.Costs["Political Power"] = p.CostPP.ToString();
        data.Costs["Budget"] = $"{(p.CostBudgetPercent * 100):F0}%";
        data.Effects["Refugees Supported"] = p.RefugeesSupported.ToString("N0");
        if (p.StabilityBonus > 0) data.Effects["Stability"] = $"+{p.StabilityBonus:F1}";
        if (p.AssimilationBoost > 0) data.Effects["Assimilation Boost"] = $"{(p.AssimilationBoost * 100):F0}%";
        if (p.GdpGrowthBonus > 0) data.Effects["GDP Growth"] = $"{(p.GdpGrowthBonus * 100):F0}%";
        if (p.ReturnRateBoost > 0) data.Effects["Return Rate Boost"] = $"{(p.ReturnRateBoost * 100):F0}%";
        data.Effects["Reputation Bonus"] = $"+{p.ReputationBonus}";

        return TooltipBuilder.BuildText(data);
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

    public override void Close()
    {
        GD.Print("MigrationPanel: kapatılıyor");
        base.Close();
    }
}