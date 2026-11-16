using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Reusable tooltip builder for creating rich tooltips with title, description, cost, effects, requirements.
/// Can be used as a custom tooltip panel or to generate tooltip text.
/// </summary>
public static class TooltipBuilder
{
    /// <summary>
    /// Tooltip data structure.
    /// </summary>
    public class TooltipData
    {
        public string Title = "";
        public string Description = "";
        public string Category = "";
        public Dictionary<string, string> Costs = new();           // "PP" -> "50", "Days" -> "30"
        public Dictionary<string, string> Requirements = new();    // "Stability" -> "≥ 50%"
        public Dictionary<string, string> Effects = new();         // "Stability" -> "+10%", "PP" -> "-50"
        public string Footer = "";
        public Color TitleColor = GameTheme.GoldBright;
        public Color CategoryColor = GameTheme.TextMuted;
    }

    /// <summary>
    /// Build a rich tooltip text string (for simple TooltipText property).
    /// </summary>
    public static string BuildText(TooltipData data)
    {
        var lines = new List<string>();

        // Title
        if (!string.IsNullOrEmpty(data.Title))
        {
            lines.Add($"[color=#{ColorToHex(data.TitleColor)}]{data.Title}[/color]");
        }

        // Category badge
        if (!string.IsNullOrEmpty(data.Category))
        {
            lines.Add($"[color=#{ColorToHex(data.CategoryColor)}][{data.Category}][/color]");
        }

        // Description
        if (!string.IsNullOrEmpty(data.Description))
        {
            lines.Add("");
            lines.Add(data.Description);
        }

        // Costs
        if (data.Costs.Count > 0)
        {
            lines.Add("");
            lines.Add("[color=#FFD700]Cost:[/color]");
            foreach (var kvp in data.Costs)
            {
                lines.Add($"  {kvp.Key}: {kvp.Value}");
            }
        }

        // Requirements
        if (data.Requirements.Count > 0)
        {
            lines.Add("");
            lines.Add("[color=#2196F3]Requirements:[/color]");
            foreach (var kvp in data.Requirements)
            {
                lines.Add($"  {kvp.Key}: {kvp.Value}");
            }
        }

        // Effects
        if (data.Effects.Count > 0)
        {
            lines.Add("");
            lines.Add("[color=#4CAF50]Effects:[/color]");
            foreach (var kvp in data.Effects)
            {
                string value = kvp.Value;
                Color color = value.StartsWith("+") || value.StartsWith("-") ? GameTheme.Green : GameTheme.TextPrimary;
                lines.Add($"  {kvp.Key}: [color=#{ColorToHex(color)}]{value}[/color]");
            }
        }

        // Footer
        if (!string.IsNullOrEmpty(data.Footer))
        {
            lines.Add("");
            lines.Add($"[color=#9AA3AC]{data.Footer}[/color]");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build a custom tooltip panel Control (for advanced tooltips).
    /// </summary>
    public static Control BuildPanel(TooltipData data)
    {
        var panel = new VBoxContainer();
        panel.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        GameTheme.Apply(panel);

        // Title
        if (!string.IsNullOrEmpty(data.Title))
        {
            var titleLabel = new Label();
            titleLabel.Text = data.Title;
            titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeLarge);
            titleLabel.AddThemeColorOverride("font_color", data.TitleColor);
            titleLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
            panel.AddChild(titleLabel);
        }

        // Category
        if (!string.IsNullOrEmpty(data.Category))
        {
            var catLabel = new Label();
            catLabel.Text = $"[{data.Category}]";
            catLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            catLabel.AddThemeColorOverride("font_color", data.CategoryColor);
            panel.AddChild(catLabel);
        }

        // Description
        if (!string.IsNullOrEmpty(data.Description))
        {
            panel.AddChild(new HSeparator());
            var descLabel = new Label();
            descLabel.Text = data.Description;
            descLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            descLabel.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
            descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            panel.AddChild(descLabel);
        }

        // Costs
        if (data.Costs.Count > 0)
        {
            panel.AddChild(new HSeparator());
            var costHeader = new Label();
            costHeader.Text = "COST";
            costHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            costHeader.AddThemeColorOverride("font_color", GameTheme.Gold);
            panel.AddChild(costHeader);

            foreach (var kvp in data.Costs)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", GameTheme.SpaceM);

                var keyLabel = new Label { Text = kvp.Key + ":" };
                keyLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
                keyLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
                keyLabel.CustomMinimumSize = new Vector2(100, 0);
                row.AddChild(keyLabel);

                var valLabel = new Label { Text = kvp.Value };
                valLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
                valLabel.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
                row.AddChild(valLabel);

                panel.AddChild(row);
            }
        }

        // Requirements
        if (data.Requirements.Count > 0)
        {
            panel.AddChild(new HSeparator());
            var reqHeader = new Label();
            reqHeader.Text = "REQUIREMENTS";
            reqHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            reqHeader.AddThemeColorOverride("font_color", GameTheme.Blue);
            panel.AddChild(reqHeader);

            foreach (var kvp in data.Requirements)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", GameTheme.SpaceM);

                var keyLabel = new Label { Text = kvp.Key };
                keyLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
                keyLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
                keyLabel.CustomMinimumSize = new Vector2(140, 0);
                row.AddChild(keyLabel);

                var valLabel = new Label { Text = kvp.Value };
                valLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
                valLabel.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
                row.AddChild(valLabel);

                panel.AddChild(row);
            }
        }

