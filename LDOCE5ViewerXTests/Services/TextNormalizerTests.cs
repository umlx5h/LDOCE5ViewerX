using AwesomeAssertions;

namespace LDOCE5ViewerX.Services;

public sealed class TextNormalizerTests
{
    [Fact]
    public void NormalizeIndexKey_keeps_python_incremental_index_rules()
    {
        string result = TextNormalizer.NormalizeIndexKey("  Café ©-12!  ");

        result.Should().Be("cafec12");
    }

    [Fact]
    public void NormalizeIndexKeyWildcardPattern_preserves_wildcards_with_index_key_rules()
    {
        string result = TextNormalizer.NormalizeIndexKeyWildcardPattern("  犬 Café ©-*?!  ");

        result.Should().Be("cafec*?");
    }

    [Fact]
    public void NormalizeFullText_splits_punctuation_like_full_text_search()
    {
        string result = TextNormalizer.NormalizeFullText("dangerous/hazardous/toxic substance");

        result.Should().Be("dangerous hazardous toxic substance");
    }

    [Fact]
    public void NormalizeFullText_removes_question_marks_from_indexed_content()
    {
        string result = TextNormalizer.NormalizeFullText("say hello? what the devil?");

        result.Should().Be("say hello what the devil");
    }

    [Fact]
    public void TokenizeFullTextQuery_preserves_question_mark_wildcards()
    {
        IReadOnlyList<string> result = TextNormalizer.TokenizeFullTextQuery("hel?");

        result.Should().ContainSingle().Which.Should().Be("hel?");
    }
}
