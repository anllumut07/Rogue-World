@tool
extends PanelContainer
class_name SettingsPanel

## SettingsPanel.gd - Game settings management panel
## Modular panel for graphics, audio, gameplay, and UI settings

signal setting_changed(setting_name: String, value: Variant)
signal settings_reset()

## UI References
@onready var title_bar: HBoxContainer = %TitleBar
@onready var panel_title: Label = %PanelTitle
@onready var close_btn: Button = %CloseBtn
@onready var content: VBoxContainer = %PanelContent

## Settings state
var settings: Dictionary = {}
var setting_widgets: Dictionary = {}

func _ready() -> void:
	_setup_title_bar()
	_load_settings()
	_build_content()

func _setup_title_bar() -> void:
	UITheme.style_label(panel_title, UITheme.GOLD, UITheme.FONT_TITLE)
	UITheme.style_button(close_btn, true)

	# Make title bar draggable
	title_bar.gui_input.connect(_on_title_bar_gui_input)

func _load_settings() -> void:
	# Default settings
	settings = {
		# Graphics
		"resolution": "1920x1080",
		"fullscreen": true,
		"vsync": true,
		"ui_scale": 1.0,
		"render_scale": 1.0,
		"shadow_quality": 2,
		"texture_quality": 2,
		"effect_quality": 2,

		# Audio
		"master_volume": 0.8,
		"music_volume": 0.7,
		"sfx_volume": 0.8,
		"ambient_volume": 0.6,
		"voice_volume": 0.8,

		# Gameplay
		"auto_pause_on_event": true,
		"show_tooltips": true,
		"confirm_diplomatic_actions": true,
		"auto_save_interval": 5,
		"difficulty": "Normal",
		"game_speed": 1,

		# UI
		"dark_mode": true,
		"show_resource_details": true,
		"compact_hud": false,
		"panel_animations": true,
		"language": "English",

		# Controls
		"camera_speed": 1.0,
		"zoom_speed": 1.0,
		"invert_mouse_zoom": false,
	}

	# Try to load saved settings
	var config = ConfigFile.new()
	if config.load("user://settings.cfg") == OK:
		for section in config.get_sections():
			for key in config.get_section_keys(section):
				var full_key = "%s/%s" % [section, key]
				if settings.has(full_key):
					settings[full_key] = config.get_value(section, key)

func _build_content() -> void:
	var tab_container = TabContainer.new()
	tab_container.theme = UITheme.get_theme()
	content.add_child(tab_container)

	# Graphics Tab
	var graphics_tab = _create_graphics_tab()
	tab_container.add_child(graphics_tab)
	tab_container.set_tab_title(tab_container.get_child_count() - 1, "Graphics")

	# Audio Tab
	var audio_tab = _create_audio_tab()
	tab_container.add_child(audio_tab)
	tab_container.set_tab_title(tab_container.get_child_count() - 1, "Audio")

	# Gameplay Tab
	var gameplay_tab = _create_gameplay_tab()
	tab_container.add_child(gameplay_tab)
	tab_container.set_tab_title(tab_container.get_child_count() - 1, "Gameplay")

	# UI Tab
	var ui_tab = _create_ui_tab()
	tab_container.add_child(ui_tab)
	tab_container.set_tab_title(tab_container.get_child_count() - 1, "UI")

	# Controls Tab
	var controls_tab = _create_controls_tab()
	tab_container.add_child(controls_tab)
	tab_container.set_tab_title(tab_container.get_child_count() - 1, "Controls")

	# Reset button at bottom
	var reset_container = HBoxContainer.new()
	reset_container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	reset_container.alignment = HORIZONTAL_ALIGNMENT_RIGHT

	var reset_btn = Button.new()
	reset_btn.text = "Reset to Defaults"
	reset_btn.theme = UITheme.get_theme()
	reset_btn.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	reset_btn.pressed.connect(_on_reset_pressed)
	UITheme.style_button(reset_btn, true)
	reset_btn.add_theme_color_override("font_color", UITheme.ORANGE)
	reset_container.add_child(reset_btn)

	content.add_child(reset_container)

