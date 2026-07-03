using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using LDOCE5ViewerX.ViewModels;

namespace LDOCE5ViewerX.Views;

/// <summary>
/// Dialog that hosts dictionary index creation UI.
/// </summary>
public partial class IndexerDialog : Window
{
    /// <summary>
    /// Creates the indexer dialog and connects view-only services to the view model.
    /// </summary>
    public IndexerDialog()
    {
        InitializeComponent();

        IndexerDialogViewModel viewModel = new();
        viewModel.BrowseRequested += OnBrowseRequested;
        viewModel.CloseRequested += OnCloseRequested;
        DataContext = viewModel;
    }

    /// <summary>
    /// Handles the folder picker request because storage APIs belong to the view layer.
    /// </summary>
    private async void OnBrowseRequested(object? sender, EventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select ldoce5.data Folder",
                AllowMultiple = false,
            });

        IStorageFolder? folder = folders.FirstOrDefault();
        if (folder is not null && folder.Path.IsAbsoluteUri && DataContext is IndexerDialogViewModel viewModel)
        {
            viewModel.SetSelectedDataPath(folder.Path.LocalPath);
        }
    }

    /// <summary>
    /// Handles close requests from the view model.
    /// </summary>
    private void OnCloseRequested(object? sender, bool? result)
    {
        Close(result);
    }
}
