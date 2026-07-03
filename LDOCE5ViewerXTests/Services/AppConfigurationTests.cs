using AwesomeAssertions;

namespace LDOCE5ViewerX.Services;

public sealed class AppConfigurationTests : IDisposable
{
    private readonly string _root;

    public AppConfigurationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ldoce5viewerx-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void LoadReturnsEmptyConfigurationWhenFileDoesNotExist()
    {
        IndexPaths paths = new(_root);

        AppConfiguration config = AppConfiguration.Load(paths);

        config.DataDirectory.Should().BeNull();
        config.IndexVersion.Should().BeNull();
        config.PronunciationPlayback.Should().Be(PronunciationPlayback.British);
        config.IsAutoPronunciationPlaybackEnabled.Should().BeTrue();
        config.IsClipboardMonitoringEnabled.Should().BeFalse();
        config.IsFullIndexClipboardSearchEnabled.Should().BeFalse();
        config.ClipboardActiveMonitorIntervalMilliseconds.Should().Be(200);
        config.ClipboardIdleMonitorIntervalMilliseconds.Should().Be(1000);
        config.ClipboardIdleThresholdSeconds.Should().Be(60);
        config.ZoomPower.Should().Be(0);
        config.AudioPlaybackVolume.Should().Be(100);
        config.SearchListWidth.Should().Be(300);
        config.SearchListBaseFontSize.Should().Be(14);
        config.ContentBaseFontSize.Should().Be(15);
        config.CustomFontFamilyName.Should().BeEmpty();
        config.IsExampleItalicEnabled.Should().BeTrue();
        config.ExampleFontFamilyName.Should().BeEmpty();
        config.ThemeMode.Should().Be(ThemeMode.Light);
        config.WebSearchAssetBoxMode.Should().Be(WebSearchAssetBoxMode.NounEntriesOnly);
        config.IsWebSearchAssetBoxNounEntriesOnlySelected.Should().BeTrue();
        config.IsWebSearchAssetBoxAllEntriesSelected.Should().BeFalse();
        config.WebSearchSites.Select(site => (site.Title, site.UrlTemplate, site.IsEnabled))
            .Should()
            .Equal(
                ("Wikipedia", "https://en.wikipedia.org/w/index.php?search={query}", true),
                ("Google Images", "https://www.google.com/images?hl=en&q={query}", true));
    }

