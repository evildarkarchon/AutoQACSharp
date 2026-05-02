---
phase: 06
slug: command-launch-escaping
status: passed
nyquist_compliant: true
wave_0_complete: true
created: 2026-04-29
updated: 2026-05-02
---

# Phase 06 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with FluentAssertions 8.8.0 and NSubstitute 5.3.0 |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj`; solution `AutoQACSharp.slnx` |
| **Quick run command** | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~CleaningServiceTests"` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx` |
| **Estimated runtime** | ~60 seconds for targeted tests, full suite as phase gate |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests|FullyQualifiedName~CleaningServiceTests"`
- **After every plan wave:** Run `dotnet test AutoQACSharp.slnx`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** targeted test feedback after each task

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 06-01-01 | 01 | 1 | SAF-03, TEST-02 | T-06-01-01 / T-06-01-02 | Direct and MO2 argv are data-first and preserve difficult characters | unit | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~XEditCommandBuilderTests` | ✅ | ✅ green via Phase 13 evidence (`13-VALIDATION.md` AC-03 and AC-04) |
| 06-01-02 | 01 | 1 | SAF-03, TEST-02 | T-06-01-01 / T-06-01-02 | Production builder uses `ArgumentList` and avoids direct `Arguments` concatenation | unit | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~XEditCommandBuilderTests` | ✅ | ✅ green via Phase 13 evidence (`13-VALIDATION.md` AC-03 and AC-04) |
| 06-02-01 | 02 | 1 | SAF-03, TEST-02 | T-06-02-01 / T-06-02-02 | Helper echoes actual argv as UTF-8 JSON | integration | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~ProcessExecutionIntegrationTests` | ✅ | ✅ superseded by Phase 13 (`13-VALIDATION.md` AC-05) |
| 06-02-02 | 02 | 1 | SAF-03, TEST-02 | T-06-02-01 / T-06-02-02 | Process layer preserves `ArgumentList` through real process launch | integration | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~ProcessExecutionIntegrationTests` | ✅ | ✅ superseded by Phase 13 (`13-VALIDATION.md` AC-05) |
| 06-03-01 | 03 | 2 | SAF-03 | T-06-03-01 | Launch-build failures do not start a process and do not expose full command lines to users | unit | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~CleaningServiceTests` | ✅ | ✅ green via Phase 13 evidence (`13-VALIDATION.md` AC-01, AC-02, and AC-06) |
| 06-03-02 | 03 | 2 | SAF-03, TEST-02 | T-06-03-01 / — | Full phase regression suite covers command builder, process boundary, and failure flow | regression | `dotnet test AutoQACSharp.slnx` | ✅ | ✅ superseded by Phase 13 (`13-VALIDATION.md` AC-07 and AC-08) |

*Status legend: ✅ green · ✅ superseded by current evidence · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- Existing infrastructure covers all phase requirements; Wave 1 tasks extend existing tests before implementation.

---

## Manual-Only Verifications

All phase behaviors have automated verification. Real xEdit and MO2 installations are not required by Phase 6 context; MO2 verification asserts AutoQAC's final argv contract.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency bounded by targeted tests
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** passed by Phase 16 metadata reconciliation using current Phase 13 evidence. Historical Phase 06 context is preserved, but draft/pending metadata no longer represents the current source-of-truth status for `SAF-03` or `TEST-02`.

## Phase 16 Metadata Reconciliation

Phase 16 reconciles this validation metadata with the current source of truth from `.planning/phases/13-command-launch-escaping-reverification-safe-mo2-failures/13-VALIDATION.md` and `.planning/phases/13-command-launch-escaping-reverification-safe-mo2-failures/13-VERIFICATION.md`. Phase 13 is the current source of truth for `SAF-03` and `TEST-02`; this file keeps the historical Phase 06 validation structure while removing contradictory draft and pending metadata.

| Phase 06 Row | Current Phase 13 Mapping | Reconciled Meaning |
|--------------|--------------------------|--------------------|
| `06-01-01` | `13-VALIDATION.md` AC-03 and AC-04 | Direct xEdit difficult-character argv and configured MO2 wrapper/nested-payload coverage are green in current command-builder evidence. |
| `06-01-02` | `13-VALIDATION.md` AC-03 and AC-04 | Production command-builder `ArgumentList` construction remains verified for direct and configured MO2 command shapes. |
| `06-02-01` | `13-VALIDATION.md` AC-05 | The helper-process UTF-8 argv echo is superseded by current process-boundary preservation evidence. |
| `06-02-02` | `13-VALIDATION.md` AC-05 | Real process launch preserves `ArgumentList` and legacy `Arguments` fallback in current evidence. |
| `06-03-01` | `13-VALIDATION.md` AC-01, AC-02, and AC-06 | Missing MO2 configuration fails closed before process start, and launch-failure diagnostics remain safe. |
| `06-03-02` | `13-VALIDATION.md` AC-07 and AC-08 | Full-suite evidence passed, and Phase 13 documented the historical non-edit boundary for Phase 06 artifacts. |

`13-VERIFICATION.md` concludes that `SAF-03` and `TEST-02` are satisfied by Phase 13 current evidence. `06-VERIFICATION.md` remains historical `gaps_found` evidence per D-12; it is intentionally not edited by Phase 16 because current closure flows through this reconciled validation metadata, Phase 13 artifacts, and the milestone audit.
