# Phase 11: User-Facing Diagnostics Boundaries - Specification

**Created:** 2026-05-01
**Ambiguity score:** 0.18 (gate: <= 0.20)
**Requirements:** 6 locked

## Goal

AutoQAC user-facing error surfaces and diagnostic logs stop exposing stack traces, raw exception text, full local paths, and full command-line details unless that detail is necessary for local troubleshooting.

## Background

Phase 11 covers the remaining SEC-01 and SEC-02 cleanup requirements. The roadmap requires concise, actionable user errors, log-file references instead of stack traces, and diagnostic logs that avoid unnecessary path and command exposure. The codebase already has some safe patterns: `SettingsViewModel` maps `ConfigPersistenceFailure` categories into concise banners, `RestoreViewModel` uses generic technical-detail copy for most restore/delete failures, `CleaningService` returns concise launch failure messages, and backup results use `BackupFailureReason` labels. Scouting also found remaining leaks: `CleaningCommandsViewModel.StartCleaningAsync` and `PreviewAsync` can show `ex.Message` plus `ex.StackTrace`, inline validation can show configured xEdit/MO2/load-order full paths, load-order and folder selection dialogs can show selected full paths plus raw exception messages, `RestoreViewModel.LoadSessionsAsync` can put `ex.Message` into status text, migration warnings can expose raw exception messages, startup logging records the full configured xEdit path, and process-start logs still include executable paths even though `ArgumentList` payloads are summarized.

## Requirements

1. **Unexpected error UI boundary**: Unexpected user-facing errors must be concise and must not expose stack traces, raw exception messages, full paths, or command fragments.
   - Current: `CleaningCommandsViewModel.StartCleaningAsync` and `PreviewAsync` set status text from `ex.Message` and show dialog details containing `ex.Message` and `ex.StackTrace`.
   - Target: Unexpected cleaning and preview failures show a generic actionable message with a latest AutoQAC log reference; technical exception details stay in logs.
   - Acceptance: Tests simulate exceptions containing an xEdit path, MO2 path, plugin path, command fragment, and stack-like text, then assert dialog title/message/details and status text exclude those details and include a latest AutoQAC log reference.

2. **Path validation UI boundary**: Validation and browse-path errors must identify the failing setting or resource without showing full local filesystem paths.
   - Current: Inline validation and dialogs can show full configured or selected paths for missing xEdit, MO2, load-order files, and game-data folders.
   - Target: User-facing validation copy may show file names, setting names, game names, plugin file names, and next actions, but not full profile-root, game-install, xEdit, MO2, load-order, game-data, or plugin paths.
   - Acceptance: Tests cover missing xEdit, missing MO2, missing load-order, invalid selected load-order, and missing selected game-data folder with representative `C:\Users\...`, game install, and tool paths; no dialog, inline validation detail, or status text contains the full path.

3. **Result and export boundary**: Cleaning result messages and exported cleaning reports must not carry unsafe diagnostic details forward from service failures.
   - Current: `CleaningSessionResult.GenerateReport()` writes each failed plugin's `result.Message`, so any unsafe failure message becomes part of a user-facing export.
   - Target: Failed plugin result messages used by result windows and exported reports contain plugin file names, safe status labels, counts, durations, and latest-log guidance only; they never contain full paths, command fragments, stack traces, or raw exception text.
   - Acceptance: Tests create failed cleaning results with unsafe exception/path/command-like input and assert generated reports and result rows exclude full paths, command fragments, stack traces, and raw exception messages while preserving plugin file names and safe failure summaries.

