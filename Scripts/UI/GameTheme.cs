using Godot;
using System;

/// <summary>
/// Centralized game theme (Stage 1 UI Upgrade).
/// Provides a dark, strategy-game aesthetic inspired by HOI4/AoH:
/// - Dark base (#0B1018), gold accent (#C9A227), status colors (green/yellow/red)
/// - Consistent fonts, StyleBoxes for panels, buttons, tabs, inputs, scrollbars, progress bars, tooltips
/// - Apply via GameTheme.Apply(control) or set as project default theme
/// </summary>
public static class GameTheme
{
    // ==================== COLOR PALETTE ====================
    // Base colors
    public static readonly Color BgDarkest      = new Color("#080C12"); // Deepest background
    public static readonly Color BgDark         = new Color("#0B1018"); // Main panel background
    public static readonly Color BgMedium       = new Color("#121A26"); // Hover/secondary background
    public static readonly Color BgLight        = new Color("#1A2636"); // Pressed/active background
    public static readonly Color BgInput        = new Color("#0A121A"); // Input fields background

    // Accent colors
    public static readonly Color Gold           = new Color("#C9A227"); // Primary accent (gold)
    public static readonly Color GoldBright     = new Color("#FFD700"); // Bright gold (hover/selected)
    public static readonly Color GoldDim        = new Color("#8B6B1A"); // Dim gold (borders)

    // Text colors
    public static readonly Color TextPrimary    = new Color("#E8E0D0"); // Main text
    public static readonly Color TextSecondary  = new Color("#B8B0A0"); // Secondary text
    public static readonly Color TextMuted      = new Color("#9AA3AC"); // Muted/disabled text
    public static readonly Color TextDisabled   = new Color("#5A636D"); // Disabled text
    public static readonly Color TextOnDark     = new Color("#FFFFFF"); // Text on dark elements
    public static readonly Color TextOnGold     = new Color("#0B1018"); // Text on gold elements

    // Status colors (semantic)
    public static readonly Color Green          = new Color("#4CAF50"); // Positive/success
    public static readonly Color GreenBright    = new Color("#66BB6A");
    public static readonly Color GreenDim       = new Color("#2E7D32");
    public static readonly Color Yellow         = new Color("#FFC107"); // Warning/caution
    public static readonly Color YellowBright   = new Color("#FFD54F");
    public static readonly Color YellowDim      = new Color("#F57F17");
    public static readonly Color Red            = new Color("#F44336"); // Negative/danger
    public static readonly Color RedBright      = new Color("#EF5350");
    public static readonly Color RedDim         = new Color("#C62828");
    public static readonly Color Blue           = new Color("#2196F3"); // Info/neutral
    public static readonly Color BlueBright     = new Color("#42A5F5");
    public static readonly Color BlueDim        = new Color("#1565C0");
    public static readonly Color Purple         = new Color("#9C27B0"); // Special/tech
    public static readonly Color PurpleBright   = new Color("#BA68C8");
    public static readonly Color PurpleDim      = new Color("#7B1FA2");
    public static readonly Color Orange         = new Color("#FF9800"); // Active/progress
    public static readonly Color OrangeBright   = new Color("#FFB74D");
    public static readonly Color OrangeDim      = new Color("#E65100");

    // Border/separator
    public static readonly Color BorderDark     = new Color("#1E2A3A");
    public static readonly Color BorderMedium   = new Color("#2A3A4A");
    public static readonly Color BorderLight    = new Color("#3A4A5A");
    public static readonly Color BorderGold     = new Color("#4A4A8A"); // Gold-tinted border

    // Shadows
    public static readonly Color ShadowColor    = new Color(0, 0, 0, 0.5f);
    public static readonly Color ShadowStrong   = new Color(0, 0, 0, 0.7f);

    // ==================== FONT SIZES ====================
    public const int FontSizeTiny   = 11;
    public const int FontSizeSmall  = 12;
    public const int FontSizeNormal = 14;
    public const int FontSizeMedium = 16;
    public const int FontSizeLarge  = 18;
    public const int FontSizeXLarge = 22;
    public const int FontSizeTitle  = 28;

    // ==================== SPACING ====================
    public const int SpaceXS  = 4;
    public const int SpaceS   = 8;
    public const int SpaceM   = 12;
    public const int SpaceL   = 16;
    public const int SpaceXL  = 24;
    public const int SpaceXXL = 32;

    // ==================== CORNER RADIUS ====================
    public const int RadiusNone   = 0;
    public const int RadiusSmall  = 3;
    public const int RadiusNormal = 4;
    public const int RadiusMedium = 6;
    public const int RadiusLarge  = 8;
    public const int RadiusFull   = 999;

