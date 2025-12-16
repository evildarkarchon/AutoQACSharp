# game-selection Specification

## Purpose
TBD - created by archiving change add-mutagen-plugin-loading. Update Purpose after archive.
## Requirements
### Requirement: Game Selection UI Control
The main window SHALL provide a dropdown control for selecting the target game.

#### Scenario: Displaying available games
- **Given** the application has started
- **When** the main window is displayed
- **Then** a game selection dropdown SHALL be visible
- **And** the dropdown SHALL contain all supported game types
- **And** each game SHALL be displayed with a user-friendly name (e.g., "Skyrim Special Edition" not "SkyrimSE")

#### Scenario: Selecting a game from dropdown
- **Given** the game selection dropdown is displayed
- **When** the user selects a different game
- **Then** the `SelectedGame` property SHALL update to the chosen game type
- **And** the plugin list SHALL refresh automatically

### Requirement: Mutagen Support Indicator
The UI SHALL indicate whether the selected game uses Mutagen (automatic) or file-based (manual) plugin loading.

#### Scenario: Mutagen-supported game selected
- **Given** the user has selected a Mutagen-supported game (e.g., SkyrimSE)
- **When** viewing the game selection area
- **Then** an indicator SHALL show that automatic plugin detection is active
- **And** the indicator color SHALL be green or similar positive color

#### Scenario: File-based game selected
- **Given** the user has selected a non-Mutagen game (e.g., Oblivion)
- **When** viewing the game selection area
- **Then** an indicator SHALL show that file-based loading is active
- **And** the indicator color SHALL be orange or similar cautionary color
- **And** the load order file path input SHALL be enabled or visible

### Requirement: Game Selection Persistence
The selected game SHALL persist across application restarts.

#### Scenario: Saving game selection
- **Given** the user has selected a game
- **When** the application closes or saves settings
- **Then** the selected game SHALL be saved to the user configuration file
- **And** the saved value SHALL be the game type identifier (e.g., "SkyrimSE")

#### Scenario: Loading saved game selection
- **Given** the user configuration contains a saved game selection
- **When** the application starts
- **Then** the game dropdown SHALL show the previously selected game
- **And** the plugin list SHALL load for that game automatically

#### Scenario: No saved game selection
- **Given** the user configuration does not contain a game selection
- **When** the application starts
- **Then** the game dropdown SHALL default to `Unknown` or prompt user to select
- **And** the plugin list SHALL be empty until a game is selected

### Requirement: Game Display Names
Each game type SHALL have a human-readable display name for the UI.

#### Scenario: Display name mapping
- **Given** a `GameType` enum value
- **When** displaying the game in the UI
- **Then** it SHALL show the following display names:
  | GameType | Display Name |
  |----------|--------------|
  | SkyrimLE | Skyrim Legendary Edition |
  | SkyrimSE | Skyrim Special Edition |
  | SkyrimVR | Skyrim VR |
  | Fallout3 | Fallout 3 |
  | FalloutNewVegas | Fallout: New Vegas |
  | Fallout4 | Fallout 4 |
  | Fallout4VR | Fallout 4 VR |
  | Oblivion | Oblivion |
  | Unknown | (Select a game) |

### Requirement: Available Games List
The application SHALL provide a list of games available for selection.

#### Scenario: Getting available games
- **Given** the application is initializing the game selection UI
- **When** requesting the list of available games
- **Then** it SHALL return all `GameType` values except `Unknown`
- **And** the list SHALL be ordered logically (Skyrim variants, then Fallout variants)

### Requirement: Game Change Triggers Plugin Refresh
When the selected game changes, the plugin list SHALL automatically refresh.

#### Scenario: Changing from SkyrimSE to Fallout4
- **Given** the user has SkyrimSE selected with plugins loaded
- **When** the user changes selection to Fallout4
- **Then** the plugin list SHALL clear
- **And** the application SHALL load the Fallout 4 plugin list
- **And** any previous operation (like cleaning) SHALL be cancelled if in progress

#### Scenario: Selecting same game again
- **Given** the user has SkyrimSE selected
- **When** the user selects SkyrimSE again (no change)
- **Then** the plugin list SHALL NOT refresh
- **And** no redundant loading operations SHALL occur

