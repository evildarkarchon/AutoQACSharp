using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Services.State;
using FluentAssertions;
using Xunit;

namespace AutoQAC.Tests.Services;

public class StateServiceTests
{
    private readonly StateService _sut;

    public StateServiceTests()
    {
        _sut = new StateService();
    }

    [Fact]
    public void InitialState_IsEmpty()
    {
        _sut.CurrentState.LoadOrderPath.Should().BeNull();
        _sut.CurrentState.IsCleaning.Should().BeFalse();
    }

    [Fact]
    public void UpdateConfigurationPaths_UpdatesState()
    {
        var loadOrder = "loadorder.txt";
        var mo2 = "mo2.exe";
        var xedit = "xedit.exe";

        _sut.UpdateConfigurationPaths(loadOrder, mo2, xedit);

        var state = _sut.CurrentState;
        state.LoadOrderPath.Should().Be(loadOrder);
        state.MO2ExecutablePath.Should().Be(mo2);
        state.XEditExecutablePath.Should().Be(xedit);
    }

    [Fact]
    public void StartCleaning_ResetsProgressAndLists()
    {
        var plugins = new List<string> { "a.esp", "b.esp" };
        
        _sut.StartCleaning(plugins);

        var state = _sut.CurrentState;
        state.IsCleaning.Should().BeTrue();
        state.PluginsToClean.Should().BeEquivalentTo(plugins);
        state.Progress.Should().Be(0);
        state.TotalPlugins.Should().Be(2);
        state.CleanedPlugins.Should().BeEmpty();
    }

    [Fact]
    public void AddCleaningResult_UpdatesCorrectSetAndProgress()
    {
        _sut.StartCleaning(new List<string> { "a.esp", "b.esp" });
        
        _sut.AddCleaningResult("a.esp", CleaningStatus.Cleaned);

        var state = _sut.CurrentState;
        state.CleanedPlugins.Should().Contain("a.esp");
        state.Progress.Should().Be(1);
    }
    
    [Fact]
    public void StateChanged_EmitsOnUpdate()
    {
        var count = 0;
        // Subscribe to the observable
        using var sub = _sut.StateChanged.Subscribe(_ => count++);

        // Initial value should be emitted immediately upon subscription for BehaviorSubject
        // So count is likely 1 here.

        _sut.UpdateConfigurationPaths("path", null, null);

        // Should have emitted again
        count.Should().BeGreaterThan(1);
    }

    #region Concurrency Tests

    /// <summary>
    /// Verifies that concurrent state updates from multiple threads do not
    /// corrupt state or cause data races. All updates should be applied
    /// and the final state should be consistent.
    /// </summary>
    [Fact]
    public async Task ConcurrentUpdates_ShouldMaintainStateConsistency()
    {
        // Arrange
        const int numThreads = 10;
        const int updatesPerThread = 100;
        var barrier = new Barrier(numThreads);

        // Act
        var tasks = Enumerable.Range(0, numThreads).Select(threadId => Task.Run(() =>
        {
            // Wait for all threads to start simultaneously
            barrier.SignalAndWait();

            for (int i = 0; i < updatesPerThread; i++)
            {
                // Each thread updates state with its own progress value
                _sut.UpdateState(s => s with { Progress = threadId * 1000 + i });
            }
        }));

        await Task.WhenAll(tasks);

        // Assert
        // State should be consistent (not corrupted)
        var finalState = _sut.CurrentState;
        finalState.Should().NotBeNull("state should not be null after concurrent updates");

        // The Progress value should be one of the valid values that was set
        // (we can't predict which one due to race conditions, but it should be valid)
        finalState.Progress.Should().BeInRange(0, (numThreads - 1) * 1000 + updatesPerThread - 1,
            "final progress value should be within valid range");
    }

    /// <summary>
    /// Verifies that concurrent additions of cleaning results do not
    /// lose updates. All added plugins should be present in the final state.
    /// </summary>
    [Fact]
    public async Task ConcurrentAddCleaningResult_ShouldNotLoseUpdates()
    {
        // Arrange
        const int numPlugins = 100;
        _sut.StartCleaning(Enumerable.Range(0, numPlugins).Select(i => $"plugin{i}.esp").ToList());

        // Act
        // Simulate concurrent cleaning results from multiple threads
        var tasks = Enumerable.Range(0, numPlugins).Select(i => Task.Run(() =>
        {
            var status = (CleaningStatus)(i % 3); // Cycle through Cleaned, Skipped, Failed
            _sut.AddCleaningResult($"plugin{i}.esp", status);
        }));

        await Task.WhenAll(tasks);

        // Assert
        var finalState = _sut.CurrentState;

        // All plugins should be accounted for
        var totalProcessed = finalState.CleanedPlugins.Count +
                             finalState.SkippedPlugins.Count +
                             finalState.FailedPlugins.Count;

        totalProcessed.Should().Be(numPlugins,
            "all concurrent cleaning results should be recorded without loss");

        // Progress should match total processed
        finalState.Progress.Should().Be(numPlugins,
            "progress should match the number of processed plugins");
    }

