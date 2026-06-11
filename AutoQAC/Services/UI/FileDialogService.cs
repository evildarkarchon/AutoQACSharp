using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace AutoQAC.Services.UI;

public sealed class FileDialogService : IFileDialogService
{
    [SuppressMessage("Performance", "CA1826:Do not use Enumerable methods on indexable collections")]
    public async Task<string?> OpenFileDialogAsync(
        string title,
        string filter,
        string? initialDirectory = null)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var topLevel = TopLevel.GetTopLevel(mainWindow);
        if (topLevel == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (!string.IsNullOrEmpty(initialDirectory))
        {
            // Try to get the folder from path
            try 
            {
                var folder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);
                options.SuggestedStartLocation = folder;
            }
            catch { /* ignore invalid path */ }
        }

        if (!string.IsNullOrEmpty(filter))
        {
            options.FileTypeFilter = FileDialogFilterParser.Parse(filter)
                .Select(entry => new FilePickerFileType(entry.Name)
                {
                    Patterns = entry.Patterns.ToList()
                })
                .ToList();
        }

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        return result.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> SaveFileDialogAsync(
        string title,
        string filter,
        string? defaultFileName = null,
        string? initialDirectory = null)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var topLevel = TopLevel.GetTopLevel(mainWindow);
        if (topLevel == null) return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName
        };

        if (!string.IsNullOrEmpty(initialDirectory))
        {
            try
            {
                var folder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);
                options.SuggestedStartLocation = folder;
            }
            catch { /* ignore invalid path */ }
        }

        if (!string.IsNullOrEmpty(filter))
        {
            options.FileTypeChoices = FileDialogFilterParser.Parse(filter)
                .Select(entry => new FilePickerFileType(entry.Name)
                {
                    Patterns = entry.Patterns.ToList()
                })
                .ToList();
        }

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(options);
        return result?.Path.LocalPath;
    }

    public async Task<string?> OpenFolderDialogAsync(
        string title,
        string? initialDirectory = null)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var topLevel = TopLevel.GetTopLevel(mainWindow);
        if (topLevel == null) return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (!string.IsNullOrEmpty(initialDirectory))
        {
            try
            {
                var folder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);
                options.SuggestedStartLocation = folder;
            }
            catch { /* ignore invalid path */ }
        }

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
        return result.FirstOrDefault()?.Path.LocalPath;
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
