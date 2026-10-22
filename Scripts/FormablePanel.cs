using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// M44.5 — Kurgu ülke paneli (oyuncu tarafı).
///
/// M44.1–M44.3'ün ürettiği veriyi tek ekranda toplar:
///   • Sol sütun: oyuncunun ebe kaynağı olduğu kurgu ülkeler — kültür, gereken
///     il sayısı, minimum kontrol oranı, PP ve gün maliyeti. Seçilen kurgu
///     ülkenin koşulları tek tek "sağlandı / sağlanmadı" olarak listelenir.
///   • Sağ sütun: oyuncunun il kültürü haritası özeti — her kültürün il sayısı
///     ve nüfus payı (M44.2 verisi). Hangi kültürün kurgu ülke kurmaya yettiği
///     buradan anlaşılır.
///
/// Etkileşim iki eyleme bağlıdır:
///   • FORM NATION — kurulumu başlatır (<see cref="FormableSystem.TryStart"/>).
///   • CANCEL — sürmekte olan kurulumu iptal eder (<see cref="FormableSystem.Cancel"/>).
///
/// Panel kalıcı durum tutmaz: kurulum durumu `FormableSystem`'te ve
/// Save/Load oradan bağlanır (M44.5). Bu yüzden burada save yazılmaz.
///
/// Ölçü sınırı: 1152x648 ekrana sığacak şekilde 1080x600 tasarlandı.
/// </summary>
public partial class FormablePanel : Control
{
    // ===== Durum =====

    private string _playerId = "";

    private Label _headerLabel;      // ülke + PP + nüfus özeti
    private Label _summaryLabel;     // seçili kurgu ülkenin kontrol haritası
    private Label _feedbackLabel;    // form/cancel mesajı
    private Button _formBtn;
    private Button _cancelBtn;

    private ItemList _formableList;  // sol: kurgu ülkeler
    private VBoxContainer _conditionList; // sol alt: koşullar
    private VBoxContainer _cultureList;   // sağ: kültür haritası özeti
    private Label _pendingLabel;

    // Kurgu ülke kimlikleri (liste sırasıyla) — test ve seçim doğrulaması için.
    private readonly List<string> _formableIds = new();

    // ItemList'in seçili satır indeksi (Godot 4'ta ItemList.Selected yok).
    private int _selectedIndex = -1;

    // Kültür satırlarında çizilen değerler (test/UI denetim anlık görüntüsü).
    private readonly List<string> _cultureLabels = new();
    private readonly List<int> _cultureCounts = new();

    // Koşul satırlarının "sağlandı" durumları (test/UI denetim).
    private readonly List<bool> _conditionMet = new();

    // Animasyon
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    /// <summary>Panelin tasarım alt sınırı — 1152x648 ekrana sığma kontrolü için (test/UI denetim).</summary>
    public Vector2 DesignedSize => _panel?.CustomMinimumSize ?? Vector2.Zero;

    /// <summary>Kurgu ülke satırı sayısı (test/UI denetim).</summary>
    public int FormableRowCount => _formableList?.ItemCount ?? 0;

    /// <summary>Kültür satırı sayısı (test/UI denetim).</summary>
    public int CultureRowCount => _cultureList?.GetChildCount() ?? 0;

    /// <summary>Kültür satırlarının etiketleri (test/UI denetim).</summary>
    public IReadOnlyList<string> CultureLabels => _cultureLabels;

    /// <summary>Kültür satırlarının il sayıları (test/UI denetim).</summary>
    public IReadOnlyList<int> CultureCounts => _cultureCounts;

    /// <summary>Koşul satırlarının sağlanma durumları (test/UI denetim).</summary>
    public IReadOnlyList<bool> ConditionMet => _conditionMet;

    /// <summary>FORM NATION butonu basılabiliyor mu? (test/UI denetim).</summary>
    public bool FormDisabled => _formBtn?.Disabled ?? true;

    /// <summary>CANCEL butonu basılabiliyor mu? (test/UI denetim).</summary>
    public bool CancelDisabled => _cancelBtn?.Disabled ?? true;

    /// <summary>Son işlem mesajı (test/UI denetim).</summary>
    public string FeedbackText => _feedbackLabel?.Text ?? "";

    /// <summary>Seçili kurgu ülke kimliği (test/UI denetim).</summary>
    public string SelectedFormableId =>
        _formableIds.Count > 0 && _formableList != null &&
        _selectedIndex >= 0 && _selectedIndex < _formableIds.Count
            ? _formableIds[_selectedIndex]
            : "";

    /// <summary>Panelin çalıştığı ülke (oyuncu).</summary>
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

        GD.Print($"[FormablePanel] Açılıyor — ülke={gm.PlayerCountryId}, " +
                 $"formable tanım={FormableDataSystem.FormableCount}");

        var panel = new FormablePanel { _playerId = gm.PlayerCountryId ?? "" };
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
        GD.Print("FormablePanel: hazır");
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

        var title = new Label { Text = "FORMABLE NATIONS" };
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

