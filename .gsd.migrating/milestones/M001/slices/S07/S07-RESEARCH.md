# Phase 6: Command Launch Escaping - Research

**Researched:** 2026-04-28  
**Domain:** Windows/.NET process argument construction and verification for direct xEdit and MO2-wrapped launches  
**Confidence:** HIGH for .NET process APIs and in-repo test architecture; MEDIUM for MO2 `run ... -a ...` CLI details because official CLI documentation is sparse.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
## Implementation Decisions

### Coverage Matrix
- **D-01:** Quote cases should be covered with synthetic command/argv parser tests when Windows cannot create equivalent real file names; real file/path tests should be used for characters Windows actually permits.
- **D-02:** The difficult-character matrix must cover all launch-bound inputs: xEdit executable path, MO2 executable path, plugin file name, and nested xEdit arguments.
- **D-03:** Unicode coverage should be representative rather than exhaustive: include accented text, at least one non-Latin case such as CJK or Cyrillic, and one supplementary/emoji-style case if the test harness can represent it reliably.
- **D-04:** Shell-sensitive characters should be proven as literal argv text under `UseShellExecute=false`; include cases such as `&`, `|`, `;`, `(`, `)`, `^`, and spaces rather than treating them as shell syntax.

### MO2 Contract
- **D-05:** Preserve the current MO2 launch shape if possible: `ModOrganizer.exe run <xEdit> -a <xEdit args>`. The goal is to replace fragile escaping, not to change the MO2 integration contract.
- **D-06:** Keep one MO2 `-a` payload containing the xEdit flags as a single nested xEdit argument string unless research proves the current contract cannot be made safe.
- **D-07:** In MO2 mode, keep `-autoload` file-name-only. Do not switch it to a full host filesystem path because MO2 VFS and xEdit load-order resolution should own plugin lookup.
- **D-08:** MO2-specific verification should assert the final MO2 argv contract (`run`, xEdit path, `-a`, one intact xEdit argument string) without requiring real MO2 to be installed or launched in tests.

### Failure Behavior
- **D-09:** If AutoQAC cannot safely build a direct or MO2 launch command, fail before starting any process. Return a clear command-build failure for the plugin and log technical detail.
- **D-10:** Do not normalize, strip, sanitize, or rewrite configured paths or plugin names to make launching easier. Inputs are authoritative; pass them exactly as argv text or fail.
- **D-11:** A command-build or launch-start failure for one plugin should use the existing cleaning failure flow. Do not invent a new session policy for this phase.
- **D-12:** User-facing launch-escaping failure messages should be concise: name the plugin and launch mode, state that no process was started, and point to logs for technical details. Do not show a full command line in the user-facing message.

### Verification Depth
- **D-13:** Phase 6 must explicitly verify that `ProcessExecutionService` preserves `ProcessStartInfo.ArgumentList` through launch; a command-builder fix is incomplete if the process layer drops `ArgumentList` while cloning `ProcessStartInfo`.
- **D-14:** Extend the existing Phase 5 `AutoQAC.TestProcessHelper` with an argv-echo mode rather than adding a separate helper process or avoiding helper-based process tests.
- **D-15:** Use a curated regression matrix: cover each locked category plus one combined worst-case input. Do not expand this phase into broad fuzzing.
- **D-16:** Prefer parsed argv assertions over raw generated command-string assertions. Assert raw string shape only where MO2's single `-a` payload contract requires it.

### the agent's Discretion
- Exact helper method names, data structures for representing xEdit/MO2 argument payloads, and test case naming are left to downstream research and planning.
- The planner may choose the smallest internal API changes that satisfy the decisions above, as long as all launch-bound `ArgumentList` entries survive to the actual process start boundary.

### Deferred Ideas (OUT OF SCOPE)
## Deferred Ideas

None - discussion stayed within phase scope.
</user_constraints>

## Summary

Phase 6 should replace manual command-line string construction with data-first `ProcessStartInfo.ArgumentList` entries at the xEdit/MO2 command-builder boundary. `ArgumentList` is the current .NET API intended for callers that do not want to hand-escape arguments; Microsoft documents that strings added to it do not need previous escaping, that it internally builds the OS command line for `Process.Start`, and that `ArgumentList` and `Arguments` must not be used at the same time. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] The existing `XEditCommandBuilder` currently uses `Arguments`, manual quotes, and `Replace("\"", "\\\"")` for nested MO2 payloads, so it is the primary bug surface. [VERIFIED: AutoQAC/Services/Cleaning/XEditCommandBuilder.cs]

Direct xEdit launch should be modeled as executable path plus exact argv tokens: optional game flag, `-QAC`, `-autoexit`, one `-autoload` payload token that preserves the plugin filename, and optional partial-forms flags. [VERIFIED: AutoQAC/Services/Cleaning/XEditCommandBuilder.cs] MO2 launch should preserve the locked current contract as `FileName = ModOrganizer.exe`, argv tokens `run`, `<xEdit path>`, `-a`, and one nested xEdit argument string; only that final `-a` payload remains a string-within-an-argument because MO2 owns a second parsing boundary. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] Test assertions should prove parsed argv, not raw command text, except for the single MO2 `-a` nested payload where raw string shape is part of the contract. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

