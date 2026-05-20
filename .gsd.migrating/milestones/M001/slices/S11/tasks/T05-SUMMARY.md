---
id: T05
parent: S11
milestone: M001
provides:
  - Settings dialog persistence failure banner bound to typed IConfigurationService failure/result streams
  - Text-only user-facing mappings for save, flush, reload, missing-file, and invalid-YAML persistence failures
  - Clear-on-success behavior for successful saves, flush/no-op results, and accepted external reloads
  - DI launch fix for ConfigWatcherService resolving through IConfigPersistenceCoordinator
requires: []
affects: []
key_files: []
key_decisions: []
patterns_established: []
observability_surfaces: []
drill_down_paths: []
duration: 15 min active execution plus manual UAT checkpoint
verification_result: passed
completed_at: 2026-04-30
blocker_discovered: false
---
# T05: 10-configuration-persistence-hardening 05

**# Phase 10 Plan 05: Settings Persistence Banner Summary**

## What Happened

# Phase 10 Plan 05: Settings Persistence Banner Summary

**Settings persistence failures now appear as a text-only Settings dialog banner driven by typed failure streams, with successful saves/reloads clearing stale warnings.**

## Performance

- **Duration:** 15 min active execution plus manual UAT checkpoint
- **Started:** 2026-04-30T23:24:29Z
- **Completed:** 2026-04-30T23:39:18Z
- **Tasks:** 2 completed, plus one launch-blocker auto-fix
- **Files modified:** 6

## Accomplishments

- Added `SettingsViewModel.PersistenceBannerText` and `HasPersistenceBanner`, fed by `IConfigurationService.Failures` and cleared by successful `PersistenceResults` or accepted `UserConfigurationChanged` reloads.
- Added a visible warning `Border` in `SettingsWindow.axaml` bound to `HasPersistenceBanner` / `PersistenceBannerText` near the top of the settings content.
- Changed explicit Settings saves to flush pending persistence before closing, keeping the dialog open and showing a restore banner if the flush fails.
- Added 11 `SettingsViewModelTests` covering failure mappings, successful save/reload clearing, subscription disposal, static XAML binding guards, no-modal behavior, and no retry UI.
- Fixed a launch-time DI blocker by changing `ConfigWatcherService` to consume `IConfigPersistenceCoordinator`, matching the registered coordinator abstraction.
- Recorded the user's blocking manual UAT response: **approved**.

## Task Commits

Each implementation task was committed atomically:

1. **Task 1 RED: Add SettingsViewModel persistence banner tests** - `f326c37` (test)
2. **Task 1 GREEN: Surface typed persistence failures as SettingsViewModel banner** - `45c844f` (feat)
3. **Task 1a: Fix launch blocker: config watcher DI resolution** - `8a116f3` (fix)

**Plan metadata:** final docs commit for this summary/state update.

## Files Created/Modified

