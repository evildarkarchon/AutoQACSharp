---
phase: 02-plugin-pipeline-robustness
plan: 01
subsystem: plugin-validation
tags: [parsing, encoding, BOM, FullPath, validation-pipeline]
dependency-graph:
  requires: []
  provides: [encoding-aware-parsing, fullpath-resolution, validate-plugin-file, plugin-warning-kind]
  affects: [02-02, phase-5-backup]
tech-stack:
  added: []
  patterns: [StreamReader-BOM-detection, line-validation-pipeline, single-code-path-validation]
key-files:
  created: []
  modified:
    - AutoQAC/Models/PluginInfo.cs
    - AutoQAC/Services/Plugin/IPluginValidationService.cs
    - AutoQAC/Services/Plugin/PluginValidationService.cs
    - AutoQAC/Services/Plugin/IPluginLoadingService.cs
    - AutoQAC/Services/Plugin/PluginLoadingService.cs
    - AutoQAC.Tests/Services/PluginValidationServiceTests.cs
    - AutoQAC.Tests/Services/PluginLoadingServiceTests.cs
    - AutoQAC.Tests/ViewModels/ErrorDialogTests.cs
    - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
decisions:
  - id: 02-01-01
    description: "StreamReader with detectEncodingFromByteOrderMarks replaces File.ReadAllLinesAsync for BOM auto-detection"
    rationale: "Handles UTF-8, UTF-16 LE/BE, UTF-32 BOMs natively without custom parsing"
  - id: 02-01-02
    description: "7-step line validation pipeline (blanks, comments, prefix strip, control chars, path separators, extension check)"
    rationale: "Ordered from cheapest to most expensive check; each step has clear skip semantics"
  - id: 02-01-03
    description: "ValidatePluginFile returns PluginWarningKind enum instead of bool"
    rationale: "Enables callers to provide specific error messages per warning type"
  - id: 02-01-04
    description: "Non-rooted paths return NotFound from ValidatePluginFile (not optimistic true)"
    rationale: "Eliminates dual code path; forces callers to provide absolute paths before validation"
metrics:
  duration: 6m 32s
  completed: 2026-02-06
---

# Phase 2 Plan 1: Plugin Line Validation and FullPath Resolution Summary

Encoding-aware load order parsing with 7-step line validation pipeline and dataFolderPath-based FullPath resolution, replacing File.ReadAllLinesAsync and eliminating the dual-path ValidatePluginExists.

## What Changed

### PluginInfo Model
- Added `PluginWarningKind` enum: None, NotFound, Unreadable, ZeroByte, MalformedEntry, InvalidExtension
- Added `Warning` property (defaults to None) for downstream consumers to inspect validation results

### IPluginValidationService Interface
- `GetPluginsFromLoadOrderAsync` gains `string? dataFolderPath = null` parameter
- `ValidatePluginExists(PluginInfo) -> bool` replaced by `ValidatePluginFile(PluginInfo) -> PluginWarningKind`

### PluginValidationService Implementation
- **Encoding**: `StreamReader(path, detectEncodingFromByteOrderMarks: true)` auto-detects UTF-8 BOM, UTF-16 LE/BE, UTF-32
- **Line pipeline**: 7 ordered steps:
  1. Skip blank/whitespace
  2. Skip comment lines (# prefix)
  3. Strip prefix chars (*, +, -)
  4. Skip empty after prefix (separators)
  5. Reject control characters (char < 0x20)
  6. Reject path separators (\ or /)
  7. Validate .esp/.esm/.esl extension
- **FullPath**: `Path.Combine(dataFolderPath, fileName)` when dataFolderPath provided; falls back to fileName
- **ValidatePluginFile**: Single code path -- non-rooted returns NotFound, then existence, zero-byte, and readability checks

### IPluginLoadingService / PluginLoadingService
- `GetPluginsFromFileAsync` gains `string? dataFolderPath = null` passthrough parameter

### Test Suite
- 25 tests in PluginValidationServiceTests covering all edge cases (BOM, blank lines, comments, separators, invalid extensions, path separators, control chars, FullPath resolution, ValidatePluginFile)
- All existing mock setups updated for 3-parameter `GetPluginsFromLoadOrderAsync` signature
- Total: 451 tests pass (was 428)

## Task Commits

| Task | Name | Commit | Type |
|------|------|--------|------|
| 1 | Write failing tests for plugin line validation and FullPath resolution | 050d467 | test |
| 2 | Implement encoding-aware parsing, line validation pipeline, and FullPath resolution | b609534 | feat |

## Decisions Made

1. **StreamReader BOM detection** (02-01-01): Uses `detectEncodingFromByteOrderMarks: true` rather than manual byte inspection. Handles all common BOMs natively.
2. **7-step validation pipeline** (02-01-02): Steps ordered from cheapest to most expensive. Each step either returns null (skip) or passes through.
3. **PluginWarningKind enum** (02-01-03): Replaces boolean return from ValidatePluginExists with specific warning categories for actionable error messages.
4. **Non-rooted = NotFound** (02-01-04): The old code returned `true` for non-rooted paths. New code returns `NotFound` since relative paths cannot be validated on disk.

## Deviations from Plan

None -- plan executed exactly as written.

## Next Phase Readiness

- Plan 02-02 (MO2 path resolution and error guidance) can proceed immediately
- The `dataFolderPath` parameter is now available for callers but not yet wired in the orchestrator (Plan 02-02 or later)
- `PluginWarningKind` is available for aggregated error reporting (Plan 02-02 scope)

## Self-Check: PASSED
