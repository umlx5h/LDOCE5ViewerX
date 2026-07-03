using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.Services;
using LDOCE5ViewerX.ViewModels;

namespace LDOCE5ViewerX.Views;

/// <summary>
/// Main application window.
/// </summary>
public partial class MainWindow : Window
{
    private bool _indexChecked;
    private AdvancedSearchDialog? _advancedSearchDialog;
    private SettingsDialog? _settingsDialog;
    private HelpDialog? _helpDialog;

    private readonly DispatcherTimer _clipboardMonitorTimer;
    private bool _clipboardPollInProgress;
    private bool _clipboardMonitoringWasEnabled;
    private bool _clipboardMonitoringSuspended;
    private DateTimeOffset? _lastClipboardChangeTime;
    private string? _lastClipboardText;

    /// <summary>
    /// Creates the main application window.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        ApplyPlatformShortcuts();
        // TODO: want to use clipboard changed event instead of timer-based polling
        // https://github.com/AvaloniaUI/Avalonia/issues/12618
        _clipboardMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AppConfiguration.DefaultClipboardActiveMonitorIntervalMilliseconds),
        };
        _clipboardMonitorTimer.Tick += OnClipboardMonitorTick;
#if DEBUG
        AddDebugMenu();
#endif
        EntryDocument.ResourceRequested += OnEntryDocumentResourceRequested;
        EntryDocument.AddHandler(PointerWheelChangedEvent, OnEntryDocumentPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void ApplyPlatformShortcuts()
    {
        if (!PlatformShortcuts.IsMacOS)
        {
            return;
        }

        foreach (KeyBinding keyBinding in KeyBindings)
        {
            if (keyBinding.Gesture is KeyGesture gesture)
            {
                keyBinding.Gesture = PlatformShortcuts.ToPrimaryGesture(gesture);
            }
        }

        foreach (MenuItem menuItem in EnumerateMenuItems(MainMenu))
        {
            if (menuItem.InputGesture is KeyGesture gesture)
            {
                menuItem.InputGesture = PlatformShortcuts.ToPrimaryGesture(gesture);
            }
        }

        static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl itemsControl)
        {
            foreach (object? item in itemsControl.Items)
            {
                if (item is not MenuItem menuItem)
                {
                    continue;
                }

                yield return menuItem;
                foreach (MenuItem childItem in EnumerateMenuItems(menuItem))
                {
                    yield return childItem;
                }
            }
        }
    }

    /// <summary>
    /// Shows the index creation dialog on startup when no usable index exists.
    /// </summary>
    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Config.PropertyChanged += ConfigPropertyChanged;
            viewModel.SettingsRequested += OnSettingsRequested;
            ApplySearchListWidth(viewModel.Config.SearchListWidth);
        }

        SyncClipboardMonitorTimer();
        if (_indexChecked)
        {
            return;
        }

        _indexChecked = true;
        if (DataContext is not MainWindowViewModel startupViewModel || startupViewModel.HasUsableIndex)
        {
            return;
        }

        await ShowIndexerDialogAsync(closeWhenCanceled: true);
    }

    private void ConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppConfiguration.IsClipboardMonitoringEnabled))
        {
            SyncClipboardMonitorTimer();
        }
        else if (e.PropertyName is nameof(AppConfiguration.ClipboardActiveMonitorIntervalMilliseconds)
            or nameof(AppConfiguration.ClipboardIdleMonitorIntervalMilliseconds)
            or nameof(AppConfiguration.ClipboardIdleThresholdSeconds))
        {
            ResetClipboardMonitorState();
        }
        else if (e.PropertyName == nameof(AppConfiguration.SearchListWidth)
            && sender is AppConfiguration config)
        {
            ApplySearchListWidth(config.SearchListWidth);
        }
    }

    private void ApplySearchListWidth(double width)
    {
        MainContentGrid.ColumnDefinitions[0].Width = new GridLength(width);
    }

    private void SyncClipboardMonitorTimer()
    {
        bool shouldRun = DataContext is MainWindowViewModel viewModel
            && viewModel.Config.IsClipboardMonitoringEnabled;

        if (shouldRun)
        {
            ResetClipboardMonitorState();
            _clipboardMonitorTimer.Start();
            return;
        }

        _clipboardMonitorTimer.Stop();
        ResetClipboardMonitorState();
    }

    private void ResetClipboardMonitorState()
    {
        _clipboardMonitoringWasEnabled = false;
        _lastClipboardChangeTime = null;
        _lastClipboardText = null;
        TimeSpan activeInterval = GetClipboardActiveMonitorInterval();
        if (_clipboardMonitorTimer.Interval != activeInterval)
        {
            _clipboardMonitorTimer.Interval = activeInterval;
        }
    }

    private void MarkClipboardChanged(DateTimeOffset changeTime)
    {
        _lastClipboardChangeTime = changeTime;
        TimeSpan activeInterval = GetClipboardActiveMonitorInterval();
        if (_clipboardMonitorTimer.Interval != activeInterval)
        {
            _clipboardMonitorTimer.Interval = activeInterval;
        }
    }

    private void UpdateClipboardMonitorInterval(DateTimeOffset pollTime)
    {
        if (_lastClipboardChangeTime is null)
        {
            _clipboardMonitorTimer.Interval = GetClipboardActiveMonitorInterval();
            return;
        }

        TimeSpan idleInterval = GetClipboardIdleMonitorInterval();
        if (pollTime - _lastClipboardChangeTime.Value > GetClipboardIdleThreshold()
            && _clipboardMonitorTimer.Interval != idleInterval)
        {
            _clipboardMonitorTimer.Interval = idleInterval;
        }
    }

    private TimeSpan GetClipboardActiveMonitorInterval()
    {
        return DataContext is MainWindowViewModel viewModel
            ? TimeSpan.FromMilliseconds(viewModel.Config.ClipboardActiveMonitorIntervalMilliseconds)
            : TimeSpan.FromMilliseconds(AppConfiguration.DefaultClipboardActiveMonitorIntervalMilliseconds);
    }

    private TimeSpan GetClipboardIdleMonitorInterval()
    {
        return DataContext is MainWindowViewModel viewModel
            ? TimeSpan.FromMilliseconds(viewModel.Config.ClipboardIdleMonitorIntervalMilliseconds)
            : TimeSpan.FromMilliseconds(AppConfiguration.DefaultClipboardIdleMonitorIntervalMilliseconds);
    }

    private TimeSpan GetClipboardIdleThreshold()
    {
        return DataContext is MainWindowViewModel viewModel
            ? TimeSpan.FromSeconds(viewModel.Config.ClipboardIdleThresholdSeconds)
            : TimeSpan.FromSeconds(AppConfiguration.DefaultClipboardIdleThresholdSeconds);
    }

    private async void OnClipboardMonitorTick(object? sender, EventArgs e)
    {
        if (_clipboardPollInProgress)
        {
            return;
        }

        _clipboardPollInProgress = true;
        try
        {
            await PollClipboardAsync();
        }
        finally
        {
            _clipboardPollInProgress = false;
        }
    }

    private async Task PollClipboardAsync()
    {
        if (DataContext is not MainWindowViewModel viewModel
            || _clipboardMonitoringSuspended
            || IsActive)
        {
            ResetClipboardMonitorState();
            return;
        }

        TopLevel? topLevel = GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            return;
        }

        string? clipboardText;
        try
        {
            clipboardText = await topLevel.Clipboard.TryGetTextAsync();
        }
        catch (Exception ex)
        {
            viewModel.ContentStatus = $"Clipboard monitoring unavailable: {ex.Message}";
            return;
        }

        DateTimeOffset pollTime = DateTimeOffset.UtcNow;
        if (!_clipboardMonitoringWasEnabled)
        {
            _clipboardMonitoringWasEnabled = true;
            _lastClipboardText = clipboardText;
            MarkClipboardChanged(pollTime);
            return;
        }

        if (string.Equals(clipboardText, _lastClipboardText, StringComparison.Ordinal))
        {
            UpdateClipboardMonitorInterval(pollTime);
            return;
        }

        _lastClipboardText = clipboardText;
        MarkClipboardChanged(pollTime);
        if (clipboardText is null)
        {
            return;
        }

        viewModel.TrySearchClipboardText(clipboardText);
    }

    /// <summary>
    /// Opens the indexer dialog and reloads indexes when creation succeeds.
    /// </summary>
    private async void OnCreateIndexClicked(object? sender, RoutedEventArgs e)
    {
        await ShowIndexerDialogAsync(closeWhenCanceled: false);
    }

    /// <summary>
    /// Opens the modeless advanced-search filter window.
    /// </summary>
    private void OnAdvancedSearchClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (_advancedSearchDialog is not null)
        {
            _advancedSearchDialog.Activate();
            return;
        }

        AdvancedSearchDialog dialog = new();
        dialog.FiltersApplied += OnAdvancedSearchFiltersApplied;
        dialog.Closed += OnAdvancedSearchDialogClosed;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _advancedSearchDialog = dialog;
        dialog.Show(this);
    }

    private void OnAdvancedSearchFiltersApplied(object? sender, SearchFilterQuery filters)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ApplyAdvancedSearchFilters(filters);
        }
    }

    private void OnAdvancedSearchDialogClosed(object? sender, EventArgs e)
    {
        if (_advancedSearchDialog is not null)
        {
            _advancedSearchDialog.FiltersApplied -= OnAdvancedSearchFiltersApplied;
            _advancedSearchDialog.Closed -= OnAdvancedSearchDialogClosed;
            _advancedSearchDialog = null;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ClearAdvancedSearchFilters();
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        ShowSettingsDialog();
    }

    internal void ShowSettingsDialog()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (_settingsDialog is not null)
        {
            _settingsDialog.Activate();
            return;
        }

        SettingsDialog dialog = new(viewModel.Config)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.Closed += OnSettingsDialogClosed;
        _settingsDialog = dialog;
        dialog.Show(this);
    }

    private void OnSettingsDialogClosed(object? sender, EventArgs e)
    {
        if (_settingsDialog is not null)
        {
            _settingsDialog.Closed -= OnSettingsDialogClosed;
            _settingsDialog = null;
        }
    }

    /// <summary>
    /// Copies the current entry word to the clipboard.
    /// </summary>
    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        await CopyCurrentEntryWordAsync();
    }

    /// <summary>
    /// Copies one search result's entry text to the clipboard.
    /// </summary>
    private async void OnCopySearchResultClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SearchResultItemViewModel result })
        {
            return;
        }

        await CopyTextToClipboardAsync(result.Title, $"Copied {result.Title}.");
    }

    /// <summary>
    /// Opens the shortcut help window.
    /// </summary>
    private void OnHelpClicked(object? sender, RoutedEventArgs e)
    {
        ShowHelpDialog();
    }

    private void ShowHelpDialog()
    {
        if (_helpDialog is not null)
        {
            _helpDialog.Activate();
            return;
        }

        HelpDialog dialog = new()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.Closed += OnHelpDialogClosed;
        _helpDialog = dialog;
        dialog.Show();
    }

    private void OnHelpDialogClosed(object? sender, EventArgs e)
    {
        if (_helpDialog is not null)
        {
            _helpDialog.Closed -= OnHelpDialogClosed;
            _helpDialog = null;
        }
    }

    private void OnQuitClicked(object? sender, RoutedEventArgs e)
    {
        QuitApplication();
    }

    private void QuitApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Close();
    }

