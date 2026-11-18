# Rogue World — UI System Documentation

## Overview

The UI system for Rogue World is built on Godot 4.7.2 using GDScript. It follows a modular, responsive design with a dark-metallic theme inspired by grand strategy games (HOI4, EU4, Stellaris).

## Architecture

```
ui/
├── MainUI.tscn           # Root scene (autoload as singleton)
├── UIManager.gd          # Central UI controller (singleton)
├── Theme/
│   └── UITheme.gd        # Theme system (colors, styleboxes, fonts)
├── panels/
│   ├── HUD.gd            # Persistent HUD (resources, turn, speed)
│   ├── DiplomacyPanel.gd # Diplomacy management panel
│   └── SettingsPanel.gd  # Game settings panel
└── graphics/
    └── icons/            # UI icons (SVG/PNG)
```

## CanvasLayer Z-Ordering

| Layer | Purpose | Z-Index |
|-------|---------|---------|
| HUDLayer | Persistent HUD (always visible) | 10 |
| PanelsLayer | Modal panels (Diplomacy, Settings) | 20 |

## Theme System (UITheme.gd)

### Color Palette

```gdscript
# Background
BG_DARK      = Color(0.043, 0.063, 0.090, 1.0)   # #0B1017
BG_PANEL     = Color(0.078, 0.102, 0.153, 1.0)   # #141A27
BG_HOVER     = Color(0.118, 0.161, 0.235, 1.0)   # #1E293C
BG_PRESSED   = Color(0.090, 0.125, 0.188, 1.0)   # #172030

# Borders
BORDER_DIM   = Color(0.165, 0.227, 0.290, 1.0)   # #2A3A4A
BORDER_GOLD  = Color(0.788, 0.635, 0.153, 1.0)   # #C9A227
BORDER_BRIGHT= Color(0.898, 0.761, 0.216, 1.0)   # #E5C237

# Text
TEXT_PRIMARY = Color(0.922, 0.941, 0.961, 1.0)   # #EBF0F5
TEXT_SECONDARY= Color(0.627, 0.706, 0.784, 1.0)  # #A0B4C8
TEXT_MUTED   = Color(0.400, 0.478, 0.557, 1.0)   # #667A8E
TEXT_DISABLED= Color(0.275, 0.333, 0.392, 1.0)   # #465564

# Accent
GOLD         = Color(0.788, 0.635, 0.153, 1.0)   # #C9A227
GOLD_BRIGHT  = Color(0.898, 0.761, 0.216, 1.0)   # #E5C237
GOLD_DIM     = Color(0.588, 0.471, 0.118, 1.0)   # #96781E

# Semantic
GREEN        = Color(0.200, 0.651, 0.314, 1.0)   # #33A650
GREEN_BRIGHT = Color(0.275, 0.784, 0.400, 1.0)   # #46C866
RED          = Color(0.784, 0.235, 0.235, 1.0)   # #C83C3C
RED_BRIGHT   = Color(0.922, 0.314, 0.314, 1.0)   # #EB5050
BLUE         = Color(0.200, 0.471, 0.784, 1.0)   # #3378C8
BLUE_BRIGHT  = Color(0.275, 0.588, 0.922, 1.0)   # #4696EB
YELLOW       = Color(0.898, 0.761, 0.157, 1.0)   # #E5C228
ORANGE       = Color(0.898, 0.510, 0.157, 1.0)   # #E58228
PURPLE       = Color(0.549, 0.314, 0.784, 1.0)   # #8C50C8
```

### StyleBox Factories

```gdscript
UITheme.make_panel_stylebox()      # Main panel background with gold border
UITheme.make_inner_stylebox()      # Inner containers
UITheme.make_button_stylebox()     # Button states (normal/hover/pressed/disabled)
UITheme.make_toolbar_button_stylebox() # Compact toolbar buttons
UITheme.make_lineedit_stylebox()   # Input fields
UITheme.make_itemlist_stylebox()   # List items
UITheme.make_slider_stylebox()     # Sliders
```

### Helper Methods

```gdscript
UITheme.style_label(label, color, font_size)        # Apply color & font
UITheme.style_button(button, is_toolbar)            # Apply button theme
UITheme.style_panel(panel)                          # Apply panel stylebox
UITheme.pop_in(node, duration, callback)            # Scale 0.9→1.0 + fade in
UITheme.pop_out(node, duration, callback)           # Scale 1.0→0.9 + fade out
UITheme.fade_in(node, duration, callback)           # Opacity 0→1
UITheme.fade_out(node, duration, callback)          # Opacity 1→0
UITheme.pulse(node, color, duration)                # Color pulse animation
UITheme.shake(node, magnitude, duration)            # Position shake
UITheme.get_theme()                                 # Get complete Theme resource
UITheme.apply_theme(control)                        # Apply theme to control tree
UITheme.set_scale(scale)                            # Set global UI scale
UITheme.set_dark_mode(enabled)                      # Toggle dark mode
```

