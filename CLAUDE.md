# Rogue World — CLAUDE.md (Project Instructions)

## Build / Run / Test Commands

| Task | Command |
|------|---------|
| **Build** | `dotnet build` (must be 0 errors / 0 warnings) |
| **Headless Import** | `godot --headless --import` (Godot path: `C:\Users\rogue\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`) |
| **Run Test** | `powershell -File tools\run-test.ps1 --<test-name>` |
| **Key Tests** | `--save-load-test` (A==B determinism), `--espionage-mission-test`, `--scenario-test`, `--soak-test`, `--balance-test`, `--occupation-test`, `--civil-war-test`, `--recognition-test` |
| **Run All Tests** | Run each `--...-test` flag sequentially; all must exit code 0 |
| **Editor Play** | Open Godot editor → F5 (manual visual/interaction test) |

**Godot executable is fixed at:** `C:\Users\rogue\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`  
**Do NOT say "Godot not installed / headless failed" — it works.**

## Code Conventions

- **Comments:** Turkish
- **UI Text:** English
- **Debug Logs:** `GD.Print` with `[SystemName]` prefix
- **RNG:** ONLY `DeterministicRNG` (xorshift64*). `new Random()` FORBIDDEN
- **Save/Load:** Every new system implements `GetSaveData()` / `LoadSaveData()` + DTO in `SaveSystem.cs` + registration in `GameManager.ResetForNewGame()` + summary hash in `SaveLoadTest.BuildSummaryCore()`
- **File Encoding:** BOM-less UTF-8 (all .cs, .json, .yaml)
- **JSON Loading:** Always use `IncludeFields = true` in `JsonSerializerOptions`
- **Log Load Counts:** Every system logs record count on startup (`GD.Print($"[System] X records loaded")`)
- **Mouse/Input:** Never break: right-drag pan, wheel zoom, WASD, country click. Panels ≤ 1152×648, `MouseFilter.Stop` backdrop when open, release on close.
- **Test Code:** `--<name>-test` hooks only; never run in normal gameplay.

## Git Rules

- Commit after each meaningful module/fix: `git commit -m "feat: descriptive message" && git push origin gece-calisma`
- **NEVER `git push --force`** — stop and ask user if needed
- No keys, `.env`, `config.yaml` secrets, or large binaries in commits
- Commit messages in English, descriptive

## PUSH RULE (Mandatory)

After EVERY completed milestone or stage, when build is 0 errors/0 warnings and all required tests pass:
- Update `PROJECT_STATE.md`
- `git add -A`
- Commit with a clear message
- `git push` to the current branch (`gece-calisma`)
- Never force-push, never push to main directly, never push if a test is failing
- If push fails, tell the user the error
- Also store the lesson in Ruflo memory (`--namespace patterns`)

## Session Start Protocol

**ALWAYS do this at the start of every session:**
1. Read `PROJECT_STATE.md` to understand current state
2. `git branch --show-current` → verify on `gece-calisma`
3. `git log --oneline -8` → recent history
4. `dotnet build` → verify clean build
5. Check `TODO.md` for next task (currently **M52.1**)

## Session End Protocol

After every completed task:
1. Update `TODO.md` — mark `[x]` for completed sub-tasks
2. Update `PROGRESS.md` and `GECE-RAPOR.md` with what was done
3. `dotnet build` → must pass
4. Run relevant `--...-test` → must PASS
5. `git add -A && git commit -m "feat: ..." && git push origin gece-calisma`
6. Every 2 sub-tasks: update `GECE-RAPOR.md`

## Current Focus

**Next Task: M52 — Recognition Consequences: Unrecognized State**
- 52.1: Recognition count → OrganizationSystem membership, trade income, AI alliance/guarantee eligibility
- 52.2: "REQUEST RECOGNITION" diplomatic action
- 52.3: Recognition effect on war tension / intervention probability
- 52.4: Save/Load + regression tests (A==B both scenarios)

## Key Files to Know

| File | Purpose |
|------|---------|
| `Scripts/GameManager.cs` | Root autoload, daily tick, test hooks, save/load orchestration |
| `Scripts/SaveSystem.cs` | All DTOs, file I/O, `SaveLoadTest` A==B verification |
| `Scripts/RecognitionSystem.cs` | M48.3 recognition logic (newborn country recognition) |
| `Scripts/OrganizationSystem.cs` | M37 international organizations (membership, bonuses) |
| `Scripts/DiplomacySystem.cs` | Relations, diplomatic actions, AI evaluation |
| `Scripts/CasusBelliSystem.cs` | War justifications, tension |
| `Scripts/InterventionSystem.cs` | Foreign intervention in civil wars |
| `TODO.md` | Master plan — source of truth for what to do next |
| `PROJECT_STATE.md` | This file — read at session start |

## Autonomous Mode Rules (when active)

- No questions, no approval waits — pick most reasonable default, rationale to `GECE-RAPOR.md`
- Continue to next sub-task immediately after completion (only stop if: all TODO done/blocked, 5 API errors, `STOP.txt` exists)
- Standard prohibitions still apply: no main push, no force push, no key files, no project-external deletion, no `dotnet run`
- On API error/429: `powershell -Command "Start-Sleep 60"` then retry

## Ruflo Integration Rules

- **Before any task:** `npx ruflo@latest memory search --query "<task keywords>"` to retrieve relevant knowledge.
- **Multi-step work:** `npx ruflo@latest task create` for each step, `npx ruflo@latest agent spawn -t coder|tester|reviewer` to delegate. Prefer Ruflo agents over native Task tool.
- **After task passes build + tests:** `npx ruflo@latest memory store --namespace patterns` with what worked, then update `PROJECT_STATE.md`.
- **Never** run `init --force`, clean, or anything that deletes `.claude/` without asking user first.