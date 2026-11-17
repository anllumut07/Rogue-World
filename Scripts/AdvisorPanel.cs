using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Advisor Panel (M53 + Stage 2 UI Upgrade): HUD ADVISORS button opens this.
/// Sections: Active Advisors, Available Advisors (Hire), Advice History.
/// Built with reusable UI components from Scripts/UI/
/// - Panel frame via GamePanel
/// - Category tabs via TabBar (Active / Available / History)
/// - Search filter via SearchBox with result count (for Available tab)
/// - Advisor cards with Chip badges for type
/// - StatBar for confidence/urgency in history
/// - Rich tooltips via TooltipBuilder
/// - Empty states via EmptyState
/// </summary>
public partial class AdvisorPanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    // === UI Components (using reusable components) ===
    private TabBar _tabBar;
    private SearchBox _searchBox;
    private VBoxContainer _contentContainer;
    private ScrollContainer _contentScroll;
    private EmptyState _emptyState;

    // Sections as tabs
    private static readonly string[] TabIds = { "Active", "Available", "History" };
    private static readonly string[] TabLabels = { "ACTIVE ADVISORS", "HIRE ADVISORS", "ADVICE HISTORY" };

    private string _activeTab = "Active";
    private string _searchText = "";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[AdvisorPanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new AdvisorPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "ADVISORS";
        MinSize = new Vector2(900, 560);
        MaxSize = new Vector2(1200, 800);
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

        GD.Print("AdvisorPanel: hazır (Stage 2 UI)");
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

        // === Search Box (using SearchBox component) - only for Available tab ===
        _searchBox = new SearchBox
        {
            PlaceholderText = "Filter advisors...",
            ShowResultCount = true,
            ShowClearButton = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Visible = false // Only show for Available tab
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

        _emptyState = EmptyState.CreateGeneric("No Data", "No advisor data available.");
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
        bool showSearch = _activeTab == "Available";
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

        switch (_activeTab)
        {
            case "Active":
                BuildActiveAdvisorsTab(playerId);
                break;
            case "Available":
                BuildAvailableAdvisorsTab(playerId);
                break;
            case "History":
                BuildAdviceHistoryTab();
                break;
        }
    }

    // ===== ACTIVE ADVISORS TAB =====
    private void BuildActiveAdvisorsTab(string playerId)
    {
        var activeAdvisors = AdvisorSystem.GetAllActiveAdvisors();

        // Country header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        _contentContainer.AddChild(header);

        var countryName = GameManager.Instance.PlayerCountryData?.Name ?? playerId;
        var countryLabel = new Label { Text = $"{countryName} ({playerId}) — Active: {activeAdvisors.Count}/3" };
        countryLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        countryLabel.AddThemeColorOverride("font_color", GameTheme.Gold);
        header.AddChild(countryLabel);

        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _contentContainer.AddChild(new HSeparator());

        if (activeAdvisors.Count == 0)
        {
            var none = new Label { Text = "No active advisors. Go to HIRE ADVISORS tab to recruit.", HorizontalAlignment = HorizontalAlignment.Center };
            none.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            none.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            none.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _contentContainer.AddChild(none);
        }

        foreach (var type in new[] { AdvisorSystem.AdvisorType.Economy, AdvisorSystem.AdvisorType.Security, AdvisorSystem.AdvisorType.Media })
        {
            var active = AdvisorSystem.GetActiveAdvisor(type);
            var card = BuildActiveAdvisorCard(type, active, playerId);
            _contentContainer.AddChild(card);
        }
    }

    private Control BuildActiveAdvisorCard(AdvisorSystem.AdvisorType type, AdvisorSystem.ActiveAdvisor active, string playerId)
    {
        var profile = AdvisorSystem.GetAllProfiles().FirstOrDefault(p => p.Id == active?.ProfileId);

        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(GetTypeColor(type), true));

        // Header row
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        var typeBadge = Chip.CreateCategory(type.ToString(), GetTypeColor(type));
        typeBadge.FontSize = GameTheme.FontSizeTiny;
        typeBadge.CustomMinimumSize = new Vector2(100, 0);
        header.AddChild(typeBadge);

        string name = profile?.Name ?? active?.ProfileId ?? "—";
        var nameLabel = new Label { Text = name };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var statusLabel = new Label { Text = "ACTIVE", CustomMinimumSize = new Vector2(80, 0) };
        statusLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
        statusLabel.AddThemeColorOverride("font_color", GameTheme.Green);
        statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddChild(statusLabel);

        // Action buttons
        var fireBtn = new Button
        {
            Text = "FIRE",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(100, 32)
        };
        var t = type;
        fireBtn.Pressed += () =>
        {
            AdvisorSystem.FireAdvisor(t, GameManager.Instance.TotalDays);
            GD.Print($"[AdvisorPanel] Advisor fired: {t}");
            RefreshContent();
        };
        GameTheme.StyleButton(fireBtn);
        header.AddChild(fireBtn);

        var adviceBtn = new Button
        {
            Text = "REQUEST ADVICE",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(140, 32)
        };
        adviceBtn.Pressed += () =>
        {
            var advice = AdvisorSystem.RequestAdvice(t, GameManager.Instance.TotalDays, playerId);
            if (advice != null)
            {
                GD.Print($"[AdvisorPanel] Advice received: {advice.Recommendation} (conf={advice.Confidence})");
            }
            else
            {
                GD.Print($"[AdvisorPanel] Advice unavailable (cooldown or no advisor)");
            }
            RefreshContent();
        };
        GameTheme.StyleButton(adviceBtn);
        header.AddChild(adviceBtn);

        // Details
        if (profile != null)
        {
            var details = new Label
            {
                Text = $"Trait: {profile.Trait}  |  Loyalty: {profile.Loyalty}  |  Popularity: {profile.Popularity}  |  Expertise: {string.Join(", ", profile.ExpertiseTags)}"
            };
            details.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            details.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            details.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            container.AddChild(details);
        }

        // Stats
        if (active != null)
        {
            var stats = new Label
            {
                Text = $"Hired: Day {active.HiredDay}  |  Advice Count: {active.AdviceCount}  |  Last Advice: {(active.LastAdviceDay >= 0 ? "Day " + active.LastAdviceDay : "Never")}"
            };
            stats.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
            stats.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            container.AddChild(stats);
        }

        // Cooldown indicator
        var cooldownDays = AdvisorSystem.GetCooldownDaysRemaining(type);
        if (cooldownDays > 0)
        {
            var cooldownLabel = new Label { Text = $"Cooldown: {cooldownDays} day(s) remaining" };
            cooldownLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
            cooldownLabel.AddThemeColorOverride("font_color", GameTheme.Orange);
            container.AddChild(cooldownLabel);
        }

        // Tooltip
        container.TooltipText = BuildActiveAdvisorTooltip(type, profile, active);

        return container;
    }

    private Color GetTypeColor(AdvisorSystem.AdvisorType type)
    {
        return type switch
        {
            AdvisorSystem.AdvisorType.Economy => GameTheme.Green,
            AdvisorSystem.AdvisorType.Security => GameTheme.Red,
            AdvisorSystem.AdvisorType.Media => GameTheme.Blue,
            _ => GameTheme.TextMuted
        };
    }

    private string BuildActiveAdvisorTooltip(AdvisorSystem.AdvisorType type, AdvisorSystem.AdvisorProfile profile, AdvisorSystem.ActiveAdvisor active)
    {
        var data = new TooltipBuilder.TooltipData
        {
            Title = profile?.Name ?? "Advisor",
            Description = $"Active {type} Advisor",
            Category = type.ToString(),
            CategoryColor = GetTypeColor(type),
        };

        if (profile != null)
        {
            data.Effects["Trait"] = profile.Trait;
            data.Effects["Loyalty"] = profile.Loyalty.ToString();
            data.Effects["Popularity"] = profile.Popularity.ToString();
            data.Effects["Expertise"] = string.Join(", ", profile.ExpertiseTags);
        }

        if (active != null)
        {
            data.Effects["Hired"] = $"Day {active.HiredDay}";
            data.Effects["Advice Given"] = active.AdviceCount.ToString();
            data.Effects["Last Advice"] = active.LastAdviceDay >= 0 ? $"Day {active.LastAdviceDay}" : "Never";
        }

        var cooldown = AdvisorSystem.GetCooldownDaysRemaining(type);
        if (cooldown > 0)
        {
            data.Effects["Cooldown"] = $"{cooldown} day(s)";
        }

        return TooltipBuilder.BuildText(data);
    }

    // ===== AVAILABLE ADVISORS TAB =====
    private void BuildAvailableAdvisorsTab(string playerId)
    {
        var allProfiles = AdvisorSystem.GetAllProfiles();
        var activeAdvisors = AdvisorSystem.GetAllActiveAdvisors();
        var activeTypes = new HashSet<AdvisorSystem.AdvisorType>(activeAdvisors.Keys);

        // Filter by type and search
        var available = allProfiles
            .Where(p => !activeTypes.Contains(p.Type))
            .Where(p => string.IsNullOrEmpty(_searchText) ||
                p.Name.ToLower().Contains(_searchText) ||
                p.Trait.ToLower().Contains(_searchText) ||
                p.CultureGroup.ToLower().Contains(_searchText) ||
                p.ExpertiseTags.Any(e => e.ToLower().Contains(_searchText)))
            .ToList();

        _searchBox.UpdateResultCount(available.Count, allProfiles.Count);

        if (available.Count == 0)
        {
            _emptyState.SetContent(
                "No Available Advisors",
                string.IsNullOrEmpty(_searchText)
                    ? "All advisor types are filled or no profiles match your filter."
                    : $"No advisors match \"{_searchText}\". Try a different search term.",
                "👥"
            );
            _emptyState.Visible = true;
            return;
        }

        _emptyState.Visible = false;

        // Group by type
        foreach (var type in new[] { AdvisorSystem.AdvisorType.Economy, AdvisorSystem.AdvisorType.Security, AdvisorSystem.AdvisorType.Media })
        {
            var typeProfiles = available.Where(p => p.Type == type).ToList();
            if (typeProfiles.Count == 0) continue;

            // Type header
            var typeHeader = new HBoxContainer();
            typeHeader.AddThemeConstantOverride("separation", GameTheme.SpaceS);
            _contentContainer.AddChild(typeHeader);

            var typeBadge = Chip.CreateCategory(type.ToString(), GetTypeColor(type));
            typeBadge.FontSize = GameTheme.FontSizeSmall;
            typeHeader.AddChild(typeBadge);

            var typeLabel = new Label { Text = $"{type} ADVISORS ({typeProfiles.Count} available)" };
            typeLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            typeLabel.AddThemeColorOverride("font_color", GameTheme.Gold);
            typeHeader.AddChild(typeLabel);

            foreach (var profile in typeProfiles)
            {
                var card = BuildAvailableAdvisorCard(profile, type, playerId);
                _contentContainer.AddChild(card);
            }
        }
    }

    private Control BuildAvailableAdvisorCard(AdvisorSystem.AdvisorProfile profile, AdvisorSystem.AdvisorType type, string playerId)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(GetTypeColor(type)));

        // Header row
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        var typeBadge = Chip.CreateCategory(type.ToString(), GetTypeColor(type));
        typeBadge.FontSize = GameTheme.FontSizeTiny;
        typeBadge.CustomMinimumSize = new Vector2(100, 0);
        header.AddChild(typeBadge);

        var nameLabel = new Label { Text = profile.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var cultureLabel = new Label { Text = $"({profile.CultureGroup})", CustomMinimumSize = new Vector2(100, 0) };
        cultureLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
        cultureLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        cultureLabel.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddChild(cultureLabel);

        // Hire button
        var hireBtn = new Button
        {
            Text = "HIRE",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(100, 32)
        };
        var pId = profile.Id;
        var t = type;
        hireBtn.Pressed += () =>
        {
            bool ok = AdvisorSystem.HireAdvisor(pId, t, GameManager.Instance.TotalDays);
            if (ok)
                GD.Print($"[AdvisorPanel] Advisor hired: {profile.Name} ({t})");
            else
                GD.Print($"[AdvisorPanel] Advisor hire failed: {profile.Name}");
            RefreshContent();
        };
        GameTheme.StyleButton(hireBtn);
        header.AddChild(hireBtn);

        // Details
        var details = new Label
        {
            Text = $"Trait: {profile.Trait}  |  Loyalty: {profile.Loyalty}  |  Popularity: {profile.Popularity}  |  Expertise: {string.Join(", ", profile.ExpertiseTags)}"
        };
        details.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        details.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        details.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(details);

        // Tooltip
        container.TooltipText = BuildAvailableAdvisorTooltip(profile, type);

        return container;
    }

    private string BuildAvailableAdvisorTooltip(AdvisorSystem.AdvisorProfile profile, AdvisorSystem.AdvisorType type)
    {
        var data = new TooltipBuilder.TooltipData
        {
            Title = profile.Name,
            Description = $"Available {type} Advisor — {profile.CultureGroup}",
            Category = type.ToString(),
            CategoryColor = GetTypeColor(type),
        };

        data.Effects["Trait"] = profile.Trait;
        data.Effects["Loyalty"] = profile.Loyalty.ToString();
        data.Effects["Popularity"] = profile.Popularity.ToString();
        data.Effects["Expertise"] = string.Join(", ", profile.ExpertiseTags);

        return TooltipBuilder.BuildText(data);
    }

    // ===== ADVICE HISTORY TAB =====
    private void BuildAdviceHistoryTab()
    {
        var history = AdvisorSystem.GetAdviceHistory(20);

        if (history.Count == 0)
        {
            _emptyState.SetContent(
                "No Advice History",
                "Request advice from your active advisors to see history here.",
                "📜"
            );
            _emptyState.Visible = true;
            return;
        }

        _emptyState.Visible = false;

        // Header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _contentContainer.AddChild(header);

        var titleLabel = new Label { Text = $"Last {history.Count} advice entries (most recent first)" };
        titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        titleLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        header.AddChild(titleLabel);

        _contentContainer.AddChild(new HSeparator());

        foreach (var advice in history.OrderByDescending(a => a.GeneratedDay))
        {
            var card = BuildAdviceHistoryCard(advice);
            _contentContainer.AddChild(card);
        }
    }

    private Control BuildAdviceHistoryCard(AdvisorSystem.AdvisorAdvice advice)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(GetTypeColor(advice.Type)));

        // Header row
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        var typeBadge = Chip.CreateCategory(advice.Type.ToString(), GetTypeColor(advice.Type));
        typeBadge.FontSize = GameTheme.FontSizeTiny;
        typeBadge.CustomMinimumSize = new Vector2(100, 0);
        header.AddChild(typeBadge);

        var confLabel = new Label { Text = $"Confidence: {advice.Confidence}%", CustomMinimumSize = new Vector2(140, 0) };
        confLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        confLabel.AddThemeColorOverride("font_color", advice.IsUrgent ? GameTheme.Orange : GameTheme.Gold);
        confLabel.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddChild(confLabel);

        var dayLabel = new Label { Text = $"Day {advice.GeneratedDay}", CustomMinimumSize = new Vector2(100, 0) };
        dayLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        dayLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        dayLabel.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddChild(dayLabel);

        if (advice.IsUrgent)
        {
            var urgentBadge = Chip.CreateStatus("URGENT", Chip.Variant.Danger);
            urgentBadge.FontSize = GameTheme.FontSizeTiny;
            header.AddChild(urgentBadge);
        }

        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        // Recommendation
        var recLabel = new Label { Text = advice.Recommendation };
        recLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        recLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(recLabel);

        // Rationale
        var ratLabel = new Label { Text = advice.Rationale };
        ratLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        ratLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        ratLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        container.AddChild(ratLabel);

        // Tooltip
        container.TooltipText = BuildAdviceHistoryTooltip(advice);

        return container;
    }

    private string BuildAdviceHistoryTooltip(AdvisorSystem.AdvisorAdvice advice)
    {
        var data = new TooltipBuilder.TooltipData
        {
            Title = $"{advice.Type} Advisor Advice",
            Description = advice.Rationale,
            Category = advice.Type.ToString(),
            CategoryColor = GetTypeColor(advice.Type),
        };

        data.Effects["Recommendation"] = advice.Recommendation;
        data.Effects["Confidence"] = $"{advice.Confidence}%";
        data.Effects["Urgency"] = advice.IsUrgent ? "HIGH" : "Normal";
        data.Effects["Generated"] = $"Day {advice.GeneratedDay}";

        return TooltipBuilder.BuildText(data);
    }

    public override void Close()
    {
        GD.Print("AdvisorPanel: kapatılıyor");
        base.Close();
    }
}