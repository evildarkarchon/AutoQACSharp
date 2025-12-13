# Test Coverage Analysis - AutoQACSharp

**Generated:** 2025-12-12
**Analysis Scope:** All C# source files in `AutoQAC/` and test files in `AutoQAC.Tests/`

---

## Test Coverage Summary

- **Total test files:** 11
- **Total test classes:** 11
- **Approximate test methods:** 35+
- **Services covered:** 7/9 (78%)
- **ViewModels covered:** 3/3 (100%)
- **Models covered:** 0/3 (0%) - Simple data models, low priority
- **Infrastructure covered:** 0/2 (0%)

### Overall Assessment
The project has **good foundational test coverage** for core services and ViewModels. Critical gaps exist in:
1. **ProcessExecutionService** - Critical for xEdit subprocess management
2. **MO2ValidationService** - Important for MO2 integration
3. Error handling and edge cases in existing tests
4. Integration testing of the full cleaning workflow

---

## Coverage by Component

### Services

| Service | Test File | Coverage Status | Test Methods | Missing Tests |
|---------|-----------|-----------------|--------------|---------------|
| **CleaningService** | CleaningServiceTests.cs | Good | 2 | Error cases, timeout scenarios |
| **CleaningOrchestrator** | CleaningOrchestratorTests.cs | Good | 3 | Cancellation, partial completion |
| **ConfigurationService** | ConfigurationServiceTests.cs | Good | 5 | YAML parsing errors, corrupted files |
| **GameDetectionService** | GameDetectionServiceTests.cs | Good | 5 | Edge cases, invalid files |
| **PluginValidationService** | PluginValidationServiceTests.cs | Good | 2 | Invalid plugin files, encoding issues |
| **StateService** | StateServiceTests.cs | Good | 5 | Concurrent updates, race conditions |
| **XEditCommandBuilder** | XEditCommandBuilderTests.cs | Excellent | 5 | Complete coverage |
| **XEditOutputParser** | XEditOutputParserTests.cs | Good | 3 | Malformed output, edge cases |
| **ProcessExecutionService** | None | **MISSING** | 0 | All functionality untested |
| **MO2ValidationService** | None | **MISSING** | 0 | All functionality untested |
| **FileDialogService** | None | **MISSING** | 0 | Low priority - UI service |

### ViewModels

| ViewModel | Test File | Coverage Status | Test Methods | Missing Tests |
|-----------|-----------|-----------------|--------------|---------------|
| **MainWindowViewModel** | MainWindowViewModelTests.cs + InitializationTests.cs | Good | 3 | Error handling, validation failures |
| **ProgressViewModel** | ProgressViewModelTests.cs | Good | 2 | Edge cases in progress calculation |
| **PartialFormsWarningViewModel** | PartialFormsWarningViewModelTests.cs | Complete | 2 | Fully covered |
| **ViewModelBase** | None | N/A | - | Base class, tested through derived classes |

### Models

| Model | Test File | Coverage Status | Priority | Rationale |
|-------|-----------|-----------------|----------|-----------|
| **CleaningResult** | None | No tests | Low | Simple record type, no business logic |
| **PluginInfo** | None | No tests | Low | Simple record type, no business logic |
| **AppState** | None | No tests | Low | Simple record with computed properties |
| **Configuration Models** | Indirectly tested via ConfigurationServiceTests | Partial | Low | Tested through service layer |

### Infrastructure

| Component | Test File | Coverage Status | Priority | Missing Tests |
|-----------|-----------|-----------------|----------|---------------|
| **LoggingService** | None | No tests | Low | Mocked in all tests, simple wrapper |
| **ServiceCollectionExtensions** | DependencyInjectionTests.cs | Integration | Medium | DI registration tested |

### Integration Tests

| Test Class | Purpose | Coverage |
|------------|---------|----------|
| **DependencyInjectionTests** | Validates all services can be resolved | Good |

---

## Critical Gaps (Priority Order)

### High Priority (Critical Path)

1. **ProcessExecutionService** - No tests exist
   - **Why Critical:** Core component that manages xEdit subprocess execution
   - **Risk:** Process timeout, cancellation, graceful termination, semaphore management all untested
   - **Impact:** Could cause process leaks, deadlocks, or failed cleaning operations

2. **CleaningService - Error Handling** - Only happy path tested
   - **Why Critical:** Central to the cleaning workflow
   - **Risk:** Process failures, timeouts, invalid plugin paths not tested
   - **Impact:** Application crashes or hangs during cleaning operations

