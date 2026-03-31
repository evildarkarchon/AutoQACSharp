# Codebase Concerns

**Analysis Date:** 2026-03-30

## Architecture Concerns

**CleaningOrchestrator is overly large and doing too much:**
- Issue: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` is 872 lines and handles validation, game detection, skip list filtering, backup orchestration, retry logic, hang monitoring, process lifecycle, and session result building -- all in a single `StartCleaningAsync` method (~500 lines).
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- Impact: Hard to test individual phases in isolation. Adding new pre-clean or post-clean steps requires modifying this monolithic method. Bug risk increases with interleaved concerns.
- Fix approach: Extract phases into dedicated pipeline steps (e.g., `PreCleanValidationStep`, `BackupStep`, `PluginCleaningStep`, `SessionFinalizationStep`) and have the orchestrator compose them. This preserves sequential execution while making each step independently testable.

**ConfigurationViewModel is the second-largest file at 948 lines:**
- Issue: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` handles path configuration, game selection, plugin loading, skip list application, approximation refresh lifecycle, file dialog interactions, and migration warnings.
- Files: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- Impact: Difficult to reason about reactive subscription ordering. The approximation refresh logic alone (lines 757-874) is complex enough to warrant its own class.
- Fix approach: Extract the approximation refresh lifecycle into a dedicated `PluginApproximationRefreshManager` class. Consider extracting game selection logic into a separate coordinator.

**Duplicated `RequiresFileLoadOrder` method:**
- Issue: The same `RequiresFileLoadOrder(GameType)` switch expression is duplicated identically in two files.
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (line 819), `AutoQAC/Services/Cleaning/CleaningService.cs` (line 161)
- Impact: If a new game is added that requires file-based load order, both copies must be updated. Easy to miss one.
- Fix approach: Move to a shared static helper (e.g., `GameTypeExtensions.RequiresFileLoadOrder()`) or a method on `IPluginLoadingService`, which already has `IsGameSupportedByMutagen()` (the inverse concept).

**Duplicated `MapToGameRelease` method:**
- Issue: The `MapToGameRelease(GameType)` switch expression is duplicated in two service files.
- Files: `AutoQAC/Services/Plugin/PluginLoadingService.cs` (line 306), `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` (line 196)
- Impact: Same risk as above -- adding a new Mutagen-supported game requires updating two copies.
- Fix approach: Extract to a shared `GameTypeMapper` or extension method class.

**Duplicated `ResolveConfigDirectory` method (4 copies):**
- Issue: The debug-mode config directory resolution logic (walking up 6 parent directories to find `AutoQAC Data`) is copy-pasted across four services.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs` (line 80), `AutoQAC/Services/Configuration/ConfigWatcherService.cs` (line 54), `AutoQAC/Services/Configuration/LegacyMigrationService.cs` (line 41), `AutoQAC/Services/Process/ProcessExecutionService.cs` (line 377)
- Impact: Any change to the resolution strategy must be applied in four places. The `ProcessExecutionService` copy uses the same logic for PID file location, which diverges slightly.
- Fix approach: Create a shared `ConfigDirectoryResolver` utility class or inject the resolved directory from DI registration in `ServiceCollectionExtensions.cs`.

**Duplicated `IsSupportedGame` / `MutagenSupportedGames` logic:**
- Issue: The set of Mutagen-supported games is defined as a `HashSet` in `PluginLoadingService` and separately as a method in `PluginIssueApproximationService`.
- Files: `AutoQAC/Services/Plugin/PluginLoadingService.cs` (line 33), `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs` (line 112)
- Impact: The two lists could drift. `PluginIssueApproximationService.IsSupportedGame` should delegate to `PluginLoadingService.IsGameSupportedByMutagen` for a single source of truth.
- Fix approach: Have `PluginIssueApproximationService` call `IPluginLoadingService.IsGameSupportedByMutagen()` instead of maintaining its own list.

## Code Quality Concerns

**`TogglePartialForms` is a no-op stub:**
- Issue: The method body is empty with only placeholder comments referencing "Phase 6." The command is bound to a UI CheckBox but does nothing.
- Files: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` (lines 876-882)
- Impact: The Partial Forms feature is documented as experimental in `CLAUDE.md`. The toggle appears interactive but has no effect, which could confuse users.
- Fix approach: Either implement the toggle behavior (persist to config, show warning dialog) or disable/hide the UI control until the feature is ready.

