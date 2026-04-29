# Codebase Concerns

**Analysis Date:** 2026-04-29

## Tech Debt

**Cleaning orchestration concentration:**
- Issue: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` owns startup cleanup, configuration flush, game detection, skip-list filtering, MO2 validation, file validation, backup creation, retry prompting, xEdit launch coordination, hang monitoring, log parsing, backup retention, stop/force-stop behavior, dry-run preview, and session finalization in one large class.
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Cleaning/CleaningService.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Backup/BackupService.cs`
- Impact: Changes to any cleaning lifecycle behavior risk regressions in unrelated areas, especially cancellation/termination and backup metadata paths. The file is over 1,000 physical lines and contains multiple duplicated branches between real cleaning and preview.
- Fix approach: Extract narrow collaborators for session preparation, plugin filtering, backup session lifecycle, log-stat collection, dry-run planning, and termination state. Keep `CleaningOrchestrator` as a sequential workflow coordinator and preserve the one-plugin-at-a-time invariant.

**Large service/view-model files:**
- Issue: Several files combine multiple responsibilities and are expensive to review safely.
- Files: `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/ViewModels/RestoreViewModel.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- Impact: Backup safety, restore UX, config persistence, plugin loading, and command availability are coupled to long methods and broad classes. Future changes can accidentally alter user-visible state or safety boundaries.
- Fix approach: Split by operation boundary: backup create/restore/delete/retention helpers, configuration persistence vs skip-list operations, plugin refresh vs path editing, and command validation vs dialog handling.

**Duplicated game/load-order requirement rules:**
- Issue: The non-Mutagen load-order requirement is implemented in several places instead of a single policy service.
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Cleaning/CleaningService.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- Impact: Adding a game, changing Mutagen support, or altering load-order behavior requires edits in multiple classes. A mismatch can enable UI actions that the service later rejects, or block a supported path.
- Fix approach: Introduce one injectable game capability/policy abstraction that answers “supports Mutagen,” “requires file load order,” “supports approximations,” and “default load-order path.” Use it in validation, UI state, cleaning, dry-run, and plugin refresh.

**Configuration directory resolution duplicated:**
- Issue: Debug/source config discovery is repeated instead of centralized.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC/Services/Configuration/LegacyMigrationService.cs`, `AutoQAC/Infrastructure/Logging/LogFilePaths.cs`
- Impact: Config loading, config watching, migration, and logging can diverge if deployment layout changes. Debug-only directory walking is easy to update in one service and miss in another.
- Fix approach: Centralize app data/config/log path resolution behind a small path provider and inject it into config, watcher, migration, logging, PID store, and tests.

**Stale or unused state abstractions:**
- Issue: `IStateService.ConfigurationValidChanged` still defines configuration validity as both load order and xEdit configured, which does not match Mutagen-supported games that do not require a load-order path. Current command enabling uses direct `AppState` instead.
- Files: `AutoQAC/Services/State/StateService.cs`, `AutoQAC/Services/State/IStateService.cs`, `AutoQAC/Models/AppState.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- Impact: Future callers of `ConfigurationValidChanged` can reintroduce incorrect UI gating for Skyrim/Fallout 4 Mutagen paths. The interface advertises a simplified validity concept that no longer reflects runtime rules.
- Fix approach: Remove the observable if unused, or replace it with policy-driven validity that accounts for selected game, xEdit path, MO2 mode, plugin count, and load-order requirements.

**QueryPlugins capability mismatch with AutoQAC UI:**
- Issue: `QueryPlugins` contains detectors for Starfield and Oblivion, but AutoQAC plugin approximation and game loading paths only invoke approximation for Skyrim/Fallout 4 families.
- Files: `QueryPlugins/PluginQueryService.cs`, `QueryPlugins/Detectors/Games/StarfieldDetector.cs`, `QueryPlugins/Detectors/Games/OblivionDetector.cs`, `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`, `AutoQAC/Models/GameType.cs`, `AutoQAC/Services/Plugin/PluginLoadingService.cs`
- Impact: The standalone library has broader detector coverage than the desktop app exposes. Future developers may assume detector support means UI/app support exists.
- Fix approach: Document library-only detectors clearly, or extend `AutoQAC/Models/GameType.cs` and `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` when adding app-level game support.

## Known Bugs

**No confirmed reproducible runtime bug detected during static mapping:**
- Symptoms: Not detected.
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC/Services/Configuration/ConfigurationService.cs`
- Trigger: Not applicable.
- Workaround: Not applicable.

