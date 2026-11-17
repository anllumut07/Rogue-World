using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Formable Nations Panel (M44.5 + Stage 2 UI Upgrade):
/// Shows formable nations and culture map for player's country.
/// Uses GameTheme + reusable UI components (GamePanel, ProgressBar, Chip, TooltipBuilder, EmptyState).
/// Preserves: backdrop blocking map input, ESC to close, country selector, condition checklist.
/// Test properties preserved for --formable-panel-test compatibility.
/// </summary>
public partial class FormablePanel : GamePanel
{
    public static bool IsOpen { get; private set; } = false;

    private string _playerId = "";
    private int _selectedIndex = -1;
    private List<string> _formableIds = new();
    private List<CultureRow> _cultureRows = new();
    private long _totalPopulation = 0;
    private int _totalProvinces = 0;

    private Label _headerLabel;
    private Label _summaryLabel;
    private Label _feedbackLabel;
    private Button _formBtn;
    private Button _cancelBtn;
    private ItemList _formableList;
    private VBoxContainer _conditionList;
    private VBoxContainer _cultureList;
    private Label _pendingLabel;
    private EmptyState _emptyState;

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

    // ===== Test / UI inspection accessors =====

    /// <summary>Panelin çalıştığı ülke (oyuncu).</summary>
    public string PlayerId => _playerId;

    /// <summary>Panelin tasarım alt sınırı — 1152x648 ekrana sığma kontrolü için (test/UI denetim).</summary>
    public Vector2 DesignedSize => new Vector2(1080, 600);

    /// <summary>Kurgu ülke satırı sayısı (test/UI denetim).</summary>
    public int FormableRowCount => _formableList?.ItemCount ?? 0;

    /// <summary>Kültür satırı sayısı (test/UI denetim).</summary>
    public int CultureRowCount => _cultureList?.GetChildCount() ?? 0;

    /// <summary>Kültür satırlarının kimlikleri (test/UI denetim).</summary>
    public IReadOnlyList<string> CultureLabels
    {
        get
        {
            var ids = new List<string>(_cultureRows.Count);
            foreach (var r in _cultureRows) ids.Add(r.Culture);
            return ids;
        }
    }

    /// <summary>Kültür satırlarının il sayıları (test/UI denetim).</summary>
    public IReadOnlyList<int> CultureCounts
    {
        get
        {
            var counts = new List<int>(_cultureRows.Count);
            foreach (var r in _cultureRows) counts.Add(r.ProvinceCount);
            return counts;
        }
    }

    /// <summary>Koşul satırlarının sağlanma durumları (test/UI denetim).</summary>
    public IReadOnlyList<bool> ConditionMet
    {
        get
        {
            var met = new List<bool>();
            string formableId = SelectedFormableId;
            if (formableId.Length == 0) return met;
            var items = FormableSystem.EvaluateConditions(_playerId, formableId);
            foreach (var item in items) met.Add(item.Met);
            return met;
        }
    }

    /// <summary>Seçili kurgu ülke kimliği (test/UI denetim).</summary>
    public string SelectedFormableId =>
        _formableIds.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _formableIds.Count
            ? _formableIds[_selectedIndex]
            : "";

    /// <summary>FORM NATION butonu basılabiliyor mu? (test/UI denetim).</summary>
    public bool FormDisabled => _formBtn?.Disabled ?? true;

    /// <summary>CANCEL butonu basılabiliyor mu? (test/UI denetim).</summary>
    public bool CancelDisabled => _cancelBtn?.Disabled ?? true;

    /// <summary>Son işlem mesajı (test/UI denetim).</summary>
    public string FeedbackText => _feedbackLabel?.Text ?? "";

    /// <summary>ESC ile kapatma istendi mi? (test/UI denetim).</summary>
    public new bool CloseRequested { get; private set; }

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        if (parent == null) return;

        var gm = GameManager.Instance;
        if (gm == null || !gm.HasPlayer) return;

        GD.Print($"[FormablePanel] Açılıyor — ülke={gm.PlayerCountryId}, formable tanım={FormableDataSystem.FormableCount}");

