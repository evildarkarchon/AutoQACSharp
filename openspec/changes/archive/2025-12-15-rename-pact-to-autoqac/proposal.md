# Proposal: Rename PACT References to AutoQAC

## Change ID
`rename-pact-to-autoqac`

## Summary
Rename all legacy "PACT" references in code, configuration files, and documentation to "AutoQAC" to establish consistent branding as the C# implementation diverges from the original XEdit-PACT project.

## Motivation
The C# Avalonia implementation is a distinct project from the original Python/Rust XEdit-PACT tool. Using "AutoQAC" consistently throughout the codebase:
- Establishes clear identity for the C# implementation
- Reduces confusion between the original and ported projects
- Prepares for eventual removal of `Code_To_Port/` reference directory
- Simplifies user-facing configuration (file/key names match the application name)

## Scope

### In Scope
1. **C# Model Classes** (`AutoQAC/Models/Configuration/`):
   - `MainConfiguration.cs`: `PACT_Data` → `AutoQAC_Data`, `PactData` → `AutoQacData`
   - `UserConfiguration.cs`: `PACT_Settings` → `AutoQAC_Settings`, `PactSettings` → `AutoQacSettings`

2. **YAML Configuration** (`AutoQAC Data/AutoQAC Main.yaml`):
   - Root key: `PACT_Data` → `AutoQAC_Data`
   - Settings section: `PACT_Settings` → `AutoQAC_Settings`
   - Ignore lists: `PACT_Ignore_*` → `AutoQAC_Ignore_*`
   - Error/warning messages: Update PACT references to AutoQAC

3. **Unit Tests** (`AutoQAC.Tests/Services/ConfigurationServiceTests.cs`):
   - Update inline YAML test data to use new key names

4. **Documentation** (non-Code_To_Port):
   - `CLAUDE.md`: Update configuration references
   - `README.md`: Update application description and file references
   - `openspec/project.md`: Already updated, verify consistency

### Out of Scope
- `Code_To_Port/` directory (reference material, will be removed at feature parity)
- `.gitmodules` URL (external repository reference)
- Historical references explaining the tool's origins (e.g., "C# implementation of XEdit-PACT")

## Impact Analysis

### Breaking Changes
- **Configuration file format change**: Users with existing `AutoQAC Main.yaml` files using `PACT_Data` keys will need to update them
- **Migration path**: Provide backwards-compatible loading that accepts both old and new key names during transition (optional enhancement)

### Risk Assessment
- **Low risk**: Internal refactoring with clear scope
- **Testing**: Existing configuration tests will verify the changes work correctly

## Success Criteria
1. All C# code uses `AutoQAC_*` naming for YAML aliases
2. All class names use `AutoQac*` prefix instead of `Pact*`
3. Default configuration file uses `AutoQAC_*` keys
4. All tests pass with new naming
5. Documentation is consistent