**Primary recommendation:** Use `ProcessStartInfo.ArgumentList` everywhere AutoQAC controls argv boundaries, copy `ArgumentList` explicitly in `ProcessExecutionService`, and verify with the existing helper process extended to echo received argv as UTF-8 JSON. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] [VERIFIED: AutoQAC.Tests/TestProcessHelper/Program.cs]

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|--------------|----------------|-----------|
| Build direct xEdit argv | Services / Backend process orchestration | Process boundary | `XEditCommandBuilder` already owns launch construction and returns `ProcessStartInfo`. [VERIFIED: AutoQAC/Services/Cleaning/XEditCommandBuilder.cs] |
| Build MO2-wrapped argv | Services / Backend process orchestration | External MO2 process | AutoQAC should construct MO2's argv contract but not emulate MO2's VFS or executable runner. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |
| Preserve argv through launch | Process boundary | Services / Backend process orchestration | `ProcessExecutionService` clones `ProcessStartInfo` before `Process.Start`; the clone currently copies `Arguments` but not `ArgumentList`. [VERIFIED: AutoQAC/Services/Process/ProcessExecutionService.cs] |
| User-facing launch-build failure | Existing cleaning service/session flow | UI dialogs | Context locks existing failure flow and concise messages; Phase 6 is not a UI redesign. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |
| Regression verification | Test projects | Helper executable | Existing tests use xUnit/FluentAssertions/NSubstitute and a helper executable already copied into the test output. [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj] [VERIFIED: AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs] |

## Project Constraints (from AGENTS.md)

