using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace AutoQAC.Services.UI;

public sealed class FileDialogService : IFileDialogService
{
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
            options.FileTypeFilter = ParseFilter(filter);
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
            options.FileTypeChoices = ParseFilter(filter);
        }

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(options);
        return result?.Path.LocalPath;
    }

    private static List<FilePickerFileType> ParseFilter(string filter)
    {
        // Format: "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        var result = new List<FilePickerFileType>();
        var parts = filter.Split('|');
        
        for (int i = 0; i < parts.Length; i += 2)
        {
            if (i + 1 >= parts.Length) break;
            
            var name = parts[i];
            var patterns = parts[i+1].Split(';');
            
            // Remove * prefix for patterns list, e.g. "*.txt" -> "txt"
            // Avalonia expects patterns like ["txt", "md"] or MIME types
            // Actually FilePickerFileType Patterns property expects glob patterns like "*.txt" 
            // Wait, checking Avalonia docs... 
            // Patterns: "The list of file name patterns (globs), e.g. *.txt, *.md."
            
            result.Add(new FilePickerFileType(name)
            {
                Patterns = patterns.ToList()
            });
        }
        
        return result;
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
