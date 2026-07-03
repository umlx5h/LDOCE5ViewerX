using BenchmarkDotNet.Attributes;

using LDOCE5ViewerX.Services;

using Microsoft.VSDiagnostics;

namespace LDOCE5ViewerXBenchmarks.Benchmarks;

/// <summary>
/// Benchmarks backend work for user-facing search modes.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[CPUUsageDiagnoser]
public class SearchModeBenchmarks
{
    private IncrementalSearchIndex _incrementalIndex = null!;
    private FullTextSearcher _headwordPhraseSearcher = null!;
    private FullTextSearcher _definitionExampleSearcher = null!;

    /// <summary>
    /// Opens generated indexes.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        IndexPaths paths = BenchmarkIndexPaths.Resolve();
        BenchmarkIndexPaths.RequireIncrementalIndex(paths);
        BenchmarkIndexPaths.RequireFullTextIndexes(paths);
        string? variationsPath = File.Exists(paths.VariationsPath) ? paths.VariationsPath : null;
        _incrementalIndex = IncrementalSearchIndex.Open(paths.IncrementalPath);
        _headwordPhraseSearcher = new FullTextSearcher(paths.FullTextHeadwordPhrasePath, variationsPath);
        _definitionExampleSearcher = new FullTextSearcher(paths.FullTextDefinitionExamplePath, variationsPath);
    }

    /// <summary>
    /// Closes generated full-text indexes.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _incrementalIndex.Dispose();
        _headwordPhraseSearcher.Dispose();
        _definitionExampleSearcher.Dispose();
    }

    /// <summary>
    /// Gets user-facing search mode benchmark cases.
    /// </summary>
    public static SearchModeCase[] Cases =>
    [
        new("All incremental", SearchModeKind.Incremental, "hello", [], 500, false),
        new("All full-text merge", SearchModeKind.HeadwordPhrase, "hello", [], 10000, false),
        new("All wildcard headwords", SearchModeKind.HeadwordPhrase, "word*", ["hm"], 10000, true),
        new("Headwords wildcard", SearchModeKind.HeadwordPhrase, "word*", ["hm"], 10000, true),
        new("Phrases", SearchModeKind.HeadwordPhrase, "take a break", ["pl"], 3000, false),
        new("Definitions", SearchModeKind.DefinitionExample, "hello", ["d"], 3000, false),
        new("Examples", SearchModeKind.DefinitionExample, "hello", ["e"], 3000, false),
    ];

    /// <summary>
    /// Measures one search mode backend operation.
    /// </summary>
    /// <returns>The result count.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(Cases))]
    public int Search(SearchModeCase c)
    {
        return c.Kind switch
        {
            SearchModeKind.Incremental => _incrementalIndex.Search(c.Query, c.Limit).Count,
            SearchModeKind.DefinitionExample => SearchFullText(_definitionExampleSearcher, c),
            _ => SearchFullText(_headwordPhraseSearcher, c),
        };

        static int SearchFullText(FullTextSearcher searcher, SearchModeCase c)
        {
            return searcher.Search(
                c.Query,
                null,
                c.ItemTypes,
                c.Limit,
                c.FilterWildcardBySortKey).Count;
        }
    }
}

/// <summary>
/// Identifies the search backend used by a user-facing mode benchmark.
/// </summary>
public enum SearchModeKind
{
    /// <summary>
    /// Incremental prefix search.
    /// </summary>
    Incremental,

    /// <summary>
    /// Headword and phrase full-text search.
    /// </summary>
    HeadwordPhrase,

    /// <summary>
    /// Definition and example full-text search.
    /// </summary>
    DefinitionExample,
}

/// <summary>
/// Describes one user-facing search-mode benchmark input.
/// </summary>
public sealed record SearchModeCase(
    string Name,
    SearchModeKind Kind,
    string Query,
    string[] ItemTypes,
    int Limit,
    bool FilterWildcardBySortKey)
{
    /// <summary>
    /// Returns the benchmark display name.
    /// </summary>
    /// <returns>The case name.</returns>
    public override string ToString()
    {
        return Name;
    }
}
