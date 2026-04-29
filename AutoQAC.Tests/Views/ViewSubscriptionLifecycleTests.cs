using System.Text.RegularExpressions;
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

    /// <summary>
    /// The normal cleaning progress window path (<c>MainWindow.ShowProgressAsync</c>) must wire
    /// <see cref="ProgressViewModel.CloseRequested"/> to close the window and must dispose the
    /// ViewModel when the window closes, mirroring the preview-path lifecycle. Assertions use
    /// regex (not exact substrings) so refactors that switch lambda <-> method-group syntax,
    /// or rename the local idempotent guard variable, do not break the test.
    ///
    /// This composes with the existing <c>ProgressWindow.OnDataContextChanged</c> +
    /// <c>OnClosed</c> -> <c>DisposeViewModelIfNeeded</c> contract as defense-in-depth, so a
    /// literal <c>"Defense in depth"</c> comment is asserted to make the dual-subscription
    /// contract explicit and refactor-resistant.
    /// </summary>
    [Fact]
    public void MainWindowShowProgressAsync_ShouldWireProgressWindowCloseAndDisposal()
    {
        // Arrange
        var source = File.ReadAllText(GetRepoFilePath("AutoQAC/Views/MainWindow.axaml.cs"));

        // Assert -- 1. CloseRequested subscription (lambda OR method group)
        Regex.IsMatch(source, @"progressViewModel\.CloseRequested\s*\+=").Should().BeTrue(
            "ShowProgressAsync must subscribe to ProgressViewModel.CloseRequested");

        // 2. progressWindow.Close() reachable from the CloseRequested handler
        Regex.IsMatch(source, @"progressWindow\.Close\s*\(\s*\)").Should().BeTrue(
            "CloseRequested handler must close the normal progress window");

        // 3. progressWindow.Closed subscription (lambda OR method group)
        Regex.IsMatch(source, @"progressWindow\.Closed\s*\+=").Should().BeTrue(
            "ShowProgressAsync must subscribe to progressWindow.Closed for disposal");

        // 4. progressViewModel.Dispose() reachable from the Closed handler scope
        Regex.IsMatch(source, @"progressViewModel\.Dispose\s*\(\s*\)").Should().BeTrue(
            "normal progress window close must dispose the ViewModel");

        // 5. Boolean local guard variable (any of disposed/cleaned/done; bool or var)
        Regex.IsMatch(source, @"(?:bool|var)\s+\w*(disposed|cleaned|done)\w*\s*=\s*false",
            RegexOptions.IgnoreCase).Should().BeTrue(
            "ShowProgressAsync must use a local idempotent dispose guard");

        // 6. Defense-in-depth code comment makes the dual-subscription contract explicit
        source.Should().Contain("Defense in depth",
            "ShowProgressAsync must document why ProgressWindow.OnDataContextChanged + OnClosed and the new ShowProgressAsync wiring both subscribe to CloseRequested/Closed");
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
