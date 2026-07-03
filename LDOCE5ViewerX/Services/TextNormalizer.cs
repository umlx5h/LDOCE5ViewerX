using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Implements text normalization rules shared by search indexes.
/// </summary>
public static partial class TextNormalizer
{
    /// <summary>
    /// Normalizes lookup text the same way Python <c>normalize_index_key</c> did.
    /// </summary>
    /// <param name="key">Raw lookup text.</param>
    /// <returns>Lowercase letters and digits after compatibility decomposition.</returns>
    public static string NormalizeIndexKey(string key)
    {
        return NormalizeIndexKey(key, preserveWildcards: false);
    }

    /// <summary>
    /// Normalizes lookup-key wildcard text while preserving wildcard syntax.
    /// </summary>
    /// <param name="pattern">Raw lookup-key wildcard pattern.</param>
    /// <returns>Lowercase letters, digits, and wildcard characters after compatibility decomposition.</returns>
    public static string NormalizeIndexKeyWildcardPattern(string pattern)
    {
        return NormalizeIndexKey(pattern, preserveWildcards: true);
    }

    private static string NormalizeIndexKey(string key, bool preserveWildcards)
    {
        string normalized = key.Trim()
            .ToLowerInvariant()
            .Replace('\u00A9', 'c')
            .Normalize(NormalizationForm.FormKD);

        StringBuilder builder = new(normalized.Length);
        foreach (char c in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.LowercaseLetter or UnicodeCategory.DecimalDigitNumber
                || (preserveWildcards && c is '*' or '?'))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Normalizes one full-text token with optional wildcard preservation.
    /// </summary>
    private static string NormalizeToken(string token, bool preserveWildcards)
    {
        string normalized = token.Trim()
            .ToLowerInvariant()
            .Replace('\u00A9', 'c')
            .Normalize(NormalizationForm.FormKD);

        StringBuilder builder = new(normalized.Length);
        foreach (char c in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c) || (preserveWildcards && c is '*' or '?'))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Tokenizes full-text content with punctuation treated as word boundaries, matching Whoosh search behavior.
    /// </summary>
    /// <param name="text">Raw content or query text.</param>
    /// <returns>Normalized full-text tokens.</returns>
    public static IReadOnlyList<string> TokenizeFullText(string text)
    {
        List<string> tokens = [];
        foreach (Match match in FullTextTokenRegex().Matches(text))
        {
            string token = NormalizeToken(match.Value, preserveWildcards: false);
            if (token.Length > 0 && token is not "a" and not "an")
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Tokenizes full-text query text while preserving wildcard characters.
    /// </summary>
    /// <param name="text">Raw query text.</param>
    /// <returns>Normalized query tokens.</returns>
    public static IReadOnlyList<string> TokenizeFullTextQuery(string text)
    {
        List<string> tokens = [];
        foreach (Match match in FullTextQueryTokenRegex().Matches(text))
        {
            string token = NormalizeToken(match.Value, preserveWildcards: true);
            if (token.Length > 0 && token is not "a" and not "an")
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Converts free text into normalized whitespace-separated full-text tokens.
    /// </summary>
    /// <param name="text">Raw text.</param>
    /// <returns>Normalized token string.</returns>
    public static string NormalizeFullText(string text)
    {
        return string.Join(' ', TokenizeFullText(text));
    }

    public static string CollapseWhitespace(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"[\p{L}\p{Nd}*?]+")]
    private static partial Regex FullTextQueryTokenRegex();

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex FullTextTokenRegex();
}
