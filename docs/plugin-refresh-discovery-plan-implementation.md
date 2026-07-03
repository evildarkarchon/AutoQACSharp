# Plugin refresh discovery plan implementation

This plan implements ADR-0002: deepen Game capability with a Plugin refresh discovery plan module under `AutoQAC/Services/GameCapability`.

The fresh agent should read these first:

- `CONTEXT.md`
- `docs/adr/0001-replace-cleaning-orchestrator-seam.md`
- `docs/adr/0002-plugin-refresh-discovery-plan-seam.md`
- `AGENTS.md`

## Goal

Create a deep module whose interface gives callers a resolved Plugin refresh discovery plan and game catalog facts without making them branch over Mutagen, file load-order, registry/default-path, or MO2 probing details.

The first slice moves Plugin refresh and main-window UI affordances to this seam. It keeps Plugin refresh publication as the row source and leaves Cleaning session preflight validation for a later slice.

## Out Of Scope

- Do not change xEdit launch or process control.
- Do not rediscover plugin rows inside Cleaning session preflight.
- Do not redesign Skip list behavior.
- Do not deepen the QueryPlugins or Issue approximation adapter seam beyond using the plan's Issue approximation attemptability.
- Do not remove AppState compatibility for Cleaning session in this slice.
- Do not parallelize plugin cleaning or plugin discovery work.

## Current Friction

`PluginRefreshModule` currently owns discovery planning inline:

- `CreateContextAsync`
- `CreateDirectContextAsync`
- `CreateMo2ContextAsync`
- `LoadPluginsAsync`
- `ResolveDataFolder`
- `GetNoPluginsFoundMessage`

It also depends directly on `IGameCapabilityProvider` for automatic discovery, load-order requirements, and Issue approximation attemptability.

`ConfigurationViewModel` also depends directly on `IGameCapabilityProvider` for `AvailableGames`, `IsMutagenSupported`, and `RequiresLoadOrderFile`.

The deletion test result: deleting `GameCapabilityProvider` mostly spreads a boolean table; deleting the discovery decisions in callers would concentrate behavior behind one module. The target module should provide locality for Game capability rules and leverage for Plugin refresh, UI affordances, and later Cleaning session preflight validation.

## Target Module

Place the module in `AutoQAC/Services/GameCapability`.

Recommended files:

- `IPluginRefreshDiscoveryPlanner.cs`
- `PluginRefreshDiscoveryPlanner.cs`
- `PluginRefreshDiscoveryPlanModels.cs`

Use XML doc comments on public members.

### Interface Shape

Keep the external interface small. A suggested shape is:

```csharp
public interface IPluginRefreshDiscoveryPlanner
{
    IReadOnlyList<GameType> GetAvailableGames();

    PluginRefreshGameAffordance GetAffordance(GameType gameType, bool mo2ModeEnabled);

    Task<PluginRefreshDiscoveryPlanResult> CreatePlanAsync(
        PluginRefreshDiscoveryPlanRequest request,
        CancellationToken ct = default);

    Task<PluginRefreshDiscoveredPlugins> LoadPluginsAsync(
        PluginRefreshDiscoveryPlan plan,
        CancellationToken ct = default);
}
```

This keeps callers from choosing between automatic discovery, file load-order discovery, and MO2 profile discovery themselves. The implementation owns those decisions and uses existing modules as adapters.

### Suggested Model Shape

Use records and enums so Plugin refresh publication can map structured outcomes to user-facing status text.

Suggested records:

- `PluginRefreshDiscoveryPlanRequest(GameType GameType, string? SelectedLoadOrderPath)`
- `PluginRefreshDiscoveryPlanResult`
- `PluginRefreshDiscoveryPlan`
- `PluginRefreshDiscoveredPlugins`
- `PluginRefreshGameAffordance`

Suggested enum values:

- `Ready`
- `NoGameSelected`
- `MissingLoadOrderFile`
- `MissingMo2Instance`
- `MissingMo2Profile`
- `MissingMo2ProfileLoadOrder`
- `UnsupportedGame`

