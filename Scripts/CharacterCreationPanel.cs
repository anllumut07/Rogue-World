using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// M24.1: Karakter oluşturma paneli — overlay (Control), backdrop MouseFilter.Stop harita girişini bloke eder,
// ESC kapatır. Panel boyutu 1152x648'e sığar (860x520 içerik). UI metinleri İngilizce, yorumlar Türkçe.
// Akış: ülke seçimi → bu panel → karakter onayı → oyun devam eder (M24.3).
public partial class CharacterCreationPanel : Control
{
    public static bool IsOpen { get; private set; } = false;

    public Action<Character> OnConfirmed;

    private PanelContainer _root;
    private VBoxContainer _content;
    private Label _titleLabel;
    private Label _statusLabel;

    // Form alanları
    private LineEdit _nameInput;
    private SpinBox _ageInput;
    private OptionButton _genderInput;
    private OptionButton _countryInput;
    private OptionButton _cultureInput;
    private OptionButton _backgroundInput;
    private Label _backgroundDescLabel;

    // Nitelik etiketleri
    private Label _statDipLabel, _statAdmLabel, _statIntLabel, _statChaLabel,
                  _statMilLabel, _statEcoLabel, _statLawLabel;

    // Portre önizleme
    private PortraitControl _portrait;

    private Character _character;
    private List<CountryData> _countries;

