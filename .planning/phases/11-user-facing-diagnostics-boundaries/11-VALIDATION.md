---
phase: 11
slug: user-facing-diagnostics-boundaries
status: ready
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
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~DiagnosticTextFormatterTests|FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~PluginResultFinalizerTests|FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~Phase11" --nologo` |
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
| 11-01-01 | 01 | 1 | SEC-01, SEC-02 | T-11-01 | Shared formatter tests lock safe copy, sanitized basenames, unsafe exception/path/command fallback, and non-C drive-rooted path detection. | unit/model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~DiagnosticTextFormatterTests --nologo` | ❌ created by task | ⬜ pending |
| 11-01-02 | 01 | 1 | SEC-01, SEC-02 | T-11-01 | `DiagnosticTextFormatter` implements safe helpers and returns fallbacks for unsafe candidate details. | unit/model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~DiagnosticTextFormatterTests --nologo` | ❌ created by task | ⬜ pending |
| 11-02-01 | 02 | 2 | SEC-01 | T-11-02 | Cleaning/preview unexpected error dialogs and status exclude raw exception/path/command details. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ErrorDialogTests --nologo` | ✅ | ⬜ pending |
| 11-02-02 | 02 | 2 | SEC-01 | T-11-02 | Cleaning/preview catches use safe formatter copy and latest-log details. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ErrorDialogTests --nologo` | ✅ | ⬜ pending |
| 11-02-03 | 02 | 2 | SEC-01 | T-11-03 | Pre-clean validation shows safe xEdit/MO2/load-order identifiers without full configured paths. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ErrorDialogTests|FullyQualifiedName~CleaningCommandsViewModel" --nologo` | ✅ | ⬜ pending |
| 11-03-01 | 03 | 2 | SEC-01 | T-11-03 | Configuration browse/read failures use safe file/folder labels and latest-log guidance only for technical failures. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~Configuration" --nologo` | ✅ | ⬜ pending |
| 11-03-02 | 03 | 2 | SEC-01 | T-11-02 | Backup-session load failures use generic latest-log guidance instead of raw exception messages. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~RestoreViewModelTests --nologo` | ✅ | ⬜ pending |
| 11-03-03 | 03 | 2 | SEC-01 | T-11-02 | Settings persistence write/read failures preserve `ConfigPersistenceFailure.SafeSummary` and exclude unsafe path-bearing input. | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~SettingsViewModelTests --nologo` | ✅ | ⬜ pending |
| 11-04-01 | 04 | 2 | SEC-01 | T-11-04 | Result finalizer creates safe failed plugin and xEdit exception-log messages at source. | unit/service | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~PluginResultFinalizerTests --nologo` | ✅ | ⬜ pending |
| 11-04-02 | 04 | 2 | SEC-01 | T-11-04 | Reports include disclaimer once and failed-row summaries defensively reject unsafe details. | unit/model | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningSessionResultTests --nologo` | ✅ | ⬜ pending |
| 11-05-01 | 05 | 2 | SEC-02 | T-11-05 | Process-start/failure logs exclude executable paths and raw argv, include argument count/reason, and include PID after successful start when available. | unit/service/logger-capture | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProcessExecutionServiceTests --nologo` | ✅ | ⬜ pending |
| 11-05-02 | 05 | 2 | SEC-01, SEC-02 | T-11-06 | Startup diagnostics avoid configured executable paths and migration warnings avoid raw exception text. | integration/source guard | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~Startup" --nologo` | ✅ | ⬜ pending |
| 11-06-01 | 06 | 3 | SEC-01, SEC-02 | T-11-07 | Phase-level sentinel tests fail if UI/export/log forbidden strings return. | unit/source guard | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Phase11 --nologo` | ❌ created by task | ⬜ pending |
| 11-06-02 | 06 | 3 | SEC-01, SEC-02 | T-11-07 | Focused Phase 11 and full solution verification pass. | suite | `dotnet test AutoQACSharp.slnx --nologo` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. No separate framework install or scaffold-only wave is required because each TDD task creates its own failing tests before implementation, and every task has a concrete automated command.

---

## Manual-Only Verifications

All phase behaviors have automated verification. No manual-only validation is required.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify commands.
- [x] Sampling continuity: no 3 consecutive tasks without automated verify.
- [x] Wave 0 covers all MISSING references through task-local RED tests; no external test framework setup is missing.
- [x] No watch-mode flags.
- [x] Feedback latency is task-level and bounded by focused `dotnet test` commands.
- [x] `nyquist_compliant: true` set in frontmatter.

**Approval:** approved 2026-05-01
