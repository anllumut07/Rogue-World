using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Releasable Nations Panel (M44.5 + Stage 2 UI Upgrade):
/// Shows player's annexed/puppet nations and allows releasing them.
/// Uses GameTheme + reusable UI components (GamePanel, Chip, TooltipBuilder, EmptyState).
/// Preserves: backdrop blocking map input, ESC to close, subject list, detail view.
/// Test properties preserved for --releasable-panel-test compatibility.
/// </summary>
public partial class ReleasablePanel : GamePanel
{
    public static bool IsOpen { get; private set; } = false;

    private string _playerId = "";
    private int _selectedIndex = -1;
    private List<string> _subjectIds = new();
    private List<string> _detailTexts = new();

    private Label _headerLabel;
    private Label _summaryLabel;
    private Label _feedbackLabel;
    private Button _releaseBtn;
    private ItemList _subjectList;
    private VBoxContainer _detailList;
    private EmptyState _emptyState;

    // ===== Test / UI inspection accessors =====

    /// <summary>Panelin çalıştığı ülke (overlord / oyuncu).</summary>
    public string PlayerId => _playerId;

    /// <summary>Panelin tasarım alt sınırı — 1152x648 ekrana sığma kontrolü için (test/UI denetim).</summary>
    public Vector2 DesignedSize => new Vector2(1080, 600);

    /// <summary>Bağımlı ülke satırı sayısı (test/UI denetim).</summary>
    public int SubjectRowCount => _subjectList?.ItemCount ?? 0;

    /// <summary>Detay satırı sayısı (test/UI denetim).</summary>
    public int DetailRowCount => _detailList?.GetChildCount() ?? 0;

    /// <summary>Detay satırlarının metinleri (test/UI denetim).</summary>
    public IReadOnlyList<string> DetailTexts => _detailTexts;

    /// <summary>Listelenen bağımlı ülke kimlikleri (test/UI denetim).</summary>
    public IReadOnlyList<string> SubjectIds => _subjectIds;

    /// <summary>RELEASE NATION butonu basılabiliyor mu? (test/UI denetim).</summary>
    public bool ReleaseDisabled => _releaseBtn?.Disabled ?? true;

    /// <summary>Son işlem mesajı (test/UI denetim).</summary>
    public string FeedbackText => _feedbackLabel?.Text ?? "";

    /// <summary>Seçili bağımlı ülke kimliği (test/UI denetim).</summary>
    public string SelectedSubjectId =>
        _subjectIds.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _subjectIds.Count
            ? _subjectIds[_selectedIndex]
            : "";

    /// <summary>ESC ile kapatma istendi mi? (test/UI denetim).</summary>
    public new bool CloseRequested { get; private set; }

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        if (parent == null) return;

        var gm = GameManager.Instance;
        if (gm == null || !gm.HasPlayer) return;

        GD.Print($"[ReleasablePanel] Açılıyor — overlord={gm.PlayerCountryId}, bağımlı kayıt={ReleasableSystem.SubjectsOf(gm.PlayerCountryId ?? "").Count}");

        var panel = new ReleasablePanel { _playerId = gm.PlayerCountryId ?? "" };
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "SUBJECTS & RELEASE";
        MinSize = new Vector2(1080, 600);
        MaxSize = new Vector2(1200, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        RefreshAll();
        IsOpen = true;

        GD.Print("ReleasablePanel: hazır (Stage 2 UI)");
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

        var title = new Label { Text = "SUBJECTS & RELEASE" };
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

        // Summary of selected subject
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

        BuildSubjectColumn(columns);
        BuildDetailColumn(columns);

        content.AddChild(new HSeparator());

        // Actions
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(actions);

        _releaseBtn = new Button
        {
            Text = "RELEASE NATION",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(190, 36),
            TooltipText = "Give this nation its independence (provinces and factories return)"
        };
        _releaseBtn.Pressed += OnReleasePressed;
        GameTheme.StyleButton(_releaseBtn);
        actions.AddChild(_releaseBtn);

        _feedbackLabel = new Label { Text = "" };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        _feedbackLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Right;
        actions.AddChild(_feedbackLabel);
    }

