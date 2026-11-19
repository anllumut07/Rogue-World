@tool
extends Resource
class_name UITheme

## UITheme.gd - Godot 4 Theme Extension for Rogue World
## This extends the C# GameTheme with Godot-native theme overrides for UI panels

## Color palette (matching GameTheme.cs)
const BG_DARKEST = Color("#080C12")
const BG_DARK = Color("#0B1018")
const BG_MEDIUM = Color("#121A26")
const BG_LIGHT = Color("#1A2636")
const BG_INPUT = Color("#0A121A")

const GOLD = Color("#C9A227")
const GOLD_BRIGHT = Color("#FFD700")
const GOLD_DIM = Color("#8B6B1A")

const TEXT_PRIMARY = Color("#E8E0D0")
const TEXT_SECONDARY = Color("#B8B0A0")
const TEXT_MUTED = Color("#9AA3AC")
const TEXT_DISABLED = Color("#5A636D")
const TEXT_ON_DARK = Color("#FFFFFF")
const TEXT_ON_GOLD = Color("#0B1018")

const GREEN = Color("#4CAF50")
const GREEN_BRIGHT = Color("#66BB6A")
const YELLOW = Color("#FFC107")
const RED = Color("#F44336")
const BLUE = Color("#2196F3")
const PURPLE = Color("#9C27B0")
const ORANGE = Color("#FF9800")

const BORDER_DARK = Color("#1E2A3A")
const BORDER_MEDIUM = Color("#2A3A4A")
const BORDER_LIGHT = Color("#3A4A5A")
const BORDER_GOLD = Color("#4A4A8A")

const SHADOW = Color(0, 0, 0, 0.5)
const SHADOW_STRONG = Color(0, 0, 0, 0.7)

## Font sizes
const FONT_TINY = 11
const FONT_SMALL = 12
const FONT_NORMAL = 14
const FONT_MEDIUM = 16
const FONT_LARGE = 18
const FONT_XLARGE = 22
const FONT_TITLE = 28

## Spacing
const SPACE_XS = 4
const SPACE_S = 8
const SPACE_M = 12
const SPACE_L = 16
const SPACE_XL = 24
const SPACE_XXL = 32

## Corner radius
const RADIUS_NONE = 0
const RADIUS_SMALL = 3
const RADIUS_NORMAL = 4
const RADIUS_MEDIUM = 6
const RADIUS_LARGE = 8
const RADIUS_FULL = 999

## Border width
const BORDER_NONE = 0
const BORDER_THIN = 1
const BORDER_THICK = 2

## Theme instance
static var _theme: Theme = null
static var _built: bool = false

func _init():
	build_theme()

