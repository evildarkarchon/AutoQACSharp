# T13: 11-user-facing-diagnostics-boundaries 13

**Slice:** S12 — **Milestone:** M001

## Description

Close the Phase 11 SEC-01 restore metadata display gap where Restore Selected confirmation, status, and error text can render raw `BackupPluginEntry.FileName` from editable backup metadata.

Purpose: backup session metadata is untrusted display input; restore operations must preserve original metadata for service safety while projecting safe copy to users.
Output: executable regression tests plus safe restore-selected display formatting in `RestoreViewModel`.

## Must-Haves

- [ ] "Restore Selected confirmation, progress/status, and error dialog text use sanitized backup metadata filenames per D-07 and D-09."
- [ ] "Restore service calls continue to receive the original BackupPluginEntry and trusted restore root; only user-facing display strings are sanitized."
- [ ] "Restore metadata regressions fail if path, command, control-character, exception, or stack sentinels appear in user-facing restore copy."

## Files

- `AutoQAC/ViewModels/RestoreViewModel.cs`
- `AutoQAC.Tests/ViewModels/RestoreViewModelTests.cs`
