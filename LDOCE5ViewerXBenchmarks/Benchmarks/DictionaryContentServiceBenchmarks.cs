using BenchmarkDotNet.Attributes;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.Services;

using Microsoft.VSDiagnostics;

namespace LDOCE5ViewerXBenchmarks.Benchmarks;

/// <summary>
/// Benchmarks plain-text content loading from real LDOCE archive data.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[CPUUsageDiagnoser]
public class DictionaryContentServiceBenchmarks
{
    private readonly Dictionary<string, string> _pathsByCaseName = [];
    private DictionaryContentService _service = null!;

    /// <summary>
    /// Resolves real dictionary paths and opens the content service.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        IndexPaths paths = BenchmarkIndexPaths.Resolve();
        BenchmarkIndexPaths.RequireIncrementalIndex(paths);
        BenchmarkIndexPaths.RequireFilemap(paths);

        string dataDirectory = BenchmarkIndexPaths.ResolveDataDirectory(paths);
        using IncrementalSearchIndex index = IncrementalSearchIndex.Open(paths.IncrementalPath);

        foreach (DictionaryContentServiceCase c in Cases)
        {
            _pathsByCaseName[c.Name] = ResolvePath(index, c);
        }

        _service = new DictionaryContentService(paths, dataDirectory);
    }

    /// <summary>
    /// Closes generated index readers.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _service.Dispose();
    }

    /// <summary>
    /// Gets real dictionary content benchmark cases.
    /// </summary>
    public static DictionaryContentServiceCase[] Cases =>
    [
        new("FS headword entry", ["hello", "good", "test"], "/fs/", false, 500),
        new("FS anchored result", ["hello", "take", "good"], "/fs/", true, 500),
        new("Activator page", ["happy", "angry", "make"], "/activator/", false, 1000),
    ];

    /// <summary>
    /// Measures loading and rendering one dictionary result as plain text.
    /// </summary>
    /// <returns>The rendered text length.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(Cases))]
    public int LoadPlainText(DictionaryContentServiceCase c)
    {
        string text = _service.LoadPlainText(_pathsByCaseName[c.Name]);
        return text.Length;
    }

    private static string ResolvePath(IncrementalSearchIndex index, DictionaryContentServiceCase c)
    {
        foreach (string query in c.Queries)
        {
            foreach (IncrementalSearchResult result in index.Search(query, c.Limit))
            {
                if (!result.Path.StartsWith(c.PathPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (c.RequiresFragment && !result.Path.Contains('#', StringComparison.Ordinal))
                {
                    continue;
                }

                return result.Path;
            }
        }

        throw new InvalidOperationException(
            $"No real dictionary path was found for benchmark case '{c.Name}'. "
            + $"Queries: {string.Join(", ", c.Queries)}; path prefix: '{c.PathPrefix}'.");
    }
}

/// <summary>
/// Describes one real dictionary content benchmark input.
/// </summary>
public sealed record DictionaryContentServiceCase(
    string Name,
    string[] Queries,
    string PathPrefix,
    bool RequiresFragment,
    int Limit)
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
