using Godot;
using System;
using System.Linq;

/// <summary>
/// Monthly Report Panel (M16.3 + Stage 2 UI Upgrade).
/// - Every 1st of month TimeTempoSystem auto-pauses; this panel opens.
/// - Statistics summary + warnings + decisions + OK button.
/// - Uses GameTheme + reusable UI components (GamePanel, EmptyState).
/// - 1152x648 fit (560x420), backdrop MouseFilter.Stop (blocks map input),
///   OK -> AcknowledgeMonthlyReport + close.
/// - Only opens when report is pending; ESC does NOT close (intentional OK required).
/// </summary>
public partial class MonthlyReportPanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    private Label _titleLabel;
    private Label _statsLabel;
    private Label _warningsLabel;
    private Label _decisionsLabel;
    private Button _okButton;

    private const float FadeDuration = 0.10f;

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "MONTHLY REPORT";
        MinSize = new Vector2(560, 420);
        MaxSize = new Vector2(800, 600);
        Draggable = false;
        Closable = false; // No close button - must press CONTINUE
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        IsOpen = true;

        RefreshContent();
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    // ESC does NOT close this panel - must press CONTINUE button
    public override void _UnhandledInput(InputEvent e)
    {
        // Intentionally not handling ESC
    }

    public static MonthlyReportPanel OpenUnder(Node parent)
    {
        if (IsOpen) return null;

        GD.Print($"[MonthlyReportPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");
        var panel = new MonthlyReportPanel();
        parent.AddChild(panel);
        return panel;
    }

    private void BuildUi()
    {
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceL);

        // Title
        _titleLabel = new Label();
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeXLarge);
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        content.AddChild(_titleLabel);

        content.AddChild(new HSeparator());

        // Statistics
        var statsHeader = new Label { Text = "ADVISOR BRIEFING" };
        statsHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        statsHeader.AddThemeColorOverride("font_color", GameTheme.Gold);
        content.AddChild(statsHeader);

        _statsLabel = new Label();
        _statsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _statsLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _statsLabel.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        content.AddChild(_statsLabel);

        // Warnings
        var warnHeader = new Label { Text = "WARNINGS" };
        warnHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        warnHeader.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.25f));
        content.AddChild(warnHeader);

        _warningsLabel = new Label();
        _warningsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _warningsLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _warningsLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.25f));
        content.AddChild(_warningsLabel);

        // Decisions / Active topics
        var decHeader = new Label { Text = "SITUATION" };
        decHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        decHeader.AddThemeColorOverride("font_color", GameTheme.Gold);
        content.AddChild(decHeader);

        _decisionsLabel = new Label();
        _decisionsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _decisionsLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _decisionsLabel.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        content.AddChild(_decisionsLabel);

        // Spacer
        var spacer = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        content.AddChild(spacer);

        // OK button (bottom center)
        _okButton = new Button
        {
            Text = "CONTINUE",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(160, 36)
        };
        _okButton.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        _okButton.Pressed += OnContinuePressed;
        GameTheme.StyleButton(_okButton, hoverScale: 1.04f);

        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        btnRow.AddChild(_okButton);
        content.AddChild(btnRow);
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

        // Statistics (keys to readable format)
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

        // Warnings
        _warningsLabel.Text = rpt.Warnings.Count > 0
            ? string.Join("\n", rpt.Warnings.Select(w => $"  ⚠ {w}"))
            : "  None — all indicators nominal.";

        // Decisions
        _decisionsLabel.Text = rpt.Decisions.Count > 0
            ? string.Join("\n", rpt.Decisions.Select(d => $"  • {d}"))
            : "  No pressing decisions.";
    }

    private void OnContinuePressed()
    {
        GD.Print("[MonthlyReportPanel] CONTINUE pressed — report acknowledged");

        // Acknowledge report (clears auto-pause)
        TimeTempoSystem.AcknowledgeMonthlyReport();

        // Close with animation
        Close();
    }

    public override void Close()
    {
        GD.Print("MonthlyReportPanel: kapatılıyor");
        base.Close();
    }
}