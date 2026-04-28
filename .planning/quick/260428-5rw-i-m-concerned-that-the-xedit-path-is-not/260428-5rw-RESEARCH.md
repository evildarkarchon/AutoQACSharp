# Quick Task 260428-5rw: xEdit Path Persistence - Research

**Researched:** 2026-04-28  
**Domain:** AutoQAC configuration persistence / Avalonia MVVM  
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- If a persistence bug is confirmed, implement the fix rather than stopping at diagnosis. [VERIFIED: `.planning/quick/260428-5rw-i-m-concerned-that-the-xedit-path-is-not/260428-5rw-CONTEXT.md`]
- Prioritize hardening the relevant configuration flow, not just the narrowest symptom, while keeping the change appropriately scoped for a quick task. [VERIFIED: `.planning/quick/260428-5rw-i-m-concerned-that-the-xedit-path-is-not/260428-5rw-CONTEXT.md`]

### the agent's Discretion
- Use systematic debugging: reproduce through tests or existing code paths before changing production code. [VERIFIED: `.planning/quick/260428-5rw-i-m-concerned-that-the-xedit-path-is-not/260428-5rw-CONTEXT.md`]
- Preserve existing MVVM boundaries and avoid unrelated cleanup. [VERIFIED: `.planning/quick/260428-5rw-i-m-concerned-that-the-xedit-path-is-not/260428-5rw-CONTEXT.md`]

### Deferred Ideas (OUT OF SCOPE)
- None listed. [VERIFIED: `.planning/quick/260428-5rw-i-m-concerned-that-the-xedit-path-is-not/260428-5rw-CONTEXT.md`]
</user_constraints>

## Summary

There are two xEdit path entry points: the main-window Browse button and the Edit > Settings dialog. [VERIFIED: `AutoQAC/Views/MainWindow.axaml`, `AutoQAC/Views/SettingsWindow.axaml`] The main-window path flow already contains a targeted guard against stale state overwriting the selected path: `ConfigureXEditAsync` assigns `XEditPath = path`, updates `IStateService`, then calls `SaveConfigurationAsync`. [VERIFIED: `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:307-324`] A regression test already covers that exact stale-property failure mode. [VERIFIED: `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs:1090-1140`]

