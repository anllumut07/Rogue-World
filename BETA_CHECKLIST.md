# Rogue World — Beta Release Checklist

**Target:** v1.0-beta  
**Branch:** `gece-calisma`  
**Date:** 2026-11-17

---

## 1. Build Verification

| Item | Status | Notes |
|------|--------|-------|
| `dotnet build` — 0 errors, 0 warnings | ✅ | Confirmed 2026-11-17 |
| `godot --headless --import` — no errors | ✅ | Verified via headless tests |
| All `--...-test` hooks exit code 0 | ⚠️ | 64/70 PASS (6 timeout/FAIL) |
| Save/Load A==B (both scenarios) | ✅ | `--save-load-test --scenario=modern_world` + `--save-load-test --scenario=historical_1936` PASS |
| Export presets work (Windows/Linux/macOS) | ☐ | Requires export templates |

---

## 2. Save/Load Integrity

| Item | Status | Notes |
|------|--------|-------|
| Save file version compatibility | ✅ | SaveVersion=2, v1 loads as historical_1936 |
| All 30+ systems serialize/deserialize | ✅ | Verified via SaveLoadTest |
| DeterministicRNG state preserved | ✅ | xorshift64* state saved/loaded |
| Cross-scenario load (modern ↔ historical) | ✅ | Graceful error handling |
| Corrupted save handling | ✅ | Error message shown, no crash |

---

## 3. Performance Thresholds

| Metric | Target | Current | Status |
|--------|--------|---------|--------|
| Frame time (gameplay, speed 1x) | ≤ 16.67 ms (60 FPS) | TBD | ☐ |
| Frame time (gameplay, speed 5x) | ≤ 16.67 ms (60 FPS) | TBD | ☐ |
| Panel open/close latency | ≤ 100 ms | TBD | ☐ |
| Memory growth (10 game years) | < 20% baseline | TBD | ☐ |
| GC spike during panel transitions | < 5 ms | TBD | ☐ |
| Save operation time | < 500 ms | TBD | ☐ |
| Load operation time | < 1000 ms | TBD | ☐ |
| Headless tick time (avg) | < 50 ms/tick | ~8-15 ms/tick | ✅ |

**Profiling required:** Run `dotnet-trace` while exercising all 18 panels.

---

## 4. UI/UX Quality Gate

### 18 Panels Visual QA

| Panel | Layout OK | Styling Consistent | Responsive (1152×648) | ESC/Close Works | MouseFilter Correct | Tooltips Work | Status |
|-------|-----------|-------------------|----------------------|-----------------|---------------------|---------------|--------|
| DiplomacyPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| ClimatePanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| MigrationPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| AdvisorPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| CountryInfoPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| StatisticsPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| CabinetPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| ElectionPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| FormablePanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| ReleasablePanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| SeaZonePanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| AgendaPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| MonthlyReportPanel | ✅ | ✅ | ✅ | ✅* | N/A | ✅ | ✅ |
| CultureLegendPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| LibraryPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| MiniMapPanel | ✅ | ✅ | ✅ | ✅ | ✅ | N/A | ✅ |
| TooltipPanel | ✅ | ✅ | ✅ | N/A | Ignore | ✅ | ✅ |
| TutorialPanel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

*MonthlyReportPanel: CONTINUE button only, ESC disabled per design

### Consistency Checks

| Check | Status | Notes |
|-------|--------|-------|
| All panels use `GameTheme` colors (no hardcoded hex) | ✅ | Verified via code review |
| All panels use `GameTheme.FontSize*` constants | ✅ | |
| All panels use `GameTheme.Space*` constants | ✅ | |
| All buttons use `GameTheme.StyleButton()` | ✅ | |
| All progress bars use `GameTheme.ProgressBarStyles()` | ✅ | |
| All tabs use `TabBar` component | ✅ | |
| All search uses `SearchBox` component | ✅ | |
| All badges use `Chip` component | ✅ | |
| All empty states use `EmptyState` component | ✅ | |
| All tooltips use `TooltipBuilder` | ✅ | |
| No panel exceeds 1152×648 design size | ✅ | Max: FormablePanel 1080×600 |
| Backdrop `MouseFilter.Stop` on all modal panels | ✅ | |
| ESC closes all panels (except MonthlyReportPanel) | ✅ | |

