using System.Text.RegularExpressions;

using BenchmarkDotNet.Attributes;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.Services;

using Microsoft.VSDiagnostics;

namespace LDOCE5ViewerXBenchmarks.Benchmarks;

/// <summary>
/// Benchmarks display-text parsing for legacy result-label markup.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[CPUUsageDiagnoser]
public partial class ResultLabelDisplayTextFormatterBenchmarks
{
    /// <summary>
    /// Measures the span-based display text parser used by production code.
    /// </summary>
    /// <param name="labelCase">The benchmark label to parse.</param>
    /// <returns>The parsed text length.</returns>
    [Benchmark(Baseline = true)]
    [ArgumentsSource(typeof(ResultLabelCases), nameof(ResultLabelCases.All))]
    public int SpanToDisplayText(ResultLabelCase labelCase)
    {
        return ResultLabelFormatter.ToDisplayText(labelCase.Label).Length;
    }

    /// <summary>
    /// Measures a generated-regex display text parser for comparison.
    /// </summary>
    /// <param name="labelCase">The benchmark label to parse.</param>
    /// <returns>The parsed text length.</returns>
    [Benchmark]
    [ArgumentsSource(typeof(ResultLabelCases), nameof(ResultLabelCases.All))]
    public int RegexToDisplayText(ResultLabelCase labelCase)
    {
        return TagRegex().Replace(labelCase.Label, string.Empty).Length;
    }

    /// <summary>
    /// Matches any mini-HTML tag token.
    /// </summary>
    [GeneratedRegex("</?[^>]+>")]
    private static partial Regex TagRegex();
}

/// <summary>
/// Benchmarks styled-run parsing for legacy result-label markup.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[CPUUsageDiagnoser]
public partial class ResultLabelRunFormatterBenchmarks
{
    /// <summary>
    /// Measures the span-based styled label parser used by production code.
    /// </summary>
    /// <param name="labelCase">The benchmark label to parse.</param>
    /// <returns>The number of parsed runs.</returns>
    [Benchmark(Baseline = true)]
    [ArgumentsSource(typeof(ResultLabelCases), nameof(ResultLabelCases.All))]
    public int SpanToLabelRuns(ResultLabelCase labelCase)
    {
        return ResultLabelFormatter.ToLabelRuns(labelCase.Label).Count;
    }

    /// <summary>
    /// Measures a generated-regex styled label parser for comparison.
    /// </summary>
    /// <param name="labelCase">The benchmark label to parse.</param>
    /// <returns>The number of parsed runs.</returns>
    [Benchmark]
    [ArgumentsSource(typeof(ResultLabelCases), nameof(ResultLabelCases.All))]
    public int RegexToLabelRuns(ResultLabelCase labelCase)
    {
        return RegexToLabelRunsCore(labelCase.Label).Count;
    }

    /// <summary>
    /// Matches any mini-HTML tag token.
    /// </summary>
    [GeneratedRegex("</?[^>]+>")]
    private static partial Regex TagRegex();

    private static IReadOnlyList<SearchResultLabelRun> RegexToLabelRunsCore(string label)
    {
        List<SearchResultLabelRun> runs = [];
        List<string> tags = [];
        int offset = 0;

        foreach (Match match in TagRegex().Matches(label))
        {
            if (match.Index > offset)
            {
                AddRegexTextRun(runs, label[offset..match.Index], tags);
            }

            ApplyRegexTag(tags, match.Value);
            offset = match.Index + match.Length;
        }

        if (offset < label.Length)
        {
            AddRegexTextRun(runs, label[offset..], tags);
        }

        return runs.Count > 0
            ? runs
            : [new SearchResultLabelRun(TagRegex().Replace(label, string.Empty), SearchResultLabelStyle.Normal)];
    }

    private static void AddRegexTextRun(List<SearchResultLabelRun> runs, string text, List<string> tags)
    {
        if (text.Length == 0)
        {
            return;
        }

        SearchResultLabelStyle style = GetRegexStyle(tags);
        if (runs.Count > 0 && runs[^1] is SearchResultLabelRun previous && previous.Style == style)
        {
            runs[^1] = previous with { Text = previous.Text + text };
            return;
        }

        runs.Add(new SearchResultLabelRun(text, style));
    }

    private static void ApplyRegexTag(List<string> tags, string tagMarkup)
    {
        Match match = TagNameRegex().Match(tagMarkup);
        if (!match.Success)
        {
            return;
        }

        string tag = match.Groups["name"].Value;
        if (tagMarkup.StartsWith("</", StringComparison.Ordinal))
        {
            for (int i = tags.Count - 1; i >= 0; i--)
            {
                if (tags[i] == tag)
                {
                    tags.RemoveAt(i);
                    return;
                }
            }
        }
        else
        {
            tags.Add(tag);
        }
    }

    private static SearchResultLabelStyle GetRegexStyle(List<string> tags)
    {
        if (tags.Contains("s"))
        {
            return SearchResultLabelStyle.Superscript;
        }

        if (tags.Contains("p"))
        {
            return SearchResultLabelStyle.PartOfSpeech;
        }

        if (tags.Contains("a") && tags.Contains("e"))
        {
            return SearchResultLabelStyle.ActivatorExponent;
        }

        if (tags.Contains("a") && tags.Contains("c"))
        {
            return SearchResultLabelStyle.ActivatorConcept;
        }

        if (tags.Contains("l") && tags.Contains("o"))
        {
            return SearchResultLabelStyle.PhraseText;
        }

        if (tags.Contains("c") && tags.Contains("o"))
        {
            return SearchResultLabelStyle.CollocationText;
        }

        if (tags.Contains("pv"))
        {
            return SearchResultLabelStyle.PhrasalVerb;
        }

        if (tags.Contains("n") || tags.Contains("f"))
        {
            return SearchResultLabelStyle.HeadwordStrong;
        }

        if (tags.Contains("h"))
        {
            return SearchResultLabelStyle.Headword;
        }

        return SearchResultLabelStyle.Normal;
    }

    /// <summary>
    /// Captures the tag name from a mini-HTML tag token.
    /// </summary>
    [GeneratedRegex(@"^</?\s*(?<name>[a-z]*)")]
    private static partial Regex TagNameRegex();
}

internal static class ResultLabelCases
{
    public static IEnumerable<ResultLabelCase> All()
    {
        return
        [
            new("Headword", "<h><n>find</n><s>1</s> <p>verb</p></h>"),
            new("Variant", "<h><v>found</v> \u2192 <n>find</n><s>1</s> <p>verb</p></h>"),
            new("PhrasalVerb", "<h><pv>take away</pv> <p>phrasal verb</p></h>"),
            new("Collocation", "<c><o>take a risk</o> (<f>risk</f> <p>noun</p>)</c>"),
            new("Phrase", "<l><o>as a matter of fact</o> (<n>matter</n> <p>noun</p>)</l>"),
            new("Activator", "<a><e>very angry</e> (<c>angry<s>2</s></c>)</a>"),
        ];
    }
}

/// <summary>
/// Describes one result-label benchmark input.
/// </summary>
/// <param name="Name">The display name.</param>
/// <param name="Label">The mini-HTML label.</param>
public sealed record ResultLabelCase(string Name, string Label)
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
