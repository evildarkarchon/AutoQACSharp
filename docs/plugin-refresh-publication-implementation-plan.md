# Plugin Refresh Publication Implementation Plan

Status: ready for implementation

## Goal

Deepen Plugin refresh publication so `PluginRefreshCoordinator` no longer publishes visible Plugin refresh state by mutating `AppState` directly or owning the status subject.

The first slice creates a dedicated Plugin refresh publication module with a small interface and a StateService-backed adapter. The module owns how a Plugin refresh becomes visible to the user: current game context, plugin rows, Issue approximation row updates, and `PluginRefreshStatus` emissions.

## Domain Language

`CONTEXT.md` now defines **Plugin refresh publication**:

> The point at which a Plugin refresh becomes visible to the user, including the current game context, plugin rows, and issue approximation status.

Use this term in new names and tests. Avoid generic names like state update, app state mutation, or row sync.

## Design Decisions Already Made

- First slice migrates only `PluginRefreshCoordinator` publication call sites.
- The seam is an explicit interface between `PluginRefreshCoordinator` and the existing runtime state store/status observable.
- The production adapter is backed by `IStateService` plus an internal `Subject<PluginRefreshStatus>`.
- Tests may use a recording adapter; this makes the seam real under the “one adapter = hypothetical seam, two = real” rule.
- Plugin refresh publication owns both runtime state writes and `PluginRefreshStatus` emissions.
- Plugin refresh publication owns stale-generation filtering for visible emissions.
- Plugin refresh publication owns path/name matching for publishing Issue approximation results to visible plugin rows.
- Plugin refresh publication owns updated-count tracking for `AnalyzingSelected`, `FullRefreshCompleted`, and `SelectedRefreshCompleted`.
- Plugin refresh publication owns visible effects of handled Issue approximation failures.
- Preserve existing `AppState`, `PluginRefreshStatus`, and ViewModel-facing behavior in this slice.
- Keep cancellation and work sequencing in `PluginRefreshCoordinator`.

## Non-Goals

- Do not reshape `AppState`.
- Do not broadly reshape `IStateService`.
- Do not migrate `ConfigurationViewModel` or `PluginListViewModel` state writes in this slice.
- Do not redesign the Issue approximation interface in this slice.
- Do not change Plugin loading, Skip list evaluation, capability decisions, QueryPlugins analysis, MO2 resolution, or ViewModel state projection.
- Do not touch `CleaningSession` or `StateServiceCleaningSessionStatePublisher`; ADR-0001 already protects that seam.
- Do not add compatibility facades unless a concrete caller requires one.

## Current Friction

`PluginRefreshCoordinator` currently has publication knowledge spread through the refresh workflow:

- It owns `Subject<PluginRefreshStatus>` and exposes `StatusChanged`.
- It writes directly to `IStateService.UpdateConfigurationPaths`, `UpdateState`, `SetPluginsToClean`, and `MergePluginApproximation`.
- It repeats `IsCurrent(generation, token)` checks around visible emissions.
- It owns visible-target matching through target path/name sets.
- It counts visible Issue approximation updates.
- It constructs unavailable approximation results during handled full-refresh failures.

By the deletion test, this is not complexity that should vanish. If deleted, the rules move into callers or tests. The deepening move is to concentrate those rules behind a Plugin refresh publication interface.

## Files To Add

- `AutoQAC/Services/Plugin/IPluginRefreshPublication.cs`
- `AutoQAC/Services/Plugin/PluginRefreshPublicationModels.cs`
- `AutoQAC/Services/Plugin/StateServicePluginRefreshPublication.cs`
- `AutoQAC.Tests/Services/PluginRefreshPublicationTests.cs`

Names can vary slightly to match repo style, but keep the domain term **Plugin refresh publication** visible in public type names.

## Files To Modify

- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- Any tests that currently assert low-level `IStateService` writes from `PluginRefreshCoordinator`

## Baseline Interface Shape

Use a generation-scoped publication handle. This gives stale-result filtering locality without turning the interface into a generic state mutation surface.

