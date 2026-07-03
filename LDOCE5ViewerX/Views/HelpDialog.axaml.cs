using System;

using Avalonia.Controls;

using LDOCE5ViewerX.ViewModels;

namespace LDOCE5ViewerX.Views;

/// <summary>
/// Window that displays keyboard shortcut help.
/// </summary>
public partial class HelpDialog : Window
{
    private readonly HelpDialogViewModel _viewModel;

    /// <summary>
    /// Creates the help dialog.
    /// </summary>
    public HelpDialog()
    {
        InitializeComponent();

        _viewModel = new HelpDialogViewModel();
        _viewModel.CloseRequested += OnCloseRequested;
        DataContext = _viewModel;
        Closed += OnClosed;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        Closed -= OnClosed;
    }
}
