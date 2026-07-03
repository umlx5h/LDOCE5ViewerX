using System.Text;

using AwesomeAssertions;

namespace LDOCE5ViewerX.Services;

public sealed class ConstantDatabaseWriterTests
{
    [Fact]
    public void Writer_round_trips_values_with_reader()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FileStream stream = File.Create(path))
            {
                ConstantDatabaseWriter writer = new(stream);
                writer.Add(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("value"));
                writer.FinalizeDatabase();
            }

            using ConstantDatabaseReader reader = new(path);

            byte[]? data = reader.Get(Encoding.UTF8.GetBytes("key"));

            data.Should().NotBeNull();
            Encoding.UTF8.GetString(data!).Should().Be("value");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reader_throws_after_dispose()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FileStream stream = File.Create(path))
            {
                ConstantDatabaseWriter writer = new(stream);
                writer.Add(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("value"));
                writer.FinalizeDatabase();
            }

            ConstantDatabaseReader reader = new(path);
            reader.Dispose();

            Action act = () => reader.Get(Encoding.UTF8.GetBytes("key"));

            act.Should().Throw<ObjectDisposedException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dispose_releases_cdb_file_handle()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FileStream stream = File.Create(path))
            {
                ConstantDatabaseWriter writer = new(stream);
                writer.Add(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("value"));
                writer.FinalizeDatabase();
            }

            ConstantDatabaseReader reader = new(path);
            reader.Dispose();

            using FileStream reopenedStream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            reopenedStream.Length.Should().BeGreaterThan(0);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
