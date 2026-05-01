# Phase 11: User-Facing Diagnostics Boundaries - Research

**Researched:** 2026-04-30  
**Domain:** .NET/Avalonia desktop diagnostics, redacted structured logging, user-facing error boundaries  
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Error wording
- **D-01:** Unexpected cleaning and preview failures use operation-specific safe copy plus latest AutoQAC log guidance. The message should identify the failed operation and next action without raw exception text, stack traces, full paths, or command fragments.
- **D-02:** Modal dialog details for unexpected technical failures are safe details only. Details may repeat next-action/latest-log guidance, but must not contain stack traces, exception messages, paths, or command fragments.
- **D-03:** Status text after unexpected failures uses short operation status, such as cleaning/preview failed plus latest-log guidance. Do not put `ex.Message` into status text.
- **D-04:** Preserve existing safe typed labels from prior phases, especially `ConfigPersistenceFailure.SafeSummary` and `BackupFailureReason` display labels. Do not flatten those already-safe, user-actionable categories into generic text.

#### Path identifiers
- **D-05:** Missing configured executable and load-order validation text should show the setting/resource plus a safe basename, such as `xEdit Path (SSEEdit.exe)` or `Load Order File (plugins.txt)`, never the directory path.
- **D-06:** Selected folder problems should show the game plus folder label, such as `Skyrim SE data folder` or `selected game data folder`, instead of a full folder path.
- **D-07:** Basenames shown in user-facing text must be sanitized display names. Remove or neutralize control characters and command-like formatting while preserving useful names such as `Plugin.esp`.
- **D-08:** Latest-log guidance appears on path-related surfaces only for technical failures such as parse/read/unexpected errors. Simple missing-path validation should provide the safe identifier and the fix action without extra log guidance.

#### Result exports
- **D-09:** Failed plugin rows and exported reports show plugin filename plus a safe failure summary/status and latest-log guidance. Counts, durations, and other already-safe facts may remain.
- **D-10:** Failed `PluginCleaningResult.Message` values must be sanitized at source before they reach result windows or `CleaningSessionResult.GenerateReport()`. Report/export code may still keep defensive checks, but source-created result messages are the primary safety boundary.
- **D-11:** xEdit exception-log detection appears as a safe xEdit failure for the plugin, such as `xEdit reported an error for Plugin.esp. See the latest AutoQAC log.` Do not surface exception-log content in result rows or reports.
- **D-12:** Exported cleaning reports should include one short disclaimer that technical details are intentionally kept in AutoQAC logs, not repeated in exported reports.

#### Log redaction
- **D-13:** Process-start and startup logs replace full executable paths and command payloads with structured safe fields: operation, launch mode, game, plugin filename, PID when available, argument count, counts/status, and safe reason/category.
- **D-14:** AutoQAC logs may keep a full local path only when that path is the direct file/folder resource that failed and omitting it would materially reduce local troubleshooting value.
- **D-15:** Normal workflow logs should identify plugins by filename plus game/mode context, not by full plugin path. A full plugin path is allowed only when the plugin file path itself is the direct failing resource.
- **D-16:** Phase 11 should prove log boundaries with captured logger tests or equivalent behavior/source assertions that fail on full executable paths, raw argv/nested payloads, and command fragments in startup/process-start diagnostics.

### the agent's Discretion
- Exact helper/service names, exact message strings, exact placeholder wording, and whether sanitization is implemented through a shared formatter or narrowly scoped helpers are planner discretion as long as D-01 through D-16 and `11-SPEC.md` are preserved.
- Exact test file organization is planner discretion, but tests must cover the surfaces named in `11-SPEC.md` and the log assertions in D-16.

