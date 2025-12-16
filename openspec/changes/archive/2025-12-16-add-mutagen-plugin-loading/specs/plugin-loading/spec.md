# plugin-loading Specification

## Purpose
Define how AutoQAC retrieves the list of plugins to clean from the user's game installation, supporting both Mutagen-based automatic detection and file-based manual configuration.

## ADDED Requirements

### Requirement: Mutagen-Based Plugin Loading
The application SHALL use the Mutagen library to read plugin load orders for supported games (SkyrimLE, SkyrimSE, SkyrimVR, Fallout3, FalloutNewVegas, Fallout4, Fallout4VR).

#### Scenario: Loading plugins for a Mutagen-supported game
- **Given** the user has selected SkyrimSE as their game
- **And** Skyrim Special Edition is installed on the system
- **When** the application loads the plugin list
- **Then** it SHALL use Mutagen's `GameEnvironment` to detect the game installation
- **And** it SHALL retrieve the load order from the game's standard locations
- **And** it SHALL populate each `PluginInfo` with the full path to the plugin file

#### Scenario: Mutagen fails to detect game installation
- **Given** the user has selected a Mutagen-supported game
- **And** the game is not installed or cannot be detected
- **When** the application attempts to load the plugin list
- **Then** it SHALL fall back to file-based loading
- **And** it SHALL display a message indicating automatic detection failed
- **And** it SHALL prompt the user to manually select a load order file

### Requirement: File-Based Plugin Loading Fallback
The application SHALL support loading plugins from a manually specified loadorder.txt or plugins.txt file as a fallback mechanism.

#### Scenario: Loading plugins from loadorder.txt
- **Given** the user has configured a `LoadOrder TXT` path in settings
- **And** the specified file exists
- **When** the application loads the plugin list in fallback mode
- **Then** it SHALL read each line from the file
- **And** it SHALL skip lines starting with `#` (comments)
- **And** it SHALL skip empty lines
- **And** it SHALL strip leading `*` characters (enabled flag in plugins.txt format)
- **And** it SHALL create a `PluginInfo` for each valid plugin entry

#### Scenario: Load order file does not exist
- **Given** the user has configured a `LoadOrder TXT` path
- **And** the specified file does not exist
- **When** the application attempts to load the plugin list
- **Then** it SHALL log a warning message
- **And** it SHALL return an empty plugin list
- **And** it SHALL NOT throw an exception

### Requirement: Plugin Information Structure
Each loaded plugin SHALL be represented with filename, full path, skip list status, and detected game type.

#### Scenario: Plugin info contains required fields
- **Given** a plugin is loaded from any source (Mutagen or file)
- **When** the plugin info is created
- **Then** it SHALL contain a `FileName` property with just the plugin filename
- **And** it SHALL contain a `FullPath` property with the absolute path (or filename if unknown)
- **And** it SHALL contain an `IsInSkipList` property defaulting to `false`
- **And** it SHALL contain a `DetectedGameType` property matching the selected game

### Requirement: Mutagen Support Detection
The application SHALL provide a method to determine if a given game type is supported by Mutagen.

#### Scenario: Checking Mutagen support for SkyrimSE
- **Given** the game type is `SkyrimSE`
- **When** checking if Mutagen supports this game
- **Then** the result SHALL be `true`

#### Scenario: Checking Mutagen support for Oblivion
- **Given** the game type is `Oblivion`
- **When** checking if Mutagen supports this game
- **Then** the result SHALL be `false`

### Requirement: Game Data Folder Detection
For Mutagen-supported games, the application SHALL be able to retrieve the game's data folder path.

#### Scenario: Retrieving data folder for installed game
- **Given** a Mutagen-supported game is installed
- **When** requesting the data folder path
- **Then** it SHALL return the absolute path to the game's Data folder
- **And** the path SHALL exist on the filesystem

#### Scenario: Retrieving data folder for uninstalled game
- **Given** a Mutagen-supported game is NOT installed
- **When** requesting the data folder path
- **Then** it SHALL return `null`
- **And** it SHALL NOT throw an exception
