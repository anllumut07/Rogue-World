using Godot;

// Ortak UI Teması (Milestone 2 görsel iyileştirme #1):
// - Koyu lacivert/gri panel arka planı, ince altın çerçeve
// - Tutarlı yazı boyutları ve renkler
// - Buton hover/pressed/disabled hâlleri (+ "Flat" ve "Toolbar" varyantları)
// - Slider, OptionButton/PopupMenu, ScrollBar stilleri
// Tek Theme kaynağı; her sahnenin kök Control'üne ApplyTheme() ile uygulanır
// (tema ağaç boyunca çocuklara yayılır).
public static class UITheme
{
    // --- Renk paleti (HOI4 benzeri koyu, ciddi tonlar) ---
    public static readonly Color PanelBg      = new Color("#0B1018"); // panel arka planı
    public static readonly Color PanelBgLight = new Color("#121A26"); // hover arka planı
    public static readonly Color PanelBgPress = new Color("#1A2636"); // pressed arka planı
    public static readonly Color Gold         = new Color("#C9A227"); // vurgu / çerçeve
    public static readonly Color GoldBright   = new Color("#FFD700"); // seçili / hover
    public static readonly Color TextMain    = new Color("#E8E0D0"); // ana metin
    public static readonly Color TextMuted    = new Color("#9AA3AC"); // ikincil metin
    public static readonly Color TextDisabled = new Color("#5A636D"); // disabled metin
    public static readonly Color BorderDark   = new Color("#1E2A3A"); // ince kenarlık
    public static readonly Color InputBg      = new Color("#0A121A"); // input arka planı

    private static Theme _theme;
    private static bool _built;

    public static Theme Get()
    {
        if (!_built) Build();
        return _theme;
    }

    // Temayı bir Control'e uygular (altındaki tüm çocuklara yayılır).
    public static void ApplyTheme(Control root)
    {
        root.Theme = Get();
    }

