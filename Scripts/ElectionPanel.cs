using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// M23.1: Seçim paneli — takvim + oy tahmini.
// Overlay (Control), backdrop MouseFilter.Stop harita girişini bloke eder, ESC kapatır.
// Panel boyutu 1152x648'e sığar (860x520 içerik).
public partial class ElectionPanel : Control
{
    public static bool IsOpen { get; private set; } = false;

    private PanelContainer _root;
    private VBoxContainer _content;
    private Label _titleLabel;
    private Label _nextElectionLabel;
    private Label _votePredictionLabel;
    private Label _breakdownLabel;
    private Label _factorsLabel;
    private OptionButton _countrySelector;
    private Button _closeButton;

    private string _selectedCountryId = "";
    private ElectionSystem.VotePrediction _currentPrediction;
    private List<CountryData> _countries; // seçiciyle eşzamanlı sıralı liste

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        var panel = new ElectionPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // paused iken de kapanış/input çalışsın
        UITheme.ApplyTheme(this);
        BuildUi();
        Open();
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

    private void Open()
    {
        Visible = true;
        _selectedCountryId = GameManager.Instance?.PlayerCountryId ?? "";
        Refresh();
    }

    private void Close()
    {
        IsOpen = false;
        Visible = false;
        QueueFree();
    }

    private void BuildUi()
    {
        SetAnchorsPreset(Control.LayoutPreset.FullRect);
        MouseFilter = Control.MouseFilterEnum.Stop;

        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        _root = new PanelContainer
        {
            CustomMinimumSize = new Vector2(860, 520),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _root.SetAnchorsPreset(Control.LayoutPreset.Center);
        UITheme.ApplyTheme(_root);
        AddChild(_root);

        _content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 12);
        _content.AddThemeConstantOverride("margin_top", 16);
        _content.AddThemeConstantOverride("margin_bottom", 16);
        _content.AddThemeConstantOverride("margin_left", 20);
        _content.AddThemeConstantOverride("margin_right", 20);
        _root.AddChild(_content);

        // Başlık
        _titleLabel = new Label { Text = "ELECTIONS — Seçim Takvimi ve Oy Tahmini" };
        _titleLabel.AddThemeFontSizeOverride("font_size", 22);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        _content.AddChild(_titleLabel);

        // Ülke seçici
        var selRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        selRow.AddThemeConstantOverride("separation", 8);
        var selLabel = new Label { Text = "Ülke:" };
        selLabel.AddThemeFontSizeOverride("font_size", 16);
        selRow.AddChild(selLabel);

        _countrySelector = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _countrySelector.ItemSelected += OnCountrySelected;
        selRow.AddChild(_countrySelector);
        _content.AddChild(selRow);

        // Ayırıcı
        _content.AddChild(new HSeparator());

        // Bir sonraki seçim
        _nextElectionLabel = new Label();
        _nextElectionLabel.AddThemeFontSizeOverride("font_size", 18);
        _nextElectionLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.85f, 1f));
        _content.AddChild(_nextElectionLabel);

        // Oy tahmini
        _votePredictionLabel = new Label();
        _votePredictionLabel.AddThemeFontSizeOverride("font_size", 20);
        _votePredictionLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.5f));
        _content.AddChild(_votePredictionLabel);

        // Detay (memnuniyet)
        _breakdownLabel = new Label();
        _breakdownLabel.AddThemeFontSizeOverride("font_size", 16);
        _breakdownLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        _content.AddChild(_breakdownLabel);

        // Faktörler (ekonomi/skandal)
        _factorsLabel = new Label();
        _factorsLabel.AddThemeFontSizeOverride("font_size", 16);
        _factorsLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        _content.AddChild(_factorsLabel);

        _content.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // Kapat butonu
        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _closeButton = new Button { Text = "KAPAT", FocusMode = Control.FocusModeEnum.None };
        _closeButton.Pressed += Close;
        btnRow.AddChild(_closeButton);
        UITheme.AddHoverScale(_closeButton);
        _content.AddChild(btnRow);
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

        _titleLabel.Text = $"ELECTIONS — {p.CountryName} ({p.CountryId})";

        // Seçim yoksa (monarşi / tek parti)
        if (p.DaysUntilElection < 0)
        {
            _nextElectionLabel.Text = "Seçim: yok (monarşi / tek parti rejimi — M22.4 parti Kongresi ile yönetilir)";
            _votePredictionLabel.Text = "Oy tahmini: —";
            _breakdownLabel.Text = "Halk memnuniyeti: —";
            _factorsLabel.Text = "Genel seçim yok — iktidar partisi partı kongresi üzerinden yönetilir.";
            return;
        }

        // Sayaç
        string dueText = p.ElectionDue ? " — SEÇİM GÜNÜ (M23.3 ile seçim gerçekleştirilebilir)" : "";
        _nextElectionLabel.Text = $"Bir sonraki seçim: {p.DaysUntilElection} gün sonra{dueText} (dönem: {p.TermLengthYears} yıl, {p.GovernmentType})";

        // Oy tahmini
        Color voteColor = p.PredictedVote >= 60 ? new Color(0.5f, 1f, 0.5f)
                      : p.PredictedVote >= 40 ? new Color(1f, 1f, 0.4f)
                      : new Color(1f, 0.5f, 0.5f);
        _votePredictionLabel.AddThemeColorOverride("font_color", voteColor);
        _votePredictionLabel.Text = $"Tahmini iktidar oy oranı: {p.PredictedVote:F1}% (taban memnuniyet: {p.BaseSatisfaction:F1})";

        // Memnuniyet detayı
        var groups = PublicSatisfactionSystem.Groups;
        var top = groups.OrderByDescending(g => PublicSatisfactionSystem.GetSatisfaction(p.CountryId, g.Id)).First();
        var bottom = groups.OrderBy(g => PublicSatisfactionSystem.GetSatisfaction(p.CountryId, g.Id)).First();
        _breakdownLabel.Text = $"9 toplum grubu ortalama memnuniyeti: {p.BaseSatisfaction:F1} | En yüksek: {top.Name} ({PublicSatisfactionSystem.GetSatisfaction(p.CountryId, top.Id):F0}) | En düşük: {bottom.Name} ({PublicSatisfactionSystem.GetSatisfaction(p.CountryId, bottom.Id):F0})";

        // Faktörler
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
            $"Faktörler: GDP büyümesi %{gdpGrowth:F1} | Bütçe açığı %{deficit:F1} | Devlet borcu %{debt:F0} | Enflasyon %{inflation:F1} | İşsizlik %{unemployment:F1} | Aktif skandal: {scandalCount}";
    }
}