**Potential stale command state after background initialization:**
- Symptoms: `MainWindowViewModel` starts `Configuration.InitializeAsync()` fire-and-forget, while command state updates through `StateChanged`; initialization errors are logged in the configuration VM but are not surfaced to the parent constructor caller.
- Files: `AutoQAC/ViewModels/MainWindowViewModel.cs`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- Trigger: Startup config load or plugin refresh failure during asynchronous initialization.
- Workaround: The current UI displays `ConfigurationViewModel.StatusText`; command availability recalculates on subsequent state updates.

## Security Considerations

**User-configured process launch boundary:**
- Risk: AutoQAC executes user-selected paths for xEdit and MO2. If configuration or UI selection points at an unintended executable, the app will launch it with user privileges.
- Files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `AutoQAC/Services/Cleaning/CleaningService.cs`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`, `AutoQAC/Services/MO2/MO2ValidationService.cs`
- Current mitigation: xEdit and MO2 paths are checked for existence; MO2 validation requires `ModOrganizer.exe`; process launch uses `UseShellExecute = false` and `ProcessStartInfo.ArgumentList` for direct mode.
- Recommendations: Keep all process launches on `ArgumentList`; validate xEdit executable names against `AutoQAC/AutoQAC Data/AutoQAC Main.yaml`; show the resolved executable path before launch when config changes externally.

**MO2 nested argument parser boundary:**
- Risk: MO2 mode must pass a nested xEdit command through `ModOrganizer.exe run -a`, creating a second command-line parser boundary where quoting bugs can alter arguments.
- Files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`, `AutoQAC.Tests/Services/CleaningServiceTests.cs`
- Current mitigation: Direct process launch uses `ArgumentList`; MO2 nested payload is formatted with Microsoft CRT quote/backslash rules; tests cover spaces, quotes, Unicode, and payload redaction.
- Recommendations: Treat `FormatMo2NestedArgument` as security-sensitive. Add tests before changing supported plugin filename characters or MO2 invocation shape.

**Backup and restore path containment:**
- Risk: Backup metadata and load-order entries can contain filenames or paths from external files. Unsafe handling could write outside the intended backup/session/restore roots.
- Files: `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC/Services/Backup/BackupPathContainment.cs`, `AutoQAC/Services/Plugin/PluginValidationService.cs`, `AutoQAC.Tests/Services/BackupServiceTests.cs`
- Current mitigation: Load-order parser rejects path separators and control characters; backup destination validation rejects traversal/rooted filenames; restore/delete use containment checks and trusted restore roots.
- Recommendations: Keep restore/delete tests as mandatory safety coverage. Do not bypass `ValidateBackupDestination`, `ValidateRestoreEntry`, or `BackupPathContainment.IsContained` for convenience.

