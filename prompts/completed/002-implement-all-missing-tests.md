<objective>
Implement all missing tests identified in the test coverage analysis to achieve comprehensive test coverage for the AutoQACSharp project.

This task will close all test gaps (High, Medium, and Low priority) identified in `./docs/test-coverage-analysis.md`, bringing the project to ~95% critical path coverage and significantly reducing regression risk.
</objective>

<context>
This is a C# Avalonia MVVM application using:
- .NET 8 with C# 12
- xUnit for testing
- Moq for mocking
- FluentAssertions for assertions

Read the CLAUDE.md for project conventions, architecture patterns, and critical constraints (especially the sequential processing requirement for xEdit).

Existing test structure:
- `AutoQAC.Tests/` - Test project root
- Tests use async patterns with `Task`-based test methods
- All dependencies are mocked using Moq
- FluentAssertions used for readable assertions
</context>

<data_sources>
Reference these files for implementation patterns:
@docs/test-coverage-analysis.md - Complete gap analysis with specific test recommendations
@AutoQAC.Tests/**/*Tests.cs - Existing test patterns to follow
@AutoQAC/Services/**/*.cs - Services being tested (verify APIs before writing tests)
@AutoQAC/ViewModels/**/*.cs - ViewModels being tested
</data_sources>

<requirements>
Implement ALL tests listed in the coverage analysis, organized by priority:

### HIGH PRIORITY - Must Complete First

#### 1. ProcessExecutionService Tests (NEW FILE)
Create `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`:
- [ ] Process Execution - Happy Path (execute process, capture output, handle exit code)
- [ ] Process Execution - Timeout Handling (enforce timeout, graceful termination, force kill)
- [ ] Process Execution - Cancellation (CancellationToken handling, process cleanup)
- [ ] Process Execution - Semaphore Slot Management (concurrent execution limits)
- [ ] Process Execution - Startup Failure (file not found, permissions errors)

#### 2. CleaningService Error Tests
Expand `AutoQAC.Tests/Services/CleaningServiceTests.cs`:
- [ ] CleaningService - Plugin Not Found
- [ ] CleaningService - Process Timeout
- [ ] CleaningService - Process Failure (non-zero exit code)
- [ ] CleaningService - Invalid Game Type (GameType.Unknown)

#### 3. CleaningOrchestrator Robustness Tests
Expand `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`:
- [ ] CleaningOrchestrator - User Cancellation Mid-Batch
- [ ] CleaningOrchestrator - Plugin Failure Mid-Batch (continue on error)
- [ ] CleaningOrchestrator - Sequential Processing Verification (CRITICAL per CLAUDE.md)

#### 4. StateService Concurrency Tests
Expand `AutoQAC.Tests/Services/StateServiceTests.cs`:
- [ ] StateService - Concurrent State Updates (thread safety)
- [ ] StateService - Observable Emissions During Rapid Updates

### MEDIUM PRIORITY

#### 5. MO2ValidationService Tests (NEW FILE)
Create `AutoQAC.Tests/Services/MO2ValidationServiceTests.cs`:
- [ ] MO2ValidationService - Detect Running Process
- [ ] MO2ValidationService - Validate Executable Path
- [ ] MO2ValidationService - Invalid Path Handling

#### 6. ConfigurationService Error Tests
Expand `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`:
- [ ] ConfigurationService - Corrupted YAML
- [ ] ConfigurationService - File Permission Errors
- [ ] ConfigurationService - Missing Skip List Keys

#### 7. GameDetectionService Edge Cases
Expand `AutoQAC.Tests/Services/GameDetectionServiceTests.cs`:
- [ ] GameDetectionService - Empty Load Order File
- [ ] GameDetectionService - Load Order File Not Found
- [ ] GameDetectionService - Multiple Game Masters (conflict resolution)

#### 8. XEditOutputParser Edge Cases
Expand `AutoQAC.Tests/Services/XEditOutputParserTests.cs`:
- [ ] XEditOutputParser - Malformed Output Lines
- [ ] XEditOutputParser - No Completion Line

#### 9. MainWindowViewModel Error Tests
Expand `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`:
- [ ] MainWindowViewModel - Command Execution Failures
- [ ] MainWindowViewModel - Validation Failures

