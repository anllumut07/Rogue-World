using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Ordu paneli (M7): aktif savaşlar (ilerleme + emirler), bölük şablonları (+ TRAIN),
/// şablon tasarımcısı (tabur +/-), eğitim kuyruğu, ordular + generaller + tümen org/hp.
/// HUD ARMY butonundan açılır.
/// MouseFilter: backdrop Stop (harita girdisini bloke), dekoratif etiketler Ignore.
/// </summary>
public partial class ArmyPanel : Control
{
    // UI referansları
    private Label _infoLabel;                // üst bilgi: tümen/manpower/güç
    private VBoxContainer _contentBox;      // ana scroll içeriği
    private VBoxContainer _warsBox;         // aktif savaşlar
    private VBoxContainer _templatesBox;    // şablon listesi (TRAIN butonlarıyla)
    private VBoxContainer _designerBox;     // şablon tasarımcısı satırları
    private Label _designerStatsLabel;      // tasarım istatistik önizleme
    private VBoxContainer _trainingBox;      // eğitim kuyruğu
    private VBoxContainer _armiesBox;       // ordular + tümenler

    // Tasarımcı durumu: tabur id → adet
    private readonly Dictionary<string, int> _designerCounts = new();
    private int _designerCounter;

    // Animasyon
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    private const string ScenePath = "res://Scenes/army_panel.tscn";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[ArmyPanel] OpenUnder çağrıldı — parent={parent?.GetType().Name ?? "null"}");

        ArmyPanel panel = null;

        // 1) Sahne dosyası projede var mı?
        if (ResourceLoader.Exists(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            if (scene != null)
            {
                try
                {
                    panel = scene.Instantiate<ArmyPanel>();
                    GD.Print($"[ArmyPanel] Sahne yüklendi ve instantiate edildi: {ScenePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[ArmyPanel] Sahne instantiate hatası ({ScenePath}): {e.Message} — KODDAN oluşturuluyor");
                }
            }
            else
            {
                GD.PrintErr($"[ArmyPanel] GD.Load null döndürdü: {ScenePath} — dosya bozuk/parse edilemiyor olabilir. KODDAN oluşturuluyor.");
            }
        }
        else
        {
            GD.PrintErr($"[ArmyPanel] Sahne YOK: {ScenePath} (ResourceLoader.Exists=false) — KODDAN oluşturuluyor.");
        }

        // 2) Yedek yol: koddan oluştur
        panel ??= new ArmyPanel();

        ModalHost.AddUnder(parent, panel);
        GD.Print($"[ArmyPanel] Sahneye eklendi: Visible={panel.Visible}, Size={panel.Size}, Pos={panel.Position}, parent={panel.GetParent()?.GetType().Name ?? "null"}");
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

        GD.Print("ArmyPanel: hazır");
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

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(880, 620) }; // 1152x648'e sigar (620 <= 648)
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        // ===== BAŞLIK + BİLGİ + CLOSE =====
        var headerRow = new HBoxContainer();
        vbox.AddChild(headerRow);

        var title = new Label { Text = "ARMY & WAR" };
        title.AddThemeFontSizeOverride("font_size", 30);
        headerRow.AddChild(title);

        headerRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _infoLabel = new Label();
        _infoLabel.AddThemeFontSizeOverride("font_size", 14);
        _infoLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        headerRow.AddChild(_infoLabel);

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        headerRow.AddChild(closeBtn);

