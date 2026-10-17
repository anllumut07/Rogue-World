using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// İstatistik & Bütçe paneli (M20.1): oyuncu ülkesinin göstergelerini
// kategorilere ayırarak gösterir. HUD STATISTICS butonundan overlay açılır.
public partial class StatisticsPanel : Control
{
    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private VBoxContainer _content;
    private ScrollContainer _scroll;

    private const string Title = "STATISTICS & BUDGET";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[StatisticsPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");
        var panel = new StatisticsPanel();
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
        GD.Print("StatisticsPanel: hazır");
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

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(860, 560) };
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

        // Eski içeriği temizle (çıkarma sırasında koleksiyon değiştiği için önce kopyala)
        var children = new List<Node>();
        foreach (Node child in _content.GetChildren()) children.Add(child);
        foreach (var child in children)
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        var gm = GameManager.Instance;
        if (!gm.HasPlayer || gm.PlayerCountryData == null)
        {
            var empty = new Label { Text = "Ülke seçili değil." };
            empty.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _content.AddChild(empty);
            return;
        }

        string countryId = gm.PlayerCountryData.Id;
        string countryName = gm.PlayerCountryData.Name;

        var countryLabel = new Label { Text = $"{countryName} ({countryId}) — GÖSTERGELER" };
        countryLabel.AddThemeFontSizeOverride("font_size", 20);
        countryLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _content.AddChild(countryLabel);

        foreach (var catGroup in StatisticsSystem.Definitions.GroupBy(d => d.Category))
        {
            var catHeader = new Label { Text = catGroup.Key.ToUpper() };
            catHeader.AddThemeFontSizeOverride("font_size", 17);
            catHeader.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _content.AddChild(catHeader);

            foreach (var def in catGroup.OrderBy(d => d.Name))
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 10);

                var name = new Label { Text = def.Name };
                name.AddThemeFontSizeOverride("font_size", 15);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                name.TooltipText = def.Description;
                row.AddChild(name);

                double value = StatisticsSystem.GetValue(countryId, def.Id);
                var valueLbl = new Label { Text = $"{value:N1}" };
                valueLbl.AddThemeFontSizeOverride("font_size", 15);
                valueLbl.AddThemeColorOverride("font_color", UITheme.Gold);
                valueLbl.CustomMinimumSize = new Vector2(70, 0);
                row.AddChild(valueLbl);

                double span = def.Max - def.Min;
                double pct = span > 0 ? (value - def.Min) / span * 100.0 : 0;
                pct = Math.Clamp(pct, 0, 100);
                var bar = new ProgressBar
                {
                    MinValue = 0,
                    MaxValue = 100,
                    Value = pct,
                    CustomMinimumSize = new Vector2(140, 16)
                };
                row.AddChild(bar);

                _content.AddChild(row);
            }
        }

        var note = new Label { Text = "Göstergeler her oyun günde deterministik olarak dalganır; Değerler [Min, Max] aralığında sınırlıdır." };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _content.AddChild(note);
    }

    private void Close()
    {
        GD.Print("StatisticsPanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}
