using Godot;
using System;
using System.Linq;

/// <summary>
/// Weekly Agenda Panel (M16.2 + Stage 2 UI Upgrade).
/// - 6 slots/week: left column shows slot list (filled/empty/locked).
/// - 5 action types (Data/agenda_actions.json): right column action cards,
///   click to place in selected slot (PP cost, duration starts).
/// - Active actions listed at bottom with progress.
/// - Uses GameTheme + reusable UI components (GamePanel, Chip, ProgressBar, EmptyState).
/// - 1152x648 fit (860x560), backdrop blocks map input, CLOSE/ESC closes.
/// Test properties preserved for --agenda-panel-test compatibility.
/// </summary>
public partial class AgendaPanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    private VBoxContainer _slotsBox;
    private VBoxContainer _actionsBox;
    private VBoxContainer _activeBox;
    private Label _weekLabel;
    private Button _closeBtn;

    private int _selectedSlot = 0;
    private EmptyState _emptyState;

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "WEEKLY AGENDA";
        MinSize = new Vector2(860, 560);
        MaxSize = new Vector2(1200, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        IsOpen = true;

        RefreshAll();
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
            Close();
        }
    }

    public static AgendaPanel OpenUnder(Node parent)
    {
        if (IsOpen) return null;

        GD.Print($"[AgendaPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");
        var panel = new AgendaPanel();
        parent.AddChild(panel);
        return panel;
    }

    private void BuildUi()
    {
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // Week label
        _weekLabel = new Label { Text = "WEEKLY AGENDA" };
        _weekLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _weekLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeXLarge);
        _weekLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        content.AddChild(_weekLabel);

        var hbox = new HBoxContainer();
        hbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        hbox.AddThemeConstantOverride("separation", GameTheme.SpaceL);
        content.AddChild(hbox);

        // ===== LEFT COLUMN: 6 slots =====
        var leftBox = new VBoxContainer();
        leftBox.CustomMinimumSize = new Vector2(300, 0);
        leftBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        leftBox.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        hbox.AddChild(leftBox);

        var slotsHeader = new Label { Text = "SLOTS (6 per week)" };
        slotsHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        slotsHeader.AddThemeColorOverride("font_color", GameTheme.Gold);
        leftBox.AddChild(slotsHeader);

        _slotsBox = new VBoxContainer();
        _slotsBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _slotsBox.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        leftBox.AddChild(_slotsBox);

        // ===== RIGHT COLUMN: 5 action types =====
        var rightBox = new VBoxContainer();
        rightBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        rightBox.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        hbox.AddChild(rightBox);

        var actionsHeader = new Label { Text = "ACTIONS (click to fill selected slot)" };
        actionsHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        actionsHeader.AddThemeColorOverride("font_color", GameTheme.Gold);
        rightBox.AddChild(actionsHeader);

        var actionsScroll = new ScrollContainer();
        actionsScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        actionsScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actionsScroll.MouseFilter = Control.MouseFilterEnum.Stop;
        rightBox.AddChild(actionsScroll);

        _actionsBox = new VBoxContainer();
        _actionsBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _actionsBox.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        actionsScroll.AddChild(_actionsBox);

        // ===== BOTTOM: active actions =====
        var activeHeader = new Label { Text = "IN PROGRESS" };
        activeHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        activeHeader.AddThemeColorOverride("font_color", GameTheme.Gold);
        content.AddChild(activeHeader);

        _activeBox = new VBoxContainer();
        _activeBox.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        content.AddChild(_activeBox);

        // ===== BOTTOM ROW: CLOSE =====
        var closeRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        content.AddChild(closeRow);

        _closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None };
        _closeBtn.Pressed += ClosePanel;
        GameTheme.StyleButton(_closeBtn);
        closeRow.AddChild(_closeBtn);
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
                color = GameTheme.Green;
            }
            else
            {
                text = $"Slot {i + 1}: (empty)";
                color = GameTheme.TextMuted;
            }

            var btn = new Button
            {
                Text = text,
                FocusMode = Control.FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left
            };
            btn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            btn.SelfModulate = (i == _selectedSlot) ? GameTheme.Gold : color;

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
            row.AddThemeConstantOverride("separation", GameTheme.SpaceS);
            _actionsBox.AddChild(row);

            var btn = new Button
            {
                Text = $"{def.Icon} {def.Name} ({def.CostPP} PP, {def.DurationDays}d)",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(320, 30)
            };
            btn.TooltipText = $"{def.Description}\nDuration: {def.DurationDays} days\nCost: {def.CostPP} PP";
            btn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);

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
            GameTheme.AddHoverScale(btn);
        }

        if (TimeTempoSystem.ActionDefs.Count == 0)
        {
            var emptyLabel = new Label { Text = "No actions loaded (Data/agenda_actions.json)" };
            emptyLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
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
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", GameTheme.SpaceS);

            var label = new Label
            {
                Text = $"  • {act.ActionName}: {act.RemainingDays}/{act.TotalDays} days left ({progress}%)"
            };
            label.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            label.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
            row.AddChild(label);

            var bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = progress,
                CustomMinimumSize = new Vector2(120, 12)
            };
            var (bg, fill) = GameTheme.ProgressBarStyles(GameTheme.Gold);
            bar.AddThemeStyleboxOverride("background", bg);
            bar.AddThemeStyleboxOverride("fill", fill);
            row.AddChild(bar);

            _activeBox.AddChild(row);
        }

        if (TimeTempoSystem.ActiveActions.Count == 0)
        {
            var emptyLabel = new Label { Text = "  No active actions." };
            emptyLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            emptyLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            _activeBox.AddChild(emptyLabel);
        }
    }

    private void ClosePanel()
    {
        Close();
    }
}