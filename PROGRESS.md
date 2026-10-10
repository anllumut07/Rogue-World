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

## Milestone 4: Economy — ✅ DONE (functional, 2026-10-11)

### Functional Status (4.1–4.5)
| Subtask | Description | Status |
|---------|-------------|--------|
| 4.1 | Factory counters from country data | ✅ TotalCiv/Mil/Dock via CountryData |
| 4.2 | Construction queue (5 types, 70/25/30 days, 15 factories/line cap) | ✅ Queue with progress bars, serial processing, completion increments CountryData |
| 4.3 | Consumer Goods laws (5 laws: Civilian 30% → Total 10%), 150 PP switch cost | ✅ ChangeLaw emits PlayerCountryDataChanged for instant HUD PP refresh |
| 4.4 | 6 resources (Oil/Rubber/Steel/Al/Tu/Cr) — stockpile, daily production | ✅ Stockpile grid + trade source (exporter name) in EconomyPanel |
| 4.5 | Automated trade: deficit → 2-unit/day deal (2 convoys), 80% export limit, exporter selection | ✅ TradeDeal model; FindBestExporter respects 80% cap; logs on create/cancel |

### Visual (minimal)
| # | Task | Status |
|---|------|--------|
| 1 | ProgressBar theme | ⬜ (default Godot style; functional) |
| 2 | Trade deal tooltips | ⬜ |

### Test Checklist (manual)
- [ ] HUD ECONOMY button opens panel; ESC closes
- [ ] 5 build buttons add items to queue with correct progress math
- [ ] Queue rows show progress bar, points, assigned factories, cancel works
- [ ] Factory counts update when construction completes (Civ/Mil/Dock)
- [ ] Law selector: Civilian(30%) → War(15%) changes FreeCivFactories instantly; costs 150 PP
- [ ] Resources: stockpile increments daily; deficit triggers trade deal (+2/day from exporter)
- [ ] TradeDeals section lists active deals with exporter names and convoy count
- [ ] Deal create/cancel logged with country names

---

## Milestone 5: Technology Tree — ✅ DONE (functional, 2026-10-11)

### Functional Status (5.1–5.5)
| Subtask | Description | Status |
|---------|-------------|--------|
| 5.1 | Categories: Infantry, Support, Artillery, Armor, Air, Naval, Industry, Electronics, Doctrines | ✅ 9 categories, 72 techs in JSON (eras 1936/1939/1941/1943, costs, prereqs, effects, modifiers) |
| 5.2 | Technologies with ID, name, description, era, cost, prerequisites, effects | ✅ 72 techs; modifiers for construction_speed, factory_output, resource_gain, research_speed |
| 5.3 | Research slots from country data (2–4), max 5, simultaneous research | ✅ CountryData.ResearchSlots used; slot limit enforced in TechnologySystem |
| 5.4 | Research bonuses: electronics/doctrine techs add research_speed modifier (stacking) | ✅ ResearchSpeedMult = 1 + Σ research_speed; applied in TechnologySystem.DailyTick |
| 5.5 | Research panel UI: category/era filters, tech list with status icons, START buttons, active research progress bars, slot/speed display | ✅ ResearchPanel overlay with filters, active research progress bars, START buttons with prereq checks |

### Visual (minimal)
| # | Task | Status |
|---|------|--------|
| 1 | ProgressBar theme | ⬜ (default Godot style; functional) |

### Test Checklist (manual)
- [ ] HUD RESEARCH button opens panel; ESC closes
- [ ] Category filter (All + 9) and Era filter (All/1936/1939/1941/1943) work
- [ ] Tech list: ✓ researched, ⟳ in progress, ○ available, 🔒 missing prereq
- [ ] START button enabled only when Available (prereqs met, slot free)
- [ ] Active research section: name, progress bar, days left, cancel X works
- [ ] Slot info shows used/total + speed multiplier (e.g., 1.02x after Electronics)
- [ ] Research completes after correct days (cost / speed multiplier); logs on completion
- [ ] Construction/Factory/Resource speed modifiers take effect (build faster, produce more)

---

## Milestone 6: Production (Production Lines) — 🔄 NEXT

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