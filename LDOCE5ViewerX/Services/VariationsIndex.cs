using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Reads and writes the word-variation CDB used to expand full-text terms.
/// </summary>
public sealed class VariationsIndex : IDisposable
{
    private readonly ConstantDatabaseReader? _reader;

    /// <summary>
    /// Opens an existing variations CDB for reading.
    /// </summary>
    /// <param name="path">Path to <c>variations.cdb</c>.</param>
    public VariationsIndex(string path)
    {
        _reader = File.Exists(path) ? new ConstantDatabaseReader(path) : null;
    }

    /// <summary>
    /// Gets a word plus known inflection variants.
    /// </summary>
    /// <param name="word">Normalized lookup word.</param>
    /// <returns>Word and variants.</returns>
    public IReadOnlyCollection<string> GetVariations(string word)
    {
        HashSet<string> words = [word];
        byte[]? data = _reader?.Get(Encoding.UTF8.GetBytes(word));
        if (data is null)
        {
            return words;
        }

        ReadOnlySpan<byte> dataSpan = data.AsSpan();
        foreach (Range range in dataSpan.Split((byte)0))
        {
            words.Add(Encoding.UTF8.GetString(dataSpan[range]));
        }

        return words;
    }

    /// <summary>
    /// Writes a complete word-variation CDB.
    /// </summary>
    /// <param name="path">Destination CDB path.</param>
    /// <param name="variations">Variation map.</param>
    public static void Write(string path, IReadOnlyDictionary<string, IReadOnlyCollection<string>> variations)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        ConstantDatabaseWriter writer = new(stream);
        foreach ((string word, IReadOnlyCollection<string> variants) in variations)
        {
            if (variants.Count == 0)
            {
                continue;
            }

            writer.Add(
                Encoding.UTF8.GetBytes(word),
                Encoding.UTF8.GetBytes(string.Join('\0', variants)));
        }

        writer.FinalizeDatabase();
    }

    /// <summary>
    /// Releases the variation database reader.
    /// </summary>
    public void Dispose()
    {
        _reader?.Dispose();
    }
}
