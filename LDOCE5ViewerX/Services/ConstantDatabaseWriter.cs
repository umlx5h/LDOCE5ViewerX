using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Writes CDB files used by generated indexes.
/// </summary>
public sealed class ConstantDatabaseWriter
{
    private const int MainTableEntryCount = 256;
    private const int MainTableSize = MainTableEntryCount * 8;

    private readonly Stream _stream;
    private readonly List<(uint Hash, uint Position)>[] _subTables;

    /// <summary>
    /// Creates a CDB writer over an already-open writable stream.
    /// </summary>
    /// <param name="stream">Destination stream.</param>
    public ConstantDatabaseWriter(Stream stream)
    {
        _stream = stream;
        _stream.SetLength(0);
        _stream.Position = MainTableSize;
        _subTables = Enumerable.Range(0, MainTableEntryCount)
            .Select(_ => new List<(uint Hash, uint Position)>())
            .ToArray();
    }

    /// <summary>
    /// Adds one key-value pair to the CDB data section.
    /// </summary>
    /// <param name="key">Raw key bytes.</param>
    /// <param name="value">Raw value bytes.</param>
    public void Add(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        uint position = checked((uint)_stream.Position);
        WriteUInt32(checked((uint)key.Length));
        WriteUInt32(checked((uint)value.Length));
        _stream.Write(key);
        _stream.Write(value);

        uint hash = ConstantDatabaseReader.Hash(key);
        _subTables[hash & 0xFF].Add((hash, position));
    }

    /// <summary>
    /// Writes subtables and the main table, completing the CDB file.
    /// </summary>
    public void FinalizeDatabase()
    {
        (uint Position, uint Count)[] mainTable = new (uint Position, uint Count)[MainTableEntryCount];

        for (int tableIndex = 0; tableIndex < MainTableEntryCount; tableIndex++)
        {
            List<(uint Hash, uint Position)> entries = _subTables[tableIndex];
            uint slotCount = checked((uint)(entries.Count * 2));
            uint subTablePosition = checked((uint)_stream.Position);
            byte[] subTableBytes = new byte[checked((int)slotCount * 8)];

            foreach ((uint hash, uint position) in entries)
            {
                uint hashHigh = hash / MainTableEntryCount;
                int initialSlot = checked((int)(hashHigh % slotCount));
                for (int attempt = 0; attempt < slotCount; attempt++)
                {
                    int slot = (initialSlot + attempt) % checked((int)slotCount);
                    int offset = slot * 8;
                    uint existingPosition = BinaryPrimitives.ReadUInt32LittleEndian(subTableBytes.AsSpan(offset + 4, 4));
                    if (existingPosition == 0)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(subTableBytes.AsSpan(offset, 4), hash);
                        BinaryPrimitives.WriteUInt32LittleEndian(subTableBytes.AsSpan(offset + 4, 4), position);
                        break;
                    }
                }
            }

            _stream.Write(subTableBytes);
            mainTable[tableIndex] = (subTablePosition, slotCount);
        }

        _stream.Position = 0;
        foreach ((uint position, uint count) in mainTable)
        {
            WriteUInt32(position);
            WriteUInt32(count);
        }
    }

    /// <summary>
    /// Writes a little-endian unsigned 32-bit integer.
    /// </summary>
    private void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }
}
