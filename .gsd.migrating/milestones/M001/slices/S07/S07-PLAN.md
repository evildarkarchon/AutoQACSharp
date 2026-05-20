# S07: Command Launch Escaping

**Goal:** Replace fragile direct xEdit and MO2 command construction with explicit argv contracts in `XEditCommandBuilder`.
**Demo:** Replace fragile direct xEdit and MO2 command construction with explicit argv contracts in `XEditCommandBuilder`.

## Must-Haves


## Tasks

- [x] **T01: 06-command-launch-escaping 01** `est:23 min`
  - Replace fragile direct xEdit and MO2 command construction with explicit argv contracts in `XEditCommandBuilder`.

Purpose: Users must be able to clean plugins whose launch-bound paths and names contain quotes, Unicode, spaces, and shell-sensitive punctuation without AutoQAC corrupting the target plugin or MO2 wrapper arguments.
Output: Command-builder tests and implementation proving direct xEdit argv tokens plus the locked MO2 `run <xEdit> -a <payload>` contract.
- [x] **T02: 06-command-launch-escaping 02** `est:5 min`
  - Prove and implement preservation of `ProcessStartInfo.ArgumentList` through the actual process-start boundary.

Purpose: A command-builder fix is incomplete if `ProcessExecutionService` drops `ArgumentList` while cloning `ProcessStartInfo`; users need the actual launched process to receive the intended argv.
Output: UTF-8 JSON argv echo helper, integration tests, and process-layer clone changes.
- [x] **T03: 06-command-launch-escaping 03** `est:2 min`
  - Integrate launch-build failures into existing cleaning failure flow with concise, safe user-facing text and final phase verification.

Purpose: If AutoQAC cannot build a safe direct or MO2 launch command, it must fail before starting any process while avoiding full command-line exposure in the user-facing result.
Output: CleaningService failure-flow tests and implementation plus final targeted/full-suite verification.
- [x] **T04: 06-command-launch-escaping 04** `est:16min`
  - Close the Phase 6 verification gaps without changing the successful ArgumentList launch contracts from Plans 06-01 through 06-03.

Purpose: Phase verification found two launch-safety gaps: MO2 mode can silently fall back to direct xEdit when the MO2 executable path is missing, and unexpected cleaning exceptions can expose raw path/command details to users. This plan makes both paths fail safely and verifies them with targeted regressions.

Output: One gap-closure implementation across command building and cleaning failure handling, plus regression tests for both verification gaps.

## Files Likely Touched

- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs`
- `AutoQAC.Tests/TestProcessHelper/Program.cs`
- `AutoQAC/Services/Cleaning/CleaningService.cs`
- `AutoQAC.Tests/Services/CleaningServiceTests.cs`
- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`
- `AutoQAC/Services/Cleaning/CleaningService.cs`
- `AutoQAC.Tests/Services/CleaningServiceTests.cs`
