# Tasks: Rename PACT References to AutoQAC

## Task List

### 1. Update C# Model Classes
- [x] **1.1** Rename `PactData` class to `AutoQacData` in `MainConfiguration.cs`
- [x] **1.2** Change `[YamlMember(Alias = "PACT_Data")]` to `[YamlMember(Alias = "AutoQAC_Data")]`
- [x] **1.3** Rename `PactSettings` class to `AutoQacSettings` in `UserConfiguration.cs`
- [x] **1.4** Change `[YamlMember(Alias = "PACT_Settings")]` to `[YamlMember(Alias = "AutoQAC_Settings")]`

**Verification**: Build succeeds (`dotnet build`)

### 2. Update YAML Configuration File
- [x] **2.1** Change root key from `PACT_Data:` to `AutoQAC_Data:` in `AutoQAC Data/AutoQAC Main.yaml`
- [x] **2.2** Change `PACT_Settings:` to `AutoQAC_Settings:` in default_settings block
- [x] **2.3** Change `PACT_Ignore_FO3:` to `AutoQAC_Ignore_FO3:`
- [x] **2.4** Change `PACT_Ignore_FNV:` to `AutoQAC_Ignore_FNV:`
- [x] **2.5** Change `PACT_Ignore_FO4:` to `AutoQAC_Ignore_FO4:`
- [x] **2.6** Change `PACT_Ignore_SSE:` to `AutoQAC_Ignore_SSE:`
- [x] **2.7** Update error/warning messages to reference "AutoQAC" instead of "PACT"

**Verification**: YAML file parses correctly

### 3. Update Unit Tests
- [x] **3.1** Update all inline YAML test strings in `ConfigurationServiceTests.cs` to use `AutoQAC_Data:` instead of `PACT_Data:`
- [x] **3.2** Update `PactSettings` references in `MainWindowViewModelInitializationTests.cs` to `AutoQacSettings`

**Verification**: All tests pass (`dotnet test`)

### 4. Update Documentation
- [x] **4.1** Update `CLAUDE.md` to change `PACT Ignore.yaml` reference to `AutoQAC Ignore.yaml`
- [x] **4.2** Update `README.md` to change configuration file references
- [x] **4.3** Verify `openspec/project.md` is consistent (already updated)

**Verification**: Documentation review

### 5. Final Verification
- [x] **5.1** Run full build: `dotnet build`
- [x] **5.2** Run all tests: `dotnet test`
- [x] **5.3** Verify application starts and loads configuration

## Dependencies
- Tasks 1.x must complete before Task 5 (build verification)
- Tasks 2.x must complete before Task 5 (config loading)
- Task 3.x depends on Tasks 1.x (tests reference renamed classes)

## Parallelization
- Tasks 1.x and 2.x can run in parallel
- Task 4.x can run in parallel with all other tasks
- Task 3.x should follow Task 1.x
- Task 5.x runs last as final verification
