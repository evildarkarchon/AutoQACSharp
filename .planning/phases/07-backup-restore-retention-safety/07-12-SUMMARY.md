---
phase: 07-backup-restore-retention-safety
plan: 12
subsystem: ui
tags: [progress-window, lifecycle, mvvm, defense-in-depth, regex-tests, gap-closure, tdd]

# Dependency graph
requires:
  - phase: 07-backup-restore-retention-safety
    provides: backup/retention progress visibility (D-09) and non-xEdit cancel affordances (D-11) from Plans 07-05 / 07-06; trusted-restore-root contract from 07-11
provides:
  - explicit normal-path ShowProgressAsync wiring of ProgressViewModel.CloseRequested -> progressWindow.Close()
  - normal-path progressWindow.Closed -> ProgressViewModel.Dispose() with idempotent local guard
  - regex-based source-level regression coverage for the normal cleaning progress window lifecycle
  - in-code "Defense in depth" documentation describing the dual-subscription contract with ProgressWindow.axaml.cs
affects: [main-window, progress-window-lifecycle, phase-07-verification]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - regex-tolerant source-level lifecycle assertions (lambda OR method-group syntax, alternate guard variable names)
    - local idempotent dispose guard inside Avalonia interaction handlers
    - dual-subscription defense-in-depth between MainWindow.ShowProgressAsync and ProgressWindow.OnDataContextChanged + OnClosed

key-files:
  created:
    - .planning/phases/07-backup-restore-retention-safety/07-12-SUMMARY.md
  modified:
    - AutoQAC/Views/MainWindow.axaml.cs
    - AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs

key-decisions:
  - "Regression coverage for the normal ShowProgressAsync lifecycle uses Regex.IsMatch (lambda OR method-group syntax, alternate guard variable names) so future refactors that preserve the contract do not break the test."
  - "Add explicit normal-path CloseRequested + Closed wiring in ShowProgressAsync as defense-in-depth alongside the pre-existing ProgressWindow.OnDataContextChanged + OnClosed contract; document the dual subscription with a literal `// Defense in depth` comment so future readers do not deduplicate the wiring."
  - "Use a local `var progressDisposed = false` guard plus a local `DisposeProgressViewModel()` function so disposal stays idempotent if ProgressWindow.OnClosed -> DisposeViewModelIfNeeded already disposed first."

patterns-established:
  - "Source-level Avalonia lifecycle regression tests assert behavior via regex instead of exact substrings to tolerate equivalent C# syntax variants."
  - "ShowProgressAsync uses an idempotent local function for disposal so the dual-subscription with ProgressWindow remains safe if either contract changes."
  - "Defense-in-depth code comments are an asserted artifact -- the literal phrase is the contract that prevents accidental deduplication."

requirements-completed: [PERF-04, TEST-04]

# Metrics
duration: 3 min
completed: 2026-04-29
---

# Phase 07 Plan 12: Normal Progress Window Lifecycle Gap Closure Summary

**MainWindow.ShowProgressAsync now explicitly wires `ProgressViewModel.CloseRequested -> progressWindow.Close()` and `progressWindow.Closed -> ProgressViewModel.Dispose()` with an idempotent local guard, documented in a `// Defense in depth` comment alongside the pre-existing `ProgressWindow.axaml.cs` cleanup contract, and is regression-covered by a regex-tolerant source-level test.**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-04-29T11:01:43Z
- **Completed:** 2026-04-29T11:05:00Z
- **Tasks:** 2 (RED + GREEN)
- **Files modified:** 2

## Accomplishments

- Added `MainWindowShowProgressAsync_ShouldWireProgressWindowCloseAndDisposal` to `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs` using six regex-tolerant assertions for the normal-path lifecycle wiring plus a literal `Defense in depth` substring assertion for the dual-subscription contract documentation.
- Added `using System.Text.RegularExpressions;` to the test file so existing source-inspection tests retain their substring style while the new test uses `Regex.IsMatch`.
- Wired `progressViewModel.CloseRequested += (_, _) => progressWindow.Close();` in `MainWindow.ShowProgressAsync` so the result Close button closes the normal progress window (mirrors the preview path).
- Wired `progressWindow.Closed += (_, _) => DisposeProgressViewModel();` plus a local `var progressDisposed = false;` guard and `DisposeProgressViewModel()` local function so disposal happens exactly once even if `ProgressWindow.OnClosed -> DisposeViewModelIfNeeded` already disposed first.
- Added the four-line `// Defense in depth: ...` comment block above the local guard explaining why both `ShowProgressAsync` and `ProgressWindow.OnDataContextChanged` / `OnClosed` subscribe to the same `CloseRequested` and `Closed` events.
- Verified `ProgressWindow.axaml.cs`, `ShowPreviewAsync`, and the rest of `MainWindow.axaml.cs` are unchanged.

