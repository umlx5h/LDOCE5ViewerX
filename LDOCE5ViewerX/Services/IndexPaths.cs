using System;
using System.IO;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Provides filesystem locations for generated app data.
/// </summary>
public sealed class IndexPaths
{
    /// <summary>
    /// Creates index paths under the default per-user data directory.
    /// </summary>
    public IndexPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LDOCE5ViewerX"))
    {
    }

    /// <summary>
    /// Creates index paths under a caller-provided base directory.
    /// </summary>
    /// <param name="baseDirectory">Directory that will contain generated index files.</param>
    public IndexPaths(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
    }

    /// <summary>
    /// Directory containing all generated index files.
    /// </summary>
    public string BaseDirectory { get; }

    /// <summary>
    /// File-location CDB path.
    /// </summary>
    public string FilemapPath => Path.Combine(BaseDirectory, "filemap.cdb");

    /// <summary>
    /// Word-variation CDB path.
    /// </summary>
    public string VariationsPath => Path.Combine(BaseDirectory, "variations.cdb");

    /// <summary>
    /// Incremental prefix index path.
    /// </summary>
    public string IncrementalPath => Path.Combine(BaseDirectory, "incremental.db");

    /// <summary>
    /// LeanCorpus index path for headwords, phrases, and activator items.
    /// </summary>
    public string FullTextHeadwordPhrasePath => Path.Combine(BaseDirectory, "fulltext_hp");

    /// <summary>
    /// LeanCorpus index path for definitions and examples.
    /// </summary>
    public string FullTextDefinitionExamplePath => Path.Combine(BaseDirectory, "fulltext_de");

    /// <summary>
    /// Application configuration file path.
    /// </summary>
    public string ConfigurationPath => Path.Combine(BaseDirectory, "config.json");

    /// <summary>
    /// Temporary scan file path used while indexing.
    /// </summary>
    public string ScanTempPath => Path.Combine(BaseDirectory, "scan.tmp");
}
