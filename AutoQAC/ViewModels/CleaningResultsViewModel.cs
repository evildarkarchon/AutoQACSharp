using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.UI;
using ReactiveUI;

namespace AutoQAC.ViewModels;

/// <summary>
/// ViewModel for the cleaning results summary window.
/// </summary>
public sealed class CleaningResultsViewModel : ViewModelBase
{
    private readonly ILoggingService? _logger;
    private readonly IFileDialogService? _fileDialog;

    private CleaningSessionResult _sessionResult;

    /// <summary>
    /// Design-time constructor for XAML previewer.
    /// </summary>
    public CleaningResultsViewModel()
    {
        _sessionResult = CleaningSessionResult.CreateEmpty();
        PluginResults = new ObservableCollection<PluginCleaningResult>();

        // Create placeholder commands for design time
        ExportReportCommand = ReactiveCommand.CreateFromTask(ExportReportAsync);
        CloseCommand = ReactiveCommand.Create(() => { });
    }

    public CleaningResultsViewModel(
        CleaningSessionResult sessionResult,
        ILoggingService logger,
        IFileDialogService fileDialog)
    {
        _sessionResult = sessionResult;
        _logger = logger;
        _fileDialog = fileDialog;

        // Initialize collections
        PluginResults = new ObservableCollection<PluginCleaningResult>(sessionResult.PluginResults);

        // Commands
        ExportReportCommand = ReactiveCommand.CreateFromTask(ExportReportAsync);
        CloseCommand = ReactiveCommand.Create(() => { });
    }

    /// <summary>
    /// The cleaning session result being displayed.
    /// </summary>
    public CleaningSessionResult SessionResult
    {
        get => _sessionResult;
        set => this.RaiseAndSetIfChanged(ref _sessionResult, value);
    }

    /// <summary>
    /// Observable collection of plugin results for display in a list.
    /// </summary>
    public ObservableCollection<PluginCleaningResult> PluginResults { get; }

    #region Summary Properties

    /// <summary>
    /// Window title based on result status.
    /// </summary>
    public string WindowTitle => SessionResult.IsSuccess
        ? "Cleaning Completed"
        : SessionResult.WasCancelled
            ? "Cleaning Cancelled"
            : "Cleaning Completed with Errors";

    /// <summary>
    /// Summary text for the header.
    /// </summary>
    public string SummaryText => SessionResult.SessionSummary;

    /// <summary>
    /// Total number of plugins processed.
    /// </summary>
    public int TotalPlugins => SessionResult.TotalPlugins;

    /// <summary>
    /// Number of plugins successfully cleaned.
    /// </summary>
    public int CleanedCount => SessionResult.CleanedCount;

    /// <summary>
    /// Number of plugins that failed.
    /// </summary>
    public int FailedCount => SessionResult.FailedCount;

    /// <summary>
    /// Number of plugins that were skipped.
    /// </summary>
    public int SkippedCount => SessionResult.SkippedCount;

    /// <summary>
    /// Total ITMs removed.
    /// </summary>
    public int TotalItms => SessionResult.TotalItemsRemoved;

    /// <summary>
    /// Total UDRs fixed.
    /// </summary>
    public int TotalUdrs => SessionResult.TotalItemsUndeleted;

    /// <summary>
    /// Total partial forms created.
    /// </summary>
    public int TotalPartialForms => SessionResult.TotalPartialFormsCreated;

    /// <summary>
    /// Formatted duration string.
    /// </summary>
    public string DurationText => SessionResult.TotalDuration.ToString(@"mm\:ss");

    /// <summary>
    /// Whether the session was successful (no failures).
    /// </summary>
    public bool IsSuccess => SessionResult.IsSuccess;

    /// <summary>
    /// Whether there were any failures.
    /// </summary>
    public bool HasFailures => SessionResult.FailedCount > 0;

    /// <summary>
    /// Whether the session was cancelled.
    /// </summary>
    public bool WasCancelled => SessionResult.WasCancelled;

    /// <summary>
    /// Whether partial forms feature was used.
    /// </summary>
    public bool HasPartialForms => SessionResult.TotalPartialFormsCreated > 0;

    #endregion

    #region Commands

    /// <summary>
    /// Command to export the report to a file.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ExportReportCommand { get; }

    /// <summary>
    /// Command to close the window.
    /// </summary>
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    #endregion

    private async Task ExportReportAsync()
    {
        // Skip if running in design mode (no services available)
        if (_fileDialog is null || _logger is null)
            return;

        try
        {
            var defaultFileName = $"AutoQAC_Report_{SessionResult.StartTime:yyyyMMdd_HHmmss}.txt";
            var path = await _fileDialog.SaveFileDialogAsync(
                "Save Cleaning Report",
                "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                defaultFileName);

            if (!string.IsNullOrEmpty(path))
            {
                var report = SessionResult.GenerateReport();
                await File.WriteAllTextAsync(path, report);
                _logger.Information("Cleaning report exported to {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export cleaning report");
        }
    }
}
