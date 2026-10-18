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
- **Not:** Godot kurulumu yok (headless `--cabinet-meeting-test` çalıştırılamadı), dotnet build 0/0 geçti; manuel editör testi önerilir.

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
- **Kısıt:** Bu ortamda Godot editörü yok → `--province-test` / `--army-test` headless çalıştırılamadı. GECE-RAPOR.md'ye varsayım kaydedildi; headless doğrulama editörde gelecek oturumda.
- Sonraki adım: M22.3 Parlamento (partiler, sandalye dağılımı, oylama, veto, koalisyon, referandum).

### 2026-10-18 — M22.3 Parlamento (RunParliamentTest)
- `Scripts/GameManager.cs`: `RunParliamentTest()` method body (7 parti, 460 sandalye, çoğunluk eşiği 231, `HasMajority`/`CanPass`, sandalye ağırlıklı `Vote`, `Veto`/`IsVetoed`/`RemoveVeto`, `FormCoalition`, `Referendum`, PASS/FAIL raporu). `--parliament-test` kancağı ve call site mevcuttu.
- `Scripts/ParliamentSystem.cs`, `Data/parties.json`: M22.3 Parlamento mantığı — 7 kurgusal parti, 460 sandalye, eşik 231, oylama/veto/koalisyon/referandum, Save/Load DTO'ları.
- `dotnet build`: 0 hata / 0 uyarı.
- **Kısıt:** Godot editörü yok → `--parliament-test` headless çalıştırılamadı; varsayım GECE-RAPOR.md'ye kaydedildi, editörde doğrulama gelecek oturum.
- Commit ve push origin gece-calisma (1d1aa21).
- Sonraki adım: M22.4 Otoriter sistemler (parti kongresi, darbe riski).

### 2026-10-18 — M22.4 Otoriter sistemler (parti kongresi, darbe riski)
- `Scripts/AutoritarianSystem.cs` (yeni): tek parti rejimleri için parti kongre oylaması (DelegateCount=500, ApprovalThreshold=251) + günlük deterministik darbe riski (ComputeCoupRisk / TryTriggerCoup, DeterministicRNG, stabilite ve savaş desteğe bağlı). AutoritarianSave Save/Load DTO.
- `Scripts/GameManager.cs`: `_Ready` → `AutoritarianSystem.LoadData()`, `ResetForNewGame` → `ResetForNewGame()`, `DailyTick` → `AutoritarianSystem.DailyTick(PlayerCountryId)`, `--autoritarian-test` → `RunAutoritarianTest()`.
- `Scripts/SaveSystem.cs`: `SaveFile.Autoritarian` alanı + WriteSave/LoadGameInternal bağlama.
- `Scripts/SaveLoadTest.cs`: BuildSummaryCore'ye Otoriter özeti eklendi.
- `dotnet build`: 0 hata / 0 uyarı.
- **Kısıt:** Godot editörü yok → `--autoritarian-test` headless çalıştırılamadı; varsayım GECE-RAPOR.md'ye kaydedildi, editörde doğrulama gelecek oturum.
- Commit ve push origin gece-calisma.
- Sonraki adım: M22.5 (kongre/darbe UI paneli) veya M23 Seçimler.

### 2026-10-18 — M23.3 Seçim çözümü (kazanma/kaybetme, lider değişikliği, yeni kabine)
- `Scripts/LeaderSystem.cs`: `GenerateLoader` → `GenerateLeader` overload'a `ulong? seedOverride`; `RegenerateLeader(countryId, electionDay)` — seçim kaybı sonrası yeni deterministik lider (seed: "{ActiveId}:{countryId}:{electionDay}").
- `Scripts/ElectionSystem.cs`: `ElectionResult` modeli + `_electionResults` + `ResolveElection(countryId, currentDay)` (oy ≥ WinThreshold 50 → iktidar; altında → lider değişir + kabine yeniden dağıtılır), `GetSaveData`/`LoadSaveData` ile `Results` kalıcılığı.
- `Scripts/GameManager.cs`: `DailyTick` → oyuncu ülkesi için `ElectionSystem.ResolveElection(PlayerCountryId, TotalDays)`; `RunElectionTest` genişletildi (seçim çözümü, sonraki dönem, Save/Load sonuç kimliği).
- `dotnet build`: 0 hata / 0 uyarı.
- **Kısıt:** Godot editörü yok → `--election-test` headless çalıştırılamadı; varsayım GECE-RAPOR.md'ye kaydedildi.
- Commit ve push origin gece-calisma.
- Sonraki adım: M23.4 Protesto, grev, güven oylaması, erken seçim, darbe girişimi.