**Config and log path disclosure:**
- Risk: Logs include local filesystem paths for game installs, plugin files, xEdit/MO2 paths, and configuration files.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC/Infrastructure/Logging/LoggingService.cs`
- Current mitigation: No secrets are expected in these files; malformed load-order control characters are sanitized in `AutoQAC/Services/Plugin/PluginValidationService.cs`.
- Recommendations: Avoid logging raw file contents. Continue logging paths only where needed for diagnostics, and do not add environment variables or credentials to YAML config/log output.

## Performance Bottlenecks

**Full load-order import for issue approximation:**
- Problem: Approximation imports the entire load order and builds an immutable link cache before analyzing each plugin.
- Files: `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`, `QueryPlugins/PluginQueryService.cs`, `QueryPlugins/Detectors/ItmDetector.cs`
- Cause: `LoadOrder.Import` and `ToImmutableLinkCache` require full load-order state; ITM detection resolves contexts for each major record.
- Improvement path: Keep approximation off the UI thread. Add cancellation stress tests with large synthetic load orders; consider limiting approximation to visible/selected plugins or offering an opt-in refresh for very large modlists.

**Per-record ITM allocation:**
- Problem: ITM detection allocates an array of all resolved contexts for every major record.
- Files: `QueryPlugins/Detectors/ItmDetector.cs`
- Cause: `linkCache.ResolveAllSimpleContexts(formLinkInfo).ToArray()` is called inside the record loop to find the plugin override and lower-priority master record.
- Improvement path: Benchmark against large real plugins. If hot, avoid materializing all contexts when the plugin context and immediate lower-priority context can be found with streaming enumeration.

**Synchronous filesystem scans in UI-facing paths:**
- Problem: Plugin refresh and validation perform file existence, directory existence, registry probing, and load-order parsing from view-model initiated operations.
- Files: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Plugin/PluginValidationService.cs`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- Cause: Path validation uses direct `File.Exists`/`Directory.Exists`; Mutagen listing and approximation use background tasks, but several preflight checks remain synchronous.
- Improvement path: Keep heavy operations async and cancellable. Avoid adding synchronous disk enumeration to property setters or `OnStateChanged` handlers.

**Backup retention directory classification:**
- Problem: Retention cleanup scans backup session directories and reads metadata before deleting old sessions.
- Files: `AutoQAC/Services/Backup/BackupService.cs`
- Cause: `CleanupOldSessionsAsync` classifies valid sessions, sorts them, reports progress, and deletes candidates sequentially.
- Improvement path: Sequential deletion is safer for filesystem operations; keep cancellation checks frequent and avoid parallel deletion unless restore safety and progress semantics are redesigned.

## Fragile Areas

**Sequential cleaning invariant:**
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- Why fragile: The workflow depends on exactly one active xEdit process and one plugin at a time. `ProcessExecutionService` enforces a single slot, while `CleaningOrchestrator` also sequences backups, launches, log offsets, and state updates.
- Safe modification: Do not parallelize plugin loops, backups that precede xEdit, or xEdit launches. Preserve the `_processSlots = new(1, 1)` behavior and per-plugin log offset capture inside the retry loop.
- Test coverage: Good coverage exists for process execution and orchestrator sequencing; add regression tests for any change that touches loops, retries, cancellation, or process slots.

**Stop/force-stop race handling:**
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`, `AutoQAC/ViewModels/ProgressViewModel.cs`
- Why fragile: Stop behavior crosses UI commands, cancellation tokens, current process references, hang-monitor subscriptions, PID tracking, and process termination return values.
- Safe modification: Preserve two-stage stop semantics: first graceful cancellation/`CloseMainWindow`, then force kill only after second click or user confirmation. Keep `CancellationToken.None` termination calls intentional so disposal/caller cancellation does not abandon termination cleanup.
- Test coverage: Process and view-model tests cover several stop paths; add integration tests when changing `StopCleaningAsync`, `ForceStopCleaningAsync`, `TerminateProcessAsync`, or progress-window command state.

**Debounced configuration persistence and external reload:**
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC/App.axaml.cs`
- Why fragile: Saves are debounced with Rx `Throttle`/`Switch`, pending config is stored in `_pendingConfig`, external file changes are hash-filtered, and reloads are deferred during cleaning.
- Safe modification: Always call `FlushPendingSavesAsync` before launching xEdit. Maintain app-initiated save hash filtering in `ConfigWatcherService` to prevent save/reload loops.
- Test coverage: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs` and `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs` cover concurrency and watcher behavior; add tests for any new config file or save path.

**Backup/restore safety model:**
- Files: `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC/Services/Backup/BackupFileCopier.cs`, `AutoQAC/ViewModels/RestoreViewModel.cs`, `AutoQAC.Tests/Services/BackupServiceTests.cs`, `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
- Why fragile: Restore must balance user convenience with preventing unsafe overwrite/delete paths. Metadata is read from JSON files in backup directories and then used to write files back to game data folders.
- Safe modification: Keep trusted restore root gating and containment checks intact. New restore operations must return structured row results instead of throwing after partial work unless the API is explicitly synchronous and documented.
- Test coverage: Strong path traversal, rooted path, cancellation, deletion, and partial-restore tests exist; preserve them as safety tests.

