# T09: 10-configuration-persistence-hardening 09

**Slice:** S11 — **Milestone:** M001

## Description

Close the remaining Phase 10 verification blocker: `ConfigurationService.FlushPendingSavesAsync` currently returns a local facade-level NoOp when `_hasPendingUserSave` is false, bypassing the coordinator queue barrier required by D-02 and D-05.

Purpose: Make forced flush a reliable serialized barrier across both app saves and queued watcher reloads so cleaning/preflight callers cannot continue while external settings-file work remains queued behind the facade.
Output: A failing regression test first, then `ConfigurationService` flush logic that always awaits the coordinator barrier and only uses facade state to decide whether to clear `_hasPendingUserSave` after accepted Success/NoOp results.

## Must-Haves

- [ ] "Forced configuration flush is always a coordinator queue barrier, even when the facade has no pending app save"
- [ ] "Queued watcher reload work cannot remain behind a pre-cleaning/no-pending facade flush"
- [ ] "Maintainers can verify the no-pending facade flush barrier regression deterministically"

## Files

- `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
