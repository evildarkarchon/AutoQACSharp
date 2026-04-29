# Phase 8: Cleaning Orchestrator Decomposition - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md - this log preserves the alternatives considered.

**Date:** 2026-04-29T16:23:02.1979641-07:00
**Phase:** 08-cleaning-orchestrator-decomposition
**Areas discussed:** Extraction boundaries, Behavior preservation, Dry-run/preflight sharing, Test proof expectations

---

## Extraction Boundaries

### Split Aggressiveness
| Option | Description | Selected |
|--------|-------------|----------|
| Focused collaborators | Extract collaborators around roadmap success criteria: preflight/selection, backup session lifecycle, per-plugin execution/result finalization, and termination coordination. Keep `CleaningOrchestrator` as the sequential coordinator. | yes |
| Minimal helper extraction | Only move obvious private helper blocks out where the code is duplicated or unwieldy. Lower risk, but may not fully satisfy the requirement to edit flow pieces independently. | no |
| Pipeline/stage rewrite | Reframe cleaning as explicit stages with richer pipeline objects. Highest long-term structure, but greater regression risk in a behavior-preserving cleanup phase. | no |
| You decide | Let downstream research/planning pick the smallest decomposition that proves `REF-01`. | no |

**User's choice:** Focused collaborators.
**Notes:** This phase should reduce broad orchestrator rewrites without becoming a pipeline redesign.

### Orchestrator Ownership
| Option | Description | Selected |
|--------|-------------|----------|
| Sequential shell only | Keep session ordering, one-plugin-at-a-time loop, high-level cancellation/finalization calls, and public `ICleaningOrchestrator` entry points. Move detailed policies into collaborators. | yes |
| Keep termination state too | Leave `_currentProcess`, `_isStopRequested`, termination result, and hang monitor ownership in the orchestrator for this phase; extract only preflight/backup/execution/finalization. | no |
| Move state to session object | Introduce a cleaning-session context object that owns CTS/current process/backup state, with orchestrator delegating most stateful behavior. | no |
| You decide | Planner can choose based on safest tests and dependency shape. | no |

**User's choice:** Sequential shell only.

### Public API
| Option | Description | Selected |
|--------|-------------|----------|
| Keep public API stable | Preserve `StartCleaningAsync`, stop/force-stop, backup cancellation, dry-run, `LastTerminationResult`, and `HangDetected`; add internal collaborators behind the existing interface. | yes |
| Small API cleanup allowed | Allow signature/model changes if they reduce coupling, but keep ViewModels on one orchestrator-like dependency. | no |
| Expose new services to UI | Let ViewModels call separate cleaning/preflight/termination services directly. More visible decomposition, but risks weakening MVVM/service boundaries. | no |
| You decide | Planner decides if public API changes are justified. | no |

**User's choice:** Keep public API stable.

### File Placement
| Option | Description | Selected |
|--------|-------------|----------|
| Cleaning service folder | Keep new interfaces/implementations under `AutoQAC/Services/Cleaning`, matching existing service registration patterns and making Phase 8 easy to review. | yes |
| Session subfolder | Create a focused subfolder such as `Services/Cleaning/Session` for preflight/session/execution/finalization collaborators. Cleaner grouping, slightly more new structure. | no |
| Mixed owner folders | Put backup pieces in `Services/Backup`, termination in `Services/Process`, and preflight in `Services/Cleaning`. Strong ownership, but broader cross-folder churn. | no |
| You decide | Planner chooses the smallest file layout that keeps responsibilities obvious. | no |

**User's choice:** Cleaning service folder.

### Migration Order
| Option | Description | Selected |
|--------|-------------|----------|
| Characterize then extract | First lock current outcomes with tests, then extract one collaborator at a time while keeping behavior identical after each step. | yes |
| Extract then fill tests | Move structure first, then add tests around new seams. Faster visually, but harder to prove no behavior drift. | no |
| One-plan big split | Create all collaborators in one pass and stabilize with full suite runs. Least incremental, highest review load. | no |
| You decide | Planner chooses migration order based on code dependencies. | no |

**User's choice:** Characterize then extract.

### Backup Split
| Option | Description | Selected |
|--------|-------------|----------|
| Lifecycle collaborator | A cleaning backup-session collaborator owns create-session, per-plugin backup call, backup-failure choice result shaping, metadata write, and retention cleanup coordination; orchestrator only asks it before/after each plugin/session. | yes |
| Only progress/CTS helper | Extract only `_backupOperationCts`, progress publishing, and cancel handling; leave backup policy branches in the orchestrator. | no |
| Move to BackupService | Push more cleaning-session backup policy into `IBackupService`. Strong backup ownership, but may mix workflow/user-choice policy with lower-level backup APIs. | no |
| You decide | Planner can choose the narrowest backup seam that satisfies independent changeability. | no |

