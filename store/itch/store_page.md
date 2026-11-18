# Rogue World - itch.io Store Page

## Game Information

**Title:** Rogue World
**Genre:** Grand Strategy, Simulation, Political
**Developer:** Rogue World Team
**Status:** In Development (Early Access planned)
**Price:** Free (prototype) → $19.99 (Early Access)
**Platforms:** Windows, Linux, macOS
**Languages:** English (UI)

---

## Short Description

A grand strategy game where you guide a nation through the modern era as a leader with a unique career path. Deep interconnected systems, deterministic simulation, 180+ countries.

---

## Detailed Description

### What is Rogue World?

Rogue World is a **grand strategy game** inspired by Hearts of Iron IV, but set in the **modern world (2026+)**. You don't just play as a country — you play as a **character** who rises through the ranks to lead a nation.

Every decision matters. Every system interconnects. The simulation runs on **deterministic logic** — no hidden RNG, no "lucky rolls." What you see is what you get, and you can verify it yourself with save/load A==B testing.

### Two Scenarios

**Modern World (Default)** — 2026 start
- 50 detailed countries with real-world data (population, GDP, military, nuclear, resources)
- Modern technology tree (Era 1990-2020)
- Active factions: NATO, EU, BRICS, SCO
- Climate change, migration crises, space race, nuclear diplomacy

**Historical 1936** — Classic grand strategy
- 8 focus trees, 73 focuses, 11 historical events
- WWII-era technology and equipment
- For players who want the classic experience

### Core Systems

| System | Depth |
|--------|-------|
| **Economy** | 17 indicators with formula linkages (GDP, inflation, unemployment, corruption, Gini, education, health, life expectancy, public debt, budget deficit, energy, environment, media freedom, judicial independence, crime rate, population growth, urbanization) |
| **Technology** | 73 techs, 9 categories, research slots, variant designer for tanks/planes/ships |
| **Military** | 17 battalion types, division templates, armies, province-based combat, frontlines, naval invasion, air superiority, morale, supply lines |
| **Diplomacy** | Relations (-100 to +100), factions, guarantees, world tension, peace conferences, sanctions, trade wars, agreements |
| **Politics** | 20 constitutions, 9 laws, parliament with parties/seats/voting, authoritarian mechanics, cabinet ministers with traits |
| **Espionage** | 4 mission types (Intel/TechTheft/Sabotage/Propaganda), agent traits/affinity, capture risk, training, rental pool |
| **Space** | 10 missions (Rocketry → Mars), budget/retries, prestige/tech/military rewards |
| **Nuclear** | Full program: enrichment → weaponization → doctrine → use, AI behavior |
| **Climate** | Global CO2/warming, 23 country profiles, 9 policies, 3 agreements, tipping points, COP meetings |
| **Migration** | 4 border policies, 4 crisis types, weighted distribution, assimilation/return |
| **Disasters** | 7 types (earthquake, flood, drought, wildfire, storm, volcano, pandemic), intervention choices, event chains |
| **Organizations** | 9 international orgs (NATO, EU, UN, BRICS, SCO, AU, ASEAN, Arab League, OPEC) with membership, voting, bonuses |

### Character & Career

**Create Your Leader:**
- 7 Attributes: Diplomacy, Administration, Intelligence, Charisma, Military, Economy, Law
- 5 Backgrounds: Politician, Military, Academic, Activist, Business
- Procedural layered portrait (face, hair, colors, clothing)
- Age, health, family, succession

**Career Paths:**
- **Sandbox:** Start directly as leader
- **Career:** University → Party youth → Local office → Parliament → Minister → Leader
- **Military:** Academy → Ranks → Commands → General Staff → Coup option (risky!)

### UI & Quality of Life

