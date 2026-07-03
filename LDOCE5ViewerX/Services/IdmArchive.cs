using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Reads IDM archive catalogs and content blocks from an LDOCE5 data directory.
/// </summary>
public sealed class IdmArchive
{
    private static readonly IReadOnlyDictionary<string, string> ArchiveDirectories =
        new Dictionary<string, string>
        {
            ["etymologies"] = "etymologies.skn",
            ["word_families"] = "word_families.skn",
            ["examples"] = "examples.skn",
            ["sound"] = "sound.skn",
            ["fs"] = "fs.skn",
            ["us_hwd_pron"] = "us_hwd_pron.skn",
            ["gb_hwd_pron"] = "gb_hwd_pron.skn",
            ["picture"] = "picture.skn",
            ["phrases"] = "phrases.skn",
            ["sfx"] = "sfx.skn",
            ["thesaurus"] = "thesaurus.skn",
            ["gram"] = "gram.skn",
            ["collocations"] = "collocations.skn",
            ["exa_pron"] = "exa_pron.skn",
            ["common_errors"] = "common_errors.skn",
            ["word_sets"] = "word_sets.skn",
            ["menus"] = "menus.skn",
            ["word_lists"] = "word_lists.skn",
            ["verb_forms"] = "verb_forms.skn",
            ["activator"] = "activator.skn",
            ["activator_section"] = Path.Combine("activator.skn", "activator_section.skn"),
            ["activator_concept"] = Path.Combine("activator.skn", "activator_concept.skn"),
        };

    private readonly string _dataDirectory;

