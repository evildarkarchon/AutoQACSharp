# Plugin Refresh Deepening Implementation Plan

This document is the handoff for a fresh agent implementing the Plugin refresh deepening refactor.

Authoritative context:

- `CONTEXT.md`: canonical domain terms are **Cleaning session**, **Plugin refresh**, **Skip list**, and **Issue approximation**.
- `docs/adr/0001-replace-cleaning-orchestrator-seam.md`: Cleaning session replacement is accepted; do not re-litigate that seam.
- `AGENTS.md`: sequential cleaning is a hard runtime requirement; do not parallelize plugin cleaning or xEdit launches.
- `docs/cleaning-session-implementation-plan.md`: current Cleaning session refactor context. This Plugin refresh slice is adjacent to it, not a replacement for it.

## Goal

Deepen the Plugin refresh module so callers no longer assemble game data folders, MO2 instance/profile facts, load-order paths, Skip list flags, state updates, and Issue approximation context before a refresh can run.

The target module is **deep**: callers cross a small Plugin refresh interface, while the implementation owns refresh context assembly, row publication, Skip list status, Issue approximation preparation, generation cancellation, stale-result filtering, and refresh status publication.

## Design Decisions

- Move the Plugin refresh seam earlier than the current `PluginRefreshRequest` call site.
- The Plugin refresh implementation owns MO2 instance/profile resolution and load-order path resolution.
- The Plugin refresh implementation owns publication of plugin rows, Issue approximation updates, and refresh status into application state.
- The first implementation slice includes shared locality for Skip list policy used by both Plugin refresh and Cleaning session preflight.
- Keep two-phase Plugin refresh behavior: visible plugin rows publish first, then Issue approximation continues inside the same module.
- Preserve generation-based cancellation and stale-result filtering: newer Plugin refresh work supersedes older row and Issue approximation results.
- `ConfigurationViewModel` keeps dialogs and direct UI display projection only. It must not compute refresh context.
- New tests should primarily exercise Plugin refresh through its seam with fake adapters. ViewModel tests should verify UI triggering and display projection only.
- Replace the current shallow Plugin refresh seam in one vertical slice. Do not add a compatibility facade that preserves the old caller burden.
- Plugin refresh reads the active in-memory configuration and must not force `FlushPendingSavesAsync`. The disk flush remains a Cleaning session safety rule before xEdit launches.

## Non-Goals

Do not include these in the same implementation slice:

- Do not redesign the QueryPlugins analysis seam.
- Do not redesign one-plugin Cleaning session attempt sequencing.
- Do not redesign Cleaning session progress projection.
- Do not redesign the whole configuration module.
- Do not add a general game capability registry unless it falls out as the smallest correct way to remove duplicated refresh/preflight checks.
- Do not change xEdit launch behavior.
- Do not parallelize plugin cleaning or xEdit launches.
- Do not modify `Mutagen/`.

## Current Friction

`ConfigurationViewModel.RefreshPluginsForGameAsync` and `RefreshMo2PluginsForGameAsync` currently perform the first half of Plugin refresh:

- Resolve per-game data folder overrides.
- Resolve direct-mode load-order path for file-based games.
- Resolve MO2 instance override.
- Resolve MO2 profiles and selected profile.
- Resolve MO2 load-order path.
- Build MO2 plugin path map.
- Publish several path/profile facts into `IStateService`.
- Construct `PluginRefreshRequest` with `DataFolderPath`, `LoadOrderPath`, `Mo2Mode`, `Mo2LoadOrderPath`, `Mo2PathMap`, `Mo2BaseDataFolder`, and `DisableSkipLists`.

`PluginRefreshCoordinator.RefreshForGameAsync` then performs the second half:

- Owns refresh generation cancellation.
- Loads plugin rows.
- Detects TTW/Enderal variant.
- Loads and applies Skip list status.
- Publishes rows.
- Starts Issue approximation if supported.
- Merges Issue approximation callbacks into state.
- Publishes terminal refresh status.

This is a shallow seam. The current `PluginRefreshRequest` interface is nearly a checklist of implementation facts the caller has to know.

## Target Module Shape

The exact C# names should be chosen during implementation, but the external Plugin refresh interface must satisfy these rules:

- Callers must not pass `DataFolderPath` as a precomputed refresh fact.
- Callers must not pass `Mo2LoadOrderPath`.
- Callers must not pass `Mo2PathMap`.
- Callers must not pass `Mo2BaseDataFolder`.
- Callers must not decide whether the current game requires a load-order file.
- Callers must not decide whether Issue approximation is available.
- Callers must not apply Skip list status.
- Callers must not publish plugin rows directly as part of Plugin refresh.
- Callers may pass user intent that only the UI can know, such as the selected game, a load-order file chosen from a dialog, or a manual cancellation intent.
- Callers may subscribe to refresh status or receive a small display projection for UI-only fields that are not in `AppState`.

Keeping the existing `IPluginRefreshCoordinator` name is acceptable if the interface becomes deep. Keeping the existing public `PluginRefreshRequest` shape is not acceptable because it preserves the shallow seam.

## Suggested New Internal Modules

Use the smallest set of modules that passes the deletion test. These names are suggestions, not mandatory.

- `PluginRefreshCoordinator`: the deep Plugin refresh module. Owns refresh generation, context assembly, row publication, Issue approximation kickoff, cancellation, and status publication.
- `PluginRefreshContext`: an internal immutable snapshot assembled by the implementation. May include game type, game data folder, direct/MO2 mode facts, selected profile, load-order path, path map, and the active user configuration values needed for refresh.
- `SkipListPolicy`: shared policy used by Plugin refresh and Cleaning session preflight. Owns variant-aware Skip list lookup and application decisions.
- `PluginRefreshStatePublisher`: optional adapter if direct `IStateService` use inside the coordinator keeps growing. Add it only if it increases locality; do not add a pass-through adapter for one caller.

Adapter rule:

- One adapter means a hypothetical seam; two adapters means a real seam.
- Use adapters where production and tests genuinely vary, such as file-system/MO2/configuration/state dependencies.
- Do not wrap every method just to make mocks easier.

## Skip List Policy

Create one locality point for Skip list decisions.

Responsibilities:

- Detect game variant from game type and plugin names, or call the existing game detection implementation to do so.
- Load the merged Skip list through existing configuration behavior.
- Respect `DisableSkipLists`.
- Produce a reusable decision for each plugin name.
- Allow Plugin refresh to mark visible rows with `IsInSkipList`.
- Allow Cleaning session preflight to skip the same plugins for the same game context.

Rules:

- Plugin refresh and Cleaning session preflight must not separately reimplement variant detection plus Skip list application.
- File validation stays in Cleaning session preflight. The shared Skip list policy is not responsible for on-disk plugin validation.
- Backup policy stays in Cleaning session preflight and Cleaning session implementation.
- `ConfigurationService.GetSkipListAsync` may remain the merge implementation; the new policy owns how that merged list affects plugin decisions.

## Plugin Refresh Behavior

### Direct Mode

For direct mode, Plugin refresh should own:

- Reading active configuration through `LoadUserConfigAsync` without flushing pending saves.
- Resolving data folder override and effective game data folder.
- Resolving load-order override/default path for games that require file-based load-order loading.
- Choosing Mutagen-backed plugin discovery for supported games without a load-order file.
- Publishing path facts needed by the rest of the app.
- Loading plugin rows.
- Applying Skip list status through the shared policy.
- Publishing rows before Issue approximation completes.
- Starting Issue approximation for supported games.
- Merging approximation results only when they belong to the active generation.

### MO2 Mode

For MO2 mode, Plugin refresh should own:

- Reading active configuration through `LoadUserConfigAsync` without flushing pending saves.
- Resolving MO2 executable path from configuration/state.
- Resolving per-game MO2 instance override.
- Resolving the active MO2 instance.
- Loading available profiles.
- Choosing the persisted profile or the best available profile through existing `IMo2InstanceService` behavior.
- Resolving the profile load-order path.
- Building the plugin path map for the selected profile.
- Publishing state needed by the rest of the app.
- Publishing a UI projection for instance/profile display if those fields remain outside `AppState`.
- Loading plugin rows from the MO2 load-order file.
- Applying Skip list status through the shared policy.
- Starting Issue approximation with resolved paths.

MO2 failure cases should remain user-visible:

- No MO2 instance found.
- No profiles with `loadorder.txt` found.
- Selected profile is missing.
- Profile load-order file is missing.

Failure handling should clear or preserve plugin rows intentionally. Do not leave stale rows after a game/profile/mode change that cannot resolve a valid Plugin refresh context.