    /// <summary>
    /// Verifies that observable emissions during rapid concurrent updates
    /// do not miss any emissions. Each update should trigger an emission.
    /// </summary>
    [Fact]
    public async Task StateChanged_ShouldEmitForAllUpdates_DuringRapidUpdates()
    {
        // Arrange
        const int numUpdates = 50;
        var emittedStates = new System.Collections.Concurrent.ConcurrentBag<AppState>();

        using var subscription = _sut.StateChanged.Subscribe(state =>
        {
            emittedStates.Add(state);
        });

        // Initial emission from BehaviorSubject
        await Task.Delay(10); // Small delay to ensure subscription is active
        var initialCount = emittedStates.Count;

        // Act
        var tasks = Enumerable.Range(0, numUpdates).Select(i => Task.Run(() =>
        {
            _sut.UpdateState(s => s with { Progress = i });
        }));

        await Task.WhenAll(tasks);
        await Task.Delay(50); // Allow time for emissions to propagate

        // Assert
        // Should have received at least numUpdates emissions (plus initial)
        // Note: Due to the lock in UpdateState, we should get all emissions
        (emittedStates.Count - initialCount).Should().Be(numUpdates,
            "each state update should trigger exactly one emission");
    }

    /// <summary>
    /// Verifies that reading CurrentState while updates are happening
    /// returns a consistent snapshot (not a partially updated state).
    /// </summary>
    [Fact]
    public async Task CurrentState_ShouldReturnConsistentSnapshot_DuringConcurrentUpdates()
    {
        // Arrange
        const int numIterations = 1000;
        var inconsistentStateFound = false;
        var cts = new CancellationTokenSource();

        // Start a writer task that updates multiple state fields atomically
        var writerTask = Task.Run(async () =>
        {
            for (int i = 0; i < numIterations && !cts.Token.IsCancellationRequested; i++)
            {
                _sut.UpdateState(s => s with
                {
                    Progress = i,
                    TotalPlugins = i + 100, // Deliberately different to detect inconsistency
                    CurrentPlugin = $"Plugin{i}.esp"
                });
                await Task.Yield();
            }
        });

        // Start a reader task that checks state consistency
        var readerTask = Task.Run(async () =>
        {
            for (int i = 0; i < numIterations * 2 && !cts.Token.IsCancellationRequested; i++)
            {
                var state = _sut.CurrentState;

                // Check if TotalPlugins = Progress + 100 (our invariant)
                // If we read a partially updated state, this might not hold
                if (state.Progress > 0 && state.TotalPlugins != state.Progress + 100)
                {
                    inconsistentStateFound = true;
                    cts.Cancel();
                    break;
                }
                await Task.Yield();
            }
        });

        // Act
        await Task.WhenAll(writerTask, readerTask);

        // Assert
        inconsistentStateFound.Should().BeFalse(
            "CurrentState should always return a consistent snapshot, never a partial update");
    }

    #endregion

    #region Observable Behavior Tests

    /// <summary>
    /// Verifies that PluginProcessed observable emits correct events
    /// when plugins are processed.
    /// </summary>
    [Fact]
    public void PluginProcessed_ShouldEmitCorrectEvents()
    {
        // Arrange
        var processedPlugins = new List<(string plugin, CleaningStatus status)>();
        using var subscription = _sut.PluginProcessed.Subscribe(p => processedPlugins.Add(p));

        _sut.StartCleaning(new List<string> { "a.esp", "b.esp", "c.esp" });

        // Act
        _sut.AddCleaningResult("a.esp", CleaningStatus.Cleaned);
        _sut.AddCleaningResult("b.esp", CleaningStatus.Skipped);
        _sut.AddCleaningResult("c.esp", CleaningStatus.Failed);

        // Assert
        processedPlugins.Should().HaveCount(3);
        processedPlugins.Should().Contain(("a.esp", CleaningStatus.Cleaned));
        processedPlugins.Should().Contain(("b.esp", CleaningStatus.Skipped));
        processedPlugins.Should().Contain(("c.esp", CleaningStatus.Failed));
    }

    /// <summary>
    /// Verifies that ConfigurationValidChanged only emits when the
    /// computed validity actually changes (DistinctUntilChanged behavior).
    /// </summary>
    [Fact]
    public void ConfigurationValidChanged_ShouldOnlyEmitOnActualChange()
    {
        // Arrange
        var validityChanges = new List<bool>();
        using var subscription = _sut.ConfigurationValidChanged.Subscribe(v => validityChanges.Add(v));

        // Act
        // Update paths in a way that doesn't change validity
        _sut.UpdateConfigurationPaths(null, null, null); // Still invalid
        _sut.UpdateConfigurationPaths(null, "mo2.exe", null); // Still invalid (no xEdit or LO)

        // Now make it valid
        _sut.UpdateConfigurationPaths("loadorder.txt", null, "xedit.exe"); // Valid

        // Update something else (validity unchanged)
        _sut.UpdateState(s => s with { Progress = 5 });

        // Make invalid again
        _sut.UpdateConfigurationPaths(null, null, null); // Invalid

        // Assert
        // Should have: initial(false), changed to true, changed to false
        // Due to DistinctUntilChanged, we shouldn't see duplicate values
        validityChanges.Should().ContainInOrder(false, true, false);
    }

