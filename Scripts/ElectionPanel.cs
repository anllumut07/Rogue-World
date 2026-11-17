using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Election Panel (M23.1 + Stage 2 UI Upgrade):
/// Shows election calendar and vote prediction.
/// Uses GameTheme + reusable UI components (GamePanel, OptionButton, Chip, ProgressBar, TooltipBuilder).
/// Preserves: backdrop blocking map input, ESC to close, country selector, vote prediction visualization.
/// </summary>
public partial class ElectionPanel : GamePanel
{
    public static bool IsOpen { get; private set; } = false;

    private VBoxContainer _contentContainer;
    private Label _nextElectionLabel;
    private Label _votePredictionLabel;
    private Label _breakdownLabel;
    private Label _factorsLabel;
    private Label _campaignBonusLabel;
    private Label _promisesLabel;
    private Button _fulfillPromiseBtn;
    private OptionButton _countrySelector;
    private Button _closeButton;

    private string _selectedCountryId = "";
    private ElectionSystem.VotePrediction _currentPrediction;
    private List<CountryData> _countries;
    private EmptyState _emptyState;

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        var panel = new ElectionPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "ELECTIONS";
        MinSize = new Vector2(860, 520);
        MaxSize = new Vector2(1200, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        Refresh();
        IsOpen = true;
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void BuildUi()
    {
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // Title
        var titleLabel = new Label { Text = "ELECTIONS — Election Calendar and Vote Prediction" };
        titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeLarge);
        titleLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        content.AddChild(titleLabel);

        // Country selector
        var selRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        selRow.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        var selLabel = new Label { Text = "Country:" };
        selLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        selRow.AddChild(selLabel);

        _countrySelector = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _countrySelector.ItemSelected += OnCountrySelected;
        selRow.AddChild(_countrySelector);
        content.AddChild(selRow);

        content.AddChild(new HSeparator());

        // Next election
        _nextElectionLabel = new Label();
        _nextElectionLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        _nextElectionLabel.AddThemeColorOverride("font_color", GameTheme.BlueBright);
        content.AddChild(_nextElectionLabel);

        // Vote prediction
        _votePredictionLabel = new Label();
        _votePredictionLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeLarge);
        content.AddChild(_votePredictionLabel);

        // Satisfaction breakdown
        _breakdownLabel = new Label();
        _breakdownLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _breakdownLabel.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
        content.AddChild(_breakdownLabel);

        // Factors
        _factorsLabel = new Label();
        _factorsLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _factorsLabel.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
        content.AddChild(_factorsLabel);

        // Campaign bonus
        _campaignBonusLabel = new Label();
        _campaignBonusLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _campaignBonusLabel.AddThemeColorOverride("font_color", GameTheme.GreenBright);
        content.AddChild(_campaignBonusLabel);

        // Active promises
        _promisesLabel = new Label();
        _promisesLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _promisesLabel.AddThemeColorOverride("font_color", GameTheme.OrangeBright);
        content.AddChild(_promisesLabel);

        _fulfillPromiseBtn = new Button { Text = "FULFILL PROMISE", FocusMode = Control.FocusModeEnum.None, Visible = false };
        _fulfillPromiseBtn.Pressed += OnFulfillPromise;
        GameTheme.StyleButton(_fulfillPromiseBtn);
        content.AddChild(_fulfillPromiseBtn);

