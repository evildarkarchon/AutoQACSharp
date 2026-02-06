# Codebase Concerns

**Analysis Date:** 2026-02-06

## Tech Debt

**Plugin FullPath Resolution Incomplete:**
- Issue: `PluginValidationService.GetPluginsFromLoadOrderAsync()` cannot determine actual file paths. It sets `FullPath = FileName` as a placeholder because the method only reads plugins.txt filenames without access to the game data directory path.
- Files: `AutoQAC/Services/Plugin/PluginValidationService.cs` (lines 36-50, 72)
- Impact: `ValidatePluginExists()` cannot validate that plugins actually exist on disk (returns `true` optimistically for non-rooted paths). This could mask missing plugins until xEdit execution fails.
- Fix approach: Pass game data directory path to `GetPluginsFromLoadOrderAsync()` or require callers to resolve full paths afterward. Consider caching resolved paths in `PluginInfo`.

**Legacy Configuration Migration Incomplete:**
- Issue: `ConfigurationService.MigrateLegacyConfigAsync()` logs intention to delete legacy file but does so outside the lock, with only warning-level error handling if deletion fails.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs` (lines 198-207)
- Impact: Failed deletion leaves duplicate config files, potential user confusion on next startup. Migration success is not validated.
- Fix approach: Add validation that legacy file is actually deleted, elevate failure to Error level, consider retry logic.

**About Dialog Not Implemented:**
- Issue: `MainWindowViewModel.ShowAbout()` has TODO comment and does nothing except set status text.
- Files: `AutoQAC/ViewModels/MainWindowViewModel.cs` (line 780)
- Impact: User request for help/version info is not fulfilled, appears as unresponsive UI.
- Fix approach: Implement actual About dialog with version info and links.

## Known Bugs

**Process Termination Race Condition:**
- Symptoms: Process may not be fully terminated before disposal if `TerminateProcessGracefullyAsync()` completes but process still exists.
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs` (lines 147-179)
- Trigger: Cancellation token triggered during xEdit execution
- Current mitigation: 2-second delay after `CloseMainWindow()` before checking `HasExited`, then force `Kill()` attempt
- Risk: On slow systems or with locked files, xEdit may still hold file handles briefly after process object disposal, blocking subsequent cleaning runs
- Workaround: Users can manually kill xEdit if subsequent plugins fail to load

**Potential Lock Deadlock in StateService:**
- Symptoms: UI freezes if state updates are called from multiple threads concurrently
- Files: `AutoQAC/Services/State/StateService.cs` (line 12 - uses `Lock` instead of `ReaderWriterLockSlim`)
- Cause: Multiple synchronous state mutations could block if `BehaviorSubject.OnNext()` triggers observable subscriptions that attempt further state updates
- Current mitigation: All state updates use immutable records, short lock durations
- Risk: Higher with complex observable chains in ViewModels
- Safer approach: Consider `ReaderWriterLockSlim` for read-heavy scenarios or fully async observable chains

## Security Considerations

**No Input Validation on xEdit Arguments:**
- Risk: `XEditCommandBuilder.BuildCommand()` directly interpolates `plugin.FileName` into command line without escaping or validation
- Files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` (line 43)
- Current mitigation: `plugin.FileName` comes from parsed plugins.txt (trusted source), not user input
- Recommendations: Add validation that plugin filename doesn't contain shell metacharacters (`|`, `&`, `;`, etc.), use argument array instead of string concatenation where possible

**File Path Traversal Not Checked:**
- Risk: Configuration files could specify arbitrary paths via `LoadOrder.File` or `XEdit.Binary`, including network paths
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs` (lines 237-247), `MainWindowViewModel` file selection
- Current mitigation: `File.Exists()` checks prevent non-existent paths, but symbolic links or junction points could redirect execution
- Recommendations: Validate paths don't contain `..`, check for symlinks on Windows (`.IsReparsePoint`), log suspicious path choices

**Logging May Expose Sensitive Paths:**
- Risk: File paths logged at Information/Debug levels could reveal user system structure
- Files: Throughout logging calls in services
- Current mitigation: None - logs contain full paths to xEdit, load order files, plugins
- Recommendations: Consider masking home directories in logs, or adding sensitive data redaction to LoggingService

## Performance Bottlenecks

