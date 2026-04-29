---
phase: 06
slug: command-launch-escaping
status: draft
nyquist_compliant: true
wave_0_complete: false
created: 2026-04-29
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
| 06-01-01 | 01 | 1 | SAF-03, TEST-02 | T-06-01-01 / T-06-01-02 | Direct and MO2 argv are data-first and preserve difficult characters | unit | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~XEditCommandBuilderTests` | ✅ | ⬜ pending |
| 06-01-02 | 01 | 1 | SAF-03, TEST-02 | T-06-01-01 / T-06-01-02 | Production builder uses `ArgumentList` and avoids direct `Arguments` concatenation | unit | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~XEditCommandBuilderTests` | ✅ | ⬜ pending |
| 06-02-01 | 02 | 1 | SAF-03, TEST-02 | T-06-02-01 / T-06-02-02 | Helper echoes actual argv as UTF-8 JSON | integration | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~ProcessExecutionIntegrationTests` | ✅ | ⬜ pending |
| 06-02-02 | 02 | 1 | SAF-03, TEST-02 | T-06-02-01 / T-06-02-02 | Process layer preserves `ArgumentList` through real process launch | integration | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~ProcessExecutionIntegrationTests` | ✅ | ⬜ pending |
| 06-03-01 | 03 | 2 | SAF-03 | T-06-03-01 | Launch-build failures do not start a process and do not expose full command lines to users | unit | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~CleaningServiceTests` | ✅ | ⬜ pending |
| 06-03-02 | 03 | 2 | SAF-03, TEST-02 | T-06-03-01 / — | Full phase regression suite covers command builder, process boundary, and failure flow | regression | `dotnet test AutoQACSharp.slnx` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

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

**Approval:** pending execution