        // Effects
        if (data.Effects.Count > 0)
        {
            panel.AddChild(new HSeparator());
            var effHeader = new Label();
            effHeader.Text = "EFFECTS";
            effHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            effHeader.AddThemeColorOverride("font_color", GameTheme.Green);
            panel.AddChild(effHeader);

            foreach (var kvp in data.Effects)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", GameTheme.SpaceM);

                var keyLabel = new Label { Text = kvp.Key };
                keyLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
                keyLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
                keyLabel.CustomMinimumSize = new Vector2(140, 0);
                row.AddChild(keyLabel);

                var valLabel = new Label { Text = kvp.Value };
                valLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
                Color effColor = kvp.Value.StartsWith("+") ? GameTheme.Green :
                                kvp.Value.StartsWith("-") ? GameTheme.Red : GameTheme.TextPrimary;
                valLabel.AddThemeColorOverride("font_color", effColor);
                row.AddChild(valLabel);

                panel.AddChild(row);
            }
        }

        // Footer
        if (!string.IsNullOrEmpty(data.Footer))
        {
            panel.AddChild(new HSeparator());
            var footerLabel = new Label();
            footerLabel.Text = data.Footer;
            footerLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            footerLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            footerLabel.HorizontalAlignment = HorizontalAlignment.Center;
            panel.AddChild(footerLabel);
        }

        // Apply tooltip panel style
        panel.AddThemeStyleboxOverride("panel", GameTheme.TooltipStyle());

        return panel;
    }

    /// <summary>
    /// Attach a rich tooltip to a control.
    /// </summary>
    public static void Attach(Control control, TooltipData data, bool usePanel = false)
    {
        if (usePanel)
        {
            // For advanced tooltips, we'd need a custom tooltip system
            // For now, use the text-based approach
            control.TooltipText = BuildText(data);
        }
        else
        {
            control.TooltipText = BuildText(data);
        }
    }

    /// <summary>
    /// Create tooltip data from a decision.
    /// </summary>
    public static TooltipData FromDecision(DecisionSystem.Decision d)
    {
        var data = new TooltipData
        {
            Title = d.Name,
            Description = d.Description,
            Category = d.Category,
            CategoryColor = GameTheme.GetCategoryColor(d.Category),
        };

        // Costs
        if (d.CostPP > 0) data.Costs["Political Power"] = d.CostPP.ToString();
        if (d.CostDays > 0) data.Costs["Days to Complete"] = d.CostDays.ToString();
        if (d.CooldownDays > 0) data.Costs["Cooldown"] = $"{d.CooldownDays} days";

        // Requirements
        var c = d.Condition;
        if (c.MinStability > 0) data.Requirements["Stability"] = $"≥ {c.MinStability}%";
        if (c.MaxStability < 100) data.Requirements["Stability"] = $"≤ {c.MaxStability}%";
        if (c.MinWarSupport > 0) data.Requirements["War Support"] = $"≥ {c.MinWarSupport}%";
        if (c.MinPP > 0) data.Requirements["Political Power"] = $"≥ {c.MinPP}";
        if (c.MaxWorldTension < 100) data.Requirements["World Tension"] = $"≤ {c.MaxWorldTension}%";
        if (!string.IsNullOrEmpty(c.RequiredFocus)) data.Requirements["Focus Completed"] = c.RequiredFocus;
        if (!string.IsNullOrEmpty(c.RequiredNotFocus)) data.Requirements["Focus NOT Completed"] = c.RequiredNotFocus;
        if (!string.IsNullOrEmpty(c.RequiredIdeology)) data.Requirements["Ideology"] = c.RequiredIdeology;
        if (c.IsAtWar) data.Requirements["At War"] = "Yes";

        // Effects
        var e = d.Effects;
        if (e.PoliticalPower != 0) data.Effects["Political Power"] = FormatSigned(e.PoliticalPower);
        if (e.Stability != 0) data.Effects["Stability"] = $"{FormatSigned(e.Stability)}%";
        if (e.WarSupport != 0) data.Effects["War Support"] = $"{FormatSigned(e.WarSupport)}%";
        if (e.CivFactories != 0) data.Effects["Civilian Factories"] = FormatSigned(e.CivFactories);
        if (e.MilFactories != 0) data.Effects["Military Factories"] = FormatSigned(e.MilFactories);
        if (e.Dockyards != 0) data.Effects["Dockyards"] = FormatSigned(e.Dockyards);
        if (e.ResearchSlots != 0) data.Effects["Research Slots"] = FormatSigned(e.ResearchSlots);
        if (e.Manpower != 0) data.Effects["Manpower"] = FormatSigned(e.Manpower);
        if (e.Oil != 0) data.Effects["Oil"] = FormatSigned(e.Oil);
        if (e.Rubber != 0) data.Effects["Rubber"] = FormatSigned(e.Rubber);
        if (e.Steel != 0) data.Effects["Steel"] = FormatSigned(e.Steel);
        if (e.Aluminum != 0) data.Effects["Aluminum"] = FormatSigned(e.Aluminum);
        if (e.Tungsten != 0) data.Effects["Tungsten"] = FormatSigned(e.Tungsten);
        if (e.Chromium != 0) data.Effects["Chromium"] = FormatSigned(e.Chromium);
        if (e.WorldTension != 0) data.Effects["World Tension"] = $"{FormatSigned(e.WorldTension)}%";

        return data;
    }

    private static string FormatSigned(int value)
    {
        return value >= 0 ? $"+{value}" : value.ToString();
    }

    private static string FormatSigned(long value)
    {
        string sign = value >= 0 ? "+" : "";
        long abs = Math.Abs(value);
        if (abs >= 1000) return $"{sign}{abs / 1000}k";
        return $"{sign}{abs}";
    }

    private static string FormatSigned(double value)
    {
        return value >= 0 ? $"+{value:F1}" : value.ToString("F1");
    }

    private static string ColorToHex(Color c)
    {
        return $"{Mathf.RoundToInt(c.R * 255):X2}{Mathf.RoundToInt(c.G * 255):X2}{Mathf.RoundToInt(c.B * 255):X2}";
    }
}

