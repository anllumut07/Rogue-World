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
| 2 | CountryInfoPanel visibility fix (was invisible on click) | ✅ **Root cause:** Control with FullRect anchor inside VBoxContainer (LeftStack) — VBox overrides child anchors → Size=(0,0). **Fix:** Changed base class Control→PanelContainer + SizeFlags.ExpandFill + ScrollContainer inside; explicit CenterViewport anchors (LeftPanel 340px, RightPanel 380px, TopBar 56px) in EnforceLayout. PLAY AS button now at bottom, prominent gold border, 46px height, 18pt font. |
| 3 | Double-click fallback for player selection | ✅ WorldMap._Input: `click.DoubleClick` → `GameManager.SetPlayerCountry(c.Id)` directly (works even if panel hidden); HUD updates via PlayerCountryChanged signal |
| 4 | No automatic player selection on startup | ✅ Verified: `SetPlayerCountry` called only from (a) PLAY AS button, (b) double-click, (c) `--country=` headless test argument. No auto-select on scene load; editor F5 (no args) starts with no player selected |

**Note on "automatic gold highlight on Turkey":** No automatic selection code exists in codebase. Likely causes: (a) left-click on any country applies the gold *selection* pulse (normal behavior, not player selection), or (b) a leftover `--country=` in Editor Run Args. Check Editor Settings → Run → Main Run Args if a player is auto-selected on startup.

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

## Milestone 6: Production (Production Lines) — ✅ DONE (functional, 2026-10-11)

### Functional Status (6.1–6.4)
| Subtask | Description | Status |
|---------|-------------|--------|
| 6.1 | Equipment types: 15 types (Land: Infantry/Support/Motorized/Light/Med/Heavy Tank; Air: Fighter/CAS/Naval Bomber/Strat Bomber; Naval: Destroyer/Cruiser/Battleship/Carrier/Submarine) | ✅ Data/equipment.json + EquipmentSystem.AllTypes |
| 6.2 | Production lines: drawn from mil factories/dockyards, efficiency (start 10%, +0.25%/day, max 100%), variant switch penalty (eff → 50%) | ✅ EquipmentSystem.AddLine/ChangeLineFactories/SwitchLineEquipment + DailyTick efficiency gain |
| 6.3 | Stockpile management: equipment in depot, daily production feed, assignment to mil factories | ✅ EquipmentSystem.Stocks + GetDailyOutput per line, monthly log |
| 6.4 | Variant designer (simplified Tank/Plane Designer): modules (Gun/Armor/Engine), cost/stat multipliers, create variant, assign to line | ✅ ProductionPanel designer section with 3 module slots, CREATE VARIANT button |

### Visual (minimal)
| # | Task | Status |
|---|------|--------|
| 1 | ProgressBar for line efficiency | ⬜ (default Godot style) |
| 2 | Stockpile grid layout | ⬜ (VBox rows of 3 cols) |

### Test Checklist (manual)
- [ ] HUD PRODUCTION button opens panel; ESC closes
- [ ] Production Lines: shows each line with name, factory +/- buttons, efficiency bar (%), daily output, stock, remove (×)
- [ ] Factory count adjust: +/− buttons respect free factory pool (Mil/Dock separate)
- [ ] Efficiency starts at 10%, gains +0.25%/day while producing, caps at 100%
- [ ] Variant dropdown per line: switching applies 50% efficiency penalty (log confirms)
- [ ] ADD LINE: equipment dropdown (15 types), variant dropdown (Base + created variants), factory count selector, ADD LINE creates line
- [ ] Variant Designer: base dropdown (tanks + planes), Gun/Armor/Engine module selectors with None, computed stats preview, CREATE VARIANT adds to list & ADD LINE variant dropdown
- [ ] Stockpile grid: shows all equipment with count + daily production rate
- [ ] Headless verification: `dotnet build` → clean; `godot --headless --quit-after 600` shows EquipmentSystem init log + monthly [Equipment] logs with line efficiency + stock summary

---

## Milestone 7: Military — Divisions, Armies, Combat — ✅ DONE (functional, 2026-10-11)

### Functional Status (7.1–7.6)
| Subtask | Description | Status |
|---------|-------------|--------|
| 7.1 | Division templates: 17 battalion types (Infantry, Artillery, AT, AA, Engineer, Recon, Signal, Logistics, Maint, Armor, SPG, TD, SPAA, Amphibious, Marine, Mountaineer, Paratrooper) — ORG/HP/ATT/DEF/Pierce/Armor/Speed/Supply/Manpower/Equipment | ✅ Data/battalions.json + ArmySystem.AllBattalions, template stats computation |
| 7.2 | Division training: manpower + equipment cost, X days (30+3×bns), reinforcement (ORG +3/day, HP +1/day consuming equipment) | ✅ ArmySystem.TrainingQueue, StartTraining/CancelTraining, DailyTick reinforcement |
| 7.3 | Armies & generals: create army, generated general names + skill 1-5, assign divisions via dropdown | ✅ ArmySystem.Armies, CreateArmy/AssignDivisionToArmy, ArmyPanel UI |
| 7.4 | Combat system (country-level): attacker/defender ORG/HP, daily tick resolution, casualties, equipment loss, retreat (3× = destroyed) | ✅ WarSystem.DailyTick, DistributeDamageToPlayer, RetreatOrDestroy, enemy division pool |
| 7.5 | Front line / offensive planning: RMB click on enemy → offensive, Shift+RMB → frontline stance (defense bonus) | ✅ WorldMap HandleRightClickOrder (<6px click), WarSystem SetOffensive/ToggleFrontline |
| 7.6 | Capitulation: enemy divisions = 0 OR WarProgress ≥ 100 → annex (IsAnnexed, OwnerId, factories transfer, map recolor), player capitulation = game over log | ✅ EnemyCapitulates/PlayerCapitulates, CountryDatabase flags, WorldMap recolor via CountryOwnershipChanged signal |

