using Godot;
using System;
using System.Linq;

/// <summary>
/// Ekonomi paneli (M4): inşaat kuyruğu, fabrika sayıları, CG oranı, ekonomi yasası,
/// kaynak stokları. Overlay olarak açılır (HUD ECONOMY butonundan).
/// </summary>
public partial class EconomyPanel : Control
{
    // UI referansları
    private VBoxContainer _queueBox;
    private VBoxContainer _tradeBox; // ticaret anlaşmaları listesi
    private Label _civLabel, _milLabel, _dockLabel, _freeCivLabel, _cgRateLabel;
    private OptionButton _lawOption;
    private Label[] _resourceLabels;

    // Animasyon
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    private const string ScenePath = "res://Scenes/economy_panel.tscn";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[EconomyPanel] OpenUnder çağrıldı — parent={parent?.GetType().Name ?? "null"}");

        EconomyPanel panel = null;

        // 1) Sahne dosyası projede var mı?
        if (ResourceLoader.Exists(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            if (scene != null)
            {
                try
                {
                    panel = scene.Instantiate<EconomyPanel>();
                    GD.Print($"[EconomyPanel] Sahne yüklendi ve instantiate edildi: {ScenePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[EconomyPanel] Sahne instantiate hatası ({ScenePath}): {e.Message} — KODDAN oluşturuluyor");
                }
            }
            else
            {
                GD.PrintErr($"[EconomyPanel] GD.Load null döndürdü: {ScenePath} — dosya bozuk/parse edilemiyor olabilir. KODDAN oluşturuluyor.");
            }
        }
        else
        {
            GD.PrintErr($"[EconomyPanel] Sahne YOK: {ScenePath} (ResourceLoader.Exists=false) — KODDAN oluşturuluyor.");
        }

        // 2) Yedek yol: koddan oluştur
        panel ??= new EconomyPanel();

        ModalHost.AddUnder(parent, panel);
        GD.Print($"[EconomyPanel] Sahneye eklendi: Visible={panel.Visible}, Size={panel.Size}, Pos={panel.Position}, parent={panel.GetParent()?.GetType().Name ?? "null"}");
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // oyunun paused olsa bile çalışsın
        UITheme.ApplyTheme(this);

        BuildUi();
        SyncFromEconomy();
        ConnectSignals();

        IsOpen = true;

        // Açılış animasyonu
        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        GD.Print("EconomyPanel: hazır");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
        // Güvenli temizlik: sinyal aboneliğini kaldır
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= SyncFromEconomy;
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

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(700, 550) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        margin.AddChild(vbox);

        // --- Başlık ---
        var titleRow = new HBoxContainer();
        var title = new Label { Text = "ECONOMY" };
        title.AddThemeFontSizeOverride("font_size", 32);
        titleRow.AddChild(title);
        titleRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); // spacer

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        titleRow.AddChild(closeBtn);
        vbox.AddChild(titleRow);

        vbox.AddChild(new HSeparator());

        // --- Fabrika sayıları ---
        var factoryRow = new HBoxContainer();
        factoryRow.AddThemeConstantOverride("separation", 16);
        _civLabel = NewStatLabel("Civilian: 0");
        _milLabel = NewStatLabel("Military: 0");
        _dockLabel = NewStatLabel("Dockyards: 0");
        _freeCivLabel = NewStatLabel("Free Civilian: 0", UITheme.Gold);
        factoryRow.AddChild(_civLabel);
        factoryRow.AddChild(_milLabel);
        factoryRow.AddChild(_dockLabel);
        factoryRow.AddChild(_freeCivLabel);
        vbox.AddChild(factoryRow);

        // --- CG oranı + Yasa ---
        var cgRow = new HBoxContainer();
        cgRow.AddThemeConstantOverride("separation", 12);
        _cgRateLabel = NewStatLabel("Consumer Goods: 30%");
        cgRow.AddChild(_cgRateLabel);

        var lawLabel = new Label { Text = "Economy Law:", CustomMinimumSize = new Vector2(120, 0) };
        cgRow.AddChild(lawLabel);

        _lawOption = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        foreach (var law in Enum.GetValues(typeof(EconomySystem.EconomyLaw)))
            _lawOption.AddItem(law.ToString());
        _lawOption.ItemSelected += OnLawChanged;
        cgRow.AddChild(_lawOption);
        vbox.AddChild(cgRow);

        vbox.AddChild(new HSeparator());

        // --- İnşaat kuyruğu ---
        var queueTitle = new Label { Text = "CONSTRUCTION QUEUE" };
        queueTitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(queueTitle);

        // İnşaat başlatma butonları (5 tür)
        var buildRow = new HBoxContainer();
        buildRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(buildRow);

