---
phase: 13
slug: command-launch-escaping-reverification-safe-mo2-failures
status: passed
nyquist_compliant: true
wave_0_complete: true
created: 2026-05-01
audited: 2026-05-01
---

# Phase 13 — Validation Strategy

> Per-phase validation contract for command-launch escaping and safe MO2 failure evidence.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3, FluentAssertions 8.8.0, NSubstitute 5.3.0 |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj`, `QueryPlugins.Tests/QueryPlugins.Tests.csproj` |
| **Quick run command** | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~CleaningServiceTests"` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx` |
| **Estimated runtime** | Targeted commands should complete quickly; full suite runtime depends on process integration tests. |

---

## Sampling Rate

- **After targeted evidence collection:** Update each acceptance-criterion row with command output summary and pass/fail status.
- **After any minimal gap fix:** Rerun the failed targeted command, then rerun the combined targeted evidence command.
- **Before final verification:** Run `dotnet test AutoQACSharp.slnx` and record whether the full suite passed or whether failures are unrelated per D-07.
- **Before `/gsd-verify-work`:** `13-VERIFICATION.md` must trace `SAF-03` and `TEST-02` conclusions to rows in this file.

---

## Acceptance-Criterion Evidence Matrix

| Row ID | Acceptance Criterion | Requirement | Evidence Source | Automated Command / Inspection | Result | Status |
|--------|----------------------|-------------|-----------------|--------------------------------|--------|--------|
| AC-01 | MO2 mode with null, empty, or whitespace MO2 executable path produces no command and cannot launch direct xEdit. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests"` | Passed after sequential rerun: `AutoQAC.Tests.dll` reported Failed: 0, Passed: 15, Skipped: 0, Total: 15. The initial parallel executor run hit a transient `CS2012` build-output file lock, then the exact targeted command passed when rerun alone. | ✅ green |
| AC-02 | The missing-MO2 failure path reports a failed no-process-started result and does not call `IProcessExecutionService.ExecuteAsync`. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/CleaningServiceTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~CleaningServiceTests"` | Passed: `AutoQAC.Tests.dll` reported Failed: 0, Passed: 17, Skipped: 0, Total: 17, including `CleanPluginAsync_WhenMo2CommandBuildFails_ShouldReturnSafeFailureWithoutStartingProcess` and `DidNotReceive().ExecuteAsync(...)` coverage. | ✅ green |
| AC-03 | Direct xEdit command-builder tests pass for quotes, Unicode, spaces, shell-sensitive punctuation, and combined worst-case plugin names. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests"` | Passed after sequential rerun: `AutoQAC.Tests.dll` reported Failed: 0, Passed: 15, Skipped: 0, Total: 15, covering direct-mode difficult-character argv and exact plugin filename assertions. | ✅ green |
| AC-04 | Configured MO2 command-builder tests pass for the four-argument wrapper contract and parsed nested xEdit payload. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests"` | Passed after sequential rerun: `AutoQAC.Tests.dll` reported Failed: 0, Passed: 15, Skipped: 0, Total: 15, covering `run`, xEdit path, `-a`, one nested payload, and parsed payload preservation. | ✅ green |
| AC-05 | Process-boundary integration tests pass for `ArgumentList` preservation and legacy `Arguments` fallback. | TEST-02 | `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~ProcessExecutionIntegrationTests"` | Passed: `AutoQAC.Tests.dll` reported Failed: 0, Passed: 7, Skipped: 0, Total: 7, including real helper-process `ArgumentList` preservation and legacy `Arguments` fallback evidence. | ✅ green |
| AC-06 | Launch-failure diagnostics tests pass for command-build failure, mocked launch-start failure, and unexpected launch exception non-disclosure. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/CleaningServiceTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~CleaningServiceTests"`; combined targeted command | Passed: `CleaningServiceTests` reported Failed: 0, Passed: 17, Skipped: 0, Total: 17. Combined targeted command reported Failed: 0, Passed: 39, Skipped: 0, Total: 39, covering command-build failure, mocked launch-start failure, and unexpected exception non-disclosure boundaries. | ✅ green |
| AC-07 | Fresh Phase 13 verification/validation artifacts mark `SAF-03` and `TEST-02` satisfied using current evidence. | SAF-03, TEST-02 | `13-VALIDATION.md`, `13-VERIFICATION.md` | File inspection plus `dotnet test AutoQACSharp.slnx` | Passed: `dotnet test AutoQACSharp.slnx` completed with `QueryPlugins.Tests.dll` Failed: 0, Passed: 61, Skipped: 0, Total: 61 and `AutoQAC.Tests.dll` Failed: 0, Passed: 1016, Skipped: 0, Total: 1016. No unrelated full-suite failures required D-07 classification. `13-VERIFICATION.md` is produced by Plan 02 and traces `SAF-03`/`TEST-02` conclusions to AC-01 through AC-08. | ✅ green |
| AC-08 | Phase 13 does not rewrite historical Phase 6 verification artifacts as part of closing the stale audit gap. | SAF-03, TEST-02 | Git diff / file inspection | `git diff -- .planning/phases/06-command-launch-escaping .planning/v1.0-MILESTONE-AUDIT.md .planning/REQUIREMENTS.md .planning/ROADMAP.md` | Passed: command produced no output before validation edits, confirming historical Phase 6 artifacts, milestone audit markers, `REQUIREMENTS.md`, and `ROADMAP.md` status markers were not edited during Plan 02 execution. Phase 13 records current evidence additively instead of changing historical or marker files. | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ unrelated failure documented*

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 13-01-01 | 01 | 1 | SAF-03, TEST-02 | T-13-01 / T-13-02 | MO2 missing configuration fails closed before process start. | unit | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests"`; `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~CleaningServiceTests"` | ✅ | ✅ green |
| 13-01-02 | 01 | 1 | SAF-03, TEST-02 | T-13-02 / T-13-03 | Direct/MO2 argv preservation and safe launch diagnostics remain covered. | unit/integration | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~CleaningServiceTests"` | ✅ | ✅ green |
| 13-02-01 | 02 | 2 | SAF-03, TEST-02 | T-13-01 / T-13-02 / T-13-03 | Full-suite evidence supports the targeted conclusions or unrelated failures are classified. | full suite | `dotnet test AutoQACSharp.slnx` | ✅ | ✅ green |
| 13-02-02 | 02 | 2 | SAF-03, TEST-02 | T-13-04 | Final report traces stale audit closure without editing historical artifacts. | inspection | `git diff -- .planning/phases/06-command-launch-escaping .planning/v1.0-MILESTONE-AUDIT.md .planning/REQUIREMENTS.md .planning/ROADMAP.md` | ✅ | ✅ green |

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. No new test framework, helper project, or Avalonia.Headless infrastructure is required.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Optional real xEdit/MO2 smoke coverage | SAF-03, TEST-02 | Phase 13 automation intentionally verifies AutoQAC command construction and process seams without requiring installed external tools. | Optional only: if the user has real tools configured, they may run one direct and one MO2 cleaning attempt with a difficult plugin name and compare behavior with the automated evidence. |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify commands.
- [x] Targeted command-builder, process-boundary, and cleaning diagnostics evidence recorded.
- [x] Full solution evidence recorded.
- [x] `13-VERIFICATION.md` traces `SAF-03` and `TEST-02` conclusions to this file.
- [x] Historical Phase 6 artifacts, milestone audit markers, `REQUIREMENTS.md`, and `ROADMAP.md` status markers remain unedited.
- [x] `nyquist_compliant: true` and `wave_0_complete: true` set in frontmatter after evidence is green or unrelated full-suite failures are documented.

**Approval:** approved — AC-01 through AC-08 are green, full-suite evidence passed, and existing infrastructure covered all Phase 13 requirements without source or test changes.

## Validation Audit 2026-05-01

| Metric | Count |
|--------|-------|
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |

Targeted validation rerun: `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~CleaningServiceTests"` passed with `AutoQAC.Tests.dll` Failed: 0, Passed: 39, Skipped: 0, Total: 39. Full-suite validation rerun: `dotnet test AutoQACSharp.slnx` passed with `QueryPlugins.Tests.dll` Failed: 0, Passed: 61, Skipped: 0, Total: 61 and `AutoQAC.Tests.dll` Failed: 0, Passed: 1016, Skipped: 0, Total: 1016.
