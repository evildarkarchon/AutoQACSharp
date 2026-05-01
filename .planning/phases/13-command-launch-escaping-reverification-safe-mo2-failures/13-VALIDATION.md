---
phase: 13
slug: command-launch-escaping-reverification-safe-mo2-failures
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-01
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
| AC-01 | MO2 mode with null, empty, or whitespace MO2 executable path produces no command and cannot launch direct xEdit. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests"` | Pending Phase 13 execution. | ⬜ pending |
| AC-02 | The missing-MO2 failure path reports a failed no-process-started result and does not call `IProcessExecutionService.ExecuteAsync`. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/CleaningServiceTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~CleaningServiceTests"` | Pending Phase 13 execution. | ⬜ pending |
| AC-03 | Direct xEdit command-builder tests pass for quotes, Unicode, spaces, shell-sensitive punctuation, and combined worst-case plugin names. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests"` | Pending Phase 13 execution. | ⬜ pending |
| AC-04 | Configured MO2 command-builder tests pass for the four-argument wrapper contract and parsed nested xEdit payload. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests"` | Pending Phase 13 execution. | ⬜ pending |
| AC-05 | Process-boundary integration tests pass for `ArgumentList` preservation and legacy `Arguments` fallback. | TEST-02 | `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~ProcessExecutionIntegrationTests"` | Pending Phase 13 execution. | ⬜ pending |
| AC-06 | Launch-failure diagnostics tests pass for command-build failure, mocked launch-start failure, and unexpected launch exception non-disclosure. | SAF-03, TEST-02 | `AutoQAC.Tests/Services/CleaningServiceTests.cs` | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~CleaningServiceTests"` | Pending Phase 13 execution. | ⬜ pending |
| AC-07 | Fresh Phase 13 verification/validation artifacts mark `SAF-03` and `TEST-02` satisfied using current evidence. | SAF-03, TEST-02 | `13-VALIDATION.md`, `13-VERIFICATION.md` | File inspection plus `dotnet test AutoQACSharp.slnx` | Pending Phase 13 execution. | ⬜ pending |
| AC-08 | Phase 13 does not rewrite historical Phase 6 verification artifacts as part of closing the stale audit gap. | SAF-03, TEST-02 | Git diff / file inspection | `git diff -- .planning/phases/06-command-launch-escaping .planning/v1.0-MILESTONE-AUDIT.md .planning/REQUIREMENTS.md .planning/ROADMAP.md` | Pending Phase 13 execution. | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ unrelated failure documented*

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 13-01-01 | 01 | 1 | SAF-03, TEST-02 | T-13-01 / T-13-02 | MO2 missing configuration fails closed before process start. | unit | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests"` | ✅ | ⬜ pending |
| 13-01-02 | 01 | 1 | SAF-03, TEST-02 | T-13-02 / T-13-03 | Direct/MO2 argv preservation and safe launch diagnostics remain covered. | unit/integration | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~CleaningServiceTests"` | ✅ | ⬜ pending |
| 13-02-01 | 02 | 2 | SAF-03, TEST-02 | T-13-01 / T-13-02 / T-13-03 | Full-suite evidence supports the targeted conclusions or unrelated failures are classified. | full suite | `dotnet test AutoQACSharp.slnx` | ✅ | ⬜ pending |
| 13-02-02 | 02 | 2 | SAF-03, TEST-02 | T-13-04 | Final report traces stale audit closure without editing historical artifacts. | inspection | `git diff -- .planning/phases/06-command-launch-escaping .planning/v1.0-MILESTONE-AUDIT.md .planning/REQUIREMENTS.md .planning/ROADMAP.md` | ✅ | ⬜ pending |

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

- [ ] All tasks have `<automated>` verify commands.
- [ ] Targeted command-builder, process-boundary, and cleaning diagnostics evidence recorded.
- [ ] Full solution evidence recorded.
- [ ] `13-VERIFICATION.md` traces `SAF-03` and `TEST-02` conclusions to this file.
- [ ] Historical Phase 6 artifacts, milestone audit markers, `REQUIREMENTS.md`, and `ROADMAP.md` status markers remain unedited.
- [ ] `nyquist_compliant: true` and `wave_0_complete: true` set in frontmatter after evidence is green or unrelated full-suite failures are documented.

**Approval:** pending
