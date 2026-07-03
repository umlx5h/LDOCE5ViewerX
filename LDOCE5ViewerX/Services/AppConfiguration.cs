using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Avalonia;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Stores user-specific application settings on disk.
/// </summary>
public sealed class AppConfiguration : ObservableObject, IEquatable<AppConfiguration>
{
    /// <summary>
    /// Index version expected by the running application.
    /// </summary>
    public const string CurrentIndexVersion = "1";

    /// <summary>
    /// Default pronunciation playback volume as a percentage.
    /// </summary>
    public const double DefaultAudioPlaybackVolume = 100;

    /// <summary>
    /// Default width of the search result list in pixels.
    /// </summary>
    public const double DefaultSearchListWidth = 300;

    /// <summary>
    /// Default base font size used by search result labels.
    /// </summary>
    public const double DefaultSearchListBaseFontSize = 14;

    /// <summary>
    /// Default base font size used by dictionary content.
    /// </summary>
    public const double DefaultContentBaseFontSize = 15;

    /// <summary>
    /// Default clipboard polling interval while clipboard changes are active, in milliseconds.
    /// </summary>
    public const int DefaultClipboardActiveMonitorIntervalMilliseconds = 200;

    /// <summary>
    /// Default clipboard polling interval after clipboard changes have been idle, in milliseconds.
    /// </summary>
    public const int DefaultClipboardIdleMonitorIntervalMilliseconds = 1000;

    /// <summary>
    /// Default idle duration before clipboard polling switches to the idle interval, in seconds.
    /// </summary>
    public const int DefaultClipboardIdleThresholdSeconds = 60;

    /// <summary>
    /// Minimum supported dictionary content zoom power.
    /// </summary>
    public const double MinimumZoomPower = -10;

    /// <summary>
    /// Maximum supported dictionary content zoom power.
    /// </summary>
    public const double MaximumZoomPower = 20;

    /// <summary>
    /// Minimum pronunciation playback volume as a percentage.
    /// </summary>
    public const double MinimumAudioPlaybackVolume = 1;

    /// <summary>
    /// Maximum pronunciation playback volume as a percentage.
    /// </summary>
    public const double MaximumAudioPlaybackVolume = 100;

    /// <summary>
    /// Minimum width of the search result list in pixels.
    /// </summary>
    public const double MinimumSearchListWidth = 180;

    /// <summary>
    /// Maximum width of the search result list in pixels.
    /// </summary>
    public const double MaximumSearchListWidth = 600;

    /// <summary>
    /// Minimum base font size used by search result labels.
    /// </summary>
    public const double MinimumSearchListBaseFontSize = 10;

    /// <summary>
    /// Maximum base font size used by search result labels.
    /// </summary>
    public const double MaximumSearchListBaseFontSize = 24;

    /// <summary>
    /// Minimum base font size used by dictionary content.
    /// </summary>
    public const double MinimumContentBaseFontSize = 10;

    /// <summary>
    /// Maximum base font size used by dictionary content.
    /// </summary>
    public const double MaximumContentBaseFontSize = 28;

    /// <summary>
    /// Minimum clipboard polling interval while clipboard changes are active, in milliseconds.
    /// </summary>
    public const int MinimumClipboardActiveMonitorIntervalMilliseconds = 50;

    /// <summary>
    /// Maximum clipboard polling interval while clipboard changes are active, in milliseconds.
    /// </summary>
    public const int MaximumClipboardActiveMonitorIntervalMilliseconds = 5000;

    /// <summary>
    /// Minimum clipboard polling interval after clipboard changes have been idle, in milliseconds.
    /// </summary>
    public const int MinimumClipboardIdleMonitorIntervalMilliseconds = 50;

    /// <summary>
    /// Maximum clipboard polling interval after clipboard changes have been idle, in milliseconds.
    /// </summary>
    public const int MaximumClipboardIdleMonitorIntervalMilliseconds = 60000;