static func build_theme() -> void:
	if _built:
		return

	_theme = Theme.new()
	_built = true

	# Font sizes for all common controls
	var controls = [
		"Label", "Button", "LineEdit", "TextEdit", "CheckBox", "OptionButton",
		"ProgressBar", "TabBar", "MenuButton", "PopupMenu", "ItemList", "Tree",
		"SpinBox", "Slider", "RichTextLabel"
	]
	for control in controls:
		_theme.set_font_size("font_size", control, FONT_NORMAL)

	# Default label colors
	_theme.set_color("font_color", "Label", TEXT_PRIMARY)
	_theme.set_color("font_outline_color", "Label", Color(0, 0, 0, 0.8))
	_theme.set_constant("font_outline_size", "Label", 1)

	# Button base styles
	var btn_styles = _make_button_styles()
	_theme.set_color("font_color", "Button", TEXT_PRIMARY)
	_theme.set_color("font_hover_color", "Button", TEXT_PRIMARY)
	_theme.set_color("font_pressed_color", "Button", TEXT_ON_GOLD)
	_theme.set_color("font_focus_color", "Button", TEXT_ON_GOLD)
	_theme.set_color("font_hover_pressed_color", "Button", TEXT_ON_GOLD)
	_theme.set_color("font_disabled_color", "Button", TEXT_DISABLED)
	_theme.set_constant("outline_size", "Button", 1)
	_theme.set_stylebox("normal", "Button", btn_styles[0])
	_theme.set_stylebox("hover", "Button", btn_styles[1])
	_theme.set_stylebox("pressed", "Button", btn_styles[2])
	_theme.set_stylebox("disabled", "Button", btn_styles[3])
	_theme.set_stylebox("focus", "Button", btn_styles[4])

	# Toolbar button variation
	_theme.set_type_variation("ToolbarButton", "Button")
	var tb_styles = _make_toolbar_button_styles()
	_theme.set_stylebox("normal", "ToolbarButton", tb_styles[0])
	_theme.set_stylebox("hover", "ToolbarButton", tb_styles[1])
	_theme.set_stylebox("pressed", "ToolbarButton", tb_styles[2])
	_theme.set_stylebox("disabled", "ToolbarButton", tb_styles[3])
	_theme.set_stylebox("focus", "ToolbarButton", tb_styles[4])

	# Header label variation
	_theme.set_type_variation("Header", "Label")
	_theme.set_color("font_color", "Header", GOLD)
	_theme.set_font_size("font_size", "Header", FONT_MEDIUM)

	# Title label variation
	_theme.set_type_variation("Title", "Label")
	_theme.set_color("font_color", "Title", TEXT_PRIMARY)
	_theme.set_font_size("font_size", "Title", FONT_TITLE)

	# Positive/Negative label variations
	_theme.set_type_variation("Positive", "Label")
	_theme.set_color("font_color", "Positive", GREEN_BRIGHT)
	_theme.set_font_size("font_size", "Positive", FONT_SMALL)

	_theme.set_type_variation("Negative", "Label")
	_theme.set_color("font_color", "Negative", RED)
	_theme.set_font_size("font_size", "Negative", FONT_SMALL)

	# Panel container
	_theme.set_stylebox("panel", "PanelContainer", _make_panel_box())

	# Inner panel variation
	_theme.set_type_variation("InnerPanel", "PanelContainer")
	_theme.set_stylebox("panel", "InnerPanel", _make_inner_panel_box())

	# Card panel variation
	_theme.set_type_variation("CardPanel", "PanelContainer")
	_theme.set_stylebox("panel", "CardPanel", _make_card_box())

	# Separator
	_theme.set_stylebox("separator", "HSeparator", _make_separator())
	_theme.set_stylebox("separator", "VSeparator", _make_separator())

	# LineEdit
	var le_styles = _make_input_styles()
	_theme.set_stylebox("normal", "LineEdit", le_styles[0])
	_theme.set_stylebox("focus", "LineEdit", le_styles[1])
	_theme.set_stylebox("read_only", "LineEdit", le_styles[2])
	_theme.set_color("font_color", "LineEdit", TEXT_PRIMARY)
	_theme.set_color("font_placeholder_color", "LineEdit", TEXT_MUTED)
	_theme.set_color("font_selected_color", "LineEdit", GOLD_BRIGHT)
	_theme.set_constant("outline_size", "LineEdit", 1)
	_theme.set_constant("minimum_spaces", "LineEdit", 12)

	# TextEdit
	_theme.set_stylebox("normal", "TextEdit", le_styles[0])
	_theme.set_stylebox("focus", "TextEdit", le_styles[1])
	_theme.set_stylebox("read_only", "TextEdit", le_styles[2])
	_theme.set_color("font_color", "TextEdit", TEXT_PRIMARY)
	_theme.set_color("font_selected_color", "TextEdit", GOLD_BRIGHT)
	_theme.set_constant("outline_size", "TextEdit", 1)

	# OptionButton
	_theme.set_stylebox("normal", "OptionButton", le_styles[0])
	_theme.set_stylebox("hover", "OptionButton", _make_box(BG_MEDIUM, GOLD))
	_theme.set_stylebox("pressed", "OptionButton", _make_box(BG_LIGHT, GOLD_BRIGHT))
	_theme.set_stylebox("focus", "OptionButton", _make_box(BG_INPUT, GOLD_BRIGHT, BORDER_THICK))
	_theme.set_stylebox("disabled", "OptionButton", _make_box(Color("#06080A"), Color("#1A222A")))
	_theme.set_color("font_color", "OptionButton", TEXT_PRIMARY)
	_theme.set_color("font_disabled_color", "OptionButton", TEXT_DISABLED)

	# PopupMenu
	_theme.set_stylebox("panel", "PopupMenu", _make_popup_style())
	_theme.set_stylebox("hover", "PopupMenu", _make_box(BG_MEDIUM, GOLD, BORDER_NONE, RADIUS_SMALL, 8, 4))
	_theme.set_color("font_color", "PopupMenu", TEXT_PRIMARY)
	_theme.set_color("font_hover_color", "PopupMenu", TEXT_ON_GOLD)
	_theme.set_color("font_disabled_color", "PopupMenu", TEXT_DISABLED)
	_theme.set_constant("v_separation", "PopupMenu", 4)
	_theme.set_constant("h_separation", "PopupMenu", 12)

	# CheckBox
	_theme.set_stylebox("normal", "CheckBox", _make_box(BG_INPUT, BORDER_DARK, BORDER_THIN, RADIUS_SMALL, 16, 16))
	_theme.set_stylebox("pressed", "CheckBox", _make_box(BG_LIGHT, GOLD_BRIGHT, BORDER_THIN, RADIUS_SMALL, 16, 16))
	_theme.set_stylebox("hover", "CheckBox", _make_box(BG_MEDIUM, GOLD, BORDER_THIN, RADIUS_SMALL, 16, 16))
	_theme.set_stylebox("focus", "CheckBox", _make_box(BG_INPUT, GOLD_BRIGHT, BORDER_THICK, RADIUS_SMALL, 16, 16))
	_theme.set_stylebox("disabled", "CheckBox", _make_box(Color("#06080A"), Color("#1A222A"), BORDER_THIN, RADIUS_SMALL, 16, 16))
	_theme.set_constant("check_vadjust", "CheckBox", 0)
	_theme.set_color("font_color", "CheckBox", TEXT_PRIMARY)
	_theme.set_color("font_disabled_color", "CheckBox", TEXT_DISABLED)

	# RadioButton
	_theme.set_stylebox("normal", "RadioButton", _make_box(BG_INPUT, BORDER_DARK, BORDER_THIN, RADIUS_FULL, 16, 16))
	_theme.set_stylebox("pressed", "RadioButton", _make_box(BG_LIGHT, GOLD_BRIGHT, BORDER_THIN, RADIUS_FULL, 16, 16))
	_theme.set_stylebox("hover", "RadioButton", _make_box(BG_MEDIUM, GOLD, BORDER_THIN, RADIUS_FULL, 16, 16))
	_theme.set_stylebox("focus", "RadioButton", _make_box(BG_INPUT, GOLD_BRIGHT, BORDER_THICK, RADIUS_FULL, 16, 16))
	_theme.set_stylebox("disabled", "RadioButton", _make_box(Color("#06080A"), Color("#1A222A"), BORDER_THIN, RADIUS_FULL, 16, 16))
	_theme.set_color("font_color", "RadioButton", TEXT_PRIMARY)
	_theme.set_color("font_disabled_color", "RadioButton", TEXT_DISABLED)

	# ProgressBar
	var pb_styles = _make_progress_bar_styles(GOLD)
	_theme.set_stylebox("background", "ProgressBar", pb_styles[0])
	_theme.set_stylebox("fill", "ProgressBar", pb_styles[1])
	_theme.set_constant("margin", "ProgressBar", 0)

	# TabBar
	var tab_styles = _make_tab_styles()
	_theme.set_stylebox("tab_normal", "TabBar", tab_styles[0])
	_theme.set_stylebox("tab_hover", "TabBar", tab_styles[1])
	_theme.set_stylebox("tab_selected", "TabBar", tab_styles[2])
	_theme.set_stylebox("tab_disabled", "TabBar", tab_styles[3])
	_theme.set_stylebox("tab_focus", "TabBar", tab_styles[4])
	_theme.set_color("font_color", "TabBar", TEXT_SECONDARY)
	_theme.set_color("font_hover_color", "TabBar", TEXT_PRIMARY)
	_theme.set_color("font_selected_color", "TabBar", TEXT_ON_GOLD)
	_theme.set_color("font_disabled_color", "TabBar", TEXT_DISABLED)
	_theme.set_constant("tab_separation", "TabBar", SPACE_XS)
	_theme.set_constant("tab_margin", "TabBar", SPACE_S)

	# TabContainer
	_theme.set_stylebox("tab_bg", "TabContainer", _make_box(BG_DARK, BORDER_DARK, BORDER_THIN, RADIUS_NONE))
	_theme.set_stylebox("tab_fg", "TabContainer", _make_box(BG_DARK, BORDER_DARK, BORDER_THIN, RADIUS_NONE))

	# ScrollBar
	var sb_styles = _make_scrollbar_styles()
	_theme.set_stylebox("scroll", "HScrollBar", sb_styles[0])
	_theme.set_stylebox("grabber", "HScrollBar", sb_styles[1])
	_theme.set_stylebox("grabber_highlight", "HScrollBar", sb_styles[2])
	_theme.set_stylebox("grabber_pressed", "HScrollBar", sb_styles[3])
	_theme.set_constant("grabber_size", "HScrollBar", 10)

	_theme.set_stylebox("scroll", "VScrollBar", sb_styles[0])
	_theme.set_stylebox("grabber", "VScrollBar", sb_styles[1])
	_theme.set_stylebox("grabber_highlight", "VScrollBar", sb_styles[2])
	_theme.set_stylebox("grabber_pressed", "VScrollBar", sb_styles[3])
	_theme.set_constant("grabber_size", "VScrollBar", 10)

	# ItemList
	_theme.set_stylebox("normal", "ItemList", _make_box(BG_INPUT, BORDER_DARK))
	_theme.set_stylebox("focus", "ItemList", _make_box(BG_INPUT, GOLD))
	_theme.set_stylebox("cursor", "ItemList", _make_box(Color(0,0,0,0), GOLD_BRIGHT, BORDER_THICK))
	_theme.set_stylebox("hover", "ItemList", _make_box(BG_MEDIUM, BORDER_DARK))
	_theme.set_stylebox("selected", "ItemList", _make_box(Color(GOLD.r*0.2, GOLD.g*0.2, GOLD.b*0.2, 1), GOLD))
	_theme.set_stylebox("selected_focus", "ItemList", _make_box(Color(GOLD.r*0.3, GOLD.g*0.3, GOLD.b*0.3, 1), GOLD_BRIGHT, BORDER_THICK))
	_theme.set_color("font_color", "ItemList", TEXT_PRIMARY)
	_theme.set_color("font_hover_color", "ItemList", TEXT_PRIMARY)
	_theme.set_color("font_selected_color", "ItemList", TEXT_ON_GOLD)
	_theme.set_constant("v_separation", "ItemList", 2)
	_theme.set_constant("h_separation", "ItemList", 8)

	# Tree
	_theme.set_stylebox("normal", "Tree", _make_box(BG_INPUT, BORDER_DARK))
	_theme.set_stylebox("focus", "Tree", _make_box(BG_INPUT, GOLD))
	_theme.set_stylebox("cursor", "Tree", _make_box(Color(0,0,0,0), GOLD_BRIGHT, BORDER_THICK))
	_theme.set_stylebox("selected", "Tree", _make_box(Color(GOLD.r*0.2, GOLD.g*0.2, GOLD.b*0.2, 1), GOLD))
	_theme.set_stylebox("custom_button", "Tree", _make_box(BG_MEDIUM, BORDER_DARK, BORDER_THIN, RADIUS_SMALL, 16, 16))
	_theme.set_stylebox("custom_button_hover", "Tree", _make_box(BG_LIGHT, GOLD, BORDER_THIN, RADIUS_SMALL, 16, 16))
	_theme.set_stylebox("custom_button_pressed", "Tree", _make_box(BG_LIGHT, GOLD_BRIGHT, BORDER_THIN, RADIUS_SMALL, 16, 16))
	_theme.set_color("font_color", "Tree", TEXT_PRIMARY)
	_theme.set_color("font_selected_color", "Tree", TEXT_ON_GOLD)
	_theme.set_constant("v_separation", "Tree", 2)
	_theme.set_constant("h_separation", "Tree", 8)

	# Window/Popup
	_theme.set_stylebox("panel", "Window", _make_panel_box(GOLD, GOLD, BORDER_THIN, RADIUS_LARGE, true))

	# Tooltip
	_theme.set_stylebox("panel", "TooltipPanel", _make_tooltip_style())
	_theme.set_color("font_color", "TooltipPanel", TEXT_PRIMARY)

	# RichTextLabel
	_theme.set_color("font_color", "RichTextLabel", TEXT_PRIMARY)
	_theme.set_color("font_outline_color", "RichTextLabel", Color(0,0,0,0.8))
	_theme.set_constant("outline_size", "RichTextLabel", 1)

