using AwesomeAssertions;

using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.ViewModels;

public sealed class SettingsDialogViewModelTests
{
    [Fact]
    public void Reset_commands_restore_configuration_defaults()
    {
        AppConfiguration config = new(new IndexPaths(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())))
        {
            ZoomPower = 5,
            SearchListWidth = 450,
            SearchListBaseFontSize = 18,
            ContentBaseFontSize = 20,
            ClipboardActiveMonitorIntervalMilliseconds = 250,
            ClipboardIdleMonitorIntervalMilliseconds = 2000,
            ClipboardIdleThresholdSeconds = 90,
        };
        SettingsDialogViewModel viewModel = new(config);

        viewModel.ResetZoomCommand.Execute(null);
        viewModel.ResetSearchListWidthCommand.Execute(null);
        viewModel.ResetSearchListBaseFontSizeCommand.Execute(null);
        viewModel.ResetContentBaseFontSizeCommand.Execute(null);
        viewModel.ResetClipboardTimerCommand.Execute(null);

        config.ZoomPower.Should().Be(0);
        config.SearchListWidth.Should().Be(AppConfiguration.DefaultSearchListWidth);
        config.SearchListBaseFontSize.Should().Be(AppConfiguration.DefaultSearchListBaseFontSize);
        config.ContentBaseFontSize.Should().Be(AppConfiguration.DefaultContentBaseFontSize);
        config.ClipboardActiveMonitorIntervalMilliseconds
            .Should()
            .Be(AppConfiguration.DefaultClipboardActiveMonitorIntervalMilliseconds);
        config.ClipboardIdleMonitorIntervalMilliseconds
            .Should()
            .Be(AppConfiguration.DefaultClipboardIdleMonitorIntervalMilliseconds);
        config.ClipboardIdleThresholdSeconds.Should().Be(AppConfiguration.DefaultClipboardIdleThresholdSeconds);
    }

    [Fact]
    public void Web_search_site_commands_edit_reorder_and_restore_defaults()
    {
        AppConfiguration config = new(new IndexPaths(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())));
        config.WebSearchSites.Clear();
        WebSearchSite first = new("First", "https://first.example/?q={query}");
        WebSearchSite second = new("Second", "https://second.example/?q={query}", isEnabled: false);
        config.WebSearchSites.Add(first);
        config.WebSearchSites.Add(second);
        SettingsDialogViewModel viewModel = new(config);

        viewModel.MoveWebSearchSiteUpCommand.Execute(second);
        viewModel.AddWebSearchSiteCommand.Execute(null);
        viewModel.RemoveWebSearchSiteCommand.Execute(first);

        config.WebSearchSites.Select(site => site.Title)
            .Should()
            .Equal("Second", "New Search");
        config.WebSearchSites[0].IsEnabled.Should().BeFalse();

        viewModel.RestoreDefaultWebSearchSitesCommand.Execute(null);

        config.WebSearchSites.Select(site => site.Title)
            .Should()
            .Equal("Wikipedia", "Google Images");
        config.WebSearchSites.Should().OnlyContain(site => site.IsEnabled);
    }
}
