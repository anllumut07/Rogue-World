using Godot;

// Seçili ülkenin bilgi paneli (sol panel): bayrak/adı, GDP, nüfus, asker, kaynaklar, fabrikalar.
public partial class CountryInfoPanel : Control
{
    private Label _nameLabel;
    private Label _capitalLabel;
    private Label _ideologyLabel;
    private Label _stabilityLabel;
    private Label _warSupportLabel;
    private Label _ppLabel;
    private Label _populationLabel;
    private Label _manpowerLabel;
    private Label _factoriesLabel;
    private Label _resourcesLabel;
    private Label _divisionsLabel;

    private CountryData _currentData;
    private string _currentCountryId = "";

    public override void _Ready()
    {
        BuildUi();
        Hide();
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var scroll = new ScrollContainer();
        margin.AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(vbox);

        _nameLabel = NewLabel("Ülke Seçilmedi", 24, new Color(0.85f, 0.66f, 0.22f));
        vbox.AddChild(_nameLabel);

        _capitalLabel = NewLabel("", 16);
        vbox.AddChild(_capitalLabel);

        _ideologyLabel = NewLabel("", 16);
        vbox.AddChild(_ideologyLabel);

        vbox.AddChild(new HSeparator());

        var row = new GridContainer { Columns = 2 };
        row.AddThemeConstantOverride("h_separation", 20);
        row.AddThemeConstantOverride("v_separation", 6);
        vbox.AddChild(row);

        _stabilityLabel = NewLabel("Stability: —", 16);
        row.AddChild(_stabilityLabel);
        _warSupportLabel = NewLabel("War Support: —", 16);
        row.AddChild(_warSupportLabel);
        _ppLabel = NewLabel("Political Power: —", 16);
        row.AddChild(_ppLabel);

        vbox.AddChild(new HSeparator());

        _populationLabel = NewLabel("Population: —", 16);
        vbox.AddChild(_populationLabel);
        _manpowerLabel = NewLabel("Manpower: —", 16);
        vbox.AddChild(_manpowerLabel);

        vbox.AddChild(new HSeparator());

        _factoriesLabel = NewLabel("Factories: —", 16);
        vbox.AddChild(_factoriesLabel);

        vbox.AddChild(new HSeparator());

        _resourcesLabel = NewLabel("Resources: —", 16);
        vbox.AddChild(_resourcesLabel);

        vbox.AddChild(new HSeparator());

        _divisionsLabel = NewLabel("Divisions: —", 16);
        vbox.AddChild(_divisionsLabel);

        // "Play as" butonu (Milestone 3'te hook edilecek)
        var playBtn = new Button { Text = "PLAY AS THIS COUNTRY", FocusMode = Control.FocusModeEnum.None };
        playBtn.Pressed += () =>
        {
            if (!string.IsNullOrEmpty(_currentCountryId))
                GameManager.Instance.SetPlayerCountry(_currentCountryId);
        };
        vbox.AddChild(playBtn);
    }

    private Label NewLabel(string text, int size, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    public void ShowCountry(CountryData data, string name)
    {
        _currentData = data;
        _currentCountryId = data?.Id ?? "";
        _nameLabel.Text = $"{name} [{_currentCountryId}]";
        _capitalLabel.Text = $"Capital: {data.Capital}";
        _ideologyLabel.Text = $"Ideology: {data.Ideology}";

        _stabilityLabel.Text = $"Stability: {data.StabilityPercent:N0}%";
        _warSupportLabel.Text = $"War Support: {data.WarSupportPercent:N0}%";
        _ppLabel.Text = $"Political Power: {data.PoliticalPower:N1}";

        _populationLabel.Text = $"Population: {data.Population:N0}";
        _manpowerLabel.Text = $"Manpower Pool: {data.ManpowerPool:N0}";

        _factoriesLabel.Text = $"Factories: {data.CivFactories} Civ / {data.MilFactories} Mil / {data.Dockyards} Dock";

        _resourcesLabel.Text = $"Resources: Oil {data.Oil} | Rubber {data.Rubber} | Steel {data.Steel} | Al {data.Aluminum} | W {data.Tungsten} | Cr {data.Chromium}";

        _divisionsLabel.Text = $"Starting Divisions: {data.StartingDivisions}";

        Show();
    }

    public void HidePanel() => Hide();
}