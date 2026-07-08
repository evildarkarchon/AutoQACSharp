---
phase: 07-backup-restore-retention-safety
reviewed: 2026-04-29T13:42:00Z
depth: deep
files_reviewed: 11
files_reviewed_list:
  - AutoQAC/Views/MainWindow.axaml.cs
  - AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs
  - AutoQAC/Services/Backup/BackupPathContainment.cs
  - AutoQAC/Services/Backup/BackupService.cs
  - AutoQAC.Tests/Services/Backup/BackupPathContainmentTests.cs
  - AutoQAC/Models/BackupOperationResults.cs
  - AutoQAC/Services/Backup/IBackupService.cs
  - AutoQAC/ViewModels/RestoreViewModel.cs
  - AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs
  - AutoQAC.Tests/Services/BackupServiceTests.cs
  - AutoQAC/AutoQAC.csproj
findings:
  critical: 0
  warning: 3
  info: 4
  total: 7
status: issues_found
---

# Phase 07: Code Review Report (Wave 9 incremental)

**Reviewed:** 2026-04-29T13:42:00Z
**Depth:** deep
**Files Reviewed:** 11
**Status:** issues_found

## Summary

This is the second pass on Phase 07, scoped to wave-9 deltas from plans 07-12 (defense-in-depth `ProgressViewModel.CloseRequested` + disposal wiring), 07-13 (service-layer `IBackupService.DeleteSessionAsync` + `RestoreViewModel` MVVM cleanup), and 07-14 (shared `BackupPathContainment.IsContained` helper). All three blocking findings from the first review (CR-01 restore-canceled-status, CR-02 normal progress lifecycle, CR-03 unvalidated `Directory.Delete` in ViewModel) are resolved in this pass.

The implementation is functionally correct against the seven attention points called out in the scope note: the containment helper swallows only the four documented exception types, trailing-separator handling defeats the sibling-prefix collision, `DeleteSessionAsync` propagates `OperationCanceledException`, `CanDeleteSession` correctly gates on both `_backupRoot` and `HasTrustedRestoreRoot`, the ViewModel no longer calls `Directory.Delete`, the dual-subscription lifecycle is idempotent, and no parallel constructs were introduced under `AutoQAC/Services/Cleaning`.

The remaining findings are non-blocking: a stale XML doc reference, an unused alternative-overload constructor pathway that bypasses the new `_sessionDeleter`, a minor cancellation-ordering inconsistency, plus a few smaller quality items.

## Warnings

### WR-01: WARNING — `BackupService(ILoggingService)` convenience constructor bypasses the deleter seam

**File:** `AutoQAC/Services/Backup/BackupService.cs:43-46`

**Issue:** The single-argument convenience constructor (`public BackupService(ILoggingService logger) : this(new BackupFileCopier(logger), logger)`) chains to the **two-argument** ctor on line 32. Because the chained call passes only two positional arguments, the optional `IBackupSessionDeleter? sessionDeleter = null` parameter defaults to `null`, which the primary ctor body then replaces with `new DirectoryBackupSessionDeleter()`. That resolves correctly today, but the call chain is fragile: if a future contributor reorders the two-argument ctor's parameter list (e.g., adds a non-optional dependency before `sessionDeleter`), this convenience ctor will silently bind the new dependency to `null`/wrong slot. There is no test that exercises the single-argument constructor path with `DeleteSessionAsync`, so a regression there would not be caught.

**Fix:** Either remove the convenience constructor (DI in `ServiceCollectionExtensions` already wires the three-arg form), or have it forward explicitly:

```csharp
public BackupService(ILoggingService logger)
    : this(new BackupFileCopier(logger), logger, sessionDeleter: null)
{
}
```

Then add a `BackupService_SingleArgConstructor_UsesDirectoryBackupSessionDeleter` test that calls `DeleteSessionAsync` on an instance built via the one-arg ctor and asserts the contained-deletion path still works.

### WR-02: WARNING — `DeleteSessionAsync` does not call `ct.ThrowIfCancellationRequested()` before validation

**File:** `AutoQAC/Services/Backup/BackupService.cs:420-468`

**Issue:** Every other cancellable async method in `BackupService` (`CleanupOldSessionsAsync` line 353, `RestoreSessionAsync` line 283, `GetBackupSessionsAsync` line 189, `DirectoryBackupSessionDeleter.DeleteAsync` line 15) checks `ct.IsCancellationRequested` or `ct.ThrowIfCancellationRequested()` before doing any work. `DeleteSessionAsync` does not — it runs `string.IsNullOrWhiteSpace`, `BackupPathContainment.IsContained`, the warning-log call, and only then enters the `try` block whose `_sessionDeleter.DeleteAsync` call observes `ct`. If a caller passes an already-canceled token, the validation runs anyway and the warning log can still fire ("Rejected backup session delete outside backup root..."), even though the user wanted no work performed. The current ViewModel call site passes `CancellationToken.None`, so this is not exploitable today, but the contract advertised by the XML doc on `IBackupService.DeleteSessionAsync` ("Cancellation token observed before deletion begins") is stronger than what the implementation delivers.

