using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.Models;

/// <summary>
/// Represents one searchable item extracted from LDOCE XML during indexing.
/// </summary>
/// <param name="TypeCode">Search item type code such as <c>hm</c>, <c>p</c>, <c>d</c>, or <c>e</c>.</param>
/// <param name="Label">Display label stored in search indexes.</param>
/// <param name="Path">Dictionary path loaded when the result is selected.</param>
/// <param name="Content">Text searched by incremental and full-text indexes.</param>
/// <param name="SortKey">Text used to sort search results.</param>
/// <param name="AdvancedSearchFilter">Advanced-search filter token string.</param>
/// <param name="Priority">Priority used to order equal sort keys.</param>
public sealed record SearchIndexItem(
    string TypeCode,
    string Label,
    string Path,
    string Content,
    string SortKey,
    string AdvancedSearchFilter,
    byte Priority)
{
    // In the old app, spaces are collapsed when displaying HTML.
    // So perform pre-processing to replicate this behavior.
    // This means that the binary contents of `incremental.db` will no longer match.
    public string Label { get; init; } = TextNormalizer.CollapseWhitespace(Label);
}
