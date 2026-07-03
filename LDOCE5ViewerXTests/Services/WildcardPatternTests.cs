using AwesomeAssertions;

namespace LDOCE5ViewerX.Services;

public sealed class WildcardPatternTests
{
    [Theory]
    [InlineData("word", false)]
    [InlineData("wor*", true)]
    [InlineData("w?rd", true)]
    public void Contains_returns_whether_text_has_wildcard_syntax(string text, bool expected)
    {
        bool result = WildcardPattern.Contains(text);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("??", true)]
    [InlineData("w?rd", false)]
    [InlineData("wo*", false)]
    [InlineData("word", false)]
    public void IsWildcardOnly_returns_whether_token_has_no_searchable_text(string token, bool expected)
    {
        bool result = WildcardPattern.IsWildcardOnly(token);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("wor*", true)]
    [InlineData("*", true)]
    [InlineData("wo*d", false)]
    [InlineData("word?", false)]
    [InlineData("word", false)]
    [InlineData("*or*", false)]
    [InlineData("?or*", false)]
    public void IsTrailingStarPrefixToken_returns_whether_token_can_use_prefix_query(string token, bool expected)
    {
        bool result = WildcardPattern.IsTrailingStarPrefixToken(token);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("word", "word", true)]
    [InlineData("wordplay", "word*", true)]
    [InlineData("sword", "*word", true)]
    [InlineData("word", "w?rd", true)]
    [InlineData("word", "w*d", true)]
    [InlineData("world", "w?rd", false)]
    [InlineData("word", "word?", false)]
    [InlineData("wordy", "word?", true)]
    public void Matches_returns_whether_value_matches_wildcard_pattern(string value, string pattern, bool expected)
    {
        bool result = WildcardPattern.Matches(value, pattern);

        result.Should().Be(expected);
    }
}