**`UserConfiguration` is mutable and cloned via YAML round-trip:**
- Issue: `ConfigurationService.CloneConfig()` serializes and deserializes through YAML to produce a deep copy. This is called on every `LoadUserConfigAsync` and `SaveUserConfigAsync` invocation -- sometimes multiple times per operation.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs` (line 703), `AutoQAC/Models/Configuration/UserConfiguration.cs`
- Impact: YAML serialization is slow relative to alternatives. Each clone allocates strings, dictionaries, and lists. Under rapid config changes (debounced saves, skip list edits), this adds unnecessary GC pressure.
- Fix approach: Either make `UserConfiguration` immutable (use `init` properties and records throughout) to eliminate cloning, or implement a targeted deep-copy method that avoids serialization.

**`PluginInfo.IsSelected` is a mutable `set` on a `record`:**
- Issue: `PluginInfo` is a `sealed record` (value semantics, `with` expressions) but `IsSelected` uses a mutable `set` accessor instead of `init`. This means the property can be mutated after construction, breaking the immutability contract of records.
- Files: `AutoQAC/Models/PluginInfo.cs` (line 34)
- Impact: The comment says "Mutation is only supported on the UI thread" but nothing enforces this. Background services reading `IsSelected` from `AppState.PluginsToClean` could see torn values. Additionally, `with` expressions on the record will copy the snapshot value of `IsSelected`, which may not reflect the UI's current state.
- Fix approach: Two options: (1) Change to `init` and rebuild the plugin list when selection changes, or (2) separate selection state from `PluginInfo` entirely (e.g., a `Dictionary<string, bool>` for selections in `AppState`).

**Backup failure dialog builds UI controls directly in a service:**
- Issue: `MessageDialogService.ShowBackupFailureDialogAsync` constructs `Window`, `StackPanel`, `Button`, and `TextBlock` objects directly in code-behind, bypassing XAML and the MVVM pattern used everywhere else.
- Files: `AutoQAC/Services/UI/MessageDialogService.cs` (lines 85-181)
- Impact: Inconsistent with the rest of the app. Cannot be styled via AXAML themes. Not testable without a UI thread. Accessibility support (keyboard navigation, screen readers) may be incomplete.
- Fix approach: Create a `BackupFailureDialog.axaml` view and `BackupFailureDialogViewModel` to match the pattern used by other dialogs (e.g., `MessageDialog`, `SettingsWindow`).

**`ConfigurationService.Dispose()` synchronously blocks on async disposal:**
- Issue: The synchronous `Dispose()` calls `DisposeAsync().AsTask().GetAwaiter().GetResult()`, which blocks the calling thread. If called on the UI thread, this could deadlock.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs` (line 737)
- Impact: During application shutdown, `App.axaml.cs` disposes the service provider (line 82), which calls `Dispose()`. If a pending debounced save is in flight, this blocks the UI thread waiting for it to complete.
- Fix approach: Ensure the DI container calls `DisposeAsync` (use `IAsyncDisposable` aware disposal), or make the shutdown handler await `FlushPendingSavesAsync` explicitly before disposing.

**Inconsistent `DateTime.Now` vs `DateTime.UtcNow` usage:**
- Issue: Session timing uses `DateTime.Now` (local time) for `StartTime`/`EndTime`, while backup timestamps and hang detection use `DateTime.UtcNow`. The PID tracking fallback uses `DateTime.Now`.
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (lines 62, 339, 455, 471), `AutoQAC/Services/Process/ProcessExecutionService.cs` (line 262), `AutoQAC/Services/Monitoring/HangDetectionService.cs` (line 69)
- Impact: Mixing local and UTC time can cause subtle bugs around DST transitions. Backup timestamps in `session.json` are UTC but session durations are local-time-based.
- Fix approach: Standardize on `DateTime.UtcNow` internally for all timing. Convert to local time only at display boundaries (ViewModels).

## Missing Infrastructure

**No input sanitization on plugin file names passed to process arguments:**
- Issue: `XEditCommandBuilder.BuildCommand` embeds `plugin.FileName` directly into command-line arguments with only double-quote wrapping. If a plugin filename contains special characters (e.g., embedded quotes, backticks), the command could break or be exploited.
- Files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` (line 41)
- Impact: Low risk in practice (plugin names come from Mutagen or file parsing), but a defense-in-depth gap. Malicious plugin names could inject arguments.
- Fix approach: Validate plugin filenames against a safe character set before building the command. Reject filenames containing `"`, `\`, or shell metacharacters.

