using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Kararlar paneli (M10.2): kullanılabilir kararları listeler, aktif kararları
/// progress bar ile gösterir, başlat/iptal butonları. HUD DECISIONS butonundan açılır.
/// 1152x648'de sığar (900x500). Backdrop Stop ile harita girişi engellenir.
/// </summary>
public partial class DecisionPanel : Control
{
    private const string ScenePath = "res://Scenes/decision_panel.tscn";

    public static bool IsOpen { get; private set; }

    // UI
    private Label _titleLabel;
    private VBoxContainer _availableContainer;
    private VBoxContainer _activeContainer;
    private Label _noDecisionsLabel;
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static void OpenUnder(Node parent)
    {
        if (IsOpen)
        {
            GD.Print("[DecisionPanel] OpenUnder: zaten açık, tekrar açılmıyor");
            return;
        }

        GD.Print($"[DecisionPanel] OpenUnder çağrıldı — parent={parent?.GetType().Name ?? "null"}");

        DecisionPanel panel = null;

        // 1) Sahne dosyası projede var mı? (ResourceLoader.Exists)
        if (ResourceLoader.Exists(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            if (scene != null)
            {
                try
                {
                    panel = scene.Instantiate<DecisionPanel>();
                    GD.Print($"[DecisionPanel] Sahne yüklendi ve instantiate edildi: {ScenePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[DecisionPanel] Sahne instantiate hatası ({ScenePath}): {e.Message} — KODDAN oluşturuluyor");
                }
            }
            else
            {
                // GD.Load null dönerse sebep genelde .tscn parse hatasıdır —
                // Godot kendi konsoluna ayrıntıyı yazar, biz de yolumuzu söyleriz.
                GD.PrintErr($"[DecisionPanel] GD.Load null döndürdü: {ScenePath} — dosya bozuk/parse edilemiyor olabilir. KODDAN oluşturuluyor.");
            }
        }
        else
        {
            GD.PrintErr($"[DecisionPanel] Sahne YOK: {ScenePath} (ResourceLoader.Exists=false) — KODDAN oluşturuluyor.");
        }

        // 2) Yedek yol: sahneye bağımlı değil — koddan yeni panel oluştur.
        //    _Ready içindeki SetAnchorsPreset(FullRect) kökü tam ekran yapar.
        panel ??= new DecisionPanel();

        ModalHost.AddUnder(parent, panel);
        GD.Print($"[DecisionPanel] Sahneye eklendi: Visible={panel.Visible}, Size={panel.Size}, Pos={panel.Position}, parent={panel.GetParent()?.GetType().Name ?? "null"}");
    }

    public override void _Ready()
    {
        // Güvence: kök Control FullRect olsun. Sahne anchor'ları eksik/bozuk olsa
        // veya panel KODDAN yaratılsa bile backdrop tam ekran olur (0x0 kalmasın).
        SetAnchorsPreset(Control.LayoutPreset.FullRect);

        ProcessMode = ProcessModeEnum.Always; // pause edilse bile çalışsın
        UITheme.ApplyTheme(this);

        BuildUi();
        ConnectSignals();
        RefreshAll();

        // === TEŞHİS LOGU (istemci isteği): panel açılış durumunu yaz ===
        GD.Print($"[DecisionPanel] _Ready: root Visible={Visible}, Pos={Position}, Size={Size}, ZIndex={ZIndex}, MouseFilter={MouseFilter}, parent={GetParent()?.GetType().Name ?? "null"}");
        GD.Print($"[DecisionPanel] _Ready: backdrop Visible={_backdrop.Visible}, Pos={_backdrop.Position}, Size={_backdrop.Size}, MouseFilter={_backdrop.MouseFilter}");
        GD.Print($"[DecisionPanel] _Ready: panel Visible={_panel.Visible}, Pos={_panel.Position}, Size={_panel.Size}");

        IsOpen = true;
        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        GD.Print("DecisionPanel: hazır");
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
        // Backdrop: Stop — harita girişi engellenir
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

        // Panel boyutu: 1152x648 dahil
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(900, 500) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        margin.AddChild(vbox);

        // --- Header ---
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(headerRow);

        _titleLabel = new Label { Text = "DECISIONS" };
        _titleLabel.AddThemeFontSizeOverride("font_size", 26);
        headerRow.AddChild(_titleLabel);

        headerRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        headerRow.AddChild(closeBtn);

        vbox.AddChild(new HSeparator());

        // --- İki sütun: Kullanılabilir | Aktif ---
        var columns = new HBoxContainer();
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        columns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddThemeConstantOverride("separation", 24);
        vbox.AddChild(columns);

        // SOL: Kullanılabilir kararlar
        var leftCol = new VBoxContainer();
        leftCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftCol.AddThemeConstantOverride("separation", 8);
        columns.AddChild(leftCol);

        var leftHeader = new Label { Text = "AVAILABLE DECISIONS" };
        leftHeader.AddThemeFontSizeOverride("font_size", 16);
        leftHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        leftCol.AddChild(leftHeader);

        var leftScroll = new ScrollContainer();
        leftScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        leftScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftCol.AddChild(leftScroll);

        _availableContainer = new VBoxContainer();
        _availableContainer.AddThemeConstantOverride("separation", 8);
        _availableContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftScroll.AddChild(_availableContainer);

        _noDecisionsLabel = new Label
        {
            Text = "No decisions available.\nRequirements not met or on cooldown.",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _noDecisionsLabel.AddThemeFontSizeOverride("font_size", 15);
        _noDecisionsLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _noDecisionsLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _noDecisionsLabel.Visible = false;
        _availableContainer.AddChild(_noDecisionsLabel);

        // SAĞ: Aktif kararlar (yürütülüyor)
        var rightCol = new VBoxContainer();
        rightCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightCol.CustomMinimumSize = new Vector2(300, 0);
        rightCol.AddThemeConstantOverride("separation", 8);
        columns.AddChild(rightCol);

        var rightHeader = new Label { Text = "ACTIVE DECISIONS" };
        rightHeader.AddThemeFontSizeOverride("font_size", 16);
        rightHeader.AddThemeColorOverride("font_color", Colors.Orange);
        rightCol.AddChild(rightHeader);

        var rightScroll = new ScrollContainer();
        rightScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        rightScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightCol.AddChild(rightScroll);

        _activeContainer = new VBoxContainer();
        _activeContainer.AddThemeConstantOverride("separation", 8);
        _activeContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightScroll.AddChild(_activeContainer);
    }

    private void ConnectSignals()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged += OnDataChanged;
    }

    private void OnDataChanged()
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshAvailable();
        RefreshActive();
    }

