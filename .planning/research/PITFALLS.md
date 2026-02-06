# Pitfalls Research

**Domain:** Bethesda plugin cleaning desktop automation (C# Avalonia, xEdit subprocess management)
**Researched:** 2026-02-06
**Confidence:** HIGH (based on codebase analysis + .NET runtime issue tracker + domain knowledge)

## Critical Pitfalls

### Pitfall 1: xEdit Process Outlives Disposal (File Handle Ghost)

**What goes wrong:**
After `ProcessExecutionService.TerminateProcessGracefullyAsync()` completes, the `Process` object is disposed via `using` scope. However, the xEdit process may still hold file handles on the plugin `.esp`/`.esm` file for a brief window. The next plugin in the sequential queue immediately tries to launch xEdit against a different plugin, but xEdit itself may fail or corrupt data because the previous instance's file handles have not yet been released by the OS.

**Why it happens:**
The current implementation waits 2 seconds after `CloseMainWindow()`, then checks `HasExited`, then calls `Kill()` if needed. But `Process.Kill()` is asynchronous at the OS level -- the process object reports `HasExited == true` before the kernel fully releases all file handles. On NTFS, this is especially pronounced with xEdit because it uses memory-mapped files for plugin data. The `dotnet/runtime` issue [#16848](https://github.com/dotnet/runtime/issues/16848) documents this exact race condition: there is no API to atomically "wait-then-kill" without a window where the process exits between the check and the kill call.

Additionally, `CloseMainWindow()` is ineffective when `CreateNoWindow = true` is set in `ProcessStartInfo` (line 64 of `ProcessExecutionService.cs`), because there is no window to close. This means the graceful termination path silently fails every time, falling through to the force kill path after a 2-second delay.

**How to avoid:**
1. After `Kill()` or confirmed exit, call `process.WaitForExit()` (the parameterless overload) to ensure all output streams are flushed and OS handles are released. Be aware of the deadlock pitfall from [dotnet/runtime #51277](https://github.com/dotnet/runtime/issues/51277): the parameterless `WaitForExit()` can deadlock if child processes keep stdout/stderr open. Use the timeout overload `WaitForExit(5000)` as a safety net.
2. Add a post-kill verification delay with file handle polling: after the process exits, attempt to open the plugin file with `FileShare.None` to confirm no handles remain. Retry with exponential backoff up to 5 seconds.
3. Wrap `Kill()` in a try-catch for `InvalidOperationException` (process already exited between check and kill -- the exact race condition documented in the runtime issue).
4. Remove `CreateNoWindow = true` and use `WindowStyle = ProcessWindowStyle.Hidden` instead if `CloseMainWindow()` is the desired graceful shutdown path. Alternatively, drop `CloseMainWindow()` entirely since xEdit launched with `-QAC -autoexit` will self-terminate.

**Warning signs:**
- Intermittent "file in use" errors on the second or third plugin in a cleaning session
- xEdit logs showing "could not open plugin" for plugins that definitely exist
- Tests pass individually but fail when run in sequence (process handle leak between tests)
- Windows Task Manager showing orphaned xEdit processes after the application exits

**Phase to address:**
Process management robustness phase (first priority -- this is a data corruption risk).

---

### Pitfall 2: Backup-Before-Clean Creates False Safety (Incomplete Rollback)

**What goes wrong:**
Implementing "backup plugin before cleaning" by copying the `.esp`/`.esm` file before xEdit runs seems straightforward but fails in several non-obvious ways:
1. xEdit may modify files *other than* the target plugin (e.g., masters, shared FormIDs).
2. If MO2 mode is enabled, the actual file location is inside MO2's virtual filesystem overlay -- the "real" file may be in `overwrite/` or a mod folder, not the game data directory.
3. Restoring a backup after xEdit has already modified the plugin and updated its CRC in the load order can desync the load order management tools (MO2, Vortex, LOOT).
4. The backup folder accumulates silently. With 300+ plugins at 1-50MB each, this is potentially 15GB of backup data per session with no automatic cleanup.

**Why it happens:**
Developers model backup as "copy file, run tool, restore if needed" which works for simple file transformations. xEdit's cleaning is not a simple file transformation -- it interacts with the entire load order context and may touch multiple files depending on master/dependent relationships.

**How to avoid:**
1. Document clearly that backup means "backup the single target plugin file only" and cannot guarantee full rollback of xEdit side effects.
2. For MO2 mode, resolve the actual physical path through MO2's virtual file system before backup. The `PluginInfo.FullPath` placeholder problem (documented in CONCERNS.md) must be fixed first -- backup depends on knowing the real file location.
3. Implement backup as a session-level operation (backup all target plugins before cleaning starts, not one at a time) so that rollback is atomic for the entire session.
4. Add configurable backup retention (e.g., keep last 3 sessions, auto-delete older) with clear disk space warnings.
5. Store backup metadata (original path, file hash, timestamp, game type) alongside the backup so rollback can verify integrity.

**Warning signs:**
- Users reporting "I restored the backup but my game still crashes" (partial rollback)
- Backup folder growing unbounded on disk
- MO2 users seeing backup files in wrong locations (game data vs. mod folder)
- Backup operation failing silently for non-rooted `FullPath` values (the current placeholder issue)

**Phase to address:**
User protection features phase -- but depends on FullPath resolution being fixed first (tech debt phase).

---

### Pitfall 3: Deferred Configuration Saves Lose Data on Crash

**What goes wrong:**
The current `ConfigurationService` writes to YAML on every setting change. The planned deferred/batched saves (dirty-flag + debounce) introduce a window where in-memory state differs from disk state. If the application crashes during cleaning (xEdit subprocess hang, unhandled exception, user force-closes), the deferred writes are lost. This is particularly dangerous for skip list changes made mid-session -- the user believes they added a plugin to the skip list, but the next launch reverts it.

**Why it happens:**
Deferred save is the correct solution for the "rapid toggle causes many disk writes" performance problem. But the naive implementation (dirty flag + timer-based flush) trades write performance for durability. Desktop applications lack the "graceful shutdown guaranteed" property of web servers. Users regularly kill applications via Task Manager, especially when xEdit hangs.

**How to avoid:**
1. Use atomic file writes for the flush: write to a temporary file, then `File.Replace()` to swap atomically. The Win32 `ReplaceFile` API (which `File.Replace()` delegates to) is atomic on NTFS, preventing partial writes. See [dotnet/runtime #18034](https://github.com/dotnet/runtime/issues/18034).
2. Classify changes by criticality: skip list modifications and path changes should flush immediately (user safety). Timeout values and UI preferences can be deferred.
3. Add a pre-cleaning flush: before `CleaningOrchestrator.StartCleaningAsync()` begins, force a synchronous config flush. This ensures the configuration is persisted before any destructive operation.
4. Implement write-ahead logging for critical changes: log the intended change before applying it in memory, so crash recovery can replay the log.
5. On application startup, detect if the last shutdown was unclean (write a "running" flag to a lockfile, clear on clean exit) and warn the user if configuration may be stale.

**Warning signs:**
- Users reporting "my skip list changes disappeared" after a crash
- Configuration file contains stale data after force-close during cleaning
- Test that changes config, simulates crash (no Dispose), and re-reads config shows stale values
- Rapid setting toggling in UI shows no disk writes (means deferred logic is working but durability is gone)

**Phase to address:**
Configuration safety phase -- must be designed alongside the deferred save implementation, not retrofitted.

---

### Pitfall 4: StateService Lock Reentrancy Causes Observable Chain Deadlock

**What goes wrong:**
`StateService.UpdateState()` acquires `_lock`, updates the `BehaviorSubject`, which fires `OnNext()` to all subscribers. If any subscriber (e.g., `MainWindowViewModel.OnStateChanged()`) synchronously calls back into `StateService.UpdateState()` or `CurrentState`, a deadlock occurs because `Lock` (the new .NET 9 `Lock` type) is not reentrant by default.

The current code has a latent form of this: `AddDetailedCleaningResult()` acquires `_lock` to add to `_currentSessionResults`, then calls `AddCleaningResult()` which calls `UpdateState()` which acquires `_lock` again. This works today because the first lock scope ends before the second begins (they are sequential, not nested). But if someone refactors `AddDetailedCleaningResult()` to call `AddCleaningResult()` *inside* the lock, the application deadlocks.

**Why it happens:**
The pattern of "lock + Subject.OnNext()" is inherently dangerous because `OnNext()` executes subscriber callbacks synchronously on the calling thread. Any subscriber that reads `CurrentState` (which also acquires the lock) will deadlock. The `Lock` class in .NET 9 is explicitly non-reentrant (unlike `Monitor`/`lock(object)` which is reentrant), making this especially fragile.

**How to avoid:**
1. Never call `Subject.OnNext()` inside a lock. Instead, compute the new state inside the lock, exit the lock, then fire `OnNext()`. This requires a two-step pattern:
   ```csharp
   AppState newState;
   lock (_lock) { newState = updateFunc(_stateSubject.Value); }
   _stateSubject.OnNext(newState);
   ```
2. Replace `Lock` with `ReaderWriterLockSlim` for the common read-heavy pattern (many `CurrentState` reads, few writes).
3. Add a reentrancy guard in debug builds that throws if `UpdateState` is called while already inside `UpdateState`.
4. Consider moving to a fully reactive pattern: use `BehaviorSubject.OnNext()` without any lock, making the subject the sole owner of state. `CurrentState` reads from the subject's `Value` property (already thread-safe for reads).

**Warning signs:**
- UI freezes during cleaning progress updates
- Debugger shows thread blocked on `Lock.Enter()` in `StateService`
- Adding a new observable subscription in a ViewModel causes intermittent hangs
- Unit tests for concurrent state updates deadlock (the test gap flagged in CONCERNS.md)

**Phase to address:**
Process management robustness phase (concurrent state is foundational -- fix before adding more observable chains).

---

### Pitfall 5: CancellationTokenSource Race Between StopCleaning and Finally Block

**What goes wrong:**
In `CleaningOrchestrator`, `_cleaningCts` is created in `StartCleaningAsync()` (line 140) and disposed in the `finally` block (line 269-274). `StopCleaning()` calls `_cleaningCts?.Cancel()` under `_ctsLock` (line 280-283). If the user clicks "Stop" at exactly the moment the finally block runs, the following race occurs:
1. `StopCleaning()` acquires `_ctsLock`, reads `_cleaningCts` (non-null), calls `Cancel()`
2. Meanwhile, `finally` block acquires `_ctsLock`, calls `Dispose()`, sets `_cleaningCts = null`
3. If step 1's `Cancel()` call is in-flight when step 2's `Dispose()` runs, the CTS is disposed while being used

The lock prevents simultaneous access, but does not prevent the `Cancel()` callback (registered in `ProcessExecutionService`) from running *after* `Dispose()` since cancellation callbacks run asynchronously on the thread pool.

**Why it happens:**
`CancellationTokenSource.Dispose()` does not wait for all registered callbacks to complete. If a callback is queued but not yet executed when `Dispose()` runs, the callback may access disposed state. This is a documented but frequently overlooked behavior.

**How to avoid:**
1. Do not dispose `CancellationTokenSource` in the finally block. Let it be garbage collected. CTS disposal is optional for short-lived tokens (it mainly releases the internal timer for timeout-based CTS).
2. If disposal is required (e.g., linked CTS to avoid memory leaks), add a delay after cancellation before disposal:
   ```csharp
   _cleaningCts?.Cancel();
   await Task.Delay(100); // Allow callbacks to complete
   _cleaningCts?.Dispose();
   ```
3. Use `Interlocked.Exchange` instead of lock for the null-check pattern, which is both faster and avoids the lock ordering problem.
4. Add a `_isDisposing` flag checked by `StopCleaning()` to short-circuit if disposal is in progress.

**Warning signs:**
- `ObjectDisposedException` logged during cleaning cancellation
- "Stop" button appears to do nothing on rare occasions (cancellation lost)
- Process continues running after user clicks stop (cancellation token not propagated)
- Sporadic `TaskCanceledException` with disposed inner token

**Phase to address:**
Process management robustness phase (fix alongside process termination improvements).

---

### Pitfall 6: Dry-Run Mode Gives False Confidence About Real Cleaning

**What goes wrong:**
A dry-run mode that skips xEdit execution but validates configuration can mislead users into believing their setup is correct when it is not. Specifically:
1. Dry-run validates file paths exist but cannot verify xEdit version compatibility
2. Dry-run cannot detect that xEdit will hang on a specific plugin (timeout issues)
3. Dry-run cannot verify MO2 virtual filesystem resolution -- MO2's `run` command modifies the environment, so a plugin that exists in MO2's virtual view may not be found without the MO2 wrapper
4. If dry-run simply returns `CleaningStatus.Skipped` for all plugins (as CONCERNS.md recommends), the results window shows "all skipped" which looks identical to a skip-list misconfiguration

**Why it happens:**
Dry-run is conceptually "do everything except the destructive part." But in this domain, the destructive part (xEdit execution) is also the validation part -- you only learn if a plugin can be cleaned by actually trying to clean it. The gap between "config looks valid" and "cleaning will succeed" is large.

**How to avoid:**
1. Clearly label dry-run results as "validation results" not "cleaning results." Use a distinct status like `CleaningStatus.DryRun` rather than reusing `Skipped`.
2. In dry-run mode, still verify: (a) xEdit executable runs with `--version` or similar, (b) each plugin file is accessible and not locked, (c) game data folder is readable, (d) MO2 integration is functional by running a no-op MO2 command.
3. Show a distinct UI for dry-run results -- not the same `CleaningResultsWindow`. Use a validation report format with pass/fail per check, not per plugin.
4. Document what dry-run does NOT verify (xEdit plugin compatibility, actual cleaning outcomes).

**Warning signs:**
- Users running dry-run, seeing "success," then having real cleaning fail
- Bug reports saying "dry-run said everything was fine but cleaning failed"
- Test suite testing dry-run with mocked services that do not reflect real xEdit behavior

**Phase to address:**
User protection features phase -- design dry-run UI and semantics carefully before implementing.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| `PluginInfo.FullPath = FileName` placeholder | Unblocks load order parsing without game data path resolution | Every downstream consumer (backup, validation, dry-run) must handle two code paths; backup feature cannot work at all | Never -- this must be resolved before adding any feature that depends on knowing real file locations |
| `MainWindowViewModel` at 904 lines handling all concerns | Single ViewModel simplifies DI wiring and view resolution | Every new feature adds more lines; testing requires mocking 8+ services; merge conflicts on every branch | Acceptable during initial development, must split before adding dry-run or backup UI |
| `_mainConfigCache` never invalidated | Fast config reads after first load | External YAML edits (by user or other tools) are invisible until restart; `FileSystemWatcher` addition will require cache invalidation plumbing | Acceptable if app is the sole writer; becomes a bug when FileSystemWatcher is added |
| Swallowing exceptions in `TerminateProcessGracefullyAsync` | Prevents crashes during cleanup | Process termination failures are silently ignored; orphaned xEdit processes accumulate without user awareness | Never -- at minimum log at Warning level and surface to user if process count exceeds 1 |
| `Lock` instead of `ReaderWriterLockSlim` in StateService | Simpler code, fewer concurrency primitives | Read-heavy access pattern (UI polls state frequently) is unnecessarily serialized; potential deadlock with `Lock`'s non-reentrant semantics | Acceptable only if observable pattern is used exclusively for reads (no direct `CurrentState` access from UI thread) |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| xEdit subprocess (`-QAC` mode) | Assuming non-zero exit code always means failure. xEdit returns non-zero for some "success with warnings" scenarios (e.g., "no ITMs found") | Parse output lines for specific patterns ("Removing:", "Undeleting:") to determine actual cleaning outcome; treat exit code as secondary signal |
| xEdit with `CreateNoWindow = true` | `CloseMainWindow()` silently fails because there is no window handle. The 2-second grace period is wasted every time | Either use `GenerateConsoleCtrlEvent` (Win32 P/Invoke) to send Ctrl+C, or skip graceful termination and go straight to `Kill()` with post-kill handle verification |
| MO2 wrapper execution | Building the command as a single string with nested quotes. MO2's `-a` argument parsing is fragile with escaped quotes | Use the exact quoting pattern from the Python reference implementation; test with plugin names containing spaces, apostrophes, and parentheses (common in Bethesda mods) |
| Mutagen plugin loading | Assuming `GameEnvironment.Typical.Builder()` always succeeds. It throws if the game is not installed or registry keys are missing | Wrap in try-catch, fall back to file-based loading, and log which detection method was used so users can debug |
| YAML configuration (YamlDotNet) | Load-modify-save pattern with `LoadUserConfigAsync()` then `SaveUserConfigAsync()` is not atomic. Concurrent UI changes can cause lost writes | Hold the lock for the entire load-modify-save cycle, or switch to a command-based mutation pattern where individual property changes are queued |
| FileSystemWatcher for config monitoring | Reacting to every `Changed` event immediately. Windows fires multiple events for a single file save (Create temp, Write, Rename, Delete old) | Debounce FSW events with a 500ms window; use `NotifyFilters.LastWrite` only; verify file is readable (not locked) before processing |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Immutable `AppState` record allocation per plugin | GC pauses during long cleaning sessions | Profile with 500+ plugin sessions; consider batch state updates or mutable state with lock | 200+ plugins at rapid progress update rate |
| `HashSet` copy-on-update in `AddCleaningResult` | Three new HashSets allocated per plugin cleaned (CleanedPlugins, FailedPlugins, SkippedPlugins) | Use `ImmutableHashSet<T>.Add()` which shares structure, or accumulate in a mutable collection and snapshot at session end | 500+ plugins with per-plugin state updates |
| `ObservableCollection.Clear()` then bulk `Add()` in `OnStateChanged` | UI flickers on every state change; triggers N collection-changed events | Use `PluginsToClean = new ObservableCollection<>(source)` for single replacement, or use `DynamicData` library for efficient observable collection diffing | Any state change while plugin list is visible |
| Per-plugin config file read in `CleaningOrchestrator` | `LoadUserConfigAsync()` called once per cleaning session start, but `GetSkipListAsync()` calls it again, reading the YAML file each time | Cache user config for the duration of a cleaning session; reload only on explicit user action or FSW notification | Noticeable lag with large YAML configs or slow disk |
| `Regex` in `XEditOutputParser` per-line evaluation | Already optimized with `GeneratedRegex` (AOT compiled) -- not an actual trap | N/A -- current implementation is correct | N/A |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Plugin filenames interpolated directly into command-line arguments without sanitization | A maliciously named plugin file (e.g., `mod.esp" & calc.exe & "`) could inject arbitrary commands if the quoting in `XEditCommandBuilder` is broken | Validate plugin filenames against a strict allowlist pattern (`^[\w\s\-\.\(\)\[\]']+\.(esp\|esm\|esl)$`); reject filenames with shell metacharacters |
| YAML deserialization with `IgnoreUnmatchedProperties()` but no schema validation | A crafted YAML file could contain unexpected keys that map to properties via deserialization reflection, potentially overwriting internal state | Add explicit YAML schema validation; use `[YamlIgnore]` on properties that should not be deserialized from file; consider `Safe` deserializer mode |
| xEdit executable path user-configurable with no verification | User could point to a malicious executable that mimics xEdit's output patterns | Verify xEdit binary signature or filename pattern at minimum; warn if the executable is not in a standard game tools directory; never execute with elevated privileges |
| Log files contain full filesystem paths | Attacker with access to logs learns directory structure, game installation locations, username (from path) | Redact or hash the user-specific portions of paths in log output; ensure log directory permissions are user-only |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Dry-run results shown in same window as real cleaning results | Users cannot distinguish "validated OK" from "actually cleaned." Confusion leads to either false confidence or unnecessary re-cleaning | Use a distinct validation report view; different colors/icons; clear "This was a dry run -- no files were modified" banner |
| Backup feature creates files but provides no UI to browse/restore them | Users forget backups exist; disk fills silently; restoration requires manual file copy | Add a "Backup Manager" view showing sessions, sizes, and one-click restore per plugin |
| Stop button during cleaning triggers cancellation but xEdit process may continue for seconds | User sees "Stopping..." but xEdit window remains open (if visible) or CPU stays pegged. User clicks Stop again, or force-closes the app | Show "Terminating xEdit process..." with a spinner; disable Stop button during termination; show "Force Kill" button after 5 seconds if still running |
| Skip list changes not immediately reflected in plugin list | User adds plugin to skip list, returns to main window, still sees plugin in the list until they refresh | Auto-refresh plugin list on skip list change (the subscription exists but verify it works for all code paths including DisableSkipLists toggle) |
| Configuration errors only shown when cleaning starts | User configures paths incorrectly, only discovers the error after clicking "Start Cleaning" and waiting for validation | Add inline validation indicators next to each path field; show checkmark/X for xEdit path, load order, MO2 path in real time |

## "Looks Done But Isn't" Checklist

- [ ] **Process termination:** Often missing post-kill handle verification -- test by killing xEdit mid-write and verifying the next plugin can launch
- [ ] **Backup implementation:** Often missing MO2 virtual path resolution -- backup copies the wrong file or copies from game data instead of MO2 mod folder
- [ ] **Deferred config saves:** Often missing crash-recovery flush -- simulate crash (kill app process) after changing settings but before timer fires, verify settings persisted
- [ ] **Dry-run mode:** Often missing MO2 integration test -- dry-run that validates paths without MO2 wrapper will show valid paths that fail under real MO2 execution
- [ ] **FileSystemWatcher integration:** Often missing debounce -- test by rapidly saving config file in external editor, verify app doesn't crash or show stale data
- [ ] **ViewModel split:** Often missing interaction re-wiring -- after extracting sub-ViewModels from MainWindowViewModel, verify all Interactions (progress, results, settings) still route correctly through the view hierarchy
- [ ] **Test coverage for cancellation:** Often missing the "cancel during process startup" path -- CancellationToken fires between `Process.Start()` and `BeginOutputReadLine()`, leaving process running with no output capture
- [ ] **Skip list with DisableSkipLists toggle:** Often missing the "toggle during cleaning" edge case -- if user could somehow toggle DisableSkipLists mid-session, remaining plugins use different skip logic

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| xEdit process ghost holds file handles | LOW | Kill orphaned xEdit processes via Task Manager; retry cleaning the affected plugin. Add process cleanup on app startup that checks for orphaned xEdit instances |
| Backup restores wrong file (MO2 path mismatch) | MEDIUM | User must manually locate correct backup and restore to correct MO2 mod folder. Verify plugin integrity with xEdit's "Check for Errors" mode |
| Config lost due to crash during deferred save | LOW | Re-apply settings manually. If write-ahead log exists, auto-recover on next startup |
| StateService deadlock | HIGH | Force-kill application. No data loss if cleaning was not in progress. If cleaning was in progress, affected plugin may be partially cleaned (corrupted) |
| CancellationTokenSource disposed during callback | LOW | Restart application. No data loss -- the cancellation was racing with cleanup, and the process was already terminating |
| Dry-run false positive | MEDIUM | User must verify each path manually. Real cleaning may fail on first plugin, requiring config correction before re-running |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| xEdit process ghost (file handle race) | Process Management Robustness | Integration test: clean 10 plugins sequentially, verify no orphaned handles between runs. Add process count assertion. |
| Backup path resolution (MO2 mismatch) | User Protection Features (depends on FullPath fix in Tech Debt) | Test with MO2 mock: backup plugin, verify backup location matches MO2's actual mod folder path |
| Deferred save crash durability | Configuration Safety | Test: change setting, kill process without Dispose, restart, verify setting persisted. Automate with xUnit `IAsyncLifetime` |
| StateService lock reentrancy | Process Management Robustness (foundational) | Unit test: subscribe to StateChanged, in callback call UpdateState, verify no deadlock (with timeout-based test failure) |
| CancellationTokenSource race | Process Management Robustness | Stress test: start cleaning, call StopCleaning in tight loop from background thread, verify no ObjectDisposedException |
| Dry-run false confidence | User Protection Features | Acceptance test: configure dry-run with intentionally broken xEdit path, verify dry-run correctly reports the specific failure mode |
| FileSystemWatcher double-fire | Configuration Safety | Test: write config file, count FSW events over 2 seconds, verify debounce reduces to single callback |
| MainWindowViewModel size (904 lines) | Tech Debt Reduction | After split: verify each extracted ViewModel has <300 lines; verify all existing ViewModel tests still pass; verify no interaction routing broken |
| PluginInfo.FullPath placeholder | Tech Debt Reduction (prerequisite for backup) | Test: load plugins, verify every PluginInfo.FullPath is rooted and File.Exists returns true |
| Config load-modify-save not atomic | Configuration Safety | Concurrent test: two tasks both call UpdateSkipListAsync simultaneously, verify no entries are lost |

## Sources

- [dotnet/runtime #16848: Process doesn't allow "wait and kill" without a race](https://github.com/dotnet/runtime/issues/16848) -- HIGH confidence, primary .NET source
- [dotnet/runtime #51277: Process.WaitForExit() deadlock with child processes](https://github.com/dotnet/runtime/issues/51277) -- HIGH confidence, primary .NET source
- [dotnet/runtime #18034: File.Replace() as atomic API](https://github.com/dotnet/runtime/issues/18034) -- HIGH confidence, primary .NET source
- [ReactiveUI Testing Handbook](https://www.reactiveui.net/docs/handbook/testing) -- HIGH confidence, official ReactiveUI docs
- [ReactiveUI Commands Handbook](https://www.reactiveui.net/docs/handbook/commands/) -- HIGH confidence, documents ThrownExceptions behavior
- [Tome of xEdit: Cleaning and Error Checking](https://tes5edit.github.io/docs/7-mod-cleaning-and-error-checking.html) -- HIGH confidence, official xEdit documentation
- [XEdit-PACT (Python reference)](https://github.com/GuidanceOfGrace/XEdit-PACT) -- HIGH confidence, upstream reference implementation
- [Microsoft Learn: FileSystemWatcher](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-io-filesystemwatcher) -- HIGH confidence, official .NET docs
- Codebase analysis of `AutoQAC/Services/Process/ProcessExecutionService.cs`, `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`, `AutoQAC/Services/State/StateService.cs`, `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/ViewModels/MainWindowViewModel.cs` -- HIGH confidence, direct source code inspection
- `.planning/codebase/CONCERNS.md` -- HIGH confidence, existing known issues documentation

---
*Pitfalls research for: Bethesda plugin cleaning desktop automation (AutoQACSharp)*
*Researched: 2026-02-06*
