using System;
using AutoQAC.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
/// Per-row UI wrapper around an immutable <see cref="PluginInfo"/>. Owns the
/// mutable <c>IsSelected</c> state with proper INotifyPropertyChanged plumbing so
/// the checkbox stays in sync with batch select/deselect operations and survives
/// rapid <c>state.PluginsToClean</c> replacements driven by background approximation
/// updates. Selection toggles bubble up to the owning view model via
/// <see cref="SelectionToggled"/>; the parent persists the change to the
/// <c>IStateService</c> excluded-plugin set, which is the source of truth the
/// cleaning orchestrator filters against.
/// </summary>
public sealed partial class PluginListItem : ObservableObject
{
    private bool _suppressSelectionCallback;

    public PluginListItem(PluginInfo info, bool isSelected)
    {
        _info = info;
        _isSelected = isSelected;
    }

    [ObservableProperty]
    private PluginInfo _info;

    [ObservableProperty]
    private bool _isSelected;

    public string FileName => Info.FileName;
    public string FullPath => Info.FullPath;
    public string ApproximationDisplayText => Info.ApproximationDisplayText;
    public bool IsApproximationPending => Info.IsApproximationPending;
    public bool HasApproximationPreview => Info.HasApproximationPreview;
    public PluginIssueApproximation Approximation => Info.Approximation;

    /// <summary>
    /// Raised when the user toggles the row's checkbox. Suppressed when the parent
    /// view model is syncing IsSelected from authoritative state (avoids bouncing
    /// the same toggle back into the state service).
    /// </summary>
    public event Action<PluginListItem, bool>? SelectionToggled;

    /// <summary>
    /// Replaces the underlying plugin record without firing
    /// <see cref="SelectionToggled"/>. Used by the parent view model when state
    /// updates produce a new <see cref="PluginInfo"/> instance for the same row.
    /// </summary>
    public void UpdateInfo(PluginInfo newInfo)
    {
        Info = newInfo;
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(ApproximationDisplayText));
        OnPropertyChanged(nameof(IsApproximationPending));
        OnPropertyChanged(nameof(HasApproximationPreview));
        OnPropertyChanged(nameof(Approximation));
    }

    /// <summary>
    /// Sets <see cref="IsSelected"/> from authoritative state without re-raising
    /// <see cref="SelectionToggled"/>.
    /// </summary>
    public void SetSelectedFromState(bool isSelected)
    {
        if (IsSelected == isSelected)
        {
            return;
        }

        _suppressSelectionCallback = true;
        try
        {
            IsSelected = isSelected;
        }
        finally
        {
            _suppressSelectionCallback = false;
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (_suppressSelectionCallback)
        {
            return;
        }

        SelectionToggled?.Invoke(this, value);
    }
}
