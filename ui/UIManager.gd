@tool
extends Node
class_name UIManager

## UIManager.gd - Central UI management for Rogue World
## Handles HUD updates, panel open/close, and UI state management

signal hud_updated(resources: Dictionary)
signal panel_opened(panel_name: String)
signal panel_closed(panel_name: String)

## Singleton instance
static var instance: UIManager = null

## Panel references
@onready var hud_layer: CanvasLayer = %HUDLayer
@onready var panels_layer: CanvasLayer = %PanelsLayer
@onready var panels_container: Control = %PanelsContainer
@onready var panel_backdrop: ColorRect = %PanelBackdrop

@onready var hud: HUD = %HUDLayer/HUD
@onready var diplomacy_panel: PanelContainer = %PanelsContainer/DiplomacyPanel
@onready var settings_panel: PanelContainer = %PanelsContainer/SettingsPanel

## Panel registry
var panels: Dictionary = {}
var open_panels: Array[String] = []

## Game data reference (to be set by GameManager)
var game_data: Dictionary = {}

func _enter_tree() -> void:
	instance = self
	_register_panels()
	_setup_panel_signals()

func _exit_tree() -> void:
	if instance == self:
		instance = null

func _register_panels() -> void:
	panels = {
		"Diplomacy": diplomacy_panel,
		"Settings": settings_panel,
	}

	# Ensure all panels start hidden
	for panel_name in panels:
		var panel = panels[panel_name]
		panel.visible = false
		panel.process_mode = Node.PROCESS_MODE_ALWAYS

func _setup_panel_signals() -> void:
	# Connect panel close buttons
	for panel_name in panels:
		var panel = panels[panel_name]
		var close_btn = panel.get_node_or_null("TitleBar/CloseBtn")
		if close_btn:
			close_btn.pressed.connect(_on_panel_close_pressed.bind(panel_name))

	# Backdrop click closes topmost panel
	panel_backdrop.gui_input.connect(_on_backdrop_gui_input)

	# HUD panel buttons
	var diplomacy_btn = hud.get_node_or_null("RightSection/PanelButtons/DiplomacyBtn")
	if diplomacy_btn:
		diplomacy_btn.pressed.connect(toggle_panel.bind("Diplomacy"))

	var settings_btn = hud.get_node_or_null("RightSection/PanelButtons/SettingsBtn")
	if settings_btn:
		settings_btn.pressed.connect(toggle_panel.bind("Settings"))

func _on_panel_close_pressed(panel_name: String) -> void:
	close_panel(panel_name)

func _on_backdrop_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if open_panels.size() > 0:
			close_panel(open_panels[-1])

## Public API

func toggle_panel(panel_name: String) -> void:
	if is_panel_open(panel_name):
		close_panel(panel_name)
	else:
		open_panel(panel_name)

func open_panel(panel_name: String) -> void:
	if not panels.has(panel_name):
		push_error("Panel not found: ", panel_name)
		return

	if is_panel_open(panel_name):
		return

	var panel = panels[panel_name]

	# Show backdrop if this is the first panel
	if open_panels.is_empty():
		panel_backdrop.visible = true
		UITheme.pop_in(panel_backdrop, 0.1)

	# Show panel with animation
	panel.visible = true
	panel.global_position = (get_viewport_rect().size - panel.size) / 2
	UITheme.pop_in(panel, 0.15)

	open_panels.append(panel_name)
	panel_opened.emit(panel_name)

	# Pause game when panels open (optional - controlled by GameManager)
	# GameManager.instance.set_paused(true)

func close_panel(panel_name: String) -> void:
	if not is_panel_open(panel_name):
		return

	var panel = panels[panel_name]
	UITheme.pop_out(panel, 0.1, func(): panel.visible = false)

	open_panels.erase(panel_name)
	panel_closed.emit(panel_name)

	# Hide backdrop if no panels open
	if open_panels.is_empty():
		UITheme.fade_out(panel_backdrop, 0.1, func(): panel_backdrop.visible = false)

	# Resume game if no panels open (optional)
	# if open_panels.is_empty():
	# 	GameManager.instance.set_paused(false)

func close_all_panels() -> void:
	for panel_name in open_panels.duplicate():
		close_panel(panel_name)

func is_panel_open(panel_name: String) -> bool:
	return panel_name in open_panels

func get_open_panels() -> Array[String]:
	return open_panels.duplicate()

## HUD Management

func update_hud(resources: Dictionary) -> void:
	game_data = resources
	if hud:
		hud.update_resources(resources)
	hud_updated.emit(resources)

func update_turn_info(date_text: String, speed: int) -> void:
	if hud:
		hud.update_turn_info(date_text, speed)

func update_speed_buttons(active_speed: int) -> void:
	if hud:
		hud.update_speed_buttons(active_speed)

## Utility

func get_panel(panel_name: String) -> Control:
	return panels.get(panel_name, null)

func get_hud() -> HUD:
	return hud

## Input handling (ESC to close panels)
func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		if open_panels.size() > 0:
			close_panel(open_panels[-1])
			get_viewport().set_input_as_handled()
		elif GameManager.instance and GameManager.instance.is_game_running:
			# Open pause menu - handled by GameManager
			pass

## Theme access
func get_theme() -> Theme:
	return UITheme.get_theme()

func apply_theme(control: Control) -> void:
	UITheme.apply_theme(control)

## Cleanup
func _notification(what: int) -> void:
	if what == NOTIFICATION_PREDELETE:
		close_all_panels()