- Preserve Windows-only desktop assumptions and do not introduce cross-platform launch semantics as a requirement. [VERIFIED: AGENTS.md]
- Do not parallelize plugin cleaning or xEdit launches; `ProcessExecutionService` intentionally uses a single process slot. [VERIFIED: AGENTS.md] [VERIFIED: AutoQAC/Services/Process/ProcessExecutionService.cs]
- Preserve `CleaningOrchestrator` session flow, including flush-before-launch and stop/force-kill semantics. [VERIFIED: AGENTS.md]
- Maintain strict MVVM boundaries; ViewModels must not directly manipulate controls. [VERIFIED: AGENTS.md]
- Keep I/O and process work async; do not block the UI thread with `.Result` or `.Wait()`. [VERIFIED: AGENTS.md]
- Use constructor injection through `ServiceCollectionExtensions`; avoid service locators and static mutable state. [VERIFIED: AGENTS.md]
- Use NSubstitute for mocks and match optional parameters explicitly. [VERIFIED: AGENTS.md]
- Do not modify `Mutagen/`; it is read-only. [VERIFIED: AGENTS.md]
- Do not claim Avalonia.Headless infrastructure; none is present unless added intentionally. [VERIFIED: AGENTS.md]
- Never delete comments as cleanup; add XML doc comments to methods that are added or substantially rewritten unless trivially private. [VERIFIED: C:/Users/evild/.config/opencode/AGENTS.md]

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SAF-03 | User can clean plugins whose paths or names contain quotes, Unicode, spaces, or shell-sensitive characters. [VERIFIED: .planning/REQUIREMENTS.md] | `ArgumentList` for direct argv preservation; Windows parser tests for quote/backslash edge cases; UTF-8 helper output for Unicode assertions. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] [CITED: https://learn.microsoft.com/windows/win32/api/processenv/nf-processenv-getcommandlinea] |
| TEST-02 | Maintainer can verify xEdit and MO2 command argument escaping across quotes, Unicode, shell-sensitive characters, and nested arguments. [VERIFIED: .planning/REQUIREMENTS.md] | Extend `XEditCommandBuilderTests` and `ProcessExecutionIntegrationTests`; add `argv-echo` mode to `AutoQAC.TestProcessHelper`. [VERIFIED: AutoQAC.Tests/Services/XEditCommandBuilderTests.cs] [VERIFIED: AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs] |
</phase_requirements>

## Standard Stack

### Core

| Library / API | Version | Purpose | Why Standard |
|---------------|---------|---------|--------------|
| `System.Diagnostics.ProcessStartInfo.ArgumentList` | .NET 10 API surface, project SDK `10.0.203` detected | Preserve each launch argument as a separate string until .NET performs OS command-line escaping. | Microsoft explicitly recommends `ArgumentList` over `Arguments` when caller is not sure how to escape arguments. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] [VERIFIED: `dotnet --version`] |
| `System.Diagnostics.ProcessStartInfo.UseShellExecute = false` | .NET 10 API surface | Start executables directly and keep shell-sensitive characters as argv text instead of shell syntax. | Existing process layer sets `UseShellExecute=false`; Microsoft documents that false starts only executables and enables redirection. [VERIFIED: AutoQAC/Services/Process/ProcessExecutionService.cs] [CITED: https://learn.microsoft.com/dotnet/fundamentals/runtime-libraries/system-diagnostics-processstartinfo-useshellexecute] |
| xUnit | 2.9.3 | Unit/integration test framework. | Existing test project standard. [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj] |
| FluentAssertions | 8.8.0 | Readable argument-list and process-result assertions. | Existing test project standard. [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj] |
| NSubstitute | 5.3.0 | Mock `IStateService`, logging, and process abstractions. | Existing test project standard and AGENTS directive. [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj] [VERIFIED: AGENTS.md] |

### Supporting

| Library / API | Version | Purpose | When to Use |
|---------------|---------|---------|-------------|
| `System.Text.Json` | .NET 10 shared framework | Serialize helper `argv-echo` output for robust Unicode and delimiter-safe test assertions. | Use in `AutoQAC.TestProcessHelper` to avoid line-splitting ambiguity in echoed args. [VERIFIED: AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj] |
| `CommandLineToArgvW` parser semantics | Windows Shell32 API | Synthetic expectations for quote/backslash parsing where no real file can exist. | Use only as a reference/parser-test concept; do not add production P/Invoke unless needed for diagnostics. [CITED: https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-commandlinetoargvw#remarks] |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `ArgumentList` | Manual `Arguments` string escaping | Microsoft documents `Arguments` as a single string requiring precise quoting/triple-escaping and says `ArgumentList` should be chosen when escaping is uncertain; manual escaping is the current bug class. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.arguments?view=net-10.0] |
| Existing helper executable | New throwaway argv echo executable | Context locks reuse of `AutoQAC.TestProcessHelper`; adding another helper increases copy/build surface with no benefit. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |
| Parsed argv assertions | Raw command-line string snapshots | Raw command strings are implementation details for direct launches; parsed argv is the user-visible correctness boundary. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |

**Installation:** No new NuGet packages are required. [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj]

```bash
dotnet restore AutoQACSharp.slnx
```

**Version verification:** Project package versions were verified from `AutoQAC/AutoQAC.csproj` and `AutoQAC.Tests/AutoQAC.Tests.csproj`; installed SDK was verified with `dotnet --version` as `10.0.203`. [VERIFIED: AutoQAC/AutoQAC.csproj] [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj] [VERIFIED: `dotnet --version`]

## Architecture Patterns

### System Architecture Diagram

```text
CleaningService.CleanPluginAsync(plugin)
        |
        v
IXEditCommandBuilder.BuildCommand(plugin, gameType)
        |
        +-- invalid game/path/config? --> return build failure/null --> existing CleaningStatus.Failed path
        |
        +-- direct mode --> ProcessStartInfo(FileName=xEdit, ArgumentList=[flags, plugin payload])
        |
        +-- MO2 mode ----> ProcessStartInfo(FileName=MO2, ArgumentList=[run, xEdit, -a, nested xEdit payload])
        |
        v
ProcessExecutionService.ExecuteAsync(startInfo)
        |
        v
clone FileName + WorkingDirectory + redirection + ArgumentList
        |
        v
Process.Start(useShellExecute=false)
        |
        v
OS/.NET command-line construction -> target process argv
        |
        +-- success --> existing PID tracking, timeout, cancellation, result parsing
        +-- startup failure --> existing failed ProcessResult / cleaning failure flow
```

### Recommended Project Structure

```text
AutoQAC/
├── Services/Cleaning/XEditCommandBuilder.cs          # construct direct and MO2 ProcessStartInfo with ArgumentList
├── Services/Process/ProcessExecutionService.cs       # preserve ArgumentList when cloning start info
└── Services/Cleaning/CleaningService.cs              # map build/start failure to existing cleaning failure messages

AutoQAC.Tests/
├── Services/XEditCommandBuilderTests.cs              # direct/MO2 argv contract matrix
├── Services/ProcessExecutionIntegrationTests.cs      # real helper process proves ArgumentList survives launch
└── TestProcessHelper/Program.cs                      # add argv-echo mode using JSON/UTF-8 output
```

### Pattern 1: ArgumentList as the Production Boundary

**What:** Build `ProcessStartInfo` with `FileName`, `WorkingDirectory`, `UseShellExecute=false`, and `ArgumentList` entries; leave `Arguments` empty. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0]  
**When to use:** Every direct xEdit launch and every MO2 argv token that AutoQAC passes to `ModOrganizer.exe`. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

**Example:**

```csharp
// Source: Microsoft ProcessStartInfo.ArgumentList docs + AutoQAC Phase 6 context.
var startInfo = new ProcessStartInfo
{
    FileName = xEditPath,
    WorkingDirectory = Path.GetDirectoryName(xEditPath),
    UseShellExecute = false
};

startInfo.ArgumentList.Add("-QAC");
startInfo.ArgumentList.Add("-autoexit");
startInfo.ArgumentList.Add("-autoload");
startInfo.ArgumentList.Add(plugin.FileName);
```

### Pattern 2: Explicitly Preserve ArgumentList When Cloning StartInfo

**What:** When `ProcessExecutionService` creates its internal `ProcessStartInfo`, copy each `startInfo.ArgumentList` entry; do not copy `Arguments` when `ArgumentList` has entries. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0]  
**When to use:** In `ProcessExecutionService.ExecuteAsync`, because the current clone copies `Arguments` but drops `ArgumentList`. [VERIFIED: AutoQAC/Services/Process/ProcessExecutionService.cs]

