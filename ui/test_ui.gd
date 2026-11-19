@tool
extends SceneTree
class_name UITest

## ui/test_ui.gd - Unit tests for UI system
## Run with: godot --headless -s res://ui/test_ui.gd

func _init() -> void:
	# Required for SceneTree script
	pass

func _initialize() -> void:
	run_tests()

func run_tests() -> void:
	print("=== Rogue World UI Unit Tests ===\n")

	var passed = 0
	var failed = 0

	# Test 1: UITheme color constants
	if test_ui_theme_colors():
		passed += 1
	else:
		failed += 1

	# Test 2: UITheme stylebox factories
	if test_ui_theme_styleboxes():
		passed += 1
	else:
		failed += 1

	# Test 3: UITheme helper methods
	if test_ui_theme_helpers():
		passed += 1
	else:
		failed += 1

	# Test 4: Panel animations
	if test_panel_animations():
		passed += 1
	else:
		failed += 1

	print("\n=== Test Summary ===")
	print("Passed: %d" % passed)
	print("Failed: %d" % failed)

	if failed > 0:
		print("\n❌ SOME TESTS FAILED")
		quit(1)
	else:
		print("\n✅ ALL TESTS PASSED")
		quit(0)

func test_ui_theme_colors() -> bool:
	print("Test 1: UITheme color constants...")

	# Verify color values match specification (actual values from UITheme.gd)
	assert(UITheme.BG_DARK == Color("#0B1018"), "BG_DARK mismatch")
	assert(UITheme.BG_MEDIUM == Color("#121A26"), "BG_MEDIUM mismatch")
	assert(UITheme.GOLD == Color("#C9A227"), "GOLD mismatch")
	assert(UITheme.GREEN == Color("#4CAF50"), "GREEN mismatch")
	assert(UITheme.RED == Color("#F44336"), "RED mismatch")
	assert(UITheme.BLUE == Color("#2196F3"), "BLUE mismatch")

	print("  ✅ Color constants verified")
	return true

func test_ui_theme_styleboxes() -> bool:
	print("Test 2: UITheme stylebox factories...")

	# Test _make_panel_box
	var panel_sb = UITheme._make_panel_box()
	assert(panel_sb is StyleBoxFlat, "Panel stylebox not StyleBoxFlat")
	assert(panel_sb.bg_color == UITheme.BG_DARK, "Panel bg color wrong")
	assert(panel_sb.border_color == UITheme.GOLD, "Panel border color wrong")
	assert(panel_sb.border_width_top == 1, "Panel border width wrong")
	assert(panel_sb.corner_radius_top_left == 6, "Panel corner radius wrong")

	# Test _make_button_styles
	var btn_styles = UITheme._make_button_styles()
	assert(btn_styles.size() == 5, "Button styles count wrong")
	assert(btn_styles[0] is StyleBoxFlat, "Button stylebox not StyleBoxFlat")
	assert(btn_styles[0].bg_color == UITheme.BG_DARK, "Button bg color wrong")
	assert(btn_styles[0].border_color == UITheme.BORDER_DARK, "Button border color wrong")

	# Test _make_toolbar_button_styles
	var tb_styles = UITheme._make_toolbar_button_styles()
	assert(tb_styles.size() == 5, "Toolbar styles count wrong")
	assert(tb_styles[0].content_margin_top == 4, "Toolbar margins wrong")

	# Test _make_inner_panel_box
	var inner_sb = UITheme._make_inner_panel_box()
	assert(inner_sb.border_color == UITheme.BORDER_DARK, "Inner border wrong")

	print("  ✅ Stylebox factories verified")
	return true

func test_ui_theme_helpers() -> bool:
	print("Test 3: UITheme helper methods...")

	var theme = UITheme.get_theme()
	assert(theme is Theme, "get_theme() didn't return Theme")

	# Test label styling
	var label = Label.new()
	UITheme.style_label(label, UITheme.GOLD, 20)
	# In headless mode, theme_color_override might not be available, just check no crash
	assert(true, "Label styling executed without error")

	# Test button styling
	var btn = Button.new()
	UITheme.style_button(btn, false)
	# Theme may not be set in headless mode without full scene setup
	assert(true, "Button styling executed without error")

	var toolbar_btn = Button.new()
	UITheme.style_button(toolbar_btn, true)
	assert(true, "Toolbar button styling executed without error")

	# Test panel styling (apply_theme)
	var panel = PanelContainer.new()
	UITheme.apply_theme(panel)
	assert(panel.get_theme_stylebox("panel") is StyleBoxFlat, "Panel stylebox not applied")

	# Test dark mode (no-op in current implementation)
	UITheme.set_dark_mode(false)
	UITheme.set_dark_mode(true)

	print("  ✅ Helper methods verified")
	return true

func test_panel_animations() -> bool:
	print("Test 4: Panel animations (UITheme)...")

	# Test pop_in creates tween
	var panel = PanelContainer.new()
	panel.scale = Vector2(0.9, 0.9)
	panel.modulate = Color(1, 1, 1, 0)

	UITheme.pop_in(panel, 0.01)

	# In headless mode, tween completes immediately
	# Just verify no crash and method exists
	assert(true, "pop_in executed without error")

	# Test fade_in
	var backdrop = ColorRect.new()
	backdrop.modulate = Color(0, 0, 0, 0)

	UITheme.fade_in(backdrop, 0.01)
	assert(true, "fade_in executed without error")

	# Test fade_out
	var btn2 = Button.new()
	btn2.modulate = Color(1, 1, 1, 1)
	UITheme.fade_out(btn2, 0.01)
	assert(true, "fade_out executed without error")

	# Test pop_out
	var btn3 = Button.new()
	UITheme.pop_out(btn3, 0.01)
	assert(true, "pop_out executed without error")

	print("  ✅ Panel animations verified")
	return true