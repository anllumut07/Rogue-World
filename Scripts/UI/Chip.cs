using Godot;
using System;

/// <summary>
/// Reusable chip/badge component for categories, status indicators, tags.
/// Supports icon, text, color variants, clickable/removable modes.
/// </summary>
[Tool]
public partial class Chip : Button
{
    // === Chip variants ===
    public enum Variant
    {
        Default,      // Grey/neutral
        Primary,      // Gold accent
        Success,      // Green
        Warning,      // Yellow/Orange
        Danger,       // Red
        Info,         // Blue
        Special,      // Purple
    }

    // === Exported properties ===
    [Export] public string ChipText { get; set; } = "";
    [Export] public Variant ChipVariant { get; set; } = Variant.Default;
    [Export] public bool Removable { get; set; } = false;
    [Export] public bool Clickable { get; set; } = true;
    [Export] public string ChipIcon { get; set; } = "";
    [Export] public int FontSize { get; set; } = GameTheme.FontSizeSmall;
    [Export] public bool Rounded { get; set; } = true;

    // === Internal ===
    private Label _textLabel;
    private Label _iconLabel;
    private Button _removeButton;
    private HBoxContainer _container;
    private Color _bgColor;
    private Color _borderColor;
    private Color _textColor;

    // === Signals ===
    [Signal] public delegate void RemovedEventHandler();
    [Signal] public delegate void ChipPressedEventHandler();

    public override void _Ready()
    {
        GameTheme.Apply(this);
        BuildChip();
    }

    private void BuildChip()
    {
        // Determine colors based on variant
        (_bgColor, _borderColor, _textColor) = GetVariantColors(ChipVariant);

        // Setup button (this)
        FocusMode = Clickable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        Flat = true;
        CustomMinimumSize = new Vector2(0, 24);

        // Create internal container
        _container = new HBoxContainer();
        _container.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        AddChild(_container);

        // Icon (optional)
        if (!string.IsNullOrEmpty(ChipIcon))
        {
            _iconLabel = new Label
            {
                Text = ChipIcon,
                CustomMinimumSize = new Vector2(18, 18),
                VerticalAlignment = VerticalAlignment.Center
            };
            _iconLabel.AddThemeFontSizeOverride("font_size", FontSize);
            _iconLabel.AddThemeColorOverride("font_color", _textColor);
            _container.AddChild(_iconLabel);
        }

        // Text label
        _textLabel = new Label
        {
            Text = ChipText,
            VerticalAlignment = VerticalAlignment.Center
        };
        _textLabel.AddThemeFontSizeOverride("font_size", FontSize);
        _textLabel.AddThemeColorOverride("font_color", _textColor);
        _container.AddChild(_textLabel);

        // Remove button (if removable)
        if (Removable)
        {
            _removeButton = new Button
            {
                Text = "✕",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(18, 18),
                Flat = true,
                TooltipText = "Remove"
            };
            _removeButton.AddThemeFontSizeOverride("font_size", FontSize - 1);
            _removeButton.AddThemeColorOverride("font_color", _textColor);
            _removeButton.Pressed += OnRemovePressed;
            _container.AddChild(_removeButton);
        }

        // Apply style
        ApplyStyle();

        // Connect pressed signal
        if (Clickable)
        {
            Pressed += () => EmitSignal(SignalName.ChipPressed);
        }
    }

    private (Color bg, Color border, Color text) GetVariantColors(Variant variant)
    {
        return variant switch
        {
            Variant.Primary => (new Color(GameTheme.Gold.R * 0.2f, GameTheme.Gold.G * 0.2f, GameTheme.Gold.B * 0.2f),
                                GameTheme.Gold, GameTheme.TextOnGold),
            Variant.Success => (new Color(GameTheme.Green.R * 0.2f, GameTheme.Green.G * 0.2f, GameTheme.Green.B * 0.2f),
                                GameTheme.Green, GameTheme.GreenBright),
            Variant.Warning => (new Color(GameTheme.Yellow.R * 0.2f, GameTheme.Yellow.G * 0.2f, GameTheme.Yellow.B * 0.2f),
                                GameTheme.Yellow, GameTheme.YellowDim),
            Variant.Danger => (new Color(GameTheme.Red.R * 0.2f, GameTheme.Red.G * 0.2f, GameTheme.Red.B * 0.2f),
                                GameTheme.Red, GameTheme.RedBright),
            Variant.Info => (new Color(GameTheme.Blue.R * 0.2f, GameTheme.Blue.G * 0.2f, GameTheme.Blue.B * 0.2f),
                                GameTheme.Blue, GameTheme.BlueBright),
            Variant.Special => (new Color(GameTheme.Purple.R * 0.2f, GameTheme.Purple.G * 0.2f, GameTheme.Purple.B * 0.2f),
                                GameTheme.Purple, GameTheme.PurpleBright),
            _ => (new Color("#2A2A3A"), GameTheme.BorderMedium, GameTheme.TextSecondary),
        };
    }

