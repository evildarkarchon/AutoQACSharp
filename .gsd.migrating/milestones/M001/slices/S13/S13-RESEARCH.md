# Phase 12 — Process Stop Verification & Progress Flow Closure Research

**Created:** 2026-05-01  
**Purpose:** Identify existing stop-flow, process/PID evidence, UI/test seams, and validation strategy needed to plan Phase 12 without changing the termination architecture.

## Summary

Phase 12 should be implemented as a narrow gap closure: share exact stop outcome copy between the main Stop command and Progress-window Stop, expose explicit `Force Terminate` / `Leave Running` action labels through the existing message dialog abstraction, make `ProgressViewModel` handle `GracePeriodExpired`, declined force termination, and `ForceKillFailed`, and produce a Phase 12 verification artifact that maps `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01` to current tests.

No external libraries are required. Existing .NET/Avalonia, CommunityToolkit.Mvvm, xUnit, FluentAssertions, NSubstitute, and helper-process patterns are sufficient.

## Existing Implementation

| Area | Current state | Planning implication |
|------|---------------|----------------------|
| Main Stop | `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` handles `GracePeriodExpired`, prompts, calls `ForceStopCleaningAsync`, reports `ForceKillFailed`, and calls `MarkLeftRunningByUser` on decline. | Treat as the behavior source but move copy and labels to a shared contract so main and Progress cannot drift (`D-01` through `D-04`). |
| Progress Stop | `AutoQAC/ViewModels/ProgressViewModel.cs` currently awaits `_orchestrator.StopCleaningAsync()` and ignores the result. | Must branch on `StopCleaningResult.TerminationResult ?? LastTerminationResult` just like main Stop (`INT-01`, `FLOW-01`). |
| Hang Kill | `ProgressViewModel.KillHungProcessAsync` calls `ForceStopCleaningAsync()` directly. | Preserve immediate force behavior; route only `ForceKillFailed` through shared failure dialog and persistent warning (`D-09` through `D-12`). |
| Dialog API | `IMessageDialogService.ShowConfirmAsync` only offers Yes/No; `ShowAsync` supports button sets but labels are hardcoded in `MessageDialog.axaml`. | Add a scoped custom choice API or custom button labels on the dialog ViewModel, then update both Stop callers to use explicit labels. |
| Progress summary | `ProgressWindow.axaml` has a results summary area with `SessionSummaryText` and result totals. | Add a persistent warning row near `SessionSummaryText`, bound to a new Progress ViewModel property, not the transient Stop button area (`D-05` through `D-07`). |
| Tests | `ProgressViewModelTests`, `MainWindowViewModelTests`, `ProcessExecutionIntegrationTests`, `JsonPidStoreTests`, and `ProcessExecutionServiceTests` already cover adjacent patterns. | Add targeted ViewModel tests for Progress Stop/Hang Kill and shared text contracts; cite existing process/PID tests in verification. |

## Architectural Responsibility Map

| Responsibility | Correct owner | Must not move to |
|----------------|---------------|------------------|
| Two-stage termination policy and active process state | `CleaningTerminationCoordinator` via `ICleaningOrchestrator` | `ProgressViewModel` or Avalonia views |
| Stop outcome copy constants and exact text contract | A small shared model/helper under `AutoQAC/Models` or equivalent neutral namespace | Duplicate literals in separate ViewModels |
| Dialog rendering and custom button labels | `IMessageDialogService`, `MessageDialogService`, `MessageDialogViewModel`, `MessageDialog.axaml` | Direct control manipulation from ViewModels |
| Progress Stop orchestration | `ProgressViewModel`, calling `ICleaningOrchestrator` and `IMessageDialogService` | `ProgressWindow.axaml.cs` |
| Persistent Progress summary warning | `ProgressViewModel` bindable properties and `ProgressWindow.axaml` | Transient spinner/Stop button area only |
| Process/PID evidence | Existing process/PID test suites plus `12-VERIFICATION.md` | Rewriting Phase 5 artifacts |

## Recommended Implementation Shape

