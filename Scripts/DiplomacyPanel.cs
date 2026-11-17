using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Diplomasi paneli (M8 + Stage 2 UI Upgrade):
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
public partial class DiplomacyPanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    // === UI Components (using reusable components) ===
    private TabBar _tabBar;
    private SearchBox _searchBox;
    private OptionButton _targetCountry;
    private Label _targetRelationLabel;
    private StatBar _tensionBar;
    private Label _tensionLabel;

    // Content containers per tab
    private VBoxContainer _factionsContainer;
    private VBoxContainer _actionsContainer;
    private VBoxContainer _guaranteesContainer;
    private VBoxContainer _relationsContainer;
    private ScrollContainer _factionsScroll;
    private ScrollContainer _actionsScroll;
    private ScrollContainer _guaranteesScroll;
    private ScrollContainer _relationsScroll;

    // Empty states
    private EmptyState _factionsEmptyState;
    private EmptyState _actionsEmptyState;
    private EmptyState _guaranteesEmptyState;
    private EmptyState _relationsEmptyState;

    // Categories in order
    private static readonly string[] CategoryIds = { "Factions", "Actions", "Guarantees", "Relations" };
    private static readonly string[] CategoryLabels = { "FACTIONS", "ACTIONS", "GUARANTEES", "RELATIONS" };

    private string _activeCategory = "Factions";
    private string _searchText = "";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[DiplomacyPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");
        var panel = new DiplomacyPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "DIPLOMACY";
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

        GD.Print("DiplomacyPanel: hazır (Stage 2 UI)");
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

        // === Header: Tension Bar ===
        var tensionRow = new HBoxContainer();
        tensionRow.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(tensionRow);

        var tensionTitle = new Label { Text = "WORLD TENSION" };
        tensionTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        tensionTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
        tensionRow.AddChild(tensionTitle);

        _tensionBar = new StatBar
        {
            LabelText = "",
            Value = DiplomacySystem.WorldTension,
            MinValue = 0,
            MaxValue = 100,
            ShowValue = true,
            ShowPercent = true,
            ColorSource = GameTheme.ColorSource.Custom,
            CustomFillColor = GetTensionColor(DiplomacySystem.WorldTension),
            BarHeight = 16,
            LabelWidth = 0,
            ValueWidth = 100,
            CustomMinimumSize = new Vector2(250, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        tensionRow.AddChild(_tensionBar);

        _tensionLabel = new Label { Text = $"{DiplomacySystem.WorldTension:F1}%" };
        _tensionLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _tensionLabel.AddThemeColorOverride("font_color", GetTensionColor(DiplomacySystem.WorldTension));
        tensionRow.AddChild(_tensionLabel);

        content.AddChild(new HSeparator());

        // === Target Country Selector ===
        var targetRow = new HBoxContainer();
        targetRow.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(targetRow);

        var targetLabel = new Label { Text = "TARGET:", CustomMinimumSize = new Vector2(80, 0) };
        targetLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        targetLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        targetRow.AddChild(targetLabel);

        _targetCountry = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 32)
        };
        _targetCountry.ItemSelected += OnTargetCountryChanged;
        targetRow.AddChild(_targetCountry);

        _targetRelationLabel = new Label();
        _targetRelationLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        _targetRelationLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        _targetRelationLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        targetRow.AddChild(_targetRelationLabel);

        content.AddChild(new HSeparator());

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

        // === Search Box (using SearchBox component) - for Relations tab ===
        _searchBox = new SearchBox
        {
            PlaceholderText = "Filter countries...",
            ShowResultCount = true,
            ShowClearButton = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Visible = false // Only show for Relations tab
        };
        _searchBox.TextChanged += OnSearchTextChanged;
        _searchBox.SearchSubmitted += OnSearchSubmitted;
        _searchBox.Cleared += OnSearchCleared;
        content.AddChild(_searchBox);

        content.AddChild(new HSeparator());

        // === Content Area: Stack of scroll containers (one per tab) ===
        var contentStack = new VBoxContainer();
        contentStack.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        contentStack.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(contentStack);

        // FACTIONS TAB
        _factionsScroll = new ScrollContainer();
        _factionsScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _factionsScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _factionsScroll.Visible = true;
        contentStack.AddChild(_factionsScroll);

        _factionsContainer = new VBoxContainer();
        _factionsContainer.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _factionsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _factionsScroll.AddChild(_factionsContainer);

        _factionsEmptyState = EmptyState.CreateGeneric("No Factions", "No factions exist yet. Create one or join an existing faction.");
        _factionsEmptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _factionsEmptyState.Visible = false;
        _factionsContainer.AddChild(_factionsEmptyState);

        // ACTIONS TAB
        _actionsScroll = new ScrollContainer();
        _actionsScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _actionsScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _actionsScroll.Visible = false;
        contentStack.AddChild(_actionsScroll);

        _actionsContainer = new VBoxContainer();
        _actionsContainer.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _actionsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _actionsScroll.AddChild(_actionsContainer);

        _actionsEmptyState = EmptyState.CreateGeneric("Select a Target", "Choose a target country above to see available diplomatic actions.");
        _actionsEmptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _actionsEmptyState.Visible = false;
        _actionsContainer.AddChild(_actionsEmptyState);

        // GUARANTEES TAB
        _guaranteesScroll = new ScrollContainer();
        _guaranteesScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _guaranteesScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _guaranteesScroll.Visible = false;
        contentStack.AddChild(_guaranteesScroll);

        _guaranteesContainer = new VBoxContainer();
        _guaranteesContainer.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _guaranteesContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _guaranteesScroll.AddChild(_guaranteesContainer);

        _guaranteesEmptyState = EmptyState.CreateGeneric("No Guarantees", "No guarantees given or received.");
        _guaranteesEmptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _guaranteesEmptyState.Visible = false;
        _guaranteesContainer.AddChild(_guaranteesEmptyState);

        // RELATIONS TAB
        _relationsScroll = new ScrollContainer();
        _relationsScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _relationsScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _relationsScroll.Visible = false;
        contentStack.AddChild(_relationsScroll);

        _relationsContainer = new VBoxContainer();
        _relationsContainer.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        _relationsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _relationsScroll.AddChild(_relationsContainer);

        _relationsEmptyState = EmptyState.CreateGeneric("No Relations", "No diplomatic relations data available.");
        _relationsEmptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _relationsEmptyState.Visible = false;
        _relationsContainer.AddChild(_relationsEmptyState);
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
        UpdateTabVisibility();
        RefreshAll();
    }

    private void UpdateTabVisibility()
    {
        bool showFactions = _activeCategory == "Factions";
        bool showActions = _activeCategory == "Actions";
        bool showGuarantees = _activeCategory == "Guarantees";
        bool showRelations = _activeCategory == "Relations";

        _factionsScroll.Visible = showFactions;
        _actionsScroll.Visible = showActions;
        _guaranteesScroll.Visible = showGuarantees;
        _relationsScroll.Visible = showRelations;

        _searchBox.Visible = showRelations;
        if (showRelations) _searchBox.Clear();
    }

    private void OnSearchTextChanged(string text)
    {
        _searchText = text.Trim().ToLower();
        RefreshRelations();
    }

    private void OnSearchSubmitted(string text) { }

    private void OnSearchCleared()
    {
        _searchText = "";
        RefreshRelations();
    }

    private void OnTargetCountryChanged(long idx) => RefreshActions();

    private void RefreshAll()
    {
        RefreshTension();
        RefreshTargetCountries();
        RefreshTargetRelation();
        RefreshFactions();
        RefreshActions();
        RefreshGuarantees();
        RefreshRelations();
    }

    private void RefreshTension()
    {
        if (_tensionBar == null || !GodotObject.IsInstanceValid(_tensionBar)) return;
        float tension = DiplomacySystem.WorldTension;
        _tensionBar.SetValue(tension);
        _tensionLabel.Text = $"{tension:F1}%";
        Color c = GetTensionColor(tension);
        _tensionBar.CustomFillColor = c;
        _tensionLabel.AddThemeColorOverride("font_color", c);
        _tensionBar.UpdateBarColor();
    }

    private Color GetTensionColor(float tension)
    {
        if (tension < 25) return GameTheme.Green;
        if (tension < 50) return GameTheme.Yellow;
        if (tension < 75) return GameTheme.Orange;
        return GameTheme.Red;
    }

    private void RefreshTargetCountries()
    {
        // Preserve selected country
        string prevTarget = null;
        if (_targetCountry.GetItemCount() > 0)
        {
            int sel = _targetCountry.Selected;
            if (sel >= 0 && sel < _targetCountry.GetItemCount())
                prevTarget = _targetCountry.GetItemMetadata(sel).AsString();
        }

        _targetCountry.Clear();
        if (!GameManager.Instance.HasPlayer) return;

        var playerId = GameManager.Instance.PlayerCountryId;
        var all = CountryDatabase.All().Where(c => c.Id != playerId && !c.IsAnnexed).ToList();

        int restoreIdx = 0;
        foreach (var c in all)
        {
            string relText = "";
            var rel = DiplomacySystem.GetRelationData(playerId, c.Id);
            if (rel != null) relText = $" (Rel: {rel.Value}, Op: {rel.Opinion})";
            _targetCountry.AddItem($"{c.Name} [{c.Id}]{relText}");
            _targetCountry.SetItemMetadata(_targetCountry.GetItemCount() - 1, c.Id);
            if (c.Id == prevTarget) restoreIdx = _targetCountry.GetItemCount() - 1;
        }
        if (_targetCountry.GetItemCount() > 0)
            _targetCountry.Selected = Mathf.Clamp(restoreIdx, 0, _targetCountry.GetItemCount() - 1);
    }

    private void RefreshTargetRelation()
    {
        if (_targetRelationLabel == null || !GodotObject.IsInstanceValid(_targetRelationLabel)) return;

        if (_targetCountry.GetItemCount() == 0)
        {
            _targetRelationLabel.Text = "";
            return;
        }

        string targetId = _targetCountry.GetItemMetadata(_targetCountry.Selected).AsString();
        if (string.IsNullOrEmpty(targetId)) { _targetRelationLabel.Text = ""; return; }

        var playerId = GameManager.Instance.PlayerCountryId;
        var rel = DiplomacySystem.GetRelationData(playerId, targetId);
        var target = CountryDatabase.Get(targetId);

        string war = WarSystem.IsAtWarWith(playerId, targetId) ? " ⚔ AT WAR" : "";
        string faction = DiplomacySystem.GetFactionOf(targetId) != null ? $" Faction: {DiplomacySystem.GetFactionOf(targetId).Name}" : "";
        string guarantee = DiplomacySystem.HasGuarantee(playerId, targetId) ? " ★ GUARANTEE" : "";
        string milAccess = DiplomacySystem.HasMilitaryAccess(playerId, targetId) ? " 🛡 MIL ACCESS" : "";
        string navAccess = DiplomacySystem.HasNavalAccess(playerId, targetId) ? " ⚓ NAVAL ACCESS" : "";

        if (rel != null)
            _targetRelationLabel.Text = $"Relation: {rel.Value}  |  Opinion: {rel.Opinion}{war}{faction}{guarantee}{milAccess}{navAccess}";
        else
            _targetRelationLabel.Text = $"Relation: 0  |  Opinion: 0{war}{faction}{guarantee}{milAccess}{navAccess}";
    }

    // ===== FACTIONS TAB =====
    private void RefreshFactions()
    {
        ClearChildren(_factionsContainer, _factionsEmptyState);

        if (DiplomacySystem.Factions.Count == 0)
        {
            _factionsEmptyState.Visible = true;
            return;
        }

        _factionsEmptyState.Visible = false;

        foreach (var kv in DiplomacySystem.Factions)
        {
            var f = kv.Value;
            var card = CreateFactionCard(f);
            _factionsContainer.AddChild(card);
        }
    }

    private Control CreateFactionCard(DiplomacySystem.Faction f)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(new Color(f.Color), true));

        // Header row
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddChild(header);

        // Category chip
        var catChip = Chip.CreateCategory("FACTION", new Color(f.Color));
        catChip.FontSize = GameTheme.FontSizeTiny;
        header.AddChild(catChip);

        // Name
        var nameLabel = new Label { Text = f.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        nameLabel.AddThemeColorOverride("font_color", new Color(f.Color));
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        // Leader
        var leader = CountryDatabase.Get(f.LeaderId);
        var leaderLabel = new Label { Text = $"Leader: {leader?.Name ?? f.LeaderId}" };
        leaderLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        leaderLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        header.AddChild(leaderLabel);

        // Members
        var memberNames = f.MemberIds.Select(m => CountryDatabase.Get(m)?.Name ?? m).ToList();
        var memberLabel = new Label { Text = $"Members: {string.Join(", ", memberNames)}" };
        memberLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        memberLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        container.AddChild(memberLabel);

        // Action buttons
        bool playerInFaction = f.IsMember(GameManager.Instance?.PlayerCountryId ?? "");
        if (playerInFaction)
        {
            var leaveBtn = new Button { Text = "LEAVE FACTION", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(140, 28) };
            leaveBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            leaveBtn.Pressed += () => { DiplomacySystem.LeaveFaction(); RefreshAll(); };
            GameTheme.StyleButton(leaveBtn);
            header.AddChild(leaveBtn);
        }
        else if (f.MemberIds.Count > 0)
        {
            var joinBtn = new Button { Text = "JOIN FACTION", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(140, 28) };
            joinBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            joinBtn.Pressed += () =>
            {
                if (DiplomacySystem.RequestJoinFaction(f.Id)) RefreshAll();
            };
            GameTheme.StyleButton(joinBtn);
            header.AddChild(joinBtn);
        }

        // Tooltip
        var tooltipData = new TooltipBuilder.TooltipData
        {
            Title = f.Name,
            Category = "Faction",
            CategoryColor = new Color(f.Color),
            Description = $"Leader: {leader?.Name ?? f.LeaderId}\nMembers: {memberNames.Count}",
        };
        container.TooltipText = TooltipBuilder.BuildText(tooltipData);

        return container;
    }

    // ===== ACTIONS TAB =====
    private void RefreshActions()
    {
        ClearChildren(_actionsContainer, _actionsEmptyState);

        if (!GameManager.Instance.HasPlayer)
        {
            _actionsEmptyState.SetContent("No Player Country", "Select a country to play first.", "🌍");
            _actionsEmptyState.Visible = true;
            return;
        }

        if (_targetCountry.GetItemCount() == 0)
        {
            _actionsEmptyState.SetContent("Select a Target", "Choose a target country above to see available diplomatic actions.", "🎯");
            _actionsEmptyState.Visible = true;
            return;
        }

        string targetId = _targetCountry.GetItemMetadata(_targetCountry.Selected).AsString();
        var target = CountryDatabase.Get(targetId);
        if (target == null || target.IsAnnexed)
        {
            _actionsEmptyState.SetContent("Invalid Target", "The selected target is no longer valid.", "❌");
            _actionsEmptyState.Visible = true;
            return;
        }

        _actionsEmptyState.Visible = false;

        var playerId = GameManager.Instance.PlayerCountryId;
        bool atWar = WarSystem.IsAtWarWith(playerId, targetId);
        bool hasGuarantee = DiplomacySystem.HasGuarantee(playerId, targetId);
        bool hasMilAccess = DiplomacySystem.HasMilitaryAccess(playerId, targetId);
        bool hasNavAccess = DiplomacySystem.HasNavalAccess(playerId, targetId);
        var myFaction = DiplomacySystem.GetFactionOf(playerId);
        var targetFaction = DiplomacySystem.GetFactionOf(targetId);
        bool sameFaction = myFaction != null && myFaction == targetFaction;

        // WAR JUSTIFICATION section
        if (!atWar)
        {
            var check = CasusBelliSystem.CheckWarPreconditions(playerId, targetId);

            var justTitle = new Label { Text = "WAR JUSTIFICATION" };
            justTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
            justTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
            _actionsContainer.AddChild(justTitle);

            // Checklist
            foreach (var item in check.Items)
            {
                var l = new Label { Text = $"  {(item.Met ? "✓" : "✗")} {item.Label}", TooltipText = item.Detail };
                l.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                l.AddThemeColorOverride("font_color", item.Met ? GameTheme.GreenBright : GameTheme.RedBright);
                _actionsContainer.AddChild(l);
            }

            // In-progress justifications
            var inProgress = CasusBelliSystem.Justifications
                .Where(j => j.CountryId == playerId && j.TargetId == targetId
                         && !j.IsConsumed && (j.BuildDaysLeft > 0 || j.UltimatumDaysLeft > 0))
                .ToList();
            foreach (var j in inProgress)
            {
                var t = CasusBelliSystem.GetType(j.TypeId);
                string status = j.BuildDaysLeft > 0
                    ? $"  ⏳ BUILDING CASE: {t?.Name ?? j.TypeId} — {j.BuildDaysLeft} days left"
                    : $"  ⚠ CASE READY — ultimatum: {j.UltimatumDaysLeft} days left";
                var l = new Label { Text = status };
                l.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                l.AddThemeColorOverride("font_color", GameTheme.Orange);
                _actionsContainer.AddChild(l);
            }

            // BUILD CASE buttons
            foreach (var type in CasusBelliSystem.GetStartableTypes(playerId, targetId))
            {
                var buildBtn = new Button
                {
                    Text = $"BUILD CASE: {type.Name} ({type.BuildDays}d, {type.PPCost} PP)",
                    FocusMode = Control.FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(240, 28),
                    TooltipText = type.Description
                };
                buildBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                buildBtn.Pressed += () =>
                {
                    if (CasusBelliSystem.StartBuildCase(playerId, targetId, type.Id))
                        RefreshAll();
                };
                GameTheme.StyleButton(buildBtn);
                _actionsContainer.AddChild(buildBtn);
            }

            // REQUEST RECOGNITION (M52.2)
            if (!atWar && RecognitionSystem.WasBorn(targetId) && !RecognitionSystem.IsRecognized(playerId, targetId))
            {
                var recTitle = new Label { Text = "RECOGNITION" };
                recTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
                recTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
                _actionsContainer.AddChild(recTitle);

                int recognizerCount = RecognitionSystem.RecognizerCount(targetId);
                int establishedCount = RecognitionSystem.EstablishedCountForOrg(targetId);
                double ratio = establishedCount > 0 ? (double)recognizerCount / establishedCount : 0.0;

                var recInfo = new Label
                {
                    Text = $"  Recognized by: {recognizerCount}/{establishedCount} ({ratio:P0}) — REQUEST RECOGNITION (25 PP)"
                };
                recInfo.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                recInfo.AddThemeColorOverride("font_color", GameTheme.TextMuted);
                _actionsContainer.AddChild(recInfo);

                var recBtn = new Button
                {
                    Text = "REQUEST RECOGNITION (25 PP)",
                    FocusMode = Control.FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(240, 32),
                    Disabled = GameManager.Instance.PlayerCountryData.PoliticalPower < 25
                };
                recBtn.TooltipText = $"Ask {target.Name} to recognize your country. Accepted: +10 relation both ways. Rejected: -5 relation both ways, +2 World Tension.";
                recBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                recBtn.Pressed += () =>
                {
                    if (DiplomacySystem.RequestRecognition(targetId)) RefreshAll();
                };
                GameTheme.StyleButton(recBtn);
                _actionsContainer.AddChild(recBtn);

                _actionsContainer.AddChild(new HSeparator());
            }

            // DECLARE WAR
            int warCost = WarSystem.CalculateWarGoalCost(targetId);
            var warBtn = new Button
            {
                Text = $"DECLARE WAR ({warCost} PP)",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(240, 32),
                Disabled = !check.CanDeclare
            };
            warBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            if (check.CanDeclare)
            {
                warBtn.TooltipText = "Declare war on this country (justified)";
            }
            else
            {
                var missing = check.Items.Where(i => !i.Met).Select(i => $"✗ {i.Label}: {i.Detail}");
                warBtn.TooltipText = "War declaration blocked:\n" + string.Join("\n", missing);

                var blockedLabel = new Label { Text = $"WAR DECLARATION BLOCKED — {check.Items.First(i => !i.Met).Label}" };
                blockedLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                blockedLabel.AddThemeColorOverride("font_color", GameTheme.Red);
                _actionsContainer.AddChild(blockedLabel);
            }
            warBtn.AddThemeColorOverride("font_disabled_color", GameTheme.TextMuted);
            warBtn.Pressed += () =>
            {
                CasusBelliSystem.LogWarDeclarationCheck(playerId, targetId);
                if (DiplomacySystem.DeclareWar(targetId)) RefreshAll();
            };
            GameTheme.StyleButton(warBtn);
            _actionsContainer.AddChild(warBtn);
        }
        else
        {
            // AT WAR - show war info and peace options
            var war = WarSystem.GetWarWith(targetId);
            if (war != null)
            {
                var warInfo = new Label { Text = $"⚔ AT WAR — Enemy div: {war.EnemyDivisions} | Progress: {war.WarProgress:F0}% | Losses: {war.PlayerCasualties:N0}/{war.EnemyCasualties:N0}" };
                warInfo.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                warInfo.AddThemeColorOverride("font_color", GameTheme.Red);
                _actionsContainer.AddChild(warInfo);

                if (war.WarProgress >= 50)
                {
                    var peaceBtn = new Button { Text = "PROPOSE PEACE (VICTORY — PUPPET/ANNEX)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 32) };
                    peaceBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                    peaceBtn.Pressed += () =>
                    {
                        var deal = DiplomacySystem.ProposePeace(playerId, targetId);
                        DiplomacySystem.ApplyPeaceDeal(deal);
                        RefreshAll();
                    };
                    GameTheme.StyleButton(peaceBtn);
                    _actionsContainer.AddChild(peaceBtn);
                }

                var whiteBtn = new Button { Text = "WHITE PEACE (STATUS QUO)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 32) };
                whiteBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                whiteBtn.Pressed += () =>
                {
                    if (DiplomacySystem.WhitePeace(targetId)) RefreshAll();
                };
                GameTheme.StyleButton(whiteBtn);
                _actionsContainer.AddChild(whiteBtn);

                var confBtn = new Button { Text = "PEACE CONFERENCE (DEMANDS)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 32) };
                confBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                confBtn.TooltipText = "Spend your war score on demands (territory, puppet, reparations)";
                confBtn.Pressed += () => PeaceConferencePanel.OpenUnder(this, war, playerId);
                GameTheme.StyleButton(confBtn);
                _actionsContainer.AddChild(confBtn);
            }
        }

        _actionsContainer.AddChild(new HSeparator());

        // GUARANTEE
        if (!atWar && !hasGuarantee)
        {
            var guarBtn = new Button { Text = "GUARANTEE INDEPENDENCE (25 PP)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 32) };
            guarBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            guarBtn.Pressed += () => { if (DiplomacySystem.GuaranteeIndependence(targetId)) RefreshAll(); };
            GameTheme.StyleButton(guarBtn);
            _actionsContainer.AddChild(guarBtn);
        }
        else if (hasGuarantee)
        {
            var revokeBtn = new Button { Text = "REVOKE GUARANTEE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 32) };
            revokeBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            revokeBtn.Pressed += () => { DiplomacySystem.RevokeGuarantee(targetId); RefreshAll(); };
            GameTheme.StyleButton(revokeBtn);
            _actionsContainer.AddChild(revokeBtn);
        }

        // MILITARY ACCESS
        if (!atWar && !hasMilAccess)
        {
            var milBtn = new Button { Text = "REQUEST MILITARY ACCESS (10 PP)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 32) };
            milBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            milBtn.Pressed += () => { if (DiplomacySystem.RequestMilitaryAccess(targetId)) RefreshAll(); };
            GameTheme.StyleButton(milBtn);
            _actionsContainer.AddChild(milBtn);
        }

        // NAVAL ACCESS
        if (!atWar && !hasNavAccess)
        {
            var navBtn = new Button { Text = "REQUEST NAVAL ACCESS (10 PP)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 32) };
            navBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            navBtn.Pressed += () => { if (DiplomacySystem.RequestNavalAccess(targetId)) RefreshAll(); };
            GameTheme.StyleButton(navBtn);
            _actionsContainer.AddChild(navBtn);
        }

        // IMPROVE RELATIONS
        if (!atWar)
        {
            var improveBtn = new Button { Text = "IMPROVE RELATIONS (10 PP/day)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 32) };
            improveBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            improveBtn.Pressed += () => { if (DiplomacySystem.ImproveRelations(targetId)) RefreshAll(); };
            GameTheme.StyleButton(improveBtn);
            _actionsContainer.AddChild(improveBtn);
        }
    }

    // ===== GUARANTEES TAB =====
    private void RefreshGuarantees()
    {
        ClearChildren(_guaranteesContainer, _guaranteesEmptyState);

        if (!GameManager.Instance.HasPlayer)
        {
            _guaranteesEmptyState.SetContent("No Player Country", "Select a country to play first.", "🌍");
            _guaranteesEmptyState.Visible = true;
            return;
        }

        var guarantees = DiplomacySystem.GetGuaranteesBy(GameManager.Instance.PlayerCountryId);
        bool hasAny = guarantees.Count > 0;

        // Given guarantees
        if (guarantees.Count > 0)
        {
            var givenTitle = new Label { Text = "GUARANTEES GIVEN" };
            givenTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
            givenTitle.AddThemeColorOverride("font_color", GameTheme.Gold);
            _guaranteesContainer.AddChild(givenTitle);

            foreach (var g in guarantees)
            {
                var target = CountryDatabase.Get(g.TargetId);
                var row = CreateGuaranteeRow(target, g.TargetId, true);
                _guaranteesContainer.AddChild(row);
            }
        }

        // Received guarantees
        var received = DiplomacySystem.GetGuaranteesFor(GameManager.Instance.PlayerCountryId);
        if (received.Count > 0)
        {
            hasAny = true;
            var receivedTitle = new Label { Text = "GUARANTEES RECEIVED FROM" };
            receivedTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
            receivedTitle.AddThemeColorOverride("font_color", GameTheme.Blue);
            _guaranteesContainer.AddChild(receivedTitle);

            foreach (var gId in received)
            {
                var target = CountryDatabase.Get(gId);
                var row = CreateGuaranteeRow(target, gId, false);
                _guaranteesContainer.AddChild(row);
            }
        }

        if (!hasAny)
        {
            _guaranteesEmptyState.Visible = true;
        }
        else
        {
            _guaranteesEmptyState.Visible = false;
        }
    }

    private Control CreateGuaranteeRow(CountryData target, string targetId, bool isGiven)
    {
        var container = new HBoxContainer();
        container.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        container.AddThemeStyleboxOverride("normal", GameTheme.CardBox(isGiven ? GameTheme.Gold : GameTheme.Blue, true));

        // Flag
        var flagBox = new FlagBox { CustomMinimumSize = new Vector2(24, 16) };
        flagBox.SetCountry(targetId);
        container.AddChild(flagBox);

        // Name
        var nameLabel = new Label { Text = $"★ {target?.Name ?? targetId}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        nameLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        nameLabel.AddThemeColorOverride("font_color", isGiven ? GameTheme.Gold : GameTheme.BlueBright);
        container.AddChild(nameLabel);

        // Revoke button (only for given)
        if (isGiven)
        {
            var revokeBtn = new Button { Text = "REVOKE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(80, 26) };
            revokeBtn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            revokeBtn.Pressed += () => { DiplomacySystem.RevokeGuarantee(targetId); RefreshAll(); };
            GameTheme.StyleButton(revokeBtn);
            container.AddChild(revokeBtn);
        }

        // Tooltip
        var tooltipData = new TooltipBuilder.TooltipData
        {
            Title = isGiven ? "Guarantee Given" : "Guarantee Received",
            Description = $"{(isGiven ? "You guarantee" : target?.Name ?? targetId)}'s independence",
            Category = "Diplomatic",
            CategoryColor = isGiven ? GameTheme.Gold : GameTheme.Blue,
        };
        container.TooltipText = TooltipBuilder.BuildText(tooltipData);

        return container;
    }

    // ===== RELATIONS TAB =====
    private void RefreshRelations()
    {
        ClearChildren(_relationsContainer, _relationsEmptyState);

        if (!GameManager.Instance.HasPlayer)
        {
            _relationsEmptyState.SetContent("No Player Country", "Select a country to play first.", "🌍");
            _relationsEmptyState.Visible = true;
            return;
        }

        var playerId = GameManager.Instance.PlayerCountryId;
        var all = CountryDatabase.All().Where(c => c.Id != playerId && !c.IsAnnexed)
            .OrderByDescending(c => DiplomacySystem.GetRelation(playerId, c.Id))
            .ToList();

        // Filter by search
        if (!string.IsNullOrEmpty(_searchText))
        {
            all = all.Where(c =>
                c.Name.ToLower().Contains(_searchText) ||
                c.Id.ToLower().Contains(_searchText)
            ).ToList();
        }

        int totalCount = CountryDatabase.All().Where(c => c.Id != playerId && !c.IsAnnexed).Count();
        int filteredCount = all.Count;

        _searchBox.UpdateResultCount(filteredCount, totalCount);

        if (filteredCount == 0)
        {
            if (!string.IsNullOrEmpty(_searchText))
            {
                _relationsEmptyState.SetContent(
                    "No Results Found",
                    $"No countries match \"{_searchText}\". Try a different search term or clear filters.",
                    "🔍"
                );
            }
            else
            {
                _relationsEmptyState.SetContent("No Relations", "No diplomatic relations data available.", "📋");
            }
            _relationsEmptyState.Visible = true;
            return;
        }

        _relationsEmptyState.Visible = false;

        foreach (var c in all)
        {
            int rel = DiplomacySystem.GetRelation(playerId, c.Id);
            int op = DiplomacySystem.GetOpinion(playerId, c.Id);
            bool atWar = WarSystem.IsAtWarWith(playerId, c.Id);

            var row = CreateRelationRow(c, rel, op, atWar);
            _relationsContainer.AddChild(row);
        }
    }

    private Control CreateRelationRow(CountryData c, int rel, int op, bool atWar)
    {
        var row = new ListRow
        {
            Title = $"{c.Name} [{c.Id}]",
            Subtitle = $"Relation: {rel}  |  Opinion: {op}",
            RightText = atWar ? "⚔ WAR" : "",
            AccentColor = atWar ? GameTheme.Red : (rel > 50 ? GameTheme.Green : (rel < -50 ? GameTheme.Red : GameTheme.TextMuted)),
            UseAccentColor = true,
            RowHeight = 56
        };

        // Add flag
        var flagBox = new FlagBox { CustomMinimumSize = new Vector2(24, 16) };
        flagBox.SetCountry(c.Id);
        var mainContainer = row.GetChild(0) as HBoxContainer;
        if (mainContainer != null)
        {
            mainContainer.MoveChild(flagBox, 0);
        }

        // Tooltip
        var tooltipData = new TooltipBuilder.TooltipData
        {
            Title = $"{c.Name} [{c.Id}]",
            Description = $"Relation: {rel}\nOpinion: {op}\n{(atWar ? "AT WAR" : "At Peace")}",
            Category = "Relations",
            CategoryColor = GameTheme.Blue,
        };
        row.TooltipText = TooltipBuilder.BuildText(tooltipData);

        // Click to set as target
        row.RowPressed += () =>
        {
            _tabBar.SelectedIndex = 1; // Switch to Actions tab
            _tabBar.UpdateVisuals();
            UpdateTabVisibility();
            // Find and select this country in target dropdown
            for (int i = 0; i < _targetCountry.GetItemCount(); i++)
            {
                if (_targetCountry.GetItemMetadata(i).AsString() == c.Id)
                {
                    _targetCountry.Selected = i;
                    RefreshTargetRelation();
                    RefreshActions();
                    break;
                }
            }
        };
        GameTheme.AddHoverScale(row, 1.02f);

        return row;
    }

    private static void ClearChildren(Node container, EmptyState emptyState)
    {
        foreach (var child in container.GetChildren())
        {
            if (child == emptyState) continue;
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    public override void Close()
    {
        GD.Print("DiplomacyPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;

        base.Close();
    }
}