        content.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // Close button row
        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _closeButton = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None };
        _closeButton.Pressed += Close;
        GameTheme.StyleButton(_closeButton);
        btnRow.AddChild(_closeButton);
        content.AddChild(btnRow);
    }

    private void OnCountrySelected(long idx)
    {
        if (idx < 0 || idx >= _countries.Count) return;
        _selectedCountryId = _countries[(int)idx].Id;
        Refresh();
    }

    private void Refresh()
    {
        FillCountrySelector();

        int idx = 0;
        for (int i = 0; i < _countries.Count; i++)
        {
            if (_countries[i].Id == _selectedCountryId) { idx = i; break; }
        }
        if (idx < _countrySelector.GetItemCount())
            _countrySelector.Selected = idx;

        if (GameManager.Instance == null) return;
        _currentPrediction = ElectionSystem.GetVotePrediction(_selectedCountryId, GameManager.Instance.TotalDays);
        UpdateLabels();
    }

    private void FillCountrySelector()
    {
        _countrySelector.Clear();
        _countries = CountryDatabase.All().Where(c => c != null).OrderBy(c => c.Name).ToList();
        for (int i = 0; i < _countries.Count; i++)
            _countrySelector.AddItem(_countries[i].Name, i);
    }

    private void UpdateLabels()
    {
        var p = _currentPrediction;
        if (p == null) return;

        // Title
        var titleLabel = ContentContainer.GetChild(0) as Label;
        if (titleLabel != null)
            titleLabel.Text = $"ELECTIONS — {p.CountryName} ({p.CountryId})";

        // No elections (monarchy / one-party)
        if (p.DaysUntilElection < 0)
        {
            _nextElectionLabel.Text = "No elections (monarchy / one-party regime — governed by party Congress)";
            _votePredictionLabel.Text = "Vote prediction: —";
            _breakdownLabel.Text = "Public satisfaction: —";
            _factorsLabel.Text = "No general election — the ruling party is governed through the party Congress.";
            return;
        }

        // Counter
        string dueText = p.ElectionDue ? " — ELECTION DAY (election can be held per M23.3)" : "";
        _nextElectionLabel.Text = $"Next election: {p.DaysUntilElection} days{dueText} (term: {p.TermLengthYears} years, {p.GovernmentType})";

        // Vote prediction with color coding
        Color voteColor = p.PredictedVote >= 60 ? GameTheme.GreenBright
                          : p.PredictedVote >= 40 ? GameTheme.YellowBright
                          : GameTheme.RedBright;
        _votePredictionLabel.AddThemeColorOverride("font_color", voteColor);
        _votePredictionLabel.Text = $"Predicted ruling party vote share: {p.PredictedVote:F1}% (base satisfaction: {p.BaseSatisfaction:F1})";

        // Satisfaction breakdown
        var groups = PublicSatisfactionSystem.Groups;
        var top = groups.OrderByDescending(g => PublicSatisfactionSystem.GetSatisfaction(p.CountryId, g.Id)).First();
        var bottom = groups.OrderBy(g => PublicSatisfactionSystem.GetSatisfaction(p.CountryId, g.Id)).First();
        _breakdownLabel.Text = $"Average public satisfaction across 9 social groups: {p.BaseSatisfaction:F1} | Highest: {top.Name} ({PublicSatisfactionSystem.GetSatisfaction(p.CountryId, top.Id):F0}) | Lowest: {bottom.Name} ({PublicSatisfactionSystem.GetSatisfaction(p.CountryId, bottom.Id):F0})";

        // Factors
        double gdpGrowth = StatisticsSystem.GetValue(p.CountryId, "gdp_growth");
        double deficit = StatisticsSystem.GetValue(p.CountryId, "budget_deficit");
        double debt = StatisticsSystem.GetValue(p.CountryId, "public_debt");
        double inflation = StatisticsSystem.GetValue(p.CountryId, "inflation");
        double unemployment = StatisticsSystem.GetValue(p.CountryId, "unemployment");

        int scandalCount = 0;
        foreach (var m in CabinetSystem.Ministries)
        {
            string ministerId = CabinetSystem.GetMinister(p.CountryId, m.Id);
            if (!string.IsNullOrEmpty(ministerId) && !string.IsNullOrEmpty(CabinetSystem.GetScandalStatus(ministerId)))
                scandalCount++;
        }

        _factorsLabel.Text =
            $"Factors: GDP growth %{gdpGrowth:F1} | Budget deficit %{deficit:F1} | State debt %{debt:F0} | Inflation %{inflation:F1} | Unemployment %{unemployment:F1} | Active scandals: {scandalCount}";

        // Campaign bonus
        _campaignBonusLabel.Text = $"Active campaign bonus: +{p.CampaignBonus:F1} votes";

        // Active promises
        var promises = ElectionCampaignSystem.GetActivePromises(p.CountryId);
        if (promises.Count > 0)
        {
            _promisesLabel.Text = $"Active promises ({promises.Count}): {string.Join(", ", promises.Select(pr => pr.ActionName))}";
            _fulfillPromiseBtn.Visible = true;
        }
        else
        {
            _promisesLabel.Text = "No active promises.";
            _fulfillPromiseBtn.Visible = false;
        }
    }

    private void OnFulfillPromise()
    {
        if (string.IsNullOrEmpty(_selectedCountryId)) return;
        var promises = ElectionCampaignSystem.GetActivePromises(_selectedCountryId);
        if (promises.Count == 0) return;
        ElectionCampaignSystem.FulfillPromise(_selectedCountryId, 0);
        GD.Print($"[ElectionPanel] {_selectedCountryId}: promise fulfilled '{promises[0].ActionName}'");
        Refresh();
    }

    public override void Close()
    {
        GD.Print("ElectionPanel: kapatılıyor");
        base.Close();
    }
}