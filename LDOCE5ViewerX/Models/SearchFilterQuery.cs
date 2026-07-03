using System;
using System.Collections.Generic;
using System.Linq;

namespace LDOCE5ViewerX.Models;

/// <summary>
/// Represents grouped advanced-search filters for full-text queries.
/// </summary>
/// <param name="Groups">Filter groups. Codes within a group are OR-ed; groups are AND-ed.</param>
public sealed record SearchFilterQuery(IReadOnlyList<IReadOnlyCollection<string>> Groups)
{
    /// <summary>
    /// Empty advanced-search filter query.
    /// </summary>
    public static SearchFilterQuery Empty { get; } = new([]);

    /// <summary>
    /// Gets whether the query contains any selected filter codes.
    /// </summary>
    public bool HasFilters => !ReferenceEquals(this, Empty) && Groups.Any(group => group.Count > 0);

    /// <summary>
    /// Creates a filter query while dropping empty and duplicate codes.
    /// </summary>
    /// <param name="groups">Selected filter groups.</param>
    /// <returns>Normalized grouped filter query.</returns>
    public static SearchFilterQuery Create(IEnumerable<IEnumerable<string>> groups)
    {
        List<IReadOnlyCollection<string>> normalized = [];
        foreach (IEnumerable<string> group in groups)
        {
            string[] codes = group
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (codes.Length > 0)
            {
                normalized.Add(codes);
            }
        }

        return normalized.Count == 0 ? Empty : new SearchFilterQuery(normalized);
    }
}
