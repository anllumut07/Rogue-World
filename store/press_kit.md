# Rogue World - Press Kit

## Quick Facts

| Field | Value |
|-------|-------|
| **Title** | Rogue World |
| **Genre** | Grand Strategy, Political Simulation |
| **Developer** | Rogue World Team |
| **Engine** | Godot 4.7.2 Mono (C#/.NET 8) |
| **Platforms** | Windows, Linux, macOS |
| **Release** | Early Access Q1 2027 |
| **Price** | $19.99 (EA), $29.99 (Full) |
| **Languages** | English (UI), more planned |
| **Website** | https://github.com/[username]/Rogue-World |
| **Contact** | GitHub Issues |

---

## Elevator Pitch

"Hearts of Iron IV meets modern geopolitics — but you play as a *character*, not just a country. Every system interconnects. Deterministic simulation. No hidden RNG."

---

## Key Selling Points

1. **Character-Driven Grand Strategy** — Rise from politician/soldier/academic to national leader
2. **Modern World (2026+)** — Real data for 50+ nations, modern tech tree, current factions
3. **Deep Interconnected Systems** — 12+ major systems (economy, diplomacy, military, climate, space, nuclear, etc.) that actually affect each other
4. **Deterministic by Design** — Custom RNG, save/load A==B verified, 70+ automated tests
5. **Two Scenarios** — Modern World + Historical 1936
6. **Modern UI** — 18 responsive panels, HOI4-style components, consistent theming

---

## Screenshots & Assets

### Required Screenshots (1920x1080 minimum)

| # | Scene | Description |
|---|-------|-------------|
| 1 | Main Menu | Animated background, scenario selector |
| 2 | World Map | Country selection, modern borders |
| 3 | Country Panel | Detailed modern data (GDP, military, nuclear) |
| 4 | Decision Panel | HOI4-style redesign with tabs/search |
| 5 | Diplomacy Panel | Factions, relations, world tension bar |
| 6 | Statistics Panel | 17 indicators with charts |
| 7 | Technology Tree | Modern era (1990-2020) |
| 8 | Military Panel | Armies, frontlines, division designer |
| 9 | Cabinet Panel | Ministers with traits, loyalty bars |
| 10 | Election Panel | Party support, campaign actions |
| 11 | Climate Panel | Warming chart, policies, COP meetings |
| 12 | Espionage Panel | Agents, missions, counter-intel |
| 13 | Space Panel | 10 missions, progress tracking |
| 14 | Monthly Report | Advisor summary, decisions |
| 15 | Character Creation | Procedural portrait, backgrounds |
| 16 | Library | 50-country encyclopedia |
| 17 | Gameplay Composite | Map + HUD + mini-map + panel |
| 18 | Peace Conference | War score, territory transfer |

### Brand Assets

- **Logo:** `store/assets/logo.png` (512x512, transparent background)
- **Capsule (Steam):** `store/assets/capsule_616x353.jpg`
- **Hero (Steam):** `store/assets/hero_1920x620.jpg`
- **Banner (itch.io):** `store/assets/banner_630x500.jpg`
- **Icon:** `icon.svg` (scalable vector)

### Color Palette

| Role | Hex | Usage |
|------|-----|-------|
| Deep Navy | `#0B1D3A` | Primary backgrounds |
| Gold Accent | `#D4A843` | Highlights, selected state, headers |
| Muted Teal | `#3A7B7B` | Secondary UI, buttons |
| Warm Gray | `#C9C9C9` | Text, borders |
| Danger Red | `#C0392B` | Warnings, war, negative trends |
| Success Green | `#27AE60` | Positive trends, peace |
| Ocean Blue | `#1A5F7A` | Water, diplomatic |

---

## Feature Deep-Dives (for Previews)

### The Character System
Unlike traditional grand strategy games where you *are* the country, in Rogue World you *become* a leader. Your character has 7 attributes that modify every action:
- **Diplomacy** → Better relations, cheaper agreements
- **Administration** → Faster construction, less corruption
- **Intelligence** → Better espionage, event detection
- **Charisma** → Election bonuses, rally effectiveness
- **Military** → Division stats, offensive planning
- **Economy** → Trade deals, factory efficiency
- **Law** → Constitution changes, legal reforms

Characters age, can get sick, marry, have heirs. Succession matters.

### Deterministic Simulation
- **Zero `new Random()`** — Custom xorshift64* (DeterministicRNG)
- **Save/Load A==B** — Run 10 years, save, load, run 10 more → identical state
- **70+ Headless Tests** — Balance, nuclear, space, war, focus, espionage, all automated
- **Reproducible Bugs** — Every bug report can include a save file that *guarantees* reproduction

### Weekly Agenda (The Tempo Mechanic)
Time moves slowly (1x = 0.5 days/second). You have **6 slots per week** for:
- **Meetings** (cabinet, foreign leaders, party)
- **Rallies** (stability, war support)
- **Press Events** (tone choice: hawkish/conciliatory/populist)
- **Summits** (organization meetings, bilateral)
- **Training** (attribute growth)

This creates a **decision density** of ~1 meaningful choice per minute of playtime.

### Modern World Data
50 countries with real-world approximations:
- Population, GDP (PPP), military personnel, nuclear warheads
- Resource production (oil, rare earths, steel, etc.)
- Government type, ruling party, leader (fictional)
- Faction membership (NATO, EU, BRICS, SCO, etc.)
- Climate profile (emissions, vulnerability, policies)

Sources cited in `Data/scenarios/modern_world/countries.json` — all "approximate values for gameplay."

---

## Quotes (Placeholder)

> "Rogue World captures the *feeling* of running a modern nation — the constant trade-offs, the interconnected crises, the weight of every decision — without drowning you in micromanagement." — *Internal playtest*

> "The deterministic simulation is a game-changer for strategy fans. You can actually *learn* the systems because they don't lie to you with hidden dice rolls." — *Internal playtest*

> "Playing as a character who *becomes* the leader, rather than just clicking buttons as an omniscient state, adds a layer of roleplay that HOI4 never had." — *Internal playtest*

---

## Technical Specifications

| Spec | Detail |
|------|--------|
| **Engine** | Godot 4.7.2 Mono |
| **Language** | C# (.NET 8) |
| **Architecture** | Static system classes + scene nodes |
| **RNG** | DeterministicRNG (xorshift64*) |
| **Save Format** | JSON (atomic write, versioned) |
| **Data Files** | 30+ JSON (GeoJSON, countries, tech, equipment, orgs, formulas) |
| **Map** | GeoJSON country polygons (~180), ~1000 procedural provinces |
| **Tests** | 70+ headless (Godot --headless) |
| **Performance** | ~17-21ms @ 5x speed, <55% CPU, <25MB/panel |
| **Build** | 0 errors / 0 warnings |

---

## Roadmap to 1.0

| Milestone | Target |
|-----------|--------|
| v1.0-beta | ✅ Complete (Nov 2026) |
| Export templates (Win/Linux/macOS) | ✅ Configured |
| CI/CD Pipeline | ✅ GitHub Actions |
| Sound integration (CC0) | 🔄 In Progress |
| Steam store page | 📋 Prepared |
| itch.io store page | 📋 Prepared |
| War balance tuning | ⏳ Planned |
| AI coverage (180 countries) | ⏳ Planned |
| Localization (6 languages) | ⏳ Planned |
| Mod support | ⏳ Planned |
| **v1.0 Release** | **Q1 2027** |

---

## Contact & Links

- **GitHub:** https://github.com/[username]/Rogue-World
- **Issues/Feedback:** GitHub Issues
- **Changelog:** `CHANGELOG.md`
- **Dev Logs:** `GECE-RAPOR.md`
- **Test Results:** `test-logs/*.log`

---

## FAQ for Press

**Q: Is this a HOI4 clone?**
A: Inspired by HOI4's UI/UX and some mechanics (focus trees, division templates, world tension), but fundamentally different: modern setting, character-driven, deterministic simulation, different tempo (weekly agenda), and deeper domestic simulation (elections, cabinet, laws, climate, migration).

**Q: Why deterministic RNG?**
A: For grand strategy, hidden RNG feels unfair. Players should be able to understand *why* something happened and reproduce it. Also enables reliable testing and multiplayer sync (future).

**Q: How many countries are playable?**
A: 50 detailed (modern) + 8 detailed (1936) + 130+ generic. All 180+ are selectable.

**Q: Is there multiplayer?**
A: Not in v1.0. Architecture supports it (deterministic sim), planned post-1.0.

**Q: Mod support?**
A: Data-driven (JSON), so modding is possible. Steam Workshop integration planned for v1.0.

**Q: System requirements?**
A: Modest. Runs on integrated graphics. 8GB RAM minimum. See store pages for details.

---

## Release Timeline

| Date | Milestone |
|------|-----------|
| Nov 2026 | v1.0-beta (all tests pass, export ready) |
| Dec 2026 | Steam page live, Early Access applications |
| Jan 2027 | Early Access launch (Steam + itch.io) |
| Q1 2027 | v1.0 full release |

---

*Press kit version 1.0 — Generated from v1.0-beta build*