**Sequential-Only Processing - Scaling Limit:**
- Problem: Architecture intentionally processes one plugin at a time due to xEdit's file locking. This is correct but means cleaning time scales linearly with plugin count.
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (lines 148-235)
- Current bottleneck: A mod list with 500 plugins at 30 seconds each = 4+ hours cleaning time
- Improvement path: Cannot parallelize xEdit execution (architectural constraint), but could optimize:
  1. Pre-validate all plugins before starting (fail fast)
  2. Cache skip list loading to disk instead of memory
  3. Batch output parsing (currently per-plugin)

**Regex Compilation on Every Parse:**
- Problem: `XEditOutputParser` uses `GeneratedRegex` (compiled at compile-time, so this is actually fine - ignore this concern)
- Files: `AutoQAC/Services/Cleaning/XEditOutputParser.cs` (lines 16-26)
- Status: Already optimized - generated regexes are AOT-compiled

**Configuration File Disk I/O Not Batched:**
- Problem: Each configuration operation acquires `_fileLock` and reads/writes entire file. Multiple rapid config changes trigger multiple disk I/O operations.
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs` (lines 95, 132, 215)
- Impact: Noticeable lag when user rapidly changes settings or toggles skip lists
- Improvement path: Implement dirty-flag pattern with periodic batch writes, debounce observable changes

**MainWindowViewModel is Very Large:**
- Problem: `MainWindowViewModel` is 904 lines - likely handling multiple concerns
- Files: `AutoQAC/ViewModels/MainWindowViewModel.cs` (entire file)
- Impact: Harder to test, higher chance of bugs in UI logic, slower to compile
- Improvement path: Extract plugin loading logic to separate `PluginSelectionViewModel`, clean results display to separate `ResultsViewModel`

## Fragile Areas

**Game Detection Fallback Path Unclear:**
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (lines 74-95)
- Why fragile: If xEdit auto-detection fails and load order has no extension to hint game type, cleaning proceeds with `GameType.Unknown`. Skip list lookup then returns empty, potentially cleaning undesired plugins.
- Safe modification: Add explicit game type validation before processing. Log warning if game type remains Unknown after detection attempts.
- Test coverage: Covered in `CleaningOrchestratorTests`, but edge case of Unknown game type needs explicit test

**Plugin FullPath Placeholder Creates Two Code Paths:**
- Files: `AutoQAC/Services/Plugin/PluginValidationService.cs` (lines 100-115)
- Why fragile: `ValidatePluginExists()` behaves differently depending on whether `FullPath` is absolute or relative. Callers must understand this distinction.
- Safe modification: Document contract clearly - either require rooted paths in `PluginInfo` at construction, or refuse validation for relative paths with explicit error
- Test coverage: No tests for `ValidatePluginExists()` with relative paths

**CancellationTokenSource Lock Synchronization:**
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (lines 24, 138-141, 269-283, 301-305)
- Why fragile: `_cleaningCts` is locked but also checked by `StopCleaning()` and accessed in finally block. If `StopCleaning()` is called during disposal, race condition on null check is possible.
- Safe modification: Use `Interlocked` operations instead of lock for null check, or ensure `StopCleaning()` cannot be called after `Dispose()` starts
- Test coverage: No explicit tests for concurrent `StopCleaning()` + cleanup scenarios

**XEdit Command Building Doesn't Validate Game Type:**
- Files: `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` (lines 34-37, 80-91)
- Why fragile: `GetGameFlag()` returns empty string for `GameType.Unknown`, producing command like `-QAC` without game flag. xEdit's behavior is undefined.
- Safe modification: Throw or return null if `GameType.Unknown` is passed, force caller to resolve before building
- Test coverage: Tests exist but don't cover Unknown game type scenario

## Scaling Limits

**State Immutability Creates Allocations:**
- Current capacity: Each state update creates new immutable `AppState` record. With frequent progress updates (per-plugin), this creates garbage collection pressure.
- Limit: On systems with 1000+ plugins, per-plugin state updates could accumulate allocations
- Scaling path: Profile with large mod lists. If necessary, switch to mutable state with lock-based synchronization, or batch state updates

**Configuration Cache Single-Level:**
- Current capacity: `_mainConfigCache` in `ConfigurationService` is never invalidated
- Limit: If user edits YAML file externally, application continues with stale config
- Scaling path: Add cache invalidation on file modification detection (File System Watcher), or make cache invalidation explicit command

**ObservableAsPropertyHelper Subscriptions Not Cleaned:**
- Current capacity: `MainWindowViewModel` creates multiple observable chains (e.g., `RequiresLoadOrderFile`, `IsMutagenSupported`)
- Limit: Long-lived ViewModels could accumulate subscriptions if not properly disposed
- Scaling path: Verify all observables are disposed in `ViewModelBase.Dispose()` via `CompositeDisposable`

## Dependencies at Risk

**Mutagen Version Lock to Old API:**
- Risk: Mutagen 0.52.0 is not latest (0.54+ exists). Potential security patches or bug fixes in newer versions not applied.
- Impact: `PluginLoadingService` uses Mutagen for games like Skyrim SE and Fallout 4. Version mismatch could cause load failures.
- Files: `AutoQAC/Services/Plugin/PluginLoadingService.cs` (uses Mutagen imports)
- Migration plan: Evaluate Mutagen 0.54+ for compatibility, update if no breaking changes. Test plugin loading after upgrade.

**Avalonia 11.3.11 Not Latest LTS:**
- Risk: Avalonia 11.4+ may have critical fixes. Project targets net10.0 which may have different compatibility requirements.
- Impact: UI rendering issues, performance problems, accessibility bugs
- Migration plan: Check Avalonia release notes for any security/stability improvements, test on actual net10.0 runtime

**YamlDotNet 16.3.0 Vulnerable?**
- Risk: Older YAML parsers had security issues. 16.3.0 released Jan 2024 - check advisories.
- Impact: Malicious YAML in config files could be exploited
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs` (uses `IDeserializer`)
- Mitigation: Add YAML schema validation (only allow expected keys), or pin to known-safe version