static func get_theme() -> Theme:
	if not _built:
		build_theme()
	return _theme

## StyleBox factories (static - can be called as UITheme._make_...)

static func _make_box(bg: Color, border: Color, border_width: int = BORDER_THIN, radius: int = RADIUS_NORMAL, margin_h: int = 10, margin_v: int = 6) -> StyleBoxFlat:
	var box = StyleBoxFlat.new()
	box.bg_color = bg
	box.border_color = border
	box.set_border_width_all(border_width)
	box.set_corner_radius_all(radius)
	box.content_margin_left = margin_h
	box.content_margin_right = margin_h
	box.content_margin_top = margin_v
	box.content_margin_bottom = margin_v
	return box

static func _make_panel_box(bg = null, border = null, border_width: int = BORDER_THIN, radius: int = RADIUS_MEDIUM, shadow: bool = true) -> StyleBoxFlat:
	var box = StyleBoxFlat.new()
	box.bg_color = bg if bg else BG_DARK
	box.border_color = border if border else GOLD
	box.content_margin_left = 16
	box.content_margin_right = 16
	box.content_margin_top = 12
	box.content_margin_bottom = 12
	box.set_border_width_all(border_width)
	box.set_corner_radius_all(radius)
	if shadow:
		box.shadow_color = SHADOW
		box.shadow_size = 6
		box.shadow_offset = Vector2(0, 3)
	return box