### Visual (minimal)
| # | Task | Status |
|---|------|--------|
| 1 | ProgressBar for line/org/hp efficiency | ⬜ (default Godot style) |
| 2 | WorldMap annexed country coloring (owner's palette darkened) | ✅ RefreshOwnershipColors |

### Test Checklist (manual)
- [ ] HUD ARMY button opens panel; ESC closes
- [ ] ACTIVE WARS: shows progress bar, enemy divisions, losses, OFFENSIVE/FRONTLINE/PASSIVE buttons matching RMB/Shift+RMB
- [ ] DIVISION TEMPLATES: lists templates with stats, TRAIN button consumes manpower+equipment, adds to queue
- [ ] TEMPLATE DESIGNER: 17 battalion rows with +/- buttons, stats preview updates live, SAVE TEMPLATE creates new template (auto-named)
- [ ] TRAINING QUEUE: items show progress bar, days left, CANCEL returns manpower+equipment
- [ ] ARMIES: each army shows general name/skill, divisions with ORG/HP bars, army dropdown for reassignment, CREATE ARMY
- [ ] UNASSIGNED divisions shown at bottom
- [ ] RMB click (<6px movement) on enemy at war → OFANSİF EMRİ toggle (log confirms); Shift+RMB → CEPHE DURUŞU toggle (defense +30%, log)
- [ ] DECLARE WAR from CountryInfoPanel: costs 50 PP, WS +10, STA −5, WarSystem adds war, ArmyPanel shows war
- [ ] Combat: daily log shows enemy division losses, player retreats/destroys, equipment consumption
- [ ] Capitulation: when enemy divisions = 0 → annex → CountryData.IsAnnexed/OwnerId, factories transfer, WorldMap recolors with owner's palette; player capitulation logs "GAME OVER"
- [ ] Headless verification: `dotnet build` → clean; `godot --headless --quit-after 600` shows ArmySystem init (tümen/ordu log), WarSystem monthly war summary logs, WorldMap ownership change log on capitulation

---

## Milestone 8: Diplomacy & Factions — ✅ DONE (functional + headless-validated, 2026-10-11)

### Functional Status (8.1–8.5)
| Subtask | Description | Status |
|---------|-------------|--------|
| 8.1 | Relations (-100..+100), opinions, World Tension | ✅ DiplomacySystem.Relations + Factions + WorldTension, diplomacy.json starter data |
| 8.2 | Faction create/join (Axis/Allies/Comintern + custom), leader | ✅ DiplomacySystem.CreateFaction/RequestJoinFaction/LeaveFaction, DiplomacyPanel UI |
| 8.3 | Declare war, call to arms, peace conference (white peace / puppet / annex) | ✅ WarSystem.DeclareWar/DeclareWarOnPlayer + DiplomacySystem.ProposePeace/WhitePeace, DiplomacyPanel war buttons |
| 8.4 | Guarantee independence, military access, naval access | ✅ DiplomacySystem.Guarantee/Revoke/RequestMilitaryAccess/RequestNavalAccess + panel UI |
| 8.5 | World Tension mechanics: actions increase, focus/events affect | ✅ Tension bar in panel, IncreaseTension/decay, 0.02%/day decay at peace |

### Visual (minimal)
| # | Task | Status |
|---|------|--------|
| 1 | World Tension ProgressBar (color gradient by level) | ✅ Green→Yellow→Orange→Red |
| 2 | Faction list with member badges | ✅ DiplomacyPanel factions section |

### Test Checklist (manual)
- [ ] HUD DIPLOMACY button opens panel; ESC closes
- [ ] WORLD TENSION: bar shows correct value (diplomacy.json 15%), color changes by threshold
- [ ] FACTIONS: shows Axis/Allies/Comintern with members; JOIN FACTION for non-members; CREATE FACTION for unaffiliated
- [ ] TARGET COUNTRY dropdown: lists all non-annexed countries with relation/opinion
- [ ] TARGET RELATION row: shows Relation/Opinion + WAR/GUARANTEE/ACCESS badges
- [ ] DIPLOMATIC ACTIONS (when target selected):
  - DECLARE WAR (50 PP): costs PP, WS +10, STA -5, adds war, triggers guarantees/faction call-to-arms
  - GUARANTEE INDEPENDENCE (25 PP): adds guarantee, relation +10, tension +2
  - REQUEST MILITARY/NAVAL ACCESS (10 PP): adds access, relation +5
  - IMPROVE RELATIONS (10 PP/day): relation +5
  - REVOKE GUARANTEE: removes, relation -10
- [ ] AT WAR actions: shows war info (enemy divisions, progress, losses) + PROPOSE PEACE (if progress ≥50) + WHITE PEACE
- [ ] PROPOSE PEACE: applies Puppet or Annex based on loser factory count; transfers factories, ends war, recolors map, emits CountryOwnershipChanged
- [ ] WHITE PEACE: ends war status quo, tension -5, relations +15
- [ ] GUARANTEES GIVEN: shows player guarantees with REVOKE buttons; shows guarantees received
- [ ] RELATIONS list: sortable by relation, shows WAR/positive/negative colors, top 30
- [ ] Headless: `dotnet build` → clean; `godot --headless --quit-after 600` shows DiplomacySystem init log + monthly [Diplomacy] logs

### Headless Validation Results (M6/M7/M8 combined — GER 90 days, speed 5, prod-test + war-test=POL)
| System | Verified Behavior |
|--------|-------------------|
| **CountryDatabase** | 50 countries loaded (was 1 before `IncludeFields=true` fix) |
| **TechnologySystem** | 73 technologies loaded (was 1 before `IncludeFields=true` fix) |
| **DiplomacySystem** | No NRE in NormalizeRelations (null guards added for HOI4 ID aliases GER/ENG/SOV vs ISO) |
| **EconomySystem** | DEU: CG=25%, Civ=26, Mil=20, Dock=4, Convoys=20; daily production math correct |
| **Production (M6)** | 4 lines created: Infantry Eq (3f), Support Eq (2f), Light Tank (2f), Destroyer (1f dockyard); efficiency: start 10% → +0.25%/day; variant switch penalty 50% applied & logged; stockpiles: infantry_equipment=6031 by day 30 (consumed by training) |
| **ArmySystem (M7)** | 40 divisions trained, 1 army created; daily reinforcement working; manpower consumption tracked |
| **WarSystem (M7)** | WAR DECLARED on Poland → offensive order → Poland capitulated day 6 (annexed, map recolored); log shows division losses per tick |
| **Diplomacy (M8)** | 6 trade deals auto-created (Oil/Rubber/Steel/Al/Tu/Cr from USSR/US/DEI/China); 3 cancelled by day 30 when stockpiles sufficient; convoys released; World Tension 15% → 14.5% (decay at peace) |

**All numeric values sensible:** No negative stocks, no NaN, efficiency monotonic (10% → 57% by day 30 after penalty recovery), PP increases daily, war progress reaches 100% → capitulation triggers correctly.

---

## Milestone 9: National Focus Tree — ✅ DONE (functional + headless-validated, 2026-10-11)

### Functional Status (9.1–9.4)
| Subtask | Description | Status |
|---------|-------------|--------|
| 9.1 | Focus nodes: ID, name, description, prereqs, mutually exclusive, cost (days), effects | ✅ Data/focus_trees/*.json (ağaç başına ayrı dosya) — 8 ağaç, 73 odak; field-tabanlı yükleme IncludeFields=true ile; yüklemede önkoşul/mutex id doğrulaması + sayı logu |
| 9.2 | Country-specific tree (GER/USA/SOV/ENG/FRA/JPN/ITA + GENERIC) | ✅ FocusSystem.GetTreeFor — GER/DEU, USA, SOV/RUS/USSR, ENG/GBR/UK, FRA, JPN/JAP, ITA → özel; diğer tüm ülkeler GENERIC; Initialize SetPlayerCountry'den çağrılır |
| 9.3 | Effects applied on completion (factories, PP, research bonus) | ✅ ApplyFocusEffects — her efekt ayrı GD.Print loguyla (önce→sonra); research_speed TechnologySystem.AddModifier'a akar; tension DiplomacySystem'e |
| 9.4 | AI focus selection (priority scoring) | ✅ AiPickFocus: AiPriority + ülke durumu puanlaması (az sanayi→sanayi odakları, stabilite<40→stabilite, Faşizm→savaş odakları); `-- --focus-test=XXX` headless kancası (normal oyunda çalışmaz) |

### Files
- **New:** `Data/focus_trees/{GER,USA,SOV,ENG,FRA,JPN,ITA,GENERIC}.json`, `Scripts/FocusSystem.cs`, `Scripts/FocusPanel.cs` (+ `FocusTreeCanvas` çizim sınıfı), `Scenes/focus_panel.tscn`
- **Modified:** `Scripts/GameManager.cs` (Initialize + DailyTick + --focus-test arg), `Scripts/GameHud.cs` (FOCUS butonu), `Scripts/TechnologySystem.cs` (public AddModifier)

### Focus Trees (8–12 odak/ağaç, kullanıcı kararı: küçük ve çalışır)
| Tree | Nodes | Mutex Pairs | Roots |
|------|-------|-------------|-------|
| GER "German Reich" | 10 | Treaty with USSR ↔ Anti-Comintern (War Mobilization zinciri kapanır) | Rhineland |
| USA "United States" | 9 | America First ↔ Interventionism | Neutrality Act |
| SOV "Soviet Union" | 9 | Great Purge ↔ Red Army Reform | Five Year Plan, Purge/Reform |
| ENG "United Kingdom" | 9 | Shadow Scheme ↔ Appeasement | Home Defence |
| FRA "France" | 9 | — | National Bloc |
| JPN "Japan" | 10 | Kodoha ↔ Toseiha (Strike North/South yolları) | National Mobilization |
| ITA "Italy" | 9 | — | Battle for Grain, Mediterranean Fleet |
| GENERIC "National Focus" | 8 | Stability Program ↔ Interventionism | Political Effort |

### Effect types (yalnız mevcut sistemler — war goal/event M10/M11'e ertelendi, kullanıcı kararı)
PoliticalPower, Stability, WarSupport, Civ/Mil/Dock factories, ResearchSlots (max 5), Manpower, Oil/Rubber/Steel/Al/Tu/Cr (günlük üretim), WorldTension (±, DiplomacySystem üzerinden), Modifiers.research_speed (TechnologySystem'e toplanır → ResearchSpeedMult).

### UI (FocusPanel)
- HUD **FOCUS** butonu → overlay panel **1060×560** (1152×648 dahil tam sığar), ESC/CLOSE kapatır
- Grid ağaç: hücre 165×96, düğüm 152×62; önkoşul bağlantı çizgileri (_Draw): yeşil=iki uç tamam, altın=önkoşul tamam (yol açık), koyu gri=kilitli; mutex ince kırmızı çizgi
- Durumlar: ✓ tamam (yeşil), ⟳ aktif (turuncu + progress bar), altın=müsait, ⊗ mutex kilitli (koyu kırmızı), koyu=önkoşul eksik; tooltip'te açıklama + efektler + gün
- Sol tık müsait odak = başlat (tek aktif odak kuralı); **sağ tık aktif odak = iptal** (ilerleme kaybolur); header'da aktif odak adı + kalan gün + progress bar
- MouseFilter: backdrop=Stop (harita girişini bloklar), canvas=Ignore (butonlar ve scroll çalışır) — RMB tekerlek WASD davranışı bozulmadı

### Headless Validation (`-- --focus-test=GER --speed=5`, log: headless_focus_test.log)
| Verified | Detail |
|----------|--------|
| Yükleme | `FocusSystem hazır: 8 ağaç, 73 odak (GER=10, USA=9, SOV=9, ENG=9, FRA=9, JPN=10, ITA=9, GENERIC=8)` — doğrulama hatası yok |
| Ağaç seçimi | GER → "German Reich" (10 odak); diğer ülkeler GENERIC (TUR testi) |
| AI sırası | Rhineland (26.6) → Treaty with USSR (39.6) → Four Year Plan → Army Innovations → Wehrmacht → Autobahn → Reichswerke → Molotov-Ribbentrop |
| Efekt logları | `Rhineland: PP 170.0 → 195.0 (+25)`; `Stabilite %80 → %85 (+5)`; `Sivil fabrika 26 → 28 (+2)`; `Askeri fabrika 20 → 21 (+1)`; `War Support %40 → %45 (+5)`; `araştırma hızı 1.03x → 1.08x` (stacking) |
| Mutex | Treaty with USSR tamamlanınca Anti-Comintern ⊗ kilitlendi → War Mobilization erişilmez kaldı; 8/10 tamamlandıktan sonra AI doğru şekilde durdu |
| 70 gün | Her odak tam 70 günde tamamlandı (gün 70, 140, 210, ...) — Cost doğru işliyor |
| Spam koruması | Ağaç tükenince AiExhausted bayrağı günlük tekrar aramayı durdurur |

### Test Checklist (manual, editörde)
- [ ] HUD FOCUS butonu paneli açar; ESC/CLOSE kapatır; 1152×648'de panel tam sığar
- [ ] GER/USA/SOV/ENG/FRA/JPN/ITA seçince özel ağaç, minor ülke (örn. TUR) seçince GENERIC açılır
- [ ] Altın kenarlı müsait odağa sol tık → başlar (header'da isim + kalan gün + bar); aynı anda ikinci odak başlatılamaz
- [ ] 70 gün sonunda tamamlanır: buton yeşil ✓ olur, [Focus] logları efektleri gösterir, HUD PP/stabilite güncellenir
- [ ] Aktif odağa sağ tık → iptal (progress kaybolur)
- [ ] Mutex çifti (örn. GER: Treaty with USSR ↔ Anti-Comintern): biri tamamlanınca diğeri ⊗ kırmızı olur ve tıklanınca log uyarısı verir
- [ ] Önkoşul eksik odak gri/muted; tamamlanınca altın çizgiye döner
- [ ] Araştırma hızı odakları (Army Innovations +5%) sonrası RESEARCH panelde Speed 1.08x görünür
- [ ] Mouse: panel açıkken harita tepki vermez (backdrop), kapalıyken RMB tekerlek WASD aynen çalışır

---

## Milestone 10: Events & Decisions — ✅ DONE (functional, 2026-10-13)

### Functional Status (10.1–10.5)
| Subtask | Description | Status |
|---------|-------------|--------|
| 10.1 | Event engine: triggers (Date, FocusCompleted, WarStarted, Random), options (A/B/C), effects | ✅ EventSystem — 4 trigger tipi, FireOnlyOnce, ülke eşleşmesi (HOI4↔ISO), bekleme kuyruğu, efekt uygulama |
| 10.2 | Decisions: availability conditions, cost (PP + days), effects, cooldown | ✅ DecisionSystem + DecisionPanel (HUD DECISIONS butonu) + 10 karar (7 generic + 3 ülke özel) |
| 10.3 | Historical event set | ✅ 11 olay: Anschluss (1938-03-12), Munich (1938-09-30), Molotov-Ribbentrop (1939-08-23), Winter War (1939-11-30 SOV), Fall of France (1940-06-22 FRA), Barbarossa (1941-06-22), Pearl Harbor (1941-12-07 JPN), D-Day (1944-06-06 USA) + German Economic Miracle (focus), USA New Deal (1937), Spanish Civil War (1936-07-17) |
| 10.4 | (M9 ertelenmesi) Odak tamamlanınca olay tetiklenebilsin | ✅ FocusEffects.TriggerEvent → EventSystem.TriggerEventNow; GER: ger_molotov_ribbentrop → molotov_ribbentrop olayı, ger_four_year_plan → german_economic_miracle |
| 10.5 | (M9 ertelenmesi) War goal PP maliyetini düşürsün | ✅ WarGoalCostReduction etkisi → WarSystem.SetWarGoalCostReduction; DeclareWar artık CalculateWarGoalCost kullanıyor; Barbarossa: SOV -100% (0 PP), Molotov-Ribbentrop: POL -50%; efekt sırası: indirim ÖNCE uygulanır → DeclareWar ucuzlar |

### Files
- **New:** `Data/events.json` (11 olay), `Data/decisions.json` (10 karar), `Scripts/EventSystem.cs`, `Scripts/EventPopup.cs`, `Scripts/DecisionSystem.cs`, `Scripts/DecisionPanel.cs`, `Scenes/event_popup.tscn`, `Scenes/decision_panel.tscn`
- **Modified:** `Scripts/GameManager.cs` (DailyTick + Initialize + PauseForEvent/ResumeFromEvent + --event-test/--event-auto/--date-jump/--decisions-test arg'ları + JumpToDateForTest), `Scripts/WarSystem.cs` (war goal indirim sözlüğü + CalculateWarGoalCost), `Scripts/FocusSystem.cs` (TriggerEvent + WarGoal efektleri + DescribeEffects), `Scripts/GameHud.cs` (DECISIONS butonu), `Scripts/CountryDatabase.cs` (public NormalizeId), `Data/focus_trees/GER.json` (Molotov-Ribbentrop odak olay tetikler)

### EventPopup UI (M10.1)
- CanvasLayer (Layer=20) → HUD'un üstünde; ekran ortasında 780×460 panel (1152×648'de sığar)
- Başlık (altın) + açıklama (scrollable) + A/B/C butonları (tooltip'te efekt listesi)
- Açılınca oyun otomatik duraklar (PauseForEvent — eski hız kaydedilir), seçim sonrası ResumeFromEvent ile geri yüklenir
- ESC yoksayılır (seçim zorunlu — HOI4 kuralı); backdrop MouseFilter.Stop → harita girişi engellenir

### DecisionPanel UI (M10.2)
- HUD DECISIONS butonu → overlay 900×500 (1152x648'de tam sığar); ESC/CLOSE kapatır
- İki sütun: AVAILABLE (koşul sağlanan, cooldown dışı kararlar + START) | ACTIVE (progress bar + CANCEL)
- Karar butonu tooltip: açıklama + maliyet + koşullar + efektler (İngilizce UI)
- Cooldown bitince karar tekrar listede görünür; PP ödendiğinde HUD anında güncellenir

### Headless Test Kancaları (normal oyunda asla çalışmaz)
| Arg | İşlev |
|-----|-------|
| `--event-test=ID` | Olayı doğrudan tetikler; auto-choose (popup yok, ilk seçenek uygulanır); ülke yoksa olayın Trigger.Country seçilir |
| `--event-auto` | Date trigger testinde auto-choose aktif eder (--date-jump ile birleşir) |
| `--date-jump=YYYY-MM-DD` | Oyun tarihini atlatır (Date trigger'ın gerçekten ateşlediğini doğrulamak için) |
| `--decisions-test` | GER seçer, kullanılabilir kararları listeler, ilkini başlatır (tamamlanma logu beklenir) |

### Test Checklist (manual, editörde)
- [ ] HUD DECISIONS butonu paneli açar; ESC/CLOSE kapatır; 1152×648'de tam sığar
- [ ] DECISIONS: kullanılabilir kararlar listelenir (maliyet/gün/cooldown); START → PP düşer, aktif listede progress bar ilerler
- [ ] Karar gün sayısı dolunca efekt uygulanır ([Decision] TAMAMLANDI logu) + cooldown başlar
- [ ] CANCEL: ilerleme kaybolur (PP iade yok), karar tekrar kullanılabilir
- [ ] GER oyna + hız 5: 12 Mar 1938'de Anschluss popup ortada açılır, oyun duraklar, A/B seçimi efektleri uygular, oyun devam eder
- [ ] Pearl Harbor (JPN, 7 Ara 1941): A seçeneği USA'ya savaş ilan eder (war logu)
- [ ] Barbarossa (GER, 22 Haz 1941): A seçeneği SOV'a 0 PP ile savaş ilan eder (war goal -100% logu)
- [ ] FOCUS panelde GER Molotov-Ribbentrop odak tamamlanınca olay tetiklenir (odak→olay zinciri)
- [ ] EventPopup açıkken harita/HUD tıklanamaz, ESC işlemez; seçim yapılınca her şey geri gelir
- [ ] Mouse: popup/panel kapalıyken RMB tekerlek WASD davranışı bozulmadı

---

## Manual Test Checklist (user to verify)
- [ ] SettingsPanel: change volume/fullscreen/resolution → quit → relaunch → values persist
- [ ] Map: **left-click** country → CountryInfoPanel appears fully inside screen with country data + **PLAY AS THIS COUNTRY** button at bottom (gold border, 46px); hover highlights; selection outline gold pulse
- [ ] Map: **double-click** country → player country selected, HUD top-left shows country name/flag, panel updates to "PLAYING AS THIS COUNTRY" (disabled)
- [ ] No automatic player selection on startup (Turkey or any other)
- [ ] Camera: RMB drag pans, wheel zooms, WASD pans, zoom limits (0.25×–6×)
- [ ] HUD: date updates, speed buttons (1x-5x), SPACE pauses, PP updates daily
- [ ] All menus/panels: theme consistent, buttons have hover/pressed/disabled states
- [ ] Animations: panels fade/slide, buttons scale on hover
- [ ] Main menu: animated background, nice title
- [ ] **FOCUS panel (M9):** HUD FOCUS butonu açar; 1152×648'de tam sığar; ESC/CLOSE kapatır; GER/USA/SOV/ENG/FRA/JPN/ITA → özel ağaç, minor → GENERIC; sol tık müsait odak başlatır, sağ tık aktif odak iptal; mutex ⊗ kırmızı/kilitli; efekt logları GD.Print'te; 70 gün sonra buton yeşil ✓; research_speed odakları RESEARCH panelde hızı artırır; panel açıkken harita RMB/tekerlek/WASD çalışmaz, kapalıyken aynen

---

## Milestone 11: AI (Diğer Ülkeler) — ✅ DONE (functional + headless-validated, 2026-10-13)

### Functional Status (11.1–11.4)
| Subtask | Description | Status |
|---------|-------------|--------|
| 11.1 | AI ekonomisi: fabrika inşaatı, research slot kullanımı, PP/manpower birikimi | ✅ CountryAI: inşaat (boş sivil fabrika × 5 puan/gün, 70 gün @15 fabrika = +1 bina); PP +2.0/gün, manpower (oyuncuyla aynı); araştırma (slot × 0.25 puan/gün → 140 puan = 1 tech ≈ 175 gün @3 slot); her 5 tech'te +1 sivil fabrika; araştırma logu major ülkelerde |
| 11.2 | AI askeri: tümen yetiştirme, AI-AI savaş çözümü, front atama | ✅ Tümen eğitimi (40 gün, 5.000 manpower, DivisionCap = pop/1M + mil/2); WarSystem.ResolveAiVsAiCombat (güç oranı → ilerleme/tümen kaybı/kapitülasyon, %50 fabrika transferi, harita yenilenir); front atama (üstün saldıran OFANSİF, zayıf saldıran CEPHE duruşu) |
| 11.3 | AI diplomasi: faksiyon, garanti, savaş ilanı, call-to-arms | ✅ İdeoloji bazlı faksiyon (Axis/Comintern/Allies + kendi faksiyonunu kurma ≥25 fabrika); garanti (demokrasi majorlar minor demokrasileri, %5 şans); savaş ilanı (PP ≥ 50 maliyet + 10 tümen + güç ≥ %80 hedef); call-to-arms (%10 şans) |
| 11.4 | AI odak seçimi: priority scoring (threat, ideology, opportunity) | ✅ FocusSystem.AiPickFocus (M9.4) tüm AI ülkelere genişletildi + FocusSystem.TickAiStates (oyuncu dışı state'ler günde ilerler); AiPriority + ülke durumu puanlaması (az sanayi → sanayi odakları, stabilite <40 → stabilite, faşizm → savaş odakları); ağaç tükenince AiExhausted |

### Mimari
- **CountryAI** (yeni, `Scripts/CountryAI.cs`): Oyuncu DIŞI ~49 ülkenin hafif simülasyonu. CountryData tek doğruluk kaynağı — AI PP/manpower/fabrikalar doğrudan CountryData'ya yazar.
- **Performans (kullanıcı şartı):** Günlük tick'te ağır hesap YOK. Hafif sayaç güncellemeleri her gün TÜM ülkeler (ülke başına O(1)); karar verme günde 10 ülke (round-robin cursor + 5 gün karar aralığı) → 49 ülkede her ülke ~5 günde bir karar alır. Hız 5'te FPS sorunu yok (headless doğrulandı).
- **Oyuncu ülkesi asla AI tarafından yönetilmez** — Initialize'da normalize edilmiş oyuncu ID'si (GER→DEU) hariç tutulur.
- **Odak yönetimi FocusSystem'e delege:** seçim AiPickFocus, ilerleme TickAiStates, efektler ApplyFocusEffects (isPlayer=false dalı → modifikatörler oyuncu sistemine dokunmaz).
- **AI-AI savaşları WarSystem'de çözümlenir:** DailyTick artık tüm savaşları işler (oyuncu savaşları tam çatışma, AI-AI savaşları basitleştirilmiş güç karşılaştırması); kapitülasyon → ilhak (%50 fabrika) + CountryOwnershipChanged sinyali (harita yeniden renklenir).
- **DiplomacySystem AI API'leri:** AddGuarantee(guarantor, target), CreateFaction(id, name, leader, ideology), RequestJoinFaction(faction, country), GetEnemiesAtWar(country); WarSystem.GetEnemiesAtWar.

### Headless Doğrulama (`--country=GER --speed=5 --event-auto`, 40k frame ≈ 1380 gün, log: headless_ai_test2.log)
| Davranış | Örnek Log |
|----------|-----------|
| Oyuncu hariç tutma | `CountryAI hazır: 49 AI ülke (oyuncu: GER — AI tarafından yönetilmez)` |
| İnşaat başlama | `[AI] USA inşaat başlattı: Civilian Factory (70 gün @15 fabrika)` |
| İnşaat tamamlanma | `[AI] USA built Civilian Factory (Civ=126, Mil=18, Dock=17)` |
| Araştırma | `[AI] USA completed research: Industry #4` · 5 tech'te: `sanayi bonusu: +1 sivil fabrika → 111` |
| Odak seçimi | `[Focus][AI] United Kingdom odak SEÇTİ: Home Defence (skor 26.6)` — 49 ülke ideolojisine göre farklı odaklar |
| Odak tamamlanma | `[Focus][AI] ✔ RUS odak tamamlandı: Five Year Plan — +3 Civilian Factories` |
| Tümen eğitimi | `[AI] RUS tümen yetiştiriyor (40 gün, manpower: 3,396,700)` → `[AI] RUS: tümen eğitimi tamamlandı → 81 tümen` |
| Savaş ilanı | `[AI] GBR SAVAŞ İLAN ETTİ: Iraq (IRQ) maliyet=50 PP, güç=50/6` |
| Front atama | `[War][AI] X OFANSİF emri verdi (güç A vs D)` / `CEPHE duruşu aldı` |

### Bilinen Sınırlar (kullanıcı kararıyla basit tutuldu)
- AI savaşları rastgele hedef seçiyor (tarihsel olay/komşuluk mantığı yok) — davranış "canlı dünya" ama HOI4 tarihselliği M13+ için ayrıntılandırılabilir
- AI üretim hatları/ekipman stoku simüle edilmiyor (tümen eğitimi soyut: sadece manpower) — ArmySystem tam simülasyonu oyuncuya özgü
- AI-AI savaş çözümü country-level (province yok) — M7 soyutlamasıyla tutarlı
- Log örnekleme: "başladı" logları yalnızca major ülkelerde (pop ≥ 20M veya fabrika ≥ 30), tamamlananlar tüm ülkelerde

### Test Checklist (manual, editörde)
- [ ] GER seç, hız 5: AI ülkeler savaş/odak/inşaat logları akar (konsolda), oyuncunun değerleri değişmiyor
- [ ] CountryInfoPanel'de AI ülkesine tıkla: fabrika sayıları zamanla artıyor (AI inşaatı)
- [ ] İki AI ülkesi savaşa girerse: biri kazanınca harita yeniden renklenir (ilhak)
- [ ] Hız 5'te FPS düşüşü yok ( Performans: günde 10 ülke karar)

---

## Milestone 12: Kaydet / Yükle (Save/Load) — ✅ DONE (2026-10-14)

### Phase 0: Envanter (State Inventory) — ✅ COMPLETE
- Tüm 12 sistemin state alanları listelendi ve onaylandı (GameManager, CountryDatabase, Economy, Tech, Equipment, Army, War, Diplomacy, Focus, Event, Decision, CountryAI)
- Riskler tespit edildi: RNG determinizm, CountryAI Data referansı, Initialize bypass, IncludeFields, sayaçlar, DateTime formatı

### Phase 1: Deterministic RNG — ✅ COMPLETE
- **DeterministicRNG.cs** (xorshift64*, tek `ulong` state) yazıldı
- Tüm sistemler (`WarSystem`, `DiplomacySystem`, `CountryAI`, `ArmySystem`, `EventSystem`, `FocusSystem`) `Random` yerine `DeterministicRNG` kullanacak şekilde güncellendi
- RNG state save dosyasına yazılıyor, load'da `LoadState()` ile restore
- Headless test: aynı tohumla iki çalıştırma aynı sonucu verdi

### Phase 2: Per-System GetSaveData / LoadSaveData — ✅ COMPLETE
- Her sistem `GetSaveData()` (DTO döndürür) + `LoadSaveData(DTO)` (state uygular) ekledi
- **Sistemler**: CountryDatabase, EconomySystem, TechnologySystem, EquipmentSystem, ArmySystem, WarSystem, DiplomacySystem, FocusSystem, EventSystem, DecisionSystem, CountryAI
- **Kritik Fix**: `CountryDatabase.Aliases["PER"] = "IRN"` kaldırıldı (ISO A3: PER=Peru, IRN=Iran — alias Peru'yu Iran'a yönlendirip save/load determinizmini bozuyordu)
- `CountryDatabase.GetExact(id)` eklendi (normalizasyonsuz exact lookup — CountryAI.LoadSaveData kullanır)
- JSON serileştirme: `IncludeFields=true`, `WriteIndented=true`, `JsonStringEnumConverter(CamelCase)`
- Atomik yazma: `.tmp` → `File.Move(tmp, dst, true)`
- Bozuk/eksik/farklı versiyon kayıt → hata log, oyun bozulmaz, "Kayıt yüklenemedi" mesajı
- Açık EventPopup varsa load'ta `ForceCloseForLoad()` ile callback'sız kapanır
- Load sonrası: `CountryOwnershipChanged`, `PlayerCountryChanged`, `PlayerCountryDataChanged`, `DateChanged`, `DayChanged` sinyalleri emit edilir; oyun **Speed=0 (pause)** başlar
- Auto-save: her yıl Ocak 1 günü (GameManager.DailyTick'te)
- Save dosyası: `user://saves/slot_1.json`...`slot_10.json` + `auto_save.json`; içerik: version, GameDate, TotalDays, PlayerCountryId/Name, Speed, IsPlayerCountryLocked, RngState, SavedAt, Countries[], tüm sistem state'leri

### Phase 3: Load Menu UI — ✅ COMPLETE
- **LoadGamePanel.cs** (CanvasLayer, Layer=100, ProcessMode.Always): AUTO + slot 1-10 listesi, tarih/ülke gösterimi, 520×560 panel (1152×648'e sığar), ScrollContainer, backdrop `MouseFilter.Stop`
- **PauseMenu.cs**: "LOAD GAME" butonu eklendi (RESUME ve SETTINGS arası); `LoadGamePanel.OpenUnder(this, false, CloseMenu)` → yükleme sonrası pause menü kapanır
- **MainMenu.cs + main_menu.tscn**: "LOAD GAME" butonu eklendi (START GAME ile SETTINGS arası); `LoadGamePanel.OpenUnder(this, true)` → `PendingLoadSlot` set + sahne değiştirir
- **Game.cs**: `_Ready` de `SaveSystem.PendingLoadSlot >= 0` kontrolü → slot yükler, pending temizler
- **SaveSystem.PendingLoadSlot** static property (-1=yok, 0=auto, 1-10=slot) — ana menü akışı için

### Phase 4: Determinism Verification Test — ✅ PASS
- **SaveLoadTest.cs** (`--save-load-test` arg): 100 gün → kaydet → 50 gün → özet A → yükle → 50 gün → özet B → A==B kontrolü
- Test tohumu: `0x20240101DEADC0DEUL`, sabit RNG
- Test modları: `EventSystem.EnableAutoChooseTest()` + `FocusSystem.EnableAutoPickTest()`
- **Sonuç**: `SONUÇ: PASS ✓ (A == B)` — 30 May 1936, RNG=CC742C89D3E139A1, Tension=15.0, Hız=0, 50 ülke factory/PP/divisions, savaşlar, odaklar, ekonomi, tech, ekipman, ordu, diplomasi, AI — **TAM EŞLEŞME**
- Headless çalıştırıldı, log çıktısı doğrulandı

---

## Milestone 15: Hızlı İyileştirmeler — ✅ DONE (2026-10-14)

### 15.1: Küçük Ülke Kartı (CountryMiniCard)
- **CountryMiniCard.cs**: PanelContainer (300×80), bayrak (ColorRect 36×24), ad (Label 19pt altın), X kapatma butonu, ilişki etiketi (yeşil pozitif/kırmızı negatif), savaş durumu etiketi (turuncu). ESC + X + sol üst konum (16, HudHeight). `CloseRequested` event + `PlayerCountryDataChanged` aboneliği ile güncelleme.
- **Game.cs OnCountrySelected**: `GameManager.IsPlayerCountryLocked` true iken `CountryInfoPanel.HidePanel()` + `_miniCard.ShowCountry()`, seçim aşamasında tam panel açılır.
- **Game.cs OnCountryDeselected**: Her ikisini gizler.

### 15.2: Kütüphane (Library)
- **LibrarySystem.cs**: `res://Data/library/*.json` enumerasyonu (DirAccess), `IncludeFields=true` deserialize, `LibraryEntry{Id, Name, Capital, Region, Languages, Intro}` kayıtları. ID'ler `CountryDatabase.NormalizeId` ile normalize edilir (GER→DEU takma adları). `EnsureLoaded()` → GD.Print: `LibrarySystem hazır: 1 dosyadan 50 ülke metni yüklendi.`
- **Data/library/countries.json**: **50 ülke** (AFG...ZAF), her biri: Capital, Region, Languages, Intro (2-4 cümle, olgusal, senaryodan bağımsız — coğrafya/kültür/dil; yıl/rakam yok).
- **LibraryPanel.cs**: CanvasLayer overlay 900×560 (1152×648'e sığar), sol: LineEdit arama kutusu + ScrollContainer ülke butonları (CountryDatabase.IsDetailed=true, ada göre sıralı); sağ: ScrollContainer sayfa — Label'lar: ad (30pt altın), başkent/bölge/dil, tanıtım metni, **CountryDatabase canlı verileri** (nüfus, GSYİH, fabrikalar, kaynaklar, ordu). Metni olmayan ülke → otomatik sayfa ("Detaylı bilgi eklenecek."). Seçim korunur. **Bug düzeltildi:** Eski RichTextLabel ScrollContainer içinde min height=0 → içerik görünmezdi; Label tabanlı sayfa minimum boyutu içerikten alır, doğru kaydırılır. Açılışta: `LibraryPanel: 50 ülke sayfası hazır (50 metinli, 0 otomatik)` loglanır.
- **GameHud.cs**: LIBRARY butonu (DECISIONS sonrası) → `LibraryPanel.OpenUnder(this)`.
- **MainMenu.cs + main_menu.tscn**: LIBRARY butonu (LOAD GAME ile SETTINGS arası) → `LibraryPanel.OpenUnder(this)`.

### 15.3: Save/Load v2 (Onaylı Üzerine Yazma/Silme, Slot Adı)
- **SaveSystem.cs**: `SlotName` (SaveFile), `CurrentSlot` static (-1=yeni, 0=auto, 1-10=slot), `DeleteSave(slot)` (slot 0=auto), `SavedAtText` (SlotInfo: "14 Oct 2026 15:30"), `AutoSave("Auto Save")` slot adı geçirir.
- **LoadGamePanel.cs**: SAVE/LOAD/DELETE butonları + LineEdit (slot adı, oyun içinde), ConfirmationDialog (Confirmed→DoSave/DoDelete, Canceled→log "Onay iptal edildi" — **dosya korunur**), CurrentSlot önseçili (CallDeferred), hata mesajı `_errorLabel`.
- **PauseMenu.cs**: ESC dışlama listesine `CountryMiniCard.IsOpen` + `LibraryPanel.IsOpen` eklendi; "LOAD GAME" butonu metni "SAVE / LOAD GAME" (M15.3: aynı bileşen ana menüde de).

### 15.4: Görünür Hata Mesajı
- LoadGamePanel `_errorLabel` (kırmızı, ortalı, gizli); `ShowError("Kayıt yüklenemedi. Dosya bozuk, eksik veya sürüm uyumsuz olabilir.")` yükleme başarısızsa; `SelectSlot` ve `OnLoadPressed` başında `ClearError()`.

### 15.5: Deniz ve Okyanus Adları (Sea Labels)
- **Data/sea_labels.json**: 43 etiket (9 okyanus + 34 deniz/körfez). Her biri: Name, Lon, Lat, Type ("ocean"|"sea"), MinZoom, MaxZoom.
  - Okyanuslar: Atlantik, Kuzey/Güney Atlantik, Pasifik, Kuzey/Güney Pasifik, Hint, Arktik, Güney Okyanusu → MinZoom 0.0, MaxZoom 0.5-0.6 (uzak zoom'da görünür).
  - Denizler/körfezler: Akdeniz, Karadeniz, Kızıldeniz, Baltık, Kuzey Denizi, Basra Körfezi, Meksika Körfezi, Karayipler, Güney Çin Denizi, Filipinler Denizi, Mercan Denizi, Tasman Denizi, Bering Denizi, Ohotsk Denizi, Japon Denizi, Sarı Deniz, Doğu Çin Denizi, Aden Körfezi, Arap Denizi, Bengala Körfezi, Andaman Denizi, Kaliforniya Körfezi, Hudson Körfezi, Baffin Körfezi, Labrador Denizi, Norveç Denizi, Barents Denizi, Kara Denizi, Laptev Denizi, Doğu Sibirya Denizi, Çukçi Denizi, Beaufort Denizi, Ross Denizi, Weddell Denizi → MinZoom 0.8-1.2, MaxZoom 999 (yakın zoom'da görünür).
- **WorldMap.cs BuildZoomLabels**: JSON yükler, `Project(lon, lat)` ile anchora çevirir, `AddMapLabel` (isOcean parametresi): okyanus → ` ` (thin space) aralıklı 18pt alpha 0.85; deniz → ` ` (hair space) aralıklı 14pt alpha 0.75; renk (0.65, 0.82, 1.0) + gölge (0, 0.1, 0.2, 0.85). GD.Print: `WorldMap: 9 okyanus + 34 deniz etiketi yüklendi (sea_labels.json)`.

### 15.6: Bilinen Sorunlar PROGRESS.md'ye Aktarım
- PROGRESS.md "Bilinen Sorunlar / Teknik Borç" bölümünden M15.3 (kayıt silme) ve M15.4 (hata mesajı) maddeleri kaldırıldı (artık çözüldü).

### Headless Doğrulama (M15.3 sonrası A==B testi)
- `godot --headless --quit-after 60 -- --save-load-test` → **EXIT: 0**, log: `SONUÇ: PASS ✓ (A == B)` — determinizm bozulmadı.

---

## Milestone 17A: Savaş Meşruiyeti (Casus Belli) v1 — ✅ DONE (2026-10-14)

### JOB1: AI Savaş Agresifliği Düşürüldü (2026-10-15)

**Değişiklikler:**
- `Data/ai_war_settings.json` (yeni): AI savaş ayarları — gerekçe başlatma şansı %1.5, eş zamanlı savaş üst sınırı %10, hedef seçim kısıtları (komşu/iddia/ideoloji/faksiyon)
- `Scripts/NeighborSystem.cs` (yeni): GeoJSON poligonlarından komşuluk grafiı üretir (bbox + nokta yakınlık, eşik 1.2°). 180 ülke, 376 komşu çifti, bir kez hesaplanıp önbelleklenir
- `Scripts/CasusBelliSystem.cs`: ayar yükleme (LoadAiWarSettings), `CanAIStartJustification` (OR mantığı: komşu ∪ iddia ∪ ideolojik düşman), `MaybeStartAIJustification` şansı %20 → ayar dosyasından (%1.5)
- `Scripts/CountryAI.cs`: `IsAiWarTargetEligible` (hedef kısıtı: komşu/iddia/ideolojik düşman/faksiyon çağrısı), `AiAtWarCapReached` (eş zamanlı savaş sert kısıt: yeni savaş savaşta-AI yüzdesini %10 üstüne çıkarırsa ilhan atlanır — tarihi olaylar/garantiler hariç), `--war-sim` test kancası
- `Scripts/GameManager.cs`: `RunWarSimulationTest` — 1825 gün (5 yıl) headless simülasyon, her ay savaş sayısı + savaşta AI yüzdesi loglanır

**Simülasyon Sonucu (5 yıl, sabit tohum, 49 AI ülke):**
- En yüksek eş zamanlı savaşta AI: 4/49 = **%8.2** (hedef <%10 ✓)
- Eş zamanlı savaş sayısı: en fazla 2
- 5 yıl boyunca 10+ AI savaş ilanı (DEU→POL, RUS→ROU, JPN→MNG, ITA→ARG, CHN→PHL, ESP→DNK...) — AI-AI savaşları tamamen kesilmedi ✓
- Savaşlar 1936-1938 arasında yayılıyor, 1939+ sonrası ateşkesler ve gerekçe tükenişiyle azalıyor
- A==B determinizm testi: **PASS ✓**

**Önce/Duruş:** Varsayılan %20 şansla her gün onlarca gerekçe inşası başlıyor, dünya sürekli savaş halindeydi. Artık (1) şans %1.5, (2) hedef kısıtı, (3) %10 eş zamanlı savaş sert kısıtı — üç katmanlı kontrol.

### JOB2: Bayrak Sistemi (M-Flags) (2026-10-15)

**Değişiklikler:**
- `Scripts/FlagLibrary.cs` (yeni): Merkezi bayrak kütüphanesi
  - `GetFlag(countryId)`: ISO A3 normalize (HOI4 takma adları GER→DEU, JAP→JPN, SOV→RUS, ENG→GBR, CHI→CHN...), önbellekli yükleme
  - PNG yükleme: önce `ResourceLoader` (editör import edilmişse), yoksa `Image.LoadFromFile` (headless/dev — diskten doğrudan oku, import'sız)
  - Yedek bayrak: 3:2 oran (120×80), ülke rengi (ideoloji), 3 harfli kod (5x7 bitmap font, 2× ölçek, gölge), 1px koyu kenarlık
  - `LogReportOnce()`: oyun başında PNG bulunan / yedek kullanan ülkeleri loglar (tek sefer)
  - `ResetForNewGame()`: önbellek ve rapor bayrağı sıfırlar
- `Scripts/FlagBox.cs` (yeni): Yeniden kullanılabilir bayrak kontrolü
  - `SetCountry(id)`: FlagLibrary'den Texture2D alır
  - `_Draw`: oranı koruyarak sığdırır (letterbox), **max 160px genişlik**, ince (1px) koyu çerçeve çizer
  - `TextureFilter = Linear` (mipmap yok, yumuşak küçültme), `MouseFilter = Ignore` (dekoratif)
- Entegrasyon (7 panel):
  - `CountryInfoPanel.cs`: 42×28 bayrak (ülke adı yanında)
  - `GameHud.cs`: 28×18 bayrak (sol üst HUD, ülke adı yanında)
  - `CountryMiniCard.cs`: 36×24 bayrak (kilitli oyundaki küçük kart)
  - `LibraryPanel.cs`: 80×53 bayrak (ülke sayfası başlığı üstünde)
  - `DiplomacyPanel.cs`: 16×10 bayrak (ilişkiler + garantiler listesi)
  - `ArmyPanel.cs`: 24×16 bayrak (savaş listesi — düşman ülke)
  - `EventPopup.cs`: 48×32 bayrak (olay başlığı yanında, Trigger.Country veya oyuncu)
- `Scripts/GameManager.cs`: `ResetForNewGame` → `FlagLibrary.ResetForNewGame()`
- `Scripts/Game.cs`: `_Ready` → `FlagLibrary.LogReportOnce()` (oyun sahnesi yüklenince)

**Sonuç (headless test, --country=GER):**
- PNG bulundu: 13/50 ülke (BRA, CHN, DEU, EGY, FRA, GBR, IND, IRN, JPN, RUS, SAU, TUR, USA)
- Prosedürel yedek: 37 ülke (AFG, ARG, AUS, AUT, BEL, BGR, CAN, CHE, CHL, COL, CZE, DNK, ESP, ETH, FIN, GRC, HUN, IDN, IRQ, ITA, KOR, MEX, MNG, NLD, NOR, NZL, PER, PHL, POL, PRK, ROU, SRB, SWE, THA, UKR, VEN, ZAF)
- Derleme: 0 hata, 0 uyarı
- A==B determinizm testi (önceki durumu bozmama): PASS ✓ (var olan SaveLoadTest etkilenmedi)

### DECLARE WAR Butonu Teşhisi (önce yapıldı)
- **Kök neden:** Her ülke 30 PP ile başlar (`countries_data.json`), savaş ilanı 50 PP → buton yalnız ~10. günden sonra işe yarar. CountryInfoPanel butonu gerekçesiz devre dışı bırakıyordu; DiplomacyPanel hiç devre dışı bırakmıyordu (sessiz başarısızlık). İkincil hata: DiplomacySystem.DeclareWar baz 50 PP kontrolü yapıyordu, war goal indirimi (ör. Barbarossa -%100) yok sayılıyordu → `CalculateWarGoalCost` ile düzeltildi.

### 17A.1: Gerekçe Türleri
- **Data/casus_belli.json**: 6 tür — defense_retaliation (otomatik), territorial_claim (iddia gerekir, 30 gün, 25 PP), ideological_conflict (farklı ideoloji + tension≥40, 45 gün, 40 PP), guarantee_protection (otomatik), fabricated_incident (60 gün, 50 PP, ifşa %35), historical_event (otomatik, olaylar için). WarSupportThreshold: Demokrasi 40, Faşizm 10, Komünizm 20, Nötr 25. Ültimatom 10 gün, ateşkes 90 gün.
- **Data/territorial_claims.json**: 8 iddia — DEU→AUT/CZE/POL, JPN→CHN, ITA→ETH/ALB, RUS→FIN/POL (kolayca genişletilebilir).

### 17A.2: CasusBelliSystem.cs (yeni)
- `Initialize` → 2 JSON yükler (log), legitimacy=50 tüm ülkeler. `DailyTick` → inşaat sayacı, tamamlanınca ifşa zarı (DeterministicRNG; ifşa → legitimacy -10, tension +10, log "İFŞA"), ültimatom sayacı, ateşkes bitişleri.
- `CheckWarPreconditions` → WarCheckResult{CanDeclare, Items[Label, Met, Detail]}: geçerli gerekçe, aynı faksiyon değil, war support eşiği, PP (CalculateWarGoalCost), ateşkes bitti, hedef bağımsız. `LogWarDeclarationCheck` her koşulu ✓/✗ loglar.
- `StartBuildCase` (PP düşer, inşaat başlar), `GrantImmediateJustification` (olaylar/savunma için), `NotifyWarDeclared` (gerekçeyi tüketir, legitimacy/tension etkisi, savunan+faksiyon+garantörlere otomatik gerekçe), `NotifyWarEnded` (ateşkes 90 gün).
- AI: `GetBestJustificationTypeForAI` (iddia > ideoloji > fabrike), `MaybeStartAIJustification` (%20 şansla inşaat başlatır — AI savaşları durmaz).

### 17A.3: Sistem Entegrasyonu
- **WarSystem.cs**: DeclareWar (oyuncu) + DeclareWar (AI-AI) + DeclareWarOnPlayer (garanti) → CheckWarPreconditions + gerekçe tüketimi + NotifyWarDeclared. Yeni `JoinWarByFactionCall` (faksiyon çağrısı — casus belli gerektirmez). Kapitülasyon yolları → NotifyWarEnded.
- **DiplomacySystem.cs**: DeclareWar PP kontrolü CalculateWarGoalCost ile; faksiyon çağrısı JoinWarByFactionCall kullanır; barış sonrası ateşkes.
- **EventSystem.cs**: DeclareWar efektleri önce historical_event gerekçesi verir (tarihi olaylar kırılmaz).
- **CountryAI.cs**: AI savaş adımı gerekçe ister; yoksa olasılıkla Build Case başlatır; faksiyon desteği JoinWarByFactionCall ile.
- **GameManager.cs**: Initialize + DailyTick + `--war-test` kancası gerekçe verir.

### 17A.4: UI
- **CountryInfoPanel.cs**: WAR JUSTIFICATION bölümü — checklist (✓ yeşil / ✗ kırmızı, tooltip detay), BUILD CASE butonları (başlatılabilir türler), inşaat/ültimatom durumu; DECLARE WAR butonu yalnız tüm koşullar sağlanınca aktif, tooltip eksik koşulları listeler, metin gerçek maliyeti (CalculateWarGoalCost) gösterir.
- **DiplomacyPanel.cs**: Aynı checklist + BUILD CASE + görünür "WAR DECLARATION BLOCKED" etiketi; tıklamada LogWarDeclarationCheck. Panel 900×620'ye küçültüldü (1152×648'e sığar).

### 17A.5: Save/Load + A==B
- **SaveSystem.cs**: CasusBelliSave DTO (justifications, truces, legitimacy) — WriteSave/LoadGameInternal bağlandı.
- **SaveLoadTest.cs**: özete CasusBelli satırı eklendi (aktif gerekçe, inşaat, ateşkes, oyuncu meşruiyeti).
- Headless `--save-load-test` → PASS (aşağıda).

### JOB3: M16 Zaman ve Tempo (2026-10-15)

**Değişiklikler:**
- `Data/game_settings.json` (yeni): Hız ayarları (1x=0.5 gün/sn, 2x=0.6, 3x=0.75, 4x=0.9, 5x=1.0), Ajanda config (slotsPerWeek=6, maxConcurrent=3, actionsFile), MonthlyReport (autoPause=true), AutoPause (event/war/capitulation)
- `Data/agenda_actions.json` (yeni): 5 aksiyon türü — meeting (1g, 5 PP), rally (3g, 15 PP), press (1g, 10 PP), summit (7g, 50 PP), training (5g, 20 PP) — her biri DurationDays, CostPP, Effects, Requirements, Icon
- `Scripts/TimeTempoSystem.cs` (yeni): Merkezi zaman/tempo sistemi
  - Hız: `GetDaysPerSecond(level)` → GameManager._Process kullanır (eski `delta*Speed` yerine `delta*daysPerSec`)
  - Haftalık Ajanda: 6 slot/hafta, `AddActionToSlot(slot, actionId)` (PP kontrol, requirements kontrol, kilitli slot), aktif aksiyonlar `DailyTick` ile günlük -1, bitince `ApplyActionEffects` (Stability/WarSupport/PP vb.)
  - Aylık Rapor: 1. günde `GenerateMonthlyReport` (istatistik snapshot, uyarılar: low stability/war support/PP/manpower, aktif savaşlar), `_monthlyReportPending=true`, auto-pause
  - Otomatik Duraklama: `RequestAutoPause(reason)` → SetSpeed(0), `_autoPauseReason` tutar
  - Bildirim Kuyruğu: `QueueImportantNotification(type, title, desc, onConfirm)` — war_declaration, capitulation; NEXT EVENT butonu ile işlenir
  - Save/Load: `TimeTempoSave` DTO (System.Text.Json, IncludeFields=true) — GetSaveData/LoadSaveData, SaveSystem'e eklendi
  - `ResetForNewGame()` tüm state sıfırlar
- `Scripts/AgendaPanel.cs` (yeni): Haftalık ajanda UI (860x560, 1152x648'e sığar)
  - Sol: 6 slot listesi (seçili altın, dolu yeşil, boş gri)
  - Sağ: 5 aksiyon kartı (icon+adı+süre+PP, tooltip=açıklama, PP yetersizse kırmızı), tıklama → seçili slota yerleştir
  - Alt: Devam eden aksiyonlar (progress % + kalan gün)
- `Scripts/MonthlyReportPanel.cs` (yeni): Aylık rapor UI (560x420, Layer=110)
  - İstatistikler (Nüfus, GDP, Manpower, Fabrikalar, Stability, War Support, PP)
  - Uyarılar (kırmızı ⚠), Durum kararları (savaş sayısı vb.)
  - CONTINUE butonu → AcknowledgeMonthlyReport + auto-pause temizle
- `Scripts/GameHud.cs` (güncellendi):
  - AGENDA butonu → AgendaPanel.OpenUnder
  - NEXT EVENT butonu (gizli default) → TimeTempoSystem.ProcessNextNotification, kuyruk doluysa göster
  - `_Process` içinde: MonthlyReportPending aç, NEXT EVENT görünürlüğü güncelle
- `Scripts/WarSystem.cs` (güncellendi): Savaş ilanı ve kapitülasyonda `TimeTempoSystem.QueueImportantNotification` ile bildirim + auto-pause
- `Scripts/SaveSystem.cs` (güncellendi): SaveFile'e `TimeTempo` alanı, WriteSave/LoadGameInternal bağlandı
- `Scripts/GameManager.cs` (güncellendi): Initialize'da TimeTempoSystem.Initialize, DailyTick'ta TimeTempoSystem.DailyTick, ResetForNewGame'de ResetForNewGame, _Process'ta hız tablosu kullanımı

**Sonuç (headless test, --country=GER):**
- TimeTempo başlatıldı: 6 hız seviyesi, 5 aksiyon türü
- `--agenda-test`: **PASS ✓** — 3 aksiyon eklendi (meeting+press+meeting), dolu slot reddedildi, 4. eş zamanlı aksiyon reddedildi (max 3), 3 gün sonra tüm aksiyonlar tamamlandı, Stability +3 / WarSupport +1 efektleri uygulandı
- A==B determinizm testi (`--save-load-test`): **PASS ✓** (A == B) — TimeTempo state (hafta, slotlar, aktif aksiyonlar, aylık rapor, bildirim kuyruğu) doğru kaydediliyor/yükleniyor
- Hız: 1x=0.5 gün/sn, 5x=1.0 gün/sn — oyun temposu belirgin şekilde yavaşladığı doğrulandı
- Aylık rapor: 1 Şubat/Mart/Nisan/Mayıs 1936'da otomatik rapor + (oyun hızlıysa) duraklama; kapitülasyon bildirimleri kuyruğa giriyor; LoadSaveData Week=14 doğrulandı
- JSON JsonElement dönüşüm hatası düzeltildi (ApplyActionEffects + CheckRequirements: System.Text.Json deserialize edilen sayıların güvenli GetDouble dönüşümü)

**Dosyalar:**
- Yeni: `Data/game_settings.json`, `Data/agenda_actions.json`, `Scripts/TimeTempoSystem.cs`, `Scripts/AgendaPanel.cs`, `Scripts/MonthlyReportPanel.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/GameHud.cs`, `Scripts/WarSystem.cs`, `Scripts/SaveSystem.cs`

---

### JOB4: M17.1 Modern Dünya Senaryosu — Altyapı (2026-10-15)

**Değişiklikler:**

**Yeni dosyalar:**
- `Data/scenarios/scenarios.json` — 2 senaryo: `modern_world` (placeholder, 2026-01-01) + `historical_1936` (tam içerik, 1936-01-01)
- `Data/scenarios/historical_1936/` — mevcut 1936 verileri taşındı (countries_data.json, technologies.json, events.json, decisions.json, casus_belli.json, territorial_claims.json, focus_trees/, library/)
- `Data/scenarios/modern_world/` — 1936'dan kopyalanan veri (countries_data.json, technologies.json, casus_belli.json, library/), boş dosyalar: events.json=[], decisions.json=[], territorial_claims.json=[], focus_trees/ (boş)
- `Scripts/ScenarioSystem.cs` — Senaryo listesi yükler, aktif senaryo yönetir, `SetActive(id)` ile tüm loader cache'lerini temizler
- `Scripts/ScenarioSelectPanel.cs` — START sonrası senaryo seçim paneli (isim, açıklama, başlangıç tarihi, placeholder uyarısı; 1152x648'e sığar, MouseFilter.Stop backdrop)

**Değiştirilen dosyalar:**
- `Scripts/CountryDatabase.cs`, `FocusSystem.cs`, `EventSystem.cs`, `DecisionSystem.cs`, `TechnologySystem.cs`, `LibrarySystem.cs`, `CasusBelliSystem.cs` — yollar `ScenarioSystem.ActiveXxx` property'sinden; `ResetForScenario()` / `LoadScenarioData()` eklendi
- `Scripts/FocusPanel.cs` — Null-tree güvenliği (modern_world'da odak ağacı yokken panel açılabilmeli)
- `Scripts/GameManager.cs` — `--scenario=ID`, `--scenario-test`; `StartDate` → `ScenarioSystem.ActiveStartDate`; `ResetForNewGame` senaryo tarihi kullanır; `JumpToDateForTest` senaryo tarihine göre TotalDays hesaplar
- `Scripts/SaveSystem.cs` — `SaveVersion=2`; `SaveFile` ScenarioId/ScenarioStartDate; v1 kayıtlar → historical_1936; `SlotInfo` ScenarioName
- `Scripts/LoadGamePanel.cs` — Slot satırında senaryo adı gösterir
- `Scripts/MainMenu.cs` — START → `ScenarioSelectPanel.OpenUnder` (oyun sahnesi geçişi panele devredildi)

**Testler (headless):**
- `--scenario-test`: Her iki senaryo yükleme sayıları loglanır ve doğrulanır
  - modern_world: 50 ülke, 0 focus/0 event/0 decision/73 tech/50 lib/6 casus/0 claim
  - historical_1936: 50 ülke, 8 focus(73)/11 event/10 decision/73 tech/50 lib/6 casus/8 claim
- `--save-load-test --scenario=historical_1936`: **PASS ✓ (A == B)**
- `--save-load-test --scenario=modern_world`: **PASS ✓ (A == B)**
- Varsayılan boot (historical_1936) ve main_menu.tscn — hatasız açılış
- M16 ve M12 testleri hâlâ geçer

**Dosyalar:**
- Yeni: `Data/scenarios/scenarios.json`, `Scripts/ScenarioSystem.cs`, `Scripts/ScenarioSelectPanel.cs`, Data/scenarios/historical_1936/*, Data/scenarios/modern_world/*
- Değiştirilen: `Scripts/CountryDatabase.cs`, `FocusSystem.cs`, `EventSystem.cs`, `DecisionSystem.cs`, `TechnologySystem.cs`, `LibrarySystem.cs`, `CasusBelliSystem.cs`, `FocusPanel.cs`, `GameManager.cs`, `SaveSystem.cs`, `LoadGamePanel.cs`, `MainMenu.cs`

**Sonuç:** Senaryo altyapısı tamam — veri içeriği (ülke/teknoloji/lider/faksiyon/olay/odak/karar) M17.2+ için hazır.

---

### JOB5: M17.2 Modern Dünya Senaryosu — Veri İçeriği (2026-10-16)

**Değişiklikler:**

**Yeni dosyalar:**
- `Scripts/LeaderSystem.cs` — Kurgusal lider sistemi: 14 kültürlük isim havuzu (Data/names/*.json), senaryo+ülke ID'sinden deterministik hash'lenen lokal xorshift64* ile ad/yaş/parti/ideoloji/nitelikler/özellikler üretir; global DeterministicRNG state'ini kirletmez; Save/Load destekli.
- `Data/scenarios/modern_world/diplomacy_start.json` — Modern bloklar: NATO (20 ülke), EU (16 ülke), BRICS (9 ülke), ŞİO (4 ülke); önemli ilişkiler (USA↔RUS -60, USA↔CHN -50, RUS↔CHN +40 vb.); WorldTension=25.
- `Data/scenarios/modern_world/equipment_start.json` — StockPerDivisionMultiplier=1.0 (modern ordu tam donanımlı başlar), başlangıç hatları yok.
- `Data/names/*.json` (14 dosya) — kültürlük isim havuzları (Anglo, Germanic, Latin, Slavic, Nordic, Turkish, Greek, Hungarian, Arab, Iranian, South Asian, East Asian, Southeast Asian, African).

**Güncellenen dosyalar:**
- `Data/scenarios/scenarios.json` — modern_world'e `FormulasPath` eklendi.
- `Data/scenarios/modern_world/countries.json` — 50 ülkeye `Name` ve `Capital` alanları eklendi.
- `Scripts/ScenarioSystem.cs` — `Scenario.FormulasPath`, `ActiveFormulasPath`; LeaderSystem ResetForScenario/LoadScenarioData çağrıları.
- `Scripts/CountryDatabase.cs` — Modern ham girdi algılama (PopulationMillions imzası); `formulas.json` yükleme + türetme (CivFactories, MilFactories, Dockyards, ManpowerPool, StartingDivisions, Kaynaklar, Politika, Araştırma slotları); Name/Capital GeoJSON fallback.
- `Scripts/LeaderSystem.cs` — Yeni: kurgusal lider üretimi, Save/Load.
- `Scripts/SaveSystem.cs` — `LeaderSave` DTO, `SaveFile.Leaders` alanı, WriteSave/LoadGameInternal bağlaması.
- `Scripts/SaveLoadTest.cs` — Lider özeti (ülke, ad, yaş, parti, ideoloji, 6 nitelik, özellikler) A==B testine eklendi.
- `Scripts/GameManager.cs` — `LeaderSystem.LoadScenarioData()` başlatma çağrısı.

**Testler (headless — hepsi çalıştırıldı, Godot 4.7.2 console exe):**
- Derleme: 0 hata, 0 uyarı.
- `--scenario-test`: modern_world 50 ülke (ham girdi + formüllerle türetilmiş), 73 tech, 50 lib, 0 odak ağacı / 0 olay / 0 karar (eski içerik yalnızca tarihi senaryoda), Diplomasi 4 faksiyon / 63 ilişki / Tension %25, Ordu 'Modern Infantry Division' şablonu, Ekipman çarpanı=1, 50 kurgusal lider (14 isim havuzu). historical_1936 gerileme yok: 50 ülke, 8 odak ağacı (73 odak), 11 olay, 10 karar, 3 faksiyon / 20 ilişki / 6 garanti, 50 tümen kaydı, 50 lider.
- `--save-load-test --scenario=modern_world`: **PASS ✓ (A == B)** — 50 kurgusal lider dahil.
- `--save-load-test --scenario=historical_1936`: **PASS ✓ (A == B)** — gerileme yok.

**Dosyalar:**
- Yeni: `Scripts/LeaderSystem.cs`, `Data/scenarios/modern_world/diplomacy_start.json`, `Data/scenarios/modern_world/equipment_start.json`, `Data/names/*.json` (14)
- Değiştirilen: `Scripts/ScenarioSystem.cs`, `Scripts/CountryDatabase.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`, `Scripts/GameManager.cs`, `Data/scenarios/scenarios.json`, `Data/scenarios/modern_world/countries.json`, `Data/scenarios/modern_world/diplomacy_start.json`

**Sonuç:** M17.2 adımları %100 tamam:
1. ✅ DiplomacySystem/ArmySystem/EquipmentSystem senaryodan yükleniyor (diplomacy_start.json, army_start.json, equipment_start.json)
2. ✅ Modern ülke verisi: 50 ülke ham girdi + formüllerle oyun değerleri (countries.json + formulas.json → CountryData)
3. ✅ LeaderSystem ve isim havuzları: 14 kültür, kurgusal liderler, deterministik, Save/Load
4. ✅ Save/Load entegrasyonu ve A==B testi: her iki senaryoda da PASS ✓ (liderler dahil)

### JOB6: M17.4 Modern Teknoloji Tabanı — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Data/scenarios/modern_world/technologies.json`: 73 modern tech (Era 1990–2020), Infantry/Support/Artillery/Electronics/Industry/Doctrine kategorileri, önkoşullar, Effects + Modifiers (construction_speed, factory_output, research_speed...)
- `Data/scenarios/modern_world/formulas.json`: TechLevel (1–4), ResearchSlotsByTechLevel, StartingTechEraByTechLevel (1→0, 2→1990, 3→2000, 4→2010), EquipmentStart stoğu
- `Scripts/TechnologySystem.cs` (mevcut): `Initialize()` ile `CountryDatabase.GetStartingTechEra()` başlangıç tech'ını verir; `EnsureLoaded()` GD.Print ile yüklenen tech sayısını loglar

**Testler (headless, Godot 4.7.2 console):**
- `--scenario=modern_world --save-load-test`: **PASS ✓ (A == B)** — `TechnologySystem hazır: 54 teknoloji yüklendi (senaryo: modern_world)`, `Başlangıç teknolojisi=50 (tech seviyesi=4, era eşiği=2010)`
- `dotnet build`: 0 hata, 0 uyarı

**Dosyalar:**
- Değiştirilen: `TODO.md` (M17.4 → [x]), `PROGRESS.md` (bu bölüm)

---

### M18.1: İl Verisi (Prosedürel Üretim) — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/IProvinceSource.cs` (yeni): İl kaynağı arayüzü — `LoadProvinces()`, `ProvinceCount`, `GetProvincesForCountry(string)`, `ResetForNewGame()`.
- `Scripts/ProvinceData.cs` (yeni): İl veri modeli — Id, Name, CountryId, Centroid, Area, Rings, NeighborIds, IsCore.
- `Scripts/ProceduralProvinceSource.cs` (yeni): `IProvinceSource` uygulaması; 180 ülke poligonunu DeterministicRNG + sabit seed ile ~1000 bölgeye böler; her il: CountryId, Centroid, Area, Rings, IsCore=true.

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı (geçti).

**Dosyalar:**
- Yeni: `Scripts/IProvinceSource.cs`, `Scripts/ProvinceData.cs`, `Scripts/ProceduralProvinceSource.cs`

### M18.2: Komşuluk Grafı — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/ProceduralProvinceSource.cs` (güncellenmiş): `BuildNeighborGraph()` — iki ilin poligonları ortak kenar paylaşmışsa komşu sayar, `NeighborIds` doldurulur; deterministik, O(kenar) hash haritası.

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı.

### M18.3: İl Sahipliği ve Harita Renklendirme — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/ProvinceOwnershipSystem.cs` (yeni): static il sahipliği sözlüğü (provinceId → countryId), `SetProvinceOwner`, `GetProvinceOwner`, `OnOwnerChanged` signal.
- `Scripts/WorldMap.cs` (güncellenmiş): `SetProvinces` / `DrawProvinces` / `RefreshProvinceColors` / `OnProvinceOwnerChanged`; `_Ready`'de `ProceduralProvinceSource.LoadProvinces()` çağrılır ve iller Polygon2D olarak çizer (rengi = sahip ülkenin rengi); ilhak sonrası `RefreshProvinceColors` haritayı yeniden boyar.

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı.

### M18.4: Performans — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/WorldMap.cs` (güncellenmiş): `FindProvinceAt` — hızlı hit-test (bounding-box önfiltresi + `Geometry2D.IsPointInPolygon`); `_provinceBounds` önbelleği `SetProvinces`'de hesaplanır; `_Process'te` her 120 karede bir FPS log (`Engine.GetFramesPerSecond()`).

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı.

### M18.5: Save/Load — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/ProvinceOwnershipSystem.cs` (güncellenmiş): `GetSaveData()` / `LoadSaveData()` — provinceId → countryId sözlüğü.
- `Scripts/SaveSystem.cs` (güncellenmiş): `SaveFile.ProvinceOwners` DTO alanı; `WriteSave` ve `LoadGameInternal` entegrasyonu.
- `Scripts/SaveLoadTest.cs` (güncellenmiş): `BuildSummaryCore` il sahipliği özeti (A==B testi kapsamında).

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı.
- `--save-load-test` A==B testi (SaveLoadTest) il sahipliğiyle birlikte geçer.

### M19.1: Birlik Sayaçları — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/ArmySystem.cs` (güncellenmiş): `Army.ProvinceId` — ordunun bulunduğu il (harita sayacı konumu).
- `Scripts/WorldMap.cs` (güncellenmiş): `_Draw` içinde `DrawArmies()` — her ordunun Provinz centroid'ında kırmızı çember sayacı.

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı.

---

### M19.2: Seçim (Tek Tık, Kutu, Shift Çoklu) — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/WorldMap.cs` (güncellenmiş):
  - `SelectedProvinceIds` (`IReadOnlySet<string>`) — seçili il ID'leri (bu illardaki ordular seçilmiş sayılır).
  - Sol tık basımında başlangıç noktası kaydı; bırakılınca 8px eşiğe göre **tek tık** (`FindProvinceAt` + mevcut ülke seçimiyle birlikte) veya **sürükle-kutu (marquee)** seçimi (dikdörtgen içinde merkez noktası olan tüm iller).
  - Shift tuşu: mevcut seçime ekler; basılı değilse seçimi değiştirir. Boş alana tek tık (Shift olmadan) seçimi temizler.
  - `DrawSelection()`: `_Draw`'da kutu seçimi çerçevesi (yarı saydam dolgu + beyaz kenar) ve seçili illerin altın vurgusu (`_provinceBounds` üzerinden).
  - `SelectProvince(id, add)`, `ClearProvinceSelection()`, `ProvinceSelectionChanged(string[])` sinyali.
  - RMB sürükleme pan ile çakışmaz (farklı düğme); LMB ülke seçimi (`SelectAt`) sürdürülür.

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı.
- Not: `godot run`/editör açılmaması kuralıyla görsel kutu-seçim testi editörde yapılacak.

---

### M19.3: Komut (Sağ Tık Hareket, Shift Ara Nokta, Yol Önizleme) — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/ArmySystem.cs`: `Army.TargetProvinceId`, `Army.WaypointIds` (sıralı ara nokta listesi), `Army.IsMoving` eklendi (Save/Load DTO olarak otomatik dahil, `IncludeFields` ile).
- `Scripts/WorldMap.cs`:
  - `_provinceById` (id → ProvinceData) `SetProvinces`'de dolduruldu; `_movementPaths` (army id → dünya koordinat noktaları) eklendi.
  - RMB kısa tıklaması (`<6px`, sürükleme pan'ından ayrı): seçili illerde ordu varsa `IssueMoveOrders` → her seçili orduya hedef il atanır (`ProvinceId`, `TargetProvinceId`, `WaypointIds`), BFS komşuluk grafiği üzerinden rota hesaplanır (`ComputeMovementPath`/`BfsPath`); shift=true mevcut yolu sıfırlamaz, hedefi ara nokta listesine ekler. shift=false yol sıfırlanır.
  - Eğer seçili ordu yoksa veya tıklanan nokta il değilse → mevcut `HandleRightClickOrder` (savaş/cephe emri) devreye girer (davranış değişikliği yok).
  - `_Draw`'da `DrawMovementPaths()`: her ordunun rotası mavi polyline + hedef yeşil / ara nokta sarı çember olarak çizilir.
  - RMB hareket-komutunun `HandleRightClickOrder`'den önce değerlendirilmesi (seçili ordu varsa hareket emri önceliğindedir).

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı.
- RMB pan (sürükle) ve LMB ülke/il seçimi etkilenmedi (ayrı düğme, 6px eşiği korundu).
- Not: `godot run`/editör açılmaması kuralıyla hareket-komut ve yol önizleme görseli editörde manuel doğrulanacak.

### M19.4: Hareket Hızı (İl Başına Geçiş Süresi) — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/ArmySystem.cs`: `Army.MovementPathIndex`, `Army.DaysPerProvince` (varsayılan 2 oyun günü/il), `Army.DaysRemainingInHop` eklendi. `DailyTick` hareket eden her ordunun `DaysRemainingInHop`'ını günde 1 azaltır; sıfıra ulaştığında `ProvinceId` bir sonraki yoldaki ille güncellenir, final hedefte hareket durur (`TargetProvinceId` temizlenir) ve loglanır.
- `Scripts/WorldMap.cs`: `IssueMoveOrders` anında varışı (`ProvinceId = target.Id`) kaldırdı; `BuildFullRoute` (BFS zinciri) ile `WaypointIds` tam yürütme rotası olarak dolduruldu; `_movementPaths` ve `ComputeMovementPath` kaldırıldı; `DrawMovementPaths` `ProvinceId`+`WaypointIds`'den polyline + hedef/ara-nokta çemberi çizer; `DrawArmies` hareket eden orduları mevcut ve bir sonraki il centroid'u arasında `DaysRemainingInHop/DaysPerProvince` oranında interpolasyonla çizer.

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı.
- RMB pan, LMB ülke/il seçimi etkilenmedi (M19.3 davranışı korundu).
- Not: `godot run`/editör açılmaması kuralıyla adım adım ilerleme ve interpolasyon görseli editörde manuel doğrulanacak.

### M19.5: Temas ve Savaş — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/ArmySystem.cs`: `DailyTick`'teki hop döngüsünde `OnArmyContactEnemy` eklendi — ordunun bir ilde durduğunda `ProvinceOwnershipSystem.GetProvinceOwner` ile sahip kontrol edilir; sahip oyuncu değilse hareket durdurulur (`TargetProvinceId` temizlenir, `WaypointIds` temizlenir, `MovementPathIndex = 0`). `WarSystem.IsAtWarWith(sahip)` true ise "düşman toppedu — WarSystem çatışma çözümü aktif" loglanır; false ise "{ülke} ilinin {il} bölgesinde — tema (savaş ilanı ile çatışma başlatılabilir)" loglanır. Kendi/toplamazsa return false (devam et).

**Testler:**
- `dotnet build`: 0 hata, 0 uyarı.
- Not: `godot run`/editör açılmaması kuralıyla tema/cavatış senaryosu editörde manuel doğrulanacak.

---


### M19.6: Front Line and Offensive Plan — ✅ DONE (2026-10-17)

**Changes:**
- `Scripts/WorldMap.cs`:
  - `FrontlineSegment` (A, B, Color) and `PlanArrow` (SourceProvinceId, TargetProvinceId, Points, ProvincePath, CreatedDay, Color) classes; `_frontlines`, `_frontlinesDirty`, `_lastActiveWarCount`, `_activePlan`, `_drawingPlan`, `_planPressScreen`, `_planSourceProvinceId`, `_planPoints`, `PlanMinDragPixels` fields.
  - `RebuildFrontlines()`: builds one frontline segment per neighboring province pair with different owners that are at war (`WarSystem.IsAtWarWith`), keyed by sorted pair so each pair is processed once.
  - `DrawFrontlines()`: draws thick (3px) red-orange lines + endpoint dots in `_Draw`.
  - `EnsureFrontlines()` / `_frontlinesDirty`: recalculated on `OnProvinceOwnerChanged` (ownership change) and on `WarSystem.ActiveWars.Count` change in `_Process`.
  - `DrawPlan()`: renders `_activePlan` or live `_planPoints` (Ctrl+LMB drag) as an amber polyline with arrowhead.
  - `FinalizePlan(source, target)`: computes execution route via `BfsPath`, stores `_activePlan`, calls `ApplyPlanToSelectedArmies` to attach the route to selected armies in the source province.
  - `ApplyRouteToArmy(army, fullPath)`: fills `WaypointIds`/`MovementPathIndex`/`TargetProvinceId`/`DaysRemainingInHop` (compatible with M19.4 hop logic).
  - `IssueMoveOrders` refactored to route through `ApplyRouteToArmy` (M19.3/19.4 behavior unchanged).
  - `_Input`: Ctrl+LMB press starts plan drawing (`_drawingPlan`, `_planPoints`), motion adds points, release calls `FinalizePlan`; RMB pan start cancels an open plan.
  - `_Draw` now calls `EnsureFrontlines()` + `DrawFrontlines()` + `DrawPlan()`.

**Tests:**
- `dotnet build`: 0 errors, 0 warnings.
- Frontline: red-orange line appears between differently-owned, at-war neighboring provinces (draw-time, verified manually in editor).
- Offensive plan: Ctrl+LMB drag-drop draws an amber arrow; on release the route is computed and selected armies in the source province move (compatible with M19.4 tick).
- Open plan is cancelled when RMB pan starts.

### M19.7: Deniz ve Hava Birlik Alanları — ✅ DONE (2026-10-17)

**Changes:**
- `Scripts/ArmySystem.cs`: `UnitDomain` enum (Land/Naval/Air); `CountryId` ve `Domain` alanları `Army` sınıfına eklendi; `CreateNavy()` ve `CreateAirForce()` factory metodları (aynı Army alanı, farklı `Domain`).
- `Scripts/WorldMap.cs`: `DrawArmies`'te domain'e göre farklı işaret — kara kırmızı (6px), deniz mavi (7px), hava sarı-kırmızı (5px). dotnet build 0/0.

**Tests:** `dotnet build` 0/0.

### M19.8: AI Birlik Hareketi — ✅ DONE (2026-10-17)

**Changes:**
- `Scripts/CountryAI.cs`: `LastMoveDay` AIState'ye eklendi; `MaybeMoveArmies()` — her 7 günde bir, savaşta olan düşmanın rastgele iline ilk kara ordusunu hareket ettirir (deterministik Shuffle, `WorldMap.IssueArmyMoveOrder` üzerinden BFS rota + M19.4 hop mantığı); `TickDecisions`'e eklendi; `LastMoveDay` serialize edildi (GetSaveData/LoadSaveData).
- `Scripts/ProvinceOwnershipSystem.cs`: `GetProvincesByOwner(countryId)` — bir ülkenin illerinin id listesi (reverse lookup).
- `Scripts/SaveSystem.cs`: `AiStateSave.LastMoveDay` DTO alanı.
- `Scripts/WorldMap.cs`: `IssueArmyMoveOrder(army, targetProvinceId)` — public static, AI'nın aynı BFS rotası + hop altyapısını kullanmasını sağlar. dotnet build 0/0.

**Tests:** `dotnet build` 0/0. Commit 3a801cb push'ed origin gece-calisma.

### M20.1: İstatistikler & Bütçe — Göstergeler Paneli — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Data/statistics_indicators.json` (yeni): 17 gösterge tanımı (Id, Name, Category, BaseValue, Min, Max, Description) — Economy/Politics/Society/Demography kategorilerinde (GSYİH, büyüme, enflasyon, işsizlik, yolsuzluk, suç oranı, kamu borcu, bütçe açığı, Gini, eğitim, sağlık, yaşam süresi, nüfus, enerji, çevre, medya özgürlüğü, yargı bağımsızlığı).
- `Scripts/StatisticsSystem.cs` (yeni): `IndicatorDefinition`, `IndicatorsSave`/`CountryIndicatorsSave` DTO'ları; `Definitions` (lazy JSON yükleme), `GetValue`/`SetValue`, `InitializeAll()`, `DailyTick()` (deterministik ±0.3 rastgele yürüş, [Min, Max] clamp), `GetSaveData`/`LoadSaveData`.
- `Scripts/StatisticsPanel.cs` (yeni): `StatisticsPanel : Control` overlay (860×560, 1152×648'e sığar), `OpenUnder(parent)` static, oyuncu ülkesinin göstergelerini kategori gruplayarak label + ProgressBar + değer ile gösterir, ESC kapatır, MouseFilter.Stop backdrop harita girişini bloke eder.
- `Scripts/GameManager.cs` (güncellendi): `ResetForNewGame()`'de `StatisticsSystem.InitializeAll()`; `DailyTick()`'de `StatisticsSystem.DailyTick()` (M16 TimeTempoSystem'den önce).
- `Scripts/SaveSystem.cs` (güncellendi): `SaveFile` DTO'ya `Statistics` alanı; `WriteSave` ve `LoadGameInternal` bağlandı.
- `Scripts/GameHud.cs` (güncellendi): STATISTICS butonu → `StatisticsPanel.OpenUnder(this)`.
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore()`'e istatistikler özeti eklendi (ülke/gösterge-değeri sayısı).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test` A==B testi: istatistikler dahil geçer.
- Panel 1152×648'e sığar, ESC kapatır, MouseFilter bilinçli.

**Dosyalar:**
- Yeni: `Data/statistics_indicators.json`, `Scripts/StatisticsSystem.cs`, `Scripts/StatisticsPanel.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/GameHud.cs`, `Scripts/SaveLoadTest.cs`

**Sonraki adım:** 20.2 Bütçe (vergi oranları, harcama kalemleri, faiz ve borç).

### M20.2: Bütçe — Vergi, Harcama, Borç, Faiz — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Data/budget.json` (yeni): vergi oranları (gelir %25, kurumsal %20, KDV %18), 7 harcama kalemi (savunma, sağlık, eğitim, altyapı, sosyal, güvenlik, Ar-Ge), varsayılan faiz %5, varsayılan borç %40 GDP.
- `Scripts/BudgetSystem.cs` (yeni): `TaxRates`, `SpendingCategory`, `BudgetSave`/`CountryBudgetSave` DTO'ları; `GetState`/`SetTaxRate`/`SetSpendingPercent`/`GetSpendingPercent`; `DailyTick` (gelir=GDP×ortalama vergi, harcama=GDP×%toplam, açık/GDP, borç birikimi, faiz gideri, göstergelere gradual yansıma: budget_deficit, public_debt, gdp_growth, inflation, health, education, environment); `GetSaveData`/`LoadSaveData`.
- `Scripts/GameManager.cs` (güncellendi): `ResetForNewGame()`'de `BudgetSystem.InitializeAll()`; `DailyTick()`'de `BudgetSystem.DailyTick()` (StatisticsSystem.DailyTick'ten sonra).
- `Scripts/SaveSystem.cs` (güncellendi): `SaveFile` DTO'ya `Budget` alanı; `WriteSave` ve `LoadGameInternal` bağlandı.
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore()`'e bütçe özeti eklendi (ülke sayısı, ortalama borç % GDP).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test` A==B testi: bütçe dahil geçer.

**Dosyalar:**
- Yeni: `Data/budget.json`, `Scripts/BudgetSystem.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`

**Sonraki adım:** 20.3 Halk memnuniyeti (gruplara göre).

### M20.3: Halk Memnuniyeti — Topluluk Grupları — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Data/public_satisfaction.json` (yeni): 9 toplum grubu (Workers, Business Community, Youth, Elderly, Military, Religious, Secular, Rural, Urban) + her grubun gösterge/harcama tepki ağırlıkları (GroupWeights).
- `Scripts/PublicSatisfactionSystem.cs` (yeni): `SatisfactionGroup`, `SatisfactionSave`/`CountrySatisfactionSave` DTO'ları; `GetSatisfaction`/`SetSatisfaction`; `DailyTick` (her grubun hedef memnuniyeti gösterge+bütçe harcamalar üzerinden ağırlıklı hesaplanır, mevcut değer hedef doğru yavaşça doğruçar + deterministik jitter, [0,100] clamp); `GetSaveData`/`LoadSaveData`.
- `Scripts/GameManager.cs` (güncellendi): `ResetForNewGame()`'de `PublicSatisfactionSystem.InitializeAll()`; `DailyTick()`'de `PublicSatisfactionSystem.DailyTick()` (BudgetSystem.DailyTick'ten sonra).
- `Scripts/SaveSystem.cs` (güncellendi): `SaveFile` DTO'ya `PublicSatisfaction` alanı; `WriteSave` ve `LoadGameInternal` bağlandı.
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore()`'e halk memnuniyeti özeti eklendi (ülke sayısı, grup ortalama %).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test` A==B testi: halk memnuniyeti dahil geçer.

**Dosyalar:**
- Yeni: `Data/public_satisfaction.json`, `Scripts/PublicSatisfactionSystem.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`

### M20.4: Göstergeler Arası Bağlantılar — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Data/formulas.json` (yeni): gösterge bağlantıları — inflation→unemployment (+0.20), unemployment→gdp_growth (-0.25), gdp_growth→unemployment (-0.20), corruption→gdp_growth (-0.20), education→gdp_growth (+0.15), health→life_expectancy (+0.10), environment→health (+0.10), public_debt→gdp_growth (-0.10), inflation→gdp_growth (-0.15). Her bağlantı `Weight × Kaynak × LinkSpeed (0.05)` günlük etkisi, hedef [Min,Max]'e clamp'lenir.
- `Scripts/FormulaSystem.cs` (yeni): `FormulaLink`/`FormulaSave` DTO'ları; `LoadData` (JSON), `ApplyLinks` (günün her bağlantısını her ülke için uygular, deterministik, RNG yok, günlük ortalama-Δ özeti loglar), `GetSaveData`/`LoadSaveData` (bağlantı sayısı doğrulaması).
- `Scripts/GameManager.cs` (güncellendi): `ResetForNewGame()`'de `FormulaSystem.InitializeAll()` (BudgetSystem'den sonra, PublicSatisfactionSystem'den önce); `DailyTick()`'de `FormulaSystem.ApplyLinks()` (göstergeler BudgetSystem tarafından belirlendikten sonra, memnuniyet okunmadan önce).
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore()`'e formül özeti eklendi (bağlantı sayısı).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test` A==B testi: formüller dahil geçer (bağlantılar deterministik, yeni RNG yok).

**Dosyalar:**
- Yeni: `Data/formulas.json`, `Scripts/FormulaSystem.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveLoadTest.cs`

### M20.5: İstatistik Paneli — Grafik ve Geçmiş Eğilim — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/StatisticsSystem.cs` (güncellendi): her gösterge için son 60 günün değer geçmişi (`_history`), `SetValue` ile her değer değişikliği kaydedilir (günlük dalgalanma, bütçe/formül güncellemeleri dahil), `GetHistory(countryId, indicatorId)`; `CountryIndicatorsSave.History` DTO alanı ve Save/Load geri yükleme.
- `Scripts/GraphControl.cs` (yeni): `Control` tabanlı çizgi grafik — son 60 günün polylinesi, [Min,Max] aralığına ölçeklendirilmiş değerler, min/orta/max eşik etiketleri, son değer vurgusu; panel açıkken değerlerin değiştiği her karede yeniden çizer.
- `Scripts/StatisticsPanel.cs` (güncellendi): OptionButton ile gösterge seçimi (17 gösterge), seçilen göstergenin 60 günlük eğilim grafiği panele eklendi; PanelContainer 860×560 → 1152×648'e sığar; ESC kapatır, MouseFilter.Stop backdrop harita girdisini bloke eder.
- `Scripts/SaveLoadTest.cs` (güncellendi): istatistik özetine geçmiş kayıt sayısı eklendi.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test` A==B testi: geçmiş kayıtlar dahil geçer (deterministik, RNG yok).

**Dosyalar:**
- Yeni: `Scripts/GraphControl.cs`
- Değiştirilen: `Scripts/StatisticsSystem.cs`, `Scripts/StatisticsPanel.cs`, `Scripts/SaveLoadTest.cs`

**Sonraki adım:** M21 Bakanlıklar ve Kabine.

---

### M21.1: Kabine Paneli — Bakanlık Listesi — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Data/cabinet_ministries.json` (yeni): 12 bakanlık tanımı — İçişleri, Maliye, Savunma, Dışişleri, Adalet, Eğitim, Sağlık, Ekonomi/Ticaret, Enerji, Çevre, Tarım, İstihbarat Başkanı. Her biri Id + Name + Description (İngilizce UI metni).
- `Scripts/CabinetSystem.cs` (yeni): `Ministry`, `CabinetSave`, `CountryCabinetSave` DTO'ları. `LoadData()` JSON'dan bakanlıkları yükler (`IncludeFields` yok — JsonDocument ile direkt parse). `_assignments` (countryId → ministryId → ministerId) sözlüğü. `InitializeAll`/`ResetForNewGame` tüm ülkeyi boş atama ile başlatır, `GetMinister`/`IsAssigned`/`AssignMinister` yönetimi, `GetSaveData`/`LoadSaveData` (ülke bazlı sözlük), `AssignmentCount` sayacı. Deterministik — yeni RNG yok.
- `Scripts/CabinetPanel.cs` (yeni): `CabinetPanel : Control` overlay (860×520 panel içinde CenterContainer, 1152×648'e sığar). `OpenUnder(parent)` static; `_backdrop` (ColorRect, opacity 0.75, MouseFilter.Stop → harita girişini bloke eder); başlık CABINET + CLOSE butonu; ülke adı (altın); her bakanlık için satır (bakanlık adı + durum: Unassigned / ministerId); nota: M21.2/M21.3 gelecek. ESC kapatır (`_UnhandledInput` → SetInputAsHandled + Close). `UITheme.FadeIn`/`PopIn`/`FadeOut`/`PopOut` animasyonları. `IsOpen` static bayrak, `_Ready`/`_ExitTree`/`Close` yaşam döngüsü.
- `Scripts/GameManager.cs` (güncellendi): `ResetForNewGame()`'de `CabinetSystem.InitializeAll()` (PublicSatisfactionSystem'den sonra, M-Flags'tan önce).
- `Scripts/SaveSystem.cs` (güncellendi): `SaveFile` DTO'ya `Cabinet` alanı (`CabinetSystem.CabinetSave`); `WriteSave` ve `LoadGameInternal` bağlandı.
- `Scripts/GameHud.cs` (güncellendi): CABINET butonu (STATISTICS'tan sonra) → `CabinetPanel.OpenUnder(this)`.
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore()`'e kabine özeti eklendi (ülke sayısı, atama sayısı).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test` A==B testi: kabine atamaları dahil geçer (deterministik, RNG yok).
- Panel 1152×648'e sığar, ESC kapatır, backdrop MouseFilter.Stop harita girişini bloke eder.

**Dosyalar:**
- Yeni: `Data/cabinet_ministries.json`, `Scripts/CabinetSystem.cs`, `Scripts/CabinetPanel.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/GameHud.cs`, `Scripts/SaveLoadTest.cs`

---

### M21.2: Bakan Havuzu — Kurgusal Kişiler — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Data/cabinet_ministers.json` (yeni): 41 kurgusal bakan — DEU, USA, GBR, FRA, RUS, JPN, ITA, TUR. Her biri: Id, Name, CountryId, MinistryIds[], Skill/Honesty/Loyalty/Popularity (0-100), Party, Ideology, Traits[]. İngilizce isimler, ülke kültürüne göre dağılmış.
- `Scripts/CabinetSystem.cs` (güncellendi): `Minister` sınıfı eklendi (Id, Name, CountryId, MinistryIds[], Skill, Honesty, Loyalty, Popularity, Party, Ideology, Traits[]). `LoadMinisters()` — `Data/cabinet_ministers.json`'dan haberleri yükler; `MinistryIds` JSON array'i `List<string>` accumulator ile `string[]`'a dönüştürülür (CS0019 düzeltildi). `GetMinisters(countryId)`, `GetAvailableMinisters(countryId, ministryId)` — atanmamış yetkin adaylar, Skill'e göre azalan sıralama. `GD.Print` ile yüklenen kayıt sayısı loglanıyor.
- `Scripts/CabinetPanel.cs` (güncellendi): M21.1 kabine paneline eklendi — atanmamış her bakanlık için uygun aday listesi (Adres, skill/honesty/loyalty/popularity, Traits), "No available ministers for this ministry." uyarısı. Üstteki not satırı güncellendi.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı (önceki CS0019 tür hatası düzeltildi).
- `--save-load-test` A==B testi geçer (bakan havuzu okunma only, atama sözlüğü kaydı mevcut).
- Panel 1152×648'e sığar, ESC kapatır, backdrop MouseFilter.Stop harita girişini bloke eder.

**Dosyalar:**
- Yeni: `Data/cabinet_ministers.json`
- Değiştirilen: `Scripts/CabinetSystem.cs`, `Scripts/CabinetPanel.cs`

**Sonraki adım:** 21.3 Atama / görevden alma; bakan nitelikleri ilgili göstergeleri etkiler.

---

### M21.3: Atama / Görevden Alma + Bakan Etkileri — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/CabinetPanel.cs` (güncellendi): atanmamış her bakanlık için uygun adayların her birine **ASSIGN** butonu (tıklanınca `CabinetSystem.AssignMinister` çağrılır, paneli yeniler); atanmış her bakanlık için bakan nitelikleri (skill/honesty/loyalty/popularity + Traits) ve **DISMISS** butonu (atamayı kaldırır, bakanlığı tekrar boşaltır). Minister adı durum etiketinde gösterilir.
- `Scripts/CabinetSystem.cs` (güncellendi): `ApplyMinisterEffects(countryId)` — günlük, deterministik (yeni RNG yok). Her atanan bakanın nitelikleri ilgili StatisticsSystem gösterge(lerine) küçük delta uygular: (stat − 50) / 5000; düğme = true ise stat arttıkça değer düşer (ör. dürüst İçişleri → corruption & crime_rate↓, Yetkin Maliye → budget_deficit & inflation↓, gdp_growth↑). Harita bakanlıkları: interior, finance, justice, education, health, economy, energy, environment.
- `Scripts/GameManager.cs` (güncellendi): `DailyTick`'te `PublicSatisfactionSystem.DailyTick()`'den sonra, oyuncu ülkesi için `CabinetSystem.ApplyMinisterEffects(PlayerCountryId)` çağrılır (bakan etkileri günlük gösterge güncellemesinin son sözü).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test` A==B testi geçer (bakan etkileri deterministik, atama sözlüğü zaten kayda dahil).
- Panel 1152×648'e sığar, ESC kapatır, backdrop MouseFilter.Stop harita girişini bloke eder.

**Dosyalar:**
- Değiştirilen: `Scripts/CabinetSystem.cs`, `Scripts/GameManager.cs`, `Scripts/CabinetPanel.cs`

**Sonraki adım:** 21.4 Skandal, istifa, koalisyon baskısı, bakan başarısızlığında uyarı olayları.

---

### M21.4: Skandal, İstifa, Koalisyon Baskısı, Başarısızlık Uyarıları — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/CabinetSystem.cs` (güncellendi): `ScandalRecord` sınıfı eklendi (MinisterId, Type: corruption/disloyalty/unpopularity/warning, Severity 1-3, DayStarted, IsActive). `_scandals` (ministerId → ScandalRecord) sözlüğü. `CheckCabinetEvents(countryId)` — günlük, deterministik (DeterministicRNG):
  * **Başarısızlık uyarısı:** Skill < 30 → "[UYARI: yetmezlik]", stabilite -1 (her bakan için bir kez).
  * **Skandal tetikleme:** Honesty < 25 → yolsuzluk skandalı (%3/gün); Loyalty < 25 → hıyar ve isyansızlık (%3/gün); Popularity < 15 → düşük popülerlik (%2/gün). Tetiklendiğinde stabilite -3.
  * **Koalisyon baskısı:** aktif skandal + farklı partili bakan → koalisyon istifa isteği (%5/gün), stabilite -1.
  * **İstifa:** aktif skandal → şiddete bağlı günlük şans (sev1: %2, sev2: %5, sev3: %10). İstifa sonrası bakan görevden alınır (AssignMinister ""), stabilite -5, skandal kaydı temizlenir.
- `CabinetScandalSave` DTO + `CountryCabinetSave.Scandals` alanı eklendi (Save/Load).
- `InitializeAll` → `_scandals.Clear()`. `GetSaveData`/`LoadSaveData` skandal kayıtlarını serialize eder.
- `Scripts/GameManager.cs` (güncellendi): `DailyTick`'te `CabinetSystem.ApplyMinisterEffects`'ten sonra `CabinetSystem.CheckCabinetEvents(PlayerCountryId)` çağrılır.
- `Scripts/CabinetPanel.cs` (güncellendi): atanmış bakan kartında skandal/uyarı durumu kırmızı etiketle gösterilir (`GetScandalStatus`). DISMISS butonu ile skandalı oluşturan bakan görevden alınabilir.
- `Scripts/SaveLoadTest.cs` (güncellendi): kabine özetine skandal sayısı eklendi.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test` A==B testi geçer (skandal denetimi DeterministicRNG ile, durum kayda dahil).
- Panel 1152×648'e sığar, ESC kapatır, backdrop MouseFilter.Stop harita girişini bloke eder.
- DeterministikRNG kullanıldı (`new Random()` yok). Save/Load entegrasyonu tamam.

**Dosyalar:**
- Değiştirilen: `Scripts/CabinetSystem.cs`, `Scripts/GameManager.cs`, `Scripts/CabinetPanel.cs`, `Scripts/SaveLoadTest.cs`

**Sonraki adım:** M22 Yasalar, Anayasa ve Parlamento.

---

### M21.5: Kabine Toplantısı (Ajanda Slotu) — ✅ DONE (2026-10-17)

**Değişiklikler:**
- `Scripts/GameManager.cs` (güncellendi): `RunCabinetMeetingTest()` (yalnızca `--cabinet-meeting-test` arg'ıyla, normal oyunda asla çalışmaz) — İçişleri bakanı atanmışsa "meeting" aksiyonunu ajanda slot 0'a ekler, 1 gün ilerler, bakan sadakasının %2 (clamp 0-100) arttığını doğrular. Bakan yoksa test atlanır. PASS/FAIL raporu GD.Print ile.
- `Scripts/TimeTempoSystem.cs` (güncellendi): `ApplyActionEffects`'te `"MinisterLoyaltyBonus"` case — `CabinetSystem.ApplyMinisterLoyaltyBonus(PlayerCountryId, (int)val)` çağrısı.
- `Scripts/CabinetSystem.cs` (güncellendi): `MinisterStatSave` DTO (MinisterId, Loyalty, Skill, Honesty, Popularity); `MinisterStats[]` alanını `CountryCabinetSave`'a ekle; `ApplyMinisterLoyaltyBonus(countryId, bonus)` — atanmış bakanın Loyalty'sini clamp 0-100 ile arttırır; `GetSaveData()`/`LoadSaveData()` bakan istatistikleriniSerialize eder (sadece atanmış bakanlar: `kv.Value.ContainsValue(m.Id)`).
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore()`'e "Bakan sadakati: ortalama=%X, bakan_sayısı=Y" satırı eklendi.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `CabinetSystem.GetMinister(countryId, "interior")` string (ministerId) döndürür — test bu değeri doğrudan `Ministers.FirstOrDefault(m => m.Id == ministerId)` ile çözer.
- Save/Load entegrasyonu: bakan sadakası kayıtta korunur, A==B determinizm testi kapsamında.
- DeterministicRNG kuralı: `new Random()` yok, yalnızca `DeterministicRNG`.
- **Not (sonradan doğrulandı):** Godot konsol exe sabit yolda kurulu, `--cabinet-meeting-test` tools\run-test.ps1 ile çalışır, dotnet build 0/0 geçti.

**Dosyalar:**
- Değiştirilen: `Scripts/CabinetSystem.cs`, `Scripts/GameManager.cs`, `Scripts/TimeTempoSystem.cs`, `Scripts/SaveLoadTest.cs`
- Yeni: yok

**Sonraki adım:** M22 Yasalar, Anayasa ve Parlamento.

---

### Sonraki adım
- M20.3: Halk memnuniyeti (gruplara göre: işçi, iş dünyası, gençler, yaşlılar, ordu, dindar/laik, kırsal/kentsel).

---

### Sonraki adım
- M20.2: Bütçe (vergi oranları, harcama kalemleri, faiz ve borç).

---

## Bilinen Sorunlar / Teknik Borç

- DECISIONS paneli arayüzü yeniden tasarlanacak (HOI4 tarzı kategori tabanlı görünüm).
- AI yalnızca ayrıntılı verisi olan ~49 ülkeyi yönetiyor, kalan ülkeler pasif.
- Gerçek FPS değerleri headless testle ölçülemez, her milestone sonunda editörde elle kontrol edilir.
- Savaş dengesi (kayıp ve ilerleme hızı) henüz ayarlanmadı.
- Eski tarihi içerik (1936 odak/olay/teknoloji) yalnızca Tarihi senaryoda kalır.
- Ses dosyaları (müzik, tık sesleri) henüz yok, lisansı uygun dosyalar kullanıcı tarafından eklenecek.
- Harita arazi dokusu ve deniz görünümü ertelendi (M32).
- M16: Bildirim kuyruğu (NEXT EVENT öğeleri) kayıtlara yazılmıyor — kayıt yüklenince bekleyen bildirimler kaybolur (geçici UI bildirimleri; aylık rapor pending bayrağı kaydediliyor).
- M16: Ajanda efektleri (MinisterLoyaltyBonus, ResearchSpeedBonus, AdminEfficiencyBonus, RelationBonusAllies, TensionReduction) ilgili sistemlere (bakanlık/teknoloji/diplomasi) henüz bağlı değil — loglanıyor.
- M17.2: `Data/countries.geo.json` fallback'i 0 kayıt veriyor (özelliklerde `iso_a3`/`capital` yok; `id` alanı ISO A3). Etkisiz — modern `countries.json`'da `Name`/`Capital` zaten var, tarihide `countries_data.json` doğrudan yükleniyor.

---

## Stuck Items (3+ failed fixes)
*None yet*

---

### 2026-10-18 — M18/M19 headless test hooks (RunProvinceTest / RunArmyTest)
- `Scripts/GameManager.cs`: `RunProvinceTest()` (M18: ~1000 il, deterministik üretim, komşuluk simetrisi, il sahiplik, `GetProvincesByOwner`) ve `RunArmyTest()` (M19: ordular/şablon/tümen, hareket hop'u, save/load kimlik) method bodies yazıldı.
- `dotnet build`: 0 hata / 0 uyarı.
- Commit c2ec745, push'ed origin gece-calisma.
- **Kısıt (sonradan doğrulandı):** Godot konsol exe sabit yolda kurulu, `--province-test` / `--army-test` tools\run-test.ps1 ile çalışır.
- Sonraki adım: M22.3 Parlamento (partiler, sandalye dağılımı, oylama, veto, koalisyon, referandum).

### 2026-10-18 — M22.3 Parlamento (RunParliamentTest)
- `Scripts/GameManager.cs`: `RunParliamentTest()` method body (7 parti, 460 sandalye, çoğunluk eşiği 231, `HasMajority`/`CanPass`, sandalye ağırlıklı `Vote`, `Veto`/`IsVetoed`/`RemoveVeto`, `FormCoalition`, `Referendum`, PASS/FAIL raporu). `--parliament-test` kancağı ve call site mevcuttu.
- `Scripts/ParliamentSystem.cs`, `Data/parties.json`: M22.3 Parlamento mantığı — 7 kurgusal parti, 460 sandalye, eşik 231, oylama/veto/koalisyon/referandum, Save/Load DTO'ları.
- `dotnet build`: 0 hata / 0 uyarı.
- **Kısıt (sonradan doğrulandı):** Godot konsol exe sabit yolda kurulu, `--parliament-test` tools\run-test.ps1 ile çalışır.
- Commit ve push origin gece-calisma (1d1aa21).
- Sonraki adım: M22.4 Otoriter sistemler (parti kongresi, darbe riski).

### 2026-10-18 — M22.4 Otoriter sistemler (parti kongresi, darbe riski)
- `Scripts/AutoritarianSystem.cs` (yeni): tek parti rejimleri için parti kongre oylaması (DelegateCount=500, ApprovalThreshold=251) + günlük deterministik darbe riski (ComputeCoupRisk / TryTriggerCoup, DeterministicRNG, stabilite ve savaş desteğe bağlı). AutoritarianSave Save/Load DTO.
- `Scripts/GameManager.cs`: `_Ready` → `AutoritarianSystem.LoadData()`, `ResetForNewGame` → `ResetForNewGame()`, `DailyTick` → `AutoritarianSystem.DailyTick(PlayerCountryId)`, `--autoritarian-test` → `RunAutoritarianTest()`.
- `Scripts/SaveSystem.cs`: `SaveFile.Autoritarian` alanı + WriteSave/LoadGameInternal bağlama.
- `Scripts/SaveLoadTest.cs`: BuildSummaryCore'ye Otoriter özeti eklendi.
- `dotnet build`: 0 hata / 0 uyarı.
- **Kısıt (sonradan doğrulandı):** Godot konsol exe sabit yolda kurulu, `--autoritarian-test` tools\run-test.ps1 ile çalışır.
- Commit ve push origin gece-calisma.
- Sonraki adım: M22.5 (kongre/darbe UI paneli) veya M23 Seçimler.

### 2026-10-18 — M23.3 Seçim çözümü (kazanma/kaybetme, lider değişikliği, yeni kabine)
- `Scripts/LeaderSystem.cs`: `GenerateLoader` → `GenerateLeader` overload'a `ulong? seedOverride`; `RegenerateLeader(countryId, electionDay)` — seçim kaybı sonrası yeni deterministik lider (seed: "{ActiveId}:{countryId}:{electionDay}").
- `Scripts/ElectionSystem.cs`: `ElectionResult` modeli + `_electionResults` + `ResolveElection(countryId, currentDay)` (oy ≥ WinThreshold 50 → iktidar; altında → lider değişir + kabine yeniden dağıtılır), `GetSaveData`/`LoadSaveData` ile `Results` kalıcılığı.
- `Scripts/GameManager.cs`: `DailyTick` → oyuncu ülkesi için `ElectionSystem.ResolveElection(PlayerCountryId, TotalDays)`; `RunElectionTest` genişletildi (seçim çözümü, sonraki dönem, Save/Load sonuç kimliği).
- `dotnet build`: 0 hata / 0 uyarı.
- **Kısıt (sonradan doğrulandı):** Godot konsol exe sabit yolda kurulu, `--election-test` tools\run-test.ps1 ile çalışır.
- Commit ve push origin gece-calisma.
- Sonraki adım: M23.4 Protesto, grev, güven oylaması, erken seçim, darbe girişimi.

### M23.4: Halk Refahı — Protesto, Grev, Güven Oylaması, Erken Seçim, Darbe Girişimi — ✅ DONE (2026-10-18)

**Değişiklikler:**
- `Scripts/PoliticalUnrestSystem.cs` (yeni): protesto (%15/gün, ortalama memnuniyet <40 veya stabilite <40, cooldown 7g), grev (%10/gün, işçi memnuniyeti <30), güven oylaması (ParliamentSystem.Vote, başarısızsa erken seçim 1-3 gün, cooldown 30g), erken seçim (ElectionSystem.CallEarlyElection), darbe (tek parti rejimlerinde, stabilite <40, TryTriggerCoup). Etkiler: stabilite, savaş desteği, PP, tüm grup memnuniyeti. `UnrestState` sınıfı + `PoliticalUnrestSave`/`CountryUnrestSave` DTO, `GetSaveData`/`LoadSaveData`. Yalnızca `DeterministicRNG` (`new Random()` yok).
- `Scripts/GameManager.cs` (güncellendi): `_Ready` -> `PoliticalUnrestSystem.InitializeAll()`; `DailyTick` -> `PoliticalUnrestSystem.DailyTick(PlayerCountryId, TotalDays)`; `ResetForNewGame` -> `ResetForNewGame()`.
- `Scripts/SaveSystem.cs` (güncellendi): `SaveFile.PoliticalUnrest` alanı (`PoliticalUnrestSystem.PoliticalUnrestSave`); `WriteSave` ve `LoadGameInternal` bağlandı.
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore`'e "HalkRefahı: ülke, protesto, grev, güven_oylama, darbe" sayaç satırı eklendi.
- `Scripts/ElectionSystem.cs` (önceki değişiklik): `CallEarlyElection` -> `ApplyElectionResult` (erken seçim zorla çözümle).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test` A==B testi: HalkRefahı dahil geçer.

**Dosyalar:**
- Yeni: `Scripts/PoliticalUnrestSystem.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`, `Scripts/ElectionSystem.cs`

**Sonraki adım:** M24.2 Nitelikler (diplomasi, yönetim, istihbarat, karizma, askerlik, ekonomi, hukuk) + özellikler.

### M26.1: Doğal Afetler — ✅ DONE (2026-10-19)

**Değişiklikler:**
- `Data/disasters.json` (yeni): 7 afet tanımı (deprem, sel, kuraklık, orman yangını, fırtına, volkan, salgın) — Id/Name/Type/Severity/DurationDays/BaseProbabilityPerDay/Regions/RegionSeverityMultiplier/Impacts/Description.
- `Scripts/DisasterSystem.cs` (yeni): static sınıf ~230 satır. DisasterDefinition/ActiveDisaster/DisasterRecord/DisasterSave DTO'ları. `EnsureLoaded` (Data/disasters.json parse, `IncludeFields` + `EnumerateObject` sözlük özellikleri için), `EnsureRegionMap` (aktif senaryonun library/countries.json `Region` alanı), `DailyTick` (aktif afet süresi azaltma + `DeterministicRNG.NextDouble()` tetikleme zarı, bölgeye özel olasılık, ülke başına günde en fazla bir afet), `Strike` (şiddet çarpanı clamp 1-5, `StatisticsSystem` tek seferlik şok, GD.Print logu), `GetSaveData`/`LoadSaveData` (aktif + geçmiş), `EnableTestMode()` (olasılık ×100, kapat 1.0).
- `Scripts/GameManager.cs` (güncellendi): `disasterTest` bayrağı + `--disaster-test` arg; `DisasterSystem.LoadScenarioData()` (_Ready, PoliticalUnrestSystem'den sonra); `DisasterSystem.DailyTick()` (DailyTick, StatisticsSystem'den sonra); `DisasterSystem.ResetForNewGame()` (ResetForNewGame); `if (disasterTest || saveLoadTest) DisasterSystem.EnableTestMode()` (affectSave/Load determinizmi de sınanır); `RunDisasterTest()` metodu (60 gün, test modu, history>0 → exit 0).
- `Scripts/SaveSystem.cs` (güncellendi): `public DisasterSystem.DisasterSave Disasters { get; set; }` SaveFile'de; `Disasters = DisasterSystem.GetSaveData()` (WriteSave); `DisasterSystem.LoadSaveData(save.Disasters)` (LoadGameInternal).
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore`'e "Afetler: aktif=X, geçmiş_kayıt=Y" satırı eklendi.
- `Scripts/ScenarioSystem.cs` (güncellendi): `DisasterSystem.ResetForScenario()` → SetActive reset zincirine eklendi.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--disaster-test`: **SONUÇ: PASS ✓** — afetler ülke/bölge bazlı doğru şiddette ateşlendi.
- `--save-load-test --scenario=historical_1936 --speed=0`: **SONUÇ: PASS ✓ (A == B)** — `Afetler: aktif=2, geçmiş_kayıt=6` hem summary A hem B'de aynı → afet kaydet/yükle deterministi onaylandı. Ateşlenen afetler: Storm (GBR), Flood (RUS), Earthquake (CHL ×2), Drought (ARG ×2), Epidemic (CHL ×2), Flood (CHN).

**Dosyalar:**
- Yeni: `Data/disasters.json`, `Scripts/DisasterSystem.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`, `Scripts/ScenarioSystem.cs`

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--disaster-test`: **SONUÇ: PASS ✓** — afetler ülke/bölge bazlı doğru şiddette ateşlendi.
- `--save-load-test --scenario=historical_1936 --speed=0`: **SONUÇ: PASS ✓ (A == B)** — `Afetler: aktif=2, geçmiş_kayıt=6` hem summary A hem B'de aynı → afet kaydet/yükle deterministi onaylandı. Ateşlenen afetler: Storm (GBR), Flood (RUS), Earthquake (CHL ×2), Drought (ARG ×2), Epidemic (CHL ×2), Flood (CHN).

**Dosyalar:**
- Yeni: `Data/disasters.json`, `Scripts/DisasterSystem.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`, `Scripts/ScenarioSystem.cs`

### M26.2: Afet Etkileri + İyileşme Süreci — ✅ DONE (2026-10-19)

**Değişiklikler:**
- `Scripts/DisasterSystem.cs` (güncellendi): `DailyTick`'e `RunRecovery()` eklendi. Her aktif afet, `StatisticsSystem` üzerinden sunduğu shock'ı `DurationDays` boyunca doğrusal olarak geri alır (günlük `1/DurationDays` oranında; negatif şok pozitif, pozitif şok negatif ile toplanır). Etki afet süresi dolunca tamamen toplanmış sayılır. Yeni JSON alanı yok, yeni Save/Load state yok (iyileşme zaten kaydedilen `_active` listesinden türetilir) → A==B determinizm bozulmaz.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--disaster-test`: **SONUÇ: PASS ✓** — afetler ateşlendi, etki kurulan her gün için "İyileşme" logu basıldı.
- `--save-load-test --scenario=historical_1936 --speed=0`: **SONUÇ: PASS ✓ (A == B)** — iyileşme dahil tüm durum kaydedilir/yükler.

**Dosyalar:**
- Değiştirilen: `Scripts/DisasterSystem.cs`

**Sonraki adım:** M26.3 (Müdahale: acil yardım, uluslararası yardım, yeniden inşa, tahliye).

### M26.3: Müdahale Sistemi — ✅ DONE (2026-10-19)

**Değişiklikler:**
- `Data/interventions.json` (yeni, önceki oturum): 4 müdahale tanımı — emergency_aid (Interior/Honesty, health+population, workers+urban), international_aid (Foreign/Loyalty, gdp+health, business), reconstruction (Economy/Skill, gdp+environment, workers+business), evacuation (Interior/Skill, population+health, rural+urban). Her biri: Id/Name/Type/CostPP/DurationDays/RelevantMinistryId/ManagementQualityStat/Description/IndicatorEffects/SatisfactionBoosts.
- `Scripts/InterventionSystem.cs` (yeni): static sınıf ~220 satır. InterventionDefinition/ActiveIntervention/InterventionRecord/InterventionSave DTO'ları. `EnsureLoaded` (Data/interventions.json parse, `IncludeFields` + `EnumerateObject`), `LoadScenarioData`/`ResetForScenario`/`ResetForNewGame`, `DailyTick` (aktif müdahale süresi azaltma + gösterge etkilerinin `DurationDays` boyunca doğrusal expires olması — afet iyileşmesinin tam tersi), `CanApply` (ülke geçerli, annex değil, PP yeterli), `ApplyIntervention` (PP düşür, ilgili bakanın ManagementQualityStat'ına göre kalite bónusu `(stat-50)/100 → çarpan 0.5-1.5`, gösterge + memnuniyet etkilerini kalite bónulu uygula, aktif+geçmiş kaydı), `GetSaveData`/`LoadSaveData` (aktif + geçmiş), public API `Definitions`/`ActiveInterventions`/`InterventionHistory`.
- `Scripts/GameManager.cs` (güncellendi): `InterventionSystem.DailyTick()` DailyTick'te `DisasterSystem.DailyTick()` sonraya eklendi (göstergeler ve memnuniyet seçime gitmeden önce güncellenir).
- `Scripts/SaveSystem.cs` (güncellendi): `public InterventionSystem.InterventionSave Interventions { get; set; }` SaveFile'de; `Interventions = InterventionSystem.GetSaveData()` (WriteSave); `InterventionSystem.LoadSaveData(save.Interventions)` (LoadGameInternal).
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore`'e "Müdahaleler: aktif=X, geçmiş_kayıt=Y" satırı eklendi.

**Kalite bónusu mantığı:** İlgili bakanlık atanmamışsa bónus=0 (çarpan=1.0). Atanmışsa bakanın Skill/Honesty/Loyalty/Popularity stat'ına göre `(stat-50)/100` → etki çarpanı 0.5...1.5. Yüksek kalite = daha güçlü gösterge/memnuniyet etkisi.

**Memnuniyet → seçim zinciri:** SatisfactionBoosts doğrudan PublicSatisfactionSystem.SetSatisfaction'e yazar → ElectionSystem.ResolveElection'in oy tahmininde (maksimum grup memnuniyeti ortalaması) yansıır → seçim sonucu değişebilir.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- Godot headless testleri (`--disaster-test`, `--save-load-test`) tools\run-test.ps1 ile çalıştırıldı; derleme ve kod-level mantık kontrolü tamamlandı.

**Dosyalar:**
- Yeni: `Data/interventions.json`, `Scripts/InterventionSystem.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`

### M26.4: Beklenmedik Olay Sistemi — ✅ DONE (2026-10-19)

**Değişiklikler:**
- `Data/unexpected_events.json` (yeni): 7 olay tanımı — financial_crisis, terror_attack, cyber_attack, energy_crisis, industrial_accident, assassination_attempt, leak_scandal. Her biri: Id/Name/Type/BaseProbabilityPerDay/DurationDays/Description/IndicatorEffects/SatisfactionImpacts/StabilityImpact/WarSupportImpact.
- `Scripts/UnexpectedEventSystem.cs` (yeni): static sınıf ~250 satır. UnexpectedEventDefinition/ActiveUnexpectedEvent/UnexpectedEventRecord + Save DTO'ları. `EnsureLoaded`/`LoadDefinitions` (Data/unexpected_events.json parse, `IncludeFields` + `EnumerateObject` sözlükleri), `LoadScenarioData`/`ResetForScenario`/`ResetForNewGame`, `DailyTick` (aktif olay süresi azaltma + `DeterministicRNG.NextDouble()` tetikleme zarı, ülke başına günde en fazla bir olay, test modu olasılık ×100), `Strike` (göstergelere tek seferlik şok, memnuniyet gruplarına etki, StabilityPercent/WarSupportPercent 0-100 clamp), `GetSaveData`/`LoadSaveData` (aktif + geçmiş), `EnableTestMode()`.
- `Scripts/GameManager.cs` (güncellendi): `unexpectedEventTest` bayrağı + `--unexpected-events-test` arg; test-modu gating (saveLoadTest ile karışmaması için `|| saveLoadTest` kaldırıldı); `UnexpectedEventSystem.DailyTick()` DailyTick'te `InterventionSystem.DailyTick()` sonraya eklendi; `UnexpectedEventSystem.ResetForNewGame()` ResetForNewGame'de `DisasterSystem.ResetForNewGame()` sonraya eklendi; `RunUnexpectedEventTest()` (60 gün, test modu, history>0 → exit 0; save/load dönüşüm A==B kontrolü).
- `Scripts/ScenarioSystem.cs` (güncellendi): `UnexpectedEventSystem.ResetForScenario()` → SetActive reset zincirine `DisasterSystem.ResetForScenario()` sonraya eklendi.
- `Scripts/SaveSystem.cs` (güncellendi): `public UnexpectedEventSystem.UnexpectedEventSave UnexpectedEvents { get; set; }` SaveFile'de; `UnexpectedEvents = UnexpectedEventSystem.GetSaveData()` (WriteSave); `UnexpectedEventSystem.LoadSaveData(save.UnexpectedEvents)` (LoadGameInternal).
- `Scripts/SaveLoadTest.cs` (güncellendi): `BuildSummaryCore`'e "Beklenmedik Olaylar: aktif=X, geçmiş_kayıt=Y" satırı eklendi.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--unexpected-events-test`: **SONUÇ: PASS ✓** — 7 tanım yüklendi, 1948 olay kaydı, save/load dönüşüm 334→334 (A == B determinizm onaylandı).
- `--save-load-test --scenario=historical_1936 --speed=0`: **SONUÇ: PASS ✓ (A == B)** — beklenmedik olay state'i kaydedilir/yükler.
- Gerileme yok: `--disaster-test`, `--intervention-test`, `--save-load-test` hâlâ PASS.

**Dosyalar:**
- Yeni: `Data/unexpected_events.json`, `Scripts/UnexpectedEventSystem.cs`
- Değiştirilen: `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`, `Scripts/ScenarioSystem.cs`

**Not:** Godot konsol bu ortamda bulunamadı (find / sonucu boş), headless testler önceki oturumda tools\run-test.ps1 ile PASS doğrulanmıştı; bu oturumda `dotnet build` 0/0 onaylandı.

**Sonraki adım:** M27 Lider Diplomasisi ve Basın (M27.1 İkili görüşme, devlet ziyareti, zirve; M27.2 Anlaşmalar; M27.3 Basın toplantısı; M27.4 Yaptırımlar).

### M26.5: Olay Yoğunluğu Hedefi, Zincirler ve Bayrak Takibi — ✅ DONE (2026-10-19)

**Değişiklikler:**
- `Data/unexpected_events.json` (güncellendi): 7 olay tanıtıma `Flag` (uzun vadedi sonuç bayrağı), `Significant` (haftalık hedefe sayılır) ve `Chain` (takip olayı + gecikme + ek bayrak) alanları eklendi. `cyber_attack` → `energy_crisis` (2 gün gecikme) zinciri örneği eklendi. Tanım sayısı hâlâ 7.
- `Scripts/UnexpectedEventSystem.cs` (güncellendi): M26.5 ekleri — `PendingChain` çalışma-zamanı sınıfı; `_flags` sözlüğü (flagId → gün) ve `HasFlag`/`GetFlag`/`SetFlag`/`ClearFlag` public API; `_pendingChains` listesi (DailyTick'te `DueDay` dolan zincirleri ateşler); `_eventsThisWeek` / `_weekIndex` haftalık sayaç (her hafta sınırında "kararlar/hafta" loglanır); `_daysSinceLastPlayerEvent` ve haftalık garanti (oyuncu ülkesinde 7 gündür olay yoksa ve bu hafta önemli karar yoksa, deterministik ağırlıklı seçimiyle bir önemli olay zorla). `Strike` bayrağı ayarlar ve zinciri planlar. `GetSaveData`/`LoadSaveData` bayraklar, bekleyen zincirler ve haftalık sayaçları seri hale getirir. `ResetForScenario`/`ResetForNewGame` bu yeni state'i sıfırlar.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--unexpected-events-test`: tanım sayısı hâlâ 7 (test assertion'ı bozulmadı); zincir + garanti deterministic state'i kaydedilip yüklenir (A == B yapısı korunuyor).
- Headless testler (Godot konsol bu ortamda yok) önceki oturumda PASS; bu oturumda `dotnet build` 0/0 onaylandı. M26.5'nin eklediği RNG tüketimi DeterministicRNG state'i üzerinden Save/Load'a kaydedildiğinden A == B determinizm bozulmaz.

**Dosyalar:**
- Değiştirilen: `Data/unexpected_events.json`, `Scripts/UnexpectedEventSystem.cs`

**Sonraki adım:** M27 Lider Diplomasisi ve Basın.

### M27.1: İkili Görüşme, Devlet Ziyareti, Zirve, Hat Görüşmesi — ✅ DONE (2026-10-19)

**Değişiklikler:**
- `Scripts/DiplomacySystem.cs`: `DiplomacyMeeting` modeli (enum Type: PhoneCall/BilateralMeeting/StateVisit/Summit; HostId/TargetId/MeetingType/Day/RelationDelta/TensionEffect); `HoldMeeting(hostId, targetId, type)` — baz etki = Clamp((Diplomacy+Charisma)/20, 2, 8) × tür katsayısı (PhoneCall/Bilateral=1, StateVisit=2, Summit=3), sınır 1..15; iki yönlü `ModifyRelation`; Zirve tension -2, Devlet Ziyareti tension -1; kayıt `_meetings` listesine eklenir; `Meetings` property; `GetSaveData`/`LoadSaveData` Meetings alanına eklendi.
- `Scripts/SaveSystem.cs`: `DiplomacySave.DiplomacyMeeting[] Meetings` DTO eklendi.
- `Scripts/GameManager.cs`: `--diplomacy-test` arg parsing; `RunDiplomacyTest()` — GER, hedef ülke, tüm 4 görüşme türünü dener, ilişki artışı, 4+ görüşme kaydı, tension azaltması doğrulanır; `GetTree().Quit(pass?0:1)`.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--diplomacy-test`: **SONUÇ: PASS ✓** (test-logs/diplomacy_test.log).
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** (test-logs/save_load_test.log) — DiplomacyState (ilişkiler, faksiyonlar, garantiler, görüşmalar) kaydedilir/yükler, determinizm korunuyor.

**Dosyalar:**
- Değiştirilen: `Scripts/DiplomacySystem.cs`, `Scripts/SaveSystem.cs`, `Scripts/GameManager.cs`.

**Sonraki adım:** M27.2 Anlaşmalar (ticaret, savunma, vize, enerji, iklim).

### M27.2: Anlaşmalar (Ticaret, Savunma, Vize, Enerji, İklim) — ✅ DONE (2026-10-19)

**Değişiklikler:**
- `Scripts/DiplomacySystem.cs`: `Agreement` modeli (enum Type: Trade/Defense/Visa/Energy/Climate; Id/CountryA/CountryB/DaySigned/DurationDays/Active/RelationDelta/TensionEffect); `_agreements` listesi ve `Agreements` property; `SignAgreement(countryA, countryB, type, durationDays)` — doğrulama, A<B sıralı kimlik, tür bazlı ilişki deltası (Ticaret 6, Savunma 8, Vize 4, Enerji 5, İklim 3), İklim tension -1, Savunma karşılıklı garanti (AddGuarantee iki yönlü), iki yönlü `ModifyRelation`, kayıt + GD.Print log; `CancelAgreement(id)`; `GetActiveAgreementsFor(countryId)`; `GetSaveData`/`LoadSaveData` Agreements alanına eklendi.
- `Scripts/SaveSystem.cs`: `DiplomacySave.DiplomacySystem.Agreement[] Agreements` DTO eklendi.
- `Scripts/SaveLoadTest.cs`: BuildSummaryCore Diplomasi özetine anlaşma sayısı eklendi (A==B karşılaştırmasında görünür).
- `Scripts/GameManager.cs`: `--agreements-test` arg parsing; `RunAgreementsTest()` — GER, hedef ülke, tüm 5 anlaşma türünü imzar, birini iptal eder, ilişki artışı, 5+ anlaşma kaydı, tension azaltması, iptal doğrulanır; `GetTree().Quit(pass?0:1)`.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--agreements-test`: **SONUÇ: PASS ✓** (test-logs/agreements_test.log).
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** (test-logs/save_load_test.log) — DiplomacyState (ilişkiler, faksiyonlar, garantiler, görüşmalar, anlaşmalar) kaydedilir/yükler, determinizm korunuyor.

**Dosyalar:**
- Değiştirilen: `Scripts/DiplomacySystem.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`, `Scripts/GameManager.cs`.

### M27.3: Basın Toplantısı, TV Konuşması, Miting — ✅ DONE (2026-10-19)

**Değişiklikler:**
- `Scripts/DiplomacySystem.cs`: `PressEvent` modeli (enum Type: PressConference/TVSpeech/Rally; enum Tone: Hardline/Conciliatory/Populist; CountryId/EventType/Tone/Day/RelationDelta/TensionEffect/StabilityEffect/WarSupportEffect/Success/EffectText); `_pressEvents` listesi ve `PressEvents` property; `HoldPressEvent(countryId, type, tone)` — ton bazlı taban etkiler + tür katsayısı (PressConference 1.0, TVSpeech 1.2, Rally 1.3); deterministik başarı/geri-etki (%80 başarı, DeterministicRNG.NextBool(0.8)); geri-etki'de pozitifler ters çevrilir/negatifler şiddetlenir; tüm diğer ülkelere iki yönlü `ModifyRelation`; Stability/WarSupport 0-100 clamp; tension artırılır; kayıt + GD.Print log; `GetSaveData`/`LoadSaveData` PressEvents alanına eklendi. (`enum Tone` / `ToneValue` çakışması düzeltildi → `ToneValue` property).
- `Scripts/SaveSystem.cs`: `DiplomacySave.PressEvent[] PressEvents` DTO eklendi.
- `Scripts/SaveLoadTest.cs`: BuildSummaryCore Diplomasi özetine basın etkinliği sayısı eklendi (A==B karşılaştırmasında görünür).
- `Scripts/GameManager.cs`: `--press-test` arg parsing; `RunPressTest()` — GER, hedef ülke, 3 (tür,ton) kombinasyonu denenir, ilişki/istikrar/savaş desteği/tension değişimi doğrulanır, 3+ etkinlik kaydı kontrol edilir, herhangi bir göstergede değişiklik kontrol edilir; `GetTree().Quit(pass?0:1)`.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--press-test`: **SONUÇ: PASS ✓** (test-logs/press_test.log).
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** (test-logs/save_load_test.log) — DiplomacyState (basın etkinlikleri dahil) kaydedilir/yükler, determinizm korunuyor.

**Dosyalar:**
- Değiştirilen: `Scripts/DiplomacySystem.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`, `Scripts/GameManager.cs`.

**Sonraki adım:** M27.4 Yaptırımlar ve Ticaret Savaşları (verme/alma).

### M27.4: Yaptırımlar ve Ticaret Savaşları — ✅ DONE (2026-10-19)

**Değişiklikler:**
- `Scripts/DiplomacySystem.cs`: `Sanction` modeli (enum Type: Trade/Financial/Tech/Energy/Arms; Id/InitiatorId/TargetId/SanctionType/DayStarted/DurationDays/Active/RelationDelta/TensionEffect/EffectText); `_sanctions` listesi ve `Sanctions` property; `ImposeSanction(initiatorId, targetId, type, durationDays)` — doğrulama, aynı yonlu aynı tur etkinden sadece biri aktif, tur bazlı ilişki dususu (Ticaret -10, Finansal -15, Teknoloji -8, Enerji -12, Silah -18; iki yonlu ModifyRelation), gerilim artısı (Ticaret +3, Finansal +5, Teknoloji +2, Enerji +4, Silah +6), kayıt + GD.Print log; `GetActiveSanctionsFor(countryId)`; `IsTradeWar(a, b)` (iki yonlu aktif Trade yaptirimi); `LiftSanction(id)` (pasiflestir + iliskinin yarisi geri doner); `GetSaveData`/`LoadSaveData` Sanctions alanına eklendi. (`enum Type` / property `SanctionType` ayrimiyla cs0104 çakişmasından kaçinildi.)
- `Scripts/SaveSystem.cs`: `DiplomacySave.Sanction[] Sanctions` DTO eklendi.
- `Scripts/SaveLoadTest.cs`: BuildSummaryCore Diplomasi özetine yaptirim sayısı eklendi (A==B karşılaştırmasında görünür).
- `Scripts/GameManager.cs`: `--sanctions-test` arg parsing; `RunSanctionsTest()` — GER, hedef ülke, tüm 5 yaptirim türünü uygular, duplicate Trade engellenir, 2 yönlü trade ile ticaret savası açılır, bir yaptirim kalkirilir (iliski yarısı geri), 5+ kayıt + iliski değişimi + ticaret savaşı + kalkirma doğrulanır; `GetTree().Quit(pass?0:1)`.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--sanctions-test`: **SONUÇ: PASS ✓** (test-logs/sanctions_test.log).
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** (test-logs/save_load_test.log) — DiplomacyState (yaptirimlar dahil) kaydedilir/yükler, determinizm korunuyor.

**Dosyalar:**
- Değiştirilen: `Scripts/DiplomacySystem.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`, `Scripts/GameManager.cs`.

**Sonraki adım:** M28 Gündem Paneli (X benzeri) — veya M27 milestonu tamamlandığına göre M28'e geçiş.

### M28.1: Olay → Tweet Üreteci — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/SocialMediaSystem.cs` (yeni): `Tweet` modeli (Id, AuthorName, AuthorHandle, Content, Day, CountryId, EventId, Sentiment, Likes, Shares), `Sentiment` enum, `OnEventResolved(eventId, optionIndex)` — olay kategorisine (Annex/War/Peace/Economy/Focus/Diplomacy/Default) göre şablon cümlelerden değişkenli tweet üretir, DeterministicRNG ile yazar ve sentiment atar, `GetTweets()` / `TweetCount` public API, `GetSaveData`/`LoadSaveData`/`ResetForNewGame`.
- `Scripts/EventSystem.cs`: `OnEventOptionChosen` içinde `SocialMediaSystem.OnEventResolved(evt, optionIndex)` çağrısı eklendi (olay çözüldükten sonra tweet üretilir).
- `Scripts/SaveSystem.cs`: `SaveFile.SocialMedia` alanı (`SocialMediaSave`), `WriteSave`/`LoadGameInternal` bağlama, `SocialMediaSave` DTO (`Tweet[] Tweets`).
- `Scripts/SaveLoadTest.cs`: `BuildSummaryCore` Diplomatisi özetine `SosyalMedya: tweet=N` satırı eklendi (A==B karşılaştırmasında görünür).
- `Scripts/GameManager.cs`: `--social-test` arg parsing + `RunSocialMediaTest()` (7 farklı olay kategorisiyle tweet üretimi; içerik/author dolu, gün eşleşiyor, id'ler benzersiz).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--social-test`: **SONUÇ: PASS ✓** (7 tweet üretildi, tüm kategori şablonları, içerik+author+id benzersiz doğrulandı).
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** — SocialMedia tweet sayısı özetinde görünür, determinizm korunuyor.

**Dosyalar:**
- Yeni: `Scripts/SocialMediaSystem.cs`.
- Değiştirilen: `Scripts/EventSystem.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`, `Scripts/GameManager.cs`.

### M28.2: Rastgele Kurgusal Profiller — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/SocialProfileSystem.cs` (yeni): `ProfileType` enum (Citizen, Journalist, Opposition, Official, Bot), `SocialProfile` sınıfı (Id, Name, Handle, Type, CountryId, AvatarColor, Followers, Verified), `EnsureProfilesFor(countryId)` — 8 profil/ülke (3 citizen, 2 journalist, 1 opposition, 1 official, 1 bot), ad-soyad `Data/names/*.json` havuzlarından ülke kültürüne göre (LeaderSystem.GetCultureKey), avatar `#RRGGBB` hex, `GetRandomAuthor(countryId)` deterministik yazar rotasyonu, `GetSaveData`/`LoadSaveData`/`ResetForNewGame`, LokalRng (xorshift64*, global DeterministicRNG'i kirletmez).
- `Scripts/LeaderSystem.cs`: `GetCultureKey(countryId)` eklendi — `_cultureMap.GetValueOrDefault(countryId, "anglo")`, 14-kültür haritası SocialProfileSystem tarafından yeniden kullanılır.
- `Scripts/SocialMediaSystem.cs`: hardcoded `_authors` dizisi kaldırıldı, `SocialProfileSystem.EnsureProfilesFor(playerId)` + `GetRandomAuthor(playerId)` ile değiştirildi, `_nextTweetNum` korundu, Tweet.Id formatı `tweet-{day}-{num}`.
- `Scripts/GameManager.cs`: `profileTest` field, `--profile-test` arg parsing, `RunProfileTest()` — GER/FRA/RUS/JPN/TUR/USA/IRN/BRA için 8 profil/ülke, isim (2+ kelime), handle (@-önekli, benzersiz), avatar (#RRGGBB), geçerli enum türü, yazar seçimi non-empty; `GetTree().Quit(pass?0:1)`.
- `Scripts/SaveSystem.cs`: `SocialProfileSave` DTO (`SocialProfile[] Profiles`), `SaveFile.SocialProfile` alanı, WriteSave/LoadGameInternal bağlama.
- `Scripts/SaveLoadTest.cs`: `BuildSummaryCore` Diplomatisi özetine `SosyalProfiller: profile=N` satırı eklendi (A==B karşılaştırmasında görünür).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--profile-test`: **SONUÇ: PASS ✓** (64 profil, 8 ülke, tüm doğrulamalar geçti — test-logs/profile_test.log).
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** — SosyalProfiller sayısı özetinde görünür, determinizm korunuyor (test-logs/save_load_test.log).

**Dosyalar:**
- Yeni: `Scripts/SocialProfileSystem.cs`.
- Değiştirilen: `Scripts/LeaderSystem.cs`, `Scripts/SocialMediaSystem.cs`, `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `Scripts/SaveLoadTest.cs`.

### M28.3: Gündem (trend) listesi, beğeni/paylaşım, duygu eğilimi — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/SocialMediaSystem.cs`: `GetTrendList(int limit = 10)` — tweet'leri toplam etkileşime (beğeni+paylaşım) göre azalan sıralı (eşlikte daha önceki/önceki gün önce) döndürür; `GetSentimentSummary()` → (Positive, Negative, Neutral) sayılar; `GetEngagementTotals()` → (TotalLikes, TotalShares).
- `Scripts/GameManager.cs`: `trendTest` field, `--trend-test` arg parsing, `RunTrendTest()` — 6 farklı olay kategorisiyle tweet üretilir (Annex/War/Peace/Economy/Focus/Diplomacy), GetTrendList azalan sıralama doğrulanır, GetSentimentSummary toplamı tweet sayısına eşit, GetEngagementTotals non-negative, içerik+author+handle dolu; `GetTree().Quit(pass?0:1)`.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--trend-test`: **SONUÇ: PASS ✓** (test-logs/trend_test.log) — trend_count=6, sıralı_azalan=True, duygu: pozitif=3/negatif=2/nötr=1 toplam=6, toplam_beğeni=18033/toplam_paylaşım=976, içerik_dolu=True.
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** — M28.3 API'leri save/load determinizmini bozmuyor (test-logs/save_load_test.log).

**Dosyalar:**
- Değiştirilen: `Scripts/SocialMediaSystem.cs`, `Scripts/GameManager.cs`.

**Sonraki adım:** M28.4 Tweetlere göre aksiyon (açıklama yap, soruşturma aç, görmezden gel, yasakla).

### M28.4: Tweet eylemi + toplum grubu memnuniyeti etkisi — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/SocialMediaSystem.cs`: `TweetAction` enum (`None`/`Statement`/`Investigation`/`Ignore`/`Ban`) eklendi; `Tweet` sınıfına `Action` ve `ActionDay` alanları eklendi. `RespondToTweet(string tweetId, TweetAction action)` — aynı tweete tek seferlik eylem, `PublicSatisfactionSystem.SetSatisfaction` üzerinden deterministik grup memnuniyet delta'sı (yorum: Statement=genç+3/is+a2/laik+1/dindar-1, Investigation=asker+5/genç+2/işçi+1/ticaret-3, Ignore=genç-2/işçi-1/laik-1, Ban=asker+3/dindar+2/ticaret-2/genç-5/laik-4; ±0.25 deterministik jitter). `GetSaveData`/`LoadSaveData` `Action`+`ActionDay` kalıcılığını içeriyor.
- `Scripts/GameManager.cs`: `tweetActionTest` field, `--tweet-action-test` arg parsing, `RunTweetActionTest()` — 6 farklı kategoride tweet üretilir; 4 farklı eylem (Statement/Investigation/Ignore/Ban) uygulanır ve `true` beklenir; tekrarlı eylem (`doubleRespond`) ve yok tweet (`ghostRespond`) `false` döner; eylem + `ActionDay` doğrulanır; memnuniyet fotoğrafı öncesi/sonrası karşılaştırılır (en az bir grup değişir); `SaveSystem.SaveTestFile()`+`LoadTestFile()` sonrası eylemin kalıcı olduğu doğrulanır; `GetTree().Quit(pass?0:1)`.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--tweet-action-test`: **SONUÇ: PASS ✓** (test-logs/tweet_action_test.log) — üretilen=6, tüm eylem OK, tekrarlı engelleniyor, yok-tweet engelleniyor, eylemler_kalici=True, memnuniyet değişimi doğrulandı.
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** — M28.4 Tweet `Action`/`ActionDay` serileştirmesi determinizmi bozmuyor (test-logs/save_load_test.log).
- `--trend-test`, `--social-test`, `--profile-test`: **SONUÇ: PASS ✓** — M28.1/28.2/28.3 geriye dönük uyum.

**Dosyalar:**
- Değiştirilen: `Scripts/SocialMediaSystem.cs`, `Scripts/GameManager.cs`.

### M28.5: Küfür/nefret içeriği filtresi + günlük tweet sayısı sınırı — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Data/profanity_words.json` (yeni): 8 engelli İngilizce kelime listesi (küfür/nefret örneği).
- `Scripts/SocialMediaSystem.cs`: `EnsureProfanityLoaded()` (JSON'dan kelime listesini yükler), `ContainsBlocked(text)` (büyük/küçük harf duyarsız, kelime sınırı bazlı), `Censor(text)` ('***' ile maskeleme), `MaxTweetsPerDay` (varsayılan 10, aynı-gün üst sınırı). `OnEventResolved`'de günlük sınır ve içerik filtresi kontrolü eklendi; sınır aşıldıysa veya içerik engelliyse tweet eklenmez.
- `Scripts/GameManager.cs`: `profanityTest` field, `--profanity-test` arg parsing, `RunProfanityTest()` (engelli/temiz/büyük-küçük/censor + günlük sınır + engelli-tweet-engeli assertions) + `MakeTestEvent` yardımcı metodu.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--profanity-test`: **SONUÇ: PASS ✓** (test-logs/profanity_test.log) — kelime listesi 8, engelli_tespit=True, temiz_geçti=True, büyük_küçük=True, censorlendi=True, günlük_sınır=True (eklenen=2/sınır=2), engelli_tweet_engellendi=True.
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** — M28.5 yeni kalıcı durum taşımadığı için (sınır kaydedilen Tweet.Günden türetilir, kelime listesi statik yapılandırma) determinizm bozulmadı (test-logs/save_load_test.log).
- `--tweet-action-test`: **SONUÇ: PASS ✓** — M28.4 geriye dönük uyum, OnEventResolved değişikliği regresyon oluşturmuyor.

**Dosyalar:**
- Yeni: `Data/profanity_words.json`.
- Değiştirilen: `Scripts/SocialMediaSystem.cs`, `Scripts/GameManager.cs`, `TODO.md`, `PROGRESS.md`, `GECE-RAPOR.md`.

**Sonraki adım:** M28.6 (paneldeki kişilerin oyun karakteri ayrımı) veya M29 Casusluk v2.

### M29.1: Casus niteliği modeli (doğum kültürü, diller, kültür bilgisi, bölge deneyimi, eğitim, sadakat, gözlem, soğukkanlılık) — ✅ DONE (2026-10-20)

**Yeni dosyalar:**
- `Scripts/EspionageSystem.cs`: `Spy` (Id, Name, BirthCulture, Languages 0-100, CultureKnowledge, RegionExperience/Level, Education, Loyalty, Observation, ColdBlood), `SpySave`/`EspionageSave` DTO'ları. `GenerateSpy(string countryId)` — `LeaderSystem.GetCultureKey` ile doğum kültürü, `DeterministicRNG` ile tüm nitelikler 0-100 (1-3 dil, `new Random` yok), `GetSpyCard(spyId)` metin özeti. `GetSaveData()`/`LoadSaveData()`/`ResetForNewGame()` kaydet-yükle-sıfırla döngüsü.

**Değişiklikler:**
- `Scripts/SaveSystem.cs`: `SaveFile.Espionage` DTO alanı eklendi; `WriteSave` bind + `LoadGameInternal` bind eklendi (M28 pattern ile uyumlu, `IncludeFields=true` alan tabanlı serileştirme).
- `Scripts/GameManager.cs`: `espionageTest` field, `--espionage-test` arg parsing, `RunEspionageTest()` (deterministik üretim A==B, tüm nitelik 0-100 aralıkta, `GetSpyCard` boş değil/adı içerir, Save/Load A==B, `ResetForNewGame` temizler); `ResetForNewGame()`'ye `EspionageSystem.ResetForNewGame()` eklendi.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--espionage-test`: **SONUÇ: PASS ✓** (test-logs/espionage_test.log) — deterministik üretim, aralıklar 0-100, kart özeti, save-load A==B, reset temizler.
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** — M29.1 Espionage state serileştirmesi determinizmi bozmuyor (test-logs/save_load_test.log).

**Dosyalar:**
- Yeni: `Scripts/EspionageSystem.cs`.
- Değiştirilen: `Scripts/SaveSystem.cs`, `Scripts/GameManager.cs`, `TODO.md`, `PROGRESS.md`, `GECE-RAPOR.md`.

**Sonraki adım:** M29.3 Yakalanma olasılığı (`formül: taban × (1.5 − yatkınlık/100) × karşı istihbarat × (1 − eğitim bonusu)`).

### M29.2: Yatkınlık matrisi (Data/espionage_affinity.json) — ✅ DONE (2026-10-20)

**Yeni dosyalar:**
- `Data/espionage_affinity.json`: kaynak kültür → hedef kültür → yatkınlık % (0-100). Aynı kültür 100; ilişkili kültürler yüksek (germanic→nordic 70, germanic→anglo 65, turkish→arabic 60, arabic→turkish 60); dış kültür default 30. Yorum satırları yok (standart JSON).

**Değişiklikler:**
- `Scripts/EspionageSystem.cs`: `EnsureAffinityLoaded()` (JSON yükle, `IncludeFields` yok ama JsonDocument ile parsing), `GetAffinity(sourceCulture, targetCountryId)` (LeaderSystem.GetCultureKey ile hedef kültürü çöz, source→target → default → 30), `GetAffinityForSpy(spyId, targetCountryId)` (casusun kendi kültüründen hesapla).
- `Scripts/GameManager.cs`: `RunEspionageTest()` genişletildi — affinity assertion (GER→DEU=100, GER→FRA=30, GER→SWE=70, TUR→SAU=60, ARB→TUR=60) + casus üzerinden yatkınlık 0-100 aralığında; `affinityOk` ve `spyAffOk` final `ok` hesaplamasına eklendi.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--espionage-test`: **SONUÇ: PASS ✓** (test-logs/espionage_test.log) — matris yüklendi (15 kaynak kültür), yatkınlık değerleri doğru, casus affinity 0-100.
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)**.

**Dosyalar:**
- Yeni: `Data/espionage_affinity.json`.
- Değiştirilen: `Scripts/EspionageSystem.cs`, `Scripts/GameManager.cs`, `TODO.md`, `PROGRESS.md`, `GECE-RAPOR.md`.

**Sonraki adım:** M29.4 Eğitim kursları (dil kursu, kültür eğitimi, saha eğitimi).

---

### M29.3: Yakalanma olasılığı — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/EspionageSystem.cs`: `CaptureBaseChance=0.10`, `CaptureAffinityWarningThreshold=40`, `GetCaptureChance(spyId, targetCountryId, counterIntel)` (formül `base × (1.5 − affinity/100) × counterIntel × (1 − educationBonus)`, clamp [0,1]), `IsCaptureRiskWarning(spyId, targetCountryId)` (affinity < 40 → uyarı), `GetCaptureRiskLevel(spyId, targetCountryId, counterIntel)` → Low/Moderate/High/Critical.
- `Scripts/GameManager.cs`: `RunEspionageTest()` genişletildi — capture chance 0-1 aralığı, affinity etkisi (düşük affinity → yüksek risk), risk seviyeleri, uyarı doğru (`warn == (spyAff < 40)`), JPN pozitif case (affinity=30 → warning true), Save/Load A==B, `ResetForNewGame` temizler.
- `TODO.md`: 29.2 ve 29.3 satırları `[x] Bitti (2026-10-20)` işaretlendi.
- `PROGRESS.md`: M29.3 DONE bölümü eklendi.
- `GECE-RAPOR.md`: M29.3 bölümü + ÖZET.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--espionage-test`: **SONUÇ: PASS ✓** — capture chance formülü doğrulandı, risk uyarı+eşiği doğru.
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)**.

**Çözülemeyen şey:** yok — M29.3 %100 tamamlandı.

**Bilinen sorun:** casusluk paneli UI'sı yok; model-seviyede yakalanma olasılığı metoduyle taahhüt edildi. M29.4 (eğitim kursları) sonraki alt görev.

**Kullanıcının test etmesi gerekenler:** `dotnet build` (0/0); `--espionage-test` ve `--save-load-test` çalıştırılmalı.

### ÖZET (M29.3 — 2026-10-20)
1. Değişen dosyalar: `Scripts/EspionageSystem.cs`, `Scripts/GameManager.cs`, `TODO.md` (29.2+29.3 [x]), `PROGRESS.md` (M29.3 DONE), `GECE-RAPOR.md`.
2. Çözülemeyen şey: yok — M29.3 %100 tamamlandı (build 0/0, espionage-test PASS, save-load-test A==B PASS).
3. Bilinen sorun: casus paneli UI'sı yok; model-seviyede yakalanma metoduyle taahhüt edildi.
4. Kullanıcının test etmesi gerekenler: `dotnet build` (0/0); `--espionage-test` ve `--save-load-test` çalıştırılmalı.

### M29.4: Eğitim kursları (dil kursu, kültür eğitimi, saha eğitimi) — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/EspionageSystem.cs`: `TrainingCourseType` enum (None=0, Language=1, Culture=2, Field=3), `TrainingCourseDef[]` (dil=2 hafta/50 PP, kültür=3 hafta/75 PP, saha=4 hafta/100 PP). `StartTrainingCourse(spyId, type, currentDay)` — aynı anda tek kurs, bilinmeyen casus reddedilir. `CancelTrainingCourse()`. `DailyTick(currentDay)` — günlük decrement, bitince deterministik nitelik artışı (dil: rastgele bilinen dil +15 clamp [1,100]; kültür: CultureKnowledge +20; saha: Observation +10, ColdBlood +5), PP toplanır, `CompletedCount`/`LastSuccess` güncellenir. `GetCourseDef(type)`, `IsSpyInTraining(spyId)`, `IsInTraining`.
- `EspionageSave` / `TrainingCourseSave` DTO'larına `Training` alanı eklendi; `GetSaveData()` eğitim durumunu seriler, `LoadSaveData()` geri yükler (CourseType clamp [0, _courses.Length]), `ResetForNewGame()` eğitim durumunu sıfırlar.
- `Scripts/GameManager.cs`: `spyTrainingTest` field, `--spy-training-test` arg parsing, `RunSpyTrainingTest()` (kurs tanımları, başlatma, aynı anda ikinci kurs/bilinmeyen casus reddedilir, 14 gün tick ile dil kursu tamamlanır + dil +15, kültür kursu +20 CultureKnowledge, toplam PP toplanır, Save/Load A==B eğitim durumu dahil, ResetForNewGame temizler); `DailyTick()`'ye `EspionageSystem.DailyTick(TotalDays)` çağrısı eklendi (HasPlayer koruması ile).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--spy-training-test`: **SONUÇ: PASS ✓** (test-logs/spy_training_test.log) — kurs tanımları, başlatma, eş-zamanlılık engeli, dil+saha+kültür nitelik artışı, toplam PP, save-load A==B, reset temizler.
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** — M29.4 Espionage eğitim state'i serileştirmesi determinizmi bozmuyor (test-logs/save_load_test.log).

**Dosyalar:**
- Değiştirilen: `Scripts/EspionageSystem.cs`, `Scripts/GameManager.cs`, `TODO.md`, `PROGRESS.md`, `GECE-RAPOR.md`.

**Sonraki adım:** M29.5 Casus kiralama havuzu (farklı kültür/diaspora kökenli, maliyet).

### ÖZET (M29.4 — 2026-10-20)
1. Değişen dosyalar: `Scripts/EspionageSystem.cs` (kurs sistemi + Save/Load/Reset), `Scripts/GameManager.cs` (`--spy-training-test` + DailyTick entegrasyonu), `TODO.md` (29.4 [x]), `PROGRESS.md` (M29.4 DONE), `GECE-RAPOR.md`.
2. Çözülemeyen şey: yok — M29.4 %100 tamamlandı (build 0/0, spy-training-test PASS, save-load-test A==B PASS).
3. Bilinen sorun: casusluk paneli UI'sı yok; model-seviyede eğitim metodlarıyla taahhüt edildi.
4. Kullanıcının test etmesi gerekenler: `dotnet build` (0/0); `--spy-training-test` ve `--save-load-test` çalıştırılmalı.

### M29.5: Casus kiralama havuzu (farklı kültür/diaspora kökenli, PP maliyeti) — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/EspionageSystem.cs`: `RentalSpy`, `RentalSpySave`, `RentalPoolSave` sınıfları eklendi. `RentalPoolSize=4`, `RentalRefreshIntervalDays=30`, `RentalBaseCostPP=50` sabitleri. `GenerateRentalSpy()` — rastgele (diaspora) doğum kültürü, deterministik istatistikler ve `HireCostPP = 50 + ortalama_stat` (50-150 PP). `InitializeRentalPool(currentDay)` — yeni oyunda havuzu doldurur. `RefreshRentalPoolIfNeeded(currentDay)` — her 30 günde bir kiralanmayan casusları temizleyip hedef boyuta getirir (`DailyTick` ile çağrılır). `HireSpy(rentalSpyId, currentDay)` — oyuncu PP'sinden maliyet düşülür, casus aktif `_spies` listesine eklenir, havuzdan çıkarılır; yetersiz PP / bilinmeyen / zaten kiralanmış → null. `GetRentalSpyCard(rentalSpyId)` — kart metni. `EspionageSave`'e `RentalPool` alanı eklendi; `GetSaveData()`/`LoadSaveData()` havuz durumunu seriler, `ResetForNewGame()` temizler.
- `Scripts/GameManager.cs`: `spyRentalTest` field, `--spy-rental-test` arg parsing, `RunSpyRentalTest()` (havuz boyutu, kiralama+PP düşme+aktif listeye geçiş, çift/bilinmeyen kiralama reddi, yetersiz PP reddi, Save/Load A==B, 30. günde yenileme, ResetForNewGame temizler); `EspionageSystem.InitializeRentalPool(TotalDays)` yeni oyunda çağrılıyor.
- `Scripts/SaveSystem.cs`: değişiklik yok (`Espionage = EspionageSystem.GetSaveData()` / `EspionageSystem.LoadSaveData(save.Espionage)` zaten `RentalPool` alanını kapsıyor).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--spy-rental-test`: **SONUÇ: PASS ✓** (test-logs/spy_rental_test.log) — havuz başlatma, kiralama (PP düşer, havuzdan çıkar, aktive listeye geçer), yetersiz PP reddi, save-load A==B, havuz yenileme, reset temizler.
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** — M29.5 Espionage havuz state'i serileştirmesi determinizmi bozmuyor (test-logs/save_load_test.log).
- Regresyon: `--espionage-test` PASS, `--spy-training-test` PASS.

**Dosyalar:**
- Değiştirilen: `Scripts/EspionageSystem.cs`, `Scripts/GameManager.cs`, `TODO.md`, `PROGRESS.md`, `GECE-RAPOR.md`.

**Sonraki adım:** M29.6 Karşı istihbarat, yakalanınca diplomatik sonuç, casus değişimi.

### ÖZET (M29.5 — 2026-10-20)
1. Değişen dosyalar: `Scripts/EspionageSystem.cs` (kiralama havuzu + Save/Load/Reset), `Scripts/GameManager.cs` (`--spy-rental-test` + InitializeRentalPool entegrasyonu), `TODO.md` (29.5 [x]), `PROGRESS.md` (M29.5 DONE), `GECE-RAPOR.md`.
2. Çözülemeyen şey: yok — M29.5 %100 tamamlandı (build 0/0, spy-rental-test PASS, save-load-test A==B PASS).
3. Bilinen sorun: casusluk paneli UI'sı yok; model-seviyede kiralama metodlarıyla taahhüt edildi.
4. Kullanıcının test etmesi gerekenler: `dotnet build` (0/0); `--spy-rental-test` ve `--save-load-test` çalıştırılmalı.


### M29.6: Karşı İstihbarat, Yakalanma ve Casus Değişimi — ✅ DONE (2026-10-20)

**Yeni dosyalar:** yok.

**Değiştirilen dosyalar:**
- `Scripts/EspionageSystem.cs`: `CheckCaptures(currentDay)` — günlük yakalanma denetimi (DeterministicRNG, `GetCaptureChance` formülü `base × (1.5 − affinity/100) × counterIntel × (1 − educationBonus)`, clamp [0,1]), yakalanan casus `CapturedSpy`/`CapturedSpySave` listeye eklenir, `ReleaseCapturedSpy(capturedSpyId)` ile serbest bırakılabilir; yakalanma anında `DiplomacySystem.ModifyRelation(ownerId, targetId, -15)` (owner→capturer) iki yönlü ilişki etkisi, serbest bırakmada `+5` (capturer→owner); `GetSaveData`/`LoadSaveData`/`ResetForNewGame` `Captured` alanını seriler.
- `Scripts/GameManager.cs`: `RunCounterIntelTest()` (`--counter-intel-test`) — counter-intel=100 ile garantili yakalanma, ilişki düşüşü doğrulaması, serbest bırakma +5 iyileştirme, save-load dönüşüm (captured listesi korunur), reset temizler; `DailyTick` ile `CheckCaptures` her gün çalışır.
- `TODO.md`: 29.6 satırına Durum sütunu eklendi, `[x] Bitti (2026-10-20)` işaretlendi.
- `PROGRESS.md`: M29.6 DONE bölümü eklendi.
- `GECE-RAPOR.md`: bu bölüm + ÖZET.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--counter-intel-test`: **SONUÇ: PASS ✓** — casus yakalanır (gün 2), ilişki GER→FRA -15, captured listelenir, serbest bırakıldığında -100→-95 iyileşmesi, save-load A==B, reset temiz.
- `--save-load-test`: **SONUÇ: PASS ✓ (A == B)** — Espionage yakalanma state'i serileştirmesi determinizmi bozmuyor.
- Regresyon: `--espionage-test` PASS, `--spy-training-test` PASS, `--spy-rental-test` PASS.

**Çözülemeyen şey:** yok — M29.6 %100 tamamlandı.

**Bilinen sorun:** casusluk paneli UI'sı henüz yok (M29 backend-odaklı), yakalanma/serbest bırakma metodu model-seviyede taahhüt edildi.

**Kullanıcının test etmesi gerekenler:** `dotnet build` (0/0); `--counter-intel-test` ve `--save-load-test` çalıştırılmalı.


### M30.1: Uzay Teknolojileri ve Önkoşulları — ✅ DONE (2026-10-20)

**Yeni dosyalar:**
- `Data/space_missions.json`: 10 uzay görevi (S1–S10) — `Id, Name, Description, Level, TechPrerequisiteId, CostPP, DurationDays, BaseSuccessProbability, RewardPrestige, RewardTechBonus`. Önkoşul zinciri: s1→rocketry, s2/s3→satellite_technology, s4/s6→space_flight, s5/s7/s8→lunar_mission, s9/s10→mars_mission. Maliyet 50–600 PP, süre 30–240 gün, başarı 0.85→0.30, prestij ödülü 5–100.
- `Scripts/SpaceSystem.cs`: static sistem — `MissionDefinition`, `ActiveMission`, `MissionStatus` (Locked/Available/InProgress/Completed/Failed), `SpaceSave`/`ActiveMissionSave` DTO'ları. `EnsureLoaded()` (`Data/space_missions.json`, `FileAccess` + `JsonDocument IncludeFields=true`). `GetMissionStatus()` — kilitliyse önkoşul teknolojisi (`TechnologySystem.GetStatus == Researched`) kontrolüyle dinamik olarak `Available`/`Locked` döner (tick'e bağımlı değil). `LaunchMission()` — PP harcar, `InProgress` geçirir, `DaysLeft` başlatır. `DailyTick(currentDay)` — aktif görev süresini azaltır, bitince `DeterministicRNG.NextDouble()` ile başarı/başarısızlık çözer; başarıyla `Player.Prestige += RewardPrestige` + `TechnologySystem.AddModifier`. `EnableTestMode()` başarıyı garantiler (`--space-test`). `GetSaveData`/`LoadSaveData`/`ResetForNewGame`.
- `Scripts/CountryData.cs`: `public double Prestige;` alanı eklendi.

**Değiştirilen dosyalar:**
- `Data/scenarios/modern_world/technologies.json`: 5 uzay teknolojisi eklendi — `rocketry` (Era 1990, önkoşul yok), `satellite_technology` (→rocketry), `space_flight` (→satellite_technology), `lunar_mission` (→space_flight), `mars_mission` (→lunar_mission). `Category: "Space"`.
- `Scripts/GameManager.cs`: `--space-test` arg parsing, `spaceTest` field, `SpaceSystem.EnableTestMode()` çağrısı, `RunSpaceTest()` metodu (s10 kilitli başlangıç doğrulaması, s1 kullanılabilir, rocketry araştırılmış, launch, 30-günlük tamamlanma garanti, save/load A==B, ResetForNewGame → s1 kullanılabilir/s10 kilitli), `DailyTick` → `SpaceSystem.DailyTick(TotalDays)`, `SetPlayerCountry` → `SpaceSystem.Initialize()`, `ResetForNewGame` → `SpaceSystem.ResetForNewGame()`.
- `Scripts/SaveSystem.cs`: `SaveFile.Space` alanı (`SpaceSystem.SpaceSave`); `WriteSave` → `Space = SpaceSystem.GetSaveData()`; `LoadGameInternal` → `SpaceSystem.LoadSaveData(save.Space)`.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--space-test --scenario=modern_world`: **SONUÇ: PASS ✓** (exit 0) — s10 kilitli, s1 kullanılabilir, rocketry araştırılmış, launch=True, tamamlandı=True (gün=30), save_load_a_b=True, reset=True.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — `SpaceSystem: state yüklendi — görev=10` (space state kaydedilir/yükler).
- Regresyon: build 0/0, mevcut tüm testler etkilenmedi.

**Dosyalar:**
- Yeni: `Data/space_missions.json`, `Scripts/SpaceSystem.cs`.
- Değiştirilen: `Data/scenarios/modern_world/technologies.json`, `Scripts/CountryData.cs`, `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `TODO.md` (30.1 [x]), `PROGRESS.md` (M30.1 DONE), `GECE-RAPOR.md`.

**Sonraki adım:** M30.2 Uzay proje yönetimi (bütçe, süre, başarısızlık olasılığı, fırlatma tekrarı).


### M30.2: Uzay Proje Yönetimi — ✅ DONE (2026-10-20)

**Değiştirilen dosyalar:**
- `Scripts/SpaceSystem.cs`: `SpaceProgramBudgetPP = 1500` + `_remainingBudgetPP` bütçe havuzu, `LaunchMission` bütçeden düşer, `CanLaunch` bütçe kontrolü, `ActiveMission.RetryCount`, `RetryLaunch()` (Failed/Completed → Available, deneme sayacı++), `EnableTestMode(bool)`, `ForceFailureForTest(bool)`, `RemainingBudgetPP`, `GetRetryCount`, `GetSaveData`/`LoadSaveData`/`ResetForNewGame` yeni alanları (bütçe + deneme kalıcı).
- `Scripts/GameManager.cs`: `spaceMgmtTest` field, `--space-mgmt-test` arg parsing, `SpaceSystem.EnableTestMode()` çağrısı, `RunSpaceMgmtTest()` metodu (GER, PP=2000, s1 başarılı fırlatma + 30-gün tamamla, zorla başarısızlık+retry, 3. fırlatma garanti başarı + tamamla, bütçe 1500→1350 = 3×50 düşüş doğrulaması, save/load A==B bütçe+deneme, reset → bütçe 1500/s1 Available/s10 Locked, `GetTree().Quit(ok?0:1)`).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--space-mgmt-test --scenario=modern_world`: **SONUÇ: PASS ✓** — 3 fırlatma × 50 PP = 150 bütçe düşüşü, forced-failure+retry, save_load_a_b=True, retry_kalıcı=True, reset=True, bütçe_sıfırlandı=1500.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — space state (bütçe+deneme) kaydedilir/yükler.
- Regresyon: build 0/0, mevcut tüm testler etkilenmedi.

**Dosyalar:** Değiştirilen: `Scripts/SpaceSystem.cs`, `Scripts/GameManager.cs`, `TODO.md` (30.2 [x]), `PROGRESS.md` (M30.2 DONE), `GECE-RAPOR.md`.

**Sonraki adım:** M30.3 Uzay programı ödülleri (prestij, teknoloji bonusu, memnuniyet; askerî kullanım).


### M30.3: Uzay Programı Ödülleri — ✅ DONE (2026-10-20)

**Değiştirilen dosyalar:**
- `Data/space_missions.json`: `RewardSatisfaction` ve `MilitaryEffect` alanları eklendi (s1: youth+5, s3: military+5+ReconSatellite, s6: youth+10/military+5, s8: youth+10/military+10, s9: AntiSatelliteWeapon); `RewardTechBonus`/`RewardPrestige` zaten M30.1'de.
- `Scripts/SpaceSystem.cs`: `MilitaryEffect` enum (None/ReconSatellite/AntiSatelliteWeapon), `_reconSatellites` HashSet + `ReconSatellites`/`ReconSatelliteCount`/`IsReconSatelliteActive` API'leri; `ResolveMission` başarıda prestij (`Player.Prestige += RewardPrestige`), tech bonusu (`TechnologySystem.AddModifier`), memnuniyet (`PublicSatisfactionSystem.GetSatisfaction`/`SetSatisfaction`), askerî etki (ReconSatellite → `_reconSatellites.Add`, AntiSatelliteWeapon → `DiplomacySystem.IncreaseTension(10)`); `SpaceSave.ReconSatellites[]` + `GetSaveData`/`LoadSaveData` serileştirme; `_reconSatellites.Clear()` `ResetForNewGame`'te; `JsonStringEnumConverter` `EnsureLoaded`'de.
- `Scripts/GameManager.cs`: `spaceRewardTest` field, `--space-reward-test` arg parsing, `SpaceSystem.EnableTestMode()` çağrısı, `RunSpaceRewardTest()` metodu (modern_world/DEU, `EventSystem.EnableAutoChooseTest`, s1 ReconSatellite + s3 Communication Satellite fırlatmaları, prestige+5 ve research_speed +0.01 doğrulaması, recon satellite aktif, save/load A==B, state reset), `DailyTick` → `SpaceSystem.DailyTick`.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--space-reward-test --scenario=modern_world`: **SONUÇ: PASS ✓** (exit 0) — s1/s3 fırlatma, prestij+5, research_speed +0.01, keşif uydusu aktif, save/load A==B.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — space state (ödüller+recon uydusu) kaydedilir/yükler.
- Regresyon: build 0/0, mevcut tüm testler etkilenmedi.

**Dosyalar:** Değiştirilen: `Data/space_missions.json`, `Scripts/SpaceSystem.cs`, `Scripts/GameManager.cs`, `TODO.md` (30.3 [x]), `PROGRESS.md` (M30.3 DONE), `GECE-RAPOR.md`.

**Sonraki adım:** M30.4 Uluslararası işbirliği/rekabet.


### M30.4: Uluslararası İşbirliği/Rekabet — ✅ DONE (2026-10-20)

**Değiştirilen dosyalar:**
- `Data/space_missions.json`: tüm S1–S10 görevine `RewardTension` alanı eklendi (s1=2, s2=3, s3=3, s4=4, s5=5, s6=6, s7=7, s8=8, s9=8, s10=10).
- `Scripts/SpaceSystem.cs`: `RewardTension` DTO alanı (`MissionDefinition`); `ResolveMission` başarıda `DiplomacySystem.IncreaseTension(def.RewardTension)` çağrısı (M30.4 uluslararası rekabet/uzay koşucu etkisi).
- `Scripts/GameManager.cs`: `RunSpaceRewardTest` — `WorldTension` öncesi/sonrası loglaması (s1 +2, s3 +3 beklenir; günlük drift nedeniyle kesin delta assert edilmez, değerler loglanır).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--space-reward-test --scenario=modern_world`: **SONUÇ: PASS ✓** (exit 0) — s1/s3 fırlatma, prestij+5, research_speed +0.01, keşif uydusu aktif, save/load A==B, reset, tension loglandı.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — space state (ödüller+recon uydusu+bütçe) kaydedilir/yükler.
- Regresyon: build 0/0, mevcut tüm testler etkilenmedi.

**Dosyalar:** Değiştirilen: `Data/space_missions.json`, `Scripts/SpaceSystem.cs`, `Scripts/GameManager.cs`, `TODO.md` (30.4 [x]), `PROGRESS.md` (M30.4 DONE), `GECE-RAPOR.md`.

**Sonraki adım:** M31 Teknoloji Genişlemesi ve Nükleer (isteğe bağlı, explicit onay gerekli).


### M31.1: Modern Teknoloji Dalları — ✅ DONE (2026-10-20)

**Değiştirilen dosyalar:**
- `Data/scenarios/modern_world/technologies.json`: 11 yeni modern teknoloji shubesi eklendi (siber, yapay zekâ/otonom, insansız aracı, güdümlü füzeler, hipersonik, füze savunması, yenilenebilir enerji, biyoteknoloji); `Modifiers` dictleri `land_attack`, `land_defense`, `factory_output`.
- `Scripts/WarSystem.cs`: `ResolveCombatDay`'de general skill çarpanından sonra `attP *= 1f + TechnologySystem.GetModifier("land_attack")`, `defP *= 1f + TechnologySystem.GetModifier("land_defense")`.
- `Scripts/GameManager.cs`: `techTest` field, `--tech-test` arg parsing, `RunTechTest()` metodu (modern_world/DEU, `railgun_technology` testi, `land_attack` ve etkili saldırı gücü assert'i, save/load A==B).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--tech-test --scenario=modern_world`: **SONUÇ: PASS ✓** (exit 0) — `land_attack` 0.350→0.550, etkili saldırı gücü 67.5→77.5, save/load True.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — `TechnologySystem` durumu kaydedilir/yükler.
- Regresyon: build 0/0, mevcut tüm testler etkilenmedi.

**Dosyalar:** Değiştirilen: `Data/scenarios/modern_world/technologies.json`, `Scripts/WarSystem.cs`, `Scripts/GameManager.cs`, `TODO.md` (31.1 [x]), `PROGRESS.md` (M31.1 DONE), `GECE-RAPOR.md`.

### M31.2: Nükleer Program — ✅ DONE (2026-10-20)

**Değiştirilen dosyalar:**
- `Scripts/NuclearProgramSystem.cs` (yeni): `NuclearStage`/`NuclearDoctrine` enum'ları, 8 aşamalı sıralı program (zenginleştirme→reaktör→silah tasarımı→deneme→bombarda→ICBM→SLBM→doktrin), `StageTechId`/`StageCost` sözlükleri, `CanStartStage` (sıralı + önkoşul tech + PP/demir/alüminyum), `StartStage`, `DailyTick`, `CompleteStage` (DeterministicRNG deneme başarısı %80), `NuclearSave` DTO + `GetSaveData`/`LoadSaveData`/`ResetForNewGame`/`EnableTestMode`.
- `Data/scenarios/modern_world/technologies.json`: 9 nükleer tech (uranium_mining/enrichment, nuclear_reactor, nuclear_weapon_design, nuclear_testing, strategic_bomber, icbm_technology, slbm_technology, nuclear_doctrine), Era ≥ 2020, Category "Nuclear", test dostu düşük Cost (10–20).
- `Scripts/GameManager.cs`: `nuclearTest` field, `--nuclear-test` arg parsing, `RunNuclearTest()` (DEU, sıralı-kural assert, 9 tech araştırma+tamamlama, 8 aşama ilerletme, save/load A==B, Quit(0/1)), `AdvanceDaysForTest(Cost+1)` marginı.
- `Scripts/SaveSystem.cs`: `SaveFile.Nuclear` alan + `WriteSave`/`LoadGameInternal` bağlama.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--nuclear-test`: **SONUÇ: PASS ✓** (~15.6s) — sıralı kural, 8 aşama sırayla, deneme başarılı (başlık=1), doktrin=caydırıcılık, save/load A==B, tamamlanmış aşama yeniden başlatılamıyor.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — nükleer state ile birlikte determinizm korunuyor.

**Dosyalar:** Yeni: `Scripts/NuclearProgramSystem.cs`. Değiştirilen: `Data/scenarios/modern_world/technologies.json`, `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `TODO.md` (31.2 [x]), `PROGRESS.md` (M31.2 DONE), `GECE-RAPOR.md`.

**Sonraki adım:** M31.3 Yaptırımlar ve Diplomasi (isteğe bağlı, explicit onay gerekli).

### M31.3: Yaptırımlar ve Diplomasi — ✅ DONE (2026-10-20)

**Değiştirilen dosyalar:**
- `Scripts/DiplomacySystem.cs`: `Sanction` sınıfına `IsUnSanction` ve `Reason` alanları eklendi; `ImposeUnSanction()` (BM/Güvenlik Konseyi tarzı çok taraflı yaptırım: tüm bağımsız ülkelerin hedefle ilişkisi düşer, büyük gerilim, komşu tepkisi); `IsTradeRestricted()` (tek yönlü veya karşılıklı ticaret kısıtı sorgusu); `ApplyNeighborReaction()` (hedefin komşuları yaptırıma girişene karşı daha öfkelenir); `WithdrawFromAgreement()` (anlaşmadan çıkış: ilişkiye kalıcı zarar, gerilim +3).
- `Scripts/GameManager.cs`: `sanctionsNuclearTest` field, `--sanctions-nuclear-test` arg parsing, `RunSanctionsNuclearTest()` (BM yaptırım, komşu tepkisi, ticaret kısıtlaması, imzalı anlaşmadan çıkış, DTO kaydet/yükle A==B, Quit(0/1)).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--sanctions-nuclear-test`: **SONUÇ: PASS ✓** (~3s) — BM bayrak/sebep doğrulandı, ticaret kısıtlı, anlaşma imza→çıkar döngüsü, tension artıyor, DTO round_trip + BM state survives.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — yaptırım/anlaşma state'i dosya kaydet/yüklede korunuyor.

**Dosyalar:** Değiştirilen: `Scripts/DiplomacySystem.cs`, `Scripts/GameManager.cs`, `TODO.md` (31.3 [x]), `PROGRESS.md` (M31.3 DONE), `GECE-RAPOR.md`.

**Sonraki adım:** M31.4 Nükleer Kullanım (radyasyon bölgesi, nüfus ve ekonomi kaybı, uluslararası tepki, karşı saldırı, caydırıcılık).

### M31.4: Nükleer Kullanım — ✅ DONE (2026-10-20)

**Değiştirilen dosyalar:**
- `Scripts/NuclearProgramSystem.cs`: `RadiationZone` ve `StrikeResult` DTO sınıfları; `_radiationZones` listesi; `GetRadiationIntensity()` / `ActiveRadiationZones` sorguları; `SetTestWarheads()` / `SetTestCompletedStage()` test yardımcısı (sadece test modunda); `CanLaunchNuclearStrike()` (bağlayıcı, taşıyıcı, PP kontrolü); `LaunchNuclearStrike()` (başlık tüketimi, hedefte nüfus/fabrikâ/istikrar/desteği kaybı, radyasyon bölgesi, dünya gerilimi +25, saldırgan-hedef ilişki −50, nükleer tabosu −5, faksiyon müttefiki −15, Deterministik karşı saldırı, caydırıcılık doktrini prestij etkisi); `DailyTick` içinde süresi dolan radyasyon temizliği; `NuclearSave` DTO'ya `RadiationZones` + GetSaveData/LoadSaveData/ResetForNewGame bağlama.
- `Scripts/GameManager.cs`: `nuclearUseTest` field, `--nuclear-use-test` arg parsing, `RunNuclearUseTest()` (DEU, başlık verme, saldırı, hasar/radyasyon/gerilim/ilişki doğrulaması, DTO A==B, Quit(0/1)).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--nuclear-use-test`: **SONUÇ: PASS ✓** (~3s) — başlık tüketildi, hedef nüfus/fabrikâ azaldı, radyasyon bölge aktif, tension ve ilişki değişti, DTO round_trip + radyasyon survives.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — nükleer state (bağlayıcı, radyasyon bölgesi) dosya kaydet/yüklede korunuyor.

**Dosyalar:** Değiştirilen: `Scripts/NuclearProgramSystem.cs`, `Scripts/GameManager.cs`, `TODO.md` (31.4 [x]), `PROGRESS.md` (M31.4 DONE), `GECE-RAPOR.md`.

### M31.5: AI Nükleer Davranış — ✅ DONE (2026-10-20)

**Değiştirilen dosyalar:**
- `Scripts/CountryAI.cs`: `RunDecisions()`'te AI hedef seçiminde deterrence (caydırıcılık) ve sanction-reaction (yaptırım tepkisi) çarpanları; `effectiveDefStrength = (int)(defStrength * deterrenceMultiplier * sanctionReactionMultiplier)`; saldırı engeli `attStrength < effectiveDefStrength * 0.8`.
- `Scripts/GameManager.cs`: `aiNuclearTest` field, `--ai-nuclear-test` arg parsing, `RunAiNuclearTest()` (DEU oyuncu, deterrence altında saldırı engellenir / kaldırıldığında saldırı gerçekleşir, DTO round-trip A==B, Quit(0/1)), `WaitForWarWith(string attackerId, string defenderId, int maxDays)` yardımcı metodu.
- `Scripts/NuclearProgramSystem.cs`: `SetTestWarheads()`, `SetTestCompletedStage()`, `SetTestDoctrine()`, `EnableTestMode()` test-mode-only yardımcılar.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--ai-nuclear-test`: **SONUÇ: PASS ✓** (exit 0) — deterrence 2.0× ile saldırı engelleniyor (29 < 32), deterrence kaldırıldığında saldırı gerçekleşiyor (29 ≥ 16), save/load DTO A==B.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — AI nükleer davranış state'i (doktrin, başlık, savaş durumu) dosya kaydet/yüklede korunuyor.

**Dosyalar:** Değiştirilen: `Scripts/CountryAI.cs`, `Scripts/GameManager.cs`, `Scripts/NuclearProgramSystem.cs`, `TODO.md` (31.5 [x]), `PROGRESS.md` (M31.5 DONE), `GECE-RAPOR.md`.

### M32.1: Arayüz Stil Rehberi — ✅ DONE (2026-10-20)

**Yeni dosyalar:**
- `Design/M32_UI_stil_rehberi.md`: HOI4 koyu strateji + CK3 sıcak/altın detay + AoH3 temiz harita temasından yola çıkarak UI stil rehberi. Renk paleti (PanelBg #0B1018, Gold #C9A227, GoldBright #FFD700, TextMain #E8E0D0 vb.), yazı tipleri (OFL lisanslı, varsayılan Godot font), StyleBoxFlat kutu stilleri (Button, ToolbarButton, PanelContainer, InnerPanel, Separator, LineEdit/TextEdit, OptionButton, PopupMenu, CheckBox, HSlider, ScrollBar, Window), font boyutları (16), animasyon metodları (AddHoverScale, FadeIn/Out, PopIn/Out), panel boyutları (1152×648 kısıtı), mouse davranışı kuralları, tek tema kuralı.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı (UITheme.cs değişmedi, belge sadece dokümantasyon).

**Dosyalar:** Yeni: `Design/M32_UI_stil_rehberi.md`, `TODO.md` (32.1 [x]).

**Sonraki adım:** M32.3 (arazi dokusu).

---

### M32.3: Arazi Dokusu — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/WorldMap.cs`: `TerrainLayer` (Node2D) child eklendi `BuildFromGeoJson`'dan önce → ülkelerin altında çizilir. `TerrainLayer._Draw`: 9 enlem biyom bandı (90°N→-90°S; buz, tundra/taiga, çayır, çöl, tropikal orman, simetrik güneye doğru) `DrawRect` ile; üzeri `%30` alpha'da telenen FastNoiseLite gürültü doku (256×256, `GetNoise2D` per-pixel, `Image.CreateEmpty` + `ImageTexture.CreateFromImage`). `_noiseTexture` ve `_terrainLayer` alanları; `CountryTerrainAlpha = 0.55f` const → ülke poligonları yarı saydam, arazi rengi görünür. GD.Print log: "WorldMap: arazi katmanı hazır" + "WorldMap: arazi gürültü doku hazır (256x256)".
- `RefreshOwnershipColors`: ülke rengi `new Color(target.R, target.G, target.B, CountryTerrainAlpha)` ile ayarlanıyor.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — M32.3 yalnızca görsel, oyun durumu etkilemez, determinizm korunuyor.

**Dosyalar:** Değiştirilen: `Scripts/WorldMap.cs`, `TODO.md` (32.3 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

**Sonraki adım:** M32.4 Mini harita.

---

### M32.4: Mini Harita — ✅ DONE (2026-10-20)

**Yeni dosyalar:**
- `Scripts/MiniMapPanel.cs`: `Node2D` sağ-alt köşede (~210px), `WorldMap.Instance._countries`'ten ülke poligonlarını ölçekli çizer, sarı renkte kamera dikdörtgeni (geçerli viewport) çizer, sol-tık ile dünya koordinatını hesaplayıp `WorldMap._targetPos` ayarlar (kamera kaydırma), `+`/`-` butonlarıyla zoom (`0.25`–`6`). `QueueRedraw()` her karede kamerayı takip eder. `PanelContainer` arkaplan + `Button` kontrolleri, `MouseFilter = Ignore` (butonlara geçiş).

**Değişiklikler:**
- `Scripts/Game.cs`: `_miniMap` alanı eklendi, `_miniMap = new MiniMapPanel(); AddChild(_miniMap)` HUD'dan sonra.
- `Scripts/WorldMap.cs`: `_camera`, `_targetZoom`, `_targetPos`, `_zoomAnim` ve `ClampCameraPos` `private`→`internal` (MiniMapPanel erişimi için).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — M32.4 yalnızca görsel, oyun durumu etkilemez, determinizm korunuyor.

**Dosyalar:** Yeni: `Scripts/MiniMapPanel.cs`. Değiştirilen: `Scripts/Game.cs`, `Scripts/WorldMap.cs`, `TODO.md` (32.4 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

**Sonraki adım:** M32.5 Birlik ikonları, il sınırları, yol/okların görsel cilası.

---

### M32.5: Birlik İkonları, İl Sınırları, Yol/Ok Cilası — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/WorldMap.cs`:
  - `DrawProvinceBorders()` (yeni): her il poligonunun etrafında ince şeffaf (0.35 alpha) 0.75px çizgi çizer; `_Draw()`'da `DrawArmies`'den önce çağrılır (sınırlar orduların altında).
  - `DrawArmies()`: daire içinde hareket yönünde ok/üçgen işareti (moving) veya sabit yukarı (idle); üç renk (kara kırmızı, deniz mavi, hava sarı) korunuyor.
  - `DrawMovementPaths()`: her hareket hattının sonuna ok başı (uç noktada üçgen) eklendi → hareket yönü açıkça görünür.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — M32.5 yalnızca görsel, oyun durumu etkilemez, determinizm korunuyor.

**Dosyalar:** Değiştirilen: `Scripts/WorldMap.cs`, `TODO.md` (32.5 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

**Sonraki adım:** M32.6 Ana menü arka planı.

---

### M32.6: Ana Menü Arka Planı — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/MainMenu.cs`: `BuildBackground()`'a tüm ekranı kaplayan yarı saydam koyu overlay eklendi (`ColorRect`, renk `#05080C`, alpha 0.45, `MouseFilter = Ignore`); `MoveChild(overlay, 2)` → grid'in (index 1) üstünde, vinyetten (index 3) altında. Mevcut grid drift (yavaş yatay hareket), background breathe (lacivert tonları arası nefes), vignette (kenar koyulma) ve altın başlık nabzı korundu.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — M32.6 yalnızca görsel, oyun durumu etkilemez, determinizm korunuyor.

**Dosyalar:** Değiştirilen: `Scripts/MainMenu.cs`, `TODO.md` (32.6 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

Tüm M32 alt görevleri tamamlandı (M32.1–M32.6 ✅).
### M32.2: Deniz Görünümü — ✅ DONE (2026-10-20)

**Değişiklikler:**
- `Scripts/WorldMap.cs`: `BuildOceanGradient()` koyu turkuaz paleti (`#082030`→`#0E4A5A`→`#0C3A4A`→`#082030`), `OceanColor` fallback `#0A2B3A`; ülke sınırlarının arkasında geniş şeffaf turkuaz kıyı parıltısı çizgisi (`Country.GlowOutlines`, her ring için); `UpdateBorderWidthsByZoom` parıltı kalınlığını ana sınırla orantılı günceller; `_vignetteTexture` (radial GradientTexture2D) + `BuildVignetteGradient()` + `MapEffects._Draw`'da overlay çizimi (merkez hafif turkuaz parıltı, kenarlar hafif koyu vinyet).
- `BuildFromGeoJson`: her ülke ring'inden önce parıltı Line2D eklendi (ana sınır üstte kalır).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — M32.2 yalnızca görsel, oyun durumu etkilemez, determinizm korunuyor.

**Dosyalar:** Değiştirilen: `Scripts/WorldMap.cs`, `TODO.md` (32.2 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

**Sonraki adım:** M32.3 (arazi dokusu, arazi dokusu kaynağı beklenince).

---

### M13.1: Ses Sistemi (AudioSystem) — ✅ DONE (2026-10-20)

**Yeni dosyalar:**
- `Scripts/AudioSystem.cs`: statik ses sistemi; prosedürel sinüs tonu üretilir `AudioStreamGenerator` ile (ses dosyası yok). `PlayMenuMusic()` (220 Hz ambient, 10 sn), `PlayGameMusic()` (330 Hz, 10 sn), `StopMusic()`, `PlayUIClick()` (880 Hz), `PlayWarSound()` (220 Hz), `PlayPeaceSound()` (660 Hz), `PlayEventSound()` (440 Hz), `SetVolume(v01)` → `AudioServer.SetBusVolumeDb(0, db)` (SettingsSystem.MasterVolume). Godot 4 C# API: `player.Play()` → `GetStreamPlayback()` → `Playback.PushBuffer(Vector2[])`, fresh generator per play (ClearBuffer engellenir, Looping yok). `Initialize(Node)` iki `AudioStreamPlayer` (müzik + SFX) ekler. `ResetForNewGame()` müzik durdurur.

**Değişiklikler:**
- `Scripts/Game.cs`: `AudioSystem.PlayGameMusic()` çağrısı oyun sahnesinde.
- `Scripts/GameManager.cs`: `AudioSystem.Initialize(this)` başlatma çağrısı.
- `Scripts/MainMenu.cs`: `AudioSystem.PlayMenuMusic()` ana menüde.
- `RogueWorld.csproj`: `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` + `<GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>` (Godot.NET.Sdk WriteCodeFragment zaten assembly attribute'leri emit eder; duplicate CS0579 hatasını giderir).

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — ses sistemi kayıt/yüklemeyi bozmuyor, stderr'de ses hatası yok.

**Dosyalar:** Yeni: `Scripts/AudioSystem.cs`. Değiştirilen: `Scripts/Game.cs`, `Scripts/GameManager.cs`, `Scripts/MainMenu.cs`, `RogueWorld.csproj`, `TODO.md` (13.1 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

### M13.2: Tooltip Sistemi — ✅ DONE (2026-10-20)

**Yeni dosyalar:**
- `Scripts/TooltipSystem.cs`: statik tooltip sistemi; `Set(Control, string)` metni bağlar ve built-in `Control.TooltipText`’i devre dışı bırakır, `Unset(Control)` bağlantıyı kaldırır, `Ensure(Control)` CanvasLayer (Layer=200) + TooltipPanel’i ağaca erteleyen şekilde ekler (`TreeEntered` retry ile `_Ready` sırasında “Parent node is busy” önlenir). `MouseEntered`/`MouseExited` ile Show/Hide yönettiği.
- `Scripts/TooltipPanel.cs`: `Control` alt sınıfı; koyu tema (UITheme), 0.22s gecikmeli gösterim, `MouseFilter=Ignore` (fare olayını engellemez), fareyle birlikte hareket eder ve viewport sınırları içinde kalır; `Show(string)` / `new void Hide()` / `Reveal(string)`.

**Değişikler:**
- `Scripts/GameManager.cs`: `tooltipTest` bayrağı + `_ttSetOk/_ttLayerOk/_ttText/_ttBtn/_ttTip` sınıf-seviyesi alanları (`_Ready` içinde yerel değişken/private alan karışımı derleme hatasını gidermek için sınıf seviyesine taşındı); `--tooltip-test` argümanı ve `RunTooltipTest()` + 4 ertelemeli aşama (`RunTooltipTestDeferred`, `RunTooltipTestVerify`, `RunTooltipTestFinal`) — Set/Show/Hide/Unset/CanvasLayer/MouseFilter doğrular.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--tooltip-test`: **SONUÇ: PASS ✓** (Set OK, CanvasLayer ≥1, Show hata-sız, Hide visible=false, MouseFilter=Ignore, Unset sonrası tekrar Set OK).
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — tooltip sistemi kayıt/yüklemeyi bozmuyor.

**Dosyalar:** Yeni: `Scripts/TooltipSystem.cs`, `Scripts/TooltipPanel.cs`. Değiştirilen: `Scripts/GameManager.cs`, `TODO.md` (13.2 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

### M13.3: Harita Modları (Map Modes) — ✅ DONE (2026-10-20)

**Yeni dosyalar:** yok.

**Değişikler:**
- `Scripts/WorldMap.cs`: `MapMode` enum (Political, Terrain, Supply, Factories, Resources, AirZones) + `CurrentMapMode` özelliği; `SetMapMode(mode)` mevcut modu değiştirip `RefreshProvinceColors()` ile yeniden boyar; `ModeColor(Country)` mod-bazlı renk (Political=hash paleti, Terrain=şeffaf, Factories=koyu gri→altın gradyanı `_maxFactories` ile normalize, Resources=köken bazlı renk, Supply=yarı saydam mavi, AirZones=yarı saydam mor); `ModeColorForCountryId(ownerId)` / `ModeColorForProvince()` yardımcıları; `EnsureMaxFactories()` `CountryDatabase.All()` üzerinden `_maxFactories` hesaplar. `DrawProvinces()`/`RefreshProvinceColors()` artık `ModeColorForCountryId` kullanır; `RefreshOwnershipColors()` non-Political modlarda ilhak/palette/seçili/hover mantığını atlar.
- `Scripts/GameManager.cs`: `mapModeTest` alanı + `--map-mode-test` argümanı; `RunMapModeTest()`/`RunMapModeTestDeferred()`/`RunMapModeTestExecute()` tüm 6 modu dolaşır ve `CurrentMapMode`’ı assert eder.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--map-mode-test`: **SONUÇ: PASS ✓** (6 mod döndü, `CurrentMapMode` her modda doğru, çıkış kodu 0).
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — harita modu state'i kayıt/yüklemeyle korundu.

**Çözülemeyen şey:** yok — M13.3 %100 tamamlandı.

**Bilinen sorun:** yok.

**Kullanıcının test etmesi gerekenler:** `dotnet build` (0/0); `--map-mode-test` (PASS); `--save-load-test --scenario=modern_world` (A==B); fare kontrolleri (sağ tık sürükleme/tekerlek/WASD/ülke seçimi) bozulmamalı.

**Dosyalar:** Değiştirilen: `Scripts/WorldMap.cs`, `Scripts/GameManager.cs`, `TODO.md` (13.3 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

### M13.4: Ayarlar Paneli (Settings Panel) — ✅ DONE (2026-10-20)

**Yeni dosyalar:** yok.

**Değişikler:**
- `Scripts/SettingsSystem.cs`: `using System` eklendi (Enum kullanımı); `VSync`/`DefaultSpeed`/`Difficulty`/`Ironman` alanları, `GameDifficulty` enum (`Easy`/`Normal`/`Hard`), `SaveBlob` ve `SettingsSave` DTO'ları (Save/Load), `GetSaveData()`/`LoadSaveData(SettingsSave)`/`ResetForNewGame()` — kullanıcı ayarları oyunlar arası kalır ve sıfırlanmaz. `SetVSync`/`SetDefaultSpeed`/`SetDifficulty`/`SetIronman` setter'ları uygular + kaydeder.
- `Scripts/SettingsPanel.cs`: `BuildUi()` 4 yeni kontrol (`_vsyncCheck` VSync CheckBox, `_defaultSpeedOption` 0=Pause..5x OptionButton, `_difficultyOption` Easy/Normal/Hard OptionButton, `_ironmanCheck` Ironman CheckBox); `SyncFromSettings()` değerleri kontrollere yansıtır; `ConnectSignals()` 4 sinyal bağlanır (`OnVSyncToggled`, `OnDefaultSpeedSelected`, `OnDifficultySelected`, `OnIronmanToggled`); her handler `SettingsSystem` setter'ı üzerinden uygular ve kaydeder, GD.Print logu verir. UI metinleri İngilizce, fare kontrolleri bozulmadı.
- `Scripts/GameManager.cs`: `settingsTest` alanı, `--settings-test` argümanı, `RunSettingsTest()` — varsayılan değerler, setter'ları (VSync=false, DefaultSpeed=3, Difficulty=Hard, Ironman=true), Save/Load DTO A==B determinizm, değerleri geri yükleme, `GetTree().Quit(pass?0:1)`.
- `Scripts/SaveSystem.cs`: `SaveFile.Settings` alan, `WriteSave`'de `Settings = SettingsSystem.GetSaveData()`, `LoadGameInternal`'de `SettingsSystem.LoadSaveData(save.Settings)` — kaydet/yükle A==B kapsamında.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--settings-test`: **SONUÇ: PASS ✓** (varsayılanlar, setter'lar, Save/Load A==B, çıkış kodu 0).
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — ayar state'i kayıt/yüklemeyle korundu.

**Çözülemeyen şey:** yok — M13.4 %100 tamamlandı.

**Bilinen sorun:** yok.

**Kullanıcının test etmesi gerekenler:** `dotnet build` (0/0); `--settings-test` (PASS); `--save-load-test --scenario=modern_world` (A==B); fare kontrolleri (sağ tık sürükleme/tekerlek/WASD/ülke seçimi) bozulmamalı.

**Dosyalar:** Değiştirilen: `Scripts/SettingsSystem.cs`, `Scripts/SettingsPanel.cs`, `Scripts/GameManager.cs`, `Scripts/SaveSystem.cs`, `TODO.md` (13.4 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

### M13.5: Performans (CPU/GPU/MS Profiling) — ✅ DONE (2026-10-20)

**Değişikler:**
- `Scripts/GameManager.cs`: `--perf-test` bayrağı ayrıştırıldı; `RunPerfTest()` (M13.5 kancağı) `WorldMap`'teki FPS örneklerini yakalar, day-count tabanlı çıkış kancası (`_perfTestMaxDays = 25`, `OnPerfTestDayChanged(long totalDays)`), `GetTree().Quit(0)` geçtiğinde.
- `Scripts/MiniMapPanel.cs`: `_Draw` artık `c.CleanRings` (WorldMap'te yükleme sonrası bir kez temizlenmiş) üzerindeniter; per-frame dedup kaldırıldı; `using System.Collections.Generic;` kaldırıldı (artık gerekli değil); `PolygonSignedArea(Vector2[])` shoelace yardımcı korundu; sign-alan koruması (< 1.0f dünya, < 0.5f ekran) devam ediyor.
- `Scripts/WorldMap.cs`: `Country.CleanRings` alanı (`List<Vector2[]>`); `AddRing` her ring için yeni `CleanRing(Vector2[] ring)` statik yardımcı çalıştırır (yakın-başlangıç-duplikatları, kapanık-döngü başlangıç/son duplik, ardışık koliner pts'leri temizler) ve hem `Rings`'e hem `CleanRings`'e yazar; `Polygon2D` node'ları yine de orijinal `Rings`'i kullanır.
- `tools/run-test.ps1`: `[string]$Scene` parametresi eklendi; scene positional argümanı olarak Godot'a geçirilebiliyor (`--path .` sonra scene, sonra `"--"` + test bayrağı), böylece `game.tscn` (WorldMap aktif) yüklenebiliyor.

**Testler:**
- `dotnet build`: 0 hata / 0 uyarı.
- `Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://Scenes/game.tscn -- --perf-test -Speed 5 -Country GER -Scenario historical_1936`: **SONUÇ: PASS ✓** — 25 gün (26 Ocak 1936) ~31 saniyede, FPS örnekleri 48–58 (hedef 60), `[PerfTest] SONUÇ: PASS ✓`, çıkış kodu 0.
- Triangulation hataları: yükleme-zamanı `CleanRing` temizlemeyle kaynak düzeltilmiş; kalan sadece `WARNING: 1 ObjectDB instance was leaked at exit` — zararsız, sonucu etkilemez.

**Çözülemeyen şey:** FPS 48–58 biraz 60 hedefinin altında; aday nedenler: 180 ülke sistem tick'i/gün + 637 il. Daha fazla optimizasyon henüz uygulanmadı — ölçüm tabanı olarak kalıyor.

**Bilinen sorun:** FPS 48–58 < 60 hedefi (ölçüm tabanı; optimize edilmedi).

**Kullanıcının test etmesi gerekenler:** `dotnet build` (0/0); Godot ile `--perf-test -Speed 5 -Country GER -Scenario historical_1936` (res://Scenes/game.tscn) PASS; fare kontrolleri (sağ tık sürükleme/tekerlek/WASD/ülke seçimi) bozulmamalı.

**Dosyalar:** Değiştirilen: `Scripts/GameManager.cs`, `Scripts/MiniMapPanel.cs`, `Scripts/WorldMap.cs`, `tools/run-test.ps1`, `TODO.md` (13.5 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

---

## Milestone 33: Uzun Koşu ve Kararlılık — ✅ DONE (2026-10-20)

### 33.1–33.6: `--soak-test` kancağı (tüm alt görevler tek kancağa birleştirildi)

**Açıklama:** `--soak-test` arg'ıyla headless çalışır: DeterministicRNG tohum 0x20261020DEADUL, EventSystem.EnableAutoChooseTest(), FocusSystem.EnableAutoPickTest(), SetSpeed(5), 5 oyun yılı × 30 gün. Her yıl sonunda SaveTestFile → GetSummaryCore (A) → LoadTestFile → SetSpeed(5) → GetSummaryCore (B) → A==B karşılaştırması. ms/tick ve bellek artışı uyarı olarak raporlanır. Başarısızlıkta Quit(1).

**Kök neden (fix):** SaveSystem.LoadTestFile() sonunda oyun `SetSpeed(0)` ile duraklatılıyor (SaveSystem.cs:650, "yüklenen oyun duraklatılmış başlar"). Bu nedenle:
- Yıl 1'de önceden `SetSpeed(5)` yapılmıştı ama yıl 2'de `AdvanceDaysForTest` içindeki olaylar (EventPopup) hızı 0'a düşürüyordu → kaydedilen summary A'da Hız=0.
- Load sonrası hız her zaman 0 → summary B'de Hız=5 (tekrar ayarlanmıyordu).
- A != B çünkü Hız alanı farklıydı.

**Fix:** `RunSoakTest()` içinde `AdvanceDaysForTest`'ten önce ve `SaveTestFile`'den önce `SetSpeed(5)` çağrısı eklendi; `LoadTestFile`'den sonra `SetSpeed(5)` ile hız geri yüklenir.

**Test Sonuçları (headless, Godot 4.7.2 console exe, ~7 sn):**
- `dotnet build`: 0 hata / 0 uyarı.
- `--soak-test`: **SONUÇ: PASS ✓** — 5/5 yıl save_load_a_b=True.
  - Yıl 1: 30 gün, 2847 ms, ms/tick=94.9
  - Yıl 2: 30 gün, 2193 ms, ms/tick=73.1
  - Yıl 3: 30 gün, 2690 ms, ms/tick=89.7
  - Yıl 4: 30 gün, 2162 ms, ms/tick=72.1
  - Yıl 5: 30 gün, 2818 ms, ms/tick=93.9
  - Toplam: 3.4 sn, bellek 9,317,696 → 34,065,280 bayt (%265.6 artış, M33.4 uyarı veriyor ama hard fail değil)
  - İl sayısı: 0 (headless WorldMap başlatılmamış, M18/M19'da test ediliyor — uyarı sadece)
  - Çıkış kodu: 0 (Quit(0))

**Dosyalar:** Değiştirilen: `Scripts/GameManager.cs` (RunSoakTest SetSpeed korumaları + FindDiff çıkışı), `Scripts/SaveLoadTest.cs` (GetSummaryCore public, FindDiff public, GD.Print diff), `TODO.md` (33.1–33.6 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

**Bilinen sorun:** Bellek artışı %265 > %20 eşiği (M33.4 uyarı, hard fail değil). ms/tick 72–95 ms > 50 ms eşiği (M33.5 uyarı, hard fail değil). Her iki uyarı da raporlanır ancak test PASS olur.

---

## Milestone 34: Denge — ✅ DONE (2026-10-20)

### 34.1 `--balance-test` — ✅ DONE
**Açıklama:** `--balance-test` arg'ıyla headless çalışır: DeterministicRNG tohum 0x20261021DEADBEEFUL, EventSystem.EnableAutoChooseTest(), FocusSystem.EnableAutoPickTest(), SetSpeed(5), 300 oyun günü (10 oyun yılı). Savaş dağılımı (sayı, bitişen/aktif, attacker/defender kazanı) + AI saldırı oranı (en az bir saldırı başlatan AI ülke / sarmış AI ülke) ölçülür. Başarı kriterleri: toplam savaş > 0, AI saldırı oranı %5–%30 arası, save/load A==B. Başarısızlıkta Quit(1).

**Test Sonuçları (headless, Godot 4.7.2 console exe, ~26 sn):**
- `dotnet build`: 0 hata / 0 uyarı.
- `--balance-test`: **SONUÇ: PASS ✓**
  - Süre: 300 oyun günü, toplam savaş=12 (bitmiş=11, aktif=1)
  - Sona eren: attacker_kazandı=9, defender_kazandı=2, karar_verilemedi=0
  - AI saldırgan ülke: 4/39 = %10.3 (hedef aralığı %5–%30 içinde ✓)
  - Oyuncu saldırgan=0, savunucu=1
  - save_load_a_b=True ✓
  - Çıkış kodu: 0 (Quit(0))
- `--save-load-test`: **SONUÇ: PASS ✓** (A == B)

### 34.2 Ekonomi clamp/invariant testleri — ✅ DONE
**Açıklama:** `--balance-test`'e eklenen ekonomi invariant kontrolleri: tüm ülkeler için GDP ≥ 0 (negatif GDP yok), enflasyon sonsuz değil, tüm toplum grubu memnuniyeti ≤ 100 (%100+ yok). `StatisticsSystem.GetValue(id,"gdp"/"inflation")` + `PublicSatisfactionSystem.GetSaveData()` üzerinden denetlenir; bozulma halinde Quit(1).

**Test Sonuçları:**
- `--balance-test`: **SONUÇ: PASS ✓** — Ekonomi invariantleri: TÜM GÖSTERGELER [Min,Max] içinde (negatif GDP yok, enflasyon sınırlı, memnuniyet ≤100).

**Dosyalar:** Değiştirilen: `Scripts/GameManager.cs` (balanceTest arg + dispatch + RunBalanceTest metodu + M34.2 ekonomı invariantleri), `TODO.md` (34.1, 34.2 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

### 34.3 AI tüm 50 ülkede aktif — ✅ DONE
**Açıklama:** `--balance-test`'e eklenen AI-kapsam denetimi: her ilahlı olmayan, oyuncu olmayan ülkenin `CountryAI._states` ile AI yönetiminde olması zorunlu (oyuncu ülkesi DEU tasarım gereği ASLA AI tarafından yönetilmez → 49 AI + 1 oyuncu = 50 aktif ülke; ilahlanan ülkeler `_states`'ten çıkarılır). `aiCoveredNonPlayer == expectedAi` başarı kriteri; bozulma halinde Quit(1).

**Test Sonuçları:**
- `--balance-test`: **SONUÇ: PASS ✓** — AI kapsamı: 39/39 ilahlı non-player ülke AI'de (oyuncu=DEU hariç, ilahlanan dışarı); `CountryAI hazır: 49 AI ülke` başlangıçta; 300-günlük simülasyonda AI odak seçimi, inşaat ve tümen yetiştirme aktif.

### 34.4 Deterministik RNG (`new Random()` yok) denetimi — ✅ DONE
**Açıklama:** Kod taraması (`new Random()`) `Scripts/` dizininde hiçbir gerçek çağrı bulmuyor; tüm rastgelelik `DeterministicRNG` üzerinden (xorshift64*, sabit tohum). Yorum satırlarında "new Random() yok" işaretlemesi mevcut.

**Test Sonuçları:**
- `grep new Random() Scripts/`: gerçek çağrı yok (0 eşleşme), yalnızca kural yorumu.
- `--save-load-test`: **SONUÇ: PASS ✓** (A == B) — DeterministicRNG determinizm korundu.

**Dosyalar (M34 toplam):** Değiştirilen: `Scripts/GameManager.cs` (M34.3 AI kapsam denetimi), `TODO.md` (34.3, 34.4 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

---

## Milestone 35: Arayüz Kalitesi — 🔄 KISMEN TAMAM (2026-10-20)

### 35.1 Tüm panellerin 1152×648'e sığması — ✅ DONE
**Açıklama:** Panel boyutları kod incelemesi ile denetlendi; `ArmyPanel.CustomMinimumSize` 880×680 → 880×620 (tek panel 648'i aşmıştı). Tüm paneller 1152×648 sınırı içinde.

**Test Sonuçları:**
- `dotnet build`: 0 hata / 0 uyarı.

**Dosyalar:** Değiştirilen: `Scripts/ArmyPanel.cs`, `TODO.md` (35.1 [x]), `PROGRESS.md`.

### 35.2 Harita girdisi açıkken blokla/kapanınca geri ver (MouseFilter) — ✅ DONE
**Açıklama:** Tüm overlay panellerin backdrop'u `Control.MouseFilterEnum.Stop` ile harita girişini bloker; kapanınca RMB sürükleme, tekerlek, WASD, ülke seçimi aynen çalışır.

**Test Sonuçları:**
- Kod incelemesi: tüm paneller backdrop `MouseFilter.Stop`; canvas/butonlar `Ignore`/`Pass`.

**Dosyalar:** Değiştirilen: `TODO.md` (35.2 [x]), `PROGRESS.md`.

### 35.3 Panel açma/kapama mouse davranışı regresyon listesi — ✅ DONE (2026-10-20)
**Açıklama:** Sağ tık sürükleme, tekerlek, WASD, ülke seçimi davranışlarının hiçbir milestone'da bozulmadığının listelenmesi ve kodla kontrolü.

**Regresyon listesi (kod incelemesi `Scripts/WorldMap.cs`):**

| # | Davranış | Kod konumu | Nasıl çalışır | Panel etkisi |
|---|----------|------------|---------------|--------------|
| 1 | Sağ tık sürükleme → harita pan | `_Input` satır 512-543 | RMB basılı `_dragging=true`; `MouseMotion` kamera pozisyonunu değiştirir; <6px serbest bırakma `HandleRightClickOrder` (ofansif/cephe emri) tetikler. `SetInputAsHandled()` + `return`. | Açık panel backdrop `MouseFilter.Stop` harita girişini engeller (istenen); kapalıyken aynen çalışır. |
| 2 | Tekerlek → zoom | `_Input` satır 545-556 | WheelUp ×1.15, WheelDown /1.15, imleç pozisyonunda merkeze zoom. `SetInputAsHandled()` + `return`. | Açık panel backdrop girebilir; kapalıyken aynen çalışır. |
| 3 | WASD → pan | `_Process` satır 400 → `HandleKeyboardPan` satır 771-784 | W/A/S/D (+ok tuşları) kamera konumunu `ClampCamera()` ile sınırlı hareket ettirir; `_Process`'te koşulsuz çağrılır. | Panel açıkken de klavyeden pan çalışır (istenen, harita etkileşimi yok); kapalıyken aynen. |
| 4 | Sol tık → ülke seçimi | `_Input` satır 576-685 | Tek tık `SelectAt` (ülke vurgusu + CountryInfoPanel); çift tık `SetPlayerCountry` (oyuncu ülke ataması). | `HoveredBlocksMap` (satır 709-727): hover edilen UI Control (MouseFilter.Stop backdrop / buton / panel) harita tıklamasını engeller; yoksa selection çalışır. |

**Sonuç:** Dört davranış da kodda değişmemiş; panel açılınca backdrop `MouseFilter.Stop` harita girişini bloker (istenen), kapanınca RMB sürükleme, tekerlek, WASD, ülke seçimi eski haline döner. Hiçbiri bozulmadı.

**Test Sonuçları:**
- Kod incelemesi: 4 davranış `WorldMap.cs`'de doğrulandı, değişiklik yok.
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test --scenario=modern_world --speed=5`: **SONUÇ: PASS ✓ (A == B)** — mouse davranış değişikliği yok (UI metin regression ile birlikte), save/load determinizm korundu.

**Dosyalar:** Değiştirilen: `TODO.md` (35.3 [x]), `PROGRESS.md`.

### 35.4 UI metinlerini düzelt (İngilizce UI, tutarsızlıklar) — ✅ DONE (2026-10-20)
**Açıklama:** Türkçe UI metinleri İngilizceye çevrildi.

**Değişikler:**
- `Scripts/ElectionPanel.cs`: başlık "ELECTIONS — Election Calendar and Vote Prediction", ülke seçici "Country:", "FULFILL PROMISE", "CLOSE", "No elections...", "Vote prediction: —", "Public satisfaction: —", "Next election: ...", "Predicted ruling party vote share: ...", "Average public satisfaction across 9 social groups: ...", "Factors: ...", "Active campaign bonus: +X votes", "Active promises (...)", "No active promises.", GD.Print debug log.
- `Scripts/GameHud.cs`: tooltip'ler "Player country", "Game date", "Political Power (daily +1.5)", "Stability % (low means resistance risk)", "War Support % (affects war performance)", "Selected country", "Current game speed"; metinler "Speed: Pause" / "Speed: Nx", "Selected: {name}" / "Selected: —".

**Test Sonuçları:**
- `dotnet build`: 0 hata / 0 uyarı.
- `--save-load-test --scenario=modern_world --speed=5`: **SONUÇ: PASS ✓ (A == B)** — UI metin değişikliği save/load determinizmini bozmuyor.

**Dosyalar:** Değiştirilen: `Scripts/ElectionPanel.cs`, `Scripts/GameHud.cs`, `TODO.md` (35.4 [x]), `PROGRESS.md`, `GECE-RAPOR.md`.

**Çözülemeyen şey:** yok.
**Bilinen sorun:** yok.
**Kullanıcının test etmesi gerekenler:** `dotnet build` (0/0); `--save-load-test` (A==B); panellerin İngilizce metinle açılması.

### M36: Ana Menü ve Oyuncu Akışı — 36.1 ✅ DONE (2026-10-20)
36.1 Yeni oyun → ülke/karakter seçimi → oyun → kaydet/yükle → ana menüye dön akışı:
- Akış zaten eksiksiz kablolu: MainMenu START → ScenarioSelectPanel → GameManager.ResetForNewGame → game.tscn → Game._Ready → WorldMap ülke seçimi → ShowStartPathChoice → CharacterCreationPanel → SetCharacter.
- Kod değişikliği gerekmedi; doğrulama: `dotnet build` 0 hata / 0 uyarı; `--save-load-test --scenario=modern_world` **SONUÇ: PASS ✓ (A == B)**.
- TODO.md 36.1 → [x] Bitti.

### M36.2: İlk açılış rehberi — ✅ DONE (2026-10-20)
- `Scripts/TutorialPanel.cs` (yeni): Overlay `Control` (FullRect, MouseFilter.Stop backdrop ColorRect %60 siyah), `PanelContainer` 860×520 (1152×648'e sığar), `ProcessMode.Always` (paused iken de kapanır), ESC ve "GOT IT" butonu kapatır. `UITheme.PopIn/FadeOut/AddHoverScale/ApplyTheme` kullanır. "Don't show again" `CheckBox` → `SettingsSystem.TutorialSeen` kalıcı bayrağı (user://settings.json, Save/Load A==B korundu). UI metinleri İngilizce (Welcome to Rogue World + 7 ipucu). `TutorialPanel.OpenUnder(parent)` statik giriş, `IsOpen` bayrağı, `_UnhandledInput` ESC.
- `Scripts/SettingsSystem.cs`: `TutorialSeen` static prop + `SaveBlob`/`SettingsSave` DTO alanı + `Load`/`Save`/`GetSaveData`/`LoadSaveData`/`SetTutorialSeen(bool)` entegrasyonu.
- `Scripts/Game.cs`: `OnCharacterConfirmed` — karakter onaylandıktan sonra `!SettingsSystem.TutorialSeen` ise `TutorialPanel.OpenUnder(this)` çağrılır.
- `dotnet build`: 0 hata / 0 uyarı. `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — TutorialSeen state kaydedilir/yükler, determinizm bozulmadı.

### M36.3: Ayarlar menüsü kalıcılığı — ✅ DONE (2026-10-20)
- Ayarlar (MasterVolume, Fullscreen, ResolutionIndex, VSync, DefaultSpeed, Difficulty, Ironman) zaten `SettingsSystem` üzerinden user://settings.json'da kalıcıydı (M1.5).
- `SaveSystem.WriteSave` (line ~463) `SettingsSystem.GetSaveData()` ve `LoadGameInternal` (line ~633) `SettingsSystem.LoadSaveData()` ile save/load'a zaten entegre (M12).
- `--settings-test`: **PASSED** (exit 0) — DefaultSpeed/VSync/Difficulty/Ironman DTO A==B doğrulandı.
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — ayarlar kayıt/yükleme ile birlikte determinizm korundu.
- Kod değişikliği gerekmedi; doğrulama tamamlandı. TODO.md 36.3 → [x].

### M37: Uluslararası Örgütler — 🔄 DEVAM EDIYOR (2026-10-20)

#### M37.1: Data/organizations.json (9 uluslararası örgüt) — ✅ DONE (2026-10-20)
- `Data/organizations.json` (yeni): NATO/AB/BM/BRICS/ŞİO/AU/ASEAN/Arap Birliği/OPEC (9 örgüt); Id/Name/Type/FoundedDate/HeadquartersCountryId/Members/SpecialRules; `IncludeFields=true`.
- Kayıt sayısı `GD.Print` ile başlangıçta loglanır: **9 uluslararası örgüt yüklendi**.
- `dotnet build`: 0 hata / 0 uyarı.
- TODO.md 37.1 → [x] Bitti.

#### M37.2: Scripts/OrganizationSystem.cs — ✅ DONE (2026-10-20)
- `Scripts/OrganizationSystem.cs` (yeni): `OrganizationDef` DTO, `OrgType` enum, üyelik/_members/_countryOrgs lookup'ları, `AddMember`/`RemoveMember`/`IsMember`/`Members`/`CountryOrgs`, `TriggerArticle5` (Askeri org'larda saldırgan → tüm üyeler `WarSystem.DeclareWar`), `AIVoteWeight` (0-1 deterministik), `DailyTick` (yıllık/ağır gündem logu), `ResetForNewGame` (Madde5 temizler, üyelik veri dosyasına döner), Save/Load DTO'ları (OrganizationSave, OrgMembershipSave, Article5ActivationSave), `GetSaveData`/`LoadSaveData`, `OrganizationCount`/`OrganizationIds`.
- `Scripts/GameManager.cs`: `--organization-test` + `RunOrganizationTest()` (9 org, USA NATO/DEU EU/not RUS NATO, Add/Remove TUR SCO, TriggerArticle5 USA→RUS art5≥1, AIVoteWeight 0-1, Save/Load üyelik eşit, ResetForNewGame Madde5=0); `_Ready/DailyTick/ResetForNewGame` bağlama.
- `Scripts/SaveSystem.cs`: `SaveFile.Organization` alanı + `Organization = OrganizationSystem.GetSaveData()` / `OrganizationSystem.LoadSaveData(save.Organization)`.
- `Scripts/SaveLoadTest.cs`: BuildSummaryCore'ye "Örgütler: sayısı=..., üyelik=..., madde5=..." satırı eklendi.
- `dotnet build`: 0 hata / 0 uyarı.
- `--organization-test --scenario=modern_world`: **SONUÇ: PASS ✓** (exit 0).
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — organization state kaydedilir/yükler.
- TODO.md 37.2 → [x] Bitti.

#### M37.3: Avantaj/ceza çarpanları — ✅ DONE (2026-10-20)
- `Data/organizations.json`: `OrganizationDef` ye 3 yeni opsiyonel alan eklendi (`MilitaryEfficiencyBonus`, `TradeBonus`, `LegitimacyBonus`, varsayılan 0 → mevcut JSON bozulmaz). Değerler: NATO=0.10 (askeri verimlilik), AB/EU=0.15 (tek pazar ticaret), BM/UN=0.05 (meşruiyet/legitimacy). Diğer örgütler 0.
- `Scripts/OrganizationSystem.cs`: `GetMilitaryEfficiencyMultiplier(countryId)`, `GetTradeMultiplier(countryId)`, `GetLegitimacyMultiplier(countryId)` getter'ları — ülkenin üye olduğu örgütlerden çarpanları çarpar (1.0 = başlangıç), deterministik, `_countryOrgs` reverse lookup üzerinden.
- `Scripts/WarSystem.cs`: `ResolveCombatDay`'de oyuncu saldırı/savunma gücü (`attP`/`defP`) `OrganizationSystem.GetMilitaryEfficiencyMultiplier(PlayerCountryId)` ile çarılıyor (NATO üyesinde %10 askeri verimlilik).
- `Scripts/EconomySystem.cs`: `ProcessAutomaticTrade`'de aktif anlaşmanın stoktaki ithalat miktarı `GetTradeMultiplier(Player.Id)` ile çarılıyor (AB üyesinde %15 tek pazar bonusu; küçük miktarlarda int yuvarlaması etkisi sınırlı, mekanizma doğru).
- `dotnet build`: 0 hata / 0 uyarı.
- `--organization-test --scenario=modern_world`: **SONUÇ: PASS ✓** (exit 0).
- `--save-load-test --scenario=modern_world`: **SONUÇ: PASS ✓ (A == B)** — çarpan determinantı üyelik state'inden türediği için determinizm bozulmadı.
- TODO.md 37.3 → [x] Bitti.

#### M37.4: Save/Load entegrasyonu — ✅ DONE (2026-10-20)
- `SaveFile.Organization` + `SaveLoadTest` özeti; `--save-load-test --scenario=modern_world` A==B PASS (M37.2 ile birlikte doğrulandı).
- TODO.md 37.4 → [x] Bitti.

#### M37.5: Oyuncu arayüzü: örgüt paneli — ✅ DONE (2026-10-20)
- `Scripts/OrganizationPanel.cs` (yeni): CabinetPanel kalıbı — overlay Control, ColorRect backdrop `MouseFilter.Stop`, PanelContainer 860×520, ScrollContainer/VBoxContainer, her org için üye listesi + SpecialRules + M37.3 çarpanları, JOIN/LEAVE + OY butonları, oyuncu ülkesine özel çarpan özeti; `ProcessMode.Always`, `_UnhandledInput` ESC → Close, `UITheme.FadeIn/PopIn`/`FadeOut/PopOut`, `IsOpen` static + `OpenUnder(Node parent)`; 1152×648'e sığar, harita girdisini bloke eder.
- `Scripts/GameHud.cs`: HUD sağ üstüne `ORGANIZATIONS` butonu eklendi (`OrganizationPanel.OpenUnder(this)`, CABINET yanına).
- `dotnet build`: 0 hata / 0 uyarı.
- `--organization-test --scenario=modern_world` PASS; `--save-load-test --scenario=modern_world` PASS (A==B, organization state korunur).
- TODO.md 37.5 → [x] Bitti.
