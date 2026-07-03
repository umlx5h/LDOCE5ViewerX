using BenchmarkDotNet.Attributes;

using LDOCE5ViewerX.Services;

using Microsoft.VSDiagnostics;

namespace LDOCE5ViewerXBenchmarks.Benchmarks;

/// <summary>
/// Benchmarks <see cref="WildcardPattern"/> token classification and matching.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[CPUUsageDiagnoser]
public class WildcardPatternBenchmarks
{
    /// <summary>
    /// Gets inputs for wildcard syntax detection.
    /// </summary>
    /// <returns>The benchmark inputs.</returns>
    public static IEnumerable<string> ContainsCases()
    {
        return
        [
            "word*",
            "mother-in-law",
            "averylongquerywithwildcardattheend*",
        ];
    }

    /// <summary>
    /// Gets inputs for wildcard-only token detection.
    /// </summary>
    /// <returns>The benchmark inputs.</returns>
    public static IEnumerable<string> IsWildcardOnlyCases()
    {
        return
        [
            "*",
            "??",
            "word*",
            "h?llo",
            "hello",
        ];
    }

    /// <summary>
    /// Gets inputs for trailing-star prefix detection.
    /// </summary>
    /// <returns>The benchmark inputs.</returns>
    public static IEnumerable<string> IsTrailingStarPrefixTokenCases()
    {
        return
        [
            "word*",
            "*rest",
            "averylongprefixquery*",
        ];
    }

    /// <summary>
    /// Gets inputs for wildcard matching.
    /// </summary>
    /// <returns>The benchmark inputs.</returns>
    public static IEnumerable<WildcardMatchCase> MatchesCases()
    {
        return
        [
            new("Exact", "hello", "hello"),
            new("Trailing star", "wordplay", "word*"),
            new("Leading star", "forest", "*rest"),
            new("Question mark", "hello", "h?llo"),
            new("Inner star", "molecule", "mo*le"),
            new("Multi star", "unbelievable", "u*b*e"),
            new("No match", "world", "w?rd"),
            new("Backtracking miss", "aaaaaaaaaaaaaaaaab", "a*a*a*a*a*c"),
        ];
    }

    /// <summary>
    /// Measures wildcard syntax detection.
    /// </summary>
    /// <param name="text">The token to inspect.</param>
    /// <returns>Whether the text contains wildcard syntax.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(ContainsCases))]
    public bool Contains(string text)
    {
        return WildcardPattern.Contains(text);
    }

    /// <summary>
    /// Measures wildcard-only token detection.
    /// </summary>
    /// <param name="text">The token to inspect.</param>
    /// <returns>Whether the token contains only wildcard syntax.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(IsWildcardOnlyCases))]
    public bool IsWildcardOnly(string text)
    {
        return WildcardPattern.IsWildcardOnly(text);
    }

    /// <summary>
    /// Measures trailing-star prefix token detection.
    /// </summary>
    /// <param name="text">The token to inspect.</param>
    /// <returns>Whether the token is a simple trailing-star prefix query.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(IsTrailingStarPrefixTokenCases))]
    public bool IsTrailingStarPrefixToken(string text)
    {
        return WildcardPattern.IsTrailingStarPrefixToken(text);
    }

    /// <summary>
    /// Measures wildcard pattern matching.
    /// </summary>
    /// <param name="matchCase">The value/pattern pair to match.</param>
    /// <returns>Whether the value matches the pattern.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(MatchesCases))]
    public bool Matches(WildcardMatchCase matchCase)
    {
        return WildcardPattern.Matches(matchCase.Value, matchCase.Pattern);
    }
}

/// <summary>
/// Describes one wildcard match benchmark input.
/// </summary>
/// <param name="Name">The display name.</param>
/// <param name="Value">The candidate string.</param>
/// <param name="Pattern">The wildcard pattern.</param>
public sealed record WildcardMatchCase(string Name, string Value, string Pattern)
{
    /// <summary>
    /// Returns the benchmark display name.
    /// </summary>
    /// <returns>The case name.</returns>
    public override string ToString()
    {
        return Name;
    }
}