4. **Migration, restore, and persistence message consistency**: Existing category-based diagnostics remain safe, and remaining user-visible exception-message leaks are replaced with safe summaries.
   - Current: Settings persistence banners and restore/delete failures mostly use safe categories, but session loading and legacy migration can still expose raw exception messages in user-visible status or warning text.
   - Target: Settings, restore, backup-session loading, delete-session, and legacy migration surfaces use safe category text plus latest-log guidance where technical detail is needed; existing `ConfigPersistenceFailure.SafeSummary` and `BackupFailureReason` label patterns are preserved.
   - Acceptance: Tests simulate backup-session load failure, legacy migration failure, persistence write/read failure, and restore/delete failures with exception messages containing full paths; user-visible text excludes raw exception/path detail and preserves the expected safe category or generic failure label.

5. **Log command boundary**: AutoQAC logs must retain troubleshooting value without emitting full reconstructed command lines or raw argv payloads.
   - Current: `ProcessExecutionService` summarizes `ArgumentList` payloads but still logs `FileName`; startup diagnostics log the full configured xEdit path; other logs include paths for configuration, plugin loading, xEdit log files, and backup operations.
   - Target: Launch/startup logs do not include full xEdit/MO2 command lines or raw nested argv payloads; logs may include plugin file names, game names, operation names, safe categories, PIDs, counts, and full paths only where the path directly identifies the failing local resource needed for troubleshooting.
   - Acceptance: Tests or source-level assertions verify process-start and startup diagnostics exclude full command lines, raw `ArgumentList`/nested payload content, and configured executable paths while still logging enough context to identify operation, plugin, game or mode, PID, and safe reason.

6. **Regression test coverage**: Phase 11 must lock the diagnostics boundary with tests that fail on reintroduced user-facing stack/path/command leaks.
   - Current: Some Phase 6 tests verify launch failure messages do not disclose configured paths or payloads, but there is no repository-wide Phase 11 coverage for dialogs, inline validation/status text, reports, startup logs, and remaining exception-message leak paths.
   - Target: The changed surfaces have explicit tests for negative disclosure checks and positive actionability checks.
   - Acceptance: The Phase 11 test set proves no covered user-facing surface contains stack trace markers, raw exception messages, full local paths, or command fragments, and the full solution test suite passes.

## Boundaries

**In scope:**
- Existing user-facing error diagnostics in modal dialogs, inline validation panels, status text, cleaning result messages, and exported cleaning reports.
- Existing known leak paths found during scouting: cleaning/preview unexpected failures, configuration path browse failures, inline path validation, restore session load status, legacy migration warnings, startup diagnostics, and process-start diagnostics.
- Regression tests that prove safe user-facing diagnostics and log command boundaries.
- Preservation of existing safe category patterns from `ConfigPersistenceFailure.SafeSummary` and `BackupFailureReason` labels.
- Allowing plugin file names, game names, operation names, safe failure categories, counts, durations, PIDs, and latest AutoQAC log guidance in user-facing text when useful.

**Out of scope:**
- Adding a new diagnostics window, log viewer, telemetry, crash reporter, or issue-report exporter - this phase hardens existing surfaces only.
- Redacting xEdit-owned log files, backup file contents, user YAML file contents, or external tool output at rest - this phase controls AutoQAC UI/export/log emission.
- Removing every full path from AutoQAC logs - logs may keep a full path when it directly identifies the failing local resource needed for troubleshooting.
- Rewriting all application copy or normal progress/status text unrelated to errors - the phase is diagnostics-boundary focused.
- Changing xEdit/MO2 launch behavior, command construction, sequential cleaning, backup safety semantics, or plugin refresh behavior - adjacent behavior remains unchanged except for diagnostic text/log output.
- SEC-03 executable-name warnings - that requirement is explicitly future work.

## Constraints

- Preserve sequential xEdit cleaning; no diagnostics change may parallelize plugin cleaning or alter the one-process slot behavior.
- Preserve Windows-only assumptions and local troubleshooting value for a desktop app that launches user-selected local executables.
- User-facing copy may include plugin file names but not full plugin paths.
- User-facing copy must use latest AutoQAC log guidance instead of full log file paths.
- Logs must not reconstruct or emit full xEdit/MO2 command lines or raw nested argv payloads.
- Logs may include full paths only when the path is the direct local resource involved in a file/config/log/backup failure and omitting it would materially reduce local troubleshooting value.
- Existing tests that intentionally verify safe Phase 6 launch failure messages must remain valid or be tightened, not loosened.

