# T10: 08-cleaning-orchestrator-decomposition 10

**Slice:** S09 — **Milestone:** M001

## Description

Close Phase 8 verification gap 2: preflight must revalidate file-load-order requirements for the final detected game before xEdit launch.

Purpose: preserve D-13/D-15 preflight safety and REF-01 after moving validation into `CleaningPreflight`.
Output: post-detection load-order validation plus focused collaborator tests.

## Must-Haves

- [ ] "Preflight validates LoadOrderPath after Unknown game detection resolves to Fallout3, FalloutNewVegas, or Oblivion."
- [ ] "Detected file-load-order games cannot proceed to plugin-row construction when LoadOrderPath is null, empty, or points to a missing file."
- [ ] "Mutagen-supported detected games still do not require LoadOrderPath."

## Files

- `AutoQAC/Services/Cleaning/CleaningPreflight.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`