## Task Commits

Each TDD gate was committed atomically:

1. **Task 1 RED: failing normal progress window lifecycle test** - `dd6c3b9` (test)
2. **Task 2 GREEN: wire normal progress window close and disposal** - `ff5622b` (feat)

REFACTOR gate not needed; the GREEN code already matches the planned shape and no behavior-neutral cleanup is required.

**Plan metadata commit** is created separately after STATE/ROADMAP updates.

## Files Created/Modified

- `AutoQAC/Views/MainWindow.axaml.cs` - `ShowProgressAsync` gains the defense-in-depth comment block, `progressDisposed` local guard, `DisposeProgressViewModel` local function, `progressViewModel.CloseRequested += (_, _) => progressWindow.Close();`, and `progressWindow.Closed += (_, _) => DisposeProgressViewModel();` -- all inserted after the window construction and before `progressWindow.Show(this)`.
- `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs` - Adds `MainWindowShowProgressAsync_ShouldWireProgressWindowCloseAndDisposal` with six assertions (five regex, one literal `Defense in depth` substring) plus the `using System.Text.RegularExpressions;` import; existing tests untouched.
- `.planning/phases/07-backup-restore-retention-safety/07-12-SUMMARY.md` - This document.

## Reviewer Consensus Documented in Code

Plan 07-12 incorporated cross-AI review feedback (`.planning/phases/07-backup-restore-retention-safety/07-REVIEWS.md`):

- **Defense-in-depth resolution.** Gemini read the dual subscription as defense in depth; the agent and Codex flagged a redundancy risk. Consensus adopted: keep both subscription paths, add a literal `// Defense in depth:` comment explaining why, and keep the local `progressDisposed` guard idempotent so a second `DisposeViewModel*()` short-circuits. This matches Avalonia's idempotent `Window.Close()` semantics where a second `Close()` is a no-op.
- **Regex tolerance for lifecycle assertions.** Gemini, the agent, and Codex all flagged the previously planned five-substring assertion as brittle. Resolution: replace exact substring matches with `Regex.IsMatch` patterns tolerant of method-group syntax (`Closed += OnProgressClosed;`) and alternate guard variable names (`disposed`, `cleaned`, `done`, `progressDisposed`). Only the `// Defense in depth` comment is asserted as a literal substring because that comment IS the contract.

## Runtime Behavior Already Partially Covered the Gap

`ProgressWindow.OnDataContextChanged` and `ProgressWindow.OnClosed -> DisposeViewModelIfNeeded` already subscribed to `CloseRequested` and disposed the ViewModel at runtime, which is why the normal cleaning result Close button worked in practice. The verifier flagged the gap because `ShowProgressAsync` itself did not contain the lifecycle wiring, so the contract depended entirely on `ProgressWindow.axaml.cs` -- a single-point-of-failure if the View contract changed in the future.

The new wiring is **additive** (defense-in-depth) and **idempotent** (local `progressDisposed` guard), so:

- If `ProgressWindow.OnClosed -> DisposeViewModelIfNeeded` runs first, `DisposeProgressViewModel()` short-circuits via `progressDisposed`.
- If `DisposeProgressViewModel()` runs first (e.g. ViewModel-driven close), `ProgressWindow.OnClosed -> DisposeViewModelIfNeeded` short-circuits via `_disposeHandled`.
- Avalonia's `Window.Close()` is idempotent, so the `CloseRequested -> Close()` and the user clicking the title-bar X both safely converge on the same disposal path.

## Decisions Made

- Did not modify `ProgressWindow.axaml.cs`. The existing `OnDataContextChanged + OnClosed -> DisposeViewModelIfNeeded` contract is intentionally retained as the inner safety layer.
- Did not modify `ShowPreviewAsync`. The preview path already had the `CloseRequested -> Close()` wiring; the gap was specifically in `ShowProgressAsync`.
- Did not change `ProgressViewModel` DI registration. It remains transient.
- Used a local function (`void DisposeProgressViewModel()`) instead of a captured lambda so the guard is named, debuggable, and reusable from both the `Closed` handler and any future call sites in the same scope.
- Kept the existing source-inspection testing style. There is no separate Avalonia.Headless test project in the solution and Plan 07-12 explicitly does not introduce one.

