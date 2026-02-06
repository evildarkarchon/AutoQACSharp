# Stack Research

**Domain:** C# desktop application -- Bethesda plugin auto-cleaning via subprocess management
**Researched:** 2026-02-06
**Confidence:** HIGH (all versions verified via NuGet search, Context7, and official docs)

## Current Stack (Already In Use)

These are established and should NOT change. Listed for context only.

| Technology | Version | Purpose | Status |
|------------|---------|---------|--------|
| .NET 10 | net10.0-windows10.0.19041.0 | Runtime & SDK (LTS, GA Nov 2025) | Current |
| C# 12 | - | Language version | Current |
| Avalonia UI | 11.3.11 | Cross-platform XAML UI framework | Current |
| ReactiveUI.Avalonia | 11.3.8 | MVVM framework with reactive bindings | Current |
| Microsoft.Extensions.DependencyInjection | 10.0.2 | IoC container | Current |
| Serilog | 4.3.0 | Structured logging core | Current |
| Serilog.Sinks.Console | 6.1.1 | Console log output | Current |
| Serilog.Sinks.File | 7.0.0 | File log output with rotation | Current |
| YamlDotNet | 16.3.0 | YAML configuration serialization | Current |
| Mutagen.Bethesda | 0.52.0 | Bethesda plugin loading/reading | Current |
| Mutagen.Bethesda.Skyrim | 0.52.0 | Skyrim plugin support | Current |
| Mutagen.Bethesda.Fallout4 | 0.52.0 | Fallout 4 plugin support | Current |
| xunit | 2.9.3 | Unit testing framework | Current |
| xunit.runner.visualstudio | 3.1.5 | VS Test Explorer integration | Current |
| Microsoft.NET.Test.Sdk | 18.0.1 | Test platform infrastructure | Current |
| Moq | 4.20.72 | Mocking framework | Current (see concerns below) |
| FluentAssertions | 8.8.0 | Assertion library | Current |
| coverlet.collector | 6.0.4 | Code coverage collection | Current |

---

## Recommended Additions for New Milestone

### Subprocess & Process Management

| Library | Version | Purpose | Why Recommended | Confidence |
|---------|---------|---------|-----------------|------------|
| Polly | 8.6.5 | Retry/timeout/resilience for xEdit subprocess calls | Standard .NET resilience library. Provides configurable timeout strategies, retry with jitter for transient failures, and circuit breaker patterns. The xEdit process can hang or fail transiently -- Polly wraps `Process.WaitForExitAsync` with proper timeout and retry without custom timer code. Zero-allocation v8 API, built-in telemetry, and deep integration with `Microsoft.Extensions`. | HIGH |
| Polly.Core | 8.6.5 | Standalone resilience pipeline (no legacy API weight) | The `Polly.Core` package is the modern, minimal-dependency package. Use this instead of the full `Polly` package unless you need legacy v7 API compatibility. | HIGH |

**Rationale:** The project currently has hand-rolled timeout logic in `ProcessExecutionService`. Polly standardizes this and adds retry capability for the cases where xEdit fails to start, times out, or returns unexpected exit codes. The `ResiliencePipeline` pattern in v8 is composable and testable.

**What NOT to use:** Do NOT use `Microsoft.Extensions.Resilience` (the ASP.NET-oriented wrapper) -- it pulls in HTTP-specific dependencies and `IHttpClientFactory` infrastructure that are unnecessary for a desktop subprocess app.

### File Monitoring (xEdit Log Files)

| Library | Version | Purpose | Why Recommended | Confidence |
|---------|---------|---------|-----------------|------------|
| `System.IO.FileSystemWatcher` | Built-in (.NET BCL) | Monitor xEdit log file changes in real-time | Built into .NET, no additional dependency. For the use case of watching a single xEdit log file for new output lines, `FileSystemWatcher` is sufficient. The known reliability issues (buffer overflows, missed events under heavy load) apply to scenarios with thousands of files, not single-file monitoring. Wrap it in a service that also polls on a timer as a fallback. | HIGH |

**What NOT to use:**
- `myoddweb.directorywatcher` -- Overkill for single-file monitoring. Adds native C++ dependency complexity.
- `MiniFSWatcher` -- Requires a custom minispy driver. Inappropriate for end-user desktop software.
- Third-party FSW alternatives -- All add complexity that isn't needed for the narrow use case of tailing one log file.

