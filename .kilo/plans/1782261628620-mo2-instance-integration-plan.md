# MO2 Instance Integration & Refresh Fix

## Goal

When MO2 Mode is checked and MO2 is configured, the plugin list currently never refreshes. Fix that refresh, and add full MO2 integration:

1. Fix the MO2-mode refresh bug.
2. Auto-detect the MO2 instance for the selected game (with a per-game Browse override + Reset, mirroring the Data Folder row).
3. Build a pseudo-load-order by parsing the selected profile's `loadorder.txt`.
4. Add a profile selector when the instance has multiple profiles; auto-select `Default` if present; persist the user's chosen profile (do not revert to `Default`).
5. For Mutagen-supported games, run the same issue **Approximation** that non-MO2 load orders get.

Windows-only WinUI 3 app. Preserve strict MVVM, sequential cleaning, and existing service patterns. Do not modify `Mutagen/` (read-only submodule).

## Root Cause Of The Refresh Bug

`AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:221` `OnMo2ModeEnabledChanged` only calls `AutoSaveConfigurationAsync("MO2 mode")`. Compare `OnDisableSkipListsEnabledChanged` (line 227) which also calls `RefreshPluginsForGameAsync`. Toggling MO2 Mode never reloads the plugin list. The fix is part of a larger change that makes refresh MO2-aware.

## Confirmed Facts (from investigation)

