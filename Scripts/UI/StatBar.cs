using Godot;
using System;

/// <summary>
/// Reusable progress bar with label, value display, and optional color coding.
/// </summary>
[Tool]
public partial class StatBar : VBoxContainer
{
    // === Exported properties ===
    [Export] public string LabelText { get; set; } = "Stat";
    [Export] public float Value { get; set; } = 50f;
    [Export] public float MinValue { get; set; } = 0f;
    [Export] public float MaxValue { get; set; } = 100f;
    [Export] public bool ShowValue { get; set; } = true;
    [Export] public bool ShowPercent { get; set; } = true;
    [Export] public bool ColorByValue { get; set; } = true;
    [Export] public GameTheme.ColorSource ColorSource { get; set; } = GameTheme.ColorSource.Auto;
    [Export] public Color CustomFillColor { get; set; } = GameTheme.Gold;
    [Export] public int BarHeight { get; set; } = 18;
    [Export] public int LabelWidth { get; set; } = 120;
    [Export] public int ValueWidth { get; set; } = 60;

    // === Internal ===
    private Label _label;
    private ProgressBar _progressBar;
    private Label _valueLabel;
    private HBoxContainer _headerRow;

    public override void _Ready()
    {
        GameTheme.Apply(this);
        BuildUi();
    }

    private void BuildUi()
    {
        AddThemeConstantOverride("separation", GameTheme.SpaceXS);

        // Header row: label | spacer | value
        _headerRow = new HBoxContainer();
        _headerRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        AddChild(_headerRow);

        _label = new Label
        {
            Text = LabelText,
            CustomMinimumSize = new Vector2(LabelWidth, 0)
        };
        _label.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _label.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        _headerRow.AddChild(_label);

        _headerRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _valueLabel = new Label
        {
            Text = FormatValue(Value),
            CustomMinimumSize = new Vector2(ValueWidth, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _valueLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _valueLabel.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
        _valueLabel.Visible = ShowValue;
        _headerRow.AddChild(_valueLabel);

        // Progress bar
        _progressBar = new ProgressBar
        {
            MinValue = MinValue,
            MaxValue = MaxValue,
            Value = Value,
            CustomMinimumSize = new Vector2(0, BarHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        UpdateBarColor();
        AddChild(_progressBar);
    }

    public void UpdateBarColor()
    {
        Color fillColor;

        if (ColorSource == GameTheme.ColorSource.Custom)
        {
            fillColor = CustomFillColor;
        }
        else if (ColorByValue && ColorSource == GameTheme.ColorSource.Auto)
        {
            // Color based on percentage: red (0) -> yellow (50) -> green (100)
            float pct = Mathf.Clamp((Value - MinValue) / (MaxValue - MinValue), 0f, 1f);
            if (pct < 0.5f)
            {
                // Red to Yellow
                float t = pct * 2f;
                fillColor = GameTheme.Red.Lerp(GameTheme.Yellow, t);
            }
            else
            {
                // Yellow to Green
                float t = (pct - 0.5f) * 2f;
                fillColor = GameTheme.Yellow.Lerp(GameTheme.Green, t);
            }
        }
        else
        {
            fillColor = CustomFillColor;
        }

        var (bg, fill) = GameTheme.ProgressBarStyles(fillColor);
        _progressBar.AddThemeStyleboxOverride("background", bg);
        _progressBar.AddThemeStyleboxOverride("fill", fill);
    }

    private string FormatValue(float val)
    {
        if (ShowPercent && MaxValue > 0)
        {
            float pct = (val - MinValue) / (MaxValue - MinValue) * 100f;
            return $"{val:F0} ({pct:F0}%)";
        }
        return val.ToString("F0");
    }

    /// <summary>
    /// Set the value and update display.
    /// </summary>
    public void SetValue(float value)
    {
        Value = Mathf.Clamp(value, MinValue, MaxValue);
        if (_progressBar != null)
        {
            _progressBar.Value = Value;
        }
        if (_valueLabel != null)
        {
            _valueLabel.Text = FormatValue(Value);
        }
        UpdateBarColor();
    }

    /// <summary>
    /// Set min/max range.
    /// </summary>
    public void SetRange(float min, float max)
    {
        MinValue = min;
        MaxValue = max;
        if (_progressBar != null)
        {
            _progressBar.MinValue = min;
            _progressBar.MaxValue = max;
        }
        SetValue(Value); // Reclamp and update
    }

    /// <summary>
    /// Set label text.
    /// </summary>
    public void SetLabel(string text)
    {
        LabelText = text;
        if (_label != null) _label.Text = text;
    }

    /// <summary>
    /// Convenience: create a stat bar with common presets.
    /// </summary>
    public static StatBar Create(string label, float value, float max = 100f, Color? color = null)
    {
        var bar = new StatBar
        {
            LabelText = label,
            Value = value,
            MaxValue = max,
            ColorSource = color.HasValue ? GameTheme.ColorSource.Custom : GameTheme.ColorSource.Auto,
            CustomFillColor = color ?? GameTheme.Gold,
        };
        return bar;
    }
}