**Recommended pattern:** Use `FileSystemWatcher` + periodic polling hybrid. The watcher provides near-instant notification; a 1-2 second polling fallback catches any missed events. This is the standard .NET pattern for reliable file monitoring.

### Configuration Validation

| Library | Version | Purpose | Why Recommended | Confidence |
|---------|---------|---------|-----------------|------------|
| FluentValidation | 12.1.1 | Validate YAML configuration objects after deserialization | The project already uses FluentAssertions (same "Fluent" family, familiar API style). FluentValidation provides composable validation rules for the `MainConfiguration` and `UserConfiguration` models. Validators are injectable, testable, and produce structured error results suitable for UI display. Minimum supported platform is .NET 8, so it works with .NET 10. | HIGH |
| Microsoft.Extensions.Options.DataAnnotations | 10.0.2 | Data annotation validation for simple config properties | Lightweight alternative for simple property-level validation (Required, Range, RegularExpression). Works with the existing `Microsoft.Extensions.DependencyInjection` infrastructure. Good for path validation, string length checks, and enum range enforcement. | HIGH |

**Recommendation:** Use BOTH. Use `DataAnnotations` for simple property constraints (required fields, path format checks). Use `FluentValidation` for cross-field validation rules (e.g., "if MO2 is enabled, MO2 path must exist") and for rules that need async I/O (file existence checks). This layered approach keeps simple validations declarative while handling complex rules in code.

**What NOT to use:**
- Hand-rolled validation methods scattered across services -- Violates SRP and makes validation rules hard to discover and test.
- `System.ComponentModel.DataAnnotations` directly without the Options integration -- Loses the `ValidateOnStart()` capability.

### Advanced Logging

| Library | Version | Purpose | Why Recommended | Confidence |
|---------|---------|---------|-----------------|------------|
| Serilog.Enrichers.Thread | 4.0.0 | Add thread ID/name to all log events | Essential for debugging subprocess management. When cleaning plugins sequentially, thread context helps distinguish UI thread logs from background task logs. | HIGH |
| Serilog.Enrichers.Process | 3.0.0 | Add process ID to log events | Useful when the application launches xEdit subprocesses. Enriching logs with the parent process ID helps correlate application logs with subprocess activity. | HIGH |
| Serilog.Enrichers.Environment | 2.0.0 | Add machine name and environment info | Useful for bug reports. Users can share log files and the environment context is automatically included. | MEDIUM |

**What NOT to use:**
- `Serilog.Sinks.Seq` or `Serilog.Sinks.Splunk` -- This is a desktop app, not a server. No centralized log aggregation needed.
- `Serilog.Settings.Configuration` -- Adds `Microsoft.Extensions.Configuration` dependency chain. The app uses YAML config, not `appsettings.json`. Configure Serilog programmatically via the fluent API, which is already in use.

### CPU/Resource Monitoring

| Library | Version | Purpose | Why Recommended | Confidence |
|---------|---------|---------|-----------------|------------|
| `System.Diagnostics.Process` | Built-in (.NET BCL) | Monitor xEdit subprocess CPU usage via `TotalProcessorTime` | Built into .NET. `Process.TotalProcessorTime` provides cross-platform CPU time measurement. Sample it at intervals (e.g., every 2 seconds) and compute CPU percentage as `(deltaProcessorTime / deltaWallTime) / processorCount`. No external dependency needed. | HIGH |

**What NOT to use:**
- `PerformanceCounter` -- Windows-only legacy API, deprecated in .NET Core+. Requires special permissions and is unreliable in some environments.
- `System.Diagnostics.Metrics` / `EventCounters` -- Designed for _publishing_ metrics from your own code, not _reading_ external process CPU. Wrong tool for monitoring an xEdit subprocess.
- WMI (`System.Management`) -- Heavy, Windows-only, COM-based. Overkill for reading a single process's CPU.

**Recommended pattern:** Poll `Process.TotalProcessorTime` at 2-second intervals from a `PeriodicTimer`. If the CPU delta is near zero for N consecutive intervals (configurable, e.g., 30 seconds), the subprocess is likely hung. Combine with `Process.Responding` for GUI-based xEdit processes.