        var panel = new FormablePanel { _playerId = gm.PlayerCountryId ?? "" };
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "FORMABLE NATIONS";
        MinSize = new Vector2(1080, 600);
        MaxSize = new Vector2(1200, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        RebuildData();
        RefreshAll();
        IsOpen = true;

        GD.Print("FormablePanel: hazır (Stage 2 UI)");
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

    private void BuildUi()
    {
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // Header row with title and country info
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(header);

        var title = new Label { Text = "FORMABLE NATIONS" };
        title.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeXLarge);
        title.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        header.AddChild(title);

        _headerLabel = new Label { Text = "" };
        _headerLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _headerLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        _headerLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _headerLabel.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(_headerLabel);

        var closeBtn = new Button
        {
            Text = "CLOSE",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(96, 34)
        };
        closeBtn.Pressed += Close;
        GameTheme.StyleButton(closeBtn);
        header.AddChild(closeBtn);

        content.AddChild(new HSeparator());

        // Summary of selected formable
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

        BuildFormableColumn(columns);
        BuildCultureColumn(columns);

        content.AddChild(new HSeparator());

        // Actions
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(actions);

        _formBtn = new Button
        {
            Text = "FORM NATION",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(170, 36),
            TooltipText = "Start forming the selected nation"
        };
        _formBtn.Pressed += OnFormPressed;
        GameTheme.StyleButton(_formBtn);
        actions.AddChild(_formBtn);

        _cancelBtn = new Button
        {
            Text = "CANCEL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(120, 36),
            TooltipText = "Cancel the formation in progress"
        };
        _cancelBtn.Pressed += OnCancelPressed;
        GameTheme.StyleButton(_cancelBtn);
        actions.AddChild(_cancelBtn);

        _pendingLabel = new Label { Text = "" };
        _pendingLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _pendingLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        actions.AddChild(_pendingLabel);

        _feedbackLabel = new Label { Text = "" };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        _feedbackLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Right;
        actions.AddChild(_feedbackLabel);
    }

    private void BuildFormableColumn(HBoxContainer parent)
    {
        var left = new VBoxContainer();
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        parent.AddChild(left);

        var listHeader = new Label
        {
            Text = "AVAILABLE TO FOUND   (select a nation to see its conditions)"
        };
        listHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        listHeader.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        left.AddChild(listHeader);

        _formableList = new ItemList
        {
            CustomMinimumSize = new Vector2(460, 180),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MaxColumns = 1
        };
        _formableList.ItemSelected += OnFormableSelected;
        left.AddChild(_formableList);

        var condHeader = new Label
        {
            Text = "CONDITIONS"
        };
        condHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        condHeader.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        left.AddChild(condHeader);

        var condScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 120),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        left.AddChild(condScroll);

        _conditionList = new VBoxContainer();
        _conditionList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _conditionList.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        condScroll.AddChild(_conditionList);

        _emptyState = EmptyState.CreateGeneric("No Formable Nations", "Select a country to see available formables.");
        _emptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _emptyState.Visible = false;
        _conditionList.AddChild(_emptyState);