    private void RefreshAvailable()
    {
        // Eski butonları temizle (noDecisionsLabel hariç)
        foreach (var child in _availableContainer.GetChildren())
        {
            if (child == _noDecisionsLabel) continue;
            _availableContainer.RemoveChild(child);
            child.QueueFree();
        }

        var decisions = DecisionSystem.GetAvailableDecisions();

        if (decisions.Count == 0)
        {
            _noDecisionsLabel.Visible = true;
            return;
        }

        _noDecisionsLabel.Visible = false;

        foreach (var d in decisions)
        {
            var btn = CreateDecisionButton(d);
            _availableContainer.AddChild(btn);
        }
    }

    private Button CreateDecisionButton(DecisionSystem.Decision d)
    {
        var btn = new Button
        {
            CustomMinimumSize = new Vector2(0, 60),
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = BuildTooltip(d)
        };

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 12);
        btn.AddChild(hbox);

        // Sol: isim + maliyet/gün
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 2);
        hbox.AddChild(left);

        var nameLabel = new Label { Text = d.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.HorizontalAlignment = HorizontalAlignment.Left;
        left.AddChild(nameLabel);

        var details = new Label();
        var parts = new List<string>();
        if (d.CostPP > 0) parts.Add($"{d.CostPP} PP");
        if (d.CostDays > 0) parts.Add($"{d.CostDays} days");
        if (d.CooldownDays > 0) parts.Add($"Cooldown: {d.CooldownDays} days");
        details.Text = string.Join("  |  ", parts);
        details.AddThemeFontSizeOverride("font_size", 12);
        details.AddThemeColorOverride("font_color", UITheme.TextMuted);
        details.HorizontalAlignment = HorizontalAlignment.Left;
        left.AddChild(details);

        hbox.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        // Sağ: START butonu
        var startBtn = new Button
        {
            Text = "START",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(80, 36)
        };
        startBtn.Pressed += () => OnStartDecision(d.Id);
        UITheme.AddHoverScale(startBtn);
        hbox.AddChild(startBtn);

        UITheme.AddHoverScale(btn, 1.02f);
        return btn;
    }

    private void RefreshActive()
    {
        foreach (var child in _activeContainer.GetChildren())
        {
            _activeContainer.RemoveChild(child);
            child.QueueFree();
        }

        var active = DecisionSystem.GetActiveDecisions();

        if (active.Count == 0)
        {
            var noneLabel = new Label
            {
                Text = "No active decisions",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            noneLabel.AddThemeFontSizeOverride("font_size", 14);
            noneLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            noneLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _activeContainer.AddChild(noneLabel);
            return;
        }

        foreach (var info in active)
        {
            var item = CreateActiveItem(info);
            _activeContainer.AddChild(item);
        }
    }

    private Control CreateActiveItem(DecisionSystem.ActiveDecisionInfo info)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 4);
        container.AddThemeStyleboxOverride("normal", MakeBox(new Color("#1A1A2E"), new Color("#3A3A5A")));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        container.AddChild(header);

        var nameLabel = new Label { Text = info.Decision.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", 14);
        nameLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLabel);

        var cancelBtn = new Button
        {
            Text = "CANCEL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(70, 28)
        };
        cancelBtn.AddThemeFontSizeOverride("font_size", 11);
        cancelBtn.Pressed += () => OnCancelDecision(info.Decision.Id);
        UITheme.AddHoverScale(cancelBtn, 1.05f);
        header.AddChild(cancelBtn);

        // Progress bar
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100,
            Value = info.Progress * 100f,
            CustomMinimumSize = new Vector2(0, 16)
        };
        bar.AddThemeColorOverride("fill_color", Colors.Orange);
        container.AddChild(bar);

        var progressLabel = new Label
        {
            Text = $"Day {info.DaysElapsed} / {info.TotalDays}"
        };
        progressLabel.AddThemeFontSizeOverride("font_size", 12);
        progressLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        progressLabel.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(progressLabel);

        return container;
    }

    private static StyleBoxFlat MakeBox(Color bg, Color border)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        b.SetBorderWidthAll(1);
        b.SetCornerRadiusAll(4);
        b.ContentMarginLeft = 8;
        b.ContentMarginRight = 8;
        b.ContentMarginTop = 6;
        b.ContentMarginBottom = 6;
        return b;
    }

    private void OnStartDecision(string decisionId)
    {
        if (DecisionSystem.StartDecision(decisionId))
        {
            RefreshAll();
        }
    }

    private void OnCancelDecision(string decisionId)
    {
        DecisionSystem.CancelDecision(decisionId);
        RefreshAll();
    }

    private void Close()
    {
        GD.Print("DecisionPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;

        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }

    private static string BuildTooltip(DecisionSystem.Decision d)
    {
        var lines = new List<string>();
        lines.Add(d.Description);
        lines.Add("");
        lines.Add("Cost:");
        if (d.CostPP > 0) lines.Add($"  {d.CostPP} Political Power");
        if (d.CostDays > 0) lines.Add($"  {d.CostDays} days to complete");
        if (d.CooldownDays > 0) lines.Add($"  Cooldown: {d.CooldownDays} days");

        var c = d.Condition;
        if (c.MinStability > 0 || c.MaxStability < 100 ||
            c.MinWarSupport > 0 || c.MaxWarSupport < 100 ||
            c.MinPP > 0 || c.MaxWorldTension < 100 ||
            !string.IsNullOrEmpty(c.RequiredFocus) ||
            !string.IsNullOrEmpty(c.RequiredIdeology) ||
            c.IsAtWar)
        {
            lines.Add("");
            lines.Add("Requirements:");
            if (c.MinStability > 0) lines.Add($"  Stability ≥ {c.MinStability}%");
            if (c.MaxStability < 100) lines.Add($"  Stability ≤ {c.MaxStability}%");
            if (c.MinWarSupport > 0) lines.Add($"  War Support ≥ {c.MinWarSupport}%");
            if (c.MinPP > 0) lines.Add($"  PP ≥ {c.MinPP}");
            if (c.MaxWorldTension < 100) lines.Add($"  World Tension ≤ {c.MaxWorldTension}%");
            if (!string.IsNullOrEmpty(c.RequiredFocus)) lines.Add($"  Focus completed: {c.RequiredFocus}");
            if (!string.IsNullOrEmpty(c.RequiredNotFocus)) lines.Add($"  Focus NOT completed: {c.RequiredNotFocus}");
            if (!string.IsNullOrEmpty(c.RequiredIdeology)) lines.Add($"  Ideology: {c.RequiredIdeology}");
            if (c.IsAtWar) lines.Add("  Must be at war");
        }

        var e = d.Effects;
        lines.Add("");
        lines.Add("Effects:");
        if (e.PoliticalPower != 0) lines.Add($"  PP: {e.PoliticalPower:+#;-#}");
        if (e.Stability != 0) lines.Add($"  Stability: {e.Stability:+#;-#}%");
        if (e.WarSupport != 0) lines.Add($"  War Support: {e.WarSupport:+#;-#}%");
        if (e.CivFactories != 0) lines.Add($"  Civilian Factories: {e.CivFactories:+#;-#}");
        if (e.MilFactories != 0) lines.Add($"  Military Factories: {e.MilFactories:+#;-#}");
        if (e.Dockyards != 0) lines.Add($"  Dockyards: {e.Dockyards:+#;-#}");
        if (e.ResearchSlots != 0) lines.Add($"  Research Slots: {e.ResearchSlots:+#;-#}");
        if (e.Manpower != 0) lines.Add($"  Manpower: {FormatSigned(e.Manpower)}");
        if (e.Oil != 0) lines.Add($"  Oil: {e.Oil:+#;-#}");
        if (e.Rubber != 0) lines.Add($"  Rubber: {e.Rubber:+#;-#}");
        if (e.Steel != 0) lines.Add($"  Steel: {e.Steel:+#;-#}");
        if (e.Aluminum != 0) lines.Add($"  Aluminum: {e.Aluminum:+#;-#}");
        if (e.Tungsten != 0) lines.Add($"  Tungsten: {e.Tungsten:+#;-#}");
        if (e.Chromium != 0) lines.Add($"  Chromium: {e.Chromium:+#;-#}");
        if (e.WorldTension != 0) lines.Add($"  World Tension: {e.WorldTension:+#;-#}%");

        return string.Join("\n", lines);
    }

    private static string FormatSigned(long value)
    {
        string sign = value >= 0 ? "+" : "-";
        long abs = Math.Abs(value);
        if (abs >= 1000) return $"{sign}{abs / 1000}k";
        return $"{sign}{abs}";
    }
}