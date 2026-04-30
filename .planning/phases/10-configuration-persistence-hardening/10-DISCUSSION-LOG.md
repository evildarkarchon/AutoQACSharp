# Phase 10: Configuration Persistence Hardening - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-30
**Phase:** 10-configuration-persistence-hardening
**Areas discussed:** Persistence authority, External edit races, Failure surfacing, Deterministic tests, Clone replacement

---

## Persistence Authority

| Question | Options Presented | Selected | Notes |
|----------|-------------------|----------|-------|
| How much should Phase 10 consolidate ownership? | Internal coordinator; Public coordinator; Lock cleanup only; You decide | Internal coordinator | One service-owned serialized flow behind existing config/watcher APIs. |
| Should debounced saves and watcher reloads be ordered through an explicit queue? | Explicit queue; Keep Rx edge; Keep Rx core; You decide | Explicit queue | Queue covers save, flush, reload, defer, and failure ordering. |
| How conservative should the public configuration API be? | Stable plus status; Coordinator visible; Rewrite interface; You decide | Rewrite interface | User allows API change for this slice. |
| Where is the rewrite boundary? | Persistence slice; Config interface; App-wide config; You decide | Persistence slice | Scope limited to persistence methods/status; no broad config helper reorg. |
| Persist every intermediate save or coalesce latest? | Coalesce latest; Persist every; Operation-specific; You decide | Coalesce latest | Preserve debounced-save intent. |
| What should forced flush do? | Barrier flush; Best effort; Cancel reloads; You decide | Barrier flush | Flush returns success/failure before callers continue. |
| Publish all outcomes or failures only? | Failures only; All outcomes; Tests internal; You decide | Failures only | Avoid noisy success events. |
| Shutdown/disposal behavior? | Drain latest; Strict drain; Skip shutdown; You decide | Drain latest | Best-effort final flush without indefinite hang. |
| What role should ConfigWatcherService keep? | Event source; Shared owner; Collapse it; You decide | Event source | Watcher submits events; coordinator decides ordering. |
| Use versions/generations for stale operations? | Use versions; Hash only; Timestamp window; You decide | Use versions | Deterministic stale rejection. |
| Is atomic save behavior in scope? | Atomic write; Current write; Research first; You decide | Atomic write | Temp write then replace/move. |
| Validate external YAML before replacing config? | Validate first; Current split; Load then recover; You decide | Validate first | Publish only valid authoritative config. |
| When user changes a setting, active immediately or after save? | Optimistic active; Commit after save; Pending state; You decide | Optimistic active | Save failure rolls back with status. |
| Keep current debounce behavior? | Keep debounce; No debounce; Configurable debounce; You decide | Keep debounce | Forced flush bypasses debounce. |
| If queued reload sees a newer app save, what happens? | Re-evaluate reload; Cancel reload; Reload first; You decide | Re-evaluate reload | Reject if stale/racing when processed. |
| Queue owns user config and main config cache? | User config only; Include cache; Split later; You decide | User config only | Phase remains focused on `AutoQAC Settings.yaml`. |

---

## External Edit Races

| Question | Options Presented | Selected | Notes |
|----------|-------------------|----------|-------|
| External edit while app save pending? | Reject racing edit; Queue for later; Prompt conflict; You decide | Reject racing edit | App save wins close-timing race. |
| What counts as later valid external edit? | New version; Current disk; Manual reload; You decide | New version | Requires post-settle watcher/content version. |
| Multiple external edits while cleaning? | Latest valid; First valid; Reject all; You decide | Latest valid | Apply at most once after cleaning ends. |
| Latest deferred edit invalid but earlier was valid? | Reject latest; Apply earlier; Try current only; You decide | Reject latest | Keep current config; record rejection. |
| App-written hash/version echo visibility? | Silent skip; Trace outcome; User status; You decide | Silent skip | Debug log only. |
| If app save fails after rejected racing external edit? | Stay rejected; Reload external; Prompt user; You decide | Stay rejected | App intent still won the race. |
| Settings file missing during watcher reload? | Keep active; Create default; Use last good; You decide | Keep active | User first chose Create default, then asked to re-ask and changed to Keep active. |
| Expand watcher beyond Changed events? | Handle all; Changed only; Research first; You decide | Handle all | Changed/Created/Renamed/Deleted signal re-check. |

