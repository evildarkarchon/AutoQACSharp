# Requirements

## Active

## Validated

### R001 — User can stop cleaning without AutoQAC force-killing xEdit before the confirmation path is shown.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: SAF-01

User can stop cleaning without AutoQAC force-killing xEdit before the confirmation path is shown.

### R002 — User can see an accurate failure outcome when force-killing xEdit fails.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: SAF-02

User can see an accurate failure outcome when force-killing xEdit fails.

### R003 — User can clean plugins whose paths or names contain quotes, Unicode, spaces, or shell-sensitive characters.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: SAF-03

User can clean plugins whose paths or names contain quotes, Unicode, spaces, or shell-sensitive characters.

### R004 — User can restore backups with clear failure reporting when directories are missing, permissions fail, or a session partially restores.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: SAF-04

User can restore backups with clear failure reporting when directories are missing, permissions fail, or a session partially restores.

### R005 — Maintainer can change cleaning preflight, backup, execution, result finalization, or termination logic without editing one monolithic cleaning orchestrator.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: REF-01

Maintainer can change cleaning preflight, backup, execution, result finalization, or termination logic without editing one monolithic cleaning orchestrator.

### R006 — Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: REF-02

Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`.

### R007 — Maintainer can reason about configuration saves, reloads, deferrals, and failures through one serialized persistence flow.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: REF-03

Maintainer can reason about configuration saves, reloads, deferrals, and failures through one serialized persistence flow.

### R008 — Maintainer can test PID tracking through injected storage/path abstractions with process-safe update behavior.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: REF-04

Maintainer can test PID tracking through injected storage/path abstractions with process-safe update behavior.

### R009 — Maintainer can verify real child-process timeout, graceful stop, force kill, and PID cleanup behavior through controlled integration tests.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: TEST-01

Maintainer can verify real child-process timeout, graceful stop, force kill, and PID cleanup behavior through controlled integration tests.

### R010 — Maintainer can verify xEdit and MO2 command argument escaping across quotes, Unicode, shell-sensitive characters, and nested arguments.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: TEST-02

Maintainer can verify xEdit and MO2 command argument escaping across quotes, Unicode, shell-sensitive characters, and nested arguments.

### R011 — Maintainer can verify configuration watcher race cases deterministically.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: TEST-03

Maintainer can verify configuration watcher race cases deterministically.

### R012 — Maintainer can verify backup restore safety across missing target directories, permission failures, partial failures, and cleanup deletion failures.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: TEST-04

Maintainer can verify backup restore safety across missing target directories, permission failures, partial failures, and cleanup deletion failures.

### R013 — User sees concise error dialogs with log-file references instead of stack traces or excessive internal path detail.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: SEC-01

User sees concise error dialogs with log-file references instead of stack traces or excessive internal path detail.

### R014 — User diagnostic logs avoid unnecessary full command-line/path exposure while preserving enough information for local troubleshooting.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: SEC-02

User diagnostic logs avoid unnecessary full command-line/path exposure while preserving enough information for local troubleshooting.

### R015 — User can refresh plugin issue approximations with better cancellation, reduced redundant load-order work, or narrower target scope.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: PERF-01

User can refresh plugin issue approximations with better cancellation, reduced redundant load-order work, or narrower target scope.

### R016 — User can run ITM approximation on large plugins without materializing every override context for each record.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: PERF-02

User can run ITM approximation on large plugins without materializing every override context for each record.

### R017 — User configuration changes avoid YAML serialization round-trips for in-memory cloning.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: PERF-03

User configuration changes avoid YAML serialization round-trips for in-memory cloning.

### R018 — User backup and retention operations remain cancellable and visible without parallelizing xEdit cleaning.

- Status: validated
- Class: core-capability
- Source: inferred
- Primary Slice: none yet

Legacy ID: PERF-04

User backup and retention operations remain cancellable and visible without parallelizing xEdit cleaning.

## Deferred

## Out of Scope
