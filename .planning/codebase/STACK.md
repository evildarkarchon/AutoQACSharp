# Technology Stack

**Analysis Date:** 2026-02-06

## Languages

**Primary:**
- C# 12 - Application codebase with nullable reference types enabled (`<Nullable>enable</Nullable>`)

## Runtime

**Environment:**
- .NET 10.0 (latest LTS)
- Target: net10.0-windows10.0.19041.0
- Platform: Windows (Windows 10 version 19041.0 or later)

**Package Manager:**
- NuGet
- Lockfile: packages.lock.json (auto-managed by NuGet)

## Frameworks

**Core UI:**
- Avalonia 11.3.11 - Cross-platform XAML-based UI framework
- Avalonia.Controls.DataGrid 11.3.11 - Data grid control
- Avalonia.Desktop 11.3.11 - Desktop platform support
- Avalonia.Themes.Fluent 11.3.11 - Microsoft Fluent Design theme
- Avalonia.Fonts.Inter 11.3.11 - Inter font support
- Avalonia.Diagnostics 11.3.11 - Diagnostics tools (Debug only)

**MVVM & Reactive:**
- ReactiveUI.Avalonia 11.3.8 - MVVM framework with reactive extensions
- Implements ReactiveObject for ViewModels
- ReactiveCommand for command binding
- IObservable-based state management

**Dependency Injection:**
- Microsoft.Extensions.DependencyInjection 10.0.2 - Service container

## Key Dependencies

**Critical:**
- Mutagen.Bethesda 0.52.0 - Plugin loading and load order detection for supported games (Skyrim LE/SE/VR, Fallout 4/4VR)
- Mutagen.Bethesda.Skyrim 0.52.0 - Skyrim-specific plugin support
- Mutagen.Bethesda.Fallout4 0.52.0 - Fallout 4-specific plugin support

**Configuration & Serialization:**
- YamlDotNet 16.3.0 - YAML configuration parsing (AutoQAC Main.yaml, AutoQAC Settings.yaml)

**Logging:**
- Serilog 4.3.0 - Structured logging framework
- Serilog.Sinks.Console 6.1.1 - Console log output (Warning level minimum)
- Serilog.Sinks.File 7.0.0 - Rolling file logs (daily rotation, 5MB limit, 5 files retained)

**Testing:**
- xunit 2.9.3 - Unit test framework
- xunit.runner.visualstudio 3.1.5 - Test runner for Visual Studio
- Moq 4.20.72 - Mocking framework
- FluentAssertions 8.8.0 - Fluent assertion library
- Microsoft.NET.Test.Sdk 18.0.1 - Test SDK
- coverlet.collector 6.0.4 - Code coverage collection

## Configuration

**Environment:**
- Configuration resolved from `AutoQAC Data/` directory (relative to AppContext.BaseDirectory)
- In DEBUG mode: Searches parent directories (up to 6 levels) to find source tree location
- In RELEASE mode: Uses `AutoQAC Data/` relative to application directory

**Build Configuration:**
- Compiled bindings enabled by default: `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`
- Application manifest: `app.manifest` (Windows application manifest)

**Main Configuration Files:**
- `AutoQAC Main.yaml` - Game-specific xEdit executable names and default skip lists (distributed)
- `AutoQAC Settings.yaml` - User settings and custom skip lists (user-writable)

## Platform Requirements

**Development:**
- Windows OS (Windows 10 19041.0 or later)
- .NET 10.0 SDK
- Visual Studio 2022+ or Rider

**Production:**
- Windows OS (Windows 10 19041.0 or later)
- .NET 10.0 Runtime (desktop)
- xEdit installation (TES5Edit, SSEEdit, FO4Edit, etc.)
- Optional: Mod Organizer 2 (ModOrganizer.exe) for MO2 integration

## Build & Distribution

**Build Output:**
- WinExe (Windows Desktop Application)
- Asset copying: `AutoQAC Data/**` copied to output with PreserveNewest

**Configuration Copying:**
- AutoQAC data files included in output directory for distribution
- Debug configuration resolves from source tree when available

---

*Stack analysis: 2026-02-06*
