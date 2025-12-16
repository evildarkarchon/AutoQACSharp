# configuration-naming Specification

## Purpose
TBD - created by archiving change rename-pact-to-autoqac. Update Purpose after archive.
## Requirements
### Requirement: YAML Configuration Key Naming
The application SHALL use `AutoQAC_` prefix for all top-level YAML configuration keys instead of the legacy `PACT_` prefix.

#### Scenario: Loading main configuration file
- **Given** a YAML configuration file `AutoQAC Main.yaml`
- **When** the application loads the configuration
- **Then** it SHALL recognize `AutoQAC_Data` as the root configuration key
- **And** it SHALL recognize `AutoQAC_Settings` as the settings section key

#### Scenario: Loading ignore lists from configuration
- **Given** a YAML configuration file with game-specific ignore lists
- **When** the application reads ignore lists
- **Then** it SHALL recognize keys named `AutoQAC_Ignore_FO3`, `AutoQAC_Ignore_FNV`, `AutoQAC_Ignore_FO4`, and `AutoQAC_Ignore_SSE`

### Requirement: C# Class Naming Convention
Configuration model classes SHALL use `AutoQac` prefix instead of `Pact` prefix for consistency with the application name.

#### Scenario: Configuration class naming
- **Given** the C# model classes for YAML deserialization
- **When** referencing configuration data types
- **Then** the main data class SHALL be named `AutoQacData`
- **And** the settings class SHALL be named `AutoQacSettings`

### Requirement: Error and Warning Message Branding
All user-facing error and warning messages in configuration files SHALL reference "AutoQAC" instead of "PACT".

#### Scenario: Displaying configuration error messages
- **Given** an invalid configuration state
- **When** the application displays an error or warning message
- **Then** the message SHALL reference "AutoQAC" as the application name
- **And** the message SHALL NOT reference "PACT"

