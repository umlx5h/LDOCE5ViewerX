using System;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.ViewModels;

/// <summary>
/// View model for the live-apply settings dialog.
/// </summary>
public partial class SettingsDialogViewModel : ObservableObject
{
    /// <summary>
    /// Creates a settings dialog view model that edits application config.
    /// </summary>
    /// <param name="config">Application config edited by the dialog.</param>
    public SettingsDialogViewModel(AppConfiguration config)
    {
        Config = config;
    }

    /// <summary>
    /// Raised when the settings dialog should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Application config edited by the dialog.
    /// </summary>
    public AppConfiguration Config { get; }

    /// <summary>
    /// Resets dictionary content zoom to normal size.
    /// </summary>
    public RelayCommand ResetZoomCommand => field ??= new(() =>
    {
        Config.ZoomPower = 0;
    });

    /// <summary>
    /// Resets the configured search list width to the application default.
    /// </summary>
    public RelayCommand ResetSearchListWidthCommand => field ??= new(() =>
    {
        Config.SearchListWidth = AppConfiguration.DefaultSearchListWidth;
    });

    /// <summary>
    /// Resets the configured search result font size to the application default.
    /// </summary>
    public RelayCommand ResetSearchListBaseFontSizeCommand => field ??= new(() =>
    {
        Config.SearchListBaseFontSize = AppConfiguration.DefaultSearchListBaseFontSize;
    });

    /// <summary>
    /// Resets the configured dictionary content font size to the application default.
    /// </summary>
    public RelayCommand ResetContentBaseFontSizeCommand => field ??= new(() =>
    {
        Config.ContentBaseFontSize = AppConfiguration.DefaultContentBaseFontSize;
    });

    /// <summary>
    /// Resets clipboard timer settings to the application defaults.
    /// </summary>
    public RelayCommand ResetClipboardTimerCommand => field ??= new(() =>
    {
        Config.ClipboardActiveMonitorIntervalMilliseconds =
            AppConfiguration.DefaultClipboardActiveMonitorIntervalMilliseconds;
        Config.ClipboardIdleMonitorIntervalMilliseconds =
            AppConfiguration.DefaultClipboardIdleMonitorIntervalMilliseconds;
        Config.ClipboardIdleThresholdSeconds = AppConfiguration.DefaultClipboardIdleThresholdSeconds;
    });

    /// <summary>
    /// Adds a new web search engine row.
    /// </summary>
    public RelayCommand AddWebSearchSiteCommand => field ??= new(() =>
    {
        Config.WebSearchSites.Add(new WebSearchSite("New Search", "https://www.google.com/search?hl=en&q={query}"));
    });

    /// <summary>
    /// Restores the default web search engine list.
    /// </summary>
    public RelayCommand RestoreDefaultWebSearchSitesCommand => field ??= new(() =>
    {
        Config.RestoreDefaultWebSearchSites();
    });

    /// <summary>
    /// Moves a web search engine row earlier in the list.
    /// </summary>
    public RelayCommand<WebSearchSite> MoveWebSearchSiteUpCommand => field ??= new(site =>
    {
        MoveWebSearchSite(site, -1);
    });

    /// <summary>
    /// Moves a web search engine row later in the list.
    /// </summary>
    public RelayCommand<WebSearchSite> MoveWebSearchSiteDownCommand => field ??= new(site =>
    {
        MoveWebSearchSite(site, 1);
    });

    /// <summary>
    /// Removes a web search engine row.
    /// </summary>
    public RelayCommand<WebSearchSite> RemoveWebSearchSiteCommand => field ??= new(site =>
    {
        if (site is not null)
        {
            Config.WebSearchSites.Remove(site);
        }
    });

    /// <summary>
    /// Closes the settings dialog.
    /// </summary>
    public RelayCommand CloseCommand => field ??= new(() =>
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    });

    private void MoveWebSearchSite(WebSearchSite? site, int offset)
    {
        if (site is null)
        {
            return;
        }

        int index = Config.WebSearchSites.IndexOf(site);
        int targetIndex = index + offset;
        if (index < 0 || targetIndex < 0 || targetIndex >= Config.WebSearchSites.Count)
        {
            return;
        }

        Config.WebSearchSites.Move(index, targetIndex);
    }
}
