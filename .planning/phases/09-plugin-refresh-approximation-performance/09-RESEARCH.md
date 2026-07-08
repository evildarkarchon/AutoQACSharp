# Phase 9: Plugin Refresh & Approximation Performance - Research

**Researched:** 2026-04-29  
**Domain:** Avalonia MVVM service extraction, cancellable background plugin analysis, Mutagen link-cache traversal  
**Confidence:** HIGH for app architecture and package versions; MEDIUM-HIGH for Mutagen hot-loop optimization because exact allocation behavior is verified from bundled source but should still be regression-tested against package 0.53.1. [VERIFIED: repo files; VERIFIED: dotnet list package; CITED: https://api.nuget.org/v3/registration5-semver1/mutagen.bethesda/0.53.1.json]

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
## Implementation Decisions

### Refresh Targeting
- **D-01:** After a game, data folder, or load-order selection, AutoQAC should load and display plugin rows first; approximation analysis runs afterward as background work.
- **D-02:** Approximation refresh should target selected plugin rows first, not all loaded plugins by default.
- **D-03:** Add a user-facing Refresh selected approximations action for selected plugin rows.
- **D-04:** Non-targeted rows keep their existing approximation value. Rows that have never been analyzed remain Unavailable or the existing equivalent not-analyzed state.
- **D-05:** Skip-list-hidden rows are not analyzed until they become visible after skip-list settings change.
- **D-06:** A selected-plugin refresh stops after its selected target set completes; do not continue into visible deselected rows as idle fill.
- **D-07:** If no plugins are selected, Refresh selected should do nothing clearly, preferably by disabling the command or showing a concise status such as "Select plugins to refresh."
- **D-08:** The selected target set is a snapshot taken when refresh starts. Selection changes during a refresh do not mutate the active target set; a new refresh cancels/replaces the current generation.

### Stale Result And Cancellation UX
- **D-09:** When a newer refresh supersedes older work, keep existing counts until replacement results arrive. Do not clear rows just because work was canceled.
- **D-10:** Automatic supersedes should be silent; users should see the newer refresh status, not a persistent canceled state for the old generation.
- **D-11:** Refresh progress should be truthful item-count progress, such as "Analyzing 2 of 12 selected plugins," with rows updating incrementally as each result arrives.
- **D-12:** A per-plugin analysis failure marks only that row Unavailable and logs details. Do not interrupt users with a dialog for ordinary per-plugin approximation failures.
- **D-13:** Expose a cancel action while approximation refresh is running.
- **D-14:** Manual cancellation keeps completed row results from the canceled run. Not-yet-analyzed rows keep their previous value or Unavailable state, and status should say the refresh was canceled.
- **D-15:** Starting cleaning cancels any active approximation refresh so background Mutagen/file I/O does not compete with the primary cleaning workflow.
- **D-16:** After selected-plugin refresh completes, leave a concise completion status such as "Updated 12 selected plugin approximations." Rows carry detailed results.

### Count Accuracy And Detector Semantics
- **D-17:** Preserve exact count semantics for analyzed plugins. Optimize allocation, memory, and cancellation, but do not show partial, capped, or approximate-subset counts as if they were complete.
- **D-18:** Do not add an automatic per-plugin analysis timeout in this phase. User cancellation is the way to stop long-running exact analysis.
- **D-19:** If cancellation interrupts one plugin midway through exact analysis, do not publish incomplete counts. Keep the previous row value or Unavailable state for that plugin.
- **D-20:** If optimized ITM analysis hits an ambiguous or unsupported Mutagen context case, fail that plugin to Unavailable and log details rather than showing a questionable count.
- **D-21:** If one analysis category fails for a plugin, mark the whole plugin approximation Unavailable rather than mixing complete and incomplete category counts in one row.
- **D-22:** Keep the UI wording as approximations. Counts may be exact for the analyzed load-order snapshot, but they are still pre-clean preview data that can become stale.
- **D-23:** Cancellation checks should be frequent inside hot ITM record/context loops, not only between plugins.
- **D-24:** When exact counts conflict with peak memory or raw speed, prefer lower peak memory over raw speed. Avoid materializing every context per record.

### Workflow Ownership
- **D-25:** Move the whole refresh pipeline out of `ConfigurationViewModel`: game/data-folder resolution, load-order/plugin loading, skip-list application, approximation targeting, cancellation/generation, and refresh status outcomes.
- **D-26:** A refresh coordinator/service owns refresh generation IDs and `CancellationTokenSource` lifecycle. The ViewModel requests refresh/cancel and observes state/status.
- **D-27:** The coordinator publishes plugin rows and approximation updates through `IStateService`, preserving the existing application state hub and merge patterns.
- **D-28:** Game capability cleanup is refresh-scoped for Phase 9. Create only the policy/capability surface needed by plugin refresh and approximation decisions.
- **D-29:** The manual load-order file path in `ConfigureLoadOrderAsync` should join the new coordinator path so file-based and game/data-folder refreshes share behavior and cancellation.
- **D-30:** After extraction, `ConfigurationViewModel` should be a UI shell: path properties, file dialogs, commands, and display/status binding. Plugin load/skip/approximation workflow decisions belong in services.
- **D-31:** The coordinator should communicate typed progress/status outcomes; the ViewModel maps those to user-facing `StatusText` strings.
- **D-32:** The Refresh selected approximations command belongs near the plugin list because it operates on selected plugin rows.
- **D-33:** The coordinator should allow one active refresh generation at a time. New refresh requests cancel/replace old work rather than queueing or running concurrently.
- **D-34:** Refresh selected should be disabled until a stable plugin list is loaded.
- **D-35:** For games where plugin loading is available but issue approximation is unsupported, disable/hide Refresh selected with a clear reason. Do not attempt to expand app-level support in this phase.
- **D-36:** Settings reset, ViewModel disposal, or equivalent lifecycle teardown cancels active refresh work and prevents stale callbacks/state writes.

### the agent's Discretion
- Exact class/interface names, method signatures, DTO/status enum names, XAML placement details, and test file organization are planner discretion as long as the decisions above are preserved.
- The planner may choose the smallest safe refresh-scoped capability abstraction needed to remove duplicated plugin-refresh rules without broadening Phase 9 into a full registry rewrite.

### Deferred Ideas (OUT OF SCOPE)
## Deferred Ideas

- Full app-wide game capability registry cleanup across UI validation, cleaning, dry-run, plugin loading, approximation support, and default paths belongs in a later phase unless a refresh-scoped slice is required here.
- App-level issue approximation support for games beyond the current Skyrim/Fallout 4 families is out of scope for Phase 9.
- Partial/capped count display modes and automatic per-plugin analysis timeouts are intentionally not part of Phase 9.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| REF-02 | Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`. [VERIFIED: `.planning/REQUIREMENTS.md`] | Use a DI-registered plugin refresh coordinator plus refresh-scoped capability policy; leave `ConfigurationViewModel` as dialog/path/status shell. [VERIFIED: `ConfigurationViewModel.cs`; CITED: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview#the-concept] |
| PERF-01 | User can refresh plugin issue approximations with better cancellation, reduced redundant load-order work, or narrower target scope. [VERIFIED: `.planning/REQUIREMENTS.md`] | Snapshot selected visible targets, analyze only those targets, keep one active generation, and publish incremental row results through `IStateService.MergePluginApproximation`. [VERIFIED: `PluginListViewModel.cs`; VERIFIED: `StateService.cs`; CITED: https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads#listening-and-responding-to-cancellation-requests] |
| PERF-02 | User can run ITM approximation on large plugins without materializing every override context for each record. [VERIFIED: `.planning/REQUIREMENTS.md`] | Replace `ResolveAllSimpleContexts(...).ToArray()` in `ItmDetector` with streaming traversal that retains only plugin context and immediate lower-priority context, with frequent cancellation checks. [VERIFIED: `QueryPlugins/Detectors/ItmDetector.cs`; VERIFIED: `Mutagen/.../ImmutableLoadOrderLinkCache.cs`] |
</phase_requirements>

## Summary

Phase 9 should be planned as a service-boundary extraction plus a targeted hot-loop optimization. `ConfigurationViewModel` currently owns game/data-folder resolution, load-order path selection, skip-list application, plugin loading, approximation launch, refresh generation checks, CTS lifecycle, and stale callback filtering in one class. [VERIFIED: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:520-815`] This violates the project’s MVVM/service boundary and directly blocks REF-02. [VERIFIED: `AGENTS.md`; VERIFIED: `.planning/codebase/ARCHITECTURE.md`]