    private void BuildSubjectColumn(HBoxContainer parent)
    {
        var left = new VBoxContainer();
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        parent.AddChild(left);

        var listHeader = new Label
        {
            Text = "YOUR SUBJECTS   (select a nation to see what you return)"
        };
        listHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        listHeader.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        left.AddChild(listHeader);

        _subjectList = new ItemList
        {
            CustomMinimumSize = new Vector2(460, 300),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MaxColumns = 1
        };
        _subjectList.ItemSelected += OnSubjectSelected;
        left.AddChild(_subjectList);

        _emptyState = EmptyState.CreateGeneric("No Subjects", "You have no annexed or puppet nations.");
        _emptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _emptyState.Visible = false;
        left.AddChild(_emptyState);

        var note = new Label
        {
            Text = "Releasing returns the provinces you still hold, the factory share taken at " +
                   "annexation, and hands the nation back to its own AI. Occupation pressure " +
                   "from a released people is gone with them."
        };
        note.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        note.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(note);
    }

    private void BuildDetailColumn(HBoxContainer parent)
    {
        var right = new VBoxContainer();
        right.CustomMinimumSize = new Vector2(400, 0);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        parent.AddChild(right);

        var detailHeader = new Label
        {
            Text = "NATION DETAIL   (what comes back)"
        };
        detailHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        detailHeader.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        right.AddChild(detailHeader);

        var detailScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 300),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        right.AddChild(detailScroll);