    public static void OpenUnder(Node parent, Action<Character> onConfirmed = null)
    {
        if (IsOpen) return;
        var panel = new CharacterCreationPanel { OnConfirmed = onConfirmed };
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // paused iken de kapanış/input çalışsın
        UITheme.ApplyTheme(this);
        BuildUi();
        IsOpen = true;
        UITheme.PopIn(this, 0.15f);

        GD.Print("[CharacterCreationPanel] hazır — karakter oluşturma bekleniyor");
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
        _content.AddThemeConstantOverride("margin_left", 24);
        _content.AddThemeConstantOverride("margin_right", 24);
        _root.AddChild(_content);

        // Başlık
        _titleLabel = new Label { Text = "CHARACTER CREATION", HorizontalAlignment = HorizontalAlignment.Center };
        _titleLabel.AddThemeFontSizeOverride("font_size", 24);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        _content.AddChild(_titleLabel);

        _statusLabel = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
        _statusLabel.AddThemeFontSizeOverride("font_size", 13);
        _statusLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _content.AddChild(_statusLabel);

        // İsim
        _content.AddChild(FormRow("Name:", out _nameInput, placeholder: "Enter your character name"));
        _nameInput.TextChanged += _ => OnAnyChanged();

        // Yaş
        var ageRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        ageRow.AddThemeConstantOverride("separation", 8);
        var ageLabel = new Label { Text = "Age:" };
        ageLabel.AddThemeFontSizeOverride("font_size", 16);
        ageRow.AddChild(ageLabel);
        _ageInput = new SpinBox { MinValue = 18, MaxValue = 80, Value = 35, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _ageInput.ValueChanged += _ => OnAnyChanged();
        ageRow.AddChild(_ageInput);
        _content.AddChild(ageRow);

        // Cinsiyet
        _content.AddChild(FormRow("Gender:", out _genderInput));
        foreach (var g in Character.Genders) _genderInput.AddItem(g);
        _genderInput.ItemSelected += _ => OnAnyChanged();

        // Doğum ülkesi
        _content.AddChild(FormRow("Birth Country:", out _countryInput));
        _countryInput.ItemSelected += OnCountrySelected;

        // Kültür (otomatik, salt okunur)
        _content.AddChild(FormRow("Culture:", out _cultureInput));
        _cultureInput.Disabled = true;

        // Köken
        _content.AddChild(FormRow("Background:", out _backgroundInput));
        foreach (var bg in Character.Backgrounds) _backgroundInput.AddItem(Character.BackgroundLabel(bg));
        _backgroundInput.ItemSelected += OnBackgroundSelected;

        _backgroundDescLabel = new Label { Text = "" };
        _backgroundDescLabel.AddThemeFontSizeOverride("font_size", 13);
        _backgroundDescLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _backgroundDescLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(_backgroundDescLabel);

        // Nitelikler
        var statsLabel = new Label { Text = "STARTING ATTRIBUTES", HorizontalAlignment = HorizontalAlignment.Center };
        statsLabel.AddThemeFontSizeOverride("font_size", 18);
        statsLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _content.AddChild(statsLabel);

        var statsGrid = new GridContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        statsGrid.Columns = 2;
        statsGrid.AddThemeConstantOverride("hseparation", 20);
        statsGrid.AddThemeConstantOverride("vseparation", 6);
        AddStatRow(statsGrid, "Diplomacy", out _statDipLabel);
        AddStatRow(statsGrid, "Administration", out _statAdmLabel);
        AddStatRow(statsGrid, "Intelligence", out _statIntLabel);
        AddStatRow(statsGrid, "Charisma", out _statChaLabel);
        AddStatRow(statsGrid, "Military", out _statMilLabel);
        AddStatRow(statsGrid, "Economy", out _statEcoLabel);
        AddStatRow(statsGrid, "Law", out _statLawLabel);
        _content.AddChild(statsGrid);

        // Portre önizleme
        var portraitContainer = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        portraitContainer.AddThemeConstantOverride("separation", 8);
        var portraitLabel = new Label { Text = "PORTRAIT PREVIEW", HorizontalAlignment = HorizontalAlignment.Center };
        portraitLabel.AddThemeFontSizeOverride("font_size", 16);
        portraitLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        portraitContainer.AddChild(portraitLabel);

        _portrait = new PortraitControl { CustomMinimumSize = new Vector2(200, 260), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        portraitContainer.AddChild(_portrait);
        _content.AddChild(portraitContainer);

        _content.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // Butonlar
        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        btnRow.AddThemeConstantOverride("separation", 10);

        var cancelBtn = new Button { Text = "CANCEL", FocusMode = Control.FocusModeEnum.None };
        cancelBtn.Pressed += Close;
        UITheme.AddHoverScale(cancelBtn);
        btnRow.AddChild(cancelBtn);

        var startBtn = new Button { Text = "START GAME", FocusMode = Control.FocusModeEnum.None };
        startBtn.Pressed += OnStartPressed;
        UITheme.AddHoverScale(startBtn);
        btnRow.AddChild(startBtn);

        _content.AddChild(btnRow);

        // İlk değerler
        InitializeCharacter();
    }

    private HBoxContainer FormRow(string labelText, out OptionButton option, string placeholder = null)
    {
        option = null;
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        var lbl = new Label { Text = labelText };
        lbl.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(lbl);
        return row;
    }

    private HBoxContainer FormRow(string labelText, out LineEdit lineEdit, string placeholder = null)
    {
        lineEdit = null;
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        var lbl = new Label { Text = labelText };
        lbl.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(lbl);
        lineEdit = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = placeholder ?? "" };
        row.AddChild(lineEdit);
        return row;
    }

    private HBoxContainer FormRow(string labelText, out OptionButton option)
    {
        option = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        var lbl = new Label { Text = labelText };
        lbl.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(lbl);
        row.AddChild(option);
        return row;
    }

    private void AddStatRow(GridContainer grid, string name, out Label valueLabel)
    {
        var nLabel = new Label { Text = name };
        nLabel.AddThemeFontSizeOverride("font_size", 14);
        nLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        valueLabel = new Label { Text = "0" };
        valueLabel.AddThemeFontSizeOverride("font_size", 16);
        valueLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        grid.AddChild(nLabel);
        grid.AddChild(valueLabel);
    }

    private void InitializeCharacter()
    {
        // Ülke listesini doldur
        _countries = CountryDatabase.All().Where(c => c != null).OrderBy(c => c.Name).ToList();
        _countryInput.Clear();
        for (int i = 0; i < _countries.Count; i++)
            _countryInput.AddItem(_countries[i].Name, i);

        // İlk ülkeyi seç
        if (_countries.Count > 0)
        {
            _countryInput.Selected = 0;
            OnCountrySelected(0);
        }

        // Rastgele başlangıç karakteri (deterministik — oyuncu ülkesinden türetilen tohum)
        ulong seed = HashSeed($"{ScenarioSystem.ActiveId}:{_countries[0].Id}:chargen");
        _character = Character.GenerateRandom(_countries[0].Id, _countries[0].Id switch
        {
            _ => LeaderSystemCultureKey(_countries[0].Id)
        }, seed);

        ApplyCharacterToUi();
    }

    private string LeaderSystemCultureKey(string countryId)
    {
        // LeaderSystem._cultureMap'a eşdeğer eşleme (aynı kültür anahtarları)
        return countryId switch
        {
            "TUR" or "AZE" or "KAZ" or "KGZ" or "UZB" or "TKM" => "turkish",
            "DEU" or "AUT" or "CHE" or "BEL" or "NLD" or "LUX" => "germanic",
            "USA" or "GBR" or "CAN" or "AUS" or "NZL" or "IRL" => "anglo",
            "FRA" or "ITA" or "ESP" or "PRT" or "ROU" or "MDA" or "ARG" or "BRA" or "CHL"
                or "COL" or "PER" or "VEN" or "ECU" or "BOL" or "PRY" or "URY" or "MEX"
                or "CUB" or "DOM" or "GTM" or "CRI" or "PAN" => "latin",
            "RUS" or "UKR" or "BLR" or "POL" or "CZE" or "SVK" or "SRB" or "HRV" or "SVN"
                or "BGR" or "MKD" or "MNG" => "slavic",
            "SWE" or "NOR" or "DNK" or "FIN" or "ISL" => "nordic",
            "GRC" or "CYP" => "greek",
            "HUN" => "hungarian",
            "SAU" or "EGY" or "IRQ" or "JOR" or "LBN" or "SYR" or "YEM" or "OMN" or "KWT"
                or "QAT" or "BHR" or "ARE" or "DZA" or "MAR" or "TUN" or "LBY" or "SDN" or "SOM" => "arabic",
            "IRN" or "AFG" or "TJK" => "iranian",
            "IND" or "PAK" or "BGD" or "LKA" or "NPL" or "BTN" => "south_asian",
            "CHN" or "JPN" or "KOR" or "PRK" or "TWN" => "east_asian",
            "IDN" or "MYS" or "THA" or "VNM" or "PHL" or "SGP" or "MMR" or "KHM" or "LAO" => "southeast_asian",
            "ZAF" or "NGA" or "KEN" or "ETH" or "TZA" or "UGA" or "GHA" or "CIV" or "SEN"
                or "CMR" or "COD" or "RWA" or "MLI" or "BFA" or "NER" or "ZMB" or "ZWE"
                or "BWA" or "NAM" or "MDG" => "african",
            _ => "anglo"
        };
    }

    private static ulong HashSeed(string text)
    {
        unchecked
        {
            ulong h = 0x9E3779B97F4A7C15UL;
            foreach (char c in text) h = (h ^ c) * 0x2545F4914F6CDD1DUL;
            return h;
        }
    }

    private void OnCountrySelected(long idx)
    {
        if (idx < 0 || idx >= _countries.Count) return;
        var country = _countries[(int)idx];
        _character.BirthCountryId = country.Id;
        _character.CultureKey = LeaderSystemCultureKey(country.Id);

        // Ülkeye göre deterministik yeniden üret (isim, yaş, cinsiyet, portre)
        ulong seed = HashSeed($"{ScenarioSystem.ActiveId}:{country.Id}:chargen");
        var generated = Character.GenerateRandom(country.Id, _character.CultureKey, seed);

        // Kullanıcının girdiği isim/yaşı koru, diğerlerini generated'tan al
        _character.FirstName = generated.FirstName;
        _character.LastName = generated.LastName;
        _character.Age = generated.Age;
        _character.Gender = generated.Gender;
        _character.SkinTone = generated.SkinTone;
        _character.HairColor = generated.HairColor;
        _character.ClothingStyle = generated.ClothingStyle;

        // Nitelikleri generated'tan al (köken bonusu daha sonra uygulanır)
        _character.Diplomacy = generated.Diplomacy;
        _character.Administration = generated.Administration;
        _character.Intelligence = generated.Intelligence;
        _character.Charisma = generated.Charisma;
        _character.Military = generated.Military;
        _character.Economy = generated.Economy;
        _character.Law = generated.Law;
        _character.Traits = new List<string>(generated.Traits);

        _cultureInput.Clear();
        _cultureInput.AddItem(_character.CultureKey);

        // Kökeni sıfırla (ilk = military)
        _backgroundInput.Selected = 0;
        OnBackgroundSelected(0);

        ApplyCharacterToUi();
    }

    private void OnBackgroundSelected(long idx)
    {
        if (idx < 0 || idx >= Character.Backgrounds.Length) return;
        _character.Background = Character.Backgrounds[(int)idx];
        _backgroundDescLabel.Text = Character.BackgroundDescription(_character.Background);
        RecalculateStats();
        ApplyCharacterToUi();
    }

    private void OnAnyChanged()
    {
        if (_nameInput != null) _character.FirstName = _nameInput.Text;
        if (_ageInput != null) _character.Age = (int)_ageInput.Value;
        if (_genderInput != null && _genderInput.Selected >= 0)
            _character.Gender = Character.Genders[_genderInput.Selected];
        RecalculateStats();
        ApplyCharacterToUi();
    }

    private void RecalculateStats()
    {
        // Baz nitelikleri yeniden üret (deterministik, ülke+köken bazlı)
        ulong seed = HashSeed($"{ScenarioSystem.ActiveId}:{_character.BirthCountryId}:stats");
        var rng = new Character.LocalRng(seed);

        int dip = 30 + rng.NextInt(0, 41);
        int adm = 30 + rng.NextInt(0, 41);
        int intel = 30 + rng.NextInt(0, 41);
        int cha = 30 + rng.NextInt(0, 41);
        int mil = 30 + rng.NextInt(0, 41);
        int eco = 30 + rng.NextInt(0, 41);
        int law = 30 + rng.NextInt(0, 41);

        Character.ApplyBackgroundBonus(_character.Background, ref dip, ref adm, ref intel, ref cha, ref mil, ref eco, ref law);

        _character.Diplomacy = Math.Clamp(dip, 1, 100);
        _character.Administration = Math.Clamp(adm, 1, 100);
        _character.Intelligence = Math.Clamp(intel, 1, 100);
        _character.Charisma = Math.Clamp(cha, 1, 100);
        _character.Military = Math.Clamp(mil, 1, 100);
        _character.Economy = Math.Clamp(eco, 1, 100);
        _character.Law = Math.Clamp(law, 1, 100);

        ApplyCharacterToUi();
    }

    private void ApplyCharacterToUi()
    {
        if (_nameInput != null) _nameInput.Text = _character.FirstName;
        if (_ageInput != null) _ageInput.Value = _character.Age;
        if (_genderInput != null)
            _genderInput.Selected = Array.IndexOf(Character.Genders, _character.Gender);

        // Köken seçiciyi eşleştir
        for (int i = 0; i < Character.Backgrounds.Length; i++)
            if (Character.Backgrounds[i] == _character.Background) { _backgroundInput.Selected = i; break; }

        _backgroundDescLabel.Text = Character.BackgroundDescription(_character.Background);

        _statDipLabel.Text = _character.Diplomacy.ToString();
        _statAdmLabel.Text = _character.Administration.ToString();
        _statIntLabel.Text = _character.Intelligence.ToString();
        _statChaLabel.Text = _character.Charisma.ToString();
        _statMilLabel.Text = _character.Military.ToString();
        _statEcoLabel.Text = _character.Economy.ToString();
        _statLawLabel.Text = _character.Law.ToString();

        if (_portrait != null) _portrait.Character = _character;

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        bool valid = !string.IsNullOrWhiteSpace(_character.FirstName) && _character.FirstName.Length >= 2;
        _statusLabel.Text = valid ? "Karakter hazır — START GAME ile başlayın." : "İsim en az 2 karakter olmalı.";
        _statusLabel.AddThemeColorOverride("font_color", valid ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f));
    }

    private void OnStartPressed()
    {
        if (!string.IsNullOrWhiteSpace(_character.FirstName) && _character.FirstName.Length >= 2)
        {
            GD.Print($"[CharacterCreationPanel] Karakter onaylandı: {_character.FullName} ({_character.Background}, yaş {_character.Age})");
            OnConfirmed?.Invoke(_character);
            Close();
        }
    }

    private void Close()
    {
        IsOpen = false;
        Visible = false;
        QueueFree();
    }
}
