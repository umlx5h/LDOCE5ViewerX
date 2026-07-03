using System;
using System.IO;
using System.Text;
using System.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.ViewModels;

/// <summary>
/// View model that controls dictionary index creation.
/// </summary>
public partial class IndexerDialogViewModel : ObservableObject
{
    private readonly IndexPaths _indexPaths = new();
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Raised when the dialog should open a platform folder picker.
    /// </summary>
    public event EventHandler? BrowseRequested;

    /// <summary>
    /// Raised when the dialog should close with an indexing result.
    /// </summary>
    public event EventHandler<bool?>? CloseRequested;

    /// <summary>
    /// Creates the view model and initializes the indexing log.
    /// </summary>
    public IndexerDialogViewModel()
    {
        AppendLog("Select the ldoce5.data folder, then click Start Indexing.");
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartIndexingCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    public partial string DataPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LogText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartIndexingCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    public partial bool IsRunning { get; set; }

    /// <summary>
    /// Text shown on the cancel/close button.
    /// </summary>
    public string CancelButtonText => IsRunning ? "Cancel" : "Close";

    /// <summary>
    /// Updates the data path selected by the view's folder picker.
    /// </summary>
    /// <param name="path">Selected folder path.</param>
    public void SetSelectedDataPath(string path)
    {
        if (!IsRunning)
        {
            DataPath = path;
        }
    }

    /// <summary>
    /// Requests folder selection from the view.
    /// </summary>
    public RelayCommand BrowseCommand => field ??= new(() =>
    {
        BrowseRequested?.Invoke(this, EventArgs.Empty);
    }, () => !IsRunning);

    /// <summary>
    /// Starts the full index creation process.
    /// </summary>
    public AsyncRelayCommand StartIndexingCommand => field ??= new(async () =>
    {
        if (string.IsNullOrWhiteSpace(DataPath))
        {
            AppendLog("Data location is empty.");
            return;
        }

        IsRunning = true;
        OnPropertyChanged(nameof(CancelButtonText));
        _cancellationTokenSource = new CancellationTokenSource();
        Progress<IndexBuildProgress> progress = new(p => AppendLog(p.Message));

        try
        {
            DictionaryIndexBuilder builder = new(DataPath, _indexPaths, progress);
            await builder.BuildAsync(_cancellationTokenSource.Token);
            AppConfiguration config = AppConfiguration.Load(_indexPaths);
            config.DataDirectory = Path.GetFullPath(DataPath);
            config.IndexVersion = AppConfiguration.CurrentIndexVersion;
            config.Save();
            AppendLog("Index successfully created.");
            CloseRequested?.Invoke(this, true);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Indexing canceled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppendLog("Indexing failed.");
            AppendLog(ex.Message);
        }
        finally
        {
            IsRunning = false;
            OnPropertyChanged(nameof(CancelButtonText));
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }, () => !IsRunning && !string.IsNullOrWhiteSpace(DataPath));

    /// <summary>
    /// Cancels indexing when running, or requests dialog close when idle.
    /// </summary>
    public RelayCommand CancelOrCloseCommand => field ??= new(() =>
    {
        if (_cancellationTokenSource is not null)
        {
            _cancellationTokenSource.Cancel();
        }
        else
        {
            CloseRequested?.Invoke(this, false);
        }
    });

    /// <summary>
    /// Appends a message to the log text.
    /// </summary>
    private void AppendLog(string message)
    {
        _log.AppendLine(message);
        LogText = _log.ToString();
    }
}