static func _make_inner_panel_box(bg = null, radius: int = RADIUS_SMALL) -> StyleBoxFlat:
	var box = StyleBoxFlat.new()
	box.bg_color = bg if bg else BG_DARK
	box.border_color = BORDER_DARK
	box.content_margin_left = 8
	box.content_margin_right = 8
	box.content_margin_top = 6
	box.content_margin_bottom = 6
	box.set_border_width_all(BORDER_NONE)
	box.set_corner_radius_all(radius)
	return box

static func _make_card_box(accent = null, hoverable: bool = false) -> StyleBoxFlat:
	var box = StyleBoxFlat.new()
	box.bg_color = BG_DARK
	if accent:
		box.border_color = Color(accent.r * 0.3, accent.g * 0.3, accent.b * 0.3, 1)
	else:
		box.border_color = BORDER_MEDIUM
	box.content_margin_left = 12
	box.content_margin_right = 12
	box.content_margin_top = 8
	box.content_margin_bottom = 8
	box.set_border_width_all(BORDER_THIN)
	box.set_corner_radius_all(RADIUS_NORMAL)
	return box

static func _make_button_styles(base_bg = null, accent = null, radius: int = RADIUS_NORMAL) -> Array:
	var styles = []
	var bg = base_bg if base_bg else BG_DARK
	var acc = accent if accent else GOLD
	var acc_bright = GOLD_BRIGHT

	styles.append(_make_box(bg, BORDER_DARK, BORDER_THIN, radius))
	styles.append(_make_box(BG_MEDIUM, acc, BORDER_THIN, radius))
	styles.append(_make_box(BG_LIGHT, acc_bright, BORDER_THIN, radius))
	styles.append(_make_box(Color("#06080A"), Color("#1A222A"), BORDER_THIN, radius))
	styles.append(_make_box(Color(0,0,0,0), acc_bright, BORDER_THICK, radius))
	return styles