**Plugin issue approximation state merging:**
- Files: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`, `AutoQAC/Services/State/StateService.cs`
- Why fragile: Approximation runs in the background while users can change game selection, data folder overrides, skip lists, or plugin lists. Generation checks and cancellation tokens prevent stale results from overwriting current state.
- Safe modification: Keep `refreshGeneration` checks around every state merge. Do not remove `CancelPendingApproximation` from plugin refresh entry points.
- Test coverage: `AutoQAC.Tests/Services/PluginIssueApproximationServiceTests.cs` covers service behavior; add view-model race tests when changing generation/cancellation logic.

**Mutagen boundary:**
- Files: `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`, `QueryPlugins/Detectors/ItmDetector.cs`, `QueryPlugins/Detectors/Games/SkyrimDetector.cs`, `QueryPlugins/Detectors/Games/Fallout4Detector.cs`, `QueryPlugins/Detectors/Games/OblivionDetector.cs`, `QueryPlugins/Detectors/Games/StarfieldDetector.cs`, `Mutagen/`
- Why fragile: The repository includes `Mutagen/` as a read-only submodule/reference source while production packages use Mutagen 0.53.1. API assumptions can drift between docs, submodule source, and NuGet packages.
- Safe modification: Do not modify or build `Mutagen/`. Verify Mutagen API changes against package references in `AutoQAC/AutoQAC.csproj` and `QueryPlugins/QueryPlugins.csproj`.
- Test coverage: `QueryPlugins.Tests/` covers in-memory detector behavior; add package-version-specific tests when upgrading Mutagen.

## Scaling Limits

**Single xEdit process capacity:**
- Current capacity: One plugin cleaning operation at a time.
- Limit: Large modlists take linear time because every plugin waits for backup, xEdit process launch, log parsing, and result recording.
- Scaling path: Keep cleaning sequential for correctness; improve UX with better progress estimates, resumable sessions, and faster preflight/approximation instead of parallel xEdit execution.

**Large modlist approximation memory/time:**
- Current capacity: Dependent on Mutagen full-load-order import and link cache size.
- Limit: Very large Skyrim/Fallout 4 load orders can make approximation slow or memory-heavy.
- Scaling path: Add configurable approximation disable/refresh controls, cancellation visibility, and performance telemetry around `PluginIssueApproximationService.GetApproximationsAsync`.

**Backup storage growth:**
- Current capacity: Retention controlled by `BackupSettings.MaxSessions` and session cleanup.
- Limit: Backups are full file copies, so large plugins and many sessions can consume significant disk space.
- Scaling path: Keep retention enabled, surface session size before cleanup, and consider optional compression or per-plugin deduplication only after preserving restore safety.

## Dependencies at Risk

**Mutagen 0.53.1:**
- Risk: Core plugin loading and QueryPlugins analysis depend on Mutagen APIs and game-specific generated types.
- Impact: Package upgrades can break `LoadOrder.GetLoadOrderListings`, `LoadOrder.Import`, link cache behavior, or record interfaces used by detectors.
- Migration plan: Upgrade in a dedicated phase. Run `QueryPlugins.Tests/` and AutoQAC plugin loading/approximation tests; consult `docs/mutagen/` and only inspect `Mutagen/` as read-only reference.

**System.Reactive usage in services:**
- Risk: The project intentionally avoids ReactiveUI in ViewModels, but service-layer Rx is used for config save debounce, config watching, and state streams.
- Impact: Rx scheduler or subscription mistakes can cause dropped saves, background-thread UI updates, or swallowed errors.
- Migration plan: Keep ViewModels on `CallbackObserver<T>` and `IUiDispatcher`. If simplifying, replace service Rx with explicit channels/timers in small steps with concurrency tests.

**Avalonia 12 compiled bindings:**
- Risk: UI code relies on compiled bindings and code-behind interactions.
- Impact: Renaming ViewModel properties or interaction names can break XAML at compile/build time or runtime if not covered by tests.
- Migration plan: Keep MVVM boundaries; update `AutoQAC/Views/*.axaml` and corresponding ViewModels together; add view lifecycle tests for new windows/dialogs.

## Missing Critical Features

**Real xEdit end-to-end test harness:**
- Problem: The tests use unit tests and helper processes, but no real xEdit integration test validates command behavior against actual game plugin files.
- Blocks: Full confidence in log parsing, xEdit exit-code interpretation, MO2 VFS behavior, and partial forms behavior across real installations.

**Explicit performance benchmarks:**
- Problem: No benchmark project or tracked performance thresholds are present for Mutagen load-order loading, QueryPlugins ITM detection, backup copy throughput, or config watcher latency.
- Blocks: Safe optimization and regression detection for large modlists.

**Unified game capability registry:**
- Problem: Game support rules are distributed across enums, switch expressions, dictionaries, and tests.
- Blocks: Low-risk addition of Starfield, Oblivion Remastered, or expanded approximation support.

## Test Coverage Gaps

**No Avalonia.Headless UI test project:**
- What's not tested: Full rendered UI behavior, focus/keyboard flows, data-grid interactions, and actual dialog/window lifecycle under Avalonia.
- Files: `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs`, `AutoQAC/Views/MainWindow.axaml.cs`, `AutoQAC/Views/ProgressWindow.axaml.cs`, `AutoQAC/Views/RestoreWindow.axaml.cs`
- Risk: XAML binding, visual-state, or window interaction regressions can pass unit tests.
- Priority: Medium

**Real game/MO2/xEdit integration:**
- What's not tested: Real `ModOrganizer.exe run`, xEdit QAC execution, real log file generation, and actual Windows process/window behavior with xEdit.
- Files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Cleaning/XEditLogFileService.cs`, `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs`
- Risk: Command-line and process tests can pass while real external tools behave differently.
- Priority: High

**Large modlist and large plugin stress tests:**
- What's not tested: Thousands of plugins, very large plugin files, enormous xEdit logs, and long-running approximation cancellation.
- Files: `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`, `QueryPlugins/Detectors/ItmDetector.cs`, `AutoQAC/Services/Cleaning/XEditLogFileService.cs`, `AutoQAC/Services/Backup/BackupFileCopier.cs`
- Risk: UI responsiveness, memory use, and cancellation behavior degrade for real-world heavy modlists.
- Priority: Medium

**Configuration path provider consistency:**
- What's not tested: A single deployment-layout test across config service, watcher, migration, logging, and PID-store path resolution.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC/Services/Configuration/LegacyMigrationService.cs`, `AutoQAC/Infrastructure/Logging/LogFilePaths.cs`, `AutoQAC/Services/Process/DefaultPidStorePathProvider.cs`
- Risk: Published builds and debug builds resolve different paths unexpectedly.
- Priority: Medium

---

*Concerns audit: 2026-04-29*
