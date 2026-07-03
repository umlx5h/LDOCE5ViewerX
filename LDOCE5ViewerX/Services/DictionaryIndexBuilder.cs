using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Builds all generated indexes needed by the Avalonia LDOCE viewer.
/// </summary>
public sealed class DictionaryIndexBuilder
{
    /// <summary>
    /// Define the order to match the original version's index
    /// </summary>
    private static readonly string[] FilemapArchiveOrder =
    [
        "picture",
        "fs",
        "gb_hwd_pron",
        "sfx",
        "sound",
        "thesaurus",
        "etymologies",
        "examples",
        "activator_concept",
        "common_errors",
        "activator_section",
        "verb_forms",
        "us_hwd_pron",
        "collocations",
        "word_sets",
        "phrases",
        "word_lists",
        "menus",
        "word_families",
        "gram",
        "activator",
        "exa_pron",
    ];

    private readonly string _dataDirectory;
    private readonly IndexPaths _paths;
    private readonly IProgress<IndexBuildProgress>? _progress;

    /// <summary>
    /// Creates an index builder for one LDOCE data directory.
    /// </summary>
    /// <param name="dataDirectory">Source <c>ldoce5.data</c> directory.</param>
    /// <param name="paths">Destination index paths.</param>
    /// <param name="progress">Optional progress reporter.</param>
    public DictionaryIndexBuilder(string dataDirectory, IndexPaths paths, IProgress<IndexBuildProgress>? progress = null)
    {
        _dataDirectory = dataDirectory;
        _paths = paths;
        _progress = progress;
    }

    /// <summary>
    /// Builds filemap, variations, incremental, and LeanCorpus full-text indexes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task BuildAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Build(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Synchronously runs the indexing steps.
    /// </summary>
    private void Build(CancellationToken cancellationToken)
    {
        if (!IdmArchive.IsLdoce5Directory(_dataDirectory))
        {
            throw new InvalidDataException("The selected directory is not an LDOCE5 data directory.");
        }

        Directory.CreateDirectory(_paths.BaseDirectory);
        RemoveExistingIndexes();

        IdmArchive archive = new(_dataDirectory);
        BuildFilemap(archive, cancellationToken);

        List<SearchIndexItem> items = [];
        Dictionary<string, IReadOnlyCollection<string>> variations = [];

        ScanEntries(archive, items, variations, cancellationToken);
        ScanActivator(archive, items, cancellationToken);

        Report("Building the word variation database...");
        VariationsIndex.Write(_paths.VariationsPath, variations);

        BuildIncrementalIndex(items, cancellationToken);
        BuildFullTextIndexes(items, cancellationToken);
        Report("Completed.");
    }

    /// <summary>
    /// Removes old generated index files and directories.
    /// </summary>
    private void RemoveExistingIndexes()
    {
        DeleteFile(_paths.FilemapPath);
        DeleteFile(_paths.VariationsPath);
        DeleteFile(_paths.IncrementalPath);
        DeleteDirectory(_paths.FullTextHeadwordPhrasePath);
        DeleteDirectory(_paths.FullTextDefinitionExamplePath);
    }

    /// <summary>
    /// Builds <c>filemap.cdb</c> for all known dictionary archives.
    /// </summary>
    private void BuildFilemap(IdmArchive archive, CancellationToken cancellationToken)
    {
        Report("Building the file-location lookup table...");
        using FileStream stream = File.Create(_paths.FilemapPath);
        ConstantDatabaseWriter writer = new(stream);
        foreach (string archiveName in FilemapArchiveOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report($"Analyzing '{archiveName}'...");

            foreach ((string name, ArchiveLocation location) in ListFilemapEntries(archive, archiveName, cancellationToken))
            {
                writer.Add(CreateFilemapKey(archiveName, name), PackLocation(location));
            }
        }

        writer.FinalizeDatabase();
    }