```csharp
public interface IPluginRefreshPublication : IDisposable
{
    IObservable<PluginRefreshStatus> StatusChanged { get; }

    IPluginRefreshPublicationScope BeginRefresh(CancellationToken cancellationToken = default);

    void PublishSelectPlugins();

    void PublishManualCancellation();
}

public interface IPluginRefreshPublicationScope : IDisposable
{
    bool IsVisible { get; }

    void PublishNoGameSelected();

    void PublishConfiguration(
        PluginRefreshProjection projection,
        PluginRefreshPublicationSnapshot snapshot);

    void PublishNoRefreshContext(PluginRefreshStatus status);

    void PublishLoadingPlugins();

    void PublishPluginRows(IReadOnlyList<PluginInfo> rows);

    void PublishApproximationUnavailable();

    IPluginRefreshApproximationPublication BeginFullApproximationRefresh(
        IReadOnlyList<PluginRefreshTarget> targets);

    IPluginRefreshApproximationPublication BeginSelectedApproximationRefresh(
        GameType gameType,
        IReadOnlyList<PluginRefreshTarget> targets);

    void PublishApproximationFailure(
        IReadOnlyList<PluginRefreshTarget> targets,
        string message = "Approximation refresh failed.");
}

public interface IPluginRefreshApproximationPublication
{
    void PublishResult(PluginIssueApproximationResult result);
    void PublishCompleted();
}

public sealed record PluginRefreshPublicationSnapshot(
    string? LoadOrderPath,
    string? Mo2ExecutablePath,
    string? XEditExecutablePath,
    string? Mo2Profile,
    bool Mo2ModeEnabled,
    int CleaningTimeout);
```

This is a baseline, not a command to preserve every method name exactly. Keep the shape: a small top-level interface, a scoped refresh handle, named publication phases, and a separate approximation publication handle for counting/matching.

## Production Adapter Behavior

Implement `StateServicePluginRefreshPublication` as the production adapter.

State it owns:

- `IStateService _stateService`
- `Subject<PluginRefreshStatus> _statusChanged`
- `int _generation`

`BeginRefresh` behavior:

- Increment `_generation` with `Interlocked.Increment`.
- Return a scope that captures the generation and cancellation token.
- Scope `IsVisible` is true only when the token is not canceled and the captured generation matches the current generation.
- Every visible state write and status emission must no-op when `IsVisible` is false.

Top-level publication behavior:

- `StatusChanged` returns `_statusChanged.AsObservable()`.
- `PublishManualCancellation()` emits `PluginRefreshStatusKind.Canceled` even after the active token has been canceled.
- `PublishSelectPlugins()` emits `PluginRefreshStatusKind.SelectPlugins`.
- `Dispose()` disposes the subject.

Scoped publication behavior:

- `PublishNoGameSelected()` sets current game to `GameType.Unknown`, clears plugin rows, and emits idle status with `Message: "No game selected"`.
- `PublishConfiguration(...)` publishes configuration paths, current game, MO2 mode, selected profile, and cleaning timeout using the existing `AppState` shape.
- `PublishNoRefreshContext(status)` clears plugin rows and emits the supplied status.
- `PublishLoadingPlugins()` emits `PluginRefreshStatusKind.LoadingPlugins`.
- `PublishPluginRows(rows)` calls `IStateService.SetPluginsToClean(rows.ToList())` or equivalent while preserving the current state semantics.
- `PublishApproximationUnavailable()` emits `PluginRefreshStatusKind.ApproximationUnavailable`.
- `BeginSelectedApproximationRefresh(...)` marks target visible rows as `PluginIssueApproximation.Pending` and materializes pending rows when the visible plugin list is empty, matching current behavior.
- `PublishApproximationFailure(...)` marks matching target rows unavailable and emits idle status with the supplied failure message.

Approximation publication behavior:

