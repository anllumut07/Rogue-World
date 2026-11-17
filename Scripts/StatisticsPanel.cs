using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Statistics Panel (M20.1 + Stage 2 UI Upgrade): HUD STATISTICS button opens this.
/// Shows country indicators grouped by category with progress bars, and trend graph.
/// Built with reusable UI components from Scripts/UI/
/// - Panel frame via GamePanel
/// - Indicator selector via OptionButton (could be SearchBox in future)
/// - Progress bars via ProgressBar with GameTheme colors
/// - GraphControl for trend visualization
/// </summary>
public partial class StatisticsPanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    // UI Components
    private OptionButton _indicatorSelector;
    private GraphControl _graph;
    private VBoxContainer _contentContainer;
    private ScrollContainer _contentScroll;
    private EmptyState _emptyState;

    private string _selectedIndicatorId = "";
    private readonly List<string> _optionIds = new();

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[StatisticsPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");
        var panel = new StatisticsPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "STATISTICS & BUDGET";
        MinSize = new Vector2(900, 560);
        MaxSize = new Vector2(1200, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        RefreshContent();
        IsOpen = true;

        GD.Print("StatisticsPanel: hazır (Stage 2 UI)");
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

        // Header with country name and indicator selector
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(headerRow);

        var countryLabel = new Label { Text = "No Country Selected" };
        countryLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        countryLabel.AddThemeColorOverride("font_color", GameTheme.Gold);
        countryLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _countryNameLabel = countryLabel;
        headerRow.AddChild(countryLabel);

        // Indicator selector for graph
        var selLbl = new Label { Text = "Graph:", CustomMinimumSize = new Vector2(50, 0) };
        selLbl.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        headerRow.AddChild(selLbl);

        _indicatorSelector = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(220, 0) };
        _indicatorSelector.ItemSelected += OnIndicatorSelected;
        headerRow.AddChild(_indicatorSelector);

        content.AddChild(new HSeparator());

        // Content area
        _contentScroll = new ScrollContainer();
        _contentScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _contentScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(_contentScroll);

        _contentContainer = new VBoxContainer();
        _contentContainer.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _contentContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _contentScroll.AddChild(_contentContainer);

        _emptyState = EmptyState.CreateGeneric("No Data", "Select a country to view statistics.");
        _emptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _emptyState.Visible = false;
        _contentContainer.AddChild(_emptyState);

        // Graph at bottom
        _graph = new GraphControl { CustomMinimumSize = new Vector2(0, 220) };
        _graph.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(_graph);
    }

    private Label _countryNameLabel;

    private void RefreshContent()
    {
        foreach (var child in _contentContainer.GetChildren())
        {
            if (child == _emptyState) continue;
            _contentContainer.RemoveChild(child);
            child.QueueFree();
        }

        // Populate indicator selector
        _optionIds.Clear();
        _indicatorSelector.Clear();
        foreach (var def in StatisticsSystem.Definitions.OrderBy(d => d.Name))
        {
            _optionIds.Add(def.Id);
            _indicatorSelector.AddItem(def.Name);
        }
        if (_optionIds.Count > 0)
        {
            _indicatorSelector.Select(0);
            _selectedIndicatorId = _optionIds[0];
        }

        var gm = GameManager.Instance;
        if (!gm.HasPlayer || gm.PlayerCountryData == null)
        {
            _emptyState.SetContent(
                "No Player Country",
                "Select a country to play first.",
                "🌍"
            );
            _emptyState.Visible = true;

            _countryNameLabel.Text = "No Country Selected";
            return;
        }

        _emptyState.Visible = false;

        string countryId = gm.PlayerCountryData.Id;
        string countryName = gm.PlayerCountryData.Name;

        _countryNameLabel.Text = $"{countryName} ({countryId}) — INDICATORS";

        // Group indicators by category
        foreach (var catGroup in StatisticsSystem.Definitions.GroupBy(d => d.Category))
        {
            var catHeader = new Label { Text = catGroup.Key.ToUpper() };
            catHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            catHeader.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            _contentContainer.AddChild(catHeader);

            foreach (var def in catGroup.OrderBy(d => d.Name))
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", GameTheme.SpaceM);
                _contentContainer.AddChild(row);

                var name = new Label { Text = def.Name };
                name.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                name.TooltipText = def.Description;
                row.AddChild(name);

                double value = StatisticsSystem.GetValue(countryId, def.Id);
                var valueLbl = new Label { Text = $"{value:N1}", CustomMinimumSize = new Vector2(80, 0) };
                valueLbl.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
                valueLbl.AddThemeColorOverride("font_color", GameTheme.Gold);
                valueLbl.HorizontalAlignment = HorizontalAlignment.Right;
                row.AddChild(valueLbl);

                double span = def.Max - def.Min;
                double pct = span > 0 ? (value - def.Min) / span * 100.0 : 0;
                pct = Math.Clamp(pct, 0, 100);

                var bar = new ProgressBar
                {
                    MinValue = 0,
                    MaxValue = 100,
                    Value = pct,
                    CustomMinimumSize = new Vector2(180, 16)
                };
                // Apply GameTheme progress bar style
                var (bg, fill) = GameTheme.ProgressBarStyles(GameTheme.Gold);
                bar.AddThemeStyleboxOverride("background", bg);
                bar.AddThemeStyleboxOverride("fill", fill);
                row.AddChild(bar);
            }
        }

        var note = new Label { Text = "Indicators fluctuate deterministically each game day; Values clamped to [Min, Max] range." };
        note.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        note.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _contentContainer.AddChild(note);

        // Update graph
        _graph.SetSeries(countryId, _selectedIndicatorId);
    }

    private void OnIndicatorSelected(long index)
    {
        if (index < 0 || index >= _optionIds.Count) return;
        _selectedIndicatorId = _optionIds[(int)index];
        var gm = GameManager.Instance;
        _graph.SetSeries(gm?.PlayerCountryData?.Id ?? "", _selectedIndicatorId);
    }

    public override void Close()
    {
        GD.Print("StatisticsPanel: kapatılıyor");
        base.Close();
    }
}