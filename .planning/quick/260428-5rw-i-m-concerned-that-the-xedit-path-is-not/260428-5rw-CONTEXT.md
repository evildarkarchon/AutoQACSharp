# Quick Task 260428-5rw: I'm concerned that the xEdit path is not getting saved, can you check that out for me? - Context

**Gathered:** 2026-04-28
**Status:** Ready for planning

<domain>
## Task Boundary

Investigate whether the xEdit path configured in AutoQAC is failing to persist, identify the root cause, and fix it if confirmed.

</domain>

<decisions>
## Implementation Decisions

### Scope
- If a persistence bug is confirmed, implement the fix rather than stopping at diagnosis.

### Fix Breadth
- Prioritize hardening the relevant configuration flow, not just the narrowest symptom, while keeping the change appropriately scoped for a quick task.

### Agent's Discretion
- Use systematic debugging: reproduce through tests or existing code paths before changing production code.
- Preserve existing MVVM boundaries and avoid unrelated cleanup.

</decisions>

<specifics>
## Specific Ideas

- Focus on how xEdit path changes flow from UI/ViewModel state into configuration persistence and back through reload/startup behavior.

</specifics>

<canonical_refs>
## Canonical References

- `AGENTS.md`
- `.planning/STATE.md`
- `.planning/PROJECT.md`

</canonical_refs>
