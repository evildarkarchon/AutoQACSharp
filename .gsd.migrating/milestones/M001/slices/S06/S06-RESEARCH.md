# Phase 5: Process Stop & PID Safety - Research

**Researched:** 2026-04-28  
**Domain:** Windows .NET process lifecycle, MVVM stop coordination, PID persistence safety, integration testing  
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Stop Ownership
- **D-01:** User-initiated force-kill escalation belongs in the orchestrator/ViewModel confirmation flow, not as automatic escalation inside `ProcessExecutionService.ExecuteAsync`.
- **D-02:** For user-initiated Stop, the process service should report `GracePeriodExpired` after graceful termination fails; AutoQAC should prompt before force-killing.
- **D-03:** If the user declines the force-terminate prompt, leave xEdit running and report that AutoQAC stopped/cancelled while xEdit was left running by user choice.
- **D-04:** A second Stop click during the grace/confirmation path is explicit escalation and may force-kill immediately without another prompt.
- **D-05:** Timeout handling may still auto-force after the grace period. The no-auto-force rule is specifically for user-initiated Stop.

#### Kill Outcomes
- **D-06:** Add a distinct termination result for failed force-kill attempts, such as `ForceKillFailed`; do not report `ForceKilled` when `Process.Kill` or the post-kill wait fails.
- **D-07:** `ForceKilled` means `Kill(entireProcessTree: true)` was invoked and the tracked/root process exited. Descendant-process uncertainty may be logged but does not require proving every descendant exited.
- **D-08:** Force-kill failure should propagate to user-visible state and a concise dialog; detailed exception information belongs in logs.
- **D-09:** Failed force-kill should block post-exit log parsing for the affected plugin because xEdit may still be running or flushing logs.

#### PID Storage
- **D-10:** Keep the JSON PID store, but introduce injected PID storage/path abstractions so tests can control storage without reflection.
- **D-11:** Protect PID store read-modify-write operations with an interprocess file lock.
- **D-12:** If the PID file is corrupt, preserve a timestamped copy, log corruption metadata, and recreate a clean PID store.
- **D-13:** Add a session ID to PID entries so orphan cleanup can distinguish current-run and prior-run entries without adding extra executable path exposure.
- **D-14:** Add a single-instance app lock that blocks multiple AutoQAC instances.

#### Process Tests
- **D-15:** Real child-process tests should live under `AutoQAC.Tests` as integration/process coverage rather than in a new test project.
- **D-16:** The real-process tests should run by default with `dotnet test`, provided they use controlled short-lived local helper processes and reliable cleanup.
- **D-17:** Use a tiny test helper executable/project that can sleep, ignore graceful close, exit on command, and support force-kill scenarios.
- **D-18:** Process-test timing should be generous but bounded, with cleanup in `finally` paths to avoid flaky hangs or orphaned helper processes.

### the agent's Discretion
- Exact names for new interfaces/classes, result enum values, session ID format, lock implementation details, and test helper project naming are left to downstream research/planning.
- The planner may choose the smallest internal API changes that satisfy the decisions above, as long as user-visible stop semantics are preserved.

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SAF-01 | User can stop cleaning without AutoQAC force-killing xEdit before the confirmation path is shown. | Use a two-stage termination state machine: first stop cancels/waits gracefully and returns `GracePeriodExpired`; ViewModel prompts; second stop escalates. [VERIFIED: `.planning/phases/05-process-stop-pid-safety/05-CONTEXT.md`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, Microsoft Process docs] |
| SAF-02 | User can see an accurate failure outcome when force-killing xEdit fails. | Add `ForceKillFailed` and propagate it to state/dialog; `Process.Kill` can throw `Win32Exception`, `InvalidOperationException`, `NotSupportedException`, and `AggregateException` for process tree cases. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill] |
| REF-04 | Maintainer can test PID tracking through injected storage/path abstractions with process-safe update behavior. | Extract PID store path, clock/session ID, file I/O, and file lock abstractions; existing tests use reflection because PID path is private. [VERIFIED: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`; CITED: https://learn.microsoft.com/dotnet/api/system.io.filestream.lock] |
| TEST-01 | Maintainer can verify real child-process timeout, graceful stop, force kill, and PID cleanup behavior through controlled integration tests. | Add a test helper console executable and process integration tests under `AutoQAC.Tests`; existing process tests explicitly do not spawn real processes. [VERIFIED: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`, `.planning/codebase/TESTING.md`] |
</phase_requirements>

## Project Constraints (from AGENTS.md)