- Store the target set and total target count inside the approximation handle.
- `PublishResult(result)` no-ops if the parent scope is stale.
- `PublishResult(result)` no-ops when the result does not match the target set.
- On a match, increment the visible updated count, publish `PluginRefreshStatus.AnalyzingSelected(updated, total)`, and merge the approximation into visible rows.
- `PublishCompleted()` emits `PluginRefreshStatus.FullRefreshCompleted(updated)` for full refresh handles.
- `PublishCompleted()` emits `PluginRefreshStatus.SelectedRefreshCompleted(updated)` for selected refresh handles.

Path/name matching rule:

- Prefer full path matching when both sides have usable full paths.
- Fall back to file name only when either side lacks a usable full path.
- Use case-insensitive comparison for full paths and file names.
- Keep this rule in the publication implementation and its tests, not in `PluginRefreshCoordinator`.

## `PluginRefreshCoordinator` Migration

Constructor and fields:

- Inject `IPluginRefreshPublication pluginRefreshPublication`.
- Remove `_statusChanged` ownership from the coordinator.
- Replace `StatusChanged => _statusChanged.AsObservable()` with `StatusChanged => _pluginRefreshPublication.StatusChanged`.
- Remove the private `Publish(PluginRefreshStatus status)` helper once all call sites are migrated.

Generation and cancellation:

- Keep `_activeRefreshCts`, `CreateAndActivateGeneration`, `ReleaseGeneration`, and cancellation ownership in the coordinator.
- Remove `_refreshGeneration` and `IsCurrent(...)` if they are only used for visible-emission filtering after migration.
- Use `publicationScope.IsVisible` only as an optional early return to avoid extra work. Do not rely on coordinator checks for correctness; the publication module must enforce stale filtering.

Full Plugin refresh flow:

- Start the method with `using var publication = _pluginRefreshPublication.BeginRefresh(linkedCts.Token);` after creating the linked token source.
- Unknown game/no selected load order calls `publication.PublishNoGameSelected()` and returns the empty projection.
- After `CreateContextAsync`, call `publication.PublishConfiguration(projection, snapshot)`.
- If context is null, call `publication.PublishNoRefreshContext(contextResult.Status ?? idle no-plugin status)`.
- Before plugin loading, call `publication.PublishLoadingPlugins()`.
- If loaded plugin list is empty, call `publication.PublishNoRefreshContext(idle no-plugin status)`.
- After Skip list evaluation, build rows with the current pending/unavailable initial approximation and call `publication.PublishPluginRows(rows)`.
- If Issue approximation is unsupported, call `publication.PublishApproximationUnavailable()`.
- For supported Issue approximation, create `var approximationPublication = publication.BeginFullApproximationRefresh(targets);`.
- Pass `approximationPublication.PublishResult` as the callback into analysis.
- On successful analysis completion, call `approximationPublication.PublishCompleted()`.
- On currently handled non-cancel full-refresh approximation failure, call `publication.PublishApproximationFailure(targets)` instead of looping over targets in the coordinator.

Selected Issue approximation refresh flow:

- If selected target snapshot is empty, call `_pluginRefreshPublication.PublishSelectPlugins()` and return.
- Start a publication scope after creating the linked token source.
- If context is null or Issue approximation unsupported, call `publication.PublishApproximationUnavailable()` and return.
- Call `var approximationPublication = publication.BeginSelectedApproximationRefresh(context.GameType, snapshot);`.
- Pass `approximationPublication.PublishResult` as the callback into analysis.
- On successful analysis completion, call `approximationPublication.PublishCompleted()`.
- Do not broaden exception handling unless tests prove selected-refresh failure is intended to be swallowed. If an exception is already handled in the coordinator, route visible failure effects through publication.

Remove from coordinator after migration:

- Direct calls to `_stateService.UpdateState(...)` for visible Plugin refresh publication.
- Direct calls to `_stateService.SetPluginsToClean(...)` from refresh publication paths.
- Direct calls to `_stateService.MergePluginApproximation(...)`.
- `IsTarget(...)` helper.
- `_statusChanged` subject and `Publish(...)` helper.
- `updated` counting in `AnalyzeTargetsAsync`, if that method remains.

Keep in coordinator:

- Context assembly.
- MO2 resolution.
- Plugin loading.
- Skip list evaluation.
- Capability decisions.
- Data folder resolution.
- QueryPlugins/Issue approximation invocation.
- Cancellation and work sequencing.
- `_lastSuccessfulContext` management.

## Dependency Injection And Fallback Construction

Modify `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`:

```csharp
services.AddSingleton<IPluginRefreshPublication, StateServicePluginRefreshPublication>();
services.AddSingleton<IPluginRefreshCoordinator, PluginRefreshCoordinator>();
```

Register the publication adapter before `IPluginRefreshCoordinator`.

Modify the fallback `new PluginRefreshCoordinator(...)` construction in `ConfigurationViewModel` so it passes a publication adapter. The fallback can construct `new StateServicePluginRefreshPublication(stateService)` because it already has `IStateService`.

If the coordinator constructor is used widely in tests, prefer requiring the new dependency explicitly in updated tests instead of adding optional compatibility code.

## Testing Plan

Add focused tests for `StateServicePluginRefreshPublication`.

Required publication tests:

- `StatusChanged` emits existing `PluginRefreshStatus` values for loading, unavailable, selected refresh completion, full refresh completion, select-plugins, and manual cancel.
- Stale scopes do not mutate state or emit status after a newer scope begins.
- Canceled scopes do not mutate state or emit status.
- `PublishNoGameSelected` clears rows, sets current game to unknown, and emits idle status.
- `PublishConfiguration` updates the same runtime fields currently published by `PluginRefreshCoordinator`.
- `PublishPluginRows` preserves `StateService.SetPluginsToClean` behavior, including pruning stale exclusions.
- `BeginSelectedApproximationRefresh` marks selected visible rows pending and preserves unrelated rows.
- `BeginSelectedApproximationRefresh` materializes pending rows when the visible plugin list is empty.
- `PublishResult` matches by full path before file name.
- `PublishResult` falls back to file name only when a usable full path is missing.
- `PublishResult` ignores non-target results.
- Approximation handles emit `AnalyzingSelected(current, total)` only for matched visible results.
- Full approximation completion emits `FullRefreshCompleted(updated)`.
- Selected approximation completion emits `SelectedRefreshCompleted(updated)`.
- `PublishApproximationFailure` marks target rows unavailable and emits idle failure status.

Update `PluginRefreshCoordinatorTests`:

- Use a recording publication adapter for coordinator tests where possible.
- Assert coordinator behavior in terms of publication calls and sequencing, not low-level `AppState` mutation details.
- Keep integration-style tests only where they prove end-to-end behavior through the production adapter.
- Ensure `StatusChanged` still reaches ViewModel-style subscribers through the coordinator.

Regression tests to watch:

- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`

## Acceptance Criteria

- `PluginRefreshCoordinator` no longer owns `Subject<PluginRefreshStatus>`.
- `PluginRefreshCoordinator.StatusChanged` delegates to `IPluginRefreshPublication.StatusChanged`.
- `PluginRefreshCoordinator` no longer directly calls `IStateService.UpdateState`, `SetPluginsToClean`, or `MergePluginApproximation` for visible Plugin refresh publication.
- Stale generation filtering for visible rows/statuses is enforced inside the publication adapter.
- Issue approximation target matching is implemented and tested inside the publication adapter.
- Full and selected Issue approximation update counts are implemented and tested inside the publication adapter.
- Existing ViewModels continue to consume `AppState` and `PluginRefreshStatus` without interface changes.
- `dotnet test AutoQACSharp.slnx` passes.

## Verification Commands

```bash
dotnet test AutoQACSharp.slnx
```

If the full suite is slow during iteration, run the focused tests first:

```bash
dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "PluginRefresh"
```

Then run the full solution before finishing.

## Guardrails

- Preserve sequential cleaning behavior; this plan must not touch xEdit process sequencing.
- Do not modify `Mutagen/`.
- Keep I/O and process work async.
- Do not block UI threads.
- Do not remove accurate comments that document invariants.
- Add XML doc comments for new public members unless trivial.
- Match optional parameters explicitly in NSubstitute setups and assertions.