        BuildFormableColumn(columns);
        BuildCultureColumn(columns);

        vbox.AddChild(new HSeparator());

        // ===== EYLEMLER =====
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(actions);

        _formBtn = new Button
        {
            Text = "FORM NATION",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(170, 36),
            TooltipText = "Start forming the selected nation"
        };
        _formBtn.Pressed += OnFormPressed;
        UITheme.AddHoverScale(_formBtn);
        actions.AddChild(_formBtn);

        _cancelBtn = new Button
        {
            Text = "CANCEL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(120, 36),
            TooltipText = "Cancel the formation in progress"
        };
        _cancelBtn.Pressed += OnCancelPressed;
        UITheme.AddHoverScale(_cancelBtn);
        actions.AddChild(_cancelBtn);

        _pendingLabel = new Label { Text = "" };
        _pendingLabel.AddThemeFontSizeOverride("font_size", 14);
        _pendingLabel.AddThemeColorOverride("font_color", UITheme.GoldBright);
        actions.AddChild(_pendingLabel);

        _feedbackLabel = new Label { Text = "" };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", 13);
        _feedbackLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Right;
        actions.AddChild(_feedbackLabel);
    }

    /// <summary>Sol sütun: kurgu ülke listesi + seçilenin koşulları.</summary>
    private void BuildFormableColumn(HBoxContainer parent)
    {
        var left = new VBoxContainer();
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.AddThemeConstantOverride("separation", 6);
        parent.AddChild(left);

        var listHeader = new Label
        {
            Text = "AVAILABLE TO FOUND   (select a nation to see its conditions)"
        };
        listHeader.AddThemeFontSizeOverride("font_size", 14);
        listHeader.AddThemeColorOverride("font_color", UITheme.TextMuted);
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
        condHeader.AddThemeFontSizeOverride("font_size", 14);
        condHeader.AddThemeColorOverride("font_color", UITheme.TextMuted);
        left.AddChild(condHeader);

        var condScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 120),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        left.AddChild(condScroll);

        _conditionList = new VBoxContainer();
        _conditionList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _conditionList.AddThemeConstantOverride("separation", 3);
        condScroll.AddChild(_conditionList);

        var note = new Label
        {
            Text = "Political power is spent when the formation finishes, not when it starts, " +
                   "so a formation is never lost halfway if your power runs low."
        };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", UITheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(note);
    }

    /// <summary>Sağ sütun: oyuncunun il kültürü haritası özeti.</summary>
    private void BuildCultureColumn(HBoxContainer parent)
    {
        var right = new VBoxContainer();
        right.CustomMinimumSize = new Vector2(400, 0);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddThemeConstantOverride("separation", 6);
        parent.AddChild(right);

        var cultureHeader = new Label
        {
            Text = "CULTURE MAP   (your provinces by culture)"
        };
        cultureHeader.AddThemeFontSizeOverride("font_size", 14);
        cultureHeader.AddThemeColorOverride("font_color", UITheme.TextMuted);
        right.AddChild(cultureHeader);

        var cultureScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 300),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        right.AddChild(cultureScroll);

        _cultureList = new VBoxContainer();
        _cultureList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _cultureList.AddThemeConstantOverride("separation", 4);
        cultureScroll.AddChild(_cultureList);
    }

    // ===== Yenileme =====

    private void RefreshAll()
    {
        RefreshHeader();
        BuildFormableList();
        BuildCultureList();
        RefreshConditions();
        RefreshActions();
    }

    private void RefreshHeader()
    {
        var country = CountryDatabase.Get(_playerId);
        int provinceCount = ProvinceOwnershipSystem.GetProvincesByOwner(_playerId).Count;
        long population = ProvinceCultureSystem.TotalPopulation(_playerId);
        _headerLabel.Text = $"{country?.Name ?? _playerId} ({_playerId})   •   " +
                            $"PROVINCES {provinceCount}   •   POPULATION {population:N0}   •   " +
                            $"PP {country?.PoliticalPower ?? 0:N0}";
    }

    /// <summary>
    /// Oyuncunun ebe kaynağı olduğu kurgu ülkeler. İl sayısı ve kültür
    /// sağlansa bile listelenir — koşullar ayrıca gösterilir.
    /// </summary>
    private void BuildFormableList()
    {
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
    }

    /// <summary>Oyuncunun illerini kültüre göre gruplar (il sayısı + nüfus payı).</summary>
    private void BuildCultureList()
    {
        foreach (var child in _cultureList.GetChildren())
            child.QueueFree();
        _cultureLabels.Clear();
        _cultureCounts.Clear();

        var records = ProvinceCultureSystem.ForCountry(_playerId);
        long totalPopulation = records.Sum(r => r.Population);
        int totalProvinces = records.Count;

        if (totalProvinces == 0)
        {
            var empty = new Label { Text = "(no provinces)" };
            empty.AddThemeFontSizeOverride("font_size", 14);
            empty.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _cultureList.AddChild(empty);
            return;
        }

        // Kültüre göre grupla: il sayısı ve nüfus toplamı
        var groups = records
            .GroupBy(r => r.Culture ?? "", StringComparer.Ordinal)
            .Select(g => new
            {
                Culture = g.Key,
                Count = g.Count(),
                Population = g.Sum(r => r.Population)
            })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Culture, StringComparer.Ordinal)
            .ToList();

        foreach (var g in groups)
        {
            float share = totalPopulation > 0
                ? (float)g.Population / totalPopulation * 100f
                : (float)g.Count / totalProvinces * 100f;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var name = new Label
            {
                Text = g.Culture.Length == 0 ? "(unknown)" : g.Culture,
                CustomMinimumSize = new Vector2(120, 0)
            };
            name.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(name);

            var bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = Mathf.Clamp(share, 0f, 100f),
                CustomMinimumSize = new Vector2(140, 16),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            bar.AddThemeColorOverride("fill_color", CultureColor(g.Culture));
            row.AddChild(bar);

            var counts = new Label
            {
                Text = $"{g.Count} il • {g.Population:N0} (%{share:N0})",
                CustomMinimumSize = new Vector2(150, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            counts.AddThemeFontSizeOverride("font_size", 13);
            counts.AddThemeColorOverride("font_color", UITheme.TextMuted);
            row.AddChild(counts);

            _cultureList.AddChild(row);
            _cultureLabels.Add(g.Culture);
            _cultureCounts.Add(g.Count);
        }
    }

    /// <summary>Seçili kurgu ülkenin koşullarını tek tek çizer.</summary>
    private void RefreshConditions()
    {
        foreach (var child in _conditionList.GetChildren())
            child.QueueFree();
        _conditionMet.Clear();

        string formableId = SelectedFormableId;
        if (formableId.Length == 0)
        {
            var empty = new Label { Text = "(no nation selected)" };
            empty.AddThemeFontSizeOverride("font_size", 14);
            empty.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _conditionList.AddChild(empty);
            return;
        }

        var def = FormableDataSystem.Get(formableId);
        if (def == null) return;

        // Seçili kurgu ülkeyi kurma hakkı: oyuncu ebe kaynak olmayabilir (henüz
        // koşullar listelenir ama ebe kaynak madde kırmızı görünür).
        var items = FormableSystem.EvaluateConditions(_playerId, formableId);

        foreach (var item in items)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var mark = new Label { Text = item.Met ? "✓" : "✗" };
            mark.AddThemeFontSizeOverride("font_size", 15);
            mark.AddThemeColorOverride("font_color",
                item.Met ? new Color(0.30f, 0.72f, 0.40f) : new Color(0.85f, 0.40f, 0.40f));
            mark.CustomMinimumSize = new Vector2(18, 0);
            row.AddChild(mark);

            var label = new Label { Text = $"{item.Label}: {item.Detail}" };
            label.AddThemeFontSizeOverride("font_size", 14);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.AddChild(label);

            _conditionList.AddChild(row);
            _conditionMet.Add(item.Met);
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

    // ===== Eylemler =====

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
            _feedbackLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            return;
        }

        bool started = FormableSystem.TryStart(_playerId, formableId, out string reason);
        if (!started)
        {
            _feedbackLabel.Text = $"cannot form: {reason}";
            _feedbackLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.40f, 0.40f));
            GD.Print($"[FormablePanel] Kurma reddedildi — {_playerId} → {formableId}: {reason}");
            return;
        }

        var def = FormableDataSystem.Get(formableId);
        _feedbackLabel.Text = $"forming {def?.Name ?? formableId} ({def?.DaysRequired ?? 0} days)";
        _feedbackLabel.AddThemeColorOverride("font_color", UITheme.GoldBright);
        RefreshConditions();
        RefreshActions();
    }

    private void OnCancelPressed()
    {
        bool cancelled = FormableSystem.Cancel(_playerId);
        _feedbackLabel.Text = cancelled ? "formation cancelled" : "no formation in progress";
        _feedbackLabel.AddThemeColorOverride("font_color",
            cancelled ? UITheme.GoldBright : UITheme.TextMuted);
        RefreshActions();
    }

    // ===== Yardımcılar =====

    /// <summary>Kültür rengi: kimliğin hash'inden sabit (aynı kültür aynı renk).</summary>
    private static Color CultureColor(string culture)
    {
        unchecked
        {
            const uint FnvOffset = 2166136261u;
            const uint FnvPrime = 16777619u;
            uint hash = FnvOffset;
            if (!string.IsNullOrEmpty(culture))
                foreach (char ch in culture)
                {
                    hash ^= (byte)(ch & 0xFF);
                    hash *= FnvPrime;
                    hash ^= (byte)(ch >> 8);
                    hash *= FnvPrime;
                }

            float hue = (hash % 360) / 360f;
            return Color.FromHsv(hue, 0.45f, 0.85f);
        }
    }

    private void Close()
    {
        GD.Print("FormablePanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}
