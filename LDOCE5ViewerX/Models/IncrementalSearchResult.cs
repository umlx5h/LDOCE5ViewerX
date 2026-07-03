namespace LDOCE5ViewerX.Models;

/// <summary>
/// Represents one row read from the incremental search index.
/// </summary>
/// <param name="Label">Mini-HTML label used by the result list.</param>
/// <param name="Path">Dictionary content path, such as <c>/fs/example</c>.</param>
/// <param name="SortKey">Normalized lookup key stored in the index.</param>
/// <param name="Priority">Priority value used to order equal keys.</param>
/// <param name="TypeCode">Search item type code, such as <c>hm</c> or <c>pl</c>.</param>
public sealed record IncrementalSearchResult(
    string Label,
    string Path,
    string SortKey,
    byte Priority,
    string TypeCode);