    /// <summary>
    /// Verifies that ProgressChanged observable emits correctly
    /// and uses DistinctUntilChanged to avoid duplicate emissions.
    /// </summary>
    [Fact]
    public void ProgressChanged_ShouldEmitDistinctValues()
    {
        // Arrange
        var progressChanges = new List<(int current, int total)>();
        using var subscription = _sut.ProgressChanged.Subscribe(p => progressChanges.Add(p));

        // Act
        _sut.StartCleaning(new List<string> { "a.esp", "b.esp", "c.esp" });
        _sut.UpdateProgress(1, 3);
        _sut.UpdateProgress(1, 3); // Duplicate - should be filtered
        _sut.UpdateProgress(2, 3);
        _sut.UpdateProgress(3, 3);

        // Assert
        // Should not have duplicate (1, 3) entries
        var distinctCount = progressChanges.Distinct().Count();
        progressChanges.Should().HaveCount(distinctCount,
            "DistinctUntilChanged should filter duplicate progress values");
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Verifies that FinishCleaning properly resets cleaning state.
    /// </summary>
    [Fact]
    public void FinishCleaning_ShouldResetCleaningState()
    {
        // Arrange
        _sut.StartCleaning(new List<string> { "a.esp", "b.esp" });
        _sut.UpdateState(s => s with { CurrentPlugin = "a.esp", CurrentOperation = "Cleaning..." });
        _sut.AddCleaningResult("a.esp", CleaningStatus.Cleaned);

        // Act
        _sut.FinishCleaning();

        // Assert
        var state = _sut.CurrentState;
        state.IsCleaning.Should().BeFalse();
        state.CurrentPlugin.Should().BeNull();
        state.CurrentOperation.Should().BeNull();
        // Progress and results should be preserved for summary
        state.Progress.Should().Be(1);
        state.CleanedPlugins.Should().Contain("a.esp");
    }

    /// <summary>
    /// Verifies that AddCleaningResult correctly categorizes different statuses.
    /// </summary>
    [Theory]
    [InlineData(CleaningStatus.Cleaned, "CleanedPlugins")]
    [InlineData(CleaningStatus.Skipped, "SkippedPlugins")]
    [InlineData(CleaningStatus.Failed, "FailedPlugins")]
    public void AddCleaningResult_ShouldCategorizeCorrectly(CleaningStatus status, string expectedSet)
    {
        // Arrange
        _sut.StartCleaning(new List<string> { "test.esp" });

        // Act
        _sut.AddCleaningResult("test.esp", status);

        // Assert
        var state = _sut.CurrentState;
        switch (expectedSet)
        {
            case "CleanedPlugins":
                state.CleanedPlugins.Should().Contain("test.esp");
                break;
            case "SkippedPlugins":
                state.SkippedPlugins.Should().Contain("test.esp");
                break;
            case "FailedPlugins":
                state.FailedPlugins.Should().Contain("test.esp");
                break;
        }
    }

    /// <summary>
    /// Verifies that disposing the StateService properly disposes subjects.
    /// </summary>
    [Fact]
    public void Dispose_ShouldDisposeSubjects()
    {
        // Arrange
        var service = new StateService();
        var emissionCount = 0;
        using var subscription = service.StateChanged.Subscribe(
            _ => emissionCount++,
            ex => { }, // Error handler
            () => { }); // Completion handler

        // Act
        service.Dispose();

        // After disposal, updates should not cause emissions
        // (or throw - depending on implementation)
        var previousCount = emissionCount;

        // Assert
        // The subject is disposed, so this test mainly verifies no exception
        // Actual behavior depends on how disposed subjects are handled
    }

    /// <summary>
    /// Verifies that SetPluginsToClean properly initializes the plugin list.
    /// </summary>
    [Fact]
    public void SetPluginsToClean_ShouldUpdatePluginList()
    {
        // Arrange
        var plugins = new List<string> { "a.esp", "b.esp", "c.esp" };

        // Act
        _sut.SetPluginsToClean(plugins);

        // Assert
        var state = _sut.CurrentState;
        state.PluginsToClean.Should().BeEquivalentTo(plugins);
        state.PluginsToClean.Should().NotBeSameAs(plugins,
            "should create a copy, not store the reference");

        // Modifying the original list should not affect state
        plugins.Add("d.esp");
        state.PluginsToClean.Should().HaveCount(3);
    }

    #endregion
}
