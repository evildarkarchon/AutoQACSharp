# Implementation Tasks

## Overview
Tasks are ordered for incremental delivery with verification at each step. Dependencies are noted where applicable.

---

## Phase 1: Foundation

### Task 1.1: Add Mutagen NuGet Packages
- [x] **Completed**

**Files**: `AutoQAC/AutoQAC.csproj`

Add the required Mutagen NuGet packages to the project:
```xml
<PackageReference Include="Mutagen.Bethesda" Version="0.51.2" />
<PackageReference Include="Mutagen.Bethesda.Skyrim" Version="0.51.2" />
<PackageReference Include="Mutagen.Bethesda.Fallout4" Version="0.51.2" />
```

**Verification**: `dotnet build` succeeds without errors

---

### Task 1.2: Expand GameType Enum
- [x] **Completed**

**Files**: `AutoQAC/Models/GameType.cs`

Update the `GameType` enum to align with Mutagen's supported games:
- Add `SkyrimLE` (distinguish from SE)
- Rename `SkyrimSpecialEdition` to `SkyrimSE` for consistency
- Add `Oblivion` (file-based only)
- Keep existing types, ensure backward compatibility

**Verification**: Build succeeds, existing tests pass

---

### Task 1.3: Update GameDetectionService for New GameTypes
- [x] **Completed**

**Files**: `AutoQAC/Services/GameDetection/GameDetectionService.cs`, `AutoQAC/Services/GameDetection/IGameDetectionService.cs`

Update the executable patterns and master file patterns to support expanded GameType enum. Add method `GetGameDisplayName()` updates for new types.

**Verification**: Existing `GameDetectionServiceTests` pass with updated expectations

---

## Phase 2: Plugin Loading Service

### Task 2.1: Create IPluginLoadingService Interface
- [x] **Completed**

**Files**: `AutoQAC/Services/Plugin/IPluginLoadingService.cs` (new)

Define the interface with methods:
- `GetPluginsAsync(GameType, CancellationToken)`
- `GetPluginsFromFileAsync(string, CancellationToken)`
- `IsGameSupportedByMutagen(GameType)`
- `GetAvailableGames()`
- `GetGameDataFolder(GameType)`

**Verification**: Interface compiles, no implementation yet

---

### Task 2.2: Implement PluginLoadingService
- [x] **Completed**

**Files**: `AutoQAC/Services/Plugin/PluginLoadingService.cs` (new)

Implement the service with:
- Mutagen integration for supported games
- Fallback to `IPluginValidationService` for file-based loading
- GameRelease mapping helper
- Error handling and logging

**Dependencies**: Task 2.1

**Verification**: Unit tests for the service pass

---

### Task 2.3: Write PluginLoadingService Unit Tests
- [x] **Completed**

**Files**: `AutoQAC.Tests/Services/PluginLoadingServiceTests.cs` (new)

Test scenarios:
- `IsGameSupportedByMutagen` returns correct values
- `GetAvailableGames` returns all games except Unknown
- File-based loading fallback works
- Error handling for missing games

**Dependencies**: Task 2.2

**Verification**: `dotnet test` passes for new tests

---

## Phase 3: Configuration Updates

### Task 3.1: Add Game Selection to UserConfiguration
- [x] **Completed**

**Files**: `AutoQAC/Models/Configuration/UserConfiguration.cs`

Add `SelectedGame` property with YAML serialization:
```csharp
[YamlMember(Alias = "Selected_Game")]
public string SelectedGame { get; set; } = "Unknown";
```

**Verification**: Configuration file round-trips correctly

---

### Task 3.2: Update ConfigurationService for Game Selection
- [x] **Completed**