func _create_graphics_tab() -> Control:
	var container = VBoxContainer.new()
	container.theme = UITheme.get_theme()

	# Resolution
	_add_dropdown_setting(container, "Resolution", "resolution", [
		"1280x720", "1366x768", "1600x900", "1920x1080",
		"2560x1440", "3840x2160"
	], "Graphics")

	# Fullscreen
	_add_bool_setting(container, "Fullscreen", "fullscreen", "Graphics")

	# VSync
	_add_bool_setting(container, "VSync", "vsync", "Graphics")

	# UI Scale
	_add_slider_setting(container, "UI Scale", "ui_scale", 0.5, 2.0, 0.1, "Graphics")

	# Render Scale
	_add_slider_setting(container, "Render Scale", "render_scale", 0.5, 2.0, 0.1, "Graphics")

	# Shadow Quality
	_add_dropdown_setting(container, "Shadow Quality", "shadow_quality", [
		"Low", "Medium", "High", "Ultra"
	], "Graphics")

	# Texture Quality
	_add_dropdown_setting(container, "Texture Quality", "texture_quality", [
		"Low", "Medium", "High", "Ultra"
	], "Graphics")

	# Effect Quality
	_add_dropdown_setting(container, "Effect Quality", "effect_quality", [
		"Low", "Medium", "High", "Ultra"
	], "Graphics")

	return container

func _create_audio_tab() -> Control:
	var container = VBoxContainer.new()
	container.theme = UITheme.get_theme()

	_add_slider_setting(container, "Master Volume", "master_volume", 0.0, 1.0, 0.05, "Audio")
	_add_slider_setting(container, "Music Volume", "music_volume", 0.0, 1.0, 0.05, "Audio")
	_add_slider_setting(container, "SFX Volume", "sfx_volume", 0.0, 1.0, 0.05, "Audio")
	_add_slider_setting(container, "Ambient Volume", "ambient_volume", 0.0, 1.0, 0.05, "Audio")
	_add_slider_setting(container, "Voice Volume", "voice_volume", 0.0, 1.0, 0.05, "Audio")

	return container

func _create_gameplay_tab() -> Control:
	var container = VBoxContainer.new()
	container.theme = UITheme.get_theme()

	_add_bool_setting(container, "Auto-pause on Events", "auto_pause_on_event", "Gameplay")
	_add_bool_setting(container, "Show Tooltips", "show_tooltips", "Gameplay")
	_add_bool_setting(container, "Confirm Diplomatic Actions", "confirm_diplomatic_actions", "Gameplay")

	_add_slider_setting(container, "Auto-save Interval (turns)", "auto_save_interval", 1, 20, 1, "Gameplay")

	_add_dropdown_setting(container, "Difficulty", "difficulty", [
		"Easy", "Normal", "Hard", "Very Hard", "Impossible"
	], "Gameplay")

	_add_dropdown_setting(container, "Default Game Speed", "game_speed", [
		"1x", "2x", "3x"
	], "Gameplay")

	return container

func _create_ui_tab() -> Control:
	var container = VBoxContainer.new()
	container.theme = UITheme.get_theme()

	_add_bool_setting(container, "Dark Mode", "dark_mode", "UI")
	_add_bool_setting(container, "Show Resource Details", "show_resource_details", "UI")
	_add_bool_setting(container, "Compact HUD", "compact_hud", "UI")
	_add_bool_setting(container, "Panel Animations", "panel_animations", "UI")

	_add_dropdown_setting(container, "Language", "language", [
		"English", "Türkçe", "Deutsch", "Français", "Español", "Русский", "中文", "日本語"
	], "UI")

	return container

func _create_controls_tab() -> Control:
	var container = VBoxContainer.new()
	container.theme = UITheme.get_theme()

	_add_slider_setting(container, "Camera Speed", "camera_speed", 0.1, 3.0, 0.1, "Controls")
	_add_slider_setting(container, "Zoom Speed", "zoom_speed", 0.1, 3.0, 0.1, "Controls")
	_add_bool_setting(container, "Invert Mouse Zoom", "invert_mouse_zoom", "Controls")

	# Key bindings section (placeholder)
	var keybind_label = Label.new()
	keybind_label.text = "\nKey Bindings\n[Coming Soon: Customizable key bindings]"
	keybind_label.theme = UITheme.get_theme()
	keybind_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	keybind_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	container.add_child(keybind_label)

	return container

