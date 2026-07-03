namespace LDOCE5ViewerX.Models;

/// <summary>
/// Represents one stored result returned from a LeanCorpus full-text search.
/// </summary>
/// <param name="Label">Display label.</param>
/// <param name="Path">Dictionary path.</param>
/// <param name="SortKey">Normalized sort key.</param>
/// <param name="Priority">Result priority.</param>
/// <param name="TypeCode">Search item type code.</param>
/// <param name="Snippet">Optional matching text for definition/example search.</param>
public sealed record FullTextSearchResult(
    string Label,
    string Path,
    string SortKey,
    byte Priority,
    string TypeCode,
    string? Snippet);