### Issue Approximation

Preserve the current two-phase behavior:

1. Publish plugin rows with `PluginIssueApproximation.Pending` when approximation is supported.
2. Publish plugin rows with `PluginIssueApproximation.Unavailable` when approximation is unsupported.
3. Start approximation after rows are visible.
4. Merge each callback into state only if the refresh generation is still current.
5. Publish a terminal status when the active generation completes, fails, is unsupported, or is manually canceled.

Do not move QueryPlugins Mutagen preparation in this slice. `PluginIssueApproximationService` can remain the adapter for this implementation plan.

## `ConfigurationViewModel` Migration

After this refactor, `ConfigurationViewModel` should not own Plugin refresh branching.

Keep in the ViewModel:

- File and folder picker calls.
- Dialog interaction.
- Relay commands.
- Direct UI display projection when the data is not modeled in application state.
- Simple assignment of display properties returned or published by the Plugin refresh module.

Move out of the ViewModel:

- `RefreshPluginsForGameAsync` context assembly.
- `RefreshMo2PluginsForGameAsync` context assembly.
- Data folder override resolution used only for Plugin refresh.
- MO2 instance/profile refresh branching.
- Load-order fallback resolution used only for Plugin refresh.
- Path map construction.
- Skip list application.
- Direct plugin row publication.

Settings commands may still update configuration through existing configuration methods if moving all settings writes would broaden this slice too far. The important rule is that settings commands should then trigger Plugin refresh by intent, not by constructing a full refresh context.

## Cleaning Preflight Migration

Update `CleaningPreflight` to consume the shared Skip list policy.

Preserve these Cleaning session rules:

- `FlushPendingSavesAsync` still runs before xEdit launch.
- Unknown game detection still blocks cleaning if Skip lists cannot be safely applied.
- MO2 configuration validation still blocks cleaning with actionable errors.
- Direct-mode file validation still happens before cleaning.
- MO2 mode still skips direct file validation because the MO2 virtual file system resolves paths at xEdit runtime.
- Dry-run and real Cleaning session preflight still share plugin decisions.

The goal is not to make Plugin refresh replace Cleaning session preflight. The goal is to make both modules consume one Skip list policy so UI-visible rows and launch-blocking decisions cannot drift.

## State Publication

Plugin refresh should own state publication for refresh outcomes.

Expected state effects:

- Set current game type for the active refresh intent.
- Update configuration paths/profile facts needed by other modules.
- Set plugin rows after loading and Skip list application.
- Preserve or prune exclusions consistently with current `StateService.SetPluginsToClean` behavior.
- Merge Issue approximation results by path-first identity, preserving current fallback behavior.
- Clear plugin rows when no selected game, no resolvable MO2 context, or no valid load-order source exists.

Do not publish stale rows or stale Issue approximation results after a newer generation starts.

## Generation And Cancellation Rules

Preserve current generation semantics:

- Each full Plugin refresh increments a generation.
- Each selected Issue approximation refresh increments a generation if it can supersede active approximation work.
- Starting a newer Plugin refresh cancels the older active refresh token source.
- Manual cancellation publishes a canceled status.
- Superseded generations stay silent during normal unwind.
- Approximation callbacks check both cancellation and generation before merging into state.
- Disposal cancels active refresh work and disposes subscriptions/resources.

## Suggested Implementation Order