static func _make_toolbar_button_styles() -> Array:
	var styles = []
	styles.append(_make_box(Color("#0E1620"), BORDER_DARK, BORDER_THIN, RADIUS_SMALL, 8, 4))
	styles.append(_make_box(BG_MEDIUM, GOLD, BORDER_THIN, RADIUS_SMALL, 8, 4))
	styles.append(_make_box(BG_LIGHT, GOLD_BRIGHT, BORDER_THIN, RADIUS_SMALL, 8, 4))
	styles.append(_make_box(Color("#06080A"), Color("#1A222A"), BORDER_THIN, RADIUS_SMALL, 8, 4))
	styles.append(_make_box(Color(0,0,0,0), GOLD_BRIGHT, BORDER_THICK, RADIUS_SMALL, 8, 4))
	return styles

static func _make_tab_styles(active_bg = null, active_border = null) -> Array:
	var styles = []
	styles.append(_make_box(Color("#1A1A2E"), Color("#3A3A5A"), BORDER_THIN, RADIUS_SMALL, 12, 4))
	styles.append(_make_box(Color("#222242"), Color("#4A4A7A"), BORDER_THIN, RADIUS_SMALL, 12, 4))
	styles.append(_make_box(active_bg if active_bg else Color("#2A2A4A"), active_border if active_border else Color("#4A4A8A"), BORDER_THIN, RADIUS_SMALL, 12, 4))
	styles.append(_make_box(Color("#12121A"), Color("#2A2A3A"), BORDER_THIN, RADIUS_SMALL, 12, 4))
	styles.append(_make_box(Color(0,0,0,0), GOLD_BRIGHT, BORDER_THICK, RADIUS_SMALL, 12, 4))
	return styles

