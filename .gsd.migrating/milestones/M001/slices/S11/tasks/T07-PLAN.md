# T07: 10-configuration-persistence-hardening 07

**Slice:** S11 — **Milestone:** M001

## Description

Close the facade state synchronization gap: ConfigurationService._hasPendingUserSave and _loadedUserConfigFromDisk are read/written from async methods without synchronization, and ReloadFromDiskAsync clears the pending-save flag unconditionally even when the reload fails or a save is pending.

Purpose: Ensure facade bookkeeping flags cannot diverge from coordinator state, completing the "one serialized flow" contract.
Output: Synchronized facade flags with conditional clearing on reload success, plus regression tests.

## Must-Haves

- [ ] "Facade pending-save state cannot diverge from coordinator state under concurrent async access"
- [ ] "ReloadFromDiskAsync only clears pending-save flag when the reload actually succeeds"
- [ ] "Maintainers can reason about persistence through one serialized flow without unsynchronized facade flags"

## Files

- `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
