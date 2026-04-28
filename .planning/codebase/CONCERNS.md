# Codebase Concerns

**Analysis Date:** 2026-04-28

## Tech Debt

**Cleaning orchestration size and responsibility concentration:**
- Issue: `CleaningOrchestrator` owns validation, game detection, variant detection, skip filtering, backup session creation, sequential xEdit execution, retry prompting, log parsing, session finalization, stop/force-stop state, and hang-monitor lifecycle in one class.
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- Impact: Changes to cleaning behavior are high-risk because unrelated flows share fields such as `_cleaningCts`, `_isStopRequested`, `_currentProcess`, `_lastTerminationResult`, and `_hangMonitorSubscription`. Regression surface includes backups, process termination, log parsing, state updates, and UI prompts.
- Fix approach: Extract focused collaborators for preflight selection, backup session handling, per-plugin execution, log/result finalization, and termination coordination. Keep the orchestrator as the sequential session coordinator only.

**Main-window configuration ViewModel is a service coordinator:**
- Issue: `ConfigurationViewModel` mixes user configuration, game selection, load-order browsing, skip-list reactions, Mutagen loading, file fallback, async cancellation generation, background approximation refresh, and state publishing.
- Files: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- Impact: UI changes can accidentally affect plugin loading and background analysis. The refresh generation and two `CancellationTokenSource` fields make lifecycle bugs difficult to reason about.
- Fix approach: Move plugin refresh and approximation refresh into an application service with a single request model and typed result. Keep the ViewModel responsible for property binding, commands, and dialog outcomes.

**Configuration save/reload pipeline has multiple overlapping state machines:**
- Issue: `ConfigurationService` maintains `_pendingConfig`, `_lastKnownGoodConfig`, `_lastWrittenHash`, `_mainConfigCache`, a debounced Rx save stream, and a file lock, while `ConfigWatcherService` independently tracks `_lastKnownExternalHash` and deferred reload state.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`
- Impact: Save failures, external edits, and cleaning-time deferral are difficult to validate. A failed debounced save reverts in-memory state but does not surface a user-facing error, and watcher hash handling depends on exact timing between write completion and file-system events.
- Fix approach: Centralize configuration persistence behind one serialized command queue. Return explicit save/reload results and expose user-visible failures instead of logging-only fallback.

**Manual command-line construction:**
- Issue: xEdit and MO2 arguments are assembled as strings with embedded quotes and manual escaping rather than using `ProcessStartInfo.ArgumentList`.
- Files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- Impact: Paths or plugin names containing quotes or unusual characters can break command parsing, especially in MO2 mode where xEdit arguments are nested inside `-a "..."`.
- Fix approach: Use `ProcessStartInfo.ArgumentList` for direct xEdit launch. For MO2, isolate an escaping helper with exhaustive tests for spaces, quotes, Unicode, and plugin names ending in backslash-like sequences.

**PID tracking is private, file-based, and not process-safe:**
- Issue: `ProcessExecutionService` stores tracked processes in `autoqac-pids.json` with read-modify-write operations and no cross-process lock. Corrupt JSON is silently discarded.
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- Impact: Two AutoQAC instances can lose each other's tracked PIDs. Corruption removes orphan-cleanup evidence. Tests use reflection because path resolution is private and tied to `AppContext.BaseDirectory`.
- Fix approach: Inject a PID store/path provider, use an interprocess file lock or single-instance app lock, and log corrupted PID file contents metadata before replacing the file.

**QueryPlugins support matrix is not aligned with application use:**
- Issue: `QueryPlugins` registers detectors for Skyrim, Fallout 4, Starfield, and Oblivion, but `PluginIssueApproximationService` only invokes Skyrim and Fallout 4/Fallout 4 VR contexts.
- Files: `QueryPlugins/PluginQueryService.cs`, `QueryPlugins/Detectors/Games/StarfieldDetector.cs`, `QueryPlugins/Detectors/Games/OblivionDetector.cs`, `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`
- Impact: Standalone detector capabilities can look available while the desktop app reports approximations as unavailable for games outside the app-side supported set.
- Fix approach: Document the app-supported approximation matrix in code and tests, or add app-side contexts for every registered `QueryPlugins` detector.

## Known Bugs

**Stop flow can bypass the user confirmation path:**
- Symptoms: Cancellation inside `ProcessExecutionService.ExecuteAsync` attempts graceful termination and then force-kills automatically when the grace period expires. Separately, `CleaningOrchestrator.StopCleaningAsync` stores `GracePeriodExpired` so `CleaningCommandsViewModel` can ask the user whether to force terminate.
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- Trigger: User stops cleaning while xEdit does not exit during the process service grace period.
- Workaround: Use the two-stage stop UI only after verifying whether the process service has already escalated for the same process.

**Force-kill failure is reported as force-killed:**
- Symptoms: `TerminateProcessAsync` catches `Win32Exception` during `process.Kill(entireProcessTree: true)` and returns `TerminationResult.ForceKilled` even when the kill failed.
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs`
- Trigger: Access denied, protected process, or OS-level termination failure.
- Workaround: Inspect logs for `[Termination] Failed to kill process tree`; do not rely solely on `TerminationResult.ForceKilled`.