static func _make_input_styles() -> Array:
	var styles = []
	styles.append(_make_box(BG_INPUT, BORDER_DARK, BORDER_THIN, RADIUS_NORMAL))
	styles.append(_make_box(BG_INPUT, GOLD, BORDER_THIN, RADIUS_NORMAL))
	styles.append(_make_box(BG_INPUT, BORDER_DARK, BORDER_THIN, RADIUS_NORMAL))
	return styles

static func _make_scrollbar_styles() -> Array:
	var styles = []
	styles.append(_make_box(Color(0,0,0,0), BORDER_DARK, BORDER_NONE, RADIUS_FULL))
	styles.append(_make_box(GOLD, GOLD, BORDER_NONE, RADIUS_FULL))
	styles.append(_make_box(GOLD_BRIGHT, GOLD_BRIGHT, BORDER_NONE, RADIUS_FULL))
	styles.append(_make_box(GOLD_BRIGHT, GOLD_BRIGHT, BORDER_NONE, RADIUS_FULL))
	return styles

static func _make_progress_bar_styles(fill_color = null) -> Array:
	var styles = []
	styles.append(_make_box(Color("#1A2A3A"), BORDER_DARK, BORDER_NONE, RADIUS_NORMAL))
	styles.append(_make_box(fill_color if fill_color else GOLD, fill_color if fill_color else GOLD, BORDER_NONE, RADIUS_NORMAL))
	return styles

static func _make_separator(color = null, thickness: int = 1) -> StyleBoxFlat:
	var box = StyleBoxFlat.new()
	box.bg_color = color if color else BORDER_MEDIUM
	box.set_border_width_all(BORDER_NONE)
	box.set_corner_radius_all(BORDER_NONE)
	box.content_margin_top = thickness
	box.content_margin_bottom = 0
	return box

static func _make_popup_style() -> StyleBoxFlat:
	var box = StyleBoxFlat.new()
	box.bg_color = BG_DARK
	box.border_color = GOLD
	box.set_border_width_all(BORDER_THIN)
	box.set_corner_radius_all(RADIUS_NORMAL)
	box.content_margin_left = 8
	box.content_margin_right = 8
	box.content_margin_top = 4
	box.content_margin_bottom = 4
	box.shadow_color = SHADOW_STRONG
	box.shadow_size = 8
	box.shadow_offset = Vector2(0, 4)
	return box

