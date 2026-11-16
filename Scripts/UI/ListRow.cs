using Godot;
using System;

/// <summary>
/// Reusable list row component with icon slot, title, subtitle, right-side value, hover highlight.
/// Used for decision lists, country lists, etc.
/// </summary>
[Tool]
public partial class ListRow : Button
{
    // === Exported properties ===
    [Export] public string Title { get; set; } = "Item";
    [Export] public string Subtitle { get; set; } = "";
    [Export] public string RightText { get; set; } = "";
    [Export] public string IconText { get; set; } = "";
    [Export] public Color AccentColor { get; set; } = new Color(0, 0, 0, 0);
    [Export] public bool UseAccentColor { get; set; } = false;
    [Export] public bool ShowIcon { get; set; } = true;
    [Export] public int RowHeight { get; set; } = 68;
    [Export] public bool Selectable { get; set; } = true;

    // === Internal ===
    private HBoxContainer _mainContainer;
    private Label _iconLabel;
    private VBoxContainer _leftContainer;
    private Label _titleLabel;
    private Label _subtitleLabel;
    private Label _rightLabel;
    private Control _spacer;
    private StyleBoxFlat _normalStyle;
    private StyleBoxFlat _hoverStyle;
    private StyleBoxFlat _pressedStyle;

    // === Signals ===
    [Signal] public delegate void RowPressedEventHandler();
    [Signal] public delegate void RowRightClickedEventHandler(Vector2 position);

    public override void _Ready()
    {
        GameTheme.Apply(this);
        BuildRow();
    }

    private void BuildRow()
    {
        FocusMode = Selectable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        CustomMinimumSize = new Vector2(0, RowHeight);
        Flat = true;

        // Create styles - use accent color if provided, otherwise default
        Color accent = UseAccentColor && AccentColor.A > 0 ? AccentColor : new Color(0.3f, 0.3f, 0.3f, 1f);
        Color accentDim = new Color(accent.R * 0.3f, accent.G * 0.3f, accent.B * 0.3f, 1f);

        _normalStyle = GameTheme.CardBox(accent);
        _hoverStyle = GameTheme.CardBox(accent);
        _hoverStyle.BgColor = GameTheme.BgMedium;
        _pressedStyle = GameTheme.CardBox(accent);
        _pressedStyle.BgColor = GameTheme.BgLight;

        AddThemeStyleboxOverride("normal", _normalStyle);
        AddThemeStyleboxOverride("hover", _hoverStyle);
        AddThemeStyleboxOverride("pressed", _pressedStyle);
        AddThemeStyleboxOverride("focus", GameTheme.MakeBox(new Color(0,0,0,0), GameTheme.GoldBright, GameTheme.BorderThick));

        // Main container
        _mainContainer = new HBoxContainer();
        _mainContainer.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        AddChild(_mainContainer);

        // Icon slot
        if (ShowIcon)
        {
            _iconLabel = new Label
            {
                Text = IconText,
                CustomMinimumSize = new Vector2(40, 40),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _iconLabel.AddThemeFontSizeOverride("font_size", 24);
            _mainContainer.AddChild(_iconLabel);
        }

        // Left: title + subtitle
        _leftContainer = new VBoxContainer();
        _leftContainer.AddThemeConstantOverride("separation", 2);
        _leftContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _mainContainer.AddChild(_leftContainer);

        _titleLabel = new Label
        {
            Text = Title,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        _leftContainer.AddChild(_titleLabel);

        if (!string.IsNullOrEmpty(Subtitle))
        {
            _subtitleLabel = new Label
            {
                Text = Subtitle,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _subtitleLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            _subtitleLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            _leftContainer.AddChild(_subtitleLabel);
        }

        // Spacer
        _spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mainContainer.AddChild(_spacer);

        // Right text
        if (!string.IsNullOrEmpty(RightText))
        {
            _rightLabel = new Label
            {
                Text = RightText,
                HorizontalAlignment = HorizontalAlignment.Right,
                CustomMinimumSize = new Vector2(120, 0)
            };
            _rightLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            _rightLabel.AddThemeColorOverride("font_color", GameTheme.TextSecondary);
            _mainContainer.AddChild(_rightLabel);
        }

        // Connect signals
        if (Selectable)
        {
            Pressed += () => EmitSignal(SignalName.RowPressed);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right && mb.Pressed)
        {
            EmitSignal(SignalName.RowRightClicked, mb.Position);
        }
    }

    /// <summary>
    /// Update row data at runtime.
    /// </summary>
    public void SetData(string title, string subtitle = "", string rightText = "", string icon = "", Color accent = default, bool useAccent = false)
    {
        Title = title;
        Subtitle = subtitle;
        RightText = rightText;
        IconText = icon;
        AccentColor = accent;
        UseAccentColor = useAccent;

        if (_titleLabel != null) _titleLabel.Text = title;
        if (_subtitleLabel != null)
        {
            _subtitleLabel.Text = subtitle;
            _subtitleLabel.Visible = !string.IsNullOrEmpty(subtitle);
        }
        if (_rightLabel != null)
        {
            _rightLabel.Text = rightText;
            _rightLabel.Visible = !string.IsNullOrEmpty(rightText);
        }
        if (_iconLabel != null)
        {
            _iconLabel.Text = icon;
            _iconLabel.Visible = ShowIcon && !string.IsNullOrEmpty(icon);
        }

        if (useAccent)
        {
            RebuildStyles(accent);
        }
    }

    private void RebuildStyles(Color accent)
    {
        Color accentDim = new Color(accent.R * 0.3f, accent.G * 0.3f, accent.B * 0.3f, 1f);

        _normalStyle = GameTheme.CardBox(accent);
        _hoverStyle = GameTheme.CardBox(accent);
        _hoverStyle.BgColor = GameTheme.BgMedium;
        _pressedStyle = GameTheme.CardBox(accent);
        _pressedStyle.BgColor = GameTheme.BgLight;

        AddThemeStyleboxOverride("normal", _normalStyle);
        AddThemeStyleboxOverride("hover", _hoverStyle);
        AddThemeStyleboxOverride("pressed", _pressedStyle);
    }

    /// <summary>
    /// Create a decision list row (convenience).
    /// </summary>
    public static ListRow CreateDecisionRow(string name, string category, string cost, string days, Color categoryColor)
    {
        var row = new ListRow
        {
            Title = name,
            Subtitle = $"[{category}]  {cost}  |  {days} days",
            RightText = "START →",
            IconText = "",
            AccentColor = categoryColor,
            UseAccentColor = true,
            RowHeight = 68,
        };
        return row;
    }

    /// <summary>
    /// Create an active decision row with progress.
    /// </summary>
    public static ListRow CreateActiveDecisionRow(string name, string category, float progress, int daysElapsed, int totalDays, Color categoryColor)
    {
        var row = new ListRow
        {
            Title = name,
            Subtitle = $"[{category}]  Day {daysElapsed} / {totalDays}",
            RightText = $"{progress * 100:F0}%",
            AccentColor = categoryColor,
            RowHeight = 72,
        };
        return row;
    }
}