## MODIFIED Requirements

### Requirement: Mutagen-Based Plugin Loading
The application SHALL use the Mutagen library to read plugin load orders for supported games (SkyrimLE, SkyrimSE, SkyrimVR, Fallout3, FalloutNewVegas, Fallout4, Fallout4VR).

#### Scenario: Loading plugins for a Mutagen-supported game
- **WHEN** the user has selected SkyrimSE as their game
- **AND** Skyrim Special Edition is installed on the system
- **WHEN** the application loads the plugin list
- **THEN** it SHALL use Mutagen GameEnvironment to detect the game installation
- **AND** it SHALL retrieve the load order from the game standard locations
- **AND** it SHALL populate each PluginInfo with the full path to the plugin file
- **AND** each PluginInfo.IsSelected SHALL be set during construction (not mutated afterward from background threads)