    /// <summary>
    /// Minimum idle duration before clipboard polling switches to the idle interval, in seconds.
    /// </summary>
    public const int MinimumClipboardIdleThresholdSeconds = 1;

    /// <summary>
    /// Maximum idle duration before clipboard polling switches to the idle interval, in seconds.
    /// </summary>
    public const int MaximumClipboardIdleThresholdSeconds = 3600;

    private const double ZoomStepFactor = 1.05;

    private string _path;

    /// <summary>
    /// Creates a configuration bound to the generated app data directory.
    /// </summary>
    /// <param name="indexPaths">Filesystem paths used by the application.</param>
    public AppConfiguration(IndexPaths indexPaths)
        : this(indexPaths.ConfigurationPath)
    {
    }

    [JsonConstructor]
    internal AppConfiguration()
        : this(string.Empty)
    {
    }

    private AppConfiguration(string path)
    {
        _path = path;
        AttachWebSearchSites(WebSearchSites);
    }

    /// <summary>
    /// Local <c>ldoce5.data</c> directory selected during indexing.
    /// </summary>
    public string? DataDirectory
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Application version that created the current generated index.
    /// </summary>
    public string? IndexVersion
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Pronunciation variant to play from the manual playback command.
    /// </summary>
    public PronunciationPlayback PronunciationPlayback
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsBritishPronunciationPlaybackSelected));
                OnPropertyChanged(nameof(IsAmericanPronunciationPlaybackSelected));
            }
        }
    } = PronunciationPlayback.British;

    /// <summary>
    /// Whether entries play pronunciation automatically when selected.
    /// </summary>
    public bool IsAutoPronunciationPlaybackEnabled
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    /// <summary>
    /// Whether the main window watches clipboard text while inactive and searches matching entries.
    /// </summary>
    public bool IsClipboardMonitoringEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Whether clipboard searches may use the headword and phrase full-text index.
    /// </summary>
    public bool IsFullIndexClipboardSearchEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Clipboard polling interval while clipboard changes are active, in milliseconds.
    /// </summary>
    public int ClipboardActiveMonitorIntervalMilliseconds
    {
        get;
        set => SetProperty(
            ref field,
            Math.Clamp(
                value,
                MinimumClipboardActiveMonitorIntervalMilliseconds,
                MaximumClipboardActiveMonitorIntervalMilliseconds));
    } = DefaultClipboardActiveMonitorIntervalMilliseconds;

    /// <summary>
    /// Clipboard polling interval after clipboard changes have been idle, in milliseconds.
    /// </summary>
    public int ClipboardIdleMonitorIntervalMilliseconds
    {
        get;
        set => SetProperty(
            ref field,
            Math.Clamp(
                value,
                MinimumClipboardIdleMonitorIntervalMilliseconds,
                MaximumClipboardIdleMonitorIntervalMilliseconds));
    } = DefaultClipboardIdleMonitorIntervalMilliseconds;

    /// <summary>
    /// Idle duration before clipboard polling switches to the idle interval, in seconds.
    /// </summary>
    public int ClipboardIdleThresholdSeconds
    {
        get;
        set => SetProperty(
            ref field,
            Math.Clamp(value, MinimumClipboardIdleThresholdSeconds, MaximumClipboardIdleThresholdSeconds));
    } = DefaultClipboardIdleThresholdSeconds;

    /// <summary>
    /// Power used to calculate the dictionary content zoom factor.
    /// </summary>
    public int ZoomPower
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, (int)MinimumZoomPower, (int)MaximumZoomPower);
            if (SetProperty(ref field, clamped))
            {
                OnPropertyChanged(nameof(ZoomFactor));
                OnPropertyChanged(nameof(ZoomStatusText));
                OnPropertyChanged(nameof(ZoomPercentageText));
                OnPropertyChanged(nameof(ZoomInMenuHeader));
                OnPropertyChanged(nameof(ZoomOutMenuHeader));
                OnPropertyChanged(nameof(NormalSizeMenuHeader));
            }
        }
    }

    /// <summary>
    /// Pronunciation playback volume as a percentage from 1 to 100.
    /// </summary>
    public double AudioPlaybackVolume
    {
        get;
        set => SetProperty(ref field, ClampFinite(value, MinimumAudioPlaybackVolume, MaximumAudioPlaybackVolume));
    } = DefaultAudioPlaybackVolume;

    /// <summary>
    /// Width of the search result list in pixels.
    /// </summary>
    public double SearchListWidth
    {
        get;
        set => SetProperty(ref field, ClampFinite(value, MinimumSearchListWidth, MaximumSearchListWidth));
    } = DefaultSearchListWidth;

    /// <summary>
    /// Base font size used by search result labels.
    /// </summary>
    public double SearchListBaseFontSize
    {
        get;
        set
        {
            if (SetProperty(ref field, ClampFinite(value, MinimumSearchListBaseFontSize, MaximumSearchListBaseFontSize)))
            {
                OnPropertyChanged(nameof(SearchResultSnippetFontSize));
                OnPropertyChanged(nameof(SearchResultBadgeFontSize));
            }
        }
    } = DefaultSearchListBaseFontSize;

    /// <summary>
    /// Base font size used by dictionary content.
    /// </summary>
    public double ContentBaseFontSize
    {
        get;
        set => SetProperty(ref field, ClampFinite(value, MinimumContentBaseFontSize, MaximumContentBaseFontSize));
    } = DefaultContentBaseFontSize;

    /// <summary>
    /// Optional user-selected application font family name.
    /// </summary>
    public string CustomFontFamilyName
    {
        get;
        set
        {
            if (SetProperty(ref field, NormalizeCustomFontFamilyName(value)))
            {
                OnPropertyChanged(nameof(ApplicationFontFamily));
                OnPropertyChanged(nameof(ExampleFontFamily));
            }
        }
    } = string.Empty;

    /// <summary>
    /// Whether example sentences are displayed in italic text.
    /// </summary>
    public bool IsExampleItalicEnabled
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    /// <summary>
    /// Optional user-selected font family name for example sentences.
    /// </summary>
    public string ExampleFontFamilyName
    {
        get;
        set
        {
            if (SetProperty(ref field, NormalizeCustomFontFamilyName(value)))
            {
                OnPropertyChanged(nameof(ExampleFontFamily));
            }
        }
    } = string.Empty;

    /// <summary>
    /// User-selected application theme mode.
    /// </summary>
    public ThemeMode ThemeMode
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsAutomaticThemeModeSelected));
                OnPropertyChanged(nameof(IsLightThemeModeSelected));
                OnPropertyChanged(nameof(IsDarkThemeModeSelected));
            }
        }
    } = ThemeMode.Light;

    /// <summary>
    /// User-configured web search engines shown in AssetBox and selected-text menus.
    /// </summary>
    public ObservableCollection<WebSearchSite> WebSearchSites
    {
        get;
        set
        {
            ObservableCollection<WebSearchSite> sites = NormalizeWebSearchSites(value);
            if (ReferenceEquals(field, sites))
            {
                return;
            }

            DetachWebSearchSites(field);
            if (SetProperty(ref field, sites))
            {
                AttachWebSearchSites(sites);
            }
        }
    } = new(WebSearchLinks.CreateDefaultSites());

    /// <summary>
    /// Controls which entry pages show web search links in the AssetBox.
    /// </summary>
    public WebSearchAssetBoxMode WebSearchAssetBoxMode
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsWebSearchAssetBoxNounEntriesOnlySelected));
                OnPropertyChanged(nameof(IsWebSearchAssetBoxAllEntriesSelected));
            }
        }
    } = WebSearchAssetBoxMode.NounEntriesOnly;

    /// <summary>
    /// Gets the scale factor applied to dictionary content.
    /// </summary>
    [JsonIgnore]
    public double ZoomFactor => Math.Pow(ZoomStepFactor, ZoomPower);

    /// <summary>
    /// Gets the font size used by search result snippets.
    /// </summary>
    [JsonIgnore]
    public double SearchResultSnippetFontSize => SearchListBaseFontSize * 0.9;

    /// <summary>
    /// Gets the font size used by search result kind badges.
    /// </summary>
    [JsonIgnore]
    public double SearchResultBadgeFontSize => SearchListBaseFontSize * 0.75;

    /// <summary>
    /// Gets the application font family selected by the user, or global font when no custom font is set.
    /// </summary>
    [JsonIgnore]
    public FontFamily ApplicationFontFamily => GetEffectiveFontFamily(CustomFontFamilyName);

    /// <summary>
    /// Gets the font family used by example sentences.
    /// </summary>
    [JsonIgnore]
    public FontFamily ExampleFontFamily => GetEffectiveFontFamily(ExampleFontFamilyName, ApplicationFontFamily);

    /// <summary>
    /// Gets the zoom status text shown in the main window.
    /// </summary>
    [JsonIgnore]
    public string ZoomStatusText => $"Zoom {ZoomPercentageText}";

    /// <summary>
    /// Gets the current dictionary content zoom percentage.
    /// </summary>
    [JsonIgnore]
    public string ZoomPercentageText => $"{(int)Math.Round(ZoomFactor * 100, MidpointRounding.AwayFromZero)}%";

    /// <summary>
    /// Gets the zoom-in menu label with the current zoom percentage.
    /// </summary>
    [JsonIgnore]
    public string ZoomInMenuHeader => $"Zoom In ({ZoomPercentageText})";

    /// <summary>
    /// Gets the zoom-out menu label with the current zoom percentage.
    /// </summary>
    [JsonIgnore]
    public string ZoomOutMenuHeader => $"Zoom Out ({ZoomPercentageText})";

    /// <summary>
    /// Gets the normal-size menu label with the current zoom percentage.
    /// </summary>
    [JsonIgnore]
    public string NormalSizeMenuHeader => $"Normal Size ({ZoomPercentageText})";

    /// <summary>
    /// Gets or sets whether British manual pronunciation playback is selected.
    /// </summary>
    [JsonIgnore]
    public bool IsBritishPronunciationPlaybackSelected
    {
        get => PronunciationPlayback == PronunciationPlayback.British;
        set
        {
            if (value)
            {
                PronunciationPlayback = PronunciationPlayback.British;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether American manual pronunciation playback is selected.
    /// </summary>
    [JsonIgnore]
    public bool IsAmericanPronunciationPlaybackSelected
    {
        get => PronunciationPlayback == PronunciationPlayback.American;
        set
        {
            if (value)
            {
                PronunciationPlayback = PronunciationPlayback.American;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the app should follow the operating system theme.
    /// </summary>
    [JsonIgnore]
    public bool IsAutomaticThemeModeSelected
    {
        get => ThemeMode == ThemeMode.Automatic;
        set
        {
            if (value)
            {
                ThemeMode = ThemeMode.Automatic;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the app should use the light theme.
    /// </summary>
    [JsonIgnore]
    public bool IsLightThemeModeSelected
    {
        get => ThemeMode == ThemeMode.Light;
        set
        {
            if (value)
            {
                ThemeMode = ThemeMode.Light;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the app should use the dark theme.
    /// </summary>
    [JsonIgnore]
    public bool IsDarkThemeModeSelected
    {
        get => ThemeMode == ThemeMode.Dark;
        set
        {
            if (value)
            {
                ThemeMode = ThemeMode.Dark;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether AssetBox web search links are shown only for noun entries.
    /// </summary>
    [JsonIgnore]
    public bool IsWebSearchAssetBoxNounEntriesOnlySelected
    {
        get => WebSearchAssetBoxMode == WebSearchAssetBoxMode.NounEntriesOnly;
        set
        {
            if (value)
            {
                WebSearchAssetBoxMode = WebSearchAssetBoxMode.NounEntriesOnly;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether AssetBox web search links are shown for all entries.
    /// </summary>
    [JsonIgnore]
    public bool IsWebSearchAssetBoxAllEntriesSelected
    {
        get => WebSearchAssetBoxMode == WebSearchAssetBoxMode.AllEntries;
        set
        {
            if (value)
            {
                WebSearchAssetBoxMode = WebSearchAssetBoxMode.AllEntries;
            }
        }
    }

    /// <summary>
    /// Restores the default web search engine list.
    /// </summary>
    public void RestoreDefaultWebSearchSites()
    {
        WebSearchSites = new ObservableCollection<WebSearchSite>(WebSearchLinks.CreateDefaultSites());
    }

    /// <summary>
    /// Loads configuration from disk, returning empty settings when no file exists.
    /// </summary>
    /// <param name="indexPaths">Filesystem paths used by the application.</param>
    /// <returns>The loaded configuration.</returns>
    public static AppConfiguration Load(IndexPaths indexPaths)
    {
        AppConfiguration config = new(indexPaths);
        if (!File.Exists(config._path))
        {
            return config;
        }

        try
        {
            string json = File.ReadAllText(config._path);
            AppConfiguration? loaded = JsonSerializer.Deserialize(json, AppConfigurationJsonContext.Default.AppConfiguration);
            if (loaded is not null)
            {
                loaded._path = config._path;
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return config;
    }

    /// <summary>
    /// Saves configuration to disk.
    /// </summary>
    public void Save()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(this, AppConfigurationJsonContext.Default.AppConfiguration);
        File.WriteAllText(_path, json);
    }

    /// <inheritdoc/>
    public bool Equals(AppConfiguration? other)
    {
        return other is not null
            && string.Equals(DataDirectory, other.DataDirectory, StringComparison.Ordinal)
            && string.Equals(IndexVersion, other.IndexVersion, StringComparison.Ordinal)
            && PronunciationPlayback == other.PronunciationPlayback
            && IsAutoPronunciationPlaybackEnabled == other.IsAutoPronunciationPlaybackEnabled
            && IsClipboardMonitoringEnabled == other.IsClipboardMonitoringEnabled
            && IsFullIndexClipboardSearchEnabled == other.IsFullIndexClipboardSearchEnabled
            && ClipboardActiveMonitorIntervalMilliseconds == other.ClipboardActiveMonitorIntervalMilliseconds
            && ClipboardIdleMonitorIntervalMilliseconds == other.ClipboardIdleMonitorIntervalMilliseconds
            && ClipboardIdleThresholdSeconds == other.ClipboardIdleThresholdSeconds
            && ZoomPower == other.ZoomPower
            && AudioPlaybackVolume.Equals(other.AudioPlaybackVolume)
            && SearchListWidth.Equals(other.SearchListWidth)
            && SearchListBaseFontSize.Equals(other.SearchListBaseFontSize)
            && ContentBaseFontSize.Equals(other.ContentBaseFontSize)
            && string.Equals(CustomFontFamilyName, other.CustomFontFamilyName, StringComparison.Ordinal)
            && IsExampleItalicEnabled == other.IsExampleItalicEnabled
            && string.Equals(ExampleFontFamilyName, other.ExampleFontFamilyName, StringComparison.Ordinal)
            && ThemeMode == other.ThemeMode
            && WebSearchAssetBoxMode == other.WebSearchAssetBoxMode
            && WebSearchSites.SequenceEqual(other.WebSearchSites);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as AppConfiguration);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hashCode = new();
        hashCode.Add(DataDirectory);
        hashCode.Add(IndexVersion);
        hashCode.Add(PronunciationPlayback);
        hashCode.Add(IsAutoPronunciationPlaybackEnabled);
        hashCode.Add(IsClipboardMonitoringEnabled);
        hashCode.Add(IsFullIndexClipboardSearchEnabled);
        hashCode.Add(ClipboardActiveMonitorIntervalMilliseconds);
        hashCode.Add(ClipboardIdleMonitorIntervalMilliseconds);
        hashCode.Add(ClipboardIdleThresholdSeconds);
        hashCode.Add(ZoomPower);
        hashCode.Add(AudioPlaybackVolume);
        hashCode.Add(SearchListWidth);
        hashCode.Add(SearchListBaseFontSize);
        hashCode.Add(ContentBaseFontSize);
        hashCode.Add(CustomFontFamilyName);
        hashCode.Add(IsExampleItalicEnabled);
        hashCode.Add(ExampleFontFamilyName);
        hashCode.Add(ThemeMode);
        hashCode.Add(WebSearchAssetBoxMode);
        foreach (WebSearchSite site in WebSearchSites)
        {
            hashCode.Add(site);
        }

        return hashCode.ToHashCode();
    }

    internal AppConfiguration CreateSnapshot()
    {
        AppConfiguration snapshot = new(_path);
        snapshot.CopyPersistedValuesFrom(this);
        return snapshot;
    }

    internal void CopyPersistedValuesFrom(AppConfiguration source)
    {
        DataDirectory = source.DataDirectory;
        IndexVersion = source.IndexVersion;
        CopyUserSettingsFrom(source);
    }

    internal void CopyUserSettingsFrom(AppConfiguration source)
    {
        PronunciationPlayback = source.PronunciationPlayback;
        IsAutoPronunciationPlaybackEnabled = source.IsAutoPronunciationPlaybackEnabled;
        IsClipboardMonitoringEnabled = source.IsClipboardMonitoringEnabled;
        IsFullIndexClipboardSearchEnabled = source.IsFullIndexClipboardSearchEnabled;
        ClipboardActiveMonitorIntervalMilliseconds = source.ClipboardActiveMonitorIntervalMilliseconds;
        ClipboardIdleMonitorIntervalMilliseconds = source.ClipboardIdleMonitorIntervalMilliseconds;
        ClipboardIdleThresholdSeconds = source.ClipboardIdleThresholdSeconds;
        ZoomPower = source.ZoomPower;
        AudioPlaybackVolume = source.AudioPlaybackVolume;
        SearchListWidth = source.SearchListWidth;
        SearchListBaseFontSize = source.SearchListBaseFontSize;
        ContentBaseFontSize = source.ContentBaseFontSize;
        CustomFontFamilyName = source.CustomFontFamilyName;
        IsExampleItalicEnabled = source.IsExampleItalicEnabled;
        ExampleFontFamilyName = source.ExampleFontFamilyName;
        ThemeMode = source.ThemeMode;
        WebSearchSites = new ObservableCollection<WebSearchSite>(source.WebSearchSites.Select(site => site.Clone()));
        WebSearchAssetBoxMode = source.WebSearchAssetBoxMode;
    }

    public static bool IsUserConfigProperty(string? propertyName) =>
        propertyName is nameof(PronunciationPlayback)
            or nameof(IsAutoPronunciationPlaybackEnabled)
            or nameof(IsClipboardMonitoringEnabled)
            or nameof(IsFullIndexClipboardSearchEnabled)
            or nameof(ClipboardActiveMonitorIntervalMilliseconds)
            or nameof(ClipboardIdleMonitorIntervalMilliseconds)
            or nameof(ClipboardIdleThresholdSeconds)
            or nameof(ZoomPower)
            or nameof(AudioPlaybackVolume)
            or nameof(SearchListWidth)
            or nameof(SearchListBaseFontSize)
            or nameof(ContentBaseFontSize)
            or nameof(CustomFontFamilyName)
            or nameof(IsExampleItalicEnabled)
            or nameof(ExampleFontFamilyName)
            or nameof(ThemeMode)
            or nameof(WebSearchSites)
            or nameof(WebSearchAssetBoxMode);

    private static ObservableCollection<WebSearchSite> NormalizeWebSearchSites(ObservableCollection<WebSearchSite>? sites)
    {
        return sites is null ? new ObservableCollection<WebSearchSite>() : sites;
    }

    private void AttachWebSearchSites(ObservableCollection<WebSearchSite> sites)
    {
        sites.CollectionChanged += OnWebSearchSitesCollectionChanged;
        foreach (WebSearchSite site in sites)
        {
            site.PropertyChanged += OnWebSearchSitePropertyChanged;
        }
    }

    private void DetachWebSearchSites(ObservableCollection<WebSearchSite>? sites)
    {
        if (sites is null)
        {
            return;
        }

        sites.CollectionChanged -= OnWebSearchSitesCollectionChanged;
        foreach (WebSearchSite site in sites)
        {
            site.PropertyChanged -= OnWebSearchSitePropertyChanged;
        }
    }

    private void OnWebSearchSitesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (WebSearchSite site in e.OldItems.OfType<WebSearchSite>())
            {
                site.PropertyChanged -= OnWebSearchSitePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (WebSearchSite site in e.NewItems.OfType<WebSearchSite>())
            {
                site.PropertyChanged += OnWebSearchSitePropertyChanged;
            }
        }

        OnPropertyChanged(nameof(WebSearchSites));
    }

    private void OnWebSearchSitePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(WebSearchSites));
    }

    private static double ClampFinite(double value, double minimum, double maximum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return minimum;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private static string NormalizeCustomFontFamilyName(string? fontFamilyName) =>
        string.IsNullOrWhiteSpace(fontFamilyName) ? string.Empty : fontFamilyName.Trim();

    private static FontFamily GetEffectiveFontFamily(string? fontFamilyName)
    {
        return GetEffectiveFontFamily(fontFamilyName, GetGlobalFontFamily());
    }

    private static FontFamily GetEffectiveFontFamily(string? fontFamilyName, FontFamily fallbackFontFamily)
    {
        string normalized = NormalizeCustomFontFamilyName(fontFamilyName);
        if (normalized.Length > 0)
        {
            FontFamily customFontFamily = new(normalized);
            if (IsFontFamilyAvailable(customFontFamily))
            {
                return customFontFamily;
            }
        }

        return fallbackFontFamily;
    }

    private static FontFamily GetGlobalFontFamily()
    {
        if (Application.Current?.TryGetResource("GlobalFont", null, out object? resource) == true
            && resource is FontFamily fontFamily)
        {
            return fontFamily;
        }

        return FontFamily.Default;
    }

    private static bool IsFontFamilyAvailable(FontFamily fontFamily)
    {
        try
        {
            return FontManager.Current.SystemFonts.Contains(fontFamily);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Pronunciation variants available for entry playback.
/// </summary>
public enum PronunciationPlayback
{
    /// <summary>Plays the British headword pronunciation.</summary>
    British,

    /// <summary>Plays the American headword pronunciation.</summary>
    American,
}

/// <summary>
/// Application theme modes available from the Options menu.
/// </summary>
public enum ThemeMode
{
    /// <summary>Matches the operating system theme setting.</summary>
    Automatic,

    /// <summary>Uses the light application theme.</summary>
    Light,

    /// <summary>Uses the dark application theme.</summary>
    Dark,
}

/// <summary>
/// Controls when entry AssetBoxes include web search links.
/// </summary>
public enum WebSearchAssetBoxMode
{
    /// <summary>Shows AssetBox web search links only for noun entries.</summary>
    NounEntriesOnly,

    /// <summary>Shows AssetBox web search links for every entry with a headword.</summary>
    AllEntries,
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppConfiguration))]
internal sealed partial class AppConfigurationJsonContext : JsonSerializerContext
{
}
