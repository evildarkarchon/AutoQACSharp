<!--
SYNC IMPACT REPORT
==================
Version change: 0.0.0 (template) -> 1.0.0 (initial ratification)

Modified principles: N/A (initial version)

Added sections:
- Core Principles (5 principles defined)
- Technology Constraints section
- Development Workflow section
- Governance section with amendment procedures

Removed sections: N/A (initial version)

Templates requiring updates:
- .specify/templates/plan-template.md: Constitution Check section exists, compatible
- .specify/templates/spec-template.md: Compatible, no changes needed
- .specify/templates/tasks-template.md: Compatible, no changes needed

Follow-up TODOs: None
-->

# AutoQAC Constitution

## Core Principles

### I. MVVM Architecture (NON-NEGOTIABLE)

This project follows strict Model-View-ViewModel separation:

- **Models** (`AutoQAC/Models/`): Pure business logic and data structures with NO UI
  dependencies (no Avalonia, no ReactiveUI imports)
- **ViewModels** (`AutoQAC/ViewModels/`): Presentation logic only, inherit from
  `ReactiveObject`, use `ReactiveCommand` for commands, NO direct UI manipulation
- **Views** (`AutoQAC/Views/`): XAML with minimal code-behind, pure UI rendering via
  data binding, NO business logic

**Rationale**: Strict layer separation enables independent testing, maintains
codebase clarity, and prevents coupling that leads to unmaintainable code.

### II. Sequential Processing (NON-NEGOTIABLE)

Plugin cleaning operations MUST execute one at a time, never in parallel:

- Only ONE xEdit process may run at any moment
- Use `foreach` with `await`, never `Task.WhenAll` for cleaning operations
- Multiple xEdit windows opening simultaneously is a CRITICAL BUG
- Process queues MUST be strictly sequential

**Rationale**: xEdit's file locking mechanisms make parallel processing impossible.
Violating this constraint corrupts plugin files and crashes the application.

### III. Async-First

All I/O and long-running operations MUST be asynchronous:

- Async methods MUST use `Async` suffix
- NEVER use `.Result` or `.Wait()` (causes UI thread deadlocks)
- Use `ReactiveCommand.CreateFromTask` for async commands
- Use `ConfigureAwait(false)` in non-UI library code
- Use `Dispatcher.UIThread.InvokeAsync` for cross-thread UI updates

**Rationale**: Blocking the UI thread creates unresponsive applications and deadlocks
in Avalonia's reactive model.

### IV. Dependency Injection

Services and dependencies MUST be injected via constructors:

- NO static mutable state
- NO service locators or ambient context
- ALL services accessed through interfaces
- Registration occurs in `Program.cs` or `App.axaml.cs`

**Rationale**: Constructor injection enables unit testing with mocks, makes
dependencies explicit, and prevents hidden coupling.

### V. Error Transparency

Exceptions MUST be handled explicitly and never swallowed:

- Use structured exception handling with try/catch
- Log ALL exceptions with context (timestamp, thread ID, operation)
- Display user-friendly error messages via dialogs
- NEVER catch exceptions without logging or re-throwing
- Use Serilog or Microsoft.Extensions.Logging for structured logging

**Rationale**: Silent exception swallowing hides bugs, corrupts state, and makes
debugging impossible. Users deserve clear error feedback.

## Technology Constraints

**Stack Requirements**:

- **.NET 9** with C# 12, nullable reference types ENABLED
- **Avalonia UI 11.3.8** for cross-platform XAML
- **ReactiveUI** for MVVM reactive programming
- **YamlDotNet** for configuration serialization
- **xUnit** with FluentAssertions for testing

**Code Standards**:

- One class per file, file name matches class name
- PascalCase for public members, `_camelCase` for private fields
- Compiled bindings preferred over reflection-based bindings
- `IDisposable` objects MUST be disposed (especially event subscriptions)

## Development Workflow

1. **Reference First**: Check `Code_To_Port/` for equivalent functionality
2. **Models First**: Implement business logic before UI
3. **ViewModels Second**: Build presentation logic with ReactiveUI
4. **Views Last**: Create XAML UI with data binding
5. **Test Incrementally**: Verify each layer before proceeding
6. **Sequential Always**: Never parallelize plugin operations

## Governance

This constitution supersedes all other development practices for this project:

- All code reviews MUST verify compliance with these principles
- Violations require explicit justification in PR description
- Complexity beyond these principles requires documented rationale
- Use CLAUDE.md for runtime development guidance and examples

**Amendment Procedure**:

1. Propose changes via pull request modifying this file
2. Document rationale for additions, removals, or modifications
3. Update version according to semantic versioning:
   - MAJOR: Principle removal or incompatible redefinition
   - MINOR: New principle or substantial guidance expansion
   - PATCH: Clarifications, wording, typo fixes
4. Ensure dependent templates remain compatible

**Compliance Review**: Quarterly review of constitution relevance and adherence.

**Version**: 1.0.0 | **Ratified**: 2026-01-19 | **Last Amended**: 2026-01-19
