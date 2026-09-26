# Deepen Issue approximation around Plugin refresh row identity

Accepted: replace the current Issue approximation service seam with a deep `PluginIssueApproximationModule` interface that receives a source plus ordered, unique, fully resolved `PluginRefreshRowKey` targets, builds the complete dependency context including unselected and Skip list rows, analyzes only targets sequentially, and streams each result with its unchanged target identity.

Selected Issue approximation refreshes must use the accepted Plugin refresh publication's discovery plan and full rows; a stale publication requires a new Plugin refresh, and an MO2 resolved source contains exactly one conflict-winning row per plugin filename rather than rebuilding or correlating a new source beneath old row identities.

Per-target failures produce `Unavailable`, completed results survive later failure or cancellation, cancellation removes every inactive `Pending` state by restoring a prior estimate or using `Unavailable`, and public analysis-context test hooks are removed; changes to `QueryPlugins` detection, supported-game rules, general AppState compatibility, Cleaning session behavior, and xEdit execution remain outside this decision.
