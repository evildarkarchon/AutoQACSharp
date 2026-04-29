---
phase: 05
reviewers: [gemini, claude, codex]
reviewed_at: 2026-04-29T02:21:12.7273908Z
plans_reviewed: [05-01-PLAN.md, 05-02-PLAN.md, 05-03-PLAN.md, 05-04-PLAN.md]
---

# Cross-AI Plan Review - Phase 5

## Gemini Review

The proposed plans for **Phase 5: Process Stop & PID Safety** are exceptionally thorough, well-structured, and directly address the safety and testability concerns identified in the codebase audit. The strategy of decoupling cancellation intent from termination outcomes is the correct architectural choice to resolve the existing auto-force-kill bug.

### 1. Summary
This phase significantly hardens AutoQAC's process lifecycle management. By introducing an injectable `IPidStore` with interprocess file locking and a session-aware `TrackedProcess` model, you solve the concurrent-write risk and the "testability gap" (REF-04). The correction of `ProcessExecutionService` to distinguish between user-requested stops and timeouts ensures that the UI confirmation path is respected (SAF-01). Finally, the addition of a dedicated test helper executable and real-process integration tests provides the empirical verification needed to trust these critical safety paths (TEST-01).

### 2. Strengths
- **Surgical Intent Modeling**: Splitting `ProcessStopReason` into `Timeout` and `UserRequestedStop` is a high-signal fix for the "auto-kill" bug.
- **Robust PID Safety**: The use of `FileStream.Lock` for the PID store combined with session ID filtering provides strong protection against PID reuse and lost updates.
- **High-Fidelity Integration Testing**: The `TestProcessHelper` approach is the "gold standard" for testing process lifecycle code without the flakiness of mocking `System.Diagnostics.Process`.
- **UI-SPEC Adherence**: The plans meticulously incorporate the specific copywriting and interaction rules from the UI contract, ensuring a polished user experience for failure scenarios.
- **Corruption Recovery**: D-12 (preserving corrupt PID files) is an excellent defensive programming pattern that aids in field diagnostics.

