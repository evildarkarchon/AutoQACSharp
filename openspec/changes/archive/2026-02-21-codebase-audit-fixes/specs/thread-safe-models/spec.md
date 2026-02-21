## ADDED Requirements

### Requirement: PluginInfo thread-safety documentation
PluginInfo.IsSelected SHALL include documentation stating that mutation is only safe on the UI thread.

#### Scenario: UI thread mutation contract
- **WHEN** a developer reads the PluginInfo record definition
- **THEN** a comment or XML doc SHALL indicate that IsSelected must only be mutated on the UI thread

### Requirement: AppState immutable collection types
AppState record SHALL use read-only collection interfaces (IReadOnlyList, IReadOnlySet) for its public properties to prevent accidental mutation of shared state.

#### Scenario: AppState collections prevent external mutation
- **WHEN** a consumer receives an AppState from StateService.CurrentState
- **THEN** the PluginsToClean property SHALL be IReadOnlyList of PluginInfo
- **AND** the CleanedPlugins, FailedPlugins, SkippedPlugins properties SHALL be IReadOnlySet of string
- **AND** attempting to cast and mutate SHALL NOT affect the state service internal state
