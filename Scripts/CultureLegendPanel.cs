using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// M46.2 — Kültür efsanesi paneli (culture atlas).
///
/// M46.1 kültür harita modunu boyadı ama o modun okunması için bir anahtar
/// yoktu: haritada 15 rengi gören oyuncu hangi rengin hangi kültür olduğunu
/// çıkaramıyordu. Bu panel o boşluğu kapatır:
///
///   • Sol sütun: her kültür için renk örneği, il sayısı, nüfus payı ve payına
///     göre uzayan çubuk. Sıralama nüfus payına göre azalan (eşitlikte il
///     sayısı, onun da eşitliğinde kimlik) — deterministik.
///   • Sağ sütun: seçili kültürün ayrıntısı ve bu kültürden il bulunan
///     ülkelerin listesi (il sayısına göre azalan).
///   • Satıra tıklamak o kültürü haritada vurgular: harita culture moduna
///     geçer, vurgulanmayan kültürler kararır.
///
/// Panel kalıcı durum tutmaz: kültür verisi `ProvinceCultureSystem`'te ve
/// Save/Load oradan bağlı (M44.5). Vurgulama yalnızca görsel durumdur.
///
/// Ölçü sınırı: 1152x648 ekrana sığacak şekilde 1080x600 tasarlandı.
/// </summary>
public partial class CultureLegendPanel : Control
{
    /// <summary>Bir kültürün tüm özet bilgisi.</summary>
    private sealed class CultureRow
    {
        public string Culture = "";
        public string Display = "";
        public int ProvinceCount;
        public long Population;
        /// <summary>Dünya kültür nüfusuna göre 0..1 pay.</summary>
        public double PopulationShare;
        /// <summary>En büyük kültüre göre 0..1 çubuk uzunluğu.</summary>
        public double BarFraction;
        /// <summary>Bu kültürden il bulunan ülke kimlikleri (il sayısına göre azalan).</summary>
        public readonly List<string> CountryIds = new();
        /// <summary>CountryIds[i] ülkesinin bu kültürden il sayısı.</summary>
        public readonly List<int> CountryProvinceCounts = new();
    }

    // ===== Durum =====

    private Label _headerLabel;      // kültür sayısı / il / nüfus özeti
    private Label _summaryLabel;     // seçili kültür özeti
    private Label _feedbackLabel;    // işlem mesajı
    private Button _highlightBtn;    // vurguyu kaldır
    private Button _closeBtn;

    private ScrollContainer _rowScroll;
    private VBoxContainer _rowList;    // sol: kültür satırları
    private ScrollContainer _detailScroll;
    private VBoxContainer _detailList; // sağ: seçili kültür ayrıntısı

    private readonly List<CultureRow> _rows = new();

    private int _selectedIndex = -1;
    private readonly List<string> _detailTexts = new();

    /// <summary>Seçili kültürün sahibi olduğu ülkeler (il sayısına göre azalan).</summary>
    private readonly List<string> _highlightedCountries = new();

    private long _totalPopulation;
    private int _totalProvinces;

    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    /// <summary>Çubukların tam uzunluğu (piksel) — oran hesaplamada kullanılır.</summary>
    private const float BarMaxWidth = 170f;

    // ===== Test / UI denetim erişimleri =====

    /// <summary>Panelin tasarım alt sınırı — 1152x648 ekrana sığma kontrolü için.</summary>
    public Vector2 DesignedSize => _panel?.CustomMinimumSize ?? Vector2.Zero;

    /// <summary>Kültür satırı sayısı (listelenen ayrı kültür sayısı).</summary>
    public int CultureRowCount => _rows.Count;

    /// <summary>Listelenen kültür kimlikleri, liste sırasıyla.</summary>
    public IReadOnlyList<string> CultureIds
    {
        get
        {
            var ids = new List<string>(_rows.Count);
            foreach (var r in _rows) ids.Add(r.Culture);
            return ids;
        }
    }

    /// <summary>Satır sırasıyla kültürlerin il sayısı.</summary>
    public IReadOnlyList<int> ProvinceCounts
    {
        get
        {
            var counts = new List<int>(_rows.Count);
            foreach (var r in _rows) counts.Add(r.ProvinceCount);
            return counts;
        }
    }

    /// <summary>Satır sırasıyla nüfus payı (0..1, dünya kültür nüfusuna göre).</summary>
    public IReadOnlyList<double> PopulationShares
    {
        get
        {
            var shares = new List<double>(_rows.Count);
            foreach (var r in _rows) shares.Add(r.PopulationShare);
            return shares;
        }
    }

