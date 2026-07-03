using BenchmarkDotNet.Attributes;

using LDOCE5ViewerX.Services;

using Microsoft.VSDiagnostics;

namespace LDOCE5ViewerXBenchmarks.Benchmarks;

/// <summary>
/// Benchmarks direct generated LeanCorpus full-text searches.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[CPUUsageDiagnoser]
public class FullTextSearcherBenchmarks
{
    private FullTextSearcher _headwordPhraseSearcher = null!;
    private FullTextSearcher _definitionExampleSearcher = null!;

    /// <summary>
    /// Opens generated full-text indexes.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        IndexPaths paths = BenchmarkIndexPaths.Resolve();
        BenchmarkIndexPaths.RequireFullTextIndexes(paths);
        string? variationsPath = File.Exists(paths.VariationsPath) ? paths.VariationsPath : null;
        _headwordPhraseSearcher = new FullTextSearcher(paths.FullTextHeadwordPhrasePath, variationsPath);
        _definitionExampleSearcher = new FullTextSearcher(paths.FullTextDefinitionExamplePath, variationsPath);
    }

    /// <summary>
    /// Closes generated full-text indexes.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _headwordPhraseSearcher.Dispose();
        _definitionExampleSearcher.Dispose();
    }
    /// <summary>
    /// Gets direct full-text benchmark cases.
    /// </summary>
    public static FullTextSearchCase[] Cases =>
    [
        new("HP all common", FullTextIndexKind.HeadwordPhrase, "test", [], 10000, false),
        new("HP headword", FullTextIndexKind.HeadwordPhrase, "hello", ["hm"], 10000, false),
        new("HP wildcard sort-key", FullTextIndexKind.HeadwordPhrase, "word*", ["hm"], 10000, true),
        new("HP inner wildcard sort-key", FullTextIndexKind.HeadwordPhrase, "mo*le", ["hm"], 10000, true),
        new("HP phrasal verbs", FullTextIndexKind.HeadwordPhrase, "look after", ["hp"], 3000, false),
        new("HP collocations", FullTextIndexKind.HeadwordPhrase, "long-term", ["p"], 3000, false),
        new("HP phrases", FullTextIndexKind.HeadwordPhrase, "take a break", ["pl"], 3000, false),
        new("DE definitions", FullTextIndexKind.DefinitionExample, "hello", ["d"], 3000, false),
        new("DE examples", FullTextIndexKind.DefinitionExample, "hello", ["e"], 3000, false),
    ];

    /// <summary>
    /// Measures one full-text search case.
    /// </summary>
    /// <returns>The result count.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(Cases))]
    public int Search(FullTextSearchCase c)
    {
        FullTextSearcher searcher = c.IndexKind == FullTextIndexKind.DefinitionExample
            ? _definitionExampleSearcher
            : _headwordPhraseSearcher;

        return searcher.Search(
            c.Query,
            null,
            c.ItemTypes,
            c.Limit,
            c.FilterWildcardBySortKey).Count;
    }
}

/// <summary>
/// Identifies which generated LeanCorpus index a benchmark case uses.
/// </summary>
public enum FullTextIndexKind
{
    /// <summary>
    /// Headword, phrase, phrasal-verb, and collocation index.
    /// </summary>
    HeadwordPhrase,

    /// <summary>
    /// Definition and example index.
    /// </summary>
    DefinitionExample,
}

/// <summary>
/// Describes one direct full-text search benchmark input.
/// </summary>
public sealed record FullTextSearchCase(
    string Name,
    FullTextIndexKind IndexKind,
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
