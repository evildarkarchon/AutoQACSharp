using System;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
/// Per-row UI wrapper around an immutable <see cref="PluginRefreshRow"/>. Owns the
/// mutable <c>IsSelected</c> state with proper INotifyPropertyChanged plumbing so
/// the checkbox stays in sync with batch select/deselect operations and survives
/// rapid snapshot replacements driven by background approximation updates.
/// Selection toggles bubble up to the owning view model via <see cref="SelectionToggled"/>.
/// </summary>
public sealed partial class PluginListItem : ObservableObject
{
    private bool _suppressSelectionCallback;

    public PluginListItem(PluginRefreshRow info, bool isSelected)
    {
        Info = info;
        IsSelected = isSelected;
    }

    [ObservableProperty]
    public partial PluginRefreshRow Info { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string FileName => Info.FileName;
    public string FullPath => Info.FullPath;
    public PluginRefreshRowKey Key => Info.Key;
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
    /// Replaces the underlying plugin refresh row without firing
    /// <see cref="SelectionToggled"/>. Used by the parent view model when state
    /// updates produce a new <see cref="PluginRefreshRow"/> instance for the same row.
    /// </summary>
    public void UpdateInfo(PluginRefreshRow newInfo)
    {
        Info = newInfo;
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(Key));
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
