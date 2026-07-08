---
phase: 11
slug: user-facing-diagnostics-boundaries
status: verified
nyquist_compliant: true
wave_0_complete: true
created: 2026-04-30
updated: 2026-05-01
---

# Phase 11 — Validation Strategy

Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + FluentAssertions 8.8.0 + NSubstitute 5.3.0 |
| **Config file** | none — SDK-style test projects with package references |
| **Quick run command** | `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~DiagnosticTextFormatterTests|FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~CleaningServiceTests|FullyQualifiedName~LegacyMigrationServiceTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~Phase11" --nologo` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx --nologo` |
| **Estimated runtime** | Focused command should complete within normal single-project test latency; full suite is the phase gate. |

---

## Sampling Rate

- **After every task commit:** Run the task's focused `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter ... --nologo` command from the PLAN.md task.
- **After every plan wave:** Run the focused Phase 11 command above. Run commands sequentially, not in parallel, to avoid recurring MSBuild file-lock contention.
- **Before `/gsd-verify-work`:** Run `dotnet test AutoQACSharp.slnx --nologo` and keep the full suite green, or record any unrelated pre-existing failure exactly in the plan summary.
- **Max feedback latency:** one task; every task has an automated command and no three-task gap exists.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 11-01-01 | 01 | 1 | SEC-01, SEC-02 | T-11-01 | Shared formatter tests lock safe copy, sanitized basenames, unsafe exception/path/command fallback, and non-C drive-rooted path detection. | unit/model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~DiagnosticTextFormatterTests --nologo` | ✅ | ✅ green |
| 11-01-02 | 01 | 1 | SEC-01, SEC-02 | T-11-01 | `DiagnosticTextFormatter` implements safe helpers and returns fallbacks for unsafe candidate details. | unit/model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~DiagnosticTextFormatterTests --nologo` | ✅ | ✅ green |
| 11-02-01 | 02 | 2 | SEC-01 | T-11-02 | Cleaning/preview unexpected error dialogs and status exclude raw exception/path/command details. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ErrorDialogTests --nologo` | ✅ | ✅ green |
| 11-02-02 | 02 | 2 | SEC-01 | T-11-02 | Cleaning/preview catches use safe formatter copy and latest-log details. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ErrorDialogTests --nologo` | ✅ | ✅ green |
| 11-02-03 | 02 | 2 | SEC-01 | T-11-03 | Pre-clean validation shows safe xEdit/MO2/load-order identifiers without full configured paths. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~CleaningCommandsViewModel" --nologo` | ✅ | ✅ green |
| 11-03-01 | 03 | 2 | SEC-01 | T-11-03 | Configuration browse/read failures use safe file/folder labels and latest-log guidance only for technical failures. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~Configuration" --nologo` | ✅ | ✅ green |
| 11-03-02 | 03 | 2 | SEC-01 | T-11-02 | Backup-session load failures use generic latest-log guidance instead of raw exception messages. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests --nologo` | ✅ | ✅ green |
| 11-03-03 | 03 | 2 | SEC-01 | T-11-02 | Settings persistence write/read failures preserve `ConfigPersistenceFailure.SafeSummary` and exclude unsafe path-bearing input. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~SettingsViewModelTests --nologo` | ✅ | ✅ green |
| 11-04-01 | 04 | 2 | SEC-01 | T-11-04 | Result finalizer creates safe failed plugin and xEdit exception-log messages at source. | unit/service | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~PluginResultFinalizerTests --nologo` | ✅ | ✅ green |
| 11-04-02 | 04 | 2 | SEC-01 | T-11-04 | Reports include disclaimer once and failed-row summaries defensively reject unsafe details. | unit/model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningSessionResultTests --nologo` | ✅ | ✅ green |
| 11-05-01 | 05 | 2 | SEC-02 | T-11-05 | Process-start/failure logs exclude executable paths and raw argv, include argument count/reason, and include PID after successful start when available. | unit/service/logger-capture | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProcessExecutionServiceTests --nologo` | ✅ | ✅ green |
| 11-05-02 | 05 | 2 | SEC-01, SEC-02 | T-11-06 | Startup diagnostics avoid configured executable paths and migration warnings avoid raw exception text. | integration/source guard | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~Startup" --nologo` | ✅ | ✅ green |
| 11-06-01 | 06 | 3 | SEC-01, SEC-02 | T-11-07 | Phase-level sentinel tests fail if UI/export/log forbidden strings return. | unit/source guard | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Phase11 --nologo` | ✅ | ✅ green |
| 11-06-02 | 06 | 3 | SEC-01, SEC-02 | T-11-07 | Focused Phase 11 and full solution verification pass. | suite | `dotnet test AutoQACSharp.slnx --nologo` | ✅ | ✅ green |
| 11-07-01 | 07 | 4 | SEC-01 | T-11-07-01 | Legacy migration warning tests reject path, command, exception, and stack sentinel disclosure. | unit/service + source guard | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~LegacyMigrationServiceTests|FullyQualifiedName~DependencyInjectionTests" --nologo` | ✅ | ✅ green |
| 11-07-02 | 07 | 4 | SEC-01 | T-11-07-02 | Legacy migration warning UI copy uses fixed safe latest-log categories and a defensive startup display boundary. | unit/service + source guard | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~LegacyMigrationServiceTests|FullyQualifiedName~DependencyInjectionTests" --nologo` | ✅ | ✅ green |
| 11-08-01 | 08 | 4 | SEC-02 | T-11-08-01 | Successful legacy-Arguments starts track safe `ExternalProcess` labels and exclude raw launch payloads. | unit/service/logger-capture | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~Phase11LogBoundaryTests" --nologo` | ✅ | ✅ green |
| 11-08-02 | 08 | 4 | SEC-02 | T-11-08-02 | Process PID tracking uses sanitized plugin names or `ExternalProcess`, never raw legacy arguments. | unit/service/logger-capture | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~Phase11LogBoundaryTests" --nologo` | ✅ | ✅ green |
| 11-09-01 | 09 | 4 | SEC-01 | T-11-09-01 | Report plugin-name prefix tests cover path-like, control, quote, separator, and command-fragment display inputs. | unit/model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~Phase11ReportBoundaryTests" --nologo` | ✅ | ✅ green |
| 11-09-02 | 09 | 4 | SEC-01 | T-11-09-02 | Report rows sanitize plugin display prefixes and failed fallback summaries without mutating internal model data. | unit/model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~Phase11ReportBoundaryTests" --nologo` | ✅ | ✅ green |
| 11-10-01 | 10 | 5 | SEC-01 | T-11-10-01 | CleaningService failed-message tests prove unsafe plugin basenames stay out of service, finalizer, summary, and report surfaces. | unit/service + model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningServiceTests|FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~CleaningSessionResultTests" --nologo` | ✅ | ✅ green |
| 11-10-02 | 10 | 5 | SEC-01 | T-11-10-02 | CleaningService command-build and unexpected-exception failed messages use sanitized plugin display text at source. | unit/service + model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningServiceTests|FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~CleaningSessionResultTests" --nologo` | ✅ | ✅ green |
| 11-11-01 | 11 | 6 | SEC-01 | T-11-11-01 | Path-bearing xEdit log-read warnings are proven not to reach `LogParseWarning`, summaries, or reports. | unit/service + Phase11 guard | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~Phase11" --nologo` | ✅ | ✅ green |
| 11-11-02 | 11 | 6 | SEC-01 | T-11-11-01 | `PluginResultFinalizer` replaces UI-bound log-read warnings with stable latest-log copy while preserving raw local logs. | unit/service + Phase11 guard | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~Phase11" --nologo` | ✅ | ✅ green |
| 11-11-03 | 11 | 6 | SEC-01 | T-11-11-03 | Focused Phase 11/finalizer verification and full suite evidence are recorded in verification artifacts. | suite/docs | `dotnet test AutoQACSharp.slnx --nologo` | ✅ | ✅ green |
| 11-12-01 | 12 | 7 | SEC-01 | T-11-12-01 | Timeout retry and backup failure callback tests reject unsafe callback plugin/error payloads. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~Phase11DiagnosticsBoundaryTests" --nologo` | ✅ | ✅ green |
| 11-12-02 | 12 | 7 | SEC-01 | T-11-12-02, T-11-12-03 | Timeout retry and backup failure dialog boundaries sanitize callback values before user-facing display, with dialog-service defense-in-depth. | unit/ViewModel + dialog service | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~Phase11DiagnosticsBoundaryTests" --nologo` | ✅ | ✅ green |
| 11-13-01 | 13 | 7 | SEC-01 | T-11-13-01 | Restore Selected tests prove unsafe backup metadata filenames are sanitized in confirmation/status/error copy. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~DiagnosticTextFormatterTests" --nologo` | ✅ | ✅ green |
| 11-13-02 | 13 | 7 | SEC-01 | T-11-13-02 | Restore Selected UI uses safe display names while restore service calls still receive the original `BackupPluginEntry`. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~DiagnosticTextFormatterTests" --nologo` | ✅ | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. No separate framework install or scaffold-only wave is required because each TDD task creates its own failing tests before implementation, and every task has a concrete automated command.

---

## Manual-Only Verifications

All phase behaviors have automated verification. No manual-only validation is required.

---

## Validation Audit 2026-05-01

| Metric | Count |
|--------|-------|
| Gaps found | 5 |
| Resolved | 5 |
| Escalated | 0 |
| Tests created or modified | 0 |

Audit result: existing automated tests already covered Phase 11 requirements through Plan 11-13. The validation gaps were stale map/status entries and an incomplete focused command, so no redundant tests were generated.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify commands.
- [x] Sampling continuity: no 3 consecutive tasks without automated verify.
- [x] Wave 0 covers all MISSING references through task-local RED tests; no external test framework setup is missing.
- [x] No watch-mode flags.
- [x] Feedback latency is task-level and bounded by focused `dotnet test` commands.
- [x] `nyquist_compliant: true` set in frontmatter.

**Approval:** approved 2026-05-01