        _detailList = new VBoxContainer();
        _detailList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _detailList.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        detailScroll.AddChild(_detailList);
    }

    private void RefreshAll()
    {
        RefreshHeader();
        BuildSubjectList();
        RefreshDetail();
        RefreshActions();
    }

    private void RefreshHeader()
    {
        var country = CountryDatabase.Get(_playerId);
        var subjects = ReleasableSystem.SubjectsOf(_playerId);
        int subjectProvinces = 0;
        int subjectFactories = 0;
        foreach (var s in subjects)
        {
            subjectProvinces += s.ProvinceIds?.Length ?? 0;
            subjectFactories += s.FactoriesTaken;
        }

        _headerLabel.Text =
            $"{country?.Name ?? _playerId} ({_playerId})   •   " +
            $"SUBJECTS {subjects.Count}   •   HELD PROVINCES {subjectProvinces}   •   " +
            $"FACTORIES TAKEN {subjectFactories}";
    }

    private void BuildSubjectList()
    {
        _subjectIds.Clear();
        _subjectList.Clear();

        foreach (var r in ReleasableSystem.SubjectsOf(_playerId))
        {
            var country = CountryDatabase.Get(r.CountryId);
            int provinceCount = r.ProvinceIds?.Length ?? 0;
            _subjectIds.Add(r.CountryId);
            _subjectList.AddItem(
                $"{country?.Name ?? r.CountryId} ({r.CountryId}) — " +
                $"{(r.IsPuppet ? "PUPPET" : "ANNEXED")} by " +
                $"{CountryDatabase.Get(r.OverlordId)?.Name ?? r.OverlordId} — " +
                $"{provinceCount} il • {r.FactoriesTaken} fabrika");
        }

        if (_subjectIds.Count == 0)
        {
            _subjectList.AddItem("(no annexed or puppet nations under your control)");
            _emptyState.Visible = true;
        }
        else
        {
            _emptyState.Visible = false;
        }

        _selectedIndex = _subjectIds.Count > 0 ? 0 : -1;
        if (_selectedIndex == 0)
            _subjectList.Select(0);
    }

    private void RefreshDetail()
    {
        foreach (var child in _detailList.GetChildren())
            child.QueueFree();
        _detailTexts.Clear();

        var record = ReleasableSystem.Get(SelectedSubjectId);
        if (record == null)
        {
            AddDetailRow("(no nation selected)", GameTheme.TextMuted);
            _summaryLabel.Text = "Select a subject to see what you would return to it.";
            return;
        }

        var country = CountryDatabase.Get(record.CountryId);
        var overlord = CountryDatabase.Get(record.OverlordId);
        var provinces = record.ProvinceIds ?? Array.Empty<string>();

        // Still held provinces
        int stillHeld = 0;
        if (overlord != null)
            foreach (var p in provinces)
                if (string.Equals(ProvinceOwnershipSystem.GetProvinceOwner(p),
                                  overlord.Id, StringComparison.OrdinalIgnoreCase))
                    stillHeld++;

        _summaryLabel.Text =
            $"{country?.Name ?? record.CountryId} ({record.CountryId}) — " +
            $"{(record.IsPuppet ? "puppet" : "annexed")} under " +
            $"{overlord?.Name ?? record.OverlordId}: {provinces.Length} provinces recorded, " +
            $"{stillHeld} still held by you, {record.FactoriesTaken} factories taken.";

        AddDetailRow($"Type: {(record.IsPuppet ? "PUPPET — kept its own land" : "ANNEXED — land and industry absorbed")}",
            GameTheme.TextPrimary);
        AddDetailRow($"Recorded provinces: {provinces.Length}", GameTheme.TextPrimary);
        AddDetailRow($"Still held by you (returned): {stillHeld}", GameTheme.TextPrimary);
        AddDetailRow($"Factories taken: civ {record.CivTaken} / mil {record.MilTaken} / dock {record.DockTaken}",
            GameTheme.TextPrimary);
        AddDetailRow($"Annexation flags: {(country?.IsAnnexed == true ? "annexed by " + (country.OwnerId.Length > 0 ? country.OwnerId : "?") : "already independent")}",
            GameTheme.TextPrimary);

        // Culture map of recorded provinces
        var cultures = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in provinces)
        {
            string culture = ProvinceCultureSystem.CultureOf(p) ?? "";
            cultures[culture] = cultures.TryGetValue(culture, out var c) ? c + 1 : 1;
        }

        if (cultures.Count == 0)
        {
            AddDetailRow("Culture map: (no culture records)", GameTheme.TextMuted);
        }
        else
        {
            foreach (var kv in cultures.OrderByDescending(kv => kv.Value)
                                       .ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                string name = kv.Key.Length == 0 ? "(unknown)" : kv.Key;
                float share = provinces.Length > 0 ? (float)kv.Value / provinces.Length * 100f : 0f;
                AddDetailRow($"Culture: {name} — {kv.Value} il (%{share:N0})", GameTheme.TextMuted);
            }
        }
    }

    private void AddDetailRow(string text, Color color)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", GameTheme.SpaceS);

        var chip = Chip.CreateStatus("•", Chip.Variant.Default);
        chip.AddThemeColorOverride("font_color", GameTheme.Gold);
        chip.CustomMinimumSize = new Vector2(14, 0);
        row.AddChild(chip);

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
        _releaseBtn.Disabled = SelectedSubjectId.Length == 0;
    }

    private void OnSubjectSelected(long index)
    {
        if (index < 0 || index >= _subjectIds.Count) return;
        _selectedIndex = (int)index;
        GD.Print($"[ReleasablePanel] Bağımlı ülke seçildi: {_subjectIds[_selectedIndex]}");
        RefreshDetail();
        RefreshActions();
    }

    private void OnReleasePressed()
    {
        string subjectId = SelectedSubjectId;
        if (subjectId.Length == 0)
        {
            _feedbackLabel.Text = "no nation selected";
            _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            return;
        }

        var before = ReleasableSystem.Get(subjectId);
        string subjectName = before != null
            ? (CountryDatabase.Get(before.CountryId)?.Name ?? before.CountryId)
            : subjectId;

        var result = ReleasableSystem.Release(subjectId, _playerId);
        if (!result.Ok)
        {
            _feedbackLabel.Text = $"cannot release: {result.Reason}";
            _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.Red);
            GD.Print($"[ReleasablePanel] Serbest bırakma reddedildi — {_playerId} → {subjectId}: {result.Reason}");
            return;
        }

        _feedbackLabel.Text =
            $"{subjectName} is independent — {result.ProvincesReturned} provinces, " +
            $"c{result.CivReturned}/m{result.MilReturned}/t{result.DockReturned} factories returned";
        _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        GD.Print($"[ReleasablePanel] BAĞIMSIZ — {subjectName} ({subjectId}) " +
                 $"serbest bırakıldı (il={result.ProvincesReturned}).");

        // Release deletes the record so rebuild everything
        RefreshAll();
    }

    public override void Close()
    {
        GD.Print("ReleasablePanel: kapatılıyor");
        base.Close();
    }
}