- `AutoQAC/ViewModels/SettingsViewModel.cs` - Adds banner observable state, typed failure/result/reload subscriptions, safe banner mapping, flush-before-close save behavior, and subscription disposal.
- `AutoQAC/Views/SettingsWindow.axaml` - Adds the visible text-only warning banner bound to `PersistenceBannerText` and `HasPersistenceBanner`.
- `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs` - Adds deterministic tests for banner mappings, clearing, disposal, XAML binding, no modal dialog, and no retry UI.
- `AutoQAC/Services/Configuration/ConfigWatcherService.cs` - Uses `IConfigPersistenceCoordinator` so DI resolves the watcher against the registered abstraction.
- `AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs` - Keeps the coordinator contract available for watcher injection.
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` - Ensures `IConfigWatcherService` resolves in the integrated service provider.

## Banner Mapping Shipped

| Failure.Operation | Failure.Kind | Banner text |
|-------------------|--------------|-------------|
| Save | WriteFailed | `Could not save settings. Settings were restored to last saved values.` |
| Flush | WriteFailed | `Could not save settings before cleaning. Cleaning was blocked. Settings were restored to last saved values.` |
| Flush | Other | `Could not save settings (flush). Cleaning was blocked.` |
| Reload / DeferredReload | InvalidExternalYaml | `External settings file has invalid YAML. Active settings were not changed.` |
| Reload / DeferredReload | MissingFile | `Settings file is missing or unreadable. Active settings were not changed.` |
| Reload / DeferredReload | ReadFailed | `Could not read settings file. Active settings were not changed.` |
| Other | Fall-through | `Could not persist settings: {failure.SafeSummary}` |

## Subscription and UI Binding Pattern

- `SettingsViewModel` subscribes to `Failures`, `PersistenceResults`, and `UserConfigurationChanged` with `CallbackObserver<T>`.
- Each observer posts back through the injected `IUiDispatcher` before mutating observable state.
- `Dispose()` releases all three subscriptions plus existing debounced path validators.
- `SettingsWindow.axaml` binds:
  - `Border.IsVisible="{Binding HasPersistenceBanner}"`
  - `TextBlock.Text="{Binding PersistenceBannerText}"`

## Manual UAT Approval

- **Checkpoint:** Task 2 — Manual UAT: banner appears + clears + no dialog.
- **User response:** approved.
- **Recorded outcome:** The user approved the four manual scenarios from the plan: save failure banner, clear on next successful save, invalid external YAML banner, and no retry UI.
- **Visible surface:** `SettingsWindow.axaml` displays the banner inside the Settings dialog near the top of the settings content.
- **Dialog/retry behavior:** UAT approval confirms no modal dialog appeared for persistence failures and no Retry/Try Again control was visible.

## Decisions Made

- Settings persistence failures are recoverable UI state, so they are shown as text-only banner copy rather than modal dialogs.
- Save success is defined by the explicit flush barrier (`Success`/`NoOp`), not merely by enqueueing the save request.
- Accepted external reload notifications clear stale failure banners, matching D-27's successful save/reload/flush rule.
- The watcher constructor depends on `IConfigPersistenceCoordinator` to match the registered DI abstraction and prevent startup resolution failures.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed config watcher DI launch blocker**
- **Found during:** Task 2 manual UAT launch preparation.
- **Issue:** The app could not launch because `ConfigWatcherService` was not resolvable through DI after coordinator wiring; the watcher needed the registered coordinator abstraction.
- **Fix:** Changed `ConfigWatcherService` to inject `IConfigPersistenceCoordinator`, kept the contract public, and added a DI integration assertion that `IConfigWatcherService` resolves.
- **Files modified:** `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs`, `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
- **Verification:** `dotnet test AutoQACSharp.slnx --nologo` passed after the fix; manual UAT checkpoint was approved.
- **Committed in:** `8a116f3`

---

**Total deviations:** 1 auto-fixed (1 Rule 3 blocking issue).
**Impact on plan:** The fix was necessary to launch the app for UAT and aligned DI with the existing coordinator abstraction; no new feature scope was added.

## Issues Encountered

- An initial full-suite verification run was started in parallel with the focused SettingsViewModel test run and hit a transient MSBuild file lock on `AutoQAC.dll`. Rerunning the full suite sequentially passed.

## Known Stubs

None. Stub-pattern scans found only existing nullable field initializers/design-time null guards, subscription clear assignments, and test-local nullable setup variables; no UI-facing placeholder/mock data or goal-blocking stubs were introduced.

## Threat Flags

None. The new trust boundaries were already in the Plan 05 threat model: typed coordinator failures to SettingsViewModel and text-only binding to `TextBlock`. No new network endpoints, auth paths, file access trust boundaries, or schema changes were introduced beyond the planned config persistence UI surface.

## Verification

- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "PersistenceBannerText"` — passed (multiple matches).
- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "restored to last saved values"` — passed.
- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "using System\.Reactive|using ReactiveUI"` — passed (zero matches).
- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "MessageBox|ShowError|ShowAlert"` — passed (zero matches).
- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "RetryCommand|RetryButton|RetryAttempt"` — passed (zero matches).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~SettingsViewModelTests --nologo` — passed (11 tests).
- `dotnet test AutoQACSharp.slnx --nologo` — passed (AutoQAC.Tests: 909; QueryPlugins.Tests: 61).
- Manual UAT Task 2 — approved by user.

## TDD Gate Compliance

- RED commit present: `f326c37 test(10-05): add SettingsViewModel persistence banner tests`
- GREEN commit present after RED: `45c844f feat(10-05): surface typed persistence failures as SettingsViewModel banner`
- Follow-up fix commit: `8a116f3 fix(10-05): make config watcher resolvable at startup`
- REFACTOR commit: not needed.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 10 is ready to close: typed persistence now flows from serialized coordinator saves/reloads through pre-cleaning blockers and Settings UI banner feedback.
- Phase 11 can build on the safe-summary and text-only diagnostics patterns when tightening user-facing diagnostics boundaries.

## Self-Check: PASSED

- Verified key modified files and this summary exist on disk.
- Verified commits `f326c37`, `45c844f`, and `8a116f3` exist in git history.
- Verified full-suite automated tests pass after manual UAT approval.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-04-30*
