using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Reads item bytes from the compressed IDM archive folders inside <c>ldoce5.data</c>.
/// </summary>
public sealed class LdoceArchiveReader
{
    private static readonly IReadOnlyDictionary<string, string> ArchiveDirectories =
        new Dictionary<string, string>
        {
            ["collocations"] = "collocations.skn",
            ["etymologies"] = "etymologies.skn",
            ["examples"] = "examples.skn",
            ["exa_pron"] = "exa_pron.skn",
            ["activator_concept"] = Path.Combine("activator.skn", "activator_concept.skn"),
            ["activator_section"] = Path.Combine("activator.skn", "activator_section.skn"),
            ["fs"] = "fs.skn",
            ["gb_hwd_pron"] = "gb_hwd_pron.skn",
            ["phrases"] = "phrases.skn",
            ["picture"] = "picture.skn",
            ["sfx"] = "sfx.skn",
            ["thesaurus"] = "thesaurus.skn",
            ["us_hwd_pron"] = "us_hwd_pron.skn",
            ["word_families"] = "word_families.skn",
            ["word_sets"] = "word_sets.skn",
        };

    private readonly string _dataDirectory;

    /// <summary>
    /// Creates an archive reader for a local <c>ldoce5.data</c> directory.
    /// </summary>
    /// <param name="dataDirectory">Path to the dictionary data directory.</param>
    public LdoceArchiveReader(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    /// <summary>
    /// Reads one archive item using the block location from <c>filemap.cdb</c>.
    /// </summary>
    /// <param name="archive">Archive name.</param>
    /// <param name="location">Compressed block and item slice location.</param>
    /// <returns>Raw item bytes.</returns>
    public byte[] Read(string archive, ArchiveLocation location)
    {
        if (!ArchiveDirectories.TryGetValue(archive, out string? archiveDirectory))
        {
            throw new InvalidDataException($"Unsupported dictionary archive '{archive}'.");
        }

        string contentPath = Path.Combine(_dataDirectory, archiveDirectory, "files.skn", "CONTENT.tda");

        byte[] compressed = ArrayPool<byte>.Shared.Rent(location.CompressedSize);
        byte[]? skipBuffer = location.OriginalOffset > 0 ? ArrayPool<byte>.Shared.Rent(81920) : null;

        try
        {
            using FileStream file = File.OpenRead(contentPath);
            file.Seek(location.CompressedOffset, SeekOrigin.Begin);
            file.ReadExactly(compressed.AsSpan(0, location.CompressedSize));

            using MemoryStream source = new(compressed, 0, location.CompressedSize, writable: false);
            using ZLibStream zlib = new(source, CompressionMode.Decompress);

            if (skipBuffer is not null)
            {
                SkipExactly(zlib, location.OriginalOffset, skipBuffer);
            }

            byte[] item = new byte[location.OriginalSize];
            zlib.ReadExactly(item);

            return item;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compressed);
            if (skipBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(skipBuffer);
            }
        }

        static void SkipExactly(Stream stream, int count, byte[] buffer)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, 0, Math.Min(buffer.Length, count));
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                count -= read;
            }
        }
    }
}