### Testing Infrastructure

| Library | Version | Purpose | Why Recommended | Confidence |
|---------|---------|---------|-----------------|------------|
| Verify.Xunit | 31.10.0 | Snapshot testing for complex output verification | xEdit output parsing produces structured results (ITM counts, UDR counts, etc.). Snapshot testing captures expected outputs as `.verified.txt` files and diffs against actual results. Eliminates brittle hand-written assertions for complex objects. Widely adopted in .NET ecosystem (31+ million downloads for core Verify package). Note: Use `Verify.Xunit` (not `Verify.XunitV3` 28.12.0) since the project stays on xUnit v2. | HIGH |
| Microsoft.Extensions.TimeProvider.Testing | 10.2.0 | Fake `TimeProvider` for testing timeout logic | Built by Microsoft, designed for .NET 10. The `FakeTimeProvider` lets you control time in tests -- advance by specific durations, set specific timestamps. Essential for testing timeout behavior in `ProcessExecutionService` and `CleaningService` without waiting real seconds. Integrates with `TimeProvider` (BCL abstract class since .NET 8). | HIGH |
| Avalonia.Headless.XUnit | 11.3.11 | Headless UI testing for Avalonia views | Enables running Avalonia UI tests without a display server. Supports simulating clicks, keyboard input, and verifying visual state. **IMPORTANT CONSTRAINT:** This package requires xunit.core >= 2.4.0 but is NOT compatible with xUnit v3. The project MUST stay on xUnit v2 (2.9.3) until Avalonia ships an `Avalonia.Headless.XUnit.v3` package (tracked in Avalonia issue #18356). | HIGH |

#### xUnit v2 vs v3 Decision

**Recommendation: Stay on xUnit v2 (2.9.3). Do NOT migrate to xUnit v3.**

**Why:**
1. `Avalonia.Headless.XUnit` 11.3.11 depends on `xunit.core >= 2.4.0` and is **incompatible with xUnit v3** due to breaking API changes.
2. There is no `Avalonia.Headless.XUnit.v3` package yet (tracked in [Avalonia #18356](https://github.com/AvaloniaUI/Avalonia/issues/18356)).
3. The project needs headless UI testing for comprehensive coverage. Migrating to v3 now would block this capability.
4. xUnit 2.9.3 is still maintained and receives updates. The v2 runner (`xunit.runner.visualstudio 3.1.5`) supports both v2 and v3 test projects.
5. When Avalonia ships v3 support, migration is straightforward (package swap + attribute rename per the [migration guide](https://xunit.net/docs/getting-started/v3/migration)).

#### Moq Concerns and Recommendation

**Current state:** The project uses Moq 4.20.72, which is the version AFTER the SponsorLink removal. The SponsorLink data-collection code was added in 4.20.0 and removed in subsequent patches after community backlash.

**Recommendation: Keep Moq 4.20.72 for now. Plan migration to NSubstitute 5.3.0 when expanding test coverage.**

**Rationale:**
- Moq 4.20.72 does NOT contain SponsorLink -- it was removed after the controversy.
- Existing tests work. Rewriting them to use NSubstitute is churn with no functional benefit.
- For NEW tests, consider NSubstitute 5.3.0 for its simpler API and absence of trust concerns.
- A full migration is low priority and can be done incrementally as tests are touched.
- If trust is a concern, pin Moq to 4.20.72 and do not update. The project owner is known to have re-introduced controversial changes in the past.

**What NOT to use:**
- `FakeItEasy` -- Less community adoption than NSubstitute, no compelling advantage.
- `Microsoft.Fakes` -- Requires Visual Studio Enterprise. Not viable for open-source projects.

### Dry-Run Mode Support

No additional libraries needed. This is a design pattern, not a library choice.

**Recommended pattern:** Introduce an `ICleaningStrategy` interface with `RealCleaningStrategy` and `DryRunCleaningStrategy` implementations. The dry-run strategy skips `Process.Start()` and returns synthetic results. Register the appropriate strategy based on user configuration. This follows the Strategy pattern and keeps the `CleaningOrchestrator` agnostic to the execution mode.

### Backup/Rollback

No additional libraries needed. Use `System.IO` file copy operations.

**Recommended pattern:** Before cleaning a plugin, copy it to a timestamped backup location (e.g., `AutoQAC Data/Backups/{timestamp}/{pluginName}`). After cleaning, verify the output file is valid (non-zero size, basic header check via Mutagen). If verification fails, restore from backup. Use a `BackupService` that manages retention (e.g., keep last N backups per plugin).

**What NOT to use:**
- Git-based backup (libgit2sharp) -- Overkill for binary files. Git is terrible with large binary diffs.
- Shadow copy / Volume Shadow Copy Service (VSS) -- Windows-only, requires elevated permissions, far too complex for this use case.

---

## Alternatives Considered

| Category | Recommended | Alternative | Why Not Alternative |
|----------|-------------|-------------|---------------------|
| Resilience | Polly.Core 8.6.5 | Hand-rolled retry loops | Polly is battle-tested, configurable, and handles edge cases (jitter, circuit breaking). Hand-rolled code inevitably misses corner cases. |
| Config validation | FluentValidation 12.1.1 | Custom validation methods | FluentValidation separates validation rules from models, is unit-testable, and produces structured error messages for UI display. |
| Config validation (simple) | DataAnnotations 10.0.2 | FluentValidation alone | DataAnnotations handles simple property constraints declaratively. Using FluentValidation for `[Required]`-level checks is overkill. |
| File monitoring | FileSystemWatcher (BCL) | myoddweb.directorywatcher | Single-file monitoring doesn't need the reliability guarantees that justify a third-party FSW. Polling fallback covers the gap. |
| CPU monitoring | Process.TotalProcessorTime (BCL) | PerformanceCounter | PerformanceCounter is deprecated in .NET Core+, requires special permissions, and is Windows-only legacy. |
| Mocking | Moq 4.20.72 (existing) / NSubstitute 5.3.0 (new) | FakeItEasy | NSubstitute has better community momentum post-Moq controversy and a cleaner API. FakeItEasy is viable but less popular. |
| Snapshot testing | Verify.Xunit | Manual assertion of complex objects | Verify reduces assertion code by 80%+ for structured output verification and catches regressions in output format. |
| Time testing | TimeProvider.Testing 10.2.0 | Custom `IClock` interface | Microsoft's `TimeProvider` is the BCL standard since .NET 8. Using it means no custom abstraction, and `FakeTimeProvider` is purpose-built for testing. |
| xUnit version | xUnit v2 (2.9.3) | xUnit v3 (3.2.2) | Avalonia.Headless.XUnit is incompatible with v3. No Headless.XUnit.v3 package exists yet. |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| xUnit v3 (3.2.2) | Incompatible with Avalonia.Headless.XUnit. Would block headless UI testing. | xUnit v2 (2.9.3) -- stay on current version. |
| PerformanceCounter | Deprecated in .NET Core+. Requires special permissions. Windows-only legacy API. | `Process.TotalProcessorTime` from BCL. |
| Microsoft.Extensions.Resilience | Pulls in HTTP client infrastructure (`IHttpClientFactory`, etc.) designed for ASP.NET Core. Unnecessary dependency weight for a desktop app managing subprocesses. | `Polly.Core` 8.6.5 directly. |
| Serilog.Settings.Configuration | Adds Microsoft.Extensions.Configuration dependency chain. The app uses YAML config, not appsettings.json. | Configure Serilog programmatically via fluent API (already in use). |
| MiniFSWatcher / myoddweb.directorywatcher | Add native code dependencies for a single-file monitoring use case. | `FileSystemWatcher` from BCL with polling fallback. |
| WMI (System.Management) | COM-based, heavy, slow. Requires elevated permissions for some queries. | `Process.TotalProcessorTime` for CPU, `Process.WorkingSet64` for memory. |
| libgit2sharp for backups | Git is terrible with large binary files. Massive complexity for simple file copy. | `System.IO.File.Copy` to a timestamped backup directory. |
| Microsoft.Fakes | Requires Visual Studio Enterprise license. | Moq 4.20.72 / NSubstitute 5.3.0. |

---

## Version Compatibility Matrix

| Package A | Compatible With | Notes |
|-----------|-----------------|-------|
| Avalonia 11.3.11 | ReactiveUI.Avalonia 11.3.8 | Versions are coordinated but not identical. 11.3.x is compatible. |
| Avalonia 11.3.11 | Avalonia.Headless.XUnit 11.3.11 | Must match Avalonia major.minor.patch for headless testing. |
| Avalonia.Headless.XUnit 11.3.11 | xunit 2.9.3 | Requires xunit.core >= 2.4.0. NOT compatible with xunit.v3. |
| xunit 2.9.3 | xunit.runner.visualstudio 3.1.5 | Runner 3.x supports both v2 and v3 test projects. |
| Polly.Core 8.6.5 | .NET 10 | Targets .NET Standard 2.0+. Full compatibility. |
| FluentValidation 12.1.1 | .NET 10 | Minimum supported platform: .NET 8. |
| Microsoft.Extensions.TimeProvider.Testing 10.2.0 | .NET 10 | Follows .NET major version. 10.x for .NET 10. |
| Serilog 4.3.0 | Serilog.Enrichers.Thread 4.0.0 | Both target .NET 6.0+. Compatible. |
| Serilog 4.3.0 | Serilog.Enrichers.Process 3.0.0 | Both target .NET 6.0+. Compatible. |
| Verify.Xunit 31.10.0 | xunit 2.9.3 | Verify.Xunit targets xUnit v2. Separate `Verify.XunitV3` (28.12.0) exists for v3. |
| NSubstitute 5.3.0 | .NET 10 | Targets .NET 6.0 and .NET Standard 2.0. Compatible. |

---

## Installation

```xml
<!-- New packages to add to AutoQAC.csproj -->
<PackageReference Include="Polly.Core" Version="8.6.5" />
<PackageReference Include="FluentValidation" Version="12.1.1" />
<PackageReference Include="Serilog.Enrichers.Thread" Version="4.0.0" />
<PackageReference Include="Serilog.Enrichers.Process" Version="3.0.0" />

<!-- New packages to add to AutoQAC.Tests.csproj -->
<PackageReference Include="Verify.Xunit" Version="31.10.0" />
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.2.0" />
<PackageReference Include="Avalonia.Headless.XUnit" Version="11.3.11" />
```

```bash
# Core project
dotnet add AutoQAC/AutoQAC.csproj package Polly.Core --version 8.6.5
dotnet add AutoQAC/AutoQAC.csproj package FluentValidation --version 12.1.1
dotnet add AutoQAC/AutoQAC.csproj package Serilog.Enrichers.Thread --version 4.0.0
dotnet add AutoQAC/AutoQAC.csproj package Serilog.Enrichers.Process --version 3.0.0

# Test project
dotnet add AutoQAC.Tests/AutoQAC.Tests.csproj package Verify.Xunit
dotnet add AutoQAC.Tests/AutoQAC.Tests.csproj package Microsoft.Extensions.TimeProvider.Testing --version 10.2.0
dotnet add AutoQAC.Tests/AutoQAC.Tests.csproj package Avalonia.Headless.XUnit --version 11.3.11
```

---

## Stack Patterns by Capability

**If adding deferred config saves:**
- Use `System.Reactive` (already available via ReactiveUI) to debounce save operations
- Pattern: `configChanges.Throttle(TimeSpan.FromSeconds(2)).Subscribe(SaveConfig)`
- No new library needed -- ReactiveUI already brings in System.Reactive

**If adding real-time progress callbacks:**
- Use `IProgress<T>` (already in the interface) with a structured progress type
- Pattern: `IProgress<CleaningProgress>` where `CleaningProgress` is a record with percentage, message, phase
- No new library needed -- BCL `IProgress<T>` is the standard

**If adding robust process termination:**
- Use `Process.CloseMainWindow()` first, then `Process.Kill(entireProcessTree: true)` as fallback
- Wrap in Polly timeout strategy for configurable wait duration between graceful and forceful termination
- Pattern: Try `CloseMainWindow()`, wait 5s via Polly timeout, then `Kill(true)`

**If adding log file monitoring:**
- Use `FileSystemWatcher` on the xEdit log directory + `StreamReader` tailing
- Pattern: `FileSystemWatcher.Changed` event triggers `StreamReader.ReadLine()` loop from last position
- Fallback: `PeriodicTimer` polling every 1-2 seconds reads any new content

**If adding CPU monitoring:**
- Use `Process.TotalProcessorTime` sampled via `PeriodicTimer`
- Pattern: Sample every 2s, compute CPU% = `deltaProcessorTime / (deltaWallTime * processorCount)`
- Alert if CPU stays below 1% for 30+ consecutive seconds (configurable, indicates hung process)

**If adding config validation UI:**
- Use FluentValidation validators executed when the user clicks "Save" or "Apply"
- Display validation errors in an `ItemsControl` bound to a `ValidationResult.Errors` collection
- Pattern: ViewModel calls `validator.ValidateAsync(config)` and populates `ObservableCollection<string> ValidationErrors`

---

## Sources

- [NuGet Gallery - Polly 8.6.5](https://www.nuget.org/packages/polly/) -- Version verified 2026-02-06
- [NuGet Gallery - Polly.Core 8.6.5](https://www.nuget.org/packages/polly.core/) -- Version verified 2026-02-06
- [NuGet Gallery - FluentValidation 12.1.1](https://www.nuget.org/packages/fluentvalidation/) -- Version verified 2026-02-06
- [NuGet Gallery - Serilog.Enrichers.Thread 4.0.0](https://www.nuget.org/packages/serilog.enrichers.thread) -- Version verified 2026-02-06
- [NuGet Gallery - Serilog.Enrichers.Process 3.0.0](https://www.nuget.org/packages/Serilog.Enrichers.Process/3.0.0) -- Version verified 2026-02-06
- [NuGet Gallery - Verify.Xunit 31.10.0](https://www.nuget.org/packages/Verify.Xunit/) -- Version verified 2026-02-06
- [NuGet Gallery - Microsoft.Extensions.TimeProvider.Testing 10.2.0](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing/) -- Version verified 2026-02-06
- [NuGet Gallery - Avalonia.Headless.XUnit 11.3.11](https://www.nuget.org/packages/Avalonia.Headless.XUnit) -- Version verified 2026-02-06
- [NuGet Gallery - NSubstitute 5.3.0](https://www.nuget.org/packages/nsubstitute/) -- Version verified 2026-02-06
- [NuGet Gallery - Microsoft.Extensions.Options.DataAnnotations 10.0.2](https://www.nuget.org/packages/Microsoft.Extensions.Options.DataAnnotations/) -- Version verified 2026-02-06
- [NuGet Gallery - xunit 2.9.3](https://www.nuget.org/packages/xunit) -- Current project version confirmed
- [NuGet Gallery - xunit.v3 3.2.2](https://www.nuget.org/packages/xunit.v3) -- Latest v3, NOT recommended due to Avalonia incompatibility
- [Avalonia #18356 - Consider adding Avalonia.Headless.XUnit.v3](https://github.com/AvaloniaUI/Avalonia/issues/18356) -- xUnit v3 headless testing blocked
- [xUnit v3 What's New](https://xunit.net/docs/getting-started/v3/whats-new) -- v3 feature reference
- [xUnit v3 Migration Guide](https://xunit.net/docs/getting-started/v3/migration) -- For future migration when Avalonia supports v3
- [Serilog Wiki - Enrichment](https://github.com/serilog/serilog/wiki/Enrichment) -- Enricher documentation (Context7 verified)
- [Serilog Wiki - Configuration Basics](https://github.com/serilog/serilog/wiki/Configuration-Basics) -- Sink configuration (Context7 verified)
- [Microsoft Learn - EventCounters in .NET Core](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/event-counters) -- CPU monitoring alternatives
- [Microsoft Learn - Migrate from Windows Performance Counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/migrate-from-windows-performance-counters) -- PerformanceCounter deprecation
- [Microsoft Learn - Process.Kill Method](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-9.0) -- Process termination API
- [Microsoft Learn - Options Pattern](https://learn.microsoft.com/en-us/dotnet/core/extensions/options) -- IOptions configuration pattern
- [Avalonia Docs - Headless Testing with XUnit](https://docs.avaloniaui.net/docs/concepts/headless/headless-xunit) -- Headless testing setup
- [.NET 10 Release](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/) -- .NET 10 GA confirmation

---
*Stack research for: AutoQACSharp milestone -- subprocess management, file monitoring, configuration, testing*
*Researched: 2026-02-06*
