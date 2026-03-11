## ADDED Requirements

### Requirement: Mutagen-backed plugin issue approximations
The application SHALL analyze plugins with `QueryPlugins` after a successful Mutagen-based plugin refresh for supported games and produce approximate ITM, deleted reference, and deleted navmesh counts for each loaded plugin.

#### Scenario: Supported game refresh starts approximation analysis
- **WHEN** the user refreshes the plugin list for a Mutagen-supported game
- **THEN** the application SHALL load the plugin list first and then start a cancellable approximation pass using the same resolved game and load-order context

#### Scenario: File-based game refresh does not run approximation analysis
- **WHEN** the user refreshes the plugin list for a game that does not use Mutagen-based plugin loading
- **THEN** the application SHALL leave plugin issue approximations unavailable and SHALL NOT attempt QueryPlugins analysis

### Requirement: Plugin list surfaces approximation results
The main plugin list SHALL expose per-plugin approximate ITM, deleted reference, and deleted navmesh counts when analysis results are available, and it SHALL distinguish those results from unavailable or pending analysis states.

#### Scenario: Approximation results are available
- **WHEN** plugin issue approximation analysis completes successfully for a plugin
- **THEN** that plugin row SHALL show approximate counts for ITMs, deleted references, and deleted navmeshes

#### Scenario: Approximation analysis is unavailable for a plugin
- **WHEN** plugin issue approximation analysis has not run, is still pending, or cannot produce a result for a plugin
- **THEN** the plugin row SHALL NOT present missing analysis as zero issues

### Requirement: Approximation updates preserve plugin list interaction
Applying approximation results SHALL preserve the current plugin selection and skip-list state for each plugin, and approximation failures SHALL NOT prevent plugin list loading.

#### Scenario: Background approximation results arrive after user interaction
- **WHEN** the user changes plugin selections before the approximation pass completes
- **THEN** the application SHALL preserve the current `IsSelected` value for each matching plugin when it applies approximation metadata

#### Scenario: Approximation analysis fails after plugins are loaded
- **WHEN** the plugin list has already loaded and QueryPlugins analysis fails or is canceled
- **THEN** the application SHALL keep the loaded plugin list available and SHALL leave approximation metadata unavailable for the affected refresh
