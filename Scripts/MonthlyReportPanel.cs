using Godot;
using System;
using System.Linq;

/// <summary>
/// Aylık Rapor Paneli (M16.3).
/// - Her ayın 1. günü TimeTempoSystem otomatik duraklatır; bu panel açılır.
/// - İstatistik özeti + uyarılar + kararlar + OK butonu.
/// - 1152x648'e sığar (560x420), backdrop MouseFilter.Stop (harita girdisi bloklanır),
///   OK → AcknowledgeMonthlyReport + kapanış.
/// - Yalnızca rapor beklerken açılır; ESC kapatmaz (bilinçli OK gerekir).
/// </summary>
public partial class MonthlyReportPanel : CanvasLayer
{
    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private Label _titleLabel;
    private Label _statsLabel;
    private Label _warningsLabel;
    private Label _decisionsLabel;
    private Button _okButton;

    private const float FadeDuration = 0.10f;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 110; // PauseMenu(100) üstünde — rapor her şeyi kapatır
        BuildUi();
        IsOpen = true;

        // Açılış animasyonu
        UITheme.FadeIn(_backdrop, FadeDuration);
        UITheme.PopIn(_panel, FadeDuration + 0.02f);

        RefreshContent();
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    // ==== Açma/Kapama ====

    public static MonthlyReportPanel OpenUnder(Node parent)
    {
        if (IsOpen) return null;

        GD.Print($"[MonthlyReportPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");
        var panel = new MonthlyReportPanel();
        ModalHost.AddUnder(parent, panel);
        return panel;
    }

    private void BuildUi()
    {
        // Backdrop: ekranı kapar, fare girdilerini yakalar (harita girdisi bloklansın)
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.78f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_backdrop);
        UITheme.ApplyTheme(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        // Panel boyutu: 1152x648'e sığar
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 420) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 22);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        _panel.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 14);
        margin.AddChild(root);

        // Başlık
        _titleLabel = new Label();
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AddThemeFontSizeOverride("font_size", 26);
        _titleLabel.AddThemeColorOverride("font_color", UITheme.GoldBright);
        root.AddChild(_titleLabel);

        root.AddChild(new HSeparator());

        // İstatistikler
        var statsHeader = new Label { Text = "ADVISOR BRIEFING" };
        statsHeader.AddThemeFontSizeOverride("font_size", 14);
        statsHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        root.AddChild(statsHeader);

        _statsLabel = new Label();
        _statsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _statsLabel.AddThemeFontSizeOverride("font_size", 14);
        _statsLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        root.AddChild(_statsLabel);

        // Uyarılar
        var warnHeader = new Label { Text = "WARNINGS" };
        warnHeader.AddThemeFontSizeOverride("font_size", 14);
        warnHeader.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.25f));
        root.AddChild(warnHeader);

        _warningsLabel = new Label();
        _warningsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _warningsLabel.AddThemeFontSizeOverride("font_size", 14);
        _warningsLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.25f));
        root.AddChild(_warningsLabel);

        // Kararlar / Aktif konular
        var decHeader = new Label { Text = "SITUATION" };
        decHeader.AddThemeFontSizeOverride("font_size", 14);
        decHeader.AddThemeColorOverride("font_color", UITheme.Gold);
        root.AddChild(decHeader);

        _decisionsLabel = new Label();
        _decisionsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _decisionsLabel.AddThemeFontSizeOverride("font_size", 14);
        _decisionsLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        root.AddChild(_decisionsLabel);

        // Spacer
        var spacer = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(spacer);

        // OK butonu (en altta ortada)
        _okButton = new Button
        {
            Text = "CONTINUE",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(160, 36)
        };
        _okButton.AddThemeFontSizeOverride("font_size", 16);
        _okButton.Pressed += OnContinuePressed;
        root.AddChild(_okButton);
        UITheme.AddHoverScale(_okButton, 1.04f);
    }

    private void RefreshContent()
    {
        var rpt = TimeTempoSystem.LastMonthlyReport;
        if (rpt == null)
        {
            _titleLabel.Text = "MONTHLY REPORT";
            _statsLabel.Text = "No data available.";
            _warningsLabel.Text = "";
            _decisionsLabel.Text = "";
            return;
        }

        _titleLabel.Text = $"MONTHLY REPORT — {rpt.Date:MMMM yyyy}";

        // İstatistikler (anahtarları okunur biçime çevir)
        var sb = new System.Text.StringBuilder();
        foreach (var kv in rpt.StatChanges)
        {
            string label = kv.Key switch
            {
                "Population" => "Population",
                "GDP" => "Est. GDP",
                "Manpower" => "Manpower Pool",
                "CivFactories" => "Civ Factories",
                "MilFactories" => "Mil Factories",
                "Dockyards" => "Dockyards",
                "Stability" => "Stability %",
                "WarSupport" => "War Support %",
                "PoliticalPower" => "Political Power",
                _ => kv.Key
            };
            sb.Append($"  • {label}: {kv.Value:N0}\n");
        }
        _statsLabel.Text = sb.ToString().TrimEnd('\n');

        // Uyarılar
        _warningsLabel.Text = rpt.Warnings.Count > 0
            ? string.Join("\n", rpt.Warnings.Select(w => $"  ⚠ {w}"))
            : "  None — all indicators nominal.";

        // Kararlar
        _decisionsLabel.Text = rpt.Decisions.Count > 0
            ? string.Join("\n", rpt.Decisions.Select(d => $"  • {d}"))
            : "  No pressing decisions.";
    }

    private void OnContinuePressed()
    {
        GD.Print("[MonthlyReportPanel] CONTINUE basıldı — rapor onaylanıyor");

        // Raporu onayla (auto-pause temizlenir)
        TimeTempoSystem.AcknowledgeMonthlyReport();

        // Kapanış animasyonu
        UITheme.FadeOut(_backdrop, FadeDuration);
        UITheme.PopOut(_panel, FadeDuration, () =>
        {
            IsOpen = false;
            QueueFree();
        });
    }
}