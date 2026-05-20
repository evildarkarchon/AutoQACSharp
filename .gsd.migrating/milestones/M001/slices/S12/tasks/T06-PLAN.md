# T06: 11-user-facing-diagnostics-boundaries 06

**Slice:** S12 — **Milestone:** M001

## Description

Add phase-level regression guards and run final verification.

Purpose: Requirement 6 in `11-SPEC.md` requires explicit tests that fail if covered UI, export, or log surfaces reintroduce stack/path/command leaks. Earlier plans add focused tests; this plan adds a small cross-surface sentinel net and performs final automated verification.
Output: Shared sentinel helper, phase-level boundary tests, and final solution test pass.

## Must-Haves

- [ ] "Covered user-facing surfaces fail tests if stack traces, raw exception messages, full local paths, or command fragments are reintroduced."
- [ ] "Covered log surfaces fail tests if full configured executable paths, raw argv payloads, or nested MO2 payloads are reintroduced."
- [ ] "Phase-level sentinel tests exercise real ViewModel, model, and logger paths instead of only formatter literals or source text."
- [ ] "Full solution tests pass after all Phase 11 diagnostic boundary changes."

## Files

- `AutoQAC.Tests/Helpers/DiagnosticSentinels.cs`
- `AutoQAC.Tests/ViewModels/Phase11DiagnosticsBoundaryTests.cs`
- `AutoQAC.Tests/Models/Phase11ReportBoundaryTests.cs`
- `AutoQAC.Tests/Services/Phase11LogBoundaryTests.cs`
