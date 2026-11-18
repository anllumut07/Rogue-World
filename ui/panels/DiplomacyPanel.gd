@tool
extends PanelContainer
class_name DiplomacyPanel

## DiplomacyPanel.gd - Diplomacy management panel
## Modular panel that can be opened/closed via UIManager

signal country_selected(country_id: String)
signal action_requested(action: String, target: String)

## UI References
@onready var title_bar: HBoxContainer = %TitleBar
@onready var panel_title: Label = %PanelTitle
@onready var close_btn: Button = %CloseBtn
@onready var content: VBoxContainer = %PanelContent

## Child panels (lazy loaded)
var relations_tab: Control = null
var factions_tab: Control = null
var guarantees_tab: Control = null
var actions_tab: Control = null

func _ready() -> void:
	_setup_title_bar()
	_build_content()

func _setup_title_bar() -> void:
	UITheme.style_label(panel_title, UITheme.GOLD, UITheme.FONT_TITLE)
	UITheme.style_button(close_btn, true)

	# Make title bar draggable
	title_bar.gui_input.connect(_on_title_bar_gui_input)

func _build_content() -> void:
	# Create tab container for diplomacy sections
	var tab_container = TabContainer.new()
	tab_container.theme = UITheme.get_theme()
	content.add_child(tab_container)

	# Relations Tab
	relations_tab = _create_relations_tab()
	tab_container.add_child(relations_tab)
	tab_container.set_tab_title(tab_container.get_child_count() - 1, "Relations")

	# Factions Tab
	factions_tab = _create_factions_tab()
	tab_container.add_child(factions_tab)
	tab_container.set_tab_title(tab_container.get_child_count() - 1, "Factions")

	# Guarantees Tab
	guarantees_tab = _create_guarantees_tab()
	tab_container.add_child(guarantees_tab)
	tab_container.set_tab_title(tab_container.get_child_count() - 1, "Guarantees")

	# Actions Tab
	actions_tab = _create_actions_tab()
	tab_container.add_child(actions_tab)
	tab_container.set_tab_title(tab_container.get_child_count() - 1, "Actions")

func _create_relations_tab() -> Control:
	var container = VBoxContainer.new()
	container.theme = UITheme.get_theme()

	# Search/filter
	var search_box = LineEdit.new()
	search_box.placeholder_text = "Search countries..."
	search_box.theme = UITheme.get_theme()
	search_box.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	container.add_child(search_box)

	# Country list
	var item_list = ItemList.new()
	item_list.theme = UITheme.get_theme()
	item_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	item_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	item_list.select_mode = ItemList.SELECT_MODE_SINGLE
	item_list.item_selected.connect(_on_country_selected)
	container.add_child(item_list)

	# Populate with sample data (in real game, this comes from DiplomacySystem)
	_call_deferred("_populate_relations", item_list)

	return container

func _create_factions_tab() -> Control:
	var container = VBoxContainer.new()
	container.theme = UITheme.get_theme()

	var label = Label.new()
	label.text = "Faction Management\n\n[Coming Soon: Create/Join Factions, Faction Members, Faction Policies]"
	label.theme = UITheme.get_theme()
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.size_flags_vertical = Control.SIZE_EXPAND_FILL
	container.add_child(label)

	return container

func _create_guarantees_tab() -> Control:
	var container = VBoxContainer.new()
	container.theme = UITheme.get_theme()

	var label = Label.new()
	label.text = "Guarantees\n\n[Coming Soon: Active Guarantees, Guarantee History, Guarantee Actions]"
	label.theme = UITheme.get_theme()
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.size_flags_vertical = Control.SIZE_EXPAND_FILL
	container.add_child(label)

	return container

