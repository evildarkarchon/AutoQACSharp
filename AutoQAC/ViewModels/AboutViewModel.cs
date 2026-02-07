using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using ReactiveUI;

namespace AutoQAC.ViewModels;

/// <summary>
/// ViewModel for the About dialog. Displays version info, library versions,
/// links, and provides a GitHub release update check.
/// </summary>
public sealed class AboutViewModel : ViewModelBase
{
    private static readonly HttpClient HttpClient = new();

    static AboutViewModel()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AutoQACSharp/1.0");
        HttpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    // Version info (read-only)
    public string AppVersion { get; }
    public string InformationalVersion { get; }
    public string BuildDate { get; }
    public string DotNetVersion { get; }
    public string AvaloniaVersion { get; }
    public string ReactiveUIVersion { get; }

    // Links (constants)
    public string GitHubUrl => "https://github.com/evildarkarchon/AutoQACSharp";
    public string GitHubIssuesUrl => "https://github.com/evildarkarchon/AutoQACSharp/issues";
    public string XEditUrl => "https://github.com/TES5Edit/TES5Edit";

    // Update check state
    private string _updateStatusText = string.Empty;
    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => this.RaiseAndSetIfChanged(ref _updateStatusText, value);
    }

    private bool _isCheckingUpdate;
    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set => this.RaiseAndSetIfChanged(ref _isCheckingUpdate, value);
    }

    private bool _updateAvailable;
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set => this.RaiseAndSetIfChanged(ref _updateAvailable, value);
    }

    private string? _latestVersionUrl;
    public string? LatestVersionUrl
    {
        get => _latestVersionUrl;
        private set => this.RaiseAndSetIfChanged(ref _latestVersionUrl, value);
    }

    // Commands
    public ReactiveCommand<Unit, Unit> CheckForUpdateCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenGitHubCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenIssuesCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenXEditCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLatestReleaseCommand { get; }

    public AboutViewModel()
    {
        // Gather version info
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        AppVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";

        var infoAttr = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        InformationalVersion = infoAttr?.InformationalVersion ?? AppVersion;

        var buildDateAttr = assembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate");
        BuildDate = buildDateAttr?.Value ?? "Unknown";

        DotNetVersion = RuntimeInformation.FrameworkDescription;

        var avaloniaAssembly = typeof(Avalonia.Application).Assembly;
        var avaloniaVer = avaloniaAssembly.GetName().Version;
        AvaloniaVersion = avaloniaVer != null ? $"{avaloniaVer.Major}.{avaloniaVer.Minor}.{avaloniaVer.Build}" : "Unknown";

        var rxuiAssembly = typeof(ReactiveUI.ReactiveObject).Assembly;
        var rxuiVer = rxuiAssembly.GetName().Version;
        ReactiveUIVersion = rxuiVer != null ? $"{rxuiVer.Major}.{rxuiVer.Minor}.{rxuiVer.Build}" : "Unknown";

        // Commands
        var canCheckUpdate = this.WhenAnyValue(x => x.IsCheckingUpdate)
            .Select(checking => !checking);

        CheckForUpdateCommand = ReactiveCommand.CreateFromTask(CheckForUpdateAsync, canCheckUpdate);

        OpenGitHubCommand = ReactiveCommand.Create(() => OpenUrl(GitHubUrl));
        OpenIssuesCommand = ReactiveCommand.Create(() => OpenUrl(GitHubIssuesUrl));
        OpenXEditCommand = ReactiveCommand.Create(() => OpenUrl(XEditUrl));

        var canOpenRelease = this.WhenAnyValue(x => x.UpdateAvailable, x => x.LatestVersionUrl,
            (available, url) => available && !string.IsNullOrEmpty(url));
        OpenLatestReleaseCommand = ReactiveCommand.Create(
            () => OpenUrl(LatestVersionUrl!),
            canOpenRelease);
    }

    private async Task CheckForUpdateAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatusText = "Checking for updates...";
        UpdateAvailable = false;
        LatestVersionUrl = null;

        try
        {
            var response = await HttpClient.GetStringAsync(
                "https://api.github.com/repos/evildarkarchon/AutoQACSharp/releases/latest");

            using var doc = JsonDocument.Parse(response);
            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();

            if (tagName == null)
            {
                UpdateStatusText = "Unable to parse release info.";
                return;
            }

            var remoteVersionStr = tagName.TrimStart('v');
            var currentVersionStr = AppVersion;

            var isNewer = Version.TryParse(remoteVersionStr, out var remote)
                          && Version.TryParse(currentVersionStr, out var current)
                          && remote > current;

            if (isNewer)
            {
                UpdateAvailable = true;
                LatestVersionUrl = htmlUrl;
                UpdateStatusText = $"Update available: v{remoteVersionStr}";
            }
            else
            {
                UpdateStatusText = "You are running the latest version.";
            }
        }
        catch (HttpRequestException ex)
        {
            UpdateStatusText = $"Network error: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            UpdateStatusText = "Request timed out.";
        }
        catch (JsonException)
        {
            UpdateStatusText = "Failed to parse update response.";
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Silently ignore if URL cannot be opened
        }
    }
}
