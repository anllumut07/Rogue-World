# Rogue World — Project State

## What is this game?
A **grand strategy game** inspired by Hearts of Iron IV / Age of History, built in **Godot 4.7.2 (C#, .NET 8)**. Player manages a modern country (or historical 1936 scenario) through politics, economy, military, diplomacy, technology, espionage, and internal governance. Deep simulation with deterministic mechanics (no RNG except `DeterministicRNG`), full save/load with A==B verification, and 30+ integrated systems.

## Tech Stack & Engine
- **Engine:** Godot 4.7.2 Mono (C#)
- **Language:** C# (.NET 8)
- **Architecture:** Static system classes (`*System.cs`) + scene nodes (UI, map)
- **Data:** `Data/*.json` (GeoJSON countries, technologies, equipment, organizations, formulas, scenarios)
- **RNG:** `DeterministicRNG` (xorshift64*) only — `new Random()` forbidden
- **Save/Load:** JSON to `user://saves/slot_N.json`, atomic write, versioned, A==B determinism test
- **Testing:** Headless via `tools/run-test.ps1 --<test-name>` (Godot `--quit-after`)

## Folder Structure (key)
```
C:\Proje\Rogue-World\
├── Scripts\                 # 100+ C# system classes + UI nodes
│   ├── *System.cs           # Core static systems (Economy, Army, Tech, Diplomacy, etc.)
│   ├── *Panel.cs            # UI panels (CountryInfo, Economy, Diplomacy, Statistics, etc.)
│   ├── GameManager.cs       # Root autoload, daily tick, test hooks, save/load orchestration
│   ├── WorldMap.cs          # Map rendering, province/army drawing, input handling
│   ├── CountryDatabase.cs   # Country data lookup (50 detailed + fallback)
│   ├── SaveSystem.cs        # Central save/load DTOs and file I/O
│   └── DeterministicRNG.cs  # xorshift64* RNG
├── Data\                    # All game data (JSON)
│   ├── scenarios/
│   │   ├── modern_world/    # Default scenario (current world ~2024)
│   │   └── historical_1936/ # Historical scenario
│   ├── countries.geo.json   # Country polygons (~180)
│   ├── countries_data.json  # Base country stats
│   ├── technologies.json    # 73 techs per scenario
│   ├── equipment.json       # 15 equipment types
│   ├── organizations.json   # 9 international orgs
│   ├── formulas.json        # Indicator linkage weights
│   └── ... (30+ JSON files)
├── Scenes\                  # .tscn files (main, game, main_menu, panels)
├── Assets\flags\            # 13 PNG flags + procedural fallback
├── tools\run-test.ps1       # Headless test runner
├── TODO.md                  # Master plan (this is the source of truth)
├── PROGRESS.md              # Detailed progress log
└── GECE-RAPOR.md            # Nightly work log
```

## What Works (Major Completed Milestones)
- **M0-M12:** Core game loop, map, country DB, economy, tech tree, production, military, diplomacy, focus trees, events/decisions, AI, save/load (A==B verified)
- **M15:** Quick improvements (mini country card, library, save/load v2, sea labels, flags)
- **M16:** Time & Tempo (weekly agenda, monthly report, auto-pause)
- **M17:** Modern World Scenario (50 countries, fictional leaders, modern tech, factions)
- **M17B:** International Organizations data design (NATO, EU, UN, BRICS, SCO, AU, ASEAN, Arab League, OPEC)
- **M18:** Province System (~1000 provinces, ownership, neighbors, performance, save/load)
- **M19:** Unit Movement & Command (HOI4-style: selection, drag-box, right-click move, waypoints, frontlines, offensive plans, naval/air)
- **M20:** Statistics & Budget (17 indicators, budget with tax/spending/debt, public satisfaction groups, formula linkages, graphs)
- **M21:** Ministries & Cabinet (12 ministries, 41 fictional ministers, traits, assignment effects)
- **M22:** Laws, Constitution, Parliament (20 constitutions, 9 laws, parties/seats/voting, authoritarian kongre)
- **M23:** Elections & Public (campaign actions, win/lose → new government, protests/coups)
- **M24:** Character Creation (appearance, 7 attributes, traits, origin, sandbox/career start)
- **M25:** Career & Education (politician/military paths, education modules, candidacy criteria, career events, character takeover)
- **M26:** Disasters & Unexpected Events (7 disaster types, intervention, event chains, weekly decision density)
- **M27:** Leader Diplomacy & Press (bilateral meetings, agreements, press events, sanctions/trade wars)
- **M28:** Agenda Panel / Social Media (tweet generator, fictional profiles, trends, actions, filters)
- **M29:** Espionage v2 (agent traits, affinity matrix, capture risk, training, rental pool, counter-intel)
- **M30:** Space Program (10 missions S1-S10, budget/retries, rewards: prestige/tech/military, tension)
- **M31:** Tech Expansion & Nuclear (8 modern branches, full nuclear program: enrichment→test→delivery→doctrine→use, AI behavior, sanctions)
- **M32:** Visual Identity (UI style guide, ocean gradient, terrain texture, minimap, unit icons/borders/paths, main menu)
- **M33:** Soak Test (10 game years, save/load checkpoints, tick timing, memory, crash-on-fail)
- **M34:** Balance (war distribution, indicator clamps, all 50 AI active, no `new Random()`)
- **M35:** UI Quality (panel sizing, input blocking, regression list, English UI texts)
- **M36:** Main Menu & Player Flow (new game → country/char → game → save/load → menu, tutorial, settings persistence)
- **M37:** International Organizations (code: membership, Article 5, meetings, AI voting, bonuses/penalties, UI, save/load)
- **M38:** Bug Fixes & Save/Load Regression (notifications, agenda effects, province neighbors, double init, A==B both scenarios)
- **M39:** War Depth (supply lines, naval invasion, army morale, air superiority, save/load)
- **M40:** Final Tests & GitHub Sync (all tests pass, performance, test-only code separation, push)
- **M41:** Victory Conditions & Game Over (scenario conditions, score system, game over panel, save/load)
- **M42:** War Score & Peace Conference (war score accumulation, peace terms JSON, territory transfer, AI behavior, UI, save/load)
- **M43:** Naval Supremacy & Convoy Raiding (12 sea zones, supremacy, sub raiding, lend-lease escort, UI, save/load)
- **M44:** Formable Nations & Releasable Countries (15 formables, province culture layer, formable system, releasable system, UI, save/load)
- **M45:** Autonomy, Resistance, Subject Management (autonomy 0-100 drift, resistance partisan actions, subject panel, save/load)
- **M46:** Culture Map View (MapMode.Culture, deterministic culture colors, CultureLegendPanel, save/load)
- **M47:** Province-Based Economy (province weights, recalc on transfer, resource shortage effects, save/load)
- **M48:** AI Dynamic Nations (AI formable nations, subject autonomy demands, recognition system for newborn countries, independence war)
- **M49:** War Changes Map (OccupationSystem: frontline progression, temporary ownership, war score integration, peace conference respects occupation, save/load)
- **M50:** Civil War & State Collapse (collapse score, deterministic rebellion, rebel country spawns with war+recognition, resolution paths: crush/autonomy/intervention, spread/calming, save/load)
- **M51:** Espionage Mission Effects (4 mission types: Intelligence/TechTheft/Sabotage/Propaganda with effects, counter-intel slowdown, diplomatic consequences, save/load A==B, test `--espionage-mission-test` PASS)
- **M52:** Recognition Consequences (Unrecognized State) — Recognition count gates org membership/trade/alliances; REQUEST RECOGNITION diplomatic action; war tension/intervention scaling; Save/Load A==B both scenarios — ALL DONE ✓
- **M53:** Advisor System — 18 advisor profiles (6 Economy/Security/Media), hire/fire per type, deterministic advice (confidence 10-100, rationale), 7-day cooldown, AdvisorPanel UI (active/müsait/history), HUD ADVISORS button, Save/Load A==B both scenarios — DONE ✓

## What is Broken / Unfinished
- **M54+** — Not yet planned in detail (see TODO.md "Önerilen Ek Özellikler")
- **Export/Deployment (M13-14)** — Blocked on Godot export templates, versioning, CI/CD (user approval needed)

## Known Issues / Technical Debt
- AI only manages ~49 detailed countries; rest passive
- Real FPS unmeasurable headless; editor manual check needed
- War balance (losses/progress speed) not tuned
- Sound files (music, UI clicks) missing — user must add CC0/licensed assets
- Decisions panel UI needs redesign (HOI4-style category view)
- Some test-only code paths still in production (being cleaned incrementally)

## Prioritized TODO (from TODO.md)
1. **M52.1-52.4** — Recognition consequences (org membership, trade, AI alliances, REQUEST RECOGNITION action, war tension/intervention, save/load A==B) — ALL DONE ✓
2. **M53.1-53.5** — Advisor System (18 profiles, hire/fire, deterministic advice, 7-day cooldown, AdvisorPanel UI, HUD button, save/load A==B both scenarios) — ALL DONE ✓
3. Then evaluate "Önerilen Ek Özellikler" or proceed to M13/M14 (polish/export)

---

**Current Branch:** `gece-calisma`  
**Last Commit:** `e74e5c3` — "feat: M52.3+M52.4 — Recognition war tension effects, civil war intervention scaling, save/load A==B verified both scenarios"  
**Last Test Run:** `--advisor-test` → PASS ✓, `--save-load-test` (historical_1936 + modern_world) → PASS ✓ A==B