func _add_bool_setting(container: VBoxContainer, label_text: String, key: String, section: String) -> void:
	var row = HBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var label = Label.new()
	label.text = label_text
	label.theme = UITheme.get_theme()
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(label)

	var check_btn = CheckButton.new()
	check_btn.theme = UITheme.get_theme()
	check_btn.button_pressed = settings[key]
	check_btn.toggled.connect(_on_setting_changed.bind(key, section))
	row.add_child(check_btn)

	container.add_child(row)
	setting_widgets[key] = check_btn

func _add_slider_setting(container: VBoxContainer, label_text: String, key: String, min_val: float, max_val: float, step: float, section: String) -> void:
	var row = VBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var label_row = HBoxContainer.new()
	label_row.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var label = Label.new()
	label.text = label_text
	label.theme = UITheme.get_theme()
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label_row.add_child(label)

	var value_label = Label.new()
	value_label.text = str(settings[key])
	value_label.theme = UITheme.get_theme()
	value_label.theme_type_variation = "Header"
	value_label.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	label_row.add_child(value_label)

	row.add_child(label_row)

	var slider = HSlider.new()
	slider.theme = UITheme.get_theme()
	slider.min_value = min_val
	slider.max_value = max_val
	slider.step = step
	slider.value = settings[key]
	slider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	slider.value_changed.connect(_on_slider_changed.bind(key, section, value_label))
	row.add_child(slider)

	container.add_child(row)
	setting_widgets[key] = {"slider": slider, "label": value_label}

func _add_dropdown_setting(container: VBoxContainer, label_text: String, key: String, options: Array, section: String) -> void:
	var row = HBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var label = Label.new()
	label.text = label_text
	label.theme = UITheme.get_theme()
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(label)

	var dropdown = OptionButton.new()
	dropdown.theme = UITheme.get_theme()
	dropdown.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for opt in options:
		dropdown.add_item(opt)

	# Find current value index
	var current_val = settings[key]
	var idx = 0
	if current_val is int:
		idx = current_val
	else:
		for i in range(options.size()):
			if options[i] == current_val:
				idx = i
				break

	dropdown.selected = idx
	dropdown.item_selected.connect(_on_dropdown_changed.bind(key, section))
	row.add_child(dropdown)

	container.add_child(row)
	setting_widgets[key] = dropdown

func _on_setting_changed(pressed: bool, key: String, section: String) -> void:
	settings[key] = pressed
	_save_setting(section, key, pressed)
	setting_changed.emit(key, pressed)

func _on_slider_changed(value: float, key: String, section: String, value_label: Label) -> void:
	# Round to step
	var widget = setting_widgets[key]
	var step = widget.slider.step
	value = round(value / step) * step
	widget.slider.value = value
	value_label.text = str(value)

	settings[key] = value
	_save_setting(section, key, value)
	setting_changed.emit(key, value)

func _on_dropdown_changed(index: int, key: String, section: String) -> void:
	var widget = setting_widgets[key]
	var value = widget.get_item_text(index)
	settings[key] = index  # Store index for easy retrieval
	_save_setting(section, key, index)
	setting_changed.emit(key, index)

func _save_setting(section: String, key: String, value: Variant) -> void:
	var config = ConfigFile.new()
	config.load("user://settings.cfg")
	config.set_value(section, key, value)
	config.save("user://settings.cfg")

func _on_title_bar_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			get_viewport().set_input_as_handled()

func _on_reset_pressed() -> void:
	# Show confirmation dialog
	var dialog = ConfirmationDialog.new()
	dialog.dialog_text = "Reset all settings to defaults?"
	dialog.confirmed.connect(_do_reset)
	dialog.theme = UITheme.get_theme()
	add_child(dialog)
	dialog.popup_centered()