1. Create a shared stop outcome text/choice contract, e.g. `AutoQAC/Models/StopTerminationDialogContent.cs`, with exact strings:
   - Title: `Force Terminate xEdit?`
   - Confirmation: `xEdit did not exit after the stop request. AutoQAC can leave xEdit running, or force terminate it now. Force terminating can interrupt remaining file or log writes.`
   - Primary label: `Force Terminate`
   - Secondary label: `Leave Running`
   - Force failure title: `Could Not Force Terminate xEdit`
   - Force failure message: `AutoQAC could not force terminate xEdit. xEdit may still be running; close it manually before starting another cleaning session. Technical details are in the latest AutoQAC log.`
   - Left-running title: `Cleaning Stopped`
   - Left-running message: `AutoQAC stopped the cleaning session. xEdit was left running by your choice; close it manually when it is safe.`
2. Extend the dialog abstraction narrowly so production can show those action labels and tests can assert the labels exactly.
3. Update `CleaningCommandsViewModel.StopCleaningAsync` to call the new shared choice path and constants, preserving existing stop behavior.
4. Inject `IMessageDialogService` into `ProgressViewModel`, update `MainWindow.ShowProgressAsync` and `ShowPreviewAsync`, and update tests.
5. Add `ProgressViewModel` handling for:
   - Progress Stop + `GracePeriodExpired` + confirmed choice → call `ForceStopCleaningAsync`.
   - Progress Stop + declined choice → call `MarkLeftRunningByUser`, no force stop, set persistent warning.
   - Progress Stop + `ForceKillFailed` → shared failure dialog and persistent warning.
   - Hang Kill + `ForceKillFailed` → no confirmation, shared failure dialog and persistent warning.
6. Add `12-VERIFICATION.md` after implementation and test runs, not Phase 5 artifacts.

## Tests To Add Or Run

### Targeted new/updated tests

- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
  - Main Stop uses the new custom labels `Force Terminate` and `Leave Running`.
  - Main Stop assertions reference the same shared text contract used by Progress Stop.
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
  - Progress Stop + `GracePeriodExpired` asks for confirmation before force stop.
  - Confirmed Progress Stop + `ForceKillFailed` shows shared failure dialog and sets persistent warning text.
  - Declined Progress Stop invokes `MarkLeftRunningByUser`, does not force stop, and sets persistent warning text.
  - Hang warning Kill calls `ForceStopCleaningAsync` directly, does not ask for confirmation, and reports `ForceKillFailed` through the shared failure path.
  - New cleaning session clears any persistent stop outcome warning.

### Existing evidence to preserve and cite

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProcessExecutionIntegrationTests`
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProcessExecutionServiceTests`
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~JsonPidStoreTests`
- `dotnet test AutoQACSharp.slnx`

## Validation Architecture

| Requirement | Evidence type | Primary files | Automated command |
|-------------|---------------|---------------|-------------------|
| `SAF-01` | Progress Stop confirmation and no pre-confirm force-stop tests | `ProgressViewModelTests.cs`, `MainWindowViewModelTests.cs` | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests|FullyQualifiedName~MainWindowViewModelTests"` |
| `SAF-02` | Shared force-failure dialog and persistent Progress warning tests | `ProgressViewModelTests.cs`, `MainWindowViewModelTests.cs` | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProgressViewModelTests` |
| `REF-04` | PID store seam and process-safe update tests | `JsonPidStoreTests.cs`, `ProcessExecutionServiceTests.cs` | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~JsonPidStoreTests|FullyQualifiedName~ProcessExecutionServiceTests"` |
| `TEST-01` | Real helper-process timeout, graceful behavior, force kill, user cancellation, and PID cleanup tests | `ProcessExecutionIntegrationTests.cs` | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProcessExecutionIntegrationTests` |

## Common Pitfalls

- Do not add confirmation to the Hang warning `Kill` button; it is intentionally immediate (`D-09`).
- Do not update `05-VERIFICATION.md`, Phase 5 summaries, `REQUIREMENTS.md`, or completion markers in Phase 12 (`D-13`, `D-16`).
- Do not add Avalonia.Headless or new UI infrastructure.
- Do not let user-facing stop copy contain raw exception text, stack traces, full paths, or command lines.
- Do not duplicate stop outcome strings across main and Progress ViewModels; tests should catch drift.