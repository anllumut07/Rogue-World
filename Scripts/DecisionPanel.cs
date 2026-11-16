using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// HOI4-style Decisions Panel (M54 - Stage 1 UI Upgrade):
/// Built with reusable UI components from Scripts/UI/
/// - Category tabs via TabBar
/// - Search filter via SearchBox with result count
/// - Decision cards via ListRow
/// - Category badges via Chip
/// - Progress bars via StatBar
/// - Rich tooltips via TooltipBuilder
/// - Empty states via EmptyState
/// - Panel frame via GamePanel
/// </summary>
public partial class DecisionPanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    // === UI Components (using reusable components) ===
    private TabBar _tabBar;
    private SearchBox _searchBox;
    private VBoxContainer _availableContainer;
    private ScrollContainer _availableScroll;
    private EmptyState _availableEmptyState;

    private VBoxContainer _activeContainer;
    private ScrollContainer _activeScroll;
    private EmptyState _activeEmptyState;

    // Categories in order (matching DecisionSystem)
    private static readonly string[] CategoryIds = { "All", "Economy", "Military", "Political", "Diplomatic", "Technology", "General" };
    private static readonly string[] CategoryLabels = { "ALL", "ECONOMY", "MILITARY", "POLITICAL", "DIPLOMATIC", "TECHNOLOGY", "GENERAL" };

    private string _activeCategory = "All";
    private string _searchText = "";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[DecisionPanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new DecisionPanel();
        // Add to parent (which should be a Node in the scene tree)
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "DECISIONS";
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
        RefreshAll();
        IsOpen = true;

        GD.Print("DecisionPanel: hazır (Stage 1 UI)");
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
        // Content goes into GamePanel's ContentContainer
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // === Category Tabs (using TabBar component) ===
        _tabBar = new TabBar
        {
            TabIds = CategoryIds,
            TabLabels = CategoryLabels,
            SelectedIndex = 0,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _tabBar.TabChanged += OnCategoryTabChanged;
        content.AddChild(_tabBar);

        // === Search Box (using SearchBox component) ===
        _searchBox = new SearchBox
        {
            PlaceholderText = "Type to filter decisions...",
            ShowResultCount = true,
            ShowClearButton = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _searchBox.TextChanged += OnSearchTextChanged;
        _searchBox.SearchSubmitted += OnSearchSubmitted;
        _searchBox.Cleared += OnSearchCleared;
        content.AddChild(_searchBox);

        // Separator
        var separator = new HSeparator();
        separator.AddThemeStyleboxOverride("separator", GameTheme.SeparatorStyle(GameTheme.BorderMedium, 1));
        content.AddChild(separator);

        // === Two columns: Available | Active ===
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", GameTheme.SpaceXL);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        columns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(columns);

        // LEFT: Available decisions
        var leftCol = BuildAvailableColumn();
        columns.AddChild(leftCol);

        // RIGHT: Active decisions
        var rightCol = BuildActiveColumn();
        columns.AddChild(rightCol);
    }

    private VBoxContainer BuildAvailableColumn()
    {
        var leftCol = new VBoxContainer();
        leftCol.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        leftCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftCol.CustomMinimumSize = new Vector2(500, 0);

        // Header row
        var leftHeader = new HBoxContainer();
        leftHeader.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        leftCol.AddChild(leftHeader);

        var leftTitle = new Label { Text = "AVAILABLE DECISIONS" };
        leftTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        leftTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        leftHeader.AddChild(leftTitle);

        leftHeader.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        // Clear filter button (handled by SearchBox's clear button, but we also clear category)
        var clearFilterBtn = new Button
        {
            Text = "CLEAR ALL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(100, 28)
        };
        clearFilterBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        clearFilterBtn.Pressed += OnClearAllFilters;
        GameTheme.StyleButton(clearFilterBtn);
        leftHeader.AddChild(clearFilterBtn);

        // Scroll container for available decisions
        _availableScroll = new ScrollContainer();
        _availableScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _availableScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftCol.AddChild(_availableScroll);

        _availableContainer = new VBoxContainer();
        _availableContainer.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _availableContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _availableScroll.AddChild(_availableContainer);

        // Empty state
        _availableEmptyState = EmptyState.CreateNoDecisions();
        _availableEmptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _availableEmptyState.Visible = false;
        _availableContainer.AddChild(_availableEmptyState);

        return leftCol;
    }

    private VBoxContainer BuildActiveColumn()
    {
        var rightCol = new VBoxContainer();
        rightCol.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        rightCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightCol.CustomMinimumSize = new Vector2(320, 0);

        var rightHeader = new Label { Text = "ACTIVE DECISIONS" };
        rightHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        rightHeader.AddThemeColorOverride("font_color", GameTheme.Orange);
        rightCol.AddChild(rightHeader);

        _activeScroll = new ScrollContainer();
        _activeScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _activeScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightCol.AddChild(_activeScroll);

        _activeContainer = new VBoxContainer();
        _activeContainer.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _activeContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _activeScroll.AddChild(_activeContainer);

        // Empty state
        _activeEmptyState = EmptyState.CreateNoActiveDecisions();
        _activeEmptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _activeEmptyState.Visible = false;
        _activeContainer.AddChild(_activeEmptyState);

        return rightCol;
    }

    private void ConnectSignals()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged += OnDataChanged;
    }

    private void OnDataChanged() => RefreshAll();

    private void OnCategoryTabChanged(string tabId, int tabIndex)
    {
        _activeCategory = tabId;
        RefreshAvailable();
    }

    private void OnSearchTextChanged(string text)
    {
        _searchText = text.Trim().ToLower();
        RefreshAvailable();
    }

    private void OnSearchSubmitted(string text)
    {
        // Optional: could trigger something on enter
    }

    private void OnSearchCleared()
    {
        _searchText = "";
        RefreshAvailable();
    }

    private void OnClearAllFilters()
    {
        _activeCategory = "All";
        _searchText = "";
        _tabBar.SelectedIndex = 0;
        _tabBar.UpdateVisuals();
        _searchBox.Clear();
        RefreshAvailable();
    }

    private void RefreshAll()
    {
        RefreshAvailable();
        RefreshActive();
    }

    private void RefreshAvailable()
    {
        // Clear old items (keep empty state)
        foreach (var child in _availableContainer.GetChildren())
        {
            if (child == _availableEmptyState) continue;
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
        if (!string.IsNullOrEmpty(_searchText))
        {
            decisions = decisions.Where(d =>
                d.Name.ToLower().Contains(_searchText) ||
                d.Description.ToLower().Contains(_searchText) ||
                d.Category.ToLower().Contains(_searchText)
            ).ToList();
        }

        int totalCount = DecisionSystem.GetAvailableDecisions().Count;
        int filteredCount = decisions.Count;

        // Update search box result count
        _searchBox.UpdateResultCount(filteredCount, totalCount);

        if (filteredCount == 0)
        {
            _availableEmptyState.Visible = true;
            if (!string.IsNullOrEmpty(_searchText))
            {
                _availableEmptyState.SetContent(
                    "No Results Found",
                    $"No decisions match \"{_searchText}\". Try a different search term or clear filters.",
                    "🔍"
                );
            }
            else if (_activeCategory != "All")
            {
                _availableEmptyState.SetContent(
                    $"No {_activeCategory} Decisions",
                    "No available decisions in this category. Check other categories or requirements.",
                    "📋"
                );
            }
            else
            {
                _availableEmptyState.SetContent(
                    "No Decisions Available",
                    "Requirements not met, on cooldown, or filtered out.",
                    "📋"
                );
            }
            return;
        }

        _availableEmptyState.Visible = false;

        foreach (var d in decisions)
        {
            var row = CreateDecisionRow(d);
            _availableContainer.AddChild(row);
        }
    }

    private Control CreateDecisionRow(DecisionSystem.Decision d)
    {
        Color categoryColor = GameTheme.GetCategoryColor(d.Category);

        // Create ListRow
        var row = new ListRow
        {
            Title = d.Name,
            Subtitle = $"[{d.Category}]  {FormatCost(d)}  |  {FormatDays(d.CostDays)}",
            RightText = "START →",
            AccentColor = categoryColor,
            RowHeight = 72,
            Selectable = true
        };

        // Add category badge using Chip component
        var catChip = Chip.CreateCategory(d.Category, categoryColor);
        catChip.FontSize = GameTheme.FontSizeTiny;
        // Add chip to the row's main container
        var mainContainer = row.GetChild(0) as HBoxContainer;
        if (mainContainer != null)
        {
            // Insert chip after icon (index 1)
            mainContainer.MoveChild(catChip, 1);
        }

        // Rich tooltip using TooltipBuilder
        var tooltipData = TooltipBuilder.FromDecision(d);
        row.TooltipText = TooltipBuilder.BuildText(tooltipData);

        // Connect press
        row.RowPressed += () => OnStartDecision(d.Id);

        // Hover scale
        GameTheme.AddHoverScale(row, 1.02f);

        return row;
    }

    private void RefreshActive()
    {
        // Clear old items (keep empty state)
        foreach (var child in _activeContainer.GetChildren())
        {
            if (child == _activeEmptyState) continue;
            _activeContainer.RemoveChild(child);
            child.QueueFree();
        }

        var active = DecisionSystem.GetActiveDecisions();

        if (active.Count == 0)
        {
            _activeEmptyState.Visible = true;
            return;
        }

        _activeEmptyState.Visible = false;

        foreach (var info in active)
        {
            var item = CreateActiveItem(info);
            _activeContainer.AddChild(item);
        }
    }

    private Control CreateActiveItem(DecisionSystem.ActiveDecisionInfo info)
    {
        Color categoryColor = GameTheme.GetCategoryColor(info.Decision.Category);

        // Container with card style
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(categoryColor));

        // Header row with category badge and cancel button
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        // Category chip
        var catChip = Chip.CreateCategory(info.Decision.Category, categoryColor);
        catChip.FontSize = GameTheme.FontSizeTiny;
        header.AddChild(catChip);

        // Name
        var nameLabel = new Label { Text = info.Decision.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        nameLabel.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        // Cancel button
        var cancelBtn = new Button
        {
            Text = "CANCEL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(70, 26)
        };
        cancelBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        cancelBtn.Pressed += () => OnCancelDecision(info.Decision.Id);
        GameTheme.StyleButton(cancelBtn, false, 1.05f);
        header.AddChild(cancelBtn);

        // Progress bar (using StatBar)
        var progressBar = new StatBar
        {
            LabelText = "",
            Value = info.Progress * 100f,
            MinValue = 0,
            MaxValue = 100,
            ShowValue = true,
            ShowPercent = true,
            ColorSource = GameTheme.ColorSource.Custom,
            CustomFillColor = GameTheme.Orange,
            BarHeight = 14,
            LabelWidth = 0,
            ValueWidth = 100,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        container.AddChild(progressBar);

        // Progress label
        var progressLabel = new Label
        {
            Text = $"Day {info.DaysElapsed} / {info.TotalDays}",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        progressLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        progressLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        container.AddChild(progressLabel);

        // Tooltip
        var tooltipData = TooltipBuilder.FromDecision(info.Decision);
        tooltipData.Footer = $"Progress: {info.Progress * 100:F0}% ({info.DaysElapsed}/{info.TotalDays} days)";
        container.TooltipText = TooltipBuilder.BuildText(tooltipData);

        return container;
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

    public override void Close()
    {
        GD.Print("DecisionPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;

        base.Close();
    }

    // === Formatting helpers ===

    private string FormatCost(DecisionSystem.Decision d)
    {
        var parts = new List<string>();
        if (d.CostPP > 0) parts.Add($"{d.CostPP} PP");
        return parts.Count > 0 ? string.Join(" + ", parts) : "Free";
    }

    private string FormatDays(int days)
    {
        if (days <= 0) return "Instant";
        return $"{days} days";
    }
}