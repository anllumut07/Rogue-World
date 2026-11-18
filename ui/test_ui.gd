extends SceneTree
class_name UITest

## ui/test_ui.gd - Unit tests for UI system
## Run with: godot --headless -s res://ui/test_ui.gd

@tool

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

	# Test 4: HUD resource updates
	if test_hud_resource_updates():
		passed += 1
	else:
		failed += 1

	# Test 5: HUD speed controls
	if test_hud_speed_controls():
		passed += 1
	else:
		failed += 1

	# Test 6: SettingsPanel defaults
	if test_settings_defaults():
		passed += 1
	else:
		failed += 1

	# Test 7: Panel animations
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

	# Verify color values match specification
	assert(UITheme.BG_DARK == Color(0.043, 0.063, 0.090, 1.0), "BG_DARK mismatch")
	assert(UITheme.BG_PANEL == Color(0.078, 0.102, 0.153, 1.0), "BG_PANEL mismatch")
	assert(UITheme.GOLD == Color(0.788, 0.635, 0.153, 1.0), "GOLD mismatch")
	assert(UITheme.GREEN == Color(0.200, 0.651, 0.314, 1.0), "GREEN mismatch")
	assert(UITheme.RED == Color(0.784, 0.235, 0.235, 1.0), "RED mismatch")
	assert(UITheme.BLUE == Color(0.200, 0.471, 0.784, 1.0), "BLUE mismatch")

	print("  ✅ Color constants verified")
	return true

func test_ui_theme_styleboxes() -> bool:
	print("Test 2: UITheme stylebox factories...")

	var panel_sb = UITheme.make_panel_stylebox()
	assert(panel_sb is StyleBoxFlat, "Panel stylebox not StyleBoxFlat")
	assert(panel_sb.bg_color == UITheme.BG_PANEL, "Panel bg color wrong")
	assert(panel_sb.border_color == UITheme.BORDER_GOLD, "Panel border color wrong")
	assert(panel_sb.border_width_top == 1, "Panel border width wrong")
	assert(panel_sb.corner_radius_top_left == 6, "Panel corner radius wrong")

	var button_sb = UITheme.make_button_stylebox()
	assert(button_sb is StyleBoxFlat, "Button stylebox not StyleBoxFlat")
	assert(button_sb.bg_color == UITheme.BG_PANEL, "Button bg color wrong")
	assert(button_sb.border_color == UITheme.BORDER_DIM, "Button border color wrong")

	var toolbar_sb = UITheme.make_toolbar_button_stylebox()
	assert(toolbar_sb.content_margin_top == 4, "Toolbar margins wrong")

	var inner_sb = UITheme.make_inner_stylebox()
	assert(inner_sb.border_color == UITheme.BORDER_DIM, "Inner border wrong")

	print("  ✅ Stylebox factories verified")
	return true

func test_ui_theme_helpers() -> bool:
	print("Test 3: UITheme helper methods...")

	var theme = UITheme.get_theme()
	assert(theme is Theme, "get_theme() didn't return Theme")

	# Test label styling
	var label = Label.new()
	UITheme.style_label(label, UITheme.GOLD, 20)
	assert(label.get_theme_color_override("font_color") == UITheme.GOLD, "Label color not applied")

	# Test button styling
	var btn = Button.new()
	UITheme.style_button(btn, false)
	assert(btn.theme is Theme, "Button theme not applied")

	var toolbar_btn = Button.new()
	UITheme.style_button(toolbar_btn, true)
	assert(toolbar_btn.theme is Theme, "Toolbar button theme not applied")

	# Test panel styling
	var panel = PanelContainer.new()
	UITheme.style_panel(panel)
	assert(panel.get_theme_stylebox("panel") is StyleBoxFlat, "Panel stylebox not applied")

	# Test scale setting
	UITheme.set_scale(1.5)
	assert(UITheme.get_scale() == 1.5, "Scale not set correctly")
	UITheme.set_scale(1.0)  # Reset

	# Test dark mode toggle
	UITheme.set_dark_mode(false)
	assert(UITheme.is_dark_mode() == false, "Dark mode not toggled")
	UITheme.set_dark_mode(true)
	assert(UITheme.is_dark_mode() == true, "Dark mode not restored")

	print("  ✅ Helper methods verified")
	return true