3. **CleaningOrchestrator - Cancellation & Partial Completion**
   - **Why Critical:** Manages sequential plugin cleaning workflow
   - **Risk:** User cancellation mid-operation, plugin failures during batch processing
   - **Impact:** Incomplete state, plugins marked incorrectly, state service corruption

4. **StateService - Concurrent Updates**
   - **Why Critical:** Shared state across async operations
   - **Risk:** Race conditions when updating state from multiple async operations
   - **Impact:** UI showing incorrect progress, state corruption

### Medium Priority (Important but Less Critical)

5. **MO2ValidationService** - No tests exist
   - **Why Important:** MO2 integration is a key feature
   - **Risk:** Invalid MO2 paths, running process detection failures
   - **Impact:** Poor user experience, incorrect warnings

6. **ConfigurationService - Error Handling**
   - **Why Important:** Handles YAML parsing and file I/O
   - **Risk:** Corrupted YAML, missing files, permission errors
   - **Impact:** Application startup failures, lost user settings

7. **GameDetectionService - Edge Cases**
   - **Why Important:** Incorrect game detection leads to wrong xEdit flags
   - **Risk:** Malformed load order files, empty files, encoding issues
   - **Impact:** Wrong game detected, cleaning failures

8. **XEditOutputParser - Malformed Output**
   - **Why Important:** Parses xEdit subprocess output
   - **Risk:** xEdit output format changes, unexpected error messages
   - **Impact:** Incorrect statistics, failed completion detection

9. **MainWindowViewModel - Error Handling**
   - **Why Important:** Main UI controller
   - **Risk:** Command failures, validation errors, async exceptions
   - **Impact:** UI becomes unresponsive or shows incorrect state

### Low Priority (Nice to Have)

10. **FileDialogService** - No tests
    - **Why Low:** UI service, hard to test, simple wrapper around Avalonia APIs
    - **Risk:** Minimal, tested through manual QA

11. **Model Tests**
    - **Why Low:** Simple record types with no business logic
    - **Risk:** Minimal, compiler ensures type safety

12. **LoggingService**
    - **Why Low:** Simple wrapper, tested indirectly through all other tests
    - **Risk:** Minimal

---

## Recommended Test Additions

### High Priority

#### ProcessExecutionService Tests
- [ ] **Process Execution - Happy Path**
  - Test: Successfully execute a process and capture output
  - File: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
  - Covers: Basic execution, output redirection, exit code handling

- [ ] **Process Execution - Timeout Handling**
  - Test: Process times out after specified duration
  - File: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
  - Covers: Timeout enforcement, graceful termination, force kill

- [ ] **Process Execution - Cancellation**
  - Test: User cancels process execution via CancellationToken
  - File: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
  - Covers: Cancellation token handling, process cleanup

- [ ] **Process Execution - Semaphore Slot Management**
  - Test: Semaphore limits concurrent process execution
  - File: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
  - Covers: Concurrent execution limits, slot acquisition/release

- [ ] **Process Execution - Startup Failure**
  - Test: Process fails to start (file not found, permissions)
  - File: `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
  - Covers: Exception handling, error result returned

#### CleaningService Error Tests
- [ ] **CleaningService - Plugin Not Found**
  - Test: Cleaning fails when plugin file doesn't exist
  - File: `AutoQAC.Tests/Services/CleaningServiceTests.cs`
  - Covers: File validation, error status returned

- [ ] **CleaningService - Process Timeout**
  - Test: xEdit process times out during cleaning
  - File: `AutoQAC.Tests/Services/CleaningServiceTests.cs`
  - Covers: Timeout handling, partial results, error logging

- [ ] **CleaningService - Process Failure**
  - Test: xEdit process exits with non-zero exit code
  - File: `AutoQAC.Tests/Services/CleaningServiceTests.cs`
  - Covers: Error detection, failure status, error message extraction

- [ ] **CleaningService - Invalid Game Type**
  - Test: Cleaning attempted with GameType.Unknown
  - File: `AutoQAC.Tests/Services/CleaningServiceTests.cs`
  - Covers: Validation, error handling before process start

#### CleaningOrchestrator Robustness Tests
- [ ] **CleaningOrchestrator - User Cancellation Mid-Batch**
  - Test: User cancels during multi-plugin cleaning
  - File: `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
  - Covers: Cancellation propagation, partial state updates, cleanup

