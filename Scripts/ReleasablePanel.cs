using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// M45.1 — Bağımlı ülkeler paneli (oyuncu = ilhak eden / overlord tarafı).
///
/// M44.4'ün yazdığı `ReleasableSystem` mekaniği vardı ama oyunda ona ulaşan
/// hiçbir yol yoktu (yalnızca headless test `Release`'i çağırıyordu). Bu panel
/// o boşluğu kapatır:
///
///   • Sol sütun: oyuncunun ilhak ettiği / kukla yaptığı ülkeler — tür,
///     ilhak anındaki il sayısı ve alınan fabrika payı.
///   • Sağ sütun: seçili bağımlının ayrıntısı — hâlâ bizde duran iller,
///     fabrika tür dağılımı ve kayıtlı illerin kültür haritası.
///   • RELEASE NATION — `ReleasableSystem.Release` çağrılır (iller + fabrika
///     payı geri döner, bağımsızlık bayrakları kalkar, ülke AI devralır).
///
/// Panel kalıcı durum tutmaz: bağımlılık kaydı `ReleasableSystem`'te ve
/// Save/Load oradan bağlı (M44.5). Burada save yazılmaz.
///
/// Ölçü sınırı: 1152x648 ekrana sığacak şekilde 1080x600 tasarlandı.
/// </summary>
public partial class ReleasablePanel : Control
{
    // ===== Durum =====

    private string _playerId = "";

    private Label _headerLabel;     // overlord özeti
    private Label _summaryLabel;    // seçili bağımlı özeti
    private Label _feedbackLabel;   // release mesajı
    private Button _releaseBtn;

    private ItemList _subjectList;      // sol: bağımlı ülkeler
    private VBoxContainer _detailList;  // sağ: seçili bağımlı ayrıntısı

    // Bağımlı ülke kimlikleri (liste sırasıyla) — test ve seçim doğrulaması için.
    private readonly List<string> _subjectIds = new();

    // ItemList'in seçili satır indeksi (Godot 4'ta ItemList.Selected yok).
    private int _selectedIndex = -1;

    // Detay satırlarının metinleri (test/UI denetim anlık görüntüsü).
    private readonly List<string> _detailTexts = new();

    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    /// <summary>Panelin tasarım alt sınırı — 1152x648 ekrana sığma kontrolü için (test/UI denetim).</summary>
    public Vector2 DesignedSize => _panel?.CustomMinimumSize ?? Vector2.Zero;

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

    /// <summary>Panelin çalıştığı ülke (overlord / oyuncu).</summary>
    public string PlayerId => _playerId;

    /// <summary>
    /// ESC ile kapatma istendi mi? `_UnhandledInput` senkron set eder.
    /// </summary>
    public bool CloseRequested { get; private set; }

    // ===== Açılış =====

    /// <summary>Paneli parent altında açılacak şekilde kurar (ModalHost Layer=100).</summary>
    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        if (parent == null) return;

        var gm = GameManager.Instance;
        if (gm == null || !gm.HasPlayer) return;

        GD.Print($"[ReleasablePanel] Açılıyor — overlord={gm.PlayerCountryId}, " +
                 $"bağımlı kayıt={ReleasableSystem.SubjectsOf(gm.PlayerCountryId ?? "").Count}");

        var panel = new ReleasablePanel { _playerId = gm.PlayerCountryId ?? "" };
        ModalHost.AddUnder(parent, panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // duraklatılmış oyunda da kapatma çalışsın
        UITheme.ApplyTheme(this);

        BuildUi();
        RefreshAll();

        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);
        GD.Print("ReleasablePanel: hazır");
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

        var title = new Label { Text = "SUBJECTS & RELEASE" };
        title.AddThemeFontSizeOverride("font_size", 26);
        header.AddChild(title);

        _headerLabel = new Label { Text = "" };
        _headerLabel.AddThemeFontSizeOverride("font_size", 15);
        _headerLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
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
        UITheme.AddHoverScale(closeBtn);
        header.AddChild(closeBtn);

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

        BuildSubjectColumn(columns);
        BuildDetailColumn(columns);

        vbox.AddChild(new HSeparator());

        // ===== EYLEMLER =====
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(actions);

        _releaseBtn = new Button
        {
            Text = "RELEASE NATION",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(190, 36),
            TooltipText = "Give this nation its independence (provinces and factories return)"
        };
        _releaseBtn.Pressed += OnReleasePressed;
        UITheme.AddHoverScale(_releaseBtn);
        actions.AddChild(_releaseBtn);

