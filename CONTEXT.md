# AutoQAC Context

AutoQAC helps users run xEdit Quick Auto Clean safely against selected Bethesda plugins.

## Language

**Cleaning session**:
A single user-initiated attempt to clean selected plugins, one at a time, from readiness checks through completion or cancellation.
_Avoid_: Cleaning workflow, cleaning transaction, cleaning run

**Plugin cleaning**:
The per-plugin cleaning attempt within a Cleaning session, including that plugin's xEdit Quick Auto Clean attempt and final cleaned, skipped, or failed outcome. It does not include Cleaning session backup policy or session metadata.
_Avoid_: Runner refactor, single-plugin workflow

**Plugin refresh**:
A user-visible update of plugin rows for the current game context, including skip-list status and issue approximation preparation.

**Plugin refresh publication**:
The point at which a Plugin refresh becomes visible to the user and authoritative for a later Cleaning session, including the current game context, user-visible plugin rows, full clean/skip row facts, Issue approximation status, and whether the Plugin refresh discovery plan still matches Discovery-affecting settings.
A selected Issue approximation refresh uses this publication's accepted discovery plan and rows; a stale publication requires a new Plugin refresh.
_Avoid_: State update, app state mutation, row sync, cleaning-time rediscovery

**Plugin refresh publication rows**:
The full row facts owned by a Plugin refresh publication, including hidden Skip list rows, Plugin selection, Issue approximation state, row identity, and the visible row projection shown to the user.
_Avoid_: UI rows, state rows, plugin row store

**Plugin refresh row identity**:
The stable identity of one effective plugin file in a Plugin refresh publication, including its full path when known. In MO2 it identifies the conflict-winning file, not every physical file with the same plugin name, and remains unchanged through Issue approximation.
_Avoid_: Filename match, result correlation key

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
A pre-cleaning estimate of known plugin issues shown before a Cleaning session. It uses the full source load order as dependency context even when only some publication rows are targets.

**Issue approximation target**:
A fully resolved Plugin refresh publication row chosen to receive a new Issue approximation. Its full path is required; the target set is authoritative, only its rows may receive results, and each target receives one Available or Unavailable estimate unless the refresh is canceled.
_Avoid_: Analyzed plugin, approximation candidate

**Pending Issue approximation**:
An Issue approximation target whose current estimate is actively being calculated. Pending ends when the target receives a result, the refresh fails, or cancellation restores or replaces its prior estimate.
_Avoid_: Not analyzed, unknown estimate

**Game capability**:
The set of AutoQAC behaviors available for a selected game, including how plugins can be discovered, whether a load-order file is required, and whether Issue approximation can be shown.
_Avoid_: Mutagen support, supported game

**Discovery-affecting settings**:
User choices that can change a Plugin refresh discovery plan or the published plugin rows, such as selected game, MO2 mode, load-order file, game data folder override, MO2 instance/profile, and Skip list settings.
_Avoid_: Configuration generation, dirty config, any setting change

**Discovery settings change**:
A user or app intent that updates one Discovery-affecting setting and results in either an accepted Plugin refresh publication or a typed rejection.
_Avoid_: Settings mutation, config save, refresh trigger
