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
        ModalHost.AddUnder(parent, panel);
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
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", 6);

            // --- Üst satır: bakanlık adı + durum (bakan adı veya "Unassigned") ---
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);

            var name = new Label { Text = ministry.Name };
            name.AddThemeFontSizeOverride("font_size", 15);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.TooltipText = ministry.Description;
            row.AddChild(name);

            string ministerId = CabinetSystem.GetMinister(countryId, ministry.Id);
            var minister = string.IsNullOrEmpty(ministerId)
                ? null
                : CabinetSystem.Ministers.FirstOrDefault(m => m.Id == ministerId);
            string ministerName = minister?.Name ?? ministerId ?? "Unassigned";
            var status = new Label
            {
                Text = string.IsNullOrEmpty(ministerId) ? "Unassigned" : ministerName
            };
            status.AddThemeFontSizeOverride("font_size", 15);
            status.AddThemeColorOverride("font_color",
                string.IsNullOrEmpty(ministerId) ? UITheme.TextMuted : UITheme.Gold);
            status.CustomMinimumSize = new Vector2(210, 0);
            row.AddChild(status);

            card.AddChild(row);

            if (string.IsNullOrEmpty(ministerId))
            {
                // --- Atanmamış: uygun adaylar, her birine ASSIGN butonu ---
                var candidates = CabinetSystem.GetAvailableMinisters(countryId, ministry.Id);
                if (candidates.Count > 0)
                {
                    foreach (var m in candidates)
                    {
                        var candRow = new HBoxContainer();
                        candRow.AddThemeConstantOverride("separation", 8);

                        var info = new Label
                        {
                            Text = $"{m.Name} — skill {m.Skill}, honesty {m.Honesty}, loyalty {m.Loyalty}, pop {m.Popularity} [{string.Join(",", m.Traits)}]"
                        };
                        info.AddThemeFontSizeOverride("font_size", 12);
                        info.AddThemeColorOverride("font_color", UITheme.TextMuted);
                        info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                        candRow.AddChild(info);

                        var assignBtn = new Button
                        {
                            Text = "ASSIGN",
                            FocusMode = Control.FocusModeEnum.None,
                            CustomMinimumSize = new Vector2(70, 26)
                        };
                        string candId = m.Id; // closure için kopya
                        string candName = m.Name;
                        assignBtn.Pressed += () =>
                        {
                            CabinetSystem.AssignMinister(countryId, ministry.Id, candId);
                            GD.Print($"[CabinetPanel] {candName} atandı → {ministry.Name}");
                            RefreshContent();
                        };
                        UITheme.AddHoverScale(assignBtn);
                        candRow.AddChild(assignBtn);

                        card.AddChild(candRow);
                    }
                }
                else
                {
                    var none = new Label { Text = "No available ministers for this ministry." };
                    none.AddThemeFontSizeOverride("font_size", 12);
                    none.AddThemeColorOverride("font_color", UITheme.TextMuted);
                    card.AddChild(none);
                }
            }
            else
            {
                // --- Atanmış: bakan nitelikleri + DISMISS butonu ---
                var stats = new Label
                {
                    Text = $"skill {minister.Skill} | honesty {minister.Honesty} | loyalty {minister.Loyalty} | pop {minister.Popularity}"
                };
                stats.AddThemeFontSizeOverride("font_size", 12);
                stats.AddThemeColorOverride("font_color", UITheme.TextMuted);
                card.AddChild(stats);

                var traits = minister.Traits != null && minister.Traits.Count > 0
                    ? $"Traits: {string.Join(", ", minister.Traits)}"
                    : "";
                if (!string.IsNullOrEmpty(traits))
                {
                    var tLabel = new Label { Text = traits };
                    tLabel.AddThemeFontSizeOverride("font_size", 11);
                    tLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
                    card.AddChild(tLabel);
                }

                var scandal = CabinetSystem.GetScandalStatus(minister.Id);
                if (!string.IsNullOrEmpty(scandal))
                {
                    var sLabel = new Label { Text = scandal };
                    sLabel.AddThemeFontSizeOverride("font_size", 11);
                    sLabel.AddThemeColorOverride("font_color", new Color(1, 0.5f, 0.5f));
                    card.AddChild(sLabel);
                }

                var dismissRow = new HBoxContainer();
                dismissRow.AddThemeConstantOverride("separation", 8);

                var dismissBtn = new Button
                {
                    Text = "DISMISS",
                    FocusMode = Control.FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(80, 28)
                };
                string minId = ministerId; // closure için kopya
                string minName = ministerName;
                dismissBtn.Pressed += () =>
                {
                    CabinetSystem.AssignMinister(countryId, ministry.Id, "");
                    GD.Print($"[CabinetPanel] {minName} görevden alındı — {ministry.Name} tekrar boş");
                    RefreshContent();
                };
                UITheme.AddHoverScale(dismissBtn);
                dismissRow.AddChild(dismissBtn);

                card.AddChild(dismissRow);
            }

            _content.AddChild(card);
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
