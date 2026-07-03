using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Resolves dictionary archive item names to compressed archive locations using <c>filemap.cdb</c>.
/// </summary>
public sealed class FilemapReader : IDisposable
{
    private readonly ConstantDatabaseReader _database;

    /// <summary>
    /// Loads the file-location map.
    /// </summary>
    /// <param name="filemapPath">Path to <c>filemap.cdb</c>.</param>
    public FilemapReader(string filemapPath)
    {
        _database = new ConstantDatabaseReader(filemapPath);
    }

    /// <summary>
    /// Looks up one archive item by archive name and logical item name.
    /// </summary>
    /// <param name="archive">Archive name, such as <c>fs</c>.</param>
    /// <param name="name">Logical item name stored in the file map.</param>
    /// <returns>The archive block location, or <see langword="null"/> if absent.</returns>
    public ArchiveLocation? Lookup(string archive, string name)
    {
        byte[] key = CreateKey(archive, name);
        byte[]? data = _database.Get(key);
        if (data is null)
        {
            return null;
        }

        if (data.Length == 16)
        {
            return new ArchiveLocation(
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4))),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4))),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4))),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12, 4))));
        }

        if (data.Length == 10)
        {
            return new ArchiveLocation(
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4))),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8, 2)));
        }

        throw new InvalidDataException("The filemap location record has an unsupported size.");
    }

    /// <summary>
    /// Builds the first ten bytes of MD5(<c>archive:name</c>) used by the Python file map.
    /// </summary>
    private static byte[] CreateKey(string archive, string name)
    {
        byte[] fullHash = MD5.HashData(Encoding.ASCII.GetBytes($"{archive}:{name}"));
        byte[] key = new byte[10];
        Buffer.BlockCopy(fullHash, 0, key, 0, key.Length);
        return key;
    }

    /// <summary>
    /// Releases the file map database reader.
    /// </summary>
    public void Dispose()
    {
        _database.Dispose();
    }
}