**Oblivion PathGrid deletion detection is intentionally absent:**
- Symptoms: Oblivion detector returns no deleted-navmesh issues because Oblivion uses PathGrids rather than NAVM records.
- Files: `QueryPlugins/Detectors/Games/OblivionDetector.cs`, `QueryPlugins.Tests/Detectors/Games/OblivionDetectorTests.cs`
- Trigger: An Oblivion plugin contains deleted PathGrid records.
- Workaround: Treat Oblivion deleted-navigation approximation as incomplete and rely on xEdit cleaning results for authoritative output.

## Security Considerations

**User-configured executable launch:**
- Risk: The app launches whatever path is configured as xEdit or MO2 after only existence checks.
- Files: `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- Current mitigation: Pre-clean validation checks path presence and `ConfigurationViewModel` checks `.exe` extension for UI validity.
- Recommendations: Add executable allow-list/name validation through configured xEdit names, display the final executable path before first launch, and consider warning when the binary name does not match the detected/selected game.

**Verbose error dialogs expose internal paths and stack traces:**
- Risk: User-facing error dialogs include exception stack traces and full configured paths.
- Files: `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- Current mitigation: Errors are logged through `ILoggingService`.
- Recommendations: Show concise user-facing errors with a log-file reference. Keep stack traces in logs only.

**Logs include local filesystem paths and process arguments:**
- Risk: Logs can reveal user profile paths, game install locations, plugin names, xEdit path, MO2 path, and command arguments.
- Files: `AutoQAC/Infrastructure/Logging/LoggingService.cs`, `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Configuration/ConfigurationService.cs`
- Current mitigation: No credential-style secrets are used by the application.
- Recommendations: Treat logs as sensitive local diagnostics. Avoid logging full command lines when user-configured paths are enough; redact profile-root paths in user-facing exports.

## Performance Bottlenecks

**Full load-order import for approximation refresh:**
- Problem: `PluginIssueApproximationService` imports the full Mutagen load order and builds an immutable link cache before iterating every target plugin.
- Files: `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`
- Cause: `LoadOrder.Import` and `ToImmutableLinkCache` are performed for the entire load order, then every target calls `PluginQueryService.Analyse`.
- Improvement path: Limit approximation refresh to visible/selected plugins first, cache the imported load order per game/data folder generation, and expose progress/cancellation more aggressively.

**ITM detection materializes every context per record:**
- Problem: `ItmDetector` calls `ResolveAllSimpleContexts(...).ToArray()` for each major record.
- Files: `QueryPlugins/Detectors/ItmDetector.cs`
- Cause: The detector needs the analyzed plugin's context and immediate lower-priority context, but materializes all contexts for each record.
- Improvement path: Replace full materialization with an iterator helper that stops once the analyzed plugin and next lower-priority context are found.

**Configuration clone uses YAML round-tripping:**
- Problem: Every `CloneConfig` serializes and deserializes `UserConfiguration`.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs`
- Cause: Defensive cloning is implemented through YamlDotNet rather than copy constructors or record-style cloning.
- Improvement path: Add explicit deep-copy methods for configuration models and reserve YAML serialization for disk persistence only.

**Backup and retention operations run synchronously in the cleaning flow:**
- Problem: Plugin backup uses `File.Copy`, session cleanup uses `Directory.Delete`, and backup session enumeration uses directory scans.
- Files: `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- Cause: Backup operations are synchronous and are invoked between plugin-processing steps.
- Improvement path: Keep sequential cleaning, but move large file copy/delete work to cancellable background operations before xEdit launch for each plugin, with progress and clear failure choices.

## Fragile Areas

**Termination coordination:**
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- Why fragile: The process service, orchestrator, and ViewModel each participate in stop decisions. Cancellation can mean timeout, user stop, or forced stop, and `CancellationToken.None` is used for cleanup/termination calls.
- Safe modification: Change termination behavior with state-machine tests covering timeout, first stop, second stop, grace-expired user decline, grace-expired user confirm, and failed force kill.
- Test coverage: Unit tests cover some orchestrator and ViewModel force-stop paths, but direct process termination tests avoid spawning real processes in `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`.

