using BenchmarkDotNet.Attributes;

using LDOCE5ViewerX.Services;

using Microsoft.VSDiagnostics;

namespace LDOCE5ViewerXBenchmarks.Benchmarks;

/// <summary>
/// Benchmarks generated incremental prefix-index searches.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[CPUUsageDiagnoser]
public class IncrementalSearchIndexBenchmarks
{
    private IncrementalSearchIndex _index = null!;

    /// <summary>
    /// Opens the generated incremental index.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        IndexPaths paths = BenchmarkIndexPaths.Resolve();
        BenchmarkIndexPaths.RequireIncrementalIndex(paths);
        _index = IncrementalSearchIndex.Open(paths.IncrementalPath);
    }

    /// <summary>
    /// Closes the generated incremental index.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _index.Dispose();
    }

    /// <summary>
    /// Gets benchmark query values.
    /// </summary>
    public static IEnumerable<string> Queries =>
    [
        "test",
        "hello",
        "devil",
        "long-term",
        "look after",
        "notfoundquery",
    ];

    /// <summary>
    /// Measures prefix lookup and result materialization.
    /// </summary>
    /// <returns>The result count.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(Queries))]
    public int Search(string query)
    {
        return _index.Search(query, 500).Count;
    }
}
