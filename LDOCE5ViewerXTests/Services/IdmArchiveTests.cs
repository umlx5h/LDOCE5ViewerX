using AwesomeAssertions;

namespace LDOCE5ViewerX.Services;

public sealed class IdmArchiveTests
{
    private const string DataDirectoryEnvironmentVariable = "LDOCE5_DATA";

    [Fact]
    public void ListFiles_reads_fs_catalog_when_local_data_exists()
    {
        string? dataDirectory = GetDataDirectory();
        if (dataDirectory is null || !Directory.Exists(dataDirectory))
        {
            return;
        }

        IdmArchive archive = new(dataDirectory);

        archive.ListFiles("fs").Should().NotBeEmpty();
    }

    private static string? GetDataDirectory()
    {
        string? environmentPath = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return environmentPath;
        }

        string repositoryPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "ldoce5.data"));

        return Directory.Exists(repositoryPath) ? repositoryPath : null;
    }
}
