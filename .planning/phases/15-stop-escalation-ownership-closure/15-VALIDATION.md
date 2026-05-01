---
phase: 15
slug: stop-escalation-ownership-closure
status: draft
nyquist_compliant: false
wave_0_complete: true
created: 2026-05-01
---

# Phase 15 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3, FluentAssertions 8.8.0, NSubstitute 5.3.0 |
| **Config file** | `AutoQAC.Tests/AutoQAC.Tests.csproj` |
| **Quick run command** | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningTerminationCoordinatorTests --nologo` |
| **Full suite command** | `dotnet test AutoQACSharp.slnx --nologo` |
| **Estimated runtime** | Existing project test runtime |

---

## Sampling Rate

- **After every task commit:** Run the task-specific targeted command from the plan.
- **After every plan wave:** Run `dotnet test AutoQACSharp.slnx --nologo`.
- **Before `/gsd-verify-work`:** Full suite must be green, or unrelated pre-existing failures must be documented in `15-VERIFICATION.md`.
- **Max feedback latency:** Targeted commands should run before full-suite evidence.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 15-01-01 | 01 | 1 | SAF-01, TEST-01 | — | No force kill before confirmation; retained target only after GracePeriodExpired | unit/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningTerminationCoordinatorTests --nologo` | ✅ | ⬜ pending |
| 15-01-02 | 01 | 1 | SAF-01, SAF-02, TEST-01 | — | Confirmed force target returns terminal ForceKilled/AlreadyExited/ForceKillFailed | unit/integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningTerminationCoordinatorTests --nologo` | ✅ | ⬜ pending |
| 15-02-01 | 02 | 2 | SAF-01, TEST-01 | — | Orchestrator finalization preserves unresolved pending target until user resolution | service | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningOrchestratorTests --nologo` | ✅ | ⬜ pending |
| 15-02-02 | 02 | 2 | SAF-01, SAF-02, TEST-01 | — | Progress Stop prompts before force and shows shared failure warning on ForceKillFailed | viewmodel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~ProgressViewModelTests --nologo` | ✅ | ⬜ pending |
| 15-03-01 | 03 | 3 | SAF-01, SAF-02, TEST-01 | — | Verification maps requirements and audit gaps to current evidence | artifact | `dotnet test AutoQACSharp.slnx --nologo` | ❌ W3 | ⬜ pending |

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements: xUnit test projects, helper-process executable, Progress ViewModel tests, coordinator tests, and full solution test command already exist.

---

## Manual-Only Verifications

All Phase 15 behaviors have automated verification through coordinator, process-helper, orchestrator, and Progress ViewModel tests. No Avalonia.Headless or manual UI gate is required by this phase.

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency is bounded by targeted commands before full-suite evidence
- [ ] `nyquist_compliant: true` set in frontmatter after execution evidence passes

**Approval:** pending
