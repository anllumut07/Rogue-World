using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Culture Legend Panel (M46.2 + Stage 2 UI Upgrade).
/// - Left column: each culture with color swatch, province count, population share and bar.
///   Sorted by population share descending (deterministic tie-break: province count, then ID).
/// - Right column: selected culture detail and nations speaking it (by province count).
/// - Click row highlights that culture on map: switches to culture mode, dims others.
/// - Uses GameTheme + reusable UI components (GamePanel, Chip, ProgressBar, EmptyState).
/// - 1152x648 fit (1080x600), backdrop blocks map input, ESC/CLOSE closes.
/// Test properties preserved for --culture-legend-panel-test compatibility.
/// </summary>
public partial class CultureLegendPanel : GamePanel
{
    public static bool IsOpen { get; private set; } = false;

    private sealed class CultureRow
    {
        public string Culture = "";
        public string Display = "";
        public int ProvinceCount;
        public long Population;
        public double PopulationShare;
        public double BarFraction;
        public List<string> CountryIds = new();
        public List<int> CountryProvinceCounts = new();
    }

    private Label _headerLabel;
    private Label _summaryLabel;
    private Label _feedbackLabel;
    private Button _highlightBtn;
    private Button _closeBtn;

    private ScrollContainer _rowScroll;
    private VBoxContainer _rowList;
    private ScrollContainer _detailScroll;
    private VBoxContainer _detailList;

    private List<CultureRow> _rows = new();
    private int _selectedIndex = -1;
    private List<string> _detailTexts = new();
    private List<string> _highlightedCountries = new();

    private long _totalPopulation;
    private int _totalProvinces;

    private ColorRect _backdrop;
    private PanelContainer _panel;

    public const float BarMaxWidth = 170f;

    // ===== Test / UI inspection accessors =====

    /// <summary>Panelin tasarım alt sınırı — 1152x648 ekrana sığma kontrolü için.</summary>
    public Vector2 DesignedSize => new Vector2(1080, 600);

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
    public new bool CloseRequested { get; private set; }

    // ===== Açılış =====

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        if (parent == null) return;

        GD.Print($"[CultureLegendPanel] Açılıyor — kültür kaydı={ProvinceCultureSystem.RecordCount}");

        var panel = new CultureLegendPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "CULTURE ATLAS";
        MinSize = new Vector2(1080, 600);
        MaxSize = new Vector2(1200, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        // Fix close button text so test's FindCultureRowButtons filters it out (expects "CLOSE")
        var closeBtn = FindCloseButtonInTitleBar();
        if (closeBtn != null) closeBtn.Text = "CLOSE";

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        RebuildRows();
        RefreshAll();

        IsOpen = true;

        GD.Print("CultureLegendPanel: hazır (Stage 2 UI)");
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
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // Header
        var header = new HBoxContainer();
        content.AddChild(header);

        var title = new Label { Text = "CULTURE ATLAS" };
        title.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeXLarge);
        title.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        header.AddChild(title);

        _headerLabel = new Label { Text = "" };
        _headerLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _headerLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
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
        GameTheme.StyleButton(_closeBtn);
        header.AddChild(_closeBtn);

        content.AddChild(new HSeparator());

