using System.Buffers.Binary;
using System.Text;

using AwesomeAssertions;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

public sealed class IncrementalSearchIndexTests
{
    [Fact]
    public void Search_returns_prefix_matches_in_index_order()
    {
        string indexPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            WriteIndex(
                indexPath,
                [
                    new TestItem("apple", "hm", "<h><n>apple</n></h>", "/fs/apple", 1),
                    new TestItem("application", "hm", "<h><n>application</n></h>", "/fs/application", 1),
                    new TestItem("banana", "hm", "<h><n>banana</n></h>", "/fs/banana", 1),
                ]);

            using IncrementalSearchIndex index = IncrementalSearchIndex.Open(indexPath);

            IReadOnlyList<IncrementalSearchResult> results = index.Search("APP", 10);

            results.Should().HaveCount(2);
            results[0].SortKey.Should().Be("apple");
            results[0].Label.Should().Be("<h><n>apple</n></h>");
            results[0].Path.Should().Be("/fs/apple");
            results[0].TypeCode.Should().Be("hm");
            results[1].SortKey.Should().Be("application");
        }
        finally
        {
            File.Delete(indexPath);
        }
    }

    [Fact]
    public void Search_honors_limit()
    {
        string indexPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            WriteIndex(
                indexPath,
                [
                    new TestItem("apple", "hm", "apple", "/fs/apple", 1),
                    new TestItem("application", "hm", "application", "/fs/application", 1),
                ]);

            using IncrementalSearchIndex index = IncrementalSearchIndex.Open(indexPath);

            IReadOnlyList<IncrementalSearchResult> results = index.Search("app", 1);

            results.Should().ContainSingle();
            results[0].SortKey.Should().Be("apple");
        }
        finally
        {
            File.Delete(indexPath);
        }
    }

    [Fact]
    public void Search_throws_after_index_is_disposed()
    {
        string indexPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            WriteIndex(
                indexPath,
                [
                    new TestItem("apple", "hm", "apple", "/fs/apple", 1),
                ]);

            IncrementalSearchIndex index = IncrementalSearchIndex.Open(indexPath);
            index.Dispose();

            Action act = () => index.Search("app", 10);

            act.Should().Throw<ObjectDisposedException>();
        }
        finally
        {
            File.Delete(indexPath);
        }
    }

    [Fact]
    public void Dispose_releases_index_file_handle()
    {
        string indexPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            WriteIndex(
                indexPath,
                [
                    new TestItem("apple", "hm", "apple", "/fs/apple", 1),
                ]);

            IncrementalSearchIndex index = IncrementalSearchIndex.Open(indexPath);
            index.Dispose();

            using FileStream stream = File.Open(indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Length.Should().BeGreaterThan(0);
        }
        finally
        {
            File.Delete(indexPath);
        }
    }

    private static void WriteIndex(string path, TestItem[] items)
    {
        TestItem[] sortedItems = items
            .OrderBy(item => item.Plain, StringComparer.Ordinal)
            .ThenBy(item => item.Priority)
            .ToArray();

        using MemoryStream stream = new();
        stream.Write(new byte[16]);

        List<uint> recordOffsets = [];
        foreach (TestItem item in sortedItems)
        {
            recordOffsets.Add(checked((uint)stream.Position));
            WriteRecord(stream, item);
        }

        uint offsetTableStart = checked((uint)stream.Position);
        foreach (uint offset in recordOffsets)
        {
            WriteUInt32(stream, offset);
        }

        byte[] data = stream.ToArray();
        WriteUInt32(data, 0, 0x28061691);
        WriteUInt32(data, 4, 1);
        WriteUInt32(data, 8, checked((uint)sortedItems.Length));
        WriteUInt32(data, 12, offsetTableStart);
        File.WriteAllBytes(path, data);
    }

    private static void WriteRecord(Stream stream, TestItem item)
    {
        byte[] plain = Encoding.UTF8.GetBytes(item.Plain);
        byte[] typeCode = Encoding.UTF8.GetBytes(item.TypeCode);
        byte[] label = Encoding.UTF8.GetBytes(item.Label);
        byte[] path = Encoding.ASCII.GetBytes(item.Path);

        WriteUInt16(stream, checked((ushort)plain.Length));
        stream.WriteByte(checked((byte)typeCode.Length));
        WriteUInt16(stream, checked((ushort)label.Length));
        WriteUInt16(stream, checked((ushort)path.Length));
        stream.WriteByte(item.Priority);
        stream.Write(plain);
        stream.Write(typeCode);
        stream.Write(label);
        stream.Write(path);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private sealed record TestItem(string Plain, string TypeCode, string Label, string Path, byte Priority);
}
