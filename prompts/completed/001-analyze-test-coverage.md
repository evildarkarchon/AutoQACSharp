<objective>
Analyze test coverage for the AutoQACSharp project and identify areas that need better test coverage.

This analysis will help prioritize which components need additional tests to improve code quality and reduce regression risk.
</objective>

<context>
This is a C# Avalonia MVVM application for plugin cleaning that interfaces with xEdit.

Key project structure:
- `AutoQAC/` - Main application code
  - `Services/` - Business logic services
  - `ViewModels/` - MVVM presentation logic
  - `Models/` - Data models and configuration
  - `Infrastructure/` - Logging and DI setup
- `AutoQAC.Tests/` - Test project

Read the CLAUDE.md for project conventions and architecture patterns.
</context>

<data_sources>
@AutoQAC/**/*.cs
@AutoQAC.Tests/**/*.cs
</data_sources>

<analysis_requirements>
1. **Inventory existing tests**: List all test classes and what they cover
2. **Map coverage by component**:
   - Services: Which services have tests? Which are missing?
   - ViewModels: Which ViewModels have tests? Which are missing?
   - Models: Are there any model tests? Should there be?
   - Infrastructure: Any tests for logging, DI, etc.?

3. **Identify critical gaps**: Focus on:
   - Services that handle critical operations (CleaningService, CleaningOrchestrator, ProcessExecutionService)
   - Error handling paths
   - Edge cases in configuration loading
   - State management transitions

4. **Assess test quality**:
   - Do existing tests cover happy paths AND error paths?
   - Are async patterns tested correctly?
   - Are there integration tests where needed?

5. **Prioritize missing coverage**:
   - High priority: Core cleaning workflow, state management
   - Medium priority: Configuration, game detection
   - Low priority: UI services, simple models
</analysis_requirements>

<output_format>
Create a structured coverage report with:

## Test Coverage Summary
- Total test files: X
- Total test methods: X
- Services covered: X/Y
- ViewModels covered: X/Y

## Coverage by Component

### Services
| Service | Test File | Coverage Status | Missing Tests |
|---------|-----------|-----------------|---------------|
| ... | ... | ... | ... |

### ViewModels
| ViewModel | Test File | Coverage Status | Missing Tests |
|-----------|-----------|-----------------|---------------|
| ... | ... | ... | ... |

## Critical Gaps (Priority Order)
1. [Component] - [What's missing] - [Why it matters]
2. ...

## Recommended Test Additions
### High Priority
- [ ] Test: [description] in [file]
- ...

### Medium Priority
- [ ] Test: [description] in [file]
- ...

### Low Priority
- [ ] Test: [description] in [file]
- ...

Save analysis to: `./docs/test-coverage-analysis.md`
</output_format>

<verification>
Before completing, verify:
- All service files have been checked for corresponding tests
- All ViewModel files have been checked for corresponding tests
- The priority list reflects actual risk (critical paths first)
- Recommended tests are specific and actionable
</verification>

<success_criteria>
- Complete mapping of existing tests to source files
- Clear identification of untested components
- Prioritized list of recommended test additions
- Analysis saved to specified location
</success_criteria>