    /// <summary>
    /// Creates an archive reader for a local <c>ldoce5.data</c> directory.
    /// </summary>
    /// <param name="dataDirectory">Dictionary data directory.</param>
    public IdmArchive(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    /// <summary>
    /// Returns all dictionary archive names used by the viewer.
    /// </summary>
    public static IReadOnlyCollection<string> ArchiveNames => ArchiveDirectories.Keys.ToArray();

    /// <summary>
    /// Checks whether a directory has the expected IDM archive structure.
    /// </summary>
    /// <param name="dataDirectory">Candidate <c>ldoce5.data</c> directory.</param>
    /// <returns><see langword="true"/> when required catalog files exist.</returns>
    public static bool IsLdoce5Directory(string dataDirectory)
    {
        foreach (string archivePath in ArchiveDirectories.Values)
        {
            string targetBase = Path.Combine(dataDirectory, archivePath);
            string filesBase = Path.Combine(targetBase, "files.skn");
            string dirsBase = Path.Combine(targetBase, "dirs.skn");
            if (!File.Exists(Path.Combine(dirsBase, "config.cft"))
                || !File.Exists(Path.Combine(dirsBase, "NAME.tda"))
                || !File.Exists(Path.Combine(dirsBase, "dirs.dat"))
                || !File.Exists(Path.Combine(filesBase, "config.cft"))
                || !File.Exists(Path.Combine(filesBase, "NAME.tda"))
                || !File.Exists(Path.Combine(filesBase, "files.dat"))
                || !File.Exists(Path.Combine(filesBase, "CONTENT.tda"))
                || !File.Exists(Path.Combine(filesBase, "CONTENT.tda.tdz")))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Enumerates file catalog entries for an archive.
    /// </summary>
    /// <param name="archive">Archive name.</param>
    /// <returns>Catalog entries with logical names and compressed block locations.</returns>
    public IReadOnlyList<ArchiveFileEntry> ListFiles(string archive)
    {
        string targetBase = GetArchiveBasePath(archive);
        TableInfo filesInfo = ParseConfig(Path.Combine(targetBase, "files.skn", "config.cft"));
        TableInfo dirsInfo = ParseConfig(Path.Combine(targetBase, "dirs.skn", "config.cft"));

        List<(string Name, int Parent)> directories = LoadDirectories(targetBase, dirsInfo);
        List<(string Name, int Parent, int Offset, int Size)> files = LoadFiles(targetBase, filesInfo);
        (int[] originalOffsets, int[] originalSizes, int[] compressedOffsets, int[] compressedSizes) = LoadCatalog(targetBase);

        List<ArchiveFileEntry> entries = [];
        int catalogIndex = 0;
        foreach ((string name, int parent, int offset, int sizeFromCatalog) in files)
        {
            if (catalogIndex != originalOffsets.Length - 1 && offset >= originalOffsets[catalogIndex + 1])
            {
                catalogIndex++;
            }

            int originalSize = sizeFromCatalog;
            if (originalSize < 0)
            {
                originalSize = originalSizes[catalogIndex] - (offset - originalOffsets[catalogIndex]) - 1;
            }

            ArchiveLocation location = new(
                compressedOffsets[catalogIndex],
                compressedSizes[catalogIndex],
                offset - originalOffsets[catalogIndex],
                originalSize);
            entries.Add(new ArchiveFileEntry(BuildDirectoryPath(directories, parent), name, location));
        }

        return entries;
    }

    /// <summary>
    /// Reads raw item bytes from an archive using a catalog location.
    /// </summary>
    /// <param name="archive">Archive name.</param>
    /// <param name="location">Compressed block location.</param>
    /// <returns>Raw item bytes.</returns>
    public byte[] Read(string archive, ArchiveLocation location)
    {
        string contentPath = Path.Combine(GetArchiveBasePath(archive), "files.skn", "CONTENT.tda");
        using FileStream file = File.OpenRead(contentPath);
        file.Seek(location.CompressedOffset, SeekOrigin.Begin);

        byte[] compressed = new byte[location.CompressedSize];
        file.ReadExactly(compressed);

        using MemoryStream source = new(compressed);
        using ZLibStream zlib = new(source, CompressionMode.Decompress);
        using MemoryStream decompressed = new();
        zlib.CopyTo(decompressed);

        byte[] block = decompressed.ToArray();
        byte[] item = new byte[location.OriginalSize];
        Buffer.BlockCopy(block, location.OriginalOffset, item, 0, item.Length);
        return item;
    }

    /// <summary>
    /// Converts an LDOCE XML id to the shortened id stored in result paths.
    /// </summary>
    public static string ShortenId(string id)
    {
        string[] parts = id.Split('.');
        return parts.Length == 4 ? $"{parts[2]}.{parts[3]}" : id;
    }

    /// <summary>
    /// Resolves an archive name to its directory path.
    /// </summary>
    private string GetArchiveBasePath(string archive)
    {
        return Path.Combine(_dataDirectory, ArchiveDirectories[archive]);
    }

    /// <summary>
    /// Parses an IDM <c>config.cft</c> file enough to locate record fields.
    /// </summary>
    private static TableInfo ParseConfig(string path)
    {
        Dictionary<string, (int Offset, int Size)> fields = [];
        int offset = 0;
        bool inDatSection = false;
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inDatSection = string.Equals(line, "[DAT]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inDatSection)
            {
                continue;
            }

            string[] pair = line.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            int size = pair[1].Trim() switch
            {
                "UBYTE" => 1,
                "USHORT" => 2,
                "U24" => 3,
                "ULONG" => 4,
                _ => 0,
            };
            if (size == 0)
            {
                continue;
            }

            string name = pair[0].Split(',')[0].Trim().ToLowerInvariant();
            fields[name] = (offset, size);
            offset += size;
        }

        return new TableInfo(fields, offset);
    }

    /// <summary>
    /// Loads directory records from <c>dirs.skn</c>.
    /// </summary>
    private static List<(string Name, int Parent)> LoadDirectories(string targetBase, TableInfo info)
    {
        string dirsBase = Path.Combine(targetBase, "dirs.skn");
        string[] names = ReadNullSeparatedUtf8(Path.Combine(dirsBase, "NAME.tda"));
        byte[] data = File.ReadAllBytes(Path.Combine(dirsBase, "dirs.dat"));
        (int offset, int size) = info.Fields["$parent"];

        List<(string Name, int Parent)> directories = [];
        for (int i = 0; i < names.Length; i++)
        {
            int recordOffset = i * info.RecordSize;
            directories.Add((names[i], ReadLittleEndianInt(data.AsSpan(recordOffset + offset, size))));
        }

        return directories;
    }

    /// <summary>
    /// Loads file records from <c>files.skn</c>.
    /// </summary>
    private static List<(string Name, int Parent, int Offset, int Size)> LoadFiles(string targetBase, TableInfo info)
    {
        string filesBase = Path.Combine(targetBase, "files.skn");
        string[] names = ReadNullSeparatedUtf8(Path.Combine(filesBase, "NAME.tda"));
        byte[] data = File.ReadAllBytes(Path.Combine(filesBase, "files.dat"));
        (int contentOffset, int contentSize) = info.Fields["$content"];
        (int parentOffset, int parentSize) = info.Fields["$a_dirs"];

        List<(string Name, int Parent, int Offset, int Size)> files = new(names.Length);
        List<int> offsets = new(names.Length);
        List<int> parents = new(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            int recordOffset = i * info.RecordSize;
            offsets.Add(ReadLittleEndianInt(data.AsSpan(recordOffset + contentOffset, contentSize)));
            parents.Add(ReadLittleEndianInt(data.AsSpan(recordOffset + parentOffset, parentSize)));
        }

        for (int i = 0; i < names.Length; i++)
        {
            int size = i == names.Length - 1 ? -1 : offsets[i + 1] - offsets[i] - 1;
            files.Add((names[i], parents[i], offsets[i], size));
        }

        return files;
    }

    /// <summary>
    /// Loads compressed and decompressed block sizes from <c>CONTENT.tda.tdz</c>.
    /// </summary>
    private static (int[] OriginalOffsets, int[] OriginalSizes, int[] CompressedOffsets, int[] CompressedSizes) LoadCatalog(string targetBase)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(targetBase, "files.skn", "CONTENT.tda.tdz"));
        int count = data.Length / 8;
        int[] originalSizes = new int[count];
        int[] compressedSizes = new int[count];
        for (int i = 0; i < count; i++)
        {
            int offset = i * 8;
            originalSizes[i] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)));
            compressedSizes[i] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4)));
        }

        int[] originalOffsets = MakeOffsets(originalSizes);
        int[] compressedOffsets = MakeOffsets(compressedSizes);
        return (originalOffsets, originalSizes, compressedOffsets, compressedSizes);
    }

    /// <summary>
    /// Builds cumulative offsets for a block-size list.
    /// </summary>
    private static int[] MakeOffsets(int[] sizes)
    {
        int[] offsets = new int[sizes.Length];
        for (int i = 1; i < sizes.Length; i++)
        {
            offsets[i] = offsets[i - 1] + sizes[i - 1];
        }

        return offsets;
    }

    /// <summary>
    /// Builds a directory path by walking parent pointers.
    /// </summary>
    private static IReadOnlyList<string> BuildDirectoryPath(List<(string Name, int Parent)> directories, int index)
    {
        if (index < 0 || index >= directories.Count)
        {
            return [string.Empty];
        }

        (string name, int parent) = directories[index];
        if (parent == 0)
        {
            return [name];
        }

        List<string> path = [.. BuildDirectoryPath(directories, parent), name];
        return path;
    }

    /// <summary>
    /// Reads null-separated UTF-8 strings and drops the final empty item.
    /// </summary>
    private static string[] ReadNullSeparatedUtf8(string path)
    {
        string text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        string[] parts = text.Split('\0');
        return parts.Length > 0 && parts[^1].Length == 0 ? parts[..^1] : parts;
    }

    /// <summary>
    /// Reads an unsigned little-endian integer with an IDM field width.
    /// </summary>
    private static int ReadLittleEndianInt(ReadOnlySpan<byte> bytes)
    {
        int value = 0;
        for (int i = bytes.Length - 1; i >= 0; i--)
        {
            value = value * 256 + bytes[i];
        }

        return value;
    }

    private sealed record TableInfo(Dictionary<string, (int Offset, int Size)> Fields, int RecordSize);
}
