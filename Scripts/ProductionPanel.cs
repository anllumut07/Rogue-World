using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Üretim paneli (M6): üretim hatları (efficiency, fabrika ataması, variant switch),
/// yeni hat ekleme, variant tasarım (Tank/Plane Designer), stok paneli.
/// HUD PRODUCTION butonundan açılır.
/// </summary>
public partial class ProductionPanel : Control
{
    // UI referansları
    private VBoxContainer _linesBox;          // üretim hatları satırları
    private VBoxContainer _stockBox;          // stok grid
    private Label _poolLabel;                 // "Mil: X/Y free | Dock: A/B free"
    private OptionButton _addEquipment;       // ADD LINE: ekipman seçici
    private OptionButton _addVariant;         // ADD LINE: variant seçici
    private Label _addFactoryCountLabel;      // ADD LINE: fabrika sayısı göstergesi
    private int _addFactoryCount = 1;         // ADD LINE: fabrika sayısı
    private OptionButton _designBase;         // DESIGNER: temel ekipman (tank/plane)
    private OptionButton _designGun;          // DESIGNER: Gun modülü
    private OptionButton _designArmor;        // DESIGNER: Armor modülü
    private OptionButton _designEngine;       // DESIGNER: Engine modülü
    private Label _designStatsLabel;          // DESIGNER: hesaplı istatistikler

    // Animasyon
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    // Tasarlanabilir ekipman ID'leri (tank + hava)
    private static readonly HashSet<string> DesignableIds = new()
    {
        "light_tank", "medium_tank", "heavy_tank",
        "fighter", "cas", "naval_bomber", "strategic_bomber"
    };

    private const string ScenePath = "res://Scenes/production_panel.tscn";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[ProductionPanel] OpenUnder çağrıldı — parent={parent?.GetType().Name ?? "null"}");

        ProductionPanel panel = null;

        // 1) Sahne dosyası projede var mı?
        if (ResourceLoader.Exists(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            if (scene != null)
            {
                try
                {
                    panel = scene.Instantiate<ProductionPanel>();
                    GD.Print($"[ProductionPanel] Sahne yüklendi ve instantiate edildi: {ScenePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[ProductionPanel] Sahne instantiate hatası ({ScenePath}): {e.Message} — KODDAN oluşturuluyor");
                }
            }
            else
            {
                GD.PrintErr($"[ProductionPanel] GD.Load null döndürdü: {ScenePath} — dosya bozuk/parse edilemiyor olabilir. KODDAN oluşturuluyor.");
            }
        }
        else
        {
            GD.PrintErr($"[ProductionPanel] Sahne YOK: {ScenePath} (ResourceLoader.Exists=false) — KODDAN oluşturuluyor.");
        }

        // 2) Yedek yol: koddan oluştur
        panel ??= new ProductionPanel();

        ModalHost.AddUnder(parent, panel);
        GD.Print($"[ProductionPanel] Sahneye eklendi: Visible={panel.Visible}, Size={panel.Size}, Pos={panel.Position}, parent={panel.GetParent()?.GetType().Name ?? "null"}");
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

        GD.Print("ProductionPanel: hazır");
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

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(850, 640) };
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

        // ===== BAŞLIK + HAVUZ BİLGİSİ + CLOSE =====
        var headerRow = new HBoxContainer();
        vbox.AddChild(headerRow);

        var title = new Label { Text = "PRODUCTION" };
        title.AddThemeFontSizeOverride("font_size", 30);
        headerRow.AddChild(title);

        headerRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _poolLabel = new Label();
        _poolLabel.AddThemeFontSizeOverride("font_size", 14);
        _poolLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        headerRow.AddChild(_poolLabel);

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        headerRow.AddChild(closeBtn);

        vbox.AddChild(new HSeparator());

        // ===== 1. PRODUCTION LINES =====
        var linesTitle = new Label { Text = "PRODUCTION LINES" };
        linesTitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(linesTitle);

        var scrollLines = new ScrollContainer();
        scrollLines.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(scrollLines);

        _linesBox = new VBoxContainer();
        _linesBox.AddThemeConstantOverride("separation", 8);
        scrollLines.AddChild(_linesBox);

        vbox.AddChild(new HSeparator());

        // ===== 2. ADD LINE =====
        var addTitle = new Label { Text = "ADD NEW LINE" };
        addTitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(addTitle);

        var addRow = new HBoxContainer();
        addRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(addRow);

        // Equipment seçici
        _addEquipment = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        _addEquipment.ItemSelected += OnAddEquipmentChanged;
        addRow.AddChild(_addEquipment);

