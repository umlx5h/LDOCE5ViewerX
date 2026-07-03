using System;

using Avalonia.Controls;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.ViewModels;

namespace LDOCE5ViewerX.Views;

/// <summary>
/// Modeless window that collects advanced-search filter criteria.
/// </summary>
public partial class AdvancedSearchDialog : Window
{
    /// <summary>
    /// Creates the advanced-search filter window.
    /// </summary>
    public AdvancedSearchDialog()
    {
        InitializeComponent();

        AdvancedSearchDialogViewModel viewModel = new();
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.FiltersApplied += OnFiltersApplied;
        DataContext = viewModel;
    }

    /// <summary>
    /// Raised when selected filters should be applied to the main window.
    /// </summary>
    public event EventHandler<SearchFilterQuery>? FiltersApplied;

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnFiltersApplied(object? sender, SearchFilterQuery filters)
    {
        FiltersApplied?.Invoke(this, filters);
    }
}