**Example:**

```csharp
// Source: Microsoft ProcessStartInfo.ArgumentList docs + existing ProcessExecutionService clone pattern.
var processStartInfo = new ProcessStartInfo
{
    FileName = startInfo.FileName,
    WorkingDirectory = startInfo.WorkingDirectory,
    UseShellExecute = false,
    CreateNoWindow = startInfo.CreateNoWindow,
    RedirectStandardInput = startInfo.RedirectStandardInput,
    RedirectStandardOutput = startInfo.RedirectStandardOutput,
    RedirectStandardError = startInfo.RedirectStandardError,
    StandardInputEncoding = startInfo.StandardInputEncoding,
    StandardOutputEncoding = startInfo.StandardOutputEncoding,
    StandardErrorEncoding = startInfo.StandardErrorEncoding
};

if (startInfo.ArgumentList.Count > 0)
{
    foreach (var argument in startInfo.ArgumentList)
    {
        processStartInfo.ArgumentList.Add(argument);
    }
}
else
{
    processStartInfo.Arguments = startInfo.Arguments;
}
```

### Pattern 3: Test the Real Process Boundary with an Argv Echo Helper

**What:** Extend `AutoQAC.TestProcessHelper` with `argv-echo` mode that writes received args as JSON to stdout, then invoke it through `ProcessExecutionService` with `ArgumentList`. [VERIFIED: AutoQAC.Tests/TestProcessHelper/Program.cs]  
**When to use:** To verify `ProcessExecutionService` preserves Unicode, spaces, quotes, shell-sensitive characters, and backslash/quote cases through `Process.Start`. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

**Example:**

```csharp
// Source: Existing helper process pattern in ProcessExecutionIntegrationTests.
var startInfo = HelperStartInfo();
startInfo.RedirectStandardOutput = true;
startInfo.ArgumentList.Add("argv-echo");
startInfo.ArgumentList.Add("Résumé 測試 🚀 & | ; ( ) ^.esp");

var result = await service.ExecuteAsync(startInfo, timeout: TimeSpan.FromSeconds(5));
result.ExitCode.Should().Be(0);
```

### Anti-Patterns to Avoid

- **Quote-decorated fragments in production argv:** Do not build entries such as `$"-autoload \"{plugin.FileName}\""` for direct launch; pass the option and payload as argv data. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0]
- **Mixing `Arguments` and `ArgumentList`:** Microsoft documents these APIs as independent and says only one can be used at the same time. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0]
- **Shell escaping under `UseShellExecute=false`:** Do not escape `&`, `|`, `;`, `(`, `)`, or `^` as if `cmd.exe` were parsing them; the locked phase requires these to remain literal argv text. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]
- **Changing MO2 lookup semantics:** Do not replace MO2 `-autoload` file-name-only with a full host path. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]
- **Fuzzing expansion:** Do not turn this phase into broad fuzzing; use the curated matrix required by context. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Direct process argument escaping | Custom quote/backslash escaping for `Arguments` | `ProcessStartInfo.ArgumentList` | Microsoft documents that `ArgumentList` escapes provided arguments and should be chosen over `Arguments` when escaping is uncertain. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] |
| Shell metacharacter handling | `cmd.exe`-style caret/ampersand escaping | `UseShellExecute=false` plus literal `ArgumentList` entries | Existing code starts processes directly, and phase context requires shell-sensitive characters to remain literal. [VERIFIED: AutoQAC/Services/Process/ProcessExecutionService.cs] [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |
| Unicode command-line conversion | ANSI command-line APIs or code-page normalization | .NET strings / Unicode process APIs; helper output as UTF-8 JSON | Microsoft warns `GetCommandLineA` conversion can be lossy and alter file names or parsing; Unicode paths must remain exact. [CITED: https://learn.microsoft.com/windows/win32/api/processenv/nf-processenv-getcommandlinea] |
| Parsed argv verification | Raw string snapshot comparisons for direct launch | Argv echo helper and `ArgumentList` collection assertions | Direct raw command strings are implementation details; context prefers parsed argv assertions. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |
| MO2 execution simulation | Fake MO2 VFS or real MO2 dependency in tests | Assert MO2 argv contract without launching MO2 | Context explicitly says MO2 verification should not require real MO2. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |

**Key insight:** There are two boundaries, not one: AutoQAC-to-process should use `ArgumentList`, while MO2 `-a` remains a nested xEdit command payload by locked contract. Treat the nested payload as one MO2 argv value and test it separately instead of spreading xEdit args into MO2's argv. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

## Common Pitfalls

### Pitfall 1: Fixing the Builder but Dropping `ArgumentList` in the Process Layer
**What goes wrong:** Unit tests pass on `XEditCommandBuilder.ArgumentList`, but `ProcessExecutionService` clones only `Arguments`, launches with no arguments, or launches stale string arguments. [VERIFIED: AutoQAC/Services/Process/ProcessExecutionService.cs]  
**Why it happens:** `ArgumentList` and `Arguments` are independent; assigning one does not populate the other. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.arguments?view=net-10.0]  
**How to avoid:** Copy `ArgumentList` explicitly when present and add a real helper-process integration test. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]  
**Warning signs:** `startInfo.ArgumentList.Count > 0` before `ExecuteAsync`, but helper receives only `argv[0]` mode or no difficult payload. [VERIFIED: AutoQAC.Tests/TestProcessHelper/Program.cs]