        _feedbackLabel = new Label { Text = "" };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", 13);
        _feedbackLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Right;
        actions.AddChild(_feedbackLabel);
    }

    /// <summary>Sol sütun: ilhak edilmiş / kukla ülkeler.</summary>
    private void BuildSubjectColumn(HBoxContainer parent)
    {
        var left = new VBoxContainer();
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.AddThemeConstantOverride("separation", 6);
        parent.AddChild(left);

        var listHeader = new Label
        {
            Text = "YOUR SUBJECTS   (select a nation to see what you return)"
        };
        listHeader.AddThemeFontSizeOverride("font_size", 14);
        listHeader.AddThemeColorOverride("font_color", UITheme.TextMuted);
        left.AddChild(listHeader);

        _subjectList = new ItemList
        {
            CustomMinimumSize = new Vector2(460, 300),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MaxColumns = 1
        };
        _subjectList.ItemSelected += OnSubjectSelected;
        left.AddChild(_subjectList);

        var note = new Label
        {
            Text = "Releasing returns the provinces you still hold, the factory share taken at " +
                   "annexation, and hands the nation back to its own AI. Occupation pressure " +
                   "from a released people is gone with them."
        };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", UITheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(note);
    }

    /// <summary>Sağ sütun: seçili bağımlının ayrıntısı.</summary>
    private void BuildDetailColumn(HBoxContainer parent)
    {
        var right = new VBoxContainer();
        right.CustomMinimumSize = new Vector2(400, 0);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddThemeConstantOverride("separation", 6);
        parent.AddChild(right);

        var detailHeader = new Label
        {
            Text = "NATION DETAIL   (what comes back)"
        };
        detailHeader.AddThemeFontSizeOverride("font_size", 14);
        detailHeader.AddThemeColorOverride("font_color", UITheme.TextMuted);
        right.AddChild(detailHeader);

        var detailScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 300),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        right.AddChild(detailScroll);

        _detailList = new VBoxContainer();
        _detailList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _detailList.AddThemeConstantOverride("separation", 4);
        detailScroll.AddChild(_detailList);
    }

    // ===== Yenileme =====

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

    /// <summary>
    /// Oyuncunun ilhak ettiği / kukla yaptığı ülkeler. Bağımsızlık bayrağı
    /// kalkmış ya da kaydı silinmiş ülkeler listede görünmez.
    /// </summary>
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
            _subjectList.AddItem("(no annexed or puppet nations under your control)");

        _selectedIndex = _subjectIds.Count > 0 ? 0 : -1;
        if (_selectedIndex == 0)
            _subjectList.Select(0);
    }

    /// <summary>Seçili bağımlının ayrıntılarını tek tek çizer.</summary>
    private void RefreshDetail()
    {
        foreach (var child in _detailList.GetChildren())
            child.QueueFree();
        _detailTexts.Clear();

        var record = ReleasableSystem.Get(SelectedSubjectId);
        if (record == null)
        {
            AddDetailRow("(no nation selected)", UITheme.TextMuted);
            _summaryLabel.Text = "Select a subject to see what you would return to it.";
            return;
        }

        var country = CountryDatabase.Get(record.CountryId);
        var overlord = CountryDatabase.Get(record.OverlordId);
        var provinces = record.ProvinceIds ?? Array.Empty<string>();

        // Hâlâ bizde duran iller — yalnızca bunlar gerçekten devredilir.
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
            UITheme.TextMain);
        AddDetailRow($"Recorded provinces: {provinces.Length}", UITheme.TextMain);
        AddDetailRow($"Still held by you (returned): {stillHeld}", UITheme.TextMain);
        AddDetailRow($"Factories taken: civ {record.CivTaken} / mil {record.MilTaken} / dock {record.DockTaken}",
            UITheme.TextMain);
        AddDetailRow($"Annexation flags: {(country?.IsAnnexed == true ? "annexed by " + (country.OwnerId.Length > 0 ? country.OwnerId : "?") : "already independent")}",
            UITheme.TextMain);

        // Kayıtlı illerin kültür haritası — serbest bırakmanın kültür bedelini gösterir.
        var cultures = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in provinces)
        {
            string culture = ProvinceCultureSystem.CultureOf(p) ?? "";
            cultures[culture] = cultures.TryGetValue(culture, out var c) ? c + 1 : 1;
        }

        if (cultures.Count == 0)
        {
            AddDetailRow("Culture map: (no culture records)", UITheme.TextMuted);
        }
        else
        {
            foreach (var kv in cultures.OrderByDescending(kv => kv.Value)
                                       .ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                string name = kv.Key.Length == 0 ? "(unknown)" : kv.Key;
                float share = provinces.Length > 0 ? (float)kv.Value / provinces.Length * 100f : 0f;
                AddDetailRow($"Culture: {name} — {kv.Value} il (%{share:N0})", UITheme.TextMuted);
            }
        }
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
        row.AddChild(label);

        _detailList.AddChild(row);
        _detailTexts.Add(text);
    }

    private void RefreshActions()
    {
        _releaseBtn.Disabled = SelectedSubjectId.Length == 0;
    }

    // ===== Eylemler =====

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
            _feedbackLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
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
            _feedbackLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.40f, 0.40f));
            GD.Print($"[ReleasablePanel] Serbest bırakma reddedildi — {_playerId} → {subjectId}: {result.Reason}");
            return;
        }

        _feedbackLabel.Text =
            $"{subjectName} is independent — {result.ProvincesReturned} provinces, " +
            $"c{result.CivReturned}/m{result.MilReturned}/t{result.DockReturned} factories returned";
        _feedbackLabel.AddThemeColorOverride("font_color", UITheme.GoldBright);
        GD.Print($"[ReleasablePanel] BAĞIMSIZ — {subjectName} ({subjectId}) " +
                 $"serbest bırakıldı (il={result.ProvincesReturned}).");

        // Serbest bırakma kaydı sildiği için listeler baştan kurulur.
        RefreshAll();
    }

    // ===== Yardımcılar =====

    private void Close()
    {
        GD.Print("ReleasablePanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}
