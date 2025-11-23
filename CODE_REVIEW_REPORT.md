# Code Review Report: AutoQACSharp
**Date:** 2025-11-22
**Focus:** Async Threading, Resource Usage, and Disposal

## 1. Executive Summary
The codebase generally demonstrates good adherence to modern C# async patterns and resource management. 
- **Strengths:** 
    - Correct usage of `using` statements for `IDisposable`.
    - `SemaphoreSlim` used effectively for concurrency control.
    - ReactiveUI implementation in ViewModels follows standard patterns.
    - Serilog integration is handled correctly.
- **Weaknesses:** 
    - A few instances of "Sync over Async" (blocking calls) which can lead to deadlocks or UI freezes.
    - One potential race condition in `CleaningService` regarding cancellation token management.

## 2. Critical Issues

### A. Blocking Async Calls in `ConfigurationService`
**Severity:** High
**File:** `AutoQAC/Services/Configuration/ConfigurationService.cs`

**Issue:** 
The methods `GetSkipList` and `GetXEditExecutableNames` are synchronous but internally call `LoadMainConfigAsync().GetAwaiter().GetResult()`.
This "Sync over Async" pattern is dangerous. If called from the UI thread (even indirectly), and `LoadMainConfigAsync` attempts to reschedule onto the captured context, it can cause a deadlock. Even if not a deadlock, it blocks the calling thread.

**Code:**
```csharp
public List<string> GetSkipList(GameType gameType)
{
    if (_mainConfigCache == null)
    {
         // DANGER: Blocking async call
         _mainConfigCache = LoadMainConfigAsync().GetAwaiter().GetResult();
    }
    // ...
}
```

**Recommendation:**
Refactor these methods to be asynchronous: `Task<List<string>> GetSkipListAsync(...)`.
Alternatively, ensure `LoadMainConfigAsync()` is called and awaited during application startup (e.g., in `App.axaml.cs`) so that `_mainConfigCache` is guaranteed to be populated before these methods are ever called.

### B. Shared State Race Condition in `CleaningService`
**Severity:** Medium
**File:** `AutoQAC/Services/Cleaning/CleaningService.cs`

**Issue:** 
The field `_currentOperationCts` is a class-level member that is overwritten every time `CleanPluginAsync` is called.
If `CleanPluginAsync` were theoretically called concurrently (or if a previous call hadn't fully cleaned up due to an exception swallowing logic error), this would overwrite the cancellation token source of the running operation, potentially leaving it un-cancellable or disposing a token still in use.

**Code:**
```csharp
// Link with internal cancellation
_currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
```

**Recommendation:**
Remove the `_currentOperationCts` class field.
Cancellation should be driven entirely by the `CancellationToken` passed into the method. The `CleaningOrchestrator` already manages a "master" cancellation token (`_cleaningCts`). 
If you need the specific `StopCurrentOperation()` method in `CleaningService`, it implies `CleaningService` is stateful. It is safer to let the caller (Orchestrator) own the `CancellationTokenSource` and cancel it.

## 3. Minor Issues & Improvements

### A. Process Execution - Task Completion Source
**File:** `AutoQAC/Services/Process/ProcessExecutionService.cs`
**Observation:**
The usage of `TaskCompletionSource` to wrap the `Exited` event is correct. The `using` block for the `Process` object correctly covers the `await tcs.Task` duration.
**Tip:** Ensure `process.EnableRaisingEvents = true` is always set (it is currently, which is good).

### B. Pre-loading Configuration
**File:** `App.axaml.cs` (Concept)
**Observation:**
To mitigate the blocking issue in `ConfigurationService` without changing every consumer to async, consider "Pre-warming" the service.
**Recommendation:**
In `OnFrameworkInitializationCompleted`, await `configService.LoadMainConfigAsync()` before showing the main window.

### C. Heavy Logic on UI Thread
**File:** `AutoQAC/Services/Cleaning/CleaningService.cs`
**Observation:**
`CleanPluginAsync` is likely called from the UI context (via `MainWindowViewModel` -> `CleaningOrchestrator`).
After `_processService.ExecuteAsync` returns, the parsing logic `_outputParser.ParseOutput(result.OutputLines)` runs on the captured context (likely the UI thread).
If the xEdit output is large (thousands of lines), this parsing could cause a noticeable UI stutter.
**Recommendation:**
Wrap the parsing logic in `Task.Run(() => ...)` to ensure it executes on a thread pool thread.

## 4. Action Plan
1.  **Refactor `ConfigurationService`**: Change `GetSkipList` to `GetSkipListAsync`. Update `CleaningOrchestrator` to await it.
2.  **Refactor `CleaningService`**: Remove `_currentOperationCts`. Rely on the `CancellationToken` passed from `CleaningOrchestrator`. Remove `StopCurrentOperation()` from `ICleaningService` and let the Orchestrator handle stopping by cancelling the token it passes down.

