using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Reads constant database files used by generated indexes.
/// </summary>
public sealed class ConstantDatabaseReader : IDisposable
{
    private const int MainTableEntryCount = 256;
    private const int MainTableSize = MainTableEntryCount * 8;

    private MemoryMappedFile? _memoryMappedFile;
    private MemoryMappedViewAccessor? _accessor;
    private readonly (uint Position, uint Count)[] _mainTable;

    /// <summary>
    /// Opens a read-only memory map for a CDB file and reads its main table.
    /// </summary>
    /// <param name="path">Path to a CDB file.</param>
    /// <exception cref="InvalidDataException">Thrown when the file is too small to contain a CDB table.</exception>
    public ConstantDatabaseReader(string path)
    {
        long fileSize = new FileInfo(path).Length;
        if (fileSize < MainTableSize)
        {
            throw new InvalidDataException("The CDB file is too small.");
        }

        MemoryMappedFile? memoryMappedFile = null;
        MemoryMappedViewAccessor? accessor = null;
        try
        {
            memoryMappedFile = MemoryMappedFile.CreateFromFile(
                path,
                FileMode.Open,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read);
            accessor = memoryMappedFile.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);

            _mainTable = new (uint Position, uint Count)[MainTableEntryCount];
            for (int i = 0; i < MainTableEntryCount; i++)
            {
                int offset = i * 8;
                _mainTable[i] = (ReadUInt32(accessor, offset), ReadUInt32(accessor, offset + 4));
            }

            _memoryMappedFile = memoryMappedFile;
            _accessor = accessor;
            memoryMappedFile = null;
            accessor = null;
        }
        finally
        {
            accessor?.Dispose();
            memoryMappedFile?.Dispose();
        }
    }

    /// <summary>
    /// Returns the value for a key, or <see langword="null"/> when the key is absent.
    /// </summary>
    /// <param name="key">Raw CDB key bytes.</param>
    /// <returns>Stored value bytes, or <see langword="null"/>.</returns>
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        MemoryMappedViewAccessor accessor = Accessor;

        uint hash = Hash(key);
        uint hashHigh = hash / MainTableEntryCount;
        int tableIndex = checked((int)(hash % MainTableEntryCount));
        (uint position, uint count) = _mainTable[tableIndex];
        if (count == 0)
        {
            return null;
        }

        int firstSlot = checked((int)(hashHigh % count));
        for (int i = 0; i < count; i++)
        {
            int slot = (firstSlot + i) % checked((int)count);
            int entryOffset = checked((int)position + slot * 8);
            uint entryHash = ReadUInt32(accessor, entryOffset);
            uint recordPosition = ReadUInt32(accessor, entryOffset + 4);
            if (recordPosition == 0)
            {
                return null;
            }

            if (entryHash == hash && KeyEquals(accessor, recordPosition, key))
            {
                return ReadValue(accessor, recordPosition);
            }
        }

        return null;
    }

    /// <summary>
    /// Computes the CDB hash used by generated index files.
    /// </summary>
    /// <param name="key">Raw key bytes.</param>
    /// <returns>Unsigned CDB hash value.</returns>
    public static uint Hash(ReadOnlySpan<byte> key)
    {
        uint hash = 5381;
        foreach (byte b in key)
        {
            hash = ((hash * 33) & 0xFFFFFFFF) ^ b;
        }

        return hash;
    }

    /// <summary>
    /// Checks whether a record key at a CDB data position matches the requested key.
    /// </summary>
    private static bool KeyEquals(MemoryMappedViewAccessor accessor, uint recordPosition, ReadOnlySpan<byte> key)
    {
        int offset = checked((int)recordPosition);
        int keyLength = checked((int)ReadUInt32(accessor, offset));
        if (keyLength != key.Length)
        {
            return false;
        }

        int keyOffset = offset + 8;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(keyLength);
        try
        {
            accessor.ReadArray(keyOffset, buffer, 0, keyLength);
            return buffer.AsSpan(0, keyLength).SequenceEqual(key);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads the value bytes from a matching CDB record.
    /// </summary>
    private static byte[] ReadValue(MemoryMappedViewAccessor accessor, uint recordPosition)
    {
        int offset = checked((int)recordPosition);
        int keyLength = checked((int)ReadUInt32(accessor, offset));
        int valueLength = checked((int)ReadUInt32(accessor, offset + 4));
        int valueOffset = offset + 8 + keyLength;

        byte[] value = new byte[valueLength];
        accessor.ReadArray(valueOffset, value, 0, valueLength);
        return value;
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
    /// Returns the live view accessor or throws when the reader has been disposed.
    /// </summary>
    private MemoryMappedViewAccessor Accessor =>
        _accessor ?? throw new ObjectDisposedException(nameof(ConstantDatabaseReader));
}