func _do_reset() -> void:
	# Reset to defaults
	settings = {
		"resolution": "1920x1080",
		"fullscreen": true,
		"vsync": true,
		"ui_scale": 1.0,
		"render_scale": 1.0,
		"shadow_quality": 2,
		"texture_quality": 2,
		"effect_quality": 2,
		"master_volume": 0.8,
		"music_volume": 0.7,
		"sfx_volume": 0.8,
		"ambient_volume": 0.6,
		"voice_volume": 0.8,
		"auto_pause_on_event": true,
		"show_tooltips": true,
		"confirm_diplomatic_actions": true,
		"auto_save_interval": 5,
		"difficulty": "Normal",
		"game_speed": 1,
		"dark_mode": true,
		"show_resource_details": true,
		"compact_hud": false,
		"panel_animations": true,
		"language": "English",
		"camera_speed": 1.0,
		"zoom_speed": 1.0,
		"invert_mouse_zoom": false,
	}

	# Update widgets
	for key in setting_widgets:
		var widget = setting_widgets[key]
		if widget is CheckButton:
			widget.button_pressed = settings[key]
		elif widget is HSlider:
			widget.slider.value = settings[key]
			widget.label.text = str(settings[key])
		elif widget is OptionButton:
			if key == "difficulty":
				var diffs = ["Easy", "Normal", "Hard", "Very Hard", "Impossible"]
				widget.selected = diffs.find(settings[key])
			elif key == "game_speed":
				var speeds = ["1x", "2x", "3x"]
				widget.selected = speeds.find(str(settings[key]) + "x")
			elif key == "language":
				var langs = ["English", "Türkçe", "Deutsch", "Français", "Español", "Русский", "中文", "日本語"]
				widget.selected = langs.find(settings[key])
			elif key in ["shadow_quality", "texture_quality", "effect_quality"]:
				widget.selected = settings[key]
			elif key == "resolution":
				var ress = ["1280x720", "1366x768", "1600x900", "1920x1080", "2560x1440", "3840x2160"]
				widget.selected = ress.find(settings[key])

		# Save
		var section = _get_section_for_key(key)
		_save_setting(section, key, settings[key])
		setting_changed.emit(key, settings[key])

	settings_reset.emit()

func _get_section_for_key(key: String) -> String:
	match key:
		"resolution", "fullscreen", "vsync", "ui_scale", "render_scale", "shadow_quality", "texture_quality", "effect_quality":
			return "Graphics"
		"master_volume", "music_volume", "sfx_volume", "ambient_volume", "voice_volume":
			return "Audio"
		"auto_pause_on_event", "show_tooltips", "confirm_diplomatic_actions", "auto_save_interval", "difficulty", "game_speed":
			return "Gameplay"
		"dark_mode", "show_resource_details", "compact_hud", "panel_animations", "language":
			return "UI"
		"camera_speed", "zoom_speed", "invert_mouse_zoom":
			return "Controls"
		_:
			return "General"

## Public API

func get_setting(key: String) -> Variant:
	return settings.get(key, null)

func set_setting(key: String, value: Variant) -> void:
	if settings.has(key):
		settings[key] = value
		var section = _get_section_for_key(key)
		_save_setting(section, key, value)
		setting_changed.emit(key, value)

func apply_settings() -> void:
	# Apply graphics settings
	if settings.has("fullscreen"):
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN if settings["fullscreen"] else DisplayServer.WINDOW_MODE_WINDOWED)
	if settings.has("vsync"):
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_ENABLED if settings["vsync"] else DisplayServer.VSYNC_DISABLED)
	if settings.has("ui_scale"):
		# UI scale is handled by the theme system
		pass

	# Apply audio settings
	if AudioServer:
		AudioServer.set_bus_volume_db(AudioServer.get_bus_index("Master"), linear_to_db(settings["master_volume"]))
		AudioServer.set_bus_volume_db(AudioServer.get_bus_index("Music"), linear_to_db(settings["music_volume"]))
		AudioServer.set_bus_volume_db(AudioServer.get_bus_index("SFX"), linear_to_db(settings["sfx_volume"]))
		AudioServer.set_bus_volume_db(AudioServer.get_bus_index("Ambient"), linear_to_db(settings["ambient_volume"]))
		AudioServer.set_bus_volume_db(AudioServer.get_bus_index("Voice"), linear_to_db(settings["voice_volume"]))

	# Apply dark mode - handled by UITheme
	if settings.has("dark_mode"):
		UITheme.set_dark_mode(settings["dark_mode"])

func linear_to_db(linear: float) -> float:
	if linear <= 0:
		return -80.0
	return 20.0 * log(linear) / log(10.0)