        // Ana scroll
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        _contentBox = new VBoxContainer();
        _contentBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _contentBox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_contentBox);

        // ===== 1. AKTİF SAVAŞLAR =====
        var warsTitle = new Label { Text = "ACTIVE WARS" };
        warsTitle.AddThemeFontSizeOverride("font_size", 18);
        _contentBox.AddChild(warsTitle);

        _warsBox = new VBoxContainer();
        _warsBox.AddThemeConstantOverride("separation", 6);
        _contentBox.AddChild(_warsBox);

        _contentBox.AddChild(new HSeparator());

        // ===== 2. BÖLÜK ŞABLONLARI =====
        var tplTitle = new Label { Text = "DIVISION TEMPLATES" };
        tplTitle.AddThemeFontSizeOverride("font_size", 18);
        _contentBox.AddChild(tplTitle);

        _templatesBox = new VBoxContainer();
        _templatesBox.AddThemeConstantOverride("separation", 4);
        _contentBox.AddChild(_templatesBox);

        _contentBox.AddChild(new HSeparator());

        // ===== 3. ŞABLON TASARIMCISI =====
        var designerTitle = new Label { Text = "TEMPLATE DESIGNER" };
        designerTitle.AddThemeFontSizeOverride("font_size", 18);
        _contentBox.AddChild(designerTitle);

        _designerStatsLabel = new Label();
        _designerStatsLabel.AddThemeFontSizeOverride("font_size", 12);
        _designerStatsLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _contentBox.AddChild(_designerStatsLabel);

        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 12);
        _contentBox.AddChild(saveRow);

        var saveBtn = new Button { Text = "SAVE TEMPLATE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(140, 32) };
        saveBtn.Pressed += OnSaveTemplatePressed;
        UITheme.AddHoverScale(saveBtn);
        saveRow.AddChild(saveBtn);

        var clearBtn = new Button { Text = "CLEAR", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(90, 32) };
        clearBtn.Pressed += () => { _designerCounts.Clear(); RefreshDesigner(); };
        UITheme.AddHoverScale(clearBtn);
        saveRow.AddChild(clearBtn);

        _designerBox = new VBoxContainer();
        _designerBox.AddThemeConstantOverride("separation", 2);
        _contentBox.AddChild(_designerBox);

        _contentBox.AddChild(new HSeparator());

        // ===== 4. EĞİTİM KUYRUĞU =====
        var trainTitle = new Label { Text = "TRAINING QUEUE" };
        trainTitle.AddThemeFontSizeOverride("font_size", 18);
        _contentBox.AddChild(trainTitle);

        _trainingBox = new VBoxContainer();
        _trainingBox.AddThemeConstantOverride("separation", 4);
        _contentBox.AddChild(_trainingBox);

        _contentBox.AddChild(new HSeparator());

        // ===== 5. ORDULAR =====
        var armiesTitleRow = new HBoxContainer();
        armiesTitleRow.AddThemeConstantOverride("separation", 12);
        _contentBox.AddChild(armiesTitleRow);

        var armiesTitle = new Label { Text = "ARMIES" };
        armiesTitle.AddThemeFontSizeOverride("font_size", 18);
        armiesTitleRow.AddChild(armiesTitle);

        var createArmyBtn = new Button { Text = "CREATE ARMY", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 30) };
        createArmyBtn.Pressed += () => { ArmySystem.CreateArmy(""); RefreshAll(); };
        UITheme.AddHoverScale(createArmyBtn);
        armiesTitleRow.AddChild(createArmyBtn);

        _armiesBox = new VBoxContainer();
        _armiesBox.AddThemeConstantOverride("separation", 8);
        _contentBox.AddChild(_armiesBox);
    }

    private void ConnectSignals()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged += OnDataChanged;
    }

    private void OnDataChanged() => RefreshAll();

    private void RefreshAll()
    {
        RefreshInfoLabel();
        RefreshWars();
        RefreshTemplates();
        RefreshDesigner();
        RefreshTraining();
        RefreshArmies();
    }

    private void RefreshInfoLabel()
    {
        if (_infoLabel == null || !GodotObject.IsInstanceValid(_infoLabel)) return;
        var player = GameManager.Instance?.PlayerCountryData;
        if (player == null) { _infoLabel.Text = ""; return; }
        _infoLabel.Text = $"Divisions: {ArmySystem.DeployedDivisions} (+{ArmySystem.TrainingDivisions} training)  |  " +
                           $"Manpower: {player.ManpowerPool:N0}  |  Strength: {ArmySystem.GetMilitaryStrength():F0}";
    }

    // ===== 1. Savaşlar =====
    private void RefreshWars()
    {
        ClearChildren(_warsBox);

        var wars = WarSystem.ActiveWars;
        if (wars.Count == 0)
        {
            AddMuted(_warsBox, "No active wars — select a country and DECLARE WAR from its info panel");
            return;
        }

        foreach (var war in wars)
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            _warsBox.AddChild(box);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            box.AddChild(row);

            // Düşman bayrağı (24x16)
            var flagBox = new FlagBox { CustomMinimumSize = new Vector2(24, 16) };
            flagBox.SetCountry(war.DefenderId);
            row.AddChild(flagBox);

            var nameLabel = new Label
            {
                Text = $"vs {war.DefenderName}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 15);
            row.AddChild(nameLabel);

            // İlerleme çubuğu
            var progress = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = war.WarProgress,
                CustomMinimumSize = new Vector2(180, 18)
            };
            row.AddChild(progress);

            var detail = new Label { Text = $"{war.WarProgress:F0}% | Enemy div: {war.EnemyDivisions} | Losses: {war.PlayerCasualties:N0}/{war.EnemyCasualties:N0}" };
            detail.AddThemeFontSizeOverride("font_size", 12);
            detail.AddThemeColorOverride("font_color", UITheme.TextMuted);
            row.AddChild(detail);

            // Emir butonları (RMB / Shift+RMB ile aynı işlev)
            var stanceLabel = new Label
            {
                Text = war.OffensiveActive ? "OFFENSIVE" : (war.FrontlineStance ? "FRONTLINE (holding)" : "PASSIVE")
            };
            stanceLabel.AddThemeFontSizeOverride("font_size", 12);
            stanceLabel.AddThemeColorOverride("font_color",
                war.OffensiveActive ? Colors.Orange : (war.FrontlineStance ? new Color(0.5f, 0.8f, 1f) : UITheme.TextMuted));
            row.AddChild(stanceLabel);

            var offBtn = new Button
            {
                Text = war.OffensiveActive ? "STOP OFFENSIVE" : "OFFENSIVE",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(110, 26)
            };
            offBtn.Pressed += () => { WarSystem.SetOffensive(war.DefenderId, !war.OffensiveActive); RefreshWars(); };
            UITheme.AddHoverScale(offBtn);
            row.AddChild(offBtn);

            var frontBtn = new Button
            {
                Text = war.FrontlineStance ? "RELEASE FRONT" : "HOLD FRONT",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(110, 26)
            };
            frontBtn.Pressed += () => { WarSystem.ToggleFrontline(war.DefenderId); RefreshWars(); };
            UITheme.AddHoverScale(frontBtn);
            row.AddChild(frontBtn);
        }
    }

    // ===== 2. Şablonlar =====
    private void RefreshTemplates()
    {
        ClearChildren(_templatesBox);

        foreach (var kv in ArmySystem.Templates)
        {
            var tpl = kv.Value;
            int count = ArmySystem.Divisions.Count(d => d.TemplateId == tpl.Id);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            _templatesBox.AddChild(row);

            var nameLabel = new Label { Text = $"{tpl.Name} ×{count}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            nameLabel.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(nameLabel);

            var statsLabel = new Label
            {
                Text = $"ORG {tpl.MaxOrg} | HP {tpl.MaxHp} | ATT {tpl.Attack} | DEF {tpl.Defense} | " +
                       $"Men {tpl.Manpower:N0} | {tpl.BattalionIds.Count} bns"
            };
            statsLabel.AddThemeFontSizeOverride("font_size", 11);
            statsLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            row.AddChild(statsLabel);

            var trainBtn = new Button
            {
                Text = "TRAIN",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(80, 26)
            };
            trainBtn.TooltipText = $"Manpower: {tpl.Manpower:N0}\nEquipment: " +
                                   string.Join("\n  ", tpl.Equipment.Select(e => $"{e.Key} ×{e.Value}"));
            trainBtn.Pressed += () =>
            {
                if (ArmySystem.StartTraining(tpl.Id)) RefreshAll();
            };
            UITheme.AddHoverScale(trainBtn);
            row.AddChild(trainBtn);
        }

        if (ArmySystem.Templates.Count == 0)
            AddMuted(_templatesBox, "No templates");
    }

    // ===== 3. Tasarımcı =====
    private void RefreshDesigner()
    {
        // Tabur satırlarını bir kez oluştur; sadece sayaç etiketleri güncellenir.
        if (_designerBox.GetChildCount() == 0)
        {
            foreach (var kv in ArmySystem.AllBattalions)
            {
                var b = kv.Value;
                string bid = b.Id;

                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                _designerBox.AddChild(row);

                var nameLabel = new Label { Text = b.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                nameLabel.AddThemeFontSizeOverride("font_size", 12);
                if (b.Category == "Support")
                    nameLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 1f));
                nameLabel.TooltipText = $"ORG {b.Org} | HP {b.Hp} | ATT {b.Attack} | DEF {b.Defense} | " +
                                        $"Pierce {b.Pierce} | Armor {b.Armor} | Speed {b.Speed} | " +
                                        $"Supply {b.SupplyUse:F1} | Men {b.Manpower}";
                row.AddChild(nameLabel);

                var minusBtn = new Button { Text = "−", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(26, 22) };
                minusBtn.Pressed += () => ChangeDesignerCount(bid, -1);
                UITheme.AddHoverScale(minusBtn, 1.1f);
                row.AddChild(minusBtn);

                var countLabel = new Label
                {
                    Text = "0",
                    CustomMinimumSize = new Vector2(32, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Name = $"cnt_{bid}" // güncelleme için isim
                };
                countLabel.AddThemeFontSizeOverride("font_size", 12);
                row.AddChild(countLabel);

                var plusBtn = new Button { Text = "+", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(26, 22) };
                plusBtn.Pressed += () => ChangeDesignerCount(bid, 1);
                UITheme.AddHoverScale(plusBtn, 1.1f);
                row.AddChild(plusBtn);
            }
        }

        // Sayaçları güncelle
        foreach (var kv in _designerCounts)
        {
            var label = _designerBox.GetNodeOrNull<Label>($"cnt_{kv.Key}");
            if (label != null) label.Text = kv.Value.ToString();
        }
        // Sıfır olanları da sıfırla
        foreach (var b in ArmySystem.AllBattalions.Values)
        {
            var label = _designerBox.GetNodeOrNull<Label>($"cnt_{b.Id}");
            if (label != null && !_designerCounts.ContainsKey(b.Id)) label.Text = "0";
        }

        UpdateDesignerStats();
    }

    private void ChangeDesignerCount(string battalionId, int delta)
    {
        int current = _designerCounts.GetValueOrDefault(battalionId);
        int next = Math.Max(0, current + delta);
        if (next == 0) _designerCounts.Remove(battalionId);
        else _designerCounts[battalionId] = next;
        RefreshDesigner();
    }

    private void UpdateDesignerStats()
    {
        if (_designerCounts.Count == 0)
        {
            _designerStatsLabel.Text = "Select battalions with + buttons to design a new division template";
            return;
        }

        // Geçici şablon üzerinden istatistik hesapla (ArmySystem.ComputeTemplateStats)
        var ids = new List<string>();
        foreach (var kv in _designerCounts)
            for (int i = 0; i < kv.Value; i++)
                ids.Add(kv.Key);

        var tmp = new ArmySystem.DivisionTemplate { BattalionIds = ids };
        ArmySystem.ComputeTemplateStats(tmp);

        _designerStatsLabel.Text = $"Preview: ORG {tmp.MaxOrg} | HP {tmp.MaxHp} | ATT {tmp.Attack} | DEF {tmp.Defense} | " +
                                   $"Pierce {tmp.Pierce} | Armor {tmp.Armor} | Speed {tmp.Speed} | " +
                                   $"Supply {tmp.SupplyUse:F1} | Men {tmp.Manpower:N0} | " +
                                   $"{tmp.BattalionIds.Count} bns";
    }

    private void OnSaveTemplatePressed()
    {
        if (_designerCounts.Count == 0)
        {
            GD.Print("[ArmyPanel] Tasarım boş — önce tabur ekle");
            return;
        }

        _designerCounter++;
        var ids = new List<string>();
        foreach (var kv in _designerCounts)
            for (int i = 0; i < kv.Value; i++)
                ids.Add(kv.Key);

        var tpl = ArmySystem.CreateTemplate($"Division Mk.{_designerCounter}", ids);
        if (tpl != null)
        {
            _designerCounts.Clear();
            RefreshAll();
        }
    }

    // ===== 4. Eğitim kuyruğu =====
    private void RefreshTraining()
    {
        ClearChildren(_trainingBox);

        var queue = ArmySystem.TrainingQueue;
        if (queue.Count == 0)
        {
            AddMuted(_trainingBox, "No divisions in training");
            return;
        }

        foreach (var item in queue.ToList())
        {
            string itemId = item.Id; // closure
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            _trainingBox.AddChild(row);

            var nameLabel = new Label { Text = item.TemplateName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            nameLabel.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(nameLabel);

            var progress = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = 100f * (item.TotalDays - item.DaysLeft) / item.TotalDays,
                CustomMinimumSize = new Vector2(200, 18)
            };
            row.AddChild(progress);

            var daysLabel = new Label { Text = $"{item.DaysLeft}/{item.TotalDays} days" };
            daysLabel.AddThemeFontSizeOverride("font_size", 12);
            daysLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            row.AddChild(daysLabel);

            var cancelBtn = new Button { Text = "×", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(26, 26) };
            cancelBtn.Pressed += () => { ArmySystem.CancelTraining(itemId); RefreshAll(); };
            UITheme.AddHoverScale(cancelBtn);
            row.AddChild(cancelBtn);
        }
    }

    // ===== 5. Ordular =====
    private void RefreshArmies()
    {
        ClearChildren(_armiesBox);

        var armies = ArmySystem.Armies;
        if (armies.Count == 0)
        {
            AddMuted(_armiesBox, "No armies");
            return;
        }

        foreach (var army in armies.ToList())
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            _armiesBox.AddChild(box);

            // Ordu başlığı
            var header = new HBoxContainer();
            header.AddThemeConstantOverride("separation", 10);
            box.AddChild(header);

            var nameLabel = new Label { Text = $"{army.Name} — Gen. {army.GeneralName} (Skill {army.GeneralSkill})", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            nameLabel.AddThemeFontSizeOverride("font_size", 14);
            nameLabel.AddThemeColorOverride("font_color", UITheme.Gold);
            box.AddChild(header);
            header.AddChild(nameLabel);

            var divCount = new Label { Text = $"{army.DivisionIds.Count} divisions" };
            divCount.AddThemeFontSizeOverride("font_size", 12);
            divCount.AddThemeColorOverride("font_color", UITheme.TextMuted);
            header.AddChild(divCount);

            // Tümenler
            foreach (var div in ArmySystem.GetDivisionsInArmy(army.Id))
                AddDivisionRow(box, div);
        }

        // Atasız tümenler
        var unassigned = ArmySystem.GetUnassignedDivisions();
        if (unassigned.Count > 0)
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            _armiesBox.AddChild(box);

            var nameLabel = new Label { Text = "UNASSIGNED" };
            nameLabel.AddThemeFontSizeOverride("font_size", 14);
            nameLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            box.AddChild(nameLabel);

            foreach (var div in unassigned)
                AddDivisionRow(box, div);
        }
    }

    private void AddDivisionRow(VBoxContainer parent, ArmySystem.Division div)
    {
        string divId = div.Id; // closure
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        parent.AddChild(row);

        var nameLabel = new Label { Text = div.TemplateName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        nameLabel.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(nameLabel);

        // ORG bar
        var orgBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = div.OrgPercent * 100,
            CustomMinimumSize = new Vector2(90, 14)
        };
        orgBar.TooltipText = $"ORG: {div.CurrentOrg}/{div.MaxOrg}";
        row.AddChild(orgBar);

        // HP bar
        var hpBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = div.HpPercent * 100,
            CustomMinimumSize = new Vector2(90, 14)
        };
        hpBar.TooltipText = $"HP: {div.CurrentHp}/{div.MaxHp}";
        hpBar.AddThemeColorOverride("fill_color", new Color(0.9f, 0.5f, 0.2f)); // HP turuncu ton
        row.AddChild(hpBar);

        var retreatLabel = new Label { Text = div.Retreats > 0 ? $"retreats: {div.Retreats}/3" : "" };
        retreatLabel.AddThemeFontSizeOverride("font_size", 11);
        retreatLabel.AddThemeColorOverride("font_color", Colors.OrangeRed);
        row.AddChild(retreatLabel);

        // Ordu seçici (yeniden ata)
        if (ArmySystem.Armies.Count > 1)
        {
            var dropdown = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 22) };
            foreach (var army in ArmySystem.Armies)
                dropdown.AddItem(army.Name);
            // Mevcut ordusu seçili
            int sel = 0;
            for (int i = 0; i < ArmySystem.Armies.Count; i++)
                if (ArmySystem.Armies[i].Id == div.ArmyId) { sel = i; break; }
            dropdown.Selected = sel;
            dropdown.ItemSelected += (idx) =>
            {
                var target = ArmySystem.Armies[(int)idx];
                ArmySystem.AssignDivisionToArmy(divId, target.Id);
                RefreshArmies();
            };
            row.AddChild(dropdown);
        }
    }

    // ===== Yardımcılar =====

    private static void ClearChildren(Node box)
    {
        foreach (Node child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void AddMuted(Node box, string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", UITheme.TextMuted);
        box.AddChild(l);
    }

    private void Close()
    {
        GD.Print("ArmyPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;

        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}