        var note = new Label
        {
            Text = "Political power is spent when the formation finishes, not when it starts, so a formation is never lost halfway if your power runs low."
        };
        note.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        note.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(note);
    }

    private void BuildCultureColumn(HBoxContainer parent)
    {
        var right = new VBoxContainer();
        right.CustomMinimumSize = new Vector2(400, 0);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        parent.AddChild(right);

        var cultureHeader = new Label
        {
            Text = "CULTURE MAP   (your provinces by culture)"
        };
        cultureHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        cultureHeader.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        right.AddChild(cultureHeader);

        var cultureScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 300),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        right.AddChild(cultureScroll);

        _cultureList = new VBoxContainer();
        _cultureList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _cultureList.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        cultureScroll.AddChild(_cultureList);
    }

    private void RebuildData()
    {
        // Build formable list
        _formableIds.Clear();
        _formableList.Clear();

        foreach (var def in FormableDataSystem.ForParent(_playerId)
                     .OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            var country = CountryDatabase.Get(_playerId);
            int owned = ProvinceCultureSystem.CultureProvinceCount(_playerId, def.Culture);
            _formableIds.Add(def.Id);
            _formableList.AddItem(
                $"{def.Name} ({def.Id}) — {def.Culture}: {owned}/{def.RequiredProvinces} il, " +
                $"%{def.MinimumControlPercent:N0} control, {def.PoliticalPowerCost:N0} PP, " +
                $"{def.DaysRequired:N0} days");
        }

        if (_formableIds.Count == 0)
            _formableList.AddItem("(no nation can be founded from your country)");

        _selectedIndex = _formableIds.Count > 0 ? 0 : -1;
        if (_selectedIndex == 0)
            _formableList.Select(0);

        // Build culture rows
        _cultureRows.Clear();
        _totalProvinces = 0;
        _totalPopulation = 0;

        var records = ProvinceCultureSystem.ForCountry(_playerId);
        if (records.Count == 0)
        {
            GD.Print("[FormablePanel] Kültür kaydı yok — pano boş");
            return;
        }

        var byCulture = new Dictionary<string, List<ProvinceCultureSystem.CultureRecord>>(StringComparer.Ordinal);

        foreach (var rec in records)
        {
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
            _cultureRows.Add(row);
        }

        // Owner countries
        foreach (var row in _cultureRows)
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
        foreach (var row in _cultureRows)
            if (row.PopulationShare > maxShare) maxShare = row.PopulationShare;

        foreach (var row in _cultureRows)
            row.BarFraction = maxShare > 0d ? row.PopulationShare / maxShare : 0d;

        _cultureRows.Sort((a, b) =>
        {
            int c = b.ProvinceCount.CompareTo(a.ProvinceCount);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Culture, b.Culture);
        });

        GD.Print($"[FormablePanel] {_cultureRows.Count} kültür, {_totalProvinces} il, nüfus={_totalPopulation}");
    }

    private static string DisplayName(string culture)
    {
        return culture.Length == 0 ? "(no culture)" : culture;
    }

    private void RefreshAll()
    {
        RefreshHeader();
        BuildCultureRows();
        RefreshConditions();
        RefreshActions();
    }

    private void RefreshHeader()
    {
        var country = CountryDatabase.Get(_playerId);
        int provinceCount = ProvinceOwnershipSystem.GetProvincesByOwner(_playerId).Count;
        long population = ProvinceCultureSystem.TotalPopulation(_playerId);
        _headerLabel.Text = $"{country?.Name ?? _playerId} ({_playerId})   •   PROVINCES {provinceCount}   •   POPULATION {population:N0}   •   PP {country?.PoliticalPower ?? 0:N0}";
    }

    private void BuildCultureRows()
    {
        foreach (var child in _cultureList.GetChildren())
            child.QueueFree();

        if (_cultureRows.Count == 0)
        {
            var empty = new Label { Text = "(no culture records)" };
            empty.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            empty.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            _cultureList.AddChild(empty);
            return;
        }

        const float BarMaxWidth = 170f;

        for (int i = 0; i < _cultureRows.Count; i++)
        {
            var row = _cultureRows[i];
            var color = WorldMap.ColorForCulture(row.Culture);

            var btn = new Button
            {
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 40),
                TooltipText = $"{row.Display} — {row.ProvinceCount} provinces, {row.PopulationShare * 100d:N1}% of the world's population"
            };
            int captured = i;
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

            _cultureList.AddChild(btn);
        }
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

    private void RefreshConditions()
    {
        foreach (var child in _conditionList.GetChildren())
        {
            if (child == _emptyState) continue;
            _conditionList.RemoveChild(child);
            child.QueueFree();
        }

        string formableId = SelectedFormableId;
        if (formableId.Length == 0)
        {
            _emptyState.SetContent("No Nation Selected", "Select a formable nation to see its conditions.");
            _emptyState.Visible = true;
            _summaryLabel.Text = "Select a nation from the list to see its conditions.";
            return;
        }

        _emptyState.Visible = false;

        var def = FormableDataSystem.Get(formableId);
        if (def == null) return;

        var items = FormableSystem.EvaluateConditions(_playerId, formableId);

        foreach (var item in items)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", GameTheme.SpaceS);

            var chip = Chip.CreateStatus(item.Met ? "✓" : "✗",
                item.Met ? Chip.Variant.Success : Chip.Variant.Danger);
            row.AddChild(chip);

            var label = new Label { Text = $"{item.Label}: {item.Detail}" };
            label.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.AddChild(label);

            _conditionList.AddChild(row);
        }

        _summaryLabel.Text =
            $"{def.Name} ({def.Id}) — culture '{def.Culture}', " +
            $"{def.RequiredProvinces} provinces min, %{def.MinimumControlPercent:N0} control, " +
            $"{def.PoliticalPowerCost:N0} PP, {def.DaysRequired:N0} days. Parents: " +
            $"{string.Join(", ", def.ParentCountryIds ?? new List<string>())}";
    }

    private void RefreshActions()
    {
        bool canForm = SelectedFormableId.Length > 0 &&
                       FormableSystem.CanForm(_playerId, SelectedFormableId);
        _formBtn.Disabled = !canForm;
        _cancelBtn.Disabled = !FormableSystem.IsPending(_playerId);

        var state = FormableSystem.State(_playerId);
        if (state == null)
        {
            _pendingLabel.Text = "";
            return;
        }
        float progress = FormableSystem.Progress(_playerId) * 100f;
        _pendingLabel.Text =
            $"forming {state.FormableId}: {state.DaysElapsed:N0}/{state.DaysRequired:N0} days (%{progress:N0})";
    }

    private void OnFormableSelected(long index)
    {
        if (index < 0 || index >= _formableIds.Count) return;
        _selectedIndex = (int)index;
        GD.Print($"[FormablePanel] Kurgu ülke seçildi: {_formableIds[_selectedIndex]}");
        RefreshConditions();
        RefreshActions();
    }

    private void OnFormPressed()
    {
        string formableId = SelectedFormableId;
        if (formableId.Length == 0)
        {
            _feedbackLabel.Text = "no nation selected";
            _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            return;
        }

        bool started = FormableSystem.TryStart(_playerId, formableId, out string reason);
        if (!started)
        {
            _feedbackLabel.Text = $"cannot form: {reason}";
            _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.Red);
            GD.Print($"[FormablePanel] Kurma reddedildi — {_playerId} → {formableId}: {reason}");
            return;
        }

        var def = FormableDataSystem.Get(formableId);
        _feedbackLabel.Text = $"forming {def?.Name ?? formableId} ({def?.DaysRequired ?? 0} days)";
        _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        RefreshConditions();
        RefreshActions();
    }

    private void OnCancelPressed()
    {
        bool cancelled = FormableSystem.Cancel(_playerId);
        _feedbackLabel.Text = cancelled ? "formation cancelled" : "no formation in progress";
        _feedbackLabel.AddThemeColorOverride("font_color",
            cancelled ? GameTheme.GoldBright : GameTheme.TextMuted);
        RefreshActions();
    }

    private void OnCultureSelected(int index)
    {
        if (index < 0 || index >= _cultureRows.Count) return;
        var row = _cultureRows[index];
        GD.Print($"[FormablePanel] Kültür seçildi: {row.Display} ({row.ProvinceCount} il, {row.CountryIds.Count} ülke)");

        // Apply map highlight
        var wm = WorldMap.Instance;
        if (wm != null)
        {
            wm.SetCultureHighlight(row.Culture);
            _feedbackLabel.Text = $"{row.Display} highlighted on map";
            _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        }
        else
        {
            _feedbackLabel.Text = $"{row.Display} selected (no map open)";
            _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        }
    }

    public override void Close()
    {
        GD.Print("FormablePanel: kapatılıyor");
        base.Close();
    }
}