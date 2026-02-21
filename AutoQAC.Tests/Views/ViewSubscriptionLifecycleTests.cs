using FluentAssertions;

namespace AutoQAC.Tests.Views;

public sealed class ViewSubscriptionLifecycleTests
{
    [Fact]
    public void SkipListWindow_ShouldTrackSubscriptionsInCompositeDisposable_AndDisposeOnClose()
    {
        // Arrange
        var source = File.ReadAllText(GetRepoFilePath("AutoQAC/Views/SkipListWindow.axaml.cs"));

        // Assert
        source.Should().Contain("CompositeDisposable _disposables", "window should track Rx subscriptions");
        source.Should().Contain("_disposables.Add(viewModel.SaveCommand.Subscribe", "save subscription must be tracked");
        source.Should().Contain("_disposables.Add(viewModel.CancelCommand.Subscribe", "cancel subscription must be tracked");
        source.Should().Contain("protected override void OnClosed", "cleanup should happen on close");
        source.Should().Contain("_disposables.Dispose();", "all tracked subscriptions must be disposed");
    }

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