    /// <summary>Satır sırasıyla çubuk uzunluğu (0..1, en büyük kültüre göre).</summary>
    public IReadOnlyList<double> BarFractions
    {
        get
        {
            var fracs = new List<double>(_rows.Count);
            foreach (var r in _rows) fracs.Add(r.BarFraction);
            return fracs;
        }
    }

    /// <summary>Seçili kültürün sahibi olduğu ülke kimlikleri.</summary>
    public IReadOnlyList<string> HighlightedCountryIds => _highlightedCountries;

    /// <summary>Seçili kültür kimliği (boş = seçim yok).</summary>
    public string SelectedCultureId =>
        _selectedIndex >= 0 && _selectedIndex < _rows.Count ? _rows[_selectedIndex].Culture : "";

    /// <summary>Detay satırlarının metinleri.</summary>
    public IReadOnlyList<string> DetailTexts => _detailTexts;

    /// <summary>Son işlem mesajı.</summary>
    public string FeedbackText => _feedbackLabel?.Text ?? "";

    /// <summary>Kültür kaydı olan toplam il sayısı.</summary>
    public int TotalProvinces => _totalProvinces;

    /// <summary>Kültür kaydındaki toplam nüfus.</summary>
    public long TotalPopulation => _totalPopulation;

    /// <summary>Vurgu haritaya uygulanmış mı? (WorldMap yoksa false)</summary>
    public bool MapHighlightApplied
    {
        get
        {
            var wm = WorldMap.Instance;
            if (wm == null) return false;
            string selected = SelectedCultureId;
            return selected.Length > 0 &&
                   string.Equals(wm.CultureHighlight, selected, StringComparison.Ordinal) &&
                   wm.CurrentMapMode == WorldMap.MapMode.Culture;
        }
    }

    /// <summary>ESC ile kapatma istendi mi?</summary>
    public bool CloseRequested { get; private set; }

    // ===== Açılış =====

    /// <summary>Paneli parent altında açılacak şekilde kurar (ModalHost Layer=100).</summary>
    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        if (parent == null) return;

        GD.Print($"[CultureLegendPanel] Açılıyor — kültür kaydı={ProvinceCultureSystem.RecordCount}");

        var panel = new CultureLegendPanel();
        ModalHost.AddUnder(parent, panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // duraklatılmış oyunda da kapatma çalışsın
        UITheme.ApplyTheme(this);

        BuildUi();
        RebuildRows();
        RefreshAll();

        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);
        GD.Print("CultureLegendPanel: hazır");
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

    // ===== Kurulum =====

