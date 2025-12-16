# Project Context

## Purpose
AutoQAC (Auto Quick Auto Clean) is a C# implementation of XEdit-PACT (Plugin Auto Cleaning Tool) using Avalonia UI. It automates the cleaning of Bethesda game plugins (ESP/ESM/ESL files) by orchestrating xEdit's Quick Auto Clean functionality. The tool detects installed games, manages plugin cleaning queues, and provides a user-friendly interface for batch cleaning operations.

**Current Stage**: Foundation/infrastructure stage, porting from Python/Qt and Rust/Slint reference implementations in `Code_To_Port/`.

## Tech Stack
- **.NET 8** (LTS) - Target framework
- **C# 12** - With nullable reference types enabled
- **Avalonia UI 11.3.8** - Cross-platform XAML-based UI framework
- **ReactiveUI** - MVVM framework for reactive programming
- **Fluent Design** - Microsoft Fluent theme for Avalonia
- **YamlDotNet** - YAML configuration file parsing
- **xUnit + FluentAssertions** - Testing framework (planned)

## Project Conventions

### Code Style
- **Nullable reference types**: Always enabled; annotate nullability explicitly (`string?` vs `string`)
- **Naming conventions**:
  - PascalCase for public members and types
  - camelCase with `_` prefix for private fields
  - UPPER_CASE for constants
  - Async methods must end with `Async` suffix
- **One class per file**, file name matches class name
- **Namespaces** match folder structure
- Use descriptive names over abbreviations

### Architecture Patterns
**Strict MVVM Pattern**:
1. **Models** (`AutoQAC/Models/`): Pure business logic, no UI dependencies
2. **ViewModels** (`AutoQAC/ViewModels/`): Inherit from `ReactiveObject`, presentation logic only
3. **Views** (`AutoQAC/Views/`): XAML with minimal code-behind, pure UI rendering

**ReactiveUI Patterns**:
- `ReactiveCommand` for all commands
- `RaiseAndSetIfChanged` for property setters
- `WhenAnyValue` for computed properties
- `ObservableAsPropertyHelper` for derived properties

**Async/Await**:
- All I/O operations must be async
- Never use `.Result` or `.Wait()` (causes deadlocks)
- Use `ConfigureAwait(false)` in library code
- Use `Dispatcher.UIThread.InvokeAsync` for cross-thread UI updates

**Dependency Injection**:
- Constructor injection for services
- Interfaces for testability
- No static dependencies or service locators

### Testing Strategy
- **Unit Tests**: Test ViewModels and Models independently
- **Integration Tests**: Test file I/O and subprocess execution
- **UI Tests**: Avalonia.Headless for automated UI testing (future)
- **Coverage Target**: Minimum 80% for critical paths

### Git Workflow
- **Main branch**: `main`
- **Commit messages**: Conventional commits style (feat:, fix:, docs:, etc.)
- Standard PR workflow for changes

## Domain Context
- **xEdit**: Command-line tool for cleaning Bethesda game plugins (SSEEdit, FO4Edit, etc.)
- **Quick Auto Clean (QAC)**: xEdit's `-QAC` flag for automated cleaning
- **ITMs**: Identical To Master records (should be removed)
- **UDRs**: Undeleted and Disabled References (should be fixed)
- **Deleted Navmeshes**: Navigation mesh deletions that can cause crashes
- **MO2 (Mod Organizer 2)**: Popular mod manager that virtualizes the game's data folder
- **Partial Forms**: Advanced cleaning option using `-iknowwhatimdoing -allowmakepartial`

## Important Constraints

### Sequential Processing Requirement
**CRITICAL**: Only one plugin can be cleaned at a time due to xEdit's file locking mechanisms.
- Never attempt parallel or concurrent cleaning operations
- Ensure only one xEdit process runs at any time
- If multiple xEdit windows open, this is a critical bug
- Process plugins in a strict sequential queue

### Thread Safety
- All ViewModel properties must be thread-safe
- Protect shared state with proper synchronization
- Prefer async patterns over `lock` statements

### Error Handling
- Structured exception handling
- Log errors appropriately
- User-friendly error messages via dialogs
- Never swallow exceptions silently

## External Dependencies

### xEdit (Required)
- Game-specific variants: SSEEdit, FO4Edit, TES5Edit, etc.
- Invoked via `System.Diagnostics.Process`
- Commands built with `-QAC -autoload "{pluginName}"` flags
- Default timeout: 300 seconds per plugin
- Output parsed for ITMs, UDRs, and deleted navmeshes

### Mod Organizer 2 (Optional)
- Integration via `ModOrganizer.exe run` command wrapper
- Virtualizes game data folder for proper mod loading

### Configuration Files
Located in `AutoQAC Data/`:
- `AutoQAC Main.yaml`: Game configurations, plugin skip lists
- `AutoQAC Config.yaml`: User settings, file paths
- `AutoQAC Ignore.yaml`: Plugin ignore list (refactor pending)
