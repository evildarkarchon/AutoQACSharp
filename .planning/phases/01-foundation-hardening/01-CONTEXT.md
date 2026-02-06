# Phase 1: Foundation Hardening - Context

**Gathered:** 2026-02-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix critical process management, state handling, and configuration persistence bugs that risk data corruption and block all subsequent feature work. Covers: process termination hardening (PROC-01/02/03/06), state concurrency fixes (PROC-04, STAT-01), and deferred config saves (CONF-01, CONF-05).

</domain>

<decisions>
## Implementation Decisions

### Process kill strategy
- **Grace period:** 2-3 second grace period after user clicks Stop before escalating
- **Escalation model:** After grace period, prompt user "xEdit didn't stop gracefully. Force kill?" — user must confirm force kill
- **Escalating stop button:** First click = graceful stop with grace period. Second click (during grace period or after timeout) = immediate force kill — no prompt needed on second click
- **Orphan detection:** Auto-kill orphaned xEdit processes silently on startup and before new cleaning runs — no user prompt for orphans
- **Process tree kill:** Kill the entire process tree, not just the main xEdit process — handles child processes and re-spawns
- **PID tracking:** Track PIDs of xEdit processes we launched (distinguish our processes from user-launched xEdit instances) — only kill processes we started

### Stop/cancel UX
- **UI blocking during stop:** Block all controls with a "Stopping..." spinner while termination is in progress — prevents conflicting actions
- **Stop button exception:** The Stop/Force Kill button itself remains clickable even in the blocked "Stopping..." state — user always has manual escalation if automated methods fail
- **Post-cancel display:** Show a summary of what was cleaned before cancellation — which plugins completed, which were skipped
- **Distinct failure states:** Differentiate between user-cancelled, xEdit crashed, and xEdit timed out with different icons/messages — helps troubleshooting

### Config save timing
- **Save trigger:** Debounced writes (e.g., 500ms) — batch rapid setting changes into one disk write
- **Pre-clean flush:** Always force-flush any pending config saves before launching xEdit — guarantees xEdit runs with up-to-date settings
- **Save failure handling:** Retry once or twice silently, then show a non-blocking warning if still failing
- **Memory vs disk on failure:** Revert to last known-good config on disk if save fails — prevents config divergence between memory and disk

### State recovery
- **Startup cleanup:** Auto-detect and clean stale state on startup (orphan processes, dead PID files, incomplete state) — no user prompt
- **Partial results:** Preserve partial cleaning results after cancel/crash — remember which plugins were successfully cleaned before interruption
- **Cleanup logging:** Always log every auto-cleanup action for diagnostics — helps debug recurring issues

### Claude's Discretion
- Exact debounce timing for config saves (500ms is a starting point)
- Grace period duration tuning (2-3s range)
- PID tracking mechanism (PID file vs in-memory with crash recovery)
- Process tree enumeration approach (Windows API specifics)
- Specific logging format and verbosity for cleanup actions
- How partial results are stored/displayed (in-memory vs persisted)

</decisions>

<specifics>
## Specific Ideas

- The stop button escalation pattern (click once = graceful, click again = force) is important — user should never feel "stuck" waiting for the app to handle termination
- Config revert-to-disk on save failure is a strong preference — the user would rather lose a change than have invisible config drift
- Orphan cleanup should be invisible — the user shouldn't have to think about leftover processes from crashes

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-foundation-hardening*
*Context gathered: 2026-02-06*
