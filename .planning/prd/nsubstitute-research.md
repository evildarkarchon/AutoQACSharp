# NSubstitute Migration Research

> Research findings for migrating AutoQACSharp's test suite from Moq 4.20.72 to NSubstitute.
> Date: 2026-02-07

---

## Table of Contents

1. [NSubstitute Equivalents for Moq Patterns](#1-nsubstitute-equivalents-for-moq-patterns)
2. [NSubstitute Advantages](#2-nsubstitute-advantages)
3. [Known Migration Gotchas](#3-known-migration-gotchas)
4. [Version Compatibility](#4-version-compatibility)
5. [NuGet Package Changes](#5-nuget-package-changes)
6. [Automated Migration Tools](#6-automated-migration-tools)
7. [NSubstitute Best Practices and Anti-Patterns](#7-nsubstitute-best-practices-and-anti-patterns)
8. [Project-Specific Considerations](#8-project-specific-considerations)

---

## 1. NSubstitute Equivalents for Moq Patterns

### 1.1 Creating Mocks

| Moq | NSubstitute |
|-----|-------------|
| `var mock = new Mock<IService>();` | `var mock = Substitute.For<IService>();` |
| `Mock.Of<IService>()` | `Substitute.For<IService>()` |
| `mock.Object` (to get the actual interface) | Not needed -- the substitute IS the object |

**Key difference**: In Moq, `mock` is a `Mock<T>` wrapper, and you access the actual `T` via `.Object`. In NSubstitute, the substitute is directly the `T` type. This eliminates the need for `.Object` everywhere.

```csharp
// Moq
var mock = new Mock<ICleaningService>();
var orchestrator = new CleaningOrchestrator(mock.Object);

// NSubstitute
var mock = Substitute.For<ICleaningService>();
var orchestrator = new CleaningOrchestrator(mock);
```

### 1.2 Setting Up Return Values

#### Simple Return Value: `Setup().Returns()` -> Direct `.Returns()`

```csharp
// Moq
mock.Setup(x => x.GetValue()).Returns("hello");

// NSubstitute
mock.GetValue().Returns("hello");
```

#### Return Value with Argument Matching

```csharp
// Moq
mock.Setup(x => x.GetById(It.IsAny<int>())).Returns(new Item());

// NSubstitute
mock.GetById(Arg.Any<int>()).Returns(new Item());
```

#### Return Value Based on Input: Lambda in Returns

```csharp
// Moq
mock.Setup(x => x.GetById(It.IsAny<int>()))
    .Returns((int id) => new Item { Id = id });

// NSubstitute
mock.GetById(Arg.Any<int>())
    .Returns(callInfo => new Item { Id = callInfo.ArgAt<int>(0) });
// Or using Arg<T>() if the type is unambiguous:
mock.GetById(Arg.Any<int>())
    .Returns(callInfo => new Item { Id = callInfo.Arg<int>() });
```

### 1.3 Async Return Values: `ReturnsAsync()` -> `Returns()`

NSubstitute does NOT have a `ReturnsAsync()` method. Instead, you use `Returns()` with `Task.FromResult()` or just pass the value directly (NSubstitute auto-wraps for `Task<T>` return types in recent versions).

```csharp
// Moq
mock.Setup(x => x.GetAsync()).ReturnsAsync("value");
mock.Setup(x => x.GetAsync()).ReturnsAsync(new List<string>());

// NSubstitute -- these all work:
mock.GetAsync().Returns("value");                          // auto-wraps in Task
mock.GetAsync().Returns(Task.FromResult("value"));         // explicit Task
mock.GetAsync().Returns(new List<string>());               // auto-wraps
mock.GetAsync().Returns(Task.FromResult(new List<string>())); // explicit

// For Task (non-generic void tasks):
mock.DoAsync().Returns(Task.CompletedTask);
```

**Important**: For `Task` (non-generic), you must use `Returns(Task.CompletedTask)` since there's no value to auto-wrap.

### 1.4 Multiple/Sequential Return Values: `SetupSequence()` -> Multi-arg `Returns()`

```csharp
// Moq
mock.SetupSequence(x => x.GetValue())
    .Returns("first")
    .Returns("second")
    .Returns("third");

// NSubstitute
mock.GetValue().Returns("first", "second", "third");
```

**Difference**: In Moq, `SetupSequence` returns `default(T)` after the sequence is exhausted. In NSubstitute, the LAST value continues to be returned for all subsequent calls. This is a behavioral difference to watch for.

### 1.5 Argument Matchers

| Moq | NSubstitute |
|-----|-------------|
| `It.IsAny<T>()` | `Arg.Any<T>()` |
| `It.Is<T>(predicate)` | `Arg.Is<T>(predicate)` |
| `It.IsIn(values)` | `Arg.Is<T>(x => values.Contains(x))` |
| `It.IsRegex(pattern)` | `Arg.Is<string>(x => Regex.IsMatch(x, pattern))` |
| `It.IsAnyType` | No direct equivalent (see note below) |
| `It.IsNotNull<T>()` | `Arg.Is<T>(x => x != null)` |

**`It.IsAnyType` gap**: Moq's `It.IsAnyType`, `It.IsValueType`, and `It.IsSubtype<T>` are NOT currently supported in NSubstitute. This is tracked in NSubstitute issue #634. Workaround: use specific type matchers or restructure the test.

### 1.6 Verification: `Verify()` -> `Received()`

#### Basic Verification

```csharp
// Moq
mock.Verify(x => x.DoSomething());
mock.Verify(x => x.DoSomething(), Times.Once);

// NSubstitute
mock.Received().DoSomething();
mock.Received(1).DoSomething();
```

#### Exact Call Counts

| Moq | NSubstitute |
|-----|-------------|
| `Times.Once` / `Times.Exactly(1)` | `Received(1)` |
| `Times.Never` / `Times.Exactly(0)` | `DidNotReceive()` |
| `Times.Exactly(N)` | `Received(N)` |
| `Times.AtLeastOnce` | `Received()` (default = at least 1) |
| `Times.AtLeast(N)` | No direct equivalent; use `ReceivedCalls()` with assertion |
| `Times.AtMost(N)` | No direct equivalent; use `ReceivedCalls()` with assertion |
| `Times.Between(min, max, ...)` | No direct equivalent; use `ReceivedCalls()` with assertion |

**Workaround for AtLeast/AtMost**:
```csharp
// Moq: mock.Verify(x => x.Method(), Times.AtLeast(3));
// NSubstitute:
mock.ReceivedCalls()
    .Count(c => c.GetMethodInfo().Name == "Method")
    .Should().BeGreaterOrEqualTo(3);
```

#### Never Called

```csharp
// Moq
mock.Verify(x => x.DoSomething(), Times.Never);

// NSubstitute
mock.DidNotReceive().DoSomething();
```

#### Verify with Argument Matchers

```csharp
// Moq
mock.Verify(x => x.Save(It.Is<string>(s => s.Contains("test"))), Times.Once);

// NSubstitute
mock.Received(1).Save(Arg.Is<string>(s => s.Contains("test")));
```

#### Verify Async Calls

```csharp
// Moq
mock.Verify(x => x.SaveAsync(It.IsAny<string>()), Times.Once);

// NSubstitute
await mock.Received(1).SaveAsync(Arg.Any<string>());
// Note: the `await` avoids CS4014 compiler warning but is not functionally required
```

### 1.7 VerifyNoOtherCalls: No Direct Equivalent

```csharp
// Moq
mock.VerifyNoOtherCalls();

// NSubstitute -- no built-in equivalent. Workaround:
mock.ReceivedCalls().Should().BeEmpty();
// Or after some expected calls:
mock.ReceivedCalls().Should().HaveCount(expectedCount);
// Or using a custom extension:
mock.ReceivedCalls()
    .Where(c => c.GetMethodInfo().Name != "ExpectedMethod")
    .Should().BeEmpty();
```

### 1.8 Callbacks: `Callback()` -> `AndDoes()` or `When..Do`

#### Callback on Methods with Return Values

```csharp
// Moq
mock.Setup(x => x.GetValue(It.IsAny<int>()))
    .Returns("result")
    .Callback((int x) => capturedArg = x);

// NSubstitute
mock.GetValue(Arg.Any<int>())
    .Returns("result")
    .AndDoes(callInfo => capturedArg = callInfo.ArgAt<int>(0));
```

#### Callback on Void Methods: `Callback()` -> `When..Do`

```csharp
// Moq
mock.Setup(x => x.DoSomething(It.IsAny<string>()))
    .Callback((string s) => captured = s);

// NSubstitute
mock.When(x => x.DoSomething(Arg.Any<string>()))
    .Do(callInfo => captured = callInfo.ArgAt<string>(0));
```

#### Per-Argument Callback with `Arg.Do<T>()`

```csharp
// NSubstitute has a more concise alternative:
mock.DoSomething(Arg.Do<string>(s => captured = s));
```

### 1.9 Throwing Exceptions: `Throws()` -> `Throws()` / `When..Do`

#### Synchronous Methods

```csharp
// Moq
mock.Setup(x => x.GetValue()).Throws(new InvalidOperationException());
mock.Setup(x => x.GetValue()).Throws<InvalidOperationException>();

// NSubstitute
mock.GetValue().Throws(new InvalidOperationException());
mock.GetValue().Throws<InvalidOperationException>();
// Or: mock.When(x => x.GetValue()).Do(_ => throw new InvalidOperationException());
```

#### Async Methods (Task-returning)

```csharp
// Moq
mock.Setup(x => x.GetAsync()).ThrowsAsync(new Exception("fail"));

// NSubstitute -- use ThrowsAsync or Returns with Task.FromException:
mock.GetAsync().ThrowsAsync(new Exception("fail"));
// Or:
mock.GetAsync().Returns(Task.FromException<string>(new Exception("fail")));

// For void Task methods:
mock.DoAsync().ThrowsAsync(new Exception("fail"));
// Or:
mock.DoAsync().Returns(Task.FromException(new Exception("fail")));
```

**Note**: `Throws()` and `ThrowsAsync()` are extension methods in the `NSubstitute.ExceptionExtensions` namespace. You must add `using NSubstitute.ExceptionExtensions;`.

### 1.10 Raising Events: `Raises()` -> `Raise.Event` / `Raise.EventWith`

```csharp
// Moq
mock.Raise(x => x.SomeEvent += null, new EventArgs());
mock.Raise(x => x.SomeEvent += null, EventArgs.Empty);

// NSubstitute
mock.SomeEvent += Raise.EventWith(new EventArgs());
mock.SomeEvent += Raise.Event();  // auto-creates default EventArgs

// For custom event args:
mock.LowFuel += Raise.EventWith(new LowFuelEventArgs(10));

// For Action-based events:
mock.OnAction += Raise.Event<Action<int>>(42);
```

### 1.11 Mock Behavior (Strict vs Loose)

```csharp
// Moq: Strict mode throws on unexpected calls
var mock = new Mock<IService>(MockBehavior.Strict);

// NSubstitute: No direct strict mode.
// All substitutes are "loose" by default (return default values for unconfigured calls).
// To enforce strict-like behavior, verify calls explicitly:
mock.ReceivedWithAnyArgs(1).Method(default);
// Or check no unexpected calls happened:
mock.ReceivedCalls().Should().HaveCount(expectedCount);
```

### 1.12 SetupAllProperties -> Auto-properties (Built-in)

```csharp
// Moq
mock.SetupAllProperties();

// NSubstitute: properties are auto-tracked by default.
// Setting and getting properties just works:
var sub = Substitute.For<IService>();
sub.Name = "test";
Assert.Equal("test", sub.Name);  // Works automatically
```

### 1.13 SetupGet/SetupSet -> Direct Property Access

```csharp
// Moq
mock.SetupGet(x => x.Name).Returns("test");
mock.SetupSet(x => x.Name = "test").Verifiable();
mock.VerifySet(x => x.Name = "test");

// NSubstitute
mock.Name.Returns("test");           // SetupGet equivalent
mock.Name = "test";                  // Set value
mock.Received().Name = "test";       // VerifySet equivalent
```

### 1.14 Clearing/Resetting Mocks

```csharp
// Moq
mock.Reset();
mock.Invocations.Clear();

// NSubstitute
mock.ClearReceivedCalls();  // Clears received call history only
// For full reset (clear returns + received calls):
mock.ClearSubstitute(ClearOptions.All);
// Or selective:
mock.ClearSubstitute(ClearOptions.ReceivedCalls);
mock.ClearSubstitute(ClearOptions.ReturnValues);
mock.ClearSubstitute(ClearOptions.CallActions);
```

### 1.15 Accessing Invocations/Calls

```csharp
// Moq
mock.Invocations;
mock.Invocations[0].Arguments;

// NSubstitute
mock.ReceivedCalls();
mock.ReceivedCalls().First().GetArguments();
mock.ReceivedCalls().First().GetMethodInfo();
```

### 1.16 Checking Call Order: `MockSequence` -> `Received.InOrder`

```csharp
// Moq (cumbersome MockSequence)
var sequence = new MockSequence();
mock1.InSequence(sequence).Setup(x => x.First());
mock2.InSequence(sequence).Setup(x => x.Second());

// NSubstitute (much cleaner)
Received.InOrder(() => {
    mock1.First();
    mock2.Second();
});
```

### 1.17 Protected Members: `mock.Protected()` -> Not Supported

Moq's `.Protected()` for testing protected members has no NSubstitute equivalent. NSubstitute only works with public interfaces and virtual methods. If you test protected methods with Moq's Protected API, you'll need to refactor (extract to interface, make public, or use partial substitutes).

### 1.18 Arg.Invoke / Arg.Do (NSubstitute-specific, no Moq equivalent)

```csharp
// NSubstitute: invoke a callback argument in-line
mock.DoWork(Arg.Any<string>(), Arg.Invoke("result"));
// This will automatically invoke the Action<string> parameter with "result"

// Capture argument values
var captured = new List<string>();
mock.DoWork(Arg.Do<string>(s => captured.Add(s)));
```

---

## 2. NSubstitute Advantages

### 2.1 Cleaner, More Readable Syntax
- **No `.Object` property**: The substitute IS the interface instance. Eliminates `.Object` clutter throughout tests.
- **No `Setup()` ceremony**: Call the method directly on the substitute and chain `.Returns()`.
- **No lambda wrappers for verification**: `mock.Received().Method()` vs `mock.Verify(x => x.Method())`.
- **Less noise = clearer test intent**: Tests read more like natural language.

### 2.2 Better Error Messages
- NSubstitute produces highly descriptive exceptions showing expected vs actual calls.
- Non-matching arguments are highlighted with `*` characters in the output.
- Example error:
  ```
  ReceivedCallsException: Expected to receive a call matching:
      Add(1, 2)
  Actually received no matching calls.
  Received 2 non-matching calls (non-matching arguments indicated with '*' characters):
      Add(*4*, *7*)
      Add(1, *5*)
  ```

### 2.3 Active Maintenance and Community
- NSubstitute 5.3.0 released October 2024; actively maintained as of early 2026.
- 2.9k GitHub stars, 279 forks, strong community.
- MIT-like license (BSD-3-Clause) -- no licensing concerns.
- No SponsorLink or telemetry issues (a factor in Moq's community reputation hit in 2023).

### 2.4 Built-in Roslyn Analyzers
- `NSubstitute.Analyzers.CSharp` catches common mistakes at compile time.
- Detects misuse of `Arg.Any`/`Arg.Is` outside of `Returns`/`Received` contexts.
- Detects non-virtual member substitution attempts.

### 2.5 Auto/Recursive Mocks
- NSubstitute automatically creates recursive substitutes for properties that return interfaces.
- No additional setup needed for chains like `mock.Parent.Child.Name`.

---

## 3. Known Migration Gotchas

### 3.1 Argument Matcher Mixing Rule (CRITICAL)
In NSubstitute, when using argument matchers (`Arg.Any<T>()`, `Arg.Is<T>()`), you must use matchers for ALL arguments of the same type, or use literal values for ALL. You cannot mix matchers with literal values for the same type.

```csharp
// This WON'T work if both args are int:
mock.Add(Arg.Any<int>(), 5);  // AMBIGUOUS -- NSubstitute can't tell which arg the matcher is for

// Use Arg.Is for literal matching:
mock.Add(Arg.Any<int>(), Arg.Is(5));  // Correct
```

However, this is only ambiguous when multiple parameters share the same type. Different types can be mixed:
```csharp
// This is fine (different types):
mock.Save("literal-string", Arg.Any<int>());  // OK
```

### 3.2 Async Returns Syntax
- No `ReturnsAsync()` -- use `Returns()` which auto-wraps in `Task.FromResult`.
- For `Task` (non-generic): use `Returns(Task.CompletedTask)`.
- For async exceptions: use `ThrowsAsync()` from `NSubstitute.ExceptionExtensions` or `Returns(Task.FromException<T>(...))`.

### 3.3 Sequential Returns Behavior Difference
- **Moq `SetupSequence`**: Returns `default(T)` after sequence exhausts.
- **NSubstitute `Returns(a, b, c)`**: Repeats the LAST value forever after sequence exhausts.
- This means tests relying on `default` after sequence exhaustion need adjustment.

### 3.4 No Direct Strict Mock Equivalent
- NSubstitute substitutes are always "loose" (return defaults for unconfigured calls).
- For strict-like behavior, explicitly verify all expected calls and check `ReceivedCalls()`.

### 3.5 No VerifyNoOtherCalls Equivalent
- Moq's `VerifyNoOtherCalls()` has no built-in NSubstitute equivalent.
- Use `mock.ReceivedCalls().Should().HaveCount(N)` or similar assertions.

### 3.6 Thread Safety / xUnit Parallel Execution
- **CRITICAL for this project**: NSubstitute's argument matchers use ambient state (static thread-local storage). If tests run in parallel and one test has a misplaced `Arg.Any`/`Arg.Is`, it can leak into another test's context causing `UnexpectedArgumentMatcherException`.
- The NSubstitute.Analyzers package can catch these at compile time (highly recommended).
- If parallel test failures occur, investigate misplaced matchers first. As a last resort, disable parallelization with `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
- Our project currently runs tests in parallel with xUnit -- this should work fine as long as matchers are used correctly.

### 3.7 `.Object` Removal Requires Care
- Find-and-replace of `.Object` can accidentally replace non-Moq usages (e.g., `someResult.Object`, `typeof(X).Object`).
- This is the most error-prone regex replacement in automated migration.

### 3.8 `It.IsAnyType` Not Supported
- Moq's `It.IsAnyType` for matching generic type parameters is not available in NSubstitute.
- NSubstitute issue #634 tracks this. Workaround: substitute for each concrete generic type used.

### 3.9 Protected Members
- Moq's `mock.Protected()` API has no NSubstitute equivalent.
- If any tests use this pattern, the production code must be refactored.

### 3.10 `Callback` with Strongly-Typed Arguments
- Moq `Callback()` receives strongly-typed arguments matching the method signature.
- NSubstitute `AndDoes()` / `When..Do` receives `CallInfo` which requires `ArgAt<T>(index)` or `Arg<T>()` to access arguments.

### 3.11 `ReturnsForAnyArgs` Behavior
- NSubstitute has `ReturnsForAnyArgs()` which ignores all argument matchers and returns for any call to that method.
- This is different from Moq where you must always set up with specific matchers or `It.IsAny<T>()`.

---

## 4. Version Compatibility

### NSubstitute 5.3.0 (Latest Stable)
- **Target frameworks**: .NET 6.0+, .NET Standard 2.0, .NET Framework 4.6.2+
- **.NET 10 compatibility**: Fully compatible. NSubstitute targets .NET Standard 2.0 and .NET 6.0, both of which are compatible with .NET 10.
- **C# 13 compatibility**: Fully compatible. NSubstitute works with any C# version.
- **xUnit 2.9.3 compatibility**: Fully compatible. NSubstitute has no xUnit-specific dependencies.
- **FluentAssertions 8.8.0 compatibility**: Fully compatible. These are independent libraries.
- **Castle.Core dependency**: NSubstitute uses Castle.Core (DynamicProxy) for proxying. This is the same proxy library Moq uses, so no new transitive dependency concerns.

### NSubstitute.Analyzers.CSharp 1.0.17 (Latest Stable)
- **Target framework**: .NET Standard 2.0
- **Fully compatible** with .NET 10 and any C# version.
- Provides Roslyn analyzers that run at compile time.

### Version Summary

| Package | Version | Compatibility |
|---------|---------|---------------|
| NSubstitute | 5.3.0 | .NET 10 + C# 13 + xUnit 2.9.3 -- fully compatible |
| NSubstitute.Analyzers.CSharp | 1.0.17 | .NET Standard 2.0 -- fully compatible |

---

## 5. NuGet Package Changes

### Remove
```xml
<!-- Remove from AutoQAC.Tests.csproj -->
<PackageReference Include="Moq" Version="4.20.72" />
```

### Add
```xml
<!-- Add to AutoQAC.Tests.csproj -->
<PackageReference Include="NSubstitute" Version="5.3.0" />
<PackageReference Include="NSubstitute.Analyzers.CSharp" Version="1.0.17">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

### Using Directive Changes
```csharp
// Remove
using Moq;

// Add
using NSubstitute;
using NSubstitute.ExceptionExtensions;  // Only if using Throws/ThrowsAsync
using NSubstitute.ReturnsExtensions;    // Only if using ReturnsNull/ReturnsNullForAnyArgs
```

---

## 6. Automated Migration Tools

### 6.1 `moq-to-nsub` CLI Tool (Recommended for Bulk Pass)
- **Package**: `Moq2NSubstitute` on NuGet (dotnet tool)
- **Repo**: https://github.com/dylan-asos/moq-to-nsub
- **Usage**: `moq2nsub convert --project-path <path-to-test-project>`
- **What it does**: Scans all `.cs` files, applies regex replacements, then runs `dotnet remove package Moq` and `dotnet add package NSubstitute`.
- **Accuracy**: ~80% -- handles common patterns but requires manual cleanup.
- **Status**: Development stopped (author completed their migration), but still functional.

### 6.2 MoqToNSubstituteConverter Web Tool
- **URL**: https://moqtonsubstitute.azurewebsites.net/
- **Repo**: https://github.com/samsmithnz/MoqToNSubstituteConverter
- **What it does**: Paste Moq code, get NSubstitute code. ~90% accuracy.
- **Status**: Development stopped, but still works.

### 6.3 Regex Find-and-Replace Patterns (Manual)

From Tim Deschryver's cheat sheet, these regex patterns can be applied in order:

| Find | Replace |
|------|---------|
| `using Moq` | `using NSubstitute` |
| `new Mock<(.*)>\(\)` | `Substitute.For<$1>()` |
| `Mock<(.*?)>` | `$1` (change type declarations) |
| `\.Setup\([\w\s]*=>[\w\s]*(.*)\)` | `$1` (strip Setup wrapper) |
| `\.ReturnsAsync\(` | `.Returns(` |
| `It\.IsAny` | `Arg.Any` |
| `It\.Is` | `Arg.Is` |
| `\.Object` | (careful -- may replace non-Moq properties) |
| `\.Verify\([\w\s]*=>[\w\s]*\.(.*)\)(.+?), Times\.Never\(\)\)` | `.DidNotReceive().$1)` |
| `\.Verify\([\w\s]*=>[\w\s]*\.(.*)\)(.+?), Times\.Once\(\)\)` | `.Received(1).$1)` |
| `using Moq.AutoMock` | `using AutoFixture.AutoNSubstitute` |

**Warning**: These regexes are starting points, not complete solutions. They handle ~80% of cases. Manual review is required for:
- `.Object` replacements (false positives)
- Complex callback patterns
- Async exception throwing
- Verify with argument capture

### 6.4 Recommended Migration Strategy

Given our 510+ tests, a hybrid approach is recommended:
1. **Run automated tool first** (`moq-to-nsub` CLI) for the bulk mechanical conversion.
2. **Manual review and fix** remaining compilation errors.
3. **Use NSubstitute.Analyzers** to catch misuses at compile time.
4. **Run full test suite** and fix semantic differences (sequential return behavior, etc.).

---

## 7. NSubstitute Best Practices and Anti-Patterns

### Best Practices

1. **Install NSubstitute.Analyzers.CSharp**: Catches misuses at compile time. This is strongly recommended by the NSubstitute team.

2. **Only use Arg matchers in Returns/Received contexts**: Using `Arg.Any` or `Arg.Is` outside of `Returns()`, `Received()`, or `When..Do` blocks causes `UnexpectedArgumentMatcherException` and can leak state to other tests.

3. **Prefer `Arg.Any<T>()` with `Returns()` over `ReturnsForAnyArgs()`**: The former is more explicit about which calls match.

4. **Use `Arg.Do<T>()` for argument capture**: Cleaner than `AndDoes()` with `CallInfo` indexing.
   ```csharp
   string captured = null;
   mock.Save(Arg.Do<string>(s => captured = s));
   // Now call the code under test
   sut.DoWork();
   captured.Should().Be("expected");
   ```

5. **Configure substitutes before exercising production code**: Avoid configuring returns or setting up callbacks while the substitute is being used by another thread.

6. **Use `Received.InOrder()` sparingly**: Temporal coupling makes tests brittle. Only use when call order is actually part of the contract.

7. **Prefer `DidNotReceive()` over `Received(0)`**: More readable and idiomatic.

8. **Use `ClearReceivedCalls()` in setup/teardown if reusing substitutes**: Prevents call count leaking between tests.

### Anti-Patterns to Avoid

1. **Using Arg matchers outside of substitute calls**:
   ```csharp
   // BAD -- Arg.Any used on real object, not substitute
   var result = realService.GetData(Arg.Any<int>());
   ```

2. **Modifying argument objects after the call**:
   ```csharp
   // BAD -- NSubstitute stores references, not snapshots
   var person = new Person { Name = "Alice" };
   mock.Save(person);
   person.Name = "Bob";  // This changes what Received() sees!
   mock.Received().Save(Arg.Is<Person>(p => p.Name == "Alice")); // FAILS
   ```

3. **Mixing literal values and Arg matchers for same-type parameters**:
   ```csharp
   // BAD -- ambiguous when both params are same type
   mock.Add(Arg.Any<int>(), 5);
   // GOOD
   mock.Add(Arg.Any<int>(), Arg.Is(5));
   ```

4. **Configuring substitutes while they're in use by other threads**: This leads to race conditions. Configure fully before passing to production code.

5. **Over-specifying**: Don't verify every single call. Only verify calls that are part of the behavior contract. Use `Arg.Any<T>()` for arguments you don't care about.

6. **Substituting for non-virtual concrete classes**: NSubstitute can only intercept virtual methods and interface methods. Non-virtual methods will run the real implementation.

---

## 8. Project-Specific Considerations

### 8.1 Our Moq Usage Patterns (from MEMORY.md)
- **Critical**: When adding optional parameters to interface methods, ALL mock Setup AND Verify calls must be updated. Moq does NOT treat C# optional parameters as truly optional in mock expressions.
- **NSubstitute equivalent**: Same constraint -- `Arg.Any<T>()` or specific values must be provided for ALL parameters. NSubstitute also matches by exact parameter count.

### 8.2 Test Volume
- 510+ tests across the test suite.
- Automated migration tool should handle the bulk, with manual cleanup for edge cases.

### 8.3 Async-Heavy Codebase
- Many services use `async Task` and `async Task<T>` patterns.
- The `ReturnsAsync()` -> `Returns()` change is straightforward but affects many lines.
- Remember: `Task.CompletedTask` for void task returns.

### 8.4 Event Testing
- If any tests verify event subscriptions or raise events via Moq's `.Raise()`, these need manual conversion to NSubstitute's `Raise.Event` / `Raise.EventWith` syntax.

### 8.5 `using NSubstitute.ExceptionExtensions` Required
- Any test that uses `Throws()` or `ThrowsAsync()` needs the `NSubstitute.ExceptionExtensions` namespace import.

### 8.6 No Impact on Production Code
- This migration is test-project-only. No changes to `AutoQAC/` production code.
- All changes confined to `AutoQAC.Tests/`.

---

## Sources

- [NSubstitute Official Documentation](https://nsubstitute.github.io/help/)
- [Tim Deschryver - Moq to NSubstitute Cheat Sheet](https://timdeschryver.dev/blog/a-cheat-sheet-to-migrate-from-moq-to-nsubstitute) (Jan 2026)
- [Ardalis - Porting Moq to NSubstitute](https://ardalis.com/porting-moq-nsubstitute/)
- [NimblePros - Moq vs NSubstitute Code Comparisons](https://blog.nimblepros.com/blogs/moq-vs-nsubstitute-code-comparisons/)
- [Improve and Repeat - How to Migrate from Moq to NSubstitute](https://improveandrepeat.com/2023/09/how-to-migrate-from-moq-to-nsubstitute/)
- [code4it - Moq vs NSubstitute Syntax Cheat Sheet](https://code4it.dev/blog/moq-vs-nsubstitute-syntax)
- [NSubstitute GitHub - Threading](https://nsubstitute.github.io/help/threading/)
- [NSubstitute NuGet](https://www.nuget.org/packages/nsubstitute/) (v5.3.0)
- [NSubstitute.Analyzers.CSharp NuGet](https://www.nuget.org/packages/NSubstitute.Analyzers.CSharp) (v1.0.17)
- [moq-to-nsub CLI Tool](https://github.com/dylan-asos/moq-to-nsub)
- [MoqToNSubstituteConverter](https://github.com/samsmithnz/MoqToNSubstituteConverter)
- [NSubstitute Issue #634 - IsAnyType support](https://github.com/nsubstitute/NSubstitute/issues/634)
- [NSubstitute Issue #720 - Migration tooling discussion](https://github.com/nsubstitute/NSubstitute/issues/720)