#if DEBUG
    /// <summary>
    /// Adds debug-only menu items for inspecting the current dictionary content.
    /// </summary>
    private void AddDebugMenu()
    {
        MenuItem debugMenu = new()
        {
            Header = "_Debug",
        };
        MenuItem copyEntryItem = new()
        {
            Header = "Copy Entry",
        };
        copyEntryItem.Click += OnCopyEntryClicked;
        MenuItem copyRawXmlItem = new()
        {
            Header = "Copy Raw XML",
        };
        copyRawXmlItem.Click += OnCopyRawXmlClicked;

        debugMenu.Items.Add(copyEntryItem);
        debugMenu.Items.Add(copyRawXmlItem);

        int helpMenuIndex = Math.Max(0, MainMenu.Items.Count - 1);
        MainMenu.Items.Insert(helpMenuIndex, debugMenu);
    }

    /// <summary>
    /// Copies the currently displayed entry title and path as a temporary debugging aid.
    /// </summary>
    private async void OnCopyEntryClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            string entryInfo = viewModel.FormatCurrentEntryInfo();
            TopLevel? topLevel = GetTopLevel(this);
            if (topLevel?.Clipboard is null)
            {
                viewModel.ContentStatus = "Clipboard is not available.";
                return;
            }

            await topLevel.Clipboard.SetTextAsync(entryInfo);
            viewModel.ContentStatus = $"Copied entry info for {viewModel.CurrentContentPath}.";
        }
        catch (InvalidOperationException ex)
        {
            viewModel.ContentStatus = $"Unable to copy entry info: {ex.Message}";
        }
    }

    /// <summary>
    /// Copies raw XML for the currently displayed content as a temporary debugging aid.
    /// </summary>
    private async void OnCopyRawXmlClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            string rawXml = viewModel.LoadCurrentRawXml();
            TopLevel? topLevel = GetTopLevel(this);
            if (topLevel?.Clipboard is null)
            {
                viewModel.ContentStatus = "Clipboard is not available.";
                return;
            }

            await topLevel.Clipboard.SetTextAsync(rawXml);
            viewModel.ContentStatus = $"Copied raw XML for {viewModel.CurrentContentPath}.";
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or System.IO.IOException
            or System.IO.InvalidDataException
            or UnauthorizedAccessException)
        {
            viewModel.ContentStatus = $"Unable to copy raw XML: {ex.Message}";
        }
    }