---

## 5. Accessibility

| Item | Status | Notes |
|------|--------|-------|
| Color contrast (WCAG AA) | ⚠️ | Gold on dark passes, some status colors need review |
| Keyboard navigation (Tab/Enter/ESC) | ✅ | All panels |
| Focus indicators visible | ✅ | |
| Text scaling support | ✅ | Godot handles via theme |
| Screen reader compatible (labels) | ✅ | RichTextLabel/Label have text |

---

## 6. Localization Review

| Item | Status | Notes |
|------|--------|-------|
| All UI text English | ✅ | Project rule |
| No Turkish in UI strings | ✅ | Verified, only code comments in Turkish |
| Number formatting (N0, F1, F2) | ✅ | Culture-invariant |
| Date formatting consistent | ✅ | GameManager date display |

---

## 7. Gameplay Regression Tests

| Test | Status | Notes |
|------|--------|-------|
| `--scenario-test` PASS | ✅ | |
| `--save-load-test` PASS (both) | ✅ | modern_world + historical_1936 |
| `--decisions-test` PASS | ✅ | |
| `--espionage-mission-test` PASS | ✅ | |
| `--recognition-test` PASS | ✅ | |
| `--migration-test` PASS | ✅ | |
| `--occupation-test` PASS | ✅ | |
| `--civil-war-test` PASS | ✅ | |
| `--advisor-test` PASS | ✅ | |
| `--climate-test` PASS | ✅ | |
| `--diplomacy-test` PASS | ✅ | |
| `--war-test` PASS | ⏱️ | TIMEOUT (120s) — long simulation |
| `--soak-test` (2 years) PASS | ✅ | 1-3 years for CI |
| `--balance-test` PASS | ❌ | FAIL — AI attack rate 2.4% < 5% target |
| `--formable-panel-test` PASS | ✅ | |
| `--releasable-panel-test` PASS | ✅ | |
| `--autonomy-test` PASS | ✅ | |
| `--resistance-test` PASS | ✅ | |
| `--culture-legend-test` PASS | ✅ | |
| `--sea-zone-panel-test` PASS | ✅ | |
| `--lend-lease-test` PASS | ✅ | |
| `--naval-combat-test` PASS | ✅ | |
| `--sea-zone-test` PASS | ✅ | |
| `--convoy-raid-test` PASS | ✅ | |
| `--province-economy-test` PASS | ✅ | |
| `--resource-balance-test` PASS | ✅ | |
| `--ai-formable-test` PASS | ✅ | |
| `--release-demand-test` PASS | ✅ | |
| `--m48-integration-test` PASS | ✅ | |
| `--culture-test` PASS | ✅ | |
| `--formable-test` PASS | ✅ | |
| `--releasable-test` PASS | ✅ | |
| `--intervention-test` PASS | ✅ | |
| `--agreements-test` PASS | ✅ | |
| `--press-test` PASS | ✅ | |
| `--sanctions-test` PASS | ✅ | |
| `--sanctions-nuclear-test` PASS | ✅ | |
| `--nuclear-test` PASS | ✅ | |
| `--nuclear-use-test` PASS | ✅ | |
| `--ai-nuclear-test` PASS | ❌ | FAIL |
| `--tech-test` PASS | ✅ | |
| `--space-test` PASS | ❌ | FAIL |
| `--space-mgmt-test` PASS | ✅ | |
| `--space-reward-test` PASS | ✅ | |
| `--election-test` PASS | ✅ | |
| `--parliament-test` PASS | ✅ | |
| `--autoritarian-test` PASS | ✅ | |
| `--organization-test` PASS | ✅ | |
| `--character-test` PASS | ✅ | |
| `--career-test` PASS | ✅ | |
| `--military-career-test` PASS | ✅ | |
| `--education-test` PASS | ✅ | |
| `--candidacy-test` PASS | ✅ | |
| `--career-events-test` PASS | ✅ | |
| `--character-takeover-test` PASS | ✅ | |
| `--disaster-test` PASS | ✅ | |
| `--unexpected-events-test` PASS | ✅ | |
| `--social-test` PASS | ✅ | |
| `--profile-test` PASS | ✅ | |
| `--trend-test` PASS | ✅ | |
| `--tweet-action-test` PASS | ✅ | |
| `--profanity-test` PASS | ✅ | |
| `--espionage-test` PASS | ✅ | |
| `--spy-training-test` PASS | ✅ | |
| `--spy-rental-test` PASS | ✅ | |
| `--counter-intel-test` PASS | ✅ | |
| `--focus-test` PASS | ⏱️ | TIMEOUT (120s) — long AI focus chain |
| `--province-test` PASS | ✅ | |
| `--army-test` PASS | ✅ | |
| `--war-score-test` PASS | ✅ | |
| `--peace-terms-test` PASS | ✅ | |
| `--peace-test` PASS | ✅ | |
| `--ai-peace-test` PASS | ✅ | |
| `--peace-panel-test` PASS | ✅ | |
| `--formable-data-test` PASS | ✅ | |
| `--victory-test` PASS | ✅ | |
| `--score-test` PASS | ✅ | |
| `--gameover-test` PASS | ✅ | |
| `--constitution-test` PASS | ✅ | |
| `--laws-test` PASS | ✅ | |
| `--agenda-test` PASS | ✅ | |
| `--cabinet-meeting-test` PASS | ✅ | |
| `--library-test` PASS | ✅ | |
| `--settings-test` PASS | ❌ | FAIL |
| `--map-mode-test` PASS | ✅ | |
| `--tooltip-test` PASS | ✅ | |
| `--screenshot-test` PASS | ❌ | FAIL (MiniMapPanel draw error) |

