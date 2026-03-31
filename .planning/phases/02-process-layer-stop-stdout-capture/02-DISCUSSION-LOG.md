# Phase 2: Process Layer -- Stop Stdout Capture - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-30
**Phase:** 02-process-layer-stop-stdout-capture
**Areas discussed:** Scope boundary with Phase 4, Test strategy, MO2 mode concerns

---

## Scope Boundary with Phase 4

| Option | Description | Selected |
|--------|-------------|----------|
| Minimal -- redirect only (Recommended) | Only change ProcessStartInfo flags and remove BeginOutputReadLine/BeginErrorReadLine + event handlers. Leave ProcessResult fields and IProgress<string> parameter intact. Phase 4 cleans up the dead interface contracts. | ✓ |
| Trim ProcessResult too | Also remove OutputLines/ErrorLines from ProcessResult and update all callers. IProgress<string> stays. Medium scope. | |
| Full contract cleanup | Remove redirect, OutputLines/ErrorLines, AND the IProgress<string> parameter. Fully clean interface in one shot. | |

**User's choice:** Minimal -- redirect only
**Notes:** Clean separation — Phase 2 stops the redirect, Phase 4 cleans up dead interface contracts.

---

## Test Strategy

### Question 1: What should happen to the stdout capture test?

| Option | Description | Selected |
|--------|-------------|----------|
| Delete it (Recommended) | The behavior it tests no longer exists. Phase 4 cleanup will sweep remaining dead test infrastructure. | ✓ |
| Replace with exit-code test | Delete the stdout test and add a new test: launch cmd.exe /c exit 0, verify ExitCode=0 and OutputLines is empty. | |
| Keep but expect empty | Modify the test to assert OutputLines and ErrorLines are empty after the same cmd.exe run. | |

**User's choice:** Delete it

### Question 2: Should Phase 2 add any new process tests?

| Option | Description | Selected |
|--------|-------------|----------|
| Add basic exit-code test (Recommended) | One new test: launch a trivial process, verify ExitCode=0, TimedOut=false. | |
| Existing tests are enough | Startup-failure test, disposal test, and orchestrator-level termination tests already cover the important paths. | ✓ |
| You decide | Claude decides based on implementation. | |

**User's choice:** Existing tests are enough
**Notes:** No new tests needed — existing coverage is sufficient.

---

## MO2 Mode Concerns

| Option | Description | Selected |
|--------|-------------|----------|
| Trust the analysis (Recommended) | MO2 is a GUI app like xEdit — it doesn't write to stdout. Removing redirect is safe. Existing orchestrator-level tests mock IProcessExecutionService, verifying the MO2 code path. | ✓ |
| Add a code comment | Trust the analysis, but add a comment noting MO2 mode was verified. | |
| Flag for manual smoke test | Proceed but document that MO2 mode should be manually tested before milestone ships. | |

**User's choice:** Trust the analysis
**Notes:** MO2 wraps xEdit via ModOrganizer.exe run. ProcessExecutionService tracks the MO2 PID and uses WaitForExitAsync — neither depends on stdout redirection.

---

## Claude's Discretion

- Whether to leave OutputLines/ErrorLines as empty lists in the return value or skip populating them entirely

## Deferred Ideas

None — discussion stayed within phase scope.
