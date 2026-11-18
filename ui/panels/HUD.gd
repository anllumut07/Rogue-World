@tool
extends HBoxContainer
class_name HUD

## HUD.gd - Persistent Heads-Up Display
## Shows resources, turn info, and speed controls in real-time

signal speed_changed(speed: int)
signal pause_toggled()

## UI References
@onready var gold_label: Label = %GoldLabel
@onready var gold_per_turn: Label = %GoldPerTurn
@onready var manpower_label: Label = %ManpowerLabel
@onready var manpower_per_turn: Label = %ManpowerPerTurn
@onready var stability_label: Label = %StabilityLabel
@onready var war_support_label: Label = %WarSupportLabel

@onready var date_label: Label = %DateLabel
@onready var speed_label: Label = %SpeedLabel
@onready var pause_btn: Button = %PauseBtn
@onready var speed_1x_btn: Button = %Speed1xBtn
@onready var speed_2x_btn: Button = %Speed2xBtn
@onready var speed_3x_btn: Button = %Speed3xBtn

## State
var current_speed: int = 1
var is_paused: bool = false
var speed_buttons: Array[Button] = []

func _ready() -> void:
	speed_buttons = [speed_1x_btn, speed_2x_btn, speed_3x_btn]
	_setup_speed_buttons()
	_update_speed_visual()

	pause_btn.pressed.connect(_on_pause_pressed)
	speed_1x_btn.pressed.connect(_on_speed_pressed.bind(1))
	speed_2x_btn.pressed.connect(_on_speed_pressed.bind(2))
	speed_3x_btn.pressed.connect(_on_speed_pressed.bind(3))

func _setup_speed_buttons() -> void:
	for btn in speed_buttons:
		UITheme.style_button(btn, true)

	UITheme.style_button(pause_btn, true)

func _on_pause_pressed() -> void:
	is_paused = not is_paused
	pause_btn.text = "▶" if is_paused else "⏸"
	pause_toggled.emit()

func _on_speed_pressed(speed: int) -> void:
	if is_paused:
		return
	current_speed = speed
	_update_speed_visual()
	speed_changed.emit(speed)

func _update_speed_visual() -> void:
	for i, btn in enumerate(speed_buttons):
		var target_speed = i + 1
		btn.disabled = (target_speed == current_speed)
		if target_speed == current_speed:
			btn.add_theme_color_override("font_color", UITheme.GOLD_BRIGHT)
		else:
			btn.add_theme_color_override("font_color", UITheme.TEXT_PRIMARY)

## Public API

func update_resources(resources: Dictionary) -> void:
	# Gold
	if resources.has("gold"):
		gold_label.text = "Gold: %d" % resources["gold"]
	if resources.has("gold_per_turn"):
		var gpt = resources["gold_per_turn"]
		gold_per_turn.text = "%s%d/turn" % ["+" if gpt >= 0 else "", gpt]
		gold_per_turn.add_theme_color_override("font_color", UITheme.GREEN_BRIGHT if gpt >= 0 else UITheme.RED)

	# Manpower
	if resources.has("manpower"):
		manpower_label.text = "Manpower: %d" % resources["manpower"]
	if resources.has("manpower_per_turn"):
		var mpt = resources["manpower_per_turn"]
		manpower_per_turn.text = "%s%d/turn" % ["+" if mpt >= 0 else "", mpt]
		manpower_per_turn.add_theme_color_override("font_color", UITheme.GREEN_BRIGHT if mpt >= 0 else UITheme.RED)

	# Stability
	if resources.has("stability"):
		var stab = resources["stability"]
		stability_label.text = "Stability: %d%%" % stab
		stability_label.add_theme_color_override("font_color", _get_status_color(stab))

	# War Support
	if resources.has("war_support"):
		var ws = resources["war_support"]
		war_support_label.text = "War Support: %d%%" % ws
		war_support_label.add_theme_color_override("font_color", _get_status_color(ws))

func update_turn_info(date_text: String, speed: int) -> void:
	date_label.text = date_text
	current_speed = speed
	_update_speed_visual()

func update_speed_buttons(active_speed: int) -> void:
	current_speed = active_speed
	_update_speed_visual()

func set_paused(paused: bool) -> void:
	if is_paused != paused:
		is_paused = paused
		pause_btn.text = "▶" if paused else "⏸"

func get_current_speed() -> int:
	return current_speed

func is_game_paused() -> bool:
	return is_paused

## Helpers

func _get_status_color(value: float) -> Color:
	if value >= 75:
		return UITheme.GREEN_BRIGHT
	elif value >= 50:
		return UITheme.YELLOW
	elif value >= 25:
		return UITheme.ORANGE
	else:
		return UITheme.RED

## Test helper - simulates resource change for unit testing
func simulate_resource_change(resources: Dictionary) -> void:
	update_resources(resources)