        // Variant seçici (None + variantlar)
        _addVariant = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        addRow.AddChild(_addVariant);

        // Fabrika sayısı: [-] [N] [+]
        var factoryCountRow = new HBoxContainer();
        factoryCountRow.AddThemeConstantOverride("separation", 6);
        addRow.AddChild(factoryCountRow);

        var minusBtn = new Button { Text = "−", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 30) };
        minusBtn.Pressed += () => ChangeAddFactoryCount(-1);
        UITheme.AddHoverScale(minusBtn, 1.1f);
        factoryCountRow.AddChild(minusBtn);

        _addFactoryCountLabel = new Label { Text = "1", CustomMinimumSize = new Vector2(40, 0), HorizontalAlignment = HorizontalAlignment.Center };
        _addFactoryCountLabel.AddThemeFontSizeOverride("font_size", 14);
        factoryCountRow.AddChild(_addFactoryCountLabel);

        var plusBtn = new Button { Text = "+", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 30) };
        plusBtn.Pressed += () => ChangeAddFactoryCount(1);
        UITheme.AddHoverScale(plusBtn, 1.1f);
        factoryCountRow.AddChild(plusBtn);

        var addLineBtn = new Button { Text = "ADD LINE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 36) };
        addLineBtn.Pressed += OnAddLinePressed;
        UITheme.AddHoverScale(addLineBtn);
        addRow.AddChild(addLineBtn);

        vbox.AddChild(new HSeparator());

        // ===== 3. VARIANT DESIGNER =====
        var designTitle = new Label { Text = "VARIANT DESIGNER (Tanks & Planes)" };
        designTitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(designTitle);

        var designRow = new HBoxContainer();
        designRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(designRow);

        var designBaseLabel = new Label { Text = "Base:", CustomMinimumSize = new Vector2(50, 0) };
        designRow.AddChild(designBaseLabel);

        _designBase = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        _designBase.ItemSelected += OnDesignBaseChanged;
        designRow.AddChild(_designBase);

        // Modül seçimleri
        var gunLabel = new Label { Text = "Gun:", CustomMinimumSize = new Vector2(45, 0) };
        designRow.AddChild(gunLabel);

        _designGun = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        _designGun.ItemSelected += OnDesignModuleChanged;
        designRow.AddChild(_designGun);

        var armorLabel = new Label { Text = "Armor:", CustomMinimumSize = new Vector2(50, 0) };
        designRow.AddChild(armorLabel);

        _designArmor = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        _designArmor.ItemSelected += OnDesignModuleChanged;
        designRow.AddChild(_designArmor);

        var engineLabel = new Label { Text = "Engine:", CustomMinimumSize = new Vector2(55, 0) };
        designRow.AddChild(engineLabel);

        _designEngine = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        _designEngine.ItemSelected += OnDesignModuleChanged;
        designRow.AddChild(_designEngine);

        var createBtn = new Button { Text = "CREATE VARIANT", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(140, 36) };
        createBtn.Pressed += OnCreateVariantPressed;
        UITheme.AddHoverScale(createBtn);
        designRow.AddChild(createBtn);

        // Hesaplı istatistikler etiketi
        _designStatsLabel = new Label();
        _designStatsLabel.AddThemeFontSizeOverride("font_size", 12);
        _designStatsLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _designStatsLabel.CustomMinimumSize = new Vector2(0, 24);
        vbox.AddChild(_designStatsLabel);

        vbox.AddChild(new HSeparator());

        // ===== 4. STOCKPILE =====
        var stockTitle = new Label { Text = "STOCKPILE" };
        stockTitle.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(stockTitle);

        var scrollStock = new ScrollContainer { CustomMinimumSize = new Vector2(0, 120) };
        scrollStock.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        vbox.AddChild(scrollStock);

        _stockBox = new VBoxContainer();
        _stockBox.AddThemeConstantOverride("separation", 4);
        scrollStock.AddChild(_stockBox);
    }

    private void ConnectSignals()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged += OnDataChanged;
    }

    private void OnDataChanged() => RefreshAll();

    private void RefreshAll()
    {
        RefreshPoolLabel();
        RefreshAddEquipment();
        RefreshAddVariant();
        RefreshLines();
        RefreshDesignBase();
        RefreshDesignModules();
        RefreshDesignStats();
        RefreshStockpile();
    }

    private void RefreshPoolLabel()
    {
        if (_poolLabel == null || !GodotObject.IsInstanceValid(_poolLabel)) return;
        _poolLabel.Text = $"Mil: {EquipmentSystem.FreeMilFactories}/{EquipmentSystem.TotalMilFactories} free  |  Dock: {EquipmentSystem.FreeDockyards}/{EquipmentSystem.TotalDockyards} free";
    }

    // ADD LINE: ekipman listesi (AllTypes)
    private void RefreshAddEquipment()
    {
        _addEquipment.Clear();
        foreach (var kv in EquipmentSystem.AllTypes)
        {
            var t = kv.Value;
            string display = $"{t.Name}  ({t.Category}/{t.Factory})";
            _addEquipment.AddItem(display);
            _addEquipment.SetItemMetadata(_addEquipment.GetItemCount() - 1, t.Id);
        }
        _addEquipment.Selected = 0;
    }

    private void OnAddEquipmentChanged(long idx)
    {
        RefreshAddVariant();
    }

    // ADD LINE: seçili ekipmanın variantları (None + variantlar)
    private void RefreshAddVariant()
    {
        _addVariant.Clear();
        _addVariant.AddItem("Base Equipment (No Variant)");

        if (_addEquipment.GetItemCount() > 0)
        {
            string equipId = _addEquipment.GetItemMetadata(_addEquipment.Selected).AsString();
            var variants = EquipmentSystem.GetVariantsFor(equipId);
            foreach (var v in variants)
            {
                _addVariant.AddItem(v.Name);
                _addVariant.SetItemMetadata(_addVariant.GetItemCount() - 1, v.Id);
            }
        }
        _addVariant.Selected = 0;
    }

    private void ChangeAddFactoryCount(int delta)
    {
        _addFactoryCount = Math.Max(1, _addFactoryCount + delta);
        _addFactoryCountLabel.Text = _addFactoryCount.ToString();
    }

    private void OnAddLinePressed()
    {
        if (_addEquipment.GetItemCount() == 0) return;

        string equipId = _addEquipment.GetItemMetadata(_addEquipment.Selected).AsString();
        string variantId = "";

        if (_addVariant.Selected > 0 && _addVariant.GetItemCount() > _addVariant.Selected)
            variantId = _addVariant.GetItemMetadata(_addVariant.Selected).AsString();

        bool ok = EquipmentSystem.AddLine(equipId, _addFactoryCount, variantId);
        if (ok)
        {
            _addFactoryCount = 1;
            _addFactoryCountLabel.Text = "1";
            RefreshAll();
        }
    }

    // PRODUCTION LINES listeleme
    private void RefreshLines()
    {
        foreach (Node child in _linesBox.GetChildren())
        {
            _linesBox.RemoveChild(child);
            child.QueueFree();
        }

        var lines = EquipmentSystem.Lines;
        if (lines.Count == 0)
        {
            var none = new Label { Text = "No production lines — add one below" };
            none.AddThemeFontSizeOverride("font_size", 14);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _linesBox.AddChild(none);
            return;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            int index = i; // closure için
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);

            // İsim + variant
            string displayName = EquipmentSystem.GetDisplayName(line.EquipmentId, line.VariantId);
            var nameLabel = new Label { Text = displayName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            nameLabel.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(nameLabel);

            // Fabrika sayısı: [−] N [+]
            var factoryBtns = new HBoxContainer();
            factoryBtns.AddThemeConstantOverride("separation", 4);
            row.AddChild(factoryBtns);

            var decBtn = new Button { Text = "−", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 24) };
            decBtn.Pressed += () => { EquipmentSystem.ChangeLineFactories(index, -1); RefreshLines(); };
            UITheme.AddHoverScale(decBtn, 1.1f);
            factoryBtns.AddChild(decBtn);

            var countLabel = new Label { Text = line.AssignedFactories.ToString(), CustomMinimumSize = new Vector2(30, 0), HorizontalAlignment = HorizontalAlignment.Center };
            countLabel.AddThemeFontSizeOverride("font_size", 12);
            factoryBtns.AddChild(countLabel);

            var incBtn = new Button { Text = "+", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 24) };
            incBtn.Pressed += () => { EquipmentSystem.ChangeLineFactories(index, 1); RefreshLines(); };
            UITheme.AddHoverScale(incBtn, 1.1f);
            factoryBtns.AddChild(incBtn);

            // Etkinlik ProgressBar + % etiketi
            var effBar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = line.Efficiency * 100,
                CustomMinimumSize = new Vector2(150, 18)
            };
            row.AddChild(effBar);

            var effLabel = new Label { Text = $"{line.Efficiency*100:F0}%", CustomMinimumSize = new Vector2(40, 0) };
            effLabel.AddThemeFontSizeOverride("font_size", 11);
            effLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            row.AddChild(effLabel);

            // Günlük üretim + stok
            float dailyOut = EquipmentSystem.GetDailyOutput(line);
            long stock = EquipmentSystem.GetStock(line.EquipmentId);
            var outLabel = new Label { Text = $"+{dailyOut:F2}/day  |  stock: {stock}", CustomMinimumSize = new Vector2(180, 0) };
            outLabel.AddThemeFontSizeOverride("font_size", 11);
            outLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            row.AddChild(outLabel);

            // Variant switch (Base + variantlar)
            var variantDropdown = new OptionButton
            {
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(140, 24)
            };
            variantDropdown.AddItem("Base Equipment");
            var variants = EquipmentSystem.GetVariantsFor(line.EquipmentId);
            foreach (var v in variants)
            {
                variantDropdown.AddItem(v.Name);
                variantDropdown.SetItemMetadata(variantDropdown.GetItemCount() - 1, v.Id);
            }
            // Mevcut variantı seçili yap
            if (!string.IsNullOrEmpty(line.VariantId))
            {
                for (int j = 1; j < variantDropdown.GetItemCount(); j++)
                    if (variantDropdown.GetItemMetadata(j).AsString() == line.VariantId)
                        variantDropdown.Selected = j;
            }
            variantDropdown.ItemSelected += (idx) => OnLineVariantChanged(index, idx, variantDropdown);
            row.AddChild(variantDropdown);

            // Kaldır butonu
            var removeBtn = new Button { Text = "×", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 28) };
            removeBtn.Pressed += () => { EquipmentSystem.RemoveLine(index); RefreshLines(); };
            UITheme.AddHoverScale(removeBtn);
            row.AddChild(removeBtn);

            _linesBox.AddChild(row);
        }
    }

    private void OnLineVariantChanged(int lineIndex, long selectedIdx, OptionButton dropdown)
    {
        string variantId = "";
        if (selectedIdx > 0)
            variantId = dropdown.GetItemMetadata((int)selectedIdx).AsString();

        EquipmentSystem.SwitchLineEquipment(lineIndex, variantId);
        RefreshLines();
    }

    // VARIANT DESIGNER: temel ekipman listesi
    private void RefreshDesignBase()
    {
        _designBase.Clear();
        foreach (var kv in EquipmentSystem.AllTypes)
        {
            var t = kv.Value;
            if (!DesignableIds.Contains(t.Id)) continue;
            _designBase.AddItem($"{t.Name}  ({t.Category})");
            _designBase.SetItemMetadata(_designBase.GetItemCount() - 1, t.Id);
        }
        _designBase.Selected = 0;
    }

    private void OnDesignBaseChanged(long idx) => RefreshDesignModules();

    // Modül adından Module nesnesini bulur (metadata'da ad saklanır — Variant uyumlu)
    private static EquipmentSystem.Module FindModule(string slot, string name)
    {
        return EquipmentSystem.GetModuleOptions(slot).Find(m => m.Name == name);
    }

    // VARIANT DESIGNER: modül listeleri (None + seçenekler)
    private void RefreshDesignModules()
    {
        _designGun.Clear();
        _designArmor.Clear();
        _designEngine.Clear();

        _designGun.AddItem("None");
        var gunOpts = EquipmentSystem.GetModuleOptions("Gun");
        foreach (var m in gunOpts)
        {
            _designGun.AddItem(m.Name);
            _designGun.SetItemMetadata(_designGun.GetItemCount() - 1, m.Name);
        }

        _designArmor.AddItem("None");
        var armorOpts = EquipmentSystem.GetModuleOptions("Armor");
        foreach (var m in armorOpts)
        {
            _designArmor.AddItem(m.Name);
            _designArmor.SetItemMetadata(_designArmor.GetItemCount() - 1, m.Name);
        }

        _designEngine.AddItem("None");
        var engineOpts = EquipmentSystem.GetModuleOptions("Engine");
        foreach (var m in engineOpts)
        {
            _designEngine.AddItem(m.Name);
            _designEngine.SetItemMetadata(_designEngine.GetItemCount() - 1, m.Name);
        }

        _designGun.Selected = 0;
        _designArmor.Selected = 0;
        _designEngine.Selected = 0;
    }

    private void OnDesignModuleChanged(long idx) => RefreshDesignStats();

    private void RefreshDesignStats()
    {
        if (_designBase.GetItemCount() == 0)
        {
            _designStatsLabel.Text = "";
            return;
        }

        string baseId = _designBase.GetItemMetadata(_designBase.Selected).AsString();
        var baseType = EquipmentSystem.GetEquipmentType(baseId);
        if (baseType == null)
        {
            _designStatsLabel.Text = "";
            return;
        }

        float costMult = 1f, atkMult = 1f, defMult = 1f;

        if (_designGun.Selected > 0 && _designGun.GetItemCount() > _designGun.Selected)
        {
            var m = FindModule("Gun", _designGun.GetItemMetadata(_designGun.Selected).AsString());
            if (m != null) { costMult += m.CostMult; atkMult += m.AttackMult; defMult += m.DefenseMult; }
        }
        if (_designArmor.Selected > 0 && _designArmor.GetItemCount() > _designArmor.Selected)
        {
            var m = FindModule("Armor", _designArmor.GetItemMetadata(_designArmor.Selected).AsString());
            if (m != null) { costMult += m.CostMult; atkMult += m.AttackMult; defMult += m.DefenseMult; }
        }
        if (_designEngine.Selected > 0 && _designEngine.GetItemCount() > _designEngine.Selected)
        {
            var m = FindModule("Engine", _designEngine.GetItemMetadata(_designEngine.Selected).AsString());
            if (m != null) { costMult += m.CostMult; atkMult += m.AttackMult; defMult += m.DefenseMult; }
        }

        int atk = (int)(baseType.Attack * atkMult);
        int def = (int)(baseType.Defense * defMult);

        _designStatsLabel.Text = $"Cost ×{costMult:F2}  |  ATK {atk} (+{(atkMult-1)*100:F0}%)  |  DEF {def} (+{(defMult-1)*100:F0}%)  |  Base: ATK {baseType.Attack} DEF {baseType.Defense}";
    }

    private void OnCreateVariantPressed()
    {
        if (_designBase.GetItemCount() == 0) return;

        string baseId = _designBase.GetItemMetadata(_designBase.Selected).AsString();
        var modules = new List<EquipmentSystem.Module>();

        if (_designGun.Selected > 0)
        {
            var m = FindModule("Gun", _designGun.GetItemMetadata(_designGun.Selected).AsString());
            if (m != null) modules.Add(m);
        }
        if (_designArmor.Selected > 0)
        {
            var m = FindModule("Armor", _designArmor.GetItemMetadata(_designArmor.Selected).AsString());
            if (m != null) modules.Add(m);
        }
        if (_designEngine.Selected > 0)
        {
            var m = FindModule("Engine", _designEngine.GetItemMetadata(_designEngine.Selected).AsString());
            if (m != null) modules.Add(m);
        }

        var variant = EquipmentSystem.CreateVariant(baseId, modules);
        if (variant != null)
        {
            RefreshAddVariant(); // ADD LINE variant listesi güncellenir
            RefreshDesignModules(); // modül seçimleri sıfırlanır (None)
            RefreshDesignStats();
        }
    }

    private void RefreshStockpile()
    {
        foreach (Node child in _stockBox.GetChildren())
        {
            _stockBox.RemoveChild(child);
            child.QueueFree();
        }

        var stocks = EquipmentSystem.Stocks;
        if (stocks.Count == 0)
        {
            var none = new Label { Text = "Stockpile empty" };
            none.AddThemeFontSizeOverride("font_size", 14);
            none.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _stockBox.AddChild(none);
            return;
        }

        // 3 sütunlu grid (GridContainer yerine VBox içinde HBox satırları, 3'erli)
        var currentRow = new HBoxContainer();
        currentRow.AddThemeConstantOverride("separation", 24);
        _stockBox.AddChild(currentRow);
        int col = 0;

        foreach (var kv in stocks.OrderBy(k => k.Key))
        {
            string equipId = kv.Key;
            long count = kv.Value;
            float dailyOut = EquipmentSystem.Lines
                .Where(l => l.EquipmentId == equipId)
                .Sum(l => EquipmentSystem.GetDailyOutput(l));

            var type = EquipmentSystem.GetEquipmentType(equipId);
            string display = type?.Name ?? equipId;

            var item = new VBoxContainer();
            item.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            item.AddThemeConstantOverride("separation", 2);

            var nameLabel = new Label { Text = display };
            nameLabel.AddThemeFontSizeOverride("font_size", 12);
            item.AddChild(nameLabel);

            var countLabel = new Label { Text = $"{count}  (+{dailyOut:F2}/day)" };
            countLabel.AddThemeFontSizeOverride("font_size", 12);
            countLabel.AddThemeColorOverride("font_color", UITheme.Gold);
            item.AddChild(countLabel);

            currentRow.AddChild(item);

            col++;
            if (col >= 3)
            {
                col = 0;
                currentRow = new HBoxContainer();
                currentRow.AddThemeConstantOverride("separation", 24);
                _stockBox.AddChild(currentRow);
            }
        }
    }

    private void Close()
    {
        GD.Print("ProductionPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;

        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}