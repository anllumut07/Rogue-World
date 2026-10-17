using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Kabine paneli (M21.1): ülkenin bakanlıkları ve atanmış bakanları.
// HUD CABINET butonundan overlay olarak açılır; ESC kapatır, backdrop harita girdisini bloke eder.
public partial class CabinetPanel : Control
{
    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private VBoxContainer _content;
    private ScrollContainer _scroll;

    private const string Title = "CABINET";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[CabinetPanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new CabinetPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // paused iken de kapanış/input çalışsın
        UITheme.ApplyTheme(this);
        BuildUi();
        RefreshContent();
        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);
        GD.Print("CabinetPanel: hazır");
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

    private void BuildUi()
    {
        _backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.75f), MouseFilter = Control.MouseFilterEnum.Stop };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(860, 520) };
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
        var title = new Label { Text = Title };
        title.AddThemeFontSizeOverride("font_size", 30);
        titleRow.AddChild(title);
        titleRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        titleRow.AddChild(closeBtn);
        vbox.AddChild(titleRow);
        vbox.AddChild(new HSeparator());

        var gm = GameManager.Instance;
        var countryLabel = new Label
        {
            Text = gm != null && gm.HasPlayer && gm.PlayerCountryData != null
                ? $"{gm.PlayerCountryData.Name} ({gm.PlayerCountryData.Id}) — MINISTERS"
                : "Ülke seçili değil."
        };
        countryLabel.AddThemeFontSizeOverride("font_size", 20);
        countryLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        vbox.AddChild(countryLabel);

        // --- İçerik (kaydırılabilir) ---
        _scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(_content);
        vbox.AddChild(_scroll);

        RefreshContent();
    }

    private void RefreshContent()
    {
        if (_content == null) return;

        var children = new List<Node>();
        foreach (Node child in _content.GetChildren()) children.Add(child);
        foreach (var child in children)
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        var gm = GameManager.Instance;
        if (!gm?.HasPlayer ?? true)
        {
            var empty = new Label { Text = "Ülke seçili değil." };
            empty.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _content.AddChild(empty);
            return;
        }

        string countryId = gm.PlayerCountryData.Id;

        foreach (var ministry in CabinetSystem.Ministries)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);

            var name = new Label { Text = ministry.Name };
            name.AddThemeFontSizeOverride("font_size", 15);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.TooltipText = ministry.Description;
            row.AddChild(name);

            string ministerId = CabinetSystem.GetMinister(countryId, ministry.Id);
            var status = new Label
            {
                Text = string.IsNullOrEmpty(ministerId) ? "Unassigned" : ministerId
            };
            status.AddThemeFontSizeOverride("font_size", 15);
            status.AddThemeColorOverride("font_color",
                string.IsNullOrEmpty(ministerId) ? UITheme.TextMuted : UITheme.Gold);
            status.CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(status);

            _content.AddChild(row);
        }

        var note = new Label { Text = "Minister pool and appointment/dismissal (M21.2/M21.3): coming next. Honest minister → lower crime & corruption." };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _content.AddChild(note);
    }

    private void Close()
    {
        GD.Print("CabinetPanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}