The likely remaining gap is the Settings dialog integration: `SettingsViewModel.SaveAsync` persists `config.XEdit.Binary = XEditPath`, but after the dialog returns true, `CleaningCommandsViewModel.ShowSettingsAsync` reloads config and only pushes `Mo2ModeEnabled` and `CleaningTimeout` into `AppState`, not `XEditExecutablePath`, `Mo2ExecutablePath`, or load-order path. [VERIFIED: `AutoQAC/ViewModels/SettingsViewModel.cs:263-288`, `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs:252-271`] Because `MainWindowViewModel` drives `ConfigurationViewModel.OnStateChanged` from `IStateService.StateChanged`, a saved xEdit path from Settings may persist to YAML while the main-window state/UI still shows the old path until restart or another path update. [VERIFIED: `AutoQAC/ViewModels/MainWindowViewModel.cs:64-76`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:491-498`]

**Primary recommendation:** first add/adjust tests that reproduce the Settings-dialog save path failing to update runtime state, then harden the post-settings save bridge to call `UpdateConfigurationPaths(config.LoadOrder.File, config.ModOrganizer.Binary, config.XEdit.Binary)` along with the existing settings state update. [VERIFIED: codebase flow inspection]

## Project Constraints (from AGENTS.md)

- Preserve Windows-only assumptions for registry, executable paths, and process handling. [VERIFIED: `AGENTS.md`]
- Maintain strict MVVM boundaries; code-behind owns dialogs/windows, ViewModels should not directly manipulate controls. [VERIFIED: `AGENTS.md`]
- Use CommunityToolkit.Mvvm source generators in ViewModels; do not introduce ReactiveUI/System.Reactive in the ViewModel layer. [VERIFIED: `AGENTS.md`]
- Keep I/O and process work async; do not block the UI thread with `.Result` or `.Wait()`. [VERIFIED: `AGENTS.md`]
- Use constructor injection through `ServiceCollectionExtensions`; avoid static mutable state and service locators. [VERIFIED: `AGENTS.md`]
- Do not modify `Mutagen/`. [VERIFIED: `AGENTS.md`]
- Use NSubstitute for mocks, and match optional parameters explicitly in substitute setups/assertions. [VERIFIED: `AGENTS.md`]
- Do not claim or depend on an Avalonia.Headless project unless one is intentionally added. [VERIFIED: `AGENTS.md`]
- Never delete comments as cleanup; add XML doc comments for methods added or substantially rewritten unless trivial/private. [VERIFIED: `C:/Users/evild/.config/opencode/AGENTS.md`]

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|--------------|----------------|-----------|
| Browse/select xEdit path | UI/ViewModel | UI service | Dialog is mediated by `IFileDialogService`; ViewModel receives the selected path. [VERIFIED: `ConfigurationViewModel.cs:307-324`, `SettingsViewModel.cs:319-333`] |
| Persist xEdit path | Configuration service | ViewModel | ViewModels mutate `UserConfiguration.XEdit.Binary`; `ConfigurationService` handles debounced writes and flush. [VERIFIED: `SettingsViewModel.cs:263-288`, `ConfigurationService.cs:219-331`] |
| Runtime availability of saved path | State service | Main window sub-VMs | `AppState.XEditExecutablePath` drives validation/start command and main-window display. [VERIFIED: `StateService.cs:72-80`, `CleaningCommandsViewModel.cs:330-342`, `ConfigurationViewModel.cs:491-498`] |

## Current Persistence Flow

### Main-window Browse flow (already hardened)

1. User clicks main-window xEdit Browse. [VERIFIED: `AutoQAC/Views/MainWindow.axaml:158-180`]
2. `ConfigurationViewModel.ConfigureXEditAsync` opens the file dialog. [VERIFIED: `ConfigurationViewModel.cs:307-314`]
3. It assigns `XEditPath = path` before saving to avoid stale asynchronous state sync. [VERIFIED: `ConfigurationViewModel.cs:316-323`]
4. It updates runtime state with `_stateService.UpdateConfigurationPaths(LoadOrderPath, Mo2Path, path)`. [VERIFIED: `ConfigurationViewModel.cs:321-323`]
5. `SaveConfigurationAsync` reloads config, sets `config.XEdit.Binary = XEditPath`, and calls `SaveUserConfigAsync`. [VERIFIED: `ConfigurationViewModel.cs:500-509`]
6. A focused unit test verifies the newly selected path is persisted rather than the old one. [VERIFIED: `MainWindowViewModelTests.cs:1090-1140`]

### Settings dialog flow (likely integration gap)

1. Edit > Settings invokes `CleaningCommandsViewModel.ShowSettingsAsync`, which delegates to the registered `ShowSettingsInteraction`. [VERIFIED: `AutoQAC/Views/MainWindow.axaml:25-27`, `CleaningCommandsViewModel.cs:252-258`]
2. `MainWindow.axaml.cs` creates `SettingsViewModel`, calls `LoadSettingsAsync`, shows `SettingsWindow`, disposes the VM, and returns the dialog result. [VERIFIED: `AutoQAC/Views/MainWindow.axaml.cs:89-106`]
3. `SettingsViewModel.SaveAsync` reloads user config and writes `config.XEdit.Binary = XEditPath` before `SaveUserConfigAsync`. [VERIFIED: `SettingsViewModel.cs:263-288`]
4. Back in `CleaningCommandsViewModel.ShowSettingsAsync`, a successful result reloads config but updates only `Mo2ModeEnabled` and `CleaningTimeout` in state. [VERIFIED: `CleaningCommandsViewModel.cs:259-267`]
5. No code path found subscribes to `UserConfigurationChanged` and maps app-initiated config saves back into `IStateService.UpdateConfigurationPaths`. [VERIFIED: grep for `UserConfigurationChanged` and `UpdateConfigurationPaths`]

## Root-Cause Hypothesis to Test

**Hypothesis H1:** xEdit path is saved to `AutoQAC Settings.yaml` from the Settings dialog, but runtime state/main-window UI is not refreshed after Save, making it appear unsaved until restart. [VERIFIED: codebase flow inspection]

**Why H1 is plausible:** `ConfigurationService.SaveUserConfigAsync` returns immediately after setting `_pendingConfig` and emitting `UserConfigurationChanged`; disk write is debounced by 500 ms unless flushed. [VERIFIED: `ConfigurationService.cs:60-78`, `ConfigurationService.cs:219-232`] `LoadUserConfigAsync` reads `_pendingConfig` first, so the Settings dialog's immediate post-save reload can see the saved path even before disk flush. [VERIFIED: `ConfigurationService.cs:162-175`] However, `CleaningCommandsViewModel.ShowSettingsAsync` does not push that reloaded path into `IStateService`. [VERIFIED: `CleaningCommandsViewModel.cs:259-267`]

**Evidence needed before fixing:**
- A test where `ShowSettingsCommand` returns true, `LoadUserConfigAsync` returns config with a new `XEdit.Binary`, and `IStateService.UpdateConfigurationPaths` is expected with that path. [VERIFIED: existing test patterns in `MainWindowViewModelTests.cs`]
- If the user specifically means disk persistence across app restart, add/inspect a `ConfigurationService` test that saves `XEdit.Binary`, calls `FlushPendingSavesAsync`, constructs a fresh service, and reloads the same path. [VERIFIED: existing save/flush/reload patterns in `ConfigurationServiceTests.cs:58-99`]

## Minimal Hardened Implementation Approach

1. Add a failing ViewModel integration test in `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` for the Settings dialog path: mock `ShowSettingsInteraction` result indirectly if practical, or test `CleaningCommandsViewModel.ShowSettingsCommand` through `MainWindowViewModel.Commands.ShowSettingsCommand`; assert `UpdateConfigurationPaths(loadOrder, mo2, xEdit)` receives values from the reloaded config. [VERIFIED: existing NSubstitute setup style in `MainWindowViewModelTests.cs`]
2. Change `CleaningCommandsViewModel.ShowSettingsAsync` success branch to update both path state and settings state from the reloaded config. [VERIFIED: `CleaningCommandsViewModel.cs:259-267`]
3. Keep the fix in the ViewModel/state bridge; do not make `SettingsViewModel` manipulate `IStateService` because it is currently constructed with configuration/logging/dispatcher/file-dialog dependencies only and is owned by the settings window. [VERIFIED: `SettingsViewModel.cs:148-162`, `MainWindow.axaml.cs:96-100`]
4. Consider adding a small `ConfigurationService` regression test only if disk persistence itself is suspect; otherwise, the stronger first test is the runtime reload/state-sync scenario. [VERIFIED: `ConfigurationServiceTests.cs:58-99`]

## Common Pitfalls

- **Mistaking runtime-state staleness for YAML save failure:** the Settings dialog can persist config while main-window `AppState` remains old. [VERIFIED: `SettingsViewModel.cs:263-288`, `CleaningCommandsViewModel.cs:259-267`]
- **Forgetting debounced disk writes:** `SaveUserConfigAsync` does not immediately write to disk; tests that assert file contents must call `FlushPendingSavesAsync` or dispose the service. [VERIFIED: `ConfigurationService.cs:60-78`, `ConfigurationService.cs:313-331`, `ConfigurationService.cs:740-756`]
- **Bypassing state service:** start-cleaning validation uses `IStateService.CurrentState.XEditExecutablePath`, so updating only YAML is insufficient for immediate UX correctness. [VERIFIED: `CleaningCommandsViewModel.cs:330-342`]
- **Breaking MVVM boundaries:** keep dialogs in code-behind/interactions and keep path persistence/state updates in ViewModels/services. [VERIFIED: `AGENTS.md`, `MainWindow.axaml.cs:89-106`]

## Validation Architecture

| Check | Command | Purpose |
|-------|---------|---------|
| Targeted ViewModel tests | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests"` | Validate Settings/main-window state integration. [VERIFIED: project test layout] |
| Targeted configuration tests | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigurationServiceTests"` | Validate config save/flush/reload if disk persistence is touched. [VERIFIED: project test layout] |
| Full suite | `dotnet test AutoQACSharp.slnx` | Required regression gate. [VERIFIED: `AGENTS.md`] |

**Environment:** .NET SDK `10.0.203` is available. [VERIFIED: `dotnet --version`]

## Sources

### Primary (HIGH confidence)
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` — main-window xEdit path save path and state sync. [VERIFIED]
- `AutoQAC/ViewModels/SettingsViewModel.cs` — Settings dialog xEdit path load/browse/save path. [VERIFIED]
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` — post-settings state refresh gap. [VERIFIED]
- `AutoQAC/Services/Configuration/ConfigurationService.cs` — debounced/pending config persistence behavior. [VERIFIED]
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` — existing stale-path regression coverage for main-window Browse. [VERIFIED]
- `AGENTS.md` — repository constraints and test commands. [VERIFIED]

### Secondary / Tertiary
- None; this is codebase-only research. [VERIFIED]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | User's concern likely comes from Settings dialog/main-window state not refreshing, not proven disk-write failure. [ASSUMED] | Root-Cause Hypothesis | If wrong, implementation should pivot to the disk flush/reload path after tests reveal actual failure. |

## Open Questions

1. Does the user observe the path disappearing immediately after closing Settings, after clicking Start Cleaning, or only after restarting? [ASSUMED]
   - What we know: Settings post-save path state is not refreshed in the main window. [VERIFIED: `CleaningCommandsViewModel.cs:259-267`]
   - What's unclear: Whether disk contents also fail to flush in their scenario. [ASSUMED]
   - Recommendation: write the Settings state-sync test first, then add a save/flush/reload test only if needed. [VERIFIED: codebase test patterns]