**Fix:** Add `ct.ThrowIfCancellationRequested();` as the first line of `DeleteSessionAsync`, before the null/whitespace check, mirroring `DirectoryBackupSessionDeleter.DeleteAsync`. Add a `DeleteSessionAsync_AlreadyCanceledToken_ThrowsBeforeValidation` test using a pre-canceled `CancellationTokenSource` to lock the contract.

### WR-03: WARNING — Stale XML doc reference in `BackupService.IsRestoreTargetInsideTrustedRoot`

**File:** `AutoQAC/Services/Backup/BackupService.cs:698-709`

**Issue:** The XML summary on the delegating wrapper says: "Delegates to `BackupPathContainment.IsContained` so the canonical containment policy is shared with `RestoreViewModel.DeleteSessionAsync` (Plan 07-13) and any future delete/restore safety boundaries." After Plan 07-13 landed, `RestoreViewModel.DeleteSessionAsync` does **not** call `BackupPathContainment.IsContained` directly — it calls `_backupService.DeleteSessionAsync(...)`, which then delegates to the helper. The doc comment promises a coupling that does not exist and may mislead a future contributor into thinking `RestoreViewModel` still consumes the helper directly. Per the project comment policy, this comment is now wrong and should be either deleted (with the change called out in the reply) or rewritten.

**Fix:** Update the comment to:

```csharp
/// Delegates to <see cref="BackupPathContainment.IsContained"/> so the canonical containment policy
/// is shared with <see cref="DeleteSessionAsync"/> (Plan 07-13) and any future delete/restore
/// safety boundaries.
```

That keeps the historical Plan 07-13 reference correct (the service-layer method, which is where containment is consumed) without falsely implying that the ViewModel touches the helper.

## Info

### IN-01: INFO — `EnsureTrailingDirectorySeparator` exists in two places

**File:** `AutoQAC/Services/Backup/BackupPathContainment.cs:61-62` and `AutoQAC/Services/Backup/BackupService.cs:723-724`

**Issue:** Plan 07-14 left an identical `EnsureTrailingDirectorySeparator` private helper in `BackupService.cs` because `ValidateBackupDestination` (line 554) and `ValidateRestoreEntry` (line 631) still need it for **session-root** containment, distinct from trusted-restore-root containment. The 07-14 SUMMARY explicitly documents this as out of scope. Future hardening should consider unifying session-root containment under `BackupPathContainment.IsContained` so a single helper owns the trailing-separator policy. Not a wave-9 regression.

**Fix:** Track in a future plan; no immediate change.

### IN-02: INFO — `ToString("MMM d, yyyy h:mm tt")` is repeated three times in `RestoreViewModel`

**File:** `AutoQAC/ViewModels/RestoreViewModel.cs:109, 313, 523`

**Issue:** The static helper `FormatSessionTimestamp` already exists at line 523 and is used in `RestorePluginAsync` and `RestoreAllAsync`. `OnSelectedSessionChanged` (line 109) and `DeleteSessionAsync` (line 313) inline the same `ToString` format string instead of calling the helper. If the locale or format ever changes, three call sites must be updated together.

**Fix:** Replace the two inlined format strings with `FormatSessionTimestamp(value.Timestamp)` / `FormatSessionTimestamp(session.Timestamp)`.

### IN-03: INFO — `RestoreViewModel.DeleteSessionAsync` does not log the canonical out-of-root rejection in `Information` form

**File:** `AutoQAC/ViewModels/RestoreViewModel.cs:338-347`

**Issue:** `BackupService.DeleteSessionAsync` already logs a `Warning` with `BackupRoot` and `SessionDirectory` when it rejects an out-of-root session (line 439-442). The ViewModel branch (line 338-347) does not emit any additional log entry — only `StatusText` and `ShowErrorAsync`. This is consistent with the D-04 "concise reason" pattern and is intentional, but the lack of a ViewModel-side audit log means a user reproducing the rejection from the UI has only the service-level warning to correlate against. Acceptable as designed; flagging only as documentation.

**Fix:** None; design choice.

### IN-04: INFO — `MainWindowValidationPanel_ShouldRenderValidationErrorMessage` is unrelated to the rest of the file

**File:** `AutoQAC.Tests/Views/ViewSubscriptionLifecycleTests.cs:8-17`

**Issue:** The first test in `ViewSubscriptionLifecycleTests` asserts that `MainWindow.axaml` renders an inline validation error binding (`Text="{Binding Message}"`). It does not test subscription lifecycle. This was pre-existing before wave 9 and not in scope, but its presence makes the file's class name slightly misleading. Consider moving to a separate `MainWindowValidationTests` class.

**Fix:** None required; cosmetic suggestion.

---

## Verification of scope-note attention points

