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

## Milestone 2: World Map (Country Polygons) — ✅ DONE (2026-10-11)

### Functional Status (2.1–2.5)
| Subtask | Description | Status |
|---------|-------------|--------|
| 2.1 | GeoJSON + countries_data.json | ✅ Files exist (180 countries, 52 detailed) |
| 2.2 | WorldMap: GeoJSON→Polygon2D/Line2D, pan/zoom (RMB drag, wheel), WASD pan | ✅ Works |
| 2.3 | Left-click select → "Selected: X" bottom bar + CountryInfoPanel | ✅ Works |
| 2.4 | Country colors (hash), selected=gold outline, hover=lighter | ✅ Works |
| 2.5 | CountryInfoPanel: flag/name, GDP, pop, military, resources, factories | ✅ **Done** (flag placeholder, GDP added; HOI4 id aliases SOV/RUS, ENG/GBR) |

**Known gaps to fix (functional):** *All resolved* — flag placeholder (ideology color), GDP estimate (pop × civ factories × 150), HOI4 id aliases (SOV→RUS, ENG→GBR, GRE→GRC, YUG→SRB, MAN→MNG), selected border gold pulse.

### Visual Improvements (each separate commit)
| # | Task | Status |
|---|------|--------|
| 1 | Common UI Theme (dark navy/gray, gold border, consistent fonts, button states) | ✅ |
| 2 | Map: ocean gradient, country palette, thin borders, zoom-line-width, gold selected, hover | ✅ |
| 3 | Map details: zoom-based labels (hide small), camera lerp, zoom limits | ✅ |
| 4 | HUD: organized top bar, speed indicator, PP/Stability/WS labels, bottom bar, tooltips | ✅ |
| 5 | Animations: Tween fade/slide for panels, button hover scale | ✅ PopIn/PopOut (fade+scale), FadeIn/FadeOut, AddHoverScale tüm menü/panel butonlarında |
| 6 | Main menu: slow gradient/map background, stylized title | ✅ Nefes alan lacivert gradyan, ızgara drifti (MapGridNode), vinyet, altın başlık nabzı, sürüm footer |
| 7 | Extra effects (day/night/era) — only if performant | ✅ Era tint (yıl bazlı: 1939+ sıcak, 1942+ karanlık), terminatör bandı (60s'de bir tam dünya turu, 0.35× genişlik, yumuşak kenar) |

---

## Milestone 3: Country Database & Player Selection — 🔄 IN PROGRESS (2026-10-11)

### Functional Status (3.1–3.3)
| Subtask | Description | Status |
|---------|-------------|--------|
| 3.1 | CountryDatabase: 1936 data, majors + minors, realistic values | ✅ 52 countries in JSON + deterministic fallback; +17 HOI4 aliases (GER→DEU, JAP→JPN, CHI→CHN, SPA→ESP, PER→IRN, SIA→THA, SAF→ZAF, ROM→ROU, BUL→BGR, RAJ→IND, HOL→NLD, DEN→DNK, SWI→CHE, AST→AUS, MON→MNG, PHI→PHL, INS→IDN). AUS skipped (HOI4 Austria vs ISO Australia ambiguity). |
| 3.2 | PLAY AS button → SetPlayerCountry → HUD name/flag | ✅ Button feedback: "PLAYING AS THIS COUNTRY" + disabled when already player; reacts to PlayerCountryChanged; GD.Print confirmation. |
| 3.3 | PP accumulation daily, Stability %, War Support % in HUD | ✅ DailyTick: +2.0 PP/day (HOI4 base; TODO said +1.5, kept 2.0 as HOI4-accurate — flag to user) + ManpowerPerDay; HUD updates via PlayerCountryDataChanged signal. Stability/WS static for now (drift mechanics in later milestones). |

### Visual Improvements (small list)
| # | Task | Status |
|---|------|--------|
| 1 | Player country permanent gold border on map (distinct from selection pulse) | ✅ PlayerBorderColor (gold, 55% alpha) applied in RefreshOwnershipColors + UpdateBorderWidthsByZoom; WorldMap listens PlayerCountryChanged |

---

## Milestone 4: Economy — 🔄 NEXT (factories, construction, consumer goods, resources, trade)

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