### Pitfall 2: Keeping `-autoload "plugin"` as One Direct Argument Without Verifying xEdit Expectations
**What goes wrong:** The current string format `-autoload "Plugin.esp"` may be parsed differently than two argv entries `-autoload`, `Plugin.esp` if xEdit expects a specific form. [VERIFIED: AutoQAC/Services/Cleaning/XEditCommandBuilder.cs]  
**Why it happens:** xEdit launch docs list `-autoload` as a launch argument but do not document AutoQAC's exact plugin-targeting shape on the fetched guide page. [CITED: https://stepmodifications.org/wiki/Guide:XEdit]  
**How to avoid:** Preserve behavior as closely as possible where required by existing tests, but prefer tests that assert the intended target plugin payload is intact; if changing from one token to two, add explicit `XEditCommandBuilderTests` documenting that contract. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]  
**Warning signs:** A test only checks `.Arguments.Contains("-autoload")` and never checks parsed argv or plugin payload boundaries. [VERIFIED: AutoQAC.Tests/Services/XEditCommandBuilderTests.cs]

### Pitfall 3: Treating Shell-Sensitive Characters as Dangerous Under Direct Launch
**What goes wrong:** Code strips or rewrites `&`, `|`, `;`, `(`, `)`, or `^`, corrupting valid plugin names or paths. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]  
**Why it happens:** Developers conflate `cmd.exe` parsing with `UseShellExecute=false` direct process launch. [CITED: https://learn.microsoft.com/dotnet/fundamentals/runtime-libraries/system-diagnostics-processstartinfo-useshellexecute]  
**How to avoid:** Pass these characters as literal `ArgumentList` string content and verify with the helper. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0]  
**Warning signs:** Code contains caret escaping, ampersand replacement, sanitization, or plugin-name normalization in the launch builder. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