    // --- Yardımcı: StyleBoxFlat üret ---
    private static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 1, int corner = 4)
    {
        var b = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        b.SetBorderWidthAll(borderWidth);
        b.SetCornerRadiusAll(corner);
        return b;
    }

    private static void Build()
    {
        _theme = new Theme();

        // ============ FONT BOYUTLARI ============
        _theme.SetFontSize("font_size", "Label", 16);
        _theme.SetFontSize("font_size", "Button", 16);
        _theme.SetFontSize("font_size", "LineEdit", 16);
        _theme.SetFontSize("font_size", "TextEdit", 16);
        _theme.SetFontSize("font_size", "CheckBox", 16);
        _theme.SetFontSize("font_size", "OptionButton", 16);

        // ============ LABEL RENKLERİ ============
        _theme.SetColor("font_color", "Label", TextMain);
        _theme.SetColor("font_outline_color", "Label", new Color(0, 0, 0, 0.8f));

        // ============ BUTTON RENKLERİ ============
        // (CheckBox / OptionButton Button'dan kalıtım alır, ayrıca gerek yok)
        _theme.SetColor("font_color", "Button", TextMain);
        _theme.SetColor("font_hover_color", "Button", TextMain);
        _theme.SetColor("font_pressed_color", "Button", GoldBright);
        _theme.SetColor("font_focus_color", "Button", GoldBright);
        _theme.SetColor("font_hover_pressed_color", "Button", GoldBright);
        _theme.SetColor("font_disabled_color", "Button", TextDisabled);
        _theme.SetColor("font_outline_color", "Button", new Color(0, 0, 0, 0.8f));

        // ============ BUTTON STİL KUTULARI ============
        _theme.SetStylebox("normal", "Button", Box(PanelBg, BorderDark));
        _theme.SetStylebox("hover", "Button", Box(PanelBgLight, Gold));
        _theme.SetStylebox("pressed", "Button", Box(PanelBgPress, GoldBright));
        _theme.SetStylebox("disabled", "Button", Box(new Color("#080C12"), new Color("#2A3542")));
        _theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), GoldBright, 0));

        // --- "ToolbarButton" varyantı: HUD hız butonları gibi küçük toggle'lar ---
        _theme.SetTypeVariation("ToolbarButton", "Button");
        var tbNormal = Box(new Color("#0E1620"), BorderDark, 1, 3);
        tbNormal.ContentMarginLeft = 8; tbNormal.ContentMarginRight = 8;
        tbNormal.ContentMarginTop = 4; tbNormal.ContentMarginBottom = 4;
        var tbHover = Box(PanelBgLight, Gold, 1, 3);
        tbHover.ContentMarginLeft = 8; tbHover.ContentMarginRight = 8;
        tbHover.ContentMarginTop = 4; tbHover.ContentMarginBottom = 4;
        var tbPressed = Box(PanelBgPress, GoldBright, 1, 3);
        tbPressed.ContentMarginLeft = 8; tbPressed.ContentMarginRight = 8;
        tbPressed.ContentMarginTop = 4; tbPressed.ContentMarginBottom = 4;
        _theme.SetStylebox("normal", "ToolbarButton", tbNormal);
        _theme.SetStylebox("hover", "ToolbarButton", tbHover);
        _theme.SetStylebox("pressed", "ToolbarButton", tbPressed);
        _theme.SetStylebox("disabled", "ToolbarButton", Box(new Color("#080C12"), new Color("#2A3542"), 1, 3));

        // ============ PANELCONTAINER ============
        var panel = Box(PanelBg, Gold, 1, 6);
        panel.ShadowColor = new Color(0, 0, 0, 0.4f);
        panel.ShadowSize = 4;
        _theme.SetStylebox("panel", "PanelContainer", panel);

        // --- "InnerPanel" varyantı: çerçevesiz (iç içe paneller) ---
        _theme.SetTypeVariation("InnerPanel", "PanelContainer");
        var inner = Box(PanelBg, BorderDark, 0, 4);
        inner.ShadowSize = 0;
        _theme.SetStylebox("panel", "InnerPanel", inner);

        // ============ SEPARATOR ============
        _theme.SetStylebox("separator", "HSeparator", Box(BorderDark, BorderDark, 0, 0));
        _theme.SetStylebox("separator", "VSeparator", Box(BorderDark, BorderDark, 0, 0));

        // ============ LINEEDIT / TEXTEDIT / OPTIONBUTTON ============
        _theme.SetStylebox("normal", "LineEdit", Box(InputBg, BorderDark));
        _theme.SetStylebox("focus", "LineEdit", Box(InputBg, Gold));
        _theme.SetStylebox("read_only", "LineEdit", Box(InputBg, BorderDark));
        _theme.SetColor("font_color", "LineEdit", TextMain);
        _theme.SetColor("font_placeholder_color", "LineEdit", TextMuted);
        _theme.SetColor("font_selected_color", "LineEdit", GoldBright);

        _theme.SetStylebox("normal", "TextEdit", Box(InputBg, BorderDark));
        _theme.SetStylebox("focus", "TextEdit", Box(InputBg, Gold));
        _theme.SetColor("font_color", "TextEdit", TextMain);

        _theme.SetStylebox("normal", "OptionButton", Box(InputBg, BorderDark));
        _theme.SetStylebox("hover", "OptionButton", Box(PanelBgLight, Gold));
        _theme.SetStylebox("pressed", "OptionButton", Box(PanelBgPress, GoldBright));
        _theme.SetStylebox("focus", "OptionButton", Box(InputBg, GoldBright, 0));
        _theme.SetColor("font_color", "OptionButton", TextMain);

        // OptionButton'un açılır listesi (PopupMenu)
        _theme.SetStylebox("panel", "PopupMenu", Box(PanelBg, Gold, 1, 4));
        _theme.SetStylebox("hover", "PopupMenu", Box(PanelBgLight, Gold, 0, 2));
        _theme.SetColor("font_color", "PopupMenu", TextMain);
        _theme.SetColor("font_hover_color", "PopupMenu", GoldBright);
        _theme.SetColor("font_disabled_color", "PopupMenu", TextDisabled);

        // ============ CHECKBOX ============
        // Stil kutuları Button'dan miras; sadece yerleşim sabitleri:
        _theme.SetConstant("check_vadjust", "CheckBox", 0);

        // ============ HSLIDER ============
        // "slider": ray (track), "grabber_area": dolu kısım
        _theme.SetStylebox("slider", "HSlider", Box(new Color("#1A2A3A"), BorderDark, 0, 4));
        _theme.SetStylebox("grabber_area", "HSlider", Box(Gold, Gold, 0, 4));
        _theme.SetStylebox("grabber_area_highlight", "HSlider", Box(GoldBright, GoldBright, 0, 4));

        // ============ SCROLLBAR ============
        var sbBg = Box(new Color(0, 0, 0, 0), BorderDark, 0, 0);
        var sbGrab = Box(Gold, Gold, 0, 4);
        var sbGrabHi = Box(GoldBright, GoldBright, 0, 4);
        _theme.SetStylebox("scroll", "HScrollBar", sbBg);
        _theme.SetStylebox("grabber", "HScrollBar", sbGrab);
        _theme.SetStylebox("grabber_highlight", "HScrollBar", sbGrabHi);
        _theme.SetStylebox("grabber_pressed", "HScrollBar", sbGrabHi);
        _theme.SetStylebox("scroll", "VScrollBar", sbBg);
        _theme.SetStylebox("grabber", "VScrollBar", sbGrab);
        _theme.SetStylebox("grabber_highlight", "VScrollBar", sbGrabHi);
        _theme.SetStylebox("grabber_pressed", "VScrollBar", sbGrabHi);

        // ============ POPUP / WINDOW (temel) ============
        _theme.SetStylebox("panel", "Window", Box(PanelBg, Gold, 1, 6));

        _built = true;
        GD.Print("UITheme: hazır");
    }
}