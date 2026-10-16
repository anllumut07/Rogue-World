using Godot;
using System;
using System.Linq;

/// <summary>
/// Teknoloji panel (M5): kategori/era filtresi, teknoloji listesi (durum + START butonu),
/// aktif araştırma slotları, progress bar. HUD RESEARCH butonundan açılır.
/// </summary>
public partial class ResearchPanel : Control
{
    // UI
    private VBoxContainer _activeBox;     // aktif araştırma satırları
    private VBoxContainer _techBox;       // teknoloji listesi
    private OptionButton _categoryFilter;
    private OptionButton _eraFilter;
    private Label _slotInfoLabel;

    // Animasyon
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    private const string ScenePath = "res://Scenes/research_panel.tscn";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[ResearchPanel] OpenUnder çağrıldı — parent={parent?.GetType().Name ?? "null"}");

        ResearchPanel panel = null;

        // 1) Sahne dosyası projede var mı?
        if (ResourceLoader.Exists(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            if (scene != null)
            {
                try
                {
                    panel = scene.Instantiate<ResearchPanel>();
                    GD.Print($"[ResearchPanel] Sahne yüklendi ve instantiate edildi: {ScenePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[ResearchPanel] Sahne instantiate hatası ({ScenePath}): {e.Message} — KODDAN oluşturuluyor");
                }
            }
            else
            {
                GD.PrintErr($"[ResearchPanel] GD.Load null döndürdü: {ScenePath} — dosya bozuk/parse edilemiyor olabilir. KODDAN oluşturuluyor.");
            }
        }
        else
        {
            GD.PrintErr($"[ResearchPanel] Sahne YOK: {ScenePath} (ResourceLoader.Exists=false) — KODDAN oluşturuluyor.");
        }

        // 2) Yedek yol: koddan oluştur
        panel ??= new ResearchPanel();

        parent.AddChild(panel);
        GD.Print($"[ResearchPanel] Sahneye eklendi: Visible={panel.Visible}, Size={panel.Size}, Pos={panel.Position}, parent={panel.GetParent()?.GetType().Name ?? "null"}");
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        UITheme.ApplyTheme(this);

        BuildUi();
        ConnectSignals();
        RefreshAll();

        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        GD.Print("ResearchPanel: hazır");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void BuildUi()
    {
        _backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.75f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(800, 600) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vbox);

        // --- Başlık + Slot bilgisi + CLOSE ---
        var headerRow = new HBoxContainer();
        vbox.AddChild(headerRow);

        var title = new Label { Text = "RESEARCH" };
        title.AddThemeFontSizeOverride("font_size", 30);
        headerRow.AddChild(title);

        headerRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var slotInfo = new Label();
        _slotInfoLabel = slotInfo;
        slotInfo.AddThemeFontSizeOverride("font_size", 14);
        slotInfo.AddThemeColorOverride("font_color", UITheme.TextMuted);
        headerRow.AddChild(slotInfo);

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        headerRow.AddChild(closeBtn);

        vbox.AddChild(new HSeparator());

        // --- Aktif araştırma slotları ---
        var activeTitle = new Label { Text = "ACTIVE RESEARCH" };
        activeTitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(activeTitle);

        var scrollActive = new ScrollContainer();
        scrollActive.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(scrollActive);

        _activeBox = new VBoxContainer();
        _activeBox.AddThemeConstantOverride("separation", 8);
        scrollActive.AddChild(_activeBox);

        vbox.AddChild(new HSeparator());

        // --- Filtreler ---
        var filterRow = new HBoxContainer();
        filterRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(filterRow);

        var catLabel = new Label { Text = "Category:", CustomMinimumSize = new Vector2(80, 0) };
        filterRow.AddChild(catLabel);

        _categoryFilter = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        _categoryFilter.AddItem("All Categories");
        foreach (var c in TechnologySystem.GetCategories())
            _categoryFilter.AddItem(c);
        _categoryFilter.ItemSelected += (idx) => RefreshTechList();
        filterRow.AddChild(_categoryFilter);

        var eraLabel = new Label { Text = "Era:", CustomMinimumSize = new Vector2(60, 0) };
        filterRow.AddChild(eraLabel);

        _eraFilter = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        _eraFilter.AddItem("All Eras");
        // M17.4: era listesi yüklü teknolojilerden dinamik (1936: 1936-1943, modern: 1990-2020)
        foreach (int era in TechnologySystem.All.Values.Select(t => t.Era).Distinct().OrderBy(e => e))
            _eraFilter.AddItem(era.ToString());
        _eraFilter.ItemSelected += (idx) => RefreshTechList();
        filterRow.AddChild(_eraFilter);

        vbox.AddChild(new HSeparator());

        // --- Teknoloji listesi ---
        var techTitle = new Label { Text = "TECHNOLOGY TREE" };
        techTitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(techTitle);

        var scrollTech = new ScrollContainer();
        scrollTech.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(scrollTech);

        _techBox = new VBoxContainer();
        _techBox.AddThemeConstantOverride("separation", 4);
        scrollTech.AddChild(_techBox);
    }

    private void ConnectSignals()
    {
        // Günlük tick + araştırma başlat/bitir sonrası yenileme
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged += OnDataChanged;
    }

