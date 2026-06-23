using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class SkipListViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;

    private List<string> _originalSkipList = [];

    private static readonly string[] ValidExtensions = [".esp", ".esm", ".esl"];

    [ObservableProperty]
    public partial GameType SelectedGame { get; set; }

    public IReadOnlyList<GameType> AvailableGames { get; }

    public ObservableCollection<string> SkipListEntries { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedEntryCommand))]
    public partial string? SelectedEntry { get; set; }

    public ObservableCollection<string> AvailablePlugins { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedPluginCommand))]
    public partial string? SelectedPlugin { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddManualEntryCommand))]
    public partial string ManualEntryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ManualEntryError { get; set; }

    [ObservableProperty]
    public partial bool HasUnsavedChanges { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Raised when the user picks Save or Cancel. The view closes the dialog with this value.</summary>
    public event Action<bool>? CloseRequested;

    public SkipListViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ILoggingService logger)
    {
        _configService = configService;
        _stateService = stateService;
        _logger = logger;

        AvailableGames = Enum.GetValues<GameType>()
            .Where(g => g != GameType.Unknown)
            .ToList()
            .AsReadOnly();

        SkipListEntries.CollectionChanged += OnSkipListEntriesChanged;
        RecomputeHasUnsavedChanges();
    }

    private void OnSkipListEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RecomputeHasUnsavedChanges();

    private void RecomputeHasUnsavedChanges() =>
        HasUnsavedChanges = !SkipListEntriesMatchOriginal();

    partial void OnManualEntryTextChanged(string value) =>
        ManualEntryError = ValidatePluginName(value);

    partial void OnSelectedGameChanged(GameType value)
    {
        if (IsLoading) return;
        _ = LoadSkipListForGameAsync(value);
    }

    public async Task LoadSkipListAsync()
    {
        try
        {
            IsLoading = true;

            var currentGame = _stateService.CurrentState.CurrentGameType;
            if (currentGame == GameType.Unknown && AvailableGames.Count > 0)
            {
                currentGame = AvailableGames[0];
            }

            // IsLoading is true → OnSelectedGameChanged bails out; we load explicitly below.
            SelectedGame = currentGame;

            await LoadSkipListForGameAsync(currentGame);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load skip list");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSkipListForGameAsync(GameType game)
    {
        try
        {
            var list = await _configService.GetGameSpecificSkipListAsync(game);
            _originalSkipList = list.ToList();

            SkipListEntries.Clear();
            foreach (var entry in list.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                SkipListEntries.Add(entry);
            }

            await RefreshAvailablePluginsAsync();
            _logger.Debug("Loaded {Count} entries for {Game} skip list", list.Count, game);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load skip list for {Game}", game);
            _originalSkipList.Clear();
            SkipListEntries.Clear();
        }
        finally
        {
            RecomputeHasUnsavedChanges();
        }
    }

    private async Task RefreshAvailablePluginsAsync()
    {
        AvailablePlugins.Clear();

        var loadedPlugins = _stateService.CurrentState.PluginsToClean;

        var mergedSkipList = await _configService.GetSkipListAsync(SelectedGame, ct: CancellationToken.None);
        var skipSet = new HashSet<string>(mergedSkipList, StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in loadedPlugins.Where(p => !skipSet.Contains(p.FileName)))
        {
            AvailablePlugins.Add(plugin.FileName);
        }

        SelectedPlugin = null;
    }

    private bool CanAddSelectedPlugin() => !string.IsNullOrEmpty(SelectedPlugin);

    [RelayCommand(CanExecute = nameof(CanAddSelectedPlugin))]
    private void AddSelectedPlugin()
    {
        if (string.IsNullOrEmpty(SelectedPlugin))
            return;

        var plugin = SelectedPlugin;

        if (!SkipListEntries.Contains(plugin, StringComparer.OrdinalIgnoreCase))
        {
            SkipListEntries.Add(plugin);
        }

        AvailablePlugins.Remove(plugin);
        SelectedPlugin = null;

        _logger.Debug("Added {Plugin} to skip list from loaded plugins", plugin);
    }

    private bool CanAddManualEntry() => ValidatePluginName(ManualEntryText) == null && !string.IsNullOrWhiteSpace(ManualEntryText);

    [RelayCommand(CanExecute = nameof(CanAddManualEntry))]
    private void AddManualEntry()
    {
        var entry = ManualEntryText.Trim();
        var error = ValidatePluginName(entry);

        if (error != null)
        {
            ManualEntryError = error;
            return;
        }

        if (SkipListEntries.Contains(entry, StringComparer.OrdinalIgnoreCase))
        {
            ManualEntryError = "Plugin already in skip list";
            return;
        }

        SkipListEntries.Add(entry);

        var toRemove = AvailablePlugins.FirstOrDefault(p =>
            string.Equals(p, entry, StringComparison.OrdinalIgnoreCase));
        if (toRemove != null)
        {
            AvailablePlugins.Remove(toRemove);
        }

        ManualEntryText = string.Empty;
        ManualEntryError = null;

        _logger.Debug("Added {Plugin} to skip list via manual entry", entry);
    }

    private bool CanRemoveSelectedEntry() => !string.IsNullOrEmpty(SelectedEntry);

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedEntry))]
    private async Task RemoveSelectedEntryAsync()
    {
        if (string.IsNullOrEmpty(SelectedEntry))
            return;

        var entry = SelectedEntry;
        SkipListEntries.Remove(entry);

        var loadedPlugins = _stateService.CurrentState.PluginsToClean;
        var isLoadedPlugin = loadedPlugins.Any(p => string.Equals(p.FileName, entry, StringComparison.OrdinalIgnoreCase));

        if (isLoadedPlugin)
        {
            var defaultSkipList = await _configService.GetDefaultSkipListAsync(SelectedGame, CancellationToken.None);
            var inDefaultSkipList = defaultSkipList.Any(s => string.Equals(s, entry, StringComparison.OrdinalIgnoreCase));

            if (!inDefaultSkipList)
            {
                AvailablePlugins.Add(entry);
            }
        }

        SelectedEntry = null;

        _logger.Debug("Removed {Plugin} from skip list", entry);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var skipList = SkipListEntries.ToList();
            await _configService.UpdateSkipListAsync(SelectedGame, skipList);

            _originalSkipList = skipList.ToList();
            RecomputeHasUnsavedChanges();
            _logger.Information("Skip list saved for {Game} with {Count} entries",
                SelectedGame, skipList.Count);

            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save skip list");
            CloseRequested?.Invoke(false);
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    private bool SkipListEntriesMatchOriginal()
    {
        if (SkipListEntries.Count != _originalSkipList.Count)
            return false;

        var currentSet = new HashSet<string>(SkipListEntries, StringComparer.OrdinalIgnoreCase);
        var originalSet = new HashSet<string>(_originalSkipList, StringComparer.OrdinalIgnoreCase);

        return currentSet.SetEquals(originalSet);
    }

    private static string? ValidatePluginName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        if (text.Length < 5)
            return "Plugin name too short";

        var hasValidExtension = ValidExtensions.Any(ext =>
            text.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        if (!hasValidExtension)
            return "Must end with .esp, .esm, or .esl";

        return null;
    }

    public void Dispose()
    {
        SkipListEntries.CollectionChanged -= OnSkipListEntriesChanged;
    }
}
