using AwesomeAssertions;

using LDOCE5ViewerX.Models;
using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.ViewModels;

public sealed class SearchResultItemViewModelTests
{
    [Fact]
    public void ResultKey_distinguishes_variants_that_share_a_path()
    {
        FullTextSearchResult singular = new(
            "<h><v>basset hound</v> &rarr; <n>basset</n> <p>noun</p></h>",
            "/fs/basset#variant",
            "bassethound",
            2,
            "hv",
            "basset hound");
        FullTextSearchResult plural = new(
            "<h><v>basset hounds</v> &rarr; <n>basset</n> <p>noun</p></h>",
            "/fs/basset#variant",
            "bassethounds",
            2,
            "hv",
            "basset hounds");

        SearchResultItemViewModel singularViewModel = SearchResultItemViewModel.FromFullTextResult(singular);
        SearchResultItemViewModel pluralViewModel = SearchResultItemViewModel.FromFullTextResult(plural);

        singularViewModel.Path.Should().Be(pluralViewModel.Path);
        singularViewModel.GetHashCode().Should().NotBe(pluralViewModel.GetHashCode());
    }

    [Theory]
    [InlineData("hp", "Phrasal Verb")]
    [InlineData("p", "Collocation")]
    [InlineData("pl", "Phrase")]
    [InlineData("d", "Definition")]
    [InlineData("e", "Example")]
    public void FromFullTextResult_maps_search_mode_kinds(string typeCode, string expectedKind)
    {
        FullTextSearchResult result = new("label", "/fs/path", "label", 1, typeCode, "snippet text");

        SearchResultItemViewModel viewModel = SearchResultItemViewModel.FromFullTextResult(result);

        viewModel.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("hm", "Headword", "HW")]
    [InlineData("hv", "Variant", "Var")]
    [InlineData("p", "Collocation", "Col")]
    [InlineData("pl", "Phrase", "Phr")]
    [InlineData("ac", "Activator", "Act")]
    public void FromFullTextResult_exposes_abbreviated_kind_for_badges(
        string typeCode,
        string expectedKind,
        string expectedAbbreviation)
    {
        FullTextSearchResult result = new("label", "/fs/path", "label", 1, typeCode, "snippet text");

        SearchResultItemViewModel viewModel = SearchResultItemViewModel.FromFullTextResult(result);

        viewModel.Kind.Should().Be(expectedKind);
        viewModel.KindAbbreviation.Should().Be(expectedAbbreviation);
    }

    [Fact]
    public void FromFullTextResult_uses_snippet_for_definition_and_example_descriptions()
    {
        FullTextSearchResult result = new("label", "/fs/path", "label", 1, "d", "definition text");

        SearchResultItemViewModel viewModel = SearchResultItemViewModel.FromFullTextResult(result);

        viewModel.Description.Should().Be("definition text");
        viewModel.HasDescription.Should().BeTrue();
    }

    [Fact]
    public void FromIncrementalResult_keeps_path_for_navigation_without_display_description()
    {
        IncrementalSearchResult result = new("<h><n>find</n></h>", "/fs/find", "find", 1, "hm");

        SearchResultItemViewModel viewModel = SearchResultItemViewModel.FromIncrementalResult(result);

        viewModel.Path.Should().Be("/fs/find");
        viewModel.Description.Should().BeEmpty();
        viewModel.HasDescription.Should().BeFalse();
    }

    [Fact]
    public void FromFullTextResult_hides_path_when_no_definition_or_example_snippet_exists()
    {
        FullTextSearchResult result = new("<h><n>find</n></h>", "/fs/find", "find", 1, "hm", null);

        SearchResultItemViewModel viewModel = SearchResultItemViewModel.FromFullTextResult(result);

        viewModel.Path.Should().Be("/fs/find");
        viewModel.Description.Should().BeEmpty();
        viewModel.HasDescription.Should().BeFalse();
    }

    [Fact]
    public void ToLabelRuns_maps_headword_homonym_and_part_of_speech_styles()
    {
        IReadOnlyList<SearchResultLabelRun> runs =
            ResultLabelFormatter.ToLabelRuns("<h><n>find</n><s>1</s> <p>verb</p></h>");

        string.Concat(runs.Select(run => run.Text)).Should().Be("find1 verb");
        runs.Should().ContainInOrder(
            new SearchResultLabelRun("find", SearchResultLabelStyle.HeadwordStrong),
            new SearchResultLabelRun("1", SearchResultLabelStyle.Superscript),
            new SearchResultLabelRun(" ", SearchResultLabelStyle.Headword),
            new SearchResultLabelRun("verb", SearchResultLabelStyle.PartOfSpeech));
    }

    [Fact]
    public void ToLabelRuns_decodes_entities_in_variant_labels()
    {
        IReadOnlyList<SearchResultLabelRun> runs =
            ResultLabelFormatter.ToLabelRuns("<h><v>found</v> \u2192 <n>find</n><s>1</s> <p>verb</p></h>");

        string.Concat(runs.Select(run => run.Text)).Should().Be("found \u2192 find1 verb");
        runs.Should().Contain(run => run.Text == "find" && run.Style == SearchResultLabelStyle.HeadwordStrong);
        runs.Should().Contain(run => run.Text == "verb" && run.Style == SearchResultLabelStyle.PartOfSpeech);
    }

    [Fact]
    public void ToLabelRuns_tolerates_unknown_and_unbalanced_tags()
    {
        IReadOnlyList<SearchResultLabelRun> runs =
            ResultLabelFormatter.ToLabelRuns("<h><unknown>word</unknown> <p>noun</h>");

        string.Concat(runs.Select(run => run.Text)).Should().Be("word noun");
        runs.Should().Contain(run => run.Text == "noun" && run.Style == SearchResultLabelStyle.PartOfSpeech);
    }
}
