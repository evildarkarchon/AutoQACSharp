# T01: 13-command-launch-escaping-reverification-safe-mo2-failures 01

**Slice:** S14 — **Milestone:** M001

## Description

Collect and record the targeted Phase 13 evidence proving safe MO2 missing-configuration failure, direct/MO2 command escaping, process-boundary argument preservation, and launch-failure diagnostics.

Purpose: Close the stale Phase 6 `SAF-03`/`TEST-02` evidence gap with current targeted evidence before full-suite and final verification reporting.
Output: Updated `13-VALIDATION.md` rows AC-01 through AC-06 with command output summaries, statuses, and any minimal gap-fix notes.

## Must-Haves

- [ ] "D-01/D-02: 13-VALIDATION.md records acceptance-criterion evidence rows for SAF-03 and TEST-02."
- [ ] "D-05/D-06: Targeted command-builder, process-boundary, and cleaning failure diagnostics evidence is run and recorded."
- [ ] "D-08: Any targeted evidence failure is closed by the smallest code or test change before validation rows are marked green."

## Files

- `.planning/phases/13-command-launch-escaping-reverification-safe-mo2-failures/13-VALIDATION.md`
- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`
- `AutoQAC/Services/Cleaning/CleaningService.cs`
- `AutoQAC.Tests/Services/CleaningServiceTests.cs`
- `AutoQAC/Services/Process/ProcessExecutionService.cs`
- `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs`