**User's choice:** Lifecycle collaborator.

### Plugin Execution Split
| Option | Description | Selected |
|--------|-------------|----------|
| Runner plus finalizer | One collaborator handles per-plugin attempt/retry/xEdit launch/log-offset capture; another builds final `PluginCleaningResult` from process result, logs, termination state, and parser output. | yes |
| Single plugin executor | One collaborator owns the entire per-plugin backup-independent flow from log offsets through result creation. Simpler seam, less ability to edit finalization independently. | no |
| Keep in orchestrator | Leave per-plugin execution/result logic in the coordinator and extract only preflight/backup/termination. Lower churn, weaker success-criteria match. | no |
| You decide | Planner chooses based on testability and smallest safe cuts. | no |

**User's choice:** Runner plus finalizer.

### Termination Split
| Option | Description | Selected |
|--------|-------------|----------|
| Dedicated coordinator | Extract current-process tracking, stop/force-stop escalation state, `LastTerminationResult`, hang monitor lifecycle, and `MayProcessStillBeRunning` decisions behind a collaborator; orchestrator remains the public facade. | yes |
| Stop service only | Move `StopCleaningAsync`/`ForceStopCleaningAsync` mechanics out, but leave hang monitoring and current-process assignment in the orchestrator. | no |
| Leave termination intact | Avoid touching Phase 5 stop behavior in this refactor except through tests. Safer, but termination remains a monolithic responsibility. | no |
| You decide | Planner picks the safest termination seam after research. | no |

**User's choice:** Dedicated coordinator.

---

## Behavior Preservation

### User-Visible Changes
| Option | Description | Selected |
|--------|-------------|----------|
| Zero behavior change | Any user-visible change beyond incidental log/test structure is out of scope. Preserve success, skipped, failed, stopped, already-clean, backup-canceled, retention-warning, and dry-run behavior exactly. | yes |
| Allow wording polish | Allow small message wording improvements if tests are updated. Useful but risks overlapping Phase 11 diagnostics work. | no |
| Allow cleanup fixes | Permit behavior fixes discovered during extraction. Could reduce debt, but can turn a refactor phase into a functional-change phase. | no |
| You decide | Planner decides where behavior preservation ends. | no |

**User's choice:** Zero behavior change.

### Discovered Bugs
| Option | Description | Selected |
|--------|-------------|----------|
| Defer and note it | Capture it as a deferred idea or future gap unless it blocks behavior-preserving extraction. Keeps Phase 8 scoped to decomposition. | no |
| Fix if low risk | Allow small bug fixes when tests make the current behavior clearly wrong. Faster cleanup, but broadens scope. | no |
| Stop and ask | Pause planning/execution whenever a behavior bug appears. Safest for product intent, slower workflow. | yes |
| You decide | Planner chooses based on severity. | no |

**User's choice:** Stop and ask.

### Non-Negotiable Contracts
| Option | Description | Selected |
|--------|-------------|----------|
| All phase locks | List the prior locked contracts from Phases 5-7 plus project invariants: sequential xEdit, stop escalation, no log read after unsafe termination, launch escaping, backup cancellation, retention warning/cancel, concise user-facing messages. | yes |
| Only Phase 8 criteria | Mention only the Phase 8 success criteria and let prior CONTEXT files carry the rest. | no |
| Critical runtime only | Call out only sequential xEdit and stop/termination behavior; treat backup/diagnostic details as references. | no |
| You decide | Planner reads prior contexts and decides what to enforce. | no |

**User's choice:** All phase locks.

### Logs and Diagnostics
| Option | Description | Selected |
|--------|-------------|----------|
| Preserve user messages, flexible logs | User-facing messages/results stay identical; internal log line placement or collaborator names may change if tests are not asserting exact log text. | yes |
| Preserve logs too | Keep important log messages and categories as close as possible. More conservative, but can constrain extraction. | no |
| Improve logs now | Allow reorganized or richer log messages as part of decomposition. Useful for maintainers, but overlaps diagnostics boundaries. | no |
| You decide | Planner decides log stability needs. | no |

**User's choice:** Preserve user messages, flexible logs.

---

## Dry-Run/Preflight Sharing

### Sharing Depth
| Option | Description | Selected |
|--------|-------------|----------|
| Shared preflight plan | Create one preview-safe preflight/selection result consumed by both `RunDryRunAsync` and `StartCleaningAsync`; it handles config flush, game detection, variant, skip lists, exclusions, MO2 validation, and file validation without starting cleaning. | yes |
| Shared helpers only | Extract common helper methods for game detection/skip lists/file validation, but keep dry-run and real cleaning as separate flows. | no |
| Keep separate | Do not risk coupling dry-run to real cleaning in this phase; only add tests to catch drift. | no |
| You decide | Planner chooses the safest sharing depth. | no |

