using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string ApplicationTitle = "LDOCE5 Viewer X";
    private const int IncrementalSearchLimit = 500;
    private const int FullTextSearchLimit = 10000;
    private const int FullTextDelayMilliseconds = 100;
    private const int SpellSuggestionDelayMilliseconds = 200;
    private const int SpellSuggestionLimit = 5;
    private const int ModeSearchLimit = 3000;
    private const int MaximumContentHistoryEntries = 100;
    private const int MaximumVisibleContentHistoryEntries = 20;
    private const int MaximumClipboardSearchTextLength = 100;
    private const string GitHubUrl = "https://github.com/umlx5h/LDOCE5ViewerX";

    private readonly IndexPaths _indexPaths;
    private readonly bool _dispatchUiUpdates;
    private readonly Func<CancellationToken, Task> _searchDebounceDelay;
    private readonly Func<CancellationToken, Task> _spellSuggestionDelay;
    private readonly IAudioPlayer _audioPlayer;
    private readonly string? _productVersion;

    private IncrementalSearchIndex? _incrementalSearchIndex;
    private DictionaryContentService? _dictionaryContentService;
    private FullTextSearcher? _fullTextHeadwordPhraseSearcher;
    private FullTextSearcher? _fullTextDefinitionExampleSearcher;
    private CancellationTokenSource? _fullTextSearchCancellation;
    private CancellationTokenSource? _spellSuggestionCancellation;
    private string? _dataDirectory;
    private bool _isIndexVersionCurrent;
    private readonly List<ContentHistoryEntry> _contentHistory = [];
    private int _contentHistoryIndex = -1;
    private bool _suppressSelectedResultNavigation;
    private bool _suppressSearchRequests;
    private SearchModeOption? _visibleResultsSearchMode;
    private string _visibleResultsQuery = string.Empty;
    private SearchFilterQuery _activeSearchFilters = SearchFilterQuery.Empty;
    private bool _suppressConfigSave;
    private bool _isUserConfigDirty;
    private AppConfiguration _loadedConfig = null!;

    private readonly record struct FullTextSearchOutcome(
        IReadOnlyList<FullTextSearchResult> Results,
        string? UnavailableMessage);

    /// <summary>
    /// Raised after the active search request has finished updating the visible result list.
    /// </summary>
    public event EventHandler? SearchCompleted;

    /// <summary>
    /// Raised when the settings dialog should be shown.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Creates the main window view model with default app data paths.
    /// </summary>
    public MainWindowViewModel()
        : this(new IndexPaths())
    {
    }

    /// <summary>
    /// Creates the main window view model with caller-provided app data paths.
    /// </summary>
    /// <param name="indexPaths">Filesystem paths used by generated indexes.</param>
    /// <param name="dispatchUiUpdates">Whether delayed searches should dispatch result updates to the UI thread.</param>
    /// <param name="searchDebounceDelay">Delay used before full-index searches run.</param>
    /// <param name="spellSuggestionDelay">Delay used before spelling suggestions run.</param>
    /// <param name="audioPlayer">Optional audio backend for pronunciation playback.</param>
    /// <param name="productVersion">Optional product version override used by tests.</param>
    public MainWindowViewModel(
        IndexPaths indexPaths,
        bool dispatchUiUpdates = true,
        Func<CancellationToken, Task>? searchDebounceDelay = null,
        Func<CancellationToken, Task>? spellSuggestionDelay = null,
        IAudioPlayer? audioPlayer = null,
        string? productVersion = null)
    {
        _indexPaths = indexPaths;
        _dispatchUiUpdates = dispatchUiUpdates;
        _audioPlayer = audioPlayer ?? AudioPlayerFactory.CreateDefault();
        _productVersion = productVersion;
        _searchDebounceDelay =
            searchDebounceDelay ?? (token => Task.Delay(FullTextDelayMilliseconds, token));
        _spellSuggestionDelay =
            spellSuggestionDelay ?? (token => Task.Delay(SpellSuggestionDelayMilliseconds, token));
        Config = new(indexPaths);
        Config.PropertyChanged += OnConfigPropertyChanged;
        SearchModes =
        [
            new("All", "Incremental search", usesDefinitionExampleIndex: false, [], FullTextSearchLimit),
            new("Headwords", "Headword search", usesDefinitionExampleIndex: false, ["hm"], FullTextSearchLimit),
            new("Phrasal Verbs", "Phrasal verb search", usesDefinitionExampleIndex: false, ["hp"], ModeSearchLimit),
            new("Collocations", "Collocation search", usesDefinitionExampleIndex: false, ["p"], ModeSearchLimit),
            new("Phrases", "Phrase search", usesDefinitionExampleIndex: false, ["pl"], ModeSearchLimit),
            new("Definitions", "Definition search", usesDefinitionExampleIndex: true, ["d"], ModeSearchLimit),
            new("Examples", "Example search", usesDefinitionExampleIndex: true, ["e"], ModeSearchLimit),
        ];
        SelectedSearchMode = SearchModes[0];
        ReloadIndexes();
    }

    /// <summary>
    /// Reloads generated indexes from the Avalonia app index directory.
    /// </summary>
    public void ReloadIndexes()
    {
        CloseIndexes();

        string indexPath = _indexPaths.IncrementalPath;
        AppConfiguration config = AppConfiguration.Load(_indexPaths);
        _loadedConfig = config.CreateSnapshot();
        _suppressConfigSave = true;
        try
        {
            if (_isUserConfigDirty)
            {
                Config.DataDirectory = config.DataDirectory;
                Config.IndexVersion = config.IndexVersion;
            }
            else
            {
                Config.CopyPersistedValuesFrom(config);
            }
        }
        finally
        {
            _suppressConfigSave = false;
        }

        App.ApplyThemeMode(Config.ThemeMode);

        _dataDirectory = Config.DataDirectory;
        _isIndexVersionCurrent = Config.IndexVersion == AppConfiguration.CurrentIndexVersion;
        if (!_isIndexVersionCurrent)
        {
            IndexStatus = string.IsNullOrWhiteSpace(Config.IndexVersion)
                ? $"Index version is not recorded. Current version is {AppConfiguration.CurrentIndexVersion}; recreate the index."
                : $"Index version {Config.IndexVersion} does not match current version {AppConfiguration.CurrentIndexVersion}; recreate the index.";
            ContentTitle = "Index rebuild required";
            ContentStatus = IndexStatus;
            ContentText = "Create the index again before searching.";
            ContentDocument = DictionaryDocument.FromPlainText(ContentText);
            OnPropertyChanged(nameof(HasGeneratedIndex));
            OnPropertyChanged(nameof(HasUsableIndex));
            RunModeSearchCommand.NotifyCanExecuteChanged();
            return;
        }

        try
        {
            if (File.Exists(indexPath))
            {
                _incrementalSearchIndex = IncrementalSearchIndex.Open(indexPath);
                IndexStatus = $"Incremental index loaded: {indexPath}";
            }
            else
            {
                IndexStatus = $"Incremental index not found: {indexPath}";
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            IndexStatus = $"Incremental index unavailable: {ex.Message}";
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(_dataDirectory)
                && Directory.Exists(_dataDirectory)
                && File.Exists(_indexPaths.FilemapPath))
            {
                _dictionaryContentService = new DictionaryContentService(
                    _indexPaths,
                    _dataDirectory,
                    () => Config.WebSearchSites,
                    () => Config.WebSearchAssetBoxMode);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ContentText = $"Dictionary content service unavailable: {ex.Message}";
        }

        try
        {
            List<string> unavailableMessages = [];
            if (Directory.Exists(_indexPaths.FullTextHeadwordPhrasePath))
            {
                _fullTextHeadwordPhraseSearcher = new FullTextSearcher(
                    _indexPaths.FullTextHeadwordPhrasePath,
                    File.Exists(_indexPaths.VariationsPath) ? _indexPaths.VariationsPath : null);
            }
            else
            {
                unavailableMessages.Add("headword/phrase full-text index not found");
            }

            if (Directory.Exists(_indexPaths.FullTextDefinitionExamplePath))
            {
                _fullTextDefinitionExampleSearcher = new FullTextSearcher(
                    _indexPaths.FullTextDefinitionExamplePath,
                    File.Exists(_indexPaths.VariationsPath) ? _indexPaths.VariationsPath : null);
            }
            else
            {
                unavailableMessages.Add("definition/example full-text index not found");
            }

            if (unavailableMessages.Count > 0)
            {
                IndexStatus += $" {string.Join("; ", unavailableMessages)}.";
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            IndexStatus += $" Full-text index unavailable: {ex.Message}";
        }

        OnPropertyChanged(nameof(HasGeneratedIndex));
        OnPropertyChanged(nameof(HasUsableIndex));
        RunModeSearchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Closes generated indexes without reloading them.
    /// </summary>
    public void UnloadIndexes()
    {
        CloseIndexes();
        SearchResults.Clear();
        SelectedResult = null;
        IndexStatus = "Generated indexes unloaded.";
        ResultCountText = "0";
        ContentTitle = ApplicationTitle;
        ContentStatus = IndexStatus;
        ContentText = "Create the index again before searching.";
        OnPropertyChanged(nameof(HasGeneratedIndex));
        OnPropertyChanged(nameof(HasUsableIndex));
        RunModeSearchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Releases loaded index resources.
    /// </summary>
    public void Dispose()
    {
        SaveUserConfigIfNeeded();
        Config.PropertyChanged -= OnConfigPropertyChanged;
        CloseIndexes();
        _audioPlayer.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Closes all loaded index resources and clears their references.
    /// </summary>
    private void CloseIndexes()
    {
        _fullTextSearchCancellation?.Cancel();
        _fullTextSearchCancellation?.Dispose();
        _fullTextSearchCancellation = null;
        CancelSpellSuggestionRequest(clearSuggestions: true);
        _incrementalSearchIndex?.Dispose();
        _dictionaryContentService?.Dispose();
        _fullTextHeadwordPhraseSearcher?.Dispose();
        _fullTextDefinitionExampleSearcher?.Dispose();
        _incrementalSearchIndex = null;
        _dictionaryContentService = null;
        _fullTextHeadwordPhraseSearcher = null;
        _fullTextDefinitionExampleSearcher = null;
        _dataDirectory = null;
        _isIndexVersionCurrent = false;
    }

    /// <summary>
    /// Returns whether the generated app index is available for search.
    /// </summary>
    public bool HasGeneratedIndex => _isIndexVersionCurrent && _incrementalSearchIndex is not null;

    /// <summary>
    /// Returns whether indexes and dictionary source data are available.
    /// </summary>
    public bool HasUsableIndex => HasGeneratedIndex && _dictionaryContentService is not null;

    /// <summary>
    /// Gets the persisted application config edited by menus and settings dialogs.
    /// </summary>
    public AppConfiguration Config { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunModeSearchCommand))]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunModeSearchCommand))]
    public partial SearchModeOption SelectedSearchMode { get; set; }

    [ObservableProperty]
    public partial SearchResultItemViewModel? SelectedResult { get; set; }

    [ObservableProperty]
    public partial string IndexStatus { get; set; } = "Incremental index not loaded";

    [ObservableProperty]
    public partial string ResultCountText { get; set; } = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(CanCopyCurrentEntryWord))]
    public partial string ContentTitle { get; set; } = ApplicationTitle;

    /// <summary>
    /// Gets the main window title text.
    /// </summary>
    public string WindowTitle => FormatWindowTitle(ContentTitle);

    [ObservableProperty]
    public partial string ContentStatus { get; set; } =
        "Type in the search box to search the generated index.";

    [ObservableProperty]
    public partial string ContentText { get; set; } =
        "Search for a word, then select a result to display plain text content.";

    [ObservableProperty]
    public partial DictionaryDocument ContentDocument { get; set; } =
        DictionaryDocument.FromPlainText("Search for a word, then select a result to display rich dictionary content.");

    [ObservableProperty]
    public partial string? ContentAnchor { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCurrentPronunciationCommand))]
    public partial string? CurrentContentPath { get; set; }

    [ObservableProperty]
    public partial string FindText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FindStatus { get; set; } = string.Empty;

    /// <summary>
    /// Gets whether content navigation can go back.
    /// </summary>
    public bool CanNavigateBack => _contentHistoryIndex > 0;

    /// <summary>
    /// Gets whether content navigation can go forward.
    /// </summary>
    public bool CanNavigateForward => _contentHistoryIndex >= 0 && _contentHistoryIndex < _contentHistory.Count - 1;

    /// <summary>
    /// Gets the current number of content history entries.
    /// </summary>
    public int ContentHistoryCount => _contentHistory.Count;

    /// <summary>
    /// Gets the previous content history entries shown in the back navigation menu.
    /// </summary>
    public IReadOnlyList<ContentHistoryMenuItemViewModel> BackHistoryMenuEntries =>
        BuildHistoryMenuEntries(back: true);

    /// <summary>
    /// Gets the next content history entries shown in the forward navigation menu.
    /// </summary>
    public IReadOnlyList<ContentHistoryMenuItemViewModel> ForwardHistoryMenuEntries =>
        BuildHistoryMenuEntries(back: false);

    public AvaloniaList<SearchResultItemViewModel> SearchResults { get; } = [];

    public AvaloniaList<string> SpellSuggestions { get; } = [];

    [ObservableProperty]
    public partial bool IsSpellSuggestionPopupOpen { get; set; }

    /// <summary>
    /// Gets whether the visible search results were produced by the current mode and query text.
    /// </summary>
    public bool SearchResultsMatchCurrentQuery =>
        ReferenceEquals(_visibleResultsSearchMode, SelectedSearchMode)
        && string.Equals(_visibleResultsQuery.Trim(), SearchText.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Gets whether main-window searches are currently constrained by advanced-search filters.
    /// </summary>
    public bool HasActiveSearchFilters => _activeSearchFilters.HasFilters;

    /// <summary>
    /// Gets whether the currently displayed content has an entry word that can be copied.
    /// </summary>
    public bool CanCopyCurrentEntryWord => GetCurrentEntryWordForCopy() is not null;

    /// <summary>
    /// Gets the base word from the currently displayed entry title for clipboard copy.
    /// </summary>
    /// <returns>Entry word text, or <see langword="null"/> when no entry is displayed.</returns>
    public string? GetCurrentEntryWordForCopy()
    {
        if (!IsEntryContentPath(CurrentContentPath))
        {
            return null;
        }

        return ExtractEntryWordFromTitle(ContentTitle);
    }

    /// <summary>
    /// Selects a search result relative to the current selection.
    /// </summary>
    /// <param name="offset">Positive values move down; negative values move up.</param>
    /// <returns><see langword="true"/> when a result was selected.</returns>
    public bool SelectSearchResultRelative(int offset)
    {
        if (SearchResults.Count == 0)
        {
            return false;
        }

        int currentIndex = SelectedResult is null ? -1 : SearchResults.IndexOf(SelectedResult);
        int targetIndex = currentIndex < 0
            ? 0
            : Math.Clamp(currentIndex + offset, 0, SearchResults.Count - 1);
        if (targetIndex < 0)
        {
            return false;
        }

        SelectedResult = SearchResults[targetIndex];
        return true;
    }

    /// <summary>
    /// Search modes available from the main toolbar.
    /// </summary>
    public IReadOnlyList<SearchModeOption> SearchModes { get; }

    partial void OnSearchTextChanged(string value)
    {
        CancelSpellSuggestionRequest(clearSuggestions: true);
        if (_suppressSearchRequests)
        {
            return;
        }

        if (SelectedSearchMode.SearchOnTextChanged)
        {
            RequestSearch(SelectedSearchMode, value, SearchRequestTiming.Debounced);
        }
    }

    partial void OnSelectedSearchModeChanged(SearchModeOption value)
    {
        CancelSpellSuggestionRequest(clearSuggestions: true);
        if (_suppressSearchRequests)
        {
            return;
        }

        RequestSearch(value, SearchText, SearchRequestTiming.Immediate);
    }

    partial void OnSelectedResultChanged(SearchResultItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        if (_suppressSelectedResultNavigation)
        {
            return;
        }

        ContentTitle = value.Title;
        ContentStatus = value.Path;
        if (LoadSelectedContent(value, addToHistory: true))
        {
            PlayAutoPronunciation();
        }
    }

    partial void OnContentTextChanged(string value)
    {
        ContentDocument = DictionaryDocument.FromPlainText(value);
        ContentAnchor = null;
        CurrentContentPath = null;
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppConfiguration.ThemeMode))
        {
            App.ApplyThemeMode(Config.ThemeMode);
        }

        if (AppConfiguration.IsUserConfigProperty(e.PropertyName))
        {
            MarkUserConfigDirty();
        }
    }

    private void UpdateIncrementalResults(string query)
    {
        _fullTextSearchCancellation?.Cancel();
        CancelSpellSuggestionRequest(clearSuggestions: true);
        SearchResults.Clear();
        SelectedResult = null;

        if (string.IsNullOrWhiteSpace(query))
        {
            MarkSearchResultsCurrent(SelectedSearchMode, query);
            ResultCountText = "0";
            ContentTitle = ApplicationTitle;
            ContentStatus = "Type in the search box to search the generated index.";
            ContentText = "Search for a word, then select a result to display plain text content.";
            NotifySearchCompleted();
            return;
        }

        if (WildcardPattern.Contains(query))
        {
            MarkSearchResultsCurrent(SelectedSearchMode, query);
            ResultCountText = "0";
            ContentTitle = "Headword search";
            ContentStatus = "Select a result to display plain text content.";
            ContentText = "Select a result to display plain text content.";
            if (!ScheduleFullTextSearch(query))
            {
                ContentTitle = "Headword search unavailable";
                ContentStatus = "The required full-text index is not loaded.";
                ContentText = "Create the index again, then retry the search.";
                NotifySearchCompleted();
            }

            return;
        }

        if (_incrementalSearchIndex is null)
        {
            MarkSearchResultsCurrent(SelectedSearchMode, query);
            ResultCountText = "0";
            ContentTitle = "Incremental index unavailable";
            ContentStatus = IndexStatus;
            ContentText = IndexStatus;
            NotifySearchCompleted();
            return;
        }

        IReadOnlyList<IncrementalSearchResult> results =
            _incrementalSearchIndex.Search(query, IncrementalSearchLimit);

        SearchResults.AddRange(results.Select(SearchResultItemViewModel.FromIncrementalResult));

        ResultCountText = SearchResults.Count.ToString();
        MarkSearchResultsCurrent(SelectedSearchMode, query);
        ContentTitle = SearchResults.Count == 0 ? "No results" : "Incremental search";
        ContentStatus = SearchResults.Count == 0
            ? $"No incremental matches for \"{query.Trim()}\"."
            : "Select a result to view its dictionary path.";
        ContentText = SearchResults.Count == 0
            ? "No entry content to display."
            : "Select a result to display plain text content.";

        if (!ScheduleFullTextSearch(query))
        {
            NotifySearchCompleted();
        }
    }

    /// <summary>
    /// Runs the selected explicit full-text search mode.
    /// </summary>
    public RelayCommand RunModeSearchCommand => field ??= new(() =>
    {
        RequestSearch(SelectedSearchMode, SearchText, SearchRequestTiming.Immediate);
    }, CanRunModeSearch);

    /// <summary>
    /// Applies one spelling suggestion to the search box and reruns normal search.
    /// </summary>
    public RelayCommand<string> ApplySpellSuggestionCommand => field ??= new(ApplySpellSuggestion);

    /// <summary>
    /// Navigates back in content history.
    /// </summary>
    public RelayCommand NavigateBackCommand => field ??= new(NavigateBack, () => CanNavigateBack);

    /// <summary>
    /// Navigates forward in content history.
    /// </summary>
    public RelayCommand NavigateForwardCommand => field ??= new(NavigateForward, () => CanNavigateForward);

    /// <summary>
    /// Navigates directly to one content history entry.
    /// </summary>
    public RelayCommand<ContentHistoryMenuItemViewModel> NavigateHistoryEntryCommand =>
        field ??= new(NavigateHistoryEntry);

    /// <summary>
    /// Navigates to a rich dictionary content link.
    /// </summary>
    public RelayCommand<string> NavigateContentCommand => field ??= new(NavigateContent);

    /// <summary>
    /// Runs a lookup link through the search box.
    /// </summary>
    public RelayCommand<string> LookupContentCommand => field ??= new(LookupContent);

    /// <summary>
    /// Plays a dictionary audio resource.
    /// </summary>
    public RelayCommand<DictionaryResourceRef> PlayAudioCommand => field ??= new(PlayAudio);

    /// <summary>
    /// Plays the selected pronunciation variant for the currently displayed entry.
    /// </summary>
    public RelayCommand PlayCurrentPronunciationCommand => field ??= new(PlayCurrentPronunciation, CanPlayCurrentPronunciation);

    /// <summary>
    /// Increases dictionary content zoom by one step.
    /// </summary>
    public RelayCommand ZoomInCommand => field ??= new(() => ChangeZoomPower(1));

    /// <summary>
    /// Decreases dictionary content zoom by one step.
    /// </summary>
    public RelayCommand ZoomOutCommand => field ??= new(() => ChangeZoomPower(-1));

    /// <summary>
    /// Resets dictionary content zoom to normal size.
    /// </summary>
    public RelayCommand NormalSizeCommand => field ??= new(() => Config.ZoomPower = 0);

    /// <summary>
    /// Requests the settings dialog.
    /// </summary>
    public RelayCommand OpenSettingsCommand => field ??= new(() =>
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>
    /// Shows the application About content in the main document pane.
    /// </summary>
    public RelayCommand ShowAboutCommand => field ??= new(ShowAbout);

    private void ShowAbout()
    {
        _fullTextSearchCancellation?.Cancel();
        CancelSpellSuggestionRequest(clearSuggestions: true);
        _suppressSelectedResultNavigation = true;
        try
        {
            SelectedResult = null;
        }
        finally
        {
            _suppressSelectedResultNavigation = false;
        }

        string productVersion = _productVersion ?? GetProductVersion();
        string version = productVersion;
        string commitHash = "unknown";
        int commitIndex = productVersion.IndexOf('+', StringComparison.Ordinal);
        if (commitIndex >= 0)
        {
            version = productVersion[..commitIndex];
            commitHash = productVersion[(commitIndex + 1)..];
        }

        ContentTitle = $"About {ApplicationTitle}";
        ContentStatus = $"Version {version}; commit {commitHash}";
        ContentAnchor = null;
        CurrentContentPath = null;
        ContentDocument = BuildAboutDocument(version, commitHash);
    }

    private static DictionaryDocument BuildAboutDocument(string version, string commitHash)
    {
        return new DictionaryDocument(
        [
            new DictionaryHeadingBlock(
                1,
                [new DictionaryTextInline($"About {ApplicationTitle}", DictionaryTextStyle.Headword)],
                null),
            Paragraph(
                new DictionaryTextInline(
                    $"{ApplicationTitle} is an alternative dictionary viewer for “Longman Dictionary of Contemporary English 5th Edition.”",
                    DictionaryTextStyle.Normal)),
            Paragraph(
                new DictionaryTextInline("Version: ", DictionaryTextStyle.Strong),
                new DictionaryTextInline(version, DictionaryTextStyle.Normal)),
            Paragraph(
                new DictionaryTextInline("Commit: ", DictionaryTextStyle.Strong),
                new DictionaryTextInline(commitHash, DictionaryTextStyle.Normal)),
            Paragraph(
                new DictionaryTextInline("GitHub: ", DictionaryTextStyle.Strong),
                new DictionaryLinkInline(
                    new DictionaryLinkTarget(DictionaryLinkTargetKind.External, GitHubUrl),
                    [new DictionaryTextInline(GitHubUrl, DictionaryTextStyle.Normal)])),
            new DictionaryHeadingBlock(
                2,
                [new DictionaryTextInline("Author", DictionaryTextStyle.Label)],
                null),
            Paragraph(new DictionaryTextInline("The original software is written by Taku Fukuda.", DictionaryTextStyle.Normal)),
            new DictionaryHeadingBlock(
                2,
                [new DictionaryTextInline("Acknowledgements", DictionaryTextStyle.Label)],
                null),
            Paragraph(
                new DictionaryTextInline(
                    "This C# version uses .NET 10, Avalonia UI, CommunityToolkit.Mvvm, LeanCorpus, and MiniAudioEx.",
                    DictionaryTextStyle.Normal)),
        ]);

        static DictionaryParagraphBlock Paragraph(params DictionaryInline[] inlines)
        {
            return new DictionaryParagraphBlock(inlines, DictionaryBlockStyle.Normal, null);
        }
    }

    private static string GetProductVersion()
    {
        Assembly assembly = typeof(MainWindowViewModel).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Resets the configured search list width to the application default.
    /// </summary>
    public RelayCommand ResetSearchListWidthCommand =>
        field ??= new(() => Config.SearchListWidth = AppConfiguration.DefaultSearchListWidth);

    /// <summary>
    /// Resets the configured search result font size to the application default.
    /// </summary>
    public RelayCommand ResetSearchListBaseFontSizeCommand =>
        field ??= new(() => Config.SearchListBaseFontSize = AppConfiguration.DefaultSearchListBaseFontSize);

    /// <summary>
    /// Resets the configured dictionary content font size to the application default.
    /// </summary>
    public RelayCommand ResetContentBaseFontSizeCommand =>
        field ??= new(() => Config.ContentBaseFontSize = AppConfiguration.DefaultContentBaseFontSize);

    /// <summary>
    /// Searches clipboard text when it matches an enabled clipboard search index.
    /// </summary>
    /// <param name="text">Clipboard text to inspect.</param>
    /// <returns><see langword="true"/> when a matching entry search was started.</returns>
    public bool TrySearchClipboardText(string? text)
    {
        string query = NormalizeClipboardSearchText(text);
        if (query.Length == 0)
        {
            return false;
        }

        if (!ClipboardTextHasIncrementalMatch(query) && !ClipboardTextHasFullIndexMatch(query))
        {
            return false;
        }

        LookupContent(query);
        return true;

        static string NormalizeClipboardSearchText(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (text.Length > MaximumClipboardSearchTextLength)
            {
                return string.Empty;
            }

            return string.Join(" ", text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)).Trim();
        }
    }

    private bool ClipboardTextHasIncrementalMatch(string query)
    {
        if (_incrementalSearchIndex is null)
        {
            return false;
        }

        try
        {
            return _incrementalSearchIndex.Search(query, limit: 1).Count > 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
        {
            ContentStatus = $"Unable to search clipboard text: {ex.Message}";
            return false;
        }
    }

    private bool ClipboardTextHasFullIndexMatch(string query)
    {
        if (!Config.IsFullIndexClipboardSearchEnabled || _fullTextHeadwordPhraseSearcher is null)
        {
            return false;
        }

        try
        {
            return _fullTextHeadwordPhraseSearcher
                .Search(query, null, [], limit: 1, filterWildcardBySortKey: false, getRawContent: false)
                .Count > 0;
        }
        catch (Exception ex) when (FullTextSearcher.IsIndexUnavailableException(ex))
        {
            ContentStatus = FormatFullTextIndexUnavailableMessage(ex);
            return false;
        }
    }

    /// <summary>
    /// Applies advanced-search filters to subsequent main-window searches and reruns the current search.
    /// </summary>
    /// <param name="filters">Grouped advanced-search filters.</param>
    public void ApplyAdvancedSearchFilters(SearchFilterQuery filters)
    {
        _activeSearchFilters = filters;
        OnPropertyChanged(nameof(HasActiveSearchFilters));
        RunModeSearchCommand.NotifyCanExecuteChanged();
        SearchModeOption mode = SelectedSearchMode;
        if (filters.HasFilters && mode.UsesIncrementalSearch)
        {
            mode = SearchModes.First(option => option.DisplayName == "Headwords");
            _suppressSearchRequests = true;
            try
            {
                SelectedSearchMode = mode;
            }
            finally
            {
                _suppressSearchRequests = false;
            }
        }

        RequestSearch(SelectedSearchMode, SearchText, SearchRequestTiming.Immediate);
    }

    /// <summary>
    /// Clears advanced-search filters and reruns the current main-window search without filters.
    /// </summary>
    public void ClearAdvancedSearchFilters()
    {
        bool hadFilters = _activeSearchFilters.HasFilters;
        _activeSearchFilters = SearchFilterQuery.Empty;
        OnPropertyChanged(nameof(HasActiveSearchFilters));
        RunModeSearchCommand.NotifyCanExecuteChanged();
        if (hadFilters && SelectedSearchMode.DisplayName == "Headwords")
        {
            _suppressSearchRequests = true;
            try
            {
                SelectedSearchMode = SearchModes[0];
            }
            finally
            {
                _suppressSearchRequests = false;
            }
        }

        if (hadFilters)
        {
            RequestSearch(SelectedSearchMode, SearchText, SearchRequestTiming.Immediate);
        }
    }

    /// <summary>
    /// Requests a search using the selected timing behavior.
    /// </summary>
    private void RequestSearch(SearchModeOption mode, string query, SearchRequestTiming timing)
    {
        if (_activeSearchFilters.HasFilters)
        {
            if (timing == SearchRequestTiming.Debounced)
            {
                ScheduleAdvancedSearch(mode, query, _activeSearchFilters);
                return;
            }

            UpdateAdvancedSearchResults(mode, query.Trim(), _activeSearchFilters);
            return;
        }

        if (mode.UsesIncrementalSearch)
        {
            UpdateIncrementalResults(query);
            return;
        }

        if (timing == SearchRequestTiming.Debounced)
        {
            ScheduleModeSearch(mode, query);
            return;
        }

        UpdateModeSearchResults(mode, query);
    }

    /// <summary>
    /// Runs the selected explicit full-text search mode and replaces the result list.
    /// </summary>
    private void UpdateModeSearchResults(SearchModeOption mode, string queryText)
    {
        UpdateFullTextModeSearchResults(mode, queryText, SearchFilterQuery.Empty);
    }

    /// <summary>
    /// Runs the selected full-text search mode with advanced-search filters.
    /// </summary>
    private void UpdateAdvancedSearchResults(SearchModeOption mode, string query, SearchFilterQuery filters)
    {
        UpdateFullTextModeSearchResults(mode, query, filters);
    }

    private void UpdateFullTextModeSearchResults(SearchModeOption mode, string queryText, SearchFilterQuery filters)
    {
        _fullTextSearchCancellation?.Cancel();
        CancelSpellSuggestionRequest(clearSuggestions: true);
        SearchResults.Clear();
        SelectedResult = null;
        string query = queryText.Trim();
        if (query.Length == 0 && !filters.HasFilters)
        {
            MarkSearchResultsCurrent(mode, query, filters);
            ResultCountText = "0";
            ContentTitle = ApplicationTitle;
            ContentStatus = "Type in the search box to search the generated index.";
            ContentText = "Search for a word, then select a result to display plain text content.";
            NotifySearchCompleted();
            return;
        }

        FullTextSearchOutcome outcome = RunFullTextModeSearch(mode, query, filters);
        if (outcome.UnavailableMessage is not null)
        {
            ApplyFullTextSearchUnavailable(mode, query, outcome.UnavailableMessage, filters);
            return;
        }

        ApplyModeSearchResults(mode, query, outcome.Results, filters);
    }

    /// <summary>
    /// Starts a delayed category search and cancels stale pending searches.
    /// </summary>
    private void ScheduleModeSearch(SearchModeOption mode, string queryText)
    {
        ScheduleModeSearch(mode, queryText, SearchFilterQuery.Empty);
    }

    /// <summary>
    /// Starts a delayed filtered search and cancels stale pending searches.
    /// </summary>
    private void ScheduleAdvancedSearch(SearchModeOption mode, string queryText, SearchFilterQuery filters)
    {
        ScheduleModeSearch(mode, queryText, filters);
    }

    private void ScheduleModeSearch(SearchModeOption mode, string queryText, SearchFilterQuery filters)
    {
        _fullTextSearchCancellation?.Cancel();
        CancelSpellSuggestionRequest(clearSuggestions: true);
        if (string.IsNullOrWhiteSpace(queryText) && !filters.HasFilters)
        {
            UpdateFullTextModeSearchResults(mode, queryText, filters);
            return;
        }

        CancellationTokenSource cancellation = new();
        _fullTextSearchCancellation = cancellation;
        _ = RunDelayedModeSearchAsync(mode, queryText, filters, cancellation.Token);
    }

    /// <summary>
    /// Runs a category search after the debounce delay and applies the latest result.
    /// </summary>
    private async Task RunDelayedModeSearchAsync(
        SearchModeOption mode,
        string queryText,
        SearchFilterQuery filters,
        CancellationToken cancellationToken)
    {
        try
        {
            await _searchDebounceDelay(cancellationToken);
            string query = queryText.Trim();
            FullTextSearchOutcome outcome = RunFullTextModeSearch(mode, query, filters);

            await InvokeUiAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested
                    || !ReferenceEquals(SelectedSearchMode, mode)
                    || !ReferenceEquals(_activeSearchFilters, filters))
                {
                    return;
                }

                SearchResults.Clear();
                SelectedResult = null;
                if (outcome.UnavailableMessage is not null)
                {
                    ApplyFullTextSearchUnavailable(mode, query, outcome.UnavailableMessage, filters);
                    return;
                }

                ApplyModeSearchResults(mode, query, outcome.Results, filters);
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private FullTextSearchOutcome RunFullTextModeSearch(SearchModeOption mode, string query, SearchFilterQuery filters)
    {
        FullTextSearcher? searcher = mode.UsesDefinitionExampleIndex
            ? _fullTextDefinitionExampleSearcher
            : _fullTextHeadwordPhraseSearcher;
        if (searcher is null)
        {
            return new([], "The required full-text index is not loaded.");
        }

        try
        {
            IReadOnlyList<FullTextSearchResult> results = searcher.Search(
                query,
                filters.HasFilters ? filters : null,
                mode.ItemTypes,
                mode.Limit,
                mode.FilterWildcardBySortKey,
                mode.GetRawContent);
            return new(results, null);
        }
        catch (Exception ex) when (FullTextSearcher.IsIndexUnavailableException(ex))
        {
            return new([], FormatFullTextIndexUnavailableMessage(ex));
        }
    }

    /// <summary>
    /// Applies completed category search results to the result list.
    /// </summary>
    private void ApplyModeSearchResults(
        SearchModeOption mode,
        string query,
        IReadOnlyList<FullTextSearchResult> results,
        SearchFilterQuery? filters = null)
    {
        SearchResults.AddRange(results.Select(SearchResultItemViewModel.FromFullTextResult));

        ResultCountText = SearchResults.Count.ToString();
        SearchFilterQuery filterQuery = filters ?? SearchFilterQuery.Empty;
        MarkSearchResultsCurrent(mode, query, filterQuery);
        string title = filterQuery.HasFilters ? $"Advanced {mode.DisplayName}" : mode.ResultTitle;
        ContentTitle = SearchResults.Count == 0 ? "No results" : title;
        ContentStatus = SearchResults.Count == 0
            ? FormatNoSearchResultsMessage(mode, query, filterQuery)
            : "Select a result to display plain text content.";
        ContentText = SearchResults.Count == 0
            ? "No entry content to display."
            : "Select a result to display plain text content.";
        NotifySearchCompleted();
    }

    private void ApplyFullTextSearchUnavailable(
        SearchModeOption mode,
        string query,
        string message,
        SearchFilterQuery? filters = null)
    {
        MarkSearchResultsCurrent(mode, query, filters ?? SearchFilterQuery.Empty);
        ResultCountText = "0";
        ContentTitle = $"{mode.DisplayName} unavailable";
        ContentStatus = message;
        ContentText = "Create the index again, then retry the search.";
        NotifySearchCompleted();
    }

    private void MarkSearchResultsCurrent(SearchModeOption mode, string query, SearchFilterQuery? filters = null)
    {
        _visibleResultsSearchMode = mode;
        _visibleResultsQuery = query;
    }

    private static string FormatNoSearchResultsMessage(SearchModeOption mode, string query, SearchFilterQuery filters)
    {
        string label = mode.DisplayName.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(query))
        {
            return $"No {label} matches for \"{query}\".";
        }

        return filters.HasFilters
            ? $"No {label} matches for the selected filters."
            : $"No {label} matches.";
    }

    /// <summary>
    /// Returns whether an explicit search mode can run.
    /// </summary>
    private bool CanRunModeSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText) && !_activeSearchFilters.HasFilters)
        {
            return false;
        }

        if (SelectedSearchMode.UsesIncrementalSearch && !_activeSearchFilters.HasFilters)
        {
            return _incrementalSearchIndex is not null;
        }

        return SelectedSearchMode.UsesDefinitionExampleIndex
            ? _fullTextDefinitionExampleSearcher is not null
            : _fullTextHeadwordPhraseSearcher is not null;
    }

    /// <summary>
    /// Starts the delayed full-text search that augments incremental results.
    /// </summary>
    private bool ScheduleFullTextSearch(string query)
    {
        if (_fullTextHeadwordPhraseSearcher is null || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        CancellationTokenSource cancellation = new();
        _fullTextSearchCancellation = cancellation;
        _ = RunDelayedFullTextSearchAsync(query, cancellation.Token);
        return true;
    }

    /// <summary>
    /// Runs full-text search after a short delay and merges non-duplicate results into the UI list.
    /// </summary>
    private async Task RunDelayedFullTextSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await _searchDebounceDelay(cancellationToken);

            bool containWildcards = WildcardPattern.Contains(query);
            IReadOnlyCollection<string> itemTypes = containWildcards ? ["hm"] : [];
            string? unavailableMessage = null;
            IReadOnlyList<FullTextSearchResult> fullTextResults = [];
            if (_fullTextHeadwordPhraseSearcher is null)
            {
                unavailableMessage = "The required full-text index is not loaded.";
            }
            else
            {
                try
                {
                    fullTextResults = _fullTextHeadwordPhraseSearcher.Search(
                        query,
                        null,
                        itemTypes,
                        FullTextSearchLimit,
                        filterWildcardBySortKey: containWildcards,
                        getRawContent: false);
                }
                catch (Exception ex) when (FullTextSearcher.IsIndexUnavailableException(ex))
                {
                    unavailableMessage = FormatFullTextIndexUnavailableMessage(ex);
                }
            }

            await InvokeUiAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (unavailableMessage is not null)
                {
                    if (containWildcards && SearchResults.Count == 0)
                    {
                        ResultCountText = "0";
                        ContentTitle = "Headword search unavailable";
                        ContentStatus = unavailableMessage;
                        ContentText = "Create the index again, then retry the search.";
                    }

                    NotifySearchCompleted();
                    return;
                }

                bool hadResults = SearchResults.Count > 0;
                HashSet<int> existingResults = SearchResults.Select(result => result.GetHashCode()).ToHashSet();
                List<SearchResultItemViewModel> adds = new();

                foreach (FullTextSearchResult result in fullTextResults)
                {
                    SearchResultItemViewModel resultViewModel = SearchResultItemViewModel.FromFullTextResult(result);
                    if (existingResults.Add(resultViewModel.GetHashCode()))
                    {
                        adds.Add(resultViewModel);
                    }
                }
                if (adds.Count > 0)
                {
                    SearchResults.AddRange(adds);
                }

                ResultCountText = SearchResults.Count.ToString();
                if (!hadResults && SearchResults.Count > 0)
                {
                    ContentTitle = "Headword search";
                    ContentStatus = "Select a result to display plain text content.";
                    ContentText = "Select a result to display plain text content.";
                }

                ScheduleSpellSuggestionsIfNeeded(query);

                NotifySearchCompleted();
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string FormatFullTextIndexUnavailableMessage(Exception ex)
    {
        return $"Full-text index unavailable: {ex.Message}";
    }

    private void ScheduleSpellSuggestionsIfNeeded(string query)
    {
        if (SearchResults.Count == 0
            && _fullTextHeadwordPhraseSearcher is not null
            && SelectedSearchMode.UsesIncrementalSearch
            && !_activeSearchFilters.HasFilters
            && TextNormalizer.TokenizeFullTextQuery(query).Count == 1)
        {
            CancelSpellSuggestionRequest(clearSuggestions: true);
            CancellationTokenSource cancellation = new();
            _spellSuggestionCancellation = cancellation;
            _ = RunDelayedSpellSuggestionAsync(query, cancellation.Token);
        }
    }

    private async Task RunDelayedSpellSuggestionAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await _spellSuggestionDelay(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            IReadOnlyList<string> suggestions = [];
            if (_fullTextHeadwordPhraseSearcher is not null)
            {
                try
                {
                    suggestions = _fullTextHeadwordPhraseSearcher.SuggestCorrections(query, SpellSuggestionLimit);
                }
                catch (Exception ex) when (FullTextSearcher.IsIndexUnavailableException(ex))
                {
                    suggestions = [];
                }
            }

            await InvokeUiAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                SpellSuggestions.Clear();
                SpellSuggestions.AddRange(suggestions);

                IsSpellSuggestionPopupOpen = SpellSuggestions.Count > 0;
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelSpellSuggestionRequest(bool clearSuggestions)
    {
        _spellSuggestionCancellation?.Cancel();
        _spellSuggestionCancellation?.Dispose();
        _spellSuggestionCancellation = null;
        if (!clearSuggestions || SpellSuggestions.Count == 0)
        {
            return;
        }

        SpellSuggestions.Clear();
        IsSpellSuggestionPopupOpen = false;
    }

    private void ApplySpellSuggestion(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        CancelSpellSuggestionRequest(clearSuggestions: true);
        SearchText = suggestion;
    }

    private bool LoadSelectedContent(SearchResultItemViewModel result, bool addToHistory)
    {
        if (_dictionaryContentService is null)
        {
            string dataDirectoryStatus = string.IsNullOrWhiteSpace(_dataDirectory)
                ? "Dictionary data path is not configured. Create the index to select ldoce5.data."
                : $"Configured dictionary data path is unavailable: {_dataDirectory}.";
            ContentText =
                $"Dictionary content service is not available. {dataDirectoryStatus} Expected file map at {_indexPaths.FilemapPath}.";
            return false;
        }

        try
        {
            LoadContentPath(result.Path, addToHistory, selectVisibleResult: false);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
            or InvalidDataException
            or InvalidOperationException
            or System.Xml.XmlException)
        {
            ContentText = $"Unable to load '{result.Path}'.{Environment.NewLine}{Environment.NewLine}{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Loads an internal dictionary path into the content pane.
    /// </summary>
    private void NavigateContent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_dictionaryContentService is null)
        {
            ContentText = "Dictionary content service is not available.";
            return;
        }

        try
        {
            if (path.StartsWith("/picture/", StringComparison.Ordinal))
            {
                ShowPicture(path, addToHistory: true);
            }
            else
            {
                LoadContentPath(path, addToHistory: true, selectVisibleResult: true);
            }
        }
        catch (Exception ex) when (
            ex is IOException
            or InvalidDataException
            or InvalidOperationException
            or System.Xml.XmlException)
        {
            ContentText = $"Unable to load '{path}'.{Environment.NewLine}{Environment.NewLine}{ex.Message}";
        }
    }

    /// <summary>
    /// Uses a lookup link as the active search query and opens the closest result.
    /// </summary>
    private void LookupContent(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        string searchText = query.Trim();
        SearchModeOption lookupMode = SearchModes[0];
        _suppressSearchRequests = true;
        try
        {
            SelectedSearchMode = lookupMode;
            SearchText = searchText;
        }
        finally
        {
            _suppressSearchRequests = false;
        }

        RequestSearch(lookupMode, searchText, SearchRequestTiming.Immediate);
        SelectLookupResult(searchText);
    }

    /// <summary>
    /// Selects the best visible result for a lookup-link query.
    /// </summary>
    private void SelectLookupResult(string query)
    {
        if (SearchResults.Count == 0)
        {
            return;
        }

        int resultIndex = FindBestSearchResultIndex(query);
        SelectedResult = SearchResults[resultIndex < 0 ? 0 : resultIndex];
    }

    private int FindBestSearchResultIndex(string query)
    {
        string normalizedQuery = TextNormalizer.NormalizeIndexKey(query).ToLowerInvariant();
        if (normalizedQuery.Length > 0)
        {
            for (int i = 0; i < SearchResults.Count; i++)
            {
                if (SearchResults[i].SortKey.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return SearchResults.Count == 0 ? -1 : 0;
    }

    /// <summary>
    /// Loads resource bytes for document images.
    /// </summary>
    /// <param name="resource">Resource to load.</param>
    /// <returns>Resource bytes or <see langword="null"/> when unavailable.</returns>
    public byte[]? LoadResource(DictionaryResourceRef resource)
    {
        try
        {
            return _dictionaryContentService?.LoadResource(resource);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ContentStatus = $"Unable to load resource {resource.Archive}/{resource.Name}: {ex.Message}";
            return null;
        }
    }

#if DEBUG
    /// <summary>
    /// Loads raw XML for the currently displayed dictionary content.
    /// </summary>
    /// <returns>Raw XML text for the current content path.</returns>
    public string LoadCurrentRawXml()
    {
        if (_dictionaryContentService is null)
        {
            throw new InvalidOperationException("Dictionary content service is not available.");
        }

        if (string.IsNullOrWhiteSpace(CurrentContentPath))
        {
            throw new InvalidOperationException("No XML dictionary content is currently displayed.");
        }

        return _dictionaryContentService.LoadRawXml(CurrentContentPath);
    }

    /// <summary>
    /// Formats the currently displayed entry title and dictionary path for debugging.
    /// </summary>
    /// <returns>Clipboard text containing the current entry title and path.</returns>
    public string FormatCurrentEntryInfo()
    {
        if (string.IsNullOrWhiteSpace(CurrentContentPath))
        {
            throw new InvalidOperationException("No dictionary content is currently displayed.");
        }

        string path = string.IsNullOrWhiteSpace(ContentAnchor)
            ? CurrentContentPath
            : $"{CurrentContentPath}#{ContentAnchor}";
        return $"Title: {ContentTitle}{Environment.NewLine}Path: {path}";
    }
#endif

    /// <summary>
    /// Plays a dictionary audio resource.
    /// </summary>
    private void PlayAudio(DictionaryResourceRef? resource)
    {
        if (resource is null)
        {
            return;
        }

        byte[]? data = LoadResource(resource);
        if (data is null)
        {
            return;
        }

        try
        {
            _audioPlayer.Play(data, resource.MediaType, Config.AudioPlaybackVolume / 100.0);
        }
        catch (Exception ex) when (
            ex is IOException
            or InvalidOperationException
            or UnauthorizedAccessException
            or DllNotFoundException
            or BadImageFormatException
            or TypeInitializationException)
        {
            ContentStatus = $"Unable to play audio: {ex.Message}";
        }
    }

    private void PlayAutoPronunciation()
    {
        if (!Config.IsAutoPronunciationPlaybackEnabled)
        {
            return;
        }

        PlayPronunciation(Config.PronunciationPlayback);
    }

    private void PlayCurrentPronunciation()
    {
        PlayPronunciation(Config.PronunciationPlayback);
    }

    private bool CanPlayCurrentPronunciation()
    {
        return IsEntryContentPath(CurrentContentPath);
    }

    private void PlayPronunciation(PronunciationPlayback playback)
    {
        if (!CanPlayCurrentPronunciation())
        {
            return;
        }

        string archive = playback == PronunciationPlayback.British ? "gb_hwd_pron" : "us_hwd_pron";
        DictionaryAudioInline? audio = EnumerateAudioInlines(ContentDocument)
            .FirstOrDefault(inline => string.Equals(inline.Resource.Archive, archive, StringComparison.Ordinal));

        PlayAudio(audio?.Resource);
    }

    private static bool IsEntryContentPath(string? path)
    {
        return path?.StartsWith("/fs/", StringComparison.Ordinal) == true;
    }

    private static string? ExtractEntryWordFromTitle(string title)
    {
        string word = title.Trim();
        if (word.Length == 0)
        {
            return null;
        }

        int partOfSpeechStart = word.LastIndexOf(" (", StringComparison.Ordinal);
        if (partOfSpeechStart > 0 && word.EndsWith(')'))
        {
            word = word[..partOfSpeechStart].TrimEnd();
        }

        word = word
            .Replace("\u00b7", string.Empty, StringComparison.Ordinal)
            .Replace("\u2027", string.Empty, StringComparison.Ordinal);

        return word.Length == 0 ? null : word;
    }

    private static IEnumerable<DictionaryAudioInline> EnumerateAudioInlines(DictionaryDocument document) =>
        document.Blocks.SelectMany(EnumerateAudioInlines);

    private static IEnumerable<DictionaryAudioInline> EnumerateAudioInlines(DictionaryBlock block) =>
        block switch
        {
            DictionaryParagraphBlock paragraph => EnumerateAudioInlines(paragraph.Inlines),
            DictionaryHeadingBlock heading => EnumerateAudioInlines(heading.Inlines),
            DictionaryContainerBlock container => container.Blocks.SelectMany(EnumerateAudioInlines),
            _ => Enumerable.Empty<DictionaryAudioInline>(),
        };

    private static IEnumerable<DictionaryAudioInline> EnumerateAudioInlines(IReadOnlyList<DictionaryInline> inlines) =>
        inlines.SelectMany(static inline => inline switch
        {
            DictionaryAudioInline audio => [audio],
            DictionaryLinkInline link => EnumerateAudioInlines(link.Inlines),
            _ => Enumerable.Empty<DictionaryAudioInline>(),
        });

    private void SaveUserConfigIfNeeded()
    {
        if (!_isUserConfigDirty)
        {
            return;
        }

        AppConfiguration current = AppConfiguration.Load(_indexPaths);
        current.CopyUserSettingsFrom(Config);
        if (current.Equals(_loadedConfig))
        {
            _isUserConfigDirty = false;
            return;
        }

        current.Save();
        _loadedConfig = current.CreateSnapshot();
        _isUserConfigDirty = false;
    }

    private void MarkUserConfigDirty()
    {
        if (_suppressConfigSave)
        {
            return;
        }

        _isUserConfigDirty = true;
    }

    private void ChangeZoomPower(int delta)
    {
        Config.ZoomPower += delta;
    }

    /// <summary>
    /// Loads one rich content path.
    /// </summary>
    private void LoadContentPath(string path, bool addToHistory, bool selectVisibleResult)
    {
        DictionaryContentPage page = _dictionaryContentService!.LoadContent(path);
        ContentTitle = string.IsNullOrWhiteSpace(page.Title) ? path : page.Title;
        ContentStatus = page.Path;
        ContentAnchor = page.Anchor;
        ContentDocument = page.Document;
        CurrentContentPath = page.Path;
        if (selectVisibleResult)
        {
            SelectVisibleResultForPath(path);
        }

        if (addToHistory)
        {
            AddContentHistory(path);
        }
    }

    /// <summary>
    /// Shows a dictionary picture as a standalone content page.
    /// </summary>
    private void ShowPicture(string path, bool addToHistory)
    {
        string[] parts = path.Trim('/').Split('/', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            throw new InvalidDataException("The picture path is invalid.");
        }

        DictionaryResourceRef resource = new("picture", parts[1] + "/" + parts[2], "image/jpeg");
        ContentTitle = "Picture";
        ContentStatus = path;
        ContentDocument = new DictionaryDocument([new DictionaryImageBlock(resource, null, null)]);
        ContentAnchor = null;
        CurrentContentPath = null;
        SelectVisibleResultForPath(path);
        if (addToHistory)
        {
            AddContentHistory(path);
        }
    }

    /// <summary>
    /// Navigates back in content history.
    /// </summary>
    private void NavigateBack()
    {
        if (!CanNavigateBack)
        {
            return;
        }

        _contentHistoryIndex--;
        LoadHistoryEntry(_contentHistory[_contentHistoryIndex]);
        NotifyHistoryChanged();
    }

    /// <summary>
    /// Navigates forward in content history.
    /// </summary>
    private void NavigateForward()
    {
        if (!CanNavigateForward)
        {
            return;
        }

        _contentHistoryIndex++;
        LoadHistoryEntry(_contentHistory[_contentHistoryIndex]);
        NotifyHistoryChanged();
    }

    /// <summary>
    /// Navigates directly to a content history entry selected from a navigation menu.
    /// </summary>
    /// <param name="entry">History menu entry to navigate to.</param>
    private void NavigateHistoryEntry(ContentHistoryMenuItemViewModel? entry)
    {
        if (entry is null
            || entry.HistoryIndex < 0
            || entry.HistoryIndex >= _contentHistory.Count
            || !string.Equals(_contentHistory[entry.HistoryIndex].Path, entry.Path, StringComparison.Ordinal))
        {
            return;
        }

        _contentHistoryIndex = entry.HistoryIndex;
        LoadHistoryEntry(_contentHistory[_contentHistoryIndex]);
        NotifyHistoryChanged();
    }

    /// <summary>
    /// Loads a history entry without adding a new history item.
    /// </summary>
    private void LoadHistoryEntry(ContentHistoryEntry entry)
    {
        try
        {
            RestoreSearchState(entry);
            if (entry.Path.StartsWith("/picture/", StringComparison.Ordinal))
            {
                ShowPicture(entry.Path, addToHistory: false);
            }
            else if (_dictionaryContentService is not null)
            {
                LoadContentPath(entry.Path, addToHistory: false, selectVisibleResult: true);
            }
        }
        catch (Exception ex) when (
            ex is IOException
            or InvalidDataException
            or InvalidOperationException
            or System.Xml.XmlException)
        {
            ContentText = $"Unable to load '{entry.Path}'.{Environment.NewLine}{Environment.NewLine}{ex.Message}";
        }
    }

    /// <summary>
    /// Adds a content path to navigation history.
    /// </summary>
    private void AddContentHistory(string path)
    {
        if (_contentHistoryIndex >= 0
            && _contentHistoryIndex < _contentHistory.Count
            && string.Equals(_contentHistory[_contentHistoryIndex].Path, path, StringComparison.Ordinal))
        {
            _contentHistory[_contentHistoryIndex] = CreateContentHistoryEntry(path);
            NotifyHistoryChanged();
            return;
        }

        if (_contentHistoryIndex < _contentHistory.Count - 1)
        {
            _contentHistory.RemoveRange(_contentHistoryIndex + 1, _contentHistory.Count - _contentHistoryIndex - 1);
        }

        _contentHistory.Add(CreateContentHistoryEntry(path));
        _contentHistoryIndex = _contentHistory.Count - 1;
        TrimContentHistory();
        NotifyHistoryChanged();
    }

    /// <summary>
    /// Creates a history entry from the current content path and search state.
    /// </summary>
    private ContentHistoryEntry CreateContentHistoryEntry(string path)
    {
        return new ContentHistoryEntry(path, NormalizeHistoryTitle(ContentTitle, path), SearchText, SelectedSearchMode);
    }

    /// <summary>
    /// Restores the query and search result list associated with a history entry.
    /// </summary>
    private void RestoreSearchState(ContentHistoryEntry entry)
    {
        bool modeChanged = !ReferenceEquals(SelectedSearchMode, entry.SearchMode);
        bool queryChanged = !string.Equals(SearchText, entry.SearchText, StringComparison.Ordinal);
        if (!modeChanged && !queryChanged)
        {
            return;
        }

        _suppressSearchRequests = true;
        try
        {
            SelectedSearchMode = entry.SearchMode;
            SearchText = entry.SearchText;
        }
        finally
        {
            _suppressSearchRequests = false;
        }

        RequestSearch(SelectedSearchMode, SearchText, SearchRequestTiming.Immediate);
    }

    /// <summary>
    /// Selects the currently visible search result that points at the displayed content path.
    /// </summary>
    private void SelectVisibleResultForPath(string path)
    {
        SearchResultItemViewModel? result = FindVisibleResultForPath(path);
        if (result is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedResult, result))
        {
            return;
        }

        _suppressSelectedResultNavigation = true;
        try
        {
            SelectedResult = result;
        }
        finally
        {
            _suppressSelectedResultNavigation = false;
        }
    }

    /// <summary>
    /// Finds the best visible search-result row for a content path.
    /// </summary>
    private SearchResultItemViewModel? FindVisibleResultForPath(string path)
    {
        SearchResultItemViewModel? exactResult = SearchResults.FirstOrDefault(
            result => string.Equals(result.Path, path, StringComparison.Ordinal));
        if (exactResult is not null)
        {
            return exactResult;
        }

        string cleanPath = StripFragment(path);
        return SearchResults.FirstOrDefault(
            result => string.Equals(StripFragment(result.Path), cleanPath, StringComparison.Ordinal));
    }

    /// <summary>
    /// Removes an in-entry fragment from a dictionary path.
    /// </summary>
    private static string StripFragment(string path)
    {
        int fragmentIndex = path.IndexOf('#', StringComparison.Ordinal);
        return fragmentIndex < 0 ? path : path[..fragmentIndex];
    }

    /// <summary>
    /// Keeps the content history within the old viewer's visible history limit.
    /// </summary>
    private void TrimContentHistory()
    {
        if (_contentHistory.Count <= MaximumContentHistoryEntries)
        {
            return;
        }

        int removeCount = _contentHistory.Count - MaximumContentHistoryEntries;
        _contentHistory.RemoveRange(0, removeCount);
        _contentHistoryIndex = Math.Max(0, _contentHistoryIndex - removeCount);
    }

    /// <summary>
    /// Updates navigation command states.
    /// </summary>
    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
        OnPropertyChanged(nameof(ContentHistoryCount));
        OnPropertyChanged(nameof(BackHistoryMenuEntries));
        OnPropertyChanged(nameof(ForwardHistoryMenuEntries));
        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Builds the visible navigation menu entries using the visible entries limit.
    /// </summary>
    private IReadOnlyList<ContentHistoryMenuItemViewModel> BuildHistoryMenuEntries(bool back)
    {
        if (_contentHistoryIndex < 0)
        {
            return [];
        }

        List<ContentHistoryMenuItemViewModel> entries = [];
        HashSet<string> paths = new(StringComparer.Ordinal);
        int startIndex = back ? _contentHistoryIndex - 1 : _contentHistoryIndex + 1;
        int endIndex = back
            ? Math.Max(-1, _contentHistoryIndex - MaximumVisibleContentHistoryEntries - 1)
            : Math.Min(_contentHistory.Count, _contentHistoryIndex + MaximumVisibleContentHistoryEntries + 1);
        int step = back ? -1 : 1;

        for (int i = startIndex; i != endIndex; i += step)
        {
            ContentHistoryEntry entry = _contentHistory[i];
            if (entry.DisplayText.Length == 0 || !paths.Add(entry.Path))
            {
                continue;
            }

            entries.Add(new ContentHistoryMenuItemViewModel(entry.DisplayText, entry.Path, i));
        }

        return entries;
    }

    /// <summary>
    /// Normalizes a history menu title and falls back to the dictionary path when needed.
    /// </summary>
    private static string NormalizeHistoryTitle(string title, string path)
    {
        title = TextNormalizer.CollapseWhitespace(title);
        return title.Length == 0 ? path : title;
    }

    /// <summary>
    /// Runs UI updates through Avalonia dispatching unless tests request direct updates.
    /// </summary>
    private async Task InvokeUiAsync(Action action)
    {
        if (!_dispatchUiUpdates || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    /// <summary>
    /// Notifies listeners that the current search request has finished.
    /// </summary>
    private void NotifySearchCompleted()
    {
        SearchCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Formats the main window title
    /// </summary>
    private static string FormatWindowTitle(string title)
    {
        title = title.Trim();
        if (title.Length == 0
            || string.Equals(title, "about:blank", StringComparison.Ordinal)
            || string.Equals(title, ApplicationTitle, StringComparison.Ordinal))
        {
            return ApplicationTitle;
        }

        return $"{title} - {ApplicationTitle}";
    }

    /// <summary>
    /// One browser-like content history item and the search state that selected it.
    /// </summary>
    private sealed record ContentHistoryEntry(
        string Path,
        string DisplayText,
        string SearchText,
        SearchModeOption SearchMode);
}

/// <summary>
/// One selectable item in a back or forward content history menu.
/// </summary>
/// <param name="DisplayText">Plain text shown in the menu.</param>
/// <param name="Path">Dictionary content path represented by the history item.</param>
/// <param name="HistoryIndex">Index of the item in the complete content history.</param>
public sealed record ContentHistoryMenuItemViewModel(string DisplayText, string Path, int HistoryIndex);

internal enum SearchRequestTiming
{
    Immediate,
    Debounced,
}

public sealed class SearchResultItemViewModel
{
    public SearchResultItemViewModel(
        string title,
        string kind,
        string description,
        string path,
        string sortKey,
        byte priority,
        string typeCode,
        IReadOnlyList<SearchResultLabelRun>? labelRuns = null)
    {
        Title = title;
        Kind = kind;
        Description = description;
        Path = path;
        SortKey = sortKey;
        Priority = priority;
        TypeCode = typeCode;
        LabelRuns = labelRuns ?? [new SearchResultLabelRun(title, SearchResultLabelStyle.Normal)];

        KindAbbreviation = GetKindAbbreviation(kind);
        HasDescription = Description != string.Empty;
    }

    public string Title { get; }

    /// <summary>
    /// Gets the styled runs used to render the result title.
    /// </summary>
    public IReadOnlyList<SearchResultLabelRun> LabelRuns { get; }

    public string Kind { get; }

    /// <summary>
    /// Gets the compact label shown in the result kind badge.
    /// </summary>
    public string KindAbbreviation { get; }

    public string Description { get; }

    /// <summary>
    /// Gets whether the result has supporting description text to show under the title.
    /// </summary>
    public bool HasDescription { get; }

    public string Path { get; }

    public string SortKey { get; }

    public byte Priority { get; }

    public string TypeCode { get; }

    public static SearchResultItemViewModel FromIncrementalResult(IncrementalSearchResult result)
    {
        string title = ResultLabelFormatter.ToDisplayText(result.Label);
        IReadOnlyList<SearchResultLabelRun> labelRuns = ResultLabelFormatter.ToLabelRuns(result.Label);
        string kind = GetKind(result.TypeCode);
        return new SearchResultItemViewModel(
            title,
            kind,
            string.Empty,
            result.Path,
            result.SortKey,
            result.Priority,
            result.TypeCode,
            labelRuns);
    }

    public static SearchResultItemViewModel FromFullTextResult(FullTextSearchResult result)
    {
        string title = ResultLabelFormatter.ToDisplayText(result.Label);
        IReadOnlyList<SearchResultLabelRun> labelRuns = ResultLabelFormatter.ToLabelRuns(result.Label);
        string kind = GetKind(result.TypeCode);
        string description = result.TypeCode is "d" or "e" && !string.IsNullOrWhiteSpace(result.Snippet)
            ? result.Snippet
            : string.Empty;
        return new SearchResultItemViewModel(
            title,
            kind,
            description,
            result.Path,
            result.SortKey,
            result.Priority,
            result.TypeCode,
            labelRuns);
    }

    private static string GetKind(string typeCode)
    {
        return typeCode switch
        {
            "hm" => "Headword",
            "hv" => "Variant",
            "hp" => "Phrasal Verb",
            "p" => "Collocation",
            "pl" => "Phrase",
            "ac" => "Activator",
            "ae" => "Activator",
            "d" => "Definition",
            "e" => "Example",
            _ => typeCode,
        };
    }

    private static string GetKindAbbreviation(string kind)
    {
        return kind switch
        {
            "Headword" => "HW",
            "Variant" => "Var",
            "Phrasal Verb" => "PV",
            "Collocation" => "Col",
            "Phrase" => "Phr",
            "Activator" => "Act",
            "Definition" => "Def",
            "Example" => "Exa",
            _ => kind,
        };
    }

    /// <summary>
    /// Identifies one visible search row while allowing distinct variants to share a dictionary path.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(TypeCode, Path, Title);
    }
}

/// <summary>
/// Describes one full-text search mode exposed by the main toolbar.
/// </summary>
public sealed class SearchModeOption
{
    public SearchModeOption(
        string displayName,
        string resultTitle,
        bool usesDefinitionExampleIndex,
        IReadOnlyCollection<string> itemTypes,
        int limit)
    {
        DisplayName = displayName;
        ResultTitle = resultTitle;
        UsesDefinitionExampleIndex = usesDefinitionExampleIndex;
        ItemTypes = itemTypes;
        Limit = limit;

        UsesIncrementalSearch = itemTypes.Count == 0;
        SearchOnTextChanged = !usesDefinitionExampleIndex;
        FilterWildcardBySortKey = !usesDefinitionExampleIndex;
        GetRawContent = usesDefinitionExampleIndex;
    }

    /// <summary>
    /// User-visible mode name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Title shown when this mode returns results.
    /// </summary>
    public string ResultTitle { get; }

    /// <summary>
    /// Gets whether this mode searches the definition/example index.
    /// </summary>
    public bool UsesDefinitionExampleIndex { get; }

    /// <summary>
    /// Gets whether this mode uses the incremental search workflow.
    /// </summary>
    public bool UsesIncrementalSearch { get; }

    /// <summary>
    /// Gets whether text changes immediately trigger this mode.
    /// </summary>
    public bool SearchOnTextChanged { get; }

    /// <summary>
    /// Gets whether wildcard searches must also match dictionary sort keys.
    /// </summary>
    public bool FilterWildcardBySortKey { get; }

    /// <summary>
    /// Gets whether to get raw content.
    /// </summary>
    public bool GetRawContent { get; }

    /// <summary>
    /// Full-text item type filters.
    /// </summary>
    public IReadOnlyCollection<string> ItemTypes { get; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int Limit { get; }

    /// <summary>
    /// Returns the user-visible mode name.
    /// </summary>
    /// <returns>The display name.</returns>
    public override string ToString()
    {
        return DisplayName;
    }
}