1. Read `CONTEXT.md`, `AGENTS.md`, `docs/adr/0001-replace-cleaning-orchestrator-seam.md`, and this document.
2. Add shared Skip list policy tests first, covering variant detection, Disable Skip Lists, TTW, Enderal, and universal entries through existing configuration behavior.
3. Add or reshape the Skip list policy module.
4. Update `PluginRefreshCoordinator` tests to describe the deep Plugin refresh behavior through the Plugin refresh seam, not through caller-built `PluginRefreshRequest` details.
5. Reshape the Plugin refresh interface so callers pass intent, not precomputed context.
6. Move direct-mode context assembly from `ConfigurationViewModel` into the Plugin refresh implementation.
7. Move MO2 context assembly from `ConfigurationViewModel` into the Plugin refresh implementation.
8. Keep two-phase Issue approximation and generation cancellation behavior working after the move.
9. Update `ConfigurationViewModel` to trigger Plugin refresh by intent and apply only UI display projection.
10. Update `CleaningPreflight` to consume the shared Skip list policy.
11. Update DI registrations in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` if new modules are introduced.
12. Delete or internalize the old shallow `PluginRefreshRequest` surface once no caller needs it.
13. Run targeted tests, then the full solution tests.

## Test Plan

Primary tests should cross the Plugin refresh interface. The interface is the test surface.

Add or update tests for:

- Direct Mutagen-supported game publishes rows with Skip list status and pending Issue approximation.
- Direct file-based game resolves load-order override or default path before loading rows.
- Direct file-based game with no load-order source clears rows and publishes a useful status.
- MO2 mode with no resolved instance clears rows and publishes a useful status.
- MO2 mode with no usable profile clears rows and publishes a useful status.
- MO2 mode with valid instance/profile loads rows from MO2 load order and uses path map for Issue approximation.
- Disable Skip Lists prevents Skip list entries from being marked or excluded from approximation targets.
- TTW and Enderal variants produce the same Skip list decisions in Plugin refresh and Cleaning session preflight.
- Issue approximation unsupported games publish unavailable rows and do not call approximation analysis.
- Approximation callbacks from stale generations do not merge into state.
- New Plugin refresh cancels older active refresh work.
- Manual cancellation publishes canceled status.
- Plugin refresh reads active in-memory configuration and does not call `FlushPendingSavesAsync`.
- `ConfigurationViewModel` triggers Plugin refresh by intent and no longer constructs `Mo2PathMap`, `Mo2LoadOrderPath`, or effective data folder context.
- Cleaning session preflight uses the shared Skip list policy while preserving file-validation and MO2 validation behavior.

Keep existing tests where they still give leverage:

- `PluginRefreshCoordinatorTests.cs` should become seam-level Plugin refresh tests.
- `PluginLoadingServiceTests.cs` should continue testing plugin loading details.
- `PluginIssueApproximationServiceTests.cs` should continue testing the AutoQAC-to-QueryPlugins adapter.
- `CleaningPreflightTests.cs` should continue testing Cleaning session readiness and file validation.
- `ConfigurationServiceSkipListTests.cs` should continue testing Skip list merge data.
- `ConfigurationViewModelTests.cs` should shrink to UI triggering and display projection.

## Invariants To Preserve

Runtime invariants:

- Plugin refresh never launches xEdit.
- Plugin refresh never calls `FlushPendingSavesAsync`.
- Cleaning session preflight still calls `FlushPendingSavesAsync` before xEdit launch.
- Visible rows publish before Issue approximation completes.
- Issue approximation results merge only into the active refresh generation.
- Skip list decisions are consistent between Plugin refresh and Cleaning session preflight.
- MO2 mode uses MO2 profile load order and resolved plugin paths.
- MO2 mode does not require direct plugin files to exist under the game data folder.
- Direct-mode file-based games still use file load-order loading.
- Direct-mode Mutagen-supported games still use Mutagen-backed plugin discovery when no explicit load order is selected.
- Exclusion pruning behavior remains centralized through state publication rather than duplicated in callers.

Code-shape invariants:

- Do not keep a public refresh interface that requires callers to pass `Mo2PathMap`, `Mo2LoadOrderPath`, or `Mo2BaseDataFolder`.
- Do not duplicate Skip list application in both Plugin refresh and Cleaning session preflight.
- Do not add a pass-through compatibility facade around the old shallow interface.
- Do not move QueryPlugins Mutagen preparation in this slice.
- Do not mutate or build files under `Mutagen/`.
- Do not use `.Result` or `.Wait()` on UI paths.
- ViewModels remain partial and use CommunityToolkit.Mvvm source generators.

## Validation Commands

Run from the repository root:

```bash
dotnet build AutoQACSharp.slnx
dotnet test AutoQACSharp.slnx
```

If a failure is specific to WinUI build/runtime prerequisites, use the WinUI development workflow before changing architecture.

## Suggested Skills For The Implementing Agent

- `codebase-design`: keep module, interface, depth, seam, adapter, leverage, and locality language consistent.
- `tdd`: use for the shared Skip list policy and Plugin refresh seam tests.
- `systematic-debugging`: use before fixing any unexpected build or test failure.
- `winui-dev-workflow`: use for WinUI build/run issues.
- `winui-code-review`: use before finalizing if ViewModel, dialog, or window lifecycle code changes.
