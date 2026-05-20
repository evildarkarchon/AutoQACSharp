# T02: 05-process-stop-pid-safety 02

**Slice:** S06 — **Milestone:** M001

## Description

Correct low-level process termination semantics while preserving the single process slot.

Purpose: The process service currently conflates user cancellation with timeout and reports failed force-kill attempts as success.
Output: Explicit cancellation intent, `ForceKillFailed`, PID store integration, and focused process-service regression tests.

## Must-Haves

- [ ] "User-initiated Stop reports `GracePeriodExpired` instead of auto-force-killing inside `ExecuteAsync` (D-01, D-02, SAF-01)."
- [ ] "Timeout handling may still auto-force after the grace period (D-05)."
- [ ] "Failed `Kill(true)` or post-kill wait returns `ForceKillFailed`, never `ForceKilled` (D-06, SAF-02)."
- [ ] "`ForceKilled` means kill was invoked and the tracked/root process exited (D-07)."
- [ ] "`ProcessResult.TerminationResult` carries `GracePeriodExpired`, `ForceKillFailed`, and left-running outcomes to callers without relying only on mutable side channels (review consensus)."
- [ ] "Process service uses injected PID store/session abstractions from Plan 01 (D-10, D-13, REF-04)."

## Files

- `AutoQAC/Models/ProcessResult.cs`
- `AutoQAC/Models/TerminationResult.cs`
- `AutoQAC/Services/Process/IProcessExecutionService.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
