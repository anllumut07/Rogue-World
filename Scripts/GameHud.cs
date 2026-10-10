using Godot;

// Üst HUD bar: tarih, hız butonları (1x-5x), pause/play, oyuncu ülke etiketi, PP.
public partial class GameHud : CanvasLayer
{
    private Label _dateLabel;
    private Label _countryLabel;
    private Label _ppLabel;
    private Label _stateLabel;
    private Button _pauseButton;
    private Button[] _speedButtons;

    private GameManager GM => GameManager.Instance;

    public override void _Ready()
    {
        BuildUi();

        GM.DateChanged += OnDateChanged;
        GM.SpeedChanged += OnSpeedChanged;
        GM.PlayerCountryChanged += OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged += OnPlayerCountryDataChanged;

        OnDateChanged(GM.GetDateText());
        OnSpeedChanged(GM.Speed);
        RefreshCountryLabel();
        RefreshPPLabel();
    }

    public override void _ExitTree()
    {
        if (!GameManager.IsAvailable) return;
        GM.DateChanged -= OnDateChanged;
        GM.SpeedChanged -= OnSpeedChanged;
        GM.PlayerCountryChanged -= OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged -= OnPlayerCountryDataChanged;
    }

    private void BuildUi()
    {
        Layer = 10;

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        margin.AddChild(row);

        var panel = new PanelContainer();
        row.AddChild(panel);

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 12);
        panel.AddChild(bar);

        _countryLabel = new Label();
        _countryLabel.AddThemeFontSizeOverride("font_size", 22);
        _countryLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.66f, 0.22f));
        bar.AddChild(_countryLabel);

        _ppLabel = new Label();
        _ppLabel.AddThemeFontSizeOverride("font_size", 20);
        _ppLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
        bar.AddChild(_ppLabel);

        _dateLabel = new Label();
        _dateLabel.AddThemeFontSizeOverride("font_size", 22);
        bar.AddChild(_dateLabel);

        _stateLabel = new Label();
        _stateLabel.AddThemeFontSizeOverride("font_size", 22);
        _stateLabel.AddThemeColorOverride("font_color", Colors.OrangeRed);
        bar.AddChild(_stateLabel);

        bar.AddChild(new VSeparator());

        _pauseButton = new Button { FocusMode = Control.FocusModeEnum.None };
        _pauseButton.Pressed += () => GM.TogglePause();
        bar.AddChild(_pauseButton);

        // Hız butonları: 1x - 5x (HOI4 gibi)
        _speedButtons = new Button[5];
        for (int i = 0; i < 5; i++)
        {
            int speed = i + 1;
            var b = new Button
            {
                Text = $"{speed}x",
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None
            };
            b.Pressed += () =>
            {
                GM.SetSpeed(speed);
                SyncSpeedButtons();
            };
            bar.AddChild(b);
            _speedButtons[i] = b;
        }

        AddChild(margin);
    }

    private void OnDateChanged(string text) => _dateLabel.Text = $"  |  {text}";

    private void OnPlayerCountryChanged(string id) => RefreshCountryLabel();

    private void OnPlayerCountryDataChanged() => RefreshPPLabel();

    private void RefreshCountryLabel()
    {
        if (GM.HasPlayer && GM.PlayerCountryData != null)
            _countryLabel.Text = GM.PlayerCountryData.Name;
        else
            _countryLabel.Text = "Rogue World";
    }

    private void RefreshPPLabel()
    {
        if (GM.HasPlayer && GM.PlayerCountryData != null)
            _ppLabel.Text = $"PP: {GM.PlayerCountryData.PoliticalPower:N1}";
        else
            _ppLabel.Text = "";
    }

    private void OnSpeedChanged(int speed)
    {
        bool paused = speed == 0;
        _pauseButton.Text = paused ? "PLAY" : "PAUSE";
        _stateLabel.Text = paused ? "PAUSED" : "";
        SyncSpeedButtons();
    }

    private void SyncSpeedButtons()
    {
        for (int i = 0; i < _speedButtons.Length; i++)
            _speedButtons[i].SetPressedNoSignal(i + 1 == GM.Speed);
    }
}