/// <summary>
/// Custom tooltip panel that shows rich tooltips.
/// Use by setting as the tooltip for a control via code (not directly in editor).
/// </summary>
[Tool]
public partial class RichTooltipPanel : PanelContainer
{
    private VBoxContainer _content;
    private Timer _hideTimer;
    private Control _targetControl;

    public override void _Ready()
    {
        GameTheme.Apply(this);
        AddThemeStyleboxOverride("panel", GameTheme.TooltipStyle());
        MouseFilter = Control.MouseFilterEnum.Ignore;

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        AddChild(_content);

        _hideTimer = new Timer();
        _hideTimer.WaitTime = 0.1f;
        _hideTimer.OneShot = true;
        _hideTimer.Timeout += Hide;
        AddChild(_hideTimer);
    }

    public void ShowFor(Control target, TooltipBuilder.TooltipData data)
    {
        _targetControl = target;
        // Clear children
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        var builtPanel = TooltipBuilder.BuildPanel(data);
        _content.AddChild(builtPanel);

        // Position near target
        var targetRect = target.GetGlobalRect();
        var viewportSize = GetViewportRect().Size;
        var panelSize = GetRect().Size;

        Vector2 pos = targetRect.Position + new Vector2(targetRect.Size.X + 10, 0);
        if (pos.X + panelSize.X > viewportSize.X)
        {
            pos.X = targetRect.Position.X - panelSize.X - 10;
        }
        if (pos.Y + panelSize.Y > viewportSize.Y)
        {
            pos.Y = viewportSize.Y - panelSize.Y - 10;
        }

        GlobalPosition = pos;
        Show();
    }

    public void HidePanel()
    {
        _hideTimer.Stop();
        base.Hide();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
        {
            _hideTimer.Start();
        }
        else if (what == NotificationMouseEnter)
        {
            _hideTimer.Stop();
        }
    }

    // Alias for backward compatibility
    public new void Hide() => HidePanel();
}