## Grep Evidence

| Acceptance check | Pattern | Result |
|------------------|---------|--------|
| Defense-in-depth comment in production | `Defense in depth` in `AutoQAC/Views/MainWindow.axaml.cs` | 1 match (line 142) |
| CloseRequested wiring in production (preview + new normal) | `progressViewModel\.CloseRequested\s*\+=` in `AutoQAC/Views/MainWindow.axaml.cs` | 2 matches (line 157 ShowProgressAsync, line 181 ShowPreviewAsync) |
| progressWindow.Closed wiring in production | `progressWindow\.Closed\s*\+=` in `AutoQAC/Views/MainWindow.axaml.cs` | 1 match (line 158, ShowProgressAsync only) |
| Local idempotent guard variable | `(var\|bool)\s+progressDisposed\s*=\s*false` in `AutoQAC/Views/MainWindow.axaml.cs` | 1 match (line 146) |
| DisposeProgressViewModel local function definition + invocation | `DisposeProgressViewModel` in `AutoQAC/Views/MainWindow.axaml.cs` | 2 matches (line 147 definition, line 158 invocation) |
| ProgressWindow.axaml.cs unchanged | `git diff --stat AutoQAC/Views/ProgressWindow.axaml.cs` | empty (0 lines changed) |
| Regex-based test assertions present | `Regex\.IsMatch` in `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs` | 5 matches (regex assertions 1-5) |
| Brittle exact-substring CloseRequested assertion absent | `Should\(\)\.Contain\("progressViewModel\.CloseRequested \+= ` in `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs` | 0 matches (proves no brittle literal substring assertion was used) |

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None. The pre-existing `ProgressWindow.axaml.cs` cleanup contract meant the runtime behavior was already correct, so the GREEN production change was purely additive and the regex-tolerant test transitioned cleanly RED -> GREEN.

## Known Stubs

None. The new code introduces no UI-bound placeholders, hardcoded empty data sources, or `TODO`/`FIXME` markers.

## Threat Flags

None. Plan 07-12 is local UI lifecycle wiring inside `MainWindow`. It does not introduce new network endpoints, auth paths, filesystem trust boundaries, or schema/state changes. The plan's `<threat_model>` mitigations (T-07-12-01 disposal completeness, T-07-12-03 dual-subscription readability) are satisfied by the GREEN code and the `// Defense in depth` comment.

## TDD Gate Compliance

- RED gate present: `dd6c3b9` (`test(07-12): add failing normal progress window lifecycle test`).
- GREEN gate present after RED: `ff5622b` (`feat(07-12): wire normal progress window close and disposal`).
- REFACTOR gate not produced; no behavior-neutral cleanup commit was needed.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter 'FullyQualifiedName~MainWindowShowProgressAsync_ShouldWireProgressWindowCloseAndDisposal'` -- 1 failed, 0 passed (expected RED state).
- RED + other lifecycle tests: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter 'FullyQualifiedName~ViewSubscriptionLifecycleTests'` -- 1 failed, 3 passed (existing lifecycle tests stayed green during RED, as required).
- GREEN: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter 'FullyQualifiedName~ViewSubscriptionLifecycleTests'` -- 4 passed, 0 failed.
- Build: `dotnet build AutoQAC/AutoQAC.csproj` -- 0 warnings, 0 errors.
- Full solution: `dotnet test AutoQACSharp.slnx` -- 753 AutoQAC.Tests + 59 QueryPlugins.Tests passed, 0 failures.
- File state: `git diff --stat AutoQAC/Views/ProgressWindow.axaml.cs` -- empty (0 lines changed; the existing cleanup contract is intact).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 07 normal-path progress lifecycle gap closure is complete and ready for the Phase 7 cluster verification owned by Plan 07-13. Sequential xEdit cleaning, MO2 backup skip behavior, hang detection, and trusted restore root containment from Plans 07-01 through 07-11 are unchanged.

## Self-Check: PASSED

- Created/modified files verified on disk: `AutoQAC/Views/MainWindow.axaml.cs`, `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs`, and this summary.
- Task commits verified in git history: `dd6c3b9` (test) and `ff5622b` (feat) — both reachable on `gsd/v1.0-cleanup`.

---
*Phase: 07-backup-restore-retention-safety*
*Completed: 2026-04-29*
