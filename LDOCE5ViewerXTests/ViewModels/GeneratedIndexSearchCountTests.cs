using AwesomeAssertions;
using AwesomeAssertions.Execution;

using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.ViewModels;

public sealed class GeneratedIndexSearchCountTests
{
    public static TheoryData<SearchCountCase> SearchCountCases => new()
    {
        new(new("test", 227, 32, 0, 108, 12, 223, 341)),
        new(new("hello", 20, 5, 0, 4, 2, 18, 30)),
        new(new("devil", 42, 6, 0, 5, 19, 24, 23)),
        new(new("café", 18, 3, 0, 9, 1, 7, 33)),
        new(new("façade", 7, 1, 0, 4, 0, 0, 4)),
        new(new("fiancé", 5, 1, 0, 0, 0, 0, 3)),
        new(new("coöperate", 12, 1, 0, 6, 0, 1, 10)), // Old - All: 11
        new(new("long-term", 35, 1, 0, 31, 2, 0, 63)),
        new(new("mother-in-law", 3, 1, 0, 0, 0, 2, 1)), // Old - All: 2
        new(new("co-operate", 13, 0, 0, 2, 0, 0, 5)),
        new(new("cooperate", 12, 1, 0, 6, 0, 1, 10)), // Old - All: 11
        new(new("jack-o'-lantern", 2, 1, 0, 0, 0, 0, 0)),
        new(new("what the hell", 2, 0, 0, 0, 2, 0, 2)),
        new(new("look after", 11, 0, 1, 2, 3, 163, 78)),
        new(new("found out", 25, 0, 1, 15, 4, 307, 129)),
        new(new("take a break", 5, 0, 0, 2, 1, 6, 23)),
        new(new("take a shower", 2, 0, 0, 1, 1, 1, 6)),
        new(new("was in charge of", 1, 0, 0, 0, 0, 201, 19)),

        new(new("word*", 18, 18, 0, 19, 4, 1313, 457)), // skip match for definition/example?
        new(new("swear*", 5, 5, 2, 16, 3, 33, 29)),
        new(new("penny*", 9, 9, 0, 1, 0, 14, 31)),
        new(new("mother*", 30, 30, 0, 6, 1, 164, 469)),
        new(new("*rest", 33, 33, 0, 97, 25, 481, 756)),
        new(new("*pt", 72, 72, 0, 102, 5, 874, 973)),
        new(new("mo*le", 14, 14, 0, 0, 0, 112, 110)),
        new(new("h?llo", 4, 4, 0, 0, 0, 18, 30)),
        new(new("u*b*e", 88, 88, 0, 2, 0, 646, 287)),
        new(new("goldsmit*", 3, 3, 0, 0, 0, 2, 0)),
        new(new("goldsmit?", 1, 1, 0, 0, 0, 2, 0)),

        new(new("🍣", 0, 0, 0, 0, 0, 0, 0)),
        new(new("  ", 0, 0, 0, 0, 0, 0, 0)),
    };

    [Theory]
    [MemberData(nameof(SearchCountCases))]
    public async Task Search_counts_match_generated_indexes(SearchCountCase testCase)
    {
        IndexPaths paths = new();
        if (!HasGeneratedIndexes(paths))
        {
            return;
        }

        MainWindowViewModel viewModel = new(
            paths,
            dispatchUiUpdates: false,
            searchDebounceDelay: _ => Task.CompletedTask);

        viewModel.HasGeneratedIndex.Should().BeTrue("the generated index must be current before count assertions run");

        using AssertionScope scope = new();

        foreach ((string mode, int expectedCount) in testCase.ExpectedCounts)
        {
            int actualCount = await SearchAndCountAsync(paths, mode, testCase.Query, TestContext.Current.CancellationToken);

            actualCount.Should().Be(
                expectedCount,
                "query '{0}' in mode '{1}' should match the generated index count",
                testCase.Query,
                mode);
        }
    }

    private static async Task<int> SearchAndCountAsync(
        IndexPaths paths,
        string mode,
        string query,
        CancellationToken token)
    {
        MainWindowViewModel viewModel = new(
            paths,
            dispatchUiUpdates: false,
            searchDebounceDelay: _ => Task.CompletedTask);
        SearchModeOption searchMode = viewModel.SearchModes.Single(option => option.DisplayName == mode);
        viewModel.SelectedSearchMode = searchMode;
        Task searchCompleted = WaitForSearchCompletedAsync(viewModel);
        viewModel.SearchText = query;

        if (!searchMode.SearchOnTextChanged)
        {
            viewModel.RunModeSearchCommand.Execute(null);
        }

        await searchCompleted.WaitAsync(TimeSpan.FromSeconds(5), token);
        return viewModel.SearchResults.Count;
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

    private static bool HasGeneratedIndexes(IndexPaths paths)
    {
        AppConfiguration config = AppConfiguration.Load(paths);
        return config.IndexVersion == AppConfiguration.CurrentIndexVersion
            && File.Exists(paths.IncrementalPath)
            && Directory.Exists(paths.FullTextHeadwordPhrasePath)
            && Directory.Exists(paths.FullTextDefinitionExamplePath);
    }
}

public sealed record SearchCountCase(
    string Query,
    int All,
    int Headwords,
    int PhrasalVerbs,
    int Collocations,
    int Phrases,
    int Definitions,
    int Examples)
{
    public IReadOnlyList<(string Mode, int ExpectedCount)> ExpectedCounts { get; } =
    [
        ("All", All),
        ("Headwords", Headwords),
        ("Phrasal Verbs", PhrasalVerbs),
        ("Collocations", Collocations),
        ("Phrases", Phrases),
        ("Definitions", Definitions),
        ("Examples", Examples),
    ];
}
