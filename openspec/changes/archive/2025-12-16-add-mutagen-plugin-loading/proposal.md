# Proposal: Add Mutagen Plugin Loading with Game Selection

## Change ID
`add-mutagen-plugin-loading`

## Summary
Add support for the Mutagen library to read plugin load orders directly from game installations for supported games. Introduce a game selection UI that allows users to choose their target game, with automatic load order detection. Retain the existing loadorder.txt/plugins.txt file-based approach as a fallback for games not supported by Mutagen.

## Motivation
Currently, AutoQAC requires users to manually specify the path to their `loadorder.txt` or `plugins.txt` file. This has several drawbacks:

1. **Manual Configuration**: Users must locate and specify the correct load order file path
2. **Error-Prone**: Users may accidentally select the wrong file or game's load order
3. **No Game Context**: The application infers the game type from the load order contents rather than explicit selection
4. **Limited Validation**: Cannot validate plugin existence without knowing the game's data folder

Mutagen is a mature .NET library specifically designed for Bethesda game modding that provides:
- Direct load order reading from standard game locations
- Automatic game data folder detection
- Plugin file header parsing for validation
- Support for multiple games: Skyrim SE, Skyrim LE, Skyrim VR, Fallout 4, Fallout 3, Fallout NV, Oblivion

## Scope

### In Scope
- Add Mutagen.Bethesda NuGet package dependency
- Create game selection UI (dropdown or list) in MainWindow
- Implement Mutagen-based plugin loading service for supported games
- Implement fallback to file-based loading for unsupported games
- Add game selection to configuration persistence
- Update GameType enum to align with Mutagen's GameRelease

### Out of Scope
- Plugin content analysis or modification (beyond load order)
- Mutagen's Synthesis patcher framework integration
- Oblivion support (xEdit's QAC mode has limited support)
- Starfield support (xEdit doesn't support Starfield yet)

## Affected Capabilities
- **plugin-loading** (new capability): Core plugin list retrieval from load order
- **game-selection** (new capability): User interface for game selection

## Dependencies
- NuGet: `Mutagen.Bethesda` (latest stable version)
- NuGet: `Mutagen.Bethesda.Skyrim` (for Skyrim-specific types)
- NuGet: `Mutagen.Bethesda.Fallout4` (for Fallout 4-specific types)

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Mutagen API changes | Low | Medium | Pin to specific version, monitor releases |
| Game detection fails | Medium | Low | Fall back to file-based loading |
| Large dependency size | Low | Low | Mutagen packages are reasonably sized |
| Performance overhead | Low | Low | Load order reading is fast, done once at startup |

## Success Criteria
1. Users can select their game from a dropdown menu
2. Plugin list populates automatically for Mutagen-supported games
3. File-based fallback works for unsupported games
4. Game selection persists across sessions
5. Existing loadorder.txt/plugins.txt configuration continues to work