### 3. Concerns
- **Dialog Button Customization (MEDIUM)**: `IMessageDialogService` currently uses standard `MessageDialogButtons` (e.g., `YesNo`). The `UI-SPEC` requests labels like `Force Terminate` and `Leave Running`. If the underlying Avalonia dialog implementation doesn't support custom button text yet, the "Yes/No" mapping should be explicitly documented in the code to prevent future confusion.
- **Mutex Naming & Permissions (LOW)**: The plan uses `Global\AutoQAC-{Environment.UserName}`. While `Global\` is correct for cross-session/admin scenarios, it can occasionally require specific privileges. Since AutoQAC is a local user tool, ensure the chosen name is stable but doesn't cause unnecessary "Access Denied" errors in restricted environments.
- **Blocking `Lock` Call (LOW)**: `FileStream.Lock` is a synchronous OS call. While the PID file is tiny and the lock duration is minimal, ensure the UI remains responsive by keeping these calls strictly within the `ProcessExecutionService`'s async Task boundaries.

### 4. Suggestions
- **Migration Logic**: In `CleanOrphanedProcessesAsync`, explicitly treat entries with a missing `SessionId` as "Prior Session" entries. This ensures that after the update, any existing legacy entries are cleaned up properly without being ignored.
- **Log Parsing Guard**: In Plan 03, ensure the logic that blocks log reading also clears any "Stale" log content from the UI if the user starts a new plugin clean after a `ForceKillFailed` event.
- **Helper Executable Location**: Ensure the `TestProcessHelper` path calculation in integration tests is robust across different build configurations (Debug/Release) and doesn't rely on hardcoded relative paths.

### 5. Risk Assessment: LOW
The overall risk is **LOW**. The plans follow a logical **Data -> Service -> Orchestrator -> UI** progression. The heavy emphasis on automated testing (both unit and integration) significantly mitigates the risk of introducing new process-management bugs. The phase is well-scoped and does not drift into command-line escaping or other downstream requirements.

**Verdict:** The plans are ready for execution.

---

## the agent Review

# Cross-AI Plan Review: Phase 5 - Process Stop & PID Safety

## Overall Phase Assessment

**Summary**: The four-plan decomposition cleanly maps to the locked decisions (D-01 through D-18) and the four phase requirements (SAF-01, SAF-02, REF-04, TEST-01). Wave ordering (1: storage foundation -> 2: process service semantics -> 3: orchestrator/VM + integration harness in parallel) is sound. The plans are concrete, testable, and stay within phase scope. However, there are several real gaps around DI wiring for `ProcessExecutionService`'s new constructor, ambiguity in how the orchestrator distinguishes "user declined" from `GracePeriodExpired`, and a notable risk in Plan 04's startup-shutdown ordering and helper-path resolution.

---

## Plan 05-01: PID Storage Foundation

### Strengths
- Cleanly extracts three orthogonal seams (`IPidStore`, `IPidStorePathProvider`, `IProcessSessionIdProvider`) - matches D-10/D-13 precisely.
- TDD acceptance criteria are grep-checkable (`stream.Lock(0, 1)`, `corrupt-`, `JsonException`).
- Concurrent update test (Test 2 of Task 2) directly validates D-11 across processes/threads.
- Corruption preservation pattern (`autoqac-pids.corrupt-{timestamp}.json`) is unambiguous.

### Concerns
- **MEDIUM** - `IPidStorePathProvider` is introduced but `JsonPidStore` already needs the path. Plan doesn't say `JsonPidStore` takes the provider via constructor injection. Worth making explicit so test substitution path is mechanical.
- **MEDIUM** - `FileStream.Lock(0, 1)` locks one byte, but `OpenOrCreate` + `FileShare.ReadWrite` means another process could still *read* the file mid-write. The locked region must be checked by *cooperating* readers (the lock is advisory on Windows when both sides participate). The plan does not require readers to also call `Lock` before reading, which is the actual correctness requirement. Add a `Lock` on the read path inside `LoadAsync` too.
- **LOW** - `ProcessSessionIdProvider` is a singleton GUID per app run, but DI lifetime is not specified. If registered Transient, every consumer gets a different session ID and orphan filtering breaks.
- **LOW** - Acceptance criterion says "tests named with `Corrupt`, `Concurrent`, and `SessionId`" - concurrent test on a single machine with `FileStream.Lock` is hard to make non-flaky without careful coordination. Consider whether the test should use two `FileStream` handles in the same process simulating contention.

### Suggestions
- Make `JsonPidStore`'s constructor signature explicit in the `<interfaces>` block: `JsonPidStore(IPidStorePathProvider pathProvider, ILoggingService logger)`.
- State that `LoadAsync` also acquires `stream.Lock(0, 1)` for read consistency.
- Specify DI lifetime: `IProcessSessionIdProvider` MUST be singleton.
- Pin `JsonSerializerOptions` to a static instance to avoid per-call allocation in concurrent paths.

### Risk: **LOW**

---

## Plan 05-02: Process Service Cancellation & Force-Kill Semantics

### Strengths
- Correctly identifies the conflation bug at `ProcessExecutionService.cs:102-119` and surfaces `ProcessStopReason` as the disambiguation.
- Plan to remove `GetPidFilePath` and reflection-based tests is the right testability win.
- Exception list for `ForceKillFailed` (`Win32Exception or NotSupportedException or AggregateException or OperationCanceledException`) matches Microsoft's documented surface.
- D-07's "root process exit != tree exit" caveat is honored in the `ForceKilled` definition.

### Concerns
- **HIGH** - DI break: `ProcessExecutionService` is registered in `ServiceCollectionExtensions.AddBusinessLogic` as a singleton with only `ILoggingService`. Adding two new constructor params (`IPidStore`, `IProcessSessionIdProvider`) requires the providers to be registered *first* - but the plan does not modify `ServiceCollectionExtensions.cs` in `files_modified`. This will produce a runtime DI failure on app start. Plan 05-01 also doesn't register them. **One of these plans must register the new services.**
- **HIGH** - How does `ExecuteAsync` know whether interruption is "user stop" or "timeout" when both signals collapse to `OperationCanceledException`? The current design only has the linked CTS. The plan implies a `ProcessStopReason` enum but doesn't show how the *reason* is communicated into `ExecuteAsync`. Options: (a) check `timeoutCts.IsCancellationRequested` (already done) and treat `!timedOut` as user stop - but `ExecuteAsync` should NOT call force-kill in that case, just return `GracePeriodExpired` to the caller. The plan says "result path preserves `GracePeriodExpired` for caller handling" but `ExecuteAsync` returns `ProcessResult`, not `TerminationResult`. Where does `GracePeriodExpired` go? Likely orchestrator already reads `LastTerminationResult` separately, but the plan should make this explicit.
- **MEDIUM** - Removing reflection from existing tests (`AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs:136-140`) is mentioned as an acceptance criterion but the existing handle-leak test (`CleanOrphanedProcessesAsync_ShouldNotLeakProcessHandlesAcrossRepeatedRuns`) depends on reflection to seed the PID file. Plan must specify how this test is rewritten through the new `IPidStore` seam.
- **MEDIUM** - `CleanOrphanedProcessesAsync` filtering: "current-session entries blindly" - but the *current-session* entries should also be cleared once the process they reference exits. The plan says "remove only processed prior-session entries" - what happens to current-session entries that exist but are no longer running? They should be untracked too. Define the filter precisely.
- **LOW** - `OperationCanceledException` from `WaitForExitAsync` post-kill is in the catch list, but if `ct` is `CancellationToken.None` (as in the orchestrator's force-stop call), this can't fire. Worth noting it's defensive only.

### Suggestions
- Add `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` to `files_modified` in either Plan 01 or Plan 02 with explicit DI lifetime registrations:
  ```csharp
  services.AddSingleton<IPidStorePathProvider, DefaultPidStorePathProvider>();
  services.AddSingleton<IProcessSessionIdProvider, ProcessSessionIdProvider>();
  services.AddSingleton<IPidStore, JsonPidStore>();
  ```
- Specify how `ExecuteAsync` distinguishes intent. Recommended: in the catch, check `timeoutCts?.IsCancellationRequested ?? false`; if **timeout**, call `HandleInterruptedWaitAsync(reason: Timeout)` which may force-kill; if **user**, call only the graceful path and let `ProcessResult.TimedOut = false` propagate. Caller observes via `LastTerminationResult` on the orchestrator.
- Specify the rewrite path for the handle-leak test: it should now write through `IPidStore.UpdateAsync` against an injected temp `IPidStorePathProvider`.
- Define orphan-filter rule precisely: "prior-session entries are killed-and-removed; current-session entries whose PID is no longer running are also removed; current-session entries with live PIDs are preserved."

### Risk: **MEDIUM** (DI wiring + intent-propagation gaps)

---

## Plan 05-03: Orchestrator + ViewModel Stop Wiring

### Strengths
- Explicitly maps every UI string to the locked UI-SPEC copy - no ambiguity for the executor.
- Preserves D-04 second-click escalation by keeping the existing `_isStopRequested` short-circuit.
- D-09 log-read guard is correctly extended to include `ForceKillFailed` and user-decline outcomes.

### Concerns
- **HIGH** - "User declined force termination" is described as "any user-decline outcome represented by the implementation" but no concrete representation exists yet. `_lastTerminationResult` is `TerminationResult?` and no enum value covers "user declined." Either add `LeftRunningByUser` to `TerminationResult` or track decline as a separate orchestrator field that the log-read guard checks. Plan must pick one explicitly. Without this, the log-read guard cannot distinguish "grace expired and user prompt is pending" from "grace expired and user said no."
- **HIGH** - Race condition: the current code in `CleaningOrchestrator.StopCleaningAsync` calls `TerminateProcessAsync(forceKill: false)` synchronously inside the stop method. If the user clicks Stop, then *before* the ViewModel observes `LastTerminationResult` the orchestrator's main cleaning loop has already advanced (e.g., the `CleanPluginAsync` task threw `OperationCanceledException`), the ViewModel will see `_lastTerminationResult` reset to null in the `finally` block. Plan needs to specify whether `_lastTerminationResult` is preserved across the cleaning task's completion or whether the ViewModel reads it before awaiting the orchestrator's task.
- **MEDIUM** - The current `CleaningCommandsViewModel.StopCleaningAsync` reads `_orchestrator.LastTerminationResult` *after* `await _orchestrator.StopCleaningAsync()`. This works only if `StopCleaningAsync` does not return until graceful termination resolves. The orchestrator's `StopCleaningAsync` does call `TerminateProcessAsync(forceKill: false)` synchronously, so this is currently fine - but the plan should call out preserving this contract since changes to the cleaning loop could break it.
- **MEDIUM** - Acceptance criterion grep for `Force Terminate xEdit?` will match the title but not validate that `ShowConfirmAsync` (which uses YesNo, not customizable button labels) actually shows the right semantics. The UI-SPEC notes "Preferred copy if button labels become customizable" - current dialog uses generic Yes/No. Plan acknowledges this implicitly via UI-SPEC's "legacy constraint" but should make it explicit so the executor doesn't try to rewrite `IMessageDialogService`.
- **LOW** - `AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs` is created from scratch - does not yet exist. Worth flagging that the test file requires substantial substitute setup mirroring the existing orchestrator test patterns.

### Suggestions
- Add a concrete decision: introduce `TerminationResult.LeftRunningByUser` in this plan (or in 05-02) and use it as the orchestrator's signal when the user declines. Update the `<interfaces>` block.
- Document the ordering contract: "ViewModel reads `LastTerminationResult` after `await StopCleaningAsync` completes; orchestrator must not reset it until either `ForceStopCleaningAsync` runs or the cleaning session enters its `finally` block (which already nulls it)."
- Move the `_lastTerminationResult = null` reset out of the `finally` block, OR have the ViewModel snapshot the result into a local before awaiting any subsequent calls.
- Note explicitly that `IMessageDialogService` is unchanged - only copy strings change.

### Risk: **MEDIUM** (state-representation gap for "declined" outcome + result lifetime ambiguity)

---

## Plan 05-04: Single-Instance Guard + Real-Process Tests

### Strengths
- Helper executable as separate project (not as test code spawning `dotnet exec`) is the right call for determinism.
- Helper modes (`sleep`, `exit-on-stdin`, `spawn-child`) cover the four TEST-01 scenarios precisely.
- `Global\` mutex prefix scoped per-user avoids cross-session collisions while still blocking duplicates for the same user.

### Concerns
- **HIGH** - Startup-shutdown ordering in `App.axaml.cs` is fragile. The plan says "shut down the desktop lifetime before starting config watching/migration/log retention" but `OnFrameworkInitializationCompleted` is synchronous and `desktop.Shutdown()` enqueues a shutdown - it does not block. Pre-shutdown work continues unless explicitly guarded. Plan must specify either show dialog -> call `desktop.Shutdown()` -> return early from the method, or use `Environment.Exit(0)` after dialog. The current `MessageDialogService` requires a `MainWindow` owner that doesn't exist yet at this point in startup, so showing a dialog at all is non-trivial. Plan needs to address this explicitly - possibly use a minimal owner-less dialog or `MessageBox` equivalent.
- **HIGH** - Helper executable path resolution: "computes the built helper path from test output or project metadata" is hand-wavy. Common patterns include `[CallerFilePath]` with relative path navigation, using an assembly location from a test reference, or MSBuild output propagation. Plan must pick one. Without this, the integration test will fail to launch the helper on CI.
- **HIGH** - Helper project as a `ProjectReference` from `AutoQAC.Tests.csproj` has a subtle problem: a test project referencing a console executable typically doesn't copy the executable to test output by default. Plan needs `<ReferenceOutputAssembly>false</ReferenceOutputAssembly>` plus a custom `<MSBuild Targets="Build">` step OR manual `<Content Include="$(SolutionDir)AutoQAC.Tests/TestProcessHelper/bin/$(Configuration)/net10.0/AutoQAC.TestProcessHelper.exe">` to ensure the helper binary is available next to the test DLL.
- **MEDIUM** - `SingleInstanceGuard` constructor uses `initiallyOwned: true` which acquires the mutex synchronously. If a duplicate is already running, the constructor should NOT throw - it should set `HasInstanceLock = false`. The `Mutex(initiallyOwned, name, out createdNew)` pattern's `createdNew` is correct, but the plan uses `out _ownsMutex` which is a misleading name (it's really "did this call create the mutex"). When `createdNew == false`, the current process did NOT acquire ownership, but a `WaitOne(0)` call would test acquisition. The research example is correct but the variable naming and Dispose path (`_mutex.ReleaseMutex()` only when `_ownsMutex`) only works if "owns" means "acquired ownership," which `initiallyOwned: true` + `createdNew == true` does correctly imply. Document this nuance to prevent the executor from "fixing" it incorrectly.
- **MEDIUM** - Plan 04 depends on `05-02` only, but Plan 03 (orchestrator/VM) and Plan 04 (single-instance + integration tests) both target wave 3. If Plan 03 changes `ProcessExecutionService` constructor or behavior, Plan 04's integration tests against the real `ProcessExecutionService` may break. Should Plan 04 depend on `05-03` too, OR should the integration tests in Plan 04 use direct DI and not depend on orchestrator changes? Worth clarifying.
- **MEDIUM** - `dotnet test` on a project that references a console exe in the same solution can produce flaky behavior on CI when the helper binary is from a different config (e.g., test runs in Release but helper builds in Debug). Add explicit `<Configuration>` propagation.
- **LOW** - Solution file `.slnx` modification - confirm the slnx XML schema supports adding a new project entry, and that the solution path is `AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj` not the more conventional `tests/` location. Existing solution layout puts test helper *under* `AutoQAC.Tests/` which is non-standard but acceptable.

### Suggestions
- Specify the helper path resolution strategy precisely. Recommended:
  ```xml
  <ProjectReference Include="..\TestProcessHelper\AutoQAC.TestProcessHelper.csproj"
                    ReferenceOutputAssembly="false"
                    OutputItemType="Content"
                    CopyToOutputDirectory="PreserveNewest" />
  ```
  Then in test code: `Path.Combine(AppContext.BaseDirectory, "AutoQAC.TestProcessHelper.exe")`.
- For startup-shutdown: use `Environment.Exit(0)` after the duplicate-instance dialog returns. Add a WHY comment.
- Make Plan 04 depend on `05-03` to ensure orchestrator stop changes are in place when integration tests touch them. Or scope Plan 04 integration tests strictly to `ProcessExecutionService.ExecuteAsync`/`TerminateProcessAsync` and explicitly avoid orchestrator code paths.
- Pin Helper project's TFM and `OutputType=Exe` explicitly in the plan; tests will need `.exe` on Windows.

### Risk: **MEDIUM-HIGH** (startup ordering + helper-path resolution + cross-project build coordination are real execution risks)

---

## Cross-Plan Concerns

### HIGH - DI Wiring Gap
None of the four plans modifies `ServiceCollectionExtensions.cs` in `files_modified` *with explicit registrations* for `IPidStore`, `IPidStorePathProvider`, `IProcessSessionIdProvider`. Plan 04 modifies it for `ISingleInstanceGuard` only. **The phase will not run** when Plan 02 modifies the `ProcessExecutionService` constructor unless the new services are registered. This needs to be fixed in Plan 01 or Plan 02.

### MEDIUM - Untouched Comments Risk
The user's CLAUDE.md explicitly says "Never delete a comment as cleanup." `ProcessExecutionService` has substantial inline comments (especially around the cancellation handling at lines 96-119). Plan 02 substantially rewrites this region - the executor needs explicit guidance to preserve or update (not delete) the existing safety comments. Add a directive like "Preserve all existing safety comments in `ProcessExecutionService`; rewrite them only where the underlying behavior changes."

### MEDIUM - `TerminationResult` Enum Evolution
Plan 02 adds `ForceKillFailed`. Plan 03 implies but doesn't add `LeftRunningByUser`. Either Plan 02 should add both at once, or Plan 03 should explicitly add the second value. Splitting enum changes across plans creates a wave-2/wave-3 ordering hazard.

### LOW - XML Doc Comments
The CLAUDE.md policy requires XML doc comments on new/substantially-rewritten methods. All four plans mention this for *new* types but Plan 02's `ExecuteAsync` rewrite will be substantial - the existing docs need updating, not just preservation.

---

## Phase-Level Risk Assessment

**Overall: MEDIUM**

**Justification:**
- The plans correctly map to all four requirements and 18 locked decisions.
- Wave ordering and dependency graph are sound.
- The two highest-impact risks are addressable with targeted edits before execution: (1) explicit DI registration in Plan 01 or 02, and (2) explicit handling of "user declined" termination state in Plan 03.
- Plan 04's startup-shutdown ordering and helper-path resolution are real execution risks but well-scoped.
- Total scope (~10 new source files + ~4 test files + 1 helper project) is appropriate for a hardening slice and matches the research's "small process-lifecycle hardening slice, not a broad orchestrator rewrite" framing.

**Recommended pre-execution edits:**
1. **Plan 01 or 02**: Add `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` to `files_modified` with explicit DI registrations and lifetimes.
2. **Plan 02 or 03**: Add `LeftRunningByUser` to `TerminationResult` and specify how the orchestrator stores/exposes the user-decline outcome.
3. **Plan 02**: Specify how `ExecuteAsync` propagates user-stop intent without auto-forcing (since `ProcessResult` doesn't carry `TerminationResult`).
4. **Plan 03**: Document the `_lastTerminationResult` lifetime contract relative to the cleaning task's `finally` block.
5. **Plan 04**: Pick a concrete helper-path resolution strategy and a concrete startup-duplicate exit pattern.

After those five edits, the phase should execute cleanly.

---

## Codex Review

## Summary

The plans are directionally strong and trace well to Phase 5, but they still have a few execution-level gaps that could cause regressions: DI registration is missing for the new PID services, `ProcessResult`/orchestrator APIs do not yet carry enough termination outcome information, and the "leave xEdit running" path risks disposing/untracking the process too early. I'd treat the phase as feasible, but not ready to execute unchanged.

## 05-01 PID Store Foundation

### Strengths
- Good scoped extraction: PID path, store, and session ID abstractions directly address REF-04.
- Corrupt-file preservation is the right behavior.
- Session ID migration via empty string is a reasonable compatibility path.

### Concerns
- **HIGH:** `FileStream.Lock(0, 1)` does not by itself guarantee waiting under contention; concurrent `UpdateAsync` may throw instead of preserving both writes unless there is bounded retry or a process-local semaphore.
- **HIGH:** No DI registration is planned for `IPidStore`, `IPidStorePathProvider`, or `IProcessSessionIdProvider`, which will break Plan 02 constructor changes.
- **MEDIUM:** "Unreadable JSON" is too broad. Access-denied or sharing failures should not be silently converted into an empty PID store.
- **MEDIUM:** Recovery writes from `LoadAsync` must also happen under the same lock.

### Suggestions
- Add bounded lock acquisition retry/backoff and a process-local `SemaphoreSlim`.
- Register PID services in Plan 01 or explicitly move DI work into Plan 02.
- Limit corruption recovery to JSON/format errors; let permission/sharing failures surface or log distinctly.

### Risk Assessment
**MEDIUM.** The design is sound, but lock semantics and DI gaps need tightening before implementation.

## 05-02 Process Termination Semantics

### Strengths
- Correctly separates timeout auto-force from user-requested stop.
- `ForceKillFailed` is necessary and well aligned with SAF-02.
- Session-aware orphan cleanup is the right replacement for clearing the whole PID file.

### Concerns
- **HIGH:** `ExecuteAsync` still returns only `ProcessResult` with `ExitCode`/`TimedOut`; there is no clear way to return `GracePeriodExpired` or `ForceKillFailed` to callers.
- **HIGH:** If user stop leaves xEdit running, `using var process` and `finally UntrackProcessAsync` may dispose the process handle and remove PID evidence while the real process is still alive.
- **HIGH:** Testing `Process.Kill` exception branches is hard with raw `System.Diagnostics.Process`; the plan needs a seam or integration scenario.
- **MEDIUM:** `CleanOrphanedProcessesAsync` ignoring current-session entries may be unsafe if the user declined force termination and starts another cleaning session in the same app run.

### Suggestions
- Extend `ProcessResult` with `TerminationResult?`, or add explicit execution options/result modeling.
- Do not untrack when the process may still be running.
- Clarify process object ownership when `onProcessStarted` hands the process to the orchestrator.
- Add a wrapper/factory seam for kill/wait failure unit tests, or move those assertions to controlled integration tests.

### Risk Assessment
**HIGH.** This is the core behavioral fix, and the current API shape is not sufficient to carry the new semantics safely.

## 05-03 Orchestrator/ViewModel Stop Flow

### Strengths
- Captures the right user-facing behaviors: first prompt, decline leaves xEdit running, second stop escalates.
- Explicitly blocks log parsing when xEdit may still be writing.
- Keeps process control out of the ViewModel.

### Concerns
- **HIGH:** `LastTerminationResult` is a fragile side channel; `StartCleaningAsync` currently resets it in `finally`, so the ViewModel can miss outcomes during races.
- **HIGH:** `ForceStopCleaningAsync` returns `Task`, so force-kill failure is only observable through mutable state.
- **MEDIUM:** There is no explicit `LeftRunningByUser`/declined outcome, making later state and log-parse decisions ambiguous.
- **MEDIUM:** The plan says "set status to a warning outcome" but does not identify the state/result model to carry that outcome.

### Suggestions
- Change orchestrator stop APIs to return a structured stop outcome instead of relying only on `LastTerminationResult`.
- Add an explicit declined/left-running outcome.
- Store unsafe-log-read state per plugin/attempt, not only as a global last termination result.

### Risk Assessment
**HIGH.** The UX goal is correct, but outcome propagation needs a stronger contract to avoid race-prone behavior.

## 05-04 Single Instance & Integration Tests

### Strengths
- Single-instance protection is a good complement to PID locking.
- Controlled helper processes are the right alternative to launching xEdit.
- Default-running integration tests match the phase requirement.

### Concerns
- **HIGH:** `Global\AutoQAC-{Environment.UserName}` may have namespace/permission/name issues. A `Local\` app-specific mutex name is safer for a per-user desktop app.
- **HIGH:** A console `exit-on-stdin` helper will not exercise `CloseMainWindow` success; console processes usually produce `CloseMainWindow == false`.
- **HIGH:** Showing a duplicate-instance dialog before `MainWindow` exists may not work with the current dialog service unless it supports ownerless startup dialogs.
- **MEDIUM:** ProjectReference may build the helper but not make the executable path easy to locate; the plan needs explicit copy/path mechanics.
- **LOW:** Full `dotnet test` can hit Avalonia resource file-lock noise; validation should account for a sequential rerun if that known build artifact issue appears.

### Suggestions
- Use a stable `Local\AutoQAC` mutex name, or sanitize/hash any user-specific suffix.
- Either add a tiny windowed helper for real graceful-close coverage or narrow the helper test claim and cover graceful-close via an abstraction seam.
- Define startup duplicate handling without depending on an owner window.
- Add explicit helper output copy metadata or a reliable path resolver.

### Risk Assessment
**MEDIUM-HIGH.** The harness is valuable, but the graceful-stop test and startup dialog mechanics need sharper design.

## Overall Risk

**MEDIUM-HIGH.** The phase is well decomposed and mostly addresses SAF-01, SAF-02, REF-04, and TEST-01, but the plans need stronger contracts for termination outcomes, process ownership, PID retention when xEdit is left running, and DI/test wiring. I would revise 05-01/05-02 before execution, then let 05-03 and 05-04 build on those corrected contracts.

---

## Consensus Summary

Gemini considered the plan set broadly ready with low residual risk, while Claude and Codex found execution-blocking gaps that should be fed back into planning before implementation. The shared conclusion is that the phase direction is correct: the plan decomposition, cancellation-intent separation, PID store extraction, UI confirmation flow, and controlled process tests all align with Phase 5. The important follow-up is to strengthen the contracts and wiring before execution so the implementation does not fail at DI, lose stop outcomes, or leave ambiguous process/PID state when xEdit remains alive.

### Agreed Strengths

- The phase is well-scoped to process stop/PID safety and does not drift into later command escaping or backup work.
- Separating user-requested stop from timeout-driven termination is the right fix for SAF-01.
- Adding `ForceKillFailed` is necessary for accurate SAF-02 reporting.
- Extracting PID storage/path/session abstractions directly supports REF-04 and removes reflection-based testing pressure.
- Controlled helper-process integration tests are the right path for TEST-01, provided the helper build/path mechanics are made explicit.

### Agreed Concerns

- **HIGH: Missing DI registration for new PID services.** Claude and Codex both flagged that `ProcessExecutionService` constructor changes will break app startup unless `IPidStorePathProvider`, `IProcessSessionIdProvider`, and `IPidStore` are registered with explicit lifetimes in `ServiceCollectionExtensions` before Plan 02 lands.
- **HIGH: Termination outcome propagation is underspecified.** Claude and Codex both questioned how `ExecuteAsync`, `ProcessResult`, `LastTerminationResult`, `ForceStopCleaningAsync`, and ViewModel code will reliably carry `GracePeriodExpired`, `ForceKillFailed`, and the user-declined/left-running outcome without races or mutable-state loss.
- **HIGH: Left-running process ownership and PID retention need a concrete contract.** Codex specifically flagged that `using var process` and unconditional `UntrackProcessAsync` may dispose/untrack while xEdit is still alive. Claude similarly flagged current-session orphan filtering and user-decline ambiguity.
- **MEDIUM/HIGH: PID lock semantics need tightening.** Claude and Codex both flagged that all cooperating reads and recovery writes must participate in the lock, and Codex recommended bounded retry/backoff or a process-local `SemaphoreSlim` so contention does not become a failed update.
- **MEDIUM/HIGH: Plan 04 needs concrete mechanics.** All reviewers noted some combination of helper executable path resolution, build/copy wiring, mutex naming/permissions, startup duplicate dialog ownership, or console-helper limitations around `CloseMainWindow`.

### Divergent Views

- **Readiness:** Gemini judged the plans ready for execution with low risk. Claude and Codex judged them feasible but not ready unchanged, mostly because of DI, outcome contract, and test harness details.
- **PID locking severity:** Gemini considered the locking design low risk if kept off the UI thread. Claude and Codex treated lock behavior as a correctness issue requiring explicit read locking, recovery locking, and contention handling.
- **API shape:** Claude suggested making existing `LastTerminationResult` behavior explicit and adding `LeftRunningByUser`; Codex recommended stronger structured stop outcomes and possibly extending `ProcessResult` with `TerminationResult?`.
- **Single-instance mutex:** Gemini accepted `Global\AutoQAC-{Environment.UserName}` with a caution. Codex preferred a stable `Local\AutoQAC`-style mutex for a per-user desktop app.

### Recommended Plan Updates Before Execution

1. Add `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` to Plan 01 or Plan 02 and register `IPidStorePathProvider`, `IProcessSessionIdProvider`, and `IPidStore` with explicit singleton lifetimes.
2. Specify the concrete termination outcome contract: how `GracePeriodExpired`, `ForceKillFailed`, and user-declined/left-running results flow from process service to orchestrator to ViewModel without relying on a fragile side channel.
3. Add an explicit `LeftRunningByUser` outcome or an equivalent structured stop result, and define log-read blocking around that outcome.
4. Define process ownership and PID retention when xEdit is intentionally left running or force-kill fails; do not untrack/dispose in a way that loses evidence of a live process.
5. Tighten `JsonPidStore` semantics so `LoadAsync`, `UpdateAsync`, and corruption recovery all use the same lock, distinguish JSON corruption from access/sharing failures, and handle lock contention deterministically.
6. Make Plan 04 concrete on helper project copy/path mechanics, startup duplicate handling before `MainWindow`, mutex namespace choice, and whether console helpers are sufficient for graceful-close coverage.
