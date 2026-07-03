# AutoQAC Context

AutoQAC helps users run xEdit Quick Auto Clean safely against selected Bethesda plugins.

## Language

**Cleaning session**:
A single user-initiated attempt to clean selected plugins, one at a time, from readiness checks through completion or cancellation.
_Avoid_: Cleaning workflow, cleaning transaction, cleaning run

**Plugin refresh**:
A user-visible update of plugin rows for the current game context, including skip-list status and issue approximation preparation.

**Plugin refresh publication**:
The point at which a Plugin refresh becomes visible to the user, including the current game context, plugin rows, and issue approximation status.
_Avoid_: State update, app state mutation, row sync

**Skip list**:
A game-specific set of plugin names that AutoQAC should not select for cleaning by default.

**Issue approximation**:
A pre-cleaning estimate of known plugin issues shown before a Cleaning session.

**Game capability**:
The set of AutoQAC behaviors available for a selected game, including how plugins can be discovered, whether a load-order file is required, and whether Issue approximation can be shown.
_Avoid_: Mutagen support, supported game
