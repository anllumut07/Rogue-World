# Rogue World — Development Progress Log

## Milestone 0: Project Skeleton — ✅ DONE
- Project structure, Main scene, GameManager autoload, build passes.

## Milestone 1: Core Game Loop — ✅ DONE (2026-10-10)
- 1.1 GameManager (date, speed, tick signals) — ✅
- 1.2 Main Menu (START/QUIT) — ✅
- 1.3 Game Scene + HUD (date, speed buttons, PAUSE) — ✅
- 1.4 Menu↔Game transitions, PauseMenu (RESUME/MAIN MENU/QUIT) — ✅
- 1.5 SettingsSystem backend + **SettingsPanel UI** (volume slider, fullscreen checkbox, resolution OptionButton, BACK) — ✅
  - MainMenu SETTINGS button opens panel
  - PauseMenu SETTINGS button opens panel (ESC closes panel first)
  - Values persist in user://settings.json

---

## Milestone 2: World Map (Country Polygons) — 🔄 IN PROGRESS

### Functional Status (2.1–2.5)
| Subtask | Description | Status |
|---------|-------------|--------|
| 2.1 | GeoJSON + countries_data.json | ✅ Files exist (180 countries, 52 detailed) |
| 2.2 | WorldMap: GeoJSON→Polygon2D/Line2D, pan/zoom (RMB drag, wheel), WASD pan | ✅ Works |
| 2.3 | Left-click select → "Selected: X" bottom bar + CountryInfoPanel | ✅ Works |
| 2.4 | Country colors (hash), selected=gold outline, hover=lighter | ✅ Works |
| 2.5 | CountryInfoPanel: flag/name, GDP, pop, military, resources, factories | ✅ **Done** (flag placeholder, GDP added; HOI4 id aliases SOV/RUS, ENG/GBR) |

**Known gaps to fix (functional):**
- CountryInfoPanel: add flag placeholder (procedural colored rect by ideology color), add GDP estimate (Population × CivFactories × k)
- CountryDatabase: SOV vs RUS id mismatch (TODO mentions SOV, data uses RUS) — ensure CountryDatabase maps both
- Minor: ensure selected country gold outline pulse works (already in AnimateSelectedBorder)

### Visual Improvements (each separate commit)
| # | Task | Status |
|---|------|--------|
| 1 | Common UI Theme (dark navy/gray, gold border, consistent fonts, button states) | ⬜ |
| 2 | Map: ocean gradient, country palette, thin borders, zoom-line-width, gold selected, hover | ⬜ |
| 3 | Map details: zoom-based labels (hide small), camera lerp, zoom limits | ⬜ |
| 4 | HUD: organized top bar, speed indicator, PP/Stability/WS labels, bottom bar, tooltips | ⬜ |
| 5 | Animations: Tween fade/slide for panels, button hover scale | ⬜ |
| 6 | Main menu: slow gradient/map background, stylized title | ⬜ |
| 7 | Extra effects (day/night/era) — only if performant | ⬜ |

---

## Milestone 3+: Not started

---

## Manual Test Checklist (user to verify)
- [ ] SettingsPanel: change volume/fullscreen/resolution → quit → relaunch → values persist
- [ ] Map: click country → CountryInfoPanel shows data; hover highlights; selection outline gold
- [ ] Camera: RMB drag pans, wheel zooms, WASD pans, zoom limits (0.25×–6×)
- [ ] HUD: date updates, speed buttons (1x-5x), SPACE pauses, PP updates daily
- [ ] All menus/panels: theme consistent, buttons have hover/pressed/disabled states
- [ ] Animations: panels fade/slide, buttons scale on hover
- [ ] Main menu: animated background, nice title

---

## Stuck Items (3+ failed fixes)
*None yet*