- Cleaning uses only `plugin.FileName` (`XEditCommandBuilder.cs:74,99`). MO2 mode wraps xEdit via `ModOrganizer.exe run <xEdit> -a "<args>"` (`XEditCommandBuilder.cs:35-55`). A real file path (`FullPath`) is therefore needed **only** for the Approximation feature, not for cleaning.
- Approximation (`PluginIssueApproximationService.cs:127-194`) uses `LoadOrder.Import<TMod>(dataFolder, listings, release)` + `ToImmutableLinkCache()`, which assumes all files live in one folder. In MO2 the files are scattered across `<instance>/mods/<ModName>/` plus the real game Data folder.
- Mutagen 0.53.1 exposes `LoadOrder.Import<TMod>(DirectoryPath dataFolder, IEnumerable<ILoadOrderListingGetter> loadOrder, GameRelease, Func<ModPath,TMod> factory, IFileSystem?)` (`Mutagen/Mutagen.Bethesda.Core/Plugins/Order/LoadOrder.cs:469`). The custom factory lets us redirect each plugin to its real resolved path. (Alternative if the factory proves awkward with missing files: a redirecting `IFileSystem` shim — the `Import`/`GetListings` APIs accept `IFileSystem?`.)
- `LoadOrderListing.CreateEnabled(ModKey)` builds listings; `ModKey.FromFileName(string)` builds keys (`docs/mutagen/Mutagen.Bethesda.Core.md:836-853`, Kernel `ModPath`/`ModKey`).
- Config currently stores only `ModOrganizer.Binary` and `Settings.Mo2Mode` (global) — no instance/profile concept (`Models/Configuration/UserConfiguration.cs:67-73,93`).
- Per-game override pattern already exists: `GameDataFolderOverrides` (top-level dict, alias `Game_Data_Folders`) with `GetGameDataFolderOverrideAsync`/`SetGameDataFolderOverrideAsync`; the Data Folder UI row is auto-detected + Override... + Reset (`MainWindow.xaml:60-69`).
- MO2 supports a global `-p "ProfileName"` launch flag (STEP wiki, confirmed).
- `ModOrganizer.ini` = QSettings INI: `[General] gameName`, `gamePath`, `selected_profile=@ByteArray(<name>)`; paths in a settings section (`base_directory`, `mod_directory`, `profiles_directory`) may use `%BASE_DIR%`. Profiles live under `profiles/<name>/` containing `loadorder.txt`, `plugins.txt`, `modlist.txt`. Global instances: `%LOCALAPPDATA%\ModOrganizer\<instance>\`; portable instances next to `ModOrganizer.exe`.

## Decisions (resolved with user)

- **D1 — Instance discovery:** auto-detect the instance whose `gameName` matches the Selected Game, with a per-game **Browse override + Reset** like the Data Folder row. The override is the instance **base directory** (the folder containing `mods/` and `profiles/`). Auto-detected value is computed, not stored; only the override and the chosen profile are persisted.
- **D2 — Approximation accuracy:** VFS-accurate via `modlist.txt` priority. Conflict winner order: `overwrite/` → enabled mods in `modlist.txt` priority order (top of file = highest priority = wins) → real game Data folder (vanilla/DLC). Unresolved plugins are marked `Approximation.Unavailable`.
- **D3 — Clean consistency:** pass `-p "<selected profile>"` so cleaning uses the same profile as the list/approximation. Only added when a profile name is known; otherwise launch is unchanged.
- **D4 — Profile precedence:** persisted per-game choice (if its folder still exists) → `Default` (if exists) → ini `selected_profile` (if exists) → first profile (alphabetical).
- **D5 — List source in MO2 mode:** for ALL games (including Mutagen-supported), the plugin list comes from the profile's `loadorder.txt` (Mutagen auto-detection reads the real game folder, not the VFS, so it is wrong under MO2).

## gameName → GameType Mapping (verify exact strings against a real ini)

| MO2 `gameName` | `GameType` |
|---|---|
| `Skyrim` | `SkyrimLe` |
| `Skyrim Special Edition` | `SkyrimSe` |
| `Skyrim VR` | `SkyrimVr` |
| `Fallout 4` | `Fallout4` |
| `Fallout 4 VR` | `Fallout4Vr` |
| `Fallout 3` | `Fallout3` |
| `New Vegas` / `Fallout New Vegas` / `TTW` | `FalloutNewVegas` |
| `Oblivion` | `Oblivion` |

Implementer: confirm the literal `gameName` strings (and Enderal handling) from actual `ModOrganizer.ini` files before finalizing the map; treat unknown names as "no match".

## Implementation Tasks (ordered)

### 1. Config model + persistence
- `Models/Configuration/UserConfiguration.cs`: add two top-level dictionaries mirroring `GameDataFolderOverrides`:
  - `Mo2InstanceOverrides` (alias `Mo2_Instance_Overrides`) — per-game instance base-dir override.
  - `Mo2ProfileSelections` (alias `Mo2_Profile_Selections`) — per-game chosen profile name.
  - Update `Copy()` to deep-copy both.
- `Services/Configuration/IConfigurationService.cs` + `ConfigurationService.cs`: add accessors mirroring the data-folder-override methods:
  - `Task<string?> GetMo2InstanceOverrideAsync(GameType, CancellationToken)` / `SetMo2InstanceOverrideAsync(GameType, string?, …)`.
  - `Task<string?> GetMo2ProfileAsync(GameType, …)` / `SetMo2ProfileAsync(GameType, string?, …)`.
  - Surface keys in `GetAllSettingsAsync` for diagnostics.
- Keep `ModOrganizer.Binary` and `Settings.Mo2Mode` as-is.

### 2. New service: `IMo2InstanceService` (Services/MO2)
Pure file/IO + parsing, async, no UI. Methods:
- `Task<Mo2InstanceInfo?> ResolveInstanceAsync(GameType game, string? mo2BinaryPath, string? overrideBaseDir, CancellationToken)`:
  - If `overrideBaseDir` set and exists → build `Mo2InstanceInfo` from it (`profiles` = `<base>/profiles`, `mods` = `<base>/mods`, `overwrite` = `<base>/overwrite`); still read a co-located `ModOrganizer.ini` if present to honor custom `mod_directory`/`profiles_directory`.
  - Else gather candidate `ModOrganizer.ini` files: portable (`<dir of mo2 binary>/ModOrganizer.ini`) + global (`%LOCALAPPDATA%\ModOrganizer\*\ModOrganizer.ini`). Parse `[General] gameName`, match to `game`. On multiple matches, prefer the portable one (if binary is portable) else most-recently-written ini.
  - Resolve `base_directory` (default = ini directory), `mod_directory` (default `<base>/mods`), `profiles_directory` (default `<base>/profiles`), expanding `%BASE_DIR%`. Decode `selected_profile` from `@ByteArray(<name>)`.
- `IReadOnlyList<string> GetProfiles(Mo2InstanceInfo)` — immediate subfolders of `ProfilesDirectory` that contain `loadorder.txt` (fallback `plugins.txt`).
- `string ChooseProfile(Mo2InstanceInfo, IReadOnlyList<string> profiles, string? persisted)` — applies D4 precedence.
- `string? GetLoadOrderPath(Mo2InstanceInfo, string profile)` → `<profiles>/<profile>/loadorder.txt`.
- `IReadOnlyDictionary<string,string> BuildPluginPathMap(Mo2InstanceInfo, string profile, string? gameDataFolder)` (case-insensitive):
  - Parse `profiles/<profile>/modlist.txt`: `+Name` enabled, `-Name` disabled, `*Name`/`_separator` entries ignored. File order top→bottom = highest→lowest priority.
  - Insert highest-priority first: `overwrite/` root, then each enabled mod root `<mods>/<Name>/`, scanning `*.esp/*.esm/*.esl`; first occurrence (highest priority) wins.
  - Add `<gameDataFolder>/` plugins last (lowest priority) for vanilla/DLC not provided by mods.
  - If `modlist.txt` missing: fall back to scanning all `mods/*` (first-match) + base Data, and log a warning (less accurate).

New model `Mo2InstanceInfo { BaseDirectory, ModsDirectory, ProfilesDirectory, OverwriteDirectory, IniSelectedProfile, GameName, IsAutoDetected, IniPath }`.

Register `IMo2InstanceService` in `ServiceCollectionExtensions.AddBusinessLogic`.

### 3. Approximation: support resolved paths
- `Services/Plugin/IPluginIssueApproximationService.cs`: add an overload:
  - `Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(GameType, string baseDataFolder, IReadOnlyList<string> orderedPluginNames, Func<ModKey,string?> pathResolver, Action<PluginIssueApproximationResult>? onReady, CancellationToken)`.
- `PluginIssueApproximationService.cs`: implement by building `listings` from `orderedPluginNames` (`LoadOrderListing.CreateEnabled(ModKey.FromFileName(name))`) and calling `LoadOrder.Import<TMod>(new DirectoryPath(baseDataFolder), listings, release, factory)` where `factory(modPath)` imports from `pathResolver(modPath.ModKey) ?? modPath.Path`. Reuse the existing per-game `ISkyrimModGetter`/`IFallout4ModGetter` paths and `ToImmutableLinkCache()`. Catch per-plugin import failures → `Unavailable` (existing pattern at lines 100-106). Keep the existing single-folder method for non-MO2.
- Verify factory behavior on a missing file in the submodule; if a thrown exception isn't catchable per-plugin, pre-filter listings to resolvable keys and append `Unavailable` results for the rest.

### 4. Refresh pipeline (MO2-aware)
- `Services/Plugin/PluginRefreshCoordinator.cs` + its `PluginRefreshRequest`: add MO2 fields, e.g. `bool Mo2Mode`, `string? Mo2LoadOrderPath`, `IReadOnlyDictionary<string,string>? Mo2PathMap`, `string? Mo2BaseDataFolder`.
  - `LoadPluginsAsync`: when `Mo2Mode`, load the list from `Mo2LoadOrderPath` via `GetPluginsFromFileAsync` (names + order). Set each row's `FullPath` to the resolved real path from `Mo2PathMap` when present (so approximation targeting and path-based dedupe/exclusions work), else leave the bare name.
  - Approximation step: when `Mo2Mode` and Mutagen game, call the new approximation overload with `Mo2BaseDataFolder` + a resolver backed by `Mo2PathMap`; otherwise keep current behavior.
  - `ResolveDataFolder`: for MO2 return `Mo2BaseDataFolder` (never the first plugin's directory, which is scattered).

### 5. ConfigurationViewModel
- Add observable props: `Mo2InstancePath`, `bool IsMo2InstanceOverride`, `bool? IsMo2InstanceValid`, `ObservableCollection<string> AvailableProfiles`, `string? SelectedProfile`, computed `ShowMo2Config => Mo2ModeEnabled && IsGameSelected`, `ShowProfileSelector => ShowMo2Config && AvailableProfiles.Count > 1`.
- Fix the bug: `OnMo2ModeEnabledChanged` → save + recompute MO2 instance/profiles + `RefreshPluginsForGameAsync(SelectedGame)`.
- Recompute MO2 instance/profiles on: game change, MO2 binary change (`ConfigureMo2Async`), instance override change, and init.
- `OnSelectedProfileChanged`: persist via `SetMo2ProfileAsync(SelectedGame, value)` + refresh.
- New commands: `ConfigureMo2InstanceCommand` (folder picker → `SetMo2InstanceOverrideAsync` → recompute → refresh) and `ResetMo2InstanceCommand` (clear override → re-auto-detect → refresh).
- `RefreshPluginsForGameAsync`: when `Mo2ModeEnabled`, resolve instance + profile, compute `loadorder.txt` path, build path map, get base Data folder (`GetGameDataFolder`), and pass all into the `PluginRefreshRequest`. Also push the selected profile into state for the command builder (see task 6).
- Empty/invalid instance → set status text + leave list empty; do not throw.

### 6. Cleaning consistency (`-p`)
- `Models/AppState.cs`: add `string? Mo2Profile`.
- `Services/State`: carry `Mo2Profile` (extend `UpdateConfigurationPaths` or set via `UpdateState`); sync into VM in `OnStateChanged`.
- `Services/Cleaning/XEditCommandBuilder.cs`: when `Mo2ModeEnabled` and `Mo2Profile` is non-empty, build `ModOrganizer.exe -p "<profile>" run <xEdit> -a "<args>"` (insert `-p` + profile before `run`). Verify arg ordering against MO2.
- MO2 preflight (`Services/Cleaning/CleaningPreflight.cs` + `CleaningCommandsViewModel.ValidatePreClean`): when MO2 mode, validate MO2 binary, resolved instance, selected profile, and existence of `loadorder.txt`; emit friendly `ValidationError`s.

### 7. UI (XAML)
- `Views/MainWindow.xaml` (config panel ~lines 60-106):
  - Add "MO2 Instance:" row (read-only textbox + ✓/✗ + `Browse...` → `ConfigureMo2InstanceCommand` + `Reset` → `ResetMo2InstanceCommand`), visible on `ShowMo2Config`.
  - Add "Profile:" `ComboBox` bound to `AvailableProfiles`/`SelectedProfile`, visible on `ShowProfileSelector`.
  - Hide the manual "Load Order File" row (line 71) when MO2 Mode is on (MO2 derives it). Keep the Data Folder row (needed for vanilla master resolution).
- `Views/SettingsContent.xaml`: optional parity note for the MO2 executable row; instance/profile live in the main window workflow.
- Keep all view logic in code-behind/bindings; ViewModels must not touch controls.

## Edge Cases / Failure Modes

- MO2 mode on but no instance detected/configured → status message + validation error; empty list.
- `loadorder.txt` missing for the chosen profile → error message; empty list.
- Persisted profile folder deleted externally → fall back via D4 precedence; overwrite the stale persisted value only when the user picks a new one.
- `modlist.txt` missing → degraded resolver (first-match scan + base Data); log warning.
- Plugin file unresolved → list still shows it; approximation `Unavailable` for that plugin.
- Multiple instances match the same game → prefer portable (if binary portable) else most-recently-written ini; user can override.
- `selected_profile` stored as `@ByteArray(<name>)` (and possibly `@Invalid()`) → decode/strip wrapper.

## Risks To Verify During Implementation

- Exact `gameName` strings per game (and Enderal/TTW).
- `-p` argument ordering with the `run` verb.
- Mutagen factory behavior when a resolved file is missing (catch per-plugin → `Unavailable`, or pre-filter listings).
- `modlist.txt` separator markers and priority direction (top = highest).

## Validation

- Unit tests (xUnit + NSubstitute, match optional params explicitly):
  - `ModOrganizer.ini` parser: `gameName`, `base_directory`, `%BASE_DIR%` expansion, `mod_directory`/`profiles_directory`, `@ByteArray` decode.
  - Instance auto-detection: portable + global; multi-match preference; gameName→GameType mapping.
  - Profile discovery + D4 precedence + persistence (don't revert from a saved non-Default).
  - `modlist.txt` priority resolver: conflict winner = overwrite > top-of-modlist > base Data; disabled mods excluded.
  - Refresh fires on MO2 toggle, instance change, profile change.
  - `XEditCommandBuilder` injects `-p "<profile>"` only in MO2 mode with a known profile; argv intact otherwise.
- `dotnet build AutoQACSharp.slnx` and `dotnet test AutoQACSharp.slnx` (auto-collects coverage).
- Manual smoke test (Windows, real MO2): toggle MO2 Mode → list refreshes from `loadorder.txt`; switch profile → persists across restart; approximation populates for Skyrim SE / Fallout 4; clean launches with the selected profile.

## Out Of Scope

- Reading/honoring MO2 `mod_directory`/`profiles_directory` beyond `%BASE_DIR%` indirection edge cases (handle if trivial; otherwise default layout).
- Writing to `ModOrganizer.ini` (we read only; profile is forced via `-p`).
- Non-Mutagen games keep no approximation (unchanged), but DO get the MO2 `loadorder.txt`-based list.
- No new UI automation/headless test project (none exists in this solution).

## Constraints (must preserve)

- Strict MVVM; CommunityToolkit.Mvvm source generators; `partial` ViewModels; marshal `IObservable<T>` via `CallbackObserver<T>` + `IUiDispatcher`.
- Sequential cleaning; single process slot; `FlushPendingSavesAsync` before launch.
- Constructor injection via `ServiceCollectionExtensions`; no static mutable state.
- Do not modify `Mutagen/`.

> Implementation requires source edits; switch to an implementation-capable agent to execute this plan.
