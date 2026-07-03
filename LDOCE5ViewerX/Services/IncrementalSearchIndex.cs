using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Reads the binary <c>incremental.db</c> prefix-search index.
/// </summary>
public sealed class IncrementalSearchIndex : IDisposable
{
    private const uint Magic = 0x28061691;
    private const uint DatabaseVersion = 1;
    private const int HeaderSize = 16;
    private const int RecordHeaderSize = 8;

    private MemoryMappedFile? _memoryMappedFile;
    private MemoryMappedViewAccessor? _accessor;
    private readonly int _count;
    private readonly int _offsetTableStart;

    private IncrementalSearchIndex(
        MemoryMappedFile memoryMappedFile,
        MemoryMappedViewAccessor accessor,
        int count,
        int offsetTableStart)
    {
        _memoryMappedFile = memoryMappedFile;
        _accessor = accessor;
        _count = count;
        _offsetTableStart = offsetTableStart;
    }

    /// <summary>
    /// Loads and validates an incremental index file.
    /// </summary>
    /// <param name="indexPath">Path to <c>incremental.db</c>.</param>
    /// <returns>An index reader backed by a read-only memory map.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file does not match the expected format.</exception>
    public static IncrementalSearchIndex Open(string indexPath)
    {
        long fileSize = new FileInfo(indexPath).Length;
        if (fileSize < HeaderSize)
        {
            throw new InvalidDataException("The incremental index is too small.");
        }

        MemoryMappedFile? memoryMappedFile = null;
        MemoryMappedViewAccessor? accessor = null;
        try
        {
            memoryMappedFile = MemoryMappedFile.CreateFromFile(
                indexPath,
                FileMode.Open,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read);
            accessor = memoryMappedFile.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);

            uint magic = ReadUInt32(accessor, 0);
            if (magic != Magic)
            {
                throw new InvalidDataException("The incremental index header is invalid.");
            }

            uint version = ReadUInt32(accessor, 4);
            if (version != DatabaseVersion)
            {
                throw new InvalidDataException("The incremental index version is not supported.");
            }

            int count = checked((int)ReadUInt32(accessor, 8));
            int offsetTableStart = checked((int)ReadUInt32(accessor, 12));
            if (count <= 0 || offsetTableStart <= 0)
            {
                throw new InvalidDataException("The incremental index contains no data.");
            }

            long expectedSize = checked(offsetTableStart + (long)count * sizeof(uint));
            if (fileSize != expectedSize)
            {
                throw new InvalidDataException("The incremental index size is inconsistent.");
            }

            IncrementalSearchIndex index = new(memoryMappedFile, accessor, count, offsetTableStart);
            memoryMappedFile = null;
            accessor = null;
            return index;
        }
        finally
        {
            accessor?.Dispose();
            memoryMappedFile?.Dispose();
        }
    }

    /// <summary>
    /// Finds prefix matches for a query using the index normalization rules.
    /// </summary>
    /// <param name="query">User-entered lookup text.</param>
    /// <param name="limit">Maximum number of rows to return.</param>
    /// <returns>Matching rows in index order.</returns>
    public IReadOnlyList<IncrementalSearchResult> Search(string query, int limit)
    {
        MemoryMappedViewAccessor accessor = Accessor;

        string key = TextNormalizer.NormalizeIndexKey(query);
        if (key.Length == 0 || limit <= 0)
        {
            return [];
        }

        int start = BisectStart(key);
        if (start == _count)
        {
            return [];
        }

        int end = BisectEnd(key, start);
        int resultCount = Math.Min(limit, end - start);
        IncrementalSearchResult[] results = new IncrementalSearchResult[resultCount];

        for (int i = 0; i < resultCount; i++)
        {
            int recordOffset = ReadRecordOffset(start + i);
            results[i] = ReadRecord(recordOffset);
        }

        return results;
    }

    /// <summary>
    /// Locates the first index row whose normalized key is greater than or equal to <paramref name="key"/>.
    /// </summary>
    private int BisectStart(string key)
    {
        int left = 0;
        int right = _count;
        while (left != right)
        {
            int middle = (left + right) / 2;
            string plain = ReadSortKeyAtIndex(middle);
            if (string.CompareOrdinal(key, plain) > 0)
            {
                left = middle + 1;
            }
            else
            {
                right = middle;
            }
        }

        return left;
    }

    /// <summary>
    /// Locates the first row after the prefix-match range for <paramref name="key"/>.
    /// </summary>
    private int BisectEnd(string key, int start)
    {
        int left = start;
        int right = _count;
        while (left != right)
        {
            int middle = (left + right) / 2;
            string plain = ReadSortKeyAtIndex(middle);
            if (string.CompareOrdinal(key, plain) < 0 && !plain.StartsWith(key, StringComparison.Ordinal))
            {
                right = middle;
            }
            else
            {
                left = middle + 1;
            }
        }

        return left;
    }

    /// <summary>
    /// Reads only the normalized key from a row for binary-search comparisons.
    /// </summary>
    private string ReadSortKeyAtIndex(int index)
    {
        int recordOffset = ReadRecordOffset(index);
        int plainLength = ReadUInt16(Accessor, recordOffset);
        return ReadString(Accessor, recordOffset + RecordHeaderSize, plainLength);
    }

    /// <summary>
    /// Decodes a complete record into a search result.
    /// </summary>
    private IncrementalSearchResult ReadRecord(int recordOffset)
    {
        MemoryMappedViewAccessor accessor = Accessor;

        byte[] headerBuffer = ArrayPool<byte>.Shared.Rent(RecordHeaderSize);
        int plainLength;
        int typeCodeLength;
        int labelLength;
        int pathLength;
        byte priority;
        try
        {
            accessor.ReadArray(recordOffset, headerBuffer, 0, RecordHeaderSize);
            ReadOnlySpan<byte> header = headerBuffer.AsSpan(0, RecordHeaderSize);
            plainLength = BinaryPrimitives.ReadUInt16LittleEndian(header);
            typeCodeLength = header[2];
            labelLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(3, sizeof(ushort)));
            pathLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(5, sizeof(ushort)));
            priority = header[7];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }

        int dataOffset = recordOffset + RecordHeaderSize;
        string plain = ReadString(accessor, dataOffset, plainLength);

        int typeCodeOffset = dataOffset + plainLength;
        string typeCode = ReadString(accessor, typeCodeOffset, typeCodeLength);

        int labelOffset = typeCodeOffset + typeCodeLength;
        string label = ReadString(accessor, labelOffset, labelLength);

        int pathOffset = labelOffset + labelLength;
        string path = ReadString(accessor, pathOffset, pathLength);

        return new IncrementalSearchResult(label, path, plain, priority, typeCode);
    }

    /// <summary>
    /// Reads the byte offset of a record from the trailing offset table.
    /// </summary>
    private int ReadRecordOffset(int index)
    {
        int offset = _offsetTableStart + index * sizeof(uint);
        return checked((int)ReadUInt32(Accessor, offset));
    }

    /// <summary>
    /// Releases the mapped file and view handles.
    /// </summary>
    public void Dispose()
    {
        _accessor?.Dispose();
        _memoryMappedFile?.Dispose();
        _accessor = null;
        _memoryMappedFile = null;
    }

    /// <summary>
    /// Reads a little-endian 16-bit value from the mapped view.
    /// </summary>
    private static ushort ReadUInt16(MemoryMappedViewAccessor accessor, long offset)
    {
        ushort value = accessor.ReadUInt16(offset);
        return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    /// <summary>
    /// Reads a little-endian 32-bit value from the mapped view.
    /// </summary>
    private static uint ReadUInt32(MemoryMappedViewAccessor accessor, long offset)
    {
        uint value = accessor.ReadUInt32(offset);
        return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    /// <summary>
    /// Reads a string by copying bytes through a pooled buffer to avoid per-call allocations.
    /// </summary>
    private static string ReadString(MemoryMappedViewAccessor accessor, int offset, int length)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            accessor.ReadArray(offset, buffer, 0, length);
            return Encoding.UTF8.GetString(buffer, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Returns the live view accessor or throws when the index has been disposed.
    /// </summary>
    private MemoryMappedViewAccessor Accessor =>
        _accessor ?? throw new ObjectDisposedException(nameof(IncrementalSearchIndex));
}
