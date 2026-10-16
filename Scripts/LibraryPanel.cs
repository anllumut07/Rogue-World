using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Kütüphane / Ansiklopedi paneli (M15.2 + genişletme).
/// - MainMenu ve oyun içi HUD'den açılır (OpenUnder).
/// - Sol: isim arama kutusu + kaydırılabilir ülke listesi (CountryDatabase, ~50 ülke).
/// - Sağ: seçili ülkenin sayfası — ad, başkent, bölge, resmî dil(ler), kısa tanıtım
///   ve CountryDatabase'den canlı sayısal veriler (nüfus, GSYİH, fabrikalar, kaynaklar, ordu).
/// - Metni olmayan ülke için otomatik genel sayfa ("Detaylı bilgi eklenecek").
/// - RichTextLabel KULLANILMAZ: ScrollContainer içinde min boyutu 0 olur (görünmez içerik bug'ı).
///   Label'lar içerikten minimum boyut alır → kaydırma doğru çalışır.
/// - 1152x648'e sığar (900x560), backdrop MouseFilter.Stop, ESC/CLOSE kapatır.
/// </summary>
public partial class LibraryPanel : CanvasLayer
{
    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private LineEdit _searchEdit;
    private VBoxContainer _countryList;
    private VBoxContainer _pageBox;      // sağ sayfa kökü (ScrollContainer içinde)
    private Label _annexLabel;           // ilhak uyarısı (gizli)
    private Label _pageTitle;
    private Label _pageInfo;             // başkent / bölge / dil satırları
    private Label _pageIntro;            // tanıtım metni
    private Label _pageStats;            // canlı sayısal veriler
    private Button _closeBtn;

    private string _selectedCountryId = "";
    private List<CountryData> _allCountries = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 100;

        BuildUi();

        IsOpen = true;

