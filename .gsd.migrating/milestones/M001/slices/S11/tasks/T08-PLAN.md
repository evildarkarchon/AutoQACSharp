# T08: 10-configuration-persistence-hardening 08

**Slice:** S11 — **Milestone:** M001

## Description

Close the remaining Phase 10 verification blocker: explicit reload currently flushes a pending app save first, but if that prerequisite flush fails it still reads disk and can return Success using old persisted content.

Purpose: Preserve app-save protected reload semantics under failure so queued user edits cannot be dropped while the reload caller receives a successful result.
Output: A failing regression test first, then coordinator logic that returns the failed/rejected flush result immediately without reading or applying disk content.

## Must-Haves

- [ ] "Explicit reload cannot mask a failed pending app-save flush as a successful disk reload"
- [ ] "Failed prerequisite flush results stop reload processing before old disk content is read or applied"
- [ ] "Maintainers can verify the pending-save write-failure reload race deterministically"

## Files

- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
