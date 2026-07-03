using System;

using CommunityToolkit.Mvvm.ComponentModel;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// User-configurable web search engine entry.
/// </summary>
public sealed class WebSearchSite : ObservableObject, IEquatable<WebSearchSite>
{
    /// <summary>
    /// Creates an empty web search engine entry.
    /// </summary>
    public WebSearchSite()
    {
    }

    /// <summary>
    /// Creates a web search engine entry.
    /// </summary>
    /// <param name="title">Menu label for the site.</param>
    /// <param name="urlTemplate">URL template containing the encoded query placeholder.</param>
    /// <param name="isEnabled">Whether the site appears in web-search menus.</param>
    public WebSearchSite(string title, string urlTemplate, bool isEnabled = true)
    {
        Title = title;
        UrlTemplate = urlTemplate;
        IsEnabled = isEnabled;
    }

    /// <summary>
    /// Menu label for the site.
    /// </summary>
    public string Title
    {
        get;
        set => SetProperty(ref field, value?.Trim() ?? string.Empty);
    } = string.Empty;

    /// <summary>
    /// URL template. Use <c>{query}</c> where the encoded query should be inserted.
    /// </summary>
    public string UrlTemplate
    {
        get;
        set => SetProperty(ref field, value?.Trim() ?? string.Empty);
    } = string.Empty;

    /// <summary>
    /// Whether the site appears in web-search menus.
    /// </summary>
    public bool IsEnabled
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    /// <summary>
    /// Creates a detached copy of the site entry.
    /// </summary>
    /// <returns>A copy with the same persisted values.</returns>
    public WebSearchSite Clone() => new(Title, UrlTemplate, IsEnabled);

    /// <inheritdoc/>
    public bool Equals(WebSearchSite? other)
    {
        return other is not null
            && string.Equals(Title, other.Title, StringComparison.Ordinal)
            && string.Equals(UrlTemplate, other.UrlTemplate, StringComparison.Ordinal)
            && IsEnabled == other.IsEnabled;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as WebSearchSite);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Title, UrlTemplate, IsEnabled);
}
