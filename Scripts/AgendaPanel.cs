using Godot;
using System;
using System.Linq;

/// <summary>
/// Haftalık Ajanda Paneli (M16.2).
/// - 6 slot/hafta: sol sütunda slot listesi (dolu/boş/kilitli).
/// - 5 aksiyon türü (Data/agenda_actions.json): sağ sütonda aksiyon kartları,
///   tıklanınca seçili slota yerleşir (PP düşer, süre başlar).
/// - Devam eden aksiyonlar altta progress ile listelenir.
/// - 1152x648'e sığar (860x560), backdrop MouseFilter.Stop, CLOSE/ESC kapatır.
/// </summary>
public partial class AgendaPanel : CanvasLayer
{
    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private VBoxContainer _slotsBox;
    private VBoxContainer _actionsBox;
    private VBoxContainer _activeBox;
    private Label _weekLabel;
    private Button _closeBtn;

    private int _selectedSlot = 0;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 100;
        BuildUi();
        IsOpen = true;

        // Açılış animasyonu
        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        RefreshAll();
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    // ESC: paneli kapat (PauseMenu Layer=100, IsOpen true iken ESC'yi bize bırakır)
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            ClosePanel();
        }
    }

    public static AgendaPanel OpenUnder(Node parent)
    {
        if (IsOpen) return null;

        GD.Print($"[AgendaPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");
        var panel = new AgendaPanel();
        ModalHost.AddUnder(parent, panel);
        return panel;
    }

    private void BuildUi()
    {
        // Backdrop: ekranı kapar, fare girdilerini yakalar (harita girdisi bloklansın)
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.75f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_backdrop);
        UITheme.ApplyTheme(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(860, 560) }; // 1152x648'e sığar
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 26);
        margin.AddThemeConstantOverride("margin_right", 26);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        _panel.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        // Başlık satırı: WEEKLY AGENDA + hafta numarası
        _weekLabel = new Label { Text = "WEEKLY AGENDA" };
        _weekLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _weekLabel.AddThemeFontSizeOverride("font_size", 24);
        _weekLabel.AddThemeColorOverride("font_color", UITheme.GoldBright);
        root.AddChild(_weekLabel);

        var hbox = new HBoxContainer();
        hbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        hbox.AddThemeConstantOverride("separation", 20);
        root.AddChild(hbox);

        // ===== SOL SÜTUN: 6 slot =====
        var leftBox = new VBoxContainer();
        leftBox.CustomMinimumSize = new Vector2(300, 0);
        leftBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        leftBox.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(leftBox);

        var slotsHeader = new Label { Text = "SLOTS (6 per week)" };
        slotsHeader.AddThemeFontSizeOverride("font_size", 14);
        slotsHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        leftBox.AddChild(slotsHeader);

        _slotsBox = new VBoxContainer();
        _slotsBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _slotsBox.AddThemeConstantOverride("separation", 6);
        leftBox.AddChild(_slotsBox);

        // ===== SAĞ SÜTUN: 5 aksiyon türü =====
        var rightBox = new VBoxContainer();
        rightBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        rightBox.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(rightBox);

        var actionsHeader = new Label { Text = "ACTIONS (click to fill selected slot)" };
        actionsHeader.AddThemeFontSizeOverride("font_size", 14);
        actionsHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        rightBox.AddChild(actionsHeader);

        var actionsScroll = new ScrollContainer();
        actionsScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        actionsScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actionsScroll.MouseFilter = Control.MouseFilterEnum.Stop;
        rightBox.AddChild(actionsScroll);

        _actionsBox = new VBoxContainer();
        _actionsBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _actionsBox.AddThemeConstantOverride("separation", 6);
        actionsScroll.AddChild(_actionsBox);

        // ===== ALT: devam eden aksiyonlar =====
        var activeHeader = new Label { Text = "IN PROGRESS" };
        activeHeader.AddThemeFontSizeOverride("font_size", 14);
        activeHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        root.AddChild(activeHeader);

        _activeBox = new VBoxContainer();
        _activeBox.AddThemeConstantOverride("separation", 4);
        root.AddChild(_activeBox);

        // ===== ALT SATIR: CLOSE =====
        var closeRow = new HBoxContainer();
        root.AddChild(closeRow);

        _closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None };
        _closeBtn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _closeBtn.Pressed += ClosePanel;
        closeRow.AddChild(_closeBtn);
        UITheme.AddHoverScale(_closeBtn);
    }

    private void RefreshAll()
    {
        RefreshWeekLabel();
        RefreshSlots();
        RefreshActions();
        RefreshActive();
    }

    private void RefreshWeekLabel()
    {
        int used = TimeTempoSystem.SlotsUsedThisWeek;
        int max = TimeTempoSystem.MaxSlotsPerWeekConst;
        _weekLabel.Text = $"WEEKLY AGENDA — Week {TimeTempoSystem.CurrentWeekNumber} ({used}/{max} slots used)";
    }

    private void RefreshSlots()
    {
        // Eski slotları temizle
        foreach (var child in _slotsBox.GetChildren())
            child.QueueFree();

        var agenda = TimeTempoSystem.WeeklyAgenda;
        for (int i = 0; i < agenda.Count; i++)
        {
            var slot = agenda[i];
            string text;
            Color color;

            if (!string.IsNullOrEmpty(slot.ActionId))
            {
                var def = TimeTempoSystem.ActionDefs.FirstOrDefault(a => a.Id == slot.ActionId);
                text = $"Slot {i + 1}: {def?.Name ?? slot.ActionId}";
                color = Colors.LightGreen;
            }
            else
            {
                text = $"Slot {i + 1}: (empty)";
                color = UITheme.TextMuted;
            }

            // Seçili slot altın çerçeve efektiyle (SelfModulate)
            var btn = new Button
            {
                Text = text,
                FocusMode = Control.FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left
            };
            btn.AddThemeFontSizeOverride("font_size", 13);
            btn.SelfModulate = (i == _selectedSlot) ? UITheme.Gold : color;

            int idx = i;
            btn.Pressed += () =>
            {
                _selectedSlot = idx;
                GD.Print($"[AgendaPanel] Slot {idx + 1} seçildi");
                RefreshSlots();
            };

            _slotsBox.AddChild(btn);
        }
    }

    private void RefreshActions()
    {
        foreach (var child in _actionsBox.GetChildren())
            child.QueueFree();

        var player = GameManager.IsAvailable ? GameManager.Instance.PlayerCountryData : null;
        double currentPP = player?.PoliticalPower ?? 0;

        foreach (var def in TimeTempoSystem.ActionDefs)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            _actionsBox.AddChild(row);

            // Aksiyon butonu
            var btn = new Button
            {
                Text = $"{def.Icon} {def.Name} ({def.CostPP} PP, {def.DurationDays}d)",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(320, 30)
            };
            btn.TooltipText = $"{def.Description}\nDuration: {def.DurationDays} days\nCost: {def.CostPP} PP";
            btn.AddThemeFontSizeOverride("font_size", 13);

            // PP yetersizse kırmızı, yeterliyse normal
            if (currentPP < def.CostPP)
                btn.SelfModulate = new Color(0.7f, 0.35f, 0.35f);

            string actionId = def.Id;
            btn.Pressed += () =>
            {
                int slotIdx = _selectedSlot;
                bool ok = TimeTempoSystem.AddActionToSlot(slotIdx, actionId);
                GD.Print($"[AgendaPanel] Aksiyon '{def.Name}' → slot {slotIdx + 1}: {(ok ? "OK" : "REDDEDİLDİ")}");
                RefreshAll();
            };
            row.AddChild(btn);
            UITheme.AddHoverScale(btn);
        }

        if (TimeTempoSystem.ActionDefs.Count == 0)
        {
            var emptyLabel = new Label { Text = "No actions loaded (Data/agenda_actions.json)" };
            emptyLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _actionsBox.AddChild(emptyLabel);
        }
    }

    private void RefreshActive()
    {
        foreach (var child in _activeBox.GetChildren())
            child.QueueFree();

        foreach (var act in TimeTempoSystem.ActiveActions)
        {
            var progress = (act.TotalDays - act.RemainingDays) * 100 / Math.Max(1, act.TotalDays);
            var label = new Label
            {
                Text = $"  • {act.ActionName}: {act.RemainingDays}/{act.TotalDays} days left ({progress}%)"
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", UITheme.TextMain);
            _activeBox.AddChild(label);
        }

        if (TimeTempoSystem.ActiveActions.Count == 0)
        {
            var emptyLabel = new Label { Text = "  No active actions." };
            emptyLabel.AddThemeFontSizeOverride("font_size", 12);
            emptyLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _activeBox.AddChild(emptyLabel);
        }
    }

    private void ClosePanel()
    {
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () =>
        {
            _backdrop.Hide();
            _panel.Hide();
            IsOpen = false;
            QueueFree();
        });
    }
}