#### 10. ProgressViewModel Edge Cases
Expand `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`:
- [ ] ProgressViewModel - Division by Zero in Progress (TotalPlugins = 0)
- [ ] ProgressViewModel - Rapid State Updates

### LOW PRIORITY

#### 11. FileDialogService Tests (NEW FILE if feasible)
Create `AutoQAC.Tests/Services/UI/FileDialogServiceTests.cs`:
- [ ] FileDialogService - Filter Parsing (testable without UI)
- [ ] FileDialogService - Invalid Initial Directory

#### 12. Model Tests (NEW FILE)
Create `AutoQAC.Tests/Models/AppStateTests.cs`:
- [ ] AppState - Computed Properties (IsLoadOrderConfigured, IsMO2Configured, IsXEditConfigured)
</requirements>

<implementation_guidelines>
1. **CRITICAL: Verify APIs First**
   - Before writing ANY test, read the actual service/class being tested
   - Verify method signatures, parameter types, return types
   - Check what interfaces exist and what can be mocked
   - Do NOT assume APIs match the analysis - the code may have changed

2. **Follow Existing Patterns**
   - Match the coding style and structure of existing tests
   - Use the same mocking patterns (Moq setup/verify)
   - Use FluentAssertions consistently
   - Follow async test patterns with `async Task` methods

3. **Test Quality Standards**
   - Each test should have a clear Arrange/Act/Assert structure
   - Test names should describe the scenario being tested
   - Use `[Fact]` for single tests, `[Theory]` with `[InlineData]` for parameterized tests
   - Mock all dependencies - tests should be isolated

4. **Error Path Testing**
   - For error tests, verify the correct exception type is thrown OR the correct error result is returned
   - Verify error messages are meaningful
   - Check that state is not corrupted after errors

5. **Concurrency Testing**
   - Use `Task.WhenAll` to simulate concurrent operations
   - Use `SemaphoreSlim` or other synchronization if needed
   - Verify thread safety with multiple simultaneous operations

6. **Sequential Processing Verification**
   - For CleaningOrchestrator, verify that plugins are NEVER processed in parallel
   - Use timing or callback tracking to prove sequential execution
</implementation_guidelines>

<constraints>
- Never use `.Result` or `.Wait()` - always `await` async operations
- Never use `Thread.Sleep()` - use `Task.Delay()` for timing
- All test classes should be `public` and use the `IClassFixture<T>` pattern if shared setup is needed
- Tests should complete in under 5 seconds each (mock slow operations)
- Do not create integration tests that require actual file system or process execution
</constraints>

<verification>
Before declaring complete:

1. **Build Check**: Run `dotnet build AutoQAC.Tests` - must compile without errors
2. **Test Run**: Run `dotnet test AutoQAC.Tests` - all tests must pass
3. **Coverage Check**: Verify each gap from the analysis has at least one corresponding test
4. **Pattern Check**: Ensure new tests follow existing code patterns and conventions
</verification>

<success_criteria>
- All 30+ tests from the coverage analysis are implemented
- All tests compile and pass
- New test files created for ProcessExecutionService, MO2ValidationService, FileDialogService, and AppState
- Existing test files expanded with error/edge case tests
- Code follows existing patterns and conventions
- No regressions in existing tests
</success_criteria>

<output>
Test files created/modified:
- `./AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` (NEW)
- `./AutoQAC.Tests/Services/MO2ValidationServiceTests.cs` (NEW)
- `./AutoQAC.Tests/Services/UI/FileDialogServiceTests.cs` (NEW)
- `./AutoQAC.Tests/Models/AppStateTests.cs` (NEW)
- `./AutoQAC.Tests/Services/CleaningServiceTests.cs` (EXPANDED)
- `./AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` (EXPANDED)
- `./AutoQAC.Tests/Services/StateServiceTests.cs` (EXPANDED)
- `./AutoQAC.Tests/Services/ConfigurationServiceTests.cs` (EXPANDED)
- `./AutoQAC.Tests/Services/GameDetectionServiceTests.cs` (EXPANDED)
- `./AutoQAC.Tests/Services/XEditOutputParserTests.cs` (EXPANDED)
- `./AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` (EXPANDED)
- `./AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs` (EXPANDED)
</output>