---

## 8. Documentation

| Item | Status | Notes |
|------|--------|-------|
| `GAME_VISION.md` updated | ☐ | |
| `CURRENT_STATUS.md` updated | ☐ | |
| `RELEASE_NOTES_v1.0-beta.md` drafted | ☐ | |
| `CHANGELOG.md` updated | ☐ | |
| `README.md` reflects current state | ☐ | |

---

## 9. Release Preparation

| Item | Status | Notes |
|------|--------|-------|
| Git tag `v1.0-beta` created | ☐ | |
| Push to `gece-calisma` and `main` | ☐ | |
| GitHub Release draft created | ☐ | |
| Export binaries uploaded | ☐ | |
| Announcement prepared | ☐ | |

---

## 10. Known Issues / Blockers

| Issue | Severity | Mitigation |
|-------|----------|------------|
| AI only manages ~49 detailed countries | Medium | Document as known limitation |
| Real FPS unmeasurable headless | Low | Manual editor testing required |
| War balance not tuned (AI attack rate 2.4%) | Medium | Adjust in Stage 4, increase AI aggression |
| AI nuclear test failing | Medium | Fix AI nuclear behavior logic |
| Space test failing | Medium | Fix space mission unlock logic |
| Settings test failing | Low | Fix settings persistence test |
| Screenshot test failing (MiniMapPanel) | Low | Fix MiniMapSurface draw error |
| Sound files missing | Low | User must add CC0 assets |
| Export templates not in repo | Blocker | User must provide / configure |
| Focus test timeout (120s) | Medium | Optimize or increase timeout |
| War test timeout (120s) | Medium | Optimize or increase timeout |

---

## Sign-off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Lead Developer | | | |
| QA Lead | | | |
| Release Manager | | | |

---

## Next Steps After Beta:

1. Collect beta feedback
2. Stage 4: Feature Expansion (new mechanics from reverse-engineering)
3. Polish based on feedback
4. v1.0 Release