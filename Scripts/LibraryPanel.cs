using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Library / Encyclopedia Panel (M15.2 + Stage 2 UI Upgrade).
/// - MainMenu and in-game HUD accessible (OpenUnder).
/// - Left: search box + scrollable country list (CountryDatabase, ~50 countries).
/// - Right: selected country page — name, capital, region, official language(s), short intro
///   and live numeric data from CountryDatabase (population, GDP, factories, resources, army).
/// - Countries without text get automatic generic page ("Detailed info will be added").
/// - Uses GameTheme + reusable UI components (GamePanel, Chip, EmptyState).
/// - 1152x648 fit (900x560), backdrop blocks map input, ESC/CLOSE closes.
/// Test properties preserved for --library-test compatibility.
/// </summary>
public partial class LibraryPanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    private LineEdit _searchEdit;
    private VBoxContainer _countryList;
    private VBoxContainer _pageBox;
    private Label _annexLabel;
    private Label _pageTitle;
    private Label _pageInfo;
    private Label _pageIntro;
    private Label _pageStats;

    private string _selectedCountryId = "";
    private List<CountryData> _allCountries = new();

    // ===== Test / UI inspection accessors =====

    /// <summary>Panel tasarım boyutu — 1152x648 ekrana sığma kontrolü için.</summary>
    public Vector2 DesignedSize => new Vector2(900, 560);

    /// <summary>Listelenen ülke sayısı (filtre uygulanmış).</summary>
    public int CountryListCount => _countryList.GetChildren().OfType<Button>().Count();

    /// <summary>Filtresiz toplam ülke sayısı (CountryDatabase detaylı kayıtları).</summary>
    public int TotalCountries => _allCountries.Count;

    /// <summary>Şu an seçili ülke ID'si.</summary>
    public string SelectedCountryId => _selectedCountryId;

    /// <summary>Sayfa başlığı metni.</summary>
    public string PageTitleText => _pageTitle?.Text ?? "";

    /// <summary>Sayfa bilgi metni (başkent/bölge/dil).</summary>
    public string PageInfoText => _pageInfo?.Text ?? "";

    /// <summary>Tanıtım metni.</summary>
    public string PageIntroText => _pageIntro?.Text ?? "";

    /// <summary>Canlı istatistik metni.</summary>
    public string PageStatsText => _pageStats?.Text ?? "";

    /// <summary>ESC ile kapatma istendi mi?</summary>
    public new bool CloseRequested { get; private set; }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "LIBRARY";
        MinSize = new Vector2(900, 560);
        MaxSize = new Vector2(1200, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();

        IsOpen = true;

        RefreshCountryList("");

        GD.Print("LibraryPanel: hazır (Stage 2 UI)");
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
            CloseRequested = true;
            Close();
        }
    }

    public static LibraryPanel OpenUnder(Node parent)
    {
        if (IsOpen) return null;

        GD.Print($"[LibraryPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");

        var panel = new LibraryPanel();
        parent.AddChild(panel);
        return panel;
    }

    public void SimulateClickForTest(int buttonIndex)
    {
        var buttons = _countryList.GetChildren().OfType<Button>().ToList();
        if (buttonIndex >= 0 && buttonIndex < buttons.Count)
        {
            GD.Print($"[Test] Buton #{buttonIndex} tıklama simülasyonu: {buttons[buttonIndex].Text}");
            buttons[buttonIndex].EmitSignal(Button.SignalName.Pressed);
        }
        else
        {
            GD.Print($"[Test] Buton #{buttonIndex} yok (liste: {buttons.Count})");
        }
    }

    private void BuildUi()
    {
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // Header
        var header = new HBoxContainer();
        content.AddChild(header);

        var title = new Label { Text = "LIBRARY" };
        title.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeXLarge);
        title.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        header.AddChild(title);

        content.AddChild(new HSeparator());

        // Two columns
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", GameTheme.SpaceL);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        content.AddChild(columns);

        BuildLeftColumn(columns);
        BuildRightColumn(columns);
    }

    private void BuildLeftColumn(HBoxContainer parent)
    {
        var leftBox = new VBoxContainer();
        leftBox.CustomMinimumSize = new Vector2(280, 0);
        leftBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        leftBox.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        parent.AddChild(leftBox);

        // Search box
        _searchEdit = new LineEdit();
        _searchEdit.PlaceholderText = "Search country...";
        _searchEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _searchEdit.TextChanged += OnSearchChanged;
        leftBox.AddChild(_searchEdit);

        var leftScroll = new ScrollContainer();
        leftScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        leftScroll.MouseFilter = Control.MouseFilterEnum.Stop;
        leftBox.AddChild(leftScroll);

        _countryList = new VBoxContainer();
        _countryList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _countryList.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        leftScroll.AddChild(_countryList);
    }

    private void BuildRightColumn(HBoxContainer parent)
    {
        var rightScroll = new ScrollContainer();
        rightScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        rightScroll.MouseFilter = Control.MouseFilterEnum.Stop;
        parent.AddChild(rightScroll);

        var rightMargin = new MarginContainer();
        rightMargin.AddThemeConstantOverride("margin_left", 10);
        rightMargin.AddThemeConstantOverride("margin_right", 10);
        rightMargin.AddThemeConstantOverride("margin_top", 10);
        rightMargin.AddThemeConstantOverride("margin_bottom", 10);
        rightScroll.AddChild(rightMargin);

        _pageBox = new VBoxContainer();
        _pageBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pageBox.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        rightMargin.AddChild(_pageBox);

        // Annex warning (only visible for annexed countries)
        _annexLabel = new Label();
        _annexLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        _annexLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.35f, 0.3f));
        _annexLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _annexLabel.Hide();
        _pageBox.AddChild(_annexLabel);

        // Country name (page title)
        _pageTitle = new Label();
        _pageTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _pageTitle.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeXLarge);
        _pageTitle.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        _pageTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageBox.AddChild(_pageTitle);

        var separator = new HSeparator();
        _pageBox.AddChild(separator);

        // Capital / region / languages
        _pageInfo = new Label();
        _pageInfo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pageInfo.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _pageInfo.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        _pageInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageBox.AddChild(_pageInfo);

        // Intro text
        _pageIntro = new Label();
        _pageIntro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pageIntro.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _pageIntro.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        _pageIntro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageBox.AddChild(_pageIntro);

        var separator2 = new HSeparator();
        _pageBox.AddChild(separator2);

        // Live data header
        var statsHeader = new Label();
        statsHeader.Text = "COUNTRY DATA (LIVE)";
        statsHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        statsHeader.AddThemeColorOverride("font_color", GameTheme.Gold);
        _pageBox.AddChild(statsHeader);

        // Live numeric data
        _pageStats = new Label();
        _pageStats.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pageStats.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _pageStats.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        _pageStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageBox.AddChild(_pageStats);
    }

    private void OnSearchChanged(string filter)
    {
        RefreshCountryList(filter);
    }

    private void RefreshCountryList(string filter)
    {
        foreach (var child in _countryList.GetChildren())
            child.QueueFree();

        _allCountries = CountryDatabase.All()
            .Where(c => c.IsDetailed)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        filter = (filter ?? "").Trim();

        var visible = _allCountries
            .Where(c => string.IsNullOrEmpty(filter)
                        || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || c.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var c in visible)
        {
            var btn = new Button { FocusMode = Control.FocusModeEnum.None };
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.Text = $"{c.Name} [{c.Id}]";
            btn.Alignment = HorizontalAlignment.Left;
            btn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            string id = c.Id;
            btn.Pressed += () => SelectCountry(id);

            // Highlight selected country
            if (id == _selectedCountryId)
                btn.SelfModulate = GameTheme.Gold;

            GameTheme.AddHoverScale(btn);
            _countryList.AddChild(btn);
        }

        int withText = _allCountries.Count(c => LibrarySystem.GetEntry(c.Id) != null);
        int autoPages = _allCountries.Count - withText;
        GD.Print($"[LibraryPanel] {_allCountries.Count} ülke sayfası hazır ({withText} metinli, {autoPages} otomatik) — filtre: '{filter}' → {visible.Count} görünür");

        // Auto-select first country on initial open
        if (string.IsNullOrEmpty(_selectedCountryId) && _allCountries.Count > 0)
            SelectCountry(_allCountries[0].Id);
    }

    private void SelectCountry(string countryId)
    {
        _selectedCountryId = countryId;

        // Highlight selected button
        foreach (var child in _countryList.GetChildren())
        {
            if (child is Button btn)
                btn.SelfModulate = btn.Text.Contains($"[{countryId}]") ? GameTheme.Gold : Colors.White;
        }

        var data = CountryDatabase.Get(countryId);
        if (data == null)
        {
            GD.PrintErr($"[LibraryPanel] Ülke bulunamadı: {countryId}");
            return;
        }

        var entry = LibrarySystem.GetEntry(countryId);

        string name = !string.IsNullOrEmpty(entry?.Name) ? entry.Name : data.Name;
        string capital = !string.IsNullOrEmpty(entry?.Capital) ? entry.Capital
                         : !string.IsNullOrEmpty(data.Capital) ? data.Capital : "—";
        string region = !string.IsNullOrEmpty(entry?.Region) ? entry.Region : "—";
        string languages = !string.IsNullOrEmpty(entry?.Languages) ? entry.Languages : "—";
        string intro = !string.IsNullOrEmpty(entry?.Intro) ? entry.Intro
                       : "Detailed info will be added.";

        // Flag box at top of page
        var flagBox = new FlagBox { CustomMinimumSize = new Vector2(80, 53) };
        _pageBox.AddChild(flagBox);
        flagBox.SetCountry(countryId);

        _pageTitle.Text = name;
        _pageInfo.Text = $"Capital: {capital}\nRegion: {region}\nLanguages: {languages}";
        _pageIntro.Text = intro;

        // Annex warning
        if (data.IsAnnexed)
        {
            var owner = CountryDatabase.Get(data.OwnerId);
            _annexLabel.Text = $"ANNEXED by {owner?.Name ?? data.OwnerId}";
            _annexLabel.Show();
        }
        else
        {
            _annexLabel.Hide();
        }

        _pageStats.Text = BuildStats(data);

        GD.Print($"[LibraryPanel] Sayfa gösteriliyor: {name} [{data.Id}] — intro: {(entry != null ? "metinli" : "otomatik")}");
    }

    private string BuildStats(CountryData d)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Population: {d.Population:N0}\n");
        sb.Append($"Manpower Pool: {d.ManpowerPool:N0}\n");
        sb.Append($"Estimated GDP: {d.EstimatedGDP:N0}\n");
        sb.Append($"Factories — Civilian: {d.CivFactories}, Military: {d.MilFactories}, Dockyards: {d.Dockyards}\n");
        sb.Append($"Resources/day — Oil: {d.Oil}, Rubber: {d.Rubber}, Steel: {d.Steel}, Aluminum: {d.Aluminum}, Tungsten: {d.Tungsten}, Chromium: {d.Chromium}\n");
        sb.Append($"Army: {d.StartingDivisions} divisions • Research slots: {d.ResearchSlots}");
        return sb.ToString();
    }

    public override void Close()
    {
        GD.Print("LibraryPanel: kapatılıyor");
        base.Close();
    }
}