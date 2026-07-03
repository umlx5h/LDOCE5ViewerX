using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerXBenchmarks;

/// <summary>
/// Resolves generated index paths used by benchmarks.
/// </summary>
public static class BenchmarkIndexPaths
{
    /// <summary>
    /// Environment variable that overrides the generated index directory.
    /// </summary>
    public const string EnvironmentVariableName = "LDOCE5VIEWERX_INDEX_DIR";

    /// <summary>
    /// Environment variable that overrides the local <c>ldoce5.data</c> directory.
    /// </summary>
    public const string DataDirectoryEnvironmentVariableName = "LDOCE5VIEWERX_DATA_DIR";

    /// <summary>
    /// Resolves benchmark index paths from the environment or the app default.
    /// </summary>
    /// <returns>Generated index paths.</returns>
    public static IndexPaths Resolve()
    {
        string? indexDirectory = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.IsNullOrWhiteSpace(indexDirectory)
            ? new IndexPaths()
            : new IndexPaths(indexDirectory);
    }

    /// <summary>
    /// Verifies that the incremental index exists.
    /// </summary>
    /// <param name="paths">Resolved index paths.</param>
    public static void RequireIncrementalIndex(IndexPaths paths)
    {
        if (!File.Exists(paths.IncrementalPath))
        {
            throw CreateMissingIndexException(paths, paths.IncrementalPath);
        }
    }

    /// <summary>
    /// Verifies that the file-location map exists.
    /// </summary>
    /// <param name="paths">Resolved index paths.</param>
    public static void RequireFilemap(IndexPaths paths)
    {
        if (!File.Exists(paths.FilemapPath))
        {
            throw CreateMissingIndexException(paths, paths.FilemapPath);
        }
    }

    /// <summary>
    /// Verifies that full-text indexes exist.
    /// </summary>
    /// <param name="paths">Resolved index paths.</param>
    public static void RequireFullTextIndexes(IndexPaths paths)
    {
        if (!Directory.Exists(paths.FullTextHeadwordPhrasePath))
        {
            throw CreateMissingIndexException(paths, paths.FullTextHeadwordPhrasePath);
        }

        if (!Directory.Exists(paths.FullTextDefinitionExamplePath))
        {
            throw CreateMissingIndexException(paths, paths.FullTextDefinitionExamplePath);
        }
    }

    /// <summary>
    /// Resolves the real <c>ldoce5.data</c> directory from the environment or app configuration.
    /// </summary>
    /// <param name="paths">Resolved index paths.</param>
    /// <returns>Local dictionary data directory.</returns>
    public static string ResolveDataDirectory(IndexPaths paths)
    {
        string? dataDirectory = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            dataDirectory = AppConfiguration.Load(paths).DataDirectory;
        }

        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new InvalidOperationException(
                "Benchmark dictionary data was not configured. "
                + $"Set {DataDirectoryEnvironmentVariableName} to the local ldoce5.data directory, "
                + $"or set it in '{paths.ConfigurationPath}'.");
        }

        if (!IdmArchive.IsLdoce5Directory(dataDirectory))
        {
            throw new InvalidOperationException(
                $"Benchmark dictionary data was not found at '{dataDirectory}'. "
                + $"Set {DataDirectoryEnvironmentVariableName} to a valid ldoce5.data directory.");
        }

        return dataDirectory;
    }

    private static InvalidOperationException CreateMissingIndexException(IndexPaths paths, string missingPath)
    {
        return new InvalidOperationException(
            $"Benchmark index data was not found at '{missingPath}'. "
            + $"Create the generated index or set {EnvironmentVariableName} to a directory containing "
            + "filemap.cdb, incremental.db, fulltext_hp, and fulltext_de. "
            + $"Resolved base directory: '{paths.BaseDirectory}'.");
    }
}
