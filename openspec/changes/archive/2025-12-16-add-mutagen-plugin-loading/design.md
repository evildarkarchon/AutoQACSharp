# Design: Mutagen Plugin Loading with Game Selection

## Architecture Overview

This change introduces a dual-source plugin loading system where Mutagen serves as the primary source for supported games, with file-based loading as a fallback.

```
┌─────────────────────────────────────────────────────────────┐
│                    MainWindowViewModel                       │
│  - SelectedGame (GameType)                                  │
│  - PluginList (ObservableCollection<PluginInfo>)            │
└─────────────────────┬───────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────┐
│              IPluginLoadingService                          │
│  + GetPluginsAsync(GameType, CancellationToken)             │
│  + IsGameSupportedByMutagen(GameType)                       │
│  + GetAvailableGames() : List<GameType>                     │
└─────────────────────┬───────────────────────────────────────┘
                      │
          ┌───────────┴───────────┐
          ▼                       ▼
┌──────────────────────┐  ┌──────────────────────┐
│ MutagenPluginLoader  │  │ FilePluginLoader     │
│ (Mutagen-based)      │  │ (loadorder.txt)      │
└──────────────────────┘  └──────────────────────┘
```

## Component Design

### 1. GameType Enum Expansion

Align the existing `GameType` enum with Mutagen's `GameRelease`:

```csharp
public enum GameType
{
    Unknown,
    // Mutagen-supported games
    SkyrimLE,           // Skyrim Legendary Edition
    SkyrimSE,           // Skyrim Special Edition
    SkyrimVR,           // Skyrim VR
    Fallout3,           // Fallout 3
    FalloutNewVegas,    // Fallout: New Vegas
    Fallout4,           // Fallout 4
    Fallout4VR,         // Fallout 4 VR
    // Not supported by Mutagen (file-based only)
    Oblivion,           // Oblivion (limited xEdit QAC support)
}
```

### 2. IPluginLoadingService Interface

New service interface that abstracts plugin source:

```csharp
public interface IPluginLoadingService
{
    /// <summary>
    /// Gets plugins for the specified game.
    /// Uses Mutagen if supported, falls back to file-based loading.
    /// </summary>
    Task<List<PluginInfo>> GetPluginsAsync(
        GameType gameType,
        CancellationToken ct = default);

    /// <summary>
    /// Gets plugins from a specific load order file path (fallback mode).
    /// </summary>
    Task<List<PluginInfo>> GetPluginsFromFileAsync(
        string loadOrderPath,
        CancellationToken ct = default);

    /// <summary>
    /// Returns true if the game is supported by Mutagen.
    /// </summary>
    bool IsGameSupportedByMutagen(GameType gameType);

    /// <summary>
    /// Gets list of games available for selection.
    /// </summary>
    IReadOnlyList<GameType> GetAvailableGames();

    /// <summary>
    /// Gets the data folder path for a game (if detectable).
    /// </summary>
    string? GetGameDataFolder(GameType gameType);
}
```

### 3. PluginLoadingService Implementation

The service orchestrates between Mutagen and file-based loading:

```csharp
public sealed class PluginLoadingService : IPluginLoadingService
{
    private static readonly HashSet<GameType> MutagenSupportedGames = new()
    {
        GameType.SkyrimLE,
        GameType.SkyrimSE,
        GameType.SkyrimVR,
        GameType.Fallout3,
        GameType.FalloutNewVegas,
        GameType.Fallout4,
        GameType.Fallout4VR
    };

    public async Task<List<PluginInfo>> GetPluginsAsync(
        GameType gameType,
        CancellationToken ct = default)
    {
        if (IsGameSupportedByMutagen(gameType))
        {
            return await LoadFromMutagenAsync(gameType, ct);
        }

        // Fall back to configured file path
        return await LoadFromFileAsync(gameType, ct);
    }

    private async Task<List<PluginInfo>> LoadFromMutagenAsync(
        GameType gameType,
        CancellationToken ct)
    {
        var release = MapToGameRelease(gameType);

        // Use Mutagen's GameEnvironment to get load order
        using var env = GameEnvironment.Typical.Builder(release)
            .Build();

        var plugins = new List<PluginInfo>();
        foreach (var listing in env.LoadOrder.ListedOrder)
        {
            plugins.Add(new PluginInfo
            {
                FileName = listing.FileName,
                FullPath = Path.Combine(env.DataFolderPath, listing.FileName),
                IsInSkipList = false,
                DetectedGameType = gameType
            });
        }

        return plugins;
    }
}
```