## UIManager (Singleton)

### Access
```gdscript
UIManager.instance  # Global access
```

### Panel Management

```gdscript
UIManager.instance.open_panel("Diplomacy")   # Open panel with animation
UIManager.instance.close_panel("Diplomacy")  # Close panel with animation
UIManager.instance.toggle_panel("Settings")  # Toggle panel
UIManager.instance.close_all_panels()        # Close all open panels
UIManager.instance.is_panel_open("Diplomacy") # Check if panel is open
UIManager.instance.get_open_panels()         # Get list of open panels
```

### HUD Updates

```gdscript
# Resources: {gold, gold_per_turn, manpower, manpower_per_turn, stability, war_support}
UIManager.instance.update_hud({
    "gold": 1500,
    "gold_per_turn": 25,
    "manpower": 75000,
    "manpower_per_turn": 300,
    "stability": 82,
    "war_support": 55
})

UIManager.instance.update_turn_info("January 1936", 1)  # Date + speed
UIManager.instance.update_speed_buttons(2)              # 1x, 2x, 3x
```

### Signals

```gdscript
UIManager.instance.hud_updated.connect(_on_hud_updated)
UIManager.instance.panel_opened.connect(_on_panel_opened)
UIManager.instance.panel_closed.connect(_on_panel_closed)
```

## HUD Component (HUD.gd)

### Structure
- **LeftSection**: Resources (Gold, Manpower, Stability, War Support)
- **CenterSection**: Turn info (Date) + Speed controls (Pause, 1x/2x/3x)
- **RightSection**: Panel buttons (Diplomacy, Settings)

### Signals
```gdscript
hud.speed_changed.connect(_on_speed_changed)   # speed: int
hud.pause_toggled.connect(_on_pause_toggled)
```

### Public API
```gdscript
hud.update_resources(resources_dict)
hud.update_turn_info(date_text, speed)
hud.update_speed_buttons(active_speed)
hud.set_paused(paused)
hud.get_current_speed() -> int
hud.is_game_paused() -> bool
```

## DiplomacyPanel (DiplomacyPanel.gd)

### Tabs
1. **Relations** - Country list with relations, search/filter
2. **Factions** - Faction management (placeholder)
3. **Guarantees** - Active guarantees (placeholder)
4. **Actions** - Diplomatic action buttons

### Signals
```gdscript
panel.country_selected.connect(_on_country_selected)   # country_id: String
panel.action_requested.connect(_on_action_requested)   # action: String, target: String
```

### Actions Available
- Improve Relations
- Declare War
- Request Military Access
- Request Naval Access
- Guarantee Independence
- Form Alliance
- Request Recognition

### Public API
```gdscript
panel.refresh_data()              # Reload from DiplomacySystem
panel.select_country("GER")       # Select country in list
```

## SettingsPanel (SettingsPanel.gd)

### Tabs
1. **Graphics** - Resolution, Fullscreen, VSync, UI Scale, Render Scale, Quality presets
2. **Audio** - Master, Music, SFX, Ambient, Voice volumes
3. **Gameplay** - Auto-pause, Tooltips, Confirmations, Auto-save, Difficulty, Game Speed
4. **UI** - Dark Mode, Resource Details, Compact HUD, Animations, Language
5. **Controls** - Camera Speed, Zoom Speed, Invert Mouse Zoom, Key Bindings (placeholder)

### Settings Persistence
- Saved to `user://settings.cfg` (Godot ConfigFile)
- Loaded on panel creation
- Applied via `apply_settings()` method

### Signals
```gdscript
panel.setting_changed.connect(_on_setting_changed)  # setting_name, value
panel.settings_reset.connect(_on_settings_reset)
```

### Public API
```gdscript
panel.get_setting("resolution")        # Get current value
panel.set_setting("master_volume", 0.5) # Set and persist
panel.apply_settings()                  # Apply all to engine
```

## Responsive Design

### Anchoring Strategy
- **HUD**: Full viewport anchors (15 = all edges), 10px margins
- **Panels**: Percentage-based anchors (e.g., 0.1/0.9 = 10% margins)
- **Min Size**: Panels ≤ 1152×648 (720p safe)
- **Scaling**: `UITheme.set_scale()` adjusts all fonts & margins

### Breakpoints
| Resolution | UI Scale |
|------------|----------|
| ≤ 1366×768 | 0.9      |
| 1920×1080  | 1.0      |
| 2560×1440  | 1.15     |
| ≥ 3840×2160| 1.3      |

## Animation System

All panels use `UITheme` animation helpers:

```gdscript
# Panel open
UITheme.pop_in(panel_backdrop, 0.1)
UITheme.pop_in(panel, 0.15)

# Panel close
UITheme.pop_out(panel, 0.1, func(): panel.visible = false)
UITheme.fade_out(panel_backdrop, 0.1, func(): panel_backdrop.visible = false)
```

