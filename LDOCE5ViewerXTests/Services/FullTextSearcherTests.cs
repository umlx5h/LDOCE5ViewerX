using AwesomeAssertions;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

public sealed class FullTextSearcherTests
{
    [Fact]
    public void Constructor_defers_opening_lucene_index_until_search()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(indexDirectory);
        try
        {
            Action createSearcher = () =>
            {
                using FullTextSearcher searcher = new(indexDirectory);
            };

            createSearcher.Should().NotThrow();
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_returns_lucene_matches_for_indexed_items()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "test", "/fs/test", "test word", "test", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "other", "/fs/other", "other word", "other", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search("test", null, [], 10);

            results.Should().ContainSingle();
            results[0].Path.Should().Be("/fs/test");
            results[0].Label.Should().Be("test");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void SuggestCorrections_returns_similar_terms_from_existing_index()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "word", "word", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "work", "/fs/work", "work", "work", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<string> suggestions = searcher.SuggestCorrections("worl", 5);

            suggestions.Should().Contain("word");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void SuggestCorrections_returns_no_suggestions_for_existing_term()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "word", "word", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<string> suggestions = searcher.SuggestCorrections("word", 5);

            suggestions.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void SuggestCorrections_works_after_normal_search_uses_shared_resources()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "word", "word", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);
            searcher.Search("word", null, [], 10).Should().ContainSingle();

            IReadOnlyList<string> suggestions = searcher.SuggestCorrections("worl", 5);

