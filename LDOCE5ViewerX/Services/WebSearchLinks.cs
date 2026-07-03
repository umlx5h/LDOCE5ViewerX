using System;
using System.Collections.Generic;
using System.Linq;

namespace LDOCE5ViewerX.Services;

internal readonly record struct WebSearchLink(string Title, string Url);

internal static class WebSearchLinks
{
    public static IReadOnlyList<WebSearchLink> Create(string query, IEnumerable<WebSearchSite>? sites)
    {
        string encodedQuery = EncodeQueryValue(query);
        return (sites ?? CreateDefaultSites())
            .Where(site => site.IsEnabled
                && !string.IsNullOrWhiteSpace(site.Title)
                && !string.IsNullOrWhiteSpace(site.UrlTemplate))
            .Select(site => new WebSearchLink(site.Title, ExpandTemplate(site.UrlTemplate, encodedQuery)))
            .Where(link => !string.IsNullOrWhiteSpace(link.Url))
            .ToArray();
    }

    public static IReadOnlyList<WebSearchSite> CreateDefaultSites()
    {
        return
        [
            new WebSearchSite("Wikipedia", "https://en.wikipedia.org/w/index.php?search={query}"),
            new WebSearchSite("Google Images", "https://www.google.com/images?hl=en&q={query}"),
        ];
    }

    private static string ExpandTemplate(string template, string encodedQuery)
    {
        if (template.Contains("{query}", StringComparison.Ordinal))
        {
            return template.Replace("{query}", encodedQuery, StringComparison.Ordinal);
        }

        return template + encodedQuery;
    }

    private static string EncodeQueryValue(string value)
    {
        return Uri.EscapeDataString(value);
    }
}