---

## Failure Surfacing

| Question | Options Presented | Selected | Notes |
|----------|-------------------|----------|-------|
| How should failures reach UI layer? | Typed observable; AppState field; Dialog service; You decide | Typed observable | MVVM-safe status stream. |
| Which failures become user-visible? | Action blockers; All failures; Flush only; You decide | Action blockers | Pre-clean flush, user-initiated save/reload, invalid external YAML. |
| Pre-cleaning flush failure behavior? | Block cleaning; Offer continue; Retry prompt; You decide | Block cleaning | No xEdit launch. |
| How should failure clear after recovery? | Next success clears; Manual clear; Timed clear; You decide | Next success clears | Recovery is observable. |
| What should typed failure carry? | Safe summary; Full exception; String only; You decide | Safe summary | Operation type, safe reason, recovery state, optional log reference. |
| Invalid external YAML visibility? | Status only; Dialog; Log only; You decide | Status only | No modal. |
| Save failure rollback wording? | Say restored; Save failed; Retry prompt; You decide | Say restored | Make rollback explicit. |
| Add retry action? | No retry UI; Retry command; Preflight retry; You decide | No retry UI | Avoid UI redesign. |

---

## Deterministic Tests

| Question | Options Presented | Selected | Notes |
|----------|-------------------|----------|-------|
| Primary deterministic race seam? | Coordinator seam; Fake watcher; Fake time; You decide | Coordinator seam | Submit operations directly. |
| Existing real watcher tests? | Smoke only; Remove them; Keep all; You decide | Smoke only | Race proof moves elsewhere. |
| Exact operation order or final state? | Outcome first; Exact order; Final only; You decide | Outcome first | Assert critical ordering only where protected. |
| Add guard tests for clone/no waits? | Targeted guards; No guards; Broad guards; You decide | Targeted guards | Narrow guard scope. |
| How inject filesystem failures? | File IO seam; Temp locks; Hybrid; You decide | File IO seam | Deterministic fake for read/write/replace/hash. |
| Pre-clean flush failure no-launch proof? | Mock boundary; Integration flow; Both levels; You decide | Mock boundary | Assert process/cleaning service is not called. |
| Race matrix breadth? | Spec matrix; Minimal matrix; Exhaustive matrix; You decide | Spec matrix | Cover acceptance cases, not every permutation. |
| No production throttle waits? | Avoid sleeps; Assert source; No rule; You decide | Avoid sleeps | New deterministic tests do not wait on 500 ms throttle or arbitrary sleeps. |

---

## Clone Replacement

| Question | Options Presented | Selected | Notes |
|----------|-------------------|----------|-------|
| YAML-free clone strategy? | Manual deep copy; Clone library; Immutable refactor; You decide | Manual deep copy | No new dependency or broad refactor. |
| Where should clone behavior live? | Model method; Service helper; Mapper class; You decide | Model method | Discoverable near the config models. |
| How protect clone maintenance? | Reflection guard; Behavior only; Analyzer later; You decide | Behavior only | Expand behavior tests, no reflection guard for every property. |
| Ban YAML from clone paths or all memory use? | Clone paths; All memory; Research first; You decide | Clone paths | YAML remains for disk persistence. |
| Defensive null handling? | Normalize defaults; Preserve null; Throw on null; You decide | Normalize defaults | Default empty nested objects/collections. |
| Copy all mutable containers? | Deep containers; Only lists; Selective; You decide | Deep containers | New dictionary/list instances throughout. |
| Clone method accessibility? | Public method; Internal method; Extension method; You decide | Public method | Clear public model API. |
| Performance proof? | Proof only; Simple timing; Benchmark later; You decide | Proof only | No benchmark project. |

---

## the agent's Discretion

- Exact coordinator/interface names, queue implementation details, result DTO names, status enum names, clone method names, and test class organization.

## Deferred Ideas

None from the discussion. Broader app-data/path-provider centralization, settings UI redesign, diagnostics redaction, and main configuration behavior changes remain out of scope per `10-SPEC.md`.
