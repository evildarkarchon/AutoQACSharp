using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class CleaningResultsViewModel : ViewModelBase
{
    private readonly ILoggingService? _logger;
    private readonly IFileDialogService? _fileDialog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    [NotifyPropertyChangedFor(nameof(TotalPlugins))]
    [NotifyPropertyChangedFor(nameof(CleanedCount))]
    [NotifyPropertyChangedFor(nameof(FailedCount))]
    [NotifyPropertyChangedFor(nameof(SkippedCount))]
    [NotifyPropertyChangedFor(nameof(TotalItms))]
    [NotifyPropertyChangedFor(nameof(TotalUdrs))]
    [NotifyPropertyChangedFor(nameof(TotalPartialForms))]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(HasFailures))]
    [NotifyPropertyChangedFor(nameof(WasCancelled))]
    [NotifyPropertyChangedFor(nameof(HasPartialForms))]
    public partial CleaningSessionResult SessionResult { get; set; }

    public ObservableCollection<PluginCleaningResult> PluginResults { get; }

    public string WindowTitle => SessionResult.IsSuccess
        ? "Cleaning Completed"
        : SessionResult.WasCancelled
            ? "Cleaning Cancelled"
            : "Cleaning Completed with Errors";

    public string SummaryText => SessionResult.SessionSummary;
    public int TotalPlugins => SessionResult.TotalPlugins;
    public int CleanedCount => SessionResult.CleanedCount;
    public int FailedCount => SessionResult.FailedCount;
    public int SkippedCount => SessionResult.SkippedCount;
    public int TotalItms => SessionResult.TotalItemsRemoved;
    public int TotalUdrs => SessionResult.TotalItemsUndeleted;
    public int TotalPartialForms => SessionResult.TotalPartialFormsCreated;
    public string DurationText => SessionResult.TotalDuration.ToString(@"mm\:ss");
    public bool IsSuccess => SessionResult.IsSuccess;
    public bool HasFailures => SessionResult.FailedCount > 0;
    public bool WasCancelled => SessionResult.WasCancelled;
    public bool HasPartialForms => SessionResult.TotalPartialFormsCreated > 0;

    /// <summary>Raised when the user clicks Close. The view closes the window.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Design-time constructor for XAML previewer.</summary>
    public CleaningResultsViewModel()
    {
        SessionResult = CleaningSessionResult.CreateEmpty();
        PluginResults = [];
    }

    public CleaningResultsViewModel(
        CleaningSessionResult sessionResult,
        ILoggingService logger,
        IFileDialogService fileDialog)
    {
        SessionResult = sessionResult;
        _logger = logger;
        _fileDialog = fileDialog;

        PluginResults = new ObservableCollection<PluginCleaningResult>(sessionResult.PluginResults);
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
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

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
