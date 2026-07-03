using System;

using Avalonia.Controls;
using Avalonia.Interactivity;

using LDOCE5ViewerX.Services;
using LDOCE5ViewerX.ViewModels;

namespace LDOCE5ViewerX.Views;

/// <summary>
/// Dialog that hosts live application settings.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly SettingsDialogViewModel _viewModel;

    /// <summary>
    /// Creates the settings dialog with default application settings.
    /// </summary>
    public SettingsDialog()
        : this(new AppConfiguration(new IndexPaths()))
    {
    }

    /// <summary>
    /// Creates the settings dialog for the supplied application config.
    /// </summary>
    /// <param name="config">Application config edited by the dialog.</param>
    public SettingsDialog(AppConfiguration config)
    {
        InitializeComponent();

        _viewModel = new SettingsDialogViewModel(config);
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

    private void OnMoveWebSearchSiteUpClicked(object? sender, RoutedEventArgs e)
    {
        ExecuteWebSearchSiteCommand(sender, _viewModel.MoveWebSearchSiteUpCommand);
    }

    private void OnMoveWebSearchSiteDownClicked(object? sender, RoutedEventArgs e)
    {
        ExecuteWebSearchSiteCommand(sender, _viewModel.MoveWebSearchSiteDownCommand);
    }

    private void OnRemoveWebSearchSiteClicked(object? sender, RoutedEventArgs e)
    {
        ExecuteWebSearchSiteCommand(sender, _viewModel.RemoveWebSearchSiteCommand);
    }

    private static void ExecuteWebSearchSiteCommand(object? sender, System.Windows.Input.ICommand command)
    {
        if (sender is Control { DataContext: WebSearchSite site } && command.CanExecute(site))
        {
            command.Execute(site);
        }
    }
}