- Preserve Windows-only Avalonia desktop assumptions and `net10.0-windows10.0.19041.0` app/test target when touching process behavior. [VERIFIED: `AGENTS.md`, `AutoQAC/AutoQAC.csproj`, `AutoQAC.Tests/AutoQAC.Tests.csproj`]
- Preserve sequential cleaning; do not parallelize plugin cleaning or xEdit launches. [VERIFIED: `AGENTS.md`, `.planning/codebase/ARCHITECTURE.md`]
- Keep `ProcessExecutionService` as a single process slot. [VERIFIED: `AGENTS.md`, `AutoQAC/Services/Process/ProcessExecutionService.cs`]
- Keep dialog/window interaction out of ViewModels except through established interaction/UI service boundaries; `MainWindow.axaml.cs` owns window interactions. [VERIFIED: `AGENTS.md`, `.planning/codebase/ARCHITECTURE.md`]
- Use CommunityToolkit.Mvvm source generators for ViewModel state and do not introduce ReactiveUI/System.Reactive in the ViewModel layer. [VERIFIED: `AGENTS.md`]
- Keep I/O and process work async; do not block the UI thread with `.Result` or `.Wait()`. [VERIFIED: `AGENTS.md`]
- Register new services through `ServiceCollectionExtensions`; avoid static mutable state and service locators. [VERIFIED: `AGENTS.md`, `.planning/codebase/ARCHITECTURE.md`]
- Use xUnit, FluentAssertions, NSubstitute, and explicit optional parameters in substitute setups/assertions. [VERIFIED: `AGENTS.md`, `.planning/codebase/TESTING.md`]
- Do not add or depend on an Avalonia.Headless test project for this phase. [VERIFIED: `AGENTS.md`, `.planning/codebase/TESTING.md`]
- Do not modify `Mutagen/`; it is read-only. [VERIFIED: `AGENTS.md`]
- Preserve accurate comments, add XML doc comments for new/substantially rewritten methods, and add WHY comments for non-obvious race/cancellation/cleanup behavior. [VERIFIED: `C:\Users\evild\.config\opencode\AGENTS.md`]

## Summary