## Acceptance Criteria

- [ ] Unexpected cleaning and preview exceptions do not surface stack traces, raw exception messages, full paths, or command fragments in dialogs or status text.
- [ ] Missing xEdit, MO2, load-order, selected load-order, and selected game-data folder errors identify the setting/action without exposing full local paths.
- [ ] Cleaning result rows and exported cleaning reports preserve plugin file names and safe summaries while excluding full paths, command fragments, stack traces, and raw exception text.
- [ ] Backup-session loading, restore/delete failures, Settings persistence failures, and legacy migration warnings use safe category/generic copy and latest-log guidance instead of raw exception details.
- [ ] Process-start and startup diagnostics do not log full command lines, raw argv payloads, nested MO2 payloads, or configured xEdit/MO2 executable paths.
- [ ] Logs still preserve actionable local troubleshooting context through operation names, plugin file names, game/mode labels, safe failure categories, counts, durations, PIDs, and direct failing-resource paths where necessary.
- [ ] Regression tests fail if covered user-facing surfaces reintroduce stack trace markers, raw exception messages, full local paths, or command fragments.
- [ ] `dotnet test AutoQACSharp.slnx` passes after implementation.

## Ambiguity Report

| Dimension           | Score | Min   | Status | Notes |
|---------------------|-------|-------|--------|-------|
| Goal Clarity        | 0.90  | 0.75  | met    | Goal locks concise user-facing diagnostics and bounded log disclosure. |
| Boundary Clarity    | 0.80  | 0.70  | met    | Scope covers errors and reports, not all app copy or new diagnostics tooling. |
| Constraint Clarity  | 0.78  | 0.65  | met    | User-facing path/log-reference rules and log command boundaries are explicit. |
| Acceptance Criteria | 0.76  | 0.70  | met    | Eight pass/fail checks cover UI, exports, logs, and regression tests. |
| **Ambiguity**       | 0.18  | <=0.20| met    | Gate passed after round 2. |

Status: met = dimension meets the minimum; below minimum = planner treats as assumption.

## Interview Log

| Round | Perspective | Question summary | Decision locked |
|-------|-------------|------------------|-----------------|
| 1 | Researcher | Which user-facing diagnostic surfaces must Phase 11 cover? | Modal dialogs, inline validation/status errors, cleaning result messages, and exported cleaning reports are required scope. |
| 1 | Researcher | Which details are unacceptable in user-facing error text? | Stack traces, raw exception text, full xEdit/MO2/load-order/game-data/plugin paths, and full command fragments are disallowed; plugin file names remain allowed. |
| 1 | Researcher | What is the log-side SEC-02 target? | Logs must retain troubleshooting context but must not emit full reconstructed command lines or raw argv payloads; full paths are minimized to directly failing resources. |
| 2 | Researcher + Simplifier | What is the irreducible successful outcome? | Harden the discovered leak paths and add regression tests proving they stay safe. |
| 2 | Researcher + Simplifier | What user-facing path detail remains allowed? | Show file/executable/plugin names and the setting/action to fix; do not show full local paths in dialogs, inline errors, status, or exports. |
| 2 | Researcher + Simplifier | What satisfies the log-file reference requirement? | Use concise latest AutoQAC log guidance without exposing a full log path. |
| Gate | Spec Gate Passed | Ambiguity 0.18 with all dimensions above minimum | User selected "Yes - write SPEC.md". |

---

*Phase: 11-user-facing-diagnostics-boundaries*
*Spec created: 2026-05-01*
*Next step: /gsd-discuss-phase 11 - implementation decisions only*
