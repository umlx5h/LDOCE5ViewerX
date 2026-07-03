using System;
using System.Text.RegularExpressions;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Provides wildcard query helpers for full-text and lookup-key searches.
/// </summary>
public static class WildcardPattern
{
    /// <summary>
    /// Returns whether the text contains full-text wildcard syntax.
    /// </summary>
    /// <param name="text">Text to inspect.</param>
    /// <returns><c>true</c> when the text contains <c>*</c> or <c>?</c>.</returns>
    public static bool Contains(ReadOnlySpan<char> text)
    {
        return text.ContainsAny('*', '?');
    }

    /// <summary>
    /// Returns whether the token contains only wildcard syntax and no searchable text.
    /// </summary>
    /// <param name="token">Normalized full-text query token.</param>
    /// <returns><c>true</c> when every token character is <c>*</c> or <c>?</c>.</returns>
    public static bool IsWildcardOnly(string token)
    {
        return !token.ContainsAnyExcept('*', '?');
    }

    /// <summary>
    /// Returns whether the token is a simple trailing-star prefix query such as <c>wor*</c>.
    /// </summary>
    /// <param name="token">Normalized full-text query token.</param>
    /// <returns><c>true</c> when the token has exactly one wildcard and it is a trailing <c>*</c>.</returns>
    public static bool IsTrailingStarPrefixToken(ReadOnlySpan<char> token)
    {
        if (token.Length == 0 || token[^1] != '*')
        {
            return false;
        }

        return !token[..^1].ContainsAny('*', '?');
    }

    /// <summary>
    /// Matches a normalized value against a normalized wildcard pattern.
    /// </summary>
    /// <param name="value">Normalized value to test.</param>
    /// <param name="pattern">Normalized wildcard pattern containing literal characters, <c>*</c>, or <c>?</c>.</param>
    /// <returns><c>true</c> when the value matches the pattern.</returns>
    public static bool Matches(string value, string pattern)
    {
        int valueIndex = 0;
        int patternIndex = 0;
        int starIndex = -1;
        int retryValueIndex = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?' || pattern[patternIndex] == value[valueIndex]))
            {
                valueIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex;
                retryValueIndex = valueIndex;
                patternIndex++;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                retryValueIndex++;
                valueIndex = retryValueIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