### 4. Game Selection UI

Add game selection to MainWindow:

```xml
<!-- MainWindow.axaml -->
<StackPanel Orientation="Horizontal" Margin="0,0,0,10">
    <TextBlock Text="Game:" VerticalAlignment="Center" Margin="0,0,10,0"/>
    <ComboBox ItemsSource="{Binding AvailableGames}"
              SelectedItem="{Binding SelectedGame}"
              Width="200">
        <ComboBox.ItemTemplate>
            <DataTemplate>
                <TextBlock Text="{Binding Converter={StaticResource GameTypeDisplayConverter}}"/>
            </DataTemplate>
        </ComboBox.ItemTemplate>
    </ComboBox>
    <TextBlock Text="(Mutagen)"
               Foreground="Green"
               Margin="10,0,0,0"
               IsVisible="{Binding IsMutagenSupported}"/>
    <TextBlock Text="(File-based)"
               Foreground="Orange"
               Margin="10,0,0,0"
               IsVisible="{Binding !IsMutagenSupported}"/>
</StackPanel>
```

### 5. Configuration Changes

Update UserConfiguration to persist game selection:

```csharp
public sealed class UserConfiguration
{
    // Existing properties...

    [YamlMember(Alias = "Selected_Game")]
    public string SelectedGame { get; set; } = "SkyrimSE";

    [YamlMember(Alias = "LoadOrder TXT")]
    public string LoadOrderPath { get; set; } = string.Empty;

    // LoadOrderPath is still used as fallback for non-Mutagen games
}
```

## Mutagen Integration Details

### GameRelease Mapping

```csharp
private static GameRelease MapToGameRelease(GameType gameType) => gameType switch
{
    GameType.SkyrimLE => GameRelease.SkyrimLE,
    GameType.SkyrimSE => GameRelease.SkyrimSE,
    GameType.SkyrimVR => GameRelease.SkyrimVR,
    GameType.Fallout3 => GameRelease.Fallout3,
    GameType.FalloutNewVegas => GameRelease.FalloutNV,
    GameType.Fallout4 => GameRelease.Fallout4,
    GameType.Fallout4VR => GameRelease.Fallout4VR,
    _ => throw new ArgumentException($"Game {gameType} not supported by Mutagen")
};
```

### NuGet Packages Required

```xml
<PackageReference Include="Mutagen.Bethesda" Version="0.45.0" />
<PackageReference Include="Mutagen.Bethesda.Skyrim" Version="0.45.0" />
<PackageReference Include="Mutagen.Bethesda.Fallout4" Version="0.45.0" />
```

## Fallback Strategy

When Mutagen cannot detect a game installation:

1. Check if `LoadOrderPath` is configured in user settings
2. If configured, use existing `PluginValidationService.GetPluginsFromLoadOrderAsync()`
3. Display user-friendly message explaining the fallback
4. Allow user to manually browse for loadorder.txt/plugins.txt

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Game not installed | Show message, disable game in dropdown or allow manual file selection |
| Load order file missing | Prompt user to select file manually |
| Mutagen throws exception | Log error, fall back to file-based loading |
| Invalid plugin in load order | Skip plugin, log warning |

## Testing Strategy

1. **Unit Tests**: Mock `IPluginLoadingService` for ViewModel tests
2. **Integration Tests**: Test Mutagen loading with sample mod setups
3. **Fallback Tests**: Verify file-based loading when Mutagen unavailable
4. **UI Tests**: Verify game selection dropdown behavior

## Migration Path

Existing users with configured `LoadOrder TXT` paths will:
1. See their game auto-detected from the path (if possible)
2. Have the file-based fallback work as before
3. Can switch to Mutagen mode by selecting a game
