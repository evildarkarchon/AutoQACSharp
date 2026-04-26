using FluentAssertions;

namespace AutoQAC.Tests.Views;

public sealed class ViewSubscriptionLifecycleTests
{
    [Fact]
    public void MainWindowValidationPanel_ShouldRenderValidationErrorMessage()
    {
        // Arrange
        var source = File.ReadAllText(GetRepoFilePath("AutoQAC/Views/MainWindow.axaml"));

        // Assert
        source.Should().Contain("Text=\"{Binding Message}\"",
            "inline validation errors should show the specific reason, not just the title and fix step");
    }

    /// <summary>
    /// After the migration to CommunityToolkit.Mvvm, dialog windows expose a
    /// <c>CloseRequested</c> event from the ViewModel rather than ReactiveUI commands.
    /// The View must subscribe in <c>DataContextChanged</c>, unsubscribe when the
    /// DataContext is replaced, and unsubscribe again on close to prevent leaks.
    /// </summary>
    [Fact]
    public void SkipListWindow_ShouldSubscribeToCloseRequestedEvent_AndUnsubscribeOnClose()
    {
        // Arrange
        var source = File.ReadAllText(GetRepoFilePath("AutoQAC/Views/SkipListWindow.axaml.cs"));

        // Assert
        source.Should().Contain("CloseRequested += OnCloseRequested",
            "window should subscribe to the VM CloseRequested event");
        source.Should().Contain("CloseRequested -= OnCloseRequested",
            "window must unsubscribe to avoid leaking the VM after close");
        source.Should().Contain("protected override void OnClosed",
            "cleanup should happen on close");
    }

    /// <summary>
    /// ProgressWindow is special: it must guard against double-dispose (the user can
    /// either click the close button on the title bar or have the VM raise
    /// CloseRequested), and must dispose the previous VM when the DataContext is
    /// reassigned.
    /// </summary>
    [Fact]
    public void ProgressWindow_ShouldUnsubscribePreviousViewModel_AndGuardDoubleDispose()
    {
        // Arrange
        var source = File.ReadAllText(GetRepoFilePath("AutoQAC/Views/ProgressWindow.axaml.cs"));

        // Assert
        source.Should().Contain("ProgressViewModel? _subscribedViewModel", "window should track current VM subscription");
        source.Should().Contain("bool _disposeHandled", "window should guard against double disposal");
        source.Should().Contain("_subscribedViewModel.CloseRequested -= OnCloseRequested;", "old VM subscriptions must be removed");
        source.Should().Contain("DisposeViewModelIfNeeded()", "cleanup must be centralized");
        source.Should().Contain("if (_disposeHandled)", "double-dispose guard must short-circuit");
        source.Should().Contain("DataContextChanged -= OnDataContextChanged;", "DataContext handler should be detached on dispose");
    }

    private static string GetRepoFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !Directory.Exists(Path.Combine(current.FullName, "AutoQAC")))
        {
            current = current.Parent;
        }

        if (current == null)
        {
            throw new DirectoryNotFoundException("Unable to locate repository root for source assertions.");
        }

        return Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