    private void ApplyStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = _bgColor,
            BorderColor = _borderColor,
            ContentMarginLeft = GameTheme.SpaceS,
            ContentMarginRight = GameTheme.SpaceS,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
        };
        style.SetBorderWidthAll(GameTheme.BorderThin);
        style.SetCornerRadiusAll(Rounded ? GameTheme.RadiusFull : GameTheme.RadiusSmall);
        AddThemeStyleboxOverride("normal", style);

        var hoverStyle = new StyleBoxFlat
        {
            BgColor = new Color(
                Math.Min(_bgColor.R + 0.1f, 1f),
                Math.Min(_bgColor.G + 0.1f, 1f),
                Math.Min(_bgColor.B + 0.1f, 1f)),
            BorderColor = _borderColor,
            ContentMarginLeft = GameTheme.SpaceS,
            ContentMarginRight = GameTheme.SpaceS,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
        };
        hoverStyle.SetBorderWidthAll(GameTheme.BorderThin);
        hoverStyle.SetCornerRadiusAll(Rounded ? GameTheme.RadiusFull : GameTheme.RadiusSmall);
        AddThemeStyleboxOverride("hover", hoverStyle);

        var pressedStyle = new StyleBoxFlat
        {
            BgColor = new Color(
                Math.Max(_bgColor.R - 0.1f, 0f),
                Math.Max(_bgColor.G - 0.1f, 0f),
                Math.Max(_bgColor.B - 0.1f, 0f)),
            BorderColor = _borderColor,
            ContentMarginLeft = GameTheme.SpaceS,
            ContentMarginRight = GameTheme.SpaceS,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
        };
        pressedStyle.SetBorderWidthAll(GameTheme.BorderThin);
        pressedStyle.SetCornerRadiusAll(Rounded ? GameTheme.RadiusFull : GameTheme.RadiusSmall);
        AddThemeStyleboxOverride("pressed", pressedStyle);

        var disabledStyle = new StyleBoxFlat
        {
            BgColor = new Color("#1A1A2A"),
            BorderColor = GameTheme.BorderDark,
            ContentMarginLeft = GameTheme.SpaceS,
            ContentMarginRight = GameTheme.SpaceS,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
        };
        disabledStyle.SetBorderWidthAll(GameTheme.BorderThin);
        disabledStyle.SetCornerRadiusAll(Rounded ? GameTheme.RadiusFull : GameTheme.RadiusSmall);
        AddThemeStyleboxOverride("disabled", disabledStyle);
    }

    private void OnRemovePressed()
    {
        EmitSignal(SignalName.Removed);
    }

    /// <summary>
    /// Update chip text at runtime.
    /// </summary>
    public new void SetText(string text)
    {
        ChipText = text;
        if (_textLabel != null) _textLabel.Text = text;
    }

    /// <summary>
    /// Update chip variant at runtime.
    /// </summary>
    public void SetVariant(Variant variant)
    {
        ChipVariant = variant;
        (_bgColor, _borderColor, _textColor) = GetVariantColors(variant);
        ApplyStyle();
        if (_textLabel != null) _textLabel.AddThemeColorOverride("font_color", _textColor);
        if (_iconLabel != null) _iconLabel.AddThemeColorOverride("font_color", _textColor);
        if (_removeButton != null) _removeButton.AddThemeColorOverride("font_color", _textColor);
    }

    /// <summary>
    /// Set custom colors for the chip.
    /// </summary>
    public void SetCustomColors(Color bg, Color border, Color text)
    {
        _bgColor = bg;
        _borderColor = border;
        _textColor = text;
        ApplyStyle();
        if (_textLabel != null) _textLabel.AddThemeColorOverride("font_color", _textColor);
        if (_iconLabel != null) _iconLabel.AddThemeColorOverride("font_color", _textColor);
        if (_removeButton != null) _removeButton.AddThemeColorOverride("font_color", _textColor);
    }

    /// <summary>
    /// Create a category chip (convenience method).
    /// </summary>
    public static Chip CreateCategory(string category, Color? color = null)
    {
        var chip = new Chip
        {
            ChipText = category,
            ChipVariant = Variant.Default,
            Rounded = true,
            FontSize = GameTheme.FontSizeSmall,
        };
        if (color.HasValue)
        {
            // Override with custom color
            chip._bgColor = new Color(color.Value.R * 0.2f, color.Value.G * 0.2f, color.Value.B * 0.2f);
            chip._borderColor = color.Value;
            chip._textColor = new Color(
                Math.Min(color.Value.R + 0.3f, 1f),
                Math.Min(color.Value.G + 0.3f, 1f),
                Math.Min(color.Value.B + 0.3f, 1f));
        }
        else
        {
            chip._bgColor = new Color("#2A2A3A");
            chip._borderColor = GameTheme.BorderMedium;
            chip._textColor = GameTheme.TextSecondary;
        }
        return chip;
    }

    /// <summary>
    /// Create a status chip.
    /// </summary>
    public static Chip CreateStatus(string text, Variant variant = Variant.Default)
    {
        return new Chip
        {
            ChipText = text,
            ChipVariant = variant,
            Rounded = true,
            FontSize = GameTheme.FontSizeSmall,
        };
    }
}