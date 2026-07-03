using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Writes <c>incremental.db</c> files for prefix search.
/// </summary>
public sealed class IncrementalSearchIndexWriter
{
    private const uint Magic = 0x28061691;
    private const uint DatabaseVersion = 1;
    private const int HeaderSize = 16;

    private readonly List<(byte[] Record, string Plain, byte Priority)> _items = [];

    /// <summary>
    /// Adds one item to the in-memory incremental index build list.
    /// </summary>
    /// <param name="plain">Searchable text before normalization.</param>
    /// <param name="typeCode">Search item type code.</param>
    /// <param name="label">Display label.</param>
    /// <param name="path">Dictionary path.</param>
    /// <param name="priority">Priority used for result ordering.</param>
    public void AddItem(string plain, string typeCode, string label, string path, byte priority)
    {
        string normalizedPlain = TextNormalizer.NormalizeIndexKey(plain);
        if (normalizedPlain.Length == 0)
        {
            return;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(normalizedPlain);
        byte[] typeBytes = Encoding.UTF8.GetBytes(typeCode);
        byte[] labelBytes = Encoding.UTF8.GetBytes(label);
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);

        using MemoryStream stream = new();
        WriteUInt16(stream, checked((ushort)plainBytes.Length));
        stream.WriteByte(checked((byte)typeBytes.Length));
        WriteUInt16(stream, checked((ushort)labelBytes.Length));
        WriteUInt16(stream, checked((ushort)pathBytes.Length));
        stream.WriteByte(priority);
        stream.Write(plainBytes);
        stream.Write(typeBytes);
        stream.Write(labelBytes);
        stream.Write(pathBytes);

        _items.Add((stream.ToArray(), normalizedPlain, priority));
    }

    /// <summary>
    /// Writes the completed incremental index to disk.
    /// </summary>
    /// <param name="path">Destination <c>incremental.db</c> path.</param>
    public void WriteTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        List<(byte[] Record, string Plain, byte Priority)> ordered = _items
            .OrderBy(item => item.Plain, StringComparer.Ordinal)
            .ThenBy(item => item.Priority)
            .ToList();

        using MemoryStream stream = new();
        stream.Write(new byte[HeaderSize]);

        List<uint> offsets = [];
        foreach ((byte[] record, _, _) in ordered)
        {
            offsets.Add(checked((uint)stream.Position));
            stream.Write(record);
        }

        uint offsetTableStart = checked((uint)stream.Position);
        foreach (uint offset in offsets)
        {
            WriteUInt32(stream, offset);
        }

        byte[] data = stream.ToArray();
        WriteUInt32(data, 0, Magic);
        WriteUInt32(data, 4, DatabaseVersion);
        WriteUInt32(data, 8, checked((uint)ordered.Count));
        WriteUInt32(data, 12, offsetTableStart);
        File.WriteAllBytes(path, data);
    }

    /// <summary>
    /// Writes a little-endian unsigned 16-bit integer.
    /// </summary>
    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>
    /// Writes a little-endian unsigned 32-bit integer.
    /// </summary>
    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>
    /// Writes a little-endian unsigned 32-bit integer into an existing byte array.
    /// </summary>
    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }
}