static func _make_tooltip_style() -> StyleBoxFlat:
	var box = StyleBoxFlat.new()
	box.bg_color = Color("#0E1218")
	box.border_color = GOLD
	box.set_border_width_all(BORDER_THIN)
	box.set_corner_radius_all(RADIUS_NORMAL)
	box.content_margin_left = 12
	box.content_margin_right = 12
	box.content_margin_top = 10
	box.content_margin_bottom = 10
	box.shadow_color = SHADOW_STRONG
	box.shadow_size = 8
	box.shadow_offset = Vector2(0, 4)
	return box

## Helper methods for runtime use (static - can be called as UITheme.style_button())

static func style_button(button: Button, toolbar: bool = false, hover_scale: float = 1.05) -> void:
	if toolbar:
		button.theme_type_variation = "ToolbarButton"
	if hover_scale > 1.0:
		_add_hover_scale(button, hover_scale)

static func style_label(label: Label, color = null, font_size = null, alignment: int = HORIZONTAL_ALIGNMENT_LEFT) -> void:
	label.add_theme_color_override("font_color", color if color else TEXT_PRIMARY)
	if font_size:
		label.add_theme_font_size_override("font_size", font_size)
	label.horizontal_alignment = alignment

static func _add_hover_scale(control: Control, target_scale: float = 1.05, duration: float = 0.08) -> void:
	control.mouse_entered.connect(func():
		var tween = control.create_tween()
		tween.tween_property(control, "scale", Vector2(target_scale, target_scale), duration)
		tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)
	)
	control.mouse_exited.connect(func():
		var tween = control.create_tween()
		tween.tween_property(control, "scale", Vector2.ONE, duration)
		tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)
	)

static func fade_in(control: Control, duration: float = 0.12) -> Tween:
	control.modulate = Color(1, 1, 1, 0)
	control.show()
	var tween = control.create_tween()
	tween.tween_property(control, "modulate:a", 1.0, duration)
	tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)
	return tween

static func fade_out(control: Control, duration: float = 0.1, on_complete = null) -> Tween:
	var tween = control.create_tween()
	tween.tween_property(control, "modulate:a", 0.0, duration)
	tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)
	if on_complete:
		tween.tween_callback(on_complete)
	return tween

static func pop_in(control: Control, duration: float = 0.15) -> void:
	control.modulate = Color(1, 1, 1, 0)
	control.scale = Vector2(0.96, 0.96)
	control.show()
	var tween = control.create_tween()
	tween.set_parallel(true)
	tween.tween_property(control, "modulate:a", 1.0, duration).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)
	tween.tween_property(control, "scale", Vector2.ONE, duration).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_BACK)

static func pop_out(control: Control, duration: float = 0.1, on_complete = null) -> void:
	var tween = control.create_tween()
	tween.set_parallel(true)
	tween.tween_property(control, "modulate:a", 0.0, duration).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)
	tween.tween_property(control, "scale", Vector2(0.97, 0.97), duration).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)
	if on_complete:
		tween.chain().tween_callback(on_complete)

static func get_status_color(status: String) -> Color:
	match status.to_lower():
		"positive", "success", "good": return GREEN
		"negative", "danger", "bad": return RED
		"warning", "caution": return YELLOW
		"info", "neutral": return BLUE
		"special", "tech": return PURPLE
		"active", "progress": return ORANGE
		_: return TEXT_MUTED

static func get_category_color(category: String) -> Color:
	match category:
		"Economy": return GREEN
		"Military": return RED
		"Political": return PURPLE
		"Diplomatic": return BLUE
		"Technology": return ORANGE
		"General": return TEXT_MUTED
		_: return TEXT_MUTED

static func set_dark_mode(enabled: bool) -> void:
	# Dark mode is handled by the theme colors - already dark by default
	pass

static func set_scale(scale: float) -> void:
	# Scale is handled by individual controls
	pass

static func apply_theme(control: Control) -> void:
	control.theme = get_theme()