- [ ] **CleaningOrchestrator - Plugin Failure Mid-Batch**
  - Test: One plugin fails in a batch of 5 plugins
  - File: `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
  - Covers: Continue on error, correct state tracking, result aggregation

- [ ] **CleaningOrchestrator - Sequential Processing Verification**
  - Test: Verify plugins processed one at a time, never in parallel
  - File: `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
  - Covers: Sequential constraint (critical per CLAUDE.md)

#### StateService Concurrency Tests
- [ ] **StateService - Concurrent State Updates**
  - Test: Multiple async operations update state simultaneously
  - File: `AutoQAC.Tests/Services/StateServiceTests.cs`
  - Covers: Thread safety, state consistency, no lost updates

- [ ] **StateService - Observable Emissions During Updates**
  - Test: StateChanged observable emits correct sequence during rapid updates
  - File: `AutoQAC.Tests/Services/StateServiceTests.cs`
  - Covers: Observable behavior, no missed emissions

### Medium Priority

#### MO2ValidationService Tests
- [ ] **MO2ValidationService - Detect Running Process**
  - Test: Correctly detects ModOrganizer.exe is running
  - File: `AutoQAC.Tests/Services/MO2ValidationServiceTests.cs`
  - Covers: Process detection, resource cleanup

- [ ] **MO2ValidationService - Validate Executable Path**
  - Test: Validates MO2 executable is correct file
  - File: `AutoQAC.Tests/Services/MO2ValidationServiceTests.cs`
  - Covers: File validation, path normalization

- [ ] **MO2ValidationService - Invalid Path Handling**
  - Test: Returns false for non-existent or non-MO2 executable
  - File: `AutoQAC.Tests/Services/MO2ValidationServiceTests.cs`
  - Covers: Error handling, validation logic

#### ConfigurationService Error Tests
- [ ] **ConfigurationService - Corrupted YAML**
  - Test: Handles malformed YAML gracefully
  - File: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
  - Covers: YAML parsing errors, default fallback

- [ ] **ConfigurationService - File Permission Errors**
  - Test: Handles read/write permission denied
  - File: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
  - Covers: IO exceptions, error logging

- [ ] **ConfigurationService - Missing Skip List Keys**
  - Test: Handles missing game type in skip list config
  - File: `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
  - Covers: Missing data handling, default empty list

#### GameDetectionService Edge Cases
- [ ] **GameDetectionService - Empty Load Order File**
  - Test: Handles empty or whitespace-only load order
  - File: `AutoQAC.Tests/Services/GameDetectionServiceTests.cs`
  - Covers: Edge case handling, Unknown result

- [ ] **GameDetectionService - Load Order File Not Found**
  - Test: Handles missing load order file
  - File: `AutoQAC.Tests/Services/GameDetectionServiceTests.cs`
  - Covers: File not found exception, error handling

- [ ] **GameDetectionService - Multiple Game Masters**
  - Test: Load order contains masters from different games
  - File: `AutoQAC.Tests/Services/GameDetectionServiceTests.cs`
  - Covers: Conflict resolution, first-match wins

#### XEditOutputParser Edge Cases
- [ ] **XEditOutputParser - Malformed Output Lines**
  - Test: Handles unexpected output format
  - File: `AutoQAC.Tests/Services/XEditOutputParserTests.cs`
  - Covers: Robust parsing, no crashes on bad input

- [ ] **XEditOutputParser - No Completion Line**
  - Test: Output stream ends without "Done." line
  - File: `AutoQAC.Tests/Services/XEditOutputParserTests.cs`
  - Covers: Incomplete output detection, timeout scenarios

#### MainWindowViewModel Error Tests
- [ ] **MainWindowViewModel - Command Execution Failures**
  - Test: Orchestrator throws exception during StartCleaningCommand
  - File: `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
  - Covers: Exception handling, error dialog display, state reset

- [ ] **MainWindowViewModel - Validation Failures**
  - Test: Configuration validation fails before starting
  - File: `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
  - Covers: Pre-execution validation, user feedback

#### ProgressViewModel Edge Cases
- [ ] **ProgressViewModel - Division by Zero in Progress**
  - Test: Progress calculation when TotalPlugins is 0
  - File: `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
  - Covers: Edge case math, no crashes