**Files**: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC/Services/Configuration/IConfigurationService.cs`

Add methods to get/set the selected game. Ensure backward compatibility with existing configs.

**Dependencies**: Task 3.1

**Verification**: `ConfigurationServiceTests` pass with new functionality

---

## Phase 4: ViewModel Updates

### Task 4.1: Add Game Selection to MainWindowViewModel
- [x] **Completed**

**Files**: `AutoQAC/ViewModels/MainWindowViewModel.cs`

Add properties:
- `AvailableGames` (IReadOnlyList<GameType>)
- `SelectedGame` (GameType with change notification)
- `IsMutagenSupported` (computed from SelectedGame)

Wire up game selection change to trigger plugin list refresh.

**Dependencies**: Task 2.2, Task 3.2

**Verification**: ViewModel tests pass

---

### Task 4.2: Update MainWindowViewModel Plugin Loading
- [x] **Completed**

**Files**: `AutoQAC/ViewModels/MainWindowViewModel.cs`

Replace direct `IPluginValidationService` usage with `IPluginLoadingService`. Update `LoadPluginsCommand` to use new service.

**Dependencies**: Task 4.1

**Verification**: Plugin loading works via ViewModel

---

### Task 4.3: Write MainWindowViewModel Game Selection Tests
- [x] **Completed**

**Files**: `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`

Add tests for:
- Game selection property changes
- Plugin list refresh on game change
- Mutagen support indicator
- Configuration persistence

**Dependencies**: Task 4.2

**Verification**: `dotnet test` passes

---

## Phase 5: UI Implementation

### Task 5.1: Add GameTypeDisplayConverter
- [x] **Completed**

**Files**: `AutoQAC/Converters/GameTypeDisplayConverter.cs` (new)

Create IValueConverter that maps GameType to display string using `GameDetectionService.GetGameDisplayName()`.

**Verification**: Converter compiles

---

### Task 5.2: Add Game Selection UI to MainWindow
- [x] **Completed**

**Files**: `AutoQAC/Views/MainWindow.axaml`

Add XAML for:
- Game selection ComboBox with ItemTemplate
- Mutagen/File-based indicator TextBlocks
- Proper data binding to ViewModel properties

**Dependencies**: Task 4.1, Task 5.1

**Verification**: UI displays correctly, dropdown populated

---

### Task 5.3: Update MainWindow Code-Behind (if needed)
- [x] **Completed**

**Files**: `AutoQAC/Views/MainWindow.axaml.cs`

Add any required code-behind for converter registration or UI initialization.

**Dependencies**: Task 5.2

**Verification**: Application runs, game selection works

---

## Phase 6: Integration and Polish

### Task 6.1: Register Services in DI Container
- [x] **Completed**

**Files**: `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`

Register `IPluginLoadingService` -> `PluginLoadingService` in the dependency injection container.

**Dependencies**: Task 2.2

**Verification**: Application starts without DI errors

---

### Task 6.2: Integration Testing
- [x] **Completed**

**Files**: `AutoQAC.Tests/Integration/GameSelectionIntegrationTests.cs` (new)

Write integration tests that verify end-to-end plugin loading for both Mutagen and file-based scenarios.

**Dependencies**: All previous tasks

**Verification**: Integration tests pass

---

### Task 6.3: Update Documentation
- [x] **Completed**

**Files**: `CLAUDE.md`, `README.md`

Update documentation to reflect:
- New Mutagen dependency
- Game selection feature
- Fallback behavior for unsupported games

**Dependencies**: All previous tasks

**Verification**: Documentation accurately reflects implementation

---

## Implementation Summary

All 18 tasks have been completed successfully:

- **Phase 1**: Foundation (3 tasks) - GameType enum expanded, Mutagen packages added
- **Phase 2**: Plugin Loading Service (3 tasks) - IPluginLoadingService with Mutagen integration
- **Phase 3**: Configuration Updates (2 tasks) - SelectedGame persistence
- **Phase 4**: ViewModel Updates (3 tasks) - Game selection in MainWindowViewModel
- **Phase 5**: UI Implementation (3 tasks) - Game dropdown with display converter
- **Phase 6**: Integration and Polish (3 tasks) - DI registration, integration tests

**Total Tests**: 269 passing

---

## Parallelization Notes

The following tasks can be done in parallel:
- Task 1.2 and Task 1.3 (GameType changes)
- Task 2.3 (tests) can start after Task 2.2 interface is defined
- Task 5.1 (converter) can be done alongside Phase 4

## Rollback Plan

If Mutagen integration causes issues:
1. Keep `IPluginLoadingService` interface
2. Implement a file-only version that delegates to `IPluginValidationService`
3. Remove Mutagen NuGet packages
4. Game selection UI remains, just always shows "File-based"