        // Açılış animasyonu: backdrop fade + panel pop-in
        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        RefreshCountryList("");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    // ESC: paneli kapat. PauseMenu Layer=100 önce alır ama IsOpen true iken ESC'yi bize bırakır.
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            ClosePanel();
        }
    }

    // Overlay olarak açar (SettingsPanel.OpenUnder ile aynı desen)
    public static LibraryPanel OpenUnder(Node parent)
    {
        if (IsOpen) return null;

        GD.Print($"[LibraryPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");

        var panel = new LibraryPanel();
        parent.AddChild(panel);
        return panel;
    }

    // Headless test kancası (--library-test): listedeki N. butona tıklama simülasyonu.
    // Normal oyunda asla çağrılmaz.
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
        // Backdrop: ekranı kaplar, fare girdilerini yakalar (harita girdisi bloklansın)
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.75f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_backdrop);

        UITheme.ApplyTheme(_backdrop);

        // CenterContainer -> Panel
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer();
        _panel.CustomMinimumSize = new Vector2(900, 560); // 1152x648'e sığar
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 30);
        margin.AddThemeConstantOverride("margin_right", 30);
        margin.AddThemeConstantOverride("margin_top", 25);
        margin.AddThemeConstantOverride("margin_bottom", 25);
        _panel.AddChild(margin);

        // Dikey: içerik satırı + CLOSE satırı
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        var hbox = new HBoxContainer();
        hbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        hbox.AddThemeConstantOverride("separation", 20);
        root.AddChild(hbox);

        // ===== SOL SÜTUN: arama + ülke listesi =====
        var leftBox = new VBoxContainer();
        leftBox.CustomMinimumSize = new Vector2(280, 0);
        leftBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        leftBox.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(leftBox);

        // Panel başlığı (sol üst)
        var titleLabel = new Label();
        titleLabel.Text = "LIBRARY";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeFontSizeOverride("font_size", 28);
        leftBox.AddChild(titleLabel);

        // İsim arama kutusu
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
        _countryList.AddThemeConstantOverride("separation", 6);
        leftScroll.AddChild(_countryList);

        // ===== SAĞ SÜTUN: kaydırılabilir ülke sayfası =====
        var rightScroll = new ScrollContainer();
        rightScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        rightScroll.MouseFilter = Control.MouseFilterEnum.Stop;
        hbox.AddChild(rightScroll);

        var rightMargin = new MarginContainer();
        rightMargin.AddThemeConstantOverride("margin_left", 10);
        rightMargin.AddThemeConstantOverride("margin_right", 10);
        rightMargin.AddThemeConstantOverride("margin_top", 10);
        rightMargin.AddThemeConstantOverride("margin_bottom", 10);
        rightScroll.AddChild(rightMargin);

        _pageBox = new VBoxContainer();
        _pageBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pageBox.AddThemeConstantOverride("separation", 10);
        rightMargin.AddChild(_pageBox);

        // İlhak uyarısı (yalnızca ilhak edilmiş ülkelerde görünür)
        _annexLabel = new Label();
        _annexLabel.AddThemeFontSizeOverride("font_size", 15);
        _annexLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.35f, 0.3f));
        _annexLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _annexLabel.Hide();
        _pageBox.AddChild(_annexLabel);

        // Ülke adı (sayfa başlığı)
        _pageTitle = new Label();
        _pageTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _pageTitle.AddThemeFontSizeOverride("font_size", 30);
        _pageTitle.AddThemeColorOverride("font_color", UITheme.GoldBright);
        _pageTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageBox.AddChild(_pageTitle);

        var separator = new HSeparator();
        _pageBox.AddChild(separator);

        // Başkent / bölge / dil satırları
        _pageInfo = new Label();
        _pageInfo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pageInfo.AddThemeFontSizeOverride("font_size", 17);
        _pageInfo.AddThemeColorOverride("font_color", UITheme.TextMain);
        _pageInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageBox.AddChild(_pageInfo);

        // Tanıtım metni
        _pageIntro = new Label();
        _pageIntro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pageIntro.AddThemeFontSizeOverride("font_size", 16);
        _pageIntro.AddThemeColorOverride("font_color", UITheme.TextMain);
        _pageIntro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageBox.AddChild(_pageIntro);

        var separator2 = new HSeparator();
        _pageBox.AddChild(separator2);

        // Canlı veri başlığı
        var statsHeader = new Label();
        statsHeader.Text = "COUNTRY DATA (LIVE)";
        statsHeader.AddThemeFontSizeOverride("font_size", 13);
        statsHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        _pageBox.AddChild(statsHeader);

        // Canlı sayısal veriler (seçim anında CountryDatabase'den okunur)
        _pageStats = new Label();
        _pageStats.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pageStats.AddThemeFontSizeOverride("font_size", 15);
        _pageStats.AddThemeColorOverride("font_color", UITheme.TextMain);
        _pageStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pageBox.AddChild(_pageStats);

        // ===== ALT SATIR: CLOSE =====
        var closeRow = new HBoxContainer();
        root.AddChild(closeRow);

        _closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None };
        _closeBtn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _closeBtn.Pressed += ClosePanel;
        closeRow.AddChild(_closeBtn);
        UITheme.AddHoverScale(_closeBtn);
    }

    // Arama kutusu değişti: listeyi filtreleyerek yeniden kur
    private void OnSearchChanged(string filter)
    {
        RefreshCountryList(filter);
    }

    // Ülke listesini kur (arama filtresiyle). Kaynak: CountryDatabase ayrıntılı ülkeleri.
    private void RefreshCountryList(string filter)
    {
        foreach (var child in _countryList.GetChildren())
            child.QueueFree();

        // Ayrıntılı verili ülkeler, ada göre sıralı (fallback ülkeler listede yok)
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
            btn.Alignment = HorizontalAlignment.Left; // Buton metni sola (Button API)
            btn.AddThemeFontSizeOverride("font_size", 15);
            string id = c.Id;
            btn.Pressed += () => SelectCountry(id);
            // Seçili ülkeyi vurgula
            if (id == _selectedCountryId)
                btn.SelfModulate = UITheme.Gold;
            _countryList.AddChild(btn);
        }

        // Sayfa sayısı logu: metinli (JSON'dan) + otomatik (DB verisiyle)
        int withText = _allCountries.Count(c => LibrarySystem.GetEntry(c.Id) != null);
        int autoPages = _allCountries.Count - withText;
        GD.Print($"[LibraryPanel] {_allCountries.Count} ülke sayfası hazır ({withText} metinli, {autoPages} otomatik) — filtre: '{filter}' → {visible.Count} görünür");

        // İlk açılışta ilk ülkeyi otomatik seç (seçim korunursa dokunma)
        if (string.IsNullOrEmpty(_selectedCountryId) && _allCountries.Count > 0)
            SelectCountry(_allCountries[0].Id);
    }

    // Seçili ülkenin sayfasını kur: Library metni + CountryDatabase canlı verisi
    private void SelectCountry(string countryId)
    {
        _selectedCountryId = countryId;

        // Buton vurgulaması
        foreach (var child in _countryList.GetChildren())
        {
            if (child is Button btn)
                btn.SelfModulate = btn.Text.Contains($"[{countryId}]") ? UITheme.Gold : Colors.White;
        }

        var data = CountryDatabase.Get(countryId);
        if (data == null)
        {
            GD.PrintErr($"[LibraryPanel] Ülke bulunamadı: {countryId}");
            return;
        }

        var entry = LibrarySystem.GetEntry(countryId);

        // === Sayfa alanları: JSON metni varsa ondan, yoksa DB/otomatik değerler ===
        string name = !string.IsNullOrEmpty(entry?.Name) ? entry.Name : data.Name;
        string capital = !string.IsNullOrEmpty(entry?.Capital) ? entry.Capital
                         : !string.IsNullOrEmpty(data.Capital) ? data.Capital : "—";
        string region = !string.IsNullOrEmpty(entry?.Region) ? entry.Region : "—";
        string languages = !string.IsNullOrEmpty(entry?.Languages) ? entry.Languages : "—";
        string intro = !string.IsNullOrEmpty(entry?.Intro) ? entry.Intro
                       : "Detaylı bilgi eklenecek."; // otomatik genel sayfa

        // Bayrak kutusu (sayfa başlığının üstüne)
        var flagBox = new FlagBox { CustomMinimumSize = new Vector2(80, 53) }; // ~3:2 aspect
        _pageBox.AddChild(flagBox);
        flagBox.SetCountry(countryId);
        _pageInfo.Text = $"Capital: {capital}\nRegion: {region}\nLanguages: {languages}";
        _pageIntro.Text = intro;

        // İlhak uyarısı
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

        // === Canlı veriler: seçim anında CountryDatabase'den ===
        _pageStats.Text = BuildStats(data);

        GD.Print($"[LibraryPanel] Sayfa gösteriliyor: {name} [{data.Id}] — intro: {(entry != null ? "metinli" : "otomatik")}");
    }

    // Canlı sayısal veri metni (nüfus, GSYİH, fabrikalar, kaynaklar, ordu)
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

    private void ClosePanel()
    {
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () =>
        {
            _backdrop.Hide();
            _panel.Hide();
            IsOpen = false;
            QueueFree();
        });
    }
}