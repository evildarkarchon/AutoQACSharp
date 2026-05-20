# T01: 10-configuration-persistence-hardening 01

**Slice:** S11 — **Milestone:** M001

## Description

Replace the YAML round-trip clone in `ConfigurationService.CloneConfig` with public, model-owned `Copy()` methods on `UserConfiguration` and every nested config class. This plan delivers requirement PERF-03 by proving (a) deep copy independence holds for all mutable containers including the `Dictionary<string, List<string>> SkipLists` field flagged in research as a common pitfall, and (b) the new `Copy()` is BEHAVIORALLY equivalent to the existing YAML clone (round-trip parity).

Purpose: PERF-03 closes the YAML clone overhead concern from `.planning/codebase/CONCERNS.md` and unblocks Plan 02's coordinator (which calls `UserConfiguration.Copy()` in the consumer loop). Wave 1 — independent of Plan 02 at the model layer; Plan 02 declares `depends_on: [01]` so Wave 1 still parallelizes inside the wave (Plan 02 RED step can be authored alongside Plan 01) but Wave 2 (Plan 03) does not start until BOTH 01 and 02 commit (already enforced by 03's `depends_on: [01, 02]`).

Output: New `Copy()` methods on `UserConfiguration`, `LoadOrderConfig`, `ModOrganizerConfig`, `XEditConfig`, `AutoQacSettings`, `BackupSettings`, `RetentionSettings`, plus a new test file `UserConfigurationCopyTests.cs` proving deep-copy independence, null normalization, and behavior parity with the existing YAML clone.

### Review Feedback Addressed
- **Codex MEDIUM (10-01)** "Source-regex tests around `Copy()` bodies are brittle; behavior tests are more valuable." — Removed source-regex YAML-free guard (case 12 in original plan). Replaced with a **behavior parity test** that round-trips a populated `UserConfiguration` through YAML serialize/deserialize and asserts `Copy()` produces a value-equal graph. The "no YAML in the clone path" guarantee is now enforced by Plan 03 deleting `_serializer.Serialize` from `ConfigurationService` (the actual clone caller) — that is where it matters.
- **OpenCode MEDIUM (10-01)** "Static YAML-free guard fragility" (path-resolution in CI) — Same fix; the brittle source-walking helper is gone.
- **Codex MEDIUM (10-01)** "Reword success as 'copy primitive delivered; clone-path replacement completes in 10-03.'" — Updated objective and success criteria to reflect that Plan 01 ships the primitive and Plan 03 closes PERF-03.
- **OpenCode LOW (10-01)** "Wave-1 parallel dependency risk" — Resolved: Plan 02 frontmatter now declares `depends_on: [01]` (see 10-02-PLAN.md). Plan 02 RED authoring can still proceed alongside Plan 01; Plan 02 GREEN waits for Plan 01 commit.

## Must-Haves

- [ ] "UserConfiguration.Copy() returns an instance whose mutable containers are independent of the source (mutating the copy never mutates the source, and vice versa)."
- [ ] "UserConfiguration.Copy() does not call YamlDotNet ISerializer/IDeserializer in the normal in-memory clone path."
- [ ] "D-40: YAML-based clone is replaced with manual deep-copy behavior; no clone library or immutable model refactor is introduced."
- [ ] "D-41: Copy behavior lives near the models as public methods on UserConfiguration and nested config models."
- [ ] "D-42: Clone maintenance is protected by behavior tests rather than reflection/source mapping guards for every property."
- [ ] "D-46: PERF-03 is proven by removing YAML round-trip from the normal clone path; no benchmark project or timing comparison is added."
- [ ] "Copy() normalizes null nested config objects (LoadOrder, ModOrganizer, XEdit, Settings, LogRetention, Backup) and null collections (LoadOrderFileOverrides, SkipLists, GameDataFolderOverrides) to default empty instances (D-44)."
- [ ] "Copy() deep-copies SkipLists (Dictionary<string, List<string>>): mutating a list inside the copy never mutates the source's list (D-45)."
- [ ] "Copy() is BEHAVIORALLY equivalent to the existing YAML round-trip clone for every property except YAML alias casing — ConfigurationService callers Plan 03 swaps to Copy() get the same observable independence properties."

## Files

- `AutoQAC/Models/Configuration/UserConfiguration.cs`
- `AutoQAC/Models/Configuration/BackupSettings.cs`
- `AutoQAC/Models/Configuration/RetentionSettings.cs`
- `AutoQAC.Tests/Models/UserConfigurationCopyTests.cs`