    [Fact]
    public void SavePersistsSettings()
    {
        IndexPaths paths = new(_root);
        AppConfiguration config = new(paths)
        {
            DataDirectory = Path.Combine(_root, "ldoce5.data"),
            IndexVersion = AppConfiguration.CurrentIndexVersion,
            PronunciationPlayback = PronunciationPlayback.American,
            IsAutoPronunciationPlaybackEnabled = false,
            IsClipboardMonitoringEnabled = true,
            IsFullIndexClipboardSearchEnabled = true,
            ClipboardActiveMonitorIntervalMilliseconds = 250,
            ClipboardIdleMonitorIntervalMilliseconds = 2000,
            ClipboardIdleThresholdSeconds = 90,
            ZoomPower = 7,
            AudioPlaybackVolume = 42,
            SearchListWidth = 360,
            SearchListBaseFontSize = 16,
            ContentBaseFontSize = 18,
            CustomFontFamilyName = "Segoe UI",
            IsExampleItalicEnabled = false,
            ExampleFontFamilyName = "Georgia",
            ThemeMode = ThemeMode.Dark,
            WebSearchAssetBoxMode = WebSearchAssetBoxMode.AllEntries,
        };
        config.WebSearchSites.Clear();
        config.WebSearchSites.Add(new WebSearchSite("Images", "https://images.example/search?q={query}"));
        config.WebSearchSites.Add(new WebSearchSite("Disabled", "https://disabled.example/?q={query}", isEnabled: false));

        config.Save();
        AppConfiguration loaded = AppConfiguration.Load(paths);
        string json = File.ReadAllText(paths.ConfigurationPath);

        loaded.DataDirectory.Should().Be(config.DataDirectory);
        loaded.IndexVersion.Should().Be(AppConfiguration.CurrentIndexVersion);
        loaded.PronunciationPlayback.Should().Be(PronunciationPlayback.American);
        loaded.IsAutoPronunciationPlaybackEnabled.Should().BeFalse();
        loaded.IsClipboardMonitoringEnabled.Should().BeTrue();
        loaded.IsFullIndexClipboardSearchEnabled.Should().BeTrue();
        loaded.ClipboardActiveMonitorIntervalMilliseconds.Should().Be(250);
        loaded.ClipboardIdleMonitorIntervalMilliseconds.Should().Be(2000);
        loaded.ClipboardIdleThresholdSeconds.Should().Be(90);
        loaded.ZoomPower.Should().Be(7);
        loaded.AudioPlaybackVolume.Should().Be(42);
        loaded.SearchListWidth.Should().Be(360);
        loaded.SearchListBaseFontSize.Should().Be(16);
        loaded.ContentBaseFontSize.Should().Be(18);
        loaded.CustomFontFamilyName.Should().Be("Segoe UI");
        loaded.IsExampleItalicEnabled.Should().BeFalse();
        loaded.ExampleFontFamilyName.Should().Be("Georgia");
        loaded.ThemeMode.Should().Be(ThemeMode.Dark);
        loaded.WebSearchAssetBoxMode.Should().Be(WebSearchAssetBoxMode.AllEntries);
        loaded.WebSearchSites.Select(site => (site.Title, site.UrlTemplate, site.IsEnabled))
            .Should()
            .Equal(
                ("Images", "https://images.example/search?q={query}", true),
                ("Disabled", "https://disabled.example/?q={query}", false));
        json.Should().Contain("\"DataDirectory\"");
        json.Should().Contain("\"IndexVersion\"");
        json.Should().Contain("\"PronunciationPlayback\"");
        json.Should().Contain("\"IsAutoPronunciationPlaybackEnabled\"");
        json.Should().Contain("\"IsClipboardMonitoringEnabled\"");
        json.Should().Contain("\"IsFullIndexClipboardSearchEnabled\"");
        json.Should().Contain("\"ClipboardActiveMonitorIntervalMilliseconds\"");
        json.Should().Contain("\"ClipboardIdleMonitorIntervalMilliseconds\"");
        json.Should().Contain("\"ClipboardIdleThresholdSeconds\"");
        json.Should().Contain("\"ZoomPower\"");
        json.Should().Contain("\"AudioPlaybackVolume\"");
        json.Should().Contain("\"SearchListWidth\"");
        json.Should().Contain("\"SearchListBaseFontSize\"");
        json.Should().Contain("\"ContentBaseFontSize\"");
        json.Should().Contain("\"CustomFontFamilyName\"");
        json.Should().Contain("\"IsExampleItalicEnabled\"");
        json.Should().Contain("\"ExampleFontFamilyName\"");
        json.Should().Contain("\"ThemeMode\"");
        json.Should().Contain("\"WebSearchAssetBoxMode\"");
        json.Should().Contain("\"WebSearchSites\"");
    }

    [Fact]
    public void Setting_property_raises_change_notifications_for_dependent_values()
    {
        IndexPaths paths = new(_root);
        AppConfiguration config = new(paths);
        List<string?> propertyNames = [];
        config.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName);

        config.ZoomPower = 2;
        config.SearchListBaseFontSize = 18;
        config.CustomFontFamilyName = "Segoe UI";
        config.PronunciationPlayback = PronunciationPlayback.American;
        config.ThemeMode = ThemeMode.Dark;
        config.WebSearchAssetBoxMode = WebSearchAssetBoxMode.AllEntries;
        config.WebSearchSites[0].IsEnabled = false;

