# XEdit-PACT (C# Avalonia)

**Plugin Auto Cleaning Tool for Bethesda Game Plugins**

A C# implementation of XEdit-PACT using the Avalonia UI framework with MVVM architecture. Automates the sequential cleaning of Bethesda game plugins using xEdit's Quick Auto Clean (-QAC) functionality.

## Project Status

**Foundation Stage** - Core architecture and infrastructure in development.

**Reference Implementation**: The `Code_To_Port/` directory contains the Python/Qt and Rust/Slint implementations for reference. This directory is temporary and will be removed once feature parity is achieved.

## Overview

XEdit-PACT automates the process of cleaning game plugins (ESP/ESM/ESL files) to remove:
- **ITMs (Identical To Master)**: Records that are identical to the master file
- **UDRs (Undisabled References)**: References that should be disabled but aren't
- **Deleted Navmeshes**: Navigation meshes that can cause crashes

**IMPORTANT**: This tool cleans plugins **sequentially, one at a time**. Due to xEdit's file locking mechanisms, only one plugin can be cleaned at any given moment.

## Technology Stack

- **.NET 8**: Target framework
- **Avalonia UI 11.3.8**: Cross-platform XAML-based UI framework
- **ReactiveUI**: MVVM framework for reactive programming
- **Fluent Design**: Modern UI theme
- **C# 12**: Language features with nullable reference types

## Architecture

This project follows MVVM (Model-View-ViewModel) architecture:

```
AutoQAC/
├── Models/              # Data models and business logic
├── ViewModels/          # MVVM view models (presentation logic)
├── Views/               # XAML views (UI)
├── Assets/              # Images, icons, and resources
├── App.axaml            # Application definition
└── Program.cs           # Application entry point
```

### Key Design Principles

1. **MVVM Pattern**: Strict separation of UI (Views), presentation logic (ViewModels), and business logic (Models)
2. **Reactive Programming**: Using ReactiveUI for property changes and command handling
3. **Sequential Processing**: One plugin at a time due to xEdit file locking constraints
4. **Dependency Injection**: Services and dependencies injected via constructors
5. **Thread Safety**: Proper async/await patterns for background operations
6. **Cross-Platform**: Leveraging Avalonia's cross-platform capabilities

## Development Setup

### Prerequisites

- **.NET 8 SDK** or later
- **Visual Studio 2022** (recommended) or **JetBrains Rider**
- Basic understanding of C#, XAML, and MVVM

### Building and Running

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run --project AutoQAC

# Build release version
dotnet build -c Release
```

## Supported Games

| Game | Short Code | xEdit Executables |
|------|------------|-------------------|
| Fallout 3 | FO3 | FO3Edit.exe, FO3Edit64.exe |
| Fallout New Vegas | FNV | FNVEdit.exe, FNVEdit64.exe |
| Fallout 4 | FO4 | FO4Edit.exe, FO4Edit64.exe |
| Skyrim Special Edition | SSE | SSEEdit.exe, SSEEdit64.exe |
| Fallout 4 VR | FO4VR | FO4VREdit.exe |
| Skyrim VR | SkyrimVR | TES5VREdit.exe |

**Universal xEdit**: Also supports universal xEdit executables (`xEdit.exe`, `xEdit64.exe`) with automatic game detection.

## Features (Planned)

### Core Functionality
- [ ] Sequential cleaning of plugins (one at a time)
- [ ] Skip list integration (base game files protected)
- [ ] Auto-detection of game type
- [ ] MO2 (Mod Organizer 2) integration
- [ ] Configurable timeout per plugin
- [ ] Real-time progress tracking
- [ ] Comprehensive logging

### Advanced Features
- [ ] Record-level statistics (UDRs, ITMs, navmeshes, partial forms)
- [ ] Partial Forms experimental support
- [ ] Game detection from xEdit executable or load order
- [ ] Configuration validation with feedback
- [ ] Cancellation support
- [ ] Detailed error reporting

## Critical Constraints

**⚠ SEQUENTIAL PROCESSING ONLY**: Due to xEdit's file locking mechanisms, this tool can only clean **one plugin at a time**. If multiple xEdit windows open simultaneously, close the application immediately and report the issue.

## Configuration Files

Located in `AutoQAC Data/`:
- `AutoQAC Main.yaml`: Game configurations, skip lists
- `AutoQAC Config.yaml`: User settings, paths
- `PACT Ignore.yaml`: Additional ignore list

## Logging

Application logs will be stored in:
- `logs/autoqac_<timestamp>.log`: Rotating log files

## Reference Implementations

During development, refer to the reference implementations in `Code_To_Port/`:

- **Python/Qt Version**: Original implementation
- **Rust/Slint Version**: Modern rewrite

These references will be removed once the C# implementation achieves feature parity.

## License

GPL-3.0 License - See [LICENSE](LICENSE) for details.

## Credits

- **Original Author**: Poet (aka GuidanceOfGrace)
- **C# Implementation**: In development
- **xEdit Team**: For the powerful xEdit tools

## Links

- **Mod Organizer 2**: [GitHub Releases](https://github.com/ModOrganizer2/modorganizer/releases)
- **SSEEdit**: [Nexus Mods](https://www.nexusmods.com/skyrimspecialedition/mods/164?tab=files)
- **FO4Edit**: [Nexus Mods](https://www.nexusmods.com/fallout4/mods/2737/?tab=files)
- **Avalonia UI**: [Official Documentation](https://docs.avaloniaui.net/)
- **ReactiveUI**: [Official Documentation](https://www.reactiveui.net/)

---

**Development Note**: This project is in the foundation stage. The reference implementations in `Code_To_Port/` are temporary and will be removed once feature parity is achieved.
