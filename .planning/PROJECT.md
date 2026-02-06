# AutoQACSharp

## What This Is

A C# Avalonia desktop application that automatically cleans Bethesda game plugins using xEdit's Quick Auto Clean mode. It processes plugins sequentially, detecting ITMs (Identical to Master records), UDRs (Undeleted References), and deleted navmeshes, then presents cleaning results. Built for Bethesda modders who maintain large load orders across Skyrim, Fallout 4, Oblivion, Fallout 3, and Fallout New Vegas.

## Core Value

Reliably clean every plugin in a load order with one click, without corrupting game data or cleaning plugins that shouldn't be touched.

## Requirements

### Validated

- ✓ Sequential plugin cleaning via xEdit subprocess — existing
- ✓ Game detection from xEdit executable and load order master files — existing
- ✓ Plugin loading via Mutagen (Skyrim LE/SE/VR, Fallout 4/4VR) with file-based fallback (Oblivion, FO3, FNV) — existing
- ✓ MO2 integration (wrapper execution, validation, runtime detection) — existing
- ✓ YAML configuration system (main config + user settings) — existing
- ✓ Skip list management (default + custom per-game lists, disable toggle) — existing
- ✓ xEdit output parsing (ITMs, UDRs, deleted navmeshes, partial forms) — existing
- ✓ Partial forms support with user warning dialog — existing
- ✓ Progress tracking with per-plugin status (Success/Failed/Skipped) — existing
- ✓ Cleaning results window with session summary — existing
- ✓ Settings window (xEdit path, load order, MO2 config, game selection) — existing
- ✓ Skip list editing window — existing
- ✓ Game data folder overrides (C# improvement over Python) — existing
- ✓ Structured logging with Serilog (console + rotating file) — existing
- ✓ Cancellation support with CancellationToken throughout async chains — existing
- ✓ Legacy configuration migration — existing
- ✓ Dependency injection with Microsoft.Extensions.DependencyInjection — existing

### Active

**Python Parity Gaps:**

- [ ] Deferred configuration saves (batch writes to prevent deadlocks)
- [ ] Real-time parsed progress callbacks during cleaning (stats per record, not just raw lines)
- [ ] Enhanced environment validation messages (detailed error strings, not just bool)
- [ ] Robust stop operation (graceful process termination attempt before kill)
- [ ] Improved subprocess resource management (wait condition pattern, timeout on slot acquisition)
- [ ] Advanced logging configuration (startup info logging, dual-level console/file separation)
- [ ] Plugin line validation edge cases (separator detection, malformed entry handling)
- [ ] Log file monitoring (tail xEdit log files for diagnostics)
- [ ] CPU usage threshold monitoring for hung process detection
- [ ] TTW (Tale of Two Wastelands) skip list inheritance from FNV
- [ ] Bulk state change optimization (reduce signal overhead for multi-property updates)
- [ ] Configuration helper methods (get_all, update_multiple, batch operations)
- [ ] YAML cache invalidation by file modification time
- [ ] Journal/log expiration setting support

**CONCERNS.md — Known Bugs:**

- [ ] Process termination race condition (xEdit may hold file handles after disposal)
- [ ] Potential lock deadlock in StateService (Lock vs ReaderWriterLockSlim)

**CONCERNS.md — Fragile Areas:**

- [ ] Game detection fallback path (Unknown game type proceeds without skip lists)
- [ ] Plugin FullPath placeholder creates two code paths (relative vs absolute)
- [ ] CancellationTokenSource lock synchronization (race on null check in StopCleaning + Dispose)
- [ ] XEdit command building doesn't validate GameType.Unknown

**CONCERNS.md — Tech Debt:**

- [ ] Plugin FullPath resolution incomplete (FileName used as placeholder)
- [ ] Legacy configuration migration — deletion outside lock, no validation
- [ ] About dialog not implemented
- [ ] MainWindowViewModel too large (904 lines, multiple concerns)

**CONCERNS.md — Missing Features:**

- [ ] Dry-run mode (test configuration without cleaning)
- [ ] Configuration validation UI (validate paths in Settings before cleaning)
- [ ] Undo/rollback (backup plugins before cleaning)

**CONCERNS.md — Performance:**

- [ ] Configuration file disk I/O not batched (dirty-flag pattern with debounce)

**CONCERNS.md — Test Coverage Gaps:**

- [ ] ProcessExecutionService process termination edge cases
- [ ] Configuration migration failure paths
- [ ] Skip list loading for Unknown GameType
- [ ] Concurrent state updates
- [ ] PluginValidationService with non-rooted paths

**CONCERNS.md — Dependencies at Risk:**

- [ ] Mutagen version update evaluation (0.52.0 → 0.54+)
- [ ] YamlDotNet security advisory check

**Post-Parity:**

- [ ] Remove Code_To_Port/ directory

### Out of Scope

- Cross-platform support (Linux/macOS) — xEdit is Windows-only, no benefit
- Plugin content editing — out of scope, xEdit handles the actual cleaning
- Cloud sync or remote configuration — desktop-only tool, local files sufficient
- Auto-update mechanism — distribution handled externally
- Telemetry or analytics — privacy-focused tool for modders

## Context

- **Brownfield project**: C# port of Python XEdit-PACT, with reference code in `Code_To_Port/`
- **Reference implementations**: Python/Qt (`Code_To_Port/AutoQACLib/`) and Rust/Slint (partial)
- **`Code_To_Port/` is temporary**: Will be deleted once C# achieves full feature parity
- **xEdit constraint**: Only one xEdit process can run at a time due to file locking — this is an architectural invariant, not a bug
- **Target users**: Bethesda game modders maintaining 100-500+ plugin load orders
- **Supported games**: Skyrim LE, Skyrim SE, Skyrim VR, Fallout 4, Fallout 4 VR, Oblivion, Fallout 3, Fallout New Vegas
- **Existing test suite**: xUnit + Moq + FluentAssertions with coverage for services and ViewModels

## Constraints

- **Sequential processing**: Only one xEdit process at a time — xEdit file locking is absolute
- **Windows only**: Target `net10.0-windows10.0.19041.0` — xEdit requires Windows
- **MVVM strict**: Avalonia + ReactiveUI — no business logic in Views, no UI in Models
- **Async everywhere**: All I/O must be async, never block the UI thread
- **Nullable reference types**: Enabled project-wide, enforce null safety

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Avalonia over WPF | Cross-platform potential, modern XAML, active community | ✓ Good |
| ReactiveUI for MVVM | Reactive patterns fit event-driven xEdit monitoring | ✓ Good |
| Mutagen for plugin loading | Official Bethesda plugin library, handles load order correctly | ✓ Good |
| YamlDotNet for config | Compatibility with existing YAML config files from Python version | ✓ Good |
| Serilog for logging | Structured logging, rotating files, extensible sinks | ✓ Good |
| Sequential-only cleaning | xEdit file locking makes this mandatory, not a choice | ✓ Good |
| Microsoft.Extensions.DI | Standard .NET DI, no third-party container needed | ✓ Good |
| Game data folder overrides | C# improvement — lets users override Mutagen auto-detection | ✓ Good |

---
*Last updated: 2026-02-06 after initialization*