        propertyNames.Should().Contain(nameof(AppConfiguration.ZoomPower));
        propertyNames.Should().Contain(nameof(AppConfiguration.ZoomFactor));
        propertyNames.Should().Contain(nameof(AppConfiguration.ZoomPercentageText));
        propertyNames.Should().Contain(nameof(AppConfiguration.SearchResultSnippetFontSize));
        propertyNames.Should().Contain(nameof(AppConfiguration.SearchResultBadgeFontSize));
        propertyNames.Should().Contain(nameof(AppConfiguration.ApplicationFontFamily));
        propertyNames.Should().Contain(nameof(AppConfiguration.ExampleFontFamily));
        propertyNames.Should().Contain(nameof(AppConfiguration.IsAmericanPronunciationPlaybackSelected));
        propertyNames.Should().Contain(nameof(AppConfiguration.IsDarkThemeModeSelected));
        propertyNames.Should().Contain(nameof(AppConfiguration.WebSearchAssetBoxMode));
        propertyNames.Should().Contain(nameof(AppConfiguration.IsWebSearchAssetBoxAllEntriesSelected));
        propertyNames.Should().Contain(nameof(AppConfiguration.WebSearchSites));
    }

    [Fact]
    public void Settings_clamp_to_supported_ranges()
    {
        IndexPaths paths = new(_root);
        AppConfiguration config = new(paths)
        {
            ZoomPower = 99,
            AudioPlaybackVolume = double.PositiveInfinity,
            SearchListWidth = 100,
            SearchListBaseFontSize = 4,
            ContentBaseFontSize = 5,
            ClipboardActiveMonitorIntervalMilliseconds = 1,
            ClipboardIdleMonitorIntervalMilliseconds = 1,
            ClipboardIdleThresholdSeconds = 0,
        };

        config.ZoomPower.Should().Be(20);
        config.AudioPlaybackVolume.Should().Be(1);
        config.SearchListWidth.Should().Be(180);
        config.SearchListBaseFontSize.Should().Be(10);
        config.ContentBaseFontSize.Should().Be(10);
        config.ClipboardActiveMonitorIntervalMilliseconds.Should().Be(50);
        config.ClipboardIdleMonitorIntervalMilliseconds.Should().Be(50);
        config.ClipboardIdleThresholdSeconds.Should().Be(1);

        config.ZoomPower = -99;
        config.AudioPlaybackVolume = 200;
        config.SearchListWidth = 900;
        config.SearchListBaseFontSize = 40;
        config.ContentBaseFontSize = 50;
        config.ClipboardActiveMonitorIntervalMilliseconds = 10000;
        config.ClipboardIdleMonitorIntervalMilliseconds = 120000;
        config.ClipboardIdleThresholdSeconds = 7200;

        config.ZoomPower.Should().Be(-10);
        config.AudioPlaybackVolume.Should().Be(100);
        config.SearchListWidth.Should().Be(600);
        config.SearchListBaseFontSize.Should().Be(24);
        config.ContentBaseFontSize.Should().Be(28);
        config.ClipboardActiveMonitorIntervalMilliseconds.Should().Be(5000);
        config.ClipboardIdleMonitorIntervalMilliseconds.Should().Be(60000);
        config.ClipboardIdleThresholdSeconds.Should().Be(3600);
    }

    [Fact]
    public void IsUserConfigProperty_returns_whether_property_should_mark_config_dirty()
    {
        AppConfiguration.IsUserConfigProperty(nameof(AppConfiguration.ThemeMode)).Should().BeTrue();
        AppConfiguration.IsUserConfigProperty(nameof(AppConfiguration.CustomFontFamilyName)).Should().BeTrue();
        AppConfiguration.IsUserConfigProperty(nameof(AppConfiguration.WebSearchSites)).Should().BeTrue();
        AppConfiguration.IsUserConfigProperty(nameof(AppConfiguration.WebSearchAssetBoxMode)).Should().BeTrue();
        AppConfiguration.IsUserConfigProperty(nameof(AppConfiguration.ClipboardActiveMonitorIntervalMilliseconds)).Should().BeTrue();
        AppConfiguration.IsUserConfigProperty(nameof(AppConfiguration.ClipboardIdleMonitorIntervalMilliseconds)).Should().BeTrue();
        AppConfiguration.IsUserConfigProperty(nameof(AppConfiguration.ClipboardIdleThresholdSeconds)).Should().BeTrue();
        AppConfiguration.IsUserConfigProperty(nameof(AppConfiguration.DataDirectory)).Should().BeFalse();
        AppConfiguration.IsUserConfigProperty(nameof(AppConfiguration.ZoomFactor)).Should().BeFalse();
        AppConfiguration.IsUserConfigProperty(null).Should().BeFalse();
    }

    [Fact]
    public void Equality_uses_persisted_settings_and_ignores_storage_path()
    {
        AppConfiguration first = new(new IndexPaths(Path.Combine(_root, "first")))
        {
            DataDirectory = Path.Combine(_root, "ldoce5.data"),
            IndexVersion = AppConfiguration.CurrentIndexVersion,
            PronunciationPlayback = PronunciationPlayback.American,
            IsAutoPronunciationPlaybackEnabled = false,
            IsClipboardMonitoringEnabled = true,
            IsFullIndexClipboardSearchEnabled = true,
            ClipboardActiveMonitorIntervalMilliseconds = 250,
            ClipboardIdleMonitorIntervalMilliseconds = 2000,
            ClipboardIdleThresholdSeconds = 90,
            ZoomPower = 3,
            AudioPlaybackVolume = 35,
            SearchListWidth = 420,
            SearchListBaseFontSize = 17,
            ContentBaseFontSize = 19,
            CustomFontFamilyName = "Segoe UI",
            IsExampleItalicEnabled = false,
            ExampleFontFamilyName = "Georgia",
            ThemeMode = ThemeMode.Automatic,
            WebSearchAssetBoxMode = WebSearchAssetBoxMode.AllEntries,
        };
        first.WebSearchSites.Clear();
        first.WebSearchSites.Add(new WebSearchSite("Custom", "https://example.com/?q={query}"));
        AppConfiguration second = new(new IndexPaths(Path.Combine(_root, "second")))
        {
            DataDirectory = first.DataDirectory,
            IndexVersion = first.IndexVersion,
            PronunciationPlayback = first.PronunciationPlayback,
            IsAutoPronunciationPlaybackEnabled = first.IsAutoPronunciationPlaybackEnabled,
            IsClipboardMonitoringEnabled = first.IsClipboardMonitoringEnabled,
            IsFullIndexClipboardSearchEnabled = first.IsFullIndexClipboardSearchEnabled,
            ClipboardActiveMonitorIntervalMilliseconds = first.ClipboardActiveMonitorIntervalMilliseconds,
            ClipboardIdleMonitorIntervalMilliseconds = first.ClipboardIdleMonitorIntervalMilliseconds,
            ClipboardIdleThresholdSeconds = first.ClipboardIdleThresholdSeconds,
            ZoomPower = first.ZoomPower,
            AudioPlaybackVolume = first.AudioPlaybackVolume,
            SearchListWidth = first.SearchListWidth,
            SearchListBaseFontSize = first.SearchListBaseFontSize,
            ContentBaseFontSize = first.ContentBaseFontSize,
            CustomFontFamilyName = first.CustomFontFamilyName,
            IsExampleItalicEnabled = first.IsExampleItalicEnabled,
            ExampleFontFamilyName = first.ExampleFontFamilyName,
            ThemeMode = first.ThemeMode,
            WebSearchAssetBoxMode = first.WebSearchAssetBoxMode,
        };
        second.WebSearchSites.Clear();
        second.WebSearchSites.Add(new WebSearchSite("Custom", "https://example.com/?q={query}"));

        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());

        second.ZoomPower++;

        first.Equals(second).Should().BeFalse();

        second.ZoomPower = first.ZoomPower;
        second.ClipboardActiveMonitorIntervalMilliseconds++;

        first.Equals(second).Should().BeFalse();

        second.ClipboardActiveMonitorIntervalMilliseconds = first.ClipboardActiveMonitorIntervalMilliseconds;
        second.ThemeMode = ThemeMode.Dark;

        first.Equals(second).Should().BeFalse();

        second.ThemeMode = first.ThemeMode;
        second.WebSearchSites[0].Title = "Changed";

        first.Equals(second).Should().BeFalse();

        second.WebSearchSites[0].Title = first.WebSearchSites[0].Title;
        second.WebSearchAssetBoxMode = WebSearchAssetBoxMode.NounEntriesOnly;

        first.Equals(second).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