            suggestions.Should().Contain("word");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_matches_terms_split_by_slashes_in_indexed_phrases()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem(
                    "p",
                    "dangerous/hazardous/harmful substance",
                    "/fs/substance",
                    "dangerous/hazardous/harmful substance",
                    "dangerous/hazardous/harmful substance",
                    string.Empty,
                    10));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search("hazardous", null, [], 10);

            results.Should().ContainSingle();
            results[0].Path.Should().Be("/fs/substance");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_matches_words_followed_by_question_marks_in_examples()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem(
                    "e",
                    "across",
                    "/fs/across#example",
                    "There's Brendan. Why don't you go across and say hello?",
                    "across",
                    string.Empty,
                    20));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search("hello", null, ["e"], 10);

            results.Should().ContainSingle();
            results[0].Path.Should().Be("/fs/across#example");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_matches_words_followed_by_question_marks_in_phrases()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem(
                    "pl",
                    "what/who/why etc the devil? (devil noun)",
                    "/fs/devil#phrase",
                    "what/who/why etc the devil?",
                    "what who why etc the devil",
                    string.Empty,
                    10));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search("devil", null, ["pl"], 10);

            results.Should().ContainSingle();
            results[0].Path.Should().Be("/fs/devil#phrase");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("hm", "Headword content")]
    [InlineData("hp", "Phrasal verb content")]
    [InlineData("p", "Collocation content")]
    [InlineData("pl", "Phrase content")]
    [InlineData("d", "Definition content")]
    [InlineData("e", "Example content")]
    public void Search_filters_by_item_type(string typeCode, string content)
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem(typeCode, content, $"/fs/{typeCode}", content, typeCode, string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "other", "/fs/other", "unrelated", "other", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search(content, null, [typeCode], 10);

            results.Should().ContainSingle();
            results[0].TypeCode.Should().Be(typeCode);
            results[0].Label.Should().Be(content);
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_filters_by_single_advanced_search_code()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "academic", "/fs/academic", "target", "academic", "426", 1));
                writer.AddItem(new SearchIndexItem("hm", "ordinary", "/fs/ordinary", "target", "ordinary", "334", 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search(
                "target",
                SearchFilterQuery.Create([["426"]]),
                ["hm"],
                10);

            results.Should().ContainSingle();
            results[0].Path.Should().Be("/fs/academic");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_ors_codes_inside_one_advanced_search_filter_group()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "adjective", "/fs/adjective", "target", "adjective", "334", 1));
                writer.AddItem(new SearchIndexItem("hm", "noun", "/fs/noun", "target", "noun", "341", 1));
                writer.AddItem(new SearchIndexItem("hm", "verb", "/fs/verb", "target", "verb", "349", 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search(
                "target",
                SearchFilterQuery.Create([["334", "341"]]),
                ["hm"],
                10);

            results.Select(result => result.Path).Should().Equal("/fs/adjective", "/fs/noun");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_ands_advanced_search_filter_groups()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "academic adjective", "/fs/academic-adjective", "target", "academic adjective", "426 334", 1));
                writer.AddItem(new SearchIndexItem("hm", "academic noun", "/fs/academic-noun", "target", "academic noun", "426 341", 1));
                writer.AddItem(new SearchIndexItem("hm", "ordinary adjective", "/fs/ordinary-adjective", "target", "ordinary adjective", "334", 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search(
                "target",
                SearchFilterQuery.Create([["426"], ["334"]]),
                ["hm"],
                10);

            results.Should().ContainSingle();
            results[0].Path.Should().Be("/fs/academic-adjective");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_supports_filter_only_advanced_search()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "academic", "/fs/academic", "one", "academic", "426", 1));
                writer.AddItem(new SearchIndexItem("hm", "ordinary", "/fs/ordinary", "two", "ordinary", "334", 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search(
                string.Empty,
                SearchFilterQuery.Create([["426"]]),
                ["hm"],
                10);

            results.Should().ContainSingle();
            results[0].Path.Should().Be("/fs/academic");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_returns_no_matches_when_phrase_has_no_searchable_tokens()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("e", "example", "/fs/example", "visible example", "example", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search("🍣", null, ["e"], 10);

            results.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_treats_trailing_star_as_prefix_query()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "word", "word", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "work", "/fs/work", "work", "work", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "sword", "/fs/sword", "sword", "sword", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search("wor*", null, ["hm"], 10);

            results.Count.Should().Be(2);
            results.Select(result => result.Path).Should().Equal("/fs/word", "/fs/work");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_can_filter_wildcard_matches_by_sort_key()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "word", "word", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "wordplay", "/fs/wordplay", "wordplay", "wordplay", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "swear word", "/fs/swear-word", "swear word", "swear word", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "sword", "/fs/sword", "sword", "sword", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search(
                "word*",
                null,
                ["hm"],
                10,
                filterWildcardBySortKey: true);

            results.Select(result => result.Path).Should().Equal("/fs/word", "/fs/wordplay");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_applies_result_limit_after_sort_key_wildcard_filter()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "word", "word", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "wordplay", "/fs/wordplay", "wordplay", "wordplay", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "wordsmith", "/fs/wordsmith", "wordsmith", "wordsmith", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search(
                "word*",
                null,
                ["hm"],
                2,
                filterWildcardBySortKey: true);

            results.Select(result => result.Path).Should().Equal("/fs/word", "/fs/wordplay");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_applies_result_limit_after_sorting_sort_key_wildcard_matches()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "wordz", "/fs/wordz", "wordz", "wordz", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "worda", "/fs/worda", "worda", "worda", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "wordb", "/fs/wordb", "wordb", "wordb", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search(
                "word*",
                null,
                ["hm"],
                2,
                filterWildcardBySortKey: true);

            results.Select(result => result.Path).Should().Equal("/fs/worda", "/fs/wordb");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_keeps_token_wildcard_behavior_when_sort_key_filter_is_disabled()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "word", "word", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "swear word", "/fs/swear-word", "swear word", "swear word", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search("word*", null, ["hm"], 10,
                filterWildcardBySortKey: false);

            results.Select(result => result.Path).Should().Equal("/fs/swear-word", "/fs/word");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_supports_single_character_wildcards()
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "word", "word", string.Empty, 1));
                writer.AddItem(new SearchIndexItem("hm", "world", "/fs/world", "world", "world", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search("w?rd", null, ["hm"], 10);

            results.Should().ContainSingle();
            results[0].Path.Should().Be("/fs/word");
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("*")]
    [InlineData("?")]
    [InlineData("??")]
    [InlineData("word *")]
    public void Search_rejects_wildcard_only_tokens(string query)
    {
        string indexDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (FullTextIndexWriter writer = new(indexDirectory))
            {
                writer.AddItem(new SearchIndexItem("hm", "word", "/fs/word", "word", "word", string.Empty, 1));
                writer.Commit();
            }

            using FullTextSearcher searcher = new(indexDirectory);

            IReadOnlyList<FullTextSearchResult> results = searcher.Search(query, null, ["hm"], 10);

            results.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
            }
        }
    }
}