Phase 5 should be implemented as a small process-lifecycle hardening slice, not as a broad orchestrator rewrite. The established architecture is a three-tier local desktop flow: `CleaningCommandsViewModel` requests stop and shows confirmation, `CleaningOrchestrator` owns stop state and current process coordination, and `ProcessExecutionService` owns low-level process/PID operations while preserving the single xEdit process slot. [VERIFIED: `.planning/codebase/ARCHITECTURE.md`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`]

The most important current mismatch is that `ExecuteAsync` catches cancellation, performs graceful termination, and then force-kills automatically when the grace period expires, which bypasses the user-confirmation path for user-initiated Stop. [VERIFIED: `AutoQAC/Services/Process/ProcessExecutionService.cs:102-119`, `.planning/codebase/CONCERNS.md`] The second important mismatch is that `TerminateProcessAsync(forceKill: true)` catches `Win32Exception` and returns `ForceKilled`, even though Microsoft documents `Process.Kill` can fail and throw. [VERIFIED: `AutoQAC/Services/Process/ProcessExecutionService.cs:180-184`; CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill]

**Primary recommendation:** Introduce explicit termination intent/outcome modeling plus injected PID storage/locking abstractions; keep UI confirmation in the ViewModel/orchestrator path, keep timeout auto-force distinct from user stop, and add real-process integration tests with a tiny helper executable. [VERIFIED: `05-CONTEXT.md`, Microsoft Process docs, `.planning/codebase/TESTING.md`]

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|--------------|----------------|-----------|
| First user Stop request | ViewModel / Application workflow | Process service | The ViewModel command is the user entry point and orchestrator owns `_isStopRequested`/current process state; the process service should only perform requested termination operations. [VERIFIED: `CleaningCommandsViewModel.cs`, `CleaningOrchestrator.cs`] |
| Force-kill confirmation | ViewModel / UI service | Orchestrator | User confirmation belongs at the UI boundary; orchestrator exposes/returns `GracePeriodExpired` state for that prompt. [VERIFIED: `05-CONTEXT.md`, `CleaningCommandsViewModel.cs:215-225`] |
| Low-level graceful close | Process service | — | `CloseMainWindow` is a local process API operation and should be wrapped by process service for testable semantics. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.closemainwindow] |
| Low-level force kill | Process service | Orchestrator | `Kill(entireProcessTree: true)` and post-kill wait are process API operations; orchestrator decides when to request force kill. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill] |
| PID persistence | Process service infrastructure | File system | PID JSON is process infrastructure state, not UI or cleaning business logic. [VERIFIED: `ProcessExecutionService.cs:225-405`] |
| Single-instance app lock | Application bootstrap / infrastructure | Process service | A named mutex is an interprocess synchronization primitive; app startup should acquire it before allowing a second UI/session. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.threading.mutex] |
| Real-process tests | Test project | Test helper executable | The test suite already lives under `AutoQAC.Tests`; helper executable provides controlled child-process behavior without launching xEdit. [VERIFIED: `05-CONTEXT.md`, `.planning/codebase/TESTING.md`] |

## Standard Stack

### Core

| Library / API | Version | Purpose | Why Standard |
|---------------|---------|---------|--------------|
| .NET SDK / BCL | SDK 10.0.203 installed; projects target .NET 10 | Process lifecycle, file I/O, named mutex, async waits | Existing project platform and official APIs cover required behavior without extra packages. [VERIFIED: `dotnet --version`, `AutoQAC/*.csproj`; CITED: Microsoft Learn Process/FileStream/Mutex docs] |
| `System.Diagnostics.Process` | .NET 10 BCL | `CloseMainWindow`, `Kill(true)`, `WaitForExitAsync` | Official process lifecycle abstraction already used by `ProcessExecutionService`. [VERIFIED: `ProcessExecutionService.cs`; CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process] |
| `System.IO.FileStream.Lock` | .NET 10 BCL | Interprocess file lock for PID read-modify-write | Official API prevents other processes from reading/writing a locked file region. [CITED: https://learn.microsoft.com/dotnet/api/system.io.filestream.lock] |
| `System.Threading.Mutex` | .NET 10 BCL | Single-instance app lock | Official named system mutexes are visible across processes and suitable for interprocess synchronization. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.threading.mutex] |
| `System.Text.Json` | .NET 10 BCL | PID JSON serialization | Existing PID store already uses it; no need to add Newtonsoft.Json/YamlDotNet for this store. [VERIFIED: `ProcessExecutionService.cs:7`, `ProcessExecutionService.cs:392-404`] |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xUnit | 2.9.3 | Unit/integration tests | Keep all Phase 5 tests in `AutoQAC.Tests` and add process integration tests there. [VERIFIED: `AutoQAC.Tests/AutoQAC.Tests.csproj`, `05-CONTEXT.md`] |
| FluentAssertions | 8.8.0 | Behavior assertions | Use `.Should()` assertions with reason strings, matching existing test style. [VERIFIED: `AutoQAC.Tests/AutoQAC.Tests.csproj`, `.planning/codebase/TESTING.md`] |
| NSubstitute | 5.3.0 | Mocking service boundaries | Use for orchestrator/ViewModel unit tests; use real helper processes for `ProcessExecutionService` integration tests. [VERIFIED: `AutoQAC.Tests/AutoQAC.Tests.csproj`, `.planning/codebase/TESTING.md`] |
| CommunityToolkit.Mvvm | 8.4.2 | ViewModel commands/properties | Preserve `[RelayCommand]` and source-generator patterns when touching stop command state. [VERIFIED: `AutoQAC/AutoQAC.csproj`, `CleaningCommandsViewModel.cs`] |
| Microsoft.Extensions.DependencyInjection | 10.0.3 | Service registration | Register new PID store/path/session/single-instance services in `ServiceCollectionExtensions`. [VERIFIED: `AutoQAC/AutoQAC.csproj`, `AGENTS.md`] |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| BCL `Process` | CliWrap / MedallionShell | Extra packages add abstraction but do not remove Windows `CloseMainWindow`/`Kill(true)` semantics; current phase needs precise existing API handling. [ASSUMED] |
| `FileStream.Lock` | Named mutex around PID file only | A mutex can coordinate cooperating AutoQAC instances, but file locking protects the actual PID file from concurrent read/write access by cooperating processes using the lock. [CITED: FileStream.Lock docs; ASSUMED: design tradeoff] |
| JSON PID store | SQLite | SQLite would solve concurrency but is excessive for a small PID list and adds a dependency not required by the phase decisions. [ASSUMED] |

**Installation:**
```bash
# No new NuGet packages are required for Phase 5. [VERIFIED: project csproj files + Microsoft BCL docs]
dotnet restore AutoQACSharp.slnx
```

**Version verification:** Existing package versions were verified from `AutoQAC/AutoQAC.csproj`, `AutoQAC.Tests/AutoQAC.Tests.csproj`, and `dotnet --version` during this session. [VERIFIED: file reads + `dotnet --version`]

## Architecture Patterns

### System Architecture Diagram

```text
User clicks Stop
      |
      v
CleaningCommandsViewModel.StopCleaningAsync
      |  (asks orchestrator; shows force-kill confirmation only after GracePeriodExpired)
      v
CleaningOrchestrator stop state machine
      |-- first Stop --> cancel cleaning CTS + graceful close current process
      |                  |
      |                  v
      |          ProcessExecutionService.TryGracefulStopAsync
      |                  |
      |        AlreadyExited / GracefulExit / GracePeriodExpired
      |
      |-- prompt accepted or second Stop --> force-kill request
      |                                  |
      |                                  v
      |                        ProcessExecutionService.ForceKillAsync
      |                                  |
      |               ForceKilled / ForceKillFailed / AlreadyExited
      v
StateService + concise dialog outcome
      |
      v
Skip post-exit log parsing when xEdit may still be alive

Process start/exit side path:
ProcessExecutionService.ExecuteAsync
      |
      v
IPidStore.UpdateUnderLockAsync(JSON read-modify-write + session ID)
      |
      v
Startup/pre-clean CleanOrphanedProcessesAsync filters prior sessions and xEdit identity
```

### Recommended Project Structure

```text
AutoQAC/
├── Models/
│   ├── TerminationResult.cs              # add ForceKillFailed / possibly LeftRunningByUser
│   └── TrackedProcess.cs                 # add SessionId if not already separate
├── Services/Process/
│   ├── IProcessExecutionService.cs       # explicit stop/force APIs or options
│   ├── ProcessExecutionService.cs        # low-level Process API wrapper, single slot preserved
│   ├── IPidStore.cs                      # injected JSON PID storage abstraction
│   ├── JsonPidStore.cs                   # file-lock-protected read-modify-write
│   ├── IPidStorePathProvider.cs          # production/test path source
│   ├── IProcessSessionIdProvider.cs      # stable app-run session ID
│   └── ISingleInstanceGuard.cs           # named mutex wrapper for startup
└── Infrastructure/
    └── ServiceCollectionExtensions.cs    # DI registration

AutoQAC.Tests/
├── Services/
│   ├── ProcessExecutionServiceTests.cs
│   ├── JsonPidStoreTests.cs
│   └── ProcessExecutionIntegrationTests.cs
└── TestProcessHelper/
    └── AutoQAC.TestProcessHelper.csproj  # tiny console helper if added as test-only project
```

### Pattern 1: Explicit Termination Intent

**What:** Split user-stop cancellation from timeout cancellation so `ExecuteAsync` can auto-force only for timeouts while user Stop returns `GracePeriodExpired` for UI confirmation. [VERIFIED: `05-CONTEXT.md`; VERIFIED: current conflation in `ProcessExecutionService.cs:96-119`]

**When to use:** Use whenever the caller's cancellation token can mean multiple business intents. [VERIFIED: phase concern in `.planning/codebase/CONCERNS.md`]

**Example:**
```csharp
// Source: Phase 5 research synthesis from current code + Microsoft WaitForExitAsync docs.
public enum ProcessStopReason
{
    Timeout,
    UserRequestedStop
}

private async Task<TerminationResult> HandleInterruptedWaitAsync(
    Process process,
    ProcessStopReason reason,
    CancellationToken shutdownToken)
{
    var graceful = await TerminateProcessAsync(process, forceKill: false, shutdownToken)
        .ConfigureAwait(false);

    if (graceful != TerminationResult.GracePeriodExpired)
    {
        return graceful;
    }

    return reason == ProcessStopReason.Timeout
        ? await TerminateProcessAsync(process, forceKill: true, shutdownToken).ConfigureAwait(false)
        : TerminationResult.GracePeriodExpired;
}
```

### Pattern 2: Result-Based Force Kill

**What:** Treat kill invocation, root-process exit, and failure as separate observable outcomes; return `ForceKillFailed` when `Kill(true)` or post-kill wait fails. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill]

**When to use:** Use for any process termination path shown to users or used to decide whether logs are safe to parse. [VERIFIED: `05-CONTEXT.md`]

**Example:**
```csharp
// Source: Microsoft Process.Kill and WaitForExitAsync docs.
try
{
    process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync(ct).ConfigureAwait(false);
    return TerminationResult.ForceKilled;
}
catch (InvalidOperationException)
{
    return TerminationResult.AlreadyExited;
}
catch (Exception ex) when (ex is Win32Exception or NotSupportedException or AggregateException or OperationCanceledException)
{
    logger.Error(ex, "[Termination] Failed to force kill process tree (PID: {Pid})", process.Id);
    return TerminationResult.ForceKillFailed;
}
```

### Pattern 3: File-Locked PID Store with Corruption Preservation

**What:** Encapsulate PID JSON load/save behind an injected store that opens a lock file or the PID file, locks a fixed byte range, reads, mutates, truncates/writes, flushes, and unlocks/disposes. [CITED: https://learn.microsoft.com/dotnet/api/system.io.filestream.lock]

**When to use:** Use for every `TrackProcessAsync`, `UntrackProcessAsync`, and orphan cleanup read-modify-write operation. [VERIFIED: `ProcessExecutionService.cs:227-324`]

**Example:**
```csharp
// Source: FileStream.Lock docs + current PID JSON store behavior.
public async Task UpdateAsync(Func<IReadOnlyList<TrackedProcess>, IReadOnlyList<TrackedProcess>> update, CancellationToken ct)
{
    await using var stream = new FileStream(
        _pathProvider.PidFilePath,
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.ReadWrite,
        bufferSize: 4096,
        useAsync: true);

    stream.Lock(0, 1);
    try
    {
        var current = await ReadOrRecoverAsync(stream, ct).ConfigureAwait(false);
        var next = update(current);
        stream.SetLength(0);
        await JsonSerializer.SerializeAsync(stream, next, _jsonOptions, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
    finally
    {
        stream.Unlock(0, 1);
    }
}
```

### Pattern 4: Controlled Real-Process Integration Tests

**What:** Use a small helper executable that supports modes such as `sleep`, `exit-on-stdin`, `ignore-close`, and `spawn-child`, then always kill leftovers in `finally`. [VERIFIED: `05-CONTEXT.md`; ASSUMED: helper modes are recommended implementation details]

**When to use:** Use for `TEST-01` scenarios that unit tests cannot simulate: real timeout, real process exit, force kill, and PID cleanup. [VERIFIED: `.planning/codebase/CONCERNS.md`, `.planning/codebase/TESTING.md`]

**Example:**
```csharp
// Source: existing test style in .planning/codebase/TESTING.md.
[Fact]
public async Task ExecuteAsync_WhenChildExceedsTimeout_ShouldReturnTimedOutAndCleanupPid()
{
    Process? helper = null;
    try
    {
        var startInfo = TestProcessHelper.StartInfo("sleep", "00:00:30");
        var result = await _service.ExecuteAsync(startInfo, timeout: TimeSpan.FromMilliseconds(250));

        result.TimedOut.Should().BeTrue("the helper intentionally outlives the timeout");
        (await _pidStore.LoadAsync()).Should().BeEmpty("timed-out processes must be untracked after cleanup");
    }
    finally
    {
        helper?.Kill(entireProcessTree: true);
        helper?.Dispose();
    }
}
```

### Anti-Patterns to Avoid

- **Auto-force on user cancellation inside `ExecuteAsync`:** This bypasses the confirmation path and violates SAF-01. [VERIFIED: `05-CONTEXT.md`, current `ProcessExecutionService.cs:114-119`]
- **Returning `ForceKilled` from a catch block:** This hides access/OS/process-state failures from the user and violates SAF-02. [VERIFIED: current `ProcessExecutionService.cs:180-184`; CITED: Process.Kill docs]
- **Reflection to reach PID paths in tests:** This is the existing testability smell that REF-04 exists to remove. [VERIFIED: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs:136-140`]
- **Clearing the entire PID file after orphan scan:** This can discard current-session entries unless session IDs and locked update semantics are used. [VERIFIED: current `ProcessExecutionService.cs:323-324`; ASSUMED: risk analysis]
- **Blocking waits in UI flow:** `.Wait()`/`.Result` would violate project async/UI constraints. [VERIFIED: `AGENTS.md`]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Process termination primitives | Custom Win32 `TerminateProcess`/process tree traversal | `Process.CloseMainWindow`, `Process.Kill(entireProcessTree: true)`, `WaitForExitAsync` | Official APIs already define close, abnormal kill, async wait, and documented exception semantics. [CITED: Microsoft Process docs] |
| Cross-process single-instance gate | Polling process names or PID files | Named `System.Threading.Mutex` wrapper | Named system mutexes are OS-visible across processes. [CITED: Mutex docs] |
| PID file concurrency | Unsynchronized read-modify-write or ad hoc retry loops | `FileStream.Lock` plus atomic update section | File locks prevent other processes from reading/writing the locked region. [CITED: FileStream.Lock docs] |
| JSON parsing/recovery | String slicing or regex JSON repair | `System.Text.Json` plus preserve corrupt file and recreate | Existing store is JSON and BCL serializer is already used. [VERIFIED: current `ProcessExecutionService.cs`] |
| Test process behavior | Launching real xEdit/cmd scripts | Tiny test helper executable under tests | Controlled helper processes are deterministic and avoid real user tools. [VERIFIED: `05-CONTEXT.md`; ASSUMED: helper executable pattern] |

**Key insight:** The hard part is not killing a process; it is preserving user intent and observability across asynchronous cancellation, graceful close, forced termination, PID cleanup, and UI confirmation. [VERIFIED: `.planning/codebase/CONCERNS.md`, `05-CONTEXT.md`]

## Runtime State Inventory

| Category | Items Found | Action Required |
|----------|-------------|-----------------|
| Stored data | `AutoQAC Data/autoqac-pids.json` stores PID entries with `Pid`, `StartTime`, and `PluginName`. [VERIFIED: `ProcessExecutionService.cs`, current `TrackedProcess` usage] | Add `SessionId`; implement locked migration-tolerant read that treats missing session IDs as prior-session entries; preserve corrupt JSON before recreating. [VERIFIED: `05-CONTEXT.md`] |
| Live service config | None — phase uses local desktop app services and no external service UI/database configuration was found in phase scope. [VERIFIED: `AGENTS.md`, `.planning/codebase/ARCHITECTURE.md`] | None. |
| OS-registered state | New named mutex state will exist only while the process is running. [CITED: Mutex docs] | Acquire at app startup; release on app shutdown/dispose; test duplicate acquisition behavior through abstraction. [ASSUMED: implementation detail] |
| Secrets/env vars | None — no secret or environment variable dependency is required for process stop/PID safety. [VERIFIED: phase context and project docs] | None. |
| Build artifacts | Potential new test helper executable output under `AutoQAC.Tests` build output. [ASSUMED] | Ensure test cleanup kills helper processes and does not rely on private output paths; reference helper project or compute path through test infrastructure. [ASSUMED] |

## Common Pitfalls

### Pitfall 1: Treating Cancellation as a Single Meaning
**What goes wrong:** User Stop, timeout, and app shutdown all look like `OperationCanceledException`, causing user Stop to auto-force-kill. [VERIFIED: current `ProcessExecutionService.cs:102-119`]  
**Why it happens:** The current linked token joins timeout and caller cancellation, then termination code only checks whether timeout token fired. [VERIFIED: `ProcessExecutionService.cs:87-105`]  
**How to avoid:** Carry explicit stop reason/intent into the interrupted-wait handler and branch timeout auto-force separately from user stop prompt. [VERIFIED: `05-CONTEXT.md`]  
**Warning signs:** Tests assert only `TimedOut`/exit code and do not assert confirmation prompt ordering. [VERIFIED: existing tests]

### Pitfall 2: Trusting `Kill(true)` Too Much
**What goes wrong:** UI reports force-killed even when `Kill` or post-kill wait failed. [VERIFIED: current `ProcessExecutionService.cs:180-184`]  
**Why it happens:** Exception handling treats `Win32Exception` as best effort success. [VERIFIED: current code]  
**How to avoid:** Return `ForceKillFailed`; log exception details; block post-exit log parsing when failure leaves xEdit possibly running. [VERIFIED: `05-CONTEXT.md`; CITED: Process.Kill docs]  
**Warning signs:** Catch blocks returning `ForceKilled` or ignoring `OperationCanceledException` from post-kill wait. [CITED: WaitForExitAsync docs]

### Pitfall 3: Assuming Root Process Exit Means Entire Tree Exit
**What goes wrong:** Code asserts every child process is gone after `Kill(true)` and `WaitForExitAsync` on the root process. [CITED: Process.Kill docs]  
**Why it happens:** Microsoft documents that `WaitForExit`/`HasExited` do not reflect descendant status after `Kill(entireProcessTree: true)`. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill]  
**How to avoid:** Define `ForceKilled` exactly as root/tracked process exited after `Kill(true)` invocation, and log descendant uncertainty instead of requiring proof. [VERIFIED: `05-CONTEXT.md`]  
**Warning signs:** Tests fail because a descendant lingers despite the root exiting. [ASSUMED]

### Pitfall 4: PID File Lost Updates
**What goes wrong:** Two instances read the same PID file, write different updates, and lose one entry. [VERIFIED: `.planning/codebase/CONCERNS.md`]  
**Why it happens:** Current load/save operations are separate and have no cross-process lock. [VERIFIED: `ProcessExecutionService.cs:227-267`]  
**How to avoid:** Run every PID mutation inside a `FileStream.Lock`-protected critical section and add a single-instance mutex. [CITED: FileStream.Lock docs; CITED: Mutex docs; VERIFIED: `05-CONTEXT.md`]  
**Warning signs:** Tests need sleeps/retries to make PID file updates pass. [ASSUMED]

### Pitfall 5: Flaky Real-Process Tests
**What goes wrong:** Integration tests hang or leave helper processes behind. [ASSUMED]  
**Why it happens:** Process tests use tight timing, no `finally`, or unbounded waits. [VERIFIED: `05-CONTEXT.md`]  
**How to avoid:** Use short helper modes, generous bounded waits, `TaskCompletionSource` coordination, and `finally` cleanup that kills the process tree. [VERIFIED: `05-CONTEXT.md`, `.planning/codebase/TESTING.md`]  
**Warning signs:** Tests use `Thread.Sleep` instead of a readiness signal or `WaitAsync(TimeSpan)`. [ASSUMED]

## Code Examples

Verified patterns from official/project sources:

### Graceful Close Is a Request, Not a Force
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.closemainwindow
var closeMessageSent = process.CloseMainWindow();
if (!closeMessageSent)
{
    return TerminationResult.GracePeriodExpired;
}

using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
graceCts.CancelAfter(TimeSpan.FromMilliseconds(2500));

try
{
    await process.WaitForExitAsync(graceCts.Token).ConfigureAwait(false);
    return TerminationResult.GracefulExit;
}
catch (OperationCanceledException) when (!ct.IsCancellationRequested)
{
    return TerminationResult.GracePeriodExpired;
}
```

### Force Kill Failure Is a First-Class Outcome
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill
try
{
    process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync(ct).ConfigureAwait(false);
    return TerminationResult.ForceKilled;
}
catch (InvalidOperationException)
{
    return TerminationResult.AlreadyExited;
}
catch (Exception ex) when (ex is Win32Exception or NotSupportedException or AggregateException or OperationCanceledException)
{
    logger.Error(ex, "[Termination] Force kill failed for PID {Pid}", process.Id);
    return TerminationResult.ForceKillFailed;
}
```

### Named Mutex Single-Instance Guard
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.threading.mutex
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out _ownsMutex);
    }

    public bool HasInstanceLock => _ownsMutex;

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
```

### File-Locked PID Update
```csharp
// Source: https://learn.microsoft.com/dotnet/api/system.io.filestream.lock
await using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
stream.Lock(0, 1);
try
{
    var entries = await ReadEntriesAsync(stream, ct).ConfigureAwait(false);
    var updated = entries.Where(entry => entry.Pid != pid).ToList();

    stream.SetLength(0);
    await JsonSerializer.SerializeAsync(stream, updated, cancellationToken: ct).ConfigureAwait(false);
    await stream.FlushAsync(ct).ConfigureAwait(false);
}
finally
{
    stream.Unlock(0, 1);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Treat `Process.Kill` as synchronous completion | Call `Kill`, then wait or check exit state | Microsoft docs current for .NET 10 | Plans must include post-kill `WaitForExitAsync` and failure outcome handling. [CITED: Process.Kill docs] |
| Assume `Kill(true)` wait proves descendants exited | Treat root exit as the only guaranteed wait result | Microsoft docs current for .NET 10 | Do not make descendant proof a success criterion; log uncertainty. [CITED: Process.Kill docs] |
| Use private PID path and reflection in tests | Inject path/store abstractions | Phase 5 decision | Tests should not use reflection/private path assumptions. [VERIFIED: `05-CONTEXT.md`, current tests] |
| Unit-test process behavior with mocks only | Add controlled real-process integration tests | Phase 5 decision | TEST-01 requires actual child-process coverage under `AutoQAC.Tests`. [VERIFIED: `05-CONTEXT.md`] |

**Deprecated/outdated:**
- Returning success from force-kill exception handlers is outdated for this phase because `ForceKillFailed` is a locked decision. [VERIFIED: `05-CONTEXT.md`]
- Reflection-based PID path tests are outdated because REF-04 requires injected storage/path abstractions. [VERIFIED: `REQUIREMENTS.md`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | CliWrap/MedallionShell are not worth adding for this phase. | Standard Stack alternatives | Planner might miss a useful package, but current locked decisions focus on BCL process semantics already in use. |
| A2 | SQLite is excessive for PID storage. | Standard Stack alternatives | Planner might choose simpler JSON when stronger transactional storage is desired. |
| A3 | Helper executable modes such as `sleep`, `exit-on-stdin`, `ignore-close`, and `spawn-child` are sufficient. | Architecture Patterns / Tests | Tests might need additional modes to simulate GUI `CloseMainWindow` accurately. |
| A4 | Named mutex startup guard should be acquired during app bootstrap. | Runtime State Inventory / Code Examples | If startup lifetime constraints differ, registration/disposal location may need adjustment. |

## Open Questions

1. **How should a console test helper simulate graceful UI close?**
   - What we know: `CloseMainWindow` returns false when a process has no main window or the main window is disabled. [CITED: CloseMainWindow docs]
   - What's unclear: A tiny console helper may not exercise a true successful `CloseMainWindow` path. [ASSUMED]
   - Recommendation: Cover successful graceful exit via a helper mode that exits before grace period when signaled by stdin/file/event, and cover `CloseMainWindow == false` as the no-window escalation path; only add a minimal WinForms/WPF/Avalonia helper if the planner requires actual window-close behavior. [ASSUMED]

2. **Where should user-visible force-kill failure be represented in state?**
   - What we know: `IStateService` is the shared runtime state hub and force-kill failure must surface to state/dialog. [VERIFIED: `AGENTS.md`, `05-CONTEXT.md`]
   - What's unclear: Existing `AppState`/result models do not have a dedicated termination failure field in the researched snippets. [VERIFIED: read source snippets]
   - Recommendation: Prefer extending `PluginCleaningResult`/session result message and ViewModel dialog path before adding broad state fields. [ASSUMED]

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| .NET SDK | Build/test/test helper | ✓ | 10.0.203 | None needed. [VERIFIED: `dotnet --version`] |
| Windows process APIs | `CloseMainWindow`, `Kill(true)`, named mutex | ✓ | Project targets Windows TFM | None; app is Windows-only. [VERIFIED: csproj + AGENTS.md] |
| xUnit test runner | Phase tests | ✓ | Microsoft.NET.Test.Sdk 18.0.1, xUnit 2.9.3 | None needed. [VERIFIED: `AutoQAC.Tests.csproj`, test discovery] |
| xEdit executable | Real production cleaning | Not required for Phase 5 tests | — | Use helper executable, not xEdit. [VERIFIED: `05-CONTEXT.md`] |

**Missing dependencies with no fallback:** None found for planning. [VERIFIED: environment probes and project files]

**Missing dependencies with fallback:** Real xEdit is not required; controlled test helper is the fallback for process tests. [VERIFIED: `05-CONTEXT.md`]

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 with Microsoft.NET.Test.Sdk 18.0.1, FluentAssertions 8.8.0, NSubstitute 5.3.0. [VERIFIED: `AutoQAC.Tests.csproj`] |
| Config file | `AutoQAC.Tests/AutoQAC.Tests.csproj`. [VERIFIED: file read] |
| Quick run command | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Process"` [VERIFIED: project exists and tests discovered] |
| Full suite command | `dotnet test AutoQACSharp.slnx` [VERIFIED: AGENTS.md] |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| SAF-01 | First user Stop returns/persists `GracePeriodExpired` and prompts before force kill; second Stop escalates. | unit + integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningCommandsViewModel|FullyQualifiedName~CleaningOrchestrator"` | ✅ existing files; new tests needed. [VERIFIED: source files] |
| SAF-02 | Failed `Kill(true)` or post-kill wait returns `ForceKillFailed` and surfaces concise user outcome. | unit + integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecution"` | ✅ existing file; new tests needed. [VERIFIED: source files] |
| REF-04 | PID tracking can be tested through injected path/store abstractions and locked update behavior. | unit + stress/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PidStore"` | ❌ Wave 0 create `JsonPidStoreTests.cs`. [VERIFIED: glob found no existing file] |
| TEST-01 | Real child-process timeout, graceful stop, force kill, and PID cleanup are verifiable. | integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionIntegration"` | ❌ Wave 0 create helper/tests. [VERIFIED: current tests avoid real processes and glob found no existing integration helper test] |

### Sampling Rate
- **Per task commit:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Process|FullyQualifiedName~CleaningOrchestrator|FullyQualifiedName~CleaningCommandsViewModel"` [ASSUMED: recommended command]
- **Per wave merge:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj` [VERIFIED: project exists]
- **Phase gate:** `dotnet test AutoQACSharp.slnx` before `/gsd-verify-work`. [VERIFIED: AGENTS.md]

### Wave 0 Gaps
- [ ] `AutoQAC/Services/Process/IPidStore.cs` — abstraction for REF-04. [ASSUMED: proposed path]
- [ ] `AutoQAC/Services/Process/JsonPidStore.cs` — locked JSON store and corruption preservation. [ASSUMED: proposed path]
- [ ] `AutoQAC.Tests/Services/JsonPidStoreTests.cs` — injected temp-path tests for process-safe updates and corrupt-file preservation. [ASSUMED: proposed path]
- [ ] `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` — real helper process coverage for TEST-01. [ASSUMED: proposed path]
- [ ] `AutoQAC.Tests/TestProcessHelper/` — tiny helper executable or equivalent test asset. [ASSUMED: proposed path]

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|------------------|
| V2 Authentication | no | Local desktop app has no authentication domain in this phase. [VERIFIED: `.planning/codebase/ARCHITECTURE.md`] |
| V3 Session Management | no | Process session IDs are local cleanup identifiers, not user auth sessions. [VERIFIED: `05-CONTEXT.md`] |
| V4 Access Control | yes | Do not claim force-kill success on `Win32Exception`/access-denied; report failure to user and logs. [CITED: Process.Kill docs; VERIFIED: `05-CONTEXT.md`] |
| V5 Input Validation | yes | Validate PID JSON parse failures by preserving corrupt file and recreating clean state. [VERIFIED: `05-CONTEXT.md`] |
| V6 Cryptography | no | No cryptographic feature is in scope. [VERIFIED: phase context] |

### Known Threat Patterns for local Windows process/PID stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| PID reuse kills wrong process | Tampering / Denial of Service | Validate process name and start time; add session ID filtering for current vs prior entries. [VERIFIED: current `IsXEditProcess`; VERIFIED: `05-CONTEXT.md`] |
| PID file corruption hides orphan cleanup evidence | Tampering | Preserve timestamped corrupt copy, log metadata, recreate clean store. [VERIFIED: `05-CONTEXT.md`] |
| Lost PID update across processes | Tampering / Repudiation | Use file lock around read-modify-write and single-instance mutex. [CITED: FileStream.Lock docs; CITED: Mutex docs] |
| Force-kill failure reported as success | Repudiation / Integrity | Add `ForceKillFailed`, concise user dialog, detailed logs. [VERIFIED: `05-CONTEXT.md`; CITED: Process.Kill docs] |
| Excess process/path detail in user dialog | Information Disclosure | Keep detailed exception/path data in logs and show concise dialog. [VERIFIED: `05-CONTEXT.md`, `.planning/codebase/CONCERNS.md`] |

## Sources

### Primary (HIGH confidence)
- `J:\AutoQACSharp\AGENTS.md` — project architecture, runtime behavior, testing, and coding constraints. [VERIFIED]
- `.planning/phases/05-process-stop-pid-safety/05-CONTEXT.md` — locked Phase 5 decisions and scope. [VERIFIED]
- `.planning/REQUIREMENTS.md` — SAF-01, SAF-02, REF-04, TEST-01. [VERIFIED]
- `.planning/codebase/ARCHITECTURE.md`, `CONCERNS.md`, `TESTING.md` — current architecture, known bugs, and test patterns. [VERIFIED]
- `AutoQAC/Services/Process/ProcessExecutionService.cs`, `IProcessExecutionService.cs`, `TerminationResult.cs`, `CleaningOrchestrator.cs`, `CleaningCommandsViewModel.cs`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` — current implementation and tests. [VERIFIED]
- Microsoft Learn `Process.CloseMainWindow` — graceful close semantics and exceptions. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.closemainwindow]
- Microsoft Learn `Process.Kill` — force kill semantics, exceptions, async completion, descendant caveats. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill]
- Microsoft Learn `Process.WaitForExitAsync` — cancellation cancels wait and stores exceptions in returned task. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexitasync]
- Microsoft Learn `FileStream.Lock` — interprocess file range locking. [CITED: https://learn.microsoft.com/dotnet/api/system.io.filestream.lock]
- Microsoft Learn `Mutex` — named system mutexes for interprocess synchronization. [CITED: https://learn.microsoft.com/en-us/dotnet/api/system.threading.mutex]

### Secondary (MEDIUM confidence)
- Existing test discovery via `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --list-tests` — verifies test runner availability and current ProcessExecutionService tests. [VERIFIED: command output]

### Tertiary (LOW confidence)
- Helper executable mode details and some file/class names are recommended designs, not existing project facts. [ASSUMED]

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — existing project packages and BCL APIs were verified from project files, environment, and Microsoft docs. [VERIFIED]
- Architecture: HIGH — current boundaries and locked decisions were verified from phase context and source. [VERIFIED]
- Pitfalls: HIGH — main pitfalls are documented in current source/concerns and Microsoft API docs. [VERIFIED/CITED]
- Test helper implementation details: MEDIUM — need planner validation for exact helper shape. [ASSUMED]

**Research date:** 2026-04-28  
**Valid until:** 2026-05-28 for project-specific architecture; 2026-05-05 for .NET 10 preview/SDK surface assumptions. [ASSUMED]