Suggested `PluginRefreshDiscoveryPlan` fields:

- `GameType GameType`
- `PluginRefreshDiscoveryMode Mode`
- `PluginRefreshConfigurationProjection Configuration`
- `bool DisableSkipLists`
- `bool CanAttemptIssueApproximation`
- `string? DataFolderPath`
- `string? LoadOrderPath`
- `string? Mo2LoadOrderPath`
- `IReadOnlyDictionary<string, string> Mo2PathMap`
- `string? Mo2BaseDataFolder`

Suggested `PluginRefreshDiscoveryMode` values:

- `None`
- `DirectAutomatic`
- `DirectLoadOrderFile`
- `Mo2LoadOrderFile`

Suggested `PluginRefreshDiscoveredPlugins` fields:

- `PluginRefreshDiscoveryPlan Plan`
- `IReadOnlyList<PluginInfo> Plugins`
- `PluginLoadingStatus? LoadingStatus`

It is acceptable for this module to reference `PluginRefreshConfigurationProjection` from `AutoQAC.Services.Plugin` in the first slice. Avoid moving that record unless implementation friction clearly justifies the churn.

### Adapter Dependencies

`PluginRefreshDiscoveryPlanner` should use constructor injection for:

- `IConfigurationService`
- `IPluginLoadingService`
- `IMo2InstanceService`
- `IGameCapabilityProvider` or an internal catalog object

If removing the public `IGameCapabilityProvider` interface becomes too large for the first slice, keep it as a transitional adapter for untouched modules. The first slice must still remove direct `IGameCapabilityProvider` usage from `PluginRefreshModule` and `ConfigurationViewModel`.

### Plan Creation Behavior

For direct mode:

- Load `UserConfiguration` inside the planner.
- Resolve the per-game data folder override through `IConfigurationService.GetGameDataFolderOverrideAsync`.
- Resolve the effective data folder through `IPluginLoadingService.GetGameDataFolder`.
- For load-order games, choose load-order path in this order: selected path, per-game load-order override, default load-order path from `IPluginLoadingService.GetDefaultLoadOrderPath`.
- Return `MissingLoadOrderFile` when the selected game requires a load-order file and none can be resolved.
- Return `DirectLoadOrderFile` when a load-order path is present.
- Return `DirectAutomatic` when the game catalog says automatic discovery is available and no direct load-order file is required.

For MO2 mode:

- Resolve the per-game data folder override first, then the effective data folder.
- Resolve the per-game MO2 instance override.
- Use `IMo2InstanceService.ResolveInstanceAsync` with selected game, configured MO2 binary, and override path.
- Return `MissingMo2Instance` when no instance resolves.
- Load profiles with `GetProfiles` and select with `ChooseProfile`.
- Return `MissingMo2Profile` when no profile can be selected.
- Resolve profile load order with `GetLoadOrderPath`.
- Return `MissingMo2ProfileLoadOrder` when the selected profile has no load-order path or the file is missing.
- Build `Mo2PathMap` with `BuildPluginPathMap`.
- Return `Mo2LoadOrderFile` with direct-mode `LoadOrderPath = null` in the configuration projection.

For game catalog facts:

- `GetAvailableGames()` should preserve the current stable order and exclude `GameType.Unknown`.
- `GetAffordance(gameType, mo2ModeEnabled)` should preserve current UI behavior for `IsMutagenSupported` and `RequiresLoadOrderFile`.
- `RequiresLoadOrderFile` should be false when MO2 mode is enabled because the MO2 profile supplies the load order.
- `CanAttemptIssueApproximation` should be true for the currently supported Skyrim and Fallout 4 families, false for `Unknown`, Oblivion, Fallout 3, and Fallout New Vegas.

### Status Text Mapping

Keep user-facing text in Plugin refresh publication, not in the planner.

Map structured outcomes to the existing text:

