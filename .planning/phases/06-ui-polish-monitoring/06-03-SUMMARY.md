---
phase: 06-ui-polish-monitoring
plan: 03
subsystem: monitoring
tags: [hang-detection, cpu-monitoring, process-monitoring, progress-window, inline-warning]

# Dependency graph
requires:
  - phase: 06-01
    provides: "Sub-ViewModel structure, CleaningOrchestrator wiring patterns"
provides:
  - "IHangDetectionService interface and HangDetectionService CPU-based polling implementation"
  - "HangDetected observable on ICleaningOrchestrator for ViewModel subscription"
  - "Inline hang warning banner in ProgressWindow with Wait and Kill actions"
  - "Auto-dismiss when xEdit resumes CPU activity"
affects:
  - 07-hardening-cleanup (test coverage expansion may add more hang detection edge case tests)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "CPU polling via Process.TotalProcessorTime delta over Observable.Interval(5s)"
    - "Subject<bool> relay pattern: orchestrator subscribes to service, forwards to Subject, ViewModel subscribes"
    - "Dismiss/resume cycle: _hangWarningDismissed flag prevents re-show until process resumes then hangs again"

key-files:
  created:
    - AutoQAC/Services/Monitoring/IHangDetectionService.cs
    - AutoQAC/Services/Monitoring/HangDetectionService.cs
    - AutoQAC.Tests/Services/HangDetectionServiceTests.cs
  modified:
    - AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs
    - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
    - AutoQAC/ViewModels/ProgressViewModel.cs
    - AutoQAC/Views/ProgressWindow.axaml
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
    - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs

key-decisions:
  - "Process.TotalProcessorTime for CPU monitoring (cross-platform, no admin rights, already available)"
  - "Subject<bool> relay in orchestrator: service monitors process, orchestrator forwards to Subject, ViewModel subscribes"
  - "Wait button sets _hangWarningDismissed flag -- warning only reappears if process resumes then hangs again"
  - "Kill button calls ForceStopCleaningAsync (immediate process tree kill, not graceful stop)"
  - "Hang monitor subscription disposed on plugin change -- emits false to auto-dismiss any visible warning"
  - "Public constants for PollIntervalMs/HangThresholdMs/CpuThreshold (no InternalsVisibleTo in project)"

patterns-established:
  - "IHangDetectionService.MonitorProcess returns IObservable<bool> -- subscribe per process, dispose on process end"
  - "Orchestrator relay: subscribe to service observable in onProcessStarted callback, dispose after each plugin"
  - "Inline non-modal warning pattern: Border with IsVisible binding, complementary Wait/Kill action buttons"

# Metrics
duration: 12min
completed: 2026-02-07
---

# Phase 6 Plan 3: Hang Detection Summary

**CPU-based xEdit hang detection with inline progress window warning, Wait/Kill actions, and auto-dismiss on resume**

## Performance

- **Duration:** ~12 min
- **Started:** 2026-02-07T08:09:26Z
- **Completed:** 2026-02-07T08:22:00Z
- **Tasks:** 2
- **Files created:** 3
- **Files modified:** 7

