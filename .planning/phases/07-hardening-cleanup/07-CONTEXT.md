# Phase 7: Hardening & Cleanup - Context

**Gathered:** 2026-02-07
**Status:** Ready for planning

<domain>
## Phase Boundary

Comprehensive test coverage for features built in phases 1-6, dependency updates to latest stable versions, coverage tooling integration, and removal of the Code_To_Port/ reference directory. No new features.

</domain>

<decisions>
## Implementation Decisions

### Test coverage priorities
- Claude analyzes existing test gaps and prioritizes based on both risk and coverage density
- Fill gaps in both happy-path and failure/edge-case paths equally
- Unit tests only (with mocks/stubs) -- no integration tests with real file I/O or process spawning
- 80% is a reasonable target on critical paths, not a hard floor -- some classes (UI wiring, etc.) can fall below if hard to test

### Dependency updates
- Update ALL NuGet packages to latest stable versions (not just Mutagen and YamlDotNet)
- If a dependency update introduces breaking API changes, fix the code immediately to use the new API -- don't leave deprecated patterns
- Bump target framework to latest stable .NET if available (e.g., .NET 10) -- verify Avalonia/ReactiveUI support first

### Reference code removal
- Run a feature parity audit of Code_To_Port/ against implemented C# features before deletion
- Claude decides how to handle gaps: critical ones get ported, minor ones get documented as future work
- Nothing to preserve from Code_To_Port/ -- git history is sufficient
- Full CLAUDE.md cleanup: remove all porting guidelines, translation tables, and Code_To_Port/ references after deletion

### Coverage tooling
- Coverlet for coverage collection -- integrates with dotnet test
- Track both line and branch coverage; 80% line coverage is the primary target
- Coverage integrated into `dotnet test` via MSBuild properties (automatic every test run)
- Output formats: Cobertura XML (for tooling) + HTML via ReportGenerator (for human review)

### Claude's Discretion
- Which specific classes/methods to prioritize for test coverage based on gap analysis
- How to handle minor unported features found during parity audit
- Coverlet configuration details and MSBuild property setup
- Test file organization and naming conventions for new tests

</decisions>

<specifics>
## Specific Ideas

No specific requirements -- open to standard approaches

</specifics>

<deferred>
## Deferred Ideas

None -- discussion stayed within phase scope

</deferred>

---

*Phase: 07-hardening-cleanup*
*Context gathered: 2026-02-07*