- 18 responsive panels (all ≤1152×648)
- Consistent theming: GamePanel, TabBar, SearchBox, Chip, StatBar, ListRow, TooltipBuilder, EmptyState
- HOI4-style decision panel with category tabs, search, tooltips
- Mini-map always visible (bottom-right)
- Country library/encyclopedia (50 detailed entries)
- English UI, Turkish code comments

### Deterministic & Tested

- Custom xorshift64* RNG — absolutely no `new Random()`
- Save/Load A==B verification passes 10-year soak test
- 70+ automated headless tests (all passing)
- Clean build: 0 errors / 0 warnings
- Performance: ~17-21ms frame at 5x speed, <55% CPU, <25MB/panel

### Development Status

**Completed (v1.0-beta):**
- All core systems implemented and tested
- Both scenarios playable
- All 18 UI panels migrated to new component system
- 70/70 automated tests passing
- Export presets configured for Windows/Linux/macOS
- CI/CD pipeline ready

**Planned for v1.0:**
- War balance tuning
- AI coverage expansion (all 180 countries)
- Sound assets integration (CC0 music/SFX)
- Steam/itch.io store pages finalized
- Localization (DE, FR, ES, RU, ZH, TR)
- Mod support via Steam Workshop

---

## Screenshots Needed

1. Main menu
2. World map (country selection)
3. Country info panel
4. Decision panel
5. Diplomacy panel
6. Statistics panel
7. Technology tree
8. Army/Frontline panel
9. Cabinet panel
10. Election panel
11. Climate panel
12. Espionage panel
13. Space program panel
14. Monthly report
15. Character creation
16. Library panel
17. Game in action (map + HUD + mini-map)
18. Peace conference

---

## Trailer / GIF Ideas

- Character creation → country selection → first week agenda
- War declaration → frontline movement → peace conference
- Election campaign → results → new cabinet
- Disaster strikes → intervention choices → recovery
- Space mission launch → success → tech bonus
- Nuclear program progression → test → doctrine

---

## System Requirements

**Minimum:**
- OS: Windows 10 64-bit / Ubuntu 20.04+ / macOS 12+
- Processor: Intel i5-6400 / AMD Ryzen 3 1200
- Memory: 8 GB RAM
- Graphics: Integrated (Intel UHD 630 / AMD Vega 8) or 2GB VRAM discrete
- Storage: 2 GB

**Recommended:**
- OS: Windows 11 / Ubuntu 22.04+ / macOS 13+
- Processor: Intel i7-10700 / AMD Ryzen 7 3700X
- Memory: 16 GB RAM
- Graphics: NVIDIA RTX 3060 / AMD RX 6600 XT (4GB+ VRAM)
- Storage: 4 GB SSD

---

## Pricing & Distribution

**Current (Prototype):** Free download
**Early Access:** $19.99
**Full Release:** $29.99

**Revenue Split:** Standard itch.io (10% default, adjustable)

**Updates:** Free for all purchasers

---

## Tags

grand-strategy, simulation, political, management, singleplayer, replay-value, deep-systems, modern-setting, character-customization, deterministic, godot, open-source-friendly

---

## Press / Contact

- Repository: https://github.com/[username]/Rogue-World
- Issues/Feedback: GitHub Issues
- Dev Logs: GECE-RAPOR.md in repo
- Changelog: CHANGELOG.md in repo

---

## itch.io Page Setup Checklist

- [ ] Page created at itch.io
- [ ] Cover image (630x500px) uploaded
- [ ] Screenshots (min 5) uploaded
- [ ] GIF/Video trailer uploaded
- [ ] Description copied from this document
- [ ] Tags added
- [ ] System requirements filled
- [ ] Price set (Free for prototype)
- [ ] Downloadable files: Windows .zip, Linux .tar.gz, macOS .zip
- [ ] Devlog posts linked (GitHub releases)
- [ ] Community options configured (comments, feedback)
- [ ] Analytics enabled
- [ ] Custom CSS/theme applied (optional)
- [ ] Press kit page created
- [ ] Newsletter signup considered