The standard architecture is not a new library stack: keep existing .NET/Avalonia/CommunityToolkit/Mutagen packages, add an internal `IPluginRefreshCoordinator` and a small refresh-scoped capability policy, register them in `ServiceCollectionExtensions`, and use `IStateService` as the only shared state publisher. [VERIFIED: `ServiceCollectionExtensions.cs`; CITED: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/service-registration#registration-methods]

For PERF-02, the critical unknown was Mutagen context ordering. The bundled Mutagen source shows `ImmutableLoadOrderLinkCache.ResolveAllSimpleContexts(..., ResolveTarget.Winner)` returns `_formKeyContexts.ResolveAllSimpleContexts(...)`; the simple context category processes `_listedOrder` from last to first and yields records as discovered, which matches the existing code comment that contexts are winner-first. [VERIFIED: `Mutagen.Bethesda.Core/.../ImmutableLoadOrderLinkCache.cs:751-756`; VERIFIED: `.../ImmutableLoadOrderLinkCacheSimpleContextCategory.cs:226-277`] Therefore the detector can stream contexts, stop after the analyzed plugin plus immediate lower-priority context are known, and avoid per-record arrays while preserving exact counts. [VERIFIED: `QueryPlugins/Detectors/ItmDetector.cs:44-69`]

**Primary recommendation:** Build a singleton refresh coordinator that owns generation/CTS/status/targeting and calls a narrowed approximation API; update `ItmDetector` to stream context enumeration and accept a cancellation token without changing exact count semantics. [VERIFIED: repo architecture; CITED: https://learn.microsoft.com/dotnet/standard/threading/how-to-listen-for-cancellation-requests-by-polling]

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Game/data-folder/load-order refresh coordination | Application Services | ViewModels | Workflow policy and I/O belong in services; ViewModels should request work and map status only. [VERIFIED: `AGENTS.md`; VERIFIED: `ConfigurationViewModel.cs`] |
| Selected-row approximation refresh command | ViewModels | Application Services | `PluginListViewModel` owns visible selected rows, but coordinator owns execution/cancellation. [VERIFIED: `PluginListViewModel.cs`; VERIFIED: `09-CONTEXT.md`] |
| Plugin row state and approximation merges | State model/service | ViewModels | `IStateService` already provides `SetPluginsToClean`, `MergePluginApproximation`, and `MergePluginApproximations`. [VERIFIED: `IStateService.cs`; VERIFIED: `StateService.cs`] |
| Mutagen load-order import/link cache | Application Services / QueryPlugins | External library | Import/cache creation is existing service work backed by Mutagen NuGet packages. [VERIFIED: `PluginIssueApproximationService.cs`; CITED: /mutagen-modding/mutagen] |
| ITM exact-count detection | QueryPlugins library | Mutagen link cache | Detector logic is independent of Avalonia and currently performs the costly per-record context materialization. [VERIFIED: `QueryPlugins/Detectors/ItmDetector.cs`] |
| User-facing status strings | ViewModels | Coordinator DTOs | Context decision D-31 requires typed status from coordinator and ViewModel string mapping. [VERIFIED: `09-CONTEXT.md`] |
| Cleaning start cancellation of approximation work | Application Services | Cleaning command ViewModel | Cleaning remains sequential; start path must cancel approximation before primary cleaning work competes for I/O. [VERIFIED: `AGENTS.md`; VERIFIED: `CleaningCommandsViewModel.cs`] |

## Project Constraints (from AGENTS.md)

- AutoQAC is a Windows-only Avalonia desktop app for xEdit Quick Auto Clean. [VERIFIED: `AGENTS.md`]
- QueryPlugins is a separate Mutagen-based analysis library. [VERIFIED: `AGENTS.md`]
- Use `dotnet build AutoQACSharp.slnx` and `dotnet test AutoQACSharp.slnx` as primary verification commands. [VERIFIED: `AGENTS.md`]
- Preserve sequential cleaning; do not parallelize plugin cleaning or xEdit launches. [VERIFIED: `AGENTS.md`]
- `ProcessExecutionService` intentionally uses a single process slot. [VERIFIED: `AGENTS.md`; VERIFIED: `ProcessExecutionService.cs`]
- Maintain strict MVVM boundaries; `MainWindow.axaml.cs` owns dialog/window interactions, not ViewModels. [VERIFIED: `AGENTS.md`; VERIFIED: `MainWindow.axaml.cs`]
- Use CommunityToolkit.Mvvm source generators; ViewModels using generators must be `partial`; do not add ReactiveUI/System.Reactive to ViewModels. [VERIFIED: `AGENTS.md`; CITED: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/relaycommand]
- Keep I/O and process work async; never block UI thread with `.Result` or `.Wait()`. [VERIFIED: `AGENTS.md`]
- Use constructor injection and central registration in `ServiceCollectionExtensions`; avoid static mutable state and service locators. [VERIFIED: `AGENTS.md`; CITED: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines#recommendations]
- Do not modify `Mutagen/`; treat it as read-only reference. [VERIFIED: `AGENTS.md`]
- Use NSubstitute for mocks and match optional parameters explicitly. [VERIFIED: `AGENTS.md`]
- No Avalonia.Headless test project exists; do not depend on one unless intentionally adding it. [VERIFIED: `AGENTS.md`; VERIFIED: project files]
- Preserve comments and write XML docs for new/substantially rewritten methods; do not remove accurate comments as cleanup. [VERIFIED: global AGENTS.md]

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK | 10.0.203 | Build/test runtime for solution. [VERIFIED: `dotnet --version`] | Project targets .NET 10 and uses C# 13 nullable-enabled projects. [VERIFIED: `AGENTS.md`; VERIFIED: project files] |
| Avalonia | 12.0.1, published 2026-04-13 | Desktop UI/XAML binding surface. [VERIFIED: NuGet registration API] | Existing app stack; use compiled bindings and MVVM command binding instead of code-behind business logic. [VERIFIED: `MainWindow.axaml`; CITED: /avaloniaui/avalonia-docs] |
| CommunityToolkit.Mvvm | 8.4.2, published 2026-03-25 | Source-generated observable properties and relay commands. [VERIFIED: NuGet registration API] | Existing ViewModel pattern; generator supports `CanExecute`, `NotifyCanExecuteChangedFor`, async commands, and cancellation command generation. [CITED: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/relaycommand] |
| Microsoft.Extensions.DependencyInjection | 10.0.3, published 2026-02-10 | Constructor injection/service registration. [VERIFIED: NuGet registration API] | Existing DI stack; built-in container supports interface registration and constructor injection. [CITED: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview#the-concept] |
| Mutagen.Bethesda (+ Skyrim/Fallout4) | 0.53.1, published 2026-02-04 | Load-order import, link cache, record traversal. [VERIFIED: dotnet list package; VERIFIED: NuGet registration API] | Existing plugin analysis dependency; package version matches repository constraints. [VERIFIED: `AGENTS.md`; VERIFIED: `AutoQAC.csproj`; VERIFIED: `QueryPlugins.csproj`] |
| QueryPlugins project | local project reference | ITM/deleted-reference/deleted-navmesh analysis. [VERIFIED: `QueryPlugins/PluginQueryService.cs`] | Keeps analysis library separate from Avalonia UI and AutoQAC workflow. [VERIFIED: `AGENTS.md`; VERIFIED: `.planning/codebase/ARCHITECTURE.md`] |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xUnit | 2.9.3, published 2025-01-08 | Unit/integration tests. [VERIFIED: dotnet list package; VERIFIED: NuGet registration API] | Add service/coordinator and detector tests in existing test projects. [VERIFIED: `AutoQAC.Tests/*.csproj`; VERIFIED: `QueryPlugins.Tests/*.csproj`] |
| FluentAssertions | 8.8.0, published 2025-10-23 | Readable assertions. [VERIFIED: NuGet registration API] | Follow existing test style. [VERIFIED: `ItmDetectorTests.cs`; VERIFIED: `PluginIssueApproximationServiceTests.cs`] |
| NSubstitute | 5.3.0, published 2024-10-28 | Test doubles. [VERIFIED: NuGet registration API] | Mock coordinator/service dependencies; match optional args explicitly. [VERIFIED: `AGENTS.md`] |
| Microsoft.NET.Test.Sdk | 18.0.1, published 2025-11-11 | Test runner integration. [VERIFIED: NuGet registration API] | Use existing `dotnet test AutoQACSharp.slnx` path. [VERIFIED: dotnet list package] |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Existing CommunityToolkit commands | ReactiveUI commands | Forbidden in ViewModel layer by project guidance; do not introduce. [VERIFIED: `AGENTS.md`] |
| `IStateService` incremental merge | Separate row cache in coordinator/ViewModel | Creates duplicate runtime state and stale selection/result risks; use the existing state hub. [VERIFIED: `.planning/codebase/ARCHITECTURE.md`; VERIFIED: `StateService.cs`] |
| Streaming Mutagen contexts | Keep `ToArray()` | `ToArray()` materializes every override context per record; phase requires lower peak memory. [VERIFIED: `ItmDetector.cs`; VERIFIED: `.planning/REQUIREMENTS.md`] |
| Broad app-wide game registry | Refresh-scoped capability policy | Broad registry is deferred; build only enough policy for plugin loading/approximation enablement. [VERIFIED: `09-CONTEXT.md`] |

**Installation:** No new NuGet package is recommended. [VERIFIED: dotnet list package]

```bash
dotnet restore AutoQACSharp.slnx
```

**Version verification:** Use `dotnet list AutoQACSharp.slnx package`; current package versions and selected publish dates were verified via NuGet registration API on 2026-04-29. [VERIFIED: tool output; VERIFIED: NuGet registration API]

## Architecture Patterns

### System Architecture Diagram

```text
User changes game/data folder/load-order OR clicks Refresh selected
        |
        v
ConfigurationViewModel / PluginListViewModel
  - capture UI intent only
  - map typed coordinator status to StatusText
        |
        v
IPluginRefreshCoordinator (single active generation)
  - cancel/replace previous CTS
  - resolve data folder/load-order policy
  - load plugins first and publish rows
  - snapshot selected visible targets
  - run background approximation for target set only
        |
        +--> IPluginLoadingService + IConfigurationService + refresh capability policy
        |
        +--> IPluginIssueApproximationService (narrowed target API)
                  |
                  v
              QueryPlugins.PluginQueryService
                  |
                  v
              ItmDetector streaming context traversal
        |
        v
IStateService
  - SetPluginsToClean for full loaded list
  - MergePluginApproximation for per-row results
  - do not batch-clear non-targeted rows
        |
        v
MainWindowViewModel dispatches state to child ViewModels on UI thread
```

### Recommended Project Structure

```text
AutoQAC/
├── Services/Plugin/
│   ├── IPluginRefreshCoordinator.cs        # refresh request/cancel contract
│   ├── PluginRefreshCoordinator.cs         # generation, CTS, targeting, status outcomes
│   ├── PluginRefreshRequest.cs             # game/data/load-order/target DTOs
│   ├── PluginRefreshStatus.cs              # typed status/progress outcomes
│   └── IPluginRefreshCapabilityPolicy.cs   # refresh-scoped support/load-order/approximation policy
├── ViewModels/MainWindow/
│   ├── ConfigurationViewModel.cs           # path/dialog/status shell only
│   └── PluginListViewModel.cs              # selected-row refresh/cancel commands
└── Infrastructure/
    └── ServiceCollectionExtensions.cs      # DI registration

QueryPlugins/
└── Detectors/
    └── ItmDetector.cs                      # streaming contexts + cancellation
```

### Pattern 1: Service-owned refresh generation

**What:** The coordinator owns an incrementing generation ID plus the active `CancellationTokenSource`; each callback/state write checks both token and generation before publishing. [VERIFIED: existing pattern in `ConfigurationViewModel.cs:520-815`]  
**When to use:** Every plugin list refresh and selected approximation refresh. [VERIFIED: `09-CONTEXT.md`]  
**Example:**

```csharp
// Source: existing ConfigurationViewModel generation guard + .NET cancellation docs.
// [VERIFIED: ConfigurationViewModel.cs:520-532; CITED: https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads]
var generation = Interlocked.Increment(ref _refreshGeneration);
var cts = new CancellationTokenSource();
var previous = Interlocked.Exchange(ref _activeRefreshCts, cts);
CancelAndDispose(previous);

bool IsCurrent() =>
    !cts.Token.IsCancellationRequested &&
    generation == Volatile.Read(ref _refreshGeneration);
```

### Pattern 2: Targeted incremental row updates

**What:** Publish the full loaded plugin list once, then update only targeted rows via `MergePluginApproximation`. [VERIFIED: `StateService.cs:147-167`]  
**When to use:** Selected approximation refresh and automatic background analysis. [VERIFIED: `09-CONTEXT.md`]  
**Example:**

```csharp
// Source: existing StateService single-row merge behavior.
// [VERIFIED: StateService.cs:147-167]
if (IsCurrent())
{
    _stateService.MergePluginApproximation(result);
}
```

### Pattern 3: Streaming immediate-lower-priority context selection

**What:** Iterate `ResolveAllSimpleContexts` once, remember when the analyzed plugin appears, then compare it to the next yielded lower-priority context; do not allocate an array. [VERIFIED: `ItmDetector.cs`; VERIFIED: Mutagen source lines 751-756 and 226-277]  
**When to use:** ITM detector only; preserve exact counts and fail ambiguous cases. [VERIFIED: `09-CONTEXT.md`]  
**Example:**

```csharp
// Source: Mutagen ResolveAllSimpleContexts winner-first behavior verified from bundled source.
// [VERIFIED: Mutagen.Bethesda.Core/.../ImmutableLoadOrderLinkCache.cs:751-756]
// [VERIFIED: Mutagen.Bethesda.Core/.../ImmutableLoadOrderLinkCacheSimpleContextCategory.cs:226-277]
IModContext<IMajorRecordGetter>? pluginContext = null;

foreach (var context in linkCache.ResolveAllSimpleContexts(formLinkInfo))
{
    ct.ThrowIfCancellationRequested();

    if (pluginContext is null)
    {
        if (context.ModKey == pluginModKey)
        {
            pluginContext = context;
        }

        continue;
    }

    if (pluginContext.Record.Equals(context.Record))
    {
        yield return new PluginIssue(record.FormKey, record.EditorID, IssueType.ItmRecord);
    }

    break;
}
```

### Anti-Patterns to Avoid

- **Moving business workflow into `PluginListViewModel`:** It owns selected row state, not Mutagen/import/status policy. [VERIFIED: `PluginListViewModel.cs`; VERIFIED: `AGENTS.md`]
- **Using `MergePluginApproximations` for targeted refresh completion:** Its current behavior marks missing rows Unavailable, which would clear non-targeted rows. [VERIFIED: `StateService.cs:106-145`; VERIFIED: `StateServiceTests.cs`]
- **Starting selected refresh with a live mutable selection view:** Context D-08 requires a target snapshot at start. [VERIFIED: `09-CONTEXT.md`]
- **Adding timeouts/caps for ITM counts:** Context D-17/D-18 forbids partial/capped counts and automatic per-plugin timeout in this phase. [VERIFIED: `09-CONTEXT.md`]
- **Parallelizing xEdit cleaning to solve performance:** Explicitly out of scope and forbidden by project constraints. [VERIFIED: `AGENTS.md`; VERIFIED: `.planning/REQUIREMENTS.md`]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| UI command enable/disable | Manual `ICommand` implementation | `[RelayCommand(CanExecute=...)]` + `[NotifyCanExecuteChangedFor]` or explicit `NotifyCanExecuteChanged` | Toolkit generator is existing standard and supports invalidation. [CITED: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/relaycommand#enabling-and-disabling-commands] |
| Cancellation infrastructure | Custom bool flags only | `CancellationTokenSource`, `CancellationToken`, `ThrowIfCancellationRequested` | .NET cancellation is cooperative and supports Task cancellation semantics. [CITED: https://learn.microsoft.com/dotnet/standard/parallel-programming/task-cancellation] |
| Shared plugin row state | ViewModel-local result cache | `IStateService.SetPluginsToClean` and `MergePluginApproximation` | Existing UI observes state hub; duplicate caches risk stale rows. [VERIFIED: `MainWindowViewModel.cs`; VERIFIED: `StateService.cs`] |
| Load-order/link-cache mechanics | Custom plugin parser for Mutagen-supported games | `IPluginLoadingService`, `LoadOrder.GetLoadOrderListings`, `LoadOrder.Import`, `ToImmutableLinkCache` | Existing service already handles supported vs file-based games. [VERIFIED: `PluginLoadingService.cs`; CITED: /mutagen-modding/mutagen] |
| Game support matrix | Broad app-wide registry rewrite | Small refresh-scoped capability policy | Context defers broad registry cleanup. [VERIFIED: `09-CONTEXT.md`] |

**Key insight:** The hard part is not analysis math; it is preventing stale background work from mutating a UI list that may now represent a different game/data folder/skip-list generation. [VERIFIED: `.planning/codebase/CONCERNS.md`; VERIFIED: `ConfigurationViewModel.cs`]

## Runtime State Inventory

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — phase changes in-memory plugin row/approximation workflow; YAML settings files store paths/settings, not refresh generation state. [VERIFIED: `AutoQAC Data/*.yaml`; VERIFIED: `ConfigurationViewModel.cs`] | No data migration. |
| Live service config | None — no external service UI/database participates in plugin refresh. [VERIFIED: project architecture] | None. |
| OS-registered state | None — Windows registry is read for game data-folder detection but not written by refresh logic. [VERIFIED: `PluginLoadingService.cs:335-421`] | None. |
| Secrets/env vars | None — no secrets/env vars identified in plugin refresh/approximation path. [VERIFIED: grep/code inspection] | None. |
| Build artifacts | Existing `TestResults/coverage/` may be regenerated by tests; no code rename/install artifact migration required. [VERIFIED: `AGENTS.md`] | None. |

## Common Pitfalls

### Pitfall 1: Batch merge clears non-targeted rows
**What goes wrong:** Calling `MergePluginApproximations` with only selected-target results marks every missing plugin Unavailable. [VERIFIED: `StateService.cs:122-137`]  
**Why it happens:** Batch merge is designed for full-list replacement, not targeted update. [VERIFIED: `StateServiceTests.cs`]  
**How to avoid:** Use `MergePluginApproximation` per result for selected refresh; only use batch merge when intentionally replacing the full list. [VERIFIED: `StateService.cs:147-167`]  
**Warning signs:** Tests show non-targeted visible rows lose previous approximation after selected refresh. [VERIFIED: `09-CONTEXT.md`]

### Pitfall 2: Stale callbacks after generation replacement
**What goes wrong:** Older background analysis writes results after game/data folder/skip list changes. [VERIFIED: `.planning/codebase/CONCERNS.md`]  
**Why it happens:** Mutagen import/analysis runs on background work while UI state can change. [VERIFIED: `PluginIssueApproximationService.cs`; VERIFIED: `ConfigurationViewModel.cs`]  
**How to avoid:** Check token + generation at every publish boundary and after awaited calls. [VERIFIED: existing `IsCurrent()` pattern; CITED: https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads]  
**Warning signs:** Results for a previous game appear in current plugin rows. [VERIFIED: requirement PERF-01]

### Pitfall 3: Cancellation only between plugins
**What goes wrong:** Large plugin ITM scans ignore manual cancellation until the entire plugin finishes. [VERIFIED: `ItmDetector.cs` currently lacks token]  
**Why it happens:** `PluginIssueApproximationService` passes a token around import/target loops, but `PluginQueryService.Analyse` and `ItmDetector.FindItmRecords` do not accept one. [VERIFIED: `PluginIssueApproximationService.cs`; VERIFIED: `PluginQueryService.cs`; VERIFIED: `IItmDetector.cs`]  
**How to avoid:** Add cancellation parameters through QueryPlugins detector path and poll inside major-record/context loops with `ThrowIfCancellationRequested`. [CITED: https://learn.microsoft.com/dotnet/standard/threading/how-to-listen-for-cancellation-requests-by-polling]  
**Warning signs:** Cancel button status changes but CPU stays busy until a large plugin completes. [VERIFIED: PERF-01/PERF-02]

### Pitfall 4: Treating QueryPlugins detector coverage as app support
**What goes wrong:** Refresh UI enables approximation for Starfield/Oblivion because QueryPlugins has detectors. [VERIFIED: `PluginQueryService.cs`; VERIFIED: `.planning/codebase/CONCERNS.md`]  
**Why it happens:** Library detector registry is broader than AutoQAC app-level plugin loading/approximation support. [VERIFIED: `.planning/codebase/CONCERNS.md`]  
**How to avoid:** Refresh capability policy must distinguish plugin loading support from app-level approximation support. [VERIFIED: `09-CONTEXT.md`]  
**Warning signs:** Refresh selected enables on Fallout3/FNV/Oblivion file-loaded rows. [VERIFIED: `PluginLoadingService.cs`; VERIFIED: `PluginIssueApproximationService.cs`]

### Pitfall 5: Disposing an active CTS from the wrong generation
**What goes wrong:** New refresh replaces CTS while old finally block disposes the new/current token source. [VERIFIED: existing defensive comments in `ConfigurationViewModel.cs:699-707`]  
**Why it happens:** Interlocked exchange and async finally blocks race. [VERIFIED: `ConfigurationViewModel.cs`]  
**How to avoid:** Use `Interlocked.CompareExchange(ref _activeCts, null, localCts) == localCts` before disposing in finally. [VERIFIED: existing pattern]  
**Warning signs:** `ObjectDisposedException` during a new refresh or missing cancellation. [VERIFIED: code inspection]

## Code Examples

### CommunityToolkit command gating for selected refresh

```csharp
// Source: CommunityToolkit RelayCommand docs and existing PluginListViewModel command style.
// [CITED: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/relaycommand#enabling-and-disabling-commands]
// [VERIFIED: PluginListViewModel.cs]
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
private bool _canRefreshApproximations;

[RelayCommand(CanExecute = nameof(CanRefreshSelectedApproximations))]
private async Task RefreshSelectedApproximationsAsync()
{
    var targets = PluginsToClean.Where(p => p.IsSelected).Select(p => p.Info).ToList();
    await _refreshCoordinator.RefreshApproximationsAsync(targets, CancellationToken.None);
}

private bool CanRefreshSelectedApproximations() =>
    CanRefreshApproximations && PluginsToClean.Any(p => p.IsSelected);
```

### Manual cancel action with service-owned CTS

```csharp
// Source: .NET cancellation docs and existing CancelAndDispose pattern.
// [CITED: https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads]
// [VERIFIED: ConfigurationViewModel.cs:826-848]
public void CancelActiveRefresh(PluginRefreshCancelReason reason)
{
    var cts = Interlocked.Exchange(ref _activeRefreshCts, null);
    if (cts is null)
    {
        return;
    }

    try
    {
        cts.Cancel();
    }
    finally
    {
        cts.Dispose();
    }
}
```

### QueryPlugins cancellation signature

```csharp
// Source: existing QueryPlugins service surface + .NET cancellation guidance.
// [VERIFIED: QueryPlugins/PluginQueryService.cs]
// [CITED: https://learn.microsoft.com/dotnet/standard/parallel-programming/task-cancellation]
public PluginAnalysisResult Analyse(
    IModGetter plugin,
    ILinkCache linkCache,
    GameRelease gameRelease,
    CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    issues.AddRange(_itmDetector.FindItmRecords(plugin, linkCache, ct));
    ct.ThrowIfCancellationRequested();
    issues.AddRange(gameDetector.FindDeletedReferences(plugin));
    issues.AddRange(gameDetector.FindDeletedNavmeshes(plugin));
    return new PluginAnalysisResult(issues);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| ViewModel owns plugin refresh workflow | Service coordinator owns workflow; ViewModel sends UI intent and maps status | Phase 9 target, not yet implemented. [VERIFIED: `09-CONTEXT.md`] | Satisfies REF-02 and reduces stale state risk. [VERIFIED: `.planning/REQUIREMENTS.md`] |
| Full-list approximation after every Mutagen load | Load rows first, then targeted/selected background approximation | Phase 9 target, not yet implemented. [VERIFIED: `09-CONTEXT.md`] | Reduces redundant work and improves perceived responsiveness. [VERIFIED: PERF-01] |
| `ResolveAllSimpleContexts(...).ToArray()` per record | Streaming context traversal with cancellation checks | Phase 9 target, not yet implemented. [VERIFIED: `ItmDetector.cs`; VERIFIED: `09-CONTEXT.md`] | Lowers peak allocation for large plugins while preserving exact counts. [VERIFIED: PERF-02] |
| Generic unsupported-game checks spread across classes | Refresh-scoped capability policy | Phase 9 target, not broad registry. [VERIFIED: `09-CONTEXT.md`] | Prevents enabling unsupported approximation refresh without over-scoping registry rewrite. [VERIFIED: `.planning/codebase/CONCERNS.md`] |

**Deprecated/outdated:**
- Treating `ConfigurationViewModel.RefreshPluginsForGameAsync` as the workflow owner is now out of date for Phase 9. [VERIFIED: `09-CONTEXT.md`; VERIFIED: `ConfigurationViewModel.cs`]
- Materializing all contexts with `ToArray()` in the ITM hot loop is now explicitly a performance target. [VERIFIED: `ItmDetector.cs`; VERIFIED: PERF-02]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | A small refresh-scoped capability policy is enough and does not need to become a full app-wide registry. [ASSUMED from context discretion; not yet validated against implementation complexity] | Standard Stack / Architecture Patterns | Planner may under-scope duplicated game capability cleanup and need an extra task. |
| A2 | Existing ListBox selected item plus row `IsSelected` checkboxes is sufficient for a multi-row selected refresh target snapshot. [ASSUMED based on current wrapper model] | Code Examples | Planner may need extra selection UX/model work if current row selection semantics are ambiguous to users. |

## Open Questions

1. **Should selected refresh include only visible checked rows or also hidden excluded rows?**
   - What we know: D-02 targets selected plugin rows first; D-05 says skip-list-hidden rows are not analyzed until visible. [VERIFIED: `09-CONTEXT.md`]
   - What's unclear: Whether "selected" maps to `PluginListItem.IsSelected` checkbox (currently cleaning inclusion) or Avalonia `SelectedPlugin` row focus. [VERIFIED: `PluginListViewModel.cs`; VERIFIED: `MainWindow.axaml`]
   - Recommendation: Treat selected as checked visible rows (`IsSelected == true`) and name tests accordingly; ask user only if they expected focus selection. [ASSUMED]

2. **Should automatic post-load approximation refresh run all visible rows or only selected rows?**
   - What we know: D-01 says analysis runs after rows load; D-02 says selected first, not all loaded by default; D-06 forbids selected refresh from idle-fill into deselected rows. [VERIFIED: `09-CONTEXT.md`]
   - What's unclear: Automatic initial load may still analyze visible selected rows only or may perform a different staged strategy. [VERIFIED: `09-CONTEXT.md`]
   - Recommendation: For Phase 9, auto-refresh visible selected rows only and do not idle-fill deselected rows unless a later decision expands scope. [ASSUMED]

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| .NET SDK | Build/test and package restore | ✓ | 10.0.203 [VERIFIED: `dotnet --version`] | None needed |
| NuGet.org access | Version/publish verification | ✓ | API reachable [VERIFIED: `api.nuget.org` fetches] | Existing restored packages if offline |
| ccc semantic search | Optional code discovery | Partial | Index succeeded; some searches failed with Ollama 400. [VERIFIED: `ccc index/search` output] | Use file grep/read and codebase docs |
| Mutagen submodule | Read-only API reference | ✓ | Present in `Mutagen/` [VERIFIED: grep/read] | Prefer NuGet package tests if source mismatch suspected |

**Missing dependencies with no fallback:** None identified. [VERIFIED: environment audit]

**Missing dependencies with fallback:** ccc search had intermittent embedding errors; fallback is targeted grep/read of canonical files. [VERIFIED: tool output]

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + Microsoft.NET.Test.Sdk 18.0.1 + FluentAssertions 8.8.0 + NSubstitute 5.3.0 [VERIFIED: dotnet list package] |
| Config file | none beyond project files; solution is `AutoQACSharp.slnx`. [VERIFIED: glob] |
| Quick run command | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Plugin"` [VERIFIED: test project exists] |
| Full suite command | `dotnet test AutoQACSharp.slnx` [VERIFIED: AGENTS.md] |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| REF-02 | Plugin refresh workflow no longer lives in `ConfigurationViewModel`; coordinator registered in DI and ViewModels request it | unit + integration | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator|FullyQualifiedName~DependencyInjection"` | ❌ Wave 0 for coordinator tests; ✅ DI test file exists |
| PERF-01 | Selected/visible target snapshot, cancellation generation, no stale writes, no non-targeted clearing | unit | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator|FullyQualifiedName~PluginListViewModel"` | ❌ Wave 0 for coordinator tests; ✅ PluginList tests exist |
| PERF-02 | ITM detector streams contexts, preserves exact immediate-lower-priority semantics, checks cancellation | unit | `dotnet test QueryPlugins.Tests/QueryPlugins.Tests.csproj --filter "FullyQualifiedName~ItmDetector"` | ✅ `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`; ❌ cancellation/allocation regression tests need adding |

### Sampling Rate

- **Per task commit:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator|FullyQualifiedName~PluginListViewModel|FullyQualifiedName~StateService"` or `dotnet test QueryPlugins.Tests/QueryPlugins.Tests.csproj --filter "FullyQualifiedName~ItmDetector"` depending on touched area. [VERIFIED: test files]
- **Per wave merge:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj && dotnet test QueryPlugins.Tests/QueryPlugins.Tests.csproj`. [VERIFIED: project files]
- **Phase gate:** `dotnet test AutoQACSharp.slnx` green before `/gsd-verify-work`. [VERIFIED: AGENTS.md]

### Wave 0 Gaps

- [ ] `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs` — covers REF-02/PERF-01 generation, cancellation, selected snapshots, skip-list-hidden behavior, unsupported approximation status. [VERIFIED: no existing file]
- [ ] `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs` — extend for refresh selected command gating and selected-target snapshot. [VERIFIED: file exists]
- [ ] `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` — extend for coordinator/capability policy registration. [VERIFIED: file exists]
- [ ] `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs` — extend for cancellation in hot loop and immediate-lower-priority preservation after streaming rewrite. [VERIFIED: file exists]
- [ ] `AutoQAC.Tests/Services/StateServiceTests.cs` — add targeted refresh regression proving non-targeted rows keep existing values when single-row merges are used. [VERIFIED: file exists]

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | Local desktop app has no authentication surface. [VERIFIED: `.planning/codebase/ARCHITECTURE.md`] |
| V3 Session Management | no | No web session/cookie surface. [VERIFIED: architecture] |
| V4 Access Control | no | No multi-user authorization model; preserve local file/process safety boundaries. [VERIFIED: `.planning/codebase/CONCERNS.md`] |
| V5 Input Validation | yes | Continue path/file validation in plugin loading/config services; reject invalid load-order entries through existing validation. [VERIFIED: `PluginValidationService.cs`; VERIFIED: `.planning/codebase/CONCERNS.md`] |
| V6 Cryptography | no | No cryptographic operation in phase. [VERIFIED: phase scope] |

### Known Threat Patterns for AutoQAC plugin refresh stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Stale background result overwrites current game rows | Tampering | Generation/token guard before every state write. [VERIFIED: existing pattern; VERIFIED: `09-CONTEXT.md`] |
| Raw local paths in new status/error messages | Information Disclosure | Keep user status concise; log details only where needed and avoid dumping raw file contents. [VERIFIED: `.planning/codebase/CONCERNS.md`] |
| Unsupported game approximation shown as valid | Tampering / Information Disclosure | Refresh capability policy disables unsupported approximation and reports clear reason. [VERIFIED: `09-CONTEXT.md`] |
| UI thread blocking during Mutagen import/analysis | Denial of Service | Keep work async/background and cancellation-aware; never `.Result`/`.Wait()`. [VERIFIED: `AGENTS.md`; CITED: https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads] |

## Sources

### Primary (HIGH confidence)

- `.planning/phases/09-plugin-refresh-approximation-performance/09-CONTEXT.md` — locked phase decisions D-01 through D-36 and deferred scope. [VERIFIED]
- `.planning/REQUIREMENTS.md` — REF-02, PERF-01, PERF-02 definitions. [VERIFIED]
- `AGENTS.md` and global AGENTS instructions — project stack, MVVM/testing/comment constraints. [VERIFIED]
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` — current refresh/generation/approximation workflow concentration. [VERIFIED]
- `AutoQAC/Services/State/StateService.cs` and `IStateService.cs` — existing state merge semantics. [VERIFIED]
- `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` and `PluginLoadingService.cs` — current Mutagen import/analysis/load support. [VERIFIED]
- `QueryPlugins/Detectors/ItmDetector.cs` and `QueryPlugins/PluginQueryService.cs` — current detector path. [VERIFIED]
- `Mutagen.Bethesda.Core/.../ImmutableLoadOrderLinkCache*.cs` — read-only source reference for context ordering/lazy enumeration. [VERIFIED]
- Context7 `/mutagen-modding/mutagen` — LinkCache/ResolveSimpleContext/ToImmutableLinkCache docs. [CITED]
- Context7 `/avaloniaui/avalonia-docs` — compiled binding and command binding guidance. [CITED]
- Microsoft Learn cancellation docs — cooperative cancellation and polling. [CITED: https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads]
- Microsoft Learn CommunityToolkit.Mvvm docs — RelayCommand, CanExecute, cancellation command support. [CITED: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/relaycommand]
- Microsoft Learn DI docs — constructor injection and service registration. [CITED: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview]
- NuGet registration API — package publish/version verification. [VERIFIED]

### Secondary (MEDIUM confidence)

- `.planning/codebase/ARCHITECTURE.md`, `CONCERNS.md`, `CONVENTIONS.md` — codebase mapping generated 2026-04-29 and verified against inspected files. [VERIFIED]

### Tertiary (LOW confidence)

- None used as authoritative findings. ccc semantic search was attempted but partially failed; direct file inspection was used instead. [VERIFIED: tool output]

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — package versions verified by `dotnet list package` and NuGet registration API. [VERIFIED]
- Architecture: HIGH — current responsibilities and desired extraction are explicitly documented and verified in source. [VERIFIED]
- Pitfalls: HIGH — stale merge/cancellation pitfalls are directly visible in code and phase decisions. [VERIFIED]
- Mutagen streaming optimization: MEDIUM-HIGH — ordering/lazy behavior verified from bundled source and docs, but production uses NuGet package 0.53.1, so tests must lock behavior. [VERIFIED; CITED]

**Research date:** 2026-04-29  
**Valid until:** 2026-05-29 for package/API guidance; revisit sooner if Mutagen or Avalonia packages are upgraded. [ASSUMED]