**User's choice:** Shared preflight plan.

### State Mutation
| Option | Description | Selected |
|--------|-------------|----------|
| Mode-specific state update | Preflight returns detected game/variant and selected plugins; `StartCleaningAsync` may apply the detected game to state, but `RunDryRunAsync` remains non-mutating as its current contract says. | yes |
| Never mutate state | Both real cleaning and dry-run use the preflight result without updating detected game state. Safer pure function, but may change current real-cleaning behavior. | no |
| Always mutate state | Dry-run also updates detected game like real cleaning. More consistency, but changes the existing dry-run contract. | no |
| You decide | Planner decides after checking tests. | no |

**User's choice:** Mode-specific state update.

### Skip Reason Rows
| Option | Description | Selected |
|--------|-------------|----------|
| Full reason rows | Return clean/skip candidates with explicit reasons like not selected, in skip list, missing/unreadable, invalid extension, or MO2 validation failure so dry-run and real cleaning can agree. | yes |
| Only clean list | Return only `pluginsToClean`; dry-run builds its own skipped rows. Smaller model, but keeps some duplication. | no |
| Separate clean and skipped lists | Return distinct collections for will-clean and will-skip results. Clearer for dry-run, more model surface. | no |
| You decide | Planner designs the result shape. | no |

**User's choice:** Full reason rows.

### MO2 Policy
| Option | Description | Selected |
|--------|-------------|----------|
| Explicit policy flags | Preflight should carry `isMo2Mode`, backup-skipped, file-validation-skipped, and launch-mode facts so downstream steps don't rediscover them. | yes |
| Only validate MO2 | Preflight checks MO2 path validity, but backup/file-validation decisions remain in later collaborators. | no |
| Keep current inline rules | Do not centralize MO2 decisions now; avoid altering a fragile launch boundary. | no |
| You decide | Planner decides based on safest extraction. | no |

**User's choice:** Explicit policy flags.

---

## Test Proof Expectations

### Minimum Proof
| Option | Description | Selected |
|--------|-------------|----------|
| Characterization plus seams | Add/retain characterization tests for current session outcomes, then add focused collaborator tests proving each extracted responsibility can change independently. | yes |
| Only existing tests pass | Rely on the current suite after refactor. Fastest, but weak evidence for the maintainer-facing success criteria. | no |
| Broad integration matrix | Add many end-to-end orchestrator scenarios for all outcomes. Strong behavior proof, but may be slower and brittle. | no |
| You decide | Planner picks test scope. | no |

**User's choice:** Characterization plus seams.

### Behavior Matrix
| Option | Description | Selected |
|--------|-------------|----------|
| Roadmap outcomes | Cover successful, skipped, failed, stopped/left-running, already-clean, backup-canceled/failed choices, and retention warning/canceled paths where current tests are thin. | yes |
| Only new seams | Skip broad behavior matrix and test each new collaborator contract after extraction. | no |
| Everything touched | Every branch moved out of the orchestrator needs before/after tests. Max confidence, higher planning cost. | no |
| You decide | Planner identifies gaps from existing test coverage. | no |

**User's choice:** Roadmap outcomes.

### Source-Level Guards
| Option | Description | Selected |
|--------|-------------|----------|
| Yes, sparingly | Use source-level or structural checks only for hard-to-observe invariants like no parallel xEdit constructs and stable public orchestrator surface; prefer behavior tests otherwise. | yes |
| No source tests | Only behavior/unit tests. Cleaner, but some architectural invariants may be harder to prove. | no |
| Use many guards | Assert class/file structure, method names, and collaborators explicitly. Strong architecture lock, but brittle under valid refactors. | no |
| You decide | Planner chooses structural verification level. | no |

**User's choice:** Yes, sparingly.

### Interaction Testing
| Option | Description | Selected |
|--------|-------------|----------|
| Contract outcomes first | Test collaborator inputs/outputs and state/result effects; assert call order only where it protects behavior such as backup before xEdit and log offset before launch. | yes |
| Strict call sequence | Mock every collaborator and assert the exact workflow call order. High control, but brittle. | no |
| Integration only | Avoid collaborator unit tests; test through `ICleaningOrchestrator` only. Durable, but less proof that collaborators are independently changeable. | no |
| You decide | Planner balances unit and integration coverage. | no |

**User's choice:** Contract outcomes first.

---

## the agent's Discretion

- Exact collaborator type names, interface names, internal model field names, test method names, and plan grouping.

## Deferred Ideas

None.
