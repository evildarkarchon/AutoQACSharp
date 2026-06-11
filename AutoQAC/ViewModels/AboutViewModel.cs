using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class AboutViewModel : ViewModelBase
{
    private static readonly HttpClient HttpClient = new();

    static AboutViewModel()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AutoQACSharp/1.0");
        HttpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public string AppVersion { get; }
    public string InformationalVersion { get; }
    public string BuildDate { get; }
    public string DotNetVersion { get; }
    public string UiFrameworkDisplayName { get; }
    public string UiFrameworkVersion { get; }
    public string MvvmToolkitVersion { get; }

    public string GitHubUrl => "https://github.com/evildarkarchon/AutoQACSharp";
    public string GitHubIssuesUrl => "https://github.com/evildarkarchon/AutoQACSharp/issues";
    public string XEditUrl => "https://github.com/TES5Edit/TES5Edit";

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdateCommand))]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLatestReleaseCommand))]
    private bool _updateAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLatestReleaseCommand))]
    private string? _latestVersionUrl;

    public AboutViewModel(IUiFrameworkVersionProvider uiFrameworkVersionProvider)
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        AppVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";

        var infoAttr = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        InformationalVersion = infoAttr?.InformationalVersion ?? AppVersion;

        var buildDateAttr = assembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate");
        BuildDate = buildDateAttr?.Value ?? "Unknown";

        DotNetVersion = RuntimeInformation.FrameworkDescription;

        UiFrameworkDisplayName = $"{uiFrameworkVersionProvider.DisplayName}:";
        UiFrameworkVersion = uiFrameworkVersionProvider.Version;

        var toolkitAssembly = typeof(ObservableObject).Assembly;
        var toolkitVer = toolkitAssembly.GetName().Version;
        MvvmToolkitVersion = toolkitVer != null ? $"{toolkitVer.Major}.{toolkitVer.Minor}.{toolkitVer.Build}" : "Unknown";
    }

    private bool CanCheckForUpdate() => !IsCheckingUpdate;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdate))]
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

    [RelayCommand]
    private void OpenGitHub() => OpenUrl(GitHubUrl);

    [RelayCommand]
    private void OpenIssues() => OpenUrl(GitHubIssuesUrl);

    [RelayCommand]
    private void OpenXEdit() => OpenUrl(XEditUrl);

    private bool CanOpenLatestRelease() => UpdateAvailable && !string.IsNullOrEmpty(LatestVersionUrl);

    [RelayCommand(CanExecute = nameof(CanOpenLatestRelease))]
    private void OpenLatestRelease() => OpenUrl(LatestVersionUrl!);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // URL cannot be opened — silently ignore.
        }
    }
}