### Pitfall 4: Losing Unicode Through Logs or Test Output
**What goes wrong:** Unicode filenames pass through .NET but test output mangles them, producing false failures or hiding a real issue. [CITED: https://learn.microsoft.com/windows/win32/api/processenv/nf-processenv-getcommandlinea]  
**Why it happens:** ANSI command-line APIs and non-UTF output can be lossy; Microsoft documents `GetCommandLineA` conversion risks. [CITED: https://learn.microsoft.com/windows/win32/api/processenv/nf-processenv-getcommandlinea]  
**How to avoid:** Use normal .NET `string[] args`, write JSON to stdout, and set `StandardOutputEncoding = Encoding.UTF8` when redirecting helper output. [VERIFIED: AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj]  
**Warning signs:** Tests compare console lines after default encoding assumptions or use byte conversions with `Encoding.Default`. [CITED: https://learn.microsoft.com/windows/win32/api/processenv/nf-processenv-getcommandlinea]

### Pitfall 5: Over-asserting Internal Raw Command Text
**What goes wrong:** Tests become brittle against .NET's internal escaping implementation while missing target argv corruption. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0]  
**Why it happens:** `ArgumentList` internally builds a single OS command line, but its raw representation is not the product contract. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0]  
**How to avoid:** Assert `ArgumentList` entries in unit tests and helper-received argv in integration tests; assert raw nested string only for MO2 `-a`. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]  
**Warning signs:** Snapshot tests assert backslash counts for direct xEdit mode instead of received argv. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

## Code Examples

Verified patterns from official sources and repository code:

### Direct xEdit Command Construction

```csharp
// Source: ProcessStartInfo.ArgumentList docs and AutoQAC XEditCommandBuilder target.
var startInfo = new ProcessStartInfo
{
    FileName = xEditPath,
    WorkingDirectory = Path.GetDirectoryName(xEditPath),
    UseShellExecute = false
};

if (Path.GetFileNameWithoutExtension(xEditPath).StartsWith("xEdit", StringComparison.OrdinalIgnoreCase))
{
    startInfo.ArgumentList.Add(GetGameFlag(gameType));
}

startInfo.ArgumentList.Add("-QAC");
startInfo.ArgumentList.Add("-autoexit");
startInfo.ArgumentList.Add("-autoload");
startInfo.ArgumentList.Add(plugin.FileName);
```

### MO2 Contract Construction

```csharp
// Source: Phase 6 locked MO2 contract; direct tokens use ArgumentList, nested xEdit payload remains one -a value.
var nestedXEditArgs = string.Join(" ", BuildNestedXEditTokensForMo2(plugin.FileName, gameType));

var startInfo = new ProcessStartInfo
{
    FileName = mo2Path,
    WorkingDirectory = Path.GetDirectoryName(mo2Path),
    UseShellExecute = false
};

startInfo.ArgumentList.Add("run");
startInfo.ArgumentList.Add(xEditPath);
startInfo.ArgumentList.Add("-a");
startInfo.ArgumentList.Add(nestedXEditArgs);
```

### Helper `argv-echo` Mode

```csharp
// Source: Existing AutoQAC.TestProcessHelper switch pattern.
case "argv-echo":
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    await Console.Out.WriteLineAsync(
        System.Text.Json.JsonSerializer.Serialize(args.Skip(1).ToArray()));
    return 0;
```

### Integration Test Shape

```csharp
// Source: Existing ProcessExecutionIntegrationTests helper pattern.
var startInfo = HelperStartInfo();
startInfo.RedirectStandardOutput = true;
startInfo.StandardOutputEncoding = Encoding.UTF8;
startInfo.ArgumentList.Add("argv-echo");
startInfo.ArgumentList.Add("Quote \" literal");
startInfo.ArgumentList.Add("Résumé 測試 🚀 & | ; ( ) ^.esp");

var output = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var result = await _service.ExecuteAsync(
    startInfo,
    timeout: TimeSpan.FromSeconds(5),
    onProcessStarted: p => _ = Task.Run(async () =>
        output.TrySetResult(await p.StandardOutput.ReadToEndAsync())));

result.ExitCode.Should().Be(0);
JsonSerializer.Deserialize<string[]>(await output.Task)
    .Should().Equal("Quote \" literal", "Résumé 測試 🚀 & | ; ( ) ^.esp");
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manually build `ProcessStartInfo.Arguments` with quoted substrings. | Use `ProcessStartInfo.ArgumentList` for caller-supplied argument tokens. | `ArgumentList` is available in current .NET 10 docs and documented for .NET Core/modern .NET monikers. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] | AutoQAC should stop implementing direct launch escaping. |
| Treat shell metacharacters as command syntax. | With `UseShellExecute=false`, pass them as literal process arguments. | Existing project already sets `UseShellExecute=false`; Microsoft documents false as direct executable start. [VERIFIED: AutoQAC/Services/Process/ProcessExecutionService.cs] [CITED: https://learn.microsoft.com/dotnet/fundamentals/runtime-libraries/system-diagnostics-processstartinfo-useshellexecute] | Tests should include shell-sensitive characters without shell escapes. |
| ANSI command-line inspection. | Unicode strings / wide command-line semantics. | Microsoft documents lossy `GetCommandLineA` conversion risks. [CITED: https://learn.microsoft.com/windows/win32/api/processenv/nf-processenv-getcommandlinea] | Unicode tests should avoid ANSI APIs and default code page assumptions. |

**Deprecated/outdated:**
- Manual `Arguments` escaping for direct launches is outdated for this phase because .NET provides `ArgumentList` specifically to avoid pre-escaping. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0]
- Raw command-string assertions for direct launches are not the preferred verification boundary; context locks parsed argv assertions. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | MO2's `run <xEdit> -a <payload>` contract can remain safe if `<payload>` is one intact argv value; official fetched docs confirm MO2 executable arguments UI/VFS behavior but not the exact `run -a` CLI parser. [ASSUMED] | Summary, Architecture Patterns | If MO2 parses `-a` differently than expected, implementation may preserve AutoQAC's current contract but still fail in real MO2; planner should keep MO2 tests at contract level and consider manual/fixture validation if MO2 is available. |
| A2 | xEdit accepts the planned `-autoload` target payload shape for selecting one plugin in QAC; fetched Step docs document `-autoload` generally but not AutoQAC's exact one-plugin payload syntax. [ASSUMED] | Common Pitfalls, Code Examples | If xEdit requires the current single-string form exactly, changing to split `-autoload` and plugin filename may break targeting; planner should preserve current semantics unless existing behavior/tests prove split argv is accepted. |

## Open Questions

1. **Does real MO2 document or guarantee `run <program> -a <args>` parsing?**
   - What we know: Phase context locks the current shape, and MO2 docs confirm executables have Binary/Start in/Arguments fields and should be run through MO2 for VFS. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] [CITED: https://github-wiki-see.page/m/ModOrganizer2/modorganizer/wiki/Executables-window]
   - What's unclear: The fetched MO2 wiki page does not fully document the `run -a` command-line parser. [CITED: https://github-wiki-see.page/m/ModOrganizer2/modorganizer/wiki/Executables-window]
   - Recommendation: Preserve the locked current contract, assert AutoQAC passes `run`, xEdit path, `-a`, and one intact nested payload, and do not require real MO2 in automated tests. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

2. **Should `-autoload` and plugin filename be one argv entry or two for direct xEdit?**
   - What we know: Current code emits one string fragment `-autoload "plugin"`; xEdit docs list `-autoload` but do not document AutoQAC's exact plugin target syntax in fetched content. [VERIFIED: AutoQAC/Services/Cleaning/XEditCommandBuilder.cs] [CITED: https://stepmodifications.org/wiki/Guide:XEdit]
   - What's unclear: Whether xEdit expects a space-delimited payload in one command-line token or can accept option and value as separate argv entries for this use. [ASSUMED]
   - Recommendation: Make the planner explicitly choose and test the contract. The safest minimal-change approach is to preserve the semantic payload while moving outer process escaping to `ArgumentList`; add tests that fail if the target plugin is split/corrupted. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| .NET SDK | Build and tests | ✓ | 10.0.203 | None needed. [VERIFIED: `dotnet --version`] |
| xUnit / test packages | Regression tests | ✓ | xUnit 2.9.3, FluentAssertions 8.8.0, NSubstitute 5.3.0 | None needed. [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj] |
| AutoQAC.TestProcessHelper | Real argv preservation tests | ✓ | net10.0 helper project | Extend existing helper; do not add a second helper. [VERIFIED: AutoQAC.Tests/TestProcessHelper/AutoQAC.TestProcessHelper.csproj] |
| Real MO2 installation | MO2 argv contract tests | ✗ / not required | — | Assert MO2 argv contract without launching MO2. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |
| Real xEdit installation | Direct argv contract tests | ✗ / not required | — | Use builder unit tests and helper process integration tests. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |

**Missing dependencies with no fallback:** None. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

**Missing dependencies with fallback:** Real MO2 and real xEdit are not required for automated Phase 6 verification; contract/helper tests are the fallback. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md]

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 with FluentAssertions 8.8.0 and NSubstitute 5.3.0. [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj] |
| Config file | `AutoQAC.Tests/AutoQAC.Tests.csproj`; solution `AutoQACSharp.slnx`. [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj] [VERIFIED: AutoQACSharp.slnx] |
| Quick run command | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests"` |
| Full suite command | `dotnet test AutoQACSharp.slnx` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| SAF-03 | Direct xEdit preserves plugin names/paths with spaces, Unicode, shell-sensitive characters, and quote/parser cases. | unit + integration | `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests"` | ✅ existing files; needs new cases. [VERIFIED: AutoQAC.Tests/Services/XEditCommandBuilderTests.cs] [VERIFIED: AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs] |
| SAF-03 | MO2 mode preserves `run`, xEdit path, `-a`, and one intact nested xEdit payload without requiring real MO2. | unit | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~XEditCommandBuilderTests` | ✅ existing file; needs new cases. [VERIFIED: AutoQAC.Tests/Services/XEditCommandBuilderTests.cs] |
| TEST-02 | Maintainer can verify `ProcessExecutionService` preserves `ArgumentList` through real process start. | integration | `dotnet test AutoQACSharp.slnx --filter FullyQualifiedName~ProcessExecutionIntegrationTests` | ✅ existing file; helper needs `argv-echo`. [VERIFIED: AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs] |

### Sampling Rate
- **Per task commit:** `dotnet test AutoQACSharp.slnx --filter "FullyQualifiedName~XEditCommandBuilderTests|FullyQualifiedName~ProcessExecutionIntegrationTests"` [VERIFIED: current test discovery]
- **Per wave merge:** `dotnet test AutoQACSharp.slnx` [VERIFIED: AGENTS.md]
- **Phase gate:** Full suite green before `/gsd-verify-work`. [VERIFIED: AGENTS.md]

### Wave 0 Gaps
- [ ] `AutoQAC.Tests/TestProcessHelper/Program.cs` — add `argv-echo` mode with UTF-8 JSON output. [VERIFIED: AutoQAC.Tests/TestProcessHelper/Program.cs]
- [ ] `AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs` — add helper start factory that uses `ArgumentList` rather than `Arguments`. [VERIFIED: AutoQAC.Tests/Services/ProcessExecutionIntegrationTests.cs]
- [ ] `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs` — replace string-contains command tests with `ArgumentList` contract assertions and curated difficult-character matrix. [VERIFIED: AutoQAC.Tests/Services/XEditCommandBuilderTests.cs]

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|------------------|
| V2 Authentication | no | No authentication surface in this phase. [VERIFIED: .planning/REQUIREMENTS.md] |
| V3 Session Management | no | No session/auth state surface in this phase. [VERIFIED: .planning/REQUIREMENTS.md] |
| V4 Access Control | no | Local desktop process launch only; no authorization model change. [VERIFIED: AGENTS.md] |
| V5 Input Validation | yes | Validate only impossible/missing launch configuration; do not sanitize or rewrite plugin/path inputs. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |
| V6 Cryptography | no | No cryptography surface in this phase. [VERIFIED: .planning/REQUIREMENTS.md] |

### Known Threat Patterns for Windows process launch

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Argument injection via manual quoting mistakes | Tampering | Use `ArgumentList`; do not concatenate untrusted strings into `Arguments`. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] |
| Shell metacharacter misinterpretation | Tampering | Keep `UseShellExecute=false` and do not route through `cmd.exe`. [CITED: https://learn.microsoft.com/dotnet/fundamentals/runtime-libraries/system-diagnostics-processstartinfo-useshellexecute] |
| Unicode/case-page confusion | Tampering | Use Unicode .NET strings and avoid ANSI command-line APIs. [CITED: https://learn.microsoft.com/windows/win32/api/processenv/nf-processenv-getcommandlinea] |
| Sensitive command/path disclosure in UI | Information Disclosure | Keep full command details in logs only; user-facing messages should be concise and point to logs. [VERIFIED: .planning/phases/06-command-launch-escaping/06-CONTEXT.md] |

## Sources

### Primary (HIGH confidence)
- Microsoft Learn: `ProcessStartInfo.ArgumentList` — verified escaping behavior, independence from `Arguments`, and recommendation to prefer `ArgumentList` when escaping is uncertain. https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0
- Microsoft Learn: `ProcessStartInfo.Arguments` — verified single-string parsing caveats, length limit, and independent relationship with `ArgumentList`. https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.arguments?view=net-10.0
- Microsoft Learn: `ProcessStartInfo.UseShellExecute` supplemental docs — verified direct executable start behavior and `WorkingDirectory` semantics when false. https://learn.microsoft.com/dotnet/fundamentals/runtime-libraries/system-diagnostics-processstartinfo-useshellexecute
- Microsoft Learn: `CommandLineToArgvW` — verified Windows quote/backslash parser semantics. https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-commandlinetoargvw#remarks
- Microsoft Learn: Microsoft C command-line parsing — verified CRT quote/backslash parser rules. https://learn.microsoft.com/cpp/c-language/parsing-c-command-line-arguments?view=msvc-170
- Microsoft Learn: `GetCommandLineA` — verified Unicode-to-ANSI lossy conversion security risk. https://learn.microsoft.com/windows/win32/api/processenv/nf-processenv-getcommandlinea
- Repository files: `XEditCommandBuilder.cs`, `ProcessExecutionService.cs`, `XEditCommandBuilderTests.cs`, `ProcessExecutionIntegrationTests.cs`, `TestProcessHelper/Program.cs`, project files. [VERIFIED: codebase reads]

### Secondary (MEDIUM confidence)
- MO2 wiki mirror: executable Binary/Start in/Arguments fields and VFS launch behavior. https://github-wiki-see.page/m/ModOrganizer2/modorganizer/wiki/Executables-window
- Step xEdit Guide: xEdit launch arguments including `-autoload`, `-quickautoclean`, and MO usage context. https://stepmodifications.org/wiki/Guide:XEdit

### Tertiary (LOW confidence)
- Web search result referencing MO2 command-line `run` evolution in PR discussion; not used as a locked implementation source because phase context already locks the current AutoQAC contract. [VERIFIED: web search]

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — .NET APIs and test packages verified from Microsoft docs and project files. [CITED: https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0] [VERIFIED: AutoQAC.Tests/AutoQAC.Tests.csproj]
- Architecture: HIGH — affected files and process flow are directly visible in repository and phase context. [VERIFIED: AutoQAC/Services/Cleaning/XEditCommandBuilder.cs] [VERIFIED: AutoQAC/Services/Process/ProcessExecutionService.cs]
- Pitfalls: HIGH for .NET/Windows escaping, MEDIUM for MO2 nested `-a` behavior because exact CLI parser docs were not found in official MO2 wiki content. [CITED: https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-commandlinetoargvw#remarks] [CITED: https://github-wiki-see.page/m/ModOrganizer2/modorganizer/wiki/Executables-window]

**Research date:** 2026-04-28  
**Valid until:** 2026-05-28 for .NET/API guidance; 2026-05-05 for MO2 CLI assumptions unless verified against real MO2 or upstream docs.