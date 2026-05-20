# T04: 06-command-launch-escaping 04

**Slice:** S07 — **Milestone:** M001

## Description

Close the Phase 6 verification gaps without changing the successful ArgumentList launch contracts from Plans 06-01 through 06-03.

Purpose: Phase verification found two launch-safety gaps: MO2 mode can silently fall back to direct xEdit when the MO2 executable path is missing, and unexpected cleaning exceptions can expose raw path/command details to users. This plan makes both paths fail safely and verifies them with targeted regressions.

Output: One gap-closure implementation across command building and cleaning failure handling, plus regression tests for both verification gaps.

## Must-Haves

- [ ] "MO2 mode with missing/blank MO2 executable path fails before process start instead of launching direct xEdit. Covers D-05, D-08, D-09, D-10, D-11, D-12, and verification gap truth 6."
- [ ] "Unexpected launch-related cleaning exceptions return concise user-facing text without configured executable paths, full command lines, or raw exception details. Covers D-09, D-11, D-12, and verification gap truth 7."
- [ ] "Maintainer can verify the two Phase 6 gap closures with targeted XEditCommandBuilderTests and CleaningServiceTests. Covers TEST-02."

## Files

- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`
- `AutoQAC/Services/Cleaning/CleaningService.cs`
- `AutoQAC.Tests/Services/CleaningServiceTests.cs`
