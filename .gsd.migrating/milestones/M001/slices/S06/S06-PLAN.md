# S06: Process Stop Pid Safety

**Goal:** Create the PID storage foundation for Phase 5.
**Demo:** Create the PID storage foundation for Phase 5.

## Must-Haves


## Tasks

- [x] **T01: 05-process-stop-pid-safety 01**
  - Create the PID storage foundation for Phase 5.

Purpose: Process/PID cleanup cannot be tested or made process-safe while the PID path and JSON read/write logic are private helpers inside `ProcessExecutionService`.
Output: Injectable PID store/path/session abstractions, locked JSON implementation, session-aware `TrackedProcess`, and direct unit tests.
- [x] **T02: 05-process-stop-pid-safety 02**
  - Correct low-level process termination semantics while preserving the single process slot.

Purpose: The process service currently conflates user cancellation with timeout and reports failed force-kill attempts as success.
Output: Explicit cancellation intent, `ForceKillFailed`, PID store integration, and focused process-service regression tests.
- [x] **T03: 05-process-stop-pid-safety 03**
  - Wire the corrected process termination semantics into user-visible stop behavior.

Purpose: Phase 5 is only complete if users see the explicit confirmation, decline, second-click escalation, and force-kill failure outcomes described in the context and UI contract.
Output: Orchestrator/ViewModel state-machine updates and tests proving prompt ordering, decline, second stop, failure reporting, and unsafe log-read blocking.
- [x] **T04: 05-process-stop-pid-safety 04**
  - Add the single-instance guard and real-process validation harness required to prove Phase 5 safety behavior.

Purpose: PID/process safety cannot be trusted if duplicate AutoQAC instances can race PID writes, and TEST-01 requires controlled real child-process coverage that runs by default.
Output: Named mutex guard, startup wiring, helper executable, integration tests, and test project wiring.

## Files Likely Touched

- `AutoQAC/Models/TrackedProcess.cs`
- `AutoQAC/Services/Process/IPidStore.cs`
- `AutoQAC/Services/Process/JsonPidStore.cs`
- `AutoQAC/Services/Process/IPidStorePathProvider.cs`
- `AutoQAC/Services/Process/DefaultPidStorePathProvider.cs`
- `AutoQAC/Services/Process/IProcessSessionIdProvider.cs`
- `AutoQAC/Services/Process/ProcessSessionIdProvider.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/JsonPidStoreTests.cs`
- `AutoQAC/Models/ProcessResult.cs`
- `AutoQAC/Models/TerminationResult.cs`
- `AutoQAC/Services/Process/IProcessExecutionService.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs`
- `AutoQAC/Services/Cleaning/StopCleaningResult.cs`
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs`
- `AutoQAC/Services/Process/ISingleInstanceGuard.cs`
- `AutoQAC/Services/Process/SingleInstanceGuard.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC/App.axaml.cs`
- `AutoQAC.Tests/Services/SingleInstanceGuardTests.cs`
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs`
- `AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj`
- `AutoQAC.Tests/TestProcessHelper/Program.cs`
- `AutoQAC.Tests/AutoQAC.Tests.csproj`
- `AutoQACSharp.slnx`
