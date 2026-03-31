# Technology Stack

**Analysis Date:** 2026-03-30

## Languages

**Primary:**
- C# 13 with nullable reference types (`<Nullable>enable</Nullable>` in all projects)

**Secondary:**
- XAML (Avalonia AXAML) for UI layout and resource definitions

## Runtime

**Environment:**
- .NET 10 (`net10.0` and `net10.0-windows10.0.19041.0`)
- Windows 10+ only for the main app (`AutoQAC.csproj` targets `net10.0-windows10.0.19041.0`)
- `QueryPlugins` is cross-platform capable (`net10.0`) but only consumed by the Windows app

**Package Manager:**
- NuGet (via `dotnet` CLI)
- No `nuget.config` present; uses default NuGet feeds
- No `global.json` present; relies on whatever SDK is installed
- No `Directory.Build.props`; each `.csproj` is self-contained

## Frameworks

**Core:**
- Avalonia 11.3.12 - Cross-platform UI framework (Windows-only deployment)
- ReactiveUI.Avalonia 11.3.8 - MVVM framework with reactive extensions
- Microsoft.Extensions.DependencyInjection 10.0.3 - IoC container

**Testing:**
- xUnit 2.9.3 - Test runner and assertions
- xunit.runner.visualstudio 3.1.5 - VS test adapter
- FluentAssertions 8.8.0 - Fluent assertion library
- NSubstitute 5.3.0 - Mocking framework
- NSubstitute.Analyzers.CSharp 1.0.17 - Static analysis for NSubstitute usage
- Microsoft.NET.Test.Sdk 18.0.1 - Test platform host

**Build/Dev:**
- `dotnet build` / `dotnet test` / `dotnet run` (standard .NET CLI)
- Solution format: `.slnx` (XML-based lightweight solution format)
- `AutoQAC/app.manifest` declares Windows 10 compatibility

## Key Dependencies

**Critical:**

| Package | Version | Purpose |
|---------|---------|---------|
| Avalonia | 11.3.12 | UI framework (core rendering, controls, themes) |
| Avalonia.Desktop | 11.3.12 | Desktop platform backend |
| Avalonia.Themes.Fluent | 11.3.12 | Fluent Design theme |
| Avalonia.Fonts.Inter | 11.3.12 | Inter font family |
| Avalonia.Controls.DataGrid | 11.3.12 | DataGrid control for plugin lists |
| Avalonia.Diagnostics | 11.3.12 | Dev-only diagnostic overlay (excluded from Release builds) |
| ReactiveUI.Avalonia | 11.3.8 | Reactive MVVM bindings for Avalonia |
| Mutagen.Bethesda | 0.53.1 | Core Bethesda plugin handling (load orders, game locations) |
| Mutagen.Bethesda.Skyrim | 0.53.1 | Skyrim-specific record types (LE, SE, VR, Enderal) |
| Mutagen.Bethesda.Fallout4 | 0.53.1 | Fallout 4 specific record types |
| Mutagen.Bethesda.Starfield | 0.53.1 | Starfield record types (QueryPlugins only) |
| Mutagen.Bethesda.Oblivion | 0.53.1 | Oblivion record types (QueryPlugins only) |

**Infrastructure:**

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Extensions.DependencyInjection | 10.0.3 | Service container (constructor injection) |
| Serilog | 4.3.1 | Structured logging framework |
| Serilog.Sinks.Console | 6.1.1 | Console log output (Warning+ in production) |
| Serilog.Sinks.File | 7.0.0 | Rolling file log output (5MB limit, 5 retained) |
| YamlDotNet | 16.3.0 | YAML serialization for configuration files |

**Testing:**

| Package | Version | Purpose |
|---------|---------|---------|
| coverlet.collector | 8.0.0 | Code coverage collector |
| coverlet.msbuild | 8.0.0 | MSBuild coverage integration (auto-collects on `dotnet test`) |

## Configuration

**Build Configuration:**
- `AutoQAC/AutoQAC.csproj`: `WinExe` output, compiled Avalonia bindings enabled, COM interop enabled
- `AutoQAC/AutoQAC.csproj`: Embeds build date as `AssemblyMetadata` via MSBuild `$([System.DateTime]::UtcNow)`
- `AutoQAC/AutoQAC.csproj`: Copies `AutoQAC Data/` folder to output directory (`PreserveNewest`)
- Debug vs Release: `Avalonia.Diagnostics` is included only in Debug builds

**Application Configuration:**
- `AutoQAC Data/AutoQAC Main.yaml`: Bundled read-only config (skip lists, xEdit executable names, version info)
- `AutoQAC Data/AutoQAC Settings.yaml`: User-editable config (game selection, paths, timeouts, MO2 mode)
- No `.env` files; all configuration is file-based YAML
- Config directory resolution walks up from `AppContext.BaseDirectory` in Debug mode to find the source `AutoQAC Data` folder

**Coverage Configuration (both test projects):**
- Auto-collected via MSBuild properties: `<CollectCoverage>true</CollectCoverage>`
- Format: Cobertura XML
- Output: `./TestResults/coverage/` per test project
- Include/Exclude filters scope coverage to the project under test

## Platform Requirements

**Development:**
- .NET 10 SDK (no `global.json` pins a specific version)
- Windows 10+ (Windows SDK 10.0.19041.0 for the main app)
- `dotnet build AutoQACSharp.slnx` builds all four projects
- `dotnet test AutoQACSharp.slnx` runs tests with auto-coverage
- `dotnet run --project AutoQAC/AutoQAC.csproj` launches the app

**Production:**
- Windows 10 or later (declared in `app.manifest`)
- .NET 10 runtime (framework-dependent deployment)
- Pre-built release in `Release/` directory includes `AutoQAC.exe` plus native Avalonia/Skia dependencies
- Native DLLs bundled: `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`, `D3DCompiler_47_cor3.dll`, WPF interop DLLs

## Mutagen Submodule

- Git submodule at `Mutagen/` pinned to tag `0.53.1` (commit `bdbb6ff`)
- Read-only reference; do not build or modify
- Local docs available in `docs/mutagen/` for fast lookups
- Package references in project files match the submodule version (0.53.1)

## Solution Structure

```
AutoQACSharp.slnx
  AutoQAC/AutoQAC.csproj           - Main desktop app (WinExe, net10.0-windows)
  AutoQAC.Tests/AutoQAC.Tests.csproj - App test project (net10.0-windows)
  QueryPlugins/QueryPlugins.csproj   - Plugin analysis library (net10.0)
  QueryPlugins.Tests/QueryPlugins.Tests.csproj - Library tests (net10.0)
```

**Project References:**
- `AutoQAC` depends on `QueryPlugins`
- `AutoQAC.Tests` depends on both `AutoQAC` and `QueryPlugins`
- `QueryPlugins.Tests` depends on `QueryPlugins`

---

*Stack analysis: 2026-03-30*