        AddBuildButton(buildRow, "Civ Factory (+1)", EconomySystem.BuildType.CivilianFactory);
        AddBuildButton(buildRow, "Mil Factory (+1)", EconomySystem.BuildType.MilitaryFactory);
        AddBuildButton(buildRow, "Dockyard (+1)", EconomySystem.BuildType.Dockyard);
        AddBuildButton(buildRow, "Infrastructure", EconomySystem.BuildType.Infrastructure);
        AddBuildButton(buildRow, "Convoy (+1)", EconomySystem.BuildType.Convoy);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        _queueBox = new VBoxContainer();
        _queueBox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_queueBox);

        vbox.AddChild(new HSeparator());

        // --- Kaynak stokları ---
        var resTitle = new Label { Text = "RESOURCES (Stockpile / Daily Production)" };
        resTitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(resTitle);

        var resGrid = new GridContainer { Columns = 3 };
        resGrid.AddThemeConstantOverride("h_separation", 12);
        resGrid.AddThemeConstantOverride("v_separation", 4);
        vbox.AddChild(resGrid);

        string[] resources = { "Oil", "Rubber", "Steel", "Aluminum", "Tungsten", "Chromium" };
        _resourceLabels = new Label[resources.Length];
        for (int i = 0; i < resources.Length; i++)
        {
            var lbl = new Label { Text = $"{resources[i]}: 0 / 0" };
            lbl.AddThemeFontSizeOverride("font_size", 15);
            _resourceLabels[i] = lbl;
            resGrid.AddChild(lbl);
        }

        // --- Aktif ticaret anlaşmaları (içeriği SyncFromEconomy'de doldurulur) ---
        vbox.AddChild(new HSeparator());

        var tradeTitle = new Label { Text = "ACTIVE TRADE DEALS" };
        tradeTitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(tradeTitle);

        _tradeBox = new VBoxContainer();
        _tradeBox.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(_tradeBox);
    }

    private Label NewStatLabel(string text, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 16);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return l;
    }

    // İnşaat başlatma butonu üretir
    private void AddBuildButton(HBoxContainer parent, string text, EconomySystem.BuildType type)
    {
        var b = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = $"{type}: inşaat kuyruğuna ekler",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        b.Pressed += () =>
        {
            EconomySystem.StartConstruction(type);
            SyncFromEconomy();
        };
        UITheme.AddHoverScale(b);
        parent.AddChild(b);
    }

    private void ConnectSignals()
    {
        // GameManager.PlayerCountryDataChanged sinyali — EconomySystem güncellemesi sonrası
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged += SyncFromEconomy;
    }

    private void SyncFromEconomy()
    {
        if (!EconomySystem.IsInitialized) return;

        var q = EconomySystem.ConstructionQueue;
        _civLabel.Text = $"Civilian: {EconomySystem.TotalCivFactories}";
        _milLabel.Text = $"Military: {EconomySystem.TotalMilFactories}";
        _dockLabel.Text = $"Dockyards: {EconomySystem.TotalDockyards}";
        _freeCivLabel.Text = $"Free Civilian: {EconomySystem.FreeCivFactories}";
        _cgRateLabel.Text = $"Consumer Goods: {EconomySystem.CGRate * 100:F0}%";

        _lawOption.Select((int)EconomySystem.CurrentLaw);

        // Kuyruk yenile (eski satırları hemen kaldır → beklemeden yeniden çizilsin)
        foreach (Node child in _queueBox.GetChildren())
        {
            _queueBox.RemoveChild(child);
            child.QueueFree();
        }
        for (int i = 0; i < q.Count; i++)
        {
            var item = q[i];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            int idx = i; // closure için kopya
            var name = new Label { Text = $"{idx + 1}. {item.DisplayName}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            name.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(name);

            var progress = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = item.ProgressPercent,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(200, 18)
            };
            row.AddChild(progress);

            var detail = new Label { Text = $"{item.PointsDone}/{item.PointsNeeded} pt ({item.AssignedFactories} civ)" };
            detail.AddThemeFontSizeOverride("font_size", 12);
            detail.AddThemeColorOverride("font_color", UITheme.TextMuted);
            detail.CustomMinimumSize = new Vector2(160, 0);
            row.AddChild(detail);

            var cancel = new Button { Text = "X", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 28) };
            cancel.Pressed += () => EconomySystem.CancelConstruction(idx);
            UITheme.AddHoverScale(cancel);
            row.AddChild(cancel);

            _queueBox.AddChild(row);
        }

        // Kaynaklar
        int idxRes = 0;
        foreach (var key in new[] { "Oil", "Rubber", "Steel", "Aluminum", "Tungsten", "Chromium" })
        {
            EconomySystem.Stockpiles.TryGetValue(key, out long stock);
            int prod = EconomySystem.GetDailyProduction(key);

            var deal = EconomySystem.TradeDeals.FirstOrDefault(t => t.Resource == key);
            if (deal != null)
                _resourceLabels[idxRes].Text = $"{key}: {stock} / {prod}/day  (+{deal.AmountPerDay}/day trade from {deal.ExporterName})";
            else
                _resourceLabels[idxRes].Text = $"{key}: {stock} / {prod}/day";

            idxRes++;
        }

        // Ticaret anlaşmaları listesini yenile
        foreach (Node child in _tradeBox.GetChildren())
        {
            _tradeBox.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var deal in EconomySystem.TradeDeals)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            var name = new Label { Text = $"{deal.Resource}: +{deal.AmountPerDay}/day from {deal.ExporterName}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            name.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(name);

            // Konvoy sayısı
            var convoyLbl = new Label { Text = $"({deal.AmountPerDay} convoy)" };
            convoyLbl.AddThemeFontSizeOverride("font_size", 12);
            convoyLbl.AddThemeColorOverride("font_color", UITheme.TextMuted);
            row.AddChild(convoyLbl);

            _tradeBox.AddChild(row);
        }

        if (EconomySystem.TradeDeals.Count == 0)
        {
            var none = new Label { Text = "No active trade deals" };
            none.AddThemeFontSizeOverride("font_size", 14);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _tradeBox.AddChild(none);
        }
    }

    private void OnLawChanged(long index)
    {
        var newLaw = (EconomySystem.EconomyLaw)index;
        if (EconomySystem.ChangeLaw(newLaw))
            SyncFromEconomy(); // PP değişti, fabricalar güncellenir
    }

    private void Close()
    {
        GD.Print("EconomyPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= SyncFromEconomy;

        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}