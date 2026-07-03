using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.ViewModels;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly List<MainWindowViewModel> _viewModels = [];
    private readonly List<(string Archive, string Name, ArchiveLocation Location)> _filemapEntries = [];
    private readonly IndexPaths _paths;
    private readonly string _dataDirectory;

    public MainWindowViewModelTests()
    {
        _paths = new IndexPaths(_root);
        _dataDirectory = Path.Combine(_root, "ldoce5.data");
        IncrementalSearchIndexWriter incrementalWriter = new();
        incrementalWriter.AddItem("shared query", "hm", "word", "/fs/word", 1);
        incrementalWriter.AddItem("earth", "hm", "word", "/fs/word", 1);
        incrementalWriter.AddItem("hello", "hm", "work", "/fs/work", 1);
        incrementalWriter.AddItem("multi line", "hm", "word", "/fs/word", 1);
        incrementalWriter.WriteTo(_paths.IncrementalPath);

        using FullTextIndexWriter hpWriter = new(_paths.FullTextHeadwordPhrasePath);
        hpWriter.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "shared query word", "word", string.Empty, 1));
        hpWriter.AddItem(new SearchIndexItem("hm", "work", "/fs/work", "work", "work", string.Empty, 1));
        hpWriter.AddItem(new SearchIndexItem("hm", "swear word", "/fs/swear-word", "swear word", "swear word", string.Empty, 1));
        hpWriter.AddItem(new SearchIndexItem("hm", "sword", "/fs/sword", "sword", "sword", string.Empty, 1));
        hpWriter.AddItem(new SearchIndexItem("hp", "word up", "/fs/word-up", "word", "word up", string.Empty, 1));
        hpWriter.AddItem(new SearchIndexItem("hp", "be located in", "/fs/located-in", "be located in", "be located in", string.Empty, 1));
        hpWriter.AddItem(new SearchIndexItem("hp", "divide into", "/fs/divide-into", "divide into", "divide into", string.Empty, 1));
        hpWriter.AddItem(new SearchIndexItem("hm", "academic", "/fs/academic", "filtered target", "academic", "426 334", 1));
        hpWriter.AddItem(new SearchIndexItem("hm", "ordinary", "/fs/ordinary", "filtered target", "ordinary", "334", 1));
        hpWriter.Commit();

        VariationsIndex.Write(_paths.VariationsPath, new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["divided"] = ["divide"],
        });

        using FullTextIndexWriter deWriter = new(_paths.FullTextDefinitionExamplePath);
        deWriter.AddItem(new SearchIndexItem("d", "definition", "/fs/definition", "shared query", "definition", string.Empty, 30));
        deWriter.AddItem(new SearchIndexItem("e", "example", "/fs/example", "shared query", "example", string.Empty, 20));
        deWriter.Commit();

        AddArchiveItem(
            "fs",
            "word",
            """
            <Entry>
              <Head>
                <HWD><BASE>word</BASE></HWD>
                <Audio resource="GB_HWD_PRON" topic="gb_hwd_pron/word.mp3"/>
                <Audio resource="US_HWD_PRON" topic="us_hwd_pron/word.mp3"/>
              </Head>
              <Sense><DEF>First entry.</DEF></Sense>
            </Entry>
            """);
        AddArchiveItem("fs", "work", "<Entry><Head><HWD><BASE>work</BASE></HWD></Head><Sense><DEF>Second entry.</DEF></Sense></Entry>");
        AddArchiveItem("fs", "academic", "<Entry><Head><HWD><BASE>academic</BASE></HWD></Head><Sense><DEF>Academic entry.</DEF></Sense></Entry>");
        AddArchiveItem("fs", "ordinary", "<Entry><Head><HWD><BASE>ordinary</BASE></HWD></Head><Sense><DEF>Ordinary entry.</DEF></Sense></Entry>");
        AddArchiveItem("gb_hwd_pron", "word.mp3", "gb automatic audio");
        AddArchiveItem("us_hwd_pron", "word.mp3", "us automatic audio");
        for (int i = 0; i < 105; i++)
        {
            string name = $"item{i:00}";
            AddArchiveItem("fs", name, $"<Entry><Head><HWD><BASE>{name}</BASE></HWD></Head><Sense><DEF>{name} entry.</DEF></Sense></Entry>");
        }

        WriteFilemap();

        AppConfiguration config = new(_paths)
        {
            IndexVersion = AppConfiguration.CurrentIndexVersion,
            DataDirectory = _dataDirectory,
            IsAutoPronunciationPlaybackEnabled = false,
        };
        config.Save();
    }

    [Fact]
    public void SearchTextChanged_runs_incremental_search_when_all_mode_is_selected()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.SearchText = "shared";

        viewModel.SearchResults.Should().ContainSingle();
        viewModel.SearchResults[0].Kind.Should().Be("Headword");
        viewModel.ContentTitle.Should().Be("Incremental search");
    }

    [Fact]
    public void WindowTitle_uses_legacy_app_name_when_no_content_title_is_displayed()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.WindowTitle.Should().Be("LDOCE5 Viewer X");
    }

    [Fact]
    public void WindowTitle_includes_current_content_title()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "earth";

        viewModel.SelectedResult = viewModel.SearchResults.Single();

        viewModel.WindowTitle.Should().Be("word - LDOCE5 Viewer X");
    }

    [Fact]
    public void ShowAboutCommand_displays_application_version_and_commit_hash()
    {
        MainWindowViewModel viewModel = CreateViewModel(productVersion: "0.0.1+abc123def456");
        viewModel.SearchText = "earth";
        viewModel.SelectedResult = viewModel.SearchResults.Single();

        viewModel.ShowAboutCommand.Execute(null);

        viewModel.ContentTitle.Should().Be("About LDOCE5 Viewer X");
        viewModel.ContentStatus.Should().Be("Version 0.0.1; commit abc123def456");
        viewModel.CurrentContentPath.Should().BeNull();
        viewModel.SelectedResult.Should().BeNull();
        string aboutText = FlattenDocumentText(viewModel.ContentDocument);
        aboutText.Should().Contain("Version: 0.0.1");
        aboutText.Should().Contain("Commit: abc123def456");
    }

    [Fact]
    public void SelectSearchResultRelative_selects_first_result_when_no_result_is_selected()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "work";
        SearchResultItemViewModel word = new("word", "Headword", "/fs/word", "/fs/word", "word", 1, "hm");
        SearchResultItemViewModel work = new("work", "Headword", "/fs/work", "/fs/work", "work", 1, "hm");
        viewModel.SearchResults.Clear();
        viewModel.SearchResults.Add(word);
        viewModel.SearchResults.Add(work);

        bool selected = viewModel.SelectSearchResultRelative(1);

        selected.Should().BeTrue();
        viewModel.SelectedResult.Should().BeSameAs(word);
        viewModel.ContentTitle.Should().Be("word");
    }

    [Fact]
    public void SelectSearchResultRelative_moves_result_selection_up_and_down()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SearchResultItemViewModel word = new("word", "Headword", "/fs/word", "/fs/word", "word", 1, "hm");
        SearchResultItemViewModel work = new("work", "Headword", "/fs/work", "/fs/work", "work", 1, "hm");
        viewModel.SearchResults.Add(word);
        viewModel.SearchResults.Add(work);
        viewModel.SelectedResult = word;

        viewModel.SelectSearchResultRelative(1).Should().BeTrue();
        viewModel.SelectedResult.Should().BeSameAs(work);

        viewModel.SelectSearchResultRelative(-1).Should().BeTrue();
        viewModel.SelectedResult.Should().BeSameAs(word);
    }

    [Fact]
    public void ReloadIndexes_rejects_index_when_config_version_does_not_match()
    {
        AppConfiguration config = AppConfiguration.Load(_paths);
        config.IndexVersion = "0";
        config.Save();

        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.HasGeneratedIndex.Should().BeFalse();
        viewModel.HasUsableIndex.Should().BeFalse();
        viewModel.IndexStatus.Should().Contain("does not match current version");
    }

    [Fact]
    public void RunModeSearchCommand_runs_definition_search_without_incremental_index()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "shared query";
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Definitions");

        viewModel.RunModeSearchCommand.Execute(null);

        viewModel.SearchResults.Should().ContainSingle();
        viewModel.SearchResults[0].Kind.Should().Be("Definition");
        viewModel.SearchResults[0].Description.Should().Be("shared query");
        viewModel.ContentTitle.Should().Be("Definition search");
        viewModel.SearchResultsMatchCurrentQuery.Should().BeTrue();
    }

    [Fact]
    public void SearchResultsMatchCurrentQuery_tracks_query_changes_after_explicit_search()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "shared query";
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Definitions");
        viewModel.RunModeSearchCommand.Execute(null);

        viewModel.SearchResultsMatchCurrentQuery.Should().BeTrue();

        viewModel.SearchText = "changed query";

        viewModel.SearchResultsMatchCurrentQuery.Should().BeFalse();
    }

    [Fact]
    public void RunModeSearchCommand_runs_example_search_without_incremental_index()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "shared query";
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Examples");

        viewModel.RunModeSearchCommand.Execute(null);

        viewModel.SearchResults.Should().ContainSingle();
        viewModel.SearchResults[0].Kind.Should().Be("Example");
        viewModel.SearchResults[0].Description.Should().Be("shared query");
        viewModel.ContentTitle.Should().Be("Example search");
    }

    [Fact]
    public async Task SearchTextChanged_debounces_category_searches()
    {
        TaskCompletionSource debounce = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindowViewModel viewModel = CreateViewModel(_ => debounce.Task);
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Headwords");
        Task waitForResults = WaitForPropertyAsync(
            viewModel,
            nameof(MainWindowViewModel.ResultCountText),
            () => viewModel.ResultCountText == "1");

        viewModel.SearchText = "shared query";

        viewModel.SearchResults.Should().BeEmpty();

        debounce.SetResult();
        await waitForResults.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchResults.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchTextChanged_cancels_stale_category_searches()
    {
        List<CancellationToken> tokens = [];
        TaskCompletionSource debounce = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource debounceStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindowViewModel viewModel = CreateViewModel(token =>
        {
            tokens.Add(token);
            debounceStarted.TrySetResult();
            return debounce.Task.WaitAsync(token);
        });
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Headwords");
        Task waitForNoResults = WaitForPropertyAsync(
            viewModel,
            nameof(MainWindowViewModel.ContentTitle),
            () => viewModel.ContentTitle == "No results");

        viewModel.SearchText = "shared query";
        await debounceStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchText = "not found";

        tokens.Should().NotBeEmpty();
        tokens[0].IsCancellationRequested.Should().BeTrue();
        debounce.SetResult();
        await waitForNoResults.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchResults.Should().BeEmpty();
        viewModel.ContentTitle.Should().Be("No results");
    }

    [Fact]
    public void SelectedSearchModeChanged_runs_search_immediately()
    {
        TaskCompletionSource debounce = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindowViewModel viewModel = CreateViewModel(_ => debounce.Task);

        viewModel.SearchText = "shared query";
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Examples");

        viewModel.SearchResults.Should().ContainSingle();
        viewModel.SearchResults[0].Kind.Should().Be("Example");
    }

    [Fact]
    public void RunModeSearchCommand_populates_results_for_selected_mode()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "shared query";
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Examples");

        viewModel.RunModeSearchCommand.Execute(null);

        viewModel.SearchResults.Should().ContainSingle();
        viewModel.SearchResults[0].Kind.Should().Be("Example");
        viewModel.SearchResults[0].Description.Should().Be("shared query");
        viewModel.ResultCountText.Should().Be("1");
        viewModel.ContentTitle.Should().Be("Example search");
    }

    [Fact]
    public void RunAdvancedSearch_applies_filters_to_lucene_results()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SearchModeOption headwords = viewModel.SearchModes.Single(mode => mode.DisplayName == "Headwords");
        viewModel.SelectedSearchMode = headwords;
        viewModel.SearchText = "filtered target";

        viewModel.ApplyAdvancedSearchFilters(SearchFilterQuery.Create([["426"]]));

        viewModel.SearchResults.Should().ContainSingle();
        viewModel.SearchResults[0].Path.Should().Be("/fs/academic");
        viewModel.ContentTitle.Should().Be("Advanced Headwords");
        viewModel.SearchText.Should().Be("filtered target");
        viewModel.SelectedSearchMode.Should().BeSameAs(headwords);
        viewModel.HasActiveSearchFilters.Should().BeTrue();
    }

    [Fact]
    public void RunAdvancedSearch_supports_filter_only_queries()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SearchModeOption headwords = viewModel.SearchModes.Single(mode => mode.DisplayName == "Headwords");
        viewModel.SelectedSearchMode = headwords;

        viewModel.ApplyAdvancedSearchFilters(SearchFilterQuery.Create([["426"]]));

        viewModel.SearchResults.Should().ContainSingle();
        viewModel.SearchResults[0].Path.Should().Be("/fs/academic");
        viewModel.ContentTitle.Should().Be("Advanced Headwords");
    }

    [Fact]
    public void Active_advanced_filters_are_applied_to_all_mode_search_box_queries()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.ApplyAdvancedSearchFilters(SearchFilterQuery.Create([["426"]]));

        viewModel.SearchText = "filtered target";

        viewModel.SearchResults.Should().ContainSingle();
        viewModel.SearchResults[0].Path.Should().Be("/fs/academic");
        viewModel.SelectedSearchMode.DisplayName.Should().Be("Headwords");
        viewModel.ContentTitle.Should().Be("Advanced Headwords");
        viewModel.HasActiveSearchFilters.Should().BeTrue();
    }

    [Fact]
    public void ClearAdvancedSearchFilters_restores_unfiltered_main_window_search()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SearchModeOption headwords = viewModel.SearchModes.Single(mode => mode.DisplayName == "Headwords");
        viewModel.SelectedSearchMode = headwords;
        viewModel.SearchText = "filtered target";
        viewModel.ApplyAdvancedSearchFilters(SearchFilterQuery.Create([["426"]]));

        viewModel.ClearAdvancedSearchFilters();

        viewModel.SelectedSearchMode.DisplayName.Should().Be("All");
        viewModel.HasActiveSearchFilters.Should().BeFalse();
    }

    [Fact]
    public void ClearAdvancedSearchFilters_keeps_explicit_non_headword_mode()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SearchModeOption definitions = viewModel.SearchModes.Single(mode => mode.DisplayName == "Definitions");
        viewModel.SelectedSearchMode = definitions;
        viewModel.SearchText = "shared query";
        viewModel.ApplyAdvancedSearchFilters(SearchFilterQuery.Create([["426"]]));

        viewModel.ClearAdvancedSearchFilters();

        viewModel.SelectedSearchMode.Should().BeSameAs(definitions);
        viewModel.ContentTitle.Should().Be("Definition search");
    }

    [Fact]
    public void LookupContentCommand_opens_best_incremental_result()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Examples");

        viewModel.LookupContentCommand.Execute("hello");

        viewModel.SelectedSearchMode.DisplayName.Should().Be("All");
        viewModel.SearchText.Should().Be("hello");
        viewModel.SelectedResult.Should().NotBeNull();
        viewModel.SelectedResult!.Path.Should().Be("/fs/work");
        viewModel.ContentTitle.Should().Be("work");
    }

    [Fact]
    public void GetCurrentEntryWordForCopy_returns_base_word_from_displayed_entry_title()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.CurrentContentPath = "/fs/hello";
        viewModel.ContentTitle = "hel\u00b7lo (interjection, noun)";

        string? copyText = viewModel.GetCurrentEntryWordForCopy();

        copyText.Should().Be("hello");
        viewModel.CanCopyCurrentEntryWord.Should().BeTrue();
    }

    [Fact]
    public void GetCurrentEntryWordForCopy_returns_null_when_displayed_content_is_not_an_entry()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.CurrentContentPath = "/examples/hello";
        viewModel.ContentTitle = "hello";

        string? copyText = viewModel.GetCurrentEntryWordForCopy();

        copyText.Should().BeNull();
        viewModel.CanCopyCurrentEntryWord.Should().BeFalse();
    }

