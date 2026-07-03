using System;
using System.Buffers;
using System.Collections.Generic;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Converts result-list labels from index markup into display text.
/// </summary>
public static class ResultLabelFormatter
{
    /// <summary>
    /// Strips mini-HTML tags for simple Avalonia list rendering.
    /// </summary>
    /// <param name="label">Mini-HTML label from a search index.</param>
    /// <returns>Plain result text suitable for display.</returns>
    public static string ToDisplayText(string label)
    {
        int firstTagStart = label.IndexOf('<', StringComparison.Ordinal);
        if (firstTagStart < 0)
        {
            return label;
        }

        ReadOnlySpan<char> source = label.AsSpan();
        char[]? rented = null;
        Span<char> buffer = source.Length <= 256
            ? stackalloc char[source.Length]
            : rented = ArrayPool<char>.Shared.Rent(source.Length);

        try
        {
            int written = 0;
            int offset = 0;

            while (offset < source.Length)
            {
                int tagStart = source[offset..].IndexOf('<');
                if (tagStart < 0)
                {
                    source[offset..].CopyTo(buffer[written..]);
                    written += source.Length - offset;
                    break;
                }

                tagStart += offset;
                if (tagStart > offset)
                {
                    source[offset..tagStart].CopyTo(buffer[written..]);
                    written += tagStart - offset;
                }

                int tagEnd = source[(tagStart + 1)..].IndexOf('>');
                if (tagEnd < 0)
                {
                    source[tagStart..].CopyTo(buffer[written..]);
                    written += source.Length - tagStart;
                    break;
                }

                offset = tagStart + tagEnd + 2;
            }

            return written == source.Length
                ? label
                : new string(buffer[..written]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Parses mini-HTML result labels into styled text runs for Avalonia rendering.
    /// </summary>
    /// <param name="label">Mini-HTML label from a search index.</param>
    /// <returns>Styled result label runs.</returns>
    public static IReadOnlyList<SearchResultLabelRun> ToLabelRuns(string label)
    {
        List<SearchResultLabelRun> runs = [];
        List<LabelTag> tags = [];
        int offset = 0;

        while (offset < label.Length)
        {
            int tagStart = label.IndexOf('<', offset);
            if (tagStart < 0)
            {
                AddTextRun(runs, label.AsSpan(offset), tags);
                break;
            }

            if (tagStart > offset)
            {
                AddTextRun(runs, label.AsSpan(offset, tagStart - offset), tags);
            }

            int tagEnd = label.IndexOf('>', tagStart + 1);
            if (tagEnd < 0)
            {
                AddTextRun(runs, label.AsSpan(tagStart), tags);
                break;
            }

            ApplyTag(tags, label.AsSpan(tagStart + 1, tagEnd - tagStart - 1));
            offset = tagEnd + 1;
        }

        return runs.Count > 0
            ? runs
            : [new SearchResultLabelRun(ToDisplayText(label), SearchResultLabelStyle.Normal)];
    }

    private static void AddTextRun(List<SearchResultLabelRun> runs, ReadOnlySpan<char> text, List<LabelTag> tags)
    {
        if (text.Length == 0)
        {
            return;
        }

        SearchResultLabelStyle style = GetStyle(tags);
        string value = text.ToString();
        if (runs.Count > 0 && runs[^1] is SearchResultLabelRun previous && previous.Style == style)
        {
            runs[^1] = previous with { Text = previous.Text + value };
            return;
        }

        runs.Add(new SearchResultLabelRun(value, style));
    }

    private static void ApplyTag(List<LabelTag> tags, ReadOnlySpan<char> tagMarkup)
    {
        bool isClosing = false;
        int offset = 0;
        if (tagMarkup.StartsWith('/'))
        {
            isClosing = true;
            offset = 1;
        }

        while (offset < tagMarkup.Length && char.IsWhiteSpace(tagMarkup[offset]))
        {
            offset++;
        }

        int nameStart = offset;
        while (offset < tagMarkup.Length && char.IsAsciiLetterOrDigit(tagMarkup[offset]))
        {
            offset++;
        }

        if (offset == nameStart)
        {
            return;
        }

        LabelTag tag = GetTag(tagMarkup[nameStart..offset]);
        if (tag == LabelTag.None)
        {
            return;
        }

        if (isClosing)
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

    private static LabelTag GetTag(ReadOnlySpan<char> name)
    {
        if (name.Equals("h", StringComparison.Ordinal))
        {
            return LabelTag.Headword;
        }

        if (name.Equals("n", StringComparison.Ordinal))
        {
            return LabelTag.HeadwordStrong;
        }

        if (name.Equals("f", StringComparison.Ordinal))
        {
            return LabelTag.FrequentHeadword;
        }

        if (name.Equals("pv", StringComparison.Ordinal))
        {
            return LabelTag.PhrasalVerb;
        }

        if (name.Equals("p", StringComparison.Ordinal))
        {
            return LabelTag.PartOfSpeech;
        }

        if (name.Equals("s", StringComparison.Ordinal))
        {
            return LabelTag.Superscript;
        }

        if (name.Equals("a", StringComparison.Ordinal))
        {
            return LabelTag.Activator;
        }

        if (name.Equals("c", StringComparison.Ordinal))
        {
            return LabelTag.CollocationOrConcept;
        }

        if (name.Equals("e", StringComparison.Ordinal))
        {
            return LabelTag.ActivatorExponent;
        }

        if (name.Equals("l", StringComparison.Ordinal))
        {
            return LabelTag.Phrase;
        }

        if (name.Equals("o", StringComparison.Ordinal))
        {
            return LabelTag.Object;
        }

        return LabelTag.None;
    }

    private static SearchResultLabelStyle GetStyle(List<LabelTag> tags)
    {
        if (HasTag(tags, LabelTag.Superscript))
        {
            return SearchResultLabelStyle.Superscript;
        }

        if (HasTag(tags, LabelTag.PartOfSpeech))
        {
            return SearchResultLabelStyle.PartOfSpeech;
        }

        if (HasTag(tags, LabelTag.Activator) && HasTag(tags, LabelTag.ActivatorExponent))
        {
            return SearchResultLabelStyle.ActivatorExponent;
        }

        if (HasTag(tags, LabelTag.Activator) && HasTag(tags, LabelTag.CollocationOrConcept))
        {
            return SearchResultLabelStyle.ActivatorConcept;
        }

        if (HasTag(tags, LabelTag.Phrase) && HasTag(tags, LabelTag.Object))
        {
            return SearchResultLabelStyle.PhraseText;
        }

        if (HasTag(tags, LabelTag.CollocationOrConcept) && HasTag(tags, LabelTag.Object))
        {
            return SearchResultLabelStyle.CollocationText;
        }

        if (HasTag(tags, LabelTag.PhrasalVerb))
        {
            return SearchResultLabelStyle.PhrasalVerb;
        }

        if (HasTag(tags, LabelTag.HeadwordStrong) || HasTag(tags, LabelTag.FrequentHeadword))
        {
            return SearchResultLabelStyle.HeadwordStrong;
        }

        if (HasTag(tags, LabelTag.Headword))
        {
            return SearchResultLabelStyle.Headword;
        }

        return SearchResultLabelStyle.Normal;
    }

    private static bool HasTag(List<LabelTag> tags, LabelTag tag)
    {
        for (int i = tags.Count - 1; i >= 0; i--)
        {
            if (tags[i] == tag)
            {
                return true;
            }
        }

        return false;
    }

    private enum LabelTag
    {
        None,
        Headword,
        HeadwordStrong,
        FrequentHeadword,
        PhrasalVerb,
        PartOfSpeech,
        Superscript,
        Activator,
        CollocationOrConcept,
        ActivatorExponent,
        Phrase,
        Object,
    }
}