| Planner result | Plugin refresh publication text |
| --- | --- |
| `NoGameSelected` | `No game selected` |
| `MissingLoadOrderFile` | `No load order file found for {gameType}. Browse to plugins.txt or loadorder.txt.` |
| `MissingMo2Instance` | `No MO2 instance found for {gameType}. Browse to the instance folder.` |
| `MissingMo2Profile` | `No MO2 profiles with loadorder.txt were found for {gameType}.` |
| `MissingMo2ProfileLoadOrder` | `MO2 profile '{profile}' does not contain a loadorder.txt.` |
| `UnsupportedGame` | `No plugins found in the selected load order.` or a more specific existing-safe fallback if one already exists |

For empty plugin lists after `LoadPluginsAsync`, preserve current text:

- Automatic discovery: `No plugins discovered via Mutagen for {gameType}.`
- File/MO2 load-order discovery: `No plugins found in the selected load order.`

## Implementation Steps

### 1. Add Discovery-Plan Tests First

Create `AutoQAC.Tests/Services/PluginRefreshDiscoveryPlannerTests.cs`.

Cover these cases before refactoring callers:

- Available games exclude `Unknown` and stay in stable string order.
- Direct automatic games create a ready plan with `DirectAutomatic`, resolved data folder, `CanAttemptIssueApproximation = true` for Skyrim/Fallout4 families, and no load-order requirement.
- Direct load-order games use selected load-order path first.
- Direct load-order games fall back to per-game load-order override.
- Direct load-order games fall back to default load-order path from `IPluginLoadingService`.
- Direct load-order games return `MissingLoadOrderFile` when no selected, override, or default path exists.
- MO2 mode resolves instance, profile, load-order path, profile list, path map, and returns `Mo2LoadOrderFile`.
- MO2 mode returns `MissingMo2Instance` when instance resolution fails.
- MO2 mode returns `MissingMo2Profile` when no profile can be selected.
- MO2 mode returns `MissingMo2ProfileLoadOrder` when the selected profile has no load-order file.
- `LoadPluginsAsync` uses `TryGetPluginsAsync` for `DirectAutomatic`.
- `LoadPluginsAsync` uses `GetPluginsFromFileAsync` for `DirectLoadOrderFile`.
- `LoadPluginsAsync` uses the MO2 load-order path and applies `Mo2PathMap` to returned plugin full paths.

Use NSubstitute and match optional parameters explicitly.

### 2. Add the Module

Add the planner and model files under `AutoQAC/Services/GameCapability`.

Move logic from `PluginRefreshModule` into the planner without changing behavior:

- `CreateDirectContextAsync` logic becomes direct-mode plan creation.
- `CreateMo2ContextAsync` logic becomes MO2 plan creation.
- `LoadPluginsAsync` logic becomes planner-owned plugin loading.
- The capability matrix remains the source of catalog facts, but callers should not branch over it directly.

Return structured result kinds instead of status text. Plugin refresh publication maps result kinds to text.

Preserve these current behaviors:

- `GameType.Unknown` clears rows and publishes "No game selected" from Plugin refresh publication.
- Direct-mode load-order games require a load-order path.
- MO2 mode does not expose a direct-mode load-order path.
- MO2 mode chooses the persisted profile when valid.
- MO2 path map replaces file-based plugin paths when available.
- Backups remain skipped in MO2 mode elsewhere; do not move this behavior.

### 3. Wire DI

Update `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`:

- Register `IPluginRefreshDiscoveryPlanner` as a singleton.
- Keep existing registrations required by out-of-scope modules.
- Update `PluginRefreshModule` constructor dependencies.
- Update `ConfigurationViewModel` constructor dependencies.

Update `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` to resolve the new module and verify the shared `IPluginRefreshModule` wiring still holds.

### 4. Refactor PluginRefreshModule

Replace direct capability/context planning with `IPluginRefreshDiscoveryPlanner`.

Expected changes:

- Remove `_gameCapabilityProvider` from the module.
- Add `_discoveryPlanner`.
- Delete the private `PluginRefreshContext` and `PluginRefreshContextResult` records if the planner models replace them fully.
- Delete or reduce private methods that duplicate planner behavior:
  - `CreateContextAsync`
  - `CreateDirectContextAsync`
  - `CreateMo2ContextAsync`
  - `LoadPluginsAsync`
  - `ResolveDataFolder`
  - `GetNoPluginsFoundMessage`
- In `RefreshGameAsync`, call `CreatePlanAsync`, map non-ready result kinds to status text, call `LoadPluginsAsync` for ready plans, then continue Skip list evaluation and publication.
- In `RefreshSelectedIssueApproximationsAsync`, call `CreatePlanAsync` to get current plan data without replacing rows.
- Use `plan.CanAttemptIssueApproximation` instead of direct capability lookup.
- Use `plan.Configuration` for Plugin refresh publication and state path updates.
- Use `plan.Mode`, `plan.DataFolderPath`, `plan.Mo2BaseDataFolder`, and `plan.Mo2PathMap` to build existing `PluginIssueApproximationRequest` values.

Keep these responsibilities inside `PluginRefreshModule` for this slice:

- Generation/cancellation visibility checks.
- Plugin refresh publication snapshots.
- AppState compatibility publication.
- Plugin selection updates.
- Skip list evaluation.
- Issue approximation callback handling and row matching.
- Status text mapping.

### 5. Refactor ConfigurationViewModel UI Affordances

Replace `_gameCapabilityProvider` with `_discoveryPlanner`.

Expected changes:

- `AvailableGames` comes from `GetAvailableGames()`.
- `IsMutagenSupported` comes from `GetAffordance(SelectedGame, Mo2ModeEnabled)`.
- `RequiresLoadOrderFile` comes from `GetAffordance(SelectedGame, Mo2ModeEnabled)`.

Keep existing property names if XAML depends on them. The implementation can call the new module while preserving public ViewModel interface shape.

### 6. Update Existing Tests

Update `PluginRefreshModuleTests` helper construction:

- Build a real `PluginRefreshDiscoveryPlanner` with test adapters, or use a small test planner adapter when a test needs precise outcomes.
- Keep existing snapshot tests because Plugin refresh publication remains the caller-level test surface.
- Add regression assertions where helpful that Plugin refresh no longer requires direct `IGameCapabilityProvider` setup.

Update `GameCapabilityProviderTests`:

- Keep minimal catalog coverage only.
- Move detailed discovery behavior assertions to `PluginRefreshDiscoveryPlannerTests`.
- If `GameCapabilityProvider` becomes internal, replace direct tests with planner catalog tests and remove the old test class.

Update `MainWindowViewModelTests` or `PluginListViewModelTests` only if constructor arguments or UI affordance behavior changes visible test setup.

### 7. Verification

Run:

```bash
dotnet test AutoQACSharp.slnx
```

If the refactor touches public DI or WinUI-facing constructor graphs, also run:

```bash
dotnet build AutoQACSharp.slnx
```

Do not use UI automation claims; this repo does not have a WinUI UI automation project.

## Acceptance Criteria

- `PluginRefreshModule` no longer branches directly on `IGameCapabilityProvider` for discovery mode, load-order requirements, or Issue approximation attemptability.
- `ConfigurationViewModel` no longer depends directly on `IGameCapabilityProvider` for main-window UI affordances.
- The new module returns structured planning outcomes; Plugin refresh publication maps those outcomes to user-facing status text.
- Plugin refresh snapshots and AppState compatibility behavior remain unchanged from the user's perspective.
- Existing Plugin selection, Skip list, and Issue approximation callback behavior remains covered by `PluginRefreshModuleTests`.
- New focused tests cover direct automatic, direct load-order, MO2, missing-path, and Issue approximation attemptability decisions through the new interface.
- `dotnet test AutoQACSharp.slnx` passes.

## Later Slice

After the first slice is stable, add Cleaning session preflight validation against the latest Plugin refresh discovery plan without rediscovering rows. That later slice should decide how the plan is cached or exposed from Plugin refresh publication and should not be mixed into this first refactor.