    private void OnDataChanged()
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        UpdateSlotInfo();
        RefreshActiveResearch();
        RefreshTechList();
    }

    private void UpdateSlotInfo()
    {
        if (_slotInfoLabel == null || !GodotObject.IsInstanceValid(_slotInfoLabel)) return;

        int used = TechnologySystem.UsedSlots;
        int total = TechnologySystem.ResearchSlots;
        _slotInfoLabel.Text = $"Slots: {used}/{total}  |  Speed: {TechnologySystem.ResearchSpeedMult:F2}x";
    }

    private void RefreshActiveResearch()
    {
        foreach (Node child in _activeBox.GetChildren())
        {
            _activeBox.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var ar in TechnologySystem.ActiveResearchList)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);

            var name = new Label { Text = ar.Tech.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            name.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(name);

            var progress = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = ar.ProgressPercent,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(250, 20)
            };
            row.AddChild(progress);

            var detail = new Label { Text = $"{ar.ProgressDays:F1}/{ar.Tech.Cost} days ({ar.DaysRemaining:F1} left)" };
            detail.AddThemeFontSizeOverride("font_size", 12);
            detail.AddThemeColorOverride("font_color", UITheme.TextMuted);
            detail.CustomMinimumSize = new Vector2(180, 0);
            row.AddChild(detail);

            var cancel = new Button { Text = "X", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 28) };
            cancel.Pressed += () => TechnologySystem.CancelResearch(ar.Tech.Id);
            UITheme.AddHoverScale(cancel);
            row.AddChild(cancel);

            _activeBox.AddChild(row);
        }

        if (TechnologySystem.ActiveResearchList.Count == 0)
        {
            var none = new Label { Text = "No active research" };
            none.AddThemeFontSizeOverride("font_size", 14);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _activeBox.AddChild(none);
        }
    }

    private void RefreshTechList()
    {
        foreach (Node child in _techBox.GetChildren())
        {
            _techBox.RemoveChild(child);
            child.QueueFree();
        }

        string cat = _categoryFilter.GetItemText(_categoryFilter.Selected);
        string era = _eraFilter.GetItemText(_eraFilter.Selected);

        foreach (var kv in TechnologySystem.All)
        {
            var t = kv.Value;

            // Filtre
            if (cat != "All Categories" && t.Category != cat) continue;
            if (era != "All Eras" && t.Era.ToString() != era) continue;

            var status = TechnologySystem.GetStatus(t.Id);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            // Durum ikonu
            var statusLabel = new Label { CustomMinimumSize = new Vector2(24, 0) };
            switch (status)
            {
                case TechnologySystem.ResearchStatus.Researched:
                    statusLabel.Text = "✓";
                    statusLabel.AddThemeColorOverride("font_color", Colors.Green);
                    break;
                case TechnologySystem.ResearchStatus.InProgress:
                    statusLabel.Text = "⟳";
                    statusLabel.AddThemeColorOverride("font_color", Colors.Orange);
                    break;
                case TechnologySystem.ResearchStatus.Available:
                    statusLabel.Text = "○";
                    statusLabel.AddThemeColorOverride("font_color", UITheme.Gold);
                    break;
                default: // MissingPrereq
                    statusLabel.Text = "🔒";
                    statusLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
                    break;
            }
            row.AddChild(statusLabel);

            // İsim + Era + Maliyet
            var name = new Label { Text = $"{t.Name}  [{t.Era}]  ({t.Cost} days)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            name.AddThemeFontSizeOverride("font_size", 13);
            name.TooltipText = t.Description;
            row.AddChild(name);

            // Etkiler özeti
            var effects = string.Join("; ", t.Effects);
            if (effects.Length > 80) effects = effects.Substring(0, 77) + "...";
            var effLabel = new Label { Text = effects };
            effLabel.AddThemeFontSizeOverride("font_size", 11);
            effLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            effLabel.CustomMinimumSize = new Vector2(250, 0);
            row.AddChild(effLabel);

            // START butonu
            var startBtn = new Button
            {
                Text = status == TechnologySystem.ResearchStatus.Available ? "START" : "—",
                FocusMode = Control.FocusModeEnum.None,
                Disabled = status != TechnologySystem.ResearchStatus.Available,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            startBtn.TooltipText = status switch
            {
                TechnologySystem.ResearchStatus.Available => "Start research",
                TechnologySystem.ResearchStatus.Researched => "Already researched",
                TechnologySystem.ResearchStatus.InProgress => "Already in progress",
                _ => "Prerequisites not met"
            };
            startBtn.Pressed += () => { TechnologySystem.StartResearch(t.Id); RefreshTechList(); };
            UITheme.AddHoverScale(startBtn);
            row.AddChild(startBtn);

            // Muted renk: araştırılmamış/önkoşul eksik
            if (status == TechnologySystem.ResearchStatus.MissingPrereq)
            {
                name.AddThemeColorOverride("font_color", UITheme.TextMuted);
                effLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            }
            else if (status == TechnologySystem.ResearchStatus.Researched)
            {
                name.AddThemeColorOverride("font_color", Colors.Green);
            }

            _techBox.AddChild(row);
        }
    }

    private void Close()
    {
        GD.Print("ResearchPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;

        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}