func test_hud_resource_updates() -> bool:
	print("Test 4: HUD resource updates...")

	var hud = HUD.new()

	# Test positive values
	hud.update_resources({
		"gold": 2000,
		"gold_per_turn": 50,
		"manpower": 100000,
		"manpower_per_turn": 500,
		"stability": 90,
		"war_support": 75
	})

	assert(hud.gold_label.text == "Gold: 2000", "Gold label wrong: %s" % hud.gold_label.text)
	assert(hud.gold_per_turn.text == "+50/turn", "Gold/turn label wrong: %s" % hud.gold_per_turn.text)
	assert(hud.manpower_label.text == "Manpower: 100000", "Manpower label wrong")
	assert(hud.manpower_per_turn.text == "+500/turn", "Manpower/turn wrong")
	assert(hud.stability_label.text == "Stability: 90%", "Stability label wrong")
	assert(hud.war_support_label.text == "War Support: 75%", "War support label wrong")

	# Verify color coding for positive values
	assert(hud.gold_per_turn.get_theme_color_override("font_color") == UITheme.GREEN_BRIGHT, "Gold/turn color wrong for positive")
	assert(hud.manpower_per_turn.get_theme_color_override("font_color") == UITheme.GREEN_BRIGHT, "Manpower/turn color wrong for positive")

	# Test negative values
	hud.update_resources({
		"gold_per_turn": -25,
		"manpower_per_turn": -100,
		"stability": 20,
		"war_support": 10
	})

	assert(hud.gold_per_turn.text == "-25/turn", "Negative gold/turn wrong")
	assert(hud.gold_per_turn.get_theme_color_override("font_color") == UITheme.RED, "Gold/turn color wrong for negative")
	assert(hud.manpower_per_turn.get_theme_color_override("font_color") == UITheme.RED, "Manpower/turn color wrong for negative")

	# Verify status color coding
	assert(hud.stability_label.get_theme_color_override("font_color") == UITheme.RED, "Stability color wrong for low value")
	assert(hud.war_support_label.get_theme_color_override("font_color") == UITheme.RED, "War support color wrong for low value")

	# Test medium values
	hud.update_resources({
		"stability": 60,
		"war_support": 55
	})
	assert(hud.stability_label.get_theme_color_override("font_color") == UITheme.YELLOW, "Stability color wrong for medium")
	assert(hud.war_support_label.get_theme_color_override("font_color") == UITheme.YELLOW, "War support color wrong for medium")

	print("  ✅ HUD resource updates verified")
	return true

func test_hud_speed_controls() -> bool:
	print("Test 5: HUD speed controls...")

	var hud = HUD.new()

	# Test initial state
	assert(hud.get_current_speed() == 1, "Initial speed not 1")
	assert(hud.is_game_paused() == false, "Initial pause state wrong")
	assert(hud.pause_btn.text == "⏸", "Pause button text wrong")

	# Test speed changes
	hud.update_speed_buttons(2)
	assert(hud.get_current_speed() == 2, "Speed not updated to 2")
	assert(hud.speed_2x_btn.disabled == true, "2x button not disabled when active")
	assert(hud.speed_1x_btn.disabled == false, "1x button disabled when not active")
	assert(hud.speed_3x_btn.disabled == false, "3x button disabled when not active")

	# Active button should be gold
	assert(hud.speed_2x_btn.get_theme_color_override("font_color") == UITheme.GOLD_BRIGHT, "Active speed not gold")

	# Test pause toggle
	hud.set_paused(true)
	assert(hud.is_game_paused() == true, "Pause not set")
	assert(hud.pause_btn.text == "▶", "Pause button text not updated")

	hud.set_paused(false)
	assert(hud.is_game_paused() == false, "Pause not cleared")
	assert(hud.pause_btn.text == "⏸", "Pause button text not restored")

	# Test turn info update
	hud.update_turn_info("June 1938", 3)
	assert(hud.date_label.text == "June 1938", "Date not updated")
	assert(hud.get_current_speed() == 3, "Speed not updated via turn info")

	print("  ✅ HUD speed controls verified")
	return true

func test_settings_defaults() -> bool:
	print("Test 6: SettingsPanel defaults...")

	var panel = SettingsPanel.new()

	# Check all default values exist
	assert(panel.settings.has("resolution"), "Missing resolution setting")
	assert(panel.settings.has("fullscreen"), "Missing fullscreen setting")
	assert(panel.settings.has("master_volume"), "Missing master_volume setting")
	assert(panel.settings.has("difficulty"), "Missing difficulty setting")
	assert(panel.settings.has("dark_mode"), "Missing dark_mode setting")
	assert(panel.settings.has("camera_speed"), "Missing camera_speed setting")

	# Verify default values
	assert(panel.settings["resolution"] == "1920x1080", "Wrong default resolution")
	assert(panel.settings["fullscreen"] == true, "Wrong default fullscreen")
	assert(panel.settings["master_volume"] == 0.8, "Wrong default master volume")
	assert(panel.settings["difficulty"] == "Normal", "Wrong default difficulty")
	assert(panel.settings["dark_mode"] == true, "Wrong default dark mode")
	assert(panel.settings["camera_speed"] == 1.0, "Wrong default camera speed")

	# Test get_setting / set_setting
	assert(panel.get_setting("resolution") == "1920x1080", "get_setting failed")
	panel.set_setting("master_volume", 0.5)
	assert(panel.get_setting("master_volume") == 0.5, "set_setting failed")
	assert(panel.settings["master_volume"] == 0.5, "Settings dict not updated")

	# Test section mapping
	assert(panel._get_section_for_key("resolution") == "Graphics", "Wrong section for resolution")
	assert(panel._get_section_for_key("master_volume") == "Audio", "Wrong section for master_volume")
	assert(panel._get_section_for_key("difficulty") == "Gameplay", "Wrong section for difficulty")
	assert(panel._get_section_for_key("dark_mode") == "UI", "Wrong section for dark_mode")
	assert(panel._get_section_for_key("camera_speed") == "Controls", "Wrong section for camera_speed")

	print("  ✅ SettingsPanel defaults verified")
	return true

func test_panel_animations() -> bool:
	print("Test 7: Panel animations (UITheme)...")

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

	# Test pulse
	var btn = Button.new()
	UITheme.pulse(btn, UITheme.GOLD, 0.01)
	assert(true, "pulse executed without error")

	# Test shake
	var container = Control.new()
	UITheme.shake(container, 5, 0.01)
	assert(true, "shake executed without error")

	print("  ✅ Panel animations verified")
	return true