### Deferred Ideas (OUT OF SCOPE)
None - discussion stayed within phase scope. New diagnostics windows, log viewers, crash reporting, telemetry, issue-report export, and SEC-03 executable-name warnings remain out of scope per `11-SPEC.md`.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SEC-01 | User sees concise error dialogs with log-file references instead of stack traces or excessive internal path detail. [VERIFIED: `.planning/REQUIREMENTS.md`] | Use an exception-to-safe-copy boundary in ViewModels/dialog services; preserve technical exceptions only in `ILoggingService`; use latest-log guidance without full log path. [VERIFIED: `11-SPEC.md`; VERIFIED: codebase read] |
| SEC-02 | User diagnostic logs avoid unnecessary full command-line/path exposure while preserving enough information for local troubleshooting. [VERIFIED: `.planning/REQUIREMENTS.md`] | Use Serilog structured placeholders with safe values, not reconstructed commands; keep full paths only for direct failing resources; assert process/startup logs do not include raw executable paths or argv payloads. [CITED: https://github.com/serilog/serilog/blob/dev/README.md; VERIFIED: `11-CONTEXT.md`] |
</phase_requirements>

## Summary

Phase 11 should be implemented as a diagnostics-boundary hardening pass, not a new diagnostics subsystem. [VERIFIED: `11-SPEC.md`] The established pattern in this codebase is: services log technical details through `ILoggingService`, services return typed result models where possible, ViewModels map those results into concise user-facing text, and views/code-behind own window/dialog mechanics through existing interaction/dialog services. [VERIFIED: `.planning/codebase/ARCHITECTURE.md`; VERIFIED: codebase read]

The primary unknown to avoid is assuming a single regex redactor can safely clean all outputs after the fact. [CITED: https://learn.microsoft.com/dotnet/core/extensions/data-redaction] The safer architecture is source-boundary classification: create safe display values at the point where raw paths/exceptions/commands cross into UI/export/log-message templates, then add defensive tests for each surface. [VERIFIED: `11-CONTEXT.md`; VERIFIED: codebase read] For AutoQAC, that means basenames for user-visible file identifiers, safe category labels for known failures, generic latest-log guidance for unexpected technical failures, and structured log properties such as operation/game/mode/plugin filename/PID/argument count instead of executable paths or argv strings. [VERIFIED: `11-CONTEXT.md`; CITED: https://learn.microsoft.com/dotnet/api/system.io.path.getfilename?view=net-10.0; CITED: https://github.com/serilog/serilog/blob/dev/README.md]

**Primary recommendation:** Add a small diagnostics formatting boundary in `AutoQAC/Services/UI` or a focused diagnostics folder, wire it through existing ViewModel/service seams, and prove every known leak path with negative-disclosure tests that include realistic `C:\Users\...`, game-install, xEdit, MO2, plugin-path, stack-trace, and command-fragment sentinels. [VERIFIED: `11-SPEC.md`; VERIFIED: codebase read]

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Exception-to-user-message mapping | ViewModels / UI services | Application services | Existing ViewModels catch command-boundary exceptions and `IMessageDialogService` owns dialog output; services should provide typed safe summaries where recoverable. [VERIFIED: `CleaningCommandsViewModel.cs`; VERIFIED: `IMessageDialogService.cs`] |
| Path display identifiers | Application services / UI formatting helper | ViewModels | `Path.GetFileName` is the standard .NET API for deriving a filename from a path, but display sanitization must happen before values enter dialogs/status/reports. [CITED: https://learn.microsoft.com/dotnet/api/system.io.path.getfilename?view=net-10.0; VERIFIED: `11-CONTEXT.md`] |
| Process/startup log boundary | Application services / infrastructure logging callers | Logging infrastructure | Serilog should receive already-safe structured properties; `ProcessExecutionService` and `App.axaml.cs` currently own the unsafe process/startup log call sites. [VERIFIED: `ProcessExecutionService.cs`; VERIFIED: `App.axaml.cs`; CITED: https://github.com/serilog/serilog/blob/dev/README.md] |
| Export/report sanitization | Domain models / cleaning result finalizer | ViewModels | `PluginResultFinalizer` is the source boundary for `PluginCleaningResult.Message`, while `CleaningSessionResult.GenerateReport()` is the defensive export boundary. [VERIFIED: `PluginResultFinalizer.cs`; VERIFIED: `CleaningSessionResult.cs`] |
| Regression tests | Test projects | Source-level guards where behavior is hard to observe | Existing tests are xUnit/FluentAssertions/NSubstitute; Phase 11 acceptance requires tests for dialogs, status, validation rows, reports, startup logs, and process logs. [VERIFIED: `AutoQAC.Tests` list; VERIFIED: `11-SPEC.md`] |

## Project Constraints (from AGENTS.md)

- AutoQAC is a Windows-only Avalonia desktop app that runs xEdit Quick Auto Clean one plugin at a time. [VERIFIED: `AGENTS.md`]
- Preserve sequential cleaning; do not parallelize plugin cleaning or xEdit launches. [VERIFIED: `AGENTS.md`]
- Preserve `ProcessExecutionService`'s single process slot. [VERIFIED: `AGENTS.md`; VERIFIED: `ProcessExecutionService.cs`]
- Preserve two-stage stop behavior: graceful cancellation first, then force termination if needed. [VERIFIED: `AGENTS.md`]
- Maintain strict MVVM boundaries; `MainWindow.axaml.cs` owns dialog/window interactions and ViewModels must not manipulate controls directly. [VERIFIED: `AGENTS.md`; VERIFIED: `.planning/codebase/ARCHITECTURE.md`]
- Use CommunityToolkit.Mvvm source generators for ViewModel state and commands; ViewModels using generators must be `partial`. [VERIFIED: `AGENTS.md`; CITED: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/observableproperty]
- Do not use ReactiveUI or `System.Reactive` in the ViewModel layer; service `IObservable<T>` streams are subscribed through `CallbackObserver<T>` and marshaled with `IUiDispatcher`. [VERIFIED: `AGENTS.md`]
- Keep I/O and process work async; never block the UI thread with `.Result` or `.Wait()`. [VERIFIED: `AGENTS.md`]
- Use constructor injection through `ServiceCollectionExtensions`; avoid static mutable state and service locators. [VERIFIED: `AGENTS.md`]
- Do not modify `Mutagen/`; treat it as read-only. [VERIFIED: `AGENTS.md`]
- Use NSubstitute for mocks and match optional parameters explicitly in substitute setups/assertions. [VERIFIED: `AGENTS.md`]
- There is no Avalonia.Headless test project; do not document or depend on one unless intentionally added. [VERIFIED: `AGENTS.md`; VERIFIED: `.planning/codebase/CONCERNS.md`]
- Preserve comments; add XML docs to new or substantially rewritten methods unless trivial/private. [VERIFIED: global `AGENTS.md`; VERIFIED: project `CONVENTIONS.md`]

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK / C# | SDK 10.0.203; target `net10.0-windows10.0.19041.0` | Runtime, `Path`, `ProcessStartInfo`, async process/file APIs | Existing project target and installed SDK; .NET provides `Path.GetFileName`, invalid filename character APIs, and `ProcessStartInfo.ArgumentList` semantics needed for safe diagnostics. [VERIFIED: `dotnet --version`; VERIFIED: `dotnet list package`; CITED: https://learn.microsoft.com/dotnet/api/system.io.path.getfilename?view=net-10.0; CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] |
| Avalonia | 12.0.1 in repo; NuGet latest 12.0.2 as of lookup | Desktop UI and dialog/window surfaces | Existing app framework; Avalonia recommends commands for testable application logic and code-behind/events for UI-specific behavior. [VERIFIED: `dotnet list package`; VERIFIED: NuGet search API; CITED: https://docs.avaloniaui.net/docs/input-interaction/adding-interactivity] |
| CommunityToolkit.Mvvm | 8.4.2 | Observable properties, relay commands, testable ViewModels | Existing MVVM stack; official docs require `partial` classes for source-generated observable properties and relay commands. [VERIFIED: `dotnet list package`; VERIFIED: NuGet search API; CITED: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/observableproperty; CITED: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/relaycommand] |
| Serilog + file/console sinks | Serilog 4.3.1; Console 6.1.1; File 7.0.0 | Structured local diagnostic logs | Existing logging stack; Serilog message templates capture named structured properties and support destructuring policies/limits. [VERIFIED: `dotnet list package`; VERIFIED: NuGet search API; CITED: https://github.com/serilog/serilog/blob/dev/README.md] |
| xUnit + FluentAssertions + NSubstitute | xUnit 2.9.3; FluentAssertions 8.8.0; NSubstitute 5.3.0 | Regression tests for UI text, exports, and log calls | Existing test stack; xUnit supports `[Fact]`/`[Theory]`, and project tests already use substitutes/captured assertions. [VERIFIED: `dotnet list package`; VERIFIED: test list; CITED: https://context7.com/xunit/xunit/llms.txt] |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.IO.Path` | .NET 10 | Safe basename extraction and invalid-character checks | Use for file/executable/plugin display identifiers; `Path.GetFileName` returns characters after the last directory separator. [CITED: https://learn.microsoft.com/dotnet/api/system.io.path.getfilename?view=net-10.0] |
| `ProcessStartInfo.ArgumentList` | .NET 10 | Preserve argv intent without reconstructing a command string | Keep launch behavior unchanged and summarize argument count in logs; `ArgumentList` escapes supplied arguments and should be preferred over hand-escaped `Arguments` when unsure. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] |
| `Microsoft.Extensions.Compliance.Redaction` | Not installed; NuGet/package docs current for .NET 10 | Optional future classification/redaction pipeline | Do not add for Phase 11 unless a plan explicitly wants a broader centralized redaction subsystem; Microsoft docs frame it as useful for logs/error messages but integration would expand scope beyond known surfaces. [CITED: https://learn.microsoft.com/dotnet/core/extensions/data-redaction; VERIFIED: `11-SPEC.md` out-of-scope] |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Existing `ILoggingService` + safe structured fields | Microsoft.Extensions.Telemetry redaction pipeline | More systematic classification, but adds packages/configuration and is aimed at broader telemetry; Phase 11 only needs known local AutoQAC surfaces. [CITED: https://learn.microsoft.com/dotnet/core/extensions/data-redaction; VERIFIED: `11-SPEC.md`] |
| Existing `IMessageDialogService` and interactions | New diagnostics window/log viewer | Explicitly out of scope; would add UI surface and testing burden. [VERIFIED: `11-SPEC.md`] |
| xUnit ViewModel/service tests | New Avalonia.Headless project | Not present and explicitly not to be assumed; Phase 11 can assert dialog service calls and bound state without rendered UI tests. [VERIFIED: `AGENTS.md`; VERIFIED: `.planning/codebase/CONCERNS.md`] |

**Installation:**
```bash
# No package install is recommended for Phase 11.
dotnet test AutoQACSharp.slnx
```

**Version verification:** `dotnet list AutoQACSharp.slnx package` verified repository package versions; NuGet search API verified current package versions: Avalonia latest stable 12.0.2, CommunityToolkit.Mvvm latest stable 8.4.2, Serilog latest stable 4.3.1, xUnit package 2.9.3 with NuGet deprecation message pointing future feature work to `xunit.v3`. [VERIFIED: `dotnet list package`; VERIFIED: NuGet search API]

## Architecture Patterns

### System Architecture Diagram

```text
Raw failure / diagnostic source
  ├─ caught exception at ViewModel command boundary
  ├─ typed service failure result
  ├─ process launch/startup context
  └─ xEdit log/exception-log parse result
        │
        ▼
Classify diagnostic intent
  ├─ user action needed? ───────────────┐
  ├─ direct failing local resource? ────┼─► full path allowed in local log only when needed
  ├─ known safe category? ──────────────┤
  └─ unexpected technical detail? ──────┘
        │
        ▼
Safe formatter boundary
  ├─ sanitized basename / game label / operation label
  ├─ safe category summary
  ├─ latest AutoQAC log guidance (no full log path)
  └─ structured safe log fields (operation, game, mode, PID, counts)
        │
        ├─► Dialog/status/validation/export text (never stack/path/argv/raw ex text)
        └─► Serilog event (technical exception object allowed, unsafe message properties avoided)
```

### Recommended Project Structure

```text
AutoQAC/
├── Services/UI/                 # Existing dialog service and UI-safe formatter seam
│   └── DiagnosticTextFormatter.cs # Recommended focused helper for safe UI/export text
├── Services/Process/            # Process/startup log call sites keep launch behavior intact
├── Services/Cleaning/           # Source sanitization for PluginCleaningResult messages
├── Models/                      # Defensive report/export boundary in CleaningSessionResult
└── ViewModels/                  # Map catches/results into safe text, no control manipulation

AutoQAC.Tests/
├── ViewModels/                  # Dialog/status/validation leak regression tests
├── Models/                      # Cleaning report/export leak regression tests
└── Services/                    # Process/startup log and result-finalizer regression tests
```

### Pattern 1: Safe Exception Boundary

**What:** Catch unexpected exceptions at command/startup/session-load boundaries, log the exception object, but show only operation-specific safe copy plus latest-log guidance. [VERIFIED: `11-SPEC.md`; VERIFIED: `CleaningCommandsViewModel.cs`]  
**When to use:** `StartCleaningAsync`, `PreviewAsync`, session loading, migration startup catch blocks, and any user-facing catch where `ex.Message` or `ex.StackTrace` could contain paths/commands. [VERIFIED: codebase read]

**Example:**
```csharp
// Source: Phase 11 SPEC + existing ILoggingService/IMessageDialogService patterns.
catch (Exception ex)
{
    _logger.Error(ex, "{Operation} failed unexpectedly", "Preview");
    StatusText = "Preview failed. See the latest AutoQAC log for technical details.";
    await _messageDialog.ShowErrorAsync(
        "Preview Failed",
        "Preview failed. See the latest AutoQAC log for technical details.",
        "Technical details were written to the latest AutoQAC log.");
}
```

### Pattern 2: Safe Display Identifier Boundary

**What:** Convert raw paths to a setting/resource label plus sanitized basename before text crosses into dialogs/status/validation/export. [VERIFIED: `11-CONTEXT.md`]  
**When to use:** xEdit/MO2 executable paths, load-order paths, plugin paths, and selected file identifiers. For folders, use game/folder labels rather than basenames if the basename could still reveal profile/install layout. [VERIFIED: `11-CONTEXT.md`]

**Example:**
```csharp
// Source: Path.GetFileName docs + Phase 11 D-05/D-07.
public static string SafeFileIdentifier(string label, string? path)
{
    var basename = Path.GetFileName(path) ?? string.Empty;
    var cleaned = new string(basename
        .Where(ch => !char.IsControl(ch) && ch != '"' && ch != '`')
        .ToArray())
        .Trim();

    return string.IsNullOrWhiteSpace(cleaned) ? label : $"{label} ({cleaned})";
}
```

### Pattern 3: Structured Safe Log Fields

**What:** Log fields that are useful and safe (`Operation`, `LaunchMode`, `Game`, `Plugin`, `ArgumentCount`, `Pid`, `Status`, `Reason`) instead of `FileName`, `Arguments`, nested MO2 payloads, or reconstructed command lines. [VERIFIED: `11-CONTEXT.md`; CITED: https://github.com/serilog/serilog/blob/dev/README.md]

**Example:**
```csharp
// Source: Serilog message-template docs + ProcessStartInfo.ArgumentList docs.
logger.Information(
    "Starting external tool for {Operation}: mode={LaunchMode}, plugin={Plugin}, arguments={ArgumentCount}",
    "CleanPlugin",
    launchMode,
    safePluginFileName,
    startInfo.ArgumentList.Count > 0 ? startInfo.ArgumentList.Count : null);
```

### Pattern 4: Source Sanitization + Defensive Export Check

**What:** `PluginResultFinalizer` should produce safe `PluginCleaningResult.Message` values, and `CleaningSessionResult.GenerateReport()` should include a disclaimer and defensively avoid unsafe details in failed rows. [VERIFIED: `11-CONTEXT.md`; VERIFIED: `PluginResultFinalizer.cs`; VERIFIED: `CleaningSessionResult.cs`]  
**When to use:** Every failed result row, xEdit exception-log result, timed-out/max-retry result, and generated report. [VERIFIED: `11-SPEC.md`]

### Anti-Patterns to Avoid

- **Raw `ex.Message` in status/dialog/details:** exception messages often include paths or command text; Phase 11 specifically forbids this on unexpected user-facing surfaces. [VERIFIED: `11-SPEC.md`; VERIFIED: `CleaningCommandsViewModel.cs`]
- **Stack trace in dialog details:** stack traces are technical diagnostics for logs, not user-facing details. [VERIFIED: `11-SPEC.md`]
- **Full command reconstruction in logs:** `ArgumentList` and `Arguments` have separate semantics, and command string reconstruction risks both disclosure and escaping mistakes. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0; VERIFIED: Phase 6 context in `11-CONTEXT.md`]
- **Global regex redaction as the only defense:** post-processing can miss structured properties, future messages, or exception strings; classify and format safe values at source and use tests as regression guards. [CITED: https://learn.microsoft.com/dotnet/core/extensions/data-redaction; VERIFIED: `11-SPEC.md`]
- **New UI infrastructure:** new diagnostics windows/log viewers are out of scope. [VERIFIED: `11-SPEC.md`]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Filename extraction | Custom slash/backslash parser | `Path.GetFileName` plus display sanitization | .NET documents platform path separator behavior; custom parsers miss volume/alternate separator edge cases. [CITED: https://learn.microsoft.com/dotnet/api/system.io.path.getfilename?view=net-10.0] |
| Command-line rendering | A reconstructed xEdit/MO2 command string | `ProcessStartInfo.ArgumentList` for launch and log only argument count/safe context | `ArgumentList` escapes arguments and is independent from `Arguments`; reconstructing strings reintroduces payload disclosure. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] |
| Dialog/window framework | New diagnostics window/log viewer | Existing `IMessageDialogService` and ViewModel interactions | Project already has the dialog boundary, and new diagnostics surfaces are out of scope. [VERIFIED: `IMessageDialogService.cs`; VERIFIED: `11-SPEC.md`] |
| Failure taxonomies already typed | Generic catch-all error labels | `ConfigPersistenceFailure.SafeSummary` and `BackupFailureReason` display labels | Locked decision D-04 requires preserving already-safe category labels. [VERIFIED: `11-CONTEXT.md`] |
| End-to-end UI renderer tests | New Avalonia.Headless dependency by default | ViewModel/service tests with substituted dialog/logging services | No Avalonia.Headless project exists; tests can capture strings at the surfaces Phase 11 changes. [VERIFIED: `AGENTS.md`; VERIFIED: test list] |

**Key insight:** Phase 11 is about controlling boundary values, not inventing a universal scrubber. [VERIFIED: `11-SPEC.md`] The planner should prefer small, named helper methods and targeted tests over a broad logging pipeline rewrite. [VERIFIED: codebase architecture; CITED: https://learn.microsoft.com/dotnet/core/extensions/data-redaction]

## Common Pitfalls

### Pitfall 1: `Exception.Message` Looks User-Friendly Until It Contains Paths
**What goes wrong:** Status text or dialog details leak `C:\Users\...`, game install folders, xEdit/MO2 paths, plugin paths, or command fragments. [VERIFIED: `11-SPEC.md`; VERIFIED: codebase read]  
**Why it happens:** Framework and service exceptions frequently embed resource names or paths. [VERIFIED: codebase read]  
**How to avoid:** Log exception objects and map user text through operation/category-specific safe copy. [VERIFIED: `.planning/codebase/ARCHITECTURE.md`]  
**Warning signs:** Any `StatusText = ... ex.Message`, dialog details containing `ex.StackTrace`, or report output using raw failure strings. [VERIFIED: `CleaningCommandsViewModel.cs`; VERIFIED: `CleaningSessionResult.cs`]

### Pitfall 2: Logs Accidentally Reconstruct the Thing UI Stopped Showing
**What goes wrong:** Process logs avoid UI leaks but still write full `FileName`, raw `Arguments`, or nested MO2 payloads. [VERIFIED: `ProcessExecutionService.cs`; VERIFIED: `11-SPEC.md`]  
**Why it happens:** Structured logging templates can still store unsafe values if caller-provided properties are unsafe. [CITED: https://github.com/serilog/serilog/blob/dev/README.md]  
**How to avoid:** Pass safe structured fields only; use exception objects for local technical diagnostics, but avoid unsafe message-template properties unless direct failing-resource exception context requires it. [VERIFIED: `11-CONTEXT.md`]  
**Warning signs:** Log templates with `{FileName}`, `{Arguments}`, `{ConfiguredPath}`, `{Content}` for xEdit exception-log content, or string interpolation before logging. [VERIFIED: codebase read]

### Pitfall 3: Sanitizing Reports Too Late
**What goes wrong:** Result rows remain unsafe in UI even if export code strips text later. [VERIFIED: `11-CONTEXT.md`]  
**Why it happens:** `GenerateReport()` writes `result.Message`, so unsafe messages already escaped the service boundary. [VERIFIED: `CleaningSessionResult.cs`]  
**How to avoid:** Sanitize `PluginCleaningResult.Message` at source in finalization/creation paths and add defensive report checks. [VERIFIED: `11-CONTEXT.md`]  
**Warning signs:** Tests only cover `GenerateReport()` but not the result model or finalizer path. [VERIFIED: `11-SPEC.md`]

### Pitfall 4: Basename Is Not Always Safe Enough
**What goes wrong:** A plugin/file name with control characters, quotes, backticks, newlines, or command-like punctuation creates confusing dialogs/logs. [VERIFIED: `11-CONTEXT.md`]  
**Why it happens:** `Path.GetFileName` extracts a segment; it does not enforce a display policy. [CITED: https://learn.microsoft.com/dotnet/api/system.io.path.getfilename?view=net-10.0]  
**How to avoid:** Apply a display sanitizer after basename extraction and test control characters/quotes/unicode. [VERIFIED: `11-CONTEXT.md`]  
**Warning signs:** Raw `Path.GetFileName(path)` directly embedded in a user-facing string without display cleanup. [VERIFIED: `11-CONTEXT.md`]

## Code Examples

Verified patterns from official/project sources:

### Safe `Path.GetFileName` Display Wrapper
```csharp
// Source: https://learn.microsoft.com/dotnet/api/system.io.path.getfilename?view=net-10.0
// Source: https://learn.microsoft.com/dotnet/api/system.io.path.getinvalidfilenamechars?view=net-10.0
private static string SafeBasename(string? path, string fallback)
{
    var name = Path.GetFileName(path);
    if (string.IsNullOrWhiteSpace(name))
        return fallback;

    var invalid = Path.GetInvalidFileNameChars();
    var safe = new string(name
        .Where(ch => !char.IsControl(ch) && !invalid.Contains(ch) && ch is not '`')
        .ToArray())
        .Trim();

    return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
}
```

### xUnit Disclosure Sentinel Test
```csharp
// Source: https://context7.com/xunit/xunit/llms.txt
[Theory]
[InlineData(@"C:\Users\Alice\MO2\ModOrganizer.exe")]
[InlineData(@"-QAC -autoload \"C:\Games\Skyrim\Data\Plugin.esp\"")]
[InlineData("System.InvalidOperationException: boom at AutoQAC.Services.Cleaning")]
public async Task StartCleaningAsync_WhenUnexpectedError_ShouldNotShowUnsafeDetails(string unsafeDetail)
{
    // Arrange: make orchestrator throw an exception containing unsafeDetail.
    // Act: invoke StartCleaningCommand.
    // Assert: captured dialog title/message/details and StatusText do not contain unsafeDetail.
}
```

### Structured Safe Process Log Shape
```csharp
// Source: https://github.com/serilog/serilog/blob/dev/README.md
logger.Debug(
    "Starting process for {Operation}: {LaunchMode}, plugin={Plugin}, argumentCount={ArgumentCount}",
    "QuickAutoClean",
    launchMode,
    safePluginFileName,
    startInfo.ArgumentList.Count > 0 ? startInfo.ArgumentList.Count : 0);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Raw exception text and stack traces in dialogs | Safe user copy + logged technical details | Locked for Phase 11 by `11-SPEC.md` on 2026-05-01 | Planner should remove `ex.Message`/`ex.StackTrace` from user surfaces. [VERIFIED: `11-SPEC.md`] |
| Reconstructed command-line diagnostics | Argument-list launch preservation + safe structured log summaries | Phase 6 established argv preservation/redaction expectations; .NET docs recommend `ArgumentList` when escaping is uncertain | Planner should not alter command construction and should test log output for no raw payloads. [VERIFIED: `11-CONTEXT.md`; CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] |
| Ad hoc user error strings | Typed safe summaries for known categories | Phase 7/10 introduced `BackupFailureReason` labels and `ConfigPersistenceFailure.SafeSummary` | Planner should reuse category labels, not flatten to generic messages. [VERIFIED: `11-CONTEXT.md`] |
| Broad manual redaction as primary defense | Data classification/redaction frameworks or source-boundary safe formatting | Microsoft docs recommend identifying sensitive fields and redaction for logs/error outputs | AutoQAC should keep scope small now but align with the classify-before-output principle. [CITED: https://learn.microsoft.com/dotnet/core/extensions/data-redaction] |

**Deprecated/outdated:**
- Treating `xunit` v2 as future-feature path is outdated in NuGet metadata; the package remains installed at 2.9.3 and is fine for this phase, but NuGet deprecation metadata says future feature work moved to `xunit.v3`. Do not migrate test framework inside Phase 11. [VERIFIED: NuGet search API; VERIFIED: `11-SPEC.md`]
- Using `Avalonia.Diagnostics` is deprecated per Avalonia expert rules; the project already uses `AvaloniaUI.DiagnosticsSupport`. Do not touch diagnostics tooling for this phase. [CITED: Avalonia expert rules; VERIFIED: `dotnet list package`]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|

**If this table is empty:** All claims in this research were verified or cited — no user confirmation needed.

## Open Questions (RESOLVED)

1. **Should Phase 11 add a reusable `DiagnosticTextFormatter` service or static helper?**
    - What we know: Helper/service naming is explicitly planner discretion. [VERIFIED: `11-CONTEXT.md`]
   - RESOLVED: Use a small public static `DiagnosticTextFormatter` in `AutoQAC.Models.Diagnostics`, as locked into `11-01-PLAN.md`, so Models, Services, ViewModels, and startup code can share one safe text boundary without DI churn or service-layer dependencies. [VERIFIED: `11-01-PLAN.md`; VERIFIED: codebase architecture]
   - Rationale: The phase needs deterministic safe copy and sanitization primitives across model/report, ViewModel, process, and startup call sites; a static formatter is enough because no runtime policy or external dependency is needed. [VERIFIED: `11-CONTEXT.md`; VERIFIED: `11-PATTERNS.md`]

2. **How much source-level assertion is acceptable for startup/process logs?**
    - What we know: D-16 allows captured logger tests or equivalent behavior/source assertions. [VERIFIED: `11-CONTEXT.md`]
   - RESOLVED: Use captured logger behavior tests for `ProcessExecutionService` because that service already has injectable logging and test seams; use source/behavior guard assertions for `App.axaml.cs` startup and legacy migration warnings where the startup helper is private and broader seam extraction would expand scope. [VERIFIED: `11-05-PLAN.md`; VERIFIED: `11-06-PLAN.md`; VERIFIED: `11-CONTEXT.md`]
   - Rationale: This satisfies D-16 without adding new diagnostics infrastructure, while still making the tests fail if forbidden templates or raw executable/argv payloads return. [VERIFIED: `11-SPEC.md`; VERIFIED: `11-05-PLAN.md`; VERIFIED: `11-06-PLAN.md`]

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| .NET SDK | Build/test and implementation | ✓ | 10.0.203 | None needed. [VERIFIED: `dotnet --version`] |
| NuGet package restore | Build/test | ✓ | Restore reported up-to-date during `dotnet list package` | Existing restored packages. [VERIFIED: `dotnet list package`] |
| ccc semantic search | Research only | ✓ | Command succeeded | Built-in file read/search. [VERIFIED: `ccc search`] |

**Missing dependencies with no fallback:** None. [VERIFIED: environment probes]  
**Missing dependencies with fallback:** None. [VERIFIED: environment probes]

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + FluentAssertions 8.8.0 + NSubstitute 5.3.0 [VERIFIED: `dotnet list package`] |
| Config file | none detected; test projects are SDK-style `.csproj` with package references. [VERIFIED: `dotnet list package`] |
| Quick run command | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Diagnostics|FullyQualifiedName~CleaningCommandsViewModel|FullyQualifiedName~CleaningSessionResult|FullyQualifiedName~ProcessExecutionService"` [VERIFIED: test project exists] |
| Full suite command | `dotnet test AutoQACSharp.slnx` [VERIFIED: `AGENTS.md`] |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| SEC-01 | Unexpected cleaning/preview dialogs and status exclude raw exception/stack/path/command details and include latest-log guidance | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ErrorDialogTests"` | ✅ tighten existing `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` per context [VERIFIED: `11-CONTEXT.md`] |
| SEC-01 | Path validation/browse errors use safe identifiers for xEdit/MO2/load-order/game-data folder | unit/ViewModel | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~Configuration"` | ✅/❌ existing tests named in context; likely add Phase 11 cases [VERIFIED: `11-CONTEXT.md`] |
| SEC-01 | Result rows and generated reports exclude unsafe failed-message content and include disclaimer | unit/model/service | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~CleaningSessionResultTests|FullyQualifiedName~PluginResultFinalizerTests"` | ✅ existing files listed by test discovery [VERIFIED: test list] |
| SEC-01 | Restore session loading and migration warnings use safe category/generic copy | unit/ViewModel/startup seam | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~RestoreViewModelTests|FullyQualifiedName~Migration"` | ✅ Restore tests exist; migration seam may need Wave 0 test file [VERIFIED: `11-CONTEXT.md`] |
| SEC-02 | Process-start/startup logs exclude executable paths, raw argv, and nested MO2 payloads while preserving safe fields | unit/service/source guard | `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ProcessExecutionServiceTests|FullyQualifiedName~Startup"` | ✅ process tests exist; startup log test may need new file [VERIFIED: test list; VERIFIED: `App.axaml.cs`] |

### Sampling Rate
- **Per task commit:** relevant filtered `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter ...` command for touched surface. [VERIFIED: test infrastructure]
- **Per wave merge:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj` for app-only changes. [VERIFIED: test infrastructure]
- **Phase gate:** `dotnet test AutoQACSharp.slnx` before `/gsd-verify-work`. [VERIFIED: `AGENTS.md`]

### Wave 0 Gaps
- [ ] Add or tighten a shared unsafe sentinel helper in tests (e.g., path/command/stack fragments) so all disclosure tests assert the same negative set. [VERIFIED: `11-SPEC.md`]
- [ ] Add captured logger/substitute assertions for `ProcessExecutionService` and startup diagnostics; private startup helper may need a small seam before behavior tests. [VERIFIED: `11-CONTEXT.md`; VERIFIED: `App.axaml.cs`]
- [ ] Add source or behavior coverage for `PluginResultFinalizer` xEdit exception-log content becoming safe message/log context. [VERIFIED: `PluginResultFinalizer.cs`; VERIFIED: `11-CONTEXT.md`]

## Sources

### Primary (HIGH confidence)
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-SPEC.md` - locked requirements, boundaries, constraints, acceptance criteria. [VERIFIED]
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-CONTEXT.md` - locked implementation decisions D-01 through D-16 and code-context scouting. [VERIFIED]
- `AGENTS.md` and `.planning/codebase/{ARCHITECTURE,CONVENTIONS,CONCERNS}.md` - project constraints, patterns, pitfalls. [VERIFIED]
- Code reads: `CleaningCommandsViewModel.cs`, `ConfigurationViewModel.cs`, `RestoreViewModel.cs`, `ProcessExecutionService.cs`, `PluginResultFinalizer.cs`, `CleaningSessionResult.cs`, `App.axaml.cs`, `LoggingService.cs`, `IMessageDialogService.cs`. [VERIFIED]
- Serilog Context7 `/serilog/serilog` - message templates, structured data, destructuring policies. [CITED: https://github.com/serilog/serilog/blob/dev/README.md]
- Avalonia docs - commands/MVVM patterns with CommunityToolkit.Mvvm. [CITED: https://docs.avaloniaui.net/docs/input-interaction/adding-interactivity; CITED: https://docs.avaloniaui.net/docs/how-to/mvvm-how-to]
- Microsoft Learn - .NET data redaction, `Path.GetFileName`, `Path.GetInvalidFileNameChars`, `ProcessStartInfo.ArgumentList`. [CITED: https://learn.microsoft.com/dotnet/core/extensions/data-redaction; CITED: https://learn.microsoft.com/dotnet/api/system.io.path.getfilename?view=net-10.0; CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0]
- NuGet/dotnet package verification - installed and current versions. [VERIFIED: `dotnet list AutoQACSharp.slnx package`; VERIFIED: NuGet search API]

### Secondary (MEDIUM confidence)
- Exa search for Serilog docs surfaced current Serilog documentation mirrors/GitHub wiki; authoritative claims were cross-checked through Context7/GitHub docs. [VERIFIED: Exa; CITED: Serilog official docs]

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - installed versions verified through `dotnet list package`; current package metadata checked through NuGet search API. [VERIFIED]
- Architecture: HIGH - constrained by Phase 11 SPEC/CONTEXT and direct code reads of the exact leak surfaces. [VERIFIED]
- Pitfalls: HIGH - pitfalls map directly to locked acceptance criteria and current code call sites. [VERIFIED]
- SOTA/current guidance: MEDIUM-HIGH - Serilog, Microsoft Learn, Avalonia docs checked; no package migration recommended. [CITED]

**Research date:** 2026-04-30  
**Valid until:** 2026-05-30 for Phase 11 implementation approach; re-check NuGet/doc versions if package changes are introduced.