**No retry logic on PID file operations:**
- Issue: `ProcessExecutionService.TrackProcessAsync` and `UntrackProcessAsync` read/write `autoqac-pids.json` without retry logic. File locking conflicts (e.g., antivirus scanners) can cause silent failures.
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs` (lines 250-291, 404-428)
- Impact: If PID tracking fails, orphan cleanup on next startup will miss processes. The bare `catch` blocks in `LoadTrackedProcessesAsync` (line 417) silently swallow errors and return an empty list.
- Fix approach: Add retry-with-backoff similar to `ConfigurationService.SaveToDiskWithRetryAsync`. Log warnings on partial failures instead of silently swallowing.

**Bare `catch` blocks suppress all exceptions without logging:**
- Issue: Several methods use bare `catch` (no exception type, no variable) which swallows all exceptions silently. This makes debugging difficult when things go wrong.
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs` (lines 260, 370, 417), `AutoQAC/Services/Configuration/ConfigWatcherService.cs` (line 256), `AutoQAC/ViewModels/AboutViewModel.cs` (line 186)
- Impact: Errors in process start time retrieval, xEdit process identification, PID file parsing, YAML validation, and version detection are silently lost. Failures appear as "everything is fine" instead of logged warnings.
- Fix approach: Replace bare `catch` with `catch (Exception ex)` and log at `Debug` or `Warning` level. At minimum, capture the exception type.

## Technical Debt

**All services are registered as Singletons:**
- Issue: Every service in `ServiceCollectionExtensions.cs` is registered as `Singleton`, including services that hold per-session state (`CleaningOrchestrator` has `_currentProcess`, `_cleaningCts`).
- Files: `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- Impact: Singleton lifecycle is correct for a desktop app with a single window, but it means services must carefully manage their own state reset between cleaning sessions. The `finally` block in `CleaningOrchestrator.StartCleaningAsync` handles this, but any missed reset leads to stale state across sessions.
- Fix approach: Document this design decision explicitly. Consider using a factory pattern for `CleaningOrchestrator` to create fresh instances per session, which would eliminate the state-reset burden.

**Dry-run duplicates significant game-detection and filtering logic from StartCleaningAsync:**
- Issue: `CleaningOrchestrator.RunDryRunAsync` (lines 689-796) duplicates the game detection, variant detection, skip list loading, and plugin filtering logic from `StartCleaningAsync` (lines 96-215). The two methods can drift independently.
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- Impact: Changes to the filtering pipeline (e.g., adding a new validation step) must be applied in both methods. Discrepancies between dry-run and actual cleaning would confuse users.
- Fix approach: Extract the shared preparation logic (validate, detect game, load skip list, filter plugins) into a `PrepareCleaningSession` method that both `StartCleaningAsync` and `RunDryRunAsync` call.

**Fire-and-forget interaction handles in CleaningCommandsViewModel:**
- Issue: The progress and preview interaction handles are discarded with `_ = _showProgressInteraction.Handle(Unit.Default)` without awaiting or observing errors.
- Files: `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` (lines 190, 240)
- Impact: If the interaction handler throws (e.g., window creation fails), the exception is silently swallowed. The user sees no error feedback.
- Fix approach: Await the handle and wrap in try-catch, or subscribe to the returned observable with an error handler.

## Risk Areas

**PluginInfo mutation across threads:**
- Issue: `PluginInfo.IsSelected` is a mutable property on a record that lives in `AppState.PluginsToClean`. The UI thread mutates `IsSelected` via checkbox binding, while background threads (CleaningOrchestrator) read it to determine which plugins to clean.
- Files: `AutoQAC/Models/PluginInfo.cs` (line 34), `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs` (lines 116, 124), `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (lines 163, 175, 180)
- Why fragile: No synchronization protects concurrent read/write of `IsSelected`. The cleaning orchestrator reads `PluginsToClean` from state and filters by `IsSelected` -- if the user toggles a checkbox while this runs, the behavior is undefined.
- Safe modification: Always snapshot the plugin list before entering the cleaning loop. The orchestrator already creates a new `pluginsToClean` list, but the individual `PluginInfo` records inside share identity with the UI's copies.
- Test coverage: No tests validate concurrent access to `IsSelected` across threads.

**Process termination race condition window:**
- Issue: `CleaningOrchestrator.StopCleaningAsync` reads `_currentProcess` under `_processLock`, then calls `TerminateProcessAsync` outside the lock. Between reading the reference and calling terminate, the cleaning loop could null out `_currentProcess` and dispose the process object.
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (lines 599-633)
- Impact: This could cause `InvalidOperationException` when calling `process.Id` or `process.HasExited` on a disposed process. The existing `catch (InvalidOperationException)` blocks mitigate this, but the code relies on exception handling for flow control.
- Fix approach: Hold the lock while checking `HasExited` and initiating termination, or use a reference-counted wrapper that prevents disposal while termination is in progress.