## Missing Critical Features

**No Dry-Run Mode:**
- Problem: Users cannot test configuration without actually cleaning plugins. Mistakes can corrupt plugin files.
- Blocks: Safe testing workflow
- Recommendation: Add `--dry-run` flag to orchestrator, return `CleaningStatus.Skipped` for all plugins without calling xEdit

**No Configuration Validation UI:**
- Problem: Invalid paths or games only discovered during cleaning start, not in Settings screen
- Blocks: Early error detection, user confidence
- Recommendation: Add `ValidateSettingsAsync()` button in Settings tab, show detailed errors for each path

**No Undo/Rollback:**
- Problem: Cleaned plugins cannot be restored from within application
- Blocks: Safety for new users
- Recommendation: Document manual backup approach, or implement backup before cleaning

## Test Coverage Gaps

**ProcessExecutionService Process Termination Edge Cases:**
- What's not tested: Process that ignores `CloseMainWindow()` and requires force `Kill()`, process that dies after `WaitForExitAsync()` but before disposal
- Files: `AutoQAC/Services/Process/ProcessExecutionService.cs` (lines 147-179)
- Risk: Process handle leaks, file locks persisting, next cleaning run fails to start xEdit
- Priority: High - affects core cleaning loop

**Configuration Migration Failure Paths:**
- What's not tested: Legacy file exists but new file also exists (merge scenario), disk is full during migration, permission denied on delete
- Files: `AutoQAC/Services/Configuration/ConfigurationService.cs` (lines 147-207)
- Risk: Config corruption, user data loss, application startup failure
- Priority: High - affects first launch

**Skip List Loading for Unknown GameType:**
- What's not tested: `CleaningOrchestrator` behavior when game type remains Unknown after detection, fallback to local skip lists
- Files: `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` (lines 74-95, 114-131)
- Risk: Unexpected plugins cleaned due to empty skip list
- Priority: Medium - affects safety

**Concurrent State Updates:**
- What's not tested: Multiple rapid `UpdateState()` calls from different tasks, state update during `FinishCleaning()`
- Files: `AutoQAC/Services/State/StateService.cs` (entire class)
- Risk: Race conditions, lost updates, UI display of incorrect progress
- Priority: Medium - affects progress accuracy with high concurrency

**PluginValidationService with Non-Rooted Paths:**
- What's not tested: `ValidatePluginExists()` returns true for relative paths (always), filtering behavior with mixed rooted/non-rooted paths
- Files: `AutoQAC/Services/Plugin/PluginValidationService.cs` (lines 100-116)
- Risk: No actual validation occurs, bad plugins not caught
- Priority: Low - placeholder design, needs redesign before relying on validation

---

*Concerns audit: 2026-02-06*
