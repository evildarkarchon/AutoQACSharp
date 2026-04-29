# Phase 5: Process Stop & PID Safety - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-28
**Phase:** 05-process-stop-pid-safety
**Areas discussed:** Stop ownership, Kill outcomes, PID storage, Process tests

---

## Stop Ownership

| Question | Selected | Options Considered |
|----------|----------|--------------------|
| Where should force-kill escalation be owned for user-initiated stop? | Orchestrator only | Orchestrator only; Process service; Mode-specific; You decide |
| What should happen when the user declines the force-terminate prompt after grace expires? | Leave running | Leave running; Keep waiting; Retry prompt; You decide |
| How should the second Stop click behave while xEdit is still in the grace/confirmation path? | Force immediately | Force immediately; Show prompt; Disable button; You decide |
| Should timeout handling use the same no-auto-force rule as user Stop? | Timeout can force | Same rule; Timeout can force; Separate later; You decide |

**Notes:** User-initiated stop must not be auto-escalated inside `ProcessExecutionService`; timeout behavior may remain more aggressive.

---

## Kill Outcomes

| Question | Selected | Options Considered |
|----------|----------|--------------------|
| How explicit should failed force-kill reporting be? | Add failure result | Add failure result; Return unknown; Throw exception; You decide |
| When should AutoQAC report ForceKilled? | Root exited | Root exited; Full tree proof; Best effort; You decide |
| How should a force-kill failure reach the user? | State + dialog | State + dialog; Status only; Log only; You decide |
| Should failed force-kill block log parsing for that plugin? | Block parsing | Block parsing; Try if exited; Always try; You decide |

**Notes:** `ForceKilled` should not hide direct `Kill`/wait failures. Descendant-process uncertainty is acceptable as logged caveat if the root process exits.

---

## PID Storage

| Question | Selected | Options Considered |
|----------|----------|--------------------|
| How far should Phase 5 go on PID process-safety? | File lock | File lock; Testability only; Single instance; You decide |
| What should happen when the PID file is corrupt? | Preserve copy | Preserve copy; Log only; Fail startup; You decide |
| What should PID entries identify besides PID/start time/plugin? | Add session ID | Keep minimal; Add executable; Add session ID; You decide |
| Should Phase 5 introduce a single-instance app lock? | Yes, block multi-instance | No, file lock enough; Yes, block multi-instance; Warn only; You decide |

**Notes:** PID tracking should become testable and process-safe, and the app should block multiple running AutoQAC instances.

---

## Process Tests

| Question | Selected | Options Considered |
|----------|----------|--------------------|
| Where should real child-process tests live? | AutoQAC.Tests integration | AutoQAC.Tests integration; New test project; Unit suite; You decide |
| Should real-process tests run by default in `dotnet test`? | Run by default | Run by default; Trait-gated; Env-gated; You decide |
| What helper process should tests use? | Test helper exe | Test helper exe; PowerShell; Current test host; You decide |
| How strict should process-test timing be? | Generous bounded | Generous bounded; Very fast; Slow reliable; You decide |

**Notes:** The test harness should be reliable enough to run by default and should clean up helper processes defensively.

---

## the agent's Discretion

- Exact internal names, enum value spelling, session ID format, lock primitives, helper project name, and low-level API signatures are delegated to research/planning.

## Deferred Ideas

None.
