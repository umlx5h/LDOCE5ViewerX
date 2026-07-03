using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.ViewModels;

/// <summary>
/// View model for the advanced-search dialog.
/// </summary>
public partial class AdvancedSearchDialogViewModel : ObservableObject
{
    /// <summary>
    /// Creates the advanced-search dialog view model.
    /// </summary>
    public AdvancedSearchDialogViewModel()
    {
        FilterNodes = new ObservableCollection<AdvancedSearchFilterNodeViewModel>(
            AdvancedSearchFilterCatalog.Load().Select(node => new AdvancedSearchFilterNodeViewModel(node, node.Label, UpdateCommandState)));
    }

    /// <summary>
    /// Raised when the dialog should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Raised when the selected filters should be applied to main-window searches.
    /// </summary>
    public event EventHandler<SearchFilterQuery>? FiltersApplied;

    /// <summary>
    /// Root filter nodes shown in the dialog tree.
    /// </summary>
    public ObservableCollection<AdvancedSearchFilterNodeViewModel> FilterNodes { get; }

    /// <summary>
    /// Applies the selected filters to the main-window search.
    /// </summary>
    public RelayCommand SearchCommand => field ??= new(() =>
    {
        SearchFilterQuery filters = BuildFilterQuery();
        FiltersApplied?.Invoke(this, filters);
    }, CanSearch);

    /// <summary>
    /// Resets selected filters and clears them from the main window.
    /// </summary>
    public RelayCommand ResetCommand => field ??= new(() =>
    {
        foreach (AdvancedSearchFilterNodeViewModel node in FilterNodes)
        {
            node.SetCheckedRecursive(false);
        }

        UpdateCommandState();
        FiltersApplied?.Invoke(this, SearchFilterQuery.Empty);
    });

    /// <summary>
    /// Closes the filter window.
    /// </summary>
    public RelayCommand CloseCommand => field ??= new(() =>
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>
    /// Builds grouped full-text filters from checked tree nodes.
    /// </summary>
    private SearchFilterQuery BuildFilterQuery()
    {
        return SearchFilterQuery.Create(
            FilterNodes
                .Select(node => node.GetSelectedCodesByGroup())
                .Where(group => group.Count > 0));
    }

    private bool CanSearch()
    {
        return BuildFilterQuery().HasFilters;
    }

    private void UpdateCommandState()
    {
        SearchCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// Checkable advanced-search filter node for the dialog tree.
/// </summary>
public partial class AdvancedSearchFilterNodeViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _updatingChildren;

    /// <summary>
    /// Creates one checkable filter node.
    /// </summary>
    public AdvancedSearchFilterNodeViewModel(AdvancedSearchFilterNode node, string groupKey, Action changed)
    {
        Label = node.Label;
        Code = node.Code;
        GroupKey = groupKey;
        _changed = changed;
        Children = new ObservableCollection<AdvancedSearchFilterNodeViewModel>(
            (node.Children ?? []).Select(child => new AdvancedSearchFilterNodeViewModel(child, groupKey, changed)));
    }

    /// <summary>
    /// User-visible filter label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Optional full-text filter code.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// Top-level group name used for AND/OR semantics.
    /// </summary>
    public string GroupKey { get; }

    /// <summary>
    /// Child filter nodes.
    /// </summary>
    public ObservableCollection<AdvancedSearchFilterNodeViewModel> Children { get; }

    /// <summary>
    /// Gets whether this node has children.
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_updatingChildren && HasChildren && ShouldPropagateCheckedState(value))
        {
            SetChildrenChecked(value);
        }

        _changed();
    }

    private bool ShouldPropagateCheckedState(bool value)
    {
        return !value || string.IsNullOrWhiteSpace(Code);
    }

    /// <summary>
    /// Sets this node and all descendants to the same checked state.
    /// </summary>
    public void SetCheckedRecursive(bool value)
    {
        IsChecked = value;
        SetChildrenChecked(value);
    }

    /// <summary>
    /// Returns selected filter codes in this node's top-level group.
    /// </summary>
    public IReadOnlyCollection<string> GetSelectedCodesByGroup()
    {
        List<string> codes = [];
        AppendSelectedCodes(codes);
        return codes;
    }

    private void AppendSelectedCodes(List<string> codes)
    {
        if (IsChecked && !string.IsNullOrWhiteSpace(Code))
        {
            codes.Add(Code);
        }

        foreach (AdvancedSearchFilterNodeViewModel child in Children)
        {
            child.AppendSelectedCodes(codes);
        }
    }

    private void SetChildrenChecked(bool value)
    {
        _updatingChildren = true;
        try
        {
            foreach (AdvancedSearchFilterNodeViewModel child in Children)
            {
                child.SetCheckedRecursive(value);
            }
        }
        finally
        {
            _updatingChildren = false;
        }
    }
}