#endif

    /// <summary>
    /// Focuses the find text box.
    /// </summary>
    private void OnFindClicked(object? sender, RoutedEventArgs e)
    {
        ShowFindBar();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && PlatformShortcuts.IsPrimaryModifierOnly(e.KeyModifiers))
        {
            if (HasSelectedTextToCopy(e.Source as Control))
            {
                return;
            }

            if (DataContext is MainWindowViewModel viewModel && viewModel.CanCopyCurrentEntryWord)
            {
                _ = CopyCurrentEntryWordAsync();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None)
        {
            ShowHelpDialog();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && PlatformShortcuts.IsPrimaryModifierOnly(e.KeyModifiers))
        {
            ShowFindBar();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.L && PlatformShortcuts.IsPrimaryModifierOnly(e.KeyModifiers))
        {
            FocusSearchTextBox();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && FindBar.IsVisible)
        {
            CloseFindBar();
            e.Handled = true;
            return;
        }

        if (HandleSpellSuggestionShortcut(e))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter
            && IsSearchResultNavigationKey(e)
            && IsFindTextBoxSource(e.Source as Control))
        {
            MoveFindHit(e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (TryGetCursorSearchResultNavigationOffset(e, out int cursorOffset)
            && ShouldHandleSearchResultCursorNavigation(e.Source as Control))
        {
            NavigateSearchResult(cursorOffset, IsSearchTextBoxSource(e.Source as Control));
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter
            || !IsSearchResultNavigationKey(e)
            || !ShouldHandleSearchResultNavigation(e.Source as Control))
        {
            return;
        }

        int offset = e.KeyModifiers == KeyModifiers.Shift ? -1 : 1;
        NavigateSearchResult(offset, IsSearchTextBoxSource(e.Source as Control));
        e.Handled = true;
    }

    private async Task CopyCurrentEntryWordAsync()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        string? word = viewModel.GetCurrentEntryWordForCopy();
        if (word is null)
        {
            return;
        }

        await CopyTextToClipboardAsync(word, $"Copied {word}.");
    }

    private async Task CopyTextToClipboardAsync(string text, string successStatus)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        TopLevel? topLevel = GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            viewModel.ContentStatus = "Clipboard is not available.";
            return;
        }

        try
        {
            await topLevel.Clipboard.SetTextAsync(text);
            viewModel.ContentStatus = successStatus;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            viewModel.ContentStatus = $"Unable to copy text: {ex.Message}";
        }
    }

    private static bool IsSearchResultNavigationKey(KeyEventArgs e)
    {
        return e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift;
    }

    private static bool TryGetCursorSearchResultNavigationOffset(KeyEventArgs e, out int offset)
    {
        if (e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Key == Key.Down)
            {
                offset = 1;
                return true;
            }

            if (e.Key == Key.Up)
            {
                offset = -1;
                return true;
            }
        }

        if (PlatformShortcuts.IsPrimaryModifierOnly(e.KeyModifiers))
        {
            if (e.Key is Key.J or Key.N)
            {
                offset = 1;
                return true;
            }

            if (e.Key is Key.K or Key.P)
            {
                offset = -1;
                return true;
            }
        }

        offset = 0;
        return false;
    }

    private bool ShouldHandleSearchResultNavigation(Control? source)
    {
        if (source is null)
        {
            return true;
        }

        TextBox? textBox = source as TextBox ?? source.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
        if (textBox is not null && !ReferenceEquals(textBox, SearchTextBox))
        {
            return false;
        }

        return
            source is not Button
            and not ComboBox
            and not ComboBoxItem
            and not MenuItem;
    }

    private bool ShouldHandleSearchResultCursorNavigation(Control? source)
    {
        if (source is null)
        {
            return false;
        }

        return IsSearchTextBoxSource(source) || IsResultsListBoxSource(source);
    }

    private static bool HasSelectedTextToCopy(Control? source)
    {
        if (source is null)
        {
            return false;
        }

        SelectableTextBlock? textBlock = source as SelectableTextBlock;
        if (!string.IsNullOrEmpty(textBlock?.SelectedText))
        {
            return true;
        }

        TextBox? textBox = source as TextBox;
        return !string.IsNullOrEmpty(textBox?.SelectedText);
    }

    private bool IsSearchTextBoxSource(Control? source)
    {
        if (source is null)
        {
            return false;
        }

        return ReferenceEquals(source, SearchTextBox);
    }

    private bool IsResultsListBoxSource(Control? source)
    {
        if (source is null)
        {
            return false;
        }

        return ReferenceEquals(source, ResultsListBox);
    }

    private bool IsFindTextBoxSource(Control? source)
    {
        if (source is null || source is not TextBox textBox)
        {
            return false;
        }

        return ReferenceEquals(textBox, FindTextBox);
    }

    private void FocusSearchTextBox()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void ShowFindBar()
    {
        FindBar.IsVisible = true;
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    private void CloseFindBar()
    {
        FindBar.IsVisible = false;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.FindText = string.Empty;
        }
    }

    private bool HandleSpellSuggestionShortcut(KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsSpellSuggestionPopupOpen)
        {
            return false;
        }

        Control? source = e.Source as Control;
        if (!IsSearchTextBoxSource(source))
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.IsSpellSuggestionPopupOpen = false;
            return true;
        }

        if ((e.Key == Key.Down || e.Key == Key.Up) && e.KeyModifiers == KeyModifiers.None)
        {
            MoveSpellSuggestionSelection(e.Key == Key.Down ? 1 : -1);
            return true;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (SpellSuggestionListBox.SelectedItem == null && viewModel.SpellSuggestions.Count > 0)
            {
                // select first entry
                SpellSuggestionListBox.SelectedIndex = 0;
            }
            ApplySelectedSpellSuggestion();
            return true;
        }

        return false;
    }

    private void MoveSpellSuggestionSelection(int offset)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SpellSuggestions.Count == 0)
        {
            return;
        }

        int selectedIndex = SpellSuggestionListBox.SelectedIndex;
        int targetIndex = selectedIndex < 0
            ? (offset > 0 ? 0 : viewModel.SpellSuggestions.Count - 1)
            : Math.Clamp(selectedIndex + offset, 0, viewModel.SpellSuggestions.Count - 1);

        SpellSuggestionListBox.SelectedIndex = targetIndex;
        if (SpellSuggestionListBox.SelectedItem is not null)
        {
            SpellSuggestionListBox.ScrollIntoView(SpellSuggestionListBox.SelectedItem);
        }
    }

    private void ApplySelectedSpellSuggestion()
    {
        if (DataContext is not MainWindowViewModel viewModel
            || SpellSuggestionListBox.SelectedItem is not string suggestion)
        {
            return;
        }

        viewModel.ApplySpellSuggestionCommand.Execute(suggestion);
        SearchTextBox.Focus();
        SearchTextBox.CaretIndex = SearchTextBox.Text?.Length ?? 0;
    }

    private void NavigateSearchResult(int offset, bool fromSearchTextBox)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        bool shouldRunSearch = !viewModel.SearchResultsMatchCurrentQuery
            || (viewModel.SearchResults.Count == 0 && !fromSearchTextBox);
        if (shouldRunSearch && viewModel.RunModeSearchCommand.CanExecute(null))
        {
            viewModel.RunModeSearchCommand.Execute(null);
        }

        if (viewModel.SelectSearchResultRelative(offset))
        {
            ResultsListBox.Focus();
        }
    }

    /// <summary>
    /// Moves to the next in-page find hit.
    /// </summary>
    private void OnFindNextClicked(object? sender, RoutedEventArgs e)
    {
        MoveFindHit(1);
    }

    /// <summary>
    /// Moves to the previous in-page find hit.
    /// </summary>
    private void OnFindPreviousClicked(object? sender, RoutedEventArgs e)
    {
        MoveFindHit(-1);
    }

    private void MoveFindHit(int offset)
    {
        if (offset < 0)
        {
            EntryDocument.PreviousFindHit();
        }
        else
        {
            EntryDocument.NextFindHit();
        }
    }

    /// <summary>
    /// Opens the back-history menu for the toolbar back button context gesture.
    /// </summary>
    private void OnBackButtonContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Button button)
        {
            e.Handled = ShowNavigationHistoryMenu(button, back: true);
        }
    }

    /// <summary>
    /// Opens the forward-history menu for the toolbar forward button context gesture.
    /// </summary>
    private void OnForwardButtonContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Button button)
        {
            e.Handled = ShowNavigationHistoryMenu(button, back: false);
        }
    }

    private bool ShowNavigationHistoryMenu(Button button, bool back)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return false;
        }

        IReadOnlyList<ContentHistoryMenuItemViewModel> entries =
            back ? viewModel.BackHistoryMenuEntries : viewModel.ForwardHistoryMenuEntries;
        if (entries.Count == 0)
        {
            return false;
        }

        List<MenuItem> menuItems = [];
        foreach (ContentHistoryMenuItemViewModel entry in entries)
        {
            menuItems.Add(new MenuItem
            {
                Header = entry.DisplayText,
                Command = viewModel.NavigateHistoryEntryCommand,
                CommandParameter = entry,
            });
        }

        MenuFlyout flyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            ItemsSource = menuItems,
        };
        flyout.ShowAt(button, showAtPointer: true);
        return true;
    }

    /// <summary>
    /// Keeps the selected search row visible when history navigation changes it.
    /// </summary>
    private void OnResultsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is not null)
        {
            ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
        }
    }

    /// <summary>
    /// Applies the currently clicked spelling suggestion.
    /// </summary>
    private void OnSpellSuggestionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ApplySelectedSpellSuggestion();
        e.Handled = true;
    }

    /// <summary>
    /// Clears the find query.
    /// </summary>
    private void OnFindCloseClicked(object? sender, RoutedEventArgs e)
    {
        CloseFindBar();
    }

    /// <summary>
    /// Loads dictionary image bytes for the native document view.
    /// </summary>
    private void OnEntryDocumentResourceRequested(object? sender, DictionaryResourceRequestedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            e.Data = viewModel.LoadResource(e.Resource);
        }
    }

    /// <summary>
    /// Handles content zooming through Ctrl + mouse wheel.
    /// </summary>
    private void OnEntryDocumentPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!PlatformShortcuts.HasPrimaryModifier(e.KeyModifiers)
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Delta.Y > 0)
        {
            viewModel.ZoomInCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Delta.Y < 0)
        {
            viewModel.ZoomOutCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles mouse Back and Forward side buttons.
    /// </summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        PointerUpdateKind updateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (updateKind == PointerUpdateKind.XButton1Released
            && viewModel.NavigateBackCommand.CanExecute(null))
        {
            viewModel.NavigateBackCommand.Execute(null);
            e.Handled = true;
        }
        else if (updateKind == PointerUpdateKind.XButton2Released
            && viewModel.NavigateForwardCommand.CanExecute(null))
        {
            viewModel.NavigateForwardCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Opens the indexer dialog and optionally closes the app when startup indexing is canceled.
    /// </summary>
    private async Task ShowIndexerDialogAsync(bool closeWhenCanceled)
    {
        MainWindowViewModel? viewModel = DataContext as MainWindowViewModel;
        viewModel?.UnloadIndexes();

        bool? succeeded;
        _clipboardMonitoringSuspended = true;
        try
        {
            IndexerDialog dialog = new();
            succeeded = await dialog.ShowDialog<bool?>(this);
        }
        finally
        {
            _clipboardMonitoringSuspended = false;
        }

        viewModel = DataContext as MainWindowViewModel;
        viewModel?.ReloadIndexes();
        if (succeeded != true && closeWhenCanceled && viewModel is not null && !viewModel.HasUsableIndex)
        {
            Close();
        }
    }

    /// <summary>
    /// Releases view-model resources when the main window closes.
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        _clipboardMonitorTimer.Stop();
        _clipboardMonitorTimer.Tick -= OnClipboardMonitorTick;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Config.PropertyChanged -= ConfigPropertyChanged;
            viewModel.SettingsRequested -= OnSettingsRequested;
        }

        EntryDocument.ResourceRequested -= OnEntryDocumentResourceRequested;
        EntryDocument.RemoveHandler(PointerWheelChangedEvent, OnEntryDocumentPointerWheelChanged);
        RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        RemoveHandler(PointerReleasedEvent, OnPointerReleased);
        if (_advancedSearchDialog is not null)
        {
            _advancedSearchDialog.FiltersApplied -= OnAdvancedSearchFiltersApplied;
            _advancedSearchDialog.Closed -= OnAdvancedSearchDialogClosed;
            _advancedSearchDialog.Close();
            _advancedSearchDialog = null;
        }

        if (_settingsDialog is not null)
        {
            _settingsDialog.Closed -= OnSettingsDialogClosed;
            _settingsDialog.Close();
            _settingsDialog = null;
        }

        if (_helpDialog is not null)
        {
            _helpDialog.Closed -= OnHelpDialogClosed;
            _helpDialog.Close();
            _helpDialog = null;
        }

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
