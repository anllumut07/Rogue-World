# Rogue World — QA Plan

**Version:** 1.0  
**Branch:** `gece-calisma`  
**Date:** 2026-11-17

---

## Test Classification

### Test Types

| Type | Description | Automation | Tools |
|------|-------------|------------|-------|
| **Logic** | Business rules, calculations, state transitions | Unit tests + Headless hooks | `dotnet test`, `--...-test` |
| **Integration** | Cross-system interactions, save/load, data flow | Headless hooks | `--save-load-test`, `--scenario-test` |
| **Visual** | Layout, styling, responsiveness, animations | Manual + Screenshot diff | Godot Editor, `--ui-screenshot` |
| **UI/Interaction** | Button clicks, tab switching, search, drag-drop | Semi-auto (scripted) | Custom test script |
| **Performance** | Frame time, memory, GC, load times | Profiling | `dotnet-trace`, `dotnet-counters` |
| **Regression** | Previously fixed bugs don't reappear | Automated | Full test suite |

---

## Test Matrix — 18 Panels

### DiplomacyPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| DIP-01 | Logic | World Tension bar updates correctly | Bar value = `DiplomacySystem.WorldTension`, color changes at thresholds | Headless |
| DIP-02 | Logic | Target country selector populates all non-annexed countries | Dropdown has N-1 items (excl player) | Headless |
| DIP-03 | Logic | Relation/Opinion display correct | Matches `DiplomacySystem.GetRelationData()` | Headless |
| DIP-04 | Integration | Tab switching (Factions/Actions/Guarantees/Relations) | Content updates, search visible only on Relations | Manual/Script |
| DIP-05 | UI | SearchBox filters Relations tab | Result count updates, filtered list shown | Script |
| DIP-06 | UI | War justification checklist shows ✓/✗ | Matches `CasusBelliSystem.CheckWarPreconditions()` | Headless |
| DIP-07 | UI | BUILD CASE buttons work | Starts justification, refreshes | Script |
| DIP-08 | UI | DECLARE WAR enabled only when `CanDeclare` | Button disabled with tooltip when blocked | Script |
| DIP-09 | UI | REQUEST RECOGNITION shows for newborn unrecognized | Only when `RecognitionSystem.WasBorn()` and not recognized | Headless |
| DIP-10 | UI | Guarantee/MilAccess/NavAccess buttons toggle | State changes, refreshes | Script |
| DIP-11 | UI | IMPROVE RELATIONS starts daily PP spend | `DiplomacySystem.ImproveRelations()` returns true | Script |
| DIP-12 | Visual | Faction cards show leader, members, correct colors | `Chip.CreateCategory` with faction color | Manual |
| DIP-13 | Visual | Guarantee rows show flag, name, REVOKE button | `FlagBox`, `Chip`, `GameTheme.StyleButton` | Manual |
| DIP-14 | Visual | Relation rows use `ListRow` with flag, accent color | Green/Red/Yellow based on relation value | Manual |
| DIP-15 | Interaction | Click relation row → switches to Actions tab, selects target | `_tabBar.SelectedIndex = 1`, target dropdown updated | Script |
| DIP-16 | Integration | Panel data refreshes on `PlayerCountryDataChanged` | `RefreshAll()` called | Headless |
| DIP-17 | Regression | ESC closes panel | `Close()` called, `IsOpen = false` | Script |
| DIP-18 | Regression | Backdrop click closes panel | `MouseFilter.Stop` on backdrop | Script |

### ClimatePanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| CLM-01 | Logic | Global tab shows correct CO₂, Warming, Emissions | Matches `ClimateSystem.GetGlobalCO2()` etc. | Headless |
| CLM-02 | Visual | WarmingChartControl draws chart | `ClimateSystem.GetHistory()` data rendered | Manual/Screenshot |
| CLM-03 | Logic | Tipping points progress bars correct | `progress = max(CO2/threshold, Warming/threshold)` | Headless |
| CLM-04 | UI | Emissions tab table sortable by search | `SearchBox` filters country name/ID | Script |
| CLM-05 | UI | Policy cards show category Chip, cost, effects | `Chip.CreateCategory`, `TooltipBuilder` | Manual |
| CLM-06 | UI | ENACT/REVOKE buttons work | `ClimateSystem.EnactPolicy/RevokePolicy` | Script |
| CLM-07 | UI | Agreements tab shows Signed/Available | `ClimateSystem.GetCountryState().SignedAgreements` | Headless |
| CLM-08 | UI | SIGN/WITHDRAW buttons work | `ClimateSystem.SignAgreement/WithdrawAgreement` | Script |
| CLM-09 | Visual | Tipping Points cards show status, thresholds, progress | `ProgressBar` with correct color | Manual |
| CLM-10 | Regression | Tab switching works | Content rebuilds, search visibility toggles | Script |

### MigrationPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| MIG-01 | Logic | Overview metrics match `MigrationSystem` state | Global refugees, crises, player refugees, pending | Headless |
| MIG-02 | UI | Border policy cards show category Chip, effects | `Chip.CreateCategory`, `GetCategoryColor()` | Manual |
| MIG-03 | UI | Policy change buttons enable/disable correctly | `MigrationSystem.CanSetBorderPolicy()` | Script |
| MIG-04 | UI | Crisis summary items show intensity bar | `ProgressBar` with `crisis.Intensity * 100` | Manual |
| MIG-05 | UI | Crises tab table with search | `SearchBox` filters source/type | Script |
| MIG-06 | UI | Flows tab table with search | `SearchBox` filters source/dest/cause | Script |
| MIG-07 | UI | Policies tab shows current + available with SET button | `MigrationSystem.CanSetBorderPolicy()` | Script |
| MIG-08 | UI | Aid packages show ACTIVE/AVAILABLE with ACTIVATE | `MigrationSystem.CanActivateAidPackage()` | Script |
| MIG-09 | Visual | Crisis type badges use correct colors | War=Red, CivilWar=Purple, Climate=Blue, Persecution=Pink | Manual |
| MIG-10 | Regression | Tab switching works | Content rebuilds, search visibility toggles | Script |

### AdvisorPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| ADV-01 | Logic | Active tab shows 3 advisor slots (Economy/Security/Media) | `AdvisorSystem.GetActiveAdvisor(type)` | Headless |
| ADV-02 | UI | Active advisor cards show FIRE + REQUEST ADVICE | Buttons call `AdvisorSystem.FireAdvisor/RequestAdvice` | Script |
| ADV-03 | UI | Cooldown indicator shows when `GetCooldownDaysRemaining > 0` | Label with "X day(s) remaining" | Manual |
| ADV-04 | UI | Available tab lists only unfilled types | Filtered by `activeTypes.Contains(p.Type)` | Headless |
| ADV-05 | UI | SearchBox filters available advisors | Name/trait/culture/expertise | Script |
| ADV-06 | UI | HIRE button works, moves advisor to Active | `AdvisorSystem.HireAdvisor()` | Script |
| ADV-07 | UI | History tab shows last 20 advice entries | `AdvisorSystem.GetAdviceHistory(20)` | Headless |
| ADV-08 | Visual | Urgent advice shows URGENT Chip (Danger variant) | `Chip.CreateStatus("URGENT", Chip.Variant.Danger)` | Manual |
| ADV-09 | Visual | Confidence label color: Orange if urgent, Gold if normal | `advice.IsUrgent ? GameTheme.Orange : GameTheme.Gold` | Manual |
| ADV-10 | Regression | Tab switching works | Content rebuilds, search visibility toggles | Script |

### CountryInfoPanel (Left-side Overlay)

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| CIN-01 | Logic | Shows correct country data when clicked | `CountryDatabase.Get(countryId)` | Headless |
| CIN-02 | UI | War justification checklist ✓/✗ with Success/Danger Chip | `CasusBelliSystem.CheckWarPreconditions()` | Manual |
| CIN-03 | UI | DECLARE WAR / BUILD CASE / PLAY AS buttons styled | `GameTheme.StyleButton()` | Manual |
| CIN-04 | Visual | Layout fits left side, doesn't overlap HUD | Position/Size constraints | Manual |
| CIN-05 | Interaction | ESC triggers `CloseRequested`, panel closes | `CloseRequested = true`, `Close()` | Script |

### StatisticsPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| STA-01 | Logic | 17 indicators in OptionButton selector | `StatisticsSystem` indicators | Headless |
| STA-02 | UI | ProgressBar for each indicator uses `GameTheme.ProgressBarStyles()` | Gold accent, correct value | Manual |
| STA-03 | UI | GraphControl shows 60-day trend | Existing component, updates on selection | Manual |
| STA-04 | Visual | Indicators grouped by category | Economy/Military/Politics/Diplomacy/Social/Environment/Energy | Manual |
| STA-05 | Regression | ESC closes panel | `Close()` | Script |

### CabinetPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| CAB-01 | Logic | Shows 12 ministries with ministers | `CabinetSystem.GetMinisters()` | Headless |
| CAB-02 | UI | Minister status badges use Chip | Economy/Security/Media types | Manual |
| CAB-03 | UI | Loyalty/progress bars use `GameTheme.ProgressBarStyles()` | | Manual |
| CAB-04 | Regression | Test accessors preserved | `--cabinet-test` PASS | Headless |

### ElectionPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| ELE-01 | Logic | Country selector (OptionButton) works | Populates all countries | Headless |
| ELE-02 | UI | Party support bars use `GameTheme.ProgressBarStyles()` | | Manual |
| ELE-03 | UI | Election status badges use Chip | | Manual |
| ELE-04 | Regression | Test accessors preserved | `--election-test` PASS | Headless |

### FormablePanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| FRM-01 | Logic | Formable nations list with conditions | `FormableSystem.GetAllFormables()` | Headless |
| FRM-02 | UI | Condition checklist uses Chip (Success/Danger) | `FormableSystem.EvaluateConditions()` output | Manual |
| FRM-03 | Regression | Test accessors preserved | `--formable-test` PASS | Headless |

### ReleasablePanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| REL-01 | Logic | Releasable countries list | `ReleasableSystem.GetAllReleasables()` | Headless |
| REL-02 | UI | Detail rows use Chip | | Manual |
| REL-03 | Regression | Test accessors preserved | `--releasable-test` PASS | Headless |

### SeaZonePanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| SEZ-01 | Logic | 12 sea zones with supremacy data | `NavalSystem.GetSeaZones()` | Headless |
| SEZ-02 | UI | Supremacy bars use `GameTheme.ProgressBarStyles()` | | Manual |
| SEZ-03 | UI | Protector selector (OptionButton) works | | Script |
| SEZ-04 | Regression | Test accessors preserved | `--seazone-test` PASS | Headless |

### AgendaPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| AGD-01 | Logic | Active actions show ProgressBar | `TimeTempoSystem.GetActiveActions()` | Headless |
| AGD-02 | UI | Slot selection + action cards work | `AgendaSystem` integration | Script |
| AGD-03 | Regression | Test accessors preserved | `--agenda-test` PASS | Headless |

### MonthlyReportPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| MRP-01 | Logic | Shows stats summary, warnings, decisions | `MonthlyReportSystem.GetReport()` | Headless |
| MRP-02 | UI | CONTINUE button only (Closable=false) | Must press CONTINUE, no ESC close | Script |
| MRP-03 | UI | Status indicators use Chip | | Manual |
| MRP-04 | Regression | Auto-pause works | `GameManager.Pause()` called | Script |

### CultureLegendPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| CLG-01 | Logic | Culture items with population share bars | `CultureSystem.GetCultureLegendData()` | Headless |
| CLG-02 | UI | Chip for culture items, ProgressBar for share | | Manual |
| CLG-03 | Visual | MapMode.Culture highlights on row hover | `WorldMap.SetMapMode(MapMode.Culture)` | Manual |
| CLG-04 | Regression | Test accessors preserved | `--culture-legend-test` PASS | Headless |

### LibraryPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| LIB-01 | Logic | 50 countries loaded, search works | `LibrarySystem.GetAllEntries()` | Headless |
| LIB-02 | UI | Country page shows name, capital, region, languages, intro, live stats | `CountryDatabase` + `LibrarySystem` | Manual |
| LIB-03 | UI | SimulateClickForTest(index) works for headless | `--library-test` PASS | Headless |
| LIB-04 | Regression | Test accessors preserved | `--library-test` PASS | Headless |

### MiniMapPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| MNP-01 | Visual | World polygons drawn, camera rect visible | `WorldMap` polygons scaled | Manual |
| MNP-02 | Interaction | Click on minimap moves camera | `WorldMap.SetCameraTarget()` | Script |
| MNP-03 | Interaction | +/- zoom buttons work | `WorldMap.AdjustZoom()` | Script |
| MNP-04 | Visual | GameTheme colors used | Dark bg, gold accents | Manual |

### TooltipPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| TTP-01 | Logic | Shows after 0.22s delay | `DelaySeconds = 0.22f` | Manual |
| TTP-02 | Visual | Uses `GameTheme.TooltipStyle()` | Dark bg, gold border, shadow | Manual |
| TTP-03 | Interaction | Follows mouse, clamped to viewport | `GetViewport().GetMousePosition()` | Manual |
| TTP-04 | Interaction | `MouseFilter.Ignore` — doesn't block input | Map input works under tooltip | Manual |

### TutorialPanel

| Test ID | Type | Description | Expected | Automation |
|---------|------|-------------|----------|------------|
| TUT-01 | Logic | Shows on first run, persisted via SettingsSystem | `SettingsSystem.TutorialSeen` | Headless |
| TUT-02 | UI | RichTextLabel with BBCode formatting | Bold, bullets render | Manual |
| TUT-03 | UI | "Don't show again" CheckBox persists | `SettingsSystem.SetTutorialSeen(true)` | Script |
| TUT-04 | Visual | GamePanel base, CONTINUE button styled | `Closable=true`, custom button text | Manual |
| TUT-05 | Regression | Test accessors work | `DesignedSize`, `TitleText`, `TipsText`, `DontShowChecked`, `CloseRequested` | Headless |

---

## Automated Test Scripts

### Headless Test Suite (Run via `tools/run-test.ps1`)

```powershell
# Full regression suite
--save-load-test --scenario=modern_world
--save-load-test --scenario=historical_1936
--scenario-test
--decisions-test
--espionage-mission-test
--recognition-test
--migration-test
--occupation-test
--civil-war-test
--advisor-test
--climate-test
--diplomacy-test
--war-test
--balance-test
--soak-test
```

### UI Smoke Test Script (Conceptual)

```csharp
// Pseudo-code for automated UI test
foreach (panel in AllPanels) {
    panel.OpenUnder(root);
    WaitForReady();
    VerifyTitleVisible();
    VerifySizeWithinLimits(1152, 648);
    if (panel.HasTabs) {
        foreach (tab in panel.Tabs) {
            tab.Click();
            VerifyContentLoaded();
        }
    }
    if (panel.HasSearch) {
        panel.SearchBox.SetText("test");
        VerifyFilterWorks();
        panel.SearchBox.Clear();
    }
    if (panel.HasButtons) {
        foreach (btn in panel.Buttons) {
            btn.Click();
            VerifyActionExecuted();
        }
    }
    panel.Close();
    WaitForClose();
}
```

---

## Performance Profiling Procedure

### 1. Baseline Measurement
```bash
# Start profiling
dotnet-trace collect --process-id <PID> --duration 00:02:00 --format speedscope

# Or with counters
dotnet-counters monitor --process-id <PID> --counters System.Runtime
```

### 2. Exercise Each Panel
- Open each of the 18 panels
- Perform typical interactions (click tabs, search, click buttons)
- Close panel
- Repeat 3x per panel

### 3. Analyze Results
- Open `.speedscope` file in https://www.speedscope.app/
- Look for:
  - Hot methods (> 5% CPU)
  - GC allocations during UI operations
  - Layout passes (`Control._LayoutChild` etc.)
  - Texture draws (`CanvasItem._Draw`)

### 4. Target Metrics
| Operation | Target |
|-----------|--------|
| Panel open (first time) | < 100 ms |
| Panel open (cached) | < 50 ms |
| Tab switch | < 16 ms |
| Search filter (100 items) | < 16 ms |
| Button click response | < 16 ms |
| Save game | < 500 ms |
| Load game | < 1000 ms |

