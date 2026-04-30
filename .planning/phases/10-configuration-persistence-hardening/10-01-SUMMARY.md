---
phase: 10-configuration-persistence-hardening
plan: 01
subsystem: configuration
tags: [csharp, yaml, configuration, tdd, performance]

requires: []
provides:
  - Model-owned UserConfiguration.Copy() deep-copy primitive
  - Nested configuration Copy() methods for load order, MO2, xEdit, settings, retention, and backup models
  - Behavior tests proving deep-copy independence, null normalization, YAML alias preservation, and YAML round-trip parity
affects: [configuration-persistence-hardening, PERF-03, configuration-service-clone-replacement]

tech-stack:
  added: []
  patterns:
    - Public model-owned Copy() methods for YAML-backed mutable configuration graphs
    - Behavior-first TDD coverage for clone parity instead of brittle source-regex guards

key-files:
  created:
    - AutoQAC.Tests/Models/UserConfigurationCopyTests.cs
  modified:
    - AutoQAC/Models/Configuration/UserConfiguration.cs
    - AutoQAC/Models/Configuration/BackupSettings.cs
    - AutoQAC/Models/Configuration/RetentionSettings.cs

key-decisions:
  - "Phase 10 Plan 01 implements model-owned manual Copy() methods and leaves ConfigurationService.CloneConfig unchanged for Plan 03."
  - "Behavior parity with the existing YAML round-trip clone is proven through xUnit tests instead of source-regex clone guards."
  - "AutoQAC.csproj does not enable implicit usings, so UserConfiguration.cs explicitly imports System and System.Linq for StringComparer and LINQ copy helpers."

patterns-established:
  - "Copy methods normalize null nested config objects and collections to safe defaults before copying."
  - "SkipLists are deep-copied at both dictionary and nested List<string> levels."

requirements-completed: [PERF-03]

duration: 3 min
completed: 2026-04-30
---

# Phase 10 Plan 01: Manual Configuration Copy Primitive Summary

**Model-owned UserConfiguration.Copy() deep-copy primitive with 14 behavior tests proving parity with the prior YAML round-trip clone.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-30T22:47:04Z
- **Completed:** 2026-04-30T22:50:19Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added 14 xUnit/FluentAssertions behavior tests covering scalar preservation, each nested model, all mutable dictionaries, SkipLists nested lists, null normalization, YAML alias preservation, idempotence, and YAML round-trip parity.
- Added documented public `Copy()` methods to `UserConfiguration`, `LoadOrderConfig`, `ModOrganizerConfig`, `XEditConfig`, `AutoQacSettings`, `BackupSettings`, and `RetentionSettings`.
- Preserved YAML aliases and left `ConfigurationService.CloneConfig` untouched for Plan 03, which will swap the caller and remove the YAML clone path.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — failing UserConfigurationCopy tests** - `5dc561e` (test)
2. **Task 2: GREEN — add Copy() methods to model graph** - `140f1ef` (feat)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC.Tests/Models/UserConfigurationCopyTests.cs` - New behavior-first TDD coverage for manual copy semantics and YAML parity.
- `AutoQAC/Models/Configuration/UserConfiguration.cs` - Added parent and nested config `Copy()` methods with null normalization and deep-copy logic.
- `AutoQAC/Models/Configuration/BackupSettings.cs` - Added documented `BackupSettings.Copy()`.
- `AutoQAC/Models/Configuration/RetentionSettings.cs` - Added documented `RetentionSettings.Copy()`.

## Verification

- `dotnet build AutoQAC.Tests/AutoQAC.Tests.csproj --nologo` failed in RED with `CS1061` missing `Copy()` methods.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~UserConfigurationCopyTests --nologo` passed: 14/14.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration --nologo` passed: 81/81.
- `dotnet build AutoQACSharp.slnx --nologo` passed.

## Decisions Made

- Implemented only the copy primitive in this plan; `ConfigurationService.CloneConfig` remains YAML-based until Plan 03 performs the caller swap and clone-path deletion.
- Kept the review-requested behavior parity test as the guarantee surface and deliberately did not add the brittle `Copy_DoesNotInstantiateYamlSerializer_StaticSourceGuard` source-regex test.
- Added explicit `System`/`System.Linq` imports because the production `AutoQAC.csproj` does not currently enable implicit usings, despite the plan context expecting it.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added explicit System/System.Linq imports**
- **Found during:** Task 2 (GREEN — add Copy() methods to model graph)
- **Issue:** The plan stated implicit usings were enabled in `AutoQAC.csproj`, but the production project file does not contain `<ImplicitUsings>enable</ImplicitUsings>`. `StringComparer`, `.ToDictionary()`, and `.ToList()` therefore did not compile without explicit imports.
- **Fix:** Added `using System;` and `using System.Linq;` to `UserConfiguration.cs` while preserving all existing YAML aliases and model members.
- **Files modified:** `AutoQAC/Models/Configuration/UserConfiguration.cs`
- **Verification:** `dotnet build AutoQACSharp.slnx --nologo` and configuration tests passed.
- **Committed in:** `140f1ef`

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** The deviation was necessary for compilation and does not change runtime behavior or phase scope.

## Issues Encountered

None.

## Known Stubs

None.

## TDD Gate Compliance

- **RED:** `5dc561e` — failing tests committed before production copy methods existed.
- **GREEN:** `140f1ef` — implementation committed after `UserConfigurationCopyTests` passed.
- **REFACTOR:** Not needed; Copy() bodies are small and idiomatic.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for Plan 10-02/10-03 consumers to call `UserConfiguration.Copy()`.
- Plan 03 should replace `ConfigurationService.CloneConfig` callers with the new Copy() primitive, then remove YAML serialize/deserialize calls from normal clone paths to close the remaining PERF-03 milestone.

## Self-Check: PASSED

- Found all four created/modified files on disk.
- Found task commits `5dc561e` and `140f1ef` in git history.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-04-30*