    // ==================== BORDER WIDTH ====================
    public const int BorderNone = 0;
    public const int BorderThin = 1;
    public const int BorderThick = 2;

    // ==================== STAT BAR COLOR SOURCE ====================
    public enum ColorSource
    {
        Auto,      // Automatic based on value (red->yellow->green)
        Custom,    // Use CustomFillColor
        Category,  // Use category color (not implemented yet)
    }

    // ==================== THEME INSTANCE ====================
    private static Theme _theme;
    private static bool _built;

    /// <summary>
    /// Get the built theme instance.
    /// </summary>
    public static Theme Get()
    {
        if (!_built) Build();
        return _theme;
    }

    /// <summary>
    /// Apply theme to a control and all its children.
    /// </summary>
    public static void Apply(Control root)
    {
        root.Theme = Get();
    }

    /// <summary>
    /// Apply theme to a control and force immediate propagation to children.
    /// </summary>
    public static void ApplyRecursive(Control root)
    {
        var theme = Get();
        root.Theme = theme;
        PropagateTheme(root, theme);
    }

    private static void PropagateTheme(Control node, Theme theme)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control control)
            {
                control.Theme = theme;
                PropagateTheme(control, theme);
            }
        }
    }

    // ==================== STYLEBOX FACTORIES ====================

    /// <summary>
    /// Create a StyleBoxFlat with common settings.
    /// </summary>
    public static StyleBoxFlat MakeBox(
        Color bg,
        Color border,
        int borderWidth = BorderThin,
        int radius = RadiusNormal,
        int marginH = 10,
        int marginV = 6)
    {
        var b = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = marginH,
            ContentMarginRight = marginH,
            ContentMarginTop = marginV,
            ContentMarginBottom = marginV,
        };
        b.SetBorderWidthAll(borderWidth);
        b.SetCornerRadiusAll(radius);
        return b;
    }

    /// <summary>
    /// Create a tab style (public for TabBar component).
    /// </summary>
    public static StyleBoxFlat MakeTabStyle(Color bg, Color border, int borderWidth = 1, int radius = 4, int marginH = 12, int marginV = 4)
    {
        var b = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = marginH,
            ContentMarginRight = marginH,
            ContentMarginTop = marginV,
            ContentMarginBottom = marginV,
        };
        b.SetBorderWidthAll(borderWidth);
        b.SetCornerRadiusAll(radius);
        return b;
    }

    /// <summary>
    /// Panel background (main panel containers).
    /// </summary>
    public static StyleBoxFlat PanelBox(
        Color? bg = null,
        Color? border = null,
        int borderWidth = BorderThin,
        int radius = RadiusMedium,
        bool shadow = true)
    {
        var b = new StyleBoxFlat
        {
            BgColor = bg ?? BgDark,
            BorderColor = border ?? Gold,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        };
        b.SetBorderWidthAll(borderWidth);
        b.SetCornerRadiusAll(radius);
        if (shadow)
        {
            b.ShadowColor = ShadowColor;
            b.ShadowSize = 6;
            b.ShadowOffset = new Vector2(0, 3);
        }
        return b;
    }

    /// <summary>
    /// Inner panel (no border, no shadow, for nested content).
    /// </summary>
    public static StyleBoxFlat InnerPanelBox(Color? bg = null, int radius = RadiusSmall)
    {
        var b = new StyleBoxFlat
        {
            BgColor = bg ?? BgDark,
            BorderColor = BorderDark,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        b.SetBorderWidthAll(BorderNone);
        b.SetCornerRadiusAll(radius);
        return b;
    }

    /// <summary>
    /// Card/Item box (for list items, decision cards, etc.).
    /// </summary>
    public static StyleBoxFlat CardBox(Color? accent = null, bool hoverable = false)
    {
        Color bg = BgDark;
        Color border = accent != null
            ? new Color(accent.Value.R * 0.3f, accent.Value.G * 0.3f, accent.Value.B * 0.3f, 1f)
            : BorderMedium;

        var b = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        b.SetBorderWidthAll(BorderThin);
        b.SetCornerRadiusAll(RadiusNormal);
        return b;
    }

    /// <summary>
    /// Button style boxes (normal/hover/pressed/disabled/focus).
    /// </summary>
    public static (StyleBoxFlat normal, StyleBoxFlat hover, StyleBoxFlat pressed, StyleBoxFlat disabled, StyleBoxFlat focus)
        ButtonStyles(Color? baseBg = null, Color? accent = null, int radius = RadiusNormal)
    {
        Color bg = baseBg ?? BgDark;
        Color acc = accent ?? Gold;
        Color accBright = GoldBright;

        var normal = MakeBox(bg, BorderDark, BorderThin, radius);
        var hover = MakeBox(BgMedium, acc, BorderThin, radius);
        var pressed = MakeBox(BgLight, accBright, BorderThin, radius);
        var disabled = MakeBox(new Color("#06080A"), new Color("#1A222A"), BorderThin, radius);
        var focus = MakeBox(new Color(0, 0, 0, 0), accBright, BorderThick, radius);

        return (normal, hover, pressed, disabled, focus);
    }

    /// <summary>
    /// Toolbar button styles (compact, for HUD).
    /// </summary>
    public static (StyleBoxFlat normal, StyleBoxFlat hover, StyleBoxFlat pressed, StyleBoxFlat disabled, StyleBoxFlat focus)
        ToolbarButtonStyles()
    {
        var normal = MakeBox(new Color("#0E1620"), BorderDark, BorderThin, RadiusSmall, 8, 4);
        var hover = MakeBox(BgMedium, Gold, BorderThin, RadiusSmall, 8, 4);
        var pressed = MakeBox(BgLight, GoldBright, BorderThin, RadiusSmall, 8, 4);
        var disabled = MakeBox(new Color("#06080A"), new Color("#1A222A"), BorderThin, RadiusSmall, 8, 4);
        var focus = MakeBox(new Color(0, 0, 0, 0), GoldBright, BorderThick, RadiusSmall, 8, 4);
        return (normal, hover, pressed, disabled, focus);
    }

    /// <summary>
    /// Tab button styles (for category tabs).
    /// </summary>
    public static (StyleBoxFlat normal, StyleBoxFlat hover, StyleBoxFlat pressed, StyleBoxFlat disabled, StyleBoxFlat focus)
        TabStyles(Color? activeBg = null, Color? activeBorder = null)
    {
        var normal = MakeBox(new Color("#1A1A2E"), new Color("#3A3A5A"), BorderThin, RadiusSmall, 12, 4);
        var hover = MakeBox(new Color("#222242"), new Color("#4A4A7A"), BorderThin, RadiusSmall, 12, 4);
        var pressed = MakeBox(activeBg ?? new Color("#2A2A4A"), activeBorder ?? new Color("#4A4A8A"), BorderThin, RadiusSmall, 12, 4);
        var disabled = MakeBox(new Color("#12121A"), new Color("#2A2A3A"), BorderThin, RadiusSmall, 12, 4);
        var focus = MakeBox(new Color(0, 0, 0, 0), GoldBright, BorderThick, RadiusSmall, 12, 4);
        return (normal, hover, pressed, disabled, focus);
    }

    /// <summary>
    /// LineEdit / TextEdit / OptionButton input styles.
    /// </summary>
    public static (StyleBoxFlat normal, StyleBoxFlat focus, StyleBoxFlat readOnly)
        InputStyles()
    {
        var normal = MakeBox(BgInput, BorderDark, BorderThin, RadiusNormal);
        var focus = MakeBox(BgInput, Gold, BorderThin, RadiusNormal);
        var readOnly = MakeBox(BgInput, BorderDark, BorderThin, RadiusNormal);
        return (normal, focus, readOnly);
    }

    /// <summary>
    /// ScrollBar styles (vertical and horizontal).
    /// </summary>
    public static (StyleBoxFlat track, StyleBoxFlat grabber, StyleBoxFlat grabberHighlight, StyleBoxFlat grabberPressed)
        ScrollBarStyles()
    {
        var track = MakeBox(new Color(0, 0, 0, 0), BorderDark, BorderNone, RadiusFull);
        var grabber = MakeBox(Gold, Gold, BorderNone, RadiusFull);
        var grabberHighlight = MakeBox(GoldBright, GoldBright, BorderNone, RadiusFull);
        var grabberPressed = MakeBox(GoldBright, GoldBright, BorderNone, RadiusFull);
        return (track, grabber, grabberHighlight, grabberPressed);
    }

    /// <summary>
    /// ProgressBar styles.
    /// </summary>
    public static (StyleBoxFlat background, StyleBoxFlat fill)
        ProgressBarStyles(Color? fillColor = null)
    {
        var background = MakeBox(new Color("#1A2A3A"), BorderDark, BorderNone, RadiusNormal);
        var fill = MakeBox(fillColor ?? Gold, fillColor ?? Gold, BorderNone, RadiusNormal);
        return (background, fill);
    }

    /// <summary>
    /// Separator styles.
    /// </summary>
    public static StyleBoxFlat SeparatorStyle(Color? color = null, int thickness = 1)
    {
        var b = new StyleBoxFlat { BgColor = color ?? BorderMedium };
        b.SetBorderWidthAll(BorderNone);
        b.SetCornerRadiusAll(BorderNone);
        b.ContentMarginTop = thickness;
        b.ContentMarginBottom = 0;
        return b;
    }

    /// <summary>
    /// Tooltip panel style.
    /// </summary>
    public static StyleBoxFlat TooltipStyle()
    {
        var b = new StyleBoxFlat
        {
            BgColor = new Color("#0E1218"),
            BorderColor = Gold,
        };
        b.SetBorderWidthAll(BorderThin);
        b.SetCornerRadiusAll(RadiusNormal);
        b.ContentMarginLeft = 12;
        b.ContentMarginRight = 12;
        b.ContentMarginTop = 10;
        b.ContentMarginBottom = 10;
        b.ShadowColor = ShadowStrong;
        b.ShadowSize = 8;
        b.ShadowOffset = new Vector2(0, 4);
        return b;
    }

    /// <summary>
    /// PopupMenu / dropdown style.
    /// </summary>
    public static StyleBoxFlat PopupStyle()
    {
        var b = new StyleBoxFlat
        {
            BgColor = BgDark,
            BorderColor = Gold,
        };
        b.SetBorderWidthAll(BorderThin);
        b.SetCornerRadiusAll(RadiusNormal);
        b.ContentMarginLeft = 8;
        b.ContentMarginRight = 8;
        b.ContentMarginTop = 4;
        b.ContentMarginBottom = 4;
        b.ShadowColor = ShadowStrong;
        b.ShadowSize = 8;
        b.ShadowOffset = new Vector2(0, 4);
        return b;
    }

    // ==================== THEME BUILDER ====================

    private static void Build()
    {
        _theme = new Theme();
        _built = true;

        // --- FONT SIZES ---
        _theme.SetFontSize("font_size", "Label", FontSizeNormal);
        _theme.SetFontSize("font_size", "Button", FontSizeNormal);
        _theme.SetFontSize("font_size", "LineEdit", FontSizeNormal);
        _theme.SetFontSize("font_size", "TextEdit", FontSizeNormal);
        _theme.SetFontSize("font_size", "CheckBox", FontSizeNormal);
        _theme.SetFontSize("font_size", "OptionButton", FontSizeNormal);
        _theme.SetFontSize("font_size", "ProgressBar", FontSizeSmall);
        _theme.SetFontSize("font_size", "TabBar", FontSizeNormal);
        _theme.SetFontSize("font_size", "MenuButton", FontSizeNormal);
        _theme.SetFontSize("font_size", "PopupMenu", FontSizeNormal);
        _theme.SetFontSize("font_size", "ItemList", FontSizeNormal);
        _theme.SetFontSize("font_size", "Tree", FontSizeNormal);
        _theme.SetFontSize("font_size", "SpinBox", FontSizeNormal);
        _theme.SetFontSize("font_size", "Slider", FontSizeNormal);

        // --- DEFAULT LABEL COLORS ---
        _theme.SetColor("font_color", "Label", TextPrimary);
        _theme.SetColor("font_outline_color", "Label", new Color(0, 0, 0, 0.8f));
        _theme.SetConstant("font_outline_size", "Label", 1);

        // --- BUTTON BASE ---
        var (btnNormal, btnHover, btnPressed, btnDisabled, btnFocus) = ButtonStyles();
        _theme.SetColor("font_color", "Button", TextPrimary);
        _theme.SetColor("font_hover_color", "Button", TextPrimary);
        _theme.SetColor("font_pressed_color", "Button", TextOnGold);
        _theme.SetColor("font_focus_color", "Button", TextOnGold);
        _theme.SetColor("font_hover_pressed_color", "Button", TextOnGold);
        _theme.SetColor("font_disabled_color", "Button", TextDisabled);
        _theme.SetColor("font_outline_color", "Button", new Color(0, 0, 0, 0.8f));
        _theme.SetConstant("outline_size", "Button", 1);
        _theme.SetStylebox("normal", "Button", btnNormal);
        _theme.SetStylebox("hover", "Button", btnHover);
        _theme.SetStylebox("pressed", "Button", btnPressed);
        _theme.SetStylebox("disabled", "Button", btnDisabled);
        _theme.SetStylebox("focus", "Button", btnFocus);

        // --- TOOLBAR BUTTON VARIATION ---
        _theme.SetTypeVariation("ToolbarButton", "Button");
        var (tbNormal, tbHover, tbPressed, tbDisabled, tbFocus) = ToolbarButtonStyles();
        _theme.SetStylebox("normal", "ToolbarButton", tbNormal);
        _theme.SetStylebox("hover", "ToolbarButton", tbHover);
        _theme.SetStylebox("pressed", "ToolbarButton", tbPressed);
        _theme.SetStylebox("disabled", "ToolbarButton", tbDisabled);
        _theme.SetStylebox("focus", "ToolbarButton", tbFocus);

        // --- PANEL CONTAINER ---
        _theme.SetStylebox("panel", "PanelContainer", PanelBox());

        _theme.SetTypeVariation("InnerPanel", "PanelContainer");
        _theme.SetStylebox("panel", "InnerPanel", InnerPanelBox());

        _theme.SetTypeVariation("CardPanel", "PanelContainer");
        _theme.SetStylebox("panel", "CardPanel", CardBox());

        // --- SEPARATOR ---
        _theme.SetStylebox("separator", "HSeparator", SeparatorStyle(color: BorderMedium, thickness: 1));
        _theme.SetStylebox("separator", "VSeparator", SeparatorStyle(color: BorderMedium, thickness: 1));
        _theme.SetConstant("separation", "HSeparator", 1);
        _theme.SetConstant("separation", "VSeparator", 1);

        // --- LINE EDIT ---
        var (leNormal, leFocus, leReadOnly) = InputStyles();
        _theme.SetStylebox("normal", "LineEdit", leNormal);
        _theme.SetStylebox("focus", "LineEdit", leFocus);
        _theme.SetStylebox("read_only", "LineEdit", leReadOnly);
        _theme.SetColor("font_color", "LineEdit", TextPrimary);
        _theme.SetColor("font_placeholder_color", "LineEdit", TextMuted);
        _theme.SetColor("font_selected_color", "LineEdit", GoldBright);
        _theme.SetColor("font_outline_color", "LineEdit", new Color(0, 0, 0, 0.8f));
        _theme.SetConstant("outline_size", "LineEdit", 1);
        _theme.SetConstant("minimum_spaces", "LineEdit", 12);
        _theme.SetConstant("placeholder_alpha", "LineEdit", 153); // 0.6 * 255

        // --- TEXT EDIT ---
        _theme.SetStylebox("normal", "TextEdit", leNormal);
        _theme.SetStylebox("focus", "TextEdit", leFocus);
        _theme.SetStylebox("read_only", "TextEdit", leReadOnly);
        _theme.SetColor("font_color", "TextEdit", TextPrimary);
        _theme.SetColor("font_selected_color", "TextEdit", GoldBright);
        _theme.SetColor("font_outline_color", "TextEdit", new Color(0, 0, 0, 0.8f));
        _theme.SetConstant("outline_size", "TextEdit", 1);

        // --- OPTION BUTTON ---
        _theme.SetStylebox("normal", "OptionButton", leNormal);
        _theme.SetStylebox("hover", "OptionButton", MakeBox(BgMedium, Gold, BorderThin, RadiusNormal));
        _theme.SetStylebox("pressed", "OptionButton", MakeBox(BgLight, GoldBright, BorderThin, RadiusNormal));
        _theme.SetStylebox("focus", "OptionButton", MakeBox(BgInput, GoldBright, BorderThick, RadiusNormal));
        _theme.SetStylebox("disabled", "OptionButton", MakeBox(new Color("#06080A"), new Color("#1A222A"), BorderThin, RadiusNormal));
        _theme.SetColor("font_color", "OptionButton", TextPrimary);
        _theme.SetColor("font_disabled_color", "OptionButton", TextDisabled);

        // --- POPUP MENU (for OptionButton dropdown) ---
        _theme.SetStylebox("panel", "PopupMenu", PopupStyle());
        _theme.SetStylebox("hover", "PopupMenu", MakeBox(BgMedium, Gold, BorderNone, RadiusSmall, 8, 4));
        _theme.SetColor("font_color", "PopupMenu", TextPrimary);
        _theme.SetColor("font_hover_color", "PopupMenu", TextOnGold);
        _theme.SetColor("font_disabled_color", "PopupMenu", TextDisabled);
        _theme.SetColor("font_outline_color", "PopupMenu", new Color(0, 0, 0, 0.8f));
        _theme.SetConstant("outline_size", "PopupMenu", 1);
        _theme.SetConstant("v_separation", "PopupMenu", 4);
        _theme.SetConstant("h_separation", "PopupMenu", 12);

        // --- CHECK BOX ---
        _theme.SetStylebox("normal", "CheckBox", MakeBox(BgInput, BorderDark, BorderThin, RadiusSmall, 16, 16));
        _theme.SetStylebox("pressed", "CheckBox", MakeBox(BgLight, GoldBright, BorderThin, RadiusSmall, 16, 16));
        _theme.SetStylebox("hover", "CheckBox", MakeBox(BgMedium, Gold, BorderThin, RadiusSmall, 16, 16));
        _theme.SetStylebox("focus", "CheckBox", MakeBox(BgInput, GoldBright, BorderThick, RadiusSmall, 16, 16));
        _theme.SetStylebox("disabled", "CheckBox", MakeBox(new Color("#06080A"), new Color("#1A222A"), BorderThin, RadiusSmall, 16, 16));
        _theme.SetConstant("check_vadjust", "CheckBox", 0);
        _theme.SetColor("font_color", "CheckBox", TextPrimary);
        _theme.SetColor("font_disabled_color", "CheckBox", TextDisabled);

        // --- RADIO BUTTON ---
        _theme.SetStylebox("normal", "RadioButton", MakeBox(BgInput, BorderDark, BorderThin, RadiusFull, 16, 16));
        _theme.SetStylebox("pressed", "RadioButton", MakeBox(BgLight, GoldBright, BorderThin, RadiusFull, 16, 16));
        _theme.SetStylebox("hover", "RadioButton", MakeBox(BgMedium, Gold, BorderThin, RadiusFull, 16, 16));
        _theme.SetStylebox("focus", "RadioButton", MakeBox(BgInput, GoldBright, BorderThick, RadiusFull, 16, 16));
        _theme.SetStylebox("disabled", "RadioButton", MakeBox(new Color("#06080A"), new Color("#1A222A"), BorderThin, RadiusFull, 16, 16));
        _theme.SetColor("font_color", "RadioButton", TextPrimary);
        _theme.SetColor("font_disabled_color", "RadioButton", TextDisabled);

        // --- H SLIDER ---
        _theme.SetStylebox("slider", "HSlider", MakeBox(new Color("#1A2A3A"), BorderDark, BorderNone, RadiusNormal));
        _theme.SetStylebox("grabber_area", "HSlider", MakeBox(Gold, Gold, BorderNone, RadiusNormal));
        _theme.SetStylebox("grabber_area_highlight", "HSlider", MakeBox(GoldBright, GoldBright, BorderNone, RadiusNormal));
        _theme.SetStylebox("grabber", "HSlider", MakeBox(GoldBright, GoldBright, BorderNone, RadiusFull, 14, 14));
        _theme.SetStylebox("grabber_highlight", "HSlider", MakeBox(GoldBright, GoldBright, BorderNone, RadiusFull, 16, 16));
        _theme.SetConstant("grabber_size", "HSlider", 16);

        // --- V SLIDER ---
        _theme.SetStylebox("slider", "VSlider", MakeBox(new Color("#1A2A3A"), BorderDark, BorderNone, RadiusNormal));
        _theme.SetStylebox("grabber_area", "VSlider", MakeBox(Gold, Gold, BorderNone, RadiusNormal));
        _theme.SetStylebox("grabber_area_highlight", "VSlider", MakeBox(GoldBright, GoldBright, BorderNone, RadiusNormal));
        _theme.SetStylebox("grabber", "VSlider", MakeBox(GoldBright, GoldBright, BorderNone, RadiusFull, 14, 14));
        _theme.SetStylebox("grabber_highlight", "VSlider", MakeBox(GoldBright, GoldBright, BorderNone, RadiusFull, 16, 16));
        _theme.SetConstant("grabber_size", "VSlider", 16);

        // --- SCROLL BAR ---
        var (sbTrack, sbGrabber, sbGrabberHi, sbGrabberPressed) = ScrollBarStyles();
        _theme.SetStylebox("scroll", "HScrollBar", sbTrack);
        _theme.SetStylebox("grabber", "HScrollBar", sbGrabber);
        _theme.SetStylebox("grabber_highlight", "HScrollBar", sbGrabberHi);
        _theme.SetStylebox("grabber_pressed", "HScrollBar", sbGrabberPressed);
        _theme.SetConstant("grabber_size", "HScrollBar", 10);

        _theme.SetStylebox("scroll", "VScrollBar", sbTrack);
        _theme.SetStylebox("grabber", "VScrollBar", sbGrabber);
        _theme.SetStylebox("grabber_highlight", "VScrollBar", sbGrabberHi);
        _theme.SetStylebox("grabber_pressed", "VScrollBar", sbGrabberPressed);
        _theme.SetConstant("grabber_size", "VScrollBar", 10);

        // --- PROGRESS BAR ---
        var (pbBg, pbFill) = ProgressBarStyles(Gold);
        _theme.SetStylebox("background", "ProgressBar", pbBg);
        _theme.SetStylebox("fill", "ProgressBar", pbFill);
        _theme.SetConstant("margin", "ProgressBar", 0);

        // --- TAB BAR ---
        var (tabNormal, tabHover, tabPressed, tabDisabled, tabFocus) = TabStyles();
        _theme.SetStylebox("tab_normal", "TabBar", tabNormal);
        _theme.SetStylebox("tab_hover", "TabBar", tabHover);
        _theme.SetStylebox("tab_selected", "TabBar", tabPressed);
        _theme.SetStylebox("tab_disabled", "TabBar", tabDisabled);
        _theme.SetStylebox("tab_focus", "TabBar", tabFocus);
        _theme.SetColor("font_color", "TabBar", TextSecondary);
        _theme.SetColor("font_hover_color", "TabBar", TextPrimary);
        _theme.SetColor("font_selected_color", "TabBar", TextOnGold);
        _theme.SetColor("font_disabled_color", "TabBar", TextDisabled);
        _theme.SetConstant("tab_separation", "TabBar", SpaceXS);
        _theme.SetConstant("tab_margin", "TabBar", SpaceS);

        // --- TAB CONTAINER ---
        _theme.SetStylebox("tab_bg", "TabContainer", MakeBox(BgDark, BorderDark, BorderThin, RadiusNone));
        _theme.SetStylebox("tab_fg", "TabContainer", MakeBox(BgDark, BorderDark, BorderThin, RadiusNone));

        // --- MENU BUTTON ---
        _theme.SetStylebox("normal", "MenuButton", MakeBox(BgDark, BorderDark, BorderThin, RadiusNormal));
        _theme.SetStylebox("hover", "MenuButton", MakeBox(BgMedium, Gold, BorderThin, RadiusNormal));
        _theme.SetStylebox("pressed", "MenuButton", MakeBox(BgLight, GoldBright, BorderThin, RadiusNormal));
        _theme.SetStylebox("focus", "MenuButton", MakeBox(BgInput, GoldBright, BorderThick, RadiusNormal));
        _theme.SetColor("font_color", "MenuButton", TextPrimary);

        // --- WINDOW / POPUP PANEL ---
        _theme.SetStylebox("panel", "Window", PanelBox(border: Gold, borderWidth: BorderThin, radius: RadiusLarge, shadow: true));

        // --- TOOLTIP ---
        _theme.SetStylebox("panel", "TooltipPanel", TooltipStyle());
        _theme.SetColor("font_color", "TooltipPanel", TextPrimary);

        // --- ITEM LIST ---
        _theme.SetStylebox("normal", "ItemList", MakeBox(BgInput, BorderDark, BorderThin, RadiusNormal));
        _theme.SetStylebox("focus", "ItemList", MakeBox(BgInput, Gold, BorderThin, RadiusNormal));
        _theme.SetStylebox("cursor", "ItemList", MakeBox(new Color(0, 0, 0, 0), GoldBright, BorderThick, RadiusNormal));
        _theme.SetStylebox("hover", "ItemList", MakeBox(BgMedium, BorderDark, BorderThin, RadiusNormal));
        _theme.SetStylebox("selected", "ItemList", MakeBox(new Color(Gold.R * 0.2f, Gold.G * 0.2f, Gold.B * 0.2f, 1f), Gold, BorderThin, RadiusNormal));
        _theme.SetStylebox("selected_focus", "ItemList", MakeBox(new Color(Gold.R * 0.3f, Gold.G * 0.3f, Gold.B * 0.3f, 1f), GoldBright, BorderThick, RadiusNormal));
        _theme.SetColor("font_color", "ItemList", TextPrimary);
        _theme.SetColor("font_hover_color", "ItemList", TextPrimary);
        _theme.SetColor("font_selected_color", "ItemList", TextOnGold);
        _theme.SetColor("font_outline_color", "ItemList", new Color(0, 0, 0, 0.8f));
        _theme.SetConstant("v_separation", "ItemList", 2);
        _theme.SetConstant("h_separation", "ItemList", 8);

        // --- TREE ---
        _theme.SetStylebox("normal", "Tree", MakeBox(BgInput, BorderDark, BorderThin, RadiusNormal));
        _theme.SetStylebox("focus", "Tree", MakeBox(BgInput, Gold, BorderThin, RadiusNormal));
        _theme.SetStylebox("cursor", "Tree", MakeBox(new Color(0, 0, 0, 0), GoldBright, BorderThick, RadiusNormal));
        _theme.SetStylebox("selected", "Tree", MakeBox(new Color(Gold.R * 0.2f, Gold.G * 0.2f, Gold.B * 0.2f, 1f), Gold, BorderThin, RadiusNormal));
        _theme.SetStylebox("custom_button", "Tree", MakeBox(BgMedium, BorderDark, BorderThin, RadiusSmall, 16, 16));
        _theme.SetStylebox("custom_button_hover", "Tree", MakeBox(BgLight, Gold, BorderThin, RadiusSmall, 16, 16));
        _theme.SetStylebox("custom_button_pressed", "Tree", MakeBox(BgLight, GoldBright, BorderThin, RadiusSmall, 16, 16));
        _theme.SetColor("font_color", "Tree", TextPrimary);
        _theme.SetColor("font_selected_color", "Tree", TextOnGold);
        _theme.SetConstant("v_separation", "Tree", 2);
        _theme.SetConstant("h_separation", "Tree", 8);

        // --- SPIN BOX ---
        _theme.SetStylebox("normal", "SpinBox", leNormal);
        _theme.SetStylebox("focus", "SpinBox", leFocus);
        _theme.SetColor("font_color", "SpinBox", TextPrimary);
        _theme.SetColor("font_disabled_color", "SpinBox", TextDisabled);

        // --- RICH TEXT LABEL ---
        _theme.SetColor("font_color", "RichTextLabel", TextPrimary);
        _theme.SetColor("font_outline_color", "RichTextLabel", new Color(0, 0, 0, 0.8f));
        _theme.SetConstant("outline_size", "RichTextLabel", 1);

        GD.Print("GameTheme: built and ready");
    }

    // ==================== HELPER METHODS ====================

    /// <summary>
    /// Configure a button with standard theme and hover scale animation.
    /// </summary>
    public static void StyleButton(Button button, bool toolbar = false, float hoverScale = 1.05f)
    {
        if (toolbar)
        {
            button.ThemeTypeVariation = "ToolbarButton";
        }
        if (hoverScale > 1.0f)
        {
            AddHoverScale(button, hoverScale);
        }
    }

    /// <summary>
    /// Configure a label with standard text color.
    /// </summary>
    public static void StyleLabel(Label label, Color? color = null, int? fontSize = null, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        label.AddThemeColorOverride("font_color", color ?? TextPrimary);
        if (fontSize.HasValue) label.AddThemeFontSizeOverride("font_size", fontSize.Value);
        label.HorizontalAlignment = alignment;
    }

    /// <summary>
    /// Add hover scale animation to a control.
    /// </summary>
    public static void AddHoverScale(Control control, float targetScale = 1.05f, float duration = 0.08f)
    {
        control.MouseEntered += () =>
        {
            var t = control.CreateTween();
            t.TweenProperty(control, "scale", new Vector2(targetScale, targetScale), duration)
             .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        };
        control.MouseExited += () =>
        {
            var t = control.CreateTween();
            t.TweenProperty(control, "scale", Vector2.One, duration)
             .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        };
    }

    /// <summary>
    /// Fade in a control (modulate alpha).
    /// </summary>
    public static Tween FadeIn(Control control, float duration = 0.12f)
    {
        control.Modulate = new Color(1, 1, 1, 0);
        control.Show();
        var t = control.CreateTween();
        t.TweenProperty(control, "modulate:a", 1f, duration)
         .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        return t;
    }

    /// <summary>
    /// Fade out a control with optional callback.
    /// </summary>
    public static Tween FadeOut(Control control, float duration = 0.1f, Action onComplete = null)
    {
        var t = control.CreateTween();
        t.TweenProperty(control, "modulate:a", 0f, duration)
         .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        if (onComplete != null) t.TweenCallback(Callable.From(onComplete));
        return t;
    }

    /// <summary>
    /// Pop in animation: fade + slight scale up.
    /// </summary>
    public static void PopIn(Control control, float duration = 0.15f)
    {
        control.Modulate = new Color(1, 1, 1, 0f);
        control.Scale = new Vector2(0.96f, 0.96f);
        control.Show();
        var t = control.CreateTween();
        t.SetParallel(true);
        t.TweenProperty(control, "modulate:a", 1f, duration)
         .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        t.TweenProperty(control, "scale", Vector2.One, duration)
         .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
    }

    /// <summary>
    /// Pop out animation: fade + slight scale down, then callback.
    /// </summary>
    public static void PopOut(Control control, float duration = 0.1f, Action onComplete = null)
    {
        var t = control.CreateTween();
        t.SetParallel(true);
        t.TweenProperty(control, "modulate:a", 0f, duration)
         .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        t.TweenProperty(control, "scale", new Vector2(0.97f, 0.97f), duration)
         .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        t.Chain().TweenCallback(Callable.From(() => onComplete?.Invoke()));
    }

    /// <summary>
    /// Get status color by name.
    /// </summary>
    public static Color GetStatusColor(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "positive" or "success" or "good" => Green,
            "negative" or "danger" or "bad" => Red,
            "warning" or "caution" => Yellow,
            "info" or "neutral" => Blue,
            "special" or "tech" => Purple,
            "active" or "progress" => Orange,
            _ => TextMuted,
        };
    }

    /// <summary>
    /// Get category color (for decision categories, etc.).
    /// </summary>
    public static Color GetCategoryColor(string category)
    {
        return category switch
        {
            "Economy" => Green,
            "Military" => Red,
            "Political" => Purple,
            "Diplomatic" => Blue,
            "Technology" => Orange,
            "General" => TextMuted,
            _ => TextMuted,
        };
    }
}