---

## Visual Regression Testing

### Screenshot Comparison
```bash
# Generate baseline screenshots (run once)
dotnet run --project RogueWorld -- --ui-screenshot=all --output-dir Screenshots/Baseline

# Compare on CI
dotnet run --project RogueWorld -- --ui-screenshot=all --output-dir Screenshots/Current
# Use imagemagick compare or pixelmatch for diff
```

### Test Resolutions
- 1152×648 (design baseline)
- 1920×1080 (common)
- 2560×1440 (high DPI)
- 3840×2160 (4K)

### DPI Scaling Test
- Windows 100%, 125%, 150%, 200%
- Verify no clipping, text readable, layout intact

---

## Test Data Management

### Save Files for Testing
| File | Scenario | Description |
|------|----------|-------------|
| `test_save_clean.json` | modern_world | Fresh game, day 1 |
| `test_save_midgame.json` | modern_world | ~5 years, wars, crises |
| `test_save_lategame.json` | modern_world | ~20 years, nukes, climate |
| `test_save_historical.json` | historical_1936 | 1936 start, focus trees |

### Test Country Selections
- USA (superpower, NATO leader)
- CHN (BRICS leader, high emissions)
- DEU (EU member, medium)
- BRA (developing, Amazon tipping point)
- AFG (minor, crisis source)

---

## CI/CD Integration

### GitHub Actions Workflow (Conceptual)
```yaml
name: QA Pipeline
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
      - name: Build
        run: dotnet build
      - name: Headless Tests
        run: |
          powershell -File tools/run-test.ps1 --save-load-test --scenario=modern_world
          powershell -File tools/run-test.ps1 --save-load-test --scenario=historical_1936
          powershell -File tools/run-test.ps1 --scenario-test
          # ... all tests
      - name: Upload Test Logs
        uses: actions/upload-artifact@v4
        with:
          name: test-logs
          path: test-logs/
```

---

## Release Criteria

### Must Pass (Blocking)
- [ ] All 18 panels pass Visual QA
- [ ] All headless tests PASS (exit code 0)
- [ ] Save/Load A==B both scenarios
- [ ] Performance: no panel > 16ms frame time
- [ ] No memory leaks detected in 10-year soak
- [ ] No `new Random()` usage (DeterministicRNG only)

### Should Pass (Non-blocking)
- [ ] Accessibility checks pass
- [ ] Localization review clean
- [ ] Documentation updated

### Nice to Have
- [ ] Automated UI smoke test runs in CI
- [ ] Screenshot regression testing in CI
- [ ] Performance benchmarks tracked over time

---

## Sign-off

| Test Type | Owner | Date | Status |
|-----------|-------|------|--------|
| Logic/Integration | | | |
| Visual/UI | | | |
| Performance | | | |
| Regression | | | |

---

## Appendix: Test Commands Quick Reference

```bash
# Build
dotnet build

# Headless tests (run sequentially)
powershell -File tools/run-test.ps1 --save-load-test --scenario=modern_world
powershell -File tools/run-test.ps1 --save-load-test --scenario=historical_1936
powershell -File tools/run-test.ps1 --scenario-test
powershell -File tools/run-test.ps1 --decisions-test
powershell -File tools/run-test.ps1 --espionage-mission-test
powershell -File tools/run-test.ps1 --recognition-test
powershell -File tools/run-test.ps1 --migration-test
powershell -File tools/run-test.ps1 --occupation-test
powershell -File tools/run-test.ps1 --civil-war-test
powershell -File tools/run-test.ps1 --advisor-test
powershell -File tools/run-test.ps1 --climate-test
powershell -File tools/run-test.ps1 --diplomacy-test
powershell -File tools/run-test.ps1 --war-test
powershell -File tools/run-test.ps1 --balance-test

# Performance profiling
dotnet-trace collect --process-id <PID> --duration 00:05:00 --format speedscope

# Godot headless import
godot --headless --import

# Editor play test
godot --editor --path .
# Press F5 in editor
```