### Hover Effects
- Buttons: Scale 1.0 → 1.05 on hover (Tween)
- Panel items: Background color transition

## Input Handling

| Key | Action |
|-----|--------|
| ESC | Close topmost panel (or pause menu if none) |
| Right-drag | Map pan (not consumed by panels) |
| Wheel | Zoom (not consumed by panels) |
| WASD | Map pan (not consumed by panels) |
| Click country | Select in diplomacy panel |

### Mouse Filter
- **PanelBackdrop**: `MOUSE_FILTER_STOP` (blocks input to game when panel open)
- **Panels**: `MOUSE_FILTER_STOP` (self-contained)
- **HUD**: `MOUSE_FILTER_IGNORE` (click-through for map)

## Integration with Game Systems

### GameManager (C#)
```csharp
// In GameManager.cs daily tick
UIManager.instance.update_hud(new Dictionary {
    ["gold"] = economy.Gold,
    ["gold_per_turn"] = economy.GoldPerTurn,
    ["manpower"] = manpower.Current,
    ["manpower_per_turn"] = manpower.PerTurn,
    ["stability"] = politics.Stability,
    ["war_support"] = politics.WarSupport
});

UIManager.instance.update_turn_info(date.ToString("MMMM yyyy"), GameSpeed.Current);
```

### DiplomacySystem (C#)
```csharp
// When relations change
UIManager.instance.get_panel("Diplomacy").refresh_data();
```

## Testing

### Unit Test (HUD Label Updates)
```gdscript
# tools/test_ui.gd (run with --ui-test)
func test_hud_updates():
    var hud = HUD.new()
    add_child(hud)
    
    # Test resource update
    hud.update_resources({
        "gold": 2000,
        "gold_per_turn": -50,
        "manpower": 100000,
        "manpower_per_turn": 500,
        "stability": 90,
        "war_support": 30
    })
    
    assert(hud.gold_label.text == "Gold: 2000")
    assert(hud.gold_per_turn.text == "-50/turn")
    assert(hud.manpower_label.text == "Manpower: 100000")
    assert(hud.stability_label.text == "Stability: 90%")
    
    # Test color coding
    assert(hud.gold_per_turn.get_theme_color_override("font_color") == UITheme.RED)
    assert(hud.stability_label.get_theme_color_override("font_color") == UITheme.GREEN_BRIGHT)
    
    print("HUD tests PASSED")
```

### Headless Test Runner
```bash
# Run UI tests
powershell -File tools\run-test.ps1 --ui-test

# Or run all tests including UI
powershell -File tools\run-test.ps1 --all-tests
```

## Adding New Panels

1. Create `ui/panels/NewPanel.gd` extending `PanelContainer`
2. Add to `MainUI.tscn` under `PanelsContainer`
3. Register in `UIManager._register_panels()`
4. Add HUD button in `MainUI.tscn` → `RightSection/PanelButtons`
5. Connect button in `UIManager._setup_panel_signals()`

### Panel Template
```gdscript
extends PanelContainer
class_name NewPanel

@onready var title_bar: HBoxContainer = %TitleBar
@onready var panel_title: Label = %PanelTitle
@onready var close_btn: Button = %CloseBtn
@onready var content: VBoxContainer = %PanelContent

func _ready():
    UITheme.style_label(panel_title, UITheme.GOLD, UITheme.FONT_TITLE)
    UITheme.style_button(close_btn, true)
    title_bar.gui_input.connect(_on_title_bar_gui_input)
    _build_content()

func _build_content():
    # Your UI here
    pass

func _on_title_bar_gui_input(event):
    if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
        if event.pressed:
            get_viewport().set_input_as_handled()
```

## Performance Notes

- **Theme**: Single `Theme` resource shared across all UI
- **Panels**: Lazy-loaded content (built on first open)
- **Animations**: Tween-based, GPU-friendly
- **Updates**: HUD updates batched per frame via `UIManager.update_hud()`
- **Memory**: Panels reused (hidden, not freed)

## Localization

All UI text is in English in code. For localization:
1. Use `tr()` for all user-facing strings
2. Add translations in `ProjectSettings > Localization`
3. `UITheme.set_language()` can swap fonts for CJK

## Accessibility

- High contrast colors (WCAG AA compliant)
- Focus visible on all interactive elements
- Keyboard navigation (Tab/Enter/ESC)
- Screen reader friendly labels
- Scalable UI (0.5x - 2.0x)

## Debugging

```gdscript
# Print current UI state
print("Open panels: ", UIManager.instance.get_open_panels())
print("HUD data: ", UIManager.instance.game_data)
print("Theme scale: ", UITheme.get_scale())
print("Dark mode: ", UITheme.is_dark_mode())
```

## File Encoding

All `.gd` files: **UTF-8 without BOM**

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0   | 2026-11-17 | Initial UI system: HUD, Diplomacy, Settings, Theme, UIManager |