## Accomplishments
- Created IHangDetectionService interface with MonitorProcess(Process) returning IObservable<bool>
- Implemented HangDetectionService: polls TotalProcessorTime every 5s, flags after 60s of <0.5% CPU, auto-clears on resume
- Added HangDetected observable to ICleaningOrchestrator and CleaningOrchestrator (Subject<bool> relay pattern)
- Wired hang monitoring in orchestrator's onProcessStarted callback, dispose on plugin completion
- Added IsHangWarningVisible, DismissHangWarningCommand, KillHungProcessCommand to ProgressViewModel
- Added inline warning banner to ProgressWindow.axaml (orange #FFF3E0 background, warning icon, Wait/Kill buttons)
- Registered IHangDetectionService as singleton in DI container
- Added 4 unit tests for HangDetectionService (observable creation, already-exited process, short-lived process, constants)
- Updated CleaningOrchestratorTests and ProgressViewModelTests for new constructor parameter and HangDetected mock

## Task Commits

Note: Both tasks were committed as part of the parallel 06-02 execution that was running simultaneously on disk. The code was authored by this plan but committed in 06-02's atomic commits.

1. **Task 1: HangDetectionService and DI registration** - `d3d56a9` (feat)
   - IHangDetectionService, HangDetectionService, ServiceCollectionExtensions, tests
2. **Task 2: ProgressViewModel hang warning + ProgressWindow banner + Orchestrator wiring** - `204c6e0` + `bd4e1a7` (feat + fix)
   - ICleaningOrchestrator HangDetected property, CleaningOrchestrator implementation, ProgressViewModel, ProgressWindow.axaml, test fixes

## Files Created/Modified
- `AutoQAC/Services/Monitoring/IHangDetectionService.cs` - Interface: MonitorProcess(Process) -> IObservable<bool>
- `AutoQAC/Services/Monitoring/HangDetectionService.cs` - CPU polling implementation (137 lines)
- `AutoQAC.Tests/Services/HangDetectionServiceTests.cs` - 4 unit tests for observable behavior and constants
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` - Added IObservable<bool> HangDetected property
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` - Added IHangDetectionService dependency, Subject<bool> relay, monitoring wiring
- `AutoQAC/ViewModels/ProgressViewModel.cs` - IsHangWarningVisible, DismissHangWarningCommand, KillHungProcessCommand, OnHangDetected handler
- `AutoQAC/Views/ProgressWindow.axaml` - Inline warning banner between summary bar and progress bar
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registered IHangDetectionService singleton
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - Added IHangDetectionService mock to all constructor calls
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs` - Added HangDetected mock setup

## Decisions Made

1. **Process.TotalProcessorTime over PerformanceCounter**: Cross-platform, no admin rights, already used in the project for process management. PerformanceCounter is Windows-only.

2. **Subject<bool> relay pattern**: The orchestrator subscribes to IHangDetectionService.MonitorProcess per-process and forwards emissions through a Subject<bool>. The ViewModel subscribes to the subject. This decouples the ViewModel from process lifecycle details.

3. **Wait = dismiss + _hangWarningDismissed flag**: Clicking Wait hides the warning and sets a flag. If the same hang continues, the warning stays hidden. If the process resumes then hangs again, the flag is reset and the warning can reappear.

4. **Kill = ForceStopCleaningAsync**: Clicking Kill calls ForceStopCleaningAsync for immediate process tree kill (not graceful stop). This is the strongest action -- the user explicitly chose to terminate a hung process.

5. **Public constants**: PollIntervalMs (5000), HangThresholdMs (60000), CpuThreshold (0.5) are public const on HangDetectionService. The project doesn't use InternalsVisibleTo, and these are useful documentation of service behavior.

6. **Fully-qualified System.Diagnostics.Process**: The AutoQAC.Services.Process namespace creates an ambiguity with System.Diagnostics.Process. Used fully-qualified name in interface and implementation to avoid conflicts.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Namespace conflict with System.Diagnostics.Process**
- **Found during:** Task 1
- **Issue:** `using System.Diagnostics` brought `Process` type into scope, but `AutoQAC.Services.Process` namespace created an ambiguity. Build failed with CS0118.
- **Fix:** Removed `using System.Diagnostics`, used fully-qualified `System.Diagnostics.Process` in interface and implementation.
- **Files modified:** IHangDetectionService.cs, HangDetectionService.cs

**2. [Rule 3 - Blocking] internal constants not accessible from test project**
- **Found during:** Task 1
- **Issue:** Constants were declared `internal` but the test project doesn't have InternalsVisibleTo. Build failed.
- **Fix:** Changed constants to `public`. They document service behavior and don't expose security-sensitive information.
- **Files modified:** HangDetectionService.cs

**3. [Parallel Execution Overlap] 06-02 committed 06-03 files**
- **Found during:** Task 1 and Task 2 commit
- **Issue:** Plan 06-02 was running in parallel and committed the files created/modified by this plan as part of its own commits (d3d56a9, 204c6e0, bd4e1a7). Both plans modified shared files on disk simultaneously.
- **Impact:** No code loss -- all code authored by this plan is present in the committed state. The commit history attributes the changes to 06-02's commit messages, but the code content is correct.
- **Resolution:** Tracked the actual commits containing 06-03 code for summary documentation.

---

**Total deviations:** 3 auto-fixed (2 blocking, 1 parallel execution overlap)
**Impact on plan:** All auto-fixes were necessary for builds to pass. Parallel execution overlap is cosmetic (affects git blame attribution, not code correctness).

## Issues Encountered

- **Parallel execution with 06-02:** Both plans modified CleaningOrchestrator.cs simultaneously. The 06-02 plan committed first, including the changes authored by this plan. This is the expected behavior when two plans run in wave 2 on shared files -- the second to commit finds the working tree clean.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Hang detection is fully wired: service -> orchestrator -> ViewModel -> XAML
- CPU threshold (0.5%) and hang duration (60s) are conservative and tunable via constants
- Auto-dismiss on resume handles false positives gracefully
- All 465 tests pass (no regressions)

---
*Phase: 06-ui-polish-monitoring*
*Completed: 2026-02-07*

## Self-Check: PASSED