    /// <summary>
    /// Converts archive catalog entries to filemap names used by the viewer.
    /// </summary>
    private static IEnumerable<(string Name, ArchiveLocation Location)> ListFilemapEntries(
        IdmArchive archive,
        string archiveName,
        CancellationToken cancellationToken)
    {
        foreach (ArchiveFileEntry entry in archive.ListFiles(archiveName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = entry.Name;
            if (archiveName == "picture")
            {
                name = $"{entry.Directories[0]}/{entry.Name}";
            }
            else if (archiveName == "fs")
            {
                XElement root = XElement.Parse(Encoding.UTF8.GetString(archive.Read(archiveName, entry.Location)));
                name = IdmArchive.ShortenId((string?)root.Attribute("id") ?? name);
            }
            else if (entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                XElement root = XElement.Parse(Encoding.UTF8.GetString(archive.Read(archiveName, entry.Location)));
                name = (string?)root.Attribute("id") ?? (string?)root.Attribute("idm_id") ?? name;
            }

            yield return (name, entry.Location);
        }
    }

    /// <summary>
    /// Scans entry XML files for search items and word variations.
    /// </summary>
    private void ScanEntries(
        IdmArchive archive,
        List<SearchIndexItem> items,
        Dictionary<string, IReadOnlyCollection<string>> variations,
        CancellationToken cancellationToken)
    {
        Report("Scanning entry files...");
        int count = 0;
        foreach (ArchiveFileEntry entry in archive.ListFiles("fs"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] data = archive.Read("fs", entry.Location);
            (IReadOnlyList<SearchIndexItem> entryItems, IReadOnlyDictionary<string, IReadOnlyCollection<string>> entryVariations) =
                SearchItemExtractor.ExtractEntryItems(data);

            items.AddRange(entryItems);
            foreach ((string key, IReadOnlyCollection<string> values) in entryVariations)
            {
                if (!variations.TryGetValue(key, out IReadOnlyCollection<string>? existing))
                {
                    variations[key] = values;
                }
                else
                {
                    variations[key] = existing.Concat(values).Distinct(StringComparer.Ordinal).ToArray();
                }
            }

            count += entryItems.Count;
            if (count % 10000 == 0)
            {
                Report($"{count} items found");
            }
        }

        Report($"{count} entry items were found.");
    }

    /// <summary>
    /// Scans activator concept and section XML files for search items.
    /// </summary>
    private void ScanActivator(IdmArchive archive, List<SearchIndexItem> items, CancellationToken cancellationToken)
    {
        Report("Scanning language-activator files...");
        Dictionary<string, IReadOnlyList<(string Id, string Text)>> sectionExponents = [];
        foreach (ArchiveFileEntry section in archive.ListFiles("activator_section"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string sectionId, IReadOnlyList<(string Id, string Text)> exponents) =
                SearchItemExtractor.ExtractActivatorSection(archive.Read("activator_section", section.Location));
            sectionExponents[sectionId] = exponents;
        }

        foreach (ArchiveFileEntry concept in archive.ListFiles("activator_concept"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.AddRange(SearchItemExtractor.ExtractActivatorItems(
                archive.Read("activator_concept", concept.Location),
                sectionExponents));
        }

        Report("Done.");
    }

    /// <summary>
    /// Builds the prefix-search index for headwords, phrases, and activator items.
    /// </summary>
    private void BuildIncrementalIndex(IReadOnlyList<SearchIndexItem> items, CancellationToken cancellationToken)
    {
        Report("Building the incremental search index...");
        IncrementalSearchIndexWriter writer = new();
        int count = 0;
        foreach (SearchIndexItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char typeFamily = item.TypeCode[0];
            if (typeFamily is 'p' or 'h' or 'a')
            {
                string content = item.Content;
                if (item.TypeCode == "hm")
                {
                    foreach (string word in item.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (word.Contains('-', StringComparison.Ordinal))
                        {
                            content += " " + word.Replace("-", string.Empty, StringComparison.Ordinal);
                        }
                    }
                }

                writer.AddItem(content, item.TypeCode, item.Label, item.Path, item.Priority);
                count++;
            }
        }

        writer.WriteTo(_paths.IncrementalPath);
        Report($"{count} items were added.");
    }

    /// <summary>
    /// Builds LeanCorpus full-text indexes for headword/phrase and definition/example searches.
    /// </summary>
    private void BuildFullTextIndexes(IReadOnlyList<SearchIndexItem> items, CancellationToken cancellationToken)
    {
        Report("Building the full-text search index for headwords and phrases...");
        using FullTextIndexWriter hpWriter = new(_paths.FullTextHeadwordPhrasePath);
        using FullTextIndexWriter deWriter = new(_paths.FullTextDefinitionExamplePath);

        int hpCount = 0;
        int deCount = 0;
        foreach (SearchIndexItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char typeFamily = item.TypeCode[0];
            if (typeFamily is 'p' or 'h' or 'a')
            {
                hpWriter.AddItem(item);
                hpCount++;
            }
            else if (typeFamily is 'd' or 'e')
            {
                deWriter.AddItem(item);
                deCount++;
            }
        }

        hpWriter.Commit();
        Report($"{hpCount} headword/phrase items were added.");
        deWriter.Commit();
        Report($"{deCount} definition/example items were added.");
    }

    /// <summary>
    /// Builds the first ten bytes of MD5(<c>archive:name</c>) for filemap keys.
    /// </summary>
    private static byte[] CreateFilemapKey(string archive, string name)
    {
        byte[] hash = MD5.HashData(Encoding.ASCII.GetBytes($"{archive}:{name}"));
        byte[] key = new byte[10];
        Buffer.BlockCopy(hash, 0, key, 0, key.Length);
        return key;
    }

    /// <summary>
    /// Packs an archive location using the compact filemap format when possible.
    /// </summary>
    private static byte[] PackLocation(ArchiveLocation location)
    {
        if (location.CompressedSize < 65536 && location.OriginalOffset < 65536 && location.OriginalSize < 65536)
        {
            byte[] data = new byte[10];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), checked((uint)location.CompressedOffset));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), checked((ushort)location.CompressedSize));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, 2), checked((ushort)location.OriginalOffset));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), checked((ushort)location.OriginalSize));
            return data;
        }

        byte[] wide = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(wide.AsSpan(0, 4), checked((uint)location.CompressedOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(wide.AsSpan(4, 4), checked((uint)location.CompressedSize));
        BinaryPrimitives.WriteUInt32LittleEndian(wide.AsSpan(8, 4), checked((uint)location.OriginalOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(wide.AsSpan(12, 4), checked((uint)location.OriginalSize));
        return wide;
    }

    /// <summary>
    /// Reports progress to the UI, if a reporter exists.
    /// </summary>
    private void Report(string message)
    {
        _progress?.Report(new IndexBuildProgress(message));
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

/// <summary>
/// Reports one user-visible message from the index creation process.
/// </summary>
/// <param name="Message">Progress message.</param>
public sealed record IndexBuildProgress(string Message);