    private void BuildUi()
    {
        _backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.78f),
            MouseFilter = Control.MouseFilterEnum.Stop // harita girdisini blokla
        };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(1080, 600) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        // ===== BAŞLIK =====
        var header = new HBoxContainer();
        vbox.AddChild(header);

        var title = new Label { Text = "CULTURE ATLAS" };
        title.AddThemeFontSizeOverride("font_size", 26);
        header.AddChild(title);

        _headerLabel = new Label { Text = "" };
        _headerLabel.AddThemeFontSizeOverride("font_size", 14);
        _headerLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _headerLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _headerLabel.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(_headerLabel);

        _closeBtn = new Button
        {
            Text = "CLOSE",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(96, 34)
        };
        _closeBtn.Pressed += Close;
        UITheme.AddHoverScale(_closeBtn);
        header.AddChild(_closeBtn);

        vbox.AddChild(new HSeparator());

        // ===== ÖZET =====
        _summaryLabel = new Label { Text = "" };
        _summaryLabel.AddThemeFontSizeOverride("font_size", 15);
        _summaryLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _summaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_summaryLabel);

        vbox.AddChild(new HSeparator());

        // ===== İKİ SÜTUN =====
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(columns);

        BuildCultureColumn(columns);
        BuildDetailColumn(columns);

        vbox.AddChild(new HSeparator());

        // ===== EYLEMLER =====
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(actions);

        _highlightBtn = new Button
        {
            Text = "CLEAR HIGHLIGHT",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(190, 36),
            TooltipText = "Remove the culture highlight from the map"
        };
        _highlightBtn.Pressed += OnClearHighlightPressed;
        UITheme.AddHoverScale(_highlightBtn);
        actions.AddChild(_highlightBtn);

        _feedbackLabel = new Label { Text = "" };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", 13);
        _feedbackLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Right;
        actions.AddChild(_feedbackLabel);

        BuildUiNote(vbox);
    }

    /// <summary>Sol sütun: kültür satırları (renk + il + nüfus payı + çubuk).</summary>
    private void BuildCultureColumn(HBoxContainer parent)
    {
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 6);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        parent.AddChild(left);

        var listHeader = new Label
        {
            Text = "CULTURES   (click a culture to highlight it on the map)"
        };
        listHeader.AddThemeFontSizeOverride("font_size", 14);
        listHeader.AddThemeColorOverride("font_color", UITheme.TextMuted);
        left.AddChild(listHeader);

        _rowScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(600, 380),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        left.AddChild(_rowScroll);

        _rowList = new VBoxContainer();
        _rowList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _rowList.AddThemeConstantOverride("separation", 4);
        _rowScroll.AddChild(_rowList);
    }

    /// <summary>Sağ sütun: seçili kültürün ayrıntısı ve sahip ülkeleri.</summary>
    private void BuildDetailColumn(HBoxContainer parent)
    {
        var right = new VBoxContainer();
        right.CustomMinimumSize = new Vector2(430, 0);
        right.AddThemeConstantOverride("separation", 6);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        parent.AddChild(right);

        var detailHeader = new Label
        {
            Text = "CULTURE DETAIL   (who speaks it)"
        };
        detailHeader.AddThemeFontSizeOverride("font_size", 14);
        detailHeader.AddThemeColorOverride("font_color", UITheme.TextMuted);
        right.AddChild(detailHeader);

        _detailScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 320),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        right.AddChild(_detailScroll);

        _detailList = new VBoxContainer();
        _detailList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _detailList.AddThemeConstantOverride("separation", 4);
        _detailScroll.AddChild(_detailList);
    }

    private void BuildUiNote(VBoxContainer vbox)
    {
        var note = new Label
        {
            Text = "Population share is the culture's slice of every cultured province. " +
                   "The bar scales to the largest culture, so the widest bar is the most " +
                   "spoken culture in the world."
        };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", UITheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(note);
    }

    // ===== Veri =====

    /// <summary>
    /// Kültür katmanından satırları kurar. Sıralama sabittir: nüfus payı azalan,
    /// eşitlikte il sayısı azalan, son çare olarak kimlik (Ordinal).
    /// </summary>
    private void RebuildRows()
    {
        _rows.Clear();
        _totalProvinces = 0;
        _totalPopulation = 0;

        var records = ProvinceCultureSystem.All();
        if (records.Count == 0)
        {
            GD.Print("[CultureLegendPanel] Kültür kaydı yok — pano boş");
            return;
        }

        // Kültür → o kültüre ait il kayıtları (sahipler ayrı toplanır)
        var byCulture = new Dictionary<string, List<ProvinceCultureSystem.CultureRecord>>(StringComparer.Ordinal);

        foreach (var rec in records)
        {
            if (rec == null) continue;
            _totalProvinces++;
            _totalPopulation += rec.Population;

            string culture = rec.Culture ?? "";
            if (!byCulture.TryGetValue(culture, out var list))
            {
                list = new List<ProvinceCultureSystem.CultureRecord>();
                byCulture[culture] = list;
            }
            list.Add(rec);
        }

        foreach (var kv in byCulture)
        {
            var row = new CultureRow { Culture = kv.Key, Display = DisplayName(kv.Key) };
            foreach (var rec in kv.Value)
            {
                row.ProvinceCount++;
                row.Population += rec.Population;
            }
            row.PopulationShare = _totalPopulation > 0
                ? (double)row.Population / _totalPopulation
                : (double)row.ProvinceCount / _totalProvinces;
            _rows.Add(row);
        }

        // Sahip ülkeler: il sayısına göre azalan, eşitlikte kimlik (Ordinal).
        foreach (var row in _rows)
        {
            var byCountry = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (byCulture.TryGetValue(row.Culture, out var list))
                foreach (var rec in list)
                {
                    string owner = rec.CountryId ?? "";
                    byCountry.TryGetValue(owner, out int n);
                    byCountry[owner] = n + 1;
                }

            foreach (var kv in byCountry.OrderByDescending(kv => kv.Value)
                                        .ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                row.CountryIds.Add(kv.Key);
                row.CountryProvinceCounts.Add(kv.Value);
            }
        }

        // En büyük payı bul (çubuk 1.0 olacak satır) — sıralamadan önce.
        double maxShare = 0d;
        foreach (var row in _rows)
            if (row.PopulationShare > maxShare) maxShare = row.PopulationShare;

        foreach (var row in _rows)
            row.BarFraction = maxShare > 0d ? row.PopulationShare / maxShare : 0d;

        _rows.Sort((a, b) =>
        {
            int c = b.PopulationShare.CompareTo(a.PopulationShare);
            if (c != 0) return c;
            c = b.ProvinceCount.CompareTo(a.ProvinceCount);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Culture, b.Culture);
        });

        GD.Print($"[CultureLegendPanel] {_rows.Count} kültür, {_totalProvinces} il, " +
                 $"nüfus={_totalPopulation}");
    }

    private static string DisplayName(string culture)
    {
        return culture.Length == 0 ? "(no culture)" : culture;
    }

    // ===== Yenileme =====

    private void RefreshAll()
    {
        RefreshHeader();
        BuildCultureRows();
        RefreshDetail();
        RefreshActions();
    }

    private void RefreshHeader()
    {
        _headerLabel.Text = _rows.Count == 0
            ? "NO CULTURE RECORDS"
            : $"CULTURES {_rows.Count}   •   PROVINCES {_totalProvinces}   •   POPULATION {_totalPopulation}";
    }

    private void BuildCultureRows()
    {
        foreach (var child in _rowList.GetChildren())
            child.QueueFree();

        if (_rows.Count == 0)
        {
            var empty = new Label
            {
                Text = "(no culture records — provinces have not been generated yet)"
            };
            empty.AddThemeFontSizeOverride("font_size", 14);
            empty.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _rowList.AddChild(empty);
            _selectedIndex = -1;
            return;
        }

        for (int i = 0; i < _rows.Count; i++)
            _rowList.AddChild(BuildCultureRow(_rows[i], i));

        _selectedIndex = 0;
        ApplySelectionVisual();
    }

    /// <summary>
    /// Tek bir kültür satırı. Buton olarak kurulur: tıklama seçim + harita
    /// vurgusu anlamına gelir, bu yüzden ayrı bir olay bağlantısı gerekmez.
    /// </summary>
    private Button BuildCultureRow(CultureRow row, int index)
    {
        var color = WorldMap.ColorForCulture(row.Culture);

        var btn = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 40),
            TooltipText = $"{row.Display} — {row.ProvinceCount} provinces, " +
                          $"{row.PopulationShare * 100d:N1}% of the world's population"
        };
        int captured = index;
        btn.Pressed += () => OnCultureSelected(captured);
        UITheme.AddHoverScale(btn);

        var hbox = new HBoxContainer();
        hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hbox.AddThemeConstantOverride("separation", 8);
        btn.AddChild(hbox);

        var swatch = new ColorRect { Color = color };
        swatch.CustomMinimumSize = new Vector2(18, 18);
        swatch.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        swatch.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hbox.AddChild(swatch);

        var name = new Label { Text = row.Display };
        name.AddThemeFontSizeOverride("font_size", 15);
        name.CustomMinimumSize = new Vector2(150, 0);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        hbox.AddChild(name);

        AddRowStat(hbox, $"{row.ProvinceCount} prov", 74, UITheme.TextMuted);
        AddRowStat(hbox, $"{row.PopulationShare * 100d:N1}%", 70, UITheme.TextMain);

        var track = new ColorRect { Color = new Color(0.08f, 0.11f, 0.16f, 1f) };
        track.CustomMinimumSize = new Vector2(BarMaxWidth, 10);
        track.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        track.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hbox.AddChild(track);

        var bar = new ColorRect { Color = color };
        bar.CustomMinimumSize = new Vector2(MathF.Round((float)row.BarFraction * BarMaxWidth), 10);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bar.TooltipText = $"{row.PopulationShare * 100d:N1}% of world population";
        hbox.AddChild(bar);

        return btn;
    }

    /// <summary>Satır içinde sabit genişlikli, sağa yapışık istatistik etiketi.</summary>
    private static void AddRowStat(HBoxContainer parent, string text, float width, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", color);
        label.CustomMinimumSize = new Vector2(width, 0);
        label.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.TooltipText = text;
        parent.AddChild(label);
    }

    private void RefreshDetail()
    {
        foreach (var child in _detailList.GetChildren())
            child.QueueFree();
        _detailTexts.Clear();
        _highlightedCountries.Clear();

        var row = _selectedIndex >= 0 && _selectedIndex < _rows.Count ? _rows[_selectedIndex] : null;
        if (row == null)
        {
            AddDetailRow("(no culture selected)", UITheme.TextMuted);
            _summaryLabel.Text = "Select a culture to see which nations speak it.";
            return;
        }

        double sharePercent = row.PopulationShare * 100d;
        _summaryLabel.Text =
            $"{row.Display} — {row.ProvinceCount} provinces ({sharePercent:N1}% of the world), " +
            $"population {row.Population:N0}, spoken in {row.CountryIds.Count} " +
            $"{(row.CountryIds.Count == 1 ? "nation" : "nations")}.";

        AddDetailRow($"Provinces: {row.ProvinceCount} of {_totalProvinces} ({sharePercent:N1}%)",
            UITheme.TextMain);
        AddDetailRow($"Population: {row.Population:N0} ({sharePercent:N1}% of {_totalPopulation:N0})",
            UITheme.TextMain);
        AddDetailRow($"Map colour: {WorldMap.ColorForCulture(row.Culture).ToHtml(true)}",
            UITheme.TextMuted);

        if (row.CountryIds.Count == 0)
        {
            AddDetailRow("Nations speaking it: (none recorded)", UITheme.TextMuted);
        }
        else
        {
            AddDetailRow($"Nations speaking it: {row.CountryIds.Count}", UITheme.Gold);
            for (int i = 0; i < row.CountryIds.Count; i++)
            {
                string countryId = row.CountryIds[i];
                int count = row.CountryProvinceCounts[i];
                string countryName = CountryDatabase.Get(countryId)?.Name ?? countryId;
                float ofCulture = row.ProvinceCount > 0
                    ? (float)count / row.ProvinceCount * 100f
                    : 0f;
                AddDetailRow($"{countryName} ({countryId}) — {count} prov ({ofCulture:N0}% of it)",
                    UITheme.TextMain);
                _highlightedCountries.Add(countryId);
            }
        }

        // Kültürün yalnızca tek bir ülkede toplandığını görmek oyunculuk açısından
        // kritik: o ülke "saf" kabul edilir, karışık olanlar uzlaşı ister.
        AddDetailRow(
            row.CountryIds.Count <= 1
                ? "Concentration: single nation — a unified people"
                : $"Concentration: split across {row.CountryIds.Count} nations",
            UITheme.TextMuted);
    }

    private void AddDetailRow(string text, Color color)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var mark = new Label { Text = "•" };
        mark.AddThemeFontSizeOverride("font_size", 15);
        mark.AddThemeColorOverride("font_color", UITheme.Gold);
        mark.CustomMinimumSize = new Vector2(14, 0);
        row.AddChild(mark);

        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);

        _detailList.AddChild(row);
        _detailTexts.Add(text);
    }

    private void RefreshActions()
    {
        bool hasSelection = _selectedIndex >= 0 && _selectedIndex < _rows.Count && _rows.Count > 0;
        _highlightBtn.Disabled = !hasSelection;
    }

    /// <summary>Satır tıklaması: seçim + o kültürün sahip ülkelerini vurgula.</summary>
    private void OnCultureSelected(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        _selectedIndex = index;

        var row = _rows[index];
        GD.Print($"[CultureLegendPanel] Kültür seçildi: {row.Display} " +
                 $"({row.ProvinceCount} il, {row.CountryIds.Count} ülke)");

        ApplySelectionVisual();
        RefreshDetail();
        ApplyMapHighlight(row);

        _feedbackLabel.Text =
            $"{row.Display} highlighted — {_highlightedCountries.Count} " +
            $"{(_highlightedCountries.Count == 1 ? "nation" : "nations")} on the map";
        _feedbackLabel.AddThemeColorOverride("font_color", UITheme.GoldBright);
    }

    private void OnClearHighlightPressed()
    {
        var wm = WorldMap.Instance;
        if (wm != null) wm.SetCultureHighlight("");

        _feedbackLabel.Text = "Highlight cleared";
        _feedbackLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        GD.Print("[CultureLegendPanel] Harita vurgusu kaldırıldı");
    }

    /// <summary>Seçili satırı görsel olarak işaretle (Godot 4 Button'da Selected yok).</summary>
    private void ApplySelectionVisual()
    {
        for (int i = 0; i < _rowList.GetChildCount(); i++)
        {
            if (_rowList.GetChild(i) is not Button b) continue;
            bool selected = i == _selectedIndex;
            b.AddThemeColorOverride("font_color", selected ? UITheme.GoldBright : UITheme.TextMain);
            b.Modulate = selected ? new Color(1f, 1f, 1f, 1f) : new Color(0.82f, 0.82f, 0.82f, 1f);
        }
    }

    /// <summary>
    /// Seçili kültürü haritada vurgula. Harita örneği yoksa (başlık menüsü /
    /// headless test) yalnızca panel durumu güncellenir.
    /// </summary>
    private void ApplyMapHighlight(CultureRow row)
    {
        var wm = WorldMap.Instance;
        if (wm == null)
        {
            _feedbackLabel.Text = $"{row.Display} selected (no map open)";
            _feedbackLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            return;
        }

        wm.SetCultureHighlight(row.Culture);
    }

    // ===== Yardımcılar =====

    private void Close()
    {
        GD.Print("CultureLegendPanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}