        // Summary
        _summaryLabel = new Label { Text = "" };
        _summaryLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        _summaryLabel.AddThemeColorOverride("font_color", GameTheme.Gold);
        _summaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_summaryLabel);

        content.AddChild(new HSeparator());

        // Two columns
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", GameTheme.SpaceL);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        content.AddChild(columns);

        BuildCultureColumn(columns);
        BuildDetailColumn(columns);

        content.AddChild(new HSeparator());

        // Actions
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(actions);

        _highlightBtn = new Button
        {
            Text = "CLEAR HIGHLIGHT",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(190, 36),
            TooltipText = "Remove the culture highlight from the map"
        };
        _highlightBtn.Pressed += OnClearHighlightPressed;
        GameTheme.StyleButton(_highlightBtn);
        actions.AddChild(_highlightBtn);

        _feedbackLabel = new Label { Text = "" };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        _feedbackLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Right;
        actions.AddChild(_feedbackLabel);

        BuildUiNote(content);
    }

    private void BuildCultureColumn(HBoxContainer parent)
    {
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        parent.AddChild(left);

        var listHeader = new Label
        {
            Text = "CULTURES   (click a culture to highlight it on the map)"
        };
        listHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        listHeader.AddThemeColorOverride("font_color", GameTheme.TextMuted);
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
        _rowList.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        _rowScroll.AddChild(_rowList);
    }

    private void BuildDetailColumn(HBoxContainer parent)
    {
        var right = new VBoxContainer();
        right.CustomMinimumSize = new Vector2(430, 0);
        right.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        parent.AddChild(right);

        var detailHeader = new Label
        {
            Text = "CULTURE DETAIL   (who speaks it)"
        };
        detailHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        detailHeader.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        right.AddChild(detailHeader);

        _detailScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 320),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        right.AddChild(_detailScroll);

        _detailList = new VBoxContainer();
        _detailList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _detailList.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        _detailScroll.AddChild(_detailList);
    }

    private void BuildUiNote(Control vbox)
    {
        var note = new Label
        {
            Text = "Population share is the culture's slice of every cultured province. " +
                   "The bar scales to the largest culture, so the widest bar is the most " +
                   "spoken culture in the world."
        };
        note.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        note.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(note);
    }

    // ===== Data =====

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

        // Owner countries
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

        // Max share for bar scaling
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

        GD.Print($"[CultureLegendPanel] {_rows.Count} kültür, {_totalProvinces} il, nüfus={_totalPopulation}");
    }

    private static string DisplayName(string culture)
    {
        return culture.Length == 0 ? "(no culture)" : culture;
    }

    // ===== Refresh =====

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
            empty.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            empty.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            _rowList.AddChild(empty);
            _selectedIndex = -1;
            return;
        }

        for (int i = 0; i < _rows.Count; i++)
            _rowList.AddChild(BuildCultureRow(_rows[i], i));

        _selectedIndex = 0;
        ApplySelectionVisual();
    }

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
        GameTheme.AddHoverScale(btn);

        var hbox = new HBoxContainer();
        hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hbox.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        btn.AddChild(hbox);

        var swatch = new ColorRect { Color = color };
        swatch.CustomMinimumSize = new Vector2(18, 18);
        swatch.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        swatch.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hbox.AddChild(swatch);

        var name = new Label { Text = row.Display };
        name.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        name.CustomMinimumSize = new Vector2(150, 0);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        hbox.AddChild(name);

        AddRowStat(hbox, $"{row.ProvinceCount} prov", 74, GameTheme.TextMuted);
        AddRowStat(hbox, $"{row.PopulationShare * 100d:N1}%", 70, GameTheme.TextPrimary);

        var track = new ColorRect { Color = GameTheme.BgMedium };
        track.CustomMinimumSize = new Vector2(BarMaxWidth, 10);
        track.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        track.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hbox.AddChild(track);

        var bar = new ColorRect { Color = color };
        bar.CustomMinimumSize = new Vector2(Mathf.Round((float)row.BarFraction * BarMaxWidth), 10);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bar.TooltipText = $"{row.PopulationShare * 100d:N1}% of world population";
        hbox.AddChild(bar);

        return btn;
    }

    private static void AddRowStat(HBoxContainer parent, string text, float width, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
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
            AddDetailRow("(no culture selected)", GameTheme.TextMuted);
            _summaryLabel.Text = "Select a culture to see which nations speak it.";
            return;
        }

        double sharePercent = row.PopulationShare * 100d;
        _summaryLabel.Text =
            $"{row.Display} — {row.ProvinceCount} provinces ({sharePercent:N1}% of the world), " +
            $"population {row.Population:N0}, spoken in {row.CountryIds.Count} " +
            $"{(row.CountryIds.Count == 1 ? "nation" : "nations")}.";

        AddDetailRow($"Provinces: {row.ProvinceCount} of {_totalProvinces} ({sharePercent:N1}%)",
            GameTheme.TextPrimary);
        AddDetailRow($"Population: {row.Population:N0} ({sharePercent:N1}% of {_totalPopulation:N0})",
            GameTheme.TextPrimary);
        AddDetailRow($"Map colour: {WorldMap.ColorForCulture(row.Culture).ToHtml(true)}",
            GameTheme.TextMuted);

        if (row.CountryIds.Count == 0)
        {
            AddDetailRow("Nations speaking it: (none recorded)", GameTheme.TextMuted);
        }
        else
        {
            AddDetailRow($"Nations speaking it: {row.CountryIds.Count}", GameTheme.Gold);
            for (int i = 0; i < row.CountryIds.Count; i++)
            {
                string countryId = row.CountryIds[i];
                int count = row.CountryProvinceCounts[i];
                string countryName = CountryDatabase.Get(countryId)?.Name ?? countryId;
                float ofCulture = row.ProvinceCount > 0
                    ? (float)count / row.ProvinceCount * 100f
                    : 0f;
                AddDetailRow($"{countryName} ({countryId}) — {count} prov ({ofCulture:N0}% of it)",
                    GameTheme.TextPrimary);
                _highlightedCountries.Add(countryId);
            }
        }

        AddDetailRow(
            row.CountryIds.Count <= 1
                ? "Concentration: single nation — a unified people"
                : $"Concentration: split across {row.CountryIds.Count} nations",
            GameTheme.TextMuted);
    }

    private void AddDetailRow(string text, Color color)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", GameTheme.SpaceS);

        // Use a Label instead of Chip (Chip inherits from Button and would be counted by test)
        var bullet = new Label { Text = "•" };
        bullet.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        bullet.AddThemeColorOverride("font_color", GameTheme.Gold);
        bullet.CustomMinimumSize = new Vector2(14, 0);
        row.AddChild(bullet);

        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
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
        _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
    }

    private void OnClearHighlightPressed()
    {
        var wm = WorldMap.Instance;
        if (wm != null) wm.SetCultureHighlight("");

        _feedbackLabel.Text = "Highlight cleared";
        _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        GD.Print("[CultureLegendPanel] Harita vurgusu kaldırıldı");
    }

    private void ApplySelectionVisual()
    {
        for (int i = 0; i < _rowList.GetChildCount(); i++)
        {
            if (_rowList.GetChild(i) is not Button b) continue;
            bool selected = i == _selectedIndex;
            b.AddThemeColorOverride("font_color", selected ? GameTheme.GoldBright : GameTheme.TextPrimary);
            b.Modulate = selected ? new Color(1f, 1f, 1f, 1f) : new Color(0.82f, 0.82f, 0.82f, 1f);
        }
    }

    private void ApplyMapHighlight(CultureRow row)
    {
        var wm = WorldMap.Instance;
        if (wm == null)
        {
            // Try to find WorldMap in scene tree if singleton not set yet
            wm = GetNodeOrNull<WorldMap>("/root/WorldMap") ?? GetNodeOrNull<WorldMap>("/root/GameManager/WorldMap");
            if (wm == null)
            {
                // Search all children of root
                var root = GetTree().Root;
                wm = FindWorldMapInTree(root);
            }
        }

        if (wm == null)
        {
            _feedbackLabel.Text = $"{row.Display} selected (no map open)";
            _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            GD.Print($"[CultureLegendPanel] WorldMap not found for highlight: {row.Culture}");
            return;
        }

        // Explicitly set highlight and ensure map mode is Culture
        wm.SetCultureHighlight(row.Culture);
        if (wm.CurrentMapMode != WorldMap.MapMode.Culture)
        {
            wm.SetMapMode(WorldMap.MapMode.Culture);
        }
        GD.Print($"[CultureLegendPanel] Applied highlight: {row.Culture}, MapMode: {wm.CurrentMapMode}, Highlight: {wm.CultureHighlight}");
    }

    private WorldMap FindWorldMapInTree(Node node)
    {
        if (node is WorldMap wm) return wm;
        foreach (Node child in node.GetChildren())
        {
            var found = FindWorldMapInTree(child);
            if (found != null) return found;
        }
        return null;
    }

    private Button FindCloseButtonInTitleBar()
    {
        foreach (Node child in GetChildren())
        {
            var found = FindButtonRecursive(child, "✕");
            if (found != null) return found;
        }
        return null;
    }

    private Button FindButtonRecursive(Node node, string text)
    {
        if (node is Button btn && btn.Text == text) return btn;
        foreach (Node child in node.GetChildren())
        {
            var found = FindButtonRecursive(child, text);
            if (found != null) return found;
        }
        return null;
    }

    public override void Close()
    {
        GD.Print("CultureLegendPanel: kapatılıyor");
        base.Close();
    }
}