**Config watcher and debounced saves:**
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs`
- Why fragile: FileSystemWatcher events, throttling, hash comparisons, pending config snapshots, and cleaning-time deferral interact asynchronously.
- Safe modification: Add deterministic scheduler or queue abstractions before changing debounce/defer behavior.
- Test coverage: Service tests exist, but file-watcher timing remains inherently race-prone.

**Partial Forms support:**
- Files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/ViewModels/PartialFormsWarningViewModel.cs`, `AutoQAC/Views/PartialFormsWarningDialog.axaml.cs`
- Why fragile: Enabling partial forms adds `-iknowwhatimdoing` and `-allowmakepartial` flags to xEdit command lines, which is a high-risk cleaning mode.
- Safe modification: Verify state flow from warning dialog through settings, command building, result display, and user documentation before altering flags.
- Test coverage: ViewModel and command-builder tests cover flag wiring, but no end-to-end UI/process test validates the full warning-to-command path.

**Mutagen-backed plugin loading and issue approximation:**
- Files: `AutoQAC/Services/Plugin/PluginLoadingService.cs`, `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`, `QueryPlugins/PluginQueryService.cs`
- Why fragile: The code depends on Mutagen release mapping, game install detection, load-order listings, data folder overrides, and typed mod imports.
- Safe modification: Use fake providers for unit tests and add fixture-backed integration tests outside `Mutagen/` for real plugin/load-order shapes.
- Test coverage: Current detector tests use in-memory Mutagen mods; app-side tests mock plugin loading/approximation rather than exercising real game data folders.

## Scaling Limits

**Single xEdit process slot:**
- Current capacity: One xEdit process at a time by design.
- Limit: Large load orders clean slowly because plugin cleaning is strictly sequential.
- Scaling path: Preserve sequential xEdit launches. Improve throughput only around preflight validation, backup preparation, and background issue approximation.

**Large load orders and large plugins:**
- Current capacity: Full load orders are loaded into memory for Mutagen listings and approximation.
- Limit: Very large mod lists increase memory, CPU, and UI update volume.
- Scaling path: Batch approximation results, debounce UI merges, and avoid full context arrays in per-record ITM detection.

## Dependencies at Risk

**Mutagen package/submodule coupling:**
- Risk: The repo contains a read-only `Mutagen/` submodule while package references target Mutagen 0.53.1.
- Impact: Documentation or source lookup can drift from the exact NuGet API surface used by `AutoQAC/AutoQAC.csproj` and `QueryPlugins/QueryPlugins.csproj`.
- Migration plan: Pin Mutagen documentation notes to the package version and verify API assumptions against package references before changing `PluginLoadingService` or `PluginIssueApproximationService`.

**System.Reactive use in services:**
- Risk: Rx is used for debounced config saves, config watcher pipelines, and orchestrator observables.
- Impact: Error handling and disposal bugs can terminate observable pipelines silently or leave subscriptions active.
- Migration plan: Keep Rx isolated in services; expose plain async methods/events to ViewModels and test scheduler-driven behavior where throttling is used.

## Missing Critical Features

**No real-process integration test harness:**
- Problem: Process termination, orphan cleanup, and PID tracking behavior are safety-critical but tests avoid launching controlled child processes.
- Blocks: Confident refactoring of `ProcessExecutionService` and stop/escalation behavior.

**No fixture-backed QueryPlugins integration corpus:**
- Problem: Detector tests use in-memory Mutagen objects rather than representative plugin files across supported games.
- Blocks: Confidence that ITM/deleted-reference/deleted-navmesh counts match xEdit behavior on real plugins.

**No Avalonia headless UI test project:**
- Problem: Dialog wiring and window lifecycle are covered by ViewModel/service tests, not actual Avalonia visual/control behavior.
- Blocks: Confident refactoring of `MainWindow.axaml.cs`, dialog ownership, and Partial Forms warning flow.

## Test Coverage Gaps

**Process execution and orphan cleanup:**
- What's not tested: Real child process timeout, graceful close, force kill, PID file cleanup after process exit, and access-denied failure paths.
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- Risk: Stop behavior can regress without a failing unit test.
- Priority: High

**Command-line escaping:**
- What's not tested: Plugin names and paths with embedded quotes, Unicode, shell-sensitive characters, and MO2 nested `-a` arguments.
- Files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`, `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`
- Risk: Cleaning fails or targets the wrong plugin when arguments are parsed differently by MO2/xEdit.
- Priority: High

**Configuration watcher race conditions:**
- What's not tested: App save and external edit arriving in the same debounce window, deferred edit followed by app save during cleaning, invalid YAML becoming valid before throttle fires.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs`
- Risk: User settings can revert or reload unexpectedly.
- Priority: Medium

**Backup restore safety:**
- What's not tested: Restore to missing target directories with permission failures, partial restore failures across a session, cleanup deletion failures under locked files.
- Files: `AutoQAC/Services/Backup/BackupService.cs`, `AutoQAC.Tests/Services/BackupServiceTests.cs`
- Risk: Recovery path can fail after cleaning has already modified plugins.
- Priority: High

---

*Concerns audit: 2026-04-28*