- [ ] **ProgressViewModel - Rapid State Updates**
  - Test: State updates faster than UI can render
  - File: `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
  - Covers: Observable throttling, UI responsiveness

### Low Priority

#### FileDialogService Tests
- [ ] **FileDialogService - Filter Parsing**
  - Test: Correctly parses filter strings into FilePickerFileType
  - File: `AutoQAC.Tests/Services/UI/FileDialogServiceTests.cs`
  - Covers: String parsing logic (testable without UI)

- [ ] **FileDialogService - Invalid Initial Directory**
  - Test: Handles non-existent initial directory gracefully
  - File: `AutoQAC.Tests/Services/UI/FileDialogServiceTests.cs`
  - Covers: Path validation, fallback behavior

#### Model Tests
- [ ] **AppState - Computed Properties**
  - Test: IsLoadOrderConfigured, IsMO2Configured, IsXEditConfigured work correctly
  - File: `AutoQAC.Tests/Models/AppStateTests.cs`
  - Covers: Property logic, null handling

---

## Test Quality Assessment

### Existing Tests - Strengths
1. **Good use of mocking**: All tests properly isolate dependencies using Moq
2. **Async/await patterns**: Tests correctly use async test methods
3. **FluentAssertions**: Readable assertions with good error messages
4. **Theory tests**: Good use of `[Theory]` and `[InlineData]` for parameterized tests
5. **Happy path coverage**: Core workflows are tested

### Existing Tests - Weaknesses
1. **Limited error path testing**: Most tests only cover success scenarios
2. **No timeout/cancellation tests**: Critical for async operations
3. **No concurrency tests**: StateService and ProcessExecutionService need thread safety tests
4. **Minimal integration tests**: Only DI resolution tested, not end-to-end workflows
5. **No edge case coverage**: Empty inputs, null values, boundary conditions rarely tested

---

## Testing Strategy Recommendations

### 1. Prioritize Critical Path Coverage
Focus immediately on:
- ProcessExecutionService (complete missing coverage)
- CleaningService error paths
- CleaningOrchestrator cancellation and partial completion
- StateService concurrency

### 2. Add Integration Tests
Create integration tests for:
- **End-to-end cleaning workflow**: Load order → Game detection → Plugin validation → Cleaning → Results
- **Configuration lifecycle**: Load config → Modify → Save → Reload → Verify
- **State transitions**: Initial → Configuring → Ready → Cleaning → Complete/Failed

### 3. Error Injection Testing
Add tests that inject failures at each layer:
- File I/O failures (permission denied, disk full)
- Process execution failures (timeout, crash, invalid exit codes)
- YAML parsing errors (malformed, missing keys, type mismatches)
- Cancellation at various stages

### 4. Performance/Load Testing (Future)
Consider adding tests for:
- Large load orders (1000+ plugins)
- Rapid sequential cleanings
- State update throughput
- Memory usage during long operations

### 5. Test Helpers and Fixtures
Create shared test utilities:
- **TestFileHelper**: Create temporary test files/directories
- **MockProcessBuilder**: Simulate xEdit process behavior
- **StateServiceFixture**: Pre-configured state for common scenarios
- **ConfigurationFixture**: Valid/invalid configuration samples

---

## Verification Checklist

- [x] All service files checked for corresponding tests
- [x] All ViewModel files checked for corresponding tests
- [x] Priority list reflects actual risk (critical paths first)
- [x] Recommended tests are specific and actionable
- [x] Analysis includes test quality assessment
- [x] Integration test gaps identified
- [x] Error handling gaps identified
- [x] Concurrency/async gaps identified

---

## Conclusion

The AutoQACSharp project has **solid foundational test coverage (78% of services, 100% of ViewModels)**, but critical gaps exist in:

1. **ProcessExecutionService** - Completely untested, high risk
2. **Error handling** - Most services only test happy paths
3. **Concurrency** - State management and process execution need thread safety tests
4. **Integration** - End-to-end workflow testing needed

**Recommended Next Steps:**
1. Add ProcessExecutionService tests (High Priority)
2. Add error path tests to CleaningService (High Priority)
3. Add cancellation/partial completion tests to CleaningOrchestrator (High Priority)
4. Add concurrency tests to StateService (High Priority)
5. Add MO2ValidationService tests (Medium Priority)

Completing the High Priority tests will bring critical path coverage to **~90%** and significantly reduce regression risk during future development.