#if DEBUG
    [Fact]
    public void LoadCurrentRawXml_returns_xml_for_displayed_content()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "earth";
        viewModel.SelectedResult = viewModel.SearchResults.Single();

        string rawXml = viewModel.LoadCurrentRawXml();

        viewModel.CurrentContentPath.Should().Be("/fs/word");
        rawXml.Should().Contain("<BASE>word</BASE>");
        rawXml.Should().Contain("<DEF>First entry.</DEF>");
    }

    [Fact]
    public void FormatCurrentEntryInfo_returns_title_and_path_for_displayed_content()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "earth";
        viewModel.SelectedResult = viewModel.SearchResults.Single();

        string entryInfo = viewModel.FormatCurrentEntryInfo();

        entryInfo.Should().Be($"Title: word{Environment.NewLine}Path: /fs/word");
    }
#endif

    [Fact]
    public void RunModeSearchCommand_is_disabled_for_empty_query()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.RunModeSearchCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SearchTextChanged_runs_headword_wildcard_search_when_all_mode_is_selected()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        Task searchCompleted = WaitForSearchCompletedAsync(viewModel);

        viewModel.SearchText = "wor*";

        await searchCompleted.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchResults.Select(result => result.Path).Should().Equal("/fs/word", "/fs/work");
        viewModel.ContentTitle.Should().Be("Headword search");
    }

    [Fact]
    public async Task SearchTextChanged_filters_headword_wildcard_search_by_sort_key()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Headwords");
        Task searchCompleted = WaitForSearchCompletedAsync(viewModel);

        viewModel.SearchText = "wor*";

        await searchCompleted.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchResults.Select(result => result.Path).Should().Equal("/fs/word", "/fs/work");
        viewModel.ContentTitle.Should().Be("Headword search");
    }

    [Fact]
    public async Task SearchTextChanged_shows_spell_suggestions_after_no_result_single_word_search()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        Task suggestionsOpened = WaitForPropertyAsync(
            viewModel,
            nameof(MainWindowViewModel.IsSpellSuggestionPopupOpen),
            () => viewModel.IsSpellSuggestionPopupOpen);

        viewModel.SearchText = "worl";

        await suggestionsOpened.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchResults.Should().BeEmpty();
        viewModel.SpellSuggestions.Should().Contain("word");
    }

    [Fact]
    public async Task SearchTextChanged_suppresses_spell_suggestions_when_results_exist()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        Task searchCompleted = WaitForSearchCompletedAsync(viewModel);

        viewModel.SearchText = "word";

        await searchCompleted.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchResults.Should().NotBeEmpty();
        viewModel.SpellSuggestions.Should().BeEmpty();
        viewModel.IsSpellSuggestionPopupOpen.Should().BeFalse();
    }

    [Fact]
    public async Task SearchTextChanged_clears_stale_spell_suggestions()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        Task suggestionsOpened = WaitForPropertyAsync(
            viewModel,
            nameof(MainWindowViewModel.IsSpellSuggestionPopupOpen),
            () => viewModel.IsSpellSuggestionPopupOpen);
        viewModel.SearchText = "worl";
        await suggestionsOpened.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        viewModel.SearchText = "changed";

        viewModel.SpellSuggestions.Should().BeEmpty();
        viewModel.IsSpellSuggestionPopupOpen.Should().BeFalse();
    }

    [Fact]
    public async Task ApplySpellSuggestionCommand_updates_search_text_and_runs_search()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        Task suggestionsOpened = WaitForPropertyAsync(
            viewModel,
            nameof(MainWindowViewModel.IsSpellSuggestionPopupOpen),
            () => viewModel.IsSpellSuggestionPopupOpen);
        viewModel.SearchText = "worl";
        await suggestionsOpened.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Task searchCompleted = WaitForSearchCompletedAsync(viewModel);

        viewModel.ApplySpellSuggestionCommand.Execute("word");

        await searchCompleted.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchText.Should().Be("word");
        viewModel.IsSpellSuggestionPopupOpen.Should().BeFalse();
        viewModel.SearchResults.Select(result => result.Path).Should().Contain("/fs/word");
    }

    [Fact]
    public void NavigateBack_and_forward_restore_content_history()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "shared";
        viewModel.SelectedResult = viewModel.SearchResults.Single();
        viewModel.NavigateContentCommand.Execute("/fs/work");

        viewModel.CanNavigateBack.Should().BeTrue();
        viewModel.NavigateBackCommand.Execute(null);
        viewModel.ContentTitle.Should().Be("word");

        viewModel.CanNavigateForward.Should().BeTrue();
        viewModel.NavigateForwardCommand.Execute(null);
        viewModel.ContentTitle.Should().Be("work");
    }

    [Fact]
    public void NavigateBack_and_forward_update_visible_selected_result()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SearchResultItemViewModel word = new("word", "Headword", "/fs/word", "/fs/word", "word", 1, "hm");
        SearchResultItemViewModel work = new("work", "Headword", "/fs/work", "/fs/work", "work", 1, "hm");
        viewModel.SearchResults.Add(word);
        viewModel.SearchResults.Add(work);

        viewModel.SelectedResult = word;
        viewModel.NavigateContentCommand.Execute("/fs/work");

        viewModel.SelectedResult.Should().BeSameAs(work);
        viewModel.NavigateBackCommand.Execute(null);
        viewModel.SelectedResult.Should().BeSameAs(word);

        viewModel.NavigateForwardCommand.Execute(null);
        viewModel.SelectedResult.Should().BeSameAs(work);
    }

    [Fact]
    public void Manual_selection_keeps_selected_row_when_visible_results_share_path()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SearchResultItemViewModel headword = new("word", "Headword", "/fs/word", "/fs/word", "word", 1, "hm");
        SearchResultItemViewModel variant = new("words -> word", "Variant", "/fs/word", "/fs/word", "words", 2, "hv");
        viewModel.SearchResults.Add(headword);
        viewModel.SearchResults.Add(variant);

        viewModel.SelectedResult = variant;

        viewModel.SelectedResult.Should().BeSameAs(variant);
        viewModel.ContentTitle.Should().Be("word");
    }

    [Fact]
    public void Internal_navigation_preserves_selected_row_when_no_visible_result_matches()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SearchResultItemViewModel word = new("word", "Headword", "/fs/word", "/fs/word", "word", 1, "hm");
        viewModel.SearchResults.Add(word);
        viewModel.SelectedResult = word;

        viewModel.NavigateContentCommand.Execute("/fs/work");

        viewModel.SelectedResult.Should().BeSameAs(word);
        viewModel.ContentTitle.Should().Be("work");
    }

    [Fact]
    public void NavigateBack_and_forward_restore_search_query_for_selected_content()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "earth";
        viewModel.SelectedResult = viewModel.SearchResults.Single();

        viewModel.SearchText = "hello";
        viewModel.SelectedResult = viewModel.SearchResults.Single();

        viewModel.NavigateBackCommand.Execute(null);
        viewModel.SearchText.Should().Be("earth");
        viewModel.SelectedResult.Should().NotBeNull();
        viewModel.SelectedResult!.Path.Should().Be("/fs/word");

        viewModel.NavigateForwardCommand.Execute(null);
        viewModel.SearchText.Should().Be("hello");
        viewModel.SelectedResult.Should().NotBeNull();
        viewModel.SelectedResult!.Path.Should().Be("/fs/work");
    }

    [Fact]
    public void BackHistoryMenuEntries_returns_twenty_recent_previous_entries()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        for (int i = 0; i < 25; i++)
        {
            viewModel.NavigateContentCommand.Execute($"/fs/item{i:00}");
        }

        viewModel.BackHistoryMenuEntries.Should().HaveCount(20);
        viewModel.BackHistoryMenuEntries.Select(entry => entry.DisplayText)
            .Should()
            .Equal(Enumerable.Range(4, 20).Reverse().Select(i => $"item{i:00}"));
        viewModel.BackHistoryMenuEntries.Select(entry => entry.Path)
            .Should()
            .Equal(Enumerable.Range(4, 20).Reverse().Select(i => $"/fs/item{i:00}"));
    }

    [Fact]
    public void NavigateHistoryEntryCommand_jumps_to_selected_history_entry()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        for (int i = 0; i < 6; i++)
        {
            viewModel.NavigateContentCommand.Execute($"/fs/item{i:00}");
        }

        ContentHistoryMenuItemViewModel entry = viewModel.BackHistoryMenuEntries
            .Single(item => item.Path == "/fs/item02");

        viewModel.NavigateHistoryEntryCommand.Execute(entry);

        viewModel.ContentTitle.Should().Be("item02");
        viewModel.CurrentContentPath.Should().Be("/fs/item02");
        viewModel.BackHistoryMenuEntries.Select(item => item.Path).Should().StartWith("/fs/item01");
        viewModel.ForwardHistoryMenuEntries.Select(item => item.Path).Should().StartWith("/fs/item03");
    }

    [Fact]
    public void Content_history_is_limited_to_twenty_entries()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        for (int i = 0; i < 105; i++)
        {
            viewModel.NavigateContentCommand.Execute($"/fs/item{i:00}");
        }

        viewModel.ContentHistoryCount.Should().Be(100);
        for (int i = 0; i < 99; i++)
        {
            viewModel.NavigateBackCommand.Execute(null);
        }

        viewModel.ContentTitle.Should().Be("item05");
        viewModel.CanNavigateBack.Should().BeFalse();
    }

    [Fact]
    public void PlayAudioCommand_passes_configurable_volume_to_audio_player()
    {
        AddArchiveItem("gb_hwd_pron", "manual.mp3", "fake audio bytes");
        WriteFilemap();
        TestAudioPlayer audioPlayer = new();
        MainWindowViewModel viewModel = CreateViewModel(audioPlayer: audioPlayer);
        viewModel.Config.AudioPlaybackVolume = 25;
        DictionaryResourceRef resource = new("gb_hwd_pron", "manual.mp3", "audio/mpeg");

        viewModel.PlayAudioCommand.Execute(resource);

        audioPlayer.PlayCount.Should().Be(1);
        audioPlayer.MediaType.Should().Be("audio/mpeg");
        audioPlayer.Volume.Should().Be(0.25);
        audioPlayer.Data.Should().Equal(Encoding.UTF8.GetBytes("fake audio bytes"));
    }

    [Fact]
    public void SelectedResult_does_not_play_pronunciation_when_automatic_playback_is_disabled()
    {
        TestAudioPlayer audioPlayer = new();
        MainWindowViewModel viewModel = CreateViewModel(audioPlayer: audioPlayer);
        viewModel.SearchText = "earth";

        viewModel.SelectedResult = viewModel.SearchResults.Single();

        viewModel.Config.IsAutoPronunciationPlaybackEnabled.Should().BeFalse();
        audioPlayer.PlayCount.Should().Be(0);
    }

    [Fact]
    public void PlayCurrentPronunciationCommand_plays_british_pronunciation_by_default_when_automatic_playback_is_disabled()
    {
        TestAudioPlayer audioPlayer = new();
        MainWindowViewModel viewModel = CreateViewModel(audioPlayer: audioPlayer);
        viewModel.SearchText = "earth";
        viewModel.SelectedResult = viewModel.SearchResults.Single();

        viewModel.PlayCurrentPronunciationCommand.Execute(null);

        viewModel.Config.IsAutoPronunciationPlaybackEnabled.Should().BeFalse();
        viewModel.Config.PronunciationPlayback.Should().Be(PronunciationPlayback.British);
        audioPlayer.PlayCount.Should().Be(1);
        audioPlayer.Data.Should().Equal(Encoding.UTF8.GetBytes("gb automatic audio"));
        audioPlayer.MediaType.Should().Be("audio/mpeg");
    }

    [Fact]
    public void PlayCurrentPronunciationCommand_plays_selected_american_pronunciation()
    {
        TestAudioPlayer audioPlayer = new();
        MainWindowViewModel viewModel = CreateViewModel(audioPlayer: audioPlayer);
        viewModel.Config.PronunciationPlayback = PronunciationPlayback.American;
        viewModel.SearchText = "earth";
        viewModel.SelectedResult = viewModel.SearchResults.Single();

        viewModel.PlayCurrentPronunciationCommand.Execute(null);

        audioPlayer.PlayCount.Should().Be(1);
        audioPlayer.Data.Should().Equal(Encoding.UTF8.GetBytes("us automatic audio"));
        audioPlayer.MediaType.Should().Be("audio/mpeg");
    }

    [Fact]
    public void SelectedResult_plays_british_pronunciation_when_automatic_playback_is_british()
    {
        TestAudioPlayer audioPlayer = new();
        MainWindowViewModel viewModel = CreateViewModel(audioPlayer: audioPlayer);
        viewModel.Config.IsAutoPronunciationPlaybackEnabled = true;
        viewModel.Config.PronunciationPlayback = PronunciationPlayback.British;
        viewModel.SearchText = "earth";

        viewModel.SelectedResult = viewModel.SearchResults.Single();

        audioPlayer.PlayCount.Should().Be(1);
        audioPlayer.Data.Should().Equal(Encoding.UTF8.GetBytes("gb automatic audio"));
        audioPlayer.MediaType.Should().Be("audio/mpeg");
    }

    [Fact]
    public void SelectedResult_plays_american_pronunciation_when_automatic_playback_is_american()
    {
        TestAudioPlayer audioPlayer = new();
        MainWindowViewModel viewModel = CreateViewModel(audioPlayer: audioPlayer);
        viewModel.Config.IsAutoPronunciationPlaybackEnabled = true;
        viewModel.Config.PronunciationPlayback = PronunciationPlayback.American;
        viewModel.SearchText = "earth";

        viewModel.SelectedResult = viewModel.SearchResults.Single();

        audioPlayer.PlayCount.Should().Be(1);
        audioPlayer.Data.Should().Equal(Encoding.UTF8.GetBytes("us automatic audio"));
        audioPlayer.MediaType.Should().Be("audio/mpeg");
    }

    [Fact]
    public void AutoPronunciationPlayback_option_updates_configuration()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.IsAutoPronunciationPlaybackEnabled = true;
        viewModel.Dispose();
        AppConfiguration loaded = AppConfiguration.Load(_paths);

        loaded.IsAutoPronunciationPlaybackEnabled.Should().BeTrue();
    }

    [Fact]
    public void PronunciationPlayback_option_updates_menu_flags_and_configuration()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.IsAmericanPronunciationPlaybackSelected = true;
        viewModel.Dispose();
        AppConfiguration loaded = AppConfiguration.Load(_paths);

        viewModel.Config.IsBritishPronunciationPlaybackSelected.Should().BeFalse();
        viewModel.Config.IsAmericanPronunciationPlaybackSelected.Should().BeTrue();
        loaded.PronunciationPlayback.Should().Be(PronunciationPlayback.American);
    }

    [Fact]
    public void ClipboardMonitoring_option_updates_configuration()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.IsClipboardMonitoringEnabled = true;
        viewModel.Dispose();
        AppConfiguration loaded = AppConfiguration.Load(_paths);

        loaded.IsClipboardMonitoringEnabled.Should().BeTrue();
    }

    [Fact]
    public void FullIndexClipboardSearch_option_updates_configuration()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.IsFullIndexClipboardSearchEnabled = true;
        viewModel.Dispose();
        AppConfiguration loaded = AppConfiguration.Load(_paths);

        loaded.IsFullIndexClipboardSearchEnabled.Should().BeTrue();
    }

    [Fact]
    public void ThemeMode_loads_from_configuration()
    {
        AppConfiguration config = AppConfiguration.Load(_paths);
        config.ThemeMode = ThemeMode.Dark;
        config.Save();

        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.ThemeMode.Should().Be(ThemeMode.Dark);
        viewModel.Config.IsAutomaticThemeModeSelected.Should().BeFalse();
        viewModel.Config.IsLightThemeModeSelected.Should().BeFalse();
        viewModel.Config.IsDarkThemeModeSelected.Should().BeTrue();
    }

    [Fact]
    public void ThemeMode_option_updates_menu_flags_and_configuration()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.IsAutomaticThemeModeSelected = true;
        viewModel.Dispose();
        AppConfiguration loaded = AppConfiguration.Load(_paths);

        viewModel.Config.ThemeMode.Should().Be(ThemeMode.Automatic);
        viewModel.Config.IsAutomaticThemeModeSelected.Should().BeTrue();
        viewModel.Config.IsLightThemeModeSelected.Should().BeFalse();
        viewModel.Config.IsDarkThemeModeSelected.Should().BeFalse();
        loaded.ThemeMode.Should().Be(ThemeMode.Automatic);
    }

    [Fact]
    public void ZoomPower_loads_from_configuration()
    {
        AppConfiguration config = AppConfiguration.Load(_paths);
        config.ZoomPower = 4;
        config.Save();

        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.ZoomPower.Should().Be(4);
        viewModel.Config.ZoomFactor.Should().BeApproximately(Math.Pow(1.05, 4), 0.000001);
        viewModel.Config.ZoomStatusText.Should().Be("Zoom 122%");
    }

    [Fact]
    public void Zoom_commands_update_factor_and_status_text()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.ZoomInCommand.Execute(null);

        viewModel.Config.ZoomPower.Should().Be(1);
        viewModel.Config.ZoomFactor.Should().BeApproximately(1.05, 0.000001);
        viewModel.Config.ZoomStatusText.Should().Be("Zoom 105%");
        viewModel.Config.ZoomPercentageText.Should().Be("105%");
        viewModel.Config.ZoomInMenuHeader.Should().Be("Zoom In (105%)");
        viewModel.Config.ZoomOutMenuHeader.Should().Be("Zoom Out (105%)");
        viewModel.Config.NormalSizeMenuHeader.Should().Be("Normal Size (105%)");

        viewModel.ZoomOutCommand.Execute(null);
        viewModel.ZoomOutCommand.Execute(null);

        viewModel.Config.ZoomPower.Should().Be(-1);
        viewModel.Config.ZoomFactor.Should().BeApproximately(Math.Pow(1.05, -1), 0.000001);
        viewModel.Config.ZoomStatusText.Should().Be("Zoom 95%");
        viewModel.Config.ZoomPercentageText.Should().Be("95%");
    }

    [Fact]
    public void NormalSizeCommand_resets_zoom()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.ZoomInCommand.Execute(null);

        viewModel.NormalSizeCommand.Execute(null);

        viewModel.Config.ZoomPower.Should().Be(0);
        viewModel.Config.ZoomFactor.Should().Be(1.0);
        viewModel.Config.ZoomStatusText.Should().Be("Zoom 100%");

        viewModel.ZoomInCommand.Execute(null);
        viewModel.NormalSizeCommand.Execute(null);

        viewModel.Config.ZoomPower.Should().Be(0);
        viewModel.Config.ZoomPercentageText.Should().Be("100%");
    }

    [Fact]
    public void Zoom_commands_clamp_to_legacy_range()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.Config.ZoomPower = 20;

        viewModel.ZoomInCommand.Execute(null);

        viewModel.Config.ZoomPower.Should().Be(20);

        viewModel.Config.ZoomPower = -10;
        viewModel.ZoomOutCommand.Execute(null);

        viewModel.Config.ZoomPower.Should().Be(-10);
    }

    [Fact]
    public void ZoomPower_updates_configuration()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.ZoomInCommand.Execute(null);
        viewModel.Dispose();
        AppConfiguration loaded = AppConfiguration.Load(_paths);

        loaded.ZoomPower.Should().Be(1);
    }

    [Fact]
    public void Display_settings_load_from_configuration()
    {
        AppConfiguration config = AppConfiguration.Load(_paths);
        config.AudioPlaybackVolume = 55;
        config.SearchListWidth = 420;
        config.SearchListBaseFontSize = 17;
        config.ContentBaseFontSize = 19;
        config.CustomFontFamilyName = "Segoe UI";
        config.IsExampleItalicEnabled = false;
        config.ExampleFontFamilyName = "Georgia";
        config.Save();

        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.AudioPlaybackVolume.Should().Be(55);
        viewModel.Config.SearchListWidth.Should().Be(420);
        viewModel.Config.SearchListBaseFontSize.Should().Be(17);
        viewModel.Config.ContentBaseFontSize.Should().Be(19);
        viewModel.Config.CustomFontFamilyName.Should().Be("Segoe UI");
        viewModel.Config.IsExampleItalicEnabled.Should().BeFalse();
        viewModel.Config.ExampleFontFamilyName.Should().Be("Georgia");
    }

    [Fact]
    public void Display_settings_update_configuration()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.AudioPlaybackVolume = 60;
        viewModel.Config.SearchListWidth = 350;
        viewModel.Config.SearchListBaseFontSize = 16;
        viewModel.Config.ContentBaseFontSize = 18;
        viewModel.Config.CustomFontFamilyName = "  Segoe UI  ";
        viewModel.Config.IsExampleItalicEnabled = false;
        viewModel.Config.ExampleFontFamilyName = "  Georgia  ";
        viewModel.Dispose();
        AppConfiguration loaded = AppConfiguration.Load(_paths);

        loaded.AudioPlaybackVolume.Should().Be(60);
        loaded.SearchListWidth.Should().Be(350);
        loaded.SearchListBaseFontSize.Should().Be(16);
        loaded.ContentBaseFontSize.Should().Be(18);
        loaded.CustomFontFamilyName.Should().Be("Segoe UI");
        loaded.IsExampleItalicEnabled.Should().BeFalse();
        loaded.ExampleFontFamilyName.Should().Be("Georgia");
    }

    [Fact]
    public void ApplicationFontFamily_falls_back_when_custom_font_is_empty()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        string fallbackFontFamily = viewModel.Config.ApplicationFontFamily.ToString();

        viewModel.Config.CustomFontFamilyName = string.Empty;
        viewModel.Config.ApplicationFontFamily.ToString().Should().Be(fallbackFontFamily);

        viewModel.Config.CustomFontFamilyName = "   ";
        viewModel.Config.ApplicationFontFamily.ToString().Should().Be(fallbackFontFamily);
    }

    [Fact]
    public void ApplicationFontFamily_falls_back_when_custom_font_is_unavailable()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        string fallbackFontFamily = viewModel.Config.ApplicationFontFamily.ToString();

        viewModel.Config.CustomFontFamilyName = "Definitely Missing Font 3D534D2F07BC45B98B40";

        viewModel.Config.ApplicationFontFamily.ToString().Should().Be(fallbackFontFamily);
    }

    [Fact]
    public void ExampleFontFamily_falls_back_to_application_font_when_example_font_is_empty_or_unavailable()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        string applicationFontFamily = viewModel.Config.ApplicationFontFamily.ToString();

        viewModel.Config.ExampleFontFamilyName = string.Empty;
        viewModel.Config.ExampleFontFamily.ToString().Should().Be(applicationFontFamily);

        viewModel.Config.ExampleFontFamilyName = "Definitely Missing Example Font 7D1E9A4A6FD84B5D";
        viewModel.Config.ExampleFontFamily.ToString().Should().Be(applicationFontFamily);
    }

    [Fact]
    public void Display_settings_clamp_to_supported_ranges()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Config.AudioPlaybackVolume = 0;
        viewModel.Config.SearchListWidth = 100;
        viewModel.Config.SearchListBaseFontSize = 4;
        viewModel.Config.ContentBaseFontSize = 5;

        viewModel.Config.AudioPlaybackVolume.Should().Be(1);
        viewModel.Config.SearchListWidth.Should().Be(180);
        viewModel.Config.SearchListBaseFontSize.Should().Be(10);
        viewModel.Config.ContentBaseFontSize.Should().Be(10);

        viewModel.Config.AudioPlaybackVolume = 200;
        viewModel.Config.SearchListWidth = 900;
        viewModel.Config.SearchListBaseFontSize = 40;
        viewModel.Config.ContentBaseFontSize = 50;

        viewModel.Config.AudioPlaybackVolume.Should().Be(100);
        viewModel.Config.SearchListWidth.Should().Be(600);
        viewModel.Config.SearchListBaseFontSize.Should().Be(24);
        viewModel.Config.ContentBaseFontSize.Should().Be(28);
    }

    [Fact]
    public void Display_setting_reset_commands_restore_defaults()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.Config.SearchListWidth = 450;
        viewModel.Config.SearchListBaseFontSize = 18;
        viewModel.Config.ContentBaseFontSize = 20;

        viewModel.ResetSearchListWidthCommand.Execute(null);
        viewModel.ResetSearchListBaseFontSizeCommand.Execute(null);
        viewModel.ResetContentBaseFontSizeCommand.Execute(null);

        viewModel.Config.SearchListWidth.Should().Be(300);
        viewModel.Config.SearchListBaseFontSize.Should().Be(14);
        viewModel.Config.ContentBaseFontSize.Should().Be(15);
    }

    [Fact]
    public void Dispose_does_not_save_configuration_when_user_settings_are_unchanged()
    {
        DateTime originalWriteTime = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_paths.ConfigurationPath, originalWriteTime);
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.Dispose();

        File.GetLastWriteTimeUtc(_paths.ConfigurationPath).Should().Be(originalWriteTime);
    }

    [Fact]
    public void TrySearchClipboardText_searches_normalized_incremental_match()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SelectedSearchMode = viewModel.SearchModes.Single(mode => mode.DisplayName == "Examples");

        bool searched = viewModel.TrySearchClipboardText("multi\r\nline");

        searched.Should().BeTrue();
        viewModel.SelectedSearchMode.DisplayName.Should().Be("All");
        viewModel.SearchText.Should().Be("multi line");
        viewModel.SelectedResult.Should().NotBeNull();
        viewModel.SelectedResult!.Path.Should().Be("/fs/word");
        viewModel.ContentTitle.Should().Be("word");
    }

    [Fact]
    public void TrySearchClipboardText_ignores_text_without_incremental_match()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "earth";

        bool searched = viewModel.TrySearchClipboardText("not in the index");

        searched.Should().BeFalse();
        viewModel.SearchText.Should().Be("earth");
    }

    [Fact]
    public void TrySearchClipboardText_ignores_full_text_only_match_when_full_index_clipboard_search_is_disabled()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SearchText = "earth";

        bool searched = viewModel.TrySearchClipboardText("located in");

        searched.Should().BeFalse();
        viewModel.Config.IsFullIndexClipboardSearchEnabled.Should().BeFalse();
        viewModel.SearchText.Should().Be("earth");
    }

    [Fact]
    public async Task TrySearchClipboardText_searches_full_text_only_match_when_full_index_clipboard_search_is_enabled()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.Config.IsFullIndexClipboardSearchEnabled = true;
        Task searchCompleted = WaitForSearchCompletedAsync(viewModel);

        bool searched = viewModel.TrySearchClipboardText("located in");

        searched.Should().BeTrue();
        await searchCompleted.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchText.Should().Be("located in");
        viewModel.SearchResults.Select(result => result.Path).Should().Contain("/fs/located-in");
    }

    [Fact]
    public async Task TrySearchClipboardText_uses_variations_for_full_index_clipboard_search()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.Config.IsFullIndexClipboardSearchEnabled = true;
        Task searchCompleted = WaitForSearchCompletedAsync(viewModel);

        bool searched = viewModel.TrySearchClipboardText("divided into");

        searched.Should().BeTrue();
        await searchCompleted.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        viewModel.SearchText.Should().Be("divided into");
        viewModel.SearchResults.Select(result => result.Path).Should().Contain("/fs/divide-into");
    }

    [Fact]
    public void TrySearchClipboardText_ignores_text_longer_than_one_hundred_characters()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        string longText = new('a', 101);

        bool searched = viewModel.TrySearchClipboardText(longText);

        searched.Should().BeFalse();
        viewModel.SearchText.Should().BeEmpty();
    }

    public void Dispose()
    {
        foreach (MainWindowViewModel viewModel in _viewModels)
        {
            viewModel.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private MainWindowViewModel CreateViewModel(
        Func<CancellationToken, Task>? searchDebounceDelay = null,
        IAudioPlayer? audioPlayer = null,
        string? productVersion = null)
    {
        MainWindowViewModel viewModel = new(
            _paths,
            dispatchUiUpdates: false,
            searchDebounceDelay: searchDebounceDelay ?? (_ => Task.CompletedTask),
            spellSuggestionDelay: _ => Task.CompletedTask,
            audioPlayer: audioPlayer,
            productVersion: productVersion);
        _viewModels.Add(viewModel);
        return viewModel;
    }

    private static string FlattenDocumentText(DictionaryDocument document)
    {
        return string.Concat(document.Blocks.SelectMany(FlattenBlockText));
    }

    private static IEnumerable<string> FlattenBlockText(DictionaryBlock block)
    {
        return block switch
        {
            DictionaryParagraphBlock paragraph => FlattenInlineText(paragraph.Inlines),
            DictionaryHeadingBlock heading => FlattenInlineText(heading.Inlines),
            DictionaryContainerBlock container => container.Blocks.SelectMany(FlattenBlockText),
            _ => [],
        };
    }

    private static IEnumerable<string> FlattenInlineText(IReadOnlyList<DictionaryInline> inlines)
    {
        return inlines.SelectMany(inline => inline switch
        {
            DictionaryTextInline text => [text.Text],
            DictionaryLineBreakInline => [Environment.NewLine],
            DictionaryLinkInline link => FlattenInlineText(link.Inlines),
            _ => [],
        });
    }

    private void AddArchiveItem(string archive, string name, string xml)
    {
        string contentPath = Path.Combine(_dataDirectory, archive + ".skn", "files.skn", "CONTENT.tda");
        Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);

        byte[] raw = Encoding.UTF8.GetBytes(xml);
        byte[] compressed = Compress(raw);
        long compressedOffset = 0;
        if (File.Exists(contentPath))
        {
            compressedOffset = new FileInfo(contentPath).Length;
        }

        using (FileStream stream = File.Open(contentPath, FileMode.Append, FileAccess.Write))
        {
            stream.Write(compressed);
        }

        ArchiveLocation location = new(checked((int)compressedOffset), compressed.Length, 0, raw.Length);
        _filemapEntries.Add((archive, name, location));
    }

    private void WriteFilemap()
    {
        using FileStream stream = File.Create(_paths.FilemapPath);
        ConstantDatabaseWriter writer = new(stream);
        foreach ((string archive, string name, ArchiveLocation location) in _filemapEntries)
        {
            writer.Add(CreateFilemapKey(archive, name), PackLocation(location));
        }

        writer.FinalizeDatabase();
    }

    private static byte[] Compress(byte[] raw)
    {
        using MemoryStream destination = new();
        using (System.IO.Compression.ZLibStream zlib = new(destination, System.IO.Compression.CompressionLevel.SmallestSize))
        {
            zlib.Write(raw);
        }

        return destination.ToArray();
    }

    private static byte[] CreateFilemapKey(string archive, string name)
    {
        byte[] hash = MD5.HashData(Encoding.ASCII.GetBytes($"{archive}:{name}"));
        byte[] key = new byte[10];
        Buffer.BlockCopy(hash, 0, key, 0, key.Length);
        return key;
    }

    private static byte[] PackLocation(ArchiveLocation location)
    {
        byte[] data = new byte[10];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), checked((uint)location.CompressedOffset));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), checked((ushort)location.CompressedSize));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, 2), checked((ushort)location.OriginalOffset));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), checked((ushort)location.OriginalSize));
        return data;
    }

    private sealed class TestAudioPlayer : IAudioPlayer
    {
        public int PlayCount { get; private set; }

        public byte[] Data { get; private set; } = [];

        public string MediaType { get; private set; } = string.Empty;

        public double Volume { get; private set; }

        public void Play(byte[] data, string mediaType, double volume)
        {
            PlayCount++;
            Data = data;
            MediaType = mediaType;
            Volume = volume;
        }

        public void Dispose()
        {
        }
    }

    private static Task WaitForPropertyAsync(
        MainWindowViewModel viewModel,
        string propertyName,
        Func<bool> predicate)
    {
        if (predicate())
        {
            return Task.CompletedTask;
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += OnPropertyChanged;
        return completion.Task;

        void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == propertyName && predicate())
            {
                viewModel.PropertyChanged -= OnPropertyChanged;
                completion.SetResult();
            }
        }
    }

    private static Task WaitForSearchCompletedAsync(MainWindowViewModel viewModel)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.SearchCompleted += OnSearchCompleted;
        return completion.Task;

        void OnSearchCompleted(object? sender, EventArgs e)
        {
            viewModel.SearchCompleted -= OnSearchCompleted;
            completion.SetResult();
        }
    }
}
