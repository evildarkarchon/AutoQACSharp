using System;
using System.Collections.Generic;
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
}
