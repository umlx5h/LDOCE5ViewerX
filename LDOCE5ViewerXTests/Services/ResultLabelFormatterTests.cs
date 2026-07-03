using AwesomeAssertions;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

public sealed class ResultLabelFormatterTests
{
    [Fact]
    public void ToDisplayText_returns_plain_label_without_allocating_when_no_tags_exist()
    {
        string label = "plain label";

        string displayText = ResultLabelFormatter.ToDisplayText(label);

        displayText.Should().BeSameAs(label);
    }

    [Theory]
    [InlineData("<h><n>find</n><s>1</s> <p>verb</p></h>", "find1 verb")]
    [InlineData("<c><o>take a risk</o> (<f>risk</f> <p>noun</p>)</c>", "take a risk (risk noun)")]
    [InlineData("<a><e>very angry</e> (<c>angry<s>2</s></c>)</a>", "very angry (angry2)")]
    public void ToDisplayText_strips_mini_html_tags(string label, string expected)
    {
        string displayText = ResultLabelFormatter.ToDisplayText(label);

        displayText.Should().Be(expected);
    }

    [Fact]
    public void ToDisplayText_strips_tags_with_attributes_or_whitespace()
    {
        string displayText = ResultLabelFormatter.ToDisplayText("< h class=\"x\">word</ h>");

        displayText.Should().Be("word");
    }

    [Fact]
    public void ToDisplayText_preserves_unterminated_markup_as_text()
    {
        string displayText = ResultLabelFormatter.ToDisplayText("<h>word <p");

        displayText.Should().Be("word <p");
    }

    [Fact]
    public void ToDisplayText_strips_tags_from_long_labels()
    {
        string longText = new('a', 300);

        string displayText = ResultLabelFormatter.ToDisplayText($"<h>{longText}</h>");

        displayText.Should().Be(longText);
    }

    [Fact]
    public void ToLabelRuns_returns_normal_run_for_plain_text()
    {
        IReadOnlyList<SearchResultLabelRun> runs = ResultLabelFormatter.ToLabelRuns("plain label");

        runs.Should().Equal(new SearchResultLabelRun("plain label", SearchResultLabelStyle.Normal));
    }

    [Fact]
    public void ToLabelRuns_maps_headword_homonym_and_part_of_speech_styles()
    {
        IReadOnlyList<SearchResultLabelRun> runs =
            ResultLabelFormatter.ToLabelRuns("<h><n>find</n><s>1</s> <p>verb</p></h>");

        runs.Should().Equal(
            new SearchResultLabelRun("find", SearchResultLabelStyle.HeadwordStrong),
            new SearchResultLabelRun("1", SearchResultLabelStyle.Superscript),
            new SearchResultLabelRun(" ", SearchResultLabelStyle.Headword),
            new SearchResultLabelRun("verb", SearchResultLabelStyle.PartOfSpeech));
    }

    [Theory]
    [InlineData("<pv>take away</pv>", "take away", SearchResultLabelStyle.PhrasalVerb)]
    [InlineData("<c><o>take a risk</o></c>", "take a risk", SearchResultLabelStyle.CollocationText)]
    [InlineData("<l><o>as a matter of fact</o></l>", "as a matter of fact", SearchResultLabelStyle.PhraseText)]
    [InlineData("<a><e>very angry</e></a>", "very angry", SearchResultLabelStyle.ActivatorExponent)]
    [InlineData("<a><c>angry</c></a>", "angry", SearchResultLabelStyle.ActivatorConcept)]
    [InlineData("<f>risk</f>", "risk", SearchResultLabelStyle.HeadwordStrong)]
    public void ToLabelRuns_maps_special_label_styles(string label, string expectedText, SearchResultLabelStyle expectedStyle)
    {
        IReadOnlyList<SearchResultLabelRun> runs = ResultLabelFormatter.ToLabelRuns(label);

        runs.Should().Equal(new SearchResultLabelRun(expectedText, expectedStyle));
    }

    [Fact]
    public void ToLabelRuns_gives_superscript_precedence_over_other_styles()
    {
        IReadOnlyList<SearchResultLabelRun> runs =
            ResultLabelFormatter.ToLabelRuns("<h><p><s>1</s></p></h>");

        runs.Should().Equal(new SearchResultLabelRun("1", SearchResultLabelStyle.Superscript));
    }

    [Fact]
    public void ToLabelRuns_gives_part_of_speech_precedence_over_headword_style()
    {
        IReadOnlyList<SearchResultLabelRun> runs =
            ResultLabelFormatter.ToLabelRuns("<h><p>verb</p></h>");

        runs.Should().Equal(new SearchResultLabelRun("verb", SearchResultLabelStyle.PartOfSpeech));
    }

    [Fact]
    public void ToLabelRuns_handles_tag_attributes_and_tag_name_whitespace()
    {
        IReadOnlyList<SearchResultLabelRun> runs =
            ResultLabelFormatter.ToLabelRuns("< h class=\"entry\">< n>find</ n></ h>");

        runs.Should().Equal(new SearchResultLabelRun("find", SearchResultLabelStyle.HeadwordStrong));
    }

    [Fact]
    public void ToLabelRuns_ignores_unknown_tags_but_preserves_text()
    {
        IReadOnlyList<SearchResultLabelRun> runs =
            ResultLabelFormatter.ToLabelRuns("<h><unknown>word</unknown> <p>noun</p></h>");

        runs.Should().Equal(
            new SearchResultLabelRun("word ", SearchResultLabelStyle.Headword),
            new SearchResultLabelRun("noun", SearchResultLabelStyle.PartOfSpeech));
    }

    [Fact]
    public void ToLabelRuns_preserves_unterminated_markup_as_text_with_current_style()
    {
        IReadOnlyList<SearchResultLabelRun> runs = ResultLabelFormatter.ToLabelRuns("<h>word <p");

        runs.Should().Equal(new SearchResultLabelRun("word <p", SearchResultLabelStyle.Headword));
    }

    [Fact]
    public void ToLabelRuns_removes_the_nearest_matching_open_tag_for_closing_tags()
    {
        IReadOnlyList<SearchResultLabelRun> runs = ResultLabelFormatter.ToLabelRuns("<h><n>one <n>two</n> three</n></h>");

        runs.Should().Equal(new SearchResultLabelRun("one two three", SearchResultLabelStyle.HeadwordStrong));
    }

    [Fact]
    public void ToLabelRuns_merges_adjacent_text_with_the_same_style_after_tag_boundaries()
    {
        IReadOnlyList<SearchResultLabelRun> runs = ResultLabelFormatter.ToLabelRuns("<h>one<unknown></unknown>two</h>");

        runs.Should().Equal(new SearchResultLabelRun("onetwo", SearchResultLabelStyle.Headword));
    }
}
