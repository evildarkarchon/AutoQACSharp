# AutoQAC Context

AutoQAC helps users run xEdit Quick Auto Clean safely against selected Bethesda plugins.

## Language

**Cleaning session**:
A single user-initiated attempt to clean selected plugins, one at a time, from readiness checks through completion or cancellation.
_Avoid_: Cleaning workflow, cleaning transaction, cleaning run

**Plugin refresh**:
A user-visible update of plugin rows for the current game context, including skip-list status and issue approximation preparation.

**Plugin refresh publication**:
The point at which a Plugin refresh becomes visible to the user and authoritative for a later Cleaning session, including the current game context, user-visible plugin rows, full clean/skip row facts, Issue approximation status, and whether the Plugin refresh discovery plan still matches Discovery-affecting settings.
_Avoid_: State update, app state mutation, row sync, cleaning-time rediscovery

**Plugin refresh discovery plan**:
The resolved plan for how a Plugin refresh will find plugin rows in the current game context, including whether discovery uses the game data folder, a load-order file, or an MO2 profile view. It is prepared before Plugin refresh publication and is not a Cleaning session.
_Avoid_: Discovery context, plugin loading plan, game capability plan

**Plugin selection**:
The user's current inclusion or exclusion choice among Plugin refresh rows available for a Cleaning session or selected Issue approximation refresh.
_Avoid_: Excluded plugin paths, row checkbox state

**Cleaning command readiness**:
A user-visible assessment of whether Start and Preview can be offered before a Cleaning session, based on the current Plugin refresh publication, Plugin selection, and cheap launch-readiness checks.
_Avoid_: Button enabled state, pre-clean validation, row count check

**Skip list**:
A game-specific set of plugin names that AutoQAC should not select for cleaning by default.

**Issue approximation**:
A pre-cleaning estimate of known plugin issues shown before a Cleaning session.

**Game capability**:
The set of AutoQAC behaviors available for a selected game, including how plugins can be discovered, whether a load-order file is required, and whether Issue approximation can be shown.
_Avoid_: Mutagen support, supported game

**Discovery-affecting settings**:
User choices that can change a Plugin refresh discovery plan or the published plugin rows, such as selected game, MO2 mode, load-order file, game data folder override, MO2 instance/profile, and Skip list settings.
_Avoid_: Configuration generation, dirty config, any setting change

**Discovery settings change**:
A user or app intent that updates one Discovery-affecting setting and results in either an accepted Plugin refresh publication or a typed rejection.
_Avoid_: Settings mutation, config save, refresh trigger
