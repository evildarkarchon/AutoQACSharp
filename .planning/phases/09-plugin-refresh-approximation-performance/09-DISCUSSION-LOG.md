# Phase 9: Plugin Refresh & Approximation Performance - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md - this log preserves the alternatives considered.

**Date:** 2026-04-29
**Phase:** 09-plugin-refresh-approximation-performance
**Areas discussed:** Refresh targeting, Stale result UX, Count accuracy, Workflow ownership

---

## Refresh Targeting

| Decision Question | Options Presented | User's Choice |
|-------------------|-------------------|---------------|
| Initial approximation behavior after selecting a game/data folder | Load rows first; Analyze before rows; Manual only; You decide | Load rows first |
| Target priority for narrower refresh work | Selected first; Visible rows; All loaded; You decide | Selected first |
| UI refresh action | Refresh selected; Refresh visible; Automatic only; You decide | Refresh selected |
| Non-targeted row state | Keep existing; Mark pending; Clear counts; You decide | Keep existing |
| Skip-list-hidden plugins | When visible; Always include; Manual only; You decide | When visible |
| After selected plugins finish | Stop there; Idle fill; Always fill; You decide | Stop there |
| No selected plugins | Do nothing clearly; Refresh visible; Ask each time; You decide | Do nothing clearly |
| Selection changes during refresh | Snapshot target; Follow selection; Cancel on change; You decide | Snapshot target |

**Notes:** User chose selected-first targeting and a selected-row action, with no surprise all-list background fill. Non-targeted rows retain existing values.

---

## Stale Result UX

| Decision Question | Options Presented | User's Choice |
|-------------------|-------------------|---------------|
| Existing counts after supersede | Keep until replaced; Clear immediately; Mark stale; You decide | Keep until replaced |
| Explicit canceled state for automatic supersede | Silent supersede; Brief status; Row canceled; You decide | Silent supersede |
| Progress feedback | Count processed; Rows only; Percentage; You decide | Count processed |
| Single-plugin failure presentation | Row unavailable; Status summary; Error dialog; You decide | Row unavailable |
| Cancel affordance | Cancel action; Implicit only; You decide | Cancel action |
| Manual cancel row behavior | Keep partial; Rollback all; Mark remaining; You decide | Keep partial |
| Start cleaning during approximation | Cancel approximation; Let it continue; Ask user; You decide | Cancel approximation |
| Completion status | Concise summary; Return ready; Detailed summary; You decide | Concise summary |

**Notes:** User wants truthful count-based progress, direct cancellation, completed rows retained after manual cancel, and no dialog noise for per-plugin approximation failures.

---

## Count Accuracy

| Decision Question | Options Presented | User's Choice |
|-------------------|-------------------|---------------|
| Exact vs partial/capped counts | Exact counts; Partial labeled; Cap counts; You decide | Exact counts |
| Automatic per-plugin timeout | No timeout; Timeout unavailable; Timeout prompt; You decide | No timeout |
| Cancel midway through one plugin | Keep previous; Show canceled; Publish partial; You decide | Keep previous |
| Ambiguous/unsupported Mutagen context | Fail unavailable; Fallback old path; Best effort; You decide | Fail unavailable |
| One category fails | Whole unavailable; Partial categories; Best effort total; You decide | Whole unavailable |
| Approximation label | Keep approx label; Call exact; No label change; You decide | Keep approx label |
| Cancellation check frequency | Frequent checks; Per plugin; You decide | Frequent checks |
| Exact counts vs memory/speed tradeoff | Lower memory; Raw speed; Benchmark decides; You decide | Lower memory |

**Notes:** User strongly favored exact semantics with lower memory, no partial publication, and frequent cancellation checks.

---

## Workflow Ownership

| Decision Question | Options Presented | User's Choice |
|-------------------|-------------------|---------------|
| What moves out of `ConfigurationViewModel` | Whole refresh pipeline; Approximation only; Plugin loading only; You decide | Whole refresh pipeline |
| Generation and cancellation ownership | Coordinator owns; ViewModel owns; Split ownership; You decide | Coordinator owns |
| Publishing rows and approximation updates | Use state service; Return snapshots; Expose observable; You decide | Use state service |
| Game capability cleanup breadth | Refresh scoped; Full registry; No new policy; You decide | Refresh scoped |
| Manual load-order file path | Include it; Leave it; You decide | Include it |
| Remaining `ConfigurationViewModel` role | UI shell only; Some workflow; Minimal changes; You decide | UI shell only |
| Status communication | Typed statuses; Service strings; State only; You decide | Typed statuses |
| Refresh selected command placement | Plugin list; Configuration panel; Main commands; You decide | Plugin list |
| Active refresh policy | Single active; Queue refreshes; Concurrent targets; You decide | Single active |
| Command availability while loading | Disable until loaded; Cancel loading; Queue after load; You decide | Disable until loaded |
| Unsupported approximation games | Disable with reason; Show unavailable; Attempt anyway; You decide | Disable with reason |
| Reset/dispose behavior | Cancel and clear; Let finish; You decide | Cancel and clear |

**Notes:** User wants a service-owned coordinator for the whole refresh path, with `ConfigurationViewModel` reduced to UI shell responsibilities and the selected-refresh action near the plugin list.

---

## the agent's Discretion

- Exact class names, interface names, DTO/status enum names, API signatures, XAML placement specifics, and test file organization.
- The smallest safe refresh-scoped game capability/policy surface required to satisfy Phase 9 without turning it into a full registry rewrite.

## Deferred Ideas

- Full app-wide game capability registry cleanup.
- App-level issue approximation support for additional games.
- Partial/capped count display modes and automatic per-plugin analysis timeouts.
