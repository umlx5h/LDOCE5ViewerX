using System.Collections.Generic;

namespace LDOCE5ViewerX.Models;

/// <summary>
/// Describes one node in the LDOCE advanced-search filter tree.
/// </summary>
/// <param name="Label">User-visible filter label.</param>
/// <param name="Code">Optional LDOCE advanced-search filter code.</param>
/// <param name="Children">Child filter nodes.</param>
public sealed record AdvancedSearchFilterNode(
    string Label,
    string? Code,
    IReadOnlyList<AdvancedSearchFilterNode>? Children = null);