func _create_actions_tab() -> Control:
	var container = VBoxContainer.new()
	container.theme = UITheme.get_theme()

	var actions = [
		["Improve Relations", "improve_relations", UITheme.BLUE],
		["Declare War", "declare_war", UITheme.RED],
		["Request Military Access", "military_access", UITheme.GREEN],
		["Request Naval Access", "naval_access", UITheme.GREEN],
		["Guarantee Independence", "guarantee", UITheme.YELLOW],
		["Form Alliance", "alliance", UITheme.PURPLE],
		["Request Recognition", "recognition", UITheme.ORANGE],
	]

	for action_data in actions:
		var btn = Button.new()
		btn.text = action_data[0]
		btn.theme = UITheme.get_theme()
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		btn.custom_minimum_size = Vector2(0, 40)
		var action_id = action_data[1]
		var color = action_data[2]
		btn.pressed.connect(_on_action_pressed.bind(action_id))
		UITheme.style_button(btn)
		btn.add_theme_color_override("font_color", color)
		container.add_child(btn)

	return container

func _populate_relations(item_list: ItemList) -> void:
	# Sample data - in real game, fetch from DiplomacySystem
	var countries = [
		{"id": "GER", "name": "Germany", "relation": 85, "status": "Ally"},
		{"id": "ITA", "name": "Italy", "relation": 72, "status": "Ally"},
		{"id": "JPN", "name": "Japan", "relation": 68, "status": "Ally"},
		{"id": "FRA", "name": "France", "relation": -45, "status": "Hostile"},
		{"id": "GBR", "name": "United Kingdom", "relation": -38, "status": "Hostile"},
		{"id": "SOV", "name": "Soviet Union", "relation": -52, "status": "Hostile"},
		{"id": "USA", "name": "United States", "relation": 15, "status": "Neutral"},
		{"id": "POL", "name": "Poland", "relation": 25, "status": "Neutral"},
		{"id": "CZE", "name": "Czechoslovakia", "relation": 30, "status": "Neutral"},
		{"id": "ROM", "name": "Romania", "relation": 40, "status": "Friendly"},
	]

	for country in countries:
		var text = "%s (%s): %d" % [country["name"], country["status"], country["relation"]]
		item_list.add_item(text)
		item_list.set_item_metadata(item_list.get_item_count() - 1, country["id"])

		# Color code by relation
		var rel = country["relation"]
		if rel >= 50:
			item_list.set_item_custom_bg_color(item_list.get_item_count() - 1, UITheme.GREEN)
		elif rel >= 0:
			item_list.set_item_custom_bg_color(item_list.get_item_count() - 1, UITheme.YELLOW)
		elif rel >= -50:
			item_list.set_item_custom_bg_color(item_list.get_item_count() - 1, UITheme.ORANGE)
		else:
			item_list.set_item_custom_bg_color(item_list.get_item_count() - 1, UITheme.RED)

func _on_title_bar_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			# Start drag
			get_viewport().set_input_as_handled()
		elif not event.pressed:
			# End drag - position already updated by Godot
			pass

func _on_country_selected(index: int) -> void:
	var item_list = content.get_node("TabContainer/Relations/ItemList")
	if item_list:
		var country_id = item_list.get_item_metadata(index)
		if country_id:
			country_selected.emit(country_id)

func _on_action_pressed(action_id: String) -> void:
	# Get selected country from relations tab
	var item_list = content.get_node("TabContainer/Relations/ItemList")
	if item_list and item_list.get_selected_items().size() > 0:
		var index = item_list.get_selected_items()[0]
		var country_id = item_list.get_item_metadata(index)
		action_requested.emit(action_id, country_id)
	else:
		# No country selected - show notification
		UIManager.instance.get_hud().show_notification("Select a country first", UITheme.YELLOW)

## Public API

func refresh_data() -> void:
	# Called when diplomacy data changes
	var item_list = content.get_node_or_null("TabContainer/Relations/ItemList")
	if item_list:
		item_list.clear()
		_populate_relations(item_list)

func select_country(country_id: String) -> void:
	var item_list = content.get_node_or_null("TabContainer/Relations/ItemList")
	if item_list:
		for i in range(item_list.get_item_count()):
			if item_list.get_item_metadata(i) == country_id:
				item_list.select(i)
				item_list.ensure_current_is_visible()
				break