**ConfigWatcherService deferred changes flag is volatile but not atomic with the hash update:**
- Issue: `_hasDeferredChanges` is `volatile bool` and `_lastKnownExternalHash` is a plain reference. In `HandleFileChangedAsync`, the hash is updated and deferred flag is set in separate statements. If two file change events race (unlikely but possible), the flag and hash could become inconsistent.
- Files: `AutoQAC/Services/Configuration/ConfigWatcherService.cs` (lines 33, 178-182)
- Impact: Very low probability due to the 500ms throttle. The worst case is a missed reload or a spurious reload, both of which are benign.
- Fix approach: Wrap the hash-and-flag update in a `lock` for correctness, even if the race window is vanishingly small.

## Security Considerations

**Process execution does not validate executable path contents:**
- Issue: `ProcessExecutionService.ExecuteAsync` accepts any `ProcessStartInfo` and launches the process. While the caller (CleaningService) provides the xEdit path from user configuration, there is no validation that the executable is actually xEdit (no signature check, no filename pattern enforcement at the execution layer).
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs` (lines 32-170)
- Current mitigation: The UI validates that the path ends with `.exe` and xEdit process names are used for orphan detection. The orchestrator validates the configuration before calling clean.
- Recommendations: Add a warning log if the launched executable name does not match known xEdit patterns. This is a defense-in-depth measure -- the primary protection is the user choosing the correct file.

**PID file is world-readable JSON:**
- Issue: `autoqac-pids.json` stores process IDs and plugin names in plain JSON. Any local process can read it.
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs` (lines 404-428)
- Current mitigation: The file contains only PIDs and plugin filenames -- no credentials or sensitive data. It is written to the app's data directory.
- Recommendations: No action needed. The data is non-sensitive and the threat model (local desktop app) does not warrant encryption.

**Backup restore overwrites files unconditionally:**
- Issue: `BackupService.RestorePlugin` calls `File.Copy(backupPath, entry.OriginalPath, overwrite: true)` without any confirmation beyond the UI dialog that triggered the restore.
- Files: `AutoQAC/Services/Backup/BackupService.cs` (line 143)
- Current mitigation: The `RestoreViewModel` UI asks for user confirmation before restoring.
- Recommendations: Consider creating a backup of the current file before overwriting with the restored version, so the user has a way to undo a restore.

## Test Coverage Gaps

**SettingsViewModel has zero test coverage:**
- What's not tested: Settings loading, saving, validation (timeout ranges, path validation), unsaved change detection, and the reset-to-defaults flow.
- Files: `AutoQAC/ViewModels/SettingsViewModel.cs` (610 lines)
- Risk: Settings validation bugs could allow invalid timeout values (0 or negative), invalid paths, or silently fail to save.
- Priority: Medium -- the SettingsWindow is a commonly used feature.

**RestoreViewModel has zero test coverage:**
- What's not tested: Session listing, plugin restore, error handling during restore, empty state handling.
- Files: `AutoQAC/ViewModels/RestoreViewModel.cs` (289 lines)
- Risk: Restore failures could silently corrupt plugin files if error handling is incomplete.
- Priority: Medium -- backup restore is a critical safety feature.

**AboutViewModel has zero test coverage:**
- What's not tested: Version detection, URL launching, license loading.
- Files: `AutoQAC/ViewModels/AboutViewModel.cs` (191 lines)
- Risk: Low -- purely informational UI.
- Priority: Low.

**ConfigurationViewModel and CleaningCommandsViewModel have minimal direct test coverage:**
- What's not tested: These are tested indirectly through `MainWindowViewModelTests` and `MainWindowThreadingTests`, but the split ViewModels have no dedicated test files. Complex flows like approximation refresh cancellation, game selection with fallback to file-based loading, and pre-clean validation are only exercised through integration paths.
- Files: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` (948 lines), `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` (487 lines)
- Risk: Regressions in reactive subscription ordering (e.g., `DisableSkipListsEnabled` race condition avoidance in `InitializeAsync`) are not covered by dedicated tests.
- Priority: High -- these are the most complex ViewModels.

**PluginQueryService (QueryPlugins orchestrator) has zero test coverage:**
- What's not tested: The `Analyse` method that combines ITM detection, deleted references, and deleted navmeshes. The individual detectors are tested, but the composition is not.
- Files: `QueryPlugins/PluginQueryService.cs` (88 lines)
- Risk: If the composition order matters (e.g., link cache state) or if an exception in one detector should not prevent others from running, this would not be caught.
- Priority: Medium.

**MessageDialogService is not tested (Avalonia UI dependency):**
- What's not tested: Dialog display, button click handling, thread marshaling, backup failure dialog flow.
- Files: `AutoQAC/Services/UI/MessageDialogService.cs` (191 lines)
- Risk: The backup failure dialog's code-behind UI construction is the most fragile part. No automated verification that buttons map to correct `BackupFailureChoice` values.
- Priority: Low -- requires UI test infrastructure (Avalonia.Headless) that is explicitly not in the project.

---

*Concerns audit: 2026-03-30*