| # | Concern | Status |
|---|---------|--------|
| 1 | `BackupPathContainment.IsContained` swallows only `ArgumentException`, `IOException`, `NotSupportedException`, `UnauthorizedAccessException` | **OK** — Line 48 catch filter matches exactly the four documented types; `OutOfMemoryException`, `StackOverflowException`, and unknown exceptions propagate. |
| 2 | Trailing-separator handling rejects sibling-prefix paths | **OK** — `EnsureTrailingDirectorySeparator` (line 61-62) appends `Path.DirectorySeparatorChar` if not present; `BackupPathContainmentTests.IsContained_SiblingPrefixCollision_ReturnsFalse` and `BackupServiceTests.DeleteSessionAsync_SiblingPrefixSession_ReturnsRejected` lock the behavior. |
| 3 | `DeleteSessionAsync` propagates `CancellationToken` and `OperationCanceledException` | **OK** — Line 450 passes `ct` to `_sessionDeleter.DeleteAsync`; line 454-459 explicitly rethrows `OperationCanceledException` so it is distinguishable from `Failed`. (See WR-02 for the upfront-cancel improvement.) |
| 4 | `CanDeleteSession` gates on null/empty/whitespace `_backupRoot` AND missing trusted root | **OK** — Line 298-302: `SelectedSession != null && !string.IsNullOrWhiteSpace(_backupRoot) && HasTrustedRestoreRoot && !IsRestoreActive`. `LoadSessionsAsync` calls `DeleteSessionCommand.NotifyCanExecuteChanged()` in **both** branches (line 131, 139). |
| 5 | `RestoreViewModel.DeleteSessionAsync` no longer calls `Directory.Delete` | **OK** — `grep "Directory\\.Delete\|System\\.IO" AutoQAC/ViewModels/RestoreViewModel.cs` returns 0 matches. Filesystem work routes through `_backupService.DeleteSessionAsync` (line 326). |
| 6 | Defense-in-depth wiring is idempotent and the comment explains why | **OK** — `MainWindow.axaml.cs:142-145` comment is accurate; local `progressDisposed` guard (line 146) and `ProgressWindow._disposeHandled` (line 10) cooperate. `ProgressViewModel.Dispose` is also itself idempotent because `_subscriptions.Clear()` after the first call leaves the second call iterating over an empty collection. Avalonia `Window.Close()` is idempotent. |
| 7 | No new parallel constructs in `AutoQAC/Services/Cleaning` | **OK** — `grep` for `Task\.WhenAll|Parallel\.ForEachAsync|Task\.Run` under `AutoQAC/Services/Cleaning` returns 0 matches. |
| 8 | XML doc comments on new methods | **MOSTLY OK** — `BackupPathContainment.IsContained`, `BackupService.DeleteSessionAsync`, `IBackupService.DeleteSessionAsync`, `CanDeleteSession`, the `RestoreProgressReporter.Report`, `BackupSessionDeleteResult` record, and `BackupSessionDeleteStatus` enum members all carry XML docs. WR-03 flags one stale doc that needs an update; otherwise comment policy is satisfied. |

## Tests reviewed

- `BackupPathContainmentTests` — 11 tests, one per RED case from Plan 07-14. Coverage of `IsContained` is comprehensive: null/empty/whitespace candidate (3), null/empty/whitespace root (3), valid containment, traversal-normalization escape, sibling-prefix collision, case-insensitive containment, malformed path. Per-instance temp root with `Guid.NewGuid()` keeps tests isolated under xUnit parallelism.
- `BackupServiceTests.DeleteSessionAsync_*` — 6 new tests covering null/empty/whitespace `backupRoot`, outside-root session, sibling-prefix session, traversal-normalized escape, contained deletion (real temp dir + recording deleter), and `IOException` failure. The `ThrowingBackupSessionDeleter` helper closes the gap that the `RecordingBackupSessionDeleter` could not.
- `RestoreViewModelTests` — 5 new `DeleteSessionCommand_*` tests (unsafe outside, sibling-prefix, parent traversal, null `_backupRoot` `[Theory]`, normal contained, service-failed) + the extension to `RestoreCommands_DisabledWhenTrustedRestoreRootMissing` asserting `DeleteSessionCommand.CanExecute(null).Should().BeFalse()`. The `CreateLoadedViewModelAsync` helper deduplicates setup correctly. A small gap: no explicit ViewModel test for the **`Deleted` status with prior selected-session preservation** (e.g., the test asserts `vm.SelectedSession.Should().BeNull()` after delete, but does not assert the post-delete `Sessions` count or `OnPropertyChanged(nameof(HasSessions))` raise event). The behavioral assertion via `vm.HasSessions.Should().BeFalse()` is sufficient indirectly.
- `ViewSubscriptionLifecycleTests.MainWindowShowProgressAsync_ShouldWireProgressWindowCloseAndDisposal` — six regex-tolerant assertions plus the literal `"Defense in depth"` substring assertion. Correctly tolerant of method-group vs lambda syntax and alternate guard variable names.

---

_Reviewed: 2026-04-29T13:42:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
