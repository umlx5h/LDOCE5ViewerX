using AwesomeAssertions;

namespace LDOCE5ViewerX.ViewModels;

public sealed class IndexerDialogViewModelTests
{
    [Fact]
    public void BrowseCommand_raises_browse_request_when_idle()
    {
        IndexerDialogViewModel viewModel = new();
        int count = 0;
        viewModel.BrowseRequested += (_, _) => count++;

        viewModel.BrowseCommand.Execute(null);

        count.Should().Be(1);
    }

    [Fact]
    public void SetSelectedDataPath_updates_path_when_idle()
    {
        IndexerDialogViewModel viewModel = new();

        viewModel.SetSelectedDataPath("/tmp/ldoce5.data");

        viewModel.DataPath.Should().Be("/tmp/ldoce5.data");
    }
}
