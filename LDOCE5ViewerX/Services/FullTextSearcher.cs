using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using LDOCE5ViewerX.Models;

using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Suggestions;
using Rowles.LeanCorpus.Store;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Searches a LeanCorpus full-text index created by <see cref="FullTextIndexWriter"/>.
/// </summary>
public sealed class FullTextSearcher : IDisposable
{
    private const int WildcardCandidateMultiplier = 10;
    private const int MaxWildcardCandidates = 30000;

    private readonly string _indexDirectory;
    private readonly string? _variationsPath;
    private readonly Lazy<FullTextSearchResources> _resources;
    private bool _disposed;

    private static readonly ReadOnlySet<string> HpDocNames = ["label", "path", "priority", "sortkey", "type"];
    private static readonly ReadOnlySet<string> DeDocNames = ["label", "path", "priority", "sortkey", "type", "contentraw"];

    /// <summary>
    /// Creates a searcher that opens the LeanCorpus index on first use.
    /// </summary>
    /// <param name="indexDirectory">LeanCorpus index directory.</param>
    /// <param name="variationsPath">Optional variations CDB path.</param>
    public FullTextSearcher(string indexDirectory, string? variationsPath = null)
    {
        _indexDirectory = indexDirectory;
        _variationsPath = variationsPath;
        _resources = new Lazy<FullTextSearchResources>(
            CreateResources,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Runs a full-text search.
    /// </summary>
    /// <param name="phrase">Text query. May be empty when filters are supplied.</param>
    /// <param name="filters">Advanced-search filter query.</param>
    /// <param name="itemTypes">Optional item type restriction.</param>
    /// <param name="limit">Maximum result count.</param>
    /// <param name="filterWildcardBySortKey">Whether wildcard queries must also match the stored sort key.</param>
    /// <param name="getRawContent">Whether to get raw content.</param>
    /// <returns>Stored matching results sorted for the result list.</returns>
    public IReadOnlyList<FullTextSearchResult> Search(
        string? phrase,
        SearchFilterQuery? filters,
        IReadOnlyCollection<string> itemTypes,
        int limit,
        bool filterWildcardBySortKey = false,
        bool getRawContent = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        SearchFilterQuery filterQuery = filters ?? SearchFilterQuery.Empty;
        IReadOnlyList<string> phraseTokens = string.IsNullOrWhiteSpace(phrase)
            ? []
            : TextNormalizer.TokenizeFullTextQuery(phrase);
        if (phraseTokens.Any(WildcardPattern.IsWildcardOnly)
            || (phraseTokens.Count == 0 && !filterQuery.HasFilters))
        {
            return [];
        }

        FullTextSearchResources resources = _resources.Value;
        BooleanQuery.Builder queryBuilder = new();
        AddContentTerms(queryBuilder, phraseTokens, resources.Variations);
        AddFilterTerms(queryBuilder, filterQuery);
        AddItemTypes(queryBuilder, itemTypes);

        BooleanQuery query = queryBuilder.Build();
        if (query.Clauses.Count == 0)
        {
            return [];
        }

        bool applySortKeyWildcardFilter = filterWildcardBySortKey
            && !string.IsNullOrWhiteSpace(phrase)
            && WildcardPattern.Contains(phrase);
        string sortKeyWildcardPattern = applySortKeyWildcardFilter
            ? TextNormalizer.NormalizeIndexKeyWildcardPattern(phrase!)
            : string.Empty;
        int candidateLimit = GetCandidateLimit(limit, applySortKeyWildcardFilter);
        TopDocs hits = resources.Searcher.Search(query, candidateLimit);
        List<FullTextSearchResult> results = new(Math.Min(limit, hits.ScoreDocs.Length));

        foreach (ScoreDoc scoreDoc in hits.ScoreDocs)
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> fields = resources.Searcher.GetStoredFields(scoreDoc.DocId, getRawContent ? DeDocNames : HpDocNames);
            string sortKey = GetStoredField(fields, "sortkey");
            if (applySortKeyWildcardFilter && !WildcardPattern.Matches(sortKey, sortKeyWildcardPattern))
            {
                continue;
            }

            results.Add(new FullTextSearchResult(
                GetStoredField(fields, "label"),
                GetStoredField(fields, "path"),
                sortKey,
                byte.Parse(GetStoredField(fields, "priority"), CultureInfo.InvariantCulture),
                GetStoredField(fields, "type"),
                getRawContent ? GetStoredField(fields, "contentraw") : null));

            if (!applySortKeyWildcardFilter && results.Count >= limit)
            {
                break;
            }
        }

        IOrderedEnumerable<FullTextSearchResult> sorted = results
            .OrderBy(result => result.SortKey, StringComparer.Ordinal)
            .ThenBy(result => result.Priority);

        return applySortKeyWildcardFilter
            ? sorted.Take(limit).ToArray()
            : sorted.ToArray();

        static string GetStoredField(IReadOnlyDictionary<string, IReadOnlyList<string>> fields, string name)
        {
            if (!fields.TryGetValue(name, out IReadOnlyList<string>? values) || values.Count == 0)
            {
                throw new InvalidDataException($"Stored full-text field is missing: {name}.");
            }

            return values[0];
        }
    }

    /// <summary>
    /// Suggests spelling corrections from the existing LeanCorpus content term dictionary.
    /// </summary>
    /// <param name="query">Single-word query to correct.</param>
    /// <param name="limit">Maximum suggestion count.</param>
    /// <returns>Suggested replacement terms.</returns>
    public IReadOnlyList<string> SuggestCorrections(string query, int limit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Debug.Assert(TextNormalizer.TokenizeFullTextQuery(query).Count == 1);

        FullTextSearchResources resources = _resources.Value;
        string term = TextNormalizer.TokenizeFullTextQuery(query).First();

        return DidYouMeanSuggester.Suggest(resources.Searcher, "content", term, maxEdits: 2, topN: limit)
            .Select(suggestion => suggestion.Term)
            .ToArray();
    }

    private int GetCandidateLimit(int resultLimit, bool applySortKeyWildcardFilter)
    {
        Debug.Assert(resultLimit > 0);

        if (!applySortKeyWildcardFilter)
        {
            return resultLimit;
        }

        int multipliedLimit = resultLimit * WildcardCandidateMultiplier;
        return Math.Min(MaxWildcardCandidates, multipliedLimit);
    }

    /// <summary>
    /// Adds required content terms, expanding through the variations CDB where available.
    /// </summary>
    private static void AddContentTerms(
        BooleanQuery.Builder query,
        IReadOnlyList<string> tokens,
        VariationsIndex? variations)
    {
        foreach (string token in tokens)
        {
            Query termQuery;
            if (WildcardPattern.IsTrailingStarPrefixToken(token))
            {
                termQuery = new PrefixQuery("content", token[..^1]);
            }
            else if (WildcardPattern.Contains(token))
            {
                termQuery = new WildcardQuery("content", token);
            }
            else
            {
                BooleanQuery.Builder variants = new();
                foreach (string variant in variations?.GetVariations(token) ?? [token])
                {
                    variants.Add(new TermQuery("content", variant), Occur.Should);
                }

                termQuery = variants.Build();
            }

            query.Add(termQuery, Occur.Must);
        }
    }

    /// <summary>
    /// Adds required advanced-search filter terms.
    /// </summary>
    private static void AddFilterTerms(BooleanQuery.Builder query, SearchFilterQuery filters)
    {
        if (!filters.HasFilters)
        {
            return;
        }

        foreach (IReadOnlyCollection<string> group in filters.Groups)
        {
            if (group.Count == 0)
            {
                continue;
            }

            if (group.Count == 1)
            {
                query.Add(new TermQuery("asfilter", group.Single()), Occur.Must);
                continue;
            }

            BooleanQuery.Builder groupQuery = new();
            foreach (string code in group)
            {
                groupQuery.Add(new TermQuery("asfilter", code), Occur.Should);
            }

            query.Add(groupQuery.Build(), Occur.Must);
        }
    }

    /// <summary>
    /// Adds optional item type restrictions.
    /// </summary>
    private static void AddItemTypes(BooleanQuery.Builder query, IReadOnlyCollection<string> itemTypes)
    {
        if (itemTypes.Count == 0)
        {
            return;
        }

        BooleanQuery.Builder typeQuery = new();
        foreach (string type in itemTypes)
        {
            typeQuery.Add(new TermQuery("type", type), Occur.Should);
        }

        query.Add(typeQuery.Build(), Occur.Must);
    }

    /// <summary>
    /// Closes LeanCorpus index resources.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        if (_resources.IsValueCreated)
        {
            _resources.Value.Dispose();
        }
    }

    internal static bool IsIndexUnavailableException(Exception ex)
    {
        return ex is IOException
            or InvalidDataException
            or UnauthorizedAccessException;
    }

    private FullTextSearchResources CreateResources()
    {
        return new FullTextSearchResources(_indexDirectory, _variationsPath);
    }

    private sealed class FullTextSearchResources : IDisposable
    {
        private readonly MMapDirectory _directory;

        public FullTextSearchResources(string indexDirectory, string? variationsPath)
        {
            MMapDirectory? directory = null;
            IndexSearcher? searcher = null;
            VariationsIndex? variations = null;
            try
            {
                directory = new MMapDirectory(indexDirectory);
                searcher = new IndexSearcher(directory);
                variations = variationsPath is not null ? new VariationsIndex(variationsPath) : null;

                _directory = directory;
                Searcher = searcher;
                Variations = variations;

                directory = null;
                searcher = null;
                variations = null;
            }
            finally
            {
                variations?.Dispose();
                searcher?.Dispose();
                directory?.Dispose();
            }
        }

        public IndexSearcher Searcher { get; }

        public VariationsIndex? Variations { get; }

        public void Dispose()
        {
            Variations?.Dispose();
            Searcher.Dispose();
            _directory.Dispose();
        }
    }
}
