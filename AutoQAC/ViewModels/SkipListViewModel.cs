using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using ReactiveUI;

namespace AutoQAC.ViewModels;

public sealed class SkipListViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;
    private readonly CompositeDisposable _disposables = new();

    // Original skip list for change tracking
    private List<string> _originalSkipList = [];

    // Valid plugin extensions
    private static readonly string[] ValidExtensions = [".esp", ".esm", ".esl"];

    #region Properties

    private GameType _selectedGame;
    public GameType SelectedGame
    {
        get => _selectedGame;
        set => this.RaiseAndSetIfChanged(ref _selectedGame, value);
    }

    public IReadOnlyList<GameType> AvailableGames { get; }

    public ObservableCollection<string> SkipListEntries { get; } = new();

    private string? _selectedEntry;
    public string? SelectedEntry
    {
        get => _selectedEntry;
        set => this.RaiseAndSetIfChanged(ref _selectedEntry, value);
    }

    public ObservableCollection<string> AvailablePlugins { get; } = new();

    private string? _selectedPlugin;
    public string? SelectedPlugin
    {
        get => _selectedPlugin;
        set => this.RaiseAndSetIfChanged(ref _selectedPlugin, value);
    }

    private string _manualEntryText = string.Empty;
    public string ManualEntryText
    {
        get => _manualEntryText;
        set => this.RaiseAndSetIfChanged(ref _manualEntryText, value);
    }

    private string? _manualEntryError;
    public string? ManualEntryError
    {
        get => _manualEntryError;
        set => this.RaiseAndSetIfChanged(ref _manualEntryError, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _hasUnsavedChanges;
    public bool HasUnsavedChanges => _hasUnsavedChanges.Value;

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    #endregion

    #region Commands

    public ReactiveCommand<Unit, Unit> AddSelectedPluginCommand { get; }
    public ReactiveCommand<Unit, Unit> AddManualEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveSelectedEntryCommand { get; }
    public ReactiveCommand<Unit, bool> SaveCommand { get; }
    public ReactiveCommand<Unit, bool> CancelCommand { get; }

    #endregion

    public SkipListViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ILoggingService logger)
    {
        _configService = configService;
        _stateService = stateService;
        _logger = logger;

        // Available games (excluding Unknown)
        AvailableGames = Enum.GetValues<GameType>()
            .Where(g => g != GameType.Unknown)
            .ToList()
            .AsReadOnly();

        // Track changes to skip list entries
        var entriesChanged = Observable.FromEventPattern<System.Collections.Specialized.NotifyCollectionChangedEventHandler,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs>(
                h => SkipListEntries.CollectionChanged += h,
                h => SkipListEntries.CollectionChanged -= h)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);

        _hasUnsavedChanges = entriesChanged
            .Select(_ => !SkipListEntriesMatchOriginal())
            .ToProperty(this, x => x.HasUnsavedChanges);
        _disposables.Add(_hasUnsavedChanges);

        // Can add from available plugins when one is selected
        var canAddFromPlugins = this.WhenAnyValue(x => x.SelectedPlugin)
            .Select(p => !string.IsNullOrEmpty(p));

        // Can add manual entry when text is valid
        var canAddManual = this.WhenAnyValue(x => x.ManualEntryText)
            .Select(t => ValidatePluginName(t) == null);

        // Can remove when entry is selected
        var canRemove = this.WhenAnyValue(x => x.SelectedEntry)
            .Select(e => !string.IsNullOrEmpty(e));

        // Commands
        AddSelectedPluginCommand = ReactiveCommand.Create(AddSelectedPlugin, canAddFromPlugins);
        AddManualEntryCommand = ReactiveCommand.Create(AddManualEntry, canAddManual);
        RemoveSelectedEntryCommand = ReactiveCommand.Create(RemoveSelectedEntry, canRemove);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        CancelCommand = ReactiveCommand.Create(() => false);

        // Update manual entry error message reactively
        var manualEntrySubscription = this.WhenAnyValue(x => x.ManualEntryText)
            .Subscribe(text => ManualEntryError = ValidatePluginName(text));
        _disposables.Add(manualEntrySubscription);

        // When game selection changes, reload skip list
        var gameChangeSubscription = this.WhenAnyValue(x => x.SelectedGame)
            .Skip(1) // Skip initial value
            .Where(_ => !IsLoading)
            .SelectMany(async game =>
            {
                await LoadSkipListForGameAsync(game);
                return Unit.Default;
            })
            .Subscribe();
        _disposables.Add(gameChangeSubscription);
    }

    /// <summary>
    /// Loads skip list data. Call this before showing the window.
    /// </summary>
    public async Task LoadSkipListAsync()
    {
        try
        {
            IsLoading = true;

            // Get current game from state or use first available
            var currentGame = _stateService.CurrentState.CurrentGameType;
            if (currentGame == GameType.Unknown && AvailableGames.Count > 0)
            {
                currentGame = AvailableGames[0];
            }

            _selectedGame = currentGame;
            this.RaisePropertyChanged(nameof(SelectedGame));

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

            RefreshAvailablePlugins();
            _logger.Debug("Loaded {Count} entries for {Game} skip list", list.Count, game);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load skip list for {Game}", game);
            _originalSkipList.Clear();
            SkipListEntries.Clear();
        }
    }

    private void RefreshAvailablePlugins()
    {
        AvailablePlugins.Clear();

        // Get loaded plugins from state
        var loadedPlugins = _stateService.CurrentState.PluginsToClean;

        // Filter out plugins already in skip list
        var skipSet = new HashSet<string>(SkipListEntries, StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in loadedPlugins.Where(p => !skipSet.Contains(p.FileName)))
        {
            AvailablePlugins.Add(plugin.FileName);
        }

        SelectedPlugin = null;
    }

    private void AddSelectedPlugin()
    {
        if (string.IsNullOrEmpty(SelectedPlugin))
            return;

        var plugin = SelectedPlugin;

        // Add to skip list
        if (!SkipListEntries.Contains(plugin, StringComparer.OrdinalIgnoreCase))
        {
            SkipListEntries.Add(plugin);
        }

        // Remove from available plugins
        AvailablePlugins.Remove(plugin);
        SelectedPlugin = null;

        _logger.Debug("Added {Plugin} to skip list from loaded plugins", plugin);
    }

    private void AddManualEntry()
    {
        var entry = ManualEntryText.Trim();
        var error = ValidatePluginName(entry);

        if (error != null)
        {
            ManualEntryError = error;
            return;
        }

        // Check for duplicates
        if (SkipListEntries.Contains(entry, StringComparer.OrdinalIgnoreCase))
        {
            ManualEntryError = "Plugin already in skip list";
            return;
        }

        SkipListEntries.Add(entry);

        // Also remove from available plugins if present
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

    private void RemoveSelectedEntry()
    {
        if (string.IsNullOrEmpty(SelectedEntry))
            return;

        var entry = SelectedEntry;
        SkipListEntries.Remove(entry);

        // Add back to available plugins if it was loaded
        var loadedPlugins = _stateService.CurrentState.PluginsToClean;
        if (loadedPlugins.Any(p => string.Equals(p.FileName, entry, StringComparison.OrdinalIgnoreCase)))
        {
            AvailablePlugins.Add(entry);
        }

        SelectedEntry = null;

        _logger.Debug("Removed {Plugin} from skip list", entry);
    }

    private async Task<bool> SaveAsync()
    {
        try
        {
            var skipList = SkipListEntries.ToList();
            await _configService.UpdateSkipListAsync(SelectedGame, skipList);

            _originalSkipList = skipList.ToList();
            _logger.Information("Skip list saved for {Game} with {Count} entries",
                SelectedGame, skipList.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save skip list");
            return false;
        }
    }

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
            return null; // Empty is valid (just can't submit)

        text = text.Trim();

        if (text.Length < 5) // Minimum: "a.esp"
            return "Plugin name too short";

        var hasValidExtension = ValidExtensions.Any(ext =>
            text.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        if (!hasValidExtension)
            return "Must